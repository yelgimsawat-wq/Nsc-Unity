// =============================================================================
//  !! ขึ้นอยู่กับ EnemyHealth.cs และ EnemyRagdoll.cs !!
//  เพิ่ม RequireComponent ด้านล่างถ้า EnemyHealth ถูก add ด้วย
//  EnemyController.cs
//  States: Idle → Walk → Roll (dash เข้าหา) → Attack → Dead
//  ตัดออก: Run, Stagger (รับหมัด)
//
//  BLEND TREE SETUP (Unity Animator)
//  ─────────────────────────────────────────────────────────────────────────
//  Layer 0 – Base Layer
//    └─ BlendTree (1D, Parameter: "Speed")
//         ├─ Armature_idle  @ threshold 0.0
//         └─ Armature_walk  @ threshold 0.5
//
//  Layer 1 – Roll Layer (Override, controlled by bool "IsRolling")
//    └─ BlendTree (2D Freeform Directional, Parameters: "RollX", "RollY")
//         ├─ Armature_roll (forward)  @ (0,  1)
//         ├─ Armature_roll (backward) @ (0, -1)
//         ├─ Armature_roll (left)     @ (-1, 0)
//         └─ Armature_roll (right)    @ ( 1, 0)
//
//  Layer 2 – Attack Layer (Override)
//    ├─ Trigger "LightPunch"   → Armature_Punch
//    ├─ Trigger "BarragePunch" → Armature_PunchCombo
//    └─ Trigger "Kick"         → Armature_kick
// =============================================================================

using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using NscGame.Enemy;

