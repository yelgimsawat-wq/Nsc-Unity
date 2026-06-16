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
    [Tooltip("สปีดการเคลื่อนที่ของมือตามเมาส์")]
    public float handMoveSpeed = 25f;
    [Tooltip("ความหนืดของมือ")]
    public float handDamper = 15f;
    public float planeYOffsetSpeed = 3f;
    public float grabRadius = 0.5f;
    
    [Tooltip("แรงที่แขนใช้ดึง/ลากลำตัว (ห้ามคูณ Time.fixedDeltaTime ในโค้ด)")]
    public float torsoPullForce = 60f; 
    public float detachedMoveSpeed = 20f;
    public LayerMask grabLayer;
    public LayerMask groundLayer;

    [Header("Mouse Range (World Space)")]
    public float mouseReachX = 3f;
    public float mouseReachY = 3f;
    public float mouseReachDepth = 3f;

    private float currentPlaneYOffset = 0f;
    private bool isGrabbing = false;
    private Rigidbody grabbedObject;
    private FixedJoint grabJoint;
    private Vector3 targetHandPosition;

    // ระบบกัน Network Spam
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

        if (currentState.Value == HandState.Attached)
        {
            if (torso.currentState.Value == TorsoMovement.TorsoState.Ragdoll) return;
            PerformArmMovement();
        }
    }

    private Vector2 GetNormalizedMousePosition()
    {
        float mouseX = Mathf.Clamp(Input.mousePosition.x, 0, Screen.width);
        float mouseY = Mathf.Clamp(Input.mousePosition.y, 0, Screen.height);
        return new Vector2((mouseX / Screen.width) * 2f - 1f, (mouseY / Screen.height) * 2f - 1f);
    }

    private void HandleInput()
    {
        if (currentState.Value == HandState.Attached)
        {
            if (Input.GetKey(KeyCode.E)) currentPlaneYOffset += planeYOffsetSpeed * Time.deltaTime;
            if (Input.GetKey(KeyCode.Q)) currentPlaneYOffset -= planeYOffsetSpeed * Time.deltaTime;
            currentPlaneYOffset = Mathf.Clamp(currentPlaneYOffset, -mouseReachDepth, mouseReachDepth);

            Vector2 mouseNorm = GetNormalizedMousePosition();
            
            Vector3 targetOffset = playerCamera.transform.right * (mouseNorm.x * mouseReachX)
                                 + playerCamera.transform.up    * (mouseNorm.y * mouseReachY);
            Vector3 baseCenter = pivotPoint.position + playerCamera.transform.forward * currentPlaneYOffset;
            
            Vector3 newTarget = baseCenter + targetOffset;
            
            Vector3 rayOrigin = newTarget + Vector3.up * 2f;
            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 5f, groundLayer))
            {
                if (newTarget.y < hit.point.y) newTarget.y = hit.point.y;
            }

            // ส่งข้อมูลเมื่อเมาส์ขยับเกิน Threshold เท่านั้น
            if (Vector3.Distance(lastSentTarget, newTarget) > RPC_SEND_THRESHOLD)
            {
                lastSentTarget = newTarget;
                UpdateHandTargetRpc(newTarget);
            }

            if (Input.GetMouseButtonDown(0)) TryGrabRpc();
            if (Input.GetMouseButtonUp(0)) ReleaseGrabRpc();

            if (torso.currentState.Value == TorsoMovement.TorsoState.Ragdoll && Input.GetKeyDown(KeyCode.Q))
            {
                if (IsServer) torso.ApplyContinuousRecoveryForce(pivotPoint.position);
            }
        }
    }

    [Rpc(SendTo.Server)] private void UpdateHandTargetRpc(Vector3 target) { targetHandPosition = target; }

    [Rpc(SendTo.Server)]
    private void TryGrabRpc()
    {
        isGrabbing = true;
        Collider[] hits = Physics.OverlapSphere(handRb.position, grabRadius, grabLayer);
        foreach (var hit in hits)
        {
            Rigidbody rb = hit.attachedRigidbody;
            if (rb != null)
            {
                grabbedObject = rb;
                if (!rb.isKinematic)
                {
                    grabJoint = handRb.gameObject.AddComponent<FixedJoint>();
                    grabJoint.connectedBody = rb;
                }
                break;
            }
        }
    }

    [Rpc(SendTo.Server)]
    private void ReleaseGrabRpc()
    {
        isGrabbing = false;
        if (grabJoint != null) Destroy(grabJoint);
        grabbedObject = null;
    }

    private void PerformArmMovement()
    {
        Vector3 dirFromPivot = targetHandPosition - pivotPoint.position;
        
        if (isGrabbing && grabbedObject != null && grabbedObject.isKinematic)
        {
            Vector3 pushDir = pivotPoint.position - targetHandPosition;
            // ลบ Time.fixedDeltaTime ออก
            torso.torsoRb.AddForceAtPosition(pushDir * torsoPullForce, pivotPoint.position, ForceMode.Acceleration);
        }
        else
        {
            if (dirFromPivot.magnitude > maxArmLength)
            {
                targetHandPosition = pivotPoint.position + dirFromPivot.normalized * maxArmLength;
                // ลบ Time.fixedDeltaTime ออก
                torso.torsoRb.AddForceAtPosition(dirFromPivot.normalized * torsoPullForce, pivotPoint.position, ForceMode.Acceleration);
            }

            Vector3 velocityTarget = (targetHandPosition - handRb.position) * handMoveSpeed;
            Vector3 force = (velocityTarget - handRb.linearVelocity) * handDamper; 
            handRb.AddForce(force, ForceMode.Acceleration);
        }
    }
}