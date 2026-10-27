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
            foreach (IKLimb limb in limbs)
                limb.Init(transform, ground, limb.isArm ? shoulder : hip, model);

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

        // ================================================================ ต่อเฟรม

        private void Update()
        {
            if (!IKInput.TryRead(cam, limbs.Length, out IKInput input)) return;
            if (Online) EnsureStates();
            if (Online && states.Count < limbs.Length) return; // client: ยังไม่ได้ซิงก์รอบแรก

            // ออนไลน์: ขยับได้เมื่อแมตช์เริ่ม (Host กด Start ในหน้าเลือกชิ้นส่วน)
            bool canAct = !Online || MatchSession.Current == null || MatchSession.Current.IsPlaying;
            if (!Online && input.pressedSlot >= 0) selected = input.pressedSlot;

            if (input.looking) orbit.Look(input.lookDelta);
            if (!input.shift) orbit.Zoom(input.scroll);              // ล้อเมาส์ = ซูม | Shift + ล้อ = ดัน/ดึงมือ
            float push = input.shift ? input.scroll * depthSpeed : 0f;

            bool controlsAny = false;
            float remoteT = 1f - Mathf.Exp(-remoteSmoothing * Time.deltaTime);
            for (int i = 0; i < limbs.Length; i++)
            {
                IKLimb limb = limbs[i];
                if (IsMine(i) && canAct)
                {
                    controlsAny = true;
                    if (!input.looking && cam)
                    {
                        if (limb.isArm) DriveArm(limb, input.ray, push, input.held);
                        else DriveLeg(limb, input.ray, input.held);
                    }
                    if (Online) SendLimbRpc(i, LimbNetState.Capture(limb));
                }
                else
                {
                    if (limb.isArm && !Online) limb.Grip = Mathf.MoveTowards(limb.Grip, 0f, gripSpeed * Time.deltaTime);
                    if (Online) states[i].ApplyTo(limb, remoteT);
                }
                if (limb.isArm) limb.OrientHand(transform.rotation);
            }

            float turn = input.turn * turnSpeed * Time.deltaTime;
            if (turn != 0f && controlsAny)
            {
                if (Online) TurnRpc(turn);
                else Turn(turn);
            }

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
            foreach (IKLimb limb in limbs)
            {
                limb.Solve(transform.rotation);
                limb.ApplyFingers();
            }
            if (cam) orbit.Apply(cam, transform.position);
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

        // คลิกค้าง = ยกเท้า แล้วเท้าตามจุดที่เคอร์เซอร์ชี้บนพื้น | ปล่อย = วางลงพื้นตรงนั้น
        private void DriveLeg(IKLimb leg, Ray ray, bool held)
        {
            if (held)
            {
                float height = GroundY(leg.target.position) + leg.RestHeight + footLift;
                var plane = new Plane(Vector3.up, new Vector3(0f, height, 0f));
                if (plane.Raycast(ray, out float enter))
                {
                    Vector3 goal = leg.Limits.Limit(leg, transform.rotation, ray.GetPoint(enter));
                    leg.target.position = Vector3.Lerp(leg.target.position, goal, 1f - Mathf.Exp(-follow * Time.deltaTime));
                }
                leg.Lifted = true;
            }
            else if (leg.Lifted)
            {
                Vector3 p = leg.target.position;
                p.y = GroundY(p) + leg.RestHeight;
                leg.target.SetPositionAndRotation(leg.Limits.Limit(leg, transform.rotation, p), transform.rotation * leg.RestRotation);
                leg.Lifted = false;
            }
        }

        // ================================================================ ตัวหุ่น

        // ตัวหุ่นไหลไปกลางระหว่างเท้า → ก้าวเท้าทีละข้าง = เดิน
        private void UpdateBody()
        {
            Vector3 mid = Vector3.zero;
            int legs = 0;
            foreach (IKLimb limb in limbs)
                if (!limb.isArm) { mid += limb.target.position; legs++; }
            if (legs == 0) return;
            mid /= legs;

            Vector3 goal = new Vector3(mid.x, 0f, mid.z);
            goal.y = GroundY(goal) - crouch;
            bodyPos = Vector3.Lerp(bodyPos, goal, 1f - Mathf.Exp(-bodyFollow * Time.deltaTime));
        }

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
            GUILayout.Label("แขน: มือตามเคอร์เซอร์, Shift+ล้อ = ดัน/ดึงมือ, คลิกซ้ายค้าง = กำ | ขา: คลิกซ้ายค้าง = ยกเท้าไปที่เคอร์เซอร์ ปล่อย = วาง | ล้อ = ซูม | คลิกขวาค้าง = หันกล้อง | Q/E หมุนตัว");
            GUILayout.EndArea();
        }
    }
}
