using UnityEngine;
using Unity.Netcode;

public class PlayerHandMovement : NetworkBehaviour
{
    public enum HandState { Attached, Detached }

    [Header("Network State")]
    public NetworkVariable<HandState> currentState = new NetworkVariable<HandState>(HandState.Attached);

    [Header("References")]
    public TorsoMovement torso;
    public Rigidbody handRb;
    public Transform pivotPoint;
    public Camera playerCamera;

    [Header("Movement & IK Tuning")]
    public float maxArmLength = 1.8f;
    public float handMoveSpeed = 25f;
    public float handDamper = 15f;
    public float planeYOffsetSpeed = 3f;
    public float grabRadius = 0.5f;
    public float torsoPullForce = 60f;
    [Tooltip("แรงที่ดึงตัวเมื่อจับ Kinematic Object (ใช้ปีนป่าย)")]
    public float kinematicPullForce = 150f;
    public float detachedMoveSpeed = 20f;
    public LayerMask grabLayer;
    public LayerMask groundLayer;

    [Header("Smoothing (Anti-Jitter)")]
    public float targetSmoothSpeed = 12f;

    [Header("Mouse Range (World Space)")]
    public float mouseReachX = 3f;
    public float mouseReachY = 3f;
    public float mouseReachDepth = 3f;

    protected float currentPlaneYOffset = 0f;
    protected bool isGrabbing = false;
    protected Rigidbody grabbedObject;
    protected FixedJoint grabJoint;

    protected Vector3 targetHandPosition;
    protected Vector3 smoothedHandTarget;
    protected bool smoothedHandInitialized = false;

    protected Vector3 lastSentTarget;
    protected const float RPC_SEND_THRESHOLD = 0.05f;

    protected virtual void Update()
    {
        if (!IsOwner || playerCamera == null) return;
        HandleInput();
    }

    protected virtual void FixedUpdate()
    {
        if (!IsServer) return;
        SmoothHandTarget();

        if (currentState.Value == HandState.Attached)
        {
            // ✅ นำเงื่อนไขเช็ค Ragdoll ออกแล้ว! มือสามารถขยับและช่วยลากตัวได้ตลอดเวลา
            PerformArmMovement();
        }
    }

    private void SmoothHandTarget()
    {
        float t = 1f - Mathf.Exp(-targetSmoothSpeed * Time.fixedDeltaTime);
        if (!smoothedHandInitialized) { smoothedHandTarget = targetHandPosition; smoothedHandInitialized = true; }
        else smoothedHandTarget = Vector3.Lerp(smoothedHandTarget, targetHandPosition, t);
    }

    private Vector2 GetNormalizedMousePosition()
    {
        return new Vector2(
            (Mathf.Clamp(Input.mousePosition.x, 0, Screen.width) / Screen.width) * 2f - 1f,
            (Mathf.Clamp(Input.mousePosition.y, 0, Screen.height) / Screen.height) * 2f - 1f
        );
    }

    protected virtual void HandleInput()
    {
        if (currentState.Value != HandState.Attached) return;

        // ✅ W/S สำหรับ เดินหน้า-ถอยหลัง (แกนลึก/Z-Depth)
        if (Input.GetKey(KeyCode.W)) currentPlaneYOffset += planeYOffsetSpeed * Time.deltaTime;
        if (Input.GetKey(KeyCode.S)) currentPlaneYOffset -= planeYOffsetSpeed * Time.deltaTime;
        currentPlaneYOffset = Mathf.Clamp(currentPlaneYOffset, -mouseReachDepth, mouseReachDepth);

        Vector2 mouseNorm = GetNormalizedMousePosition();

        // ✅ เมาส์ X = ซ้าย/ขวา, เมาส์ Y = บน/ล่าง
        Vector3 newTarget = pivotPoint.position
                          + playerCamera.transform.forward * currentPlaneYOffset
                          + playerCamera.transform.right * (mouseNorm.x * mouseReachX)
                          + playerCamera.transform.up * (mouseNorm.y * mouseReachY);   // ใช้งานแกน Y แล้ว!

        if (Physics.Raycast(newTarget + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 5f, groundLayer))
            if (newTarget.y < hit.point.y) newTarget.y = hit.point.y;

        if (Vector3.Distance(lastSentTarget, newTarget) > RPC_SEND_THRESHOLD)
        { lastSentTarget = newTarget; UpdateHandTargetRpc(newTarget); }

        // ✅ กดค้างเพื่อจับ / ปล่อยเพื่อคลาย
        if (Input.GetMouseButtonDown(0)) TryGrabRpc();
        if (Input.GetMouseButtonUp(0)) ReleaseGrabRpc();

        if (torso.currentState.Value == TorsoMovement.TorsoState.Ragdoll && Input.GetKeyDown(KeyCode.Q))
            ApplyHandRecoveryRpc();
    }

