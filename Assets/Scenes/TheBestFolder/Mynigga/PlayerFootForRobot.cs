using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;
using System.Collections.Generic;

public class PlayerFootForRobot : NetworkBehaviour
{
    public enum FootState { Attached, Detached }

    [Header("Network State")]
    public NetworkVariable<FootState> currentState = new NetworkVariable<FootState>(FootState.Attached);

    public bool isStepping         = false;
    public bool isPushingRecovery  = false;
    public bool isJumping          = false;

    // ✅ [Multiplayer Balance Fix] เดิม isStepping ตัดสิทธิ์ "สมดุล" ทันที แม้เท้ายังแตะพื้นอยู่จริง
    // ปัญหา: ขาซ้าย-ขวาคุมคนละผู้เล่น เดินพร้อมกันมีโอกาสสูงที่จังหวะ isStepping จะซ้อนกันแค่เฟรมเดียว
    // (ยิ่งซ้ำเติมด้วยดีเลย์เครือข่าย) → เข้าเงื่อนไข "ทั้งสองเท้าไม่สมดุล" ทั้งที่เท้ายังอยู่บนพื้น
    // → ทั้งตัวล้มทันที (TorsoMovement) → เท้าโดนปลดล็อกเข้า Ragdoll ทันที = อาการ "เท้าหลุดจากพื้นจู่ๆ"
    // แก้: ตราบใดที่เท้ายังแตะพื้นจริง (IsGrounded) ให้นับว่ายังสมดุลอยู่ แม้กำลัง step
    public bool IsBalanced => !_releasedForClimb && !isJumping && !isPushingRecovery && IsGrounded();

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
    [Tooltip("เพดานความเร็วเท้าตอนหลุด (m/s) — กันเท้าปลิวหายจากสปริงไล่เป้าเมาส์")]
    public float maxDetachedSpeed = 8f;
    public float heightAdjustSpeed = 3f;
    public float legDamper = 30f;
    public LayerMask groundLayer;

    [Header("Jump Settings")]
    public float footJumpForce  = 15f;
    [Tooltip("เพดานความเร็วเท้าตอนดีดตัวกระโดด (m/s) — ใช้ค่าต่ำสุดระหว่างค่านี้กับ footJumpForce\n" +
             "เดิมเท้าได้ 15 m/s แบบทันที ขณะลำตัวได้ ~8 m/s → ขาเหยียดสุดกระชากข้อต่อตั้งแต่เฟรมแรก")]
    public float maxFootJumpVelocity = 7f;
    // แรงดันลำตัวย้ายไปอยู่ที่ TorsoMovement (soloJumpForce / coopJumpBonusForce) —
    // torso เป็นคนตัดสินใจเรื่อง Co-op Jump + เพดานความเร็วเอง

    [Header("Standing Stability")]
    public float standingUpwardPull = 25f;

    [Header("Leg Stretch Tether (กันล้มตอนเดินขาเดียว)")]
    [Tooltip("แรงสปริงรั้งลำตัวกลับเมื่อถูกลากออกไปจนเกินระยะขาที่ปักอยู่ — เท้านิ่งสนิท ตัวไม่ไหล")]
    public float legStretchSpring = 80f;
    [Tooltip("แรงหน่วงความเร็วลำตัวเฉพาะทิศที่พุ่งออกจากเท้าที่ปัก")]
    public float legStretchDamper = 10f;

    [Header("Foot Freeze / Recovery Fix (เท้าหุ่นยนต์หนา)")]
    [Tooltip("ระยะยกจุดตรึงเท้าขึ้นชดเชยครึ่งความหนาของโมเดลเท้าใหม่ กันศูนย์กลาง Rigidbody จมพื้น")]
    public float footThicknessOffset = 0.2f;
    [Tooltip("วัดความหนาเท้าจริงจาก Collider ตอน spawn แล้วเขียนทับค่าบน — เปลี่ยนโมเดล/สเกลหุ่น\n" +
             "ไม่ต้องมานั่งจูนตัวเลขใหม่ (ปิดติ๊กถ้าอยากคุมค่าเองใน Inspector)")]
    public bool autoMeasureFootThickness = true;

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
    // ⚡ ตั้ง breakForce = Infinity ครั้งเดียวสำเร็จแล้วพอ — เดิมเดินโซ่ joint ทุก FixedUpdate
    // (GetComponents จองหน่วยความจำใหม่ทุกครั้ง 50 ครั้ง/วิ ต่อขา = GC ฟรีๆ)
    private bool _legJointsUnbreakableApplied = false;

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

