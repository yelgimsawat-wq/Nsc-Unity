// =============================================================================
//  EnemyCombat.cs
//  Server-authoritative attack execution with client-side VFX/SFX
//
//  Features:
//  - Per-attack hitbox origins (separate transforms for punch/kick)
//  - VFX rotation matches enemy facing direction + optional offset
//  - One active VFX per attack type (old VFX destroyed when same attack triggers)
//  - Automatic VFX cleanup when particle system finishes
//  - Physics-based hitbox detection with IHittable interface
// =============================================================================

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Netcode;

namespace NscGame.Enemy
{
    [RequireComponent(typeof(EnemyController))]
    [RequireComponent(typeof(AudioSource))]
    public class EnemyCombat : NetworkBehaviour
    {
        #region Inspector Fields

        [Header("Hitbox - Default Origin")]
        [Tooltip("Fallback origin if specific attack origin not set")]
        [SerializeField] private Transform hitboxOrigin;

        [Header("Hitbox - Per Attack Origin")]
        [Tooltip("Origin for Light Punch (e.g., hand bone)")]
        [SerializeField] private Transform lightPunchOrigin;

        [Tooltip("Origin for Barrage Punch (e.g., hand bone)")]
        [SerializeField] private Transform barragePunchOrigin;

        [Tooltip("Origin for Kick (e.g., foot bone)")]
        [SerializeField] private Transform kickOrigin;

        [Header("Hitbox Radius")]
        [SerializeField] private float lightPunchRadius = 4.0f;
        [SerializeField] private float barragePunchRadius = 3.0f;
        [SerializeField] private float kickRadius = 4f;

        [SerializeField] private LayerMask playerLayer;

        [Header("Simple Hit (ยึดตัวผู้เล่นเป็นศูนย์กลาง)")]
        [Tooltip("เพดานระยะที่หมัดถือว่า 'เอื้อมถึง' ผู้เล่น — วิ่งหนีพ้นระยะนี้ก่อนจังหวะกระแทก = หลบได้\nตั้งให้ ≈ Attack Range ของ EnemyController + เผื่อเล็กน้อย")]
        [SerializeField] private float maxStrikeReach = 16f;

        [Header("Hit Timing (วินาทีหลังเริ่มท่า → จังหวะคิดดาเมจ)")]
        [Tooltip("จูนให้ตรงเฟรมที่หมัดกระแทกในอนิเมชัน LightPunch")]
        [SerializeField] private float lightPunchHitDelay = 0.5f;

        [Tooltip("จูนให้ตรงเฟรมที่เท้ากระแทกในอนิเมชัน Kick")]
        [SerializeField] private float kickHitDelay = 0.8f;

        private EnemyController controller; // ใช้ดึงเป้าปัจจุบันสดๆ ทุกครั้งที่เช็คโดน

        [Header("Damage Values")]
        [SerializeField] private float lightPunchDamage = 10f;
        [SerializeField] private float barragePunchDamage = 5f;
        [SerializeField] private int barragePunchHitCount = 6;
        [SerializeField] private float kickDamage = 25f;

        [Header("Barrage Timing")]
        [Tooltip("ความยาวอนิเมชันต่อยรัว (วินาที) — ดาเมจทุกดอกกระจายเต็มช่วงนี้ และ VFX เล่นยาวจนจบท่า")]
        [SerializeField] private float barrageDuration = 1.5f;

        [Header("Attack Animation Reset")]
        [Tooltip("หน่วงหลังดาเมจดอกสุดท้าย ก่อนตัดอนิเมชันกลับท่าเริ่มต้น (ให้ภาพกระแทกค้างแป๊บนึง)")]
        [SerializeField] private float attackRecoverDelay = 0.25f;

        [Tooltip("ระยะเวลา crossfade ตอนตัดกลับท่าเริ่มต้น")]
        [SerializeField] private float attackResetFade = 0.15f;

