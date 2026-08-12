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

    // ✅ [Walk Fall Rule] "ก้าวเดิน" ในความหมายของกฎการล้มเท่านั้น
    // isStepping ดิบๆ ใช้ตัดสินล้มไม่ได้ เพราะมันถูกเซ็ตจากหลายระบบที่ "ไม่ใช่การเดิน":
    //   - กระโดด (isJumping)      → ลอยเพราะตั้งใจ ไม่ใช่พลาดท่า
    //   - เตะ (IsKickControllingFoot) → ท่าต่อสู้ ขาข้างนี้เป็นของ PlayerLegCombat อยู่
    //   - ยันตัวลุก (isPushingRecovery)
    // TorsoMovement ใช้ตัวนี้นับ "สองขาก้าวพร้อมกัน" — ดูกฎล้มในไฟล์นั้น
    public bool IsWalkStepping =>
        isStepping && !isJumping && !isPushingRecovery && !IsKickControllingFoot;

    // ✅ [Support Foot] เท้าข้างนี้มีสิทธิ์ทำหน้าที่ "ขายัน" ไหม
    // ⚠️ จงใจไม่รวมการเช็ค IsGrounded() ไว้ในนี้ — ผู้เรียก (TorsoMovement) วน loop นับเท้าอยู่แล้ว
    // และเช็คพื้นด้วย Physics.CheckCapsule ซึ่งไม่ควรยิงซ้ำสองรอบต่อเท้าต่อ tick
    public bool CanActAsSupport =>
        currentState.Value == FootState.Attached &&
        !IsWalkStepping && !isJumping && !isPushingRecovery && !_releasedForClimb;

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
    [Tooltip("ความเร็วยกเท้าตอนเริ่มก้าว คิดเป็น 'กี่เท่าของ clickLiftHeight ต่อวินาที'\n" +
             "8 = ยกจนสุดใน ~0.125 วิ เท่ากันทุกสเกลหุ่น (ไม่ใช่ m/s ตรงๆ ไม่งั้นหุ่นตัวใหญ่จะยกอืด)\n" +
             "ระบบยกให้อัตโนมัติแล้ว — ไม่ต้องกด W/S คุมแกน Y เองอีกต่อไป")]
    [Min(0.1f)]
    public float stepLiftSpeed = 8f;
    [Tooltip("ความสูงที่เท้าลอยพ้นพื้นระหว่างกดคลิกซ้ายค้าง (ก้าวเดิน)\n" +
             "ปล่อยคลิกซ้าย = เป้าหมายกลับลงระดับพื้นจริงแล้วปักเท้าตามระบบ Standing Foot Lock เดิม")]
    [Min(0f)]
    public float clickLiftHeight = 0.35f;
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

    [Header("Mouse Range (ความไวทิศทาง + รัศมีการถ่ายน้ำหนัก)")]
    [Tooltip("ใช้เป็นรัศมีสูงสุดของจุดถ่ายน้ำหนัก (balance shift) และเพดาน validate ฝั่ง Server\n" +
             "⚠️ ไม่ได้เป็นตัวกำหนด 'ระยะก้าว' อีกต่อไป — ระยะก้าวอยู่ที่ min/maxFootReach ด้านล่าง")]
    public float mouseReachX = 2f;
    public float mouseReachY = 2f;

    // ── Foot Reach (ระยะก้าว) ──────────────────────────────────────────
    // 🎯 [Direction / Distance Split] เมาส์ = "ไปทางไหน" | ล้อเมาส์ = "ไปไกลแค่ไหน"
    // สองอย่างนี้อิสระจากกันสนิท: หมุนเมาส์รอบตัวไม่ทำให้ระยะเปลี่ยน และเลื่อนล้อไม่ทำให้ทิศเปลี่ยน
    [Header("Foot Reach (ล้อเมาส์ = ระยะใกล้/ไกล)")]
    [Tooltip("คำนวณ min/maxFootReach ให้อัตโนมัติจาก minLegLength/maxLegLength ตอน spawn\n" +
             "เปิดไว้ = เปลี่ยนสเกลหุ่นแล้วไม่ต้องมาจูนระยะก้าวใหม่ | ปิด = คุมตัวเลขเองใน Inspector")]
    public bool autoFootReachFromLegLength = true;
    [Tooltip("ระยะแนวราบใกล้ที่สุดจากสะโพกถึงเท้า (เมตร) = สเกล 0%")]
    [Min(0.01f)]
    public float minFootReach = 0.4f;
    [Tooltip("ระยะแนวราบไกลที่สุดจากสะโพกถึงเท้า (เมตร) = สเกล 100%")]
    [Min(0.02f)]
    public float maxFootReach = 1.35f;
    [Tooltip("ระยะเริ่มต้น เป็นสัดส่วนของช่วง min→max (0 = ใกล้สุด, 0.5 = ระยะปกติ, 1 = ยืดขาไกลสุด)")]
    [Range(0f, 1f)]
    public float defaultFootReach = 0.5f;
    [Tooltip("สัดส่วนของช่วง min→max ที่เปลี่ยนต่อการหมุนล้อ 1 คลิก\n" +
             "0.12 = หมุนล้อราว 8 คลิกจากใกล้สุดถึงไกลสุด (ค่าต่อเนื่อง ไม่ใช่ 3 ระดับตายตัว)\n" +
             "คิดเป็นสัดส่วนเพื่อให้ความรู้สึกเท่ากันทั้งหุ่นขาสั้นและขายาว")]
    [Min(0.001f)]
    public float footReachScrollSpeed = 0.12f;
    [Tooltip("ตัวคูณระยะเอื้อมตอนเท้าหลุด (Detached) — เท้าที่หลุดแล้วเอื้อมได้ไกลกว่าปกติ")]
    [Min(1f)]
    public float detachedReachMultiplier = 2.5f;

    // ระยะแนวราบจริงที่ผู้เล่นเลือกอยู่ตอนนี้ (เมตร) — เปลี่ยนด้วยล้อเมาส์เท่านั้น
    private float _currentFootDistance;
    // ทิศทางเท้าบนระนาบ XZ (normalized, แกนโลก) — เปลี่ยนด้วยการขยับเมาส์เท่านั้น
    private Vector3 _footDirection = Vector3.forward;
    // ก้านเล็ง: สะสม delta เมาส์แล้ว clamp |v| ≤ 1 — ใช้แค่ "ทิศ" ของมัน ความยาวไม่มีผลกับระยะก้าว
    private Vector3 _footAimStick = Vector3.zero;
    private float _currentLiftOffset = 0f;

    // GetAxis("Mouse ScrollWheel") คืนราว ±0.1 ต่อการหมุนล้อ 1 คลิก
    // หารกลับให้เป็น "จำนวนคลิก" ก่อน ระยะที่ได้ต่อคลิกจะได้เท่ากันทุกเครื่อง
    private const float SCROLL_NOTCH = 0.1f;
    private const float AIM_STICK_DEADZONE_SQR = 0.0025f; // 0.05²
    // จุดปักเท้าถูกดึงเข้ามาอยู่ในระยะ 90% ของรัศมีที่ขาเอื้อม (ดู ClampPlantWithinReach)
    // maxFootReach แบบ auto จึงใช้ค่าเดียวกัน — ผู้เล่นจะเลื่อนล้อไปยังระยะที่โดน clamp ทิ้งไม่ได้เลย
    private const float PLANT_REACH_SAFETY = 0.9f;

    /// <summary>ระยะแนวราบที่ผู้เล่นเลือกอยู่ (เมตร) — สำหรับ HUD/ดีบัก</summary>
    public float CurrentFootDistance => _currentFootDistance;
    /// <summary>ระยะที่เลือกอยู่ในสเกล 0..1 ของช่วง min→max — สำหรับ HUD</summary>
    public float NormalizedFootReach =>
        maxFootReach > minFootReach
            ? Mathf.Clamp01((_currentFootDistance - minFootReach) / (maxFootReach - minFootReach))
            : 0f;
    /// <summary>ทิศทางที่เท้าจะไปบนระนาบ XZ (แกนโลก) — Kick อ่านค่านี้ผ่าน GetKickAimDirection()</summary>
    public Vector3 FootDirection => _footDirection;

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

    private bool _footEverLocked = false;
    private bool _ignoreStepUntilRelease = false; // กันคลิกที่ใช้ดึงเมาส์กลับไปเริ่มก้าวเดิน
    private Vector3 _footMarkerWorld;
    private static GUIStyle _footCrosshairStyle;
    private static GUIStyle _footReachStyle;
    private PlayerLegCombat _legCombat;

    public bool IsKickMotionActive =>
        _legCombat != null && _legCombat.IsKickMotionActive;

    // รวมช่วงง้างขาด้วย — ตอนง้าง PlayerLegCombat เป็นคนขับเท้าเอง
    // ถ้าปล่อยให้สปริงก้าวเดินทำงานต่อ มันจะลากเท้ากลับไปหาจุดที่เมาส์ชี้ ท่าง้างเลยไม่เกิด
    public bool IsKickControllingFoot =>
        _legCombat != null && _legCombat.IsKickControllingFoot;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer && torso != null) torso.RegisterFoot(this);
        _footNetworkTransform = GetComponent<NetworkTransform>();
        _legCombat = GetComponent<PlayerLegCombat>();

        // 📏 [Auto Thickness] วัดระยะจากจุดกำเนิด Rigidbody ถึง "จุดต่ำสุดของ collider เท้า"
        // = ความหนาที่ต้องยกจริง — รันทุกเครื่อง (ฝั่ง owner ใช้คำนวณเป้าตอนก้าวด้วย)
        if (autoMeasureFootThickness) MeasureFootThickness();

        // 🎯 ตั้งช่วงระยะก้าว + ค่าเริ่มต้นของทิศทาง/ระยะ ก่อนเฟรมแรกของ input
        ApplyAutoFootReach();
        ResetFootAim();

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

    // 🎯 ผูกช่วงระยะก้าวเข้ากับความยาวขาจริง — หุ่นสเกล 1.5 หรือสเกล 14 ก็ใช้ค่าเดียวกันได้
    // ปลายไกลใช้ตัวคูณเดียวกับ ClampPlantWithinReach เพื่อไม่ให้ผู้เล่นเลื่อนล้อไปยังระยะ
    // ที่พอปล่อยคลิกแล้วจุดปักโดนดึงกลับเข้ามา (ระยะที่เลือกได้ = ระยะที่ปักได้จริงเสมอ)
    private void ApplyAutoFootReach()
    {
        if (!autoFootReachFromLegLength) return;

        minFootReach = Mathf.Max(0.05f, minLegLength);
        maxFootReach = Mathf.Max(minFootReach + 0.05f, MaxHorizontalReach() * PLANT_REACH_SAFETY);
    }

    /// <summary>ตั้งทิศทาง/ระยะกลับเป็นค่าเริ่มต้น (ตอน spawn และหลัง respawn)</summary>
    private void ResetFootAim()
    {
        _currentFootDistance = Mathf.Lerp(minFootReach, maxFootReach, defaultFootReach);
        _currentLiftOffset = 0f;

        Vector3 forward = pivotPoint != null ? pivotPoint.forward : transform.forward;
        forward.y = 0f;
        _footDirection = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        _footAimStick = _footDirection;
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
        if (!IsKickMotionActive &&
            footRb.linearVelocity.sqrMagnitude > maxFootVelocity * maxFootVelocity)
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
            // Charge Kick owns the foot Rigidbody during the wind-up/launch/recovery window.
            // Do not let the standing lock or step spring cancel the kick velocity.
            if (IsKickControllingFoot)
            {
                _isPlantedSet = false;
                footRb.isKinematic = false;
                return;
            }

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
        // ปักลึกกว่าเกณฑ์รั้งเล็กน้อย — ตัวคูณเดียวกับที่ ApplyAutoFootReach ใช้กำหนด maxFootReach
        // จะได้ไม่มีวันหลุดเป็นคนละค่าจนผู้เล่นเลื่อนล้อไปยังระยะที่ปักจริงไม่ได้
        float maxHorizontal = MaxHorizontalReach() * PLANT_REACH_SAFETY;

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
                _currentLiftOffset = 0f;
            }
            if (isPushingRecovery)
            {
                isPushingRecovery = false;
                SetRecoveryInputRpc(false);
            }
            return;
        }

        // 🎯 อ่านทิศทาง (เมาส์) + ระยะ (ล้อ) ครั้งเดียวต่อเฟรม แล้วรวมเป็น offset แนวราบ
        UpdateFootAim();
        Vector3 aimOffset = PlanarAimOffset();

        if (currentState.Value == FootState.Attached)
        {
            bool isRagdoll = torso != null && (torso.currentState.Value == TorsoMovement.TorsoState.Ragdoll || torso.currentState.Value == TorsoMovement.TorsoState.Falling);
            // marker: ยิงลงพื้นให้ไปเกาะพื้นจริง (default = ระดับสะโพกถ้าไม่เจอพื้น)
            _footMarkerWorld = Physics.Raycast(pivotPoint.position + aimOffset + Vector3.up * 5f,
                    Vector3.down, out RaycastHit markerHit, 60f, groundLayer)
                ? markerHit.point
                : pivotPoint.position + aimOffset;

            // ⚖️ จุดถ่ายน้ำหนักตามทิศที่เล็ง แต่ยังคุมด้วยรัศมี mouseReach เดิม
            // ระยะก้าวไกลขึ้นได้ถึง maxFootReach แต่แรงเอนลำตัวต้องไม่โตตามไปด้วย
            // (ค่านี้ถูกคูณเป็นแรงดันลำตัวใน PerformStandingPhysics — โตขึ้นเมื่อไหร่ท่ายืนเพี้ยนทันที)
            Vector3 balanceOffset = Vector3.ClampMagnitude(aimOffset, Mathf.Max(mouseReachX, mouseReachY));
            Vector3 newBalance = pivotPoint.position + balanceOffset;
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
                // 🖱️ คลิกซ้ายค้าง = ยกขาแล้วเคลื่อนเท้าไปยังเป้า | ปล่อย = ปักเท้าลง
                if (holdingClick && !isStepping)
                {
                    isStepping = true;
                    SetSteppingStateRpc(true);
                    // เริ่มจากความสูงปัจจุบัน (0 = ระดับพื้น) แล้วไต่ขึ้นเอง — ไม่ snap ทันที
                    _currentLiftOffset = 0f;
                }
                else if (!holdingClick && isStepping) { isStepping = false; SetSteppingStateRpc(false); _currentLiftOffset = 0f; }

                if (isStepping)
                {
                    // ⬆️ [Auto Lift] ยกเท้าให้เองระหว่างก้าว — ตัด W/S ทิ้งแล้ว
                    // ผู้เล่นเลือกแค่ "ทิศ" กับ "ระยะ" ส่วนแกน Y เป็นหน้าที่ของระบบ:
                    // พื้น → ยกขึ้น clickLiftHeight → เคลื่อนไปเป้า → ปล่อยคลิกแล้วค่อยลงพื้นจริง
                    float liftTarget = Mathf.Clamp(clickLiftHeight, 0f, maxLegLength);
                    // อัตราคิดเป็นสัดส่วนของความสูงเป้า → เวลายกเท่ากันทั้งหุ่นเล็กและหุ่นสเกลยักษ์
                    _currentLiftOffset = Mathf.MoveTowards(
                        _currentLiftOffset, liftTarget, liftTarget * stepLiftSpeed * Time.deltaTime);

                    Vector3 planarTarget = pivotPoint.position + aimOffset;
                    Vector3 newTarget;
                    // 🦿 ยิง Raycast หาระดับพื้น ณ จุดที่เลือก (50m เผื่อหุ่นสเกลยักษ์สะโพกสูง)
                    // แล้วยกเป้าขึ้นตามระยะยกอัตโนมัติ + ชดเชยความหนาเท้ากันจมดิน
                    if (Physics.Raycast(planarTarget + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 50f, groundLayer))
                        newTarget = new Vector3(planarTarget.x, hit.point.y + _currentLiftOffset + footThicknessOffset, planarTarget.z);
                    else
                        newTarget = planarTarget + Vector3.down * maxLegLength;

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
                    _currentLiftOffset = 0f;
                }

                Vector3 newTarget;
                // + footThicknessOffset ด้วย — เดิมโหมด Ragdoll ใช้ hit.point ดิบๆ เท้าเลยมุดพื้น
                if (Physics.Raycast(pivotPoint.position + aimOffset + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 10f, groundLayer)) newTarget = hit.point + Vector3.up * footThicknessOffset;
                else newTarget = pivotPoint.position + aimOffset;

                if ((newTarget - _lastSentTarget).sqrMagnitude > RPC_SEND_THRESHOLD_SQR) { _lastSentTarget = newTarget; UpdateFootTargetRpc(newTarget); }

                bool pressingQ = Input.GetKey(KeyCode.Q);
                
                // ถอดเงื่อนไขระยะห่างทิ้งไปเลย! ขอแค่กด Q และเท้าเหยียบพื้นอยู่ ก็ลุกได้ทันที
                bool validPush = pressingQ && IsGrounded();
                                 
                if (validPush != isPushingRecovery) { isPushingRecovery = validPush; SetRecoveryInputRpc(validPush); }
            }
        }
        else
        {
            // เท้าหลุด (Detached): ใช้ทิศ/ระยะชุดเดียวกัน แค่ขยายระยะเอื้อมออก
            // ⚠️ ฐานจุดเล็งต้องเป็นสะโพก (จุดอ้างอิงนิ่ง) ห้ามใช้ตำแหน่งเท้าเอง —
            // เดิมเป้า = เท้า + offset ทำให้เป้าวิ่งหนีตามเท้าไปเรื่อยๆ (feedback loop)
            // ขยับเมาส์นิดเดียวเท้าเลยไล่เป้าด้วยความเร็วคงที่ไม่มีวันถึง = ปลิวหาย
            Vector3 offset = aimOffset * detachedReachMultiplier;
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
        // 🏁 เกมจบแล้ว (Win/GameOver panel ขึ้น) — ห้ามล็อกเมาส์กลับ ผู้เล่นต้องคลิกปุ่มบน panel
        if (GameFlowManager.GameEnded) return;

        // 🖱️ มี UI เปิดอยู่ (วงล้อไอเทม/เมนู) — ห้ามแย่งเมาส์กลับ (ดู UiFocus)
        if (UiFocus.IsCaptured) return;

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
        // เริ่มทิศ+ระยะจากท่าเท้าจริง ณ ตอนล็อก — เท้าไม่กระโดดตำแหน่ง
        Vector3 footOffset = footRb.position - pivotPoint.position;
        Vector3 planar = new Vector3(footOffset.x, 0f, footOffset.z);
        if (planar.sqrMagnitude > 0.0001f)
        {
            _footDirection = planar.normalized;
            _footAimStick  = _footDirection; // ก้านชี้ทิศเดิม เมาส์ครั้งต่อไป = หมุนทิศต่อจากนี้
            _currentFootDistance = Mathf.Clamp(planar.magnitude, minFootReach, maxFootReach);
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        _footEverLocked = true;
    }

    // ── Direction (เมาส์) + Distance (ล้อ) ─────────────────────────────
    //
    // 🎯 หลักการ: เมาส์เลือก "ไปทางไหน" ล้อเลือก "ไปไกลแค่ไหน" — อิสระจากกันสนิท
    //
    // ทิศทางเก็บเป็นเวกเตอร์ในแกนโลก ไม่ใช่แกนกล้อง (World-Anchored Aim ของเดิม)
    // → หันกล้องแล้วเท้าไม่กวาดตาม | คลิกขวาค้าง = เมาส์และล้อเป็นของกล้องล้วน
    //   ทิศ/ระยะค้างไว้เป๊ะ ปล่อยคลิกขวาแล้วคุมต่อจากค่าเดิมทันที ไม่มี reset ไม่มีกระโดด
    private void UpdateFootAim()
    {
        Vector3 camFwd   = playerCamera.transform.forward; camFwd.y = 0f;
        Vector3 camRight = playerCamera.transform.right;   camRight.y = 0f;
        // กล้องก้มดิ่ง 90° → forward แบนราบเหลือ ~0 ใช้ up มาแทนทิศ "หน้าจอด้านบน"
        if (camFwd.sqrMagnitude < 0.0001f)
        {
            camFwd = playerCamera.transform.up; camFwd.y = 0f;
            if (camFwd.sqrMagnitude < 0.0001f) camFwd = Vector3.forward;
        }
        camFwd.Normalize();
        camRight.Normalize();

        bool cameraMode = Input.GetMouseButton(1); // คลิกขวาค้าง = โหมดกล้อง

        // ── ทิศทาง ──────────────────────────────────────────────────
        if (useVirtualCursor && Cursor.lockState == CursorLockMode.Locked)
        {
            if (!cameraMode)
            {
                Vector2 md = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) *
                             (mouseSensitivity * MouseSettings.Multiplier * 0.1f);
                _footAimStick += camRight * md.x + camFwd * md.y;
                _footAimStick.y = 0f;
                // ก้านถูก clamp ที่รัศมี 1 — ความยาวไม่มีผลกับระยะก้าวเลย ใช้แค่ทิศของมัน
                // ผลคือพอดันเมาส์จนสุดขอบก้าน การขยับต่อ = "หมุนทิศรอบตัว" ซึ่งคือสิ่งที่ต้องการ
                if (_footAimStick.sqrMagnitude > 1f) _footAimStick.Normalize();
            }
        }
        else
        {
            // fallback: ไม่ได้ใช้ virtual cursor → ตำแหน่งเมาส์บนจอทำหน้าที่เป็นก้านทิศทาง
            Vector2 mouseNorm = GetNormalizedMousePosition();
            Vector3 stick = camRight * mouseNorm.x + camFwd * mouseNorm.y;
            stick.y = 0f;
            _footAimStick = stick.sqrMagnitude > 1f ? stick.normalized : stick;
        }

        // ก้านอยู่กลาง (แทบไม่มีทิศ) = คงทิศเดิมไว้ ห้ามให้ทิศสะบัดมั่วตอนผู้เล่นหยุดมือ
        if (_footAimStick.sqrMagnitude > AIM_STICK_DEADZONE_SQR)
            _footDirection = _footAimStick.normalized;

        // ── ระยะ ────────────────────────────────────────────────────
        if (!cameraMode)
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.0001f)
            {
                float notches = scroll / SCROLL_NOTCH;
                _currentFootDistance += notches * footReachScrollSpeed * (maxFootReach - minFootReach);
            }

            // จองล้อทุกเฟรมที่ขาถือสิทธิ์อยู่ (ไม่ใช่เฉพาะเฟรมที่หมุนจริง) — ธงจะได้นิ่ง
            // ไม่กะพริบจนกล้องแอบซูมแทรกระหว่างคลิกล้อสองครั้ง
            MouseWheelFocus.Claim();
        }

        _currentFootDistance = Mathf.Clamp(_currentFootDistance, minFootReach, maxFootReach);
    }

    /// <summary>เป้าหมายแนวราบ = ทิศทาง × ระยะ (offset จากสะโพก)</summary>
    private Vector3 PlanarAimOffset() => _footDirection * _currentFootDistance;

    public Vector3 GetKickAimDirection()
    {
        // ทิศเตะ = ทิศที่ผู้เล่นเล็งไว้ตรงๆ ไม่ต้องคำนวณย้อนจากจุด marker อีกแล้ว
        // (ระบบเก่าอนุมานทิศจาก marker ลบสะโพก ซึ่งยุบเป็นศูนย์เวลาเท้าลอยอยู่เหนือ marker พอดี)
        if (_footDirection.sqrMagnitude > 0.001f)
            return _footDirection.normalized;

        Vector3 direction = pivotPoint != null ? pivotPoint.forward : transform.forward;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f) direction = Vector3.forward;

        // A push kick travels horizontally. Upward bias made the whole robot hop
        // and read visually as a jump instead of a thrust.
        return direction.normalized;
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

        // ระยะที่เลือกด้วยล้อเมาส์ — ไม่มีตัวเลขนี้ผู้เล่นจะไม่รู้เลยว่าหมุนล้อไปถึงไหนแล้ว
        if (_footReachStyle == null)
            _footReachStyle = new GUIStyle(GUI.skin.label)
            { fontSize = 13, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };

        string reachText = $"{Mathf.RoundToInt(NormalizedFootReach * 100f)}%";
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.Label(new Rect(sx - 29f, sy + 13f, 60f, 20f), reachText, _footReachStyle);
        GUI.color = new Color(0.5f, 0.85f, 1f);
        GUI.Label(new Rect(sx - 30f, sy + 12f, 60f, 20f), reachText, _footReachStyle);
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
            // 🛡️ [Server Validation] สองชั้น — client ส่ง "จุดเป้าหมาย" มา ไม่ใช่ทิศ+ระยะดิบ
            // จึงต้องเช็คทั้งรัศมีทรงกลม (ขาเอื้อมถึงจริงไหม) และรัศมีแนวราบ (ก้าวไกลเกินระยะ
            // ที่ล้อเมาส์เลื่อนได้สูงสุดไหม) — เดิมมีแค่ชั้นแรก client จึงยิงเป้าไกลผิดปกติ
            // ในแนวราบเข้ามาได้ตราบใดที่ยังอยู่ในทรงกลม
            Vector3 dir = target - pivotPoint.position;
            float limit = maxLegLength * SERVER_REACH_MARGIN;
            if (dir.magnitude > limit) target = pivotPoint.position + dir.normalized * limit;

            Vector3 flat = new Vector3(target.x - pivotPoint.position.x, 0f, target.z - pivotPoint.position.z);
            float flatLimit = maxFootReach * SERVER_REACH_MARGIN;
            if (flat.magnitude > flatLimit)
            {
                Vector3 clampedFlat = flat.normalized * flatLimit;
                target = new Vector3(pivotPoint.position.x + clampedFlat.x, target.y, pivotPoint.position.z + clampedFlat.z);
            }
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

    /// <summary>
    /// เรียกจาก RespawnManager (ฝั่ง server) หลัง teleport ร่างกลับเช็คพอยต์
    /// สำคัญสุดคือ _isPlantedSet — plantedPosition เก่ายังชี้จุดตกเหว ถ้าไม่ล้าง
    /// PerformStandingPhysics จะ MovePosition ลากเท้ากลับไปก้นเหวในเฟรมถัดไปทันที
    /// และรีเซ็ตเป้าทุกตัวเป็นตำแหน่งปัจจุบัน (หลัง teleport แล้ว) กันสปริงไล่เป้าเก่า
    /// </summary>
    public void ResetForRespawn()
    {
        if (!IsServer) return;

        _isPlantedSet     = false;
        _releasedForClimb = false;
        isStepping        = false;
        isJumping         = false;
        isPushingRecovery = false;
        _jumpCooldownTimer = 0f;
        if (_legCombat == null)
            _legCombat = GetComponent<PlayerLegCombat>();
        _legCombat?.ResetForRespawn();

        if (footRb != null)
        {
            _targetFootPos     = footRb.position;
            _detachedTargetPos = footRb.position;
            _balanceShiftPos   = pivotPoint != null ? pivotPoint.position : footRb.position;
        }

        // Owner ถือทิศ/ระยะ/สถานะปุ่มไว้ในเครื่องตัวเอง — ถ้าไม่สั่งล้างด้วย มันจะไม่ตรงกับ server
        // ที่เพิ่งถูกรีเซ็ตไป (โดยเฉพาะ isStepping: owner ที่ยังกดคลิกซ้ายค้างอยู่จะไม่ส่ง
        // SetSteppingStateRpc ซ้ำ เพราะฝั่งมันไม่เห็นว่าค่าเปลี่ยน → เท้าค้างไม่ยอมก้าวอีกเลย)
        if (IsSpawned) ResetAimOnOwnerRpc();
    }

    [Rpc(SendTo.Owner, Delivery = RpcDelivery.Reliable)]
    private void ResetAimOnOwnerRpc()
    {
        isStepping = false;
        isPushingRecovery = false;
        _ignoreStepUntilRelease = true; // ต้องปล่อยคลิกซ้ายก่อน ถึงจะเริ่มก้าวใหม่ได้

        ApplyAutoFootReach();
        ResetFootAim();

        // ล้างแคชกันส่งซ้ำ — เป้าใหม่หลัง teleport ต้องถูกส่งทันทีแม้จะบังเอิญใกล้ค่าเดิม
        _lastSentTarget = _lastSentBalance = _lastSentDetached = Vector3.positiveInfinity;
    }
}
