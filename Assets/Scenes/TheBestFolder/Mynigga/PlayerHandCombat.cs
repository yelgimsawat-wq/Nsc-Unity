using UnityEngine;
using Unity.Netcode;
using NscGame.Enemy;

public class PlayerHandCombat : PlayerHandMovement
{
    public enum CombatState { Idle, Punching, Recovering }

    [Header("Combat Settings")]
    public NetworkVariable<CombatState> currentCombatState = new NetworkVariable<CombatState>(CombatState.Idle);

    [Header("Punch Acceleration (Shift = คันเร่งของหมัด)")]
    [Tooltip("ความเร่งที่อัดเข้ากำปั้นขณะกด Shift ค้าง (m/s²) — ที่มาของ F = ma\n" +
             "เมาส์ + W/S ยังคุมทิศได้ตลอดเวลา = หมัดนำวิถี")]
    public float punchAcceleration = 400f;
    [Tooltip("ความเร็วหมัดสูงสุด — กันทะลุ collider และเป็นเพดานดาเมจไปในตัว")]
    public float maxPunchSpeed = 55f;
    [Tooltip("เวลาบูสต์สูงสุดต่อการกดหนึ่งครั้ง กันกด Shift ค้างลากตัวบินไปเรื่อยๆ")]
    public float maxPunchDuration = 0.75f;
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
             "กันเคสหมัดถึงเป้าช้ากว่าจังหวะปล่อย Shift/หมดเวลาแค่เสี้ยววินาทีแล้วดาเมจหาย")]
    public float damageGraceTime = 0.2f;

    private float punchTimer = 0f;
    private float recoveryTimer = 0f;

    [Header("Aim Assist (ช่วยเล็ง)")]
    [Tooltip("ถ้ามีศัตรูอยู่ในกรวยรอบทิศหมัด หมัดจะเบนเข้าหาให้เองแบบ realtime\n" +
             "เล็งหลวมๆ ก็เข้าเป้า — หัวใจของ 'เล่นง่าย'")]
    public bool enableAimAssist = true;
    [Tooltip("มุมกรวยช่วยเล็ง (องศา) — กว้าง = ดูดแรง, 0 = ปิด")]
    public float aimAssistAngle = 30f;

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
        base.Update(); // ให้คลาสแม่ทำงานปกติ (รวมถึงการเล็งด้วยเมาส์ + W/S ระหว่างต่อย)

        if (!IsOwner || currentState.Value != HandState.Attached) return;

        HandleCombatInput();
    }

    private bool punchHeld = false;
    private float nextPunchRequestTime = 0f;

    private void HandleCombatInput()
    {
        // ⚡ กด Shift ค้าง = อัดความเร่งเข้าหมัด / ปล่อย = หยุดบูสต์
        if (Input.GetKeyDown(KeyCode.LeftShift)) punchHeld = true;
        if (Input.GetKeyUp(KeyCode.LeftShift)) { punchHeld = false; SetPunchingRpc(false); }

        // ✅ [Punch Buffer] ค้าง Shift ไว้ = ต่อยรัวอัตโนมัติ
        // พอ Recovery จบ (state กลับ Idle) หมัดถัดไปออกเองทันที ไม่ต้องปล่อยแล้วกดใหม่
        // (throttle 0.1s กันสแปม RPC ระหว่างรอ state sync กลับมา)
        if (punchHeld && currentCombatState.Value == CombatState.Idle && Time.time >= nextPunchRequestTime)
        {
            nextPunchRequestTime = Time.time + 0.1f;
            SetPunchingRpc(true);
        }
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
    private static readonly Collider[] _assistBuffer = new Collider[32];
    private Vector3 ApplyAimAssist(Vector3 punchDir)
    {
        float range = maxArmLength + punchExtraReach;
        int count = Physics.OverlapSphereNonAlloc(PivotPosition, range, _assistBuffer);

        float bestAngle = aimAssistAngle;
        Vector3 bestDir = punchDir;

        for (int i = 0; i < count; i++)
        {
            EnemyHealth enemy = _assistBuffer[i].GetComponentInParent<EnemyHealth>();
            if (enemy == null) continue;

            Vector3 toEnemy = _assistBuffer[i].bounds.center - PivotPosition;
            float angle = Vector3.Angle(punchDir, toEnemy);
            if (angle < bestAngle)
            {
                bestAngle = angle;
                bestDir = toEnemy.normalized;
            }
        }
        return bestDir;
    }

    protected override void FixedUpdate()
    {
        base.FixedUpdate(); // ให้คลาสแม่รันฟิสิกส์ปกติ

        if (!IsServer) return;

        if (currentCombatState.Value == CombatState.Punching)
        {
            // ✅ จำความเร็วสูงสุดของหมัดรอบนี้ไว้เป็นฐานคิดดาเมจ
            if (handRb != null)
                peakPunchSpeed = Mathf.Max(peakPunchSpeed, handRb.linearVelocity.magnitude);

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