        // คลิปอนิเมชันโจมตียาว 3-4 วิ แต่ดาเมจจบใน ~1 วิ — ดาเมจจบเมื่อไหร่
        // ตัดอนิเมชันกลับ state ว่าง (ท่าเริ่มต้น) ทันที ภาพ/ดาเมจ/VFX จะจบพร้อมกัน
        private const string AttackLayerName = " Attack layer"; // ชื่อจริงใน controller มี space นำหน้า
        private const string AttackIdleState = "New State";     // state ว่าง (default) ของ layer โจมตี

        [Header("VFX Prefabs")]
        [SerializeField] private GameObject punchVfxPrefab;
        [SerializeField] private GameObject barrageVfxPrefab;
        [SerializeField] private GameObject kickVfxPrefab;

        [Header("VFX Rotation Offset (degrees)")]
        [Tooltip("Additional rotation applied to VFX (X, Y, Z)")]
        [SerializeField] private Vector3 lightPunchRotationOffset = Vector3.zero;
        [SerializeField] private Vector3 barragePunchRotationOffset = Vector3.zero;
        [SerializeField] private Vector3 kickRotationOffset = Vector3.zero;

        [Header("Sound Effects")]
        [SerializeField] private AudioClip sfxPunch;
        [SerializeField] private AudioClip sfxBarrage;
        [SerializeField] private AudioClip sfxKick;
        [SerializeField] private AudioSource audioSource;

        #endregion

        #region Private Fields

        private Animator animator;

        /// <summary>Tracks currently playing VFX per attack type (max 1 per type)</summary>
        private readonly Dictionary<AttackType, GameObject> activeVfxByType = new Dictionary<AttackType, GameObject>();

        /// <summary>Tracks active VFX cleanup coroutines to prevent leaks</summary>
        private readonly Dictionary<AttackType, Coroutine> activeVfxCoroutines = new Dictionary<AttackType, Coroutine>();

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            animator = GetComponent<Animator>();
            // เคารพค่าที่ลากใส่ Inspector ก่อน — เดิมเขียนทับเสมอทำให้ลาก AudioSource อื่นมาใส่ไม่มีผล
            if (audioSource == null)
                audioSource = GetComponent<AudioSource>();
            controller = GetComponent<EnemyController>();

            if (hitboxOrigin == null)
                hitboxOrigin = transform;
        }

        private void OnDestroy()
        {
            // Clean up any running coroutines to prevent leaks
            foreach (var kvp in activeVfxCoroutines)
            {
                if (kvp.Value != null)
                    StopCoroutine(kvp.Value);
            }
            activeVfxCoroutines.Clear();

            // Destroy any remaining VFX objects
            foreach (var kvp in activeVfxByType)
            {
                if (kvp.Value != null)
                    Destroy(kvp.Value);
            }
            activeVfxByType.Clear();
        }

        #endregion

        #region Helpers - Origin & Rotation

        private Transform GetOriginFor(AttackType type)
        {
            return type switch
            {
                AttackType.LightPunch => lightPunchOrigin != null ? lightPunchOrigin : hitboxOrigin,
                AttackType.BarragePunch => barragePunchOrigin != null ? barragePunchOrigin : hitboxOrigin,
                AttackType.Kick => kickOrigin != null ? kickOrigin : hitboxOrigin,
                _ => hitboxOrigin
            };
        }

        private Vector3 GetRotationOffsetFor(AttackType type)
        {
            return type switch
            {
                AttackType.LightPunch => lightPunchRotationOffset,
                AttackType.BarragePunch => barragePunchRotationOffset,
                AttackType.Kick => kickRotationOffset,
                _ => Vector3.zero
            };
        }

        private Quaternion GetVfxRotation(AttackType type)
        {
            return transform.rotation * Quaternion.Euler(GetRotationOffsetFor(type));
        }

        #endregion

        #region Server - Attack Execution

