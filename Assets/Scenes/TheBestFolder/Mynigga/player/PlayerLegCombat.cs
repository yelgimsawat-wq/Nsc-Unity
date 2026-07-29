using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(PlayerFootForRobot), typeof(Rigidbody))]
public class PlayerLegCombat : NetworkBehaviour
{
    public enum LegActionState : byte
    {
        Idle,
        Charging,
        Kicking,
        Recovering
    }

    [Header("Input")]
    [SerializeField] private KeyCode kickKey = KeyCode.LeftShift;

    [Header("Charge Kick")]
    [Min(0.05f)]
    [SerializeField] private float fullChargeDuration = 1f;
    [Range(0f, 1f)]
    [SerializeField] private float minimumCharge = 0.12f;
    // เดิม 8-24 m/s / accel 120 — ตัวเลขนี้จูนมาสำหรับหุ่นสเกลเล็ก (maxLegLength ~1.5)
    // แต่หุ่นจริงถูกสเกลใหญ่ขึ้น ~9 เท่า (maxLegLength = 14) โดยไม่มีใครมาปรับแรงเตะตาม
    // เทียบกับหมัด (maxPunchSpeed=55, punchAcceleration=400 ใน PlayerHandCombat) แล้วเตะเบากว่ามาก
    // ค่าใหม่: เตะเบาสุดยังแรงกว่าหมัดเบาๆ / ชาร์จเต็มแรงกว่าหมัดสุด (รางวัลของการรอชาร์จ)
    [Min(0f)]
    [SerializeField] private float minimumKickSpeed = 30f;
    [Min(0f)]
    [SerializeField] private float maximumKickSpeed = 70f;
    // 1100 = แรงกว่าหมัด (600) เกือบเท่าตัว เพื่อให้ "ดีดออก" ไวหลังง้าง
    // เท้าถึงความเร็วสูงสุดใน ~0.08 วิ แทน ~0.14 วิ = จังหวะสะบัดคมขึ้นชัดเจน
    [Min(0f)]
    [SerializeField] private float kickAcceleration = 1100f;
    [Min(0.05f)]
    [SerializeField] private float kickDuration = 0.3f;
    // เท้าเร่งถึงความเร็วสูงสุดที่ระยะ ~5.25 m (คำนวณที่ dt 0.02 มวลประสิทธิผล 1.625 kg)
    // ตั้ง 6 = ตัดจบ "ตอนพีคพอดี" ขณะขายังอยู่ในกรวยที่เอื้อมถึง
    // ถ้าตั้งไกลกว่านี้ ครึ่งหลังของท่าจะกลายเป็นการดันขาสู้กับข้อต่อที่ล็อกแกนเลื่อนไว้
    [Min(0.1f)]
    [SerializeField] private float kickReach = 6f;
    [Min(0f)]
    [SerializeField] private float maxTorsoUpwardSpeedDuringKick = 0.75f;

    [Header("Kick Drive (สปริงแบบเดียวกับแขน)")]
    [Tooltip("อัตราแปลงระยะห่างจากเป้า → ความเร็วที่สปริงสั่ง = handMoveSpeed ของแขน")]
    [Min(0.1f)]
    [SerializeField] private float kickMoveSpeed = 25f;
    [Tooltip("ตัวหลักที่คุมความ 'แข็งเป็นหุ่นยนต์' — ยิ่งสูงยิ่งสปริงสู้ข้อต่อแรง\n" +
             "10 = ดีดคมขึ้นกว่า 8 เล็กน้อยแต่ยังไม่แข็งจนดูเป็นหุ่นยนต์\n" +
             "ถ้ารู้สึกแข็งไป ลดเป็น 8 หรือ 6 ก่อนไปแตะค่าอื่น")]
    [Min(0.1f)]
    [SerializeField] private float kickDamper = 10f;
    [Tooltip("ระยะจากสะโพกถึงเป้าที่สปริงวิ่งไล่ ต้องไกลกว่าความยาวขา (14) สปริงจะได้ไม่ผ่อนแรงกลางทาง\n" +
             "หลักเดียวกับหมัดที่ปักเป้าไว้ที่ maxArmLength + 2")]
    [Min(1f)]
    [SerializeField] private float kickTargetReach = 18f;

