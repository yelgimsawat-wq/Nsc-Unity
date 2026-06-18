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
    [Tooltip("แรงดึงลำตัวกลับมาอยู่ตรงกลางเหนือเท้า")]
    public float autoCenterGravityForce = 250f;

    [Header("Balance Constraints (Grace Period)")]
    public float maxBalanceAngle = 55f;
    [Tooltip("วินาทีที่ยอมให้เสียสมดุลก่อนล้มจริง")]
    public float fallGracePeriod = 1.0f;

    [Header("Ragdoll Recovery Delay")]
    [Tooltip("เวลาที่ต้องรอหลังจาก ragdoll ก่อนจะลุกขึ้นได้")]
    public float ragdollRecoveryDelay = 1.5f;
    private float ragdollTimer = 0f;

    [Header("Continuous Recovery")]
    public float continuousRecoveryForce = 800f;
    public float recoveryHeightThreshold = 0.7f;

    [Header("References")]
    public Rigidbody torsoRb;
    public Transform groundRaycastOrigin;
    public LayerMask groundLayer;

    /// <summary>
    /// ค่าความเข้มของแรงดึงจากแขน (0=ไม่ดึง, 1=ดึงเต็ม)
    /// PlayerHandMovement เป็นผู้ set ค่านี้ทุก FixedUpdate
    /// ใช้ลด autoCenterGravityForce ไม่ให้สู้กับการดึงของผู้เล่น
    /// </summary>
    [HideInInspector] public float armPullIntensity = 0f;
    [Tooltip("ค่าต่ำสุดของ autoCenterForce เมื่อแขนดึงเต็มที่ (0.1-0.3 แนะนำ)")]
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

        switch (currentState.Value)
        {
            case TorsoState.Standing:
                HandleFakeHoverAndPosture();
                break;
            case TorsoState.Falling:
                currentState.Value = TorsoState.Ragdoll;
                ragdollTimer = 0f; // เริ่มนับเวลา ragdoll
                break;
            case TorsoState.Ragdoll:
                ragdollTimer += Time.fixedDeltaTime;
                break;
        }
    }

    private void HandleFakeHoverAndPosture()
    {
        int groundedCount = 0;
        int steppingCount = 0;
        Vector3 averageFootPos = Vector3.zero;

        foreach (var foot in attachedFeet)
        {
            if (foot.isStepping) steppingCount++;
            if (!foot.IsGrounded()) continue;
            groundedCount++;
            averageFootPos += foot.footRb.position;
        }

        // ตรวจสอบว่ามีเท้าทั้งสองข้างกำลัง stepping พร้อมกัน
        if (attachedFeet.Count >= 2 && steppingCount >= 2)
        {
            currentState.Value = TorsoState.Falling;
            return;
        }

        if (attachedFeet.Count > 0 && groundedCount == 0)
        {
            // ไม่มีเท้าแตะพื้นเลย: นับ Grace Period ก่อนล้มจริง
            balanceLossTimer += Time.fixedDeltaTime;
            if (balanceLossTimer >= fallGracePeriod)
            {
                currentState.Value = TorsoState.Falling;
                return;
            }
        }
        else
        {
            // ลด timer เมื่อมีเท้าแตะพื้น (x2 เพื่อฟื้นสมดุลเร็วกว่าเสีย)
            balanceLossTimer = Mathf.Max(0f, balanceLossTimer - Time.fixedDeltaTime * 2f);

            if (groundedCount > 0)
            {
                averageFootPos /= groundedCount;

                // Auto-Center: ดึงลำตัวให้อยู่เหนือจุดกึ่งกลางเท้า (แกน XZ เท่านั้น)
                Vector3 flatError = new Vector3(
                    averageFootPos.x - torsoRb.position.x,
                    0f,
                    averageFootPos.z - torsoRb.position.z
                );

                // ลด autoCenterForce แบบ proportional เมื่อแขนดึง
                // armPullIntensity=0 → ใช้เต็ม, armPullIntensity=1 → ลดเหลือ minCenterForceMultiplier
                float centerScale = Mathf.Lerp(1f, minCenterForceMultiplier, armPullIntensity);
                torsoRb.AddForce(flatError * (autoCenterGravityForce * centerScale), ForceMode.Acceleration);
            }
        }

        // ต้านแรงโน้มถ่วง (ระบบ Hover ต้องจัดการเองทั้งหมด)
        torsoRb.AddForce(-Physics.gravity, ForceMode.Acceleration);

        // Hover Spring: รักษาความสูง targetTorsoHeight เหนือพื้น
        // F = k * heightError - c * velocity  (PD Controller)
        if (Physics.Raycast(groundRaycastOrigin.position, Vector3.down, out RaycastHit hit, targetTorsoHeight * 2f, groundLayer))
        {
            float heightError = targetTorsoHeight - (groundRaycastOrigin.position.y - hit.point.y);
            float upwardForce = (heightError * heightSpringForce) - (torsoRb.linearVelocity.y * heightDamper);
            torsoRb.AddForce(Vector3.up * upwardForce, ForceMode.Acceleration);
        }

        // Upright Torque: ดึงลำตัวให้ตั้งตรง (ไม่ให้เอียงแกน X/Z)
        // ใช้ ToAngleAxis เพื่อหา angular error แล้วใส่ PD Torque
        Quaternion targetRot = Quaternion.Euler(0f, torsoRb.rotation.eulerAngles.y, 0f);
        Quaternion deltaRot  = targetRot * Quaternion.Inverse(torsoRb.rotation);
        deltaRot.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;

        if (angle != 0f)
        {
            Vector3 torque = (axis * (angle * uprightSpring)) - (torsoRb.angularVelocity * uprightDamper);
            torsoRb.AddTorque(torque, ForceMode.Acceleration);
        }
    }

    /// <summary>
    /// ผลักลำตัวขึ้นจากเท้าที่ปักพื้น และตรวจว่าฟื้นตัวได้แล้วหรือยัง
    /// </summary>
    public void ApplyContinuousRecoveryForce(Vector3 forcePosition, float strengthMultiplier = 1f)
    {
        if (currentState.Value != TorsoState.Ragdoll && currentState.Value != TorsoState.Falling) return;

        // ต้องรอ ragdoll delay ก่อนจึงจะฟื้นได้
        if (ragdollTimer < ragdollRecoveryDelay) return;

        torsoRb.AddForceAtPosition(
            Vector3.up * (continuousRecoveryForce * strengthMultiplier),
            forcePosition,
            ForceMode.Acceleration
        );

        // ตรวจสอบความสูง: ถ้าลำตัวสูงพอแล้ว → กลับมา Standing
        if (Physics.Raycast(groundRaycastOrigin.position, Vector3.down, out RaycastHit hit, targetTorsoHeight * 2f, groundLayer))
        {
            float currentHeight = groundRaycastOrigin.position.y - hit.point.y;
            if (currentHeight >= targetTorsoHeight * recoveryHeightThreshold)
            {
                balanceLossTimer   = 0f;
                ragdollTimer       = 0f;
                currentState.Value = TorsoState.Standing;
            }
        }
    }

    [Rpc(SendTo.Server)]
    public void ApplyRecoveryForceRpc(Vector3 forcePosition) => ApplyContinuousRecoveryForce(forcePosition);
}
