using UnityEngine;
using Unity.Netcode;

public class PlayerFootForRobot : NetworkBehaviour
{
    public enum FootState { Attached, Detached }
    
    [Header("Network State")]
    public NetworkVariable<FootState> currentState = new NetworkVariable<FootState>(FootState.Attached);
    private bool isStepping = false;
    private bool isPushingRecovery = false;

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
    [Tooltip("ระยะห่างแนวนอน (X,Z) จากเท้าถึงสะโพกที่ยอมให้กด Q ดันตัวลุกได้")]
    public float recoveryProximityThreshold = 1.2f;
    [Tooltip("ตัวคูณความแรงเมื่อเท้าอยู่ไกลที่สุด (1.0 = แรงเต็ม, 0.2 = แรงลดลงเหลือ 20% ทำให้ลุกช้ามาก)")]
    public float minRecoveryMultiplier = 0.2f;
    public float maxRecoveryMultiplier = 1f;
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

        if (currentState.Value == FootState.Attached)
        {
            bool isRagdoll = (torso.currentState.Value == TorsoMovement.TorsoState.Ragdoll || torso.currentState.Value == TorsoMovement.TorsoState.Falling);

            if (isRagdoll)
            {
                PerformRagdollFootPhysics();

                if (isPushingRecovery)
                {
                    // 🌟 1. คำนวณระยะห่างบน Server สดๆ
                    Vector2 footPosXZ = new Vector2(footRb.position.x, footRb.position.z);
                    Vector2 pivotPosXZ = new Vector2(pivotPoint.position.x, pivotPoint.position.z);
                    float distToPivotXZ = Vector2.Distance(footPosXZ, pivotPosXZ);

                    // 🌟 2. แปลงระยะห่างเป็นเปอร์เซ็นต์ (0 คืออยู่ตรงกลางพอดี, 1 คืออยู่ขอบระยะสุด)
                    float distanceRatio = Mathf.Clamp01(distToPivotXZ / recoveryProximityThreshold);

                    // 🌟 3. เกลี่ยความแรง (Lerp) จากแรงเต็ม 100% ไปหาแรงต่ำสุดที่ตั้งไว้
                    // ตัวอย่าง: ถ้าอยู่ใกล้สุดจะได้ 1.0 (แรงเต็ม), ถ้าอยู่ขอบสุดจะได้ 0.2 (แรงหายไป 80%)
                    float pushStrength = Mathf.Lerp(maxRecoveryMultiplier, minRecoveryMultiplier, distanceRatio);

                    // 🌟 4. ส่งความแรงที่ลดทอนแล้วไปให้ลำตัว
                    torso.ApplyContinuousRecoveryForce(pivotPoint.position, pushStrength);
                }
            }
            else
            {
                if (isStepping) PerformSteppingPhysics();
                else PerformStandingPhysics();
            }
        }
        else
        {
            PerformDetachedPhysics();
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
        if (currentState.Value == FootState.Attached)
        {
            bool isRagdoll = (torso.currentState.Value == TorsoMovement.TorsoState.Ragdoll || torso.currentState.Value == TorsoMovement.TorsoState.Falling);
            
            Vector2 mouseNorm = GetNormalizedMousePosition();
            Vector3 camForward = playerCamera.transform.forward; camForward.y = 0; camForward.Normalize();
            Vector3 camRight = playerCamera.transform.right; camRight.y = 0; camRight.Normalize();
            Vector3 mouseWorldOffset = camRight * (mouseNorm.x * mouseReachX) + camForward * (mouseNorm.y * mouseReachY);

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
                    {
                        newTarget = hit.point + Vector3.up * currentYOffset;
                    }
                    else
                    {
                        newTarget = pivotPoint.position + mouseWorldOffset + Vector3.down * maxLegLength;
                    }

                    if (Vector3.Distance(lastSentTarget, newTarget) > RPC_SEND_THRESHOLD)
                    {
                        lastSentTarget = newTarget;
                        UpdateFootTargetRpc(newTarget);
                    }
                }
                else
                {
                    Vector3 newBalance = pivotPoint.position + mouseWorldOffset;
                    if (Vector3.Distance(lastSentBalance, newBalance) > RPC_SEND_THRESHOLD)
                    {
                        lastSentBalance = newBalance;
                        UpdateBalanceShiftRpc(newBalance);
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
                Vector3 rayOrigin = pivotPoint.position + mouseWorldOffset + Vector3.up * 5f;
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

                // ==========================================
                // 🛠️ [FIXED] คัดกรองระยะห่างแค่แกน X และ Z
                // ==========================================
                bool pressingQ = Input.GetKey(KeyCode.Q);
                
                // สร้าง Vector2 ที่เก็บแค่พิกัด X และ Z
                Vector2 footPosXZ = new Vector2(footRb.position.x, footRb.position.z);
                Vector2 pivotPosXZ = new Vector2(pivotPoint.position.x, pivotPoint.position.z);
                
                // วัดระยะห่างบนระนาบ 2D
                float distToPivotXZ = Vector2.Distance(footPosXZ, pivotPosXZ);
                
                bool validRecoveryPush = pressingQ && (distToPivotXZ <= recoveryProximityThreshold) && IsGrounded();

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

    // --- Physics Logic ---
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
        Vector3 offset = (balanceShiftMousePos - pivotPoint.position) * balanceShiftMultiplier;
        Vector3 pullDir = (footRb.position + Vector3.up * maxLegLength + offset) - pivotPoint.position;
        
        torso.torsoRb.AddForceAtPosition(pullDir * 100f, pivotPoint.position, ForceMode.Acceleration);
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

    public bool IsGrounded() => Physics.Raycast(footRb.position + Vector3.up * 0.2f, Vector3.down, 1f, groundLayer);
}