    [Header("Wind-Up (ง้างขาระหว่างชาร์จ)")]
    [Tooltip("ดึงเท้าถอยหลังระหว่างกด Shift ค้าง — ยิ่งค้างนานยิ่งง้างลึก\n" +
             "ได้ทั้งท่าที่อ่านออกว่ากำลังจะเตะ และเป็น feedback บอกระดับพลังไปในตัว")]
    [SerializeField] private bool enableWindup = true;
    [Tooltip("ระยะถอยหลังสูงสุดตอนชาร์จเต็ม (เมตร) — ยิ่งมากยิ่งง้างลึก เห็นท่าชัด")]
    [Min(0f)]
    [SerializeField] private float windupPullBack = 8f;
    [Min(0.1f)]
    [SerializeField] private float windupMoveSpeed = 9f;
    [Min(0.1f)]
    [SerializeField] private float windupDamper = 8f;
    [Tooltip("เพดานความเร็วตอนง้าง — ช้ากว่าตอนเตะมาก ให้อีกฝ่ายอ่านท่าออกทัน")]
    [Min(0.1f)]
    [SerializeField] private float maxWindupSpeed = 18f;

    [Header("Return To Movement")]
    [Min(0f)]
    [SerializeField] private float recoveryDuration = 0.18f;
    // สปีคสูงสุดขึ้นเกือบ 3 เท่า — ต้องหน่วงแรงขึ้นตามเพื่อไม่ให้เท้าค้างไถลหลังเตะจบ
    [Min(0f)]
    [SerializeField] private float recoveryDamping = 14f;

    [Header("Damaged Leg Power")]
    [Tooltip("Kick power retained when this leg has almost no HP. The actual power scales from this value to 100% using current HP / original MaxHp.")]
    [Range(0f, 1f)]
    [SerializeField] private float minimumPowerAtZeroHealth = 0.3f;

    [Header("Debug")]
    [Tooltip("โชว์ log สรุปทุกครั้งที่เตะจบ (ความเร็วพีค/ดาเมจที่จะได้/โดนเป้าไหม) ไว้เช็คจูนค่า\n" +
             "สไตล์เดียวกับ debugPunchLog ของหมัด — เตะวืดก็ยังโชว์ ไม่งั้นวัดแรงเตะจริงไม่ได้เลย")]
    [SerializeField] private bool debugKickLog = true;