    [Header("Network Feel (Host/Client Parity)")]
    [Tooltip("บังคับ snap ตำแหน่งแบบไม่ interpolate ทุกครั้งที่เท้า 'ปักลงพื้น' ใหม่\n" +
             "แก้ปัญหา Host รู้สึกเท้าดูดพื้นแรง แต่ Client รู้สึกอ่อนกว่า — เพราะ NetworkTransform " +
             "จะ interpolate การ snap นั้นให้ฝั่ง Client เห็นแบบค่อยๆ ลื่น แทนที่จะกระแทกทันทีเหมือน Host")]
    public bool teleportOnPlant = true;
    private NetworkTransform _footNetworkTransform;

    [Tooltip("ปิดการชนเฉพาะ 'ขา ↔ ขาอีกข้าง' (เดินไขว้/เกี่ยวกันแล้วแรงกระแทกเขย่าลำตัวจนล้ม)\n" +
             "ขายังชนลำตัว/พื้น/สิ่งอื่นได้ปกติ — ท่าล้มยังสมจริง ไม่พับทะลุตัวเอง")]
    public bool ignoreSelfCollision = true;
    private bool _selfCollisionConfigured = false;

    [Header("Camera-Independent Aim (ไม่มีเมาส์บนจอ — เหมือนมือ)")]
    [Tooltip("ล็อกเคอร์เซอร์กลางจอ แล้วใช้ mouse delta ขยับจุดเล็งเท้าในแกนโลก\n" +
             "ไม่มีเคอร์เซอร์โผล่ + ไม่ติดขอบจอ + หันกล้องแล้วจุดเล็งไม่กวาดตาม (แบบเดียวกับมือ)")]
    public bool useVirtualCursor = true;
    [Tooltip("ความไวจุดเล็งเท้า")]
    public float mouseSensitivity = 1.5f;
    [Tooltip("วาด marker ที่จุดเล็งเท้า")]
    public bool showCrosshair = false;