    [Rpc(SendTo.Server)] private void UpdateHandTargetRpc(Vector3 target) { targetHandPosition = target; }
    [Rpc(SendTo.Server)] private void ApplyHandRecoveryRpc() { torso.ApplyContinuousRecoveryForce(pivotPoint.position); }

    [Rpc(SendTo.Server)]
    private void TryGrabRpc()
    {
        isGrabbing = true;
        Collider[] hits = Physics.OverlapSphere(handRb.position, grabRadius, grabLayer);
        foreach (var h in hits)
        {
            Rigidbody rb = h.attachedRigidbody;
            if (rb == null) continue;
            grabbedObject = rb;

            // ✅ จับได้ทั้ง Kinematic และ Dynamic
            grabJoint = handRb.gameObject.AddComponent<FixedJoint>();
            grabJoint.connectedBody = rb;

            break;
        }
    }

    [Rpc(SendTo.Server)]
    private void ReleaseGrabRpc()
    {
        isGrabbing = false;
        if (grabJoint != null) Destroy(grabJoint);
        grabbedObject = null;
    }

    protected virtual void PerformArmMovement()
    {
        Vector3 dirFromPivot = smoothedHandTarget - pivotPoint.position;
        float currentDistance = dirFromPivot.magnitude;
        Vector3 physicsTarget = smoothedHandTarget;

        if (isGrabbing && grabbedObject != null && grabbedObject.isKinematic)
        {
            // ==========================================
            // 🧗 CLIMBING MODE (จับ Kinematic = ปีน)
            // ==========================================
            // เมื่อจับอยู่ มือจะไม่ขยับไปหาเมาส์ (FixedJoint ล็อกไว้แล้ว)
            // แต่ระยะห่างจากเมาส์ (physicsTarget) ถึง มือ จะกลายเป็นแรงดึงให้ลำตัวเคลื่อนที่แทน!

            Vector3 climbPullDir = physicsTarget - handRb.position;

            // ดึงลำตัวไปตามทิศทางที่ผู้เล่นบังคับเมาส์
            torso.torsoRb.AddForce(climbPullDir * kinematicPullForce, ForceMode.Acceleration);

            // เพิ่ม Stress
            float stressThisFrame = kinematicPullForce * Time.fixedDeltaTime * Mathf.Clamp01(currentDistance / maxArmLength);
            torso.AddStress(stressThisFrame);
            torso.armPullIntensity = Mathf.Clamp01(currentDistance / maxArmLength);
        }
        else
        {
            // ==========================================
            // 👋 NORMAL/GRABBING DYNAMIC MODE (ขยับมือปกติ)
            // ==========================================
            torso.armPullIntensity = 0f;

            if (currentDistance < 0.05f)
            {
                physicsTarget = pivotPoint.position + (dirFromPivot.normalized * 0.05f);
            }
            else if (currentDistance > maxArmLength)
            {
                physicsTarget = pivotPoint.position + (dirFromPivot / currentDistance) * maxArmLength;
                Vector3 pullDir = dirFromPivot / currentDistance;
                torso.torsoRb.AddForceAtPosition(pullDir * torsoPullForce, pivotPoint.position, ForceMode.Acceleration);

                float stressThisFrame = torsoPullForce * Time.fixedDeltaTime * 0.5f;
                torso.AddStress(stressThisFrame);
            }

            // เคลื่อนมือไปตามปกติ
            Vector3 velocityTarget = (physicsTarget - handRb.position) * handMoveSpeed;
            handRb.AddForce((velocityTarget - handRb.linearVelocity) * handDamper, ForceMode.Acceleration);
        }
    }
}