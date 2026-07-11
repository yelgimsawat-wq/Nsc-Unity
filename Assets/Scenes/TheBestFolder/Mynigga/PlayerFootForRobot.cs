using UnityEngine;
using Unity.Netcode;

public class PlayerFootForRobot : NetworkBehaviour
{
    public enum FootState { Attached, Detached }

    [Header("Network State")]
    public NetworkVariable<FootState> currentState = new NetworkVariable<FootState>(FootState.Attached);

    public bool isStepping         = false;
    public bool isPushingRecovery  = false;
    public bool isJumping          = false;

    public bool IsBalanced => !_releasedForClimb && !isStepping && !isJumping && !isPushingRecovery && IsGrounded();

    [Header("References")]
    public TorsoMovement torso;
    public Rigidbody footRb;
    public Transform pivotPoint;
    public Camera playerCamera;

    [Header("Movement & IK Settings")]
    public float maxLegLength  = 1.5f;
    public float minLegLength  = 0.4f;
    public float footMoveSpeed = 15f;
    public float balanceShiftMultiplier = 0.3f;
    public float detachedMoveSpeed = 20f;
    public float heightAdjustSpeed = 3f;
    public float legDamper = 30f;
    public LayerMask groundLayer;

    [Header("Jump Settings")]
    public float footJumpForce  = 15f;
    public float torsoJumpForce = 400f;

    [Header("Standing Stability")]
    public float standingUpwardPull = 25f;

    [Header("Foot Freeze / Recovery Fix (เท้าหุ่นยนต์หนา)")]
    [Tooltip("ระยะยกจุดตรึงเท้าขึ้นชดเชยครึ่งความหนาของโมเดลเท้าใหม่ กันศูนย์กลาง Rigidbody จมพื้น")]
    public float footThicknessOffset = 0.2f;

    [Header("Recovery Mechanics (ลุกตั้งไข่ง่ายขึ้น)")]
    [Tooltip("แรงเสริมทิศทางตั้งตรงส่งตรงไปที่ลำตัวแกน Y ป้องกันการนอนบิดเบี้ยวแข็งทื่อ")]
    public float upwardRecoveryBoost = 500f;
    // ลบพวกตัวแปร Threshold และ Multiplier ที่เกี่ยวกับระยะห่างทิ้งไปหมดแล้ว

    [Header("Mouse Range")]
    public float mouseReachX = 2f;
    public float mouseReachY = 2f;
    private float _currentYOffset = 0f;

    [Header("Auto Height Settings")]
    public float autoHeightDelay = 0.5f;
    private float _holdTimer = 0f;
    private bool  _autoHeightEnabled = false;

    [Header("Physics Safety")]
    public float maxFootVelocity = 25f;
    public float groundCheckDistance = 0.6f;
    [Tooltip("ทำให้ Joint ทุกชนิดทั้งโซ่ขาไม่มีวันแตกจากแรงฟิสิกส์")]
    public bool makeLegJointsUnbreakable = true;

    private Vector3 _targetFootPos;
    private Vector3 _balanceShiftPos;
    private Vector3 _detachedTargetPos;

    private Vector3 _lastSentTarget, _lastSentBalance, _lastSentDetached;
    private const float RPC_SEND_THRESHOLD = 0.05f;
    private const float RPC_SEND_THRESHOLD_SQR = RPC_SEND_THRESHOLD * RPC_SEND_THRESHOLD; // ⚡ เทียบระยะแบบไม่ sqrt
    private const float SERVER_REACH_MARGIN = 1.5f;

    private bool _localJumpLock = false;
    private float _jumpCooldownTimer = 0f;
    private const float JUMP_HOLD_DURATION = 0.3f;

    // 🧊 แช่แข็งเท้า
    private bool _isPlantedSet = false;
    private bool _releasedForClimb = false;
    public Vector3 plantedPosition;

