using UnityEngine;
using Unity.Netcode;
using NscGame.Enemy;

public class PlayerHandCombat : PlayerHandMovement
{
    public enum CombatState { Idle, Punching, Recovering }

    [Header("Combat Settings")]
    public NetworkVariable<CombatState> currentCombatState = new NetworkVariable<CombatState>(CombatState.Idle);

    [Header("Punch Acceleration (คลิกซ้าย = คันเร่งของหมัด)")]
    [Tooltip("ความเร่งที่อัดเข้ากำปั้นขณะกดคลิกซ้ายค้าง (m/s²) — ที่มาของ F = ma\n" +
             "เมาส์ + ล้อเมาส์ยังคุมเป้ามือได้ตลอดเวลาแม้กำลังต่อย = หมัดนำวิถี\n" +
             "(เล็งขึ้น = อัปเปอร์คัต / เล็งลง = หมัดทุบ / เล็งข้าง = หมัดเหวี่ยง — ไม่ต้องมี animation แยก)")]
    public float punchAcceleration = 400f;
    [Tooltip("ความเร็วหมัดสูงสุด — กันทะลุ collider และเป็นเพดานดาเมจไปในตัว")]
    public float maxPunchSpeed = 55f;
    [Tooltip("เวลาบูสต์สูงสุดต่อการกดหนึ่งครั้ง กันกดคลิกซ้ายค้างลากตัวบินไปเรื่อยๆ")]
    public float maxPunchDuration = 0.75f;
    [Tooltip("เวลาง้างหมัด (วินาที) — ดึงมือกลับเข้าไหล่ก่อนปล่อย\n" +
             "ให้ทุกหมัดมีระยะเร่งเต็มๆ แม้แขนกำลังเหยียดค้างอยู่ (ไม่มีระยะ = ไม่มีความเร็ว = ไม่มีดาเมจ)")]
    public float punchWindupTime = 0.12f;
    [Tooltip("แรงต้านสปริงตอนต่อย (ต่ำ = พุ่งทะลวง แต่ต่ำกว่า ~4 จะเริ่มคุมปลายหมัดไม่อยู่)")]
    public float punchDamper = 5f;
    [Tooltip("ระยะยืดพิเศษตอนต่อย — ขยาย reach limit ของคลาสแม่ให้หมัดยืดเกินระยะปกติได้")]
    public float punchExtraReach = 5f;
    [Tooltip("สเกลเบรกปลายทาง (brakeDamping) ตอนต่อย\n0 = ปิดเบรก พุ่งใส่จุดเล็งเต็มแรง / 1 = เบรกเท่าปกติ")]
    [Range(0f, 1f)] public float punchBrakeScale = 0.05f;

    [Header("Recovery Blend (Anti-Jitter)")]
    [Tooltip("ระยะเวลา (วินาที) ที่ damper/reach ค่อยๆ lerp กลับสู่ค่าปกติหลังหมัดจบ\n" +
             "ทำหน้าที่เป็น cooldown ระหว่างหมัดไปในตัว")]
    public float recoveryBlendDuration = 0.25f;

    [Tooltip("ช่วงผ่อนผันหลังหมัดจบ (วินาที) ที่การชนยังนับดาเมจได้\n" +
             "กันเคสหมัดถึงเป้าช้ากว่าจังหวะปล่อยคลิกซ้าย/หมดเวลาแค่เสี้ยววินาทีแล้วดาเมจหาย")]
    public float damageGraceTime = 0.2f;

    private float punchTimer = 0f;
    private float recoveryTimer = 0f;

    [Header("Aim Assist (ช่วยเล็ง)")]
    [Tooltip("ถ้ามีศัตรูอยู่ในกรวยรอบทิศหมัด หมัดจะเบนเข้าหาให้เองแบบ realtime\n" +
             "เล็งหลวมๆ ก็เข้าเป้า — หัวใจของ 'เล่นง่าย'")]
    public bool enableAimAssist = true;
    [Tooltip("มุมกรวยช่วยเล็ง (องศา) — กว้าง = ดูดแรง, 0 = ปิด")]
    public float aimAssistAngle = 30f;
    [Tooltip("Layer ที่ให้ aim assist สแกนหา (ตั้งเป็น layer ของศัตรูเท่านั้น!)\n" +
             "ถ้าปล่อย Everything ไว้ buffer จะเต็มด้วยกำแพง/พื้นก่อนถึงตัวศัตรู ทำ assist วืดแบบสุ่ม")]
    public LayerMask aimAssistLayer = ~0;

