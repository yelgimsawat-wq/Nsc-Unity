using Nsc.Feel;
using Nsc.Match;
using Nsc.Robots;
using Unity.Netcode;
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
    /// ขา: คลิกซ้ายค้าง = ยกเท้าไปที่เคอร์เซอร์ ปล่อย = วางลงพื้น | ล้อ = ซูม | คลิกขวาค้าง = หันกล้อง | Q/E หมุนตัว
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
        [Tooltip("ยกเท้าสูงจากพื้นเท่าไรตอนกดค้าง")]
        [SerializeField] private float footLift = 2f;
        [Tooltip("มือ/เท้าไหลตามเคอร์เซอร์เร็วแค่ไหน (มาก = ติดเมาส์, น้อย = นุ่ม)")]
        [SerializeField] private float follow = 12f;
        [SerializeField] private float gripSpeed = 8f;
        [SerializeField] private float turnSpeed = 60f;

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

        // ขา (เฉพาะเครื่องที่คุมขา): จุด/ทิศเท้าตอนเริ่มยก, การกระทืบลงหลังปล่อย (dropT < 0 = ไม่ได้กระทืบ)
        private Vector3[] liftFrom, dropFrom, dropTo;
        private float[] liftYaw, dropT;
        private bool[] airborne;              // เท้ายกอยู่ (ทุกเครื่องดูจากเป้า) — ใช้จับจังหวะเท้าลง
        private Vector3[] planted;            // ที่เท้าปักอยู่ล่าสุด — ตัวหุ่นไหลตามเท้าที่ปักเท่านั้น (เท้าลอยไม่ดึงตัว)
        private Vector3 lastBodyPos, leanVel;
        private Vector2 lean;
        private Vector3 bodyVel;
        private static readonly Collider[] hitBuffer = new Collider[8];

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
            dropFrom = new Vector3[limbs.Length];
            dropTo = new Vector3[limbs.Length];
            liftYaw = new float[limbs.Length];
            dropT = new float[limbs.Length];
            for (int i = 0; i < limbs.Length; i++) dropT[i] = -1f;
            airborne = new bool[limbs.Length];
            planted = new Vector3[limbs.Length];
            for (int i = 0; i < limbs.Length; i++) planted[i] = limbs[i].target.position;
            lastBodyPos = transform.position;
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                if (t.name.EndsWith("Spine1")) { spine = t; spineRest = t.localRotation; break; }

            // IK บานพับของเราเองแทน TwoBoneIK (ตัวนั้นเลือกระนาบพับเอง ทำให้ศอก/เข่าพับผิดแกนจนทะลุโมเดล)
            var rigBuilder = model.GetComponent<UnityEngine.Animations.Rigging.RigBuilder>();
            if (rigBuilder) rigBuilder.enabled = false;
        }

        public override void OnNetworkSpawn() => EnsureStates();

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
            bool canAct = !Online || MatchSession.Current == null || MatchSession.Current.IsPlaying;
            if (input.pressedSlot >= 0)
            {
                if (!Online) selected = input.pressedSlot;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                // ออนไลน์: ขอสลับไปคุมชิ้นนั้น (ว่างอยู่เท่านั้น) — ตัวสลับของหุ่นเดิม (LimbControlBinder) ใช้ไม่ได้เพราะหุ่นนี้ไม่มีตัวควบคุมฟิสิกส์ให้ผูก
                else if (LimbSelection.Current != null && canAct) LimbSelection.Current.DebugCycleLimbRpc(input.pressedSlot);
#endif
            }

            if (input.looking) orbit.Look(input.lookDelta);
            else orbit.Follow(transform.eulerAngles.y, Time.deltaTime);
            if (!input.shift) orbit.Zoom(input.scroll);              // ล้อเมาส์ = ซูม | Shift + ล้อ = ดัน/ดึงมือ
            float push = input.shift ? input.scroll * depthSpeed : 0f;

            bool controlsAny = false;
            aimArm = -1;
            float remoteT = 1f - Mathf.Exp(-remoteSmoothing * Time.deltaTime);
            for (int i = 0; i < limbs.Length; i++)
            {
                IKLimb limb = limbs[i];
                if (IsMine(i) && canAct)
                {
                    controlsAny = true;
                    if (limb.isArm && combat.pointAndClick && cam) DriveCombatArm(i, input); // หมัดต้องจบท่าแม้กำลังหันกล้อง
                    else if (!input.looking && cam)
                    {
                        if (limb.isArm) DriveArm(limb, input.ray, push, input.held);
                        else DriveLeg(i, input.ray, input.held);
                    }
                    if (!limb.isArm) Stomp(i); // กระทืบต้องจบแม้กำลังหันกล้อง
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
                    dropT[i] = -1f;
                    if (limb.isArm && !Online) limb.Grip = Mathf.MoveTowards(limb.Grip, 0f, gripSpeed * Time.deltaTime);
                    if (Online) states[i].ApplyTo(limb, remoteT);
                }
                if (limb.isArm && combat.Enabled) // กำปั้นตรงแนวแขน ฝ่ามือหันหน้า-เข้าใน (ตั้งการ์ด) / คว่ำลง (ตอนเหยียดหมัด)
                    limb.OrientHand(transform.rotation, limb.tip.position - limb.mid.position,
                                    transform.rotation * new Vector3(-limb.Side, -0.4f, 0f)); // ฝ่ามือหันเข้าหากัน สันหมัดชี้หน้า
                else if (limb.isArm) limb.OrientHand(transform.rotation);
            }

            float turn = input.turn * turnSpeed * Time.deltaTime;
            if (turn != 0f && feel.footTurn)
            {
                // บิดเท้าที่คุมอยู่ (ปักอยู่กับพื้น) — ตัวหันตามเท้าเองใน UpdateBody, คุมแขนหมุนตัวไม่ได้
                for (int i = 0; i < limbs.Length; i++)
                    if (!limbs[i].isArm && IsMine(i) && canAct && !limbs[i].Lifted && dropT[i] < 0f)
                        limbs[i].target.rotation = Quaternion.Euler(0f, FootYaw(limbs[i]) + turn, 0f) * limbs[i].RestRotation;
            }
            else if (turn != 0f && controlsAny)
            {
                if (Online) TurnRpc(turn);
                else Turn(turn);
            }

            DetectLandings();
            if (DrivesBody) UpdateBody();
        }

        private void FixedUpdate()
        {
            if (DrivesBody)
            {
                body.isKinematic = true; // Robot.SetSimulationEnabled ปลด kinematic ตอนเริ่มแมตช์ — ตัวนี้เดินด้วยสคริปต์
                body.MovePosition(bodyPos);
                body.MoveRotation(bodyRot);
            }
            foreach (IKLimb limb in limbs)
                if (limb.hit) limb.hit.MovePosition(limb.tip.position);
        }

        private void LateUpdate()
        {
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
            foreach (IKLimb limb in limbs)
            {
                limb.Solve(transform.rotation);
                limb.ApplyFingers();
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

        // คลิกค้าง = ยกเท้า แล้วเท้าตามจุดที่เคอร์เซอร์ชี้บนพื้น | ปล่อย = วางลงพื้นตรงนั้น (stompTime > 0 = กระทืบลง)
        private void DriveLeg(int i, Ray ray, bool held)
        {
            IKLimb leg = limbs[i];
            if (held && dropT[i] < 0f)
            {
                if (!leg.Lifted) { liftFrom[i] = leg.target.position; liftYaw[i] = FootYaw(leg); }
                float height = GroundY(leg.target.position) + leg.RestHeight + footLift;
                var plane = new Plane(Vector3.up, new Vector3(0f, height, 0f));
                if (plane.Raycast(ray, out float enter))
                {
                    Vector3 goal = leg.Limits.Limit(leg, transform.rotation, ray.GetPoint(enter));
                    leg.target.position = Vector3.Lerp(leg.target.position, goal, 1f - Mathf.Exp(-follow * Time.deltaTime));
                }
                if (feel.footTurn) leg.target.rotation = Quaternion.Euler(0f, StepYaw(i, leg), 0f) * leg.RestRotation;
                leg.Lifted = true;
            }
            else if (leg.Lifted)
            {
                Vector3 p = leg.target.position;
                p.y = GroundY(p) + leg.RestHeight;
                Vector3 land = leg.Limits.Limit(leg, transform.rotation, p);
                land.y = GroundY(land) + leg.RestHeight; // ไกลเกินขา Limit ดึงเป้าลอยขึ้น → วางบนพื้นเสมอ แล้วตัวไหลตามเท้าเอง
                Quaternion rot = feel.footTurn ? leg.target.rotation : transform.rotation * leg.RestRotation;
                if (feel.stompTime > 0f) { dropFrom[i] = leg.target.position; dropTo[i] = land; dropT[i] = 0f; leg.target.rotation = rot; }
                else leg.target.SetPositionAndRotation(land, rot);
                leg.Lifted = false;
            }
        }

        // หุ่นเดิม: ยกเท้าไปเฉียง/ข้าง → เท้าหันตามครึ่งหนึ่งของมุมก้าว (ไม่เกิน maxTurnPerStep) | ก้าวสั้น/ถอยหลัง = ทิศเดิม
        private float StepYaw(int i, IKLimb leg)
        {
            Vector3 step = Vector3.ProjectOnPlane(leg.target.position - liftFrom[i], Vector3.up);
            float angle = Vector3.SignedAngle(transform.forward, step, Vector3.up);
            if (step.magnitude <= leg.Reach * 0.2f || Mathf.Abs(angle) > 110f) return liftYaw[i];
            return liftYaw[i] + Mathf.Clamp(Mathf.DeltaAngle(liftYaw[i], transform.eulerAngles.y + angle) * 0.5f, -feel.maxTurnPerStep, feel.maxTurnPerStep);
        }

        // ปล่อยเท้าแล้วกระทืบลงแบบเร่ง (ไม่ลอยลงนุ่มๆ) — จบแล้ว DetectLandings เล่นเสียง/ฝุ่น/จอสั่น
        private void Stomp(int i)
        {
            if (dropT[i] < 0f) return;
            dropT[i] = Mathf.Min(1f, dropT[i] + Time.deltaTime / feel.stompTime);
            limbs[i].target.position = Vector3.Lerp(dropFrom[i], dropTo[i], dropT[i] * dropT[i]);
            if (dropT[i] >= 1f) dropT[i] = -1f;
        }

        // ================================================================ ตัวหุ่น

        // ตัวหุ่นไหลไปกลางระหว่างเท้า → ก้าวเท้าทีละข้าง = เดิน
        private void UpdateBody()
        {
            Vector3 mid = Vector3.zero;
            int legs = 0;
            for (int i = 0; i < limbs.Length; i++)
                if (!limbs[i].isArm) { mid += feel.bodyFrequency > 0f ? planted[i] : limbs[i].target.position; legs++; } // แบบเดิม = ตามเท้าแม้ลอย
            if (legs == 0) return;
            mid /= legs;

            Vector3 goal = new Vector3(mid.x, 0f, mid.z);
            goal.y = GroundY(goal) - crouch;
            if (feel.bodyFrequency > 0f) IKFeel.Spring(ref bodyPos, ref bodyVel, goal, feel.bodyFrequency, feel.bodyDamping, Time.deltaTime);
            else bodyPos = Vector3.Lerp(bodyPos, goal, 1f - Mathf.Exp(-bodyFollow * Time.deltaTime));

            if (!feel.footTurn) return;
            // ตัวหันตามค่าเฉลี่ยทิศเท้า (ทิศเท้าซิงก์มากับเป้าเท้าทุกเครื่อง) — ช้าๆ แบบตัวหนัก
            Vector3 facing = Vector3.zero;
            for (int i = 0; i < limbs.Length; i++)
                if (!limbs[i].isArm) facing += Quaternion.Euler(0f, FootYaw(limbs[i]), 0f) * Vector3.forward;
            if (facing.sqrMagnitude > 1e-4f)
                bodyRot = Quaternion.Slerp(bodyRot, Quaternion.LookRotation(facing), 1f - Mathf.Exp(-feel.bodyTurnSpeed * Time.deltaTime));
        }

        // ทิศเท้าในโลก = มุมหมุนของเป้าเท้าเทียบท่าตั้งต้น
        private static float FootYaw(IKLimb leg) => (leg.target.rotation * Quaternion.Inverse(leg.RestRotation)).eulerAngles.y;

        private void Turn(float degrees) => bodyRot = Quaternion.Euler(0f, degrees, 0f) * bodyRot;

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
            world = limb.Limits.Limit(limb, transform.rotation, world);
            s.pos = limb.isArm ? transform.InverseTransformPoint(world) : world;
            s.grip = Mathf.Clamp01(s.grip);
            states[index] = s;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void TurnRpc(float degrees, RpcParams rpcParams = default)
        {
            for (int i = 0; i < limbs.Length; i++)
                if (ControllerOf(i) == rpcParams.Receive.SenderClientId)
                {
                    Turn(Mathf.Clamp(degrees, -turnSpeed, turnSpeed));
                    return;
                }
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
            string armHelp = combat.pointAndClick
                ? "แขน: เคอร์เซอร์เล็ง, คลิกซ้ายค้าง = ชาร์จ ปล่อย = ต่อย"
                : "แขน: มือตามเคอร์เซอร์, Shift+ล้อ = ดัน/ดึงมือ, คลิกซ้ายค้าง = กำ";
            GUILayout.Label($"{armHelp} | ขา: คลิกซ้ายค้าง = ยกเท้าไปที่เคอร์เซอร์ ปล่อย = วาง | ล้อ = ซูม | คลิกขวาค้าง = หันกล้อง | Q/E หมุนตัว");
            GUILayout.EndArea();

            if (aimArm >= 0 && cam) DrawReticle();
        }

        private GUIStyle reticleStyle;

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
