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

    [Tooltip("เพดานดาเมจต่อการชนหนึ่งครั้ง")]
    public float maxDamagePerHit = 60f;

    [Header("Knockback Settings")]
    [Tooltip("แรง Knockback พื้นฐาน")]
    public float baseKnockbackForce = 5f;

    [Tooltip("คูณความเร็วชนเป็นแรง Knockback เพิ่มเติม")]
    public float knockbackVelocityMultiplier = 0.3f;

    [Header("VFX/SFX (Optional)")]
    [Tooltip("Particle effect เมื่อชนเกิดดาเมจ")]
    public GameObject impactVfxPrefab;

    [Tooltip("เสียงชนเกิดดาเมจ")]
    public AudioClip impactSfx;

    private Rigidbody rb;
    private AudioSource audioSource;
    private PlayerHandCombat combat; // ถ้าติดบนมือที่มีระบบต่อย: คิดดาเมจเฉพาะตอนปล่อยหมัด (Shift)

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        combat = GetComponent<PlayerHandCombat>();

        // สร้าง AudioSource ถ้ายังไม่มี
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null && impactSfx != null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f; // 3D sound
        }
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
            finalKnockback = baseKnockbackForce + (punchSpeed * knockbackVelocityMultiplier);
        }
        else
        {
            // [ชิ้นส่วนทั่วไป] ใช้แรงปะทะจริงเหมือนเดิม: F = m·Δv/Δt = impulse/Δt
            if (impactSpeed < minVelocityThreshold)
                return;

            float impactForce = collision.impulse.magnitude / Time.fixedDeltaTime;
            finalDamage = Mathf.Min(impactForce * forceToDamage, maxDamagePerHit);
            finalKnockback = baseKnockbackForce + (impactSpeed * knockbackVelocityMultiplier);
        }

        // ทิศทางการชน (จากจุดชนแรก)
        Vector3 hitDirection = collision.contacts[0].normal * -1f; // กลับทิศเพราะ normal ชี้ออกจากพื้นผิว
        Vector3 impactPoint = collision.contacts[0].point;

        // ค้นหา EnemyHealth ในสิ่งที่ชน
        EnemyHealth enemyHealth = collision.gameObject.GetComponentInParent<EnemyHealth>();

        if (enemyHealth != null)
        {
            // ✅ ส่งดาเมจให้ Enemy (Server จะประมวลผล)
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            {
                enemyHealth.ServerTakeHit(finalDamage, hitDirection, finalKnockback);
            }

            Debug.Log($"🥊 HIT! PeakSpeed: {(combat != null ? combat.PeakPunchSpeed : impactSpeed):F1} m/s | Damage: {finalDamage:F1} | Knockback: {finalKnockback:F1}");

            // เล่น VFX และ SFX (Client-side)
            SpawnImpactEffects(impactPoint, hitDirection);
        }
        else
        {
            // ชนกับสิ่งที่ไม่ใช่ Enemy (เช่น กำแพง, พื้น)
            Debug.Log($"💥 Collision at {impactSpeed:F1} m/s (no damage target)");
        }

        // ✅ [Bug Fix] แยกชะตากรรมหมัดตามสิ่งที่ชน:
        // - โดนศัตรู → ล็อกดาเมจ + จบหมัด (หนึ่งหมัดหนึ่งดาเมจ)
        // - โดนกำแพง/พื้นแรงๆ → จบหมัดแต่ "ไม่ล็อกดาเมจ" — ถ้าปัดไปโดนศัตรูในช่วง grace ยังนับ
        // - ครูดเบาๆ → ไม่ทำอะไร หมัดพุ่งต่อ (เดิมครูดนิดเดียวหมัดก็ดับ)
        if (combat != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            if (enemyHealth != null)
                combat.NotifyPunchImpact();
            else if (impactSpeed >= minVelocityThreshold)
                combat.NotifyPunchBlocked();
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