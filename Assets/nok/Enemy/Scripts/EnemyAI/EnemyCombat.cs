// =============================================================================
//  EnemyCombat.cs  (Simplified)
//  ตัดออก: การรับ Damage (IHittable), Knockback, Hitstun
//  เหลือ: ตรวจ Hitbox ฝั่ง Server + ClientRpc สำหรับ VFX/SFX/Animator
//  อัปเดต: แต่ละท่ามีจุดกำเนิด VFX/Hitbox แยกกัน (lightPunchOrigin, barragePunchOrigin, kickOrigin)
// =============================================================================

using System.Collections;
using UnityEngine;
using Unity.Netcode;
using NscGame.Enemy;

namespace NscGame.Enemy
{
    [RequireComponent(typeof(EnemyController))]
    [RequireComponent(typeof(AudioSource))]
    public class EnemyCombat : NetworkBehaviour
    {
        // ─────────────────────────────────────────
        //  Inspector — Hitbox
        // ─────────────────────────────────────────

        [Header("Hitbox - Default Origin (fallback)")]
        [Tooltip("ตำแหน่ง fallback ถ้าไม่ได้ตั้งค่า origin เฉพาะของท่านั้นๆ")]
        [SerializeField] private Transform hitboxOrigin;

        [Header("Hitbox - Per Attack Origin")]
        [Tooltip("จุดกำเนิด Hitbox/VFX ของ Light Punch (เช่น Bone มือ)")]
        [SerializeField] private Transform lightPunchOrigin;

        [Tooltip("จุดกำเนิด Hitbox/VFX ของ Barrage Punch (เช่น Bone มือ)")]
        [SerializeField] private Transform barragePunchOrigin;

        [Tooltip("จุดกำเนิด Hitbox/VFX ของ Kick (เช่น Bone เท้า)")]
        [SerializeField] private Transform kickOrigin;

        [SerializeField] private float lightPunchRadius   = 1.0f;
        [SerializeField] private float barragePunchRadius = 1.0f;
        [SerializeField] private float kickRadius         = 1.5f;

        [SerializeField] private LayerMask playerLayer;

        [Header("Damage Values")]
        [SerializeField] private float lightPunchDamage   = 10f;
        [SerializeField] private float barragePunchDamage = 5f;
        [SerializeField] private int   barrageHitCount    = 6;
        [SerializeField] private float barrageHitInterval = 0.15f;
        [SerializeField] private float kickDamage         = 25f;

        // ─────────────────────────────────────────
        //  Inspector — VFX
        // ─────────────────────────────────────────

        [Header("VFX Prefabs")]
        [SerializeField] private GameObject punchVfxPrefab;
        [SerializeField] private GameObject barrageVfxPrefab;
        [SerializeField] private GameObject kickVfxPrefab;

        // ─────────────────────────────────────────
        //  Inspector — SFX
        // ─────────────────────────────────────────

        [Header("Sound Effects")]
        [SerializeField] private AudioClip   sfxPunch;
        [SerializeField] private AudioClip   sfxBarrage;
        [SerializeField] private AudioClip   sfxKick;
        [SerializeField] private AudioSource audioSource;

        // ─────────────────────────────────────────
        //  Private
        // ─────────────────────────────────────────

        private Animator animator;

        private void Awake()
        {
            animator    = GetComponent<Animator>();
            audioSource = GetComponent<AudioSource>();

            if (hitboxOrigin == null)
                hitboxOrigin = transform;
        }

        // ─────────────────────────────────────────
        //  Helper: หา Origin ของแต่ละท่า (fallback ไปที่ hitboxOrigin ถ้าไม่ได้ตั้งค่า)
        // ─────────────────────────────────────────

        private Transform GetOriginFor(AttackType type)
        {
            switch (type)
            {
                case AttackType.LightPunch:
                    return lightPunchOrigin != null ? lightPunchOrigin : hitboxOrigin;
                case AttackType.BarragePunch:
                    return barragePunchOrigin != null ? barragePunchOrigin : hitboxOrigin;
                case AttackType.Kick:
                    return kickOrigin != null ? kickOrigin : hitboxOrigin;
                default:
                    return hitboxOrigin;
            }
        }

        // ─────────────────────────────────────────
        //  SERVER: Entry Point
        //  EnemyController เรียก method นี้
        //  คืนค่าระยะเวลาของ animation (วินาที)
        // ─────────────────────────────────────────

        public float ServerExecuteAttack(AttackType type)
        {
            if (!IsServer) return 0f;

            switch (type)
            {
                case AttackType.LightPunch:
                    StartCoroutine(ServerLightPunchRoutine());
                    return 0.8f;

                case AttackType.BarragePunch:
                    StartCoroutine(ServerBarrageRoutine());
                    return barrageHitCount * barrageHitInterval + 0.5f;

                case AttackType.Kick:
                    StartCoroutine(ServerKickRoutine());
                    return 1.2f;

                default:
                    return 0f;
            }
        }

        // ─────────────────────────────────────────
        //  SERVER: Attack Routines
        // ─────────────────────────────────────────

