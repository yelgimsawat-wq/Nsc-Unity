using System.Collections.Generic;
using Nsc.Combat;
using Nsc.Feel;
using Nsc.Match;
using Nsc.Robots;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace Nsc.BoneLimb
{
    /// <summary>
    /// ตัวกลางของหุ่น IK — หุ่นตัวเดียว คนละชิ้น
    /// แต่ละชิ้น = <see cref="IKLimb"/> (IK บานพับ), ข้อจำกัดข้อต่อ = <see cref="ShoulderLimit"/> / <see cref="HipLimit"/>,
    /// กล้อง = <see cref="OrbitCamera"/>, อินพุต = <see cref="IKInput"/>, ข้อมูล network = <see cref="LimbNetState"/>
    /// ไฟล์นี้ทำแค่: ใครคุมชิ้นไหน, แปลงเคอร์เซอร์เป็นเป้า, เดินตามเท้า, ซิงก์ และ UI
    ///
    /// แขน: มือตามเคอร์เซอร์ (ระนาบหน้าไหล่, Shift+ล้อ ดัน/ดึง), คลิกซ้ายค้าง = กำ
    /// ขา (แบบหุ่นเดิม): ชี้พื้น + คลิก = ก้าวเป็นเส้นโค้งไปตรงนั้น, ค้าง = ลอยตามเคอร์เซอร์ ปล่อย = วาง (ก้าวเฉียง = เลี้ยว, Shift = เตะสูงไปทาง ◈) | ล้อ = ซูม | คลิกขวาค้าง = หันกล้อง
    ///
    /// ใครคุมชิ้นไหน = หน้าเลือกชิ้นส่วนเดิม (LimbSelection บน SelectPart) — หุ่นนี้มี Robot + RobotLimb 4 ช่องให้มันรู้จัก
    /// เข้าห้อง: AutoStartHost ในฉาก (เปิดฉากตรงๆ) หรือ Join code จากเมนู | ไม่มี NetworkManager = เล่นคนเดียว กด 1-4 สลับชิ้น
    /// เครื่องที่คุมชิ้นคำนวณเป้าเอง แล้วส่งให้ Server ตรวจ (เจ้าของ + ข้อจำกัดข้อต่อ) และกระจายผ่าน NetworkList
    /// ตัวหุ่น (เดินตามเท้า) คำนวณบน Server แล้วซิงก์ด้วย NetworkTransform
    /// </summary>
    public class RobotIKControl : NetworkBehaviour
    {
        [Tooltip("ลำดับ = LimbSlot: แขนซ้าย, แขนขวา, ขาซ้าย, ขาขวา")]
        [SerializeField] private IKLimb[] limbs = new IKLimb[4];
        [SerializeField] private GameObject model;
        [SerializeField] private Camera cam;
        [SerializeField] private OrbitCamera orbit = new OrbitCamera();

        [Header("การคุม")]
        [Tooltip("Shift + ล้อเมาส์: ดัน/ดึงมือ (สัดส่วนความยาวแขนต่อหน่วยล้อ)")]
        [SerializeField] private float depthSpeed = 0.001f;
        [Tooltip("เวลาเหวี่ยงเท้าหนึ่งก้าว (วินาที) — ยาวขึ้น = ดูหนักขึ้น (หุ่นเดิม stepSwingTime 0.6)")]
        [SerializeField] private float stepSwingTime = 0.6f;
        [Tooltip("ความสูงยอดโค้งของก้าว เป็นสัดส่วนของความยาวขา (หุ่นเดิม stepArcHeightRatio 0.2)")]
        [Range(0f, 0.5f)] [SerializeField] private float stepArcHeight = 0.2f;
        [Tooltip("มือ/เท้าไหลตามเคอร์เซอร์เร็วแค่ไหน (มาก = ติดเมาส์, น้อย = นุ่ม)")]
        [SerializeField] private float follow = 12f;
        [SerializeField] private float gripSpeed = 8f;

        [Header("ตัวหุ่น")]
        [Tooltip("ย่อตัวลงจากท่ายืนตรง — ขายาวแบบนี้ 0.15 ≈ เข่างอ 27°, 0.6 ≈ 54°")]
        [SerializeField] private float crouch = 0.15f;
        [SerializeField] private float bodyFollow = 3f;
        [SerializeField] private float remoteSmoothing = 15f;

        [Header("ข้อต่อแบบคน (องศา)")]
        [SerializeField] private ShoulderLimit shoulder = new ShoulderLimit();
        [SerializeField] private HipLimit hip = new HipLimit();

        [Header("ต่อสู้: การ์ด + หมัดชาร์จ")]
        [SerializeField] private IKCombat combat = new IKCombat();

        [Header("ความหนัก: ก้าว, น้ำหนักตัว, เอฟเฟกต์")]
        [SerializeField] private IKFeel feel = new IKFeel();

        [Header("ดาเมจหมัด (แบบ LimbStrike เดิม: ดาเมจ = ความเร็วพีคของกำปั้น × speedToDamage)")]
        [Tooltip("กำปั้นเร็วกว่านี้ (m/s) = กำลังต่อย — ช้ากว่านี้ชนอะไรก็ไม่มีดาเมจ")]
        [SerializeField] private float strikeMinSpeed = 8f;
        [Tooltip("ความเร็วพีค (m/s) × ค่านี้ = ดาเมจ — หุ่น IK กำปั้นช้ากว่า (หมัดแตะ ~18 m/s) ตั้ง 1.6 ให้ได้ ~30 ใกล้หมัดหุ่นเดิม (พีค 55 × 0.6 = 33)")]
        [SerializeField] private float strikeSpeedToDamage = 1.6f;
        [SerializeField] private float strikeMaxDamage = 60f;
        [Tooltip("รัศมีกำปั้นที่นับว่าโดน")]
        [SerializeField] private float strikeRadius = 1.2f;

        [Header("ถีบแบบบาทาลูบพักตร์ (กด Shift = ยกเข่าแล้วถีบฝ่าเท้าไปทาง ◈ — ดาเมจคิดแบบหมัด)")]
        [Tooltip("เวลายกเข่าก่อนถีบ (วินาที) — สั้น = กดแล้วออกเท้าไว")]
        [SerializeField] private float kickChamberTime = 0.25f;
        [Tooltip("ความเร็วเป้าเท้าตอนถีบ (m/s) × แรงที่เหลือตามเลือดขา — ปลายเท้าจริงเร็วกว่านี้ตอนเข่าเหยียด")]
        [SerializeField] private float kickThrustSpeed = 35f;
        [Tooltip("ความเร็วพีคของเท้า × ค่านี้ = ดาเมจถีบ (เพดาน strikeMaxDamage)")]
        [SerializeField] private float kickSpeedToDamage = 1.2f;
        [Tooltip("ยกเข่า: เท้าอยู่ต่ำกว่าสะโพกเท่านี้ (สัดส่วนความยาวขา) — น้อย = ยกเข่าสูง")]
        [SerializeField] private float kickChamberDrop = 0.3f;
        [Tooltip("ขาเหยียดชี้ขึ้นเหนือแนวราบกี่องศา — 25 = ถีบตรงไปข้างหน้าระดับหน้าคู่ต่อสู้ (บาทาลูบพักตร์), 55 = เตะสูงแบบคาราเต้")]
        [SerializeField] private float kickHighAngle = 25f;
        [Tooltip("ลำตัวเอนหนีเท้าที่ยกสูงสุดกี่องศา (เตะสูงแล้วตัวเอนถ่วงไว้)")]
        [SerializeField] private float kickLean = 30f;
        [Tooltip("ถีบได้แค่ข้างหน้า: ทิศ ◈ เบนจากหน้าหุ่นได้ไม่เกินกี่องศา (เล็งตามเคอร์เซอร์ได้ระหว่างยกเข่า แบบแขนที่เล็งได้ในกรอบข้อต่อ)")]
        [SerializeField] private float kickAimLimit = 25f;
        [Tooltip("ปลายเท้าเชิดขึ้นตอนถีบ (องศา) — ฝ่าเท้าหันเข้าเป้า")]
        [SerializeField] private float kickSolePitch = 70f;
        [Tooltip("ถีบสุดแล้วเท้าตกลงพื้นด้วยความเร่งนี้ (m/s²) — มาก = กระแทกลงไว")]
        [SerializeField] private float kickFallGravity = 40f;
        [Tooltip("แรงเตะที่เหลือตอนขาแทบไม่มีเลือด (หุ่นเดิม 0.3 → 100% ตามเลือด)")]
        [Range(0f, 1f)] [SerializeField] private float kickPowerAtZeroHealth = 0.3f;

        [Header("ชิ้นส่วนหลุด (ดึงกลับ: R ค้าง แบบหุ่นเดิม)")]
        [SerializeField] private IKDetach detach = new IKDetach();

        [Header("ล้ม: ragdoll บนกระดูก + กฎล้มเดียวกับหุ่นเดิม")]
        [SerializeField] private IKRagdoll ragdoll = new IKRagdoll();
        // สองเท้ายกพร้อมกันได้ 0.3 วิก่อนล้ม (หุ่นเดิม 0.15) — ผู้ใช้ขอให้ล้มยากขึ้นนิดหนึ่ง
        // ต้องขยายทั้งสองกฎ: หุ่น IK สองเท้าลอย = ไม่มีเท้าสมดุลด้วย กฎ BothFeetOffBalance จะตัดที่ 0.15 ก่อน
        [SerializeField] private FallRules fallRules = new FallRules { bothSteppingGrace = 0.3f, bothOffBalanceGrace = 0.3f };

        // ขา (เฉพาะเครื่องที่คุมขา): ก้าวเป็นเส้นโค้งแบบหุ่นเดิม — จุดเริ่ม, จุดวางเท้าที่เล็ง, ความคืบหน้าแนวราบ/ความสูง
        private Vector3[] liftFrom, stepAim, footMark;   // footMark = จุดที่เท้าจะลงถ้าคลิกตอนนี้ (โชว์เป็น ◈)
        private float[] swingT, swingLift;
        private bool[] airborne;              // เท้ายกอยู่ (ทุกเครื่องดูจากเป้า) — ใช้จับจังหวะเท้าลง
        private Vector3 lastBodyPos, leanVel;
        private Vector3[] planted;            // ที่เท้าปักล่าสุด (ทุกเครื่อง) — ทิศก้าว = เท้าที่ลอยอยู่เทียบจุดนี้
        private Vector2 lean;
        private Vector3 bodyVel;
        private float yawVel;
        private static readonly Collider[] hitBuffer = new Collider[8];
        // ดาเมจหมัด (Server): ปลายแขนเฟรมก่อน, ความเร็วพีคของท่านี้, ท่านี้โดนไปแล้ว (หนึ่งท่าหนึ่งดาเมจ)
        private Vector3[] lastTip;
        private float[] strikePeak;
        private bool[] struck;
        private bool[] tracked;               // เฟรมก่อนนับหมัดได้ไหม — เฟรมแรกหลังลุก/ต่อชิ้น/เริ่มเกม ปลายแขนกระโดด ไม่ใช่การต่อย
        // เตะ (เครื่องที่คุมขา): เวลายกเข่า (-1 = ไม่ได้ยก), ทิศเตะ, เท้าเทียบสะโพกตอนเริ่มท่า, ปลายท่า, ความเร็วเท้า
        private float[] kickCharge;
        private Vector3[] kickDir, kickFrom, kickEnd;
        private float[] kickSpeed;            // ถีบ/ชักกลับ = ความเร็วเท้า, ตก = ความเร็วตก
        private bool[] kickFall;              // ถีบสุดแล้วเท้ากำลังตกลงตรงนั้น
        private Transform[] slots;            // ช่อง RobotLimb (มี LimbHealth) — ตามปลายแขนขา ให้ศัตรูเล็งชิ้นที่ใกล้ได้ถูก

        private IKCombat.Arm[] arms;          // ช่องเดียวกับ limbs, ขา = null
        private Transform spine;
        private Quaternion spineRest;
        private Vector3 bodyPose;
        private Vector3 modelRest;
        // เป้าเล็งของแขนที่เครื่องนี้คุม — ใช้วาดเป้าเล็ง
        private int aimArm = -1;
        private Vector3 aimPoint;
        private bool aimOnTarget, aimInReach;

        private NetworkList<LimbNetState> states;
        private Rigidbody body;
        private Robot robot;
        private int selected;
        private Vector3 bodyPos;
        private Quaternion bodyRot;

        // ล้ม: Server ตัดสินแล้วซิงก์ | สะโพกประกาศก่อน fallen ให้ client ได้ท่าสุดท้ายก่อนลุก
        private NetworkVariable<Vector3> fallHipsPos = new NetworkVariable<Vector3>();
        private NetworkVariable<Quaternion> fallHipsRot = new NetworkVariable<Quaternion>(Quaternion.identity);
        private NetworkVariable<bool> fallen = new NetworkVariable<bool>();
        private NetworkVariable<int> standVotes = new NetworkVariable<int>(); // คนที่คุมชิ้นอยู่และกด Q แล้ว — โชว์บนจอ
        // ชิ้นส่วนหลุด: สถานะจริงอยู่ที่ LimbAttachment ของแต่ละช่อง (ซิงก์เอง) | บิตละชิ้น = กำลังถูกดึงกลับ (ทุกเครื่องเล่นภาพลอยกลับ)
        private NetworkVariable<byte> pullMask = new NetworkVariable<byte>();
        private byte offlinePull;
        private LimbAttachment[] attachments;
        private bool[] attachedNow, pullHeld;
        private float[] pullTimer;            // Server: ดึงมานานเท่าไร
        private int myPull = -1;              // ชิ้นที่เครื่องนี้กำลังกด R ดึง
        private float[] seat;                 // ต่อกลับแล้ว: ชิ้นเด้งแล้วยุบเข้าที่ 0..1 (1 = เสร็จ)
        private Collider[] robotColliders;
        private bool down;              // นอนอยู่ (ragdoll คุม) — ทุกเครื่อง
        private bool votedStand;        // เครื่องนี้กด Q ขอลุกแล้ว
        private readonly HashSet<ulong> standVoters = new HashSet<ulong>(); // Server: ใครกด Q แล้ว
        private float getUp = -1f;      // กำลังลุก 0..1, -1 = ไม่ได้ลุก
        private Vector3[] legRest;      // เท้าตอนยืนในแกนตัวหุ่น — วางเท้าใหม่ตอนลุก
        private Collider bodyCollider;
        private NetworkTransform netTransform;

        private bool Online => IsSpawned;
        private bool DrivesBody => !Online || IsServer;

        private void Awake()
        {
            states = new NetworkList<LimbNetState>();
            body = GetComponent<Rigidbody>();
            robot = GetComponent<Robot>();
            bodyPos = transform.position;
            bodyRot = transform.rotation;
            orbit.FaceYaw(bodyRot.eulerAngles.y);

            float ground = GroundY(transform.position);
            modelRest = model.transform.localPosition;
            foreach (IKLimb limb in limbs)
                limb.Init(transform, ground, limb.isArm ? shoulder : hip, model);

            arms = new IKCombat.Arm[limbs.Length];
            for (int i = 0; i < limbs.Length; i++)
                if (limbs[i].isArm)
                    arms[i] = new IKCombat.Arm
                    {
                        shoulder = transform.InverseTransformPoint(limbs[i].root.position),
                        desired = limbs[i].target.localPosition,
                        world = limbs[i].target.position,
                    };
            if (combat.Enabled)
                foreach (IKLimb limb in limbs)
                    if (limb.isArm) limb.PoleOverride = new Vector3(combat.elbowPole.x * limb.Side, combat.elbowPole.y, combat.elbowPole.z);
            liftFrom = new Vector3[limbs.Length];
            stepAim = new Vector3[limbs.Length];
            footMark = new Vector3[limbs.Length];
            for (int i = 0; i < limbs.Length; i++) footMark[i] = limbs[i].target.position;
            swingT = new float[limbs.Length];
            swingLift = new float[limbs.Length];
            kickCharge = new float[limbs.Length];
            for (int i = 0; i < limbs.Length; i++) kickCharge[i] = -1f;
            kickDir = new Vector3[limbs.Length];
            kickFrom = new Vector3[limbs.Length];
            kickEnd = new Vector3[limbs.Length];
            kickSpeed = new float[limbs.Length];
            kickFall = new bool[limbs.Length];
            airborne = new bool[limbs.Length];
            lastBodyPos = transform.position;
            planted = new Vector3[limbs.Length];
            for (int i = 0; i < limbs.Length; i++) planted[i] = limbs[i].target.position;
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                if (t.name.EndsWith("Spine1")) { spine = t; spineRest = t.localRotation; break; }

            // IK บานพับของเราเองแทน TwoBoneIK (ตัวนั้นเลือกระนาบพับเอง ทำให้ศอก/เข่าพับผิดแกนจนทะลุโมเดล)
            var rigBuilder = model.GetComponent<UnityEngine.Animations.Rigging.RigBuilder>();
            if (rigBuilder) rigBuilder.enabled = false;
            // กรอบตัดการวาดของ skinned mesh ค้างท่า T-pose — IK ย้ายมือไปที่อื่นแล้วกล้องอยู่ด้านหน้า กรอบหลุดจอ มือหายทั้งที่อยู่ในจอ
            foreach (SkinnedMeshRenderer skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) skin.updateWhenOffscreen = true;

            bodyCollider = GetComponent<Collider>();
            netTransform = GetComponent<NetworkTransform>();
            // เท้าเทียบกึ่งกลางเท้า (ตัวหุ่นยืนตรงกึ่งกลางเท้าเสมอ) ในแกนตัวหุ่น
            Vector3 feetMid = (limbs[2].target.position + limbs[3].target.position) * 0.5f;
            legRest = new Vector3[limbs.Length];
            for (int i = 0; i < limbs.Length; i++) legRest[i] = Quaternion.Inverse(transform.rotation) * (limbs[i].target.position - feetMid);
            var ignore = new List<Collider>(GetComponents<Collider>());
            foreach (IKLimb limb in limbs)
                if (limb.hit) ignore.AddRange(limb.hit.GetComponentsInChildren<Collider>());
            ragdoll.Build(transform, model, limbs, ignore);
            robotColliders = ignore.ToArray();
            detach.Build(model, limbs.Length, robotColliders);
            attachedNow = new bool[limbs.Length];
            pullHeld = new bool[limbs.Length];
            pullTimer = new float[limbs.Length];
            seat = new float[limbs.Length];
            lastTip = new Vector3[limbs.Length];
            strikePeak = new float[limbs.Length];
            struck = new bool[limbs.Length];
            tracked = new bool[limbs.Length];
            for (int i = 0; i < limbs.Length; i++) seat[i] = 1f;
            for (int i = 0; i < limbs.Length; i++) attachedNow[i] = true;
            if (robot) robot.KnockDownRequested += ServerKnockDown;
        }

        // RobotLimb หา LimbAttachment ของตัวเองใน Awake ของมัน — อ่านหลังทุก Awake จบ
        private void Start()
        {
            attachments = new LimbAttachment[limbs.Length];
            slots = new Transform[limbs.Length];
            for (int i = 0; i < limbs.Length; i++)
            {
                RobotLimb slot = robot ? robot.GetLimb((LimbSlot)i) : null;
                attachments[i] = slot ? slot.Attachment : null;
                slots[i] = slot ? slot.transform : null;
            }
        }

        public override void OnDestroy()
        {
            if (robot) robot.KnockDownRequested -= ServerKnockDown;
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            EnsureStates();
            fallen.OnValueChanged += OnFallenChanged;
            if (!IsServer && fallen.Value) SetDown(true); // เข้าห้องมาตอนหุ่นนอนอยู่
        }

        public override void OnNetworkDespawn() => fallen.OnValueChanged -= OnFallenChanged;

        private void OnFallenChanged(bool was, bool now)
        {
            if (!IsServer && now != down) SetDown(now);
        }

        // เติมใหม่ทุกครั้งที่ว่าง — พบว่ารายการถูกล้างตอนแมตช์เริ่ม (หลัง OnNetworkSpawn) แล้วหุ่นจะคุมไม่ได้
        private void EnsureStates()
        {
            if (!IsServer || states.Count >= limbs.Length) return;
            states.Clear();
            foreach (IKLimb limb in limbs) states.Add(LimbNetState.Capture(limb));
        }

        /// <summary>ใครคุมชิ้นนี้ — มาจากหน้าเลือกชิ้นส่วน (LimbSelection)</summary>
        private ulong ControllerOf(int index)
        {
            LimbSelection selection = LimbSelection.Current;
            if (selection == null || robot == null || !robot.IsSpawned) return LimbSelection.NoClient;
            return selection.GetController(robot.NetworkObjectId, (LimbSlot)index);
        }

        private bool IsMine(int index) => Online ? ControllerOf(index) == NetworkManager.LocalClientId : index == selected;

        private bool HasController(int index) => Online ? ControllerOf(index) != LimbSelection.NoClient : index == selected;

        // ================================================================ ต่อเฟรม

        private void Update()
        {
            if (!IKInput.TryRead(cam, limbs.Length, out IKInput input)) return;
            if (Online) EnsureStates();
            if (Online && states.Count < limbs.Length) return; // client: ยังไม่ได้ซิงก์รอบแรก

            // ออนไลน์: ขยับได้เมื่อแมตช์เริ่ม (Host กด Start ในหน้าเลือกชิ้นส่วน)
            bool canAct = (!Online || MatchSession.Current == null || MatchSession.Current.IsPlaying) && !down && getUp < 0f;
            if (input.pressedSlot >= 0)
            {
                if (!Online) selected = input.pressedSlot;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                // ออนไลน์: ขอสลับไปคุมชิ้นนั้น (ว่างอยู่เท่านั้น) — ตัวสลับของหุ่นเดิม (LimbControlBinder) ใช้ไม่ได้เพราะหุ่นนี้ไม่มีตัวควบคุมฟิสิกส์ให้ผูก
                else if (LimbSelection.Current != null && canAct) LimbSelection.Current.DebugCycleLimbRpc(input.pressedSlot);
#endif
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (input.knockDown && canAct)
            {
                if (Online) KnockDownRpc();
                else ServerKnockDown(FallReason.KnockedDown);
            }
#endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (input.detachTest && canAct)
                for (int i = 0; i < limbs.Length; i++)
                    if (IsMine(i))
                    {
                        if (Online) DetachTestRpc(i);
                        else if (attachments[i]) attachments[i].ServerDetach();
                        break;
                    }
#endif
            UpdateDetach(input);

            // ล้มแล้วนอนไปเรื่อยๆ จนทุกคนที่คุมชิ้นกด Q (เล่นคนเดียว = กดเองลุกเลย) — ขาหลุดอยู่ต้องดึงกลับก่อน
            if (down && input.standUp && !votedStand && (Online || LegsAttached()))
            {
                votedStand = true;
                if (Online) StandVoteRpc();
                else StandUp();
            }

            // กล้องหมุนด้วยคลิกขวาเท่านั้น ไม่หันตามหุ่นเอง (ทุกชิ้น) — หันตาม = จุดเล็งบนพื้นเลื่อน/ผู้เล่นคุมมุมเองไม่ได้
            if (input.looking) orbit.Look(input.lookDelta);
            if (!input.shift) orbit.Zoom(input.scroll);              // ล้อเมาส์ = ซูม | Shift + ล้อ = ดัน/ดึงมือ
            float push = input.shift ? input.scroll * depthSpeed : 0f;

            aimArm = -1;
            float remoteT = 1f - Mathf.Exp(-remoteSmoothing * Time.deltaTime);
            for (int i = 0; i < limbs.Length; i++)
            {
                IKLimb limb = limbs[i];
                if (!Attached(i)) continue; // หลุดอยู่: คุมไม่ได้ (ภาพ/ดึงกลับอยู่ใน UpdateDetach)
                if (down)
                {
                    if (IsMine(i))
                    {
                        DriveDown(limb, input);
                        if (Online) SendLimbRpc(i, LimbNetState.Capture(limb));
                    }
                    else if (Online)
                    {
                        states[i].ApplyTo(limb, remoteT);
                        if (!HasController(i)) limb.Pull = false; // คนคุมหลุดเน็ตกลางคลิกค้าง — ไม่ให้ชิ้นนั้นดึงค้างไปถึงตอนลุก
                    }
                    continue;
                }
                if (IsMine(i) && canAct)
                {
                    if (limb.isArm && combat.pointAndClick && cam) DriveCombatArm(i, input); // หมัดต้องจบท่าแม้กำลังหันกล้อง
                    else if (!limb.isArm) DriveLeg(i, input); // ก้าวต้องจบแม้กำลังหันกล้อง
                    else if (!input.looking && cam) DriveArm(limb, input.ray, push, input.held);
                    if (Online) SendLimbRpc(i, LimbNetState.Capture(limb));
                }
                else if (limb.isArm && combat.idleGuard && !HasController(i))
                {
                    // ไม่มีใครคุม: ทุกเครื่องยกการ์ดเอง (ท่าการ์ดอยู่ในแกนตัวหุ่น ได้ค่าเดียวกันทุกเครื่อง)
                    combat.Idle(limb, arms[i], transform, follow, Time.deltaTime);
                    limb.Grip = Mathf.MoveTowards(limb.Grip, 1f, gripSpeed * Time.deltaTime);
                }
                else
                {
                    if (arms[i] != null) combat.Release(limb, arms[i]); // ไม่ให้หมัดที่ชาร์จค้างไว้พุ่งเองตอนได้คุมคืน
                    limb.Lifted = false; // ได้คุมคืนทีหลัง = เริ่มก้าวใหม่ ไม่ต่อโค้งเก่า
                    CancelKick(i);       // ชาร์จ/ถีบค้างไม่ไปต่อเอง (เครื่องอื่นได้ค่าจริงจาก ApplyTo ข้างล่าง)
                    if (limb.isArm && !Online) limb.Grip = Mathf.MoveTowards(limb.Grip, 0f, gripSpeed * Time.deltaTime);
                    if (Online) states[i].ApplyTo(limb, remoteT);
                    if (!limb.isArm && DrivesBody && !HasController(i))
                    {
                        // คนคุมปล่อยขากลางก้าว (สลับชิ้น/หลุดเน็ต) → วางเท้าลงตรงนั้น — ค้างกลางอากาศแล้วนับเป็นเท้ายกตลอด ยกอีกข้างเมื่อไรก็ล้ม
                        bool dropped = airborne[i];
                        if (dropped) limb.target.position = OnGround(limb, limb.target.position);
                        if ((PivotIdleFoot(limb) || dropped) && Online) states[i] = LimbNetState.Capture(limb);
                    }
                }
                if (limb.isArm && combat.Enabled) // กำปั้นตรงแนวแขน ฝ่ามือหันหน้า-เข้าใน (ตั้งการ์ด) / คว่ำลง (ตอนเหยียดหมัด)
                    limb.OrientHand(transform.rotation, limb.tip.position - limb.mid.position,
                                    transform.rotation * new Vector3(-limb.Side, -0.4f, 0f)); // ฝ่ามือหันเข้าหากัน สันหมัดชี้หน้า
                else if (limb.isArm) limb.OrientHand(transform.rotation);
            }

            if (getUp >= 0f) getUp = Mathf.Min(1f, getUp + Time.deltaTime / ragdoll.getUpTime); // จบใน LateUpdate หลังไล่ท่าเฟรมสุดท้าย

            if (!down)
            {
                DetectLandings(); // นอนอยู่: เป้าเท้าขึ้นลงไม่ใช่ก้าวเท้า (ไม่เล่นเสียงกระทืบ)
                if (DrivesBody) UpdateBody();
            }
        }

        private void FixedUpdate()
        {
            if (DrivesBody)
            {
                body.isKinematic = true; // Robot.SetSimulationEnabled ปลด kinematic ตอนเริ่มแมตช์ — ตัวนี้เดินด้วยสคริปต์
                body.MovePosition(bodyPos);
                body.MoveRotation(bodyRot);
            }
            ragdoll.Enforce(down, !DrivesBody);
            if (down)
            {
                // ทุกเครื่องออกแรงเอง: client เห็นแขนขาที่ตัวเองคุมขยับทันที | ตัวหุ่นเคลื่อนจริงตามฟิสิกส์ของ Server
                foreach (IKLimb limb in limbs)
                    if (limb.Pull) ragdoll.Pull(limb, limb.target.position);
                FixedDown();
            }
            else if (DrivesBody && getUp < 0f && (!Online || MatchSession.Current == null || MatchSession.Current.IsPlaying)) EvaluateFall();
            foreach (IKLimb limb in limbs)
                if (limb.hit) limb.hit.MovePosition(limb.tip.position);
        }

        private void LateUpdate()
        {
            if (down)
            {
                // ragdoll คุมกระดูก — ไม่แก้ IK, กล้องตามสะโพก (ตัวหุ่นยังอยู่ที่เดิมจนกว่าจะลุก)
                foreach (IKLimb limb in limbs) limb.ApplyFingers();
                // ช่อง RobotLimb ตามปลายแขนขาตอนนอนด้วย — ไม่งั้นค้างที่เดิม ศัตรูวัดระยะถึงชิ้นแล้ววืดทั้งที่ตีโดนตัว
                for (int i = 0; i < limbs.Length; i++)
                    if (slots != null && slots[i] && Attached(i)) slots[i].position = limbs[i].tip.position;
                lastBodyPos = transform.position;
                Vector3 hips = ragdoll.Hips.transform.position;
                if (cam) orbit.Apply(cam, new Vector3(hips.x, transform.position.y, hips.z), 0f, true);
                return;
            }
            if (getUp >= 0f)
            {
                ragdoll.RestoreRest();
                lastBodyPos = transform.position; // ตัวหุ่นเพิ่ง Teleport ไปที่สะโพก — ไม่ให้นับเป็นความเร็วแล้วลำตัวโน้มกระตุก
            }

            // ลำตัวบิด/โน้มตามหมัด + เอียงตามทิศที่ตัวเคลื่อน ก่อนแก้ IK (ไหล่ขยับตามลำตัว แล้ว IK พามือไปถึงเป้าเหมือนเดิม)
            UpdateLean();
            if (spine && (combat.Enabled || feel.leanPerSpeed > 0f))
            {
                Vector3 pose = combat.Enabled ? combat.BodyPose(limbs, arms) : Vector3.zero;
                bodyPose = Vector3.Lerp(bodyPose, pose, 1f - Mathf.Exp(-combat.bodySmoothing * Time.deltaTime));
                model.transform.localPosition = modelRest + Vector3.forward * bodyPose.z; // ตัวพุ่งตามหมัด (เท้าเป็นเป้าในโลก จึงอยู่ที่เดิม)
                combat.torsoTurn = Quaternion.AngleAxis(bodyPose.x, Vector3.up) * Quaternion.AngleAxis(bodyPose.y + lean.x, Vector3.right)
                                 * Quaternion.AngleAxis(lean.y, Vector3.forward);
                spine.localRotation = spineRest;
                spine.rotation = Quaternion.AngleAxis(bodyPose.x, transform.up)
                               * Quaternion.AngleAxis(bodyPose.y + lean.x, transform.right)
                               * Quaternion.AngleAxis(lean.y, transform.forward) * spine.rotation;
            }
            for (int i = 0; i < limbs.Length; i++)
            {
                IKLimb limb = limbs[i];
                if (!Attached(i)) continue; // ซ่อนอยู่ (กระดูกย่อ) — แก้ IK แล้วทิศเป็นศูนย์
                // ขา: เข่าชี้ตามทิศเท้า (ไม่ใช่ทิศลำตัว) — เท้าเฉียงแล้วขาไม่บิดที่ข้อเท้า
                limb.Solve(limb.isArm ? transform.rotation : Quaternion.Euler(0f, FootYaw(limb), 0f));
                limb.ApplyFingers();
                if (slots != null && slots[i]) slots[i].position = limb.tip.position;
            }
            if (DrivesBody) ServerStrikes();
            if (getUp >= 0f)
            {
                ragdoll.Blend(Mathf.SmoothStep(0f, 1f, getUp));
                if (getUp >= 1f)
                {
                    getUp = -1f;
                    if (bodyCollider) bodyCollider.enabled = true;
                    fallRules.Reset(); // ลุกปุ๊บไม่ล้มซ้ำจากตัวจับเวลาเก่า
                }
            }
            if (cam) orbit.Apply(cam, transform.position, ControlledArmSide());
        }

        // ================================================================ เคอร์เซอร์ → เป้า

        // มือไปตรงที่เคอร์เซอร์ชี้: ระนาบหันหากล้อง ห่างไหล่ไปข้างหน้า Depth แล้วจำกัดตามข้อต่อ
        private void DriveArm(IKLimb arm, Ray ray, float push, bool held)
        {
            arm.Depth = Mathf.Clamp(arm.Depth + push * arm.Reach, arm.Reach * 0.2f, arm.Reach);
            var plane = new Plane(cam.transform.forward, arm.root.position + transform.forward * arm.Depth);
            if (plane.Raycast(ray, out float enter))
            {
                // เลยขอบไหล่ → ติดขอบฝั่งที่แขนอยู่ ไม่กระโดดไปขอบอีกฝั่ง
                float yawNow = arm.Limits.AnglesOf(arm, transform.rotation, arm.target.position).x;
                Vector3 goal = arm.Limits.Limit(arm, transform.rotation, ray.GetPoint(enter), yawNow);
                arm.target.position = Vector3.Lerp(arm.target.position, goal, 1f - Mathf.Exp(-follow * Time.deltaTime));
            }
            arm.Grip = Mathf.MoveTowards(arm.Grip, held ? 1f : 0f, gripSpeed * Time.deltaTime);
        }

        // โหมดต่อสู้: เคอร์เซอร์เล็งของใกล้ตัว (ตัวหุ่นอยู่ layer Ignore Raycast ยิงไม่โดนตัวเอง)
        // ไม่มีอะไรใกล้ = เล็งบนระนาบหน้าไหล่ระยะสุดแขน (ไม่งั้นหมัดพุ่งลงไปหาพื้นไกลๆ) | มือกำตลอด
        private void DriveCombatArm(int index, IKInput input)
        {
            IKLimb arm = limbs[index];
            if (!input.looking)
            {
                bool near = Physics.Raycast(input.ray, out RaycastHit hit, 1000f)
                            && Vector3.Distance(hit.point, arm.root.position) <= arm.Reach * 1.5f;
                var plane = new Plane(cam.transform.forward, arm.root.position + transform.forward * arm.Reach);
                if (near) aimPoint = hit.point;
                else if (plane.Raycast(input.ray, out float enter)) aimPoint = input.ray.GetPoint(enter);
                aimOnTarget = near && hit.rigidbody && !hit.rigidbody.isKinematic;
                aimInReach = !near || Vector3.Distance(hit.point, arm.root.position) <= arm.Reach;
            }
            aimArm = index;
            IKCombat.Arm a = arms[index];
            combat.Drive(arm, a, transform, aimPoint, input.held, follow, Time.deltaTime);
            arm.Grip = Mathf.MoveTowards(arm.Grip, 1f, gripSpeed * Time.deltaTime);
            if (feel.hitFeel && a.state == IKCombat.State.Punching && !a.hit) TryHit(arm, a);
        }

        // กำปั้นแตะของที่มี Rigidbody (ไม่นับตัวหุ่นเอง/พื้น) ครั้งแรกของหมัดนี้ → หยุดภาพ จอสั่น ประกายไฟ เสียง
        // เอฟเฟกต์เล่นเฉพาะเครื่องที่คุมแขน — แรงผลักของจริงมาจาก collider มือ (_Hit) ที่ server
        private void TryHit(IKLimb arm, IKCombat.Arm a)
        {
            Vector3 fist = arm.tip.position;
            int n = Physics.OverlapSphereNonAlloc(fist, feel.hitRadius, hitBuffer, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider c = hitBuffer[i];
                if (c.attachedRigidbody == null || c.transform.IsChildOf(transform)) continue;
                a.hit = true;
                Vector3 point = c.ClosestPoint(fist);
                Vector3 normal = fist - point;
                ImpactFeel.PlayHit(point, normal.sqrMagnitude > 1e-6f ? normal.normalized : -transform.forward, Mathf.Lerp(10f, 40f, a.charge));
                return;
            }
        }

        // ทุกเครื่อง: เป้าเท้าลงจากที่ยกไว้ถึงพื้น = เท้ากระแทก → เสียง ฝุ่น จอสั่น + ตัวยวบ (เครื่องที่คุมตัวหุ่น)
        private void DetectLandings()
        {
            for (int i = 0; i < limbs.Length; i++)
            {
                IKLimb leg = limbs[i];
                if (leg.isArm) continue;
                Vector3 p = leg.target.position;
                float ground = GroundY(p);
                bool up = p.y > ground + leg.RestHeight + 0.3f;
                if (!up) planted[i] = p;
                if (airborne[i] && !up)
                {
                    if (feel.footstepFeel) ImpactFeel.PlayFootstep(new Vector3(p.x, ground, p.z), 1f);
                    if (DrivesBody && feel.bodyFrequency > 0f) bodyVel.y -= feel.landingDip; // ยวบได้เฉพาะตอนตัวเป็นสปริง
                }
                airborne[i] = up;
            }
        }

        // ทุกเครื่อง: ความเร็วตัวหุ่นจากตำแหน่งจริง (ซิงก์ด้วย NetworkTransform) → ลำตัวโน้มตามทิศเดินแบบสปริง ไม่แข็งทื่อ
        private void UpdateLean()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Vector3 v = transform.InverseTransformDirection((transform.position - lastBodyPos) / dt);
            lastBodyPos = transform.position;
            Vector3 goal = new Vector3(Mathf.Clamp(v.z * feel.leanPerSpeed, -feel.maxLean, feel.maxLean),
                                       Mathf.Clamp(-v.x * feel.leanPerSpeed, -feel.maxLean, feel.maxLean), 0f);
            // ถีบ: ลำตัวเอนหนีเท้าที่ยกสูง (แบบเตะสูงคาราเต้) — คิดจากเป้าเท้าที่ซิงก์ ทุกเครื่องได้ท่าเดียวกัน
            for (int i = 0; i < limbs.Length; i++)
            {
                IKLimb leg = limbs[i];
                if (leg.isArm || !leg.KickPose) continue;
                Vector3 d = transform.InverseTransformDirection(leg.target.position - leg.root.position);
                Vector2 away = new Vector2(d.x, d.z).normalized;
                float raise = kickLean * Mathf.Clamp01((leg.target.position.y - planted[i].y) / leg.Reach);
                goal.x -= away.y * raise; // ถีบไปหน้า = เอนหลัง
                goal.y += away.x * raise; // ถีบไปขวา = เอนซ้าย
            }
            Vector3 now = lean;
            IKFeel.Spring(ref now, ref leanVel, goal, 2f, 0.5f, dt);
            lean = now;
        }

        // คุมแขนในโหมดต่อสู้อยู่ → ฝั่งของแขน (-1/+1) ให้กล้องเลื่อนไปทางไหล่นั้น | ไม่ได้คุมแขน = 0
        private float ControlledArmSide()
        {
            if (!combat.pointAndClick) return 0f;
            for (int i = 0; i < limbs.Length; i++)
                if (limbs[i].isArm && IsMine(i)) return limbs[i].Side;
            return 0f;
        }

        // หุ่นเดิม (LegInput + LegController): เคอร์เซอร์ชี้พื้น = จุดวางเท้า (แนวราบจากสะโพกไม่เกิน 90% ของขา)
        // กดคลิก = ก้าวเป็นเส้นโค้งไปที่นั่น — คลิกสั้น = ก้าวครบโค้งแล้วปักเอง | กดค้าง = ลอยค้างที่ยอดโค้งตามเคอร์เซอร์ ปล่อยแล้วค่อยลง
        // แนวราบ ease in-out, ความสูง sin ¼ รอบ = ยกเร็วชะลอที่ยอด แล้วเร่งกระแทกพื้นเหมือนของหนัก | คลิกขวาหันกล้อง = จุดเล็งค้างไว้
        private void DriveLeg(int i, IKInput input)
        {
            IKLimb leg = limbs[i];
            // จุดเล็งตามเมาส์ทุกเฟรม (◈ บอกว่าคลิกแล้วเท้าจะลงตรงไหน) — คลิกขวาหันกล้อง = จุดเล็งค้าง (หุ่นเดิม UpdatePointAim)
            if (!input.looking && cam && Physics.Raycast(input.ray, out RaycastHit hit, 1000f)) footMark[i] = ClampStep(leg, hit.point);
            // ส่งเป้าเฉพาะตอนกดค้าง (หุ่นเดิม) — ปล่อยแล้วเท้าลงตรงจุดสุดท้าย ไม่ไหลตามเมาส์
            if (DriveKick(i, leg, input, Mathf.Min(Time.deltaTime, 0.05f))) return;
            if (input.held) stepAim[i] = footMark[i];
            if (input.held && !leg.Lifted)
            {
                liftFrom[i] = leg.target.position;
                swingT[i] = 0f;
                swingLift[i] = 0f;
                leg.Lifted = true;
            }
            if (!leg.Lifted) return;

            float dt = Mathf.Min(Time.deltaTime, 0.05f); // เฟรมกระตุกยาวไม่กระโดดข้ามช่วงยก (เท้าจะไถลไปกับพื้น)
            swingT[i] = Mathf.MoveTowards(swingT[i], 1f, dt / stepSwingTime);
            bool rising = input.held || swingT[i] < 0.5f;
            swingLift[i] = Mathf.MoveTowards(swingLift[i], rising ? 1f : 0f, dt / (stepSwingTime * 0.5f));
            if (!input.held && swingT[i] >= 1f && swingLift[i] <= 0f)
            {
                // เท้าถึงพื้น: ปักตรงจุดที่เล็ง หันตามก้าว (DetectLandings เล่นเสียง/ฝุ่น/จอสั่น)
                leg.target.SetPositionAndRotation(OnGround(leg, stepAim[i]), Quaternion.Euler(0f, LandingYaw(i, leg), 0f) * leg.RestRotation);
                leg.Lifted = false;
                return;
            }
            Vector3 flat = Vector3.Lerp(liftFrom[i], stepAim[i], Mathf.SmoothStep(0f, 1f, swingT[i]));
            leg.target.position = OnGround(leg, flat) + Vector3.up * (Mathf.Sin(swingLift[i] * Mathf.PI * 0.5f) * leg.Reach * stepArcHeight);
            // ลอยอยู่: เท้าค่อยๆ หมุนกลับชี้ทิศลำตัว (หุ่นเดิม) — หันตามก้าวตอนวางลง
            leg.target.rotation = Quaternion.Slerp(leg.target.rotation, transform.rotation * leg.RestRotation, 1f - Mathf.Exp(-feel.footTurnSpeed * dt));
        }

        // ถีบ (ผู้ใช้ 2026-10-28: เตะแนวพื้นแบบเดิมดูทำอะไรใครไม่ได้ → เปลี่ยนเป็นถีบ)
        // กด Shift (ผู้ใช้ 2026-10-28 "กดแล้วถีบเลย" + "เอาแบบบาทาลูบพักตร์"): ยกเข่า kickChamberTime วิ แล้วถีบขาเหยียดชี้ขึ้น kickHighAngle
        // ไปทาง ◈ เร็ว kickThrustSpeed × แรงที่เหลือของขา, ลำตัวเอนหนีเท้า (UpdateLean)
        // ยืดขาสุดแล้วเท้าตกลงตรงที่อยู่ (ผู้ใช้ขอ) — ใต้เท้ามีของสูงกว่าเท้า (ศัตรู/ตึก) = ชักกลับวางที่เดิม ไม่ขึ้นไปยืนบนนั้น
        // true = ท่าถีบคุมเท้าเฟรมนี้ (ไม่ก้าว)
        // ซิงก์: leg.KickPose = อยู่ในท่า (ตัวหุ่นไม่ไหลตามเท้านี้), leg.Kick = ช่วงพุ่งออก (Server นับดาเมจเฉพาะช่วงนี้)
        private bool DriveKick(int i, IKLimb leg, IKInput input, float dt)
        {
            Vector3 hip = leg.root.position;
            if (kickCharge[i] < 0f && !leg.KickPose)
            {
                if (!input.kick) return false;
                kickCharge[i] = 0f;
                kickDir[i] = KickAim(i, leg, hip);
                kickFrom[i] = leg.target.position - hip; // รวมความสูง — กดกลางก้าว (เท้าลอย) ยกเข่าต่อจากตรงนั้น ไม่วาร์ปลงพื้นก่อน
                leg.Lifted = leg.KickPose = true;
            }
            // จุดเริ่มท่า ยกเท่ายอดโค้งก้าว — ชักกลับมาตรงนี้แล้วต่อช่วงลงของก้าวได้ไม่กระตุก
            Vector3 home = OnGround(leg, hip + kickFrom[i]) + Vector3.up * (leg.Reach * stepArcHeight);
            // ปลายเท้าเชิดตามมุมขา ไม่เกิน 85° — เกินแล้วมุม yaw ของเท้ากลับด้าน เข่าชี้ผิดทาง
            Quaternion sole = Quaternion.AngleAxis(-Mathf.Min(kickSolePitch + kickHighAngle, 85f), Vector3.Cross(Vector3.up, kickDir[i]))
                            * Quaternion.LookRotation(kickDir[i]) * leg.RestRotation;
            if (kickCharge[i] < 0f && kickFall[i])
            {
                // ตก: เร่งลงตรงๆ จนถึงพื้น แล้วปักแบบก้าว (DetectLandings เล่นเสียง/ฝุ่น/จอสั่น) หันตามครึ่งมุมก้าวเหมือนก้าวปกติ
                kickSpeed[i] += kickFallGravity * dt;
                Vector3 land = OnGround(leg, ClampStep(leg, leg.target.position));
                Quaternion flat = Quaternion.Euler(0f, transform.eulerAngles.y + StepTurn(leg, planted[i], land), 0f) * leg.RestRotation;
                leg.target.position = new Vector3(land.x, Mathf.Max(land.y, leg.target.position.y - kickSpeed[i] * dt), land.z);
                leg.target.rotation = Quaternion.Slerp(leg.target.rotation, flat, 1f - Mathf.Exp(-12f * dt));
                if (leg.target.position.y > land.y) return true;
                leg.target.rotation = flat;
                CancelKick(i);
                leg.Lifted = false;
                return true;
            }
            if (kickCharge[i] < 0f)
            {
                if (!leg.Kick) kickEnd[i] = home; // ชักกลับ: ตัวหุ่นขยับระหว่างนั้น — ตามจุดเริ่มท่า
                leg.target.position = Vector3.MoveTowards(leg.target.position, kickEnd[i], kickSpeed[i] * dt);
                if (leg.Kick) leg.target.rotation = Quaternion.Slerp(leg.target.rotation, sole, 1f - Mathf.Exp(-20f * dt));
                if (leg.target.position != kickEnd[i]) return true;
                if (leg.Kick)
                {
                    leg.Kick = false;
                    kickFall[i] = OnGround(leg, ClampStep(leg, leg.target.position)).y < leg.target.position.y;
                    kickSpeed[i] = kickFall[i] ? 0f : kickSpeed[i] * 0.5f; // ตกเริ่มจากนิ่ง | ชักกลับช้าลงครึ่งหนึ่ง
                    return true;
                }
                CancelKick(i);
                liftFrom[i] = stepAim[i] = ClampStep(leg, leg.target.position);
                swingT[i] = swingLift[i] = 1f; // ช่วงลงของก้าว + เท้าค่อยๆ หมุนกลับราบ
                return true;
            }
            // ยกเข่า: เท้าจากพื้นขึ้นไปเก็บไว้หน้าตัวใน kickChamberTime วิ
            kickCharge[i] += dt;
            float t = Mathf.Clamp01(kickCharge[i] / kickChamberTime);
            if (t < 1f)
            {
                kickDir[i] = KickAim(i, leg, hip); // ระหว่างยกเข่ายังเล็งเบนได้ — ปล่อยเท้าแล้วล็อกทิศ
                Vector3 chamber = hip + kickDir[i] * (leg.Reach * 0.2f) - Vector3.up * (leg.Reach * kickChamberDrop);
                leg.target.position = Vector3.Lerp(hip + kickFrom[i], chamber, Mathf.SmoothStep(0f, 1f, t));
                leg.target.rotation = Quaternion.Slerp(Quaternion.LookRotation(kickDir[i]) * leg.RestRotation, sole, t * 0.5f);
                return true;
            }
            kickCharge[i] = -1f;
            kickSpeed[i] = kickThrustSpeed * KickHealthPower(i);
            // ยืดสุด (0.98 ของขา — เหยียดตรงเป๊ะ IK หาทิศเข่าไม่ได้)
            float e = kickHighAngle * Mathf.Deg2Rad;
            kickEnd[i] = hip + (kickDir[i] * Mathf.Cos(e) + Vector3.up * Mathf.Sin(e)) * (leg.Reach * 0.98f);
            leg.Kick = true;
            return true;
        }

        // ถีบได้แค่ข้างหน้า: ทิศไป ◈ เบนจากหน้าหุ่นได้ไม่เกิน ±kickAimLimit | ◈ ใกล้สะโพกเกินไป = ตรงหน้า
        private Vector3 KickAim(int i, IKLimb leg, Vector3 hip)
        {
            Vector3 front = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            Vector3 aim = Vector3.ProjectOnPlane(footMark[i] - hip, Vector3.up);
            if (aim.magnitude <= leg.Reach * 0.1f) return front;
            float yaw = Mathf.Clamp(Vector3.SignedAngle(front, aim, Vector3.up), -kickAimLimit, kickAimLimit);
            return Quaternion.AngleAxis(yaw, Vector3.up) * front;
        }

        private void CancelKick(int i)
        {
            limbs[i].Kick = limbs[i].KickPose = kickFall[i] = false;
            kickCharge[i] = -1f;
        }

        private float KickHealthPower(int i)
        {
            LimbHealth health = robot ? robot.GetLimb((LimbSlot)i)?.Health : null;
            return health ? Mathf.Lerp(kickPowerAtZeroHealth, 1f, Mathf.Clamp01(health.Hp / Mathf.Max(1f, health.StartingMaxHp))) : 1f;
        }

        // หุ่นเดิม (ClampPlantWithinReach): ตัดแค่ระยะแนวราบจากสะโพก ไม่ใช่ทรงกลมความยาวขา — ตัดทรงกลมแล้วเท้าโดนดึงมาใต้ตัวทุกก้าว
        // ระหว่างก้าวลำตัวไหลไปหาเท้า (UpdateBody) เท้าจึงเอื้อมถึง
        private static Vector3 ClampStep(IKLimb leg, Vector3 point)
        {
            Vector3 hip = leg.root.position;
            Vector3 flat = Vector3.ClampMagnitude(new Vector3(point.x - hip.x, 0f, point.z - hip.z), leg.Reach * 0.9f);
            return new Vector3(hip.x + flat.x, point.y, hip.z + flat.z);
        }

        private static Vector3 OnGround(IKLimb leg, Vector3 p) => new Vector3(p.x, GroundY(p) + leg.RestHeight, p.z);

        // หุ่นเดิม (LegController.LandingYaw): เท้าลงหันตามทิศลำตัว + ครึ่งหนึ่งของมุมก้าว (ไม่เกิน maxTurnPerStep)
        // ก้าวสั้นกว่า 20% ของขา หรือถอยหลัง/เฉียงเกิน 110° = ทิศลำตัว | ตัวหันตามเท้าที่ปัก (UpdateBody) = เลี้ยว
        private float LandingYaw(int i, IKLimb leg) => transform.eulerAngles.y + StepTurn(leg, liftFrom[i], stepAim[i]);

        // ครึ่งหนึ่งของมุมก้าว (from → to) เทียบทิศหน้าหุ่นตอนนี้ ไม่เกิน maxTurnPerStep | ก้าวสั้น/ถอยหลัง/เฉียงเกิน 110° = 0
        private float StepTurn(IKLimb leg, Vector3 from, Vector3 to)
        {
            Vector3 step = Vector3.ProjectOnPlane(to - from, Vector3.up);
            float angle = Vector3.SignedAngle(transform.forward, step, Vector3.up);
            if (step.magnitude <= leg.Reach * 0.2f || Mathf.Abs(angle) > 110f) return 0f;
            return Mathf.Clamp(angle * 0.5f, -feel.maxTurnPerStep, feel.maxTurnPerStep);
        }

        // ================================================================ ตัวหุ่น

        // หุ่นเดิม (TorsoBalance): ตัวหุ่นไหลไปกลางระหว่างเท้าที่ปัก + เท้าที่กำลังก้าว (ถ่ายน้ำหนักไปข้างหน้าระหว่างก้าว
        // ไม่งั้นตัวโดนดึงกลับมาเหนือขาที่ยืน เท้าที่ก้าวเอื้อมได้นิดเดียว) → ก้าวเท้าทีละข้าง = เดิน
        private void UpdateBody()
        {
            Vector3 mid = Vector3.zero;
            int legs = 0;
            bool stepping = false;
            float floor = float.MaxValue;
            for (int i = 0; i < limbs.Length; i++)
            {
                IKLimb leg = limbs[i];
                if (leg.isArm) continue;
                mid += leg.KickPose ? planted[i] : leg.target.position; // ถีบ = ยืนขาเดียว ตัวไม่ไหลตามเท้าที่ถีบ
                legs++;
                stepping |= airborne[i] && !leg.KickPose;
                // ความสูงตัว = พื้นใต้เท้าที่ปักอยู่ข้างที่ต่ำสุด (เท้ายก/ถีบใช้จุดที่ปักล่าสุด) — ไม่ใช่พื้นใต้กลางตัว:
                // ยืนบนกล่องสองใบ กลางตัวเป็นร่อง ตัวเคยทรุดจนขาพับ | ยืนต่างระดับ ขาข้างสูงงอเอาแบบขึ้นบันได
                floor = Mathf.Min(floor, (airborne[i] || leg.KickPose ? planted[i].y : leg.target.position.y) - leg.RestHeight);
            }
            if (legs == 0) return;
            mid /= legs;

            Vector3 goal = new Vector3(mid.x, floor - crouch, mid.z);
            // หุ่นเดิม stepCenterMultiplier 2: ระหว่างก้าวดึงตัวแรงขึ้นสองเท่า (แรงสปริง ×2 = ความถี่ ×√2) ตัวไหลทันเท้า = ก้าวยาว
            float frequency = feel.bodyFrequency * (stepping ? 1.414f : 1f);
            if (feel.bodyFrequency > 0f) IKFeel.Spring(ref bodyPos, ref bodyVel, goal, frequency, feel.bodyDamping, Time.deltaTime);
            else bodyPos = Vector3.Lerp(bodyPos, goal, 1f - Mathf.Exp(-bodyFollow * Time.deltaTime));

            // หุ่นเดิม (TorsoBalance heading): ตัวหันตามค่าเฉลี่ยทิศเท้าที่ปักอยู่ด้วยสปริง (ทิศเท้าซิงก์มากับเป้าเท้าทุกเครื่อง)
            // + ระหว่างก้าวตัวบิดไปทางที่เท้ากำลังจะลง (หุ่นเดิมเกิดจากแรงดึงสะโพกในฟิสิกส์ — วัดได้ ~25° ตอนก้าวเฉียง 60°)
            // เท้าจึงลงหันเพิ่มจากตัวที่บิดแล้ว (LandingYaw) = เลี้ยวตามจุดที่วางเท้า
            Vector3 facing = Vector3.zero;
            float twist = 0f;
            for (int i = 0; i < limbs.Length; i++)
            {
                if (limbs[i].isArm) continue;
                if (!airborne[i]) facing += Quaternion.Euler(0f, FootYaw(limbs[i]), 0f) * Vector3.forward;
                else twist += StepTurn(limbs[i], planted[i], limbs[i].target.position);
            }
            if (facing.sqrMagnitude < 1e-2f) return;
            float yaw = bodyRot.eulerAngles.y;
            Vector3 x = new Vector3(yaw, 0f, 0f), v = new Vector3(yawVel, 0f, 0f);
            Vector3 face = new Vector3(yaw + Mathf.DeltaAngle(yaw, Quaternion.LookRotation(facing).eulerAngles.y) + twist, 0f, 0f);
            IKFeel.Spring(ref x, ref v, face, feel.headingFrequency, feel.headingDamping, Time.deltaTime);
            yawVel = v.x;
            bodyRot = Quaternion.Euler(0f, x.x, 0f);
        }

        // ================================================================ ดาเมจ

        // หุ่นเดิม (LimbStrike): กำปั้นเร็วเกิน strikeMinSpeed = กำลังต่อย → แตะของที่รับดาเมจได้ = ดาเมจตามความเร็วพีคของท่า
        // หนึ่งท่าหนึ่งครั้ง (ช้าลงต่ำกว่าเกณฑ์ = จบท่า) | Server ดูกำปั้นที่แก้ IK แล้ว — แขนของ client มาจาก state ที่ซิงก์
        private void ServerStrikes()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            for (int i = 0; i < limbs.Length; i++)
            {
                IKLimb limb = limbs[i];
                Vector3 tip = limb.tip.position, move = tip - lastTip[i];
                lastTip[i] = tip;
                bool wasTracked = tracked[i];
                tracked[i] = Attached(i) && !down && getUp < 0f;
                float speed = move.magnitude / dt;
                // แขน: นับเฉพาะกำปั้นพุ่งออกจากไหล่ (ดึงหมัดกลับเร็วๆ ไม่ใช่การต่อย) | ขา: เฉพาะช่วงถีบ (ก้าวเดินไม่ใช่การเตะ)
                bool striking = (limb.isArm || limb.Kick) && Vector3.Dot(move, tip - limb.root.position) > 0f;
                if (!wasTracked || !tracked[i] || speed < strikeMinSpeed || !striking)
                {
                    strikePeak[i] = 0f;
                    struck[i] = false;
                    continue;
                }
                strikePeak[i] = Mathf.Max(strikePeak[i], speed);
                if (struck[i]) continue;

                int n = Physics.OverlapSphereNonAlloc(tip, strikeRadius, hitBuffer, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                for (int k = 0; k < n; k++)
                {
                    Collider c = hitBuffer[k];
                    if (Robot.FromCollider(c) == robot) continue;
                    float damage = Mathf.Min(strikePeak[i] * (limb.isArm ? strikeSpeedToDamage : kickSpeedToDamage), strikeMaxDamage);
                    var info = new DamageInfo(damage, c.ClosestPoint(tip), move.normalized, limb.isArm ? DamageSource.Punch : DamageSource.Kick, robot ? robot.GetTeam() : Team.None)
                    {
                        knockback = Mathf.Lerp(2f, 12f, Mathf.Pow(Mathf.InverseLerp(strikeMinSpeed, 55f, strikePeak[i]), 2f)),
                    };
                    if (!DamageRouter.TryApply(c, info)) continue;
                    struck[i] = true;
                    Debug.Log($"[RobotIKControl] {(limb.isArm ? "หมัด" : "เตะ")} {limb.name} โดน '{c.name}' ความเร็วพีค {strikePeak[i]:F1} m/s ดาเมจ {damage:F1}");
                    break;
                }
            }
        }

        // ================================================================ ชิ้นส่วนหลุด

        private bool Attached(int i) => attachments == null || attachments[i] == null || attachments[i].IsAttached;

        private bool LegsAttached()
        {
            for (int i = 0; i < limbs.Length; i++)
                if (!limbs[i].isArm && !Attached(i)) return false;
            return true;
        }

        private bool Pulling(int i) => (((Online ? pullMask.Value : offlinePull) >> i) & 1) != 0;

        // ทุกเครื่อง: สถานะหลุด/ต่อเปลี่ยน → ภาพ | ชิ้นที่ถูกดึงลอยกลับ | R ค้าง → ขอดึง | Server นับเวลาดึงแล้วสั่งต่อ
        private void UpdateDetach(IKInput input)
        {
            if (attachments == null) return;
            for (int i = 0; i < limbs.Length; i++)
            {
                bool attached = Attached(i);
                if (attached != attachedNow[i]) { attachedNow[i] = attached; OnAttachChanged(i, attached); }
                detach.Tick(i, limbs[i], Pulling(i), Time.deltaTime);
                if (seat[i] < 1f)
                {
                    seat[i] = Mathf.Min(1f, seat[i] + Time.deltaTime / detach.seatTime);
                    float punch = 1f - seat[i];
                    limbs[i].root.localScale = Vector3.one * (1f + detach.seatPunch * punch * punch);
                }
            }

            int want = input.pull ? PullTarget() : -1;
            if (want != myPull)
            {
                if (myPull >= 0) RequestPull(myPull, false);
                if (want >= 0) RequestPull(want, true);
                myPull = want;
            }

            if (!DrivesBody) return;
            for (int i = 0; i < limbs.Length; i++)
            {
                bool active = pullHeld[i] && !Attached(i) && attachments[i] && attachments[i].RemainingReconnectCooldown <= 0f;
                pullTimer[i] = active ? pullTimer[i] + Time.deltaTime : 0f;
                if (active && pullTimer[i] >= detach.pullTime)
                {
                    attachments[i].ServerReattach();
                    pullHeld[i] = false;
                    active = false;
                    pullTimer[i] = 0f;
                }
                SetPullBit(i, active);
            }
        }

        // R ดึงชิ้นของตัวเองถ้าหลุด ไม่งั้นชิ้นที่หลุดและไม่มีใครคุม (หุ่นเดิม: Host เป็นเจ้าของชิ้นที่ไม่มีใครเลือก)
        private int PullTarget()
        {
            for (int i = 0; i < limbs.Length; i++) if (!Attached(i) && IsMine(i)) return i;
            for (int i = 0; i < limbs.Length; i++) if (!Attached(i) && !HasController(i)) return i;
            return -1;
        }

        private void RequestPull(int i, bool held)
        {
            if (Online) PullRpc(i, held);
            else pullHeld[i] = held;
        }

        private void SetPullBit(int i, bool on)
        {
            byte mask = Online ? pullMask.Value : offlinePull;
            byte next = on ? (byte)(mask | (1 << i)) : (byte)(mask & ~(1 << i));
            if (next == mask) return;
            if (Online) pullMask.Value = next;
            else offlinePull = next;
        }

        private void OnAttachChanged(int i, bool attached)
        {
            IKLimb limb = limbs[i];
            Vector3 outward = Vector3.ProjectOnPlane(limb.root.position - transform.position, Vector3.up).normalized;
            if (attached)
            {
                detach.Reattach(i, limb);
                ragdoll.SetFrozen(limb, false);
                if (limb.hit) limb.hit.detectCollisions = true;
                // เสียบเข้าล็อก: เสียงเหล็กหนัก + บูม + ประกายไฟ + หยุดภาพ + ซูม + จอสั่น, ลำตัวสะเทือน, ชิ้นเด้งแล้วยุบเข้าที่
                ImpactFeel.PlayHit(limb.root.position, outward, detach.reattachImpact);
                leanVel += new Vector3(8f, limb.Side * 14f, 0f);
                if (DrivesBody && feel.bodyFrequency > 0f) bodyVel.y -= feel.landingDip;
                seat[i] = 0f;
                return;
            }
            seat[i] = 1f; // หลุดซ้ำระหว่างเด้งเข้าที่ — ไม่ให้ตัวเด้งตั้ง scale ทับการซ่อน
            limb.Lifted = false;
            limb.Pull = false;
            CancelKick(i); // หลุดกลางท่าเตะ — ต่อกลับแล้วไม่ถีบต่อไปยังจุดเก่า
            if (arms[i] != null) combat.Release(limb, arms[i]);
            ImpactFeel.PlayHit(limb.root.position, outward, 30f);  // ประกายไฟ + จอสั่นที่ข้อต่อ
            detach.Detach(i, limb, (outward + Vector3.up) * detach.flingSpeed);
            ragdoll.SetFrozen(limb, true);
            if (limb.hit) limb.hit.detectCollisions = false;   // มือ/เท้าที่หลุดไม่ดันของ
        }

        // ================================================================ ล้ม / ลุก

        // กฎล้มเดียวกับหุ่นเดิม (FallRules): สองเท้ายกพร้อมกันนานเกิน grace = ล้ม | หุ่น IK ไม่มีกระโดด/มือจับ/แรงดึงให้กันล้ม
        private void EvaluateFall()
        {
            int legs = 0, grounded = 0;
            for (int i = 0; i < limbs.Length; i++)
                if (!limbs[i].isArm) { legs++; if (!airborne[i]) grounded++; }
            var balance = new BalanceInput
            {
                footCount = legs, groundedFeet = grounded, walkSteppingFeet = legs - grounded,
                balancedFeet = grounded, supportFoot = grounded > 0,
            };
            FallReason reason = fallRules.Evaluate(balance, Time.fixedDeltaTime);
            if (reason != FallReason.None) ServerKnockDown(reason);
        }

        /// <summary>[SERVER / เล่นคนเดียว] ล้ม — จากกฎล้ม, บอส (Robot.ServerKnockDown), ปุ่มทดสอบ B | ระหว่างลุกล้มซ้ำไม่ได้</summary>
        public void ServerKnockDown(FallReason reason)
        {
            if (!DrivesBody || down || getUp >= 0f) return;
            Debug.Log($"[RobotIKControl] ล้ม: {reason}");
            standVoters.Clear();
            if (Online)
            {
                fallHipsPos.Value = ragdoll.Hips.position;
                fallHipsRot.Value = ragdoll.Hips.rotation;
                standVotes.Value = 0; // ไม่ให้จอโชว์ยอดโหวตของรอบล้มที่แล้ว
                fallen.Value = true;
            }
            SetDown(true);
        }

        // นอนอยู่: Server ประกาศท่าสะโพก + นับโหวตลุก (เช็กทุกรอบ: คนที่ยังไม่กดออกจากห้อง/ปล่อยชิ้น = ไม่ต้องรอเขาแล้ว)
        // client สะโพกตามที่ประกาศ
        private void FixedDown()
        {
            if (!DrivesBody)
            {
                ragdoll.FollowHips(fallHipsPos.Value, fallHipsRot.Value, 1f - Mathf.Exp(-remoteSmoothing * Time.fixedDeltaTime));
                return;
            }
            if (!Online) return; // เล่นคนเดียว: ลุกตอนกด Q ใน Update
            fallHipsPos.Value = ragdoll.Hips.position;
            fallHipsRot.Value = ragdoll.Hips.rotation;

            int controllers = Controllers(out int voted);
            standVotes.Value = voted;
            if (controllers > 0 && voted == controllers && LegsAttached()) StandUp(); // ไม่มีใครคุมชิ้นเลย = นอนต่อ | ขาหลุด = ดึงกลับก่อน
        }

        // คนที่คุมชิ้นของหุ่นนี้ (คนละชิ้นนับครั้งเดียว) และในนั้นกด Q แล้วกี่คน (voted รู้เฉพาะ Server)
        private int Controllers(out int voted)
        {
            int count = 0;
            voted = 0;
            for (int i = 0; i < limbs.Length; i++)
            {
                ulong c = ControllerOf(i);
                bool repeat = false;
                for (int j = 0; j < i; j++) repeat |= ControllerOf(j) == c;
                if (c == LimbSelection.NoClient || repeat) continue;
                count++;
                if (standVoters.Contains(c)) voted++;
            }
            return count;
        }

        // [SERVER / เล่นคนเดียว] ลุก — ทุกเครื่องเริ่มไล่ท่ากลับยืน
        private void StandUp()
        {
            if (Online) fallen.Value = false;
            SetDown(false);
        }

        // ทุกเครื่อง: เริ่มนอน (ragdoll คุม) / เริ่มลุก (ตัวหุ่นย้ายไปที่สะโพก, เท้าวางข้างตัว, ไล่ท่าจากท่านอนกลับเป็นท่ายืน)
        private void SetDown(bool value)
        {
            down = value;
            votedStand = false;
            for (int i = 0; i < limbs.Length; i++) { limbs[i].Lifted = false; limbs[i].Pull = false; }
            if (value)
            {
                if (bodyCollider) bodyCollider.enabled = false; // กล่องลำตัวค้างที่เดิม ไม่ให้ขวางของ/ศัตรูตอนนอน
                Vector3 push = Vector3.ProjectOnPlane(bodyVel, Vector3.up);
                if (push.sqrMagnitude < 0.25f) push = -transform.forward; // ยืนนิ่ง = หงายหลัง
                ragdoll.Enforce(true, !DrivesBody);
                ragdoll.SetPhysics(true, push.normalized * ragdoll.topple);
                return;
            }

            Vector3 hips = DrivesBody ? ragdoll.Hips.position : fallHipsPos.Value;
            Quaternion facing = DrivesBody ? bodyRot : transform.rotation;
            ragdoll.BeginBlend();
            ragdoll.Enforce(false, false);
            ragdoll.SetPhysics(false, Vector3.zero);

            Vector3 stand = new Vector3(hips.x, 0f, hips.z);
            stand.y = GroundY(stand) - crouch;
            if (DrivesBody)
            {
                bodyPos = stand;
                bodyVel = Vector3.zero;
                yawVel = 0f;
                body.position = stand;
                if (netTransform && netTransform.IsSpawned) netTransform.Teleport(stand, facing, transform.localScale); // client ไม่ไถลจากจุดเดิม
                else transform.position = stand;
            }
            for (int i = 0; i < limbs.Length; i++)
            {
                IKLimb limb = limbs[i];
                if (limb.isArm)
                {
                    // ตอนนอนเป้าแขนถูกลากไปตามเคอร์เซอร์ — กลับท่าการ์ด/ท่าตั้งต้น แล้วสปริงมือเริ่มใหม่ที่ตัวหุ่นจุดใหม่
                    limb.target.localPosition = arms[i].desired;
                    combat.Release(limb, arms[i]);
                    if (Online && IsServer) states[i] = LimbNetState.Capture(limb);
                    continue;
                }
                Vector3 p = stand + facing * new Vector3(legRest[i].x, 0f, legRest[i].z);
                p.y = GroundY(p) + limb.RestHeight;
                limb.target.SetPositionAndRotation(p, facing * limb.RestRotation);
                airborne[i] = false;
                CancelKick(i);
                planted[i] = p;
                if (Online && IsServer) states[i] = LimbNetState.Capture(limb); // ขาที่ไม่มีใครคุมไม่ไหลกลับไปที่เดิม
            }
            getUp = 0f;
        }

        // นอนอยู่ (ทุกชิ้นเหมือนกัน): เคอร์เซอร์ = จุดที่มือ/เท้าเอื้อมไป บนระนาบผ่านไหล่/สะโพกที่หันหากล้อง ไม่เกินความยาวแขนขา
        // คลิกค้าง = ดึงจริง (แรงใน FixedUpdate) + กำมือ | ปล่อย = ห้อยตามฟิสิกส์ — ไว้ยันพื้น คลาน ปัดของ
        private void DriveDown(IKLimb limb, IKInput input)
        {
            if (!input.looking && cam)
            {
                var plane = new Plane(cam.transform.forward, limb.root.position);
                if (plane.Raycast(input.ray, out float enter))
                {
                    Vector3 goal = limb.root.position + Vector3.ClampMagnitude(input.ray.GetPoint(enter) - limb.root.position, limb.Reach);
                    limb.target.position = Vector3.Lerp(limb.target.position, goal, 1f - Mathf.Exp(-follow * Time.deltaTime));
                }
            }
            limb.Pull = input.held;
            if (limb.isArm) limb.Grip = Mathf.MoveTowards(limb.Grip, input.held ? 1f : 0f, gripSpeed * Time.deltaTime);
        }

        // ขาที่ไม่มีคนคุม (Server/เล่นคนเดียว): เท้าที่ปักค่อยๆ บิดตามทิศลำตัว — ตัวหันตามค่าเฉลี่ยเท้า ถ้าเท้านี้นิ่ง
        // ตัวหันได้แค่ครึ่งเดียวแล้วติด | คืน true เมื่อขยับ (หยุดเมื่อต่างไม่ถึง 0.5° ไม่ส่ง state ทุกเฟรม)
        private bool PivotIdleFoot(IKLimb leg)
        {
            float delta = Mathf.DeltaAngle(FootYaw(leg), transform.eulerAngles.y);
            if (feel.idleFootPivot <= 0f || leg.Lifted || Mathf.Abs(delta) < 0.5f) return false;
            leg.target.rotation = Quaternion.Euler(0f, delta * (1f - Mathf.Exp(-feel.idleFootPivot * Time.deltaTime)), 0f) * leg.target.rotation;
            return true;
        }

        // ทิศเท้าในโลก = มุมหมุนของเป้าเท้าเทียบท่าตั้งต้น
        private static float FootYaw(IKLimb leg) => (leg.target.rotation * Quaternion.Inverse(leg.RestRotation)).eulerAngles.y;

        // หุ่นและ collider มือ/เท้าต้องอยู่ layer Ignore Raycast ไม่งั้นยิงโดนตัวเองแล้วตัวลอยขึ้นเรื่อยๆ
        private static float GroundY(Vector3 p) =>
            Physics.Raycast(p + Vector3.up * 100f, Vector3.down, out RaycastHit hit, 300f) ? hit.point.y : 0f;

        // ================================================================ network

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Everyone)]
        private void SendLimbRpc(int index, LimbNetState s, RpcParams rpcParams = default)
        {
            if (index < 0 || index >= limbs.Length || ControllerOf(index) != rpcParams.Receive.SenderClientId) return;
            if (MatchSession.Current != null && !MatchSession.Current.IsPlaying) return;

            // Server ตรวจซ้ำ — ไม่เชื่อเป้าจาก client ตรงๆ
            IKLimb limb = limbs[index];
            Vector3 world = limb.isArm ? transform.TransformPoint(s.pos) : s.pos;
            // นอนอยู่: ไม่เกินความยาวแขนขาจากโคน | แขนตอนยืน: ข้อจำกัดข้อต่อ
            // ขา (หุ่นเดิม SetFootTargetRpc ServerReachMargin 1.5): ก้าวยาวเป้าอยู่ไกลกว่าขาจริงได้ ตัวไหลตามไปเอง —
            // ตัดที่ความยาวขาพอดีแล้วเท้าเครื่องอื่นลอยค้าง และ Server นับเป็นเท้ายก (กฎสองเท้ายก = ล้ม)
            float reach = down ? limb.Reach : limb.Reach * 1.5f;
            world = down || !limb.isArm ? limb.root.position + Vector3.ClampMagnitude(world - limb.root.position, reach)
                                        : limb.Limits.Limit(limb, transform.rotation, world);
            s.pos = limb.isArm ? transform.InverseTransformPoint(world) : world;
            s.grip = Mathf.Clamp01(s.grip);
            states[index] = s;
        }

        // ปุ่มทดสอบ B — RPC ต้องมีทุก build (ตาราง RPC ตรงกันทุกเครื่อง) แต่ build จริงไม่ทำอะไร กัน client แก้เกมสั่งล้มรัวๆ
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void KnockDownRpc(RpcParams rpcParams = default)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            for (int i = 0; i < limbs.Length; i++)
                if (ControllerOf(i) == rpcParams.Receive.SenderClientId)
                {
                    ServerKnockDown(FallReason.KnockedDown);
                    return;
                }
