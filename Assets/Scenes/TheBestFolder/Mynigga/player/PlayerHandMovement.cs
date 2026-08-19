using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

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
    
    [Header("Offsets (ปรับจุดศูนย์กลางได้อิสระ)")]
    public Vector3 pivotOffset = Vector3.zero;
    public Vector3 grabOffset = Vector3.zero;

    public Vector3 PivotPosition => pivotPoint != null ? pivotPoint.TransformPoint(pivotOffset) : transform.position;
    public Vector3 GrabPosition => handRb != null ? handRb.transform.TransformPoint(grabOffset) : transform.position;

    [Header("Movement & IK Tuning")]
    public float maxArmLength = 1.8f;
    public float handMoveSpeed = 25f;
    public float handDamper = 15f;
    public float grabRadius = 0.5f;
    public float grabBreakForce = 10000f; // แรงฉีกขาดเมื่อดึงของหนักเกิน
    [Tooltip("ทำให้ Grab FixedJoint และข้อต่อทั้งโซ่แขนไม่มีวันแตกจากแรงฟิสิกส์ (ยังปล่อย Grab ด้วยปุ่ม F ได้)")]
    public bool preventGrabBreakWhileRagdoll = true;
    public float torsoPullForce = 60f;
    [Tooltip("แรงที่ดึงตัวเมื่อจับ Kinematic Object (ใช้ปีนป่าย)")]
    public float kinematicPullForce = 150f;
    [Tooltip("If disabled, pulling a fixed wall does not add torso break stress.")]
    public bool kinematicGrabAddsStress = false;
    public float detachedMoveSpeed = 20f;
    public LayerMask grabLayer;
    public LayerMask groundLayer;

    [Header("Smoothing (Anti-Jitter)")]
    [Tooltip("เวลาที่มือใช้ไล่ถึงเป้าหมาย (SmoothDamp) — สั้น = ตอบสนองไว/คม แนะนำ 0.03–0.05")]
    public float smoothTime = 0.04f;

    [Header("Stability (Anti-Overshoot)")]
    [Tooltip("ระยะจากเป้าหมายที่เริ่มเบรกความเร็วมือ กันพุ่งเลยเป้าแล้วดีดกลับ")]
    public float brakeDistance = 0.5f;
    [Tooltip("ความแรงเบรกเมื่อเข้าใกล้เป้าหมาย (Acceleration)")]
    public float brakeDamping = 8f;
    [Tooltip("ชดเชยแรงโน้มถ่วงของมือ — สปริงไม่ต้องแบกน้ำหนักมือค้างไว้ ทำให้ยืดถึงเป้าจริง ไม่ตกสั้น")]
    public bool compensateGravity = true;
    [Tooltip("เพดานความเร็วเป้าหมายของสปริง (m/s)\n" +
             "กันสปริงออกแรงมหาศาลตอนเป้าอยู่ไกล ซึ่งทำให้โซ่ข้อต่อแขนสั่น/ระเบิด")]
    public float maxHandVelocity = 40f;
    [Tooltip("ปิดการชนระหว่างมือกับชิ้นส่วนหุ่นตัวเอง\n" +
             "ตัดอาการสั่นจากมือครูดลำตัว/แขน/มืออีกข้างตอนเล็งไปมา")]
    public bool ignoreSelfCollision = true;
    [Tooltip("Match arm segment masses and raise solver quality while holding a grab.")]
    public bool stabilizeArmChainWhileGrabbing = true;
    [Min(0.01f)] public float stabilizedArmMass = 1f;
    [Min(1)] public int stabilizedArmSolverIterations = 20;
    [Min(1)] public int stabilizedArmSolverVelocityIterations = 8;
    [Tooltip("สัดส่วนแรงสปริงมือตอนล้ม (Ragdoll) — ต่ำ = มือห้อยตามแรงโน้มถ่วง แต่ยังนัดจากเมาส์ได้เบาๆ\n" +
             "0 = มือปล่อยตกอิสระเลย / 1 = สปริงแรงเท่าตอนยืน")]
    [Range(0f, 1f)] public float ragdollHandSpringScale = 0.25f;

    // เพดานความเร็วชั่วคราว — คลาสลูกเซ็ตตอนต่อย (0 = ใช้ maxHandVelocity ปกติ)
    protected float velocityCapOverride = 0f;

    // ระยะยืดพิเศษชั่วคราว — คลาสลูก (PlayerHandCombat) เซ็ตตอนปล่อยหมัด
    // เพื่อขยาย reach limit จริงๆ แทนการหลอกเป้าหมายที่โดน clamp ทิ้ง
    protected float extraReach = 0f;

    [Header("Arm Reach Limits")]
    [Tooltip("สัดส่วนแรงแนวราบ (XZ) ที่ส่งไปยังลำตัวเมื่อแขนยืดสุด\n" +
             "0 = ดันได้แค่แนวตั้ง (ลุกหรือยืนไม่ได้ใช้มือโกง)\n" +
             "1 = ดันได้เต็มทุกทิศ (เหมือนเดิม)\n" +
             "แนะนำ 0.15–0.25 = รู้สึกว่ามือดึงบ้างแต่ไม่โกงได้")]
    [Range(0f, 1f)]
    public float torsoPullHorizontalScale = 0.2f;
    [Tooltip("ความเร็วสูงสุดของลำตัวในแนวราบที่แรงดึงจะเริ่มลดลง (m/s)\n" +
             "ถ้าตัววิ่งเร็วกว่านี้แล้ว แรงดึงแนวราบจะ = 0 กันสะสม momentum")]
    public float maxAllowedHorizontalBoost = 3f;

    [Header("Mouse Sensitivity (ระนาบหน้าจอ)")]
    [Tooltip("ความไวการลากมือในแนวนอนของหน้าจอ (Mouse X → ซ้าย/ขวา)")]
    public float mouseReachX = 3f;
    [Tooltip("ความไวการลากมือในแนวตั้งของหน้าจอ (Mouse Y → ขึ้น/ลง)\n" +
             "⚠️ เดิมค่านี้เป็น 'ลิมิตความสูงจาก W/S' — ตอนนี้เป็นความไวแนวตั้ง\n" +
             "ตั้งให้เท่ากับ mouseReachX ถ้าอยากได้ความรู้สึกสมมาตรบนหน้าจอ")]
    public float mouseReachY = 3f;

    [Tooltip("สเกลความไวตามระยะกล้อง — ให้ 'มือขยับกี่พิกเซลบนจอ' คงที่ทุกระดับซูม\n" +
             "ปิด = ความไวคิดเป็นเมตรในโลกตายตัว ซึ่งทำให้กล้องใกล้ไวเกินคุมไม่อยู่ " +
             "แต่กล้องไกลกลับกำลังดี (มือขยับเท่ากันในโลก = กินพื้นที่จอไม่เท่ากัน)")]
    public bool scaleAimWithCameraDistance = true;

    [Tooltip("ระยะกล้าง (เมตร) ที่ถือว่าความไว = mouseReachX/Y เป๊ะๆ\n" +
             "ตั้งให้ตรงกับระยะที่จูนความไวไว้ตอนแรก — ใกล้กว่านี้จะละเอียดขึ้น ไกลกว่าจะกว้างขึ้น\n" +
             "แนะนำให้ตรงกับ PlayerCam.maxDistance")]
    [Min(0.1f)]
    public float aimReferenceCameraDistance = 20f;

    // ── Hand Depth (ล้อเมาส์ = เข้า/ออกจากตัว) ─────────────────────────
    // 🎯 ล้อเมาส์ขยับมือตามแนว "ไหล่ → มือ" ซึ่งเป็นแกนที่ไม่ขึ้นกับกล้องเลย
    // (ถ้าใช้ camera forward ตรงๆ พอหมุนกล้องแล้วมือจะกวาดตาม = ผิดข้อกำหนด World-Anchored)
    [Header("Hand Depth (ล้อเมาส์ = ยื่นออก/ดึงเข้า)")]
    [Tooltip("คำนวณ min/maxHandDepth ให้อัตโนมัติจาก maxArmLength ตอน spawn\n" +
             "เปิดไว้ = เปลี่ยนสเกลหุ่น/ความยาวแขนแล้วไม่ต้องมาจูนใหม่ | ปิด = คุมตัวเลขเองใน Inspector")]
    public bool autoHandDepthFromArmLength = true;
    [Tooltip("ระยะใกล้ที่สุดจากไหล่ถึงมือ (เมตร) = สเกล 0% — มือชิดตัว")]
    [Min(0.01f)]
    public float minHandDepth = 0.3f;
    [Tooltip("ระยะไกลที่สุดจากไหล่ถึงมือ (เมตร) = สเกล 100% — ยื่นสุดระยะที่แขนเอื้อมถึง")]
    [Min(0.02f)]
    public float maxHandDepth = 1.7f;
    [Tooltip("ความลึกเริ่มต้น เป็นสัดส่วนของช่วง min→max (0 = ชิดตัว, 0.5 = ระยะปกติ, 1 = ยื่นสุด)")]
    [Range(0f, 1f)]
    public float defaultHandDepth = 0.5f;
    [Tooltip("สัดส่วนของช่วง min→max ที่เปลี่ยนต่อการหมุนล้อ 1 คลิก\n" +
             "0.12 = หมุนล้อราว 8 คลิกจากชิดตัวถึงยื่นสุด (ค่าต่อเนื่อง ไม่ใช่ Near/Medium/Far เป็นขั้น)\n" +
             "คิดเป็นสัดส่วนเพื่อให้ความรู้สึกเท่ากันทั้งแขนสั้นและแขนยาว")]
    [Min(0.001f)]
    public float handDepthScrollSpeed = 0.12f;

    // GetAxis("Mouse ScrollWheel") คืนราว ±0.1 ต่อการหมุนล้อ 1 คลิก — หารกลับเป็น "จำนวนคลิก"
    private const float SCROLL_NOTCH = 0.1f;
    // maxHandDepth แบบ auto กันไว้ต่ำกว่า maxArmLength เล็กน้อย เป้าจะได้ไม่ไปนั่งอยู่บนผิว
    // soft-clamp ของ PerformArmMovement ตลอดเวลา (สปริงจะออกแรงสู้ลิมิตค้างแทนที่จะนิ่ง)
    private const float ARM_DEPTH_SAFETY = 0.95f;

    /// <summary>ระยะจากไหล่ถึงเป้ามือตอนนี้ (เมตร) — สำหรับ HUD/ดีบัก</summary>
    public float CurrentHandDepth => sharedAimOffsetWorld.magnitude;
    /// <summary>ความลึกในสเกล 0..1 ของช่วง min→max — สำหรับ HUD</summary>
    public float NormalizedHandDepth =>
        maxHandDepth > minHandDepth
            ? Mathf.Clamp01((CurrentHandDepth - minHandDepth) / (maxHandDepth - minHandDepth))
            : 0f;

    [Header("Virtual Cursor (แก้ปัญหาขอบจอ)")]
    [Tooltip("ล็อกเคอร์เซอร์จริงไว้กลางจอ แล้วใช้ mouse delta ขยับ 'จุดเล็งเสมือน' แทน\n" +
             "เลื่อนเมาส์ได้ไม่จำกัด ไม่มีขอบจออีกต่อไป | ปิด = กลับไปใช้ตำแหน่งเมาส์จริงแบบเดิม")]
    public bool useVirtualCursor = true;
    [Tooltip("ความไวของจุดเล็งเสมือน")]
    public float mouseSensitivity = 1.5f;
    [Tooltip("วาด crosshair ที่จุดเล็งเสมือน ให้เห็นตลอดว่ามือกำลังจะไปไหน")]
    public bool showCrosshair = true;

    // จุดเล็งเสมือน normalized [-1,1] — static เพื่อให้มือสองข้างแชร์จุดเดียวกัน
    // ⚠️ ยังต้องอัปเดตต่อไปแม้ระบบมือจะเลิกใช้แล้ว — GunHeldItem (เล็งปืน) และ
    //    PlayerFootForRobot (โหมด fallback ตอนไม่ได้ล็อกเคอร์เซอร์) อ่านผ่าน AimNormalized
    protected static Vector2 sharedVirtualCursor = Vector2.zero;
    private static int _aimUpdateFrame = -1;
    // ✅ [World-Anchored 3D Aim] จุดเล็งมือ = offset "3 แกนเต็ม" จากหัวไหล่ ในแกนโลก
    // เก็บเป็นแกนโลกเพราะ: หมุนกล้อง = ไม่มี delta = offset ไม่ขยับ = มือค้างอยู่จุดเดิมในโลก
    // ส่วน input ของเมาส์ถูก "ตีความ" ด้วยแกนกล้อง ณ วินาทีที่ขยับ — ปล่อยคลิกขวาแล้ว
    // เมาส์ครั้งถัดไปจึงคุมต่อจาก offset เดิมด้วยมุมกล้องใหม่ ไม่มี snap/reset/กระชาก
    protected static Vector3 sharedAimOffsetWorld = Vector3.down;
    private static int _cursorUpdateFrame = -1;   // กันมือสองข้างบวก delta ซ้ำในเฟรมเดียว
    private static int _crosshairDrawFrame = -1;  // กันวาด crosshair ซ้อนสองอัน
    private static bool _everLocked = false;
    private static GUIStyle _crosshairStyle;
    // คลิกซ้ายที่ใช้ดึงเคอร์เซอร์กลับเข้าเกม ห้ามถูกตีความเป็นหมัด (ดู HandleCursorLock)
    protected bool ignoreClickUntilRelease = false;

    protected bool isGrabbing = false;
    protected Rigidbody grabbedObject;
    protected FixedJoint grabJoint;
    public bool HasSupportingGrab =>
        currentState.Value == HandState.Attached &&
        isGrabbing && grabbedObject != null && grabbedObject.isKinematic && grabJoint != null;
    private readonly Dictionary<Joint, Vector2> protectedArmJointLimits =
        new Dictionary<Joint, Vector2>();
    // ✅ เก็บมวลดั้งเดิมของท่อนแขน — stabilizeArmChain เคยเขียนทับมวลถาวรตั้งแต่จับปีนครั้งแรก
    private readonly Dictionary<Rigidbody, float> originalArmMasses =
        new Dictionary<Rigidbody, float>();
    // ⚡ เดินโซ่ joint เฉพาะตอนมีอะไรต้องเปลี่ยนจริง — เดิม GetComponents ทุก FixedUpdate = GC ฟรีๆ
    private bool _armJointsProtected = false;
    private bool _armChainStabilized = false;

    protected Vector3 targetHandPosition;
    protected Vector3 smoothedHandTarget;
    protected bool smoothedHandInitialized = false;
    protected Vector3 _smoothVelocityRef = Vector3.zero; // ความเร็วสำหรับ SmoothDamp

    // ── Grab (Hold F) ──────────────────────────────────────────────────
    // 🤝 เดิมเป็น toggle (กด F ติด / กด F อีกทีปล่อย) ผู้เล่นต้องจำสถานะเอง
    // ใหม่: "ยังกด F อยู่ = ยังจับอยู่" ตรงไปตรงมา ไม่ต้องจำอะไร
    private bool grabHeld = false;     // ผู้เล่นกด F ค้างอยู่ไหม (input ล้วน)
    private bool grabActive = false;   // server ยืนยันว่าจับติดแล้วไหม
    private float nextGrabRetryTime = 0f;
    [Header("Grab Input")]
    [Tooltip("กด F ค้างแล้วยังไม่มีอะไรให้จับ → ลองใหม่ทุกๆ กี่วินาที\n" +
             "มีไว้ให้ 'ค้าง F ไว้แล้วขยับมือเข้าไปหาวัตถุ' จับติดเองได้ โดยไม่สแปม RPC ทุกเฟรม")]
    [Min(0.05f)]
    public float grabRetryInterval = 0.2f;

    protected Vector3 lastSentTarget;
    protected const float RPC_SEND_THRESHOLD = 0.05f;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // ✅ [Rest Pose] กันแขนสะบัดไปหา (0,0,0) ในเฟรมแรก:
        // ใช้ตำแหน่งจริงของมือตอน spawn (ท่าขนาบลำตัวตาม prefab) เป็นเป้าหมายเริ่มต้น
        // แขนจึงนิ่งอยู่ข้างลำตัวตั้งแต่เฟรมแรก จนกว่า Owner จะเริ่มขยับเมาส์จริง
        Vector3 restPose = handRb != null
            ? handRb.position
            : PivotPosition + Vector3.down * (maxArmLength * 0.5f);

        // 🎯 ตั้งช่วงความลึก + จุดเล็ง 3D เริ่มต้นจากท่ามือจริงตอน spawn
        ApplyAutoHandDepth();
        if (IsOwner) SeedAimFromCurrentHandPose();

        targetHandPosition = restPose;
        smoothedHandTarget = restPose;
        _smoothVelocityRef = Vector3.zero;
        smoothedHandInitialized = true;
        lastSentTarget = restPose;

        if (IsServer && handRb != null)
        {
            if (torso != null) torso.RegisterHand(this);
            // ✅ [Solver Boost] โซ่ข้อต่อแขนแก้สมการยากกว่า rigidbody เดี่ยว
            // เพิ่มรอบ solver เฉพาะมือ → ข้อต่อนิ่งขึ้นตอนรับแรงสูง (ต่อย/เหวี่ยงเร็ว)
            handRb.solverIterations = 12;
            handRb.solverVelocityIterations = 4;

            // ✅ [No Self-Collision] มือไม่ชนชิ้นส่วนหุ่นตัวเอง
            // มือครูดลำตัว/ขา/มืออีกข้าง = แหล่งแรงสั่นและหมัดสะดุดที่ใหญ่ที่สุด
            if (ignoreSelfCollision)
            {
                Collider[] handCols = handRb.GetComponentsInChildren<Collider>();
                Collider[] bodyCols = transform.root.GetComponentsInChildren<Collider>(true);
                foreach (var hc in handCols)
                    foreach (var bc in bodyCols)
                        if (hc != bc) Physics.IgnoreCollision(hc, bc, true);
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && torso != null) torso.UnregisterHand(this);
        // คืนเคอร์เซอร์ให้ระบบเมื่อหุ่นหายจากเกม (กลับเมนู/ตาย) — ไม่งั้นคลิกเมนูไม่ได้
        ReleaseCursorIfOwner();
        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        // Safety net: ถ้า object ถูกทำลายโดยไม่ผ่าน despawn ปกติ (unload scene ตรงๆ)
        // เคอร์เซอร์ต้องไม่ล็อกค้างถึงหน้าเมนู — แบบเดียวกับฝั่งเท้า
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

    protected virtual void Update()
    {
        if (!IsOwner || playerCamera == null) return;
        HandleInput();
    }

    protected virtual void FixedUpdate()
    {
        if (!IsServer) return;
        if (handRb == null) return; // 🛡️ กัน NRE ถ้า ref หลุด/ถูก despawn (แบบเดียวกับเท้า)
        SmoothHandTarget();

        if (currentState.Value == HandState.Attached)
        {
            PerformArmMovement();
        }
    }

    private void SmoothHandTarget()
    {
        // SmoothDamp: คม/แม่น — smoothTime สั้น ตอบสนองไว มีเบรกในตัวกัน overshoot
        if (!smoothedHandInitialized)
        {
            smoothedHandTarget = targetHandPosition;
            _smoothVelocityRef = Vector3.zero;
            smoothedHandInitialized = true;
        }
        else
        {
            smoothedHandTarget = Vector3.SmoothDamp(
                smoothedHandTarget, targetHandPosition, ref _smoothVelocityRef, smoothTime, Mathf.Infinity, Time.fixedDeltaTime);
        }
    }

    private static Vector2 GetAbsoluteMouseNormalized()
    {
        return new Vector2(
            (Mathf.Clamp(Input.mousePosition.x, 0, Screen.width) / Screen.width) * 2f - 1f,
            (Mathf.Clamp(Input.mousePosition.y, 0, Screen.height) / Screen.height) * 2f - 1f
        );
    }

    // ✅ จุดเล็ง normalized [-1,1] สำหรับระบบอื่นทั้งเกม (เช่น PlayerFootForRobot)
    // ตอนเคอร์เซอร์ถูกล็อก Input.mousePosition จะค้างกลางจอ — ต้องอ่านผ่านตัวนี้แทนเสมอ
    public static Vector2 AimNormalized =>
        Cursor.lockState == CursorLockMode.Locked ? sharedVirtualCursor : GetAbsoluteMouseNormalized();

    private Vector2 GetNormalizedMousePosition()
    {
        // ✅ [Virtual Cursor] เคอร์เซอร์จริงถูกล็อกกลางจอ → อ่าน delta มาขยับจุดเล็งเสมือน
        // ไม่มีขอบจอมาจำกัดการเลื่อนอีกต่อไป (แบบเดียวกับเกม FPS)
        if (useVirtualCursor && Cursor.lockState == CursorLockMode.Locked)
        {
            // ✅ คลิกขวาค้าง = โหมดหมุนกล้อง — delta เป็นของกล้อง ห้ามลากจุดเล็งตาม
            if (Time.frameCount != _cursorUpdateFrame && !Input.GetMouseButton(1))
            {
                _cursorUpdateFrame = Time.frameCount;
                Vector2 delta = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
                sharedVirtualCursor += delta * (mouseSensitivity * MouseSettings.Multiplier * 0.1f);
                sharedVirtualCursor.x = Mathf.Clamp(sharedVirtualCursor.x, -1f, 1f);
                sharedVirtualCursor.y = Mathf.Clamp(sharedVirtualCursor.y, -1f, 1f);
            }
            return sharedVirtualCursor;
        }

        // โหมดเดิม: ตำแหน่งเมาส์จริงบนจอ
        return GetAbsoluteMouseNormalized();
    }

    /// <summary>ตั้งช่วงความลึกมือให้สัมพันธ์กับความยาวแขนจริง</summary>
    private void ApplyAutoHandDepth()
    {
        if (!autoHandDepthFromArmLength) return;

        maxHandDepth = Mathf.Max(0.1f, maxArmLength * ARM_DEPTH_SAFETY);
        minHandDepth = Mathf.Clamp(maxArmLength * 0.12f, 0.05f, maxHandDepth - 0.05f);
    }

    /// <summary>
    /// ตั้งจุดเล็งจากท่ามือจริง ณ วินาทีที่เรียก — ใช้ตอน spawn / ล็อกเคอร์เซอร์ / respawn
    /// มือจึงไม่เคยกระโดดตำแหน่งเวลาสลับเข้า-ออกโหมดควบคุม
    /// </summary>
    private void SeedAimFromCurrentHandPose()
    {
        Vector3 handOffset = (handRb != null ? handRb.position : PivotPosition) - PivotPosition;

        if (handOffset.sqrMagnitude < 0.0001f)
            handOffset = Vector3.down * Mathf.Lerp(minHandDepth, maxHandDepth, defaultHandDepth);

        float depth = Mathf.Clamp(handOffset.magnitude, minHandDepth, maxHandDepth);
        sharedAimOffsetWorld = handOffset.normalized * depth;
    }

    private void LockVirtualCursor()
    {
        // เริ่มจุดเล็งจากตำแหน่งมือจริง ณ ตอนล็อก — มือไม่กระโดด
        sharedVirtualCursor = GetAbsoluteMouseNormalized();
        SeedAimFromCurrentHandPose();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        _everLocked = true;
    }

    private void HandleCursorLock()
    {
        // 🏁 เกมจบแล้ว (Win/GameOver panel ขึ้น) — ห้ามล็อกเมาส์กลับ ผู้เล่นต้องคลิกปุ่มบน panel
        if (GameFlowManager.GameEnded) return;

        // 🖱️ มี UI เปิดอยู่ (วงล้อไอเทม/เมนู) — ห้ามแย่งเมาส์กลับ
        // ไม่มีบรรทัดนี้ พอกดปุ่มแรกบน UI เคอร์เซอร์จะหายทันทีเพราะเข้าเงื่อนไข "คลิกซ้าย = ล็อกกลับ" ข้างล่าง
        if (UiFocus.IsCaptured) return;

        // Esc = ปลดล็อกชั่วคราว (ไปกดเมนู) / คลิกซ้ายกลับเข้าเกม = ล็อกต่อ
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (Cursor.lockState != CursorLockMode.Locked &&
                 (!_everLocked || Input.GetMouseButtonDown(0)))
        {
            LockVirtualCursor();
            // ⚠️ คลิกนี้ใช้ "ดึงเมาส์กลับเข้าเกม" ห้ามไปนับเป็นหมัด (LMB = Punch แล้ว)
            // ต้องปล่อยคลิกซ้ายก่อน ถึงจะต่อยครั้งใหม่ได้ — คลาสลูกอ่านธงนี้
            ignoreClickUntilRelease = true;
        }
    }

    private void OnGUI()
    {
        if (!IsOwner || !useVirtualCursor || !showCrosshair) return;
        if (Cursor.lockState != CursorLockMode.Locked) return;
        if (Time.frameCount == _crosshairDrawFrame) return; // มือเดียววาดพอ
        _crosshairDrawFrame = Time.frameCount;

        if (_crosshairStyle == null)
            _crosshairStyle = new GUIStyle(GUI.skin.label)
            { fontSize = 30, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };

        // ✅ crosshair ฉายจากตำแหน่งจุดเล็งจริงในโลก — หันกล้องแล้ว crosshair
        // เลื่อนบนจอตามจุดเดิมในโลก (feedback ตรงกับพฤติกรรมมือ)
        if (playerCamera == null) return;
        Vector3 screenPos = playerCamera.WorldToScreenPoint(PivotPosition + sharedAimOffsetWorld);
        if (screenPos.z <= 0f) return; // จุดเล็งอยู่หลังกล้อง ไม่ต้องวาด

        float sx = screenPos.x;
        float sy = Screen.height - screenPos.y; // แกน y ของ GUI กลับด้าน

        GUI.color = new Color(0f, 0f, 0f, 0.6f); // เงาให้อ่านออกบนฉากสว่าง
        GUI.Label(new Rect(sx - 19f, sy - 19f, 40f, 40f), "+", _crosshairStyle);
        GUI.color = Color.white;
        GUI.Label(new Rect(sx - 20f, sy - 20f, 40f, 40f), "+", _crosshairStyle);
    }

    protected virtual void HandleInput()
    {
        if (currentState.Value != HandState.Attached) return;

        // ✅ [Pointer Lock] จัดการล็อก/ปลดล็อกเคอร์เซอร์ (เฉพาะโหมด Virtual Cursor)
        if (useVirtualCursor) HandleCursorLock();

        // เคอร์เซอร์ปลดอยู่ (กด Esc ไปเมนู) → หยุดรับ input มือทั้งหมด
        // ✅ [Stuck-State Fix] ต้องปล่อย grab ที่ค้างอยู่ก่อนหยุดรับ input ด้วย
        // ระบบ Hold F อาศัยการเห็น GetKeyUp เป็นตัวปล่อย — ถ้าผู้เล่นกด Esc ค้าง F ไว้
        // แล้วปล่อยนิ้วตอนอยู่ในเมนู เราจะไม่มีวันเห็น KeyUp นั้น = มือจับค้างถาวร
        // จนกว่าจะกด F ลง-ขึ้นใหม่อีกรอบ
        if (useVirtualCursor && Cursor.lockState != CursorLockMode.Locked)
        {
            if (grabHeld)
            {
                grabHeld = false;
                grabActive = false;
                ReleaseGrabRpc();
            }
            return;
        }

        // ✅ [World-Anchored 3D Aim — ระบบเดียว]
        // เมาส์ = ลากมือบนระนาบหน้าจอ (X → ซ้าย/ขวา, Y → ขึ้น/ลง)
        // ล้อเมาส์ = ยื่นออก/ดึงเข้าตามแนวไหล่→มือ
        // ทั้งหมดสะสมลง sharedAimOffsetWorld ซึ่งเป็น "แกนโลก" → หมุนกล้องแล้วมือไม่กวาดตาม
        GetNormalizedMousePosition(); // อัปเดต virtual cursor ให้ GunHeldItem/ระบบขาใช้ผ่าน AimNormalized

        UpdateHandAim();

        Vector3 newTarget = PivotPosition + sharedAimOffsetWorld;

        // ✅ กันมือทะลุพื้น (คงไว้เหมือนเดิม)
        if (Physics.Raycast(newTarget + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 5f, groundLayer))
            if (newTarget.y < hit.point.y) newTarget.y = hit.point.y;

        if (Vector3.Distance(lastSentTarget, newTarget) > RPC_SEND_THRESHOLD)
        { lastSentTarget = newTarget; UpdateHandTargetRpc(newTarget); }

        HandleGrabInput();

        if (torso != null && torso.currentState.Value == TorsoMovement.TorsoState.Ragdoll && Input.GetKeyDown(KeyCode.Q))
            ApplyHandRecoveryRpc();
    }

    /// <summary>
    /// เมาส์ = ระนาบหน้าจอ (2 แกน) | ล้อ = ความลึกไหล่→มือ (แกนที่ 3)
    ///
    /// ⚠️ จุดสำคัญ: delta ของเมาส์ถูก "ตีความ" ด้วยแกนกล้อง ณ เฟรมนั้น แล้วบวกสะสมลง
    /// เวกเตอร์แกนโลก — ไม่ได้เก็บเป็นพิกัดกล้อง ผลคือ
    ///   • หมุนกล้อง (ไม่มี delta) → offset ไม่เปลี่ยน → มือค้างที่ world position เดิมเป๊ะ
    ///   • ปล่อยคลิกขวาแล้วขยับเมาส์ต่อ → คุมต่อจาก offset เดิม ด้วยมุมกล้องใหม่ ไม่มีกระชาก
    /// </summary>
    /// <summary>
    /// ตัวคูณความไวตามระยะกล้อง — 1.0 พอดีที่ aimReferenceCameraDistance
    /// ใกล้กว่านั้น &lt; 1 (ละเอียดขึ้น) / ไกลกว่านั้น &gt; 1 (กวาดได้กว้างขึ้น)
    /// เพื่อให้ระยะที่มือขยับ "บนจอ" เท่าเดิมเสมอไม่ว่าซูมเข้าออกแค่ไหน
    /// </summary>
    private float CameraDistanceGain()
    {
        if (!scaleAimWithCameraDistance || playerCamera == null) return 1f;
        float camDist = Vector3.Distance(playerCamera.transform.position, PivotPosition);
        return camDist / Mathf.Max(0.1f, aimReferenceCameraDistance);
    }

    private void UpdateHandAim()
    {
        // มือสองข้างแชร์จุดเล็งเดียวกัน — กันบวก delta ซ้ำสองรอบในเฟรมเดียว
        if (Cursor.lockState != CursorLockMode.Locked || Time.frameCount == _aimUpdateFrame)
            return;
        _aimUpdateFrame = Time.frameCount;

        // 🎥 คลิกขวาค้าง = โหมดกล้องเต็มตัว: เมาส์และล้อเป็นของกล้องล้วน
        // ห้ามแตะ offset แนวนอน/แนวตั้ง/ความลึก และห้ามจอง MouseWheelFocus
        if (Input.GetMouseButton(1)) return;

        Transform camT = playerCamera.transform;

        // ── ระนาบหน้าจอ: ใช้แกน right/up ของกล้องตรงๆ (ไม่แบนลงพื้น)
        // มือจึงขยับตรงกับสิ่งที่ตาเห็นบนจอจริงๆ แม้กล้องจะก้ม/เงย
        Vector2 md = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) *
                     (mouseSensitivity * MouseSettings.Multiplier * 0.1f);

        // ✅ [Screen-Constant Aim] ความไวเดิมคิดเป็น "เมตรในโลก" ตายตัว ไม่สนระยะกล้อง
        // ผลคือกล้องใกล้ = มือกวาดข้ามจอด้วยการขยับเมาส์นิดเดียว (คุมไม่อยู่)
        // แต่กล้องไกล = ระยะเท่ากันในโลกกินพื้นที่จอน้อยลง (กำลังดี)
        // สเกลด้วยระยะกล้องจริง → "มือขยับกี่พิกเซลบนจอ" คงที่ทุกระดับซูม
        float aimGain = CameraDistanceGain();
        sharedAimOffsetWorld += (camT.right * (md.x * mouseReachX) +
                                 camT.up    * (md.y * mouseReachY)) * aimGain;

        // ── ล้อเมาส์: ยื่นออก/ดึงเข้าตามแนวไหล่→มือ
        // จงใจไม่ใช้ camera forward — ไม่งั้นพอหมุนกล้อง "ความลึก" จะชี้ไปคนละทิศ
        // แล้วมือจะเหวี่ยงตามกล้อง = ผิดกฎ World-Anchored ข้อ 3
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.0001f && sharedAimOffsetWorld.sqrMagnitude > 0.0001f)
        {
            float notches = scroll / SCROLL_NOTCH;
            // สเกลตามระยะกล้องเหมือนแกนอื่น ไม่งั้นความลึกจะไวไม่เท่าแนวนอน/แนวตั้ง
            float step = notches * handDepthScrollSpeed * (maxHandDepth - minHandDepth) * aimGain;
            sharedAimOffsetWorld += sharedAimOffsetWorld.normalized * step;
        }

        // จองล้อทุกเฟรมที่มือถือสิทธิ์อยู่ (ไม่ใช่เฉพาะเฟรมที่หมุนจริง) — ธงจะได้นิ่ง
        // ไม่กะพริบจนกล้องแอบซูมแทรกระหว่างคลิกล้อสองครั้ง
        MouseWheelFocus.Claim();

        // ── Clamp ก้อนเดียวจบ ──
        // เมาส์ระนาบหน้าจอ + ล้อความลึก รวมกันแล้วอาจเลยระยะที่แขนเอื้อมถึง
        // ดึงทั้งเวกเตอร์กลับเข้าช่วง [minHandDepth, maxHandDepth] โดยคงทิศเดิมไว้
        float depth = sharedAimOffsetWorld.magnitude;
        if (depth < 0.0001f)
        {
            // เป้าทับหัวไหล่พอดี — ทิศหายไปแล้ว ดันกลับลงล่างเป็นท่าพัก
            sharedAimOffsetWorld = Vector3.down * minHandDepth;
            return;
        }

        float clampedDepth = Mathf.Clamp(depth, minHandDepth, maxHandDepth);
        if (!Mathf.Approximately(depth, clampedDepth))
            sharedAimOffsetWorld *= clampedDepth / depth;
    }

    /// <summary>
    /// 🤝 [Hold F = Grab] "ยังกด F อยู่ = ยังจับอยู่" — ปล่อย F = ปล่อยทันที
    /// ถ้ากดแล้วยังไม่มีอะไรให้จับ จะลองใหม่เป็นระยะ (grabRetryInterval) ไม่ใช่ทุกเฟรม
    /// ผู้เล่นจึงค้าง F ไว้แล้วขยับมือเข้าไปหาวัตถุให้มันจับติดเองได้ โดยไม่สแปม RPC
    /// </summary>
    private void HandleGrabInput()
    {
        bool torsoDown = torso != null &&
            (torso.currentState.Value == TorsoMovement.TorsoState.Ragdoll ||
             torso.currentState.Value == TorsoMovement.TorsoState.Falling);

        if (Input.GetKeyDown(KeyCode.F))
        {
            grabHeld = true;
            nextGrabRetryTime = 0f; // ขอทันทีในเฟรมนี้เลย
        }

        if (Input.GetKeyUp(KeyCode.F) && grabHeld)
        {
            grabHeld = false;
            grabActive = false;
            // ส่งเสมอ (ไม่ใช่เฉพาะตอน grabActive) — กันเคสที่ RPC ตอบกลับยังไม่ถึงเรา
            // แต่ server จับติดไปแล้ว | ServerReleaseGrab ปลอดภัยแม้ไม่มีอะไรถูกจับอยู่
            ReleaseGrabRpc();
            return;
        }

        if (!grabHeld || grabActive) return;

        // ตัวล้มอยู่ = เริ่มจับใหม่ไม่ได้ (กฎเดิมของ TryGrabRpc ฝั่ง server)
        // เช็คซ้ำฝั่ง client เพื่อไม่ให้ยิง RPC ที่รู้ผลอยู่แล้วว่าถูกปฏิเสธ
        if (torsoDown) return;

        if (Time.time < nextGrabRetryTime) return;
        nextGrabRetryTime = Time.time + grabRetryInterval;
        TryGrabRpc();
    }
    
    [Rpc(SendTo.Server)] private void UpdateHandTargetRpc(Vector3 target) { ValidateAndSetHandTarget(target); }
    [Rpc(SendTo.Server)] private void ApplyHandRecoveryRpc() { if (torso != null) torso.ApplyContinuousRecoveryForce(PivotPosition); }

    // 🛡️ [Server Validation] แบบเดียวกับ ValidateAndSetFootTarget ฝั่งขา —
    // NaN หลุดเข้ามาพังทั้งโซ่ฟิสิกส์แขน / พิกัดไกลเกินก็ถูกดึงกลับเข้าระยะเล็งจริง
    private const float SERVER_REACH_MARGIN = 1.5f;
    private void ValidateAndSetHandTarget(Vector3 target)
    {
        if (!target.IsValid()) return;

        // 🛡️ เพดานอิงระยะที่แขนเอื้อมถึงจริง ไม่ใช่ค่าความไวเมาส์อีกต่อไป
        // (เดิมใช้ max ของ mouseReachX/Y/Depth ซึ่งเป็นสเกลความไว ไม่ได้แปลว่าระยะแขน
        //  ปรับความไวขึ้นเมื่อไหร่เพดาน validate ก็หลวมตามไปด้วยโดยไม่มีใครตั้งใจ)
        float limit = Mathf.Max(maxHandDepth, maxArmLength) * SERVER_REACH_MARGIN;

        Vector3 pivot = PivotPosition;
        Vector3 dir = target - pivot;
        if (dir.magnitude > limit) target = pivot + dir.normalized * limit;

        targetHandPosition = target;
    }

    [Rpc(SendTo.Server)]
    private void TryGrabRpc()
    {
        // Server authority: do not allow a client to start a new grab while ragdolled.
        if (torso != null &&
            (torso.currentState.Value == TorsoMovement.TorsoState.Ragdoll ||
             torso.currentState.Value == TorsoMovement.TorsoState.Falling))
        {
            ForceReleaseGrabClientRpc();
            return;
        }

        // 🛡️ กันจับซ้อน — ถ้า RPC เข้ามาซ้ำตอนมี joint อยู่แล้ว FixedJoint ตัวใหม่จะทับ field
        // แต่ตัวเก่ายังเกาะ handRb ถาวร = ของที่เคยจับติดมือตลอดกาล
        if (isGrabbing || grabJoint != null || handRb == null) return;

        Collider[] hits = Physics.OverlapSphere(GrabPosition, grabRadius, grabLayer);
        bool grabbedSomething = false;

        foreach (var h in hits)
        {
            Rigidbody rb = h.attachedRigidbody;
            if (rb == null) continue;
            isGrabbing = true;
            grabbedObject = rb;
            grabbedSomething = true;

            // ✅ จับได้ทั้ง Kinematic และ Dynamic
            grabJoint = handRb.gameObject.AddComponent<FixedJoint>();
            grabJoint.connectedBody = rb;
            float jointBreakLimit = preventGrabBreakWhileRagdoll
                ? Mathf.Infinity
                : grabBreakForce;
            grabJoint.breakForce = jointBreakLimit;
            grabJoint.breakTorque = jointBreakLimit;

            // ปิดการชนระหว่างของที่ถูกจับกับลำตัวหุ่น เพื่อป้องกันบั๊กบินขึ้นฟ้า
            IgnoreCollisionWithTorso(grabbedObject, true);

            break;
        }

        // แจ้งผล owner เสมอ — สำเร็จ = หยุด retry / พลาด = ปล่อยให้ retry ต่อขณะยังกด F ค้าง
        // (เดิมแจ้งเฉพาะตอนพลาด เพราะระบบ toggle ไม่ต้องรู้ว่าสำเร็จเมื่อไหร่)
        if (grabbedSomething)
            GrabSucceededClientRpc();
        else
            ForceReleaseGrabClientRpc();
    }

    [Rpc(SendTo.Owner)]
    private void GrabSucceededClientRpc()
    {
        grabActive = true;
    }

    [Rpc(SendTo.Server)]
    private void ReleaseGrabRpc() => ServerReleaseGrab();

    /// <summary>
    /// ปล่อย grab ฝั่ง server — แยกออกจาก RPC เพื่อให้ระบบอื่น (เช่น RespawnManager)
    /// สั่งปล่อยได้โดยตรง พร้อมแจ้ง owner ให้รีเซ็ต toggle F ด้วยเสมอ
    /// </summary>
    public void ServerReleaseGrab()
    {
        if (!IsServer) return;

        isGrabbing = false;
        if (grabJoint != null)
        {
            Destroy(grabJoint);
            grabJoint = null;
        }

        if (grabbedObject != null)
        {
            IgnoreCollisionWithTorso(grabbedObject, false);
            grabbedObject = null;
        }

        if (IsSpawned) ForceReleaseGrabClientRpc();
    }

    /// <summary>
    /// เรียกจาก RespawnManager (ฝั่ง server) หลัง teleport ร่างกลับเช็คพอยต์
    /// ปล่อย grab + รีเซ็ตเป้ามือเป็นตำแหน่งปัจจุบัน กันสปริงไล่เป้าเก่าที่จุดตกเหว
    /// </summary>
    public void ResetForRespawn()
    {
        if (!IsServer) return;

        ServerReleaseGrab();

        Vector3 rest = handRb != null ? handRb.position : PivotPosition;
        targetHandPosition = rest;
        smoothedHandTarget = rest;
        _smoothVelocityRef = Vector3.zero;

        // Owner ถือจุดเล็ง/สถานะปุ่มไว้ในเครื่องตัวเอง ต้องสั่งล้างด้วย ไม่งั้นเป้าเก่า
        // (ที่จุดตกเหว) จะถูกส่งกลับมาทับทันทีในเฟรมถัดไป
        if (IsSpawned) ResetAimOnOwnerRpc();
    }

    [Rpc(SendTo.Owner, Delivery = RpcDelivery.Reliable)]
    private void ResetAimOnOwnerRpc()
    {
        grabHeld = false;
        grabActive = false;
        ignoreClickUntilRelease = true; // ต้องปล่อยคลิกซ้ายก่อน ถึงจะต่อยครั้งใหม่ได้

        ApplyAutoHandDepth();
        SeedAimFromCurrentHandPose();

        // ล้างแคชกันส่งซ้ำ — เป้าใหม่หลัง teleport ต้องถูกส่งทันทีแม้บังเอิญใกล้ค่าเดิม
        lastSentTarget = Vector3.positiveInfinity;
    }

    private void IgnoreCollisionWithTorso(Rigidbody targetRb, bool ignore)
    {
        if (targetRb == null || torso == null) return;
        Collider[] targetCols = targetRb.GetComponentsInChildren<Collider>();
        Collider[] torsoCols = torso.GetComponentsInChildren<Collider>();
        foreach (var tc in torsoCols)
        {
            foreach (var gc in targetCols)
            {
                Physics.IgnoreCollision(tc, gc, ignore);
            }
        }
    }

    void OnJointBreak(float breakForce)
    {
        Debug.Log($"Hand joint broke due to massive force: {breakForce}");
        isGrabbing = false;
        
        if (grabbedObject != null)
        {
            IgnoreCollisionWithTorso(grabbedObject, false);
            grabbedObject = null;
        }
        
        if (IsServer)
        {
            ForceReleaseGrabClientRpc();
        }
    }

    [Rpc(SendTo.Owner)]
    private void ForceReleaseGrabClientRpc()
    {
        // จับไม่ติด / ถูกสั่งปล่อยจากฝั่ง server (respawn, joint แตก, ล้ม)
        // ⚠️ ไม่แตะ grabHeld — ผู้เล่นยังกด F ค้างอยู่ก็ให้ retry ต่อได้ตามข้อกำหนด
        grabActive = false;
    }

    protected virtual void PerformArmMovement()
    {
        // ✅ [Ragdoll Limp] ตอนตัวล้ม มือยังขยับตามเมาส์ได้ (ช่วยพยุง/ดันตัวตอนลุก)
        // แต่ "ห้ามดึงลำตัว" — ตัด torsoPull ทิ้งไม่ให้แขนโกงลากตัวไหลตอนล้ม
        bool torsoDown = torso != null &&
            (torso.currentState.Value == TorsoMovement.TorsoState.Ragdoll ||
             torso.currentState.Value == TorsoMovement.TorsoState.Falling);
        bool hasActiveGrab = isGrabbing && grabbedObject != null && grabJoint != null;

        // Starting a new grab while down is blocked, so this can only protect a grab
        // that already existed before ragdoll. Restore the normal limit after recovery.
        if (hasActiveGrab)
        {
            float activeBreakForce = preventGrabBreakWhileRagdoll
                ? Mathf.Infinity
                : grabBreakForce;
            grabJoint.breakForce = activeBreakForce;
            grabJoint.breakTorque = activeBreakForce;
        }

        // When enabled, physics can never tear the arm chain apart. Explicit systems
        // may still detach a limb by destroying its joint intentionally.
        ProtectArmJointChain(preventGrabBreakWhileRagdoll);

        Vector3 dirFromPivot = smoothedHandTarget - PivotPosition;
        float currentDistance = dirFromPivot.magnitude;
        Vector3 physicsTarget = smoothedHandTarget;

        // ✅ ระยะเอื้อมจริง = ค่าฐาน + ระยะพิเศษชั่วคราว (เช่นตอนปล่อยหมัด)
        float reachLimit = maxArmLength + extraReach;

        if (isGrabbing && grabbedObject != null && grabbedObject.isKinematic)
        {
            // ปีนป่าย
            Vector3 climbPullDir = physicsTarget - GrabPosition;
            torso.torsoRb.AddForce(climbPullDir * kinematicPullForce, ForceMode.Acceleration);

            if (kinematicGrabAddsStress)
            {
                float stressThisFrame = kinematicPullForce * Time.fixedDeltaTime * Mathf.Clamp01(currentDistance / maxArmLength);
                torso.AddStress(stressThisFrame);
            }
            torso.armPullIntensity = Mathf.Clamp01(currentDistance / maxArmLength);
        }
        else
        {
            torso.armPullIntensity = 0f;

            if (currentDistance < 0.05f)
            {
                physicsTarget = PivotPosition + (dirFromPivot.normalized * 0.05f);
            }
            else if (currentDistance > reachLimit)
            {
                // ✅ [Soft Clamp] เดิม clamp แข็งที่ผิวทรงกลม → เป้าหมายฟิสิกส์กระตุกทันทีที่ชนขอบ
                // ทำให้สปริง "เด้งกลับ" ตอนยืดสุดแขน
                // ใหม่: ระยะส่วนเกินถูกบีบแบบ asymptotic (ยอมเกินได้สูงสุด ~0.4m แบบนุ่มนวล)
                float excess = currentDistance - reachLimit;
                float softExcess = excess / (1f + excess * 2.5f);
                physicsTarget = PivotPosition + (dirFromPivot / currentDistance) * (reachLimit + softExcess);

                Vector3 pullDir = dirFromPivot / currentDistance;

                // ✅ [Ragdoll No-Pull] ตอนล้ม: มือยังยืดสุดได้ แต่ไม่ส่งแรงดึงลำตัว
                // กันแขนโกงลากตัวไหลไปมาตอน Ragdoll (ตัดเฉพาะแรง torsoPull ไม่แตะสปริงมือ)
                if (!torsoDown || hasActiveGrab)
                {

                // ── [Anti Hand-Skating] ────────────────────────────────────────
                // แยกแรงดึงออกเป็น แนวตั้ง (Y) และ แนวราบ (XZ)
                // แรงแนวราบถูก scale ลงตาม torsoPullHorizontalScale
                // และถูกลดเพิ่มเติมถ้าตัวกำลังเคลื่อนที่เร็วอยู่แล้วในทิศเดียวกัน
                Vector3 pullVertical   = new Vector3(0f, pullDir.y, 0f);
                Vector3 pullHorizontal = new Vector3(pullDir.x, 0f, pullDir.z);

                // วัดความเร็วตัวในแนวราบ
                Vector3 bodyHorizVel = torso.torsoRb.linearVelocity;
                bodyHorizVel.y = 0f;

                // ถ้าตัววิ่งในทิศเดียวกับแรงดึงอยู่แล้ว → ลดแรงแนวราบลง
                float velAlongPull = Vector3.Dot(bodyHorizVel, pullHorizontal.normalized);
                float horizScale   = torsoPullHorizontalScale *
                                     Mathf.Clamp01(1f - velAlongPull / Mathf.Max(maxAllowedHorizontalBoost, 0.1f));

                // [Audit Fix] ป้องกันไม่ให้แขนกดตัวเองจมพื้นเวลาล้มหรือกำลังพยายามลุก
                bool isRagdollOrFalling = torso.currentState.Value == TorsoMovement.TorsoState.Ragdoll || torso.currentState.Value == TorsoMovement.TorsoState.Falling;
                if (pullVertical.y < 0f && isRagdollOrFalling)
                {
                    pullVertical.y *= 0.1f; // ลดแรงกดลง 90%
                }

                Vector3 cappedPullDir = pullVertical + pullHorizontal * horizScale;
                torso.torsoRb.AddForceAtPosition(cappedPullDir * torsoPullForce, PivotPosition, ForceMode.Acceleration);
                // ──────────────────────────────────────────────────────────────

                float stressThisFrame = torsoPullForce * Time.fixedDeltaTime * 0.5f;
                torso.AddStress(stressThisFrame);
                } // end standing or holding a pre-ragdoll grab
            }

            // ── Spring + Stability ─────────────────────────────────────────
            Vector3 toTarget = physicsTarget - handRb.position;

            // ✅ [Ragdoll Droop] ตอนล้ม: สปริงอ่อนลง + ไม่ชดเชยแรงโน้มถ่วง
            // → มือห้อยตกตามแรงโน้มถ่วง/ร่วงตามตัว แต่ยังนัดตามเมาส์ได้เบาๆ (ขยับได้)
            // A grab that already existed before ragdoll keeps full arm strength.
            float springScale = torsoDown && !hasActiveGrab ? ragdollHandSpringScale : 1f;

            // ✅ [Velocity Cap] จำกัดความเร็วที่สปริงเรียกร้อง — เป้าไกลแค่ไหน
            // สปริงก็ขอความเร็วได้ไม่เกินเพดาน → แรงในโซ่ข้อต่อไม่ระเบิด ไม่สั่น
            float velCap = velocityCapOverride > 0f ? velocityCapOverride : maxHandVelocity;
            Vector3 velocityTarget = Vector3.ClampMagnitude(toTarget * (handMoveSpeed * springScale), velCap);
            Vector3 force = (velocityTarget - handRb.linearVelocity) * (handDamper * springScale);

            // ✅ [Gravity Compensation] ชดเชยน้ำหนักมือ "เฉพาะตอนยืน" — ตอนล้มปล่อยให้ตกจริง
            if (compensateGravity && handRb.useGravity && (!torsoDown || hasActiveGrab))
                force -= Physics.gravity;

            // ✅ [Proximity Brake] ใกล้เป้าแล้วเบรกความเร็วเพิ่มแบบ quadratic
            // → มีแรงเหวี่ยงเต็มที่ตอนไกล แต่หยุดนิ่งแม่นยำตอนถึง กัน Overshoot/ดีดกลับ
            float proximity = 1f - Mathf.Clamp01(toTarget.magnitude / Mathf.Max(brakeDistance, 0.01f));
            if (proximity > 0f)
                force -= handRb.linearVelocity * (brakeDamping * proximity * proximity);

            handRb.AddForce(force, ForceMode.Acceleration);
        }
    }

    private void ProtectArmJointChain(bool protect)
    {
        if (!protect)
        {
            foreach (var savedLimit in protectedArmJointLimits)
            {
                if (savedLimit.Key == null) continue;
                savedLimit.Key.breakForce = savedLimit.Value.x;
                savedLimit.Key.breakTorque = savedLimit.Value.y;
            }
            protectedArmJointLimits.Clear();
            _armJointsProtected = false;
            RestoreArmMasses();
            return;
        }

        bool stabilizing = stabilizeArmChainWhileGrabbing && HasSupportingGrab;

        // ✅ เลิกจับแล้ว → คืนมวลแขนเป็นค่าดั้งเดิม (เดิมมวลถูกเขียนทับถาวรตั้งแต่ปีนครั้งแรก
        // ฟิสิกส์แขน/น้ำหนักหมัดเปลี่ยนไปตลอดชีวิตโดยไม่มีใครรู้)
        if (!stabilizing)
        {
            RestoreArmMasses();
            _armChainStabilized = false;
        }

        // ⚡ ทุกอย่างถูกตั้งครบแล้วและไม่มีอะไรเปลี่ยน → ไม่ต้องเดินโซ่ซ้ำทุก tick
        if (_armJointsProtected && (!stabilizing || _armChainStabilized))
            return;

        Rigidbody currentBody = handRb;
        int safety = 0;
        bool foundAnyJoint = false;

        while (currentBody != null &&
               (torso == null || currentBody != torso.torsoRb) &&
               safety++ < 8)
        {
            if (stabilizing)
            {
                if (!originalArmMasses.ContainsKey(currentBody))
                    originalArmMasses.Add(currentBody, currentBody.mass);

                currentBody.mass = Mathf.Max(0.01f, stabilizedArmMass);
                currentBody.solverIterations = Mathf.Max(currentBody.solverIterations, stabilizedArmSolverIterations);
                currentBody.solverVelocityIterations = Mathf.Max(currentBody.solverVelocityIterations, stabilizedArmSolverVelocityIterations);
            }

            Joint[] armJoints = currentBody.GetComponents<Joint>();
            if (armJoints.Length == 0) break;

            Rigidbody nextBody = null;
            foreach (Joint armJoint in armJoints)
            {
                if (armJoint == null) continue;

                if (!protectedArmJointLimits.ContainsKey(armJoint))
                {
                    protectedArmJointLimits.Add(
                        armJoint,
                        new Vector2(armJoint.breakForce, armJoint.breakTorque));
                }

                armJoint.breakForce = Mathf.Infinity;
                armJoint.breakTorque = Mathf.Infinity;
                foundAnyJoint = true;

                // The runtime grab FixedJoint points toward the grabbed object, not
                // toward the torso, so never use it to continue the limb traversal.
                if (nextBody == null && armJoint != grabJoint && armJoint.connectedBody != null)
                    nextBody = armJoint.connectedBody;
            }

            currentBody = nextBody;
        }

        if (foundAnyJoint) _armJointsProtected = true;
        if (stabilizing) _armChainStabilized = true;
    }

    private void RestoreArmMasses()
    {
        if (originalArmMasses.Count == 0) return;

        foreach (var savedMass in originalArmMasses)
            if (savedMass.Key != null) savedMass.Key.mass = savedMass.Value;

        originalArmMasses.Clear();
    }

    private void OnDrawGizmosSelected()
    {
        if (pivotPoint != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(PivotPosition, maxArmLength);
        }

        if (handRb != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(GrabPosition, grabRadius);
        }
    }
}
