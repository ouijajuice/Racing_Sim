using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class BroadcastCamera : MonoBehaviour
{
    private enum CameraMode { HoverLeader, HoverBattle, CarPOV }

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
    public float switchCooldown = 2.5f;

    [Header("Onboard POV Mode")]
    [Range(0f, 1f)] public float povChance = 0.35f; // Chance to cut to POV when eligible
    public float povDuration = 4.0f;                // How long to stay onboard
    public float povFollowTightness = 20f;          // High value keeps camera locked to mount
    public bool hardCutToPOV = true;                // Instant TV cut instead of swooping

    private CameraMode currentMode = CameraMode.HoverLeader;
    private Vector3 smoothVelocity;

    private AICar currentLeader;
    private AICar battleCarA;
    private AICar battleCarB;
    private AICar povCar;

    private float modeTimer;
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

        UpdateCameraMode(activeCars);

        if (currentMode == CameraMode.CarPOV && povCar != null && povCar.povCameraMount != null)
        {
            Transform mount = povCar.povCameraMount;
            transform.SetPositionAndRotation(mount.position, mount.rotation);
            return;
        }

        // Hover modes (Leader or Battle)
        Vector3 focusPoint;
        float extraHeight = 0f;

        if (currentMode == CameraMode.HoverBattle && battleCarA != null && battleCarB != null)
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

    private void UpdateCameraMode(List<AICar> sortedCars)
    {
        if (currentLeader == null || currentLeader.HasFinished)
            currentLeader = sortedCars[0];

        if (cooldownTimer > 0f) cooldownTimer -= Time.deltaTime;
        if (modeTimer > 0f) modeTimer -= Time.deltaTime;

        // 1. Handle active Car POV mode
        if (currentMode == CameraMode.CarPOV)
        {
            bool invalidTarget = povCar == null || povCar.HasFinished || povCar.povCameraMount == null;
            if (invalidTarget || modeTimer <= 0f)
            {
                ExitPOVToLeader(sortedCars[0]);
            }
            return;
        }

        // 2. Handle active Battle mode
        if (currentMode == CameraMode.HoverBattle)
        {
            bool invalidBattle = battleCarA == null || battleCarB == null || battleCarA.HasFinished || battleCarB.HasFinished;
            bool separated = !invalidBattle && Vector3.Distance(battleCarA.transform.position, battleCarB.transform.position) > battleBreakDistance;

            if (invalidBattle || (modeTimer <= 0f && cooldownTimer <= 0f && separated))
            {
                battleCarA = null;
                battleCarB = null;
                currentLeader = sortedCars[0];
                currentMode = CameraMode.HoverLeader;
                cooldownTimer = switchCooldown;
            }
            return;
        }

        // 3. Respect cooldown before making any new cut
        if (cooldownTimer > 0f) return;

        // 4. Check for overtake battles first
        for (int i = 0; i < sortedCars.Count - 1; i++)
        {
            AICar ahead = sortedCars[i];
            AICar behind = sortedCars[i + 1];

            if (Vector3.Distance(ahead.transform.position, behind.transform.position) <= battleTriggerDistance)
            {
                // Occasionally watch the overtake from the chasing car's onboard POV
                if (behind.povCameraMount != null && Random.value < povChance)
                {
                    EnterPOV(behind);
                }
                else
                {
                    battleCarA = ahead;
                    battleCarB = behind;
                    currentMode = CameraMode.HoverBattle;
                    modeTimer = minBattleHoldTime;
                    cooldownTimer = switchCooldown;
                }
                return;
            }
        }

        // 5. No battle happening: occasionally cut to a random car's POV
        if (Random.value < povChance)
        {
            var validCars = sortedCars.Where(c => c.povCameraMount != null).ToList();
            if (validCars.Count > 0)
            {
                // Bias toward the top half of the pack
                int maxIndex = Mathf.Max(1, validCars.Count / 2);
                EnterPOV(validCars[Random.Range(0, maxIndex)]);
                return;
            }
        }

        // 6. Otherwise track the leader (with cooldown if P1 changed)
        if (currentLeader != sortedCars[0])
        {
            currentLeader = sortedCars[0];
            cooldownTimer = switchCooldown;
        }
    }

    private void EnterPOV(AICar targetCar)
    {
        povCar = targetCar;
        currentMode = CameraMode.CarPOV;
        modeTimer = povDuration;
        cooldownTimer = switchCooldown;
        smoothVelocity = Vector3.zero;

        if (hardCutToPOV && povCar.povCameraMount != null)
        {
            transform.position = povCar.povCameraMount.position;
            transform.rotation = povCar.povCameraMount.rotation;
        }
    }

    private void ExitPOVToLeader(AICar leader)
    {
        povCar = null;
        currentLeader = leader;
        currentMode = CameraMode.HoverLeader;
        cooldownTimer = switchCooldown;
        smoothVelocity = Vector3.zero;

        if (hardCutToPOV && currentLeader != null)
        {
            Vector3 focus = currentLeader.transform.position + currentLeader.transform.forward * 3f;
            transform.position = focus + baseOffset;
            transform.rotation = Quaternion.LookRotation(focus - transform.position);
        }
    }
}