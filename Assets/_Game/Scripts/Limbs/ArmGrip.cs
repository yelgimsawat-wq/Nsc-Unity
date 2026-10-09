using Nsc.Robots;
using Unity.Netcode;
using UnityEngine;

namespace Nsc.Limbs
{
    /// <summary>
    /// การจับของของมือ (กด F ค้าง) — มี state และโปรโตคอล "ขอจับ → server ยืนยัน → บังคับปล่อย" ของตัวเอง
    /// แยกออกมาจากแขนเพราะลำตัวต้องอ่านแค่ส่วนนี้ (IsSupporting = มือจับของนิ่งอยู่ กันการล้มได้ทุกกฎ)
    ///
    /// server สร้าง FixedJoint ระหว่างมือกับของที่จับ / owner รู้ผลผ่าน IsGrabConfirmed
    /// </summary>
    public class ArmGrip : NetworkBehaviour
    {
        [Header("Grab")]
        [Tooltip("จุดศูนย์กลางการจับ เทียบกับมือ")]
        [SerializeField] private Vector3 grabOffset = Vector3.zero;
        [SerializeField] private float grabRadius = 0.5f;
        [Tooltip("แรงฉีกขาดเมื่อดึงของหนักเกิน (ใช้เมื่อปิด Prevent Physics Break)")]
        [SerializeField] private float grabBreakForce = 10000f;
        [Tooltip("ข้อต่อตอนจับและโซ่แขนทั้งเส้นไม่มีวันแตกจากแรงฟิสิกส์ (ยังปล่อยด้วยปุ่ม F ได้)")]
        [SerializeField] private bool preventGrabBreakWhileRagdoll = true;
        [SerializeField] private LayerMask grabLayer;

        private Rigidbody hand;
        private RobotLimb limb;
        private FixedJoint joint;
        private Rigidbody held;

        /// <summary>ฝั่ง owner: server ยืนยันแล้วว่าจับติด (ใช้หยุดส่งคำขอซ้ำ)</summary>
        public bool IsGrabConfirmed { get; private set; }

        public bool IsGrabbing => joint != null && held != null;

        /// <summary>มือจับของที่อยู่นิ่ง (kinematic) อยู่ — ใช้ปีน และกันลำตัวล้มทุกกฎ</summary>
        public bool IsSupporting =>
            (limb == null || limb.IsAttached) && IsGrabbing && held.isKinematic;

        public Rigidbody HeldBody => held;
        public FixedJoint GrabJoint => joint;
        public bool PreventPhysicsBreak => preventGrabBreakWhileRagdoll;
        public Vector3 GrabPosition => hand != null ? hand.transform.TransformPoint(grabOffset) : transform.position;

        private void Awake()
        {
            limb = GetComponent<RobotLimb>();
            LimbController controller = GetComponent<LimbController>();
            hand = controller != null && controller.Body != null ? controller.Body : GetComponent<Rigidbody>();
        }

        private TorsoBalance Torso => limb != null && limb.Owner != null ? limb.Owner.Torso : null;

        // ================================================================
        //  RPC จากเจ้าของ
        // ================================================================

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void RequestGrabRpc()
        {
            // ตัวล้มอยู่ = เริ่มจับใหม่ไม่ได้
            if (Torso != null && Torso.IsRagdoll)
            {
                ConfirmGrabRpc(false);
                return;
            }

            // กันจับซ้อน — FixedJoint ตัวใหม่จะทับ field แต่ตัวเก่ายังเกาะมือถาวร
            if (joint != null || hand == null) return;

            foreach (Collider hit in Physics.OverlapSphere(GrabPosition, grabRadius, grabLayer))
            {
                Rigidbody target = hit.attachedRigidbody;
                if (target == null) continue;

                // จับได้ทั้ง kinematic (ปีน) และ dynamic (ยกของ)
                held = target;
                joint = hand.gameObject.AddComponent<FixedJoint>();
                joint.connectedBody = target;
                ApplyBreakLimit();

                // ปิดการชนระหว่างของที่จับกับลำตัว กันบั๊กบินขึ้นฟ้า
                IgnoreCollisionWithTorso(target, true);
                break;
            }

            // แจ้งผลเสมอ — สำเร็จ = owner หยุด retry / พลาด = retry ต่อขณะยังกด F ค้าง
            ConfirmGrabRpc(joint != null);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void RequestReleaseRpc() => ServerRelease();

        /// <summary>[SERVER] ปล่อยของ — ใช้ทั้งตอนปล่อย F และตอนระบบอื่นสั่ง (respawn)</summary>
        public void ServerRelease()
        {
            if (IsSpawned && !IsServer) return;

            if (joint != null)
            {
                Destroy(joint);
                joint = null;
            }

            if (held != null)
            {
                IgnoreCollisionWithTorso(held, false);
                held = null;
            }

            if (IsSpawned) ConfirmGrabRpc(false);
        }

        /// <summary>ตั้งเพดานแรงของข้อต่อตอนจับใหม่ทุก tick (ค่าเดียวกันตอนยืนและตอนล้ม)</summary>
        public void ApplyBreakLimit()
        {
            if (joint == null) return;
            float limit = preventGrabBreakWhileRagdoll ? Mathf.Infinity : grabBreakForce;
            joint.breakForce = limit;
            joint.breakTorque = limit;
        }

        private void FixedUpdate()
        {
            // ข้อต่อตอนจับแตกจากแรงฟิสิกส์ → Unity ทำลายมันไปแล้ว เหลือแค่เก็บกวาด
            // (เช็คที่นี่แทน OnJointBreak เพราะ OnJointBreak บอกไม่ได้ว่าข้อต่อตัวไหนบนมือที่แตก)
            if (held == null || joint != null) return;
            if (IsSpawned && !IsServer) return;

            Debug.Log($"Hand grab on '{name}' broke due to massive force");
            IgnoreCollisionWithTorso(held, false);
            held = null;
            if (IsSpawned) ConfirmGrabRpc(false);
        }

        /// <summary>
        /// ผลการจับถึง owner — ⚠️ ไม่แตะสถานะปุ่ม F ของผู้เล่น
        /// ยังกด F ค้างอยู่ก็ให้ retry ต่อได้ตามข้อกำหนด "ยังกด F = ยังจับ"
        /// </summary>
        [Rpc(SendTo.Owner)]
        private void ConfirmGrabRpc(bool grabbed) => IsGrabConfirmed = grabbed;

        /// <summary>ฝั่ง owner: ล้างสถานะยืนยันก่อนส่งคำขอปล่อย</summary>
        public void ClearLocalConfirmation() => IsGrabConfirmed = false;

        private void IgnoreCollisionWithTorso(Rigidbody target, bool ignore)
        {
            TorsoBalance torso = Torso;
            if (target == null || torso == null) return;

            Collider[] targetColliders = target.GetComponentsInChildren<Collider>();
            foreach (Collider torsoCollider in torso.GetComponentsInChildren<Collider>())
                foreach (Collider targetCollider in targetColliders)
                    Physics.IgnoreCollision(torsoCollider, targetCollider, ignore);
        }

        private void OnDrawGizmosSelected()
        {
            if (hand == null) hand = GetComponent<Rigidbody>();
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(GrabPosition, grabRadius);
        }
    }
}
