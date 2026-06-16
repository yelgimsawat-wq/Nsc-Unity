using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class TorsoMovement : NetworkBehaviour
{
    public enum TorsoState { Standing, Falling, Ragdoll }
    
    [Header("Network State")]
    public NetworkVariable<TorsoState> currentState = new NetworkVariable<TorsoState>(TorsoState.Standing);
    
    [Header("Fake Hover & Posture (Standing)")]
    [Tooltip("ความสูงเป้าหมายของลำตัวจากพื้น")]
    public float targetTorsoHeight = 1.6f;
    [Tooltip("แรงสปริงในการพยุงตัว (ตัวเลขน้อยลงได้เพราะเราต้านแรงโน้มถ่วงแล้ว)")]
    public float heightSpringForce = 300f;
    public float heightDamper = 30f;
    [Tooltip("แรงบิดให้ตัวตั้งตรง")]
    public float uprightSpring = 800f;
    public float uprightDamper = 60f;

    [Header("Balance Constraints")]
    public float maxBalanceAngle = 55f;
    public float fallGracePeriod = 0.5f;

    [Header("Continuous Recovery")]
    [Tooltip("แรงที่เท้าส่งมาดันลำตัวให้ลุกขึ้น (ยิ่งเยอะยิ่งลุกไว)")]
    public float continuousRecoveryForce = 800f;
    [Tooltip("ถ้าความสูงถึงกี่เปอร์เซ็นต์ของ targetHeight ถึงจะถือว่าลุกสำเร็จ")]
    public float recoveryHeightThreshold = 0.7f;

    [Header("References")]
    public Rigidbody torsoRb;
    public Transform groundRaycastOrigin;
    public LayerMask groundLayer;
    
    private List<PlayerFootForRobot> attachedFeet = new List<PlayerFootForRobot>();
    private float timeNotGrounded = 0f;

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
                // ปล่อยให้ Joint ทำงานตามอิสระ
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

        // เช็คการล้ม
        if (attachedFeet.Count > 0 && groundedCount == 0)
        {
            timeNotGrounded += Time.fixedDeltaTime;
            if (timeNotGrounded > fallGracePeriod)
            {
                currentState.Value = TorsoState.Falling;
                return;
            }
        }
        else
        {
            timeNotGrounded = 0f;
            
            // เช็คมุมเอียงเพื่อล้ม
            averageFootPos /= groundedCount;
            Vector3 directionToTorso = (torsoRb.position - averageFootPos).normalized;
            float leanAngle = Vector3.Angle(Vector3.up, directionToTorso);

            if (leanAngle > maxBalanceAngle)
            {
                currentState.Value = TorsoState.Falling;
                return;
            }
        }

        // 🎯 1. FAKE HOVER (ต้านแรงโน้มถ่วงโลก 100%)
        // ทำให้ตัวลอยนิ่งสนิท ไม่ร่วงหล่น
        torsoRb.AddForce(-Physics.gravity, ForceMode.Acceleration);

        // 🎯 2. รักษาระยะความสูง
        if (Physics.Raycast(groundRaycastOrigin.position, Vector3.down, out RaycastHit hit, targetTorsoHeight * 2f, groundLayer))
        {
            float currentHeight = groundRaycastOrigin.position.y - hit.point.y;
            float heightError = targetTorsoHeight - currentHeight;

            float upwardForce = (heightError * heightSpringForce) - (torsoRb.linearVelocity.y * heightDamper);
            torsoRb.AddForce(Vector3.up * upwardForce, ForceMode.Acceleration);
        }

        // 🎯 3. บังคับตัวตั้งตรง (Torque)
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

    // ฟังก์ชันนี้ถูกเรียกจากเท้า (ในฝั่ง Server) ทุกๆ เฟรมที่ผู้เล่นกด Q ค้างไว้
    public void ApplyContinuousRecoveryForce(Vector3 forcePosition, float strengthMultiplier = 1f)
    {
        if (currentState.Value == TorsoState.Ragdoll || currentState.Value == TorsoState.Falling)
        {
            // 🎯 เอาแรงดันปกติ มาคูณกับระยะความห่าง (ยิ่งไกล แรงยิ่งน้อยลง)
            float finalForce = continuousRecoveryForce * strengthMultiplier;
            torsoRb.AddForceAtPosition(Vector3.up * finalForce, forcePosition, ForceMode.Acceleration);

            // เช็คว่าความสูงถึงจุดที่ยืนไหวหรือยัง
            if (Physics.Raycast(groundRaycastOrigin.position, Vector3.down, out RaycastHit hit, 20f, groundLayer))
            {
                float currentHeight = groundRaycastOrigin.position.y - hit.point.y;
                if (currentHeight >= (targetTorsoHeight * recoveryHeightThreshold))
                {
                    currentState.Value = TorsoState.Standing;
                }
            }
        }
    }
}