using UnityEngine;
using Unity.Netcode;

public class PlayerFootForRobot : NetworkBehaviour
{
    public enum FootState { Attached, Detached }
    [Header("Network State")]
    public NetworkVariable<FootState> currentState = new NetworkVariable<FootState>(FootState.Attached);
    public bool isStepping = false;
    public bool isPushingRecovery = false;

    [Header("References")]
    public TorsoMovement torso;
    public Rigidbody footRb;
    public Transform pivotPoint;
    public Camera playerCamera;
    
    [Header("Movement & IK Settings")]
    public float maxLegLength = 1.5f;
    [Tooltip("ระยะขาหดสั้นสุด ป้องกันขามุดเข้าลำตัว")]
    public float minLegLength = 0.4f;
    public float footMoveSpeed = 15f;
    public float balanceShiftMultiplier = 0.3f;
    public float detachedMoveSpeed = 20f;
    public float heightAdjustSpeed = 3f;
    public float legDamper = 30f;
    public LayerMask groundLayer;

    [Header("Smoothing (Anti-Jitter)")]
    [Tooltip("ความเร็วในการเกลี่ยตำแหน่งเป้าหมาย (8-20 แนะนำ)")]
    public float targetSmoothSpeed = 12f;

    [Header("Standing Stability")]
    [Tooltip("แรงดึงลำตัวขึ้นจากเท้าเมื่อยืนนิ่ง")]
    public float standingUpwardPull = 25f;

    [Header("Recovery Mechanics")]
    public float recoveryProximityThreshold = 5f;
    public float minRecoveryMultiplier = 0.2f;

    [Header("Mouse Range (World Space)")]
    public float mouseReachX = 2f;
    public float mouseReachY = 2f;
    private float currentYOffset = 0f;

    [Header("Auto Height Settings")]
    [Tooltip("เวลาที่ต้องกดค้าง M1 ก่อนที่จะเริ่มปรับความสูงอัตโนมัติ")]
    public float autoHeightDelay = 0.5f;
    private float holdTimer = 0f;
    private bool autoHeightEnabled = false;

    [Header("Ragdoll Ground Check")]
    [Tooltip("แรงที่ดึงเท้าลงเมื่อไม่มีพื้น")]
    public float groundSeekForce = 50f;

    // ตำแหน่งดิบจาก RPC
    private Vector3 targetFootPosition;
    private Vector3 balanceShiftMousePos;
    private Vector3 detachedTargetPos;
    
    // ตำแหน่งที่ผ่าน Exponential Lerp แล้ว ใช้คำนวณแรงจริง
    private Vector3 smoothedFootTarget;
    private Vector3 smoothedBalanceTarget;
    private Vector3 smoothedDetachedTarget;
    private bool smoothedTargetInitialized, smoothedBalanceInitialized, smoothedDetachedInitialized;

    // ป้องกัน RPC Spam
    private Vector3 lastSentTarget, lastSentBalance, lastSentDetached;
    private const float RPC_SEND_THRESHOLD = 0.05f;

    // Kinematic Lock state
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

