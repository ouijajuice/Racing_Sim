using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class AICar : MonoBehaviour
{
    [Header("Driving")]
    public float maxSpeed = 22f;
    public float turnSpeed = 5f;
    public float waypointReachDistance = 4f;

    [Header("Overtaking & Sensors")]
    public LayerMask carLayer;
    public float sensorLength = 9f;
    public float sideSensorAngle = 25f;
    public float overtakeShift = 2.2f;
    public float maxLaneOffset = 3.2f; // Keep slightly under half of trackWidth
    public float laneChangeSpeed = 3.5f;

    [Header("Race Info")]
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

    private float baseLaneOffset;
    private float targetLaneOffset;
    private float currentLaneOffset;
    private float speedMultiplier = 1f;

    public void Initialize(List<Vector3> trackWaypoints, int startIndex, float speed, float offset, Color color, string carName)
    {
        waypoints = trackWaypoints;
        currentWaypoint = startIndex % waypoints.Count;
        maxSpeed = speed;
        CarName = carName;
        CarColor = color;
        CurrentLap = 1;

        baseLaneOffset = offset;
        targetLaneOffset = offset;
        currentLaneOffset = offset;

        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.constraints = RigidbodyConstraints.FreezePositionY |
                         RigidbodyConstraints.FreezeRotationX |
                         RigidbodyConstraints.FreezeRotationZ;

        var renderer = GetComponentInChildren<Renderer>();
        if (renderer != null) renderer.material.color = color;
    }

    void FixedUpdate()
    {
        if (waypoints == null || waypoints.Count == 0) return;

        // 1. Kill physics spin from collisions so cars don't spin out uncontrollably
        rb.angularVelocity = Vector3.zero;

        UpdateSensors();

        currentLaneOffset = Mathf.Lerp(currentLaneOffset, targetLaneOffset, laneChangeSpeed * Time.fixedDeltaTime);

        Vector3 target = waypoints[currentWaypoint];
        Vector3 nextTarget = waypoints[(currentWaypoint + 1) % waypoints.Count];
        Vector3 trackDir = (nextTarget - target).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, trackDir);
        Vector3 offsetTarget = target + right * currentLaneOffset;

        int prevWaypoint = currentWaypoint;

        // 2. Advance if close OR if the car was bumped past the waypoint
        Vector3 toTarget = offsetTarget - transform.position;
        bool passedWaypoint = Vector3.Dot(toTarget, trackDir) < 0f;

        if (toTarget.magnitude < waypointReachDistance || passedWaypoint)
        {
            currentWaypoint = (currentWaypoint + 1) % waypoints.Count;
        }
        // 3. Recovery: if knocked far off-track, snap to the nearest waypoint ahead
        else if (toTarget.magnitude > waypointReachDistance * 4f)
        {
            currentWaypoint = (FindNearestWaypoint() + 1) % waypoints.Count;
        }

        // Increment lap only when crossing from the final stretch back to the start
        if (prevWaypoint > waypoints.Count - 15 && currentWaypoint < 15)
        {
            CurrentLap++;
        }

        // Measure smooth 0..1 progress along the current centerline segment
        int prevIndex = (currentWaypoint - 1 + waypoints.Count) % waypoints.Count;
        Vector3 segStart = waypoints[prevIndex];
        Vector3 segEnd = waypoints[currentWaypoint];
        Vector3 segVec = segEnd - segStart;

        float segLengthSq = segVec.sqrMagnitude;
        float segmentProgress = segLengthSq > 0.0001f
            ? Mathf.Clamp01(Vector3.Dot(transform.position - segStart, segVec) / segLengthSq)
            : 0f;

        RaceProgress = (CurrentLap * waypoints.Count) + prevIndex + segmentProgress;

        // Steer smoothly toward target
        Vector3 direction = (offsetTarget - transform.position).normalized;
        Quaternion targetRotation = Quaternion.LookRotation(direction);
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRotation, turnSpeed * Time.fixedDeltaTime));

        // Slow down for sharp corners or when boxed in
        float turnAngle = Vector3.Angle(transform.forward, direction);
        float cornerFactor = Mathf.Clamp(1f - (turnAngle / 90f) * 0.6f, 0.35f, 1f);
        rb.linearVelocity = transform.forward * (maxSpeed * cornerFactor * speedMultiplier);
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

    public void LockFinish(int position, float time)
    {
        if (HasFinished) return;
        HasFinished = true;
        FinishPosition = position;
        FinishTime = time;
    }

    private void UpdateSensors()
    {
        // Start rays slightly in front of the bumper to avoid hitting our own collider
        Vector3 origin = transform.position + transform.forward * 1.4f;

        Vector3 forwardDir = transform.forward;
        Vector3 leftDir = Quaternion.Euler(0f, -sideSensorAngle, 0f) * transform.forward;
        Vector3 rightDir = Quaternion.Euler(0f, sideSensorAngle, 0f) * transform.forward;

        bool hitCenter = Physics.Raycast(origin, forwardDir, out RaycastHit centerHit, sensorLength, carLayer);
        bool hitLeft = Physics.Raycast(origin, leftDir, sensorLength * 0.85f, carLayer);
        bool hitRight = Physics.Raycast(origin, rightDir, sensorLength * 0.85f, carLayer);

        // Debug rays visible in the Scene view
        Debug.DrawRay(origin, forwardDir * sensorLength, hitCenter ? Color.red : Color.green);
        Debug.DrawRay(origin, leftDir * (sensorLength * 0.85f), hitLeft ? Color.red : Color.yellow);
        Debug.DrawRay(origin, rightDir * (sensorLength * 0.85f), hitRight ? Color.red : Color.yellow);

        speedMultiplier = 1f;

        if (hitCenter)
        {
            // Decide which side is open (prefer moving toward track center if both are clear)
            if (!hitLeft && !hitRight)
            {
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
                // Boxed in on all sides: brake proportionally to distance
                float distRatio = centerHit.distance / sensorLength;
                speedMultiplier = Mathf.Clamp(distRatio, 0.35f, 0.85f);
            }
        }
        else if (hitLeft && !hitRight)
        {
            // Gentle nudge right if a car is crowding the left side
            targetLaneOffset = Mathf.Clamp(currentLaneOffset + 0.5f, -maxLaneOffset, maxLaneOffset);
        }
        else if (hitRight && !hitLeft)
        {
            // Gentle nudge left if a car is crowding the right side
            targetLaneOffset = Mathf.Clamp(currentLaneOffset - 0.5f, -maxLaneOffset, maxLaneOffset);
        }
        else
        {
            // Road is clear: gradually drift back to preferred racing line
            targetLaneOffset = Mathf.Lerp(targetLaneOffset, baseLaneOffset, Time.fixedDeltaTime * 0.5f);
        }
    }
}