using System;
using Nsc.Combat;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

namespace Nsc.Robots
{
    /// <summary>
    /// เลือดของแขนขาหนึ่งชิ้น (เดิมชื่อ RobotHealth, GUID เดิม)
    ///
    /// กติกาที่คงไว้: เลือดหมด → ชิ้นหลุด และเพดานเลือดลดถาวรทุกครั้งที่หลุด (หลุดบ่อยยิ่งเปราะ)
    /// ไม่โดนตี 7.5 วิ → ฟื้นเลือด 2/วิ ไม่เกินเพดานปัจจุบัน / ชิ้นที่หลุดอยู่ไม่รับดาเมจ
    ///
    /// ไม่รู้จักข้อต่ออีกแล้ว — แค่ยิง Depleted แล้ว RobotLimb เป็นคนสั่งหลุด
    /// </summary>
    public class LimbHealth : NetworkBehaviour, IDamageable
    {
        private const float RegenPerSecond = 2f;
        private const float RegenDelay = 7.5f;
        private const float KnockbackPerDamage = 2f;

        [Header("Health Settings")]
        [FormerlySerializedAs("MaxHp")]
        [SerializeField] private float startingMaxHp = 500f;

        [Tooltip("เพดานเลือดลดลงเท่านี้ทุกครั้งที่ชิ้นส่วนหลุด — ยิ่งหลุดบ่อยยิ่งเปราะ ผู้เล่นถึงแพ้ได้")]
        [SerializeField] private float hpLossPerBreak = 125f;

        [Tooltip("เพดานเลือดต่ำสุด (ลดจนต่ำกว่านี้ไม่ได้)")]
        [SerializeField] private float minMaxHp = 50f;

