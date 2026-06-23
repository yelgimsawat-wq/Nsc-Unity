// =============================================================================
//  EnemyController.cs
//  Server-authoritative AI state machine: Idle → Walk → Roll → Attack → Dead
//
//  Animator Blend Tree Setup:
//  ─────────────────────────────────────────────────────────────────────────
//  Layer 0 – Base Layer
//    └─ BlendTree (1D, Parameter: "Speed")
//         ├─ Idle @ 0.0
//         └─ Walk @ 0.5
//
//  Layer 1 – Roll Layer (Override, bool "IsRolling")
//    └─ BlendTree (2D Freeform Directional, "RollX", "RollY")
//         ├─ Roll Forward  @ (0,  1)
//         ├─ Roll Backward @ (0, -1)
//         ├─ Roll Left     @ (-1, 0)
//         └─ Roll Right    @ ( 1, 0)
//
//  Layer 2 – Attack Layer (Override)
//    ├─ Trigger "LightPunch"   → Punch animation
//    ├─ Trigger "BarragePunch" → Combo animation
//    └─ Trigger "Kick"         → Kick animation
// =============================================================================

using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;

namespace NscGame.Enemy
{
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(EnemyCombat))]
    public class EnemyController : NetworkBehaviour
    {
        #region Inspector Fields

        [Header("Detection Ranges")]
        [Tooltip("Attack range - enemy stops completely and attacks")]
        [SerializeField] private float attackRange = 1.5f;

        [Tooltip("Stop distance - enemy switches from roll to walk")]
        [SerializeField] private float stopDistance = 2.5f;

        [Tooltip("Roll trigger distance - enemy starts continuous rolling")]
        [SerializeField] private float rollTriggerDistance = 8.0f;

        [Tooltip("Walk threshold - maximum detection range")]
        [SerializeField] private float walkThreshold = 12.0f;

        [Header("Movement Speed")]
        [SerializeField] private float walkSpeed = 2.5f;
        [SerializeField] private float rollSpeed = 6.0f;

        [Header("Animator Smoothing")]
        [SerializeField] private float speedDampTime = 0.15f;

        [Header("Attack Timing")]
        [Tooltip("Decision interval while idle in attack range")]
        [SerializeField] private float attackDecisionInterval = 0.5f;

        [Tooltip("Cooldown after combo finishes")]
        [SerializeField] private float postAttackCooldown = 1.0f;

        [Tooltip("Short delay between repeated attacks in combo")]
        [SerializeField] private float comboRepeatGap = 0.15f;

        [Header("Attack Selection Weights")]
        [SerializeField] private float lightPunchWeight = 40f;
        [SerializeField] private float barragePunchWeight = 30f;
        [SerializeField] private float kickWeight = 30f;

        [Header("Attack Repeat Count (Min, Max)")]
        [SerializeField] private Vector2Int lightPunchRepeatRange = new Vector2Int(1, 1);
        [SerializeField] private Vector2Int barragePunchRepeatRange = new Vector2Int(1, 3);
        [SerializeField] private Vector2Int kickRepeatRange = new Vector2Int(1, 1);

        #endregion

        #region Network Variables

        private NetworkVariable<EnemyState> netState = new NetworkVariable<EnemyState>(
            EnemyState.Idle,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private NetworkVariable<float> netSpeed = new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private NetworkVariable<Vector2> netRollDir = new NetworkVariable<Vector2>(
            Vector2.zero,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        #endregion

        #region Private Fields

        private NavMeshAgent agent;
        private Animator animator;
        private EnemyCombat combat;
        private EnemyRagdoll ragdoll;
        private Transform playerTarget;

        private float attackDecTimer = 0f;
        private bool isActing = false;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            animator = GetComponent<Animator>();
            combat = GetComponent<EnemyCombat>();
            ragdoll = GetComponent<EnemyRagdoll>();

            agent.updateRotation = true;
            agent.angularSpeed = 300f;
            agent.stoppingDistance = 0f; // Manual control via isStopped
            agent.autoBraking = true;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            netState.OnValueChanged += OnStateChanged;
            netRollDir.OnValueChanged += OnRollDirChanged;

            if (IsServer)
            {
                GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
                if (playerObj != null)
                    playerTarget = playerObj.transform;

                StartCoroutine(ServerDecisionLoop());
            }
        }

        public override void OnNetworkDespawn()
        {
            netState.OnValueChanged -= OnStateChanged;
            netRollDir.OnValueChanged -= OnRollDirChanged;
            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (animator != null)
            {
                animator.SetFloat(EnemyAnimParam.Speed, netSpeed.Value);
            }
        }

        #endregion

        #region Server - Decision Loop

        private IEnumerator ServerDecisionLoop()
        {
            while (true)
            {
                yield return null;

                if (playerTarget == null || netState.Value == EnemyState.Dead)
                    continue;

                if (isActing)
                {
                    attackDecTimer -= Time.deltaTime;
                    continue;
                }

                attackDecTimer -= Time.deltaTime;

                float dist = Vector3.Distance(transform.position, playerTarget.position);

                // Priority 1: Attack range - stop and attack
                if (dist <= attackRange)
                {
                    agent.isStopped = true;
                    netSpeed.Value = 0f;

                    if (attackDecTimer <= 0f)
                    {
                        attackDecTimer = attackDecisionInterval;
                        ChooseAndExecuteAttack();
                    }
                    else
                    {
                        SetState(EnemyState.Idle);
                    }
                }
                // Priority 2: Stop distance - walk closer (between roll end and attack range)
                else if (dist <= stopDistance && dist > attackRange)
                {
                    agent.isStopped = false;
                    agent.speed = walkSpeed;
                    agent.SetDestination(playerTarget.position);
                    netSpeed.Value = 0.5f;
                    SetState(EnemyState.Walk);
                }
                // Priority 3: Roll distance - continuous rolling toward player
                else if (dist <= rollTriggerDistance && dist > stopDistance)
                {
                    agent.isStopped = false;
                    agent.speed = rollSpeed;
                    agent.SetDestination(playerTarget.position);

                    Vector3 localVel = transform.InverseTransformDirection(agent.velocity.normalized);
                    netRollDir.Value = new Vector2(localVel.x, localVel.z);

                    SetState(EnemyState.Roll);
                }
                // Priority 4: Walk threshold - normal walk approach
                else if (dist <= walkThreshold && dist > rollTriggerDistance)
                {
                    agent.isStopped = false;
                    agent.speed = walkSpeed;
                    agent.SetDestination(playerTarget.position);
                    netSpeed.Value = 0.5f;
                    SetState(EnemyState.Walk);
                }
                // Priority 5: Out of range - idle
                else
                {
                    agent.isStopped = true;
                    netSpeed.Value = 0f;
                    SetState(EnemyState.Idle);
                }
            }
        }

        #endregion

        #region Server - Attack System

        private void ChooseAndExecuteAttack()
        {
            if (isActing) return;

            AttackType chosen = PickWeightedAttack();
            int repeatCount = GetRepeatCountFor(chosen);

            SetState(EnemyState.Attack);
            StartCoroutine(ExecuteAttackComboCoroutine(chosen, repeatCount));
        }

        private AttackType PickWeightedAttack()
        {
            float totalWeight = lightPunchWeight + barragePunchWeight + kickWeight;
            if (totalWeight <= 0f) totalWeight = 1f;

            float roll = Random.value * totalWeight;

            if (roll < lightPunchWeight)
                return AttackType.LightPunch;
            else if (roll < lightPunchWeight + barragePunchWeight)
                return AttackType.BarragePunch;
            else
                return AttackType.Kick;
        }

        private int GetRepeatCountFor(AttackType type)
        {
            Vector2Int range = type switch
            {
                AttackType.LightPunch => lightPunchRepeatRange,
                AttackType.BarragePunch => barragePunchRepeatRange,
                AttackType.Kick => kickRepeatRange,
                _ => new Vector2Int(1, 1)
            };

            int min = Mathf.Max(1, range.x);
            int max = Mathf.Max(min, range.y);

            return Random.Range(min, max + 1);
        }

        private IEnumerator ExecuteAttackComboCoroutine(AttackType type, int repeatCount)
        {
            isActing = true;

            for (int i = 0; i < repeatCount; i++)
            {
                // Stop combo if player moves out of range
                if (playerTarget == null) break;

                float dist = Vector3.Distance(transform.position, playerTarget.position);
                if (dist > attackRange) break;

                float duration = combat.ServerExecuteAttack(type);
                yield return new WaitForSeconds(duration);

                bool isLastHit = (i == repeatCount - 1);
                if (!isLastHit && comboRepeatGap > 0f)
                    yield return new WaitForSeconds(comboRepeatGap);
            }

            SetState(EnemyState.Idle);

            if (postAttackCooldown > 0f)
                yield return new WaitForSeconds(postAttackCooldown);

            isActing = false;
        }

        private void SetState(EnemyState newState)
        {
            if (netState.Value != newState)
                netState.Value = newState;
        }

        #endregion

        #region Client - Network Callbacks

        private void OnStateChanged(EnemyState previous, EnemyState current)
        {
            switch (current)
            {
                case EnemyState.Roll:
                    animator.SetBool(EnemyAnimParam.IsRolling, true);
                    break;

                case EnemyState.Dead:
                    animator.SetBool(EnemyAnimParam.IsDead, true);
                    animator.SetBool(EnemyAnimParam.IsRolling, false);
                    break;

                default:
                    animator.SetBool(EnemyAnimParam.IsRolling, false);
                    break;
            }
        }

        private void OnRollDirChanged(Vector2 previous, Vector2 current)
        {
            animator.SetFloat(EnemyAnimParam.RollX, current.x);
            animator.SetFloat(EnemyAnimParam.RollY, current.y);
        }

        #endregion

        #region Public API

        public void ServerDie()
        {
            if (!IsServer) return;

            StopAllCoroutines();
            agent.isStopped = true;
            agent.enabled = false;
            isActing = true;
            SetState(EnemyState.Dead);

            TriggerRagdollClientRpc(Vector3.zero);
        }

        [ClientRpc]
        private void TriggerRagdollClientRpc(Vector3 impactForce)
        {
            if (ragdoll != null)
                ragdoll.EnableRagdoll(impactForce);
        }

        public void ServerBeginKnockback()
        {
            if (!IsServer) return;
            isActing = true;
        }

        public void ServerEndKnockback()
        {
            if (!IsServer) return;
            isActing = false;
            SetState(EnemyState.Idle);
        }

        #endregion

        #region Debug Gizmos

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, attackRange);

            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, stopDistance);

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, rollTriggerDistance);

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, walkThreshold);
        }
#endif

        #endregion
    }
}