namespace NscGame.Enemy
{
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(EnemyCombat))]
    public class EnemyController : NetworkBehaviour
    {
        // ─────────────────────────────────────────
        //  Inspector
        // ─────────────────────────────────────────

        [Header("Movement Thresholds")]
        [Tooltip("ระยะที่ enemy จะหยุดกลิ้ง แล้วเปลี่ยนเป็นเดินต่อ (ไม่ใช่หยุดเดินทั้งหมด)")]
        [SerializeField] private float stopDistance  = 2.5f;

        [Tooltip("ระยะที่เริ่มต่อยจริง — หยุดเคลื่อนที่สนิทตรงนี้ แยกอิสระจาก Stop Distance")]
        [SerializeField] private float attackRange   = 1.5f;

        [Tooltip("ระยะที่เริ่มเดินเข้าหาผู้เล่น")]
        [SerializeField] private float walkThreshold = 12.0f;

        [Header("Movement Speed")]
        [SerializeField] private float walkSpeed = 2.5f;

        [Header("Roll / Continuous")]
        [Tooltip("ถ้าระยะห่างน้อยกว่านี้ ศัตรูจะเปลี่ยนจากเดินเป็นกลิ้งต่อเนื่อง")]
        [SerializeField] private float rollTriggerDistance = 8.0f;

        [Tooltip("ความเร็วของการกลิ้ง")]
        [SerializeField] private float rollSpeed     = 6.0f;

        [Header("Animator Smoothing")]
        [SerializeField] private float speedDampTime = 0.15f;

        [Header("Attack Settings")]
        [Tooltip("เวลาตัดสินใจใหม่ระหว่างยืนรอ (ไม่ใช่ cooldown หลังตี)")]
        [SerializeField] private float attackDecisionInterval = 0.5f;

        [Tooltip("ช่วงพักหลังจบคอมโบ ก่อนจะเริ่มตัดสินใจโจมตีรอบใหม่ (วินาที)")]
        [SerializeField] private float postAttackCooldown = 1.0f;

        [Tooltip("ช่วงพักสั้นๆ ระหว่างแต่ละครั้งของท่าเดียวกันตอนตีซ้ำในคอมโบ")]
        [SerializeField] private float comboRepeatGap = 0.15f;

        [Header("Attack Weights (สัดส่วนการสุ่มเลือกท่า — ไม่ต้องรวมเป็น 100)")]
        [SerializeField] private float lightPunchWeight   = 40f;
        [SerializeField] private float barragePunchWeight = 30f;
        [SerializeField] private float kickWeight         = 30f;

        [Header("Attack Repeat Range (ตีซ้ำกี่ครั้งติดกันต่อ 1 คอมโบ — สุ่มในช่วงนี้)")]
        [Tooltip("X = ขั้นต่ำ, Y = ขั้นสูงสุด ของจำนวนครั้งที่ตีซ้ำติดกัน")]
        [SerializeField] private Vector2Int lightPunchRepeatRange   = new Vector2Int(1, 1);
        [SerializeField] private Vector2Int barragePunchRepeatRange = new Vector2Int(1, 3);
        [SerializeField] private Vector2Int kickRepeatRange         = new Vector2Int(1, 1);

        // ─────────────────────────────────────────
        //  NetworkVariables (Server writes → All read)
        // ─────────────────────────────────────────

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

        // ─────────────────────────────────────────
        //  Private
        // ─────────────────────────────────────────

        private NavMeshAgent  agent;
        private Animator      animator;
        private EnemyCombat   combat;
        private EnemyRagdoll  ragdoll;
        private Transform     playerTarget;

        private float attackDecTimer = 0f;
        private bool  isActing       = false;

        // ─────────────────────────────────────────
        //  Unity Lifecycle
        // ─────────────────────────────────────────

        private void Awake()
        {
            agent    = GetComponent<NavMeshAgent>();
            animator = GetComponent<Animator>();
            combat   = GetComponent<EnemyCombat>();
            ragdoll  = GetComponent<EnemyRagdoll>();

            agent.updateRotation = true;
            agent.angularSpeed   = 300f;

            agent.stoppingDistance = attackRange;
            agent.autoBraking      = true;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            netState.OnValueChanged  += OnStateChanged;
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
            netState.OnValueChanged   -= OnStateChanged;
            netRollDir.OnValueChanged -= OnRollDirChanged;
            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (animator != null)
            {
                animator.SetFloat("Speed", netSpeed.Value);

                if (netSpeed.Value > 0f)
                {
                    Debug.Log($"[EnemyController] ส่งค่าเดิน {netSpeed.Value} ไปที่ Animator");
                }
            }
        }

        // ─────────────────────────────────────────
        //  SERVER: Decision Loop
        // ─────────────────────────────────────────

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

                // ── Priority 1: ใกล้พอจะโจมตี → หยุดจริง + ตี ──────────────
                if (dist <= attackRange)
                {
                    agent.isStopped = true;
                    netSpeed.Value  = 0f;

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

                // ── Priority 2: เลยจุดหยุดกลิ้งแล้ว แต่ยังไม่ถึงระยะตี → เดินต่อ ──
                else if (dist <= stopDistance)
                {
                    agent.isStopped = false;
                    agent.speed     = walkSpeed;
                    agent.SetDestination(playerTarget.position);
                    netSpeed.Value  = 0.5f;
                    SetState(EnemyState.Walk);
                }

                // ── Priority 3: อยู่ในระยะกลิ้ง → กลิ้งต่อเนื่องเข้าหา ───────
                else if (dist <= rollTriggerDistance)
                {
                    agent.isStopped = false;
                    agent.speed     = rollSpeed;
                    agent.SetDestination(playerTarget.position);

                    Vector3 localVel = transform.InverseTransformDirection(agent.velocity.normalized);
                    netRollDir.Value = new Vector2(localVel.x, localVel.z);

                    SetState(EnemyState.Roll);
                }

                // ── Priority 4: ในระยะตรวจจับ → เดิน ────────────────────
                else if (dist <= walkThreshold)
                {
                    agent.isStopped = false;
                    agent.speed     = walkSpeed;
                    agent.SetDestination(playerTarget.position);
                    netSpeed.Value  = 0.5f;
                    SetState(EnemyState.Walk);
                }

                // ── Priority 5: นอกระยะ → Idle ───────────────────────────
                else
                {
                    agent.isStopped = true;
                    netSpeed.Value  = 0f;
                    SetState(EnemyState.Idle);
                }
            }
        }

        // ─────────────────────────────────────────
        //  SERVER: Attack Selection (สุ่ม + คอมโบ)
        // ─────────────────────────────────────────

        private void ChooseAndExecuteAttack()
        {
            if (isActing) return;

            AttackType chosen = PickWeightedAttack();
            int repeatCount   = GetRepeatCountFor(chosen);

            SetState(EnemyState.Attack);
            StartCoroutine(ExecuteAttackComboCoroutine(chosen, repeatCount));
        }

        /// <summary>สุ่มเลือกท่าตามสัดส่วน Weight ที่ตั้งไว้</summary>
        private AttackType PickWeightedAttack()
        {
            float totalWeight = lightPunchWeight + barragePunchWeight + kickWeight;
            if (totalWeight <= 0f) totalWeight = 1f; // กันหารด้วย 0

            float roll = Random.value * totalWeight;

            if (roll < lightPunchWeight)
                return AttackType.LightPunch;
            else if (roll < lightPunchWeight + barragePunchWeight)
                return AttackType.BarragePunch;
            else
                return AttackType.Kick;
        }

        /// <summary>สุ่มจำนวนครั้งที่จะตีซ้ำติดกัน ตามช่วงที่ตั้งไว้ของท่านั้นๆ</summary>
        private int GetRepeatCountFor(AttackType type)
        {
            Vector2Int range = type switch
            {
                AttackType.LightPunch   => lightPunchRepeatRange,
                AttackType.BarragePunch => barragePunchRepeatRange,
                AttackType.Kick         => kickRepeatRange,
                _                       => new Vector2Int(1, 1)
            };

            int min = Mathf.Max(1, range.x);
            int max = Mathf.Max(min, range.y);

            return Random.Range(min, max + 1); // Random.Range(int,int) max เป็น exclusive จึง +1
        }

        /// <summary>ตีท่าเดียวกันซ้ำตาม repeatCount โดยเช็คระยะใหม่ก่อนตีแต่ละครั้ง</summary>
        private IEnumerator ExecuteAttackComboCoroutine(AttackType type, int repeatCount)
        {
            isActing = true;

            for (int i = 0; i < repeatCount; i++)
            {
                // ถ้าผู้เล่นหนีออกนอกระยะระหว่างคอมโบ ให้หยุดคอมโบทันที
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

            // ── ช่วงพักหลังจบคอมโบ ก่อนจะกลับไปตัดสินใจใหม่ ──
            if (postAttackCooldown > 0f)
                yield return new WaitForSeconds(postAttackCooldown);

            isActing = false;
        }

        private void SetState(EnemyState newState)
        {
            if (netState.Value != newState)
                netState.Value = newState;
        }

        // ─────────────────────────────────────────
        //  CLIENT: NetworkVariable Callbacks → Animator
        // ─────────────────────────────────────────

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

        // ─────────────────────────────────────────
        //  Public API
        // ─────────────────────────────────────────

        public void ServerDie()
        {
            if (!IsServer) return;
            StopAllCoroutines();
            agent.isStopped = true;
            agent.enabled   = false;
            isActing        = true;
            SetState(EnemyState.Dead);

            TriggerRagdollClientRpc(Vector3.zero);
        }

        [ClientRpc]
        private void TriggerRagdollClientRpc(Vector3 impactForce)
        {
            if (ragdoll != null)
                ragdoll.EnableRagdoll(impactForce);
        }

        // ─────────────────────────────────────────
        //  PUBLIC: Knockback Pause API
        // ─────────────────────────────────────────

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
    }
}