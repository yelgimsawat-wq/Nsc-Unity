using UnityEngine;
using Unity.Netcode;

public class PlayerFootForRobot : NetworkBehaviour
{
    public enum FootState { Attached, Detached }
    [Header("Network State")]
    public NetworkVariable<FootState> currentState = new NetworkVariable<FootState>(FootState.Attached);
    public bool isStepping = false;
    public bool isPushingRecovery = false;
    public bool isJumping = false;

    // เท้าจะ "สมดุล" ก็ต่อเมื่อ: ไม่ก้าว + ไม่กระโดด + ไม่พยายามดันตัวลุก + ติดพื้น
    public bool IsBalanced => !isStepping && !isJumping && !isPushingRecovery && IsGrounded();

    [Header("References")]
    public TorsoMovement torso;
    public Rigidbody footRb;
    public Transform pivotPoint;
    public Camera playerCamera;
    public Vector3 pivotOffset = Vector3.zero;
    public Vector3 PivotPosition => pivotPoint != null ? pivotPoint.TransformPoint(pivotOffset) : transform.position;

    [Header("Movement & IK Settings")]
    public float maxLegLength = 1.5f;
    [Tooltip("ระยะขาหดสั้นสุด (ใช้เฉพาะตอนยืน/เดิน)")]
    public float minLegLength = 0.4f;
    public float footMoveSpeed = 15f;
    public float balanceShiftMultiplier = 0.3f;
    public float detachedMoveSpeed = 20f;
    public float heightAdjustSpeed = 3f;
    public float legDamper = 30f;
    public LayerMask groundLayer;

    [Header("Jump Settings")]
    public float footJumpForce = 15f;
    public float torsoJumpForce = 400f;

    [Header("Smoothing (Anti-Jitter)")]
    public float targetSmoothSpeed = 12f;

    [Header("Standing Stability")]
    public float standingUpwardPull = 25f;

    [Header("Recovery Mechanics")]
    public float recoveryProximityThreshold = 5f;
    public float minRecoveryMultiplier = 0.2f;

    [Header("Mouse Range (World Space)")]
    public float mouseReachX = 2f;
    public float mouseReachY = 2f;
    private float currentYOffset = 0f;

    [Header("Auto Height Settings")]
    public float autoHeightDelay = 0.5f;
    private float holdTimer = 0f;
    private bool autoHeightEnabled = false;

    [Header("Magnetic Boots Settings")]
    public float breakForceLimit = 8000f;
    private FixedJoint currentPlantedJoint;

    [Header("Ragdoll Ground Check")]
    public float groundSeekForce = 50f;
    public float groundCheckDistance = 5f;

    private Vector3 targetFootPosition;
    private Vector3 balanceShiftMousePos;
    private Vector3 detachedTargetPos;

    private Vector3 smoothedFootTarget;
    private Vector3 smoothedBalanceTarget;
    private Vector3 smoothedDetachedTarget;
    private bool smoothedTargetInitialized, smoothedBalanceInitialized, smoothedDetachedInitialized;

    private Vector3 lastSentTarget, lastSentBalance, lastSentDetached;
    private const float RPC_SEND_THRESHOLD = 0.05f;

    private Vector3 plantedPosition;
    private bool isPlantedSet = false;
    private bool wasKinematic = false;