    [Header("Aim Assist Gizmos")]
    public bool showAimAssistGizmos = true;
    public Color aimRangeColor = new Color(1f, 0.75f, 0f, 0.35f);
    public Color rawAimColor = Color.cyan;
    public Color assistedAimColor = Color.green;
    [Range(4, 32)] public int aimConeSegments = 16;

    [Tooltip("โชว์ log สรุปทุกหมัด (ความเร็วพีค/ดาเมจที่จะได้/โดนเป้าไหม) ไว้เช็คจูนค่า")]
    public bool debugPunchLog = true;

    private float originalHandDamper;
    private float originalBrakeDamping;
    private CollisionDetectionMode originalCollisionMode;
    private PhysicsDamageSender damageSender;

    // ✅ ความเร็วสูงสุดที่หมัดทำได้ในรอบ Punching นี้ — ใช้เป็นฐานคิดดาเมจ
    private float peakPunchSpeed = 0f;
    // กันดาเมจซ้ำหลายครั้งจากหมัดเดียว
    private bool hasHitThisPunch = false;

    // ให้ PhysicsDamageSender เช็คว่าหมัดกำลังพุ่งอยู่ไหม
    public bool IsPunching => currentCombatState.Value == CombatState.Punching;

    // ความเร็วพีคของหมัดรอบนี้ (คงค่าไว้จนถึงช่วง grace หลังปล่อย)
    public float PeakPunchSpeed => peakPunchSpeed;

    // ✅ แรงหมัด (0-1) คำนวณจากความเร็วจริงบน Server แล้ว sync ให้ทุกเครื่องแสดง UI
    // (ฟิสิกส์หมัดรันบน Server — ถ้า client อ่าน linearVelocity ตรงๆ จะได้ ~0 ตลอด)
    private readonly NetworkVariable<float> netNormalizedPunchForce = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ค่าแรงหมัดสำหรับ UI — Server/โหมดออฟไลน์คำนวณสด, client ใช้ค่าที่ sync มา
    // (ดาเมจจริงยังคิดจาก PeakPunchSpeed เหมือนเดิม ตัวนี้มีไว้แสดงผลอย่างเดียว)
    public float NormalizedPunchForce
    {
        get
        {
            if (currentCombatState.Value != CombatState.Punching)
                return 0f;

            if (IsSpawned && !IsServer)
                return netNormalizedPunchForce.Value;

            if (handRb == null)
                return 0f;

            return Mathf.Clamp01(
                handRb.linearVelocity.magnitude /
                Mathf.Max(0.01f, maxPunchSpeed));
        }
    }

    // ✅ เงื่อนไขนับดาเมจ: ยังไม่เคยชนในหมัดนี้ และ (กำลังต่อย หรือ อยู่ในช่วงผ่อนผันหลังปล่อย)
    public bool CanDealDamage =>
        !hasHitThisPunch &&
        (currentCombatState.Value == CombatState.Punching ||
         (currentCombatState.Value == CombatState.Recovering && recoveryTimer <= damageGraceTime));