    public override void OnNetworkSpawn()
    {
        if (IsServer && torso != null) torso.RegisterFoot(this);

        // ✅ [Rest Pose] กันเป้าเท้าเริ่มที่ (0,0,0) — บั๊กตระกูลเดียวกับที่แขนเคยเป็น
        // ถ้าหุ่นล้มก่อน RPC แรกมาถึง เท้าจะพุ่งไปหาจุดกำเนิดโลก
        if (IsServer && footRb != null)
        {
            _targetFootPos     = footRb.position;
            _detachedTargetPos = footRb.position;
            _balanceShiftPos   = pivotPoint != null ? pivotPoint.position : footRb.position;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && torso != null) torso.UnregisterFoot(this);
    }

    void Update()
    {
        if (!IsOwner || playerCamera == null) return;
        if (footRb == null || pivotPoint == null) return; // 🛡️ กัน NRE ฝั่ง owner ถ้า ref ยังไม่พร้อม
        HandleInput();
    }

    void FixedUpdate()
    {
        if (!IsServer) return;

        if (makeLegJointsUnbreakable && footRb != null)
            MakeLegJointChainUnbreakable();
        if (footRb == null || pivotPoint == null) return; // 🛡️ กัน NRE ถ้า ref หลุด/ถูก despawn

        // ⚡ เทียบด้วย sqrMagnitude เลี่ยง sqrt ทุกเฟรม (ผลเท่าเดิม); คำนวณ magnitude จริงเฉพาะตอนเกินลิมิต
        if (footRb.linearVelocity.sqrMagnitude > maxFootVelocity * maxFootVelocity)
        {
            float excess = footRb.linearVelocity.magnitude - maxFootVelocity;
            footRb.AddForce(-footRb.linearVelocity.normalized * excess * 10f, ForceMode.Acceleration);
        }

        if (_jumpCooldownTimer > 0f)
            _jumpCooldownTimer -= Time.fixedDeltaTime;
        else if (isJumping && footRb.linearVelocity.y <= 0.1f && IsGrounded())
            isJumping = false;

        if (currentState.Value == FootState.Attached)
            HandleAttachedState();
        else
            PerformDetachedPhysics();
    }

    private void HandleAttachedState()
    {
        bool isRagdoll = torso != null &&
            (torso.currentState.Value == TorsoMovement.TorsoState.Ragdoll ||
             torso.currentState.Value == TorsoMovement.TorsoState.Falling);

        if (isRagdoll)
        {
            _releasedForClimb = false;
            if (isPushingRecovery)
            {
                footRb.isKinematic = false; // 🔓 ลุกยืน: ปล่อยให้ฟิสิกส์/ข้อต่อทำงาน กัน Solver รวน
                // 🧊 แช่แข็งเท้าติดกับพื้นทันทีที่กด Q ยันตัวลุก!
                ApplyFootFreeze(true);

                // 🦵 ส่งแรงดึงสะโพกเข้าหาศูนย์กลางเต็ม 100% (1f) เสมอ ไม่ต้องสนใจว่าขากางไกลแค่ไหนแล้ว
                torso.ApplyContinuousRecoveryForce(pivotPoint.position, 1f);

                // 🚀 แรงงัดขึ้น — ✅ [4-Player Fix] หยุดอัดเมื่อตัวพุ่งขึ้นเร็วพอแล้ว
                // เดิม 2 เท้าอัดพร้อมกันไม่มีเพดาน → หุ่นพุ่งขึ้นฟ้าตอนหลายคนช่วยกันกด Q
                if (torso.torsoRb != null && torso.torsoRb.linearVelocity.y < torso.maxRecoveryUpVelocity)
                    torso.torsoRb.AddForce(Vector3.up * upwardRecoveryBoost, ForceMode.Acceleration);
            }
            else
            {
                _isPlantedSet = false;
                PerformRagdollFootPhysics();
            }
        }
        else
        {
            bool supportingClimb = torso != null && torso.HasSupportingHandGrab;

            // Keep a planted foot fixed while the leg can physically reach it. Once
            // the torso climbs beyond that reach, release the plant without detaching
            // the foot/leg joint chain, so the solver is not forced to tear the model apart.
            if (supportingClimb && _isPlantedSet)
            {
                Vector3 plantedWorldPosition = plantedPosition + Vector3.up * footThicknessOffset;
                if (Vector3.Distance(pivotPoint.position, plantedWorldPosition) > maxLegLength * 0.95f)
                {
                    _releasedForClimb = true;
                    _isPlantedSet = false;
                }
            }
            else if (!supportingClimb && _releasedForClimb && IsGrounded())
            {
                _releasedForClimb = false;
            }

            if (_releasedForClimb || isStepping || isJumping)
            {
                _isPlantedSet = false;
                PerformSteppingPhysics();
            }
            else
            {
                PerformStandingPhysics();
            }
        }
    }

