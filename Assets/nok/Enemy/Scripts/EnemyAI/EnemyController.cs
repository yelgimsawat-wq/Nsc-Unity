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
        [Tooltip("ช่วงเวลารอต่ำสุดก่อนโจมตีครั้งต่อไป (วินาที)")]
        [SerializeField] private float minAttackInterval = 1.5f;

        [Tooltip("ช่วงเวลารอสูงสุดก่อนโจมตีครั้งต่อไป (วินาที)")]
        [SerializeField] private float maxAttackInterval = 3.0f;

        [Tooltip("Cooldown after combo finishes")]
        [SerializeField] private float postAttackCooldown = 1.0f;

        [Tooltip("Short delay between repeated attacks in combo")]
        [SerializeField] private float comboRepeatGap = 0.0f;

        [Header("Attack Selection Chance (%)")]
        [Tooltip("โอกาสในการออกท่าต่อยธรรมดา (0-100)\nรวมกันทั้งสามท่าไม่จำเป็นต้องเท่ากับ 100 - ระบบจะคำนวณสัดส่วนอัตโนมัติ")]
        [Range(0f, 100f)]
        [SerializeField] private float lightPunchChance = 40f;

        [Tooltip("โอกาสในการออกท่าต่อยรัว (0-100)")]
        [Range(0f, 100f)]
        [SerializeField] private float barragePunchChance = 30f;

        [Tooltip("โอกาสในการออกท่าเตะ (0-100)")]
        [Range(0f, 100f)]
        [SerializeField] private float kickChance = 30f;

        [Header("Attack Repeat Count (Min, Max)")]
        [SerializeField] private Vector2Int lightPunchRepeatRange = new Vector2Int(1, 1);
        [SerializeField] private Vector2Int barragePunchRepeatRange = new Vector2Int(1, 3);
        [SerializeField] private Vector2Int kickRepeatRange = new Vector2Int(1, 1);

        [Header("Rotation Settings")]
        [Tooltip("หมุนหาเพลเยอร์ด้วยความเร็วเท่าไหร่ (องศาต่อวินาที) ตอนปกติ/เดิน")]
        [SerializeField] private float turnSpeed = 720f;

        [Tooltip("ความเร็วในการหมุนหาเพลเยอร์ตอนโจมตี")]
        [SerializeField] private float attackTurnSpeed = 360f;

        [Tooltip("ความเร็วในการหมุนหาเพลเยอร์ตอนกลิ้ง (หลบ/แดช)")]
        [SerializeField] private float rollTurnSpeed = 540f;

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
        private float pacingTimer = 0f;
        private Vector3 pacingDestination;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            animator = GetComponent<Animator>();
            combat = GetComponent<EnemyCombat>();
            ragdoll = GetComponent<EnemyRagdoll>();

            agent.updateRotation = false; // Manual control of rotation to face the player quickly
            agent.angularSpeed = 300f;
            // ✅ [Bulldozer Fix] เบรกก่อนถึงตัวหุ่นที่ ~75% ของ attackRange — เดิม stoppingDistance = 0
            // บอสเดินดันหุ่นไถลไปเรื่อยๆ ระยะเลยแกว่งไม่เคยนิ่งพอให้เข้าเงื่อนไขตี
            // (พิสูจน์ผ่าน MCP แล้ว: เซ็ต 14 แล้วระยะนิ่ง → ตีติดทันที)
            agent.stoppingDistance = Mathf.Max(0f, attackRange * 0.75f);
            agent.autoBraking = true;
        }

#if UNITY_EDITOR
        // แสดงเปอร์เซ็นต์จริงใน Inspector console เมื่อแก้ค่า
        private void OnValidate()
        {
            float total = lightPunchChance + barragePunchChance + kickChance;
            if (total <= 0f) return;

            float lp = lightPunchChance / total * 100f;
            float bp = barragePunchChance / total * 100f;
            float k  = kickChance / total * 100f;

            UnityEngine.Debug.Log(
                $"[EnemyController] Attack Chance (จริง): " +
                $"ต่อยธรรมดา {lp:F1}% | " +
                $"ต่อยรัว {bp:F1}% | " +
                $"เตะ {k:F1}%"
            );
        }
