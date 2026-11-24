using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class AICar : MonoBehaviour
{
    [Header("Driving & Momentum")]
    public float maxSpeed = 24f;
    public float acceleration = 10f;
    public float braking = 20f;
    public float turnSpeed = 6f;
    public float waypointReachDistance = 4f;

    [Header("Racing Line & Cornering")]
    public int lookAheadWaypoints = 7;     // How far ahead to scan for corners
    public float maxLaneOffset = 3.2f;     // Slightly under half of trackWidth
    public float laneChangeSpeed = 3.5f;
    [Range(0f, 1f)] public float apexAggression = 0.85f; // How tightly cars hug the inside

    [Header("Overtaking & Slipstream")]
    public LayerMask carLayer;
    public float sensorLength = 9f;
    public float draftDistance = 15f;      // Range to catch a slipstream
    public float draftSpeedBoost = 1.12f;  // +12% top speed while drafting
    public float sideSensorAngle = 25f;
    public float overtakeShift = 2.4f;

    [Header("Camera")]
    public Transform povCameraMount;

    // Public state for RaceManager & BroadcastCamera
    public string CarName { get; private set; }
    public Color CarColor { get; private set; }
    public int CurrentLap { get; private set; } = 1;
    public float RaceProgress { get; private set; }
    public bool HasFinished { get; private set; }
    public int FinishPosition { get; private set; }
    public float FinishTime { get; private set; }

    private List<Vector3> waypoints;
    private int currentWaypoint = 0;
    private Rigidbody rb;

    private float currentSpeed = 0f;
    private float personalOffset;
    private float targetLaneOffset;
    private float currentLaneOffset;
    private float sensorSpeedMultiplier = 1f;
    private bool isOvertaking = false;

    public void Initialize(List<Vector3> trackWaypoints, int startIndex, float speed, float offset, Color color, string carName)
    {
        waypoints = trackWaypoints;
        currentWaypoint = startIndex % waypoints.Count;
        maxSpeed = speed;
        CarName = carName;
        CarColor = color;
        CurrentLap = 1;
        HasFinished = false;

        // Keep a small personal bias so cars don't drive in a single-file train on straights
        personalOffset = offset * 0.4f;
        targetLaneOffset = personalOffset;
        currentLaneOffset = personalOffset;
        currentSpeed = speed * 0.5f;

        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.constraints = RigidbodyConstraints.FreezePositionY |
                         RigidbodyConstraints.FreezeRotationX |
                         RigidbodyConstraints.FreezeRotationZ;

        if (povCameraMount == null)
            povCameraMount = transform.Find("POVMount");

        var renderer = GetComponentInChildren<Renderer>();
        if (renderer != null) renderer.material.color = color;
    }

    public void LockFinish(int position, float time)
    {
        if (HasFinished) return;
        HasFinished = true;
        FinishPosition = position;
        FinishTime = time;
    }

    void FixedUpdate()
    {
        if (waypoints == null || waypoints.Count == 0) return;

        rb.angularVelocity = Vector3.zero;

        // 1. Analyze upcoming corner curvature for racing line & braking
        float upcomingSignedAngle = GetUpcomingCornerAngle(lookAheadWaypoints);
        float idealRacingLine = CalculateRacingLine(upcomingSignedAngle);

        // 2. Run sensors for overtaking, braking, and slipstream
        UpdateSensors(idealRacingLine);

        currentLaneOffset = Mathf.Lerp(currentLaneOffset, targetLaneOffset, laneChangeSpeed * Time.fixedDeltaTime);

        Vector3 target = waypoints[currentWaypoint];
        Vector3 nextTarget = waypoints[(currentWaypoint + 1) % waypoints.Count];
        Vector3 trackDir = (nextTarget - target).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, trackDir);
        Vector3 offsetTarget = target + right * currentLaneOffset;

        int prevWaypoint = currentWaypoint;
        Vector3 toTarget = offsetTarget - transform.position;
        bool passedWaypoint = Vector3.Dot(toTarget, trackDir) < 0f;

        if (toTarget.magnitude < waypointReachDistance || passedWaypoint)
        {
            currentWaypoint = (currentWaypoint + 1) % waypoints.Count;
        }
        else if (toTarget.magnitude > waypointReachDistance * 4f)
        {
            currentWaypoint = (FindNearestWaypoint() + 1) % waypoints.Count;
        }

        if (prevWaypoint > waypoints.Count - 15 && currentWaypoint < 15)
        {
            CurrentLap++;
        }

        // 3. Steer toward target
        Vector3 direction = (offsetTarget - transform.position).normalized;
        Quaternion targetRotation = Quaternion.LookRotation(direction);
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRotation, turnSpeed * Time.fixedDeltaTime));

        // 4. Calculate target speed using BOTH immediate turn and upcoming corner
        float immediateAngle = Vector3.Angle(transform.forward, direction);
        float effectiveCornerAngle = Mathf.Max(immediateAngle, Mathf.Abs(upcomingSignedAngle) * 0.85f);
        float cornerSpeedFactor = Mathf.Clamp(1f - (effectiveCornerAngle / 75f) * 0.65f, 0.35f, 1f);

        float desiredSpeed = maxSpeed * cornerSpeedFactor * sensorSpeedMultiplier;

        // Smooth acceleration and braking (momentum)
        float rate = desiredSpeed > currentSpeed ? acceleration : braking;
        currentSpeed = Mathf.MoveTowards(currentSpeed, desiredSpeed, rate * Time.fixedDeltaTime);
        rb.linearVelocity = transform.forward * currentSpeed;

        // 5. Smooth centerline progress for the leaderboard
        int prevIndex = (currentWaypoint - 1 + waypoints.Count) % waypoints.Count;
        Vector3 segStart = waypoints[prevIndex];
        Vector3 segEnd = waypoints[currentWaypoint];
        Vector3 segVec = segEnd - segStart;

        float segLengthSq = segVec.sqrMagnitude;
        float segmentProgress = segLengthSq > 0.0001f
            ? Mathf.Clamp01(Vector3.Dot(transform.position - segStart, segVec) / segLengthSq)
            : 0f;

        RaceProgress = (CurrentLap * waypoints.Count) + prevIndex + segmentProgress;
    }

    private float GetUpcomingCornerAngle(int stepsAhead)
    {
        Vector3 currentDir = (waypoints[(currentWaypoint + 1) % waypoints.Count] - waypoints[currentWaypoint]).normalized;
        int futureIdx = (currentWaypoint + stepsAhead) % waypoints.Count;
        int futureNextIdx = (futureIdx + 1) % waypoints.Count;
        Vector3 futureDir = (waypoints[futureNextIdx] - waypoints[futureIdx]).normalized;

        // Positive = right turn, Negative = left turn
        return Vector3.SignedAngle(currentDir, futureDir, Vector3.up);
    }

    private float CalculateRacingLine(float upcomingSignedAngle)
    {
        // Map turn sharpness (-45° to +45°) to the inside edge of the track
        float turnIntensity = Mathf.Clamp(upcomingSignedAngle / 40f, -1f, 1f);
        float apexOffset = turnIntensity * maxLaneOffset * apexAggression;

        // Blend with personal offset on straights, commit to the apex in corners
        return Mathf.Lerp(personalOffset, apexOffset, Mathf.Abs(turnIntensity));
    }

    private void UpdateSensors(float idealRacingLine)
    {
        Vector3 origin = transform.position + transform.forward * 1.4f;
        Vector3 forwardDir = transform.forward;
        Vector3 leftDir = Quaternion.Euler(0f, -sideSensorAngle, 0f) * transform.forward;
        Vector3 rightDir = Quaternion.Euler(0f, sideSensorAngle, 0f) * transform.forward;

        bool hitCenter = Physics.Raycast(origin, forwardDir, out RaycastHit centerHit, sensorLength, carLayer);
        bool hitLeft = Physics.Raycast(origin, leftDir, sensorLength * 0.85f, carLayer);
        bool hitRight = Physics.Raycast(origin, rightDir, sensorLength * 0.85f, carLayer);

        // Longer ray for slipstream detection
        bool inSlipstream = !hitCenter && Physics.Raycast(origin, forwardDir, draftDistance, carLayer);

        sensorSpeedMultiplier = inSlipstream ? draftSpeedBoost : 1f;
        isOvertaking = false;

        if (hitCenter)
        {
            isOvertaking = true;
            if (!hitLeft && !hitRight)
            {
                // Pick the side with more room toward track center
                float dir = currentLaneOffset > 0f ? -1f : 1f;
                targetLaneOffset = Mathf.Clamp(currentLaneOffset + dir * overtakeShift, -maxLaneOffset, maxLaneOffset);
            }
            else if (!hitLeft)
            {
                targetLaneOffset = Mathf.Clamp(currentLaneOffset - overtakeShift, -maxLaneOffset, maxLaneOffset);
            }
            else if (!hitRight)
            {
                targetLaneOffset = Mathf.Clamp(currentLaneOffset + overtakeShift, -maxLaneOffset, maxLaneOffset);
            }
            else
            {
                // Boxed in: match pace safely behind the car ahead
                float distRatio = centerHit.distance / sensorLength;
                sensorSpeedMultiplier = Mathf.Clamp(distRatio, 0.35f, 0.85f);
            }
        }
        else if (hitLeft && !hitRight)
        {
            targetLaneOffset = Mathf.Clamp(currentLaneOffset + 0.6f, -maxLaneOffset, maxLaneOffset);
        }
        else if (hitRight && !hitLeft)
        {
            targetLaneOffset = Mathf.Clamp(currentLaneOffset - 0.6f, -maxLaneOffset, maxLaneOffset);
        }
        else
        {
            // Road is clear: smoothly follow the apex-hugging racing line
            targetLaneOffset = Mathf.Lerp(targetLaneOffset, idealRacingLine, Time.fixedDeltaTime * 2f);
        }
    }

    private int FindNearestWaypoint()
    {
        int bestIndex = currentWaypoint;
        float bestDistSq = float.MaxValue;

        for (int i = 0; i < waypoints.Count; i++)
        {
            float distSq = (waypoints[i] - transform.position).sqrMagnitude;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                bestIndex = i;
            }
        }
        return bestIndex;
    }
}