    private void ApplyFootFreeze(bool isRecovering = false)
    {
        if (!_isPlantedSet)
        {
            if (Physics.Raycast(footRb.position + (Vector3.up * 1.5f), Vector3.down, out RaycastHit hit, 20f, groundLayer))
                plantedPosition = hit.point;
            else if (Physics.Raycast(pivotPoint.position, Vector3.down, out RaycastHit pivotHit, 20f, groundLayer))
                plantedPosition = new Vector3(footRb.position.x, pivotHit.point.y, footRb.position.z);
            else
                plantedPosition = footRb.position;

            _isPlantedSet = true;
        }

        // 🛑 เบรกเท้าให้นิ่ง — เซ็ต velocity ได้เฉพาะตอน dynamic (kinematic body ไม่มี velocity จาก solver)
        // คำสั่งยังอยู่ครบตามเดิม แค่ทำงานตอนที่มีผลจริง (เช่นตอน recovery ที่ isKinematic=false)
        if (!footRb.isKinematic)
        {
            footRb.linearVelocity = Vector3.zero;
            if (!isRecovering) footRb.angularVelocity = Vector3.zero;
        }

        footRb.MovePosition(plantedPosition + Vector3.up * footThicknessOffset);

        if (!isRecovering)
        {
            footRb.rotation = Quaternion.Euler(0, footRb.rotation.eulerAngles.y, 0);
        }
    }

    [Header("Ragdoll Limp")]
    [Tooltip("แรงขยับเท้าตอนล้ม (Ragdoll) — เบากว่าตอนยืนมาก เพื่อให้ยับๆ ขยับได้แต่ไม่ดันลำตัวจนไหล")]
    public float ragdollFootMoveSpeed = 3f;

    private void PerformRagdollFootPhysics()
    {
        footRb.isKinematic = false; // 🔓 ล้ม: ปล่อยให้ฟิสิกส์ทำงานปกติ
        Vector3 rawTarget = _targetFootPos;
        Vector3 dir = rawTarget - pivotPoint.position;
        if (dir.magnitude > maxLegLength) rawTarget = pivotPoint.position + dir.normalized * maxLegLength;

        // ✅ [Ragdoll Fix] ใช้สปริงเบาลง — เท้าขยับตามเมาส์ได้นิดหน่อยเพื่อลุกง่ายขึ้น
        Vector3 vel = (rawTarget - footRb.position) * ragdollFootMoveSpeed;
        Vector3 force = (vel - footRb.linearVelocity) * legDamper;

        // ✅ [No Torso Push] ตัดแรงส่วนที่ "ชี้เข้าหาลำตัว" ทิ้งเป็น 0
        // เท้าดึงออก/ขยับด้านนอกได้ แต่ห้ามผลักตัวไหล (เอาเท้าจ่อตัวแล้วดันหุ่น)
        if (torso != null && torso.torsoRb != null)
        {
            Vector3 toTorso = (torso.torsoRb.position - footRb.position).normalized;
            float into = Vector3.Dot(force, toTorso);
            if (into > 0f) force -= toTorso * into; // ลบเฉพาะองค์ประกอบที่พุ่งเข้าตัว
        }

        footRb.AddForce(force, ForceMode.Acceleration);
    }