    private void Start()
    {
        // เก็บค่าดั้งเดิมจากคลาสแม่เอาไว้
        originalHandDamper = handDamper;
        originalBrakeDamping = brakeDamping;
        damageSender = GetComponent<PhysicsDamageSender>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer && handRb != null)
            originalCollisionMode = handRb.collisionDetectionMode;
    }

    protected override void Update()
    {
        // คลาสแม่รันก่อนเสมอ → เป้ามือถูกอัปเดตจากเมาส์/ล้อทุกเฟรม "รวมถึงระหว่างกำลังต่อย"
        // นี่คือสิ่งที่ทำให้ผู้เล่นสร้างท่าหมัดเองได้จากการเล็งจริง โดยไม่ต้องมี animation แยก
        base.Update();

        if (!IsOwner || currentState.Value != HandState.Attached) return;

        if (!CanReadCombatInputForThisLimb())
        {
            if (punchHeld)
            {
                punchHeld = false;
                SetPunchingRpc(false);
            }
            return;
        }

        HandleCombatInput();
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

    private LobbyManager limbSelectionLobby;
    private bool punchHeld = false;
    // "การกดครั้งนี้ยังไม่ได้กลายเป็นหมัด" — ใช้บังคับกฎ 1 คลิก = 1 หมัด
    private bool punchRequestPending = false;
    private float punchRequestExpireTime = 0f;
    private float nextPunchRequestTime = 0f;

    [Header("Punch Input (Left Mouse)")]
    [Tooltip("ช่วงเวลาที่ยังพยายามส่งคำขอต่อยซ้ำ ถ้าคลิกไปตอน server ยังอยู่ใน Recovering ของหมัดก่อน\n" +
             "มีไว้กัน 'คลิกแล้วหมัดหาย' เพราะกดเร็วไปเสี้ยววินาที — ไม่ใช่ระบบต่อยรัวอัตโนมัติ\n" +
             "หมดเวลาแล้วคำขอถูกทิ้ง ต้องปล่อยคลิกซ้ายแล้วกดใหม่เท่านั้น")]
    [Min(0f)]
    [SerializeField] private float punchRequestWindow = 0.35f;

    private void HandleCombatInput()
    {
        // เคอร์เซอร์ปลดอยู่ (กด Esc ไปเมนู) → ปล่อยหมัดที่ค้างแล้วหยุดรับ input ต่อย
        // ไม่งั้นคลิกซ้ายค้างจะต่อยต่อระหว่างผู้เล่นคลิกเมนูอยู่
        if (useVirtualCursor && Cursor.lockState != CursorLockMode.Locked)
        {
            if (punchHeld) { punchHeld = false; SetPunchingRpc(false); }
            punchRequestPending = false;
            return;
        }

        // 🖱️ คลิกซ้ายที่ใช้ "ดึงเคอร์เซอร์กลับเข้าเกม" ห้ามนับเป็นหมัด (ดู HandleCursorLock ในคลาสแม่)
        // ต้องปล่อยคลิกก่อน ถึงจะเริ่มต่อยได้ — ไม่งั้นทุกครั้งที่กลับจากเมนูจะต่อยฟรีหนึ่งหมัด
        if (ignoreClickUntilRelease)
        {
            if (!Input.GetMouseButton(0)) ignoreClickUntilRelease = false;
            return;
        }

        // ⚡ กดคลิกซ้าย = เริ่มหมัด / ค้างไว้ = อัดความเร่งต่อ / ปล่อย = หยุดบูสต์แล้วเข้า Recovery
        if (Input.GetMouseButtonDown(0))
        {
            punchHeld = true;
            punchRequestPending = true;
            punchRequestExpireTime = Time.time + punchRequestWindow;
            nextPunchRequestTime = 0f; // ขอทันทีในเฟรมนี้เลย
        }

        if (Input.GetMouseButtonUp(0))
        {
            punchHeld = false;
            punchRequestPending = false;
            SetPunchingRpc(false);
        }

        // ⭐ [1 คลิก = 1 หมัด] คำขอถูก "ใช้ไปแล้ว" ทันทีที่หมัดออก
        // ค้างคลิกซ้ายต่อไม่ทำให้ต่อยรัว — ต้องปล่อยแล้วกดใหม่เท่านั้น (ต่างจากระบบ Shift เดิม)
        if (!punchRequestPending) return;

        if (currentCombatState.Value != CombatState.Idle)
        {
            // server ยังไม่ว่าง (Recovering ของหมัดก่อน) — ลองใหม่จนหมด window แล้วทิ้ง
            if (Time.time > punchRequestExpireTime) punchRequestPending = false;
            return;
        }

        if (Time.time < nextPunchRequestTime) return;

        nextPunchRequestTime = Time.time + 0.1f; // throttle ระหว่างรอ state sync กลับมา
        punchRequestPending = false;             // ⭐ กินคำขอทิ้งที่นี่ = ไม่มีทางต่อยซ้ำจากการกดครั้งเดียว
        SetPunchingRpc(true);
    }

    [Rpc(SendTo.Server)]
    private void SetPunchingRpc(bool punching)
    {
        // ✅ ตัวล้มอยู่ = ต่อยไม่ได้ (แขนอยู่โหมดปล่อยตามฟิสิกส์) — Q เพื่อลุกก่อน
        bool torsoDown = torso != null &&
            (torso.currentState.Value == TorsoMovement.TorsoState.Ragdoll ||
             torso.currentState.Value == TorsoMovement.TorsoState.Falling);

        // Server ตัดสิน state เองทั้งหมด — client ส่งได้แค่สัญญาณกด/ปล่อย
        if (punching && currentCombatState.Value == CombatState.Idle && !torsoDown)
            BeginPunch();
        else if (!punching && currentCombatState.Value == CombatState.Punching)
            EndPunch();
    }

    private void BeginPunch()
    {
        currentCombatState.Value = CombatState.Punching;
        punchTimer = maxPunchDuration;
        peakPunchSpeed = 0f;      // เริ่มนับความเร็วพีคใหม่ทุกหมัด
        hasHitThisPunch = false;  // ปลดล็อกดาเมจรอบใหม่

        // หมัดเร็วระดับนี้ต้องใช้ Continuous Dynamic กันทะลุ collider บางๆ
        if (handRb != null)
            handRb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    private void EndPunch()
    {
        currentCombatState.Value = CombatState.Recovering;
        recoveryTimer = 0f;
        netNormalizedPunchForce.Value = 0f;

        if (handRb != null)
            handRb.collisionDetectionMode = originalCollisionMode;

        // ✅ [Debug] สรุปทุกหมัดไว้เช็คจูนค่า — โชว์แม้ต่อยวืด
        if (debugPunchLog)
        {
            string dmgText = "?";
            if (damageSender != null)
            {
                dmgText = peakPunchSpeed < damageSender.minVelocityThreshold
                    ? $"0 (พีคต่ำกว่าเกณฑ์ {damageSender.minVelocityThreshold})"
                    : $"{Mathf.Min(peakPunchSpeed * damageSender.speedToDamage, damageSender.maxDamagePerHit):F1}";
            }
            Debug.Log($"👊 [{gameObject.name}] หมัดจบ | Peak: {peakPunchSpeed:F1} m/s | ดาเมจถ้าเข้าเป้า: {dmgText} | โดนเป้า: {(hasHitThisPunch ? "✅" : "❌ วืด")}");
        }
    }

    // เรียกจาก PhysicsDamageSender เมื่อหมัดปะทะ "ศัตรู" → ล็อกดาเมจ + จบหมัดทันที
    // ให้ความรู้สึก "ปะทะแล้วจบ" ไม่ใช่ไถลถูเป้าต่อ และกันดาเมจรัวจากการเสียดสี
    public void NotifyPunchImpact()
    {
        if (!IsServer) return;
        hasHitThisPunch = true;
        if (currentCombatState.Value == CombatState.Punching)
            EndPunch();
    }

    // เรียกเมื่อหมัดชนของแข็งที่ "ไม่ใช่ศัตรู" (กำแพง/พื้น) → จบหมัดแต่ไม่ล็อกดาเมจ
    // ถ้าหมัดปัดต่อไปโดนศัตรูภายในช่วง grace ยังนับดาเมจได้อยู่
    public void NotifyPunchBlocked()
    {
        if (!IsServer) return;
        if (currentCombatState.Value == CombatState.Punching)
            EndPunch();
    }

    // ✅ [Aim Assist] หา EnemyHealth ในกรวยรอบทิศหมัด แล้วเบนทิศเข้าหาเป้าที่มุมแคบสุด
    // buffer 64 + กรองด้วย aimAssistLayer — เดิมสแกนทุก layer ในรัศมี ~7m
    // ฉากเมือง prop เยอะ buffer 32 ช่องเต็มด้วยกำแพงก่อนถึงศัตรู = assist วืดแบบสุ่ม
    private static readonly Collider[] _assistBuffer = new Collider[64];
    private Vector3 ApplyAimAssist(Vector3 punchDir)
    {
        return TryFindAimAssistTarget(punchDir, out Vector3 assistedDirection, out _)
            ? assistedDirection
            : punchDir;
    }

    private bool TryFindAimAssistTarget(Vector3 punchDir, out Vector3 assistedDirection, out Vector3 targetPosition)
    {
        Vector3 rawDirection = punchDir.sqrMagnitude > 0.0001f ? punchDir.normalized : transform.forward;
        assistedDirection = rawDirection;
        targetPosition = Vector3.zero;

        float range = maxArmLength + punchExtraReach;
        int count = Physics.OverlapSphereNonAlloc(PivotPosition, range, _assistBuffer, aimAssistLayer);
        float bestAngle = aimAssistAngle;
        bool found = false;

        for (int i = 0; i < count; i++)
        {
            Collider candidate = _assistBuffer[i];
            if (candidate == null || candidate.GetComponentInParent<EnemyHealth>() == null) continue;

            Vector3 candidatePosition = candidate.bounds.center;
            Vector3 toEnemy = candidatePosition - PivotPosition;
            if (toEnemy.sqrMagnitude < 0.0001f) continue;

            float angle = Vector3.Angle(rawDirection, toEnemy);
            if (angle < bestAngle)
            {
                bestAngle = angle;
                assistedDirection = toEnemy.normalized;
                targetPosition = candidatePosition;
                found = true;
            }
        }

        return found;
    }

    private void OnDrawGizmosSelected()
    {
        if (!showAimAssistGizmos || !enableAimAssist || aimAssistAngle <= 0f) return;

        Vector3 origin = PivotPosition;
        float range = Mathf.Max(0.01f, maxArmLength + punchExtraReach);
        // smoothedHandTarget is initialized only after spawning in Play Mode.
        // In Edit Mode it is still world zero, which made the cone point backward
        // toward the world origin. Preview the actual shoulder-to-hand direction.
        Vector3 rawDirection = Application.isPlaying
            ? smoothedHandTarget - origin
            : (handRb != null ? handRb.position - origin : transform.position - origin);
        if (rawDirection.sqrMagnitude < 0.0001f && handRb != null)
            rawDirection = handRb.position - origin;
        if (rawDirection.sqrMagnitude < 0.0001f)
            rawDirection = transform.forward;
        rawDirection.Normalize();

        Gizmos.color = aimRangeColor;
        Gizmos.DrawWireSphere(origin, range);

        Gizmos.color = rawAimColor;
        Gizmos.DrawLine(origin, origin + rawDirection * range);

        Vector3 referenceUp = Mathf.Abs(Vector3.Dot(rawDirection, Vector3.up)) > 0.98f
            ? Vector3.right
            : Vector3.up;
        Vector3 coneRight = Vector3.Cross(rawDirection, referenceUp).normalized;
        Vector3 coneUp = Vector3.Cross(coneRight, rawDirection).normalized;
        float coneRadians = aimAssistAngle * Mathf.Deg2Rad;
        float coneRadius = Mathf.Sin(coneRadians) * range;
        Vector3 coneCenter = origin + rawDirection * (Mathf.Cos(coneRadians) * range);
        int segments = Mathf.Max(4, aimConeSegments);
        Vector3 previous = coneCenter + coneRight * coneRadius;

        for (int i = 1; i <= segments; i++)
        {
            float radians = (i / (float)segments) * Mathf.PI * 2f;
            Vector3 point = coneCenter +
                (coneRight * Mathf.Cos(radians) + coneUp * Mathf.Sin(radians)) * coneRadius;
            Gizmos.DrawLine(previous, point);
            if (i % Mathf.Max(1, segments / 4) == 0)
                Gizmos.DrawLine(origin, point);
            previous = point;
        }

        if (TryFindAimAssistTarget(rawDirection, out Vector3 assistedDirection, out Vector3 targetPosition))
        {
            Gizmos.color = assistedAimColor;
            Gizmos.DrawLine(origin, origin + assistedDirection * range);
            Gizmos.DrawWireSphere(targetPosition, Mathf.Max(0.1f, range * 0.04f));
        }
    }

    protected override void FixedUpdate()
    {
        base.FixedUpdate(); // ให้คลาสแม่รันฟิสิกส์ปกติ

        if (!IsServer) return;

        if (currentCombatState.Value == CombatState.Punching)
        {
            // ✅ จำความเร็วสูงสุดของหมัดรอบนี้ไว้เป็นฐานคิดดาเมจ
            if (handRb != null)
            {
                peakPunchSpeed = Mathf.Max(peakPunchSpeed, handRb.linearVelocity.magnitude);

                // sync แรงหมัดให้ client แสดง UI — เขียนเฉพาะตอนค่าขยับพอ ลด traffic
                float normalizedForce = Mathf.Clamp01(
                    handRb.linearVelocity.magnitude / Mathf.Max(0.01f, maxPunchSpeed));
                if (Mathf.Abs(netNormalizedPunchForce.Value - normalizedForce) > 0.02f)
                    netNormalizedPunchForce.Value = normalizedForce;
            }

            punchTimer -= Time.fixedDeltaTime;
            if (punchTimer <= 0f) EndPunch();
        }
        else if (currentCombatState.Value == CombatState.Recovering)
        {
            recoveryTimer += Time.fixedDeltaTime;
            if (recoveryTimer >= recoveryBlendDuration)
                currentCombatState.Value = CombatState.Idle;
        }
    }

    // ⭐ เขียนทับการขยับแขน: ตอนต่อยใช้สปริงเดิมเป็นพวงมาลัย + อัดความเร่ง (F = ma) เป็นเครื่องยนต์
    protected override void PerformArmMovement()
    {
        switch (currentCombatState.Value)
        {
            case CombatState.Punching:
            {
                extraReach = punchExtraReach;

                // ทิศพุ่ง = เส้นตรงจากหัวไหล่ ผ่านจุดที่ผู้เล่นเล็ง (วิถีเดียว ตรงแน่นอน)
                Vector3 originalTarget = smoothedHandTarget;
                Vector3 punchDir = originalTarget - PivotPosition;
                if (punchDir.sqrMagnitude > 0.0001f)
                    punchDir.Normalize();
                else
                {
                    Vector3 outward = handRb.position - PivotPosition;
                    punchDir = outward.sqrMagnitude > 0.0001f ? outward.normalized : transform.forward;
                }

                // ✅ [Aim Assist] มีศัตรูในกรวยเล็ง → เบนทิศหมัดเข้าหาให้เอง (realtime)
                if (enableAimAssist && aimAssistAngle > 0f)
                    punchDir = ApplyAimAssist(punchDir);

                // ✅ [Wind-up] ช่วงแรกของหมัด: ดึงมือกลับเข้าใกล้ไหล่ก่อน (ง้างหมัด)
                // แก้ปัญหา "แขนเหยียดค้างแล้วต่อยไม่ออก" — หมัดมีระยะพุ่งเต็มทุกครั้ง
                float punchElapsed = maxPunchDuration - punchTimer;
                if (punchElapsed < punchWindupTime)
                {
                    smoothedHandTarget = PivotPosition + punchDir * (maxArmLength * 0.25f);
                    velocityCapOverride = maxPunchSpeed;
                    base.PerformArmMovement();
                    smoothedHandTarget = originalTarget;
                    velocityCapOverride = 0f;
                    break; // ยังไม่อัดความเร่ง — รอปล่อยหมัดจริงหลังง้างเสร็จ
                }

                // ✅ [Force Full Extension] เป้าตอนต่อย = ระยะสุดแขน + เผื่อ เสมอ
                // ไม่สนว่าจุดเล็งใกล้แค่ไหน → แขนเหยียดตรงเต็มระยะทุกหมัด
                smoothedHandTarget = PivotPosition + punchDir * (maxArmLength + 2f);

                // สปริงคลาสแม่เป็นตัวคุมวิถี: ลดความหนืด/เบรก และใช้เพดานความเร็วหมัด
                velocityCapOverride = maxPunchSpeed;
                handDamper = punchDamper;
                brakeDamping = originalBrakeDamping * punchBrakeScale;
                base.PerformArmMovement();
                smoothedHandTarget = originalTarget;
                velocityCapOverride = 0f;
                handDamper = originalHandDamper;
                brakeDamping = originalBrakeDamping;

                // ⚡ [F = ma] อัดความเร่งเข้ากำปั้นตรงๆ — ความเร็วปะทะเกิดจาก v = a·t จริงๆ
                // ดาเมจปลายทางคิดจาก impulse ตอนชน (ใน PhysicsDamageSender)
                if (handRb.linearVelocity.magnitude < maxPunchSpeed)
                    handRb.AddForce(punchDir * punchAcceleration, ForceMode.Acceleration);
                break;
            }

            case CombatState.Recovering:
            {
                // ✅ ค่อยๆ lerp damper/reach กลับเป็นค่าปกติ ไม่ snap
                float t = recoveryTimer / Mathf.Max(recoveryBlendDuration, 0.001f);
                float smooth = Mathf.SmoothStep(0f, 1f, t);

                handDamper = Mathf.Lerp(punchDamper, originalHandDamper, smooth);
                extraReach = Mathf.Lerp(punchExtraReach, 0f, smooth);

                base.PerformArmMovement();

                handDamper = originalHandDamper;
                break;
            }

            default: // Idle
            {
                extraReach = 0f;
                base.PerformArmMovement();
                break;
            }
        }
    }
}
