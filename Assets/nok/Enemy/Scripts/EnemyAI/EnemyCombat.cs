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

        [Header("Damage Values")]
        [SerializeField] private float lightPunchDamage = 10f;
        [SerializeField] private float barragePunchDamage = 5f;
        [SerializeField] private int barragePunchHitCount = 6;
        [SerializeField] private float barrageHitInterval = 0.15f;
        [SerializeField] private float kickDamage = 25f;

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
            audioSource = GetComponent<AudioSource>();

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

        private float ExecuteLightPunch()
        {
            StartCoroutine(ServerLightPunchRoutine());
            return 0.8f;
        }

        private float ExecuteBarragePunch()
        {
            StartCoroutine(ServerBarrageRoutine());
            return barragePunchHitCount * barrageHitInterval + 0.5f;
        }

        private float ExecuteKick()
        {
            StartCoroutine(ServerKickRoutine());
            return 1.2f;
        }

        #endregion

        #region Server - Attack Routines

        private IEnumerator ServerLightPunchRoutine()
        {
            Transform origin = GetOriginFor(AttackType.LightPunch);

            PlayAttackEffectsClientRpc(AttackType.LightPunch, origin.position, GetVfxRotation(AttackType.LightPunch));

            yield return new WaitForSeconds(3.5f);

            ProcessHitDetection(origin.position, lightPunchRadius, lightPunchDamage, AttackType.LightPunch);
        }

        private IEnumerator ServerBarrageRoutine()
        {
            Transform origin = GetOriginFor(AttackType.BarragePunch);

            PlayAttackEffectsClientRpc(AttackType.BarragePunch, origin.position, GetVfxRotation(AttackType.BarragePunch));

            for (int i = 0; i < barragePunchHitCount; i++)
            {
                yield return new WaitForSeconds(barrageHitInterval);
                ProcessHitDetection(origin.position, barragePunchRadius, barragePunchDamage, AttackType.BarragePunch);
            }
        }

        private IEnumerator ServerKickRoutine()
        {
            Transform origin = GetOriginFor(AttackType.Kick);

            PlayAttackEffectsClientRpc(AttackType.Kick, origin.position, GetVfxRotation(AttackType.Kick));

            yield return new WaitForSeconds(4f);

            ProcessHitDetection(origin.position, kickRadius, kickDamage, AttackType.Kick);
        }

        private void ProcessHitDetection(Vector3 origin, float radius, float damage, AttackType attackType)
        {
            Collider[] hits = Physics.OverlapSphere(origin, radius, playerLayer);

            foreach (Collider col in hits)
            {
                IHittable target = col.GetComponent<IHittable>();
                if (target != null)
                {
                    target.ServerTakeDamage(damage, attackType, transform.forward);

                    Vector3 hitPoint = col.ClosestPoint(origin);
                    SpawnHitConfirmClientRpc(attackType, hitPoint, GetVfxRotation(attackType));
                }
            }
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
