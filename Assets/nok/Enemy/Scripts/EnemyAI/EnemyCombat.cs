// =============================================================================
//  EnemyCombat.cs  (Simplified)
//  ตัดออก: การรับ Damage (IHittable), Knockback, Hitstun
//  เหลือ: ตรวจ Hitbox ฝั่ง Server + ClientRpc สำหรับ VFX/SFX/Animator
//  อัปเดต: แต่ละท่ามีจุดกำเนิด VFX/Hitbox แยกกัน (lightPunchOrigin, barragePunchOrigin, kickOrigin)
//  อัปเดต: VFX หมุนตามทิศที่ enemy หันอยู่ (transform.rotation) + ปรับ offset ต่อท่าได้
//  อัปเดต: ทำลาย VFX เมื่อ particle เล่นจบจริงๆ (ไม่ใช่ประมาณเวลา)
//  อัปเดต: จำกัด VFX ต่อท่าให้เกิดได้สูงสุด "อันเดียว" ต่อ AttackType — ถ้าเอฟเฟกต์ท่าเดิม
//          กำลังเล่นอยู่แล้ว trigger ซ้ำ จะทำลายตัวเก่าทิ้งทันทีก่อนเล่นตัวใหม่
// =============================================================================

using System.Collections;
using System.Collections.Generic;
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

        [Header("VFX Rotation Offset (ถ้าทิศพาร์ติเคิลเพี้ยน ปรับตรงนี้)")]
        [Tooltip("หมุนเพิ่มจากทิศที่ enemy หันอยู่ หน่วยองศา (X,Y,Z)")]
        [SerializeField] private Vector3 lightPunchRotationOffset   = Vector3.zero;
        [SerializeField] private Vector3 barragePunchRotationOffset = Vector3.zero;
        [SerializeField] private Vector3 kickRotationOffset         = Vector3.zero;

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

        /// <summary>
        /// เก็บ VFX instance ที่ "กำลังเล่นอยู่" ของแต่ละ AttackType (อย่างละไม่เกิน 1 ตัว)
        /// ใช้ตรวจ/ทำลายตัวเก่าก่อน spawn ตัวใหม่ เมื่อ trigger ท่าเดิมซ้ำ
        /// </summary>
        private readonly Dictionary<AttackType, GameObject> activeVfxByType = new Dictionary<AttackType, GameObject>();

        private void Awake()
        {
            animator    = GetComponent<Animator>();
            audioSource = GetComponent<AudioSource>();

            if (hitboxOrigin == null)
                hitboxOrigin = transform;
        }

        // ─────────────────────────────────────────
        //  Helper: หา Origin / Rotation Offset ของแต่ละท่า
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

        private Vector3 GetRotationOffsetFor(AttackType type)
        {
            switch (type)
            {
                case AttackType.LightPunch:   return lightPunchRotationOffset;
                case AttackType.BarragePunch: return barragePunchRotationOffset;
                case AttackType.Kick:         return kickRotationOffset;
                default:                      return Vector3.zero;
            }
        }

        /// <summary>ทิศ VFX = ทิศที่ enemy หันอยู่ตอนนี้ + offset ที่ปรับเอง</summary>
        private Quaternion GetVfxRotation(AttackType type)
        {
            return transform.rotation * Quaternion.Euler(GetRotationOffsetFor(type));
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

            PlayAttackEffectsClientRpc(AttackType.LightPunch, origin.position, GetVfxRotation(AttackType.LightPunch));

            yield return new WaitForSeconds(0.2f);  // wind-up ก่อน hitbox ทำงาน

            Collider[] hits = Physics.OverlapSphere(origin.position, lightPunchRadius, playerLayer);
            foreach (Collider col in hits)
            {
                IHittable target = col.GetComponent<IHittable>();
                if (target != null)
                {
                    target.ServerTakeDamage(lightPunchDamage, AttackType.LightPunch);

                    Vector3 hitPoint = col.ClosestPoint(origin.position);
                    SpawnHitConfirmClientRpc(AttackType.LightPunch, hitPoint, GetVfxRotation(AttackType.LightPunch));
                }
            }
        }

        // ── Barrage Punch ──────────────────────────────────────────────────
        private IEnumerator ServerBarrageRoutine()
        {
            Transform origin = GetOriginFor(AttackType.BarragePunch);

            PlayAttackEffectsClientRpc(AttackType.BarragePunch, origin.position, GetVfxRotation(AttackType.BarragePunch));

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

                        Vector3 hitPoint = col.ClosestPoint(origin.position);
                        SpawnHitConfirmClientRpc(AttackType.BarragePunch, hitPoint, GetVfxRotation(AttackType.BarragePunch));
                    }
                }
            }
        }

        // ── Kick ──────────────────────────────────────────────────────────
        private IEnumerator ServerKickRoutine()
        {
            Transform origin = GetOriginFor(AttackType.Kick);

            PlayAttackEffectsClientRpc(AttackType.Kick, origin.position, GetVfxRotation(AttackType.Kick));

            yield return new WaitForSeconds(0.35f);

            Collider[] hits = Physics.OverlapSphere(origin.position, kickRadius, playerLayer);
            foreach (Collider col in hits)
            {
                IHittable target = col.GetComponent<IHittable>();
                if (target != null)
                {
                    target.ServerTakeDamage(kickDamage, AttackType.Kick);

                    Vector3 hitPoint = col.ClosestPoint(origin.position);
                    SpawnHitConfirmClientRpc(AttackType.Kick, hitPoint, GetVfxRotation(AttackType.Kick));
                }
            }
        }

        // ─────────────────────────────────────────
        //  CLIENT RPC — Broadcast ไปทุก Client
        // ─────────────────────────────────────────

        /// <summary>เล่น Animation Trigger + VFX + SFX บนทุก Client</summary>
        [ClientRpc]
        private void PlayAttackEffectsClientRpc(AttackType type, Vector3 vfxPosition, Quaternion vfxRotation)
        {
            switch (type)
            {
                case AttackType.LightPunch:
                    animator.SetTrigger(EnemyAnimParam.LightPunch);
                    SpawnVfx(type, punchVfxPrefab,   vfxPosition, vfxRotation);
                    PlaySfx(sfxPunch);
                    break;

                case AttackType.BarragePunch:
                    animator.SetTrigger(EnemyAnimParam.BarragePunch);
                    SpawnVfx(type, barrageVfxPrefab, vfxPosition, vfxRotation);
                    PlaySfx(sfxBarrage);
                    break;

                case AttackType.Kick:
                    animator.SetTrigger(EnemyAnimParam.Kick);
                    SpawnVfx(type, kickVfxPrefab,    vfxPosition, vfxRotation);
                    PlaySfx(sfxKick);
                    break;
            }
        }

        /// <summary>Spawn VFX ตรงจุดที่ตีโดน บนทุก Client</summary>
        [ClientRpc]
        private void SpawnHitConfirmClientRpc(AttackType type, Vector3 impactPoint, Quaternion impactRotation)
        {
            GameObject prefab = type switch
            {
                AttackType.LightPunch   => punchVfxPrefab,
                AttackType.BarragePunch => barrageVfxPrefab,
                AttackType.Kick         => kickVfxPrefab,
                _                       => null
            };
            SpawnVfx(type, prefab, impactPoint, impactRotation);
        }

        // ─────────────────────────────────────────
        //  Client Helpers
        // ─────────────────────────────────────────

        /// <summary>
        /// Spawn VFX ของ AttackType ที่ระบุ โดยจำกัดให้มีได้สูงสุด "อันเดียว" ต่อ AttackType
        /// ถ้ามีตัวเก่าของท่านี้เล่นอยู่ จะถูกทำลายทิ้งทันทีก่อนสร้างตัวใหม่
        /// </summary>
        private void SpawnVfx(AttackType type, GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null) return;

            // ทำลายตัวเก่าของท่านี้ทิ้งทันที (ถ้ามี) ก่อนเล่นตัวใหม่
            if (activeVfxByType.TryGetValue(type, out GameObject oldVfx) && oldVfx != null)
            {
                StopCoroutine(nameof(DestroyWhenParticleFinished)); // เผื่อ coroutine เก่ายังอ้างอิงอยู่ (no-op ถ้าไม่ตรง)
                Destroy(oldVfx);
            }

            GameObject vfx = Instantiate(prefab, position, rotation);
            activeVfxByType[type] = vfx;

            // ── ทำลาย VFX ก็ต่อเมื่อ particle เล่นจบจริงๆ เท่านั้น (หรือถูกแทนที่ก่อนหน้านั้น) ──
            StartCoroutine(DestroyWhenParticleFinished(type, vfx));
        }

        /// <summary>รอจน ParticleSystem (รวมลูกทุกตัว) เล่นจบสนิทจริงๆ ค่อย Destroy</summary>
        private IEnumerator DestroyWhenParticleFinished(AttackType type, GameObject vfx)
        {
            ParticleSystem ps = vfx.GetComponentInChildren<ParticleSystem>();

            if (ps == null)
            {
                // ไม่มี ParticleSystem (อาจเป็น VFX แบบอื่น เช่น Animation/Trail อย่างเดียว)
                // fallback: รอ 3 วินาทีเฉยๆ กันค้างไม่มีวันหาย
                yield return new WaitForSeconds(3f);
                FinishVfx(type, vfx);
                yield break;
            }

            // IsAlive(true) เช็คทั้งตัวเองและลูกทุกตัว จนกว่าจะไม่มี particle เหลืออยู่เลย
            while (vfx != null && ps.IsAlive(true))
            {
                yield return null;
            }

            FinishVfx(type, vfx);
        }

        /// <summary>ทำลาย VFX และเคลียร์สถานะ "ตัวที่กำลังเล่นอยู่" ของท่านั้น (ถ้ายังเป็นตัวนี้อยู่)</summary>
        private void FinishVfx(AttackType type, GameObject vfx)
        {
            if (vfx == null) return;

            // เคลียร์ slot ก็ต่อเมื่อตัวที่บันทึกไว้ยังเป็นตัวนี้อยู่
            // (กันเคสที่ตัวใหม่ถูก spawn มาทับไปแล้วก่อนตัวเก่าจะถูกเรียก FinishVfx)
            if (activeVfxByType.TryGetValue(type, out GameObject current) && current == vfx)
            {
                activeVfxByType.Remove(type);
            }

            Destroy(vfx);
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