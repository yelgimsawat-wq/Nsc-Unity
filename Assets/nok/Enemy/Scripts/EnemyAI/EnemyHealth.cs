// =============================================================================
//  EnemyHealth.cs
//  Manages enemy HP and knockback reactions
//
//  Usage from Player Attack Script:
//  ─────────────────────────────────────────────────────────────────────────
//  EnemyHealth enemyHp = hitCollider.GetComponentInParent<EnemyHealth>();
//  if (enemyHp != null)
//      enemyHp.ServerTakeHit(damage, hitDirection, knockbackForce);
//
//  Note: hitDirection should be the attacking player's forward direction
// =============================================================================

using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;

namespace NscGame.Enemy
{
    [RequireComponent(typeof(EnemyController))]
    [RequireComponent(typeof(NavMeshAgent))]
    public class EnemyHealth : NetworkBehaviour
    {
        #region Inspector Fields

        [Header("HP Settings")]
        [SerializeField] private float maxHp = 100f;

        [Header("Knockback Settings")]
        [Tooltip("Duration of knockback effect (seconds)")]
        [SerializeField] private float knockbackDuration = 0.3f;

        [Tooltip("Knockback force multiplier")]
        [SerializeField] private float knockbackMultiplier = 1.0f;

        [Tooltip("HP threshold for bonus knockback (0-1)")]
        [SerializeField, Range(0f, 1f)] private float weakKnockbackThreshold = 0.3f;

        [Tooltip("Additional knockback when HP is below threshold")]
        [SerializeField] private float weakKnockbackBonus = 0.5f;

        [Header("Hit Reaction VFX/SFX")]
        [SerializeField] private GameObject hitVfxPrefab;
        [SerializeField] private AudioClip sfxHit;
        [SerializeField] private AudioSource audioSource;

        #endregion

        #region Network Variables

        /// <summary>Current HP synchronized to all clients for HP bar display</summary>
        public NetworkVariable<float> CurrentHp = new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        #endregion

        #region Private Fields

        private EnemyController controller;
        private NavMeshAgent agent;
        private bool isDead = false;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            controller = GetComponent<EnemyController>();
            agent = GetComponent<NavMeshAgent>();
            audioSource = GetComponent<AudioSource>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (IsServer)
                CurrentHp.Value = maxHp;
        }

        #endregion

        #region Server - Damage & Knockback

        /// <summary>
        /// [SERVER ONLY] Apply damage and knockback to enemy
        /// Called from player attack scripts
        /// </summary>
        /// <param name="damage">Damage amount to apply</param>
        /// <param name="hitDirection">Direction of attack (usually player's forward)</param>
        /// <param name="knockbackForce">Base knockback force (recommended: 3-8)</param>
        public void ServerTakeHit(float damage, Vector3 hitDirection, float knockbackForce)
        {
            if (!IsServer || isDead) return;

            // Apply damage
            CurrentHp.Value = Mathf.Max(0, CurrentHp.Value - damage);

            // Check for death before knockback
            if (CurrentHp.Value <= 0)
            {
                ServerDie();
                return;
            }

            // Calculate knockback with weakness bonus
            float hpRatio = CurrentHp.Value / maxHp;
            float bonusForce = (hpRatio < weakKnockbackThreshold) ? weakKnockbackBonus : 0f;
            float totalForce = knockbackForce * knockbackMultiplier + bonusForce;

            // Calculate knockback direction (horizontal + slight upward)
            Vector3 knockDir = hitDirection.normalized;
            knockDir.y = 0.15f;
            knockDir.Normalize();

            // Broadcast hit effects to all clients
            Vector3 impactPos = transform.position + Vector3.up * 0.8f;
            PlayHitEffectsClientRpc(impactPos);

            // Apply knockback on server
            StartCoroutine(ServerKnockbackRoutine(knockDir * totalForce));
        }

        private IEnumerator ServerKnockbackRoutine(Vector3 force)
        {
            // Notify controller to pause AI
            controller.ServerBeginKnockback();

            // Disable NavMeshAgent temporarily
            agent.isStopped = true;
            agent.enabled = false;

            float elapsed = 0f;
            while (elapsed < knockbackDuration)
            {
                // Ease out knockback force over time
                float t = elapsed / knockbackDuration;
                float eased = 1f - t * t; // Ease Out Quad
                Vector3 movement = force * eased * Time.deltaTime;

                Vector3 targetPos = transform.position + movement;

                // Ensure new position stays on NavMesh
                if (NavMesh.SamplePosition(targetPos, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                {
                    transform.position = hit.position;
                }
                else
                {
                    Debug.LogWarning("[EnemyHealth] Knockback target position is off NavMesh, skipping movement.");
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            // Re-enable NavMeshAgent and warp to final position
            agent.enabled = true;

            if (agent.enabled && NavMesh.SamplePosition(transform.position, out NavMeshHit finalHit, 2f, NavMesh.AllAreas))
            {
                agent.Warp(finalHit.position);
            }

            agent.isStopped = false;

            controller.ServerEndKnockback();
        }

        private void ServerDie()
        {
            isDead = true;
            controller.ServerDie();

            // 🏆 [GameFlow] บอสตาย = ชนะ — แจ้ง GameFlowManager โชว์ WinPanel ทุกเครื่อง
            // (ซีนที่ไม่มี GameFlowManager เช่นซีนเทส ก็แค่ไม่เกิดอะไร)
            if (GameFlowManager.Instance != null)
                GameFlowManager.Instance.TriggerVictory();
        }

        #endregion

        #region Client RPC - Visual Effects

        /// <summary>Spawn VFX and play SFX on all clients when hit</summary>
        [ClientRpc]
        private void PlayHitEffectsClientRpc(Vector3 impactPosition)
        {
            // Spawn VFX
            if (hitVfxPrefab != null)
            {
                GameObject vfx = Instantiate(hitVfxPrefab, impactPosition, Quaternion.identity);
                ParticleSystem ps = vfx.GetComponentInChildren<ParticleSystem>();
                Destroy(vfx, ps != null ? ps.main.duration + 1f : 2f);
            }

            // Play SFX
            if (sfxHit != null && audioSource != null)
                audioSource.PlayOneShot(sfxHit);
        }

        #endregion

        #region Public API

        public float GetHpPercent() => CurrentHp.Value / maxHp;
        public bool IsDead() => isDead;
        public float GetMaxHp() => maxHp;

        #endregion

        #region Debug Tools

        [ContextMenu("Test Kill (Server Only)")]
        public void TestKill()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("Test Kill only works in Play Mode!");
                return;
            }

            if (IsSpawned)
            {
                if (IsServer)
                {
                    ServerTakeHit(CurrentHp.Value, Vector3.forward, 5f);
                }
                else
                {
                    Debug.LogWarning("Test Kill must be executed on Server/Host!");
                }
            }
            else
            {
                // Fallback for non-networked testing
                isDead = true;
                if (controller != null)
                    controller.ServerDie();
            }
        }

        #endregion
    }

#if UNITY_EDITOR
    [UnityEditor.CustomEditor(typeof(EnemyHealth))]
    public class EnemyHealthEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EnemyHealth enemyHealth = (EnemyHealth)target;

            UnityEngine.GUILayout.Space(15);

            UnityEngine.GUI.backgroundColor = Color.red;
            if (UnityEngine.GUILayout.Button("Kill Enemy (Test Death)", UnityEngine.GUILayout.Height(30)))
            {
                enemyHealth.TestKill();
            }
            UnityEngine.GUI.backgroundColor = Color.white;
        }
    }
#endif
}