        /// <summary>
        /// [SERVER ONLY] Execute attack and return animation duration
        /// Called by EnemyController
        /// </summary>
        public float ServerExecuteAttack(AttackType type)
        {
            if (!IsServer) return 0f;

            // Stop any active VFX from previous attack before starting new one
            StopAllActiveVfxClientRpc();

            return type switch
            {
                AttackType.LightPunch => ExecuteLightPunch(),
                AttackType.BarragePunch => ExecuteBarragePunch(),
                AttackType.Kick => ExecuteKick(),
                _ => 0f
            };
        }

        /// <summary>
        /// [SERVER ONLY] Stop all active combat VFX/SFX on all clients.
        /// Called by EnemyController when a combo finishes or is aborted.
        /// </summary>
        public void ServerStopAllCombatEffects()
        {
            if (!IsServer) return;
            StopAllActiveVfxClientRpc();
        }

        /// <summary>
        /// [CLIENT] Stop all active combat VFX/SFX locally on this machine.
        /// Called by EnemyController's OnStateChanged callback on clients.
        /// </summary>
        public void StopAllCombatEffectsLocal()
        {
            foreach (var kvp in activeVfxByType.ToArray())
            {
                if (kvp.Value != null)
                {
                    ParticleSystem ps = kvp.Value.GetComponentInChildren<ParticleSystem>();
                    if (ps != null)
                        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                    if (activeVfxCoroutines.TryGetValue(kvp.Key, out Coroutine coroutine) && coroutine != null)
                    {
                        StopCoroutine(coroutine);
                        activeVfxCoroutines.Remove(kvp.Key);
                    }

                    Destroy(kvp.Value);
                    activeVfxByType.Remove(kvp.Key);
                }
            }
        }

        // ระยะเวลาท่าต้องยาวกว่า hit delay เสมอ — ไม่งั้น EnemyController จบท่า
        // แล้วเริ่มท่าใหม่ก่อนดาเมจท่าเก่าจะลง (delay เป็นค่าจูนได้ใน Inspector)
        private float ExecuteLightPunch()
        {
            StartCoroutine(ServerLightPunchRoutine());
            return Mathf.Max(0.8f, lightPunchHitDelay + 0.2f);
        }

        private float ExecuteBarragePunch()
        {
            StartCoroutine(ServerBarrageRoutine());
            return barrageDuration + 0.3f;
        }

        private float ExecuteKick()
        {
            StartCoroutine(ServerKickRoutine());
            return Mathf.Max(1.2f, kickHitDelay + 0.2f);
        }

        #endregion

        #region Server - Attack Routines

        private IEnumerator ServerLightPunchRoutine()
        {
            Transform origin = GetOriginFor(AttackType.LightPunch);

            PlayAttackEffectsClientRpc(AttackType.LightPunch, origin.position, GetVfxRotation(AttackType.LightPunch));

            yield return new WaitForSeconds(lightPunchHitDelay);

            ProcessHitDetection(origin.position, lightPunchRadius, lightPunchDamage, AttackType.LightPunch);

            // ดาเมจจบแล้ว → ตัดอนิเมชันกลับท่าเริ่มต้น (คลิปยาว 4 วิ ปล่อยไว้ภาพจะค้างท่าต่อย)
            yield return new WaitForSeconds(attackRecoverDelay);
            ResetAttackLayerClientRpc();
        }

        private IEnumerator ServerBarrageRoutine()
        {
            Transform origin = GetOriginFor(AttackType.BarragePunch);

            PlayAttackEffectsClientRpc(AttackType.BarragePunch, origin.position, GetVfxRotation(AttackType.BarragePunch));

            // กระจายดาเมจทุกดอกให้เต็มความยาวอนิเมชัน — เดิมตีครบใน 0.6 วิแรก
            // แล้วช่วงท้ายท่าเป็นช่วงตาย ดาเมจ/ฟีลไม่ตรงกับภาพที่ยังต่อยรัวอยู่
            float interval = barrageDuration / Mathf.Max(1, barragePunchHitCount);

            for (int i = 0; i < barragePunchHitCount; i++)
            {
                yield return new WaitForSeconds(interval);

                // บอสตาย/ล้มกลางท่า → หยุดรัวทันที (ศพห้ามตีต่อ)
                if (controller != null && controller.CurrentState == EnemyState.Dead)
                {
                    ResetAttackLayerClientRpc();
                    yield break;
                }

                ProcessHitDetection(origin.position, barragePunchRadius, barragePunchDamage, AttackType.BarragePunch);
            }

            // ดาเมจครบทุกดอกแล้ว → ตัดอนิเมชันกลับท่าเริ่มต้น (คลิปต่อยรัวเป็น loop ปล่อยไว้จะรัวไม่หยุด)
            yield return new WaitForSeconds(attackRecoverDelay);
            ResetAttackLayerClientRpc();
        }

