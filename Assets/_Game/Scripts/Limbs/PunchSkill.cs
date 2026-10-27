using System;
using Nsc.Combat;
using UnityEngine;

namespace Nsc.Limbs
{
    // Charging ต่อท้าย — ค่า byte ของสถานะเดิมต้องไม่เปลี่ยน (sync ผ่าน NetworkVariable)
    public enum PunchState : byte { Idle, Punching, Recovering, Charging }

    /// <summary>
    /// ท่าต่อย (คลิกซ้าย = คันเร่งของหมัด) — ลอจิกฝั่ง server ของหมัดหนึ่งครั้ง
    /// ArmController ถามท่านี้ทุก physics tick ว่า "tick นี้แขนควรขยับแบบไหน" (ArmMotion)
    /// แทนการสืบทอดคลาสแขนแล้วเขียนทับฟิลด์ชั่วคราวแบบเดิม (ถ้าลืมคืนค่าตัวไหน ฟีลแขนเปลี่ยนเงียบๆ)
    ///
    /// วงจร: Idle → (กด) ง้าง → พุ่ง (อัดความเร่ง F = ma) → (ปล่อย/หมดเวลา/ชน) Recovering → Idle
    /// เมาส์ยังคุมเป้ามือได้ตลอดแม้กำลังต่อย = หมัดนำวิถี (เล็งขึ้น = อัปเปอร์คัต ไม่ต้องมี animation)
    /// </summary>
    [Serializable]
    public class PunchSkill : IStrikeSource
    {
        [Header("Punch Acceleration")]
        [Tooltip("ความเร่งที่อัดเข้ากำปั้นขณะกดคลิกซ้ายค้าง (m/s²)")]
        public float acceleration = 400f;
        [Tooltip("ความเร็วหมัดสูงสุด — กันทะลุ collider และเป็นเพดานดาเมจไปในตัว")]
        public float maxSpeed = 55f;
        [Tooltip("เวลาบูสต์สูงสุดต่อการกดหนึ่งครั้ง กันกดค้างลากตัวบินไปเรื่อยๆ")]
        public float maxDuration = 0.75f;
        [Tooltip("เวลาง้างหมัด — ดึงมือกลับเข้าไหล่ก่อนปล่อย ให้ทุกหมัดมีระยะเร่งเต็มๆ")]
        public float windupTime = 0.12f;
        [Tooltip("แรงต้านสปริงตอนต่อย (ต่ำ = พุ่งทะลวง แต่ต่ำกว่า ~4 จะคุมปลายหมัดไม่อยู่)")]
        public float damper = 5f;
        [Tooltip("ระยะยืดพิเศษตอนต่อย — ให้หมัดยืดเกินระยะแขนปกติได้")]
        public float extraReach = 5f;
        [Tooltip("สเกลเบรกปลายทางตอนต่อย 0 = ปิดเบรก / 1 = เบรกเท่าปกติ")]
        [Range(0f, 1f)] public float brakeScale = 0.05f;

        [Header("Charge (โหมดต่อสู้: กดค้าง = ง้างชาร์จ / ปล่อย = ต่อย)")]
        [Tooltip("เปิด = กดค้างง้างหมัดสะสมแรง ปล่อยแล้วพุ่ง (หมัดหุ่นยักษ์แบบ Pacific Rim) | ปิด = แบบเดิม กดค้างเร่งจนปล่อย")]
        public bool chargeToRelease = false;
        [Tooltip("ง้างนานเท่านี้ (หลัง windupTime) = ชาร์จเต็ม")]
        public float chargeTime = 0.8f;
        [Tooltip("ตัวคูณความเร็ว/ความเร่งหมัด ตอนแตะแล้วปล่อยทันที → ตอนชาร์จเต็ม")]
        public float tapSpeedScale = 0.75f;
        public float fullChargeSpeedScale = 1.3f;
        [Tooltip("ปล่อยแล้วอัดความเร่งนานเท่านี้ (โหมดชาร์จ ไม่ขึ้นกับการกดค้าง)")]
        public float thrustTime = 0.3f;