#endif

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            netState.OnValueChanged += OnStateChanged;
            netRollDir.OnValueChanged += OnRollDirChanged;

            if (IsServer)
            {
                playerTarget = FindRobotTarget();
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

            if (IsServer)
            {
                RotateTowardsTarget();
            }
        }

        private void RotateTowardsTarget()
        {
            if (playerTarget == null || netState.Value == EnemyState.Dead) return;

            Vector3 direction = playerTarget.position - transform.position;
            direction.y = 0f; // Keep rotation strictly on the Y axis (no tilting up/down)

            if (direction.sqrMagnitude > 0.001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(direction);
                
                // Select turn speed based on current state
                float currentTurnSpeed = turnSpeed;
                if (netState.Value == EnemyState.Attack)
                {
                    currentTurnSpeed = attackTurnSpeed;
                }
                else if (netState.Value == EnemyState.Roll)
                {
                    currentTurnSpeed = rollTurnSpeed;
                }

                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, currentTurnSpeed * Time.deltaTime);
            }
        }

        #endregion

        #region Server - Decision Loop

        private IEnumerator ServerDecisionLoop()
        {
            while (true)
            {
                yield return null;

                if (netState.Value == EnemyState.Dead)
                    continue;

                // ✅ [NavMesh Guard] ฉากที่ไม่ได้ bake NavMesh (หรือศัตรูหลุดนอก mesh):
                // ห้ามสั่ง agent เด็ดขาด ไม่งั้น error "Stop can only be called on an active agent..."
                // จะพ่นทุกเฟรมจนอ่าน Console ไม่ได้
                if (agent == null || !agent.enabled || !agent.isOnNavMesh)
                    continue;

                if (playerTarget == null)
                {
                    playerTarget = FindRobotTarget();
                    if (playerTarget == null)
                        continue;
                }

                if (isActing)
                {
                    // Reset pacing timer when acting so we pick a new point when we start pacing again
                    pacingTimer = 0f;
                    continue;
                }

                attackDecTimer -= Time.deltaTime;

                // ✅ [Floating Torso Fix] วัดระยะ "แนวราบ" เท่านั้น — ลำตัวหุ่นลอยอยู่ที่ y≈16
                // ระยะ 3D เกิน attackRange (13) เสมอต่อให้บอสยืนชิดตัว = ไม่มีวันเข้าเงื่อนไขตี
                float dist = HorizontalDistanceTo(playerTarget.position);

                // Priority 1: Attack range and cooldown is ready - stop and attack
                if (dist <= attackRange && attackDecTimer <= 0f)
                {
                    agent.isStopped = true;
                    agent.velocity = Vector3.zero; // ✅ หยุด momentum ทันที ป้องกัน sliding
                    netSpeed.Value = 0f;
                    ChooseAndExecuteAttack();
                }
                // Priority 1b: Close to the player but on attack cooldown - walk/circle around the player
                else if (dist <= stopDistance && attackDecTimer > 0f)
                {
                    pacingTimer -= Time.deltaTime;
                    // If pacing destination is reached or timer expired, pick a new spot
                    if (pacingTimer <= 0f || Vector3.Distance(transform.position, pacingDestination) < 0.5f)
                    {
                        float randomAngle = Random.Range(-75f, 75f); // Angle offset relative to player
                        Vector3 dirToEnemy = (transform.position - playerTarget.position).normalized;
                        if (dirToEnemy == Vector3.zero) dirToEnemy = transform.forward;

                        Vector3 rotatedDir = Quaternion.Euler(0f, randomAngle, 0f) * dirToEnemy;
                        float desiredDist = Random.Range(attackRange + 0.3f, stopDistance);

                        pacingDestination = playerTarget.position + rotatedDir * desiredDist;

                        // Verify destination is valid on NavMesh
                        if (NavMesh.SamplePosition(pacingDestination, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
                        {
                            pacingDestination = hit.position;
                        }

                        pacingTimer = Random.Range(1.0f, 2.5f);
                    }

                    agent.isStopped = false;
                    agent.speed = walkSpeed * 0.8f; // Move slightly slower while pacing
                    agent.SetDestination(pacingDestination);
                    netSpeed.Value = 0.5f;
                    SetState(EnemyState.Walk);
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

            // Set the attack cooldown timer to a randomized range
            attackDecTimer = Random.Range(minAttackInterval, maxAttackInterval);

            AttackType chosen = PickWeightedAttack();
            int repeatCount = GetRepeatCountFor(chosen);

            SetState(EnemyState.Attack);
            StartCoroutine(ExecuteAttackComboCoroutine(chosen, repeatCount));
        }

        private AttackType PickWeightedAttack()
        {
            float totalWeight = lightPunchChance + barragePunchChance + kickChance;
            if (totalWeight <= 0f) totalWeight = 1f;

            float roll = Random.value * totalWeight;

            if (roll < lightPunchChance)
                return AttackType.LightPunch;
            else if (roll < lightPunchChance + barragePunchChance)
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

                float dist = HorizontalDistanceTo(playerTarget.position); // แนวราบ เหตุผลเดียวกับ decision loop
                if (dist > attackRange) break;

                bool isLastHit = (i == repeatCount - 1);
                float duration = combat.ServerExecuteAttack(type);
                yield return new WaitForSeconds(duration);

                if (!isLastHit && comboRepeatGap > 0f)
                    yield return new WaitForSeconds(comboRepeatGap);
            }

            // Immediately stop combat effects and animations when combo finishes or is aborted
            combat.ServerStopAllCombatEffects();

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

        // ✅ [No-Tag Fix] หาเป้าจาก TorsoMovement component ตรงๆ — เดิมหาจาก tag "Player"
        // ซึ่งเช็คในซีนจริงแล้ว "ไม่มีชิ้นไหนติด tag นี้เลย" (ลำตัวติด tag "Body")
        // → playerTarget เป็น null ตลอด → บอสไม่เคยเข้าโหมดโจมตีแม้แต่ครั้งเดียว
        private Transform FindRobotTarget()
        {
            TorsoMovement torso = FindFirstObjectByType<TorsoMovement>();
            if (torso == null) return null;
            return torso.torsoRb != null ? torso.torsoRb.transform : torso.transform;
        }

        private float HorizontalDistanceTo(Vector3 position)
        {
            Vector3 delta = position - transform.position;
            delta.y = 0f;
            return delta.magnitude;
        }

        #endregion

        #region Client - Network Callbacks

        private void OnStateChanged(EnemyState previous, EnemyState current)
        {
            switch (current)
            {
                case EnemyState.Roll:
                    animator.SetBool(EnemyAnimParam.IsRolling, true);
                    if (combat != null) combat.StopAllCombatEffectsLocal();
                    break;

                case EnemyState.Dead:
                    animator.SetBool(EnemyAnimParam.IsDead, true);
                    animator.SetBool(EnemyAnimParam.IsRolling, false);
                    if (combat != null) combat.StopAllCombatEffectsLocal();
                    break;

                case EnemyState.Idle:
                case EnemyState.Walk:
                    animator.SetBool(EnemyAnimParam.IsRolling, false);
                    if (combat != null) combat.StopAllCombatEffectsLocal();
                    break;

                default:
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
            // ✅ [NavMesh Guard] สั่งหยุดได้เฉพาะ agent ที่อยู่บน NavMesh จริง
            if (agent != null && agent.enabled && agent.isOnNavMesh)
                agent.isStopped = true;
            if (agent != null) agent.enabled = false;
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

        /// <summary>เป้าหมายปัจจุบัน (ลำตัวหุ่น) — EnemyCombat ใช้เป็นศูนย์กลางเช็คโดน</summary>
        public Transform PlayerTarget => playerTarget;

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
