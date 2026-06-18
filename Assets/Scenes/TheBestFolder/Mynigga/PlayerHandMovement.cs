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
    public float detachedMoveSpeed = 20f;
    public LayerMask grabLayer;
    public LayerMask groundLayer;

    [Header("Smoothing (Anti-Jitter)")]
    [Tooltip("ความเร็วในการเกลี่ยตำแหน่งเป้าหมายมือ (8-20 แนะนำ)")]
    public float targetSmoothSpeed = 12f;

    [Header("Mouse Range (World Space)")]
    public float mouseReachX = 3f;
    public float mouseReachY = 3f;
    public float mouseReachDepth = 3f;

    private float currentPlaneYOffset = 0f;
    private bool isGrabbing = false;
    private Rigidbody grabbedObject;
    private FixedJoint grabJoint;

    // ตำแหน่งดิบจาก RPC
    private Vector3 targetHandPosition;
    // ตำแหน่งที่ผ่าน Exponential Lerp แล้ว ใช้คำนวณแรงจริง
    private Vector3 smoothedHandTarget;
    private bool smoothedHandInitialized = false;

    private Vector3 lastSentTarget;
    private const float RPC_SEND_THRESHOLD = 0.05f;

    void Update()
    {
        if (!IsOwner || playerCamera == null) return;
        HandleInput();
    }

    void FixedUpdate()
    {
        if (!IsServer) return;
        SmoothHandTarget();

        if (currentState.Value == HandState.Attached)
        {
            if (torso.currentState.Value == TorsoMovement.TorsoState.Ragdoll) return;
            PerformArmMovement();
        }
    }

    /// <summary>
    /// Framerate-Independent Exponential Lerp สำหรับมือ
    /// เหมือนกับเท้า ป้องกัน RPC Snap ทำให้แรงพุ่งฉับพลัน
    /// </summary>
    private void SmoothHandTarget()
    {
        float t = 1f - Mathf.Exp(-targetSmoothSpeed * Time.fixedDeltaTime);
        if (!smoothedHandInitialized) { smoothedHandTarget = targetHandPosition; smoothedHandInitialized = true; }
        else smoothedHandTarget = Vector3.Lerp(smoothedHandTarget, targetHandPosition, t);
    }

    private Vector2 GetNormalizedMousePosition()
    {
        return new Vector2(
            (Mathf.Clamp(Input.mousePosition.x, 0, Screen.width)  / Screen.width)  * 2f - 1f,
            (Mathf.Clamp(Input.mousePosition.y, 0, Screen.height) / Screen.height) * 2f - 1f
        );
    }

    private void HandleInput()
    {
        if (currentState.Value != HandState.Attached) return;

        if (Input.GetKey(KeyCode.E)) currentPlaneYOffset += planeYOffsetSpeed * Time.deltaTime;
        if (Input.GetKey(KeyCode.Q)) currentPlaneYOffset -= planeYOffsetSpeed * Time.deltaTime;
        currentPlaneYOffset = Mathf.Clamp(currentPlaneYOffset, -mouseReachDepth, mouseReachDepth);

        Vector2 mouseNorm  = GetNormalizedMousePosition();
        Vector3 newTarget  = pivotPoint.position+ playerCamera.transform.forward * currentPlaneYOffset
                           + playerCamera.transform.right   * (mouseNorm.x * mouseReachX)
                           + playerCamera.transform.up      * (mouseNorm.y * mouseReachY);

        // ป้องกันมือจมพื้น
        if (Physics.Raycast(newTarget + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 5f, groundLayer))
            if (newTarget.y < hit.point.y) newTarget.y = hit.point.y;

        if (Vector3.Distance(lastSentTarget, newTarget) > RPC_SEND_THRESHOLD)
        { lastSentTarget = newTarget; UpdateHandTargetRpc(newTarget); }

        if (Input.GetMouseButtonDown(0)) TryGrabRpc();
        if (Input.GetMouseButtonUp(0))   ReleaseGrabRpc();

        // Recovery ผ่านมือ (กด Q ขณะล้ม)
        if (torso.currentState.Value == TorsoMovement.TorsoState.Ragdoll && Input.GetKeyDown(KeyCode.Q))ApplyHandRecoveryRpc();
    }

    [Rpc(SendTo.Server)] private void UpdateHandTargetRpc(Vector3 target) { targetHandPosition = target; }

    [Rpc(SendTo.Server)]
    private void ApplyHandRecoveryRpc()
    {
        torso.ApplyContinuousRecoveryForce(pivotPoint.position);}

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
            if (!rb.isKinematic)
            {
                grabJoint = handRb.gameObject.AddComponent<FixedJoint>();
                grabJoint.connectedBody = rb;
            }
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

    /// <summary>
    /// Spring-Damper ควบคุมมือ + ดึงลำตัวเมื่อแขนยืดเกิน maxArmLength
    /// ใช้ smoothedHandTarget แทน targetHandPosition ดิบ
    /// </summary>
    private void PerformArmMovement()
    {
        Vector3 dirFromPivot = smoothedHandTarget - pivotPoint.position;

        if (isGrabbing && grabbedObject != null && grabbedObject.isKinematic)
        {
            // จับวัตถุคงที่: ดึงลำตัวเข้าหาวัตถุ
            Vector3 pushDir = pivotPoint.position - smoothedHandTarget;
            torso.torsoRb.AddForceAtPosition(pushDir * torsoPullForce, pivotPoint.position, ForceMode.Acceleration);

            // แจ้ง Torso ว่าแขนกำลังดึง (ลด Auto-Center ไม่ให้สู้กัน)
            torso.armPullIntensity = Mathf.Clamp01(dirFromPivot.magnitude / maxArmLength);
        }
        else
        {
            torso.armPullIntensity = 0f;

            if (dirFromPivot.magnitude > maxArmLength)
            {
                // แขนยืดเกิน: ดึงลำตัวตาม
                smoothedHandTarget = pivotPoint.position + dirFromPivot.normalized * maxArmLength;
                torso.torsoRb.AddForceAtPosition(dirFromPivot.normalized * torsoPullForce, pivotPoint.position, ForceMode.Acceleration);
            }

            // Spring-Damper เคลื่อนมือไปหาเป้าหมาย
            Vector3 velocityTarget = (smoothedHandTarget - handRb.position) * handMoveSpeed;
            handRb.AddForce((velocityTarget - handRb.linearVelocity) * handDamper, ForceMode.Acceleration);
        }
    }
}