        [Header("Recovery Blend")]
        [Tooltip("เวลาที่ damper/reach ค่อยๆ กลับสู่ค่าปกติหลังหมัดจบ — เป็น cooldown ระหว่างหมัดไปในตัว")]
        public float recoveryBlendDuration = 0.25f;
        [Tooltip("ช่วงผ่อนผันหลังหมัดจบที่การชนยังนับดาเมจได้ (หมัดถึงเป้าช้ากว่าจังหวะปล่อยเสี้ยววินาที)")]
        public float damageGraceTime = 0.2f;

        [Header("Aim Assist")]
        [Tooltip("มีศัตรูในกรวยรอบทิศหมัด → หมัดเบนเข้าหาเองแบบ realtime เล็งหลวมๆ ก็เข้าเป้า")]
        public bool enableAimAssist = true;
        [Tooltip("มุมกรวยช่วยเล็ง (องศา) 0 = ปิด")]
        public float aimAssistAngle = 30f;
        [Tooltip("Layer ที่สแกนหาศัตรู — ปล่อย Everything แล้ว buffer จะเต็มด้วยกำแพงก่อนถึงศัตรู")]
        public LayerMask aimAssistLayer = ~0;

        [Header("Debug")]
        [Tooltip("log สรุปทุกหมัด (ความเร็วพีค/ดาเมจที่จะได้/โดนเป้าไหม)")]
        public bool debugLog = true;

        private static readonly Collider[] AssistBuffer = new Collider[64];

        [NonSerialized] private PunchState state;
        [NonSerialized] private float punchTimer;
        [NonSerialized] private float recoveryTimer;
        [NonSerialized] private float peakSpeed;
        [NonSerialized] private bool hasHitThisPunch;
        [NonSerialized] private LimbStrike strike;
        [NonSerialized] private string ownerName;
        [NonSerialized] private Nsc.Robots.Robot self;
        [NonSerialized] private float chargeTimer;
        [NonSerialized] private bool fireQueued;
        [NonSerialized] private float speedScale = 1f;

        public PunchState State => state;
        public bool IsActive => state == PunchState.Punching;
        /// <summary>แรงชาร์จ 0..1 — ตอนง้างคือที่สะสมอยู่ ตอนพุ่งคือที่ปล่อยออกไป</summary>
        public float Charge01 { get; private set; }

        /// <summary>หุ่นเจ้าของ — ใช้ข้ามชิ้นตัวเองและเลือกเป้าตามทีม (null = เล็งเฉพาะ Team.Enemy แบบเดิม)</summary>
        public void SetOwner(Nsc.Robots.Robot owner) => self = owner;

        /// <summary>ArmController เรียกตอน Awake</summary>
        public void Init(LimbStrike limbStrike, string owner)
        {
            strike = limbStrike;
            ownerName = owner;
        }

        // ================================================================
        //  IStrikeSource
        // ================================================================

        public bool CanDealDamage() =>
            !hasHitThisPunch &&
            (state == PunchState.Punching ||
             (state == PunchState.Recovering && recoveryTimer <= damageGraceTime));

        public float PeakSpeed() => peakSpeed;

        public DamageSource Source() => DamageSource.Punch;

        /// <summary>ปะทะแล้วจบหมัดทันที — ให้ความรู้สึก "ชนแล้วจบ" ไม่ใช่ไถลถูเป้าต่อ</summary>
        public void ResolveStrike(bool landed)
        {
            if (landed) hasHitThisPunch = true;
            if (state == PunchState.Punching) Release();
        }

        // ================================================================
        //  วงจรหมัด
        // ================================================================

        /// <summary>เริ่มหมัดใหม่ — คืน false ถ้ายังไม่ว่าง (หมัดก่อนยัง Recovering)</summary>
        public bool Begin()
        {
            if (state != PunchState.Idle) return false;

            peakSpeed = 0f;
            hasHitThisPunch = false;
            if (chargeToRelease)
            {
                state = PunchState.Charging;
                chargeTimer = 0f;
                fireQueued = false;
                Charge01 = 0f;
                return true;
            }

            state = PunchState.Punching;
            punchTimer = maxDuration;
            speedScale = 1f;
            return true;
        }

        /// <summary>ผู้เล่นปล่อยคลิก — โหมดชาร์จ: ยิงหมัด (รอให้ง้างครบ windupTime ก่อน) | โหมดเดิม: หยุดบูสต์</summary>
        public void ReleaseInput()
        {
            if (state == PunchState.Charging) fireQueued = true;
            else if (!chargeToRelease) Release();
        }