        private IEnumerator ServerKickRoutine()
        {
            Transform origin = GetOriginFor(AttackType.Kick);

            PlayAttackEffectsClientRpc(AttackType.Kick, origin.position, GetVfxRotation(AttackType.Kick));

            yield return new WaitForSeconds(kickHitDelay);

            ProcessHitDetection(origin.position, kickRadius, kickDamage, AttackType.Kick);

            yield return new WaitForSeconds(attackRecoverDelay);
            ResetAttackLayerClientRpc();
        }

        private void ProcessHitDetection(Vector3 origin, float radius, float damage, AttackType attackType)
        {
            // ✅ [Simple Hit v2] หุ่นผู้เล่นตัวใหญ่มาก (มือ/เท้าห่างลำตัว 15-18m) แต่ radius แค่ 5-7m
            // → OverlapSphere รอบลำตัวไม่มีทางแตะชิ้นที่มี RobotHealth (มือ/เท้า) เลย = โดน 0 ตลอด
            // แก้เป็น: หา IHittable ทุกชิ้นของหุ่นเป้าหมายตรงๆ — ชิ้นในรัศมีจากจุดกระแทกโดนหมด
            // ถ้าไม่มีสักชิ้น โดนชิ้นที่ใกล้จุดกระแทกสุด 1 ชิ้นเสมอ (อยู่ในระยะ = ตีติดแน่นอน)
            // การหลบ = วิ่งออกนอก maxStrikeReach ก่อนจังหวะกระแทก
            // ✅ บอสตายระหว่างรอจังหวะกระแทก (ผู้เล่นต่อยตายทัน) → ศพห้ามคิดดาเมจ
            if (controller != null && controller.CurrentState == EnemyState.Dead)
            {
                Debug.Log($"[EnemyCombat] 💀 {attackType} ยกเลิก — บอสตายก่อนจังหวะกระแทก");
                return;
            }

            Transform playerTransform = controller != null ? controller.PlayerTarget : null;
            if (playerTransform == null)
            {
                Debug.Log($"[EnemyCombat] ⚠️ {attackType} ไม่มีเป้าหมาย (PlayerTarget null)");
                return;
            }

            // ✅ เช็คระยะ "แนวราบ" เท่านั้น — ลำตัวหุ่นลอยอยู่ y≈16 ระยะ 3D เกิน 16m ตลอด → วืดฟรี
            Vector3 flatSelf = transform.position;
            Vector3 flatTarget = playerTransform.position;
            flatSelf.y = 0f;
            flatTarget.y = 0f;
            if (Vector3.Distance(flatSelf, flatTarget) > maxStrikeReach)
            {
                Debug.Log($"[EnemyCombat] 💨 {attackType} วืด — ผู้เล่นหนีพ้นระยะ");
                return;
            }

            // ชิ้นส่วนที่รับดาเมจได้ทั้งหมดของหุ่นเป้าหมาย (RobotHealth อยู่บนมือ/เท้า)
            // ข้ามชิ้นที่หลุดไปแล้ว — ตีชิ้นที่กองพื้นอยู่ = ดาเมจฟรี ไม่มีความหมาย
            IHittable[] allParts = playerTransform.root.GetComponentsInChildren<IHittable>();
            List<IHittable> partList = new List<IHittable>();
            foreach (IHittable p in allParts)
            {
                if (p is RobotHealth rh && rh.Jpar != null && !rh.Jpar.IsConnected)
                    continue;
                partList.Add(p);
            }
            IHittable[] parts = partList.ToArray();

            if (parts.Length == 0)
            {
                Debug.Log($"[EnemyCombat] 🏆 {attackType} — ชิ้นส่วนหุ่นหลุดหมดแล้ว ไม่เหลืออะไรให้ตี");
                return;
            }

            // Barrage ห้ามยิง hit-confirm รายดอก — VFX จำกัด 1 ตัว/ประเภท ดอกใหม่จะ
            // Destroy VFX หลักที่กำลังเล่นทุก interval → เอฟเฟคกระพริบไม่เคยเล่นจบท่า
            // (VFX หลักถูก spawn ตอนเริ่มท่าแล้ว ให้เล่นยาวจนจบเอง)
            bool spawnHitVfx = attackType != AttackType.BarragePunch;

            int hitCount = 0;

            foreach (IHittable part in parts)
            {
                Transform partT = ((Component)part).transform;
                float dist = Vector3.Distance(origin, partT.position);

                if (dist <= radius)
                {
                    part.ServerTakeDamage(damage, attackType, transform.forward);
                    if (spawnHitVfx)
                        SpawnHitConfirmClientRpc(attackType, partT.position, GetVfxRotation(attackType));
                    hitCount++;
                }
            }

            // ไม่มีชิ้นไหนอยู่ในรัศมี แต่ผู้เล่นยังอยู่ในระยะเอื้อม → โดน 1 ชิ้น
            // สุ่มจาก "ทุกชิ้น" เท่าๆ กัน — ถ้าเลือกตามระยะ เท้าซึ่งอยู่ระดับพื้น
            // เดียวกับจุดกระแทกบอสจะใกล้สุดตลอด → ขาโดนล้วนๆ มือไม่เคยโดนเลย
            if (hitCount == 0)
            {
                IHittable picked = parts[Random.Range(0, parts.Length)];

                Transform partT = ((Component)picked).transform;
                picked.ServerTakeDamage(damage, attackType, transform.forward);
                if (spawnHitVfx)
                    SpawnHitConfirmClientRpc(attackType, partT.position, GetVfxRotation(attackType));
                hitCount = 1;
            }

            Debug.Log($"[EnemyCombat] 👊 {attackType} โดน {hitCount} ชิ้น (dmg {damage}/ชิ้น)");
        }

