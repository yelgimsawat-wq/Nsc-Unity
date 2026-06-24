using UnityEngine;
using Unity.Netcode;
using NscGame.Enemy; // สำหรับเข้าถึง EnemyHealth

[RequireComponent(typeof(Rigidbody))]
public class PhysicsDamageSender : MonoBehaviour
{
    [Header("Damage Settings")]
    [Tooltip("ความเร็วขั้นต่ำในการชนที่จะเกิดดาเมจ (กันชนเบาๆ แล้วเลือดลด)")]
    public float minVelocityThreshold = 8f;

    [Tooltip("คูณความเร็วชนเป็นดาเมจ (เช่น ชนด้วย 10 m/s * 1.5 = 15 damage)")]
    public float damageMultiplier = 1f;

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

    void Awake()
    {
        rb = GetComponent<Rigidbody>();

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
        // คำนวณความเร็วสัมพัทธ์ (Relative Velocity) จังหวะที่ชน
        float impactSpeed = collision.relativeVelocity.magnitude;

        // ถ้าความเร็วน้อยเกินไป ไม่เกิดดาเมจ
        if (impactSpeed < minVelocityThreshold)
            return;

        // คำนวณดาเมจและแรง Knockback
        float finalDamage = impactSpeed * damageMultiplier;
        float finalKnockback = baseKnockbackForce + (impactSpeed * knockbackVelocityMultiplier);

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

            Debug.Log($"🥊 HIT! Impact Speed: {impactSpeed:F1} m/s | Damage: {finalDamage:F1} | Knockback: {finalKnockback:F1}");

            // เล่น VFX และ SFX (Client-side)
            SpawnImpactEffects(impactPoint, hitDirection);
        }
        else
        {
            // ชนกับสิ่งที่ไม่ใช่ Enemy (เช่น กำแพง, พื้น)
            Debug.Log($"💥 Collision at {impactSpeed:F1} m/s (no damage target)");
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