        private void Fire()
        {
            Charge01 = Mathf.Clamp01((chargeTimer - windupTime) / Mathf.Max(chargeTime, 0.01f));
            speedScale = Mathf.Lerp(tapSpeedScale, fullChargeSpeedScale, Charge01);
            state = PunchState.Punching;
            punchTimer = thrustTime;
        }

        /// <summary>ปล่อยคลิก/หมดเวลา/ชน → เข้า Recovering</summary>
        public void Release()
        {
            if (state != PunchState.Punching) return;

            state = PunchState.Recovering;
            recoveryTimer = 0f;

            if (debugLog)
            {
                string damage = strike == null
                    ? "?"
                    : strike.PreviewDamage(peakSpeed) <= 0f
                        ? $"0 (พีคต่ำกว่าเกณฑ์ {strike.MinVelocityThreshold})"
                        : $"{strike.PreviewDamage(peakSpeed):F1}";
                Debug.Log($"👊 [{ownerName}] หมัดจบ | Peak: {peakSpeed:F1} m/s | ดาเมจถ้าเข้าเป้า: {damage} | " +
                          $"โดนเป้า: {(hasHitThisPunch ? "✅" : "❌ วืด")}");
            }
        }

        public void Reset()
        {
            state = PunchState.Idle;
            fireQueued = false;
            Charge01 = 0f;
            punchTimer = 0f;
            recoveryTimer = 0f;
            peakSpeed = 0f;
            hasHitThisPunch = false;
        }

        /// <summary>
        /// แขน tick นี้ควรขยับแบบไหน — รับท่าปกติ แล้วคืนท่าที่ปรับตามจังหวะหมัด
        /// accelerationOut = แรงเร่งที่ต้องอัดเพิ่มเข้ากำปั้น (zero ถ้าไม่ต้อง)
        /// </summary>
        public ArmMotion DriveTarget(ArmMotion normal, Vector3 pivot, Vector3 handPosition, Vector3 handVelocity,
                                     Vector3 fallbackForward, float maxArmLength, out Vector3 accelerationOut)
        {
            accelerationOut = Vector3.zero;

            switch (state)
            {
                case PunchState.Charging:
                case PunchState.Punching:
                {
                    // ทิศพุ่ง = เส้นตรงจากหัวไหล่ผ่านจุดที่ผู้เล่นเล็ง
                    Vector3 punchDir = normal.target - pivot;
                    if (punchDir.sqrMagnitude > 0.0001f) punchDir.Normalize();
                    else
                    {
                        Vector3 outward = handPosition - pivot;
                        punchDir = outward.sqrMagnitude > 0.0001f ? outward.normalized : fallbackForward;
                    }

                    if (enableAimAssist && aimAssistAngle > 0f &&
                        TryFindAimAssistTarget(pivot, punchDir, maxArmLength, out Vector3 assisted, out _))
                        punchDir = assisted;

                    ArmMotion motion = normal;
                    motion.extraReach = extraReach;
                    motion.velocityCap = maxSpeed * speedScale;

                    // ✅ [Wind-up] ดึงมือกลับเข้าใกล้ไหล่ก่อน (ง้าง) — แขนเหยียดค้างแล้วต่อยไม่ออก
                    // โหมดชาร์จ: ง้างค้างตลอดที่กด / โหมดเดิม: ง้างแค่ช่วง windupTime แรก
                    float elapsed = maxDuration - punchTimer;
                    if (state == PunchState.Charging || (!chargeToRelease && elapsed < windupTime))
                    {
                        motion.target = pivot + punchDir * (maxArmLength * 0.25f);
                        return motion;
                    }

                    // ✅ [Force Full Extension] เป้าตอนต่อย = สุดแขน + เผื่อ เสมอ → แขนเหยียดตรงเต็มระยะทุกหมัด
                    motion.target = pivot + punchDir * (maxArmLength + 2f);
                    motion.damper = damper;
                    motion.brakeDamping = normal.brakeDamping * brakeScale;

                    // ⚡ [F = ma] อัดความเร่งเข้ากำปั้นตรงๆ — ความเร็วปะทะเกิดจาก v = a·t จริงๆ
                    if (handVelocity.magnitude < maxSpeed * speedScale)
                        accelerationOut = punchDir * (acceleration * speedScale);
                    return motion;
                }

                case PunchState.Recovering:
                {
                    // ค่อยๆ lerp damper/reach กลับเป็นค่าปกติ ไม่ snap
                    float t = recoveryTimer / Mathf.Max(recoveryBlendDuration, 0.001f);
                    float smooth = Mathf.SmoothStep(0f, 1f, t);

                    ArmMotion motion = normal;
                    motion.damper = Mathf.Lerp(damper, normal.damper, smooth);
                    motion.extraReach = Mathf.Lerp(extraReach, 0f, smooth);
                    return motion;
                }

                default:
                    return normal;
            }
        }