    // จุดเล็งแนวราบ (offset จาก pivot ในแกนโลก) — เปลี่ยนด้วย delta เมาส์เท่านั้น ไม่ตามกล้อง
    private Vector3 _footPlanarAim = Vector3.zero;
    private bool _footEverLocked = false;
    private bool _ignoreStepUntilRelease = false; // กันคลิกที่ใช้ดึงเมาส์กลับไปเริ่มก้าวเดิน
    private Vector3 _footMarkerWorld;
    private static GUIStyle _footCrosshairStyle;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer && torso != null) torso.RegisterFoot(this);
        _footNetworkTransform = GetComponent<NetworkTransform>();

        // 📏 [Auto Thickness] วัดระยะจากจุดกำเนิด Rigidbody ถึง "จุดต่ำสุดของ collider เท้า"
        // = ความหนาที่ต้องยกจริง — รันทุกเครื่อง (ฝั่ง owner ใช้คำนวณเป้าตอนก้าวด้วย)
        if (autoMeasureFootThickness) MeasureFootThickness();

        // ✅ [Rest Pose] กันเป้าเท้าเริ่มที่ (0,0,0) — บั๊กตระกูลเดียวกับที่แขนเคยเป็น
        // ถ้าหุ่นล้มก่อน RPC แรกมาถึง เท้าจะพุ่งไปหาจุดกำเนิดโลก
        if (IsServer && footRb != null)
        {
            _targetFootPos     = footRb.position;
            _detachedTargetPos = footRb.position;
            _balanceShiftPos   = pivotPoint != null ? pivotPoint.position : footRb.position;
        }
    }

    // 📏 วัดความหนาเท้าจาก collider จริง: จุดกำเนิด Rigidbody สูงจากพื้นรองเท้าเท่าไหร่
    // ใช้เฉพาะ collider ที่สังกัด footRb (กันเผลอนับ collider ของท่อนขาที่เป็นลูกใน hierarchy)
    private void MeasureFootThickness()
    {
        if (footRb == null) return;

        float lowestPoint = float.MaxValue;
        foreach (Collider col in footRb.GetComponentsInChildren<Collider>())
        {
            if (col == null || col.attachedRigidbody != footRb) continue;
            lowestPoint = Mathf.Min(lowestPoint, col.bounds.min.y);
        }

        if (lowestPoint == float.MaxValue) return; // ไม่เจอ collider — คงค่าจาก Inspector ไว้

        float measured = footRb.position.y - lowestPoint;
        // กันค่าหลุดโลก (collider ยังไม่ init/สเกลพัง) — ยอมรับเฉพาะช่วงสมเหตุสมผล
        if (measured > 0.01f && measured < 2f)
        {
            footThicknessOffset = measured;
            Debug.Log($"[Foot] 📏 Auto-measured footThicknessOffset = {measured:F3}m ({name})");
        }
    }

    // เก็บ Collider ทั้งโซ่ขา (เท้า → ท่อนขา จนถึงก่อนถึงลำตัว)
    private List<Collider> GetLegChainColliders()
    {
        var cols = new List<Collider>();
        Rigidbody currentBody = footRb;
        int safety = 0;
        while (currentBody != null &&
               (torso == null || currentBody != torso.torsoRb) &&
               safety++ < 8)
        {
            cols.AddRange(currentBody.GetComponents<Collider>());

            Rigidbody next = null;
            foreach (Joint j in currentBody.GetComponents<Joint>())
                if (j != null && next == null && j.connectedBody != null)
                    next = j.connectedBody;
            currentBody = next;
        }
        return cols;
    }

    // ✅ [Selective Self-Collision] ปิดการชนเฉพาะ "ขา ↔ ขาอีกข้าง"
    // เวอร์ชันก่อนปิดชนกับทั้งตัว → ตอน Ragdoll ลำตัวทะลุขาลงไปกองกับพื้น ล้มดูหนักผิดปกติ
    // เวอร์ชันนี้: สองขาไม่เกี่ยวกันเอง แต่ขายังชนลำตัว/พื้นได้ตามเดิม
    private void ConfigureLegSelfCollision()
    {
        if (_selfCollisionConfigured || !ignoreSelfCollision) return;
        if (torso == null || torso.RegisteredFootCount < 2) return; // รอเท้าอีกข้างลงทะเบียนก่อน

        List<Collider> myLeg = GetLegChainColliders();
        foreach (PlayerFootForRobot otherFoot in torso.AttachedFeet)
        {
            if (otherFoot == null || otherFoot == this || otherFoot.footRb == null) continue;
            List<Collider> otherLeg = otherFoot.GetLegChainColliders();

            foreach (Collider mine in myLeg)
                foreach (Collider theirs in otherLeg)
                    if (mine != null && theirs != null && mine != theirs)
                        Physics.IgnoreCollision(mine, theirs, true);
        }
        _selfCollisionConfigured = true;
    }

    // ✅ [Host/Client Parity] Snap ตำแหน่งแบบไม่ interpolate ตอนปักเท้าลงพื้นใหม่
    private void TeleportFootTo(Vector3 worldPos)
    {
        if (teleportOnPlant && _footNetworkTransform != null)
            _footNetworkTransform.Teleport(worldPos, footRb.rotation, footRb.transform.localScale);
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && torso != null) torso.UnregisterFoot(this);
        // คืนเคอร์เซอร์ตอนหุ่นหายจากเกม (กลับเมนู/ตาย) — ไม่งั้นคลิกเมนูไม่ได้
        ReleaseCursorIfOwner();

        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        // Safety net: ถ้า object ถูกทำลายโดยไม่ผ่าน despawn ปกติ (เช่น unload scene ตรงๆ)
        // เคอร์เซอร์ต้องไม่ล็อกค้างถึงหน้าเมนู
        ReleaseCursorIfOwner();

        base.OnDestroy();
    }

    private void ReleaseCursorIfOwner()
    {
        if (IsOwner && useVirtualCursor)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
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

        ConfigureLegSelfCollision(); // ตั้งครั้งเดียวเมื่อเท้าทั้งคู่พร้อม (มี flag กันทำซ้ำ)

        if (makeLegJointsUnbreakable && !_legJointsUnbreakableApplied && footRb != null)
            _legJointsUnbreakableApplied = MakeLegJointChainUnbreakable();
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
        {
            HandleAttachedState();
        }
        else if (JointPull == null || !JointPull.IsBeingPulled)
        {
            // ตอนกดดึงกลับ (R) ต้องหยุดสปริงไล่เมาส์ของเท้า
            // ไม่งั้นสองแรงสู้กันแล้วเท้าไม่มีวันกลับถึง socket
            PerformDetachedPhysics();
        }
    }

    private JointPullAndReconnect _jointPull;
    private bool _jointPullSearched;
    private JointPullAndReconnect JointPull
    {
        get
        {
            if (!_jointPullSearched)
            {
                _jointPullSearched = true;
                _jointPull = GetComponent<JointPullAndReconnect>();
            }
            return _jointPull;
        }
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
            TeleportFootTo(plantedPosition + Vector3.up * footThicknessOffset);
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

            // จุดปักต้องอยู่ในระยะที่ขาเอื้อมถึงจริงเสมอ — กันปักไกลแล้วขาเหยียดค้างตั้งแต่แรก
            plantedPosition = ClampPlantWithinReach(groundPos);
            _isPlantedSet = true;
            TeleportFootTo(plantedPosition + Vector3.up * footThicknessOffset);
        }

        footRb.MovePosition(plantedPosition + Vector3.up * footThicknessOffset);
        footRb.rotation = Quaternion.Euler(0f, footRb.rotation.eulerAngles.y, 0f);

        Vector3 offset = (_balanceShiftPos - pivotPoint.position) * balanceShiftMultiplier;
        Vector3 pullDir = (footRb.position + Vector3.up * maxLegLength + offset) - pivotPoint.position;
        torso.torsoRb.AddForceAtPosition(pullDir * standingUpwardPull, pivotPoint.position, ForceMode.Acceleration);

        // 🪢 [Anchor Tether] เดินขาเดียวต้อง "ไม่ล้ม และไม่ไหล":
        // เท้าที่ปักอยู่นิ่งสนิท (kinematic ไม่ขยับตาม) แต่แทนที่จะปล่อยให้ลำตัวถูกขาอีกข้าง
        // ลากออกไปจนขาเหยียดตึงแล้วคว่ำ → รั้งลำตัวไว้เหมือนเชือกล่ามกับจุดปัก
        // ผล: ก้าวขาเดียวไปได้ไกลสุดหนึ่งช่วงขาแล้ว "หยุด" — อยากไปต่อต้องสลับขาก้าว
        Vector3 plantedWorld = plantedPosition + Vector3.up * footThicknessOffset;
        Vector3 away = pivotPoint.position - plantedWorld;
        Vector2 flatAway = new Vector2(away.x, away.z);
        float maxHorizontal = MaxHorizontalReach();
        float excess = flatAway.magnitude - maxHorizontal;

        if (excess > 0f && flatAway.sqrMagnitude > 0.0001f)
        {
            Vector3 pullBackDir = new Vector3(-flatAway.x, 0f, -flatAway.y).normalized;

            // สปริงดึงสะโพกกลับเข้าระยะ + หน่วงเฉพาะความเร็วขาออก (ไม่แตะแกนตั้ง/แนวขวาง)
            Vector3 torsoVel = torso.torsoRb.linearVelocity;
            float outwardSpeed = Vector3.Dot(new Vector3(torsoVel.x, 0f, torsoVel.z), -pullBackDir);

            Vector3 tetherForce = pullBackDir * (excess * legStretchSpring);
            if (outwardSpeed > 0f)
                tetherForce += pullBackDir * (outwardSpeed * legStretchDamper);

            torso.torsoRb.AddForce(tetherForce, ForceMode.Acceleration);
        }
    }

    // ── Leg Reach Helpers ──────────────────────────────────────────────

    // รัศมีแนวราบสูงสุดระหว่างสะโพกกับจุดปักเท้า = maxLegLength ตรงๆ
    // ⚠️ ห้ามคิดแบบ Pythagoras หักความสูงสะโพก — hover spring พยุงสะโพกลอยสูง
    // เกือบเท่าความยาวขาอยู่แล้ว ระยะแนวราบจะเหลือ ~0 ทำให้จุดปักโดน clamp มาอยู่
    // ใต้ตัว + tether รั้งสวนตลอดเวลา = อาการ "เดินไม่ไปข้างหน้า"
    private float MaxHorizontalReach() => maxLegLength;

    private Vector3 ClampPlantWithinReach(Vector3 groundPos)
    {
        Vector3 delta = groundPos - pivotPoint.position;
        Vector2 flat = new Vector2(delta.x, delta.z);
        float maxHorizontal = MaxHorizontalReach() * 0.9f; // ปักลึกกว่าเกณฑ์รั้งเล็กน้อย

        if (flat.magnitude <= maxHorizontal) return groundPos;

        Vector2 clamped = flat.normalized * maxHorizontal;
        Vector3 target = new Vector3(pivotPoint.position.x + clamped.x, groundPos.y, pivotPoint.position.z + clamped.y);

        // จุดใหม่อาจอยู่คนละระดับพื้น (ทางลาด/ขอบต่างระดับ) — หาความสูงพื้นจริงอีกรอบ
        if (Physics.Raycast(target + Vector3.up * 2f, Vector3.down, out RaycastHit hit, standingGroundRayLength, groundLayer))
            target.y = hit.point.y;

        return target;
    }

    // คืน true เมื่อเจอ joint อย่างน้อย 1 ตัว (สำเร็จ → เลิกเรียกซ้ำ)
    // คืน false ถ้าโซ่ยังไม่พร้อม (เช่นยัง spawn ไม่ครบ) → FixedUpdate หน้าลองใหม่
    private bool MakeLegJointChainUnbreakable()
    {
        Rigidbody currentBody = footRb;
        int safety = 0;
        bool foundAnyJoint = false;

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
                foundAnyJoint = true;

                if (nextBody == null && joint.connectedBody != null)
                    nextBody = joint.connectedBody;
            }

            currentBody = nextBody;
        }

        return foundAnyJoint;
    }

    private void PerformDetachedPhysics()
    {
        // + footThicknessOffset — เป้า detached ถูกส่งมาเป็นจุดบนพื้นตรงๆ (hit.point)
        // ไม่ยกขึ้นเท้าจะพยายามเอาศูนย์กลางตัวเองมุดลงไปอยู่ระดับพื้น
        Vector3 target = _detachedTargetPos + Vector3.up * footThicknessOffset;
        Vector3 velTarget = (target - footRb.position) * detachedMoveSpeed;
        // เพดานความเร็ว — สปริงไล่เป้าไกลๆ เคยคำนวณความเร็วมหาศาลจนเท้าปลิวหาย
        velTarget = Vector3.ClampMagnitude(velTarget, maxDetachedSpeed);
        footRb.AddForce((velTarget - footRb.linearVelocity) * legDamper, ForceMode.Acceleration);
    }

    private void HandleInput()
    {
        // ✅ [Camera-Independent Aim] ล็อกเคอร์เซอร์ + คำนวณจุดเล็งเท้าในแกนโลก (เหมือนมือ)
        if (useVirtualCursor) HandleFootCursorLock();

        // เคอร์เซอร์ปลดอยู่ (กด Esc ไปเมนู) → หยุดรับ input เท้าทั้งหมด
        // ✅ [Stuck-State Fix] ต้องเคลียร์ state ที่ค้างอยู่ก่อนหยุด — ไม่งั้นถ้ากด Esc
        // กลางก้าวเดิน/กลาง Q ค้าง server จะไม่มีวันได้รับคำสั่งหยุด เท้าเดินค้างตลอดที่อยู่ในเมนู
        if (useVirtualCursor && Cursor.lockState != CursorLockMode.Locked)
        {
            if (isStepping)
            {
                isStepping = false;
                SetSteppingStateRpc(false);
                _currentYOffset = 0f;
            }
            if (isPushingRecovery)
            {
                isPushingRecovery = false;
                SetRecoveryInputRpc(false);
            }
            return;
        }

        if (currentState.Value == FootState.Attached)
        {
            bool isRagdoll = torso != null && (torso.currentState.Value == TorsoMovement.TorsoState.Ragdoll || torso.currentState.Value == TorsoMovement.TorsoState.Falling);
            Vector3 mouseOffset = ComputeFootAimOffset();
            // marker: ยิงลงพื้นให้ไปเกาะพื้นจริง (default = ระดับสะโพกถ้าไม่เจอพื้น)
            _footMarkerWorld = Physics.Raycast(pivotPoint.position + mouseOffset + Vector3.up * 5f,
                    Vector3.down, out RaycastHit markerHit, 60f, groundLayer)
                ? markerHit.point
                : pivotPoint.position + mouseOffset;

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
                // คลิกที่ใช้ดึงเมาส์กลับ ไม่นับเป็นก้าวเดิน จนกว่าจะปล่อยแล้วกดใหม่
                if (_ignoreStepUntilRelease)
                {
                    if (!holdingClick) _ignoreStepUntilRelease = false;
                    holdingClick = false;
                }
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
                // + footThicknessOffset ด้วย — เดิมโหมด Ragdoll ใช้ hit.point ดิบๆ เท้าเลยมุดพื้น
                if (Physics.Raycast(pivotPoint.position + mouseOffset + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 10f, groundLayer)) newTarget = hit.point + Vector3.up * footThicknessOffset;
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
            // เท้าหลุด (Detached): ใช้จุดเล็งแกนโลกเดียวกัน ขยายระยะเอื้อมออก
            // ⚠️ ฐานจุดเล็งต้องเป็นสะโพก (จุดอ้างอิงนิ่ง) ห้ามใช้ตำแหน่งเท้าเอง —
            // เดิมเป้า = เท้า + offset ทำให้เป้าวิ่งหนีตามเท้าไปเรื่อยๆ (feedback loop)
            // ขยับเมาส์นิดเดียวเท้าเลยไล่เป้าด้วยความเร็วคงที่ไม่มีวันถึง = ปลิวหาย
            Vector3 offset = ComputeFootAimOffset() * 2.5f;
            Vector3 aimBase = pivotPoint != null ? pivotPoint.position : footRb.position;
            if (Physics.Raycast(aimBase + offset + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 10f, groundLayer))
            {
                _footMarkerWorld = hit.point; // อัปเดต marker ตอน Detached ด้วย — เดิมค้างที่จุดสุดท้ายก่อนหลุด
                if ((hit.point - _lastSentDetached).sqrMagnitude > RPC_SEND_THRESHOLD_SQR) { _lastSentDetached = hit.point; UpdateDetachedTargetRpc(hit.point); }
            }
        }
    }

    // ✅ [Pointer Lock Fix] fallback เมื่อไม่ได้ล็อกเคอร์เซอร์ — อ่านตำแหน่งเมาส์จริงบนจอ
    private Vector2 GetNormalizedMousePosition() => PlayerHandMovement.AimNormalized;

    // ── Camera-Independent Foot Aim (ยกระบบมาจากมือ) ──────────────────

    private void HandleFootCursorLock()
    {
        // Esc = ปลดล็อก เมาส์โผล่ (ไปกดเมนู)
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }

        // เคอร์เซอร์ปลดอยู่ → ครั้งแรกที่เข้าเกม หรือ คลิกซ้าย = ล็อกกลับ เมาส์หาย
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            if (!_footEverLocked || Input.GetMouseButtonDown(0))
            {
                LockFootCursor();
                _ignoreStepUntilRelease = true; // คลิกนี้ใช้ดึงเมาส์กลับ ห้ามไปเริ่มก้าวเดิน
            }
        }
    }

    private void LockFootCursor()
    {
        // เริ่มจุดเล็งจากตำแหน่งเท้าจริง ณ ตอนล็อก — เท้าไม่กระโดด
        Vector3 footOffset = footRb.position - pivotPoint.position;
        _footPlanarAim = new Vector3(footOffset.x, 0f, footOffset.z);
        float maxR = Mathf.Max(mouseReachX, mouseReachY);
        if (_footPlanarAim.sqrMagnitude > maxR * maxR) _footPlanarAim = _footPlanarAim.normalized * maxR;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        _footEverLocked = true;
    }

    // จุดเล็งเท้า = offset แกนโลกจาก pivot | เปลี่ยนด้วย delta เมาส์ (ตีความตามกล้องปัจจุบัน)
    // หันกล้อง = ไม่มี delta = จุดเล็งอยู่ที่เดิม | คลิกขวา = หมุนกล้อง จุดเล็งนิ่ง
    private Vector3 ComputeFootAimOffset()
    {
        Vector3 camFwd   = playerCamera.transform.forward; camFwd.y = 0f; camFwd.Normalize();
        Vector3 camRight = playerCamera.transform.right;  camRight.y = 0f; camRight.Normalize();

        if (useVirtualCursor && Cursor.lockState == CursorLockMode.Locked)
        {
            if (!Input.GetMouseButton(1))
            {
                Vector2 md = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * (mouseSensitivity * 0.1f);
                _footPlanarAim += camRight * (md.x * mouseReachX) + camFwd * (md.y * mouseReachY);
                _footPlanarAim.y = 0f;
                float maxR = Mathf.Max(mouseReachX, mouseReachY);
                if (_footPlanarAim.sqrMagnitude > maxR * maxR)
                    _footPlanarAim = _footPlanarAim.normalized * maxR;
            }
            return _footPlanarAim;
        }

        // fallback: เคอร์เซอร์ยังไม่ล็อก (กด Esc อยู่) → ใช้ตำแหน่งเมาส์จริง
        Vector2 mouseNorm = GetNormalizedMousePosition();
        return camRight * (mouseNorm.x * mouseReachX) + camFwd * (mouseNorm.y * mouseReachY);
    }

    private void OnGUI()
    {
        if (!IsOwner || !useVirtualCursor || !showCrosshair) return;
        if (Cursor.lockState != CursorLockMode.Locked) return;
        if (playerCamera == null) return;

        if (_footCrosshairStyle == null)
            _footCrosshairStyle = new GUIStyle(GUI.skin.label)
            { fontSize = 26, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };

        Vector3 screenPos = playerCamera.WorldToScreenPoint(_footMarkerWorld);
        if (screenPos.z <= 0f) return;
        float sx = screenPos.x, sy = Screen.height - screenPos.y;

        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.Label(new Rect(sx - 13f, sy - 13f, 30f, 30f), "◈", _footCrosshairStyle);
        GUI.color = new Color(0.5f, 0.85f, 1f);
        GUI.Label(new Rect(sx - 14f, sy - 14f, 30f, 30f), "◈", _footCrosshairStyle);
    }

    // ✅ [Reliability Fix] เปลี่ยน footTarget/balanceShift จาก Unreliable → Reliable
    // เดิมถ้าแพ็กเก็ตหลุดกลางอากาศตอนเดิน Server จะค้างใช้เป้าหมายเก่า พอแพ็กเก็ตใหม่มาถึงทีหลัง
    // ตำแหน่งกระโดดข้ามแบบกระแทก ดูเหมือนเท้ากระตุก/หลุดจากพื้น — อัตราส่งถูกจำกัดด้วย
    // RPC_SEND_THRESHOLD อยู่แล้ว ต้นทุน bandwidth ของ Reliable จึงต่ำมาก
    [Rpc(SendTo.Server, Delivery = RpcDelivery.Reliable)] private void UpdateFootTargetRpc(Vector3 v) { ValidateAndSetFootTarget(v); }
    [Rpc(SendTo.Server, Delivery = RpcDelivery.Reliable)] private void UpdateBalanceShiftRpc(Vector3 v) { ValidateAndSetBalanceShift(v); }
    [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)] private void UpdateDetachedTargetRpc(Vector3 v) { if (v.IsValid()) _detachedTargetPos = v; }
    [Rpc(SendTo.Server, Delivery = RpcDelivery.Reliable)] private void SetSteppingStateRpc(bool v) { isStepping = v; }
    [Rpc(SendTo.Server, Delivery = RpcDelivery.Reliable)] private void SetRecoveryInputRpc(bool v) { isPushingRecovery = v; }

    [Rpc(SendTo.Server, Delivery = RpcDelivery.Reliable)]
    private void ApplyJumpRpc()
    {
        // 🛡️ [Server Validation] เช็กซ้ำฝั่ง server เสมอ — เงื่อนไขฝั่ง client เชื่อไม่ได้
        // กัน client ยิง RPC รัวๆ ให้หุ่นลอยขึ้นฟ้า (เงื่อนไขเดียวกับที่ HandleInput เช็กก่อนส่ง)
        if (footRb == null) return;
        if (isJumping || _jumpCooldownTimer > 0f || !IsGrounded()) return;

        isJumping = true;
        _jumpCooldownTimer = JUMP_HOLD_DURATION;
        footRb.isKinematic = false; // 🔓 กระโดด: ปลดล็อกก่อน AddForce (ไม่งั้นแรงไม่มีผลบน kinematic body)

        // ✅ [Force Parity] เท้าไม่พุ่งเร็วกว่าลำตัว — clamp ด้วย maxFootJumpVelocity
        float footBoost = Mathf.Min(footJumpForce, maxFootJumpVelocity);
        footRb.AddForce(Vector3.up * footBoost, ForceMode.VelocityChange);

        // 🦘 แรงลำตัวให้ torso ตัดสินใจเอง: hop ขาเดียว / Co-op Jump สองขา / เพดานความเร็ว
        // พร้อมเปิด Jump Grace ไม่ให้ระบบสมดุลตัดสินว่า "กระโดด = กำลังล้ม"
        if (torso != null) torso.NotifyFootJump(this);
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

    // 🛡️ [Server Validation] clamp จุด balance shift แบบเดียวกับ foot target
    // เดิมเช็กแค่ IsValid (กัน NaN) — client ส่งพิกัดไกลๆ มาได้ แล้วค่านี้ถูกคูณเป็นแรงดัน
    // ลำตัวใน PerformStandingPhysics → แรงมหาศาลผิดปกติ
    private void ValidateAndSetBalanceShift(Vector3 target)
    {
        if (!target.IsValid()) return;
        if (pivotPoint != null)
        {
            Vector3 dir = target - pivotPoint.position;
            float limit = Mathf.Max(mouseReachX, mouseReachY) * SERVER_REACH_MARGIN;
            if (dir.magnitude > limit) target = pivotPoint.position + dir.normalized * limit;
        }
        _balanceShiftPos = target;
    }

    public bool IsGrounded()
    {
        if (footRb == null || pivotPoint == null) return false;
        Vector3 point1 = pivotPoint.position;
        Vector3 point2 = footRb.position;
        return Physics.CheckCapsule(point1, point2, groundCheckDistance, groundLayer);
    }
}
