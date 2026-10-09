using Nsc.Robots;
using Unity.Netcode;
using UnityEngine;

namespace Nsc.Combat
{
    /// <summary>
    /// แปลง "ชิ้นส่วนที่ใช้ตีชนอะไรเข้า" เป็นคำขอทำดาเมจ — วางบนมือ เท้า และใบดาบ
    /// รวมของเดิมสองตัว (PhysicsDamageSender + PvpDamageSender) ที่ทำงานซ้อนกันบนชิ้นเดียวในซีน PVP
    /// แล้วแย่งกันตามลำดับคอมโพเนนต์ — ตอนนี้กติกาทีม/เฟสอยู่ที่ DamageRouter ที่เดียว
    ///
    /// สูตร F = ma ของเดิม: ดาเมจ = min(ความเร็วพีคของท่า × speedToDamage, maxDamagePerHit)
    /// ใช้ความเร็วพีค ไม่ใช่ impulse ตอนชน → ชนเฉียง/เป้าถอยหนี ดาเมจไม่หาย และหนึ่งท่าหนึ่งดาเมจ
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class LimbStrike : MonoBehaviour
    {
        [Header("Damage (F = ma)")]
        [Tooltip("ความเร็วพีคขั้นต่ำที่จะเกิดดาเมจ (กันชนเบาๆ แล้วเลือดลด)")]
        [SerializeField] private float minVelocityThreshold = 8f;

        [Tooltip("ตัวคูณความเร็วพีค (m/s) → ดาเมจ | หมัด 0.6 (พีค 55 = 33) / เตะ 1.2")]
        [SerializeField] private float speedToDamage = 0.6f;

        [Tooltip("เพดานดาเมจต่อการชนหนึ่งครั้ง")]
        [SerializeField] private float maxDamagePerHit = 60f;

        [Tooltip("ตัวคูณแรงปะทะจริง (นิวตัน) → ดาเมจ ตอนชิ้นนี้ชนของพังได้โดยไม่ได้อยู่ในท่าโจมตี (เดินชนตึก)")]
        [SerializeField] private float forceToDamage = 0.01f;

        [Header("Knockback (ยิ่งตีแรงยิ่งเด้งแรง)")]
        [Tooltip("แรงเด้งตอนตีแผ่วสุดที่ยังคิดดาเมจ (ความเร็ว = Min Velocity Threshold)")]
        [SerializeField] private float minKnockbackForce = 2f;
        [Tooltip("แรงเด้งตอนตีเต็มแรง (ความเร็วถึง Knockback Reference Speed)")]
        [SerializeField] private float maxKnockbackForce = 12f;
        [Tooltip("ความโค้งการไล่แรง 1 = เชิงเส้น | >1 = ตีเบาแทบไม่เด้ง แต่ตีเต็มแรงยังเด้งสุด")]
        [Range(0.5f, 4f)]
        [SerializeField] private float knockbackCurve = 2f;
        [Tooltip("ความเร็วที่ถือว่า 'ตีเต็มแรง' (m/s)")]
        [SerializeField] private float knockbackReferenceSpeed = 55f;

        [Header("VFX/SFX (Optional)")]
        [SerializeField] private GameObject impactVfxPrefab;
        [SerializeField] private AudioClip impactSfx;

        private IStrikeSource source;
        private AudioSource audioSource;

        public float MinVelocityThreshold => minVelocityThreshold;

        /// <summary>ตัวควบคุมเจ้าของท่า (หมัด เตะ ดาบ) บอกว่าการชนของชิ้นนี้ใช้ท่าไหนคิดดาเมจ</summary>
        public void SetSource(IStrikeSource strikeSource) => source = strikeSource;

        /// <summary>ดาบยืมค่าจากหมัดของมือที่ถือ แล้วคูณให้แรงกว่า</summary>
        public void Configure(LimbStrike basis, float damageMultiplier, float thresholdScale, float maxDamage)
        {
            minVelocityThreshold = basis.minVelocityThreshold * thresholdScale;
            speedToDamage = basis.speedToDamage * damageMultiplier;
            forceToDamage = basis.forceToDamage * damageMultiplier;
            maxDamagePerHit = maxDamage;
        }

        public float SpeedToDamage => speedToDamage;

        /// <summary>ท่าของชิ้นนี้กำลังทำดาเมจได้อยู่ไหม (หมัดพุ่ง / เตะ / ดาบสวิง)</summary>
        public bool IsStriking => source != null && source.CanDealDamage();

        /// <summary>ดาเมจจากแรงปะทะจริง F = m·Δv/Δt = impulse/Δt — ใช้กับการชนที่ไม่ใช่ท่าโจมตี</summary>
        public float ImpactDamage(Collision collision)
        {
            if (collision.relativeVelocity.magnitude < minVelocityThreshold) return 0f;
            float impactForce = collision.impulse.magnitude / Time.fixedDeltaTime;
            return Mathf.Min(impactForce * forceToDamage, maxDamagePerHit);
        }

        /// <summary>ดาเมจที่ความเร็วพีคนี้จะทำได้ถ้าเข้าเป้า (0 = ต่ำกว่าเกณฑ์) — ไว้ log จูนค่า</summary>
        public float PreviewDamage(float peakSpeed) =>
            peakSpeed < minVelocityThreshold ? 0f : Mathf.Min(peakSpeed * speedToDamage, maxDamagePerHit);

        private void Awake()
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null && impactSfx != null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 1f;
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            // ดาเมจตัดสินบน server เท่านั้น
            NetworkManager network = NetworkManager.Singleton;
            if (network == null || !network.IsServer) return;
            if (source == null || IsOwnBody(collision.collider)) return;
            // CCD บางเคสให้ contact = 0
            if (collision.contactCount == 0) return;
            if (!source.CanDealDamage()) return;

            float impactSpeed = collision.relativeVelocity.magnitude;
            float peakSpeed = source.PeakSpeed();
            if (peakSpeed < minVelocityThreshold) return;

            ContactPoint contact = collision.GetContact(0);
            Vector3 direction = -contact.normal; // normal ชี้ออกจากพื้นผิวที่โดน → กลับทิศ
            Robot ownRobot = Robot.FromTransform(transform);

            var info = new DamageInfo(
                PreviewDamage(peakSpeed), contact.point, direction, source.Source(),
                ownRobot != null ? ownRobot.GetTeam() : Team.None)
            {
                knockback = ComputeKnockback(peakSpeed)
            };

            bool landed = DamageRouter.TryApply(collision.collider, info);
            if (landed)
            {
                Debug.Log($"🥊 HIT! '{collision.collider.name}' | {info.source} | PeakSpeed: {peakSpeed:F1} m/s | " +
                          $"Damage: {info.amount:F1} | Knockback: {info.knockback:F1}");
                SpawnImpactEffects(contact.point, direction);
            }

            // เข้าเป้า → ล็อกดาเมจ + จบท่า (หนึ่งท่าหนึ่งดาเมจ)
            // ชนกำแพง/พื้นแรงๆ → จบท่าแต่ไม่ล็อกดาเมจ ถ้าปัดไปโดนเป้าในช่วงผ่อนผันยังนับ
            // ครูดเบาๆ → ไม่ทำอะไร ท่าพุ่งต่อ
            if (landed) source.ResolveStrike(true);
            else if (impactSpeed >= minVelocityThreshold) source.ResolveStrike(false);
        }