    private void PerformSteppingPhysics()
    {
        footRb.isKinematic = false; // 🔓 ก้าวเดิน: ปล่อยให้ฟิสิกส์ทำงานปกติ
        Vector3 rawTarget = _targetFootPos;
        Vector3 dir = rawTarget - pivotPoint.position;
        float dist = dir.magnitude;
        if (dist < minLegLength) rawTarget = pivotPoint.position + (dist > 0.01f ? dir.normalized : pivotPoint.forward) * minLegLength;
        else if (dist > maxLegLength) rawTarget = pivotPoint.position + dir.normalized * maxLegLength;

        Vector3 vel = (rawTarget - footRb.position) * footMoveSpeed;
        footRb.AddForce((vel - footRb.linearVelocity) * legDamper, ForceMode.Acceleration);
    }

    [Header("Standing Foot Lock (การันตีเท้าไม่หลุดพื้น)")]
    [Tooltip("ระยะยิง Raycast ลงหาพื้นตอนยืน (เผื่อหุ่นสเกลใหญ่/สะโพกสูง)")]
    public float standingGroundRayLength = 50f;

    private void PerformStandingPhysics()
    {
        if (torso == null || torso.torsoRb == null || torso.currentState.Value != TorsoMovement.TorsoState.Standing) return;

        // 🔒 [Hard Ground Lock] ตอนยืน (ไม่ step/ไม่ jump): เท้าต้องติดพื้นจริงเสมอ
        // Kinematic = ไม่รับแรงใดๆ + re-raycast ทุกเฟรม → ตัวถูกดันไถลไปไหน เท้าก็ยัง
        // เกาะพื้นจุดใต้ตัวเองตลอด ไม่มีวันลอย/ค้างจุดเก่า (หลุดล็อกเฉพาะ isStepping/isJumping)
        footRb.isKinematic = true;

        if (!_isPlantedSet)
        {
            Vector3 groundPos;
            if (Physics.Raycast(footRb.position + Vector3.up * 2f, Vector3.down,
                    out RaycastHit hit, standingGroundRayLength, groundLayer))
                groundPos = hit.point;
            else if (Physics.Raycast(pivotPoint.position, Vector3.down,
                    out RaycastHit pivotHit, standingGroundRayLength, groundLayer))
                groundPos = new Vector3(footRb.position.x, pivotHit.point.y, footRb.position.z);
            else
                groundPos = footRb.position;

            plantedPosition = groundPos;
            _isPlantedSet = true;
        }

        footRb.MovePosition(plantedPosition + Vector3.up * footThicknessOffset);
        footRb.rotation = Quaternion.Euler(0f, footRb.rotation.eulerAngles.y, 0f);

        Vector3 offset = (_balanceShiftPos - pivotPoint.position) * balanceShiftMultiplier;
        Vector3 pullDir = (footRb.position + Vector3.up * maxLegLength + offset) - pivotPoint.position;
        torso.torsoRb.AddForceAtPosition(pullDir * standingUpwardPull, pivotPoint.position, ForceMode.Acceleration);
    }

    private void MakeLegJointChainUnbreakable()
    {
        Rigidbody currentBody = footRb;
        int safety = 0;

        while (currentBody != null &&
               (torso == null || currentBody != torso.torsoRb) &&
               safety++ < 8)
        {
            Joint[] joints = currentBody.GetComponents<Joint>();
            if (joints.Length == 0) break;

            Rigidbody nextBody = null;
            foreach (Joint joint in joints)
            {
                if (joint == null) continue;
                joint.breakForce = Mathf.Infinity;
                joint.breakTorque = Mathf.Infinity;

                if (nextBody == null && joint.connectedBody != null)
                    nextBody = joint.connectedBody;
            }

            currentBody = nextBody;
        }
    }

