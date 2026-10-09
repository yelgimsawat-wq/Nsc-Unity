using System;
using System.Collections.Generic;
using Nsc.Combat;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace Nsc.Robots
{
    /// <summary>
    /// หุ่นทั้งตัว (aggregate root) — วางบนลำตัว คู่กับ TorsoBalance
    /// ทางเดียวในการเข้าถึงแขนขา: Robot.GetLimb(slot) / AttachedLimbs()
    /// ห้ามหาแขนขาด้วยชื่อ GameObject หรือค้นฉากตอนรันอีกต่อไป
    ///
    /// ทีมเป็น NetworkVariable — ซีน PVP ตั้งค่าเริ่มต้นไว้ (Red/Blue) หุ่น co-op เป็น None
    /// </summary>
    public class Robot : NetworkBehaviour
    {
        [Tooltip("ทีมของหุ่นตัวนี้ (PVP) — co-op ปล่อย None")]
        [SerializeField]
        private NetworkVariable<Team> team = new NetworkVariable<Team>(
            Team.None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        [Tooltip("แขนขาทั้ง 4 ชิ้น เรียงตาม LimbSlot: แขนซ้าย, แขนขวา, ขาซ้าย, ขาขวา\n" +
                 "ปล่อยว่างได้ — ระบบเติมจาก RobotLimb ที่อยู่ในหุ่นตาม slot ของมันให้เอง")]
        [SerializeField] private RobotLimb[] limbs = new RobotLimb[LimbSlots.Count];

        private static readonly List<Robot> all = new List<Robot>();
        private static readonly Dictionary<Transform, Robot> byRoot = new Dictionary<Transform, Robot>();

        /// <summary>หุ่นทุกตัวที่อยู่ในฉาก (active) — แทน FindObjectsByType</summary>
        public static IReadOnlyList<Robot> All => all;

        /// <summary>ยิงทุกครั้งที่มีหุ่นเข้า/ออกจากฉาก</summary>
        public static event Action RegistryChanged;

        /// <summary>ยิงบนทุกเครื่องเมื่อแขนขาชิ้นใดหลุดหรือต่อกลับ</summary>
        public event Action<RobotLimb> LimbStateChanged;

        public TorsoBalance Torso { get; private set; }

        /// <summary>GameObject ที่ครอบทั้งหุ่น (ลำตัว แขน ขา อยู่ใต้ตัวนี้)</summary>
        public Transform Root { get; private set; }

        public Team GetTeam() => team.Value;

        public RobotLimb GetLimb(LimbSlot slot)
        {
            int index = (int)slot;
            return limbs != null && index < limbs.Length ? limbs[index] : null;
        }

        public IEnumerable<RobotLimb> AttachedLimbs()
        {
            foreach (RobotLimb limb in limbs)
                if (limb != null && limb.IsAttached) yield return limb;
        }

        public int AttachedLimbCount
        {
            get
            {
                int count = 0;
                foreach (RobotLimb limb in limbs)
                    if (limb != null && limb.IsAttached) count++;
                return count;
            }
        }

        /// <summary>หาหุ่นเจ้าของชิ้นส่วนนี้ — ไต่ขึ้น parent จนเจอ root ที่ลงทะเบียนไว้</summary>
        public static Robot FromTransform(Transform part)
        {
            for (Transform t = part; t != null; t = t.parent)
                if (byRoot.TryGetValue(t, out Robot robot) && robot != null)
                    return robot;
            return null;
        }

        public static Robot FromCollider(Collider collider) =>
            collider != null ? FromTransform(collider.transform) : null;

        /// <summary>หาหุ่นจาก NetworkObjectId — ใช้เป็นตัวระบุหุ่นที่ตรงกันทุกเครื่อง (เช่นใน LimbSelection)</summary>
        public static Robot FindById(ulong networkObjectId)
        {
            foreach (Robot robot in all)
                if (robot != null && robot.IsSpawned && robot.NetworkObjectId == networkObjectId)
                    return robot;
            return null;
        }

        // ================================================================
        //  Lifecycle
        // ================================================================

        private void Awake()
        {
            Torso = GetComponent<TorsoBalance>();
            Root = ResolveRoot(transform);
            FillMissingLimbs();

            foreach (RobotLimb limb in limbs)
                if (limb != null) limb.BindOwner(this);

            CaptureRestPose();
        }

        private void OnEnable()
        {
            if (!all.Contains(this)) all.Add(this);
            if (Root != null) byRoot[Root] = this;
            RegistryChanged?.Invoke();
        }

        private void OnDisable()
        {
            all.Remove(this);
            if (Root != null && byRoot.TryGetValue(Root, out Robot registered) && registered == this)
                byRoot.Remove(Root);
            RegistryChanged?.Invoke();
        }

        private void FillMissingLimbs()
        {
            if (limbs == null || limbs.Length != LimbSlots.Count)
                Array.Resize(ref limbs, LimbSlots.Count);

            foreach (RobotLimb limb in Root.GetComponentsInChildren<RobotLimb>(true))
            {
                int index = (int)limb.Slot;
                if (limbs[index] == null) limbs[index] = limb;
            }
        }

        /// <summary>
        /// root ของหุ่นตัวนี้ = ไต่ขึ้นไปให้สูงสุดเท่าที่ยังครอบหุ่นตัวเดียว
        /// ใช้ transform.root ตรงๆ ไม่ได้ เพราะหุ่นสองตัวอาจอยู่ใต้ parent เดียวกัน (ฉาก PVP)
        /// </summary>
        private static Transform ResolveRoot(Transform from)
        {
            Transform best = from;
            for (Transform t = from.parent; t != null; t = t.parent)
            {
                if (t.GetComponentsInChildren<Robot>(true).Length > 1) break;
                best = t;
            }
            return best;
        }

        // ================================================================
        //  กติกาของหุ่น: แขนขาเปลี่ยนสถานะ → ลำตัวรู้
        // ================================================================

        /// <summary>RobotLimb เรียกเมื่อชิ้นของมันหลุดหรือต่อกลับ (ทุกเครื่อง)</summary>
        internal void OnLimbStateChanged(RobotLimb limb)
        {
            // ✅ ขาหลุด = ยืนต่อไม่ได้ — ล้มทันที ไม่งั้นแรงพยุงยังลอยตัวค้าง
            // แล้วขาที่หลุดเอื้อมกลับมาต่อไม่ถึง socket ที่ลอยอยู่
            if (IsServer && !limb.IsAttached && limb.Slot.IsLeg() && Torso != null)
                Torso.ServerKnockDown(FallReason.LegDetached);

            LimbStateChanged?.Invoke(limb);
        }

        /// <summary>[SERVER] ให้ลำตัวล้มด้วยเหตุผลที่ระบุ — ทางเดียวที่คนนอกหุ่นทำให้หุ่นล้มได้</summary>
        public void ServerKnockDown(FallReason reason)
        {
            if (Torso != null) Torso.ServerKnockDown(reason);
        }

        // ================================================================
        //  ฟิสิกส์ทั้งตัว (แช่แข็งตอนลอบบี้ / respawn)
        // ================================================================

        /// <summary>
        /// เปิด/ปิดการจำลองฟิสิกส์ของหุ่นทั้งตัว — ลอบบี้แช่แข็งไว้จนกว่า Host กด Start
        ///
        /// ✅ [Client Leg Collapse Fix] ฟิสิกส์หุ่นเป็นของ server ทั้งหมด:
        ///   Server = dynamic ทุกชิ้น / Client = ชิ้นที่มี NetworkTransform เป็น kinematic ให้ sync พาไป
        ///   ท่อนกลางที่ไม่ได้ sync ปล่อย dynamic ให้ joint ลากตามชิ้นที่ sync
        /// ถ้าปลด kinematic ทุกชิ้นทุกเครื่อง client จะมีฟิสิกส์ท้องถิ่นแข่งกับตำแหน่งจาก server → ขาย้วย
        /// </summary>
        public void SetSimulationEnabled(bool enabled)
        {
            if (Root == null) return;

            bool isServer = NetworkManager.Singleton == null || NetworkManager.Singleton.IsServer;
            foreach (Rigidbody body in Root.GetComponentsInChildren<Rigidbody>())
            {
                if (!enabled)
                {
                    body.isKinematic = true;
                    body.Sleep();
                    continue;
                }

                bool drivenByNetwork = !isServer && body.GetComponent<NetworkTransform>() != null;
                body.isKinematic = drivenByNetwork;
                if (!drivenByNetwork) body.WakeUp();
            }
        }

        /// <summary>
        /// [SERVER] พาหุ่นทั้งตัวไปจุดเกิดใหม่ในท่าเริ่มต้น (ท่าตอนโหลดฉาก) แล้วรีเซ็ต state ของลำตัวและแขนขา
        ///
        /// ลำดับสำคัญ (ย้ายมาจาก RespawnManager):
        ///   0) ปล่อยมือที่จับของอยู่ — FixedJoint ที่จับกำแพงไม่อยู่ในหุ่น วาร์ปไปพร้อมกันจะระเบิด
        ///   1) ปลด connectedBody ของข้อต่อทุกตัวชั่วคราว กัน solver ดึงระยะไกลด้วยแรงมหาศาลในเฟรมเดียว
        ///   2) วาร์ปทุกชิ้นด้วย Teleport() (ไม่ให้ NetworkTransform interpolate)
        ///   3) ต่อข้อต่อกลับ — anchor ถูกคำนวณจากตำแหน่งใหม่
        ///   4) รีเซ็ต state: เท้าต้องล้างจุดปักเก่า ไม่งั้นเฟรมถัดไปลากเท้ากลับก้นเหว
        ///
        /// อ่านข้อต่อจากหุ่นทุกครั้ง ไม่แคชไว้ตอนเริ่มฉาก — ข้อต่อของแขนขาถูกสร้างใหม่ทุกครั้งที่ดึงกลับมาต่อ
        /// ชิ้นที่หลุดอยู่ยังหลุดเหมือนเดิม เลือดเท่าเดิม
        /// </summary>
        public void ServerResetForRespawn(Pose spawn)
        {
            if (!IsServer || Torso == null || Torso.Body == null) return;

            foreach (RobotLimb limb in limbs)
                if (limb != null && limb.Controller is Limbs.ArmController arm && arm.Grip != null)
                    arm.Grip.ServerRelease();

            Joint[] joints = Root.GetComponentsInChildren<Joint>(true);
            Rigidbody[] connected = new Rigidbody[joints.Length];
            for (int i = 0; i < joints.Length; i++)
            {
                connected[i] = joints[i].connectedBody;
                joints[i].connectedBody = null;
            }

            foreach (RestPose rest in restPoses)
            {
                if (rest.body == null) continue;
                Vector3 position = spawn.position + spawn.rotation * rest.localPosition;
                Quaternion rotation = spawn.rotation * rest.localRotation;

                if (!rest.body.isKinematic)
                {
                    rest.body.linearVelocity = Vector3.zero;
                    rest.body.angularVelocity = Vector3.zero;
                }

                NetworkTransform networkTransform = rest.body.GetComponent<NetworkTransform>();
                if (networkTransform != null && networkTransform.IsSpawned)
                    networkTransform.Teleport(position, rotation, rest.body.transform.localScale);
                else
                    rest.body.transform.SetPositionAndRotation(position, rotation);
            }

            for (int i = 0; i < joints.Length; i++)
                if (joints[i] != null) joints[i].connectedBody = connected[i];

            Torso.ServerResetForRespawn();
            foreach (RobotLimb limb in limbs)
                if (limb != null && limb.Controller != null) limb.Controller.ServerResetForRespawn();
        }

        private struct RestPose
        {
            public Rigidbody body;
            public Vector3 localPosition;
            public Quaternion localRotation;
        }

        private RestPose[] restPoses = Array.Empty<RestPose>();

        /// <summary>เก็บท่าเริ่มต้นของทุกชิ้น เทียบกับลำตัว — ใช้สร้างท่ายืนใหม่ตอน respawn</summary>
        private void CaptureRestPose()
        {
            if (Torso == null || Torso.Body == null) return;

            Transform torso = Torso.Body.transform;
            Rigidbody[] bodies = Root.GetComponentsInChildren<Rigidbody>(true);
            restPoses = new RestPose[bodies.Length];
            for (int i = 0; i < bodies.Length; i++)
            {
                restPoses[i] = new RestPose
                {
                    body = bodies[i],
                    localPosition = torso.InverseTransformPoint(bodies[i].position),
                    localRotation = Quaternion.Inverse(torso.rotation) * bodies[i].rotation
                };
            }
        }
    }
}