        /// <summary>ชิ้นส่วนของหุ่นตัวเอง — ไม่นับเป็นการปะทะ (มือครูดลำตัวตัวเองแล้วหมัดดับก่อนถึงเป้า)</summary>
        private bool IsOwnBody(Collider other)
        {
            Robot ownRobot = Robot.FromTransform(transform);
            if (ownRobot != null) return Robot.FromCollider(other) == ownRobot;
            return other.transform.root == transform.root;
        }

        /// <summary>ไล่แรงเด้งตามสัดส่วนความเร็วจริง — knockbackCurve ทำให้ช่วงต้นแบนราบ</summary>
        private float ComputeKnockback(float speed)
        {
            float top = Mathf.Max(knockbackReferenceSpeed, minVelocityThreshold + 0.01f);
            float t = Mathf.InverseLerp(minVelocityThreshold, top, speed);
            return Mathf.Lerp(minKnockbackForce, maxKnockbackForce, Mathf.Pow(t, knockbackCurve));
        }

        private void SpawnImpactEffects(Vector3 position, Vector3 normal)
        {
            if (impactVfxPrefab != null)
            {
                GameObject vfx = Instantiate(impactVfxPrefab, position, Quaternion.LookRotation(normal));
                ParticleSystem ps = vfx.GetComponent<ParticleSystem>();
                Destroy(vfx, ps != null ? ps.main.duration + ps.main.startLifetime.constantMax : 2f);
            }

            if (impactSfx != null && audioSource != null)
                audioSource.PlayOneShot(impactSfx);
        }
    }
}
