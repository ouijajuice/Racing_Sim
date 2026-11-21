using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class BroadcastCamera : MonoBehaviour
{
    public RaceManager raceManager;

    [Header("Hover Framing")]
    public Vector3 baseOffset = new Vector3(0f, 26f, -14f);
    public float battleZoomPadding = 0.6f;
    public float positionSmoothTime = 0.6f;
    public float rotationSmoothSpeed = 4f;

    [Header("Overtake Detection & Cooldown")]
    public float battleTriggerDistance = 6.5f;
    public float battleBreakDistance = 12f;
    public float minBattleHoldTime = 3.0f;
    public float switchCooldown = 2.5f; // Delay after ANY POV switch before another is allowed

    private Vector3 smoothVelocity;
    private AICar currentLeader;
    private AICar battleCarA;
    private AICar battleCarB;
    private float battleTimer;
    private float cooldownTimer;

    void LateUpdate()
    {
        if (raceManager == null || raceManager.Cars == null || raceManager.Cars.Count == 0)
            return;

        List<AICar> activeCars = raceManager.Cars
            .Where(c => c != null && !c.HasFinished)
            .OrderByDescending(c => c.RaceProgress)
            .ToList();

        if (activeCars.Count == 0) return;

        UpdateCameraTarget(activeCars);

        Vector3 focusPoint;
        float extraHeight = 0f;

        if (battleCarA != null && battleCarB != null)
        {
            focusPoint = (battleCarA.transform.position + battleCarB.transform.position) * 0.5f;
            float separation = Vector3.Distance(battleCarA.transform.position, battleCarB.transform.position);
            extraHeight = separation * battleZoomPadding;
        }
        else if (currentLeader != null)
        {
            focusPoint = currentLeader.transform.position + currentLeader.transform.forward * 3f;
        }
        else
        {
            return;
        }

        Vector3 desiredPos = focusPoint + baseOffset + Vector3.up * extraHeight;
        transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref smoothVelocity, positionSmoothTime);

        Quaternion desiredRot = Quaternion.LookRotation(focusPoint - transform.position);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, rotationSmoothSpeed * Time.deltaTime);
    }

    private void UpdateCameraTarget(List<AICar> sortedCars)
    {
        // Safety fallback if a tracked car was destroyed (e.g., new race started)
        if (currentLeader == null || currentLeader.HasFinished)
        {
            currentLeader = sortedCars[0];
        }

        if (battleCarA == null || battleCarB == null)
        {
            battleCarA = null;
            battleCarB = null;
        }

        // Tick down timers
        if (cooldownTimer > 0f) cooldownTimer -= Time.deltaTime;
        if (battleTimer > 0f) battleTimer -= Time.deltaTime;

        // 1. If currently framing a battle, check if we should leave it
        if (battleCarA != null && battleCarB != null)
        {
            float dist = Vector3.Distance(battleCarA.transform.position, battleCarB.transform.position);
            bool battleOver = dist > battleBreakDistance || battleCarA.HasFinished || battleCarB.HasFinished;

            if (battleTimer <= 0f && cooldownTimer <= 0f && battleOver)
            {
                battleCarA = null;
                battleCarB = null;
                currentLeader = sortedCars[0];
                cooldownTimer = switchCooldown; // Lock onto the leader before allowing another cut
            }
            return;
        }

        // 2. If on cooldown, stay on the current leader and ignore new cuts
        if (cooldownTimer > 0f) return;

        // 3. Look for a new overtake battle
        for (int i = 0; i < sortedCars.Count - 1; i++)
        {
            AICar ahead = sortedCars[i];
            AICar behind = sortedCars[i + 1];

            if (Vector3.Distance(ahead.transform.position, behind.transform.position) <= battleTriggerDistance)
            {
                battleCarA = ahead;
                battleCarB = behind;
                battleTimer = minBattleHoldTime;
                cooldownTimer = switchCooldown;
                return;
            }
        }

        // 4. If no battle is happening, switch to the new leader (if P1 changed) and start cooldown
        if (currentLeader != sortedCars[0])
        {
            currentLeader = sortedCars[0];
            cooldownTimer = switchCooldown;
        }
    }
}