    public override void OnNetworkSpawn()
    {
        if (IsServer && torso != null) torso.RegisterFoot(this);
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && torso != null) torso.UnregisterFoot(this);
    }

    void Update()
    {
        if (!IsOwner || playerCamera == null) return;
        HandleInput();
    }

    void FixedUpdate()
    {
        if (!IsServer) return;
        SmoothTargets();

        if (isJumping && footRb.linearVelocity.y <= 0.1f && IsGrounded())
        {
            isJumping = false;
        }

        if (currentState.Value == FootState.Attached)
        {
            bool isRagdoll = torso.currentState.Value == TorsoMovement.TorsoState.Ragdoll || torso.currentState.Value == TorsoMovement.TorsoState.Falling;

            if (isRagdoll)
            {
                ReleaseKinematicLock();
                if (isPushingRecovery)
                {
                    // ✅ ถ้ากด Q: ล็อกเท้ากับพื้นเพื่อส่งแรงลุก
                    ApplyKinematicLock();
                    Vector2 footXZ = new Vector2(footRb.position.x, footRb.position.z);
                    Vector2 pivotXZ = new Vector2(PivotPosition.x, PivotPosition.z);
                    float ratio = Mathf.Clamp01(Vector2.Distance(footXZ, pivotXZ) / recoveryProximityThreshold);
                    torso.ApplyContinuousRecoveryForce(PivotPosition, Mathf.Lerp(1f, minRecoveryMultiplier, ratio));
                }
                else
                {
                    // ✅ ถ้าไม่ได้กด Q: เท้าตามเมาส์อย่างอิสระ ไม่ผลักออกจากลำตัว และดึงหาพื้นเสมอ
                    PerformRagdollFreePhysics(smoothedFootTarget);
                    if (!IsGrounded()) footRb.AddForce(Vector3.down * groundSeekForce, ForceMode.Acceleration);
                }
            }
            else
            {
                if (isStepping || isJumping)
                {
                    ReleaseKinematicLock();
                    PerformFootSpringPhysics(smoothedFootTarget);
                }
                else
                {
                    ApplyKinematicLock();
                    PerformStandingPhysics();
                }
            }
        }
        else
        {
            ReleaseKinematicLock();
            PerformDetachedPhysics();
        }
    }

    private void SmoothTargets()
    {
        float t = 1f - Mathf.Exp(-targetSmoothSpeed * Time.fixedDeltaTime);
        if (!smoothedTargetInitialized) { smoothedFootTarget = targetFootPosition; smoothedTargetInitialized = true; }
        else smoothedFootTarget = Vector3.Lerp(smoothedFootTarget, targetFootPosition, t);

        if (!smoothedBalanceInitialized) { smoothedBalanceTarget = balanceShiftMousePos; smoothedBalanceInitialized = true; }
        else smoothedBalanceTarget = Vector3.Lerp(smoothedBalanceTarget, balanceShiftMousePos, t);

        if (!smoothedDetachedInitialized) { smoothedDetachedTarget = detachedTargetPos; smoothedDetachedInitialized = true; }
        else smoothedDetachedTarget = Vector3.Lerp(smoothedDetachedTarget, detachedTargetPos, t);
    }

    private void ApplyKinematicLock()
    {
        if (!isPlantedSet)
        {
            if (Physics.Raycast(footRb.position + Vector3.up * 0.2f, Vector3.down, out RaycastHit hit, groundCheckDistance, groundLayer))
            {
                plantedPosition = hit.point;
                isPlantedSet = true;
                
                if (currentPlantedJoint == null)
                {
                    currentPlantedJoint = footRb.gameObject.AddComponent<FixedJoint>();
                    currentPlantedJoint.breakForce = breakForceLimit;
                    currentPlantedJoint.breakTorque = breakForceLimit;
                    
                    if (hit.collider.attachedRigidbody != null)
                    {
                        currentPlantedJoint.connectedBody = hit.collider.attachedRigidbody;
                    }
                }
            }
            else
            {
                footRb.AddForce(Vector3.down * groundSeekForce, ForceMode.Acceleration);
                return;
            }
        }
    }

    private void ReleaseKinematicLock()
    {
        isPlantedSet = false;
        
        if (currentPlantedJoint != null)
        {
            Destroy(currentPlantedJoint);
            currentPlantedJoint = null;
        }

        if (wasKinematic && footRb.isKinematic)
        {
            footRb.isKinematic = false;
            footRb.linearVelocity = Vector3.zero;
            footRb.angularVelocity = Vector3.zero;
            wasKinematic = false;
        }
    }

    void OnJointBreak(float breakForce)
    {
        Debug.Log($"Foot joint broke due to massive force: {breakForce}");
        isPlantedSet = false;
        currentPlantedJoint = null;
    }

    /// <summary>
    /// สำหรับตอน Ragdoll โดยเฉพาะ: อิสระ 100% ตามเมาส์ ไม่ผลักหนีลำตัว
    /// </summary>
    private void PerformRagdollFreePhysics(Vector3 rawTarget)
    {
        Vector3 dir = rawTarget - PivotPosition;
        if (dir.magnitude > maxLegLength)
        {
            rawTarget = PivotPosition + dir.normalized * maxLegLength;
        }

        Vector3 velocityTarget = (rawTarget - footRb.position) * (footMoveSpeed * 0.1f);
        footRb.AddForce((velocityTarget - footRb.linearVelocity) * (legDamper * 0.1f), ForceMode.Acceleration);
    }

    /// <summary>
    /// สำหรับตอนก้าวเดิน/กระโดด: มีการเช็ค minLegLength ไม่ให้ขาพับทะลุลำตัว
    /// </summary>
    private void PerformFootSpringPhysics(Vector3 rawTarget)
    {
        Vector3 dir = rawTarget - PivotPosition;
        float dist = dir.magnitude;

        if (dist < minLegLength)
        {
            rawTarget = PivotPosition + (dist > 0.01f ? dir.normalized : pivotPoint.forward) * minLegLength;
        }
        else if (dist > maxLegLength)
        {
            rawTarget = PivotPosition + dir.normalized * maxLegLength;
        }

        Vector3 velocityTarget = (rawTarget - footRb.position) * (footMoveSpeed * 0.1f);
        footRb.AddForce((velocityTarget - footRb.linearVelocity) * (legDamper * 0.1f), ForceMode.Acceleration);
    }

    private void PerformStandingPhysics()
    {
        bool isRagdoll = torso.currentState.Value == TorsoMovement.TorsoState.Ragdoll || torso.currentState.Value == TorsoMovement.TorsoState.Falling;
        if (isRagdoll) return;

        Vector3 offset = (smoothedBalanceTarget - PivotPosition) * balanceShiftMultiplier;
        Vector3 pullDir = (footRb.position + Vector3.up * maxLegLength + offset) - PivotPosition;
        torso.torsoRb.AddForceAtPosition(pullDir * standingUpwardPull, PivotPosition, ForceMode.Acceleration);
    }

    private void PerformDetachedPhysics()
    {
        footRb.linearVelocity = (smoothedDetachedTarget - footRb.position) * detachedMoveSpeed;
    }

    private Vector2 GetNormalizedMousePosition()
    {
        return new Vector2(
            (Mathf.Clamp(Input.mousePosition.x, 0, Screen.width) / Screen.width) * 2f - 1f,
            (Mathf.Clamp(Input.mousePosition.y, 0, Screen.height) / Screen.height) * 2f - 1f
        );
    }

    private void HandleInput()
    {
        if (currentState.Value == FootState.Attached)
        {
            bool isRagdoll = torso.currentState.Value == TorsoMovement.TorsoState.Ragdoll || torso.currentState.Value == TorsoMovement.TorsoState.Falling;

            Vector2 mouseNorm = GetNormalizedMousePosition();
            Vector3 camFwd = playerCamera.transform.forward; camFwd.y = 0; camFwd.Normalize();
            Vector3 camRight = playerCamera.transform.right; camRight.y = 0; camRight.Normalize();
            Vector3 mouseOffset = camRight * (mouseNorm.x * mouseReachX) + camFwd * (mouseNorm.y * mouseReachY);

            Vector3 newBalance = PivotPosition + mouseOffset;
            if (Vector3.Distance(lastSentBalance, newBalance) > RPC_SEND_THRESHOLD)
            { lastSentBalance = newBalance; UpdateBalanceShiftRpc(newBalance); }

            if (!isRagdoll)
            {
                if (Input.GetKeyDown(KeyCode.Space) && IsGrounded() && !isJumping)
                {
                    ApplyJumpRpc();
                }

                bool holdingClick = Input.GetMouseButton(0);
                if (holdingClick)
                {
                    if (!isStepping)
                    {
                        isStepping = true; SetSteppingStateRpc(true);
                        holdTimer = 0f; autoHeightEnabled = false;
                    }
                    holdTimer += Time.deltaTime;
                    if (holdTimer >= autoHeightDelay) autoHeightEnabled = true;
                }
                else
                {
                    if (isStepping) { isStepping = false; SetSteppingStateRpc(false); holdTimer = 0f; autoHeightEnabled = false; }
                }

                if (isStepping)
                {
                    if (Input.GetKey(KeyCode.E)) currentYOffset += heightAdjustSpeed * Time.deltaTime;
                    if (Input.GetKey(KeyCode.Q)) currentYOffset -= heightAdjustSpeed * Time.deltaTime;
                    currentYOffset = Mathf.Clamp(currentYOffset, -maxLegLength, 0f);

                    Vector3 newTarget;
                    if (Physics.Raycast(PivotPosition + mouseOffset + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 10f, groundLayer))
                    {
                        if (autoHeightEnabled)
                        {
                            float pivotHeight = PivotPosition.y;
                            float groundHeight = hit.point.y;
                            float optimalHeight = Mathf.Clamp(pivotHeight - groundHeight, minLegLength, maxLegLength);
                            newTarget = hit.point + Vector3.up * (optimalHeight * 0.5f);
                        }
                        else newTarget = hit.point + Vector3.up * currentYOffset;
                    }
                    else newTarget = PivotPosition + mouseOffset + Vector3.down * maxLegLength;

                    if (Vector3.Distance(lastSentTarget, newTarget) > RPC_SEND_THRESHOLD)
                    { lastSentTarget = newTarget; UpdateFootTargetRpc(newTarget); }
                }

                if (isPushingRecovery) { isPushingRecovery = false; SetRecoveryInputRpc(false); }
            }
            else
            {
                Vector3 newTarget;
                if (Physics.Raycast(PivotPosition + mouseOffset + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 10f, groundLayer))
                    newTarget = hit.point;
                else
                    newTarget = PivotPosition + mouseOffset;

                if (Vector3.Distance(lastSentTarget, newTarget) > RPC_SEND_THRESHOLD)
                { lastSentTarget = newTarget; UpdateFootTargetRpc(newTarget); }

                Vector2 footXZ = new Vector2(footRb.position.x, footRb.position.z);
                Vector2 pivotXZ = new Vector2(PivotPosition.x, PivotPosition.z);
                bool validPush = Input.GetKey(KeyCode.Q) && Vector2.Distance(footXZ, pivotXZ) <= recoveryProximityThreshold && IsGrounded();

                if (validPush != isPushingRecovery) { isPushingRecovery = validPush; SetRecoveryInputRpc(validPush); }
            }
        }
        else
        {
            Vector2 mouseNorm = GetNormalizedMousePosition();
            Vector3 camFwd = playerCamera.transform.forward; camFwd.y = 0; camFwd.Normalize();
            Vector3 camRight = playerCamera.transform.right; camRight.y = 0; camRight.Normalize();

            if (Physics.Raycast(footRb.position + (camRight * mouseNorm.x + camFwd * mouseNorm.y) * 5f + Vector3.up * 5f,
                                Vector3.down, out RaycastHit hit, 10f, groundLayer))
            {
                if (Vector3.Distance(lastSentDetached, hit.point) > RPC_SEND_THRESHOLD)
                { lastSentDetached = hit.point; UpdateDetachedTargetRpc(hit.point); }
            }
        }
    }

    [Rpc(SendTo.Server)]
    private void ApplyJumpRpc()
    {
        isJumping = true;
        footRb.AddForce(Vector3.up * footJumpForce, ForceMode.VelocityChange);
        torso.torsoRb.AddForce(Vector3.up * torsoJumpForce, ForceMode.Acceleration);
    }

    [Rpc(SendTo.Server)] private void SetSteppingStateRpc(bool v) { isStepping = v; }
    [Rpc(SendTo.Server)] private void UpdateFootTargetRpc(Vector3 v) { targetFootPosition = v; }
    [Rpc(SendTo.Server)] private void UpdateBalanceShiftRpc(Vector3 v) { balanceShiftMousePos = v; }
    [Rpc(SendTo.Server)] private void UpdateDetachedTargetRpc(Vector3 v) { detachedTargetPos = v; }
    [Rpc(SendTo.Server)] private void SetRecoveryInputRpc(bool v) { isPushingRecovery = v; }

    public bool IsGrounded() => footRb != null && Physics.Raycast(footRb.position + Vector3.up * 0.2f, Vector3.down, groundCheckDistance, groundLayer);

    private void OnDrawGizmosSelected()
    {
        if (pivotPoint != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(PivotPosition, maxLegLength);
            
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(PivotPosition, minLegLength);
        }

        if (footRb != null)
        {
            Gizmos.color = Color.green;
            Vector3 startPos = footRb.position + Vector3.up * 0.2f;
            Gizmos.DrawLine(startPos, startPos + Vector3.down * groundCheckDistance);
        }
    }
}
