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
    
    [Tooltip("แรงดึงลำตัวกลับมาอยู่ตรงกลางเหนือเท้าอัตโนมัติ (ยิ่งเยอะยิ่งล้มยาก)")]
    public float autoCenterGravityForce = 250f;

    [Header("Balance Constraints (Grace Period)")]
    public float maxBalanceAngle = 55f;
    [Tooltip("เวลาที่จะยอมให้ตัวลอยนิ่งๆ ก่อนจะล้มจริงๆ (วินาที)")]
    public float fallGracePeriod = 1.0f;

    [Header("Continuous Recovery")]
    public float continuousRecoveryForce = 800f;
    public float recoveryHeightThreshold = 0.7f;

    [Header("References")]
    public Rigidbody torsoRb;
    public Transform groundRaycastOrigin;
    public LayerMask groundLayer;
    
    private List<PlayerFootForRobot> attachedFeet = new List<PlayerFootForRobot>();
    
    // ใช้นาฬิกาจับเวลาตัวเดียว ควบคุมทั้งการล้มจากเท้าลอยและตัวเอียง
    private float balanceLossTimer = 0f;

    public void RegisterFoot(PlayerFootForRobot foot)
    {
        if (!attachedFeet.Contains(foot)) attachedFeet.Add(foot);
    }

    public void UnregisterFoot(PlayerFootForRobot foot)
    {
        if (attachedFeet.Contains(foot)) attachedFeet.Remove(foot);
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
                break;
            case TorsoState.Ragdoll:
                break;
        }
    }

    private void HandleFakeHoverAndPosture()
    {
        int groundedCount = 0;
        Vector3 averageFootPos = Vector3.zero;

        foreach (var foot in attachedFeet)
        {
            if (foot.IsGrounded())
            {
                groundedCount++;
                averageFootPos += foot.footRb.position;
            }
        }

        bool isLosingBalance = false;

        // 1. ตรวจสอบเงื่อนไขการสูญเสียสมดุล
        if (attachedFeet.Count > 0 && groundedCount == 0)
        {
            // เงื่อนไขที่ 1: เท้าลอยจากพื้นหมดเลย
            isLosingBalance = true;
        }
        else if (groundedCount > 0)
        {
            averageFootPos /= groundedCount;
                // 🌟 [AUTO BALANCE] ถ้ายังไม่ล้ม ให้ลำตัวพยายามดึงตัวเองมาอยู่จุดศูนย์กลาง (เหนือเท้า) เสมอ
                Vector3 targetTorsoPos = averageFootPos + (Vector3.up * targetTorsoHeight);
                // ดึงเฉพาะแกน X และ Z (ไม่ยุ่งกับความสูง Y)
                Vector3 flatError = new Vector3(targetTorsoPos.x - torsoRb.position.x, 0, targetTorsoPos.z - torsoRb.position.z);
                
                torsoRb.AddForce(flatError * autoCenterGravityForce, ForceMode.Acceleration);
        }

        // 2. ระบบเวลานับถอยหลังการล้ม (Grace Period / Float Time)
        if (isLosingBalance)
        {
            balanceLossTimer += Time.fixedDeltaTime;
            if (balanceLossTimer >= fallGracePeriod)
            {
                // หมดเวลาโกงความตาย ร่วงของจริง
                currentState.Value = TorsoState.Falling;
                return;
            }
        }
        else
        {
            // ถ้ากลับมาทรงตัวได้ ให้ค่อยๆ ลดเวลาสะสมลง (เผื่อกรณีเดินสะดุดแป๊บเดียว)
            balanceLossTimer = Mathf.Max(0, balanceLossTimer - (Time.fixedDeltaTime * 2f));
        }

        // ==============================================================
        // 🎯 FAKE HOVER + POSTURE (ทำงานตลอดเวลาแม้กำลังนับถอยหลังล้ม)
        // ==============================================================
        
        // 1. ต้านแรงโน้มถ่วง 100% ทำให้ตัวลอยค้างกลางอากาศเหมือนถูกจับไว้
        torsoRb.AddForce(-Physics.gravity, ForceMode.Acceleration);

        // 2. รักษาระยะความสูง
        if (Physics.Raycast(groundRaycastOrigin.position, Vector3.down, out RaycastHit hit, targetTorsoHeight * 2f, groundLayer))
        {
            float currentHeight = groundRaycastOrigin.position.y - hit.point.y;
            float heightError = targetTorsoHeight - currentHeight;

            float upwardForce = (heightError * heightSpringForce) - (torsoRb.linearVelocity.y * heightDamper);
            torsoRb.AddForce(Vector3.up * upwardForce, ForceMode.Acceleration);
        }

        // 3. บังคับกระดูกสันหลังให้ตั้งตรง
        Quaternion targetRotation = Quaternion.Euler(0, torsoRb.rotation.eulerAngles.y, 0);
        Quaternion deltaRot = targetRotation * Quaternion.Inverse(torsoRb.rotation);
        deltaRot.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;
        
        if (angle != 0)
        {
            Vector3 angularTorque = (axis * (angle * uprightSpring)) - (torsoRb.angularVelocity * uprightDamper);
            torsoRb.AddTorque(angularTorque, ForceMode.Acceleration);
        }
    }

    public void ApplyContinuousRecoveryForce(Vector3 forcePosition, float strengthMultiplier = 1f)
    {
        if (currentState.Value == TorsoState.Ragdoll || currentState.Value == TorsoState.Falling)
        {
            float finalForce = continuousRecoveryForce * strengthMultiplier;
            torsoRb.AddForceAtPosition(Vector3.up * finalForce, forcePosition, ForceMode.Acceleration);

            if (Physics.Raycast(groundRaycastOrigin.position, Vector3.down, out RaycastHit hit, targetTorsoHeight * 2f, groundLayer))
            {
                float currentHeight = groundRaycastOrigin.position.y - hit.point.y;
                if (currentHeight >= (targetTorsoHeight * recoveryHeightThreshold))
                {
                    // ลุกสำเร็จ! รีเซ็ตเวลาล้ม แล้วกลับมายืน
                    balanceLossTimer = 0f;
                    currentState.Value = TorsoState.Standing;
                }
            }
        }
    }

    // 🛠️ สะพานเชื่อมสำหรับสคริปต์มือ (กัน Error CS1061)
    [Rpc(SendTo.Server)]
    public void ApplyRecoveryForceRpc(Vector3 forcePosition)
    {
        ApplyContinuousRecoveryForce(forcePosition);
    }
}