        #endregion

        #region Client RPC - Visual Effects

        [ClientRpc]
        private void StopAllActiveVfxClientRpc()
        {
            // Stop all active VFX when switching to a new attack
            foreach (var kvp in activeVfxByType.ToArray())
            {
                if (kvp.Value != null)
                {
                    // Stop particle emission immediately
                    ParticleSystem ps = kvp.Value.GetComponentInChildren<ParticleSystem>();
                    if (ps != null)
                    {
                        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    }

                    // Stop cleanup coroutine
                    if (activeVfxCoroutines.TryGetValue(kvp.Key, out Coroutine coroutine) && coroutine != null)
                    {
                        StopCoroutine(coroutine);
                        activeVfxCoroutines.Remove(kvp.Key);
                    }

                    // Destroy immediately
                    Destroy(kvp.Value);
                    activeVfxByType.Remove(kvp.Key);
                }
            }
        }

        [ClientRpc]
        private void PlayAttackEffectsClientRpc(AttackType type, Vector3 vfxPosition, Quaternion vfxRotation)
        {
            switch (type)
            {
                case AttackType.LightPunch:
                    animator.SetTrigger(EnemyAnimParam.LightPunch);
                    SpawnVfx(type, punchVfxPrefab, vfxPosition, vfxRotation);
                    PlaySfx(sfxPunch);
                    break;

                case AttackType.BarragePunch:
                    animator.SetTrigger(EnemyAnimParam.BarragePunch);
                    SpawnVfx(type, barrageVfxPrefab, vfxPosition, vfxRotation);
                    PlaySfx(sfxBarrage);
                    break;

                case AttackType.Kick:
                    animator.SetTrigger(EnemyAnimParam.Kick);
                    SpawnVfx(type, kickVfxPrefab, vfxPosition, vfxRotation);
                    PlaySfx(sfxKick);
                    break;
            }
        }