    private void PerformDetachedPhysics()
    {
        Vector3 velTarget = (_detachedTargetPos - footRb.position) * detachedMoveSpeed;
        footRb.AddForce((velTarget - footRb.linearVelocity) * legDamper, ForceMode.Acceleration);
    }

    private void HandleInput()
    {
        if (currentState.Value == FootState.Attached)
        {
            bool isRagdoll = torso != null && (torso.currentState.Value == TorsoMovement.TorsoState.Ragdoll || torso.currentState.Value == TorsoMovement.TorsoState.Falling);
            Vector2 mouseNorm = GetNormalizedMousePosition();
            Vector3 camFwd   = playerCamera.transform.forward; camFwd.y = 0; camFwd.Normalize();
            Vector3 camRight = playerCamera.transform.right;  camRight.y = 0; camRight.Normalize();
            Vector3 mouseOffset = camRight * (mouseNorm.x * mouseReachX) + camFwd * (mouseNorm.y * mouseReachY);

            Vector3 newBalance = pivotPoint.position + mouseOffset;
            if ((newBalance - _lastSentBalance).sqrMagnitude > RPC_SEND_THRESHOLD_SQR)
            {
                _lastSentBalance = newBalance;
                UpdateBalanceShiftRpc(newBalance);
            }

            if (!isRagdoll)
            {
                if (!_localJumpLock && Input.GetKeyDown(KeyCode.Space) && IsGrounded() && !isJumping)
                {
                    _localJumpLock = true;
                    ApplyJumpRpc();
                }
                if (_localJumpLock && IsGrounded() && !isJumping) _localJumpLock = false;

                bool holdingClick = Input.GetMouseButton(0);
                if (holdingClick && !isStepping) { isStepping = true; SetSteppingStateRpc(true); _currentYOffset = 0f; }
                else if (!holdingClick && isStepping) { isStepping = false; SetSteppingStateRpc(false); _currentYOffset = 0f; }

                if (isStepping)
                {
                    // 🎮 คุมความสูงเท้าด้วยมือตรงๆ: W ยกขึ้น / S กดลง
                    if (Input.GetKey(KeyCode.W)) _currentYOffset += heightAdjustSpeed * Time.deltaTime;
                    if (Input.GetKey(KeyCode.S)) _currentYOffset -= heightAdjustSpeed * Time.deltaTime;
                    // ⬆️ ยกเท้าพ้นพื้นได้ (ค่าบวก) สูงสุดเท่ากับ maxLegLength
                    _currentYOffset = Mathf.Clamp(_currentYOffset, 0f, maxLegLength);

                    Vector3 newTarget;
                    // 🦿 ยิง Raycast หาพื้น (50m เผื่อหุ่นสเกลยักษ์สะโพกสูง) แล้วยกเป้าหมายเท้าตามค่าที่กด W/S + ชดเชยความหนาเท้ากันจมดิน
                    if (Physics.Raycast(pivotPoint.position + mouseOffset + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 50f, groundLayer))
                        newTarget = hit.point + Vector3.up * (_currentYOffset + footThicknessOffset);
                    else
                        newTarget = pivotPoint.position + mouseOffset + Vector3.down * maxLegLength;

                    if ((newTarget - _lastSentTarget).sqrMagnitude > RPC_SEND_THRESHOLD_SQR) { _lastSentTarget = newTarget; UpdateFootTargetRpc(newTarget); }
                }

                if (isPushingRecovery) { isPushingRecovery = false; SetRecoveryInputRpc(false); }
            }
            else
            {
                if (isStepping) 
                { 
                    isStepping = false; 
                    SetSteppingStateRpc(false); 
                    _currentYOffset = 0f; 
                    _holdTimer = 0f; 
                    _autoHeightEnabled = false; 
                }

                Vector3 newTarget;
                if (Physics.Raycast(pivotPoint.position + mouseOffset + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 10f, groundLayer)) newTarget = hit.point;
                else newTarget = pivotPoint.position + mouseOffset;

                if ((newTarget - _lastSentTarget).sqrMagnitude > RPC_SEND_THRESHOLD_SQR) { _lastSentTarget = newTarget; UpdateFootTargetRpc(newTarget); }

                bool pressingQ = Input.GetKey(KeyCode.Q);
                
                // ถอดเงื่อนไขระยะห่างทิ้งไปเลย! ขอแค่กด Q และเท้าเหยียบพื้นอยู่ ก็ลุกได้ทันที
                bool validPush = pressingQ && IsGrounded();
                                 
                if (validPush != isPushingRecovery) { isPushingRecovery = validPush; SetRecoveryInputRpc(validPush); }
            }
        }
        else
        {
            Vector2 mouseNorm = GetNormalizedMousePosition();
            Vector3 camFwd = playerCamera.transform.forward; camFwd.y = 0; camFwd.Normalize();
            Vector3 camRight = playerCamera.transform.right; camRight.y = 0; camRight.Normalize();
            Vector3 offset = (camRight * mouseNorm.x + camFwd * mouseNorm.y) * 5f;
            if (Physics.Raycast(footRb.position + offset + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 10f, groundLayer))
            {
                if ((hit.point - _lastSentDetached).sqrMagnitude > RPC_SEND_THRESHOLD_SQR) { _lastSentDetached = hit.point; UpdateDetachedTargetRpc(hit.point); }
            }
        }
    }

