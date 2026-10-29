using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace NscGame.Enemy
{
    /// <summary>
    /// ท่าที่ติด tag "RootMotion" ใน Animator (เช่น Mutant Jump Attack ของแพนด้า) พาตัวไปตามท่าจริง
    /// ผ่าน NavMeshAgent.Move จึงไม่ทะลุกำแพง/ไม่ตกขอบ — ท่าอื่น AI (EnemyController) พาเดินเหมือนเดิม
    /// ความสูงตอนกระโดด bake ไว้ในท่า (ภาพลอย) ส่วนนี้ขยับแค่แนวพื้น XZ
    /// ขยับบน Server เท่านั้น client รับตำแหน่งจาก NetworkTransform
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class KaijuRootMotion : NetworkBehaviour
    {
        [SerializeField] private string stateTag = "RootMotion";
        [Tooltip("พุ่งเข้าใกล้หุ่นได้สุดแค่นี้ (แนวพื้น) — ไม่พุ่งเลยตัวหุ่นไป")]
        [SerializeField] private float stopNearTarget = 6f;
        public float StopNearTarget => stopNearTarget;
        [Tooltip("ท่ากระโดดทุบพุ่งไกลขึ้นกี่เท่าของในท่า (ยังหยุดหน้าหุ่นตาม stopNearTarget)")]
        [SerializeField] private string jumpState = "Attack_Kick";
        [SerializeField] private float jumpDistanceScale = 1.5f;
        /// <summary>KaijuSpecials ปรับชั่วคราวบน Server ให้กระโดดทับลงตรงจุดที่เตือนไว้</summary>
        public float JumpDistanceScale { get => jumpDistanceScale; set => jumpDistanceScale = value; }
        /// <summary>เปิด = ไม่หยุดหน้าหุ่น (ท่ากลิ้งที่ต้องกลิ้งเลยตัวหุ่นไป) — KaijuSpecials เปิด/ปิดบน Server</summary>
        public bool PassThroughTarget { get; set; }

        private Animator animator;
        private NavMeshAgent agent;
        private int tagHash;

        private void Awake()
        {
            animator = GetComponent<Animator>();
            agent = GetComponent<NavMeshAgent>();
            animator.applyRootMotion = true;
            tagHash = Animator.StringToHash(stateTag);
        }

        // มีเมธอดนี้ = Unity ไม่ขยับตัวจากท่าเอง เราเลือกเองว่าท่าไหนพาตัวไป
        private void OnAnimatorMove()
        {
            if (IsSpawned && !IsServer) return;
            if (!InTaggedState()) return;

            Vector3 delta = animator.deltaPosition;
            delta.y = 0f;
            if (InState(jumpState)) delta *= jumpDistanceScale;
            if (!PassThroughTarget && TooCloseAfter(delta)) return;
            if (agent != null && agent.enabled && agent.isOnNavMesh) agent.Move(delta);
            else transform.position += delta;
        }

        // ก้าวนี้พาเข้าใกล้หุ่นตัวที่ใกล้สุดจนเกิน stopNearTarget → ไม่ขยับ
        private bool TooCloseAfter(Vector3 delta)
        {
            Vector3 here = transform.position, next = here + delta;
            foreach (Nsc.Robots.Robot robot in Nsc.Robots.Robot.All)
            {
                if (robot == null || !robot.isActiveAndEnabled) continue;
                Vector3 r = robot.transform.position;
                float now = Vector2.Distance(new Vector2(here.x, here.z), new Vector2(r.x, r.z));
                float after = Vector2.Distance(new Vector2(next.x, next.z), new Vector2(r.x, r.z));
                if (after < now && after < stopNearTarget) return true;
            }
            return false;
        }

        private bool InState(string stateName)
        {
            for (int i = 0; i < animator.layerCount; i++)
            {
                if (animator.GetCurrentAnimatorStateInfo(i).IsName(stateName)) return true;
                if (animator.IsInTransition(i) && animator.GetNextAnimatorStateInfo(i).IsName(stateName)) return true;
            }
            return false;
        }

        private bool InTaggedState()
        {
            for (int i = 0; i < animator.layerCount; i++)
            {
                if (animator.GetCurrentAnimatorStateInfo(i).tagHash == tagHash) return true;
                if (animator.IsInTransition(i) && animator.GetNextAnimatorStateInfo(i).tagHash == tagHash) return true;
            }
            return false;
        }
    }
}