        [FormerlySerializedAs("currentHp")]
        [SerializeField]
        private NetworkVariable<float> hp = new NetworkVariable<float>(
            500f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // เพดานเลือดปัจจุบัน — HUD ทุกเครื่องคำนวณ % จากตัวนี้ ไม่ใช่ค่าเริ่มต้นตายตัว
        [FormerlySerializedAs("currentMaxHp")]
        [SerializeField]
        private NetworkVariable<float> maxHp = new NetworkVariable<float>(
            500f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        [Header("Hit Reaction VFX/SFX")]
        [Tooltip("Particle ตรงจุดโดนต่อย/เตะ — server สั่งให้เกิดพร้อมกันทุกเครื่อง")]
        [SerializeField] private GameObject hitVfxPrefab;
        [SerializeField] private AudioClip sfxHit;
        [Tooltip("เว้นว่างได้ — หา AudioSource บน GameObject นี้ให้เองตอน Awake")]
        [SerializeField] private AudioSource audioSource;

        private RobotLimb limb;
        private float timeSinceHit;
        private bool regenerating = true;

        public float Hp => hp.Value;
        public float MaxHp => maxHp.Value;
        /// <summary>เพดานเลือดตอนเกิด (ก่อนหลุดครั้งแรก) — ใช้คิด "แรงที่เหลือ" ของขาที่บาดเจ็บ</summary>
        public float StartingMaxHp => startingMaxHp;
        public float HpPercent => maxHp.Value > 0f ? Mathf.Clamp01(hp.Value / maxHp.Value) : 0f;

        /// <summary>[SERVER] เลือดหมด — RobotLimb ฟังแล้วสั่งให้ชิ้นหลุด</summary>
        public event Action Depleted;

        /// <summary>ยิงบนทุกเครื่องเมื่อเลือดหรือเพดานเลือดเปลี่ยน (HUD)</summary>
        public event Action HealthChanged;

        private void Awake()
        {
            limb = GetComponent<RobotLimb>();
            if (audioSource == null) audioSource = GetComponent<AudioSource>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            hp.OnValueChanged += OnHealthValueChanged;
            maxHp.OnValueChanged += OnHealthValueChanged;
            if (!IsServer) return;

            maxHp.Value = startingMaxHp;
            hp.Value = maxHp.Value;
        }

        public override void OnNetworkDespawn()
        {
            hp.OnValueChanged -= OnHealthValueChanged;
            maxHp.OnValueChanged -= OnHealthValueChanged;
            base.OnNetworkDespawn();
        }

        private void OnHealthValueChanged(float previous, float current) => HealthChanged?.Invoke();

        private void Update()
        {
            // server คำนวณเลือดคนเดียว client รอรับค่าไปโชว์
            if (!IsServer) return;

            if (regenerating)
            {
                if (hp.Value < maxHp.Value)
                    hp.Value = Mathf.Min(maxHp.Value, hp.Value + RegenPerSecond * Time.deltaTime);
            }
            else
            {
                timeSinceHit += Time.deltaTime;
                if (timeSinceHit > RegenDelay)
                {
                    timeSinceHit = 0f;
                    regenerating = true;
                }
            }
        }

        public Team GetTeam() => limb != null && limb.Owner != null ? limb.Owner.GetTeam() : Team.None;

        /// <summary>ชิ้นนี้ยังกินดาเมจได้ไหม — ชิ้นที่หลุดอยู่ไม่รับดาเมจ ไม่งั้นเลือดที่ฟื้นระหว่างหลุด
        /// จะโดนตีจนหมดซ้ำ แล้วเพดานเลือดโดนหักรัวๆ ทั้งที่หลุดไปแล้ว</summary>
        private bool CanTakeDamage =>
            (limb == null || limb.IsAttached) && hp.Value > 0f;

        public bool ServerApplyDamage(DamageInfo info)
        {
            if (!IsServer || !CanTakeDamage) return false;

            // ยิงเอฟเฟกต์ก่อนหักเลือด — หมัดสุดท้ายที่ทำให้ชิ้นหลุดยังมีเอฟเฟกต์ให้เห็น
            if (ShowsHitEffects(info.source))
                PlayHitEffectsClientRpc(info.point, info.direction);

            hp.Value -= info.amount;
            regenerating = false;
            timeSinceHit = 0f;

            if (TryGetComponent(out Rigidbody body))
                body.AddForce(info.direction * (info.amount * KnockbackPerDamage), ForceMode.Impulse);

            if (hp.Value <= 0f || info.detachLimb)
            {
                hp.Value = 0f;
                Break();
            }
            return true;
        }

        /// <summary>บอสมี VFX ยืนยันการโดนของตัวเองอยู่แล้ว — ชิ้นส่วนเล่นเอฟเฟกต์เฉพาะตอนโดนผู้เล่นตี</summary>
        private static bool ShowsHitEffects(DamageSource source) =>
            source == DamageSource.Punch || source == DamageSource.Kick || source == DamageSource.MeleeWeapon;

        /// <summary>[SERVER] ต่อกลับแล้วเติมเลือดเต็มตามเพดานปัจจุบัน</summary>
        public void ServerRefill()
        {
            if (!IsServer) return;
            hp.Value = maxHp.Value;
            regenerating = true;
            timeSinceHit = 0f;
        }

        private void Break()
        {
            // ทุกครั้งที่หลุด เพดานเลือดลดถาวร (ต่อกลับก็ได้แค่เพดานใหม่)
            maxHp.Value = Mathf.Max(minMaxHp, maxHp.Value - hpLossPerBreak);
            Debug.Log($"[LimbHealth] 💔 {gameObject.name} หลุด! เพดานเลือดเหลือ {maxHp.Value}");
            Depleted?.Invoke();
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        private void PlayHitEffectsClientRpc(Vector3 impactPosition, Vector3 hitDirection)
        {
            if (hitVfxPrefab != null)
            {
                Quaternion rotation = hitDirection.sqrMagnitude > 0.0001f
                    ? Quaternion.LookRotation(hitDirection)
                    : Quaternion.identity;
                GameObject vfx = Instantiate(hitVfxPrefab, impactPosition, rotation);
                ParticleSystem ps = vfx.GetComponentInChildren<ParticleSystem>();
                Destroy(vfx, ps != null ? ps.main.duration + 1f : 2f);
            }

            if (sfxHit != null && audioSource != null)
                audioSource.PlayOneShot(sfxHit);
        }
    }
}