        // ── Light Punch ────────────────────────────────────────────────────
        private IEnumerator ServerLightPunchRoutine()
        {
            Transform origin = GetOriginFor(AttackType.LightPunch);

            // บอกทุก Client ให้เล่น Animation + VFX ทันที
            PlayAttackEffectsClientRpc(AttackType.LightPunch, origin.position);

            yield return new WaitForSeconds(0.2f);  // wind-up ก่อน hitbox ทำงาน

            Collider[] hits = Physics.OverlapSphere(origin.position, lightPunchRadius, playerLayer);
            foreach (Collider col in hits)
            {
                IHittable target = col.GetComponent<IHittable>();
                if (target != null)
                {
                    target.ServerTakeDamage(lightPunchDamage, AttackType.LightPunch);
                    SpawnHitConfirmClientRpc(AttackType.LightPunch,
                                            col.ClosestPoint(origin.position));
                }
            }
        }

        // ── Barrage Punch ──────────────────────────────────────────────────
        private IEnumerator ServerBarrageRoutine()
        {
            Transform origin = GetOriginFor(AttackType.BarragePunch);

            PlayAttackEffectsClientRpc(AttackType.BarragePunch, origin.position);

            for (int i = 0; i < barrageHitCount; i++)
            {
                yield return new WaitForSeconds(barrageHitInterval);

                Collider[] hits = Physics.OverlapSphere(origin.position, barragePunchRadius, playerLayer);
                foreach (Collider col in hits)
                {
                    IHittable target = col.GetComponent<IHittable>();
                    if (target != null)
                    {
                        target.ServerTakeDamage(barragePunchDamage, AttackType.BarragePunch);
                        SpawnHitConfirmClientRpc(AttackType.BarragePunch,
                                                col.ClosestPoint(origin.position));
                    }
                }
            }
        }

        // ── Kick ──────────────────────────────────────────────────────────
        private IEnumerator ServerKickRoutine()
        {
            Transform origin = GetOriginFor(AttackType.Kick);

            PlayAttackEffectsClientRpc(AttackType.Kick, origin.position);

            yield return new WaitForSeconds(0.35f);

            Collider[] hits = Physics.OverlapSphere(origin.position, kickRadius, playerLayer);
            foreach (Collider col in hits)
            {
                IHittable target = col.GetComponent<IHittable>();
                if (target != null)
                {
                    target.ServerTakeDamage(kickDamage, AttackType.Kick);
                    SpawnHitConfirmClientRpc(AttackType.Kick,
                                            col.ClosestPoint(origin.position));
                }
            }
        }

        // ─────────────────────────────────────────
        //  CLIENT RPC — Broadcast ไปทุก Client
        // ─────────────────────────────────────────

        /// <summary>เล่น Animation Trigger + VFX + SFX บนทุก Client</summary>
        [ClientRpc]
        private void PlayAttackEffectsClientRpc(AttackType type, Vector3 vfxPosition)
        {
            switch (type)
            {
                case AttackType.LightPunch:
                    animator.SetTrigger(EnemyAnimParam.LightPunch);
                    SpawnVfx(punchVfxPrefab,   vfxPosition);
                    PlaySfx(sfxPunch);
                    break;

                case AttackType.BarragePunch:
                    animator.SetTrigger(EnemyAnimParam.BarragePunch);
                    SpawnVfx(barrageVfxPrefab, vfxPosition);
                    PlaySfx(sfxBarrage);
                    break;

                case AttackType.Kick:
                    animator.SetTrigger(EnemyAnimParam.Kick);
                    SpawnVfx(kickVfxPrefab,    vfxPosition);
                    PlaySfx(sfxKick);
                    break;
            }
        }

        /// <summary>Spawn VFX ตรงจุดที่ตีโดน บนทุก Client</summary>
        [ClientRpc]
        private void SpawnHitConfirmClientRpc(AttackType type, Vector3 impactPoint)
        {
            GameObject prefab = type switch
            {
                AttackType.LightPunch   => punchVfxPrefab,
                AttackType.BarragePunch => barrageVfxPrefab,
                AttackType.Kick         => kickVfxPrefab,
                _                       => null
            };
            SpawnVfx(prefab, impactPoint);
        }

        // ─────────────────────────────────────────
        //  Client Helpers
        // ─────────────────────────────────────────

        private void SpawnVfx(GameObject prefab, Vector3 position)
        {
            if (prefab == null) return;
            GameObject vfx = Instantiate(prefab, position, Quaternion.identity);
            ParticleSystem ps = vfx.GetComponentInChildren<ParticleSystem>();
            float lifetime = ps != null
                ? ps.main.duration + ps.main.startLifetime.constantMax
                : 3f;
            Destroy(vfx, lifetime);
        }

        private void PlaySfx(AudioClip clip)
        {
            if (clip == null || audioSource == null) return;
            audioSource.PlayOneShot(clip);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Transform lpOrigin = GetOriginFor(AttackType.LightPunch);
            if (lpOrigin != null)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(lpOrigin.position, lightPunchRadius);
            }

            Transform bpOrigin = GetOriginFor(AttackType.BarragePunch);
            if (bpOrigin != null)
            {
                Gizmos.color = new Color(1f, 0.5f, 0f);
                Gizmos.DrawWireSphere(bpOrigin.position, barragePunchRadius);
            }

            Transform kOrigin = GetOriginFor(AttackType.Kick);
            if (kOrigin != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(kOrigin.position, kickRadius);
            }
        }
#endif
    }

    // =========================================================================
    //  IHittable — Interface สำหรับ Player รับ Damage
    //  ยังคงไว้เพื่อให้ระบบโจมตียังทำงานได้
    //  แต่ไม่มี Hitstun / Knockback แล้ว
    // =========================================================================
    public interface IHittable
    {
        /// <summary>รับ Damage บน Server</summary>
        void ServerTakeDamage(float amount, AttackType source);
    }
}