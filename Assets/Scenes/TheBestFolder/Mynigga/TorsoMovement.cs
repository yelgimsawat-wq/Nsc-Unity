using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class TorsoMovement : NetworkBehaviour
{
    public enum TorsoState { Standing, Falling, Ragdoll }

    [Header("Network State")]
    public NetworkVariable<TorsoState> currentState = new NetworkVariable<TorsoState>(TorsoState.Standing);

    [Header("Fake Hover & Posture (Standing)")]
    public float targetTorsoHeight = 1.6f;
    public float heightSpringForce = 300f;
    public float heightDamper = 30f;
    public float uprightSpring = 800f;
    public float uprightDamper = 60f;
    public float autoCenterGravityForce = 250f;

    [Header("Balance Constraints (Grace Period)")]
    public float maxBalanceAngle = 55f;
    public float fallGracePeriod = 1.0f;

    [Header("Ragdoll Recovery Delay")]
    public float ragdollRecoveryDelay = 1.5f;
    private float ragdollTimer = 0f;

    [Header("Break Force System (Torso HP)")]
    public float maxTorsoStress = 500f;
    [HideInInspector] public float currentStress = 0f;
    public float stressDecayRate = 100f;

    [Header("Continuous Recovery")]
    public float continuousRecoveryForce = 800f;
    public float recoveryHeightThreshold = 0.7f;

    [Header("References")]
    public Rigidbody torsoRb;
    public Transform groundRaycastOrigin;
    public LayerMask groundLayer;
    public ConfigurableJoint[] hipJoints;

    [HideInInspector] public float armPullIntensity = 0f;
    public float minCenterForceMultiplier = 0.15f;

    private List<PlayerFootForRobot> attachedFeet = new List<PlayerFootForRobot>();
    private float balanceLossTimer = 0f;

    public void RegisterFoot(PlayerFootForRobot foot)
    {
        if (!attachedFeet.Contains(foot)) attachedFeet.Add(foot);
    }

    public void UnregisterFoot(PlayerFootForRobot foot)
    {
        attachedFeet.Remove(foot);
    }

    void FixedUpdate()
    {
        if (!IsServer) return;

        currentStress = Mathf.Max(0f, currentStress - stressDecayRate * Time.fixedDeltaTime);

        if (currentStress >= maxTorsoStress && currentState.Value == TorsoState.Standing)
        {
            currentState.Value = TorsoState.Falling;
        }

        if (hipJoints != null)
        {
            bool isRagdoll = currentState.Value == TorsoState.Ragdoll || currentState.Value == TorsoState.Falling;
            ConfigurableJointMotion motion = isRagdoll ? ConfigurableJointMotion.Free : ConfigurableJointMotion.Locked;
            foreach (var hip in hipJoints)
            {
                if (hip != null)
                {
                    hip.angularXMotion = motion;
                    hip.angularYMotion = motion;
                    hip.angularZMotion = motion;
                }
            }
        }

        switch (currentState.Value)
        {
            case TorsoState.Standing:
                HandleFakeHoverAndPosture();
                break;
            case TorsoState.Falling:
                currentState.Value = TorsoState.Ragdoll;
                ragdollTimer = 0f;
                break;
            case TorsoState.Ragdoll:
                ragdollTimer += Time.fixedDeltaTime;
                break;
        }
    }

    private void HandleFakeHoverAndPosture()
    {
        int groundedCount = 0;
        int balancedCount = 0; // ✅ นับจำนวนเท้าที่ยังมีสมดุลอยู่
        Vector3 averageFootPos = Vector3.zero;

        foreach (var foot in attachedFeet)
        {
            if (foot.IsBalanced) balancedCount++;

            if (!foot.IsGrounded()) continue;
            groundedCount++;
            averageFootPos += foot.footRb.position;
        }

        // ✅ ถ้าเท้าเสียสมดุลทั้งหมด (เช่น กระโดดทั้ง 2 ข้าง หรือก้าว 2 ข้างพร้อมกัน) -> ล้มทันที
        if (attachedFeet.Count >= 2 && balancedCount == 0)
        {
            currentState.Value = TorsoState.Falling;
            return;
        }

        if (attachedFeet.Count > 0 && groundedCount == 0)
        {
            balanceLossTimer += Time.fixedDeltaTime;
            if (balanceLossTimer >= fallGracePeriod)
            {
                currentState.Value = TorsoState.Falling;
                return;
            }
        }
        else
        {
            balanceLossTimer = Mathf.Max(0f, balanceLossTimer - Time.fixedDeltaTime * 2f);

            if (groundedCount > 0)
            {
                averageFootPos /= groundedCount;
                Vector3 flatError = new Vector3(averageFootPos.x - torsoRb.position.x, 0f, averageFootPos.z - torsoRb.position.z);
                float centerScale = Mathf.Lerp(1f, minCenterForceMultiplier, armPullIntensity);
                torsoRb.AddForce(flatError * (autoCenterGravityForce * centerScale), ForceMode.Acceleration);
            }
        }

        torsoRb.AddForce(-Physics.gravity, ForceMode.Acceleration);

        if (Physics.Raycast(groundRaycastOrigin.position, Vector3.down, out RaycastHit hit, targetTorsoHeight * 2f, groundLayer))
        {
            float heightError = targetTorsoHeight - (groundRaycastOrigin.position.y - hit.point.y);
            float upwardForce = (heightError * heightSpringForce) - (torsoRb.linearVelocity.y * heightDamper);
            torsoRb.AddForce(Vector3.up * upwardForce, ForceMode.Acceleration);
        }

        Quaternion targetRot = Quaternion.Euler(0f, torsoRb.rotation.eulerAngles.y, 0f);
        Quaternion deltaRot = targetRot * Quaternion.Inverse(torsoRb.rotation);
        deltaRot.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;

        if (angle != 0f)
        {
            Vector3 torque = (axis * (angle * uprightSpring)) - (torsoRb.angularVelocity * uprightDamper);
            torsoRb.AddTorque(torque, ForceMode.Acceleration);
        }
    }

    public void ApplyContinuousRecoveryForce(Vector3 forcePosition, float strengthMultiplier = 1f)
    {
        if (currentState.Value != TorsoState.Ragdoll && currentState.Value != TorsoState.Falling) return;
        if (ragdollTimer < ragdollRecoveryDelay) return;

        torsoRb.AddForceAtPosition(Vector3.up * (continuousRecoveryForce * strengthMultiplier), forcePosition, ForceMode.Acceleration);

        if (Physics.Raycast(groundRaycastOrigin.position, Vector3.down, out RaycastHit hit, targetTorsoHeight * 2f, groundLayer))
        {
            float currentHeight = groundRaycastOrigin.position.y - hit.point.y;
            if (currentHeight >= targetTorsoHeight * recoveryHeightThreshold)
            {
                balanceLossTimer = 0f;
                ragdollTimer = 0f;
                currentState.Value = TorsoState.Standing;
            }
        }
    }

    [Rpc(SendTo.Server)]
    public void ApplyRecoveryForceRpc(Vector3 forcePosition) => ApplyContinuousRecoveryForce(forcePosition);

    public void AddStress(float stressAmount)
    {
        currentStress += stressAmount;
        currentStress = Mathf.Min(currentStress, maxTorsoStress * 1.5f);
    }
}