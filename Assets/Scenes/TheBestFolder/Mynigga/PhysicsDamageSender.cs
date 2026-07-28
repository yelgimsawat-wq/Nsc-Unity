using UnityEngine;
using Unity.Netcode;
using NscGame.Enemy; // สำหรับเข้าถึง EnemyHealth

[RequireComponent(typeof(Rigidbody))]
public class PhysicsDamageSender : MonoBehaviour
{
    [Header("Damage Settings (F = ma)")]
    [Tooltip("ความเร็วขั้นต่ำในการชนที่จะเกิดดาเมจ (กันชนเบาๆ แล้วเลือดลด)")]
    public float minVelocityThreshold = 8f;

    [Tooltip("ตัวคูณแปลงแรงปะทะจริง (นิวตัน) เป็นดาเมจ — ใช้กับชิ้นส่วนที่ไม่มีระบบต่อย\n" +
             "F = ma = impulse/Δt")]
    public float forceToDamage = 0.01f;

    [Tooltip("ตัวคูณแปลงความเร็วพีคของหมัด (m/s) เป็นดาเมจ — ใช้กับมือที่มี PlayerHandCombat\n" +
             "เช่น หมัดพีค 55 m/s × 0.6 = ดาเมจ 33 → ค่าคงที่ต่อหมัด ไม่แกว่งตามมุมชน")]
    public float speedToDamage = 0.6f;

    [Tooltip("ตัวคูณความเร็วพีคของ Charge Kick (m/s) เป็นดาเมจ")]
    public float kickSpeedToDamage = 1.2f;

    [Tooltip("เพดานดาเมจต่อการชนหนึ่งครั้ง")]
    public float maxDamagePerHit = 60f;

    [Header("Knockback Settings (ยิ่งต่อยแรงยิ่งเด้งแรง)")]
    // สูตรเดิม: base(5) + speed × 0.3 → ต่อยแผ่ว 7.4 / ต่อยเต็มแรง 21.5
    // ตัวคงที่ 5 กินสัดส่วนใหญ่ตอนต่อยเบา ทำให้ "เด้งแรงเท่ากันหมด" และแรงเกินโดยรวม
    // สูตรใหม่: ไล่จาก min → max ตามสัดส่วนความเร็วจริง + ปรับความโค้งได้
    [Tooltip("แรงเด้งตอนต่อยแผ่วสุดที่ยังคิดดาเมจ (ความเร็ว = Min Velocity Threshold)")]
    public float minKnockbackForce = 2f;

    [Tooltip("แรงเด้งตอนต่อยเต็มแรง (ความเร็วถึง Knockback Reference Speed)")]
    public float maxKnockbackForce = 12f;

    [Tooltip("ความโค้งการไล่แรง\n1 = เชิงเส้น | >1 = ต่อยเบาแทบไม่เด้ง แต่ต่อยเต็มแรงยังเด้งสุด (แนะนำ 1.5-2.5)")]
    [Range(0.5f, 4f)]
    public float knockbackCurve = 2f;

    [Tooltip("ความเร็วที่ถือว่า 'ต่อยเต็มแรง' (m/s) — ตั้งให้ตรงกับ Max Punch Speed ของ PlayerHandCombat")]
    public float knockbackReferenceSpeed = 55f;

    [Header("VFX/SFX (Optional)")]
    [Tooltip("Particle effect เมื่อชนเกิดดาเมจ")]
    public GameObject impactVfxPrefab;

    [Tooltip("เสียงชนเกิดดาเมจ")]
    public AudioClip impactSfx;

    private Rigidbody rb;
    private AudioSource audioSource;
    private PlayerHandCombat combat; // ถ้าติดบนมือที่มีระบบต่อย: คิดดาเมจเฉพาะตอนปล่อยหมัด (Shift)
    private PlayerLegCombat legCombat;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        combat = GetComponent<PlayerHandCombat>();
        legCombat = GetComponent<PlayerLegCombat>();

        // สร้าง AudioSource ถ้ายังไม่มี
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null && impactSfx != null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f; // 3D sound
        }
    }

    /// <summary>
    /// แปลงความเร็วปะทะ → แรงเด้ง แบบไล่สัดส่วนจริง (ต่อยเบา = เด้งน้อย / ต่อยเต็มแรง = เด้งสุด)
    /// knockbackCurve ยกกำลังทำให้ช่วงต้นแบนราบ — หมัดแผ่วๆ แทบไม่ขยับตัวบอส
    /// </summary>
    private float ComputeKnockback(float speed)
    {
        float top = Mathf.Max(knockbackReferenceSpeed, minVelocityThreshold + 0.01f);
        float t = Mathf.InverseLerp(minVelocityThreshold, top, speed);
        return Mathf.Lerp(minKnockbackForce, maxKnockbackForce, Mathf.Pow(t, knockbackCurve));
    }

    void OnCollisionEnter(Collision collision)
    {
        // ✅ [Bug Fix] ข้ามการชนกับชิ้นส่วนหุ่นตัวเอง
        // เดิมมือครูดขา/ลำตัวตัวเองก็นับเป็นการปะทะ → หมัดจบก่อนถึงเป้า
        if (collision.transform.root == transform.root)
            return;

        // ✅ [Bug Fix] กัน IndexOutOfRange จาก CCD บางเคสที่ contactCount = 0
        if (collision.contactCount == 0)
            return;

        // คำนวณความเร็วสัมพัทธ์ (Relative Velocity) จังหวะที่ชน
        float impactSpeed = collision.relativeVelocity.magnitude;

        float finalDamage;
        float finalKnockback;

        if (combat != null)
        {
            // ✅ [มือที่มีระบบต่อย] ดาเมจคิดจาก "ความเร็วพีคของหมัดรอบนี้"
            // - นับเฉพาะระหว่าง Punching หรือช่วงผ่อนผันสั้นๆ หลังปล่อย (damageGraceTime)
            //   → แก้อาการดาเมจติดๆ ดับๆ ตอนหมัดถึงเป้าช้ากว่าจังหวะ state สลับ
            // - ใช้ความเร็วพีค ไม่ใช่ impulse ตอนชน → ชนเฉียง/เป้าถอย ดาเมจไม่หายอีก
            // - hasHitThisPunch ล็อกให้หนึ่งหมัด = หนึ่งดาเมจ
            if (!combat.CanDealDamage)
                return;

            float punchSpeed = combat.PeakPunchSpeed;
            if (punchSpeed < minVelocityThreshold)
                return;

            finalDamage = Mathf.Min(punchSpeed * speedToDamage, maxDamagePerHit);
            finalKnockback = ComputeKnockback(punchSpeed);
        }
        else if (legCombat != null)
        {
            // A foot deals damage only during an intentional Charge Kick.
            if (!legCombat.CanDealDamage)
                return;

            float kickSpeed = legCombat.PeakKickSpeed;
            if (kickSpeed < minVelocityThreshold)
                return;

            finalDamage = Mathf.Min(kickSpeed * kickSpeedToDamage, maxDamagePerHit);
            finalKnockback = ComputeKnockback(kickSpeed);
        }
        else
        {
            // [ชิ้นส่วนทั่วไป] ใช้แรงปะทะจริงเหมือนเดิม: F = m·Δv/Δt = impulse/Δt
            if (impactSpeed < minVelocityThreshold)
                return;

            float impactForce = collision.impulse.magnitude / Time.fixedDeltaTime;
            finalDamage = Mathf.Min(impactForce * forceToDamage, maxDamagePerHit);
            finalKnockback = ComputeKnockback(impactSpeed);
        }

        // ทิศทางการชน (จากจุดชนแรก)
        Vector3 hitDirection = collision.contacts[0].normal * -1f; // กลับทิศเพราะ normal ชี้ออกจากพื้นผิว
        Vector3 impactPoint = collision.contacts[0].point;

        bool isServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

        // ค้นหาเป้าที่รับดาเมจได้ในสิ่งที่ชน — บอส (PVE) หรือชิ้นส่วนหุ่นอีกฝ่าย (PVP)
        EnemyHealth enemyHealth = collision.gameObject.GetComponentInParent<EnemyHealth>();
        RobotHealth robotHealth = enemyHealth == null
            ? collision.gameObject.GetComponentInParent<RobotHealth>()
            : null;

        // โดนเป้าที่คิดดาเมจได้จริงไหม — ใช้ตัดสินชะตากรรมหมัดด้านล่าง
        bool hitDamageTarget = false;

        if (enemyHealth != null)
        {
            // ✅ ส่งดาเมจให้ Enemy (Server จะประมวลผล)
            if (isServer)
            {
                // FIX: pass impactPoint (the real collision contact point) so the
                // hit VFX/SFX in EnemyHealth spawn where the punch actually
                // landed, instead of falling back to transform.position
                // (the enemy's pivot / center of the model).
                enemyHealth.ServerTakeHit(finalDamage, hitDirection, finalKnockback, impactPoint);
            }

            hitDamageTarget = true;
            float attackSpeed = combat != null
                ? combat.PeakPunchSpeed
                : legCombat != null ? legCombat.PeakKickSpeed : impactSpeed;
            Debug.Log($"🥊 HIT! PeakSpeed: {attackSpeed:F1} m/s | Damage: {finalDamage:F1} | Knockback: {finalKnockback:F1}");

            // เล่น VFX และ SFX (Client-side)
            SpawnImpactEffects(impactPoint, hitDirection);
        }
        else if (robotHealth != null)
        {
            // ✅ [PVP] ชกชิ้นส่วนหุ่นอีกฝ่าย
            // การชนชิ้นส่วนหุ่นตัวเองถูกกันด้วย root check ที่หัวฟังก์ชันแล้ว
            // ตัวนี้กันเพิ่มกรณีหุ่นคนละตัวแต่อยู่ทีมเดียวกัน (เผื่อขยายเป็น 2v2 ทีมละ 2 หุ่น)
            if (!RobotTeam.AreEnemies(transform, collision.transform))
            {
                Debug.Log($"🤝 หมัดโดนหุ่นทีมเดียวกัน — ไม่คิดดาเมจ");
            }
            else
            {
                // RobotHealth ปฏิเสธเองถ้าชิ้นนั้นหลุดไปแล้ว (เช็คใน ApplyDamage)
                if (isServer)
                    robotHealth.ServerTakeDamage(
                        finalDamage,
                        legCombat != null ? AttackType.Kick : AttackType.LightPunch,
                        hitDirection);

                hitDamageTarget = true;
                float attackSpeed = combat != null
                    ? combat.PeakPunchSpeed
                    : legCombat != null ? legCombat.PeakKickSpeed : impactSpeed;
                Debug.Log($"🥊 PVP HIT! {robotHealth.gameObject.name} | PeakSpeed: {attackSpeed:F1} m/s | Damage: {finalDamage:F1}");

                SpawnImpactEffects(impactPoint, hitDirection);
            }
        }
        else
        {
            // ชนกับสิ่งที่ไม่มีเลือด (เช่น กำแพง, พื้น)
            Debug.Log($"💥 Collision at {impactSpeed:F1} m/s (no damage target)");
        }

        // ✅ [Bug Fix] แยกชะตากรรมหมัดตามสิ่งที่ชน:
        // - โดนศัตรู → ล็อกดาเมจ + จบหมัด (หนึ่งหมัดหนึ่งดาเมจ)
        // - โดนกำแพง/พื้นแรงๆ → จบหมัดแต่ "ไม่ล็อกดาเมจ" — ถ้าปัดไปโดนศัตรูในช่วง grace ยังนับ
        // - ครูดเบาๆ → ไม่ทำอะไร หมัดพุ่งต่อ (เดิมครูดนิดเดียวหมัดก็ดับ)
        if (combat != null && isServer)
        {
            if (hitDamageTarget)
                combat.NotifyPunchImpact();
            else if (impactSpeed >= minVelocityThreshold)
                combat.NotifyPunchBlocked();
        }
        else if (legCombat != null && isServer)
        {
            if (hitDamageTarget)
                legCombat.NotifyKickImpact();
            else if (impactSpeed >= minVelocityThreshold)
                legCombat.NotifyKickBlocked();
        }
    }

    private void SpawnImpactEffects(Vector3 position, Vector3 normal)
    {
        // Spawn VFX
        if (impactVfxPrefab != null)
        {
            Quaternion rotation = Quaternion.LookRotation(normal);
            GameObject vfx = Instantiate(impactVfxPrefab, position, rotation);

            // Auto-destroy หลังจาก particle เล่นเสร็จ
            ParticleSystem ps = vfx.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                Destroy(vfx, ps.main.duration + ps.main.startLifetime.constantMax);
            }
            else
            {
                Destroy(vfx, 2f); // fallback
            }
        }

        // Play SFX
        if (impactSfx != null && audioSource != null)
        {
            audioSource.PlayOneShot(impactSfx);
        }
    }
}