    // ✅ [Pointer Lock Fix] อ่านจุดเล็งผ่านระบบกลางของ PlayerHandMovement
    // เดิมอ่าน Input.mousePosition ตรงๆ ซึ่งค้างกลางจอตลอดหลังเปิด Virtual Cursor
    // → ทิศเดิน/ถ่ายน้ำหนักติดอยู่ที่ (0,0) หุ่นเดินบอกทิศไม่ได้
    private Vector2 GetNormalizedMousePosition() => PlayerHandMovement.AimNormalized;

    [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)] private void UpdateFootTargetRpc(Vector3 v) { ValidateAndSetFootTarget(v); }
    [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)] private void UpdateBalanceShiftRpc(Vector3 v) { if (v.IsValid()) _balanceShiftPos = v; }
    [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)] private void UpdateDetachedTargetRpc(Vector3 v) { if (v.IsValid()) _detachedTargetPos = v; }
    [Rpc(SendTo.Server, Delivery = RpcDelivery.Reliable)] private void SetSteppingStateRpc(bool v) { isStepping = v; }
    [Rpc(SendTo.Server, Delivery = RpcDelivery.Reliable)] private void SetRecoveryInputRpc(bool v) { isPushingRecovery = v; }

    [Rpc(SendTo.Server, Delivery = RpcDelivery.Reliable)]
    private void ApplyJumpRpc()
    {
        isJumping = true;
        _jumpCooldownTimer = JUMP_HOLD_DURATION;
        footRb.isKinematic = false; // 🔓 กระโดด: ปลดล็อกก่อน AddForce (ไม่งั้นแรงไม่มีผลบน kinematic body)
        footRb.AddForce(Vector3.up * footJumpForce, ForceMode.VelocityChange);
        if (torso != null && torso.torsoRb != null) torso.torsoRb.AddForce(Vector3.up * torsoJumpForce, ForceMode.Acceleration);
    }

    private void ValidateAndSetFootTarget(Vector3 target)
    {
        if (!target.IsValid()) return;
        if (pivotPoint != null)
        {
            Vector3 dir = target - pivotPoint.position;
            float limit = maxLegLength * SERVER_REACH_MARGIN;
            if (dir.magnitude > limit) target = pivotPoint.position + dir.normalized * limit;
        }
        _targetFootPos = target;
    }

    public bool IsGrounded()
    {
        if (footRb == null || pivotPoint == null) return false;
        Vector3 point1 = pivotPoint.position;
        Vector3 point2 = footRb.position;
        return Physics.CheckCapsule(point1, point2, groundCheckDistance, groundLayer);
    }
}