        [ClientRpc]
        private void ResetAttackLayerClientRpc()
        {
            if (animator == null) return;

            int layer = animator.GetLayerIndex(AttackLayerName);
            if (layer < 0) layer = 2; // fallback: layer โจมตีคือ index 2 ใน controller ปัจจุบัน

            animator.CrossFade(AttackIdleState, attackResetFade, layer);
        }

        [ClientRpc]
        private void SpawnHitConfirmClientRpc(AttackType type, Vector3 impactPoint, Quaternion impactRotation)
        {
            GameObject prefab = type switch
            {
                AttackType.LightPunch => punchVfxPrefab,
                AttackType.BarragePunch => barrageVfxPrefab,
                AttackType.Kick => kickVfxPrefab,
                _ => null
            };

            SpawnVfx(type, prefab, impactPoint, impactRotation);
        }

        #endregion

        #region Client - VFX Management

        /// <summary>
        /// Spawn VFX with automatic cleanup, limiting to one active VFX per attack type
        /// Old VFX of same type is destroyed immediately when new one spawns
        /// </summary>
        private void SpawnVfx(AttackType type, GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null) return;

            // Destroy old VFX of this type if still playing
            if (activeVfxByType.TryGetValue(type, out GameObject oldVfx) && oldVfx != null)
            {
                // Stop old cleanup coroutine
                if (activeVfxCoroutines.TryGetValue(type, out Coroutine oldCoroutine) && oldCoroutine != null)
                {
                    StopCoroutine(oldCoroutine);
                    activeVfxCoroutines.Remove(type);
                }

                Destroy(oldVfx);
            }

            // Spawn new VFX
            GameObject vfx = Instantiate(prefab, position, rotation);
            activeVfxByType[type] = vfx;

            // Start cleanup coroutine and track it
            Coroutine cleanupCoroutine = StartCoroutine(DestroyWhenParticleFinished(type, vfx));
            activeVfxCoroutines[type] = cleanupCoroutine;
        }

        private IEnumerator DestroyWhenParticleFinished(AttackType type, GameObject vfx)
        {
            ParticleSystem ps = vfx.GetComponentInChildren<ParticleSystem>();

            if (ps == null)
            {
                // No particle system found, fallback timeout
                yield return new WaitForSeconds(3f);
                FinishVfx(type, vfx);
                yield break;
            }

            // Wait until all particles are finished (including children)
            while (vfx != null && ps.IsAlive(true))
            {
                yield return null;
            }

            FinishVfx(type, vfx);
        }

        private void FinishVfx(AttackType type, GameObject vfx)
        {
            if (vfx == null) return;

            // Clear tracking only if this VFX is still the active one
            if (activeVfxByType.TryGetValue(type, out GameObject current) && current == vfx)
            {
                activeVfxByType.Remove(type);
            }

            if (activeVfxCoroutines.ContainsKey(type))
            {
                activeVfxCoroutines.Remove(type);
            }

            Destroy(vfx);
        }

        private void PlaySfx(AudioClip clip)
        {
            if (clip == null || audioSource == null) return;
            audioSource.PlayOneShot(clip);
        }

        #endregion

        #region Debug Gizmos

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

        #endregion
    }

    // =========================================================================
    //  IHittable Interface
    //  Implement this on player scripts to receive damage from enemy attacks
    // =========================================================================
    public interface IHittable
    {
        /// <summary>Receive damage on server</summary>
        void ServerTakeDamage(float amount, AttackType source);
        void ServerTakeDamage(float amount, AttackType source, Vector3 direction);
    }
}