        if (currentState.Value == FootState.Attached)
        {
            bool isRagdoll = torso.currentState.Value == TorsoMovement.TorsoState.Ragdoll|| torso.currentState.Value == TorsoMovement.TorsoState.Falling;

            if (isRagdoll)
            {
                ReleaseKinematicLock();
                if (isPushingRecovery)
                {
                    ApplyKinematicLock();
                    Vector2 footXZ  = new Vector2(footRb.position.x, footRb.position.z);
                    Vector2 pivotXZ = new Vector2(pivotPoint.position.x, pivotPoint.position.z);
                    float ratio    = Mathf.Clamp01(Vector2.Distance(footXZ, pivotXZ) / recoveryProximityThreshold);
                    torso.ApplyContinuousRecoveryForce(pivotPoint.position, Mathf.Lerp(1f, minRecoveryMultiplier, ratio));
                }
                else
                {
                    PerformFootSpringPhysics(smoothedFootTarget);

                    // Ragdoll: ถ้าไม่มีพื้นให้เคลื่อนลงอย่างต่อเนื่อง
                    if (!IsGrounded())
                    {
                        footRb.AddForce(Vector3.down * groundSeekForce, ForceMode.Acceleration);
                    }
                }
            }
            else
            {
                if (isStepping)
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

    /// <summary>
    /// Framerate-Independent Exponential Lerp
    /// t = 1 - exp(-speed * dt)  ทำให้ smooth rate คงที่ไม่ว่า physics timestep จะเป็นเท่าไหร่
    /// </summary>
    private void SmoothTargets()
    {
        float t = 1f - Mathf.Exp(-targetSmoothSpeed * Time.fixedDeltaTime);

        if (!smoothedTargetInitialized)  { smoothedFootTarget     = targetFootPosition; smoothedTargetInitialized  = true; }
        else smoothedFootTarget     = Vector3.Lerp(smoothedFootTarget,     targetFootPosition,  t);

        if (!smoothedBalanceInitialized) { smoothedBalanceTarget  = balanceShiftMousePos; smoothedBalanceInitialized = true; }
        else smoothedBalanceTarget  = Vector3.Lerp(smoothedBalanceTarget,  balanceShiftMousePos, t);

        if (!smoothedDetachedInitialized){ smoothedDetachedTarget = detachedTargetPos; smoothedDetachedInitialized = true; }
        else smoothedDetachedTarget = Vector3.Lerp(smoothedDetachedTarget, detachedTargetPos,   t);
    }

    /// <summary>
    /// ล็อกเท้าด้วย Kinematic (ไม่ใช้ MovePosition บน Dynamic Rigidbody อีกต่อไป)
    /// ป้องกัน Physics Solver ตีกับ Hover Spring ของ Torso
    /// </summary>
    private void ApplyKinematicLock()
    {
        if (!isPlantedSet)
        {
            // ตรวจสอบว่ามีพื้นจริงๆ ก่อนล็อก
            if (Physics.Raycast(footRb.position + Vector3.up * 0.2f, Vector3.down, out RaycastHit hit, 3f, groundLayer))
            {
                plantedPosition = hit.point;
                isPlantedSet = true;
            }
            else
            {
                // ถ้าไม่มีพื้นให้ค่อยๆ เคลื่อนลง
                footRb.AddForce(Vector3.down * groundSeekForce, ForceMode.Acceleration);
                return; // ไม่ล็อกถ้าไม่มีพื้น
            }
        }

        if (!footRb.isKinematic)
        {
            footRb.linearVelocity  = Vector3.zero;
            footRb.angularVelocity = Vector3.zero;
            footRb.isKinematic     = true;
            wasKinematic           = true;
        }
        footRb.MovePosition(plantedPosition);
    }

    /// <summary>
    /// ปลดล็อก Kinematic กลับเป็น Dynamic พร้อม reset velocity
    /// </summary>
    private void ReleaseKinematicLock()
    {
        isPlantedSet = false;
        if (wasKinematic && footRb.isKinematic)
        {
            footRb.isKinematic     = false;
            footRb.linearVelocity  = Vector3.zero;
            footRb.angularVelocity = Vector3.zero;
            wasKinematic           = false;
        }
    }

    /// <summary>
    /// Spring-Damper สำหรับเดิน + Ragdoll foot
    /// F = (v_desired - v_current) * damper
    /// โดย v_desired = (target - pos) * speed
    /// </summary>
    private void PerformFootSpringPhysics(Vector3 rawTarget)
    {
        Vector3 dir  = rawTarget - pivotPoint.position;
        float   dist = dir.magnitude;

        // จำกัดระยะขั้นต่ำ (ป้องกัน Self-Collision)
        if (dist < minLegLength)
            rawTarget = pivotPoint.position + (dist > 0.01f ? dir.normalized : pivotPoint.forward) * minLegLength;
        // จำกัดระยะสูงสุด
        else if (dist > maxLegLength)
            rawTarget = pivotPoint.position + dir.normalized * maxLegLength;

        Vector3 velocityTarget = (rawTarget - footRb.position) * footMoveSpeed;
        footRb.AddForce((velocityTarget - footRb.linearVelocity) * legDamper, ForceMode.Acceleration);
    }

    /// <summary>
    /// ยืนนิ่ง: เท้าถูกล็อก (Kinematic) แล้ว ดึงลำตัวขึ้นตาม Balance Shift
    /// </summary>
    private void PerformStandingPhysics()
    {
        Vector3 offset  = (smoothedBalanceTarget - pivotPoint.position) * balanceShiftMultiplier;
        Vector3 pullDir = (footRb.position + Vector3.up * maxLegLength + offset) - pivotPoint.position;
        torso.torsoRb.AddForceAtPosition(pullDir * standingUpwardPull, pivotPoint.position, ForceMode.Acceleration);
    }

    private void PerformDetachedPhysics()
    {
        // ตั้ง velocity โดยตรง (เท้าหลุดจากตัว ไม่มี Joint)
        footRb.linearVelocity = (smoothedDetachedTarget - footRb.position) * detachedMoveSpeed;
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
        if (currentState.Value == FootState.Attached)
        {
            bool isRagdoll = torso.currentState.Value == TorsoMovement.TorsoState.Ragdoll 
                          || torso.currentState.Value == TorsoMovement.TorsoState.Falling;

            Vector2 mouseNorm = GetNormalizedMousePosition();
            Vector3 camFwd    = playerCamera.transform.forward; camFwd.y = 0; camFwd.Normalize();
            Vector3 camRight  = playerCamera.transform.right;   camRight.y = 0; camRight.Normalize();
            Vector3 mouseOffset = camRight * (mouseNorm.x * mouseReachX) + camFwd * (mouseNorm.y * mouseReachY);

            Vector3 newBalance = pivotPoint.position + mouseOffset;
            if (Vector3.Distance(lastSentBalance, newBalance) > RPC_SEND_THRESHOLD)
            { lastSentBalance = newBalance; UpdateBalanceShiftRpc(newBalance); }

            if (!isRagdoll)
            {
                bool holdingClick = Input.GetMouseButton(0);

                // จัดการ hold timer และ auto height
                if (holdingClick)
                {
                    if (!isStepping)
                    {
                        isStepping = true;
                        SetSteppingStateRpc(true);
                        holdTimer = 0f;
                        autoHeightEnabled = false;
                    }

                    holdTimer += Time.deltaTime;
                    if (holdTimer >= autoHeightDelay)
                    {
                        autoHeightEnabled = true;
                    }
                }
                else
                {
                    if (isStepping)
                    {
                        isStepping = false;
                        SetSteppingStateRpc(false);
                        holdTimer = 0f;
                        autoHeightEnabled = false;
                    }
                }

                if (isStepping)
                {
                    // Manual height adjustment with Q/E
                    if (Input.GetKey(KeyCode.E)) currentYOffset += heightAdjustSpeed * Time.deltaTime;
                    if (Input.GetKey(KeyCode.Q)) currentYOffset -= heightAdjustSpeed * Time.deltaTime;
                    currentYOffset = Mathf.Clamp(currentYOffset, -maxLegLength, 0f);

                    Vector3 newTarget;
                    if (Physics.Raycast(pivotPoint.position + mouseOffset + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 10f, groundLayer))
                    {
                        // ใช้ auto height เฉพาะเมื่อกดค้างเกิน 0.5 วินาที
                        if (autoHeightEnabled)
                        {
                            // คำนวณความสูงที่เหมาะสม
                            float pivotHeight = pivotPoint.position.y;
                            float groundHeight = hit.point.y;
                            float optimalHeight = Mathf.Clamp(pivotHeight - groundHeight, minLegLength, maxLegLength);
                            newTarget = hit.point + Vector3.up * (optimalHeight * 0.5f); // ยกขึ้นครึ่งหนึ่งของความยาวขาที่เหมาะสม
                        }
                        else
                        {
                            newTarget = hit.point + Vector3.up * currentYOffset;
                        }
                    }
                    else
                        newTarget = pivotPoint.position + mouseOffset + Vector3.down * maxLegLength;

                    if (Vector3.Distance(lastSentTarget, newTarget) > RPC_SEND_THRESHOLD)
                    { lastSentTarget = newTarget; UpdateFootTargetRpc(newTarget); }
                }

                if (isPushingRecovery) { isPushingRecovery = false; SetRecoveryInputRpc(false); }
            }
            else
            {
                Vector3 newTarget;
                if (Physics.Raycast(pivotPoint.position + mouseOffset + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 10f, groundLayer))
                    newTarget = hit.point;
                else
                    newTarget = pivotPoint.position + mouseOffset;

                if (Vector3.Distance(lastSentTarget, newTarget) > RPC_SEND_THRESHOLD)
                { lastSentTarget = newTarget; UpdateFootTargetRpc(newTarget); }

                Vector2 footXZ  = new Vector2(footRb.position.x, footRb.position.z);
                Vector2 pivotXZ = new Vector2(pivotPoint.position.x, pivotPoint.position.z);
                bool validPush  = Input.GetKey(KeyCode.Q) 
                               && Vector2.Distance(footXZ, pivotXZ) <= recoveryProximityThreshold 
                               && IsGrounded();

                if (validPush != isPushingRecovery) { isPushingRecovery = validPush; SetRecoveryInputRpc(validPush); }
            }
        }
        else
        {
            Vector2 mouseNorm = GetNormalizedMousePosition();
            Vector3 camFwd   = playerCamera.transform.forward; camFwd.y = 0; camFwd.Normalize();
            Vector3 camRight = playerCamera.transform.right;   camRight.y = 0; camRight.Normalize();

            if (Physics.Raycast(footRb.position + (camRight * mouseNorm.x + camFwd * mouseNorm.y) * 5f + Vector3.up * 5f,
                                Vector3.down, out RaycastHit hit, 10f, groundLayer))
            {
                if (Vector3.Distance(lastSentDetached, hit.point) > RPC_SEND_THRESHOLD)
                { lastSentDetached = hit.point; UpdateDetachedTargetRpc(hit.point); }
            }}
    }

    [Rpc(SendTo.Server)] private void SetSteppingStateRpc(bool v)    { isStepping = v; }
    [Rpc(SendTo.Server)] private void UpdateFootTargetRpc(Vector3 v) { targetFootPosition = v; }
    [Rpc(SendTo.Server)] private void UpdateBalanceShiftRpc(Vector3 v){ balanceShiftMousePos = v; }
    [Rpc(SendTo.Server)] private void UpdateDetachedTargetRpc(Vector3 v){ detachedTargetPos = v; }
    [Rpc(SendTo.Server)] private void SetRecoveryInputRpc(bool v)     { isPushingRecovery = v; }

    public bool IsGrounded() => Physics.Raycast(footRb.position + Vector3.up * 0.2f, Vector3.down, 3f, groundLayer);
}
