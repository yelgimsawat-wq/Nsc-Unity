using NscGame.Enemy; // AttackType
using Unity.Netcode;
using UnityEngine;

namespace NscGame.Pvp
{
    /// <summary>
    /// ตัวส่งดาเมจ PVP — วางบน Rigidbody ของชิ้นส่วนที่ใช้ตี (มือซ้าย/ขวา และเท้าถ้าอยากให้เตะได้)
    ///
    /// ⚠️ ไม่มีระบบเลือดของตัวเอง — ยิงดาเมจเข้า <see cref="RobotHealth"/> เดิมของชิ้นส่วนที่โดนตี
    ///    ผลที่ได้จึงเหมือนโดนบอสตีทุกอย่าง: เลือดชิ้นนั้นลด → หมดแล้วชิ้นหลุด →
    ///    เพดานเลือดลดถาวร → PlayerHUD เดิม (HullRingUI/LimbStatusUI) อัปเดตเองอัตโนมัติ
    ///
    /// ต่างจาก PhysicsDamageSender เดิมยังไง:
    ///   - ตัวเดิมมองหา EnemyHealth (บอส AI) เท่านั้น ตีหุ่นผู้เล่นด้วยกันไม่เข้า
    ///   - ตัวนี้มองหา RobotHealth ของหุ่นอีกทีม + เช็คทีมก่อนเสมอ (ตีเพื่อน/ตีตัวเองไม่เข้า)
    ///   - นับดาเมจเฉพาะตอน MatchState == Fighting (ช่วงเลือกทีม/จบแมตช์ตีไม่เข้า)
    ///
    /// ใส่คู่กับ PhysicsDamageSender เดิมได้ ทั้งสองตัวแยกกันทำงานคนละเป้า
    ///
    /// สูตรดาเมจ (ยึดตามระบบหมัดเดิม F = ma):
    ///   - ถ้าชิ้นนี้มี PlayerHandCombat → ดาเมจ = ความเร็วพีคของหมัด × speedToDamage
    ///     (ใช้ความเร็วพีค ไม่ใช่ impulse ตอนชน → ชนเฉียง/เป้าถอย ดาเมจไม่หาย)
    ///   - ถ้าไม่มี (เช่นเท้า) → ดาเมจ = แรงปะทะจริง (impulse/Δt) × forceToDamage
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PvpDamageSender : MonoBehaviour
    {
        #region Inspector

        [Header("Damage")]
        [Tooltip("ความเร็วขั้นต่ำที่จะเริ่มนับดาเมจ (กันครูดเบาๆ แล้วเลือดลด)")]
        public float minVelocityThreshold = 8f;

        [Tooltip("ตัวคูณความเร็วพีคของหมัด (m/s) → ดาเมจ | ใช้กับชิ้นที่มี PlayerHandCombat\n" +
                 "ใช้ค่าเดียวกับตอนตีบอส — RobotHealth เอาดาเมจไปคูณ 2 เป็นแรงกระเด็น ตั้งสูงแล้วหุ่นจะปลิว")]
        public float speedToDamage = 0.6f;

        [Tooltip("ตัวคูณแรงปะทะจริง (นิวตัน) → ดาเมจ | ใช้กับชิ้นที่ไม่มีระบบต่อย เช่นเท้า")]
        public float forceToDamage = 0.01f;

        [Tooltip("เพดานดาเมจต่อการชนหนึ่งครั้ง")]
        public float maxDamagePerHit = 60f;

        [Tooltip("ตัวคูณดาเมจตอนต่อยเข้าแขน/ขาตรงๆ (เล็งแม่นควรได้รางวัล)")]
        public float limbDamageMultiplier = 1f;

        [Tooltip("ตัวคูณดาเมจตอนต่อยเข้าลำตัว — ดาเมจจะถูกหารกระจายให้ชิ้นที่ยังต่ออยู่ทุกชิ้น\n" +
                 "ตั้ง 1 = ต่อยลำตัวได้ดาเมจรวมเท่าต่อยแขน แต่กระจายทั่วตัวแทนที่จะกองที่ชิ้นเดียว")]
        public float bodyDamageMultiplier = 1f;

        [Header("Rules")]
        [Tooltip("เปิด = ตีเพื่อนร่วมทีมเข้าด้วย (ปกติปิดไว้)")]
        public bool friendlyFire = false;

        [Header("Debug")]
        public bool debugLog = true;

        #endregion

        private PlayerHandCombat combat;  // มีเฉพาะบนมือที่ใช้ระบบต่อย
        private PvpRobotTeam ownRobot;    // หุ่นที่ชิ้นส่วนนี้สังกัด (= ทีมของเรา)
        private bool warnedMissingTeam;

        private void Awake()
        {
            combat = GetComponent<PlayerHandCombat>();
        }

        private void Start()
        {
            // resolve ที่ Start ไม่ใช่ Awake — PvpRobotTeam ต้อง Register ตัวเองใน Awake ก่อน
            ResolveOwnTeam();
        }

        private void ResolveOwnTeam()
        {
            ownRobot = PvpRobotTeam.FindByPart(transform);

            // เตือนครั้งเดียวพอ — ตัวนี้ถูกเรียกซ้ำทุกครั้งที่ชน จะสแปม Console จนอ่านอะไรไม่ออก
            if (ownRobot == null && !warnedMissingTeam)
            {
                warnedMissingTeam = true;
                Debug.LogWarning($"[PVP] '{name}' หา PvpRobotTeam ของหุ่นตัวเองไม่เจอ — " +
                                 "ตรวจว่าใส่ PvpRobotTeam บนลำตัวหุ่น และ robotRoot ชี้ root ถูกตัวหรือยัง", this);
            }
        }

        private PvpTeam MyTeam => ownRobot != null ? ownRobot.Team : PvpTeam.None;

        private void OnCollisionEnter(Collision collision)
        {
            // ดาเมจตัดสินบน Server เท่านั้น — client แค่เห็นผลลัพธ์ผ่าน NetworkVariable ของ RobotHealth
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

            // ตีได้เฉพาะตอนแมตช์กำลังดำเนินอยู่
            if (PvpTeamManager.Instance != null && !PvpTeamManager.Instance.IsFighting)
            {
                Reject(collision.relativeVelocity.magnitude,
                    $"แมตช์ยังไม่เริ่มสู้ (state = {PvpTeamManager.Instance.MatchState}) → ต้องกด START FIGHT ก่อน");
                return;
            }

            if (collision.contactCount == 0) return; // CCD บางเคสให้ contact = 0

            if (ownRobot == null) ResolveOwnTeam(); // เผื่อหุ่นถูก spawn/สลับหลัง Start

            float impactSpeed = collision.relativeVelocity.magnitude;

            // ไม่รู้ว่าตัวเองทีมไหน = ตัดสินไม่ได้ว่าใครเป็นศัตรู → ไม่นับดาเมจ
            // (ปล่อยผ่านจะกลายเป็นตีหุ่นตัวเองเลือดลด เพราะเช็คทีมไม่ได้)
            if (ownRobot == null)
            {
                Reject(impactSpeed, "ไม่มี PvpRobotTeam บนลำตัวหุ่น → " +
                                    "สั่ง Tools ▸ NSC ▸ PVP ▸ Setup Robots In Scene");
                return;
            }

            // หา "หุ่น" ที่เป็นเจ้าของ collider ที่เราชน
            //
            // เทียบด้วย PvpRobotTeam ไม่ใช่ transform.root เพราะหุ่นสองตัวอาจถูกวางไว้
            // ใต้ parent เดียวกัน ซึ่งจะทำให้ root เท่ากันแล้วบล็อกดาเมจทั้งหมด
            PvpRobotTeam targetRobot = PvpRobotTeam.FindByPart(collision.transform);

            bool validTarget = targetRobot != null
                            && targetRobot != ownRobot
                            && (friendlyFire || targetRobot.Team != MyTeam)
                            && !targetRobot.IsDefeated;

            if (!validTarget)
            {
                string why = targetRobot == null
                    ? $"'{collision.collider.name}' ไม่ได้อยู่ในหุ่นที่ลงทะเบียนไว้ (กำแพง/พื้น/prop)"
                    : targetRobot == ownRobot
                        ? "ชนชิ้นส่วนของหุ่นตัวเอง"
                        : targetRobot.IsDefeated
                            ? $"หุ่นทีม {targetRobot.Team.DisplayName()} แพ้ไปแล้ว"
                            : $"เป็นเพื่อนร่วมทีม {targetRobot.Team.DisplayName()} (friendlyFire ปิด)";
                Reject(impactSpeed, why);
            }

            if (validTarget)
            {
                // ชิ้นส่วนที่โดนตีจริงๆ — เลือดอยู่ที่ชิ้น ไม่ใช่ที่หุ่นทั้งตัว
                RobotHealth hitPart = collision.collider.GetComponentInParent<RobotHealth>();

                if (!TryComputeDamage(collision, impactSpeed, out float damage))
                {
                    Reject(impactSpeed, combat != null && !combat.CanDealDamage
                        ? "หมัดนี้จบไปแล้ว หรือเคยเข้าเป้าแล้วรอบนี้ (CanDealDamage = false)"
                        : $"ความเร็ว {impactSpeed:F1} m/s ต่ำกว่าเกณฑ์ {minVelocityThreshold}");
                }
                else
                {
                    Vector3 hitDirection = -collision.contacts[0].normal; // normal ชี้ออกจากพื้นผิว → กลับทิศ
                    AttackType type = AttackTypeOfThisPart();
                    bool applied;

                    if (hitPart != null)
                    {
                        // เข้าระบบเดิมทั้งหมด: เลือดลด → หลุด → เพดานลด → HUD อัปเดตเอง
                        // overload ที่รับ direction จะใส่ knockback ให้ด้วย ไม่ต้องผลักเอง
                        float hpBefore = hitPart.currentHp.Value;
                        hitPart.ServerTakeDamage(damage * limbDamageMultiplier, type, hitDirection);
                        applied = true;

                        // log เลือดก่อน→หลัง เพราะ PlayerHUD โชว์แต่หุ่นของตัวเอง
                        // ตอนตีหุ่นศัตรูจึงไม่มี UI ไหนยืนยันว่าดาเมจเข้า ต้องดูจาก Console
                        if (debugLog)
                            Debug.Log($"[PVP] 🥊 '{name}' (ทีม {MyTeam.DisplayName()}) → " +
                                      $"'{hitPart.name}' ของทีม {targetRobot.Team.DisplayName()} | " +
                                      $"ดาเมจ {damage * limbDamageMultiplier:F1} | " +
                                      $"เลือด {hpBefore:F0} → {hitPart.currentHp.Value:F0} / {hitPart.currentMaxHp.Value:F0}");
                    }
                    else
                    {
                        // โดนลำตัว/ชิ้นที่ไม่มีเลือดของตัวเอง → ลงที่ชิ้นส่วนที่ใกล้จุดปะทะสุด
                        applied = targetRobot.ServerApplyBodyDamage(
                            damage * bodyDamageMultiplier, type, hitDirection, collision.contacts[0].point);

                        if (!applied)
                            Reject(impactSpeed, $"โดน '{collision.collider.name}' ของทีม " +
                                                $"{targetRobot.Team.DisplayName()} แต่หุ่นตัวนั้นไม่มีชิ้นส่วนที่รับดาเมจได้แล้ว");
                    }

                    if (applied)
                    {
                        // หนึ่งหมัด = หนึ่งดาเมจ (ล็อกไม่ให้ครูดต่อแล้วนับซ้ำ)
                        if (combat != null) combat.NotifyPunchImpact();
                        return;
                    }
                }
            }

            // ไม่ใช่เป้าที่ตีได้ (กำแพง/พื้น/หุ่นตัวเอง/ลำตัวที่ไม่มี RobotHealth)
            // → จบหมัดแต่ไม่ล็อกดาเมจ ถ้าหมัดปัดต่อไปโดนคู่แข่งในช่วง grace ยังนับอยู่
            if (combat != null && impactSpeed >= minVelocityThreshold)
                combat.NotifyPunchBlocked();
        }

        private float _nextRejectLogTime;

        /// <summary>
        /// ฟ้องว่าทำไมการชนนี้ไม่นับดาเมจ — เดิมโค้ดนี้ return เงียบๆ 4 ที่
        /// ทำให้เวลาต่อยแล้วเลือดไม่ลด ไม่มีทางรู้เลยว่าติดเงื่อนไขไหน
        ///
        /// กรองสองชั้นกัน Console ท่วม: นับแค่การชนที่แรงพอจะเป็นหมัดจริง และหน่วงเวลาต่อชิ้นส่วน
        /// </summary>
        private void Reject(float impactSpeed, string reason)
        {
            if (!debugLog) return;
            if (impactSpeed < minVelocityThreshold) return;
            if (Time.time < _nextRejectLogTime) return;

            _nextRejectLogTime = Time.time + 0.5f;
            Debug.Log($"[PVP] ⛔ '{name}' ชนที่ {impactSpeed:F1} m/s แต่ไม่นับดาเมจ — {reason}", this);
        }

        /// <summary>มือ = หมัด, ชิ้นอื่น (เท้า) = เตะ — ส่งให้ RobotHealth ไว้แยกประเภทเอฟเฟกต์</summary>
        private AttackType AttackTypeOfThisPart()
            => combat != null ? AttackType.LightPunch : AttackType.Kick;

        /// <summary>คำนวณดาเมจตามชนิดของชิ้นส่วน — คืน false ถ้าไม่ควรนับการชนนี้</summary>
        private bool TryComputeDamage(Collision collision, float impactSpeed, out float damage)
        {
            damage = 0f;

            if (combat != null)
            {
                // [มือที่มีระบบต่อย] นับเฉพาะตอนหมัดพุ่งจริง และหมัดนี้ยังไม่เคยเข้าเป้า
                if (!combat.CanDealDamage) return false;

                float punchSpeed = combat.PeakPunchSpeed;
                if (punchSpeed < minVelocityThreshold) return false;

                damage = Mathf.Min(punchSpeed * speedToDamage, maxDamagePerHit);
                return true;
            }

            // [ชิ้นส่วนทั่วไป เช่นเท้า] ใช้แรงปะทะจริง: F = m·Δv/Δt = impulse/Δt
            if (impactSpeed < minVelocityThreshold) return false;

            float impactForce = collision.impulse.magnitude / Time.fixedDeltaTime;
            damage = Mathf.Min(impactForce * forceToDamage, maxDamagePerHit);
            return damage > 0f;
        }
    }
}