        /// <summary>เดินเวลาของหมัดหลังขยับแขนใน tick นั้น — จำความเร็วพีคไว้เป็นฐานคิดดาเมจ</summary>
        public void Tick(float handSpeed, float dt)
        {
            if (state == PunchState.Charging)
            {
                chargeTimer += dt;
                Charge01 = Mathf.Clamp01((chargeTimer - windupTime) / Mathf.Max(chargeTime, 0.01f));
                // ค้างเต็มเกิน 2 วิ = ปล่อยเอง — กันง้างค้างถาวรเมื่อคำสั่งปล่อยหาย (สลับคนคุมกลางง้าง RPC ของเจ้าของเก่าถูกปฏิเสธ)
                if ((fireQueued && chargeTimer >= windupTime) || chargeTimer > windupTime + chargeTime + 2f) Fire();
            }
            else if (state == PunchState.Punching)
            {
                peakSpeed = Mathf.Max(peakSpeed, handSpeed);
                punchTimer -= dt;
                if (punchTimer <= 0f) Release();
            }
            else if (state == PunchState.Recovering)
            {
                recoveryTimer += dt;
                if (recoveryTimer >= recoveryBlendDuration) state = PunchState.Idle;
            }
        }

        /// <summary>
        /// หาศัตรู (ดู IsAssistTarget) ในกรวยรอบทิศหมัด แล้วเบนทิศเข้าหาเป้าที่มุมแคบสุด
        /// buffer 64 + กรองด้วย aimAssistLayer — ฉากเมือง prop เยอะ buffer เล็กจะเต็มด้วยกำแพงก่อน
        /// </summary>
        public bool TryFindAimAssistTarget(Vector3 pivot, Vector3 punchDir, float maxArmLength,
                                           out Vector3 assistedDirection, out Vector3 targetPosition)
        {
            Vector3 rawDirection = punchDir.sqrMagnitude > 0.0001f ? punchDir.normalized : Vector3.forward;
            assistedDirection = rawDirection;
            targetPosition = Vector3.zero;

            float range = maxArmLength + extraReach;
            int count = Physics.OverlapSphereNonAlloc(pivot, range, AssistBuffer, aimAssistLayer);
            float bestAngle = aimAssistAngle;
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                Collider candidate = AssistBuffer[i];
                if (candidate == null) continue;

                IDamageable target = candidate.GetComponentInParent<IDamageable>();
                if (target == null || !IsAssistTarget(candidate, target.GetTeam())) continue;

                Vector3 candidatePosition = candidate.bounds.center;
                Vector3 toEnemy = candidatePosition - pivot;
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

        /// <summary>
        /// ช่วยเล็งเฉพาะฝ่ายตรงข้ามจริง: รู้เจ้าของ = ข้ามชิ้นหุ่นตัวเอง แล้วเอาทุกทีมที่เป็นศัตรู (บอส + หุ่นอีกทีมใน PVP)
        /// ไม่รู้เจ้าของ = Team.Enemy อย่างเดียวแบบเดิม | ทีม None (ตึก/กระสอบทราย) ไม่ช่วยเล็ง
        /// </summary>
        private bool IsAssistTarget(Collider candidate, Team team)
        {
            if (team == Team.None) return false;
            if (self == null) return team == Team.Enemy;
            return Nsc.Robots.Robot.FromCollider(candidate) != self && self.GetTeam().IsHostileTo(team);
        }
    }

    /// <summary>
    /// การขยับแขนหนึ่ง physics tick — PunchSkill ปรับค่าพวกนี้แทนการเขียนทับฟิลด์ของแขนแล้วคืนค่า
    /// </summary>
    public struct ArmMotion
    {
        public Vector3 target;
        public float velocityCap;
        public float damper;
        public float brakeDamping;
        public float extraReach;
    }
}