#endif
        }

        // ดึงได้: ชิ้นที่ตัวเองคุม หรือชิ้นที่ไม่มีใครคุม
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void PullRpc(int index, bool held, RpcParams rpcParams = default)
        {
            if (index < 0 || index >= limbs.Length) return;
            ulong controller = ControllerOf(index);
            if (controller != LimbSelection.NoClient && controller != rpcParams.Receive.SenderClientId) return;
            pullHeld[index] = held;
        }

        // ปุ่มทดสอบ X — RPC ต้องมีทุก build (ตาราง RPC ตรงกันทุกเครื่อง) แต่ build จริงไม่ทำอะไร
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void DetachTestRpc(int index, RpcParams rpcParams = default)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (index >= 0 && index < limbs.Length && ControllerOf(index) == rpcParams.Receive.SenderClientId && attachments[index])
                attachments[index].ServerDetach();
#endif
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void StandVoteRpc(RpcParams rpcParams = default)
        {
            if (down) standVoters.Add(rpcParams.Receive.SenderClientId); // นับเฉพาะคนที่คุมชิ้นอยู่ (FixedDown กรอง)
        }

        // ================================================================ UI

        private void OnGUI()
        {
            GUI.skin.label.fontSize = 18;
            if (Online && (MatchSession.Current == null || !MatchSession.Current.IsPlaying)) return; // หน้าเลือกชิ้นส่วนแสดงอยู่

            GUILayout.BeginArea(new Rect(10, 10, 900, 200), GUI.skin.box);
            if (!Online) GUILayout.Label($"เล่นคนเดียว: {limbs[selected].name} (กด 1-4 สลับ)");
            else
            {
                string mine = "ไม่ได้คุมชิ้นไหน";
                for (int i = 0; i < limbs.Length; i++)
                    if (IsMine(i)) mine = limbs[i].name;
                GUILayout.Label($"คุณคุม: {mine}");
            }
            if (robot)
            {
                var hp = new System.Text.StringBuilder("เลือด: ");
                for (int i = 0; i < limbs.Length; i++)
                {
                    LimbHealth health = robot.GetLimb((LimbSlot)i)?.Health;
                    if (health) hp.Append($"{limbs[i].name} {Mathf.CeilToInt(health.Hp)}/{Mathf.CeilToInt(health.MaxHp)}{(Attached(i) ? "" : " (หลุด)")}   ");
                }
                if (hp.Length > "เลือด: ".Length) GUILayout.Label(hp.ToString());
            }
            for (int i = 0; i < limbs.Length; i++)
                if (IsMine(i) && !Attached(i))
                {
                    float wait = attachments[i] ? attachments[i].RemainingReconnectCooldown : 0f;
                    GUILayout.Label(wait > 0f ? $"{limbs[i].name} หลุด! รอ {wait:F1} วิ แล้วกด R ค้างดึงกลับ" : $"{limbs[i].name} หลุด! กด R ค้างดึงกลับ");
                }
            if (down && !LegsAttached()) GUILayout.Label("ขาหลุดอยู่ — ดึงขากลับ (R ค้าง) ก่อนถึงจะลุกได้");
            if (down)
            {
                int need = Online ? Controllers(out _) : 1;
                GUILayout.Label(!Online ? "หุ่นล้ม! คลิกค้าง = ดึงแขน/ขาไปที่เคอร์เซอร์ | กด Q เพื่อลุก"
                    : votedStand ? $"หุ่นล้ม! คลิกค้าง = ดึงแขน/ขา | รอเพื่อนกด Q ({standVotes.Value}/{need})"
                    : $"หุ่นล้ม! คลิกค้าง = ดึงแขน/ขาไปที่เคอร์เซอร์ | ทุกคนกด Q เพื่อลุก ({standVotes.Value}/{need})");
            }
            string armHelp = combat.pointAndClick
                ? "แขน: เคอร์เซอร์เล็ง, คลิกซ้ายค้าง = ชาร์จ ปล่อย = ต่อย"
                : "แขน: มือตามเคอร์เซอร์, Shift+ล้อ = ดัน/ดึงมือ, คลิกซ้ายค้าง = กำ";
            GUILayout.Label($"{armHelp} | ขา: ชี้พื้น + คลิก = ก้าวไปตรงนั้น (ค้าง = เท้าลอยตามเคอร์เซอร์ ปล่อย = วาง, ก้าวเฉียง = เลี้ยว, Shift = เตะสูงไปทาง ◈) | ล้อ = ซูม | คลิกขวาค้าง = หันกล้อง | R ค้าง = ดึงชิ้นที่หลุดกลับ | B = ทดสอบล้ม | X = ทดสอบหลุด");
            GUILayout.EndArea();

            if (aimArm >= 0 && cam) DrawReticle();
            if (!down && cam) DrawFootMarker();
        }

        private GUIStyle reticleStyle, markerStyle, reachStyle;

        // หุ่นเดิม (LegInput.OnGUI): ◈ ที่จุดที่เท้าจะลง (ตัดที่ระยะเอื้อมแล้ว อาจต่างจากเคอร์เซอร์) + ระยะเป็น % ของที่ก้าวได้ไกลสุด
        private void DrawFootMarker()
        {
            int i = 0;
            while (i < limbs.Length && (limbs[i].isArm || !IsMine(i))) i++;
            if (i == limbs.Length) return;
            Vector3 mark = limbs[i].Lifted ? stepAim[i] : footMark[i];
            Vector3 screen = cam.WorldToScreenPoint(new Vector3(mark.x, GroundY(mark), mark.z));
            if (screen.z <= 0f) return;

            markerStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            reachStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            float x = screen.x, y = Screen.height - screen.y;
            Vector3 hip = limbs[i].root.position;
            float reach = new Vector2(mark.x - hip.x, mark.z - hip.z).magnitude / (limbs[i].Reach * 0.9f);
            string pct = $"{Mathf.RoundToInt(Mathf.Clamp01(reach) * 100f)}%";

            Color old = GUI.color, shadow = new Color(0f, 0f, 0f, 0.6f), tint = new Color(0.5f, 0.85f, 1f);
            GUI.color = shadow;
            GUI.Label(new Rect(x - 13f, y - 13f, 30f, 30f), "◈", markerStyle);
            GUI.Label(new Rect(x - 29f, y + 13f, 60f, 20f), pct, reachStyle);
            GUI.color = tint;
            GUI.Label(new Rect(x - 14f, y - 14f, 30f, 30f), "◈", markerStyle);
            GUI.Label(new Rect(x - 30f, y + 12f, 60f, 20f), pct, reachStyle);
            GUI.color = old;
        }

        // ◎ ที่จุดเล็ง: แดง = เล็งโดนของที่ต่อยได้, ส้ม = ของที่เล็งอยู่ไกลเกินแขน, ขาว = ปกติ + แถบชาร์จใต้เป้า
        private void DrawReticle()
        {
            Vector3 sp = cam.WorldToScreenPoint(aimPoint);
            if (sp.z <= 0f) return;
            IKCombat.Arm a = arms[aimArm];

            reticleStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 40, alignment = TextAnchor.MiddleCenter };
            reticleStyle.normal.textColor = !aimInReach ? new Color(1f, 0.55f, 0f) : aimOnTarget ? Color.red : Color.white;
            var r = new Rect(sp.x - 30f, Screen.height - sp.y - 30f, 60f, 60f);
            GUI.Label(r, "◎", reticleStyle);

            if (a.state != IKCombat.State.Charging) return;
            float c = combat.Charge01(a);
            GUI.Box(new Rect(r.x, r.yMax, 60f, 10f), GUIContent.none);
            Color old = GUI.color;
            GUI.color = c >= 1f ? Color.red : Color.yellow;
            GUI.DrawTexture(new Rect(r.x + 1f, r.yMax + 1f, 58f * c, 8f), Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