    public readonly NetworkVariable<LegActionState> currentAction = new NetworkVariable<LegActionState>(
        LegActionState.Idle,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public LegActionState CurrentAction => currentAction.Value;
    public bool IsCharging => localCharging || currentAction.Value == LegActionState.Charging;
    public bool IsKickMotionActive =>
        currentAction.Value == LegActionState.Kicking ||
        currentAction.Value == LegActionState.Recovering;

    // ช่วงที่ "ท่าเตะเป็นเจ้าของเท้า" — กว้างกว่า IsKickMotionActive ตรงที่รวมตอนง้างด้วย
    // PlayerFootForRobot ใช้ตัวนี้ตัดสินใจว่าจะหยุดสปริงก้าวเดิน/ตัวล็อกยืนของมันเอง
    // ต้องแยกจาก IsKickMotionActive เพราะเพดาน maxFootVelocity ยังควรทำงานตอนง้าง (ง้างช้าอยู่แล้ว)
    public bool IsKickControllingFoot =>
        IsKickMotionActive ||
        (enableWindup && currentAction.Value == LegActionState.Charging);
    public bool CanDealDamage =>
        currentAction.Value == LegActionState.Kicking &&
        !hasHitThisKick;
    public float PeakKickSpeed { get; private set; }

    public float HealthPowerMultiplier
    {
        get
        {
            if (legHealth == null)
                return 1f;

            float originalMaxHp = Mathf.Max(1f, legHealth.MaxHp);
            float hpRatio = Mathf.Clamp01(legHealth.currentHp.Value / originalMaxHp);
            return Mathf.Lerp(minimumPowerAtZeroHealth, 1f, hpRatio);
        }
    }

    public float NormalizedKickCharge
    {
        get
        {
            if (!localCharging)
                return 0f;

            return Mathf.Clamp01((Time.unscaledTime - localChargeStartedAt) /
                                 Mathf.Max(0.05f, fullChargeDuration));
        }
    }

    // The HUD shows real output, not just held time. A damaged leg therefore
    // cannot fill the meter as high as a healthy leg.
    public float EffectiveNormalizedKickCharge =>
        NormalizedKickCharge * HealthPowerMultiplier;

    private PlayerFootForRobot footController;
    private Rigidbody footRb;
    private RobotHealth legHealth;
    private JointPullAndReconnect reconnect;
    private LobbyManager limbSelectionLobby;
    private PhysicsDamageSender damageSender;

    private bool localCharging;
    private bool localLiftArmed;
    private bool kickNeedsMouseRelease;
    private float localChargeStartedAt;

    private float serverChargeStartedAt;
    private float actionTimer;
    private float activeKickSpeed;
    private Vector3 kickStartPosition;
    private Vector3 kickDirection;
    private bool hasHitThisKick;

    // สะโพกเก็บเป็นพิกัด "ลำตัว" ไม่ใช่พิกัดโลก และไม่อ่าน pivotPoint สดๆ ทุกเฟรม
    // ⚠️ เพราะบน prefab นี้ LeftLegPivotPoint ถูก parent ไว้ใต้ UpperRightLeg (ต้นขาอีกข้าง!)
    // อ่านสดจะได้จุดที่แกว่งไปมาตามขาที่กำลังรับน้ำหนักอยู่
    private Vector3 kickHipLocalOffset;
    private float kickReleaseFootDrop;

    private Vector3 windupDirection;
    private Vector3 windupStartOffset;
    private float windupFootY;

    private CollisionDetectionMode originalFootCollisionMode;

    private void Awake()
    {
        footController = GetComponent<PlayerFootForRobot>();
        footRb = GetComponent<Rigidbody>();
        legHealth = GetComponent<RobotHealth>();
        reconnect = GetComponent<JointPullAndReconnect>();
        damageSender = GetComponent<PhysicsDamageSender>();

        // เท้าบน prefab เป็น Discrete — ที่ 70 m/s เท้าเดินทาง 1.4 m ต่อ 1 step ฟิสิกส์
        // จะทะลุ collider ที่บางกว่านั้นแบบเงียบๆ ต้องสลับเป็น ContinuousDynamic ตอนเตะ
        // (PlayerHandCombat ทำแบบเดียวกันกับหมัด 55 m/s อยู่แล้ว)
        if (footRb != null)
            originalFootCollisionMode = footRb.collisionDetectionMode;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
            ResetServerState();
    }

    public override void OnNetworkDespawn()
    {
        localCharging = false;
        localLiftArmed = false;
        kickNeedsMouseRelease = false;
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (!IsOwner || footController == null)
            return;

        if (!CanReadCombatInputForThisLimb())
        {
            CancelLocalCharge();
            localLiftArmed = false;
            kickNeedsMouseRelease = false;
            return;
        }

        // ✅ เตะได้เลยโดยไม่ต้องกดยกเท้าก่อน — กด Shift อย่างเดียวพอ
        // เดิมบังคับให้คลิกซ้ายค้าง + เท้าต้องกำลังก้าวอยู่ ทำให้ลำดับปุ่มจุกจิกมาก
        // (ท่าง้างดึงเท้าขึ้นจากพื้นให้เองอยู่แล้ว จึงไม่ต้องพึ่งการยกเท้าด้วยมือ)
        //
        // ⚠️ ไม่ต้องใช้ธงกันกดรัวแล้ว — วงจร GetKeyDown → ชาร์จ → GetKeyUp → เตะ
        //    บังคับให้ต้องปล่อย Shift ก่อนเตะครั้งใหม่อยู่แล้ว และฝั่ง server ยังกันซ้ำอีกชั้น
        //    ด้วยเงื่อนไข currentAction ต้องเป็น Idle (ดู CanUseKickInputLocally)
        bool canUseKickInput = CanUseKickInputLocally();

        if (!localCharging && canUseKickInput && Input.GetKeyDown(kickKey))
        {
            localCharging = true;
            localChargeStartedAt = Time.unscaledTime;
            // ส่งทิศเล็ง ณ ตอนเริ่มชาร์จไปด้วย — server ต้องใช้กำหนดทิศง้างถอยหลัง
            // (server อ่านจุดเล็งเองไม่ได้ เพราะ _footMarkerWorld อัปเดตเฉพาะฝั่ง owner)
            BeginChargeRpc(footController.GetKickAimDirection());
        }

        if (!localCharging)
            return;

        if (!canUseKickInput)
        {
            CancelLocalCharge();
            return;
        }

        if (Input.GetKeyUp(kickKey))
        {
            Vector3 aimDirection = footController.GetKickAimDirection();
            localCharging = false;
            ReleaseKickRpc(aimDirection);
        }
    }

    private bool CanReadCombatInputForThisLimb()
    {
        NetworkManager manager = NetworkManager.Singleton;

        // Scenes without a limb-selection lobby keep the existing ownership rule.
        if (manager == null || !manager.IsListening)
            return true;

        if (limbSelectionLobby == null)
        {
            limbSelectionLobby = LobbyManager.Instance;
            if (limbSelectionLobby == null)
            {
                limbSelectionLobby = FindFirstObjectByType<LobbyManager>(
                    FindObjectsInactive.Include);
            }
        }

        if (limbSelectionLobby == null)
            return true;

        return limbSelectionLobby.IsLimbSelectedByClient(
            gameObject,
            manager.LocalClientId);
    }

    private void FixedUpdate()
    {
        if (!IsServer || footController == null || footRb == null)
            return;

        if (!CanRemainInCurrentAction())
        {
            ResetServerState();
            return;
        }

        switch (currentAction.Value)
        {
            case LegActionState.Charging:
                DriveWindup();
                break;

            case LegActionState.Kicking:
            {
                actionTimer -= Time.fixedDeltaTime;
                footRb.isKinematic = false;

                Vector3 hip = CurrentKickHipAnchor();

                // เป้าไกลเกินความยาวขา (18 > maxLegLength 14) สปริงจึงไม่มีวันผ่อนแรงกลางทาง
                // หลักการเดียวกับหมัดที่ปักเป้าไว้ PivotPosition + punchDir * (maxArmLength + 2)
                // แกน Y ล็อกไว้ที่ระดับเท้าตอนปล่อย = รักษาความสูงที่ผู้เล่นเล็งไว้ด้วย W/S
                Vector3 kickTarget = hip + kickDirection * kickTargetReach;
                kickTarget.y = hip.y - kickReleaseFootDrop;

                // ── สปริงแบบแขน ── ห้ามเขียน linearVelocity ตรงๆ อีกต่อไป
                // ของเดิม footRb.linearVelocity = MoveTowards(...) ทิ้งอิมพัลส์แก้ของ solver ทุกเฟรม
                // ท่อนขาเลยถูก "ลาก" ตามเท้าแทนที่จะ "เหวี่ยง" = อาการแข็งเป็นหุ่นยนต์ที่เห็น
                // ข้อต่อทั้งโซ่ล็อกแกนเลื่อนอยู่แล้ว solver จึงทำหน้าที่เป็นลิมิตระยะขาให้เอง
                Vector3 toTarget = kickTarget - footRb.position;
                Vector3 velocityTarget = Vector3.ClampMagnitude(toTarget * kickMoveSpeed, activeKickSpeed);
                Vector3 force = (velocityTarget - footRb.linearVelocity) * kickDamper;

                // เครื่องยนต์ F = ma — แกนแรงเป็นแนวราบล้วน ไม่มีองค์ประกอบ Y เด็ดขาด
                // (เคยใส่แรงเงยขึ้นแล้วหุ่นกระโดดทั้งตัว ดู GetKickAimDirection ใน PlayerFootForRobot)
                if (Vector3.Dot(footRb.linearVelocity, kickDirection) < activeKickSpeed)
                    force += kickDirection * kickAcceleration;

                footRb.AddForce(force, ForceMode.Acceleration);

                BraceTorsoAgainstJump();

                float forwardSpeed = Mathf.Max(0f, Vector3.Dot(footRb.linearVelocity, kickDirection));
                PeakKickSpeed = Mathf.Max(PeakKickSpeed, forwardSpeed);
                float forwardDistance = Vector3.Dot(footRb.position - kickStartPosition, kickDirection);

                if (actionTimer <= 0f || forwardDistance >= kickReach)
                    BeginRecovery();
                break;
            }

            case LegActionState.Recovering:
                actionTimer -= Time.fixedDeltaTime;
                footRb.isKinematic = false;
                footRb.AddForce(-footRb.linearVelocity * recoveryDamping, ForceMode.Acceleration);

                // เท้าเข้าช่วงนี้ด้วยความเร็วสูง แรงหน่วงที่กดเท้าลงจะสะท้อนขึ้นลำตัวผ่านโซ่ข้อต่อ
                // เดิมเรียกเฉพาะตอน Kicking ลำตัวเลยไม่มีอะไรกันตอนที่โมเมนตัมถูกปล่อยออกมาจริงๆ
                BraceTorsoAgainstJump();

                if (actionTimer <= 0f)
                    ResetServerState();
                break;
        }
    }

    /// <summary>
    /// ง้างขาระหว่างชาร์จ — ดึงเท้าถอยหลังตามระดับพลังที่สะสม
    /// ⚠️ แนวราบล้วน: คัดลอกความสูงเท้า ณ ตอนเริ่มชาร์จมาใช้ตรงๆ
    /// เงื่อนไขล้มทุกตัวใน TorsoMovement ต้องการ groundedCount == 0 และ IsGrounded()
    /// ตัดสินจากแกน Y ของเท้า การถอยแนวราบจึงขยับตัวจับล้มไม่ได้เลยแม้แต่ tick เดียว
    /// (เท้าลอยพ้นพื้นตั้งแต่ก่อนชาร์จอยู่แล้ว เพราะ input บังคับให้ต้องกำลังก้าวอยู่)
    /// </summary>
    private void DriveWindup()
    {
        if (!enableWindup)
            return;

        footRb.isKinematic = false;

        float heldDuration = Mathf.Max(0f, Time.time - serverChargeStartedAt);
        float charge = Mathf.Clamp01(heldDuration / Mathf.Max(0.05f, fullChargeDuration));

        // เริ่มจากตำแหน่งเท้าตอนเริ่มชาร์จ แล้วค่อยๆ ถอย — charge = 0 คือไม่ขยับเลย
        Vector3 hip = CurrentKickHipAnchor();
        Vector3 windupTarget = hip + windupStartOffset - windupDirection * (windupPullBack * charge);
        windupTarget.y = windupFootY;

        Vector3 toTarget = windupTarget - footRb.position;
        Vector3 velocityTarget = Vector3.ClampMagnitude(toTarget * windupMoveSpeed, maxWindupSpeed);
        Vector3 force = (velocityTarget - footRb.linearVelocity) * windupDamper;

        // ท่านี้แค่ "ค้างเท้าไว้" ไม่ใช่ดีดขึ้น การหักล้างแรงโน้มถ่วงจึงไม่สร้างแรงยกสุทธิ
        // ถ้าไม่หักล้าง เท้าจะตกลงมา ~0.55 m ระหว่างง้าง (แรงโน้มถ่วงโปรเจกต์นี้คือ -20 ไม่ใช่ -9.81)
        if (footRb.useGravity)
            force -= Physics.gravity;

        footRb.AddForce(force, ForceMode.Acceleration);
    }

    /// <summary>
    /// สะโพกปัจจุบัน คำนวณจาก offset ที่เก็บไว้ในพิกัดลำตัว — ลำตัวหมุน/เคลื่อนที่แล้วท่าเตะตามไปด้วย
    /// จงใจไม่อ่าน pivotPoint สดๆ เพราะมันถูก parent ไว้ใต้ต้นขาอีกข้างที่แกว่งอิสระ
    /// </summary>
    private Vector3 CurrentKickHipAnchor()
    {
        TorsoMovement kickTorso = footController != null ? footController.torso : null;
        if (kickTorso != null && kickTorso.torsoRb != null)
            return kickTorso.torsoRb.position + kickTorso.torsoRb.rotation * kickHipLocalOffset;

        return footController != null && footController.pivotPoint != null
            ? footController.pivotPoint.position
            : footRb.position;
    }

    /// <summary>เก็บตำแหน่งสะโพกเป็นพิกัดลำตัว ณ วินาทีที่เรียก</summary>
    private void CaptureKickHipAnchor()
    {
        Vector3 hipNow = footController != null && footController.pivotPoint != null
            ? footController.pivotPoint.position
            : footRb.position;

        TorsoMovement kickTorso = footController != null ? footController.torso : null;
        kickHipLocalOffset = (kickTorso != null && kickTorso.torsoRb != null)
            ? Quaternion.Inverse(kickTorso.torsoRb.rotation) * (hipNow - kickTorso.torsoRb.position)
            : Vector3.zero;
    }

    private bool CanUseKickInputLocally()
    {
        if (GameFlowManager.GameEnded)
            return false;
        if (footController.useVirtualCursor && Cursor.lockState != CursorLockMode.Locked)
            return false;
        if (footController.currentState.Value != PlayerFootForRobot.FootState.Attached)
            return false;
        if (footController.torso != null &&
            footController.torso.currentState.Value != TorsoMovement.TorsoState.Standing)
            return false;
        // ไม่บังคับให้กดยกเท้าหรือกำลังก้าวอยู่แล้ว — เตะยืนอยู่กับที่ได้เลย
        // เหลือแค่ห้ามเตะตอนลอยกลางอากาศ
        if (footController.isJumping)
            return false;
        if (reconnect != null && !reconnect.IsConnected)
            return false;

        bool actionAcceptsInput =
            currentAction.Value == LegActionState.Idle ||
            (localCharging && currentAction.Value == LegActionState.Charging);

        return actionAcceptsInput;
    }

    private bool CanStartKickOnServer()
    {
        if (footController.currentState.Value != PlayerFootForRobot.FootState.Attached)
            return false;
        if (footController.torso != null &&
            footController.torso.currentState.Value != TorsoMovement.TorsoState.Standing)
            return false;
        if (footController.isJumping)
            return false;
        if (reconnect != null && !reconnect.IsConnected)
            return false;

        return true;
    }

    private bool CanRemainInCurrentAction()
    {
        if (currentAction.Value == LegActionState.Idle)
            return true;
        if (footController.currentState.Value != PlayerFootForRobot.FootState.Attached)
            return false;
        if (reconnect != null && !reconnect.IsConnected)
            return false;
        if (footController.torso != null &&
            footController.torso.currentState.Value != TorsoMovement.TorsoState.Standing)
            return false;

        return true;
    }

    [Rpc(SendTo.Server, Delivery = RpcDelivery.Reliable)]
    private void BeginChargeRpc(Vector3 aimDirection)
    {
        if (currentAction.Value != LegActionState.Idle || !CanStartKickOnServer())
            return;

        // Local input already requires Left Mouse + a raised stepping foot.
        // Mark it raised server-side too so cross-component RPC ordering cannot
        // reject a valid kick on a higher-latency client.
        footController.isStepping = true;
        currentAction.Value = LegActionState.Charging;
        serverChargeStartedAt = Time.time;
        PeakKickSpeed = 0f;
        hasHitThisKick = false;

        CaptureKickHipAnchor();

        // ทิศง้าง = ตรงข้ามทิศเล็ง ล็อกไว้ตั้งแต่เริ่มชาร์จ (ท่ายืนถูกคอมมิตแล้ว)
        aimDirection.y = 0f;
        if (!IsFinite(aimDirection) || aimDirection.sqrMagnitude < 0.001f)
        {
            aimDirection = footController.pivotPoint != null
                ? footController.pivotPoint.forward
                : transform.forward;
            aimDirection.y = 0f;
        }
        windupDirection = aimDirection.sqrMagnitude > 0.001f
            ? aimDirection.normalized
            : Vector3.forward;

        // จุดตั้งต้นของการง้าง = ตำแหน่งเท้าปัจจุบันเทียบสะโพก (แนวราบ)
        // charge = 0 จึงแปลว่า "ไม่ขยับ" เท้าไม่กระตุกตอนเริ่มกด Shift
        Vector3 startOffset = footRb.position - CurrentKickHipAnchor();
        startOffset.y = 0f;
        windupStartOffset = startOffset;
        windupFootY = footRb.position.y;
    }

    [Rpc(SendTo.Server, Delivery = RpcDelivery.Reliable)]
    private void ReleaseKickRpc(Vector3 requestedDirection)
    {
        if (currentAction.Value != LegActionState.Charging || !CanRemainInCurrentAction())
        {
            ResetServerState();
            return;
        }

        float heldDuration = Mathf.Max(0f, Time.time - serverChargeStartedAt);
        float charge = Mathf.Clamp01(heldDuration / Mathf.Max(0.05f, fullChargeDuration));
        charge = Mathf.Max(minimumCharge, charge);

        if (!IsFinite(requestedDirection) || requestedDirection.sqrMagnitude < 0.001f)
            requestedDirection = footController.GetKickAimDirection();

        requestedDirection.y = 0f;
        if (requestedDirection.sqrMagnitude < 0.001f)
            requestedDirection = footController.pivotPoint != null
                ? footController.pivotPoint.forward
                : transform.forward;
        requestedDirection.y = 0f;
        requestedDirection.Normalize();

        float chargedSpeed = Mathf.Lerp(minimumKickSpeed, maximumKickSpeed, charge);
        activeKickSpeed = chargedSpeed * HealthPowerMultiplier;
        kickDirection = requestedDirection;
        kickStartPosition = footRb.position;
        actionTimer = kickDuration;
        PeakKickSpeed = 0f;
        hasHitThisKick = false;
        currentAction.Value = LegActionState.Kicking;

        footRb.isKinematic = false;

        // เก็บสะโพกใหม่ ณ วินาทีปล่อย — ท่าง้างเพิ่งย้ายลำตัว/ขาไปจากตอนเริ่มชาร์จ
        CaptureKickHipAnchor();

        // ระยะที่เท้าห้อยต่ำกว่าสะโพกตอนปล่อย → เป้าของสปริงเริ่มที่ระดับเท้าพอดี
        // ผู้เล่นเล็งความสูงไว้แค่ไหนด้วย W/S ท่าเตะก็ออกจากระดับนั้น
        kickReleaseFootDrop = CurrentKickHipAnchor().y - footRb.position.y;

        // 70 m/s × 0.02 s = 1.40 m ต่อ step — Discrete ทะลุ collider ที่บางกว่านั้นแบบเงียบๆ
        footRb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    [Rpc(SendTo.Server, Delivery = RpcDelivery.Reliable)]
    private void CancelChargeRpc()
    {
        if (currentAction.Value == LegActionState.Charging)
            ResetServerState();
    }

    private void CancelLocalCharge()
    {
        if (!localCharging)
            return;

        localCharging = false;
        CancelChargeRpc();
    }

    private void BraceTorsoAgainstJump()
    {
        if (footController.torso == null || footController.torso.torsoRb == null)
            return;

        Rigidbody torsoRb = footController.torso.torsoRb;
        Vector3 torsoVelocity = torsoRb.linearVelocity;
        if (torsoVelocity.y <= maxTorsoUpwardSpeedDuringKick)
            return;

        torsoVelocity.y = maxTorsoUpwardSpeedDuringKick;
        torsoRb.linearVelocity = torsoVelocity;
    }

    private void BeginRecovery()
    {
        // สรุปผลทุกครั้งที่เตะจบ — จุดนี้เป็นทางผ่านเดียวของทั้งเตะครบเวลา/ครบระยะ/ชนโดน/ชนกำแพง
        // เตะวืดก็ต้องโชว์ ไม่งั้นไม่มีทางรู้ว่าเท้าเร่งได้จริงกี่ m/s (log ชนโดนอยู่ที่ตัว DamageSender)
        if (debugKickLog)
        {
            string damageText = "?";
            if (damageSender != null)
            {
                damageText = PeakKickSpeed < damageSender.minVelocityThreshold
                    ? $"0 (พีคต่ำกว่าเกณฑ์ {damageSender.minVelocityThreshold})"
                    : $"{Mathf.Min(PeakKickSpeed * damageSender.kickSpeedToDamage, damageSender.maxDamagePerHit):F1}";
            }

            Debug.Log(
                $"🦵 [{gameObject.name}] เตะจบ | Peak: {PeakKickSpeed:F1} m/s | " +
                $"สั่งไป: {activeKickSpeed:F1} m/s | ดาเมจถ้าเข้าเป้า: {damageText} | " +
                $"โดนเป้า: {(hasHitThisKick ? "✅" : "❌ วืด")}");
        }

        currentAction.Value = LegActionState.Recovering;
        actionTimer = recoveryDuration;
    }

    public void NotifyKickImpact()
    {
        if (!IsServer || !CanDealDamage)
            return;

        hasHitThisKick = true;
        BeginRecovery();
    }

    public void NotifyKickBlocked()
    {
        if (!IsServer || currentAction.Value != LegActionState.Kicking)
            return;

        BeginRecovery();
    }

    public void ResetForRespawn()
    {
        localCharging = false;
        localLiftArmed = false;
        kickNeedsMouseRelease = false;

        if (IsServer)
            ResetServerState();
    }

    private void ResetServerState()
    {
        if (IsServer)
            currentAction.Value = LegActionState.Idle;

        actionTimer = 0f;
        activeKickSpeed = 0f;
        kickStartPosition = Vector3.zero;
        PeakKickSpeed = 0f;
        hasHitThisKick = false;

        // คืนโหมดชนที่ "ปลายช่วง recovery" ไม่ใช่ตอน BeginRecovery
        // เท้ายังวิ่งด้วยความเร็วสูงตลอดช่วงหน่วง ถ้าคืนเป็น Discrete ตั้งแต่ต้นก็ยังทะลุได้อยู่ดี
        if (footRb != null)
            footRb.collisionDetectionMode = originalFootCollisionMode;
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
               !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
