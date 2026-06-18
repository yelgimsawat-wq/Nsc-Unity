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
        [Tooltip("ระยะที่หยุดและเริ่มโจมตี (melee range)")]
        [SerializeField] private float stopDistance  = 1.5f;

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
        [SerializeField] private float attackDecisionInterval = 0.5f;

        // ─────────────────────────────────────────
        //  NetworkVariables (Server writes → All read)
        // ─────────────────────────────────────────

        /// <summary>State ปัจจุบัน — sync ไปทุก Client</summary>
        private NetworkVariable<EnemyState> netState = new NetworkVariable<EnemyState>(
            EnemyState.Idle,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>ความเร็ว (0=Idle, 0.5=Walk) — ขับ Blend Tree Layer 0</summary>
        private NetworkVariable<float> netSpeed = new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>ทิศทาง Roll ใน Local Space — ขับ Blend Tree Layer 1</summary>
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
        private EnemyRagdoll  ragdoll;     // optional — ถ้ามี EnemyRagdoll
        private Transform     playerTarget;

        private float attackDecTimer = 0f;
        private bool  isActing       = false; // true ขณะ Attack หรือรับ Knockback

        // ─────────────────────────────────────────
        //  Unity Lifecycle
        // ─────────────────────────────────────────

        private void Awake()
        {
            agent    = GetComponent<NavMeshAgent>();
            animator = GetComponent<Animator>();
            combat   = GetComponent<EnemyCombat>();
            ragdoll  = GetComponent<EnemyRagdoll>();  // null ถ้าไม่ได้ add

            agent.updateRotation = true;
            agent.angularSpeed   = 300f;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // ทุก Client subscribe เพื่อขับ Animator
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
                // ส่งค่าตรงๆ ด้วย String เลย (ชัวร์สุด)
                animator.SetFloat("Speed", netSpeed.Value);
                
                // แจ้งเตือนลง Console ถ้าระบบพยายามสั่งเดิน
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

                // รอให้ Action ปัจจุบันเสร็จก่อน (Attack / Knockback)
                if (isActing)
                {
                    attackDecTimer -= Time.deltaTime;
                    continue;
                }

                attackDecTimer -= Time.deltaTime;

                float dist = Vector3.Distance(transform.position, playerTarget.position);

                // ── Priority 1: ถึงระยะ melee → โจมตี ───────────────────
                if (dist <= stopDistance)
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

                // ── Priority 2: อยู่ในระยะกลิ้ง → กลิ้งต่อเนื่องเข้าหา ───────────
                else if (dist <= rollTriggerDistance)
                {
                    agent.isStopped = false;
                    agent.speed     = rollSpeed;
                    agent.SetDestination(playerTarget.position);

                    // คำนวณทิศทางเดินให้หน้าต่าง Animator (ส่งไปที่ 2D Blend Tree)
                    Vector3 localVel = transform.InverseTransformDirection(agent.velocity.normalized);
                    netRollDir.Value = new Vector2(localVel.x, localVel.z);

                    SetState(EnemyState.Roll);
                }

                // ── Priority 3: ในระยะตรวจจับ → เดิน ────────────────────
                else if (dist <= walkThreshold)
                {
                    agent.isStopped = false;
                    agent.speed     = walkSpeed;
                    agent.SetDestination(playerTarget.position);
                    netSpeed.Value  = 0.5f;
                    SetState(EnemyState.Walk);
                }

                // ── Priority 4: นอกระยะ → Idle ───────────────────────────
                else
                {
                    agent.isStopped = true;
                    netSpeed.Value  = 0f;
                    SetState(EnemyState.Idle);
                }
            }
        }

        // ─────────────────────────────────────────
        //  SERVER: Attack Selection
        // ─────────────────────────────────────────

        private void ChooseAndExecuteAttack()
        {
            if (isActing) return;

            float roll = Random.value;

            AttackType chosen;
            if (roll < 0.40f)
                chosen = AttackType.LightPunch;
            else if (roll < 0.70f)
                chosen = AttackType.BarragePunch;
            else
                chosen = AttackType.Kick;

            SetState(EnemyState.Attack);
            StartCoroutine(ExecuteAttackCoroutine(chosen));
        }

        private IEnumerator ExecuteAttackCoroutine(AttackType type)
        {
            isActing = true;
            float duration = combat.ServerExecuteAttack(type);
            yield return new WaitForSeconds(duration);
            isActing = false;
            SetState(EnemyState.Idle);
        }

        private void SetState(EnemyState newState)
        {
            if (netState.Value != newState)
                netState.Value = newState;
        }

        // ─────────────────────────────────────────
        //  CLIENT: NetworkVariable Callbacks → Animator
        //  ทำงานบนทุก Client รวมถึง Host
        // ─────────────────────────────────────────

        private void OnStateChanged(EnemyState previous, EnemyState current)
        {
            switch (current)
            {
                case EnemyState.Roll:
                    // เปิด Roll Layer ผ่าน bool "IsRolling"
                    animator.SetBool(EnemyAnimParam.IsRolling, true);
                    break;

                case EnemyState.Dead:
                    animator.SetBool(EnemyAnimParam.IsDead, true);
                    animator.SetBool(EnemyAnimParam.IsRolling, false);
                    break;

                default:
                    // Idle / Walk / Attack: ปิด Roll Layer
                    animator.SetBool(EnemyAnimParam.IsRolling, false);
                    break;
            }
        }

        private void OnRollDirChanged(Vector2 previous, Vector2 current)
        {
            // ขับ 2D Blend Tree ให้ Roll ถูกทิศทางบนทุก Client
            animator.SetFloat(EnemyAnimParam.RollX, current.x);
            animator.SetFloat(EnemyAnimParam.RollY, current.y);
        }

        // ─────────────────────────────────────────
        //  Public API
        // ─────────────────────────────────────────

        /// <summary>เรียกจาก EnemyHealth บน Server เพื่อฆ่าศัตรู</summary>
        public void ServerDie()
        {
            if (!IsServer) return;
            StopAllCoroutines();
            agent.isStopped = true;
            agent.enabled   = false;
            isActing        = true;
            SetState(EnemyState.Dead);

            // ถ้ามี EnemyRagdoll → บอกทุก Client เปิด Ragdoll
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
        //  เรียกจาก EnemyHealth ตอนโดนตีผลัก
        // ─────────────────────────────────────────

        /// <summary>หยุด AI Loop ชั่วคราวตอนโดนผลัก</summary>
        public void ServerBeginKnockback()
        {
            if (!IsServer) return;
            isActing = true;   // ทำให้ DecisionLoop รอ
        }

        /// <summary>คืน AI Loop หลัง Knockback เสร็จ</summary>
        public void ServerEndKnockback()
        {
            if (!IsServer) return;
            isActing = false;
            SetState(EnemyState.Idle);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // สีเขียว = Stop (melee range)
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, stopDistance);

            // สีฟ้า = Roll trigger distance
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, rollTriggerDistance);

            // สีเหลือง = Walk detection range
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, walkThreshold);
        }
#endif
    }
}
