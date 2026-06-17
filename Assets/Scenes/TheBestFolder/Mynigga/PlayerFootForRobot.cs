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
    public float footMoveSpeed = 15f;
    public float balanceShiftMultiplier = 0.3f;
    public float detachedMoveSpeed = 20f;
    public float heightAdjustSpeed = 3f;
    public float legDamper = 30f;
    public LayerMask groundLayer;

    [Header("Recovery Mechanics")]
    public float recoveryProximityThreshold = 5f;
    public float minRecoveryMultiplier = 0.2f;

    [Header("Mouse Range (World Space)")]
    public float mouseReachX = 2f;
    public float mouseReachY = 2f;
    
    private float currentYOffset = 0f;
    private Vector3 targetFootPosition;
    
    private Vector3 balanceShiftMousePos; 
    private Vector3 detachedTargetPos;    

    private Vector3 lastSentTarget;
    private Vector3 lastSentBalance;
    private Vector3 lastSentDetached;
    private const float RPC_SEND_THRESHOLD = 0.05f;

    private bool isPlantedSet = false;
    public Vector3 plantedPosition;

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
    
    // ==========================================
    // 👑 SERVER PHYSICS UPDATE
    // ==========================================
    void FixedUpdate()
    {
        if (!IsServer) return;

        if (currentState.Value == FootState.Attached)
        {
            bool isRagdoll = (torso.currentState.Value == TorsoMovement.TorsoState.Ragdoll || torso.currentState.Value == TorsoMovement.TorsoState.Falling);

            if (isRagdoll)
            {
                if (isPushingRecovery)
                {
                    // 🧊 1. แช่แข็งเท้าติดกับพื้นทันทีที่กด Q ยันตัวลุก!
                    ApplyFootFreeze();

                    // 2. ส่งแรงดันไปให้ลำตัวลุกขึ้น
                    Vector2 footPosXZ = new Vector2(footRb.position.x, footRb.position.z);
                    Vector2 pivotPosXZ = new Vector2(pivotPoint.position.x, pivotPoint.position.z);
                    float distToPivotXZ = Vector2.Distance(footPosXZ, pivotPosXZ);

                    float distanceRatio = Mathf.Clamp01(distToPivotXZ / recoveryProximityThreshold);
                    float pushStrength = Mathf.Lerp(1.0f, minRecoveryMultiplier, distanceRatio);

                    torso.ApplyContinuousRecoveryForce(pivotPoint.position, pushStrength);
                }
                else
                {
                    // ถ้าล้มอยู่แต่ไม่ได้กด Q ให้ปล่อยเท้าลากไปกับพื้นอิสระ
                    isPlantedSet = false;
                    PerformRagdollFootPhysics();
                }
            }
            else
            {
                // สถานะยืนปกติ
                if (isStepping)
                {
                    isPlantedSet = false;
                    PerformSteppingPhysics(); // ก้าวขา
                }
                else
                {
                    PerformStandingPhysics(); // ยืนนิ่งๆ (แช่แข็งเท้า)
                }
            }
        }
        else
        {
            isPlantedSet = false;
            PerformDetachedPhysics();
        }
    }
    
    private Vector2 GetNormalizedMousePosition()
    {
        float mouseX = Mathf.Clamp(Input.mousePosition.x, 0, Screen.width);
        float mouseY = Mathf.Clamp(Input.mousePosition.y, 0, Screen.height);
        return new Vector2((mouseX / Screen.width) * 2f - 1f, (mouseY / Screen.height) * 2f - 1f);
    }

    // ==========================================
    // 🖱️ CLIENT INPUT HANDLING
    // ==========================================
    private void HandleInput()
    {
        if (currentState.Value == FootState.Attached)
        {
            bool isRagdoll = (torso.currentState.Value == TorsoMovement.TorsoState.Ragdoll || torso.currentState.Value == TorsoMovement.TorsoState.Falling);
            
            Vector2 mouseNorm = GetNormalizedMousePosition();
            Vector3 camForward = playerCamera.transform.forward; camForward.y = 0; camForward.Normalize();
            Vector3 camRight = playerCamera.transform.right; camRight.y = 0; camRight.Normalize();
            Vector3 mouseWorldOffset = camRight * (mouseNorm.x * mouseReachX) + camForward * (mouseNorm.y * mouseReachY);

            Vector3 newBalance = pivotPoint.position + mouseWorldOffset;
            if (Vector3.Distance(lastSentBalance, newBalance) > RPC_SEND_THRESHOLD)
            {
                lastSentBalance = newBalance;
                UpdateBalanceShiftRpc(newBalance);
            }

            if (!isRagdoll)
            {
                bool holdingClick = Input.GetMouseButton(0);
                if (holdingClick != isStepping)
                {
                    isStepping = holdingClick;
                    SetSteppingStateRpc(isStepping);
                }
                
                if (isStepping)
                {
                    if (Input.GetKey(KeyCode.E)) currentYOffset += heightAdjustSpeed * Time.deltaTime;
                    if (Input.GetKey(KeyCode.Q)) currentYOffset -= heightAdjustSpeed * Time.deltaTime;
                    currentYOffset = Mathf.Clamp(currentYOffset, -maxLegLength, 0f);
                    
                    Vector3 rayOrigin = pivotPoint.position + mouseWorldOffset + Vector3.up * 5f;
                    Vector3 newTarget;
                    if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 10f, groundLayer))
                        newTarget = hit.point + Vector3.up * currentYOffset;
                    else
                        newTarget = pivotPoint.position + mouseWorldOffset + Vector3.down * maxLegLength;

                    if (Vector3.Distance(lastSentTarget, newTarget) > RPC_SEND_THRESHOLD)
                    {
                        lastSentTarget = newTarget;
                        UpdateFootTargetRpc(newTarget);
                    }
                }

                if (isPushingRecovery)
                {
                    isPushingRecovery = false;
                    SetRecoveryInputRpc(false);
                }
            }
            else
            {
                Vector3 rayOrigin = pivotPoint.position + mouseWorldOffset + (Vector3.up * 2f);
                Vector3 newTarget;
                if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 10f, groundLayer))
                    newTarget = hit.point;
                else
                    newTarget = pivotPoint.position + mouseWorldOffset;

                if (Vector3.Distance(lastSentTarget, newTarget) > RPC_SEND_THRESHOLD)
                {
                    lastSentTarget = newTarget;
                    UpdateFootTargetRpc(newTarget);
                }

                bool pressingQ = Input.GetKey(KeyCode.Q);
                Vector2 footPosXZ = new Vector2(footRb.position.x, footRb.position.z);
                Vector2 pivotPosXZ = new Vector2(pivotPoint.position.x, pivotPoint.position.z);
                float distToPivotXZ = Vector2.Distance(footPosXZ, pivotPosXZ);
                
                bool validRecoveryPush = pressingQ && (distToPivotXZ <= recoveryProximityThreshold) && IsGrounded();

                // 🚨 [FIXED] อัปเดตเฉพาะสถานะให้ Server ทราบ ห้ามใส่โค้ดจัดการ Physics ในนี้เด็ดขาด!
                if (validRecoveryPush != isPushingRecovery)
                {
                    isPushingRecovery = validRecoveryPush;
                    SetRecoveryInputRpc(validRecoveryPush);
                }
            }
        }
        else
        {
            Vector2 mouseNorm = GetNormalizedMousePosition();
            Vector3 camForward = playerCamera.transform.forward; camForward.y = 0; camForward.Normalize();
            Vector3 camRight = playerCamera.transform.right; camRight.y = 0; camRight.Normalize();
            
            Vector3 rayOrigin = footRb.position + (camRight * mouseNorm.x + camForward * mouseNorm.y) * 5f + Vector3.up * 5f;
            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 10f, groundLayer))
            {
                if (Vector3.Distance(lastSentDetached, hit.point) > RPC_SEND_THRESHOLD)
                {
                    lastSentDetached = hit.point;
                    UpdateDetachedTargetRpc(hit.point);
                }
            }
        }
    }
    
    // --- Server RPCs ---
    [Rpc(SendTo.Server)] private void SetSteppingStateRpc(bool stepping) { isStepping = stepping; }
    [Rpc(SendTo.Server)] private void UpdateFootTargetRpc(Vector3 target) { targetFootPosition = target; }
    [Rpc(SendTo.Server)] private void UpdateBalanceShiftRpc(Vector3 mousePos) { balanceShiftMousePos = mousePos; }
    [Rpc(SendTo.Server)] private void UpdateDetachedTargetRpc(Vector3 target) { detachedTargetPos = target; }
    [Rpc(SendTo.Server)] private void SetRecoveryInputRpc(bool isPushing) { isPushingRecovery = isPushing; }

    // ==========================================
    // 🧱 PHYSICS LOGIC (SERVER ONLY)
    // ==========================================

    /// <summary>
    /// 🧊 ระบบแช่แข็งเท้า: ค้นหาพื้นและใช้ MovePosition ตรึงเท้าไว้อย่างเด็ดขาด
    /// </summary>
    private void ApplyFootFreeze()
    {
        if (!isPlantedSet)
        {
            if (Physics.Raycast(footRb.position + (Vector3.up * 1.5f), Vector3.down, out RaycastHit hit, 20f, groundLayer))
                plantedPosition = hit.point;
            else if (Physics.Raycast(pivotPoint.position, Vector3.down, out RaycastHit pivotHit, 20f, groundLayer))
                plantedPosition = new Vector3(footRb.position.x, pivotHit.point.y, footRb.position.z);
            else
                plantedPosition = footRb.position;

            isPlantedSet = true;
        }

        // 1. ล้างความเร็วทิ้งทั้งหมด เพื่อไม่ให้เท้าลื่นไถล
        footRb.linearVelocity = Vector3.zero;

        // 2. ใช้ MovePosition บังคับตำแหน่ง! (ทรงพลังกว่า AddForce มาก มันจะสู้กับแรงดึงของลำตัวได้ 100%)
        footRb.MovePosition(plantedPosition);
    }

    private void PerformSteppingPhysics()
    {
        Vector3 dirFromPivot = targetFootPosition - pivotPoint.position;
        if (dirFromPivot.magnitude > maxLegLength)
        {
            targetFootPosition = pivotPoint.position + dirFromPivot.normalized * maxLegLength;
        }
        
        Vector3 velocityTarget = (targetFootPosition - footRb.position) * footMoveSpeed;
        Vector3 force = (velocityTarget - footRb.linearVelocity) * legDamper;
        footRb.AddForce(force, ForceMode.Acceleration);
    }
    
    private void PerformStandingPhysics()
    {
        // 🧊 แช่แข็งเท้าไว้กับพื้น!
        ApplyFootFreeze();

        // คำนวณและส่งแรงไปดึงลำตัวตามปกติ
        Vector3 offset = (balanceShiftMousePos - pivotPoint.position) * balanceShiftMultiplier;
        Vector3 pullDir = (footRb.position + Vector3.up * maxLegLength + offset) - pivotPoint.position;
        torso.torsoRb.AddForceAtPosition(pullDir * 25f, pivotPoint.position, ForceMode.Acceleration);
    }

    private void PerformRagdollFootPhysics()
    {
        Vector3 dirFromPivot = targetFootPosition - pivotPoint.position;
        if (dirFromPivot.magnitude > maxLegLength)
        {
            targetFootPosition = pivotPoint.position + dirFromPivot.normalized * maxLegLength;
        }
        
        Vector3 velocityTarget = (targetFootPosition - footRb.position) * footMoveSpeed;
        Vector3 force = (velocityTarget - footRb.linearVelocity) * legDamper;
        footRb.AddForce(force, ForceMode.Acceleration);
    }

    private void PerformDetachedPhysics()
    {
        Vector3 dir = (detachedTargetPos - footRb.position);
        footRb.linearVelocity = dir * detachedMoveSpeed;
    }

    public bool IsGrounded() => Physics.Raycast(footRb.position + Vector3.up * 0.2f, Vector3.down, 3f, groundLayer);
}