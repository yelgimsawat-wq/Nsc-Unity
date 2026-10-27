using System.Collections.Generic;
using Nsc.Combat;
using Nsc.Robots;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

namespace Nsc.Limbs
{
    /// <summary>
    /// ตัวขับฟิสิกส์ของแขนฝั่ง server: สปริงพามือไปหาเป้าที่เจ้าของส่งมา ดึงลำตัวเมื่อแขนยืดสุด
    /// ปีนเมื่อมือจับของนิ่ง และให้ PunchSkill ปรับท่าแขนระหว่างต่อย
    ///
    /// เดิมคือ PlayerHandMovement + PlayerHandCombat (ใช้ GUID ของ PlayerHandCombat ที่อยู่บน prefab)
    /// input ของเจ้าของย้ายไป ArmInput / การจับของย้ายไป ArmGrip / ค่าจูนหมัดอยู่ใน punch
    /// </summary>
    [RequireComponent(typeof(ArmGrip), typeof(LimbStrike))]
    public class ArmController : LimbController
    {
        // จุดอ้างอิงของ server ตอนตรวจเป้าที่ client ส่งมา — ยอมให้เกินระยะแขนได้ 50%
        private const float ServerReachMargin = 1.5f;
        // ท่าพัก = มือห้อยใต้ไหล่ (สัดส่วนความยาวแขน) — ไม่ใช้ท่าตอน spawn เพราะโมเดลวางท่า T-pose
        private const float RestHangRatio = 0.85f;

        [Header("Offsets (ปรับจุดศูนย์กลางได้อิสระ)")]
        [SerializeField] private Vector3 pivotOffset = Vector3.zero;

        [Header("Movement")]
        [SerializeField] private float maxArmLength = 1.8f;
        [SerializeField] private float handMoveSpeed = 25f;
        [SerializeField] private float handDamper = 15f;
        [Tooltip("เวลาที่มือใช้ไล่ถึงเป้า (SmoothDamp) — สั้น = ไว/คม แนะนำ 0.03–0.05")]
        [SerializeField] private float smoothTime = 0.04f;
        [Tooltip("ระยะจากเป้าที่เริ่มเบรก กันพุ่งเลยเป้าแล้วดีดกลับ")]
        [SerializeField] private float brakeDistance = 0.5f;
        [SerializeField] private float brakeDamping = 8f;
        [Tooltip("ชดเชยน้ำหนักมือ — สปริงไม่ต้องแบกน้ำหนักค้างไว้ ยืดถึงเป้าจริง")]
        [SerializeField] private bool compensateGravity = true;
        [Tooltip("เพดานความเร็วเป้าของสปริง — กันโซ่ข้อต่อแขนสั่น/ระเบิดตอนเป้าอยู่ไกล")]
        [SerializeField] private float maxHandVelocity = 40f;
        [Tooltip("สัดส่วนแรงสปริงมือตอนล้ม — ต่ำ = มือห้อยตามแรงโน้มถ่วงแต่ยังขยับตามเมาส์ได้เบาๆ")]
        [Range(0f, 1f)] [SerializeField] private float ragdollHandSpringScale = 0.25f;
        [Tooltip("ปิดการชนระหว่างมือกับชิ้นส่วนหุ่นตัวเอง — ตัดอาการสั่นจากมือครูดลำตัว")]
        [SerializeField] private bool ignoreSelfCollision = true;

        [Header("Torso Pull (แขนยืดสุดแล้วดึงตัว)")]
        [SerializeField] private float torsoPullForce = 60f;
        [Tooltip("สัดส่วนแรงแนวราบที่ส่งไปลำตัว — 0.15–0.25 = รู้สึกว่ามือดึงบ้างแต่โกงเดินไม่ได้")]
        [Range(0f, 1f)] [SerializeField] private float torsoPullHorizontalScale = 0.2f;
        [Tooltip("ตัววิ่งเร็วกว่านี้ในทิศเดียวกับแรงดึงแล้ว แรงแนวราบ = 0 กันสะสม momentum")]
        [SerializeField] private float maxAllowedHorizontalBoost = 3f;

        [Header("Climb (จับของนิ่ง)")]
        [Tooltip("แรงดึงตัวเมื่อจับ Kinematic Object (ปีนป่าย)")]
        [SerializeField] private float kinematicPullForce = 150f;
        [Tooltip("ปิด = ดึงกำแพงนิ่งแล้วไม่เพิ่ม stress ของลำตัว")]
        [SerializeField] private bool kinematicGrabAddsStress = false;
        [Tooltip("ตอนจับปีน ปรับมวลท่อนแขนให้เท่ากันและเพิ่มรอบ solver — โซ่แขนนิ่งขึ้น")]
        [SerializeField] private bool stabilizeArmChainWhileGrabbing = true;
        [Min(0.01f)] [SerializeField] private float stabilizedArmMass = 1f;
        [Min(1)] [SerializeField] private int stabilizedArmSolverIterations = 20;
        [Min(1)] [SerializeField] private int stabilizedArmSolverVelocityIterations = 8;

        [Header("Combat Guard (โหมดต่อสู้)")]
        [Tooltip("แขนที่ไม่มีใครคุมยกการ์ดหันไปทางหน้าหุ่น แทนห้อยข้างตัว — เปิดคู่กับ ArmInput.pointAndClick")]
        [SerializeField] private bool idleGuard = false;
        [Tooltip("ท่าการ์ด: มือยื่นไปทางที่เล็งกี่ส่วนของความยาวแขน")]
        [Range(0.1f, 1f)] [SerializeField] private float guardReach = 0.45f;
        [Tooltip("ท่าการ์ด: ยกมือขึ้นจากระดับไหล่กี่ส่วนของความยาวแขน (หมัดอยู่หน้าคาง)")]
        [Range(0f, 1f)] [SerializeField] private float guardRaise = 0.2f;
        [Tooltip("ท่าการ์ด: หุบมือเข้ากลางอกกี่ส่วนของความยาวแขน")]
        [Range(0f, 1f)] [SerializeField] private float guardInward = 0.2f;
        [Tooltip("โหมดต่อสู้: แรงบิดขับต้นแขน/แขนท่อนล่างไปตามท่า IK สองท่อน (ศอกลงข้างตัว) ทุกจังหวะ ทั้งการ์ด ง้าง และพุ่ง\n" +
                 "ไม่มีตัวนี้ = ดึงแค่มือ ไหล่หมุนอิสระ แขนค้างได้หลายท่า (มืออยู่หลังตัว/ศอกเหยียดค้าง) (0 = ปิด)\n" +
                 "ห้ามล็อกมือเป็น kinematic แทน: ศอกเป็นบานพับงอได้ระนาบเดียว ท่าที่บังคับนอกระนาบกระชากหุ่นล้ม (ลองแล้ว 2026-10-27)")]
        [Min(0f)] [SerializeField] private float armPoseSpring = 300f;
        [Tooltip("หน่วงแรงบิดท่าแขน — × fixedDeltaTime ต้องต่ำกว่า 1 ไม่งั้นแขนสั่นกลับไปกลับมา")]
        [Min(0f)] [SerializeField] private float armPoseDamper = 30f;
        [Tooltip("ทิศศอก: กางออกจากลำตัวกี่ส่วน (ต่อทิศลง 1)")]
        [Range(0f, 1f)] [SerializeField] private float guardElbowOut = 0.35f;

        [Header("Body Drive (ลำตัวส่งแรงหมัด — โหมดชาร์จ)")]
        [Tooltip("ตอนง้าง: บิดลำตัวเอาไหล่ข้างที่ต่อยถอยหลัง (rad/s² ที่ชาร์จเต็ม — สู้กับสปริงหันตามเท้าของ TorsoBalance)")]
        [Min(0f)] [SerializeField] private float windupTwist = 60f;
        [Tooltip("ตอนปล่อยหมัด: บิดลำตัวส่งไหล่ไปข้างหน้า (rad/s ที่ชาร์จเต็ม)")]
        [Min(0f)] [SerializeField] private float punchTwist = 0.8f;
        [Tooltip("ตอนปล่อยหมัด: ลำตัวพุ่งตามหมัด (m/s ที่ชาร์จเต็ม) — ขาที่ปักรั้งกลับเอง")]
        [Min(0f)] [SerializeField] private float punchLunge = 3f;

        [Header("Punch")]
        [SerializeField] private PunchSkill punch = new PunchSkill();

        [Header("Punch Gizmos")]
        [SerializeField] private bool showAimAssistGizmos = true;
        [SerializeField] private Color aimRangeColor = new Color(1f, 0.75f, 0f, 0.35f);
        [SerializeField] private Color rawAimColor = Color.cyan;
        [SerializeField] private Color assistedAimColor = Color.green;
        [Range(4, 32)] [SerializeField] private int aimConeSegments = 16;

        [FormerlySerializedAs("currentCombatState")]
        [SerializeField]
        private NetworkVariable<PunchState> punchState = new NetworkVariable<PunchState>(
            PunchState.Idle, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // ฟิสิกส์หมัดรันบน server — client อ่าน linearVelocity ตรงๆ จะได้ ~0 ตลอด จึง sync ค่าไว้โชว์ UI
        private readonly NetworkVariable<float> netNormalizedPunchForce = new NetworkVariable<float>(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private ArmGrip grip;
        private ArmInput armInput;
        // ท่อนแขนล่าง + ข้อศอก (anchor ของ HingeJoint = จุดศอก) — หาจากข้อต่อของมือตอน Awake
        private Rigidbody lowerArm;
        private HingeJoint elbowJoint;
        private Joint shoulderJoint; // ข้อต่อต้นแขน→ลำตัว: anchor = จุดหมุนไหล่จริง
        // เป้ามือเก็บเป็นระยะจากหัวไหล่ (แกนโลก) — ตัวเดินไปไหนมือไปด้วย ไม่ค้างจุดเดิมในโลกแล้วดึงแขนจนกาง
        private Vector3 targetHandOffset;
        private Vector3 smoothedHandOffset;
        private Vector3 smoothVelocity;
        private CollisionDetectionMode originalCollisionMode;
        private PunchState appliedPunchState;

        private readonly Dictionary<Joint, Vector2> protectedJointLimits = new Dictionary<Joint, Vector2>();
        private readonly Dictionary<Rigidbody, float> originalChainMasses = new Dictionary<Rigidbody, float>();
        private bool chainProtected;
        private bool chainStabilized;

        public ArmGrip Grip => grip;
        public float MaxArmLength => maxArmLength;
        /// <summary>ระยะที่หมัดเอื้อมถึงจากหัวไหล่ (แขน + ระยะยืดตอนต่อย) — HUD ใช้บอกว่าเป้าอยู่ในระยะไหม</summary>
        public float PunchReach => maxArmLength + punch.extraReach;
        /// <summary>เวลาง้างถึงชาร์จเต็ม (windup + charge) — client ใช้นับเองตอนโชว์แถบชาร์จ</summary>
        public float FullChargeTime => punch.windupTime + punch.chargeTime;
        public bool ChargeMode => punch.chargeToRelease;
        public Vector3 PivotPosition => pivot != null ? pivot.TransformPoint(pivotOffset) : transform.position;

        /// <summary>แรงดึงลำตัวตอนปีน 0..1 — ลำตัวอ่านไปลดแรงดึงเข้ากลาง (0 = ไม่ได้ปีน)</summary>
        public float ClimbPull { get; private set; }

        public PunchState CurrentPunchState => punchState.Value;
        public bool IsPunching => punchState.Value == PunchState.Punching;

        /// <summary>แรงหมัด 0..1 สำหรับ UI — server คำนวณสด / client ใช้ค่าที่ sync มา</summary>
        public float NormalizedPunchForce
        {
            get
            {
                if (punchState.Value != PunchState.Punching) return 0f;
                if (IsSpawned && !IsServer) return netNormalizedPunchForce.Value;
                return body != null ? Mathf.Clamp01(body.linearVelocity.magnitude / Mathf.Max(0.01f, punch.maxSpeed)) : 0f;
            }
        }

        protected override void Awake()
        {
            base.Awake();
            grip = GetComponent<ArmGrip>();
            armInput = GetComponent<ArmInput>();
            lowerArm = GetComponent<Joint>() != null ? GetComponent<Joint>().connectedBody : null;
            elbowJoint = lowerArm != null ? lowerArm.GetComponent<HingeJoint>() : null;
            shoulderJoint = elbowJoint != null && elbowJoint.connectedBody != null ? elbowJoint.connectedBody.GetComponent<Joint>() : null;

            LimbStrike strike = GetComponent<LimbStrike>();
            punch.Init(strike, name);
            if (strike != null) strike.SetSource(punch);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // ✅ [Rest Pose] แขนที่ไม่มีใครคุม (เทสคนเดียว) ห้อยข้างลำตัว — ไม่ค้างท่า T-pose ของโมเดล
            ResetHandTargetToRest();

            if (!IsServer || body == null) return;

            punch.SetOwner(limb.Owner);
            originalCollisionMode = body.collisionDetectionMode;

            // ✅ [Solver Boost] โซ่ข้อต่อแขนแก้สมการยากกว่า rigidbody เดี่ยว — นิ่งขึ้นตอนต่อย/เหวี่ยงเร็ว
            body.solverIterations = 12;
            body.solverVelocityIterations = 4;

            // ✅ [No Self-Collision] มือครูดลำตัว/ขา/มืออีกข้าง = แหล่งแรงสั่นและหมัดสะดุดที่ใหญ่ที่สุด
            // โหมดต่อสู้: ต้นแขน/แขนท่อนล่างด้วย — ไม่งั้นต่อยข้ามตัวแล้วท่อนแขนติดอก หมัดเบี่ยง (วัดได้ 23°)
            if (ignoreSelfCollision)
            {
                var chainColliders = new List<Collider>(body.GetComponentsInChildren<Collider>());
                if (armInput != null && armInput.IsCombatMode && lowerArm != null)
                {
                    chainColliders.AddRange(lowerArm.GetComponentsInChildren<Collider>());
                    if (elbowJoint != null && elbowJoint.connectedBody != null)
                        chainColliders.AddRange(elbowJoint.connectedBody.GetComponentsInChildren<Collider>());
                }
                List<Collider> handColliders = chainColliders;
                Collider[] bodyColliders = transform.root.GetComponentsInChildren<Collider>(true);
                foreach (Collider handCollider in handColliders)
                    foreach (Collider bodyCollider in bodyColliders)
                        if (handCollider != bodyCollider) Physics.IgnoreCollision(handCollider, bodyCollider, true);
            }
        }

        // ================================================================
        //  RPC จากเจ้าของ (ArmInput)
        // ================================================================

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void SetHandTargetRpc(Vector3 target)
        {
            // 🛡️ ห้ามเชื่อพิกัดจาก client — NaN พังทั้งโซ่ฟิสิกส์ / ไกลเกินถูกดึงกลับเข้าระยะแขนจริง
            if (!target.IsValid()) return;

            float limit = maxArmLength * ServerReachMargin;
            Vector3 fromPivot = target - PivotPosition;
            if (fromPivot.magnitude > limit) fromPivot = fromPivot.normalized * limit;

            targetHandOffset = fromPivot;
        }

        /// <summary>ท่าการ์ด (ระยะจากไหล่) หันไปทาง aimDirection — ยื่นไปข้างหน้า ยกขึ้น และหุบเข้ากลางอก</summary>
        public Vector3 GuardOffset(Vector3 aimDirection)
        {
            Vector3 inward = Torso != null && Torso.Body != null ? Torso.Body.position - PivotPosition : Vector3.zero;
            inward.y = 0f;
            if (inward.sqrMagnitude > 0.0001f) inward.Normalize();
            return (aimDirection.normalized * guardReach + Vector3.up * guardRaise + inward * guardInward) * maxArmLength;
        }

        /// <summary>
        /// [SERVER] ขับท่อนแขนตามท่า IK สองท่อน: ไหล่→เป้ามือ ศอกอยู่ทาง pole (ลงข้างตัว กางออกนิด เยื้องไปทางที่ต่อย)
        /// ต้นแขน: แรงบิดหมุนทั้งทิศท่อนและแนวบานพับศอกให้ระนาบงอตรงกับท่า (ศอกเป็นบานพับ งอนอกระนาบไม่ได้)
        /// แขนท่อนล่าง: แรงบิดชี้ไปเป้ามือ — มือยังใช้สปริงเดิมไล่เป้า ทั้งสามท่อนจึงไปทางเดียวกัน ไม่ติดท่าแปลก
        /// ความยาวท่อนวัดสดทุก tick และเป้าถูกตัดในระยะที่ถึงได้ ข้อต่อไม่ถูกดึงยืด
        /// </summary>
        private void DriveArmPose(Vector3 handTarget)
        {
            Rigidbody upperArm = elbowJoint.connectedBody;
            if (upperArm == null || armPoseSpring <= 0f) return;

            // จุดหมุนจริงของไหล่ = anchor ของข้อต่อต้นแขน — PivotPosition ถูกเลื่อนด้วย pivotOffset (จุดเล็ง) ไม่ใช่จุดหมุน
            // ใช้ PivotPosition แล้วความยาวต้นแขนที่วัดเพี้ยนไม่เท่ากันสองข้าง ท่า IK สู้กับข้อต่อจนหุ่นเซ (วัดได้ 2026-10-27)
            Vector3 shoulder = shoulderJoint != null ? upperArm.transform.TransformPoint(shoulderJoint.anchor) : PivotPosition;
            Vector3 elbow = lowerArm.transform.TransformPoint(elbowJoint.anchor);
            float upperLength = Vector3.Distance(shoulder, elbow);
            float foreLength = Vector3.Distance(elbow, body.position);
            Vector3 toTarget = handTarget - shoulder;
            if (upperLength < 0.01f || foreLength < 0.01f || toTarget.sqrMagnitude < 0.0001f) return;

            Vector3 reachDir = toTarget.normalized;
            float reach = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(upperLength - foreLength) + 0.01f, (upperLength + foreLength) * 0.999f);
            Vector3 outward = shoulder - Torso.Body.position;
            outward.y = 0f;
            if (outward.sqrMagnitude > 0.0001f) outward.Normalize();
            Vector3 forward = new Vector3(reachDir.x, 0f, reachDir.z);
            if (forward.sqrMagnitude > 0.0001f) forward.Normalize();

            Vector3 pole = Vector3.ProjectOnPlane(Vector3.down + outward * guardElbowOut + forward * 0.3f, reachDir);
            if (pole.sqrMagnitude < 0.0001f) pole = Vector3.ProjectOnPlane(outward, reachDir);
            pole.Normalize();

            // กฎโคไซน์: ศอกห่างไหล่ตามแนวเอื้อม x และออกทาง pole h
            float along = (upperLength * upperLength - foreLength * foreLength + reach * reach) / (2f * reach);
            float side = Mathf.Sqrt(Mathf.Max(upperLength * upperLength - along * along, 0f));
            Vector3 elbowGoal = shoulder + reachDir * along + pole * side;
            Vector3 upperGoal = (elbowGoal - shoulder).normalized;
            Vector3 foreGoal = (shoulder + reachDir * reach - elbowGoal).normalized;

            // แกนบานพับที่ต้องการ: งอบวก = หมุนต้นแขนเข้าหาแขนท่อนล่างรอบ +cross (วัดทั้งสองแขน 2026-10-27)
            // แขนเหยียดตรง (ตอนพุ่ง) cross เป็นศูนย์ → ใช้ระนาบของ pole แทน ศอกจะงอกลับทางเดิมเสมอ
            Vector3 hingeGoal = Vector3.Cross(upperGoal, foreGoal);
            if (hingeGoal.sqrMagnitude < 0.0001f) hingeGoal = Vector3.Cross(upperGoal, pole);
            hingeGoal = Vector3.ProjectOnPlane(hingeGoal, upperGoal).normalized;
            Vector3 upperNow = (elbow - shoulder).normalized;
            Vector3 hingeNow = Vector3.ProjectOnPlane(lowerArm.transform.TransformDirection(elbowJoint.axis), upperNow).normalized;
            if (hingeGoal.sqrMagnitude < 0.5f || hingeNow.sqrMagnitude < 0.5f) return;

            Quaternion upperDelta = Quaternion.LookRotation(upperGoal, hingeGoal) * Quaternion.Inverse(Quaternion.LookRotation(upperNow, hingeNow));
            AddPoseTorque(upperArm, upperDelta, Vector3.zero);
            AddPoseTorque(lowerArm, Quaternion.FromToRotation((body.position - elbow).normalized, foreGoal), upperArm.angularVelocity);
        }

        private void AddPoseTorque(Rigidbody segment, Quaternion delta, Vector3 parentAngularVelocity)
        {
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (float.IsNaN(axis.x) || float.IsInfinity(axis.x) || Mathf.Abs(angle) < 0.01f) axis = Vector3.zero;
            segment.AddTorque(axis * (angle * Mathf.Deg2Rad * armPoseSpring) -
                              (segment.angularVelocity - parentAngularVelocity) * armPoseDamper, ForceMode.Acceleration);
        }

        /// <summary>[SERVER] ลำตัวส่งแรงหมัด: ตอนง้างบิดไหล่ข้างที่ต่อยถอยหลัง / ตอนปล่อยบิดกลับ + พุ่งตาม (ตามแรงชาร์จ)</summary>
        private void DriveBodyForPunch(bool fired)
        {
            if (Torso == null || Torso.Body == null || !TryGetRobotForward(out Vector3 robotForward)) return;
            Rigidbody torsoBody = Torso.Body;
            // ข้างของแขน: +1 = แขนขวา → ส่งไหล่ขวาไปหน้า = ลำตัวหมุนซ้าย (yaw ลบ)
            float side = Mathf.Sign(Vector3.Dot(PivotPosition - torsoBody.position, Vector3.Cross(Vector3.up, robotForward)));

            if (!fired)
            {
                torsoBody.AddTorque(Vector3.up * (side * windupTwist * punch.Charge01), ForceMode.Acceleration);
                return;
            }

            float strength = Mathf.Lerp(0.4f, 1f, punch.Charge01);
            Vector3 lungeDir = targetHandOffset;
            lungeDir.y = 0f;
            torsoBody.AddTorque(Vector3.up * (-side * punchTwist * strength), ForceMode.VelocityChange);
            if (lungeDir.sqrMagnitude > 0.0001f)
                torsoBody.AddForce(lungeDir.normalized * (punchLunge * strength), ForceMode.VelocityChange);
        }

        /// <summary>[SERVER] แขนนี้ไม่มีผู้เล่นจองอยู่ (เทสคนเดียว / คนหลุด) — Host เป็นเจ้าของแทนแต่ไม่มีใครส่ง input</summary>
        private bool IsUncontrolled()
        {
            Nsc.Match.LimbSelection selection = Nsc.Match.LimbSelection.Current;
            return selection != null && limb.Owner != null &&
                   selection.GetController(limb.Owner.NetworkObjectId, limb.Slot) == Nsc.Match.LimbSelection.NoClient;
        }

        private bool TryGetRobotForward(out Vector3 forward)
        {
            forward = Vector3.forward;
            Robot robot = limb.Owner;
            LegController leg = robot != null
                ? (robot.GetLimb(LimbSlot.LeftLeg)?.Controller ?? robot.GetLimb(LimbSlot.RightLeg)?.Controller) as LegController
                : null;
            if (leg == null || !leg.TryGetRobotFacingYaw(out float yaw)) return false;
            forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            return true;
        }

        private void ResetHandTargetToRest()
        {
            targetHandOffset = Vector3.down * (maxArmLength * RestHangRatio);
            smoothedHandOffset = body != null ? body.position - PivotPosition : targetHandOffset;
            smoothVelocity = Vector3.zero;
        }

        /// <summary>กดคลิกซ้าย = เริ่มหมัด / ปล่อย = หยุดบูสต์ — server ตัดสิน state เองทั้งหมด</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void RequestPunchRpc(bool held)
        {
            // ตัวล้มอยู่ = ต่อยไม่ได้ (แขนอยู่โหมดปล่อยตามฟิสิกส์) — Q เพื่อลุกก่อน
            if (held && !IsTorsoDown) punch.Begin();
            else if (!held) punch.ReleaseInput();

            SyncPunchState();
        }

        /// <summary>กด Q ตอนล้ม — แรงลุกที่หัวไหล่</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void RequestRecoveryPushRpc()
        {
            if (Torso != null) Torso.ServerApplyRecoveryPush(PivotPosition);
        }

        // ================================================================
        //  ฟิสิกส์ (server)
        // ================================================================

        private void FixedUpdate()
        {
            if (!IsServer || body == null) return;

            SyncPunchState();
            // ✅ [Idle Guard] ไม่มีใครคุม → ยกการ์ดตามทิศหน้าหุ่น (ทุก tick เพราะหุ่นเลี้ยวได้)
            Vector3 robotForward = Vector3.forward;
            bool idleGuarding = idleGuard && !IsTorsoDown && IsUncontrolled() && TryGetRobotForward(out robotForward);
            if (idleGuarding) targetHandOffset = GuardOffset(robotForward);
            // โหมดต่อสู้ (แขนว่างยกการ์ด / แขนชี้แล้วต่อย): ขับท่อนแขนตามท่า IK ทุกจังหวะ — จับของ/ล้ม = ปล่อยฟิสิกส์เดิม
            bool poseDriven = (idleGuarding || (armInput != null && armInput.IsCombatMode)) && !IsTorsoDown &&
                              !grip.IsGrabbing && lowerArm != null && elbowJoint != null && Torso != null && Torso.Body != null;
            if (IsTorsoDown && punch.State == PunchState.Charging) punch.Reset(); // ล้มกลางง้าง = ทิ้งหมัด
            smoothedHandOffset = Vector3.SmoothDamp(smoothedHandOffset, targetHandOffset, ref smoothVelocity,
                                                    smoothTime, Mathf.Infinity, Time.fixedDeltaTime);

            if (limb.IsAttached)
            {
                ArmMotion normal = new ArmMotion
                {
                    // ง้าง/ต่อยอยู่ = ทิศหมัดจากเป้าดิบที่เพิ่งส่งมา ไม่ใช่เป้าที่หน่วงด้วย SmoothDamp — ชี้แล้วคลิกเร็วหมัดไม่ไปทางเก่า
                    target = PivotPosition + (punch.State != PunchState.Idle && punch.State != PunchState.Recovering
                        ? targetHandOffset : smoothedHandOffset),
                    velocityCap = maxHandVelocity,
                    damper = handDamper,
                    brakeDamping = brakeDamping,
                    extraReach = 0f
                };

                ArmMotion motion = punch.DriveTarget(normal, PivotPosition, body.position, body.linearVelocity,
                                                     transform.forward, maxArmLength, out Vector3 punchAcceleration);
                DriveHand(motion);
                if (poseDriven) DriveArmPose(motion.target);
                if (punchAcceleration != Vector3.zero)
                    body.AddForce(punchAcceleration, ForceMode.Acceleration);
                if (punch.State == PunchState.Charging) DriveBodyForPunch(fired: false);
            }
            else
            {
                ClimbPull = 0f;
            }

            punch.Tick(body.linearVelocity.magnitude, Time.fixedDeltaTime);
            SyncPunchState();

            if (punchState.Value == PunchState.Punching)
            {
                // sync แรงหมัดให้ client แสดง UI — เขียนเฉพาะตอนค่าขยับพอ ลด traffic
                float normalized = Mathf.Clamp01(body.linearVelocity.magnitude / Mathf.Max(0.01f, punch.maxSpeed));
                if (Mathf.Abs(netNormalizedPunchForce.Value - normalized) > 0.02f)
                    netNormalizedPunchForce.Value = normalized;
            }
        }

        /// <summary>ทำผลข้างเคียงของการเปลี่ยนจังหวะหมัด แล้ว sync state ให้ทุกเครื่อง</summary>
        private void SyncPunchState()
        {
            PunchState current = punch.State;
            if (current == appliedPunchState) return;

            // ง้างเสร็จแล้วปล่อย = จังหวะหมัดออก → ลำตัวบิด/พุ่งตาม
            if (current == PunchState.Punching && appliedPunchState == PunchState.Charging) DriveBodyForPunch(fired: true);

            // หมัดเร็วระดับนี้ต้องใช้ Continuous Dynamic กันทะลุ collider บางๆ
            if (current == PunchState.Punching)
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            else if (appliedPunchState == PunchState.Punching)
            {
                body.collisionDetectionMode = originalCollisionMode;
                netNormalizedPunchForce.Value = 0f;
            }

            appliedPunchState = current;
            if (IsSpawned) punchState.Value = current;
        }

        private void DriveHand(in ArmMotion motion)
        {
            // ✅ [Ragdoll Limp] ตอนล้ม มือยังขยับตามเมาส์ได้ แต่ "ห้ามดึงลำตัว" ไม่ให้แขนโกงลากตัว
            bool torsoDown = IsTorsoDown;
            bool hasActiveGrab = grip.IsGrabbing;

            // ข้อต่อตอนจับที่มีอยู่ก่อนล้มยังได้เพดานแรงเดิม (เริ่มจับใหม่ตอนล้มถูกกันไว้แล้ว)
            if (hasActiveGrab) grip.ApplyBreakLimit();

            // เปิดไว้ = ฟิสิกส์ฉีกโซ่แขนไม่ได้ (การหลุดจากดาเมจทำลายข้อต่อตั้งใจได้เสมอ)
            ProtectJointChain(grip.PreventPhysicsBreak);

            Vector3 pivotPosition = PivotPosition;
            Vector3 fromPivot = motion.target - pivotPosition;
            float distance = fromPivot.magnitude;
            Vector3 physicsTarget = motion.target;
            float reachLimit = maxArmLength + motion.extraReach;

            if (grip.IsSupporting)
            {
                PullTorsoTowardGrip(physicsTarget, distance);
                return; // ปีนอยู่: มือยึดกับของแล้ว ไม่ต้องขับสปริงมือ
            }

            ClimbPull = 0f;

            if (distance < 0.05f)
            {
                physicsTarget = pivotPosition + fromPivot.normalized * 0.05f;
            }
            else if (distance > reachLimit)
            {
                // ✅ [Soft Clamp] ระยะส่วนเกินถูกบีบแบบ asymptotic (เกินได้สูงสุด ~0.4 m นุ่มๆ)
                // clamp แข็งที่ผิวทรงกลมทำให้สปริง "เด้งกลับ" ตอนยืดสุดแขน
                float excess = distance - reachLimit;
                float softExcess = excess / (1f + excess * 2.5f);
                physicsTarget = pivotPosition + (fromPivot / distance) * (reachLimit + softExcess);

                if (!torsoDown || hasActiveGrab)
                    PullTorsoAtFullReach(fromPivot / distance);
            }

            // ── Spring + Stability ──
            // ✅ [Ragdoll Droop] ตอนล้ม: สปริงอ่อนลง + ไม่ชดเชยแรงโน้มถ่วง → มือห้อยตกจริง
            float springScale = torsoDown && !hasActiveGrab ? ragdollHandSpringScale : 1f;
            Vector3 force = SpringForce(physicsTarget, handMoveSpeed * springScale, motion.damper * springScale, motion.velocityCap);

            if (compensateGravity && body.useGravity && (!torsoDown || hasActiveGrab))
                force -= Physics.gravity;

            // ✅ [Proximity Brake] ใกล้เป้าแล้วเบรกแบบ quadratic — เหวี่ยงเต็มตอนไกล หยุดแม่นตอนถึง
            float proximity = 1f - Mathf.Clamp01((physicsTarget - body.position).magnitude / Mathf.Max(brakeDistance, 0.01f));
            if (proximity > 0f)
                force -= body.linearVelocity * (motion.brakeDamping * proximity * proximity);

            body.AddForce(force, ForceMode.Acceleration);
        }

        /// <summary>มือจับของนิ่งอยู่ (ปีน) — ดึงลำตัวเข้าหาทิศที่เล็ง</summary>
        private void PullTorsoTowardGrip(Vector3 physicsTarget, float distance)
        {
            if (Torso == null || Torso.Body == null) return;

            Vector3 climbPullDir = physicsTarget - grip.GrabPosition;
            Torso.Body.AddForce(climbPullDir * kinematicPullForce, ForceMode.Acceleration);

            float reach = Mathf.Clamp01(distance / maxArmLength);
            if (kinematicGrabAddsStress)
                Torso.ServerAddStress(kinematicPullForce * Time.fixedDeltaTime * reach);
            ClimbPull = reach;
        }

        /// <summary>
        /// [Anti Hand-Skating] แขนยืดสุดแล้วดึงลำตัว — แยกแรงเป็นแนวตั้งกับแนวราบ
        /// แนวราบถูกลดตาม torsoPullHorizontalScale และลดอีกถ้าตัววิ่งไปทางเดียวกันอยู่แล้ว
        /// </summary>
        private void PullTorsoAtFullReach(Vector3 pullDir)
        {
            if (Torso == null || Torso.Body == null) return;
            Rigidbody torsoBody = Torso.Body;

            Vector3 pullVertical = new Vector3(0f, pullDir.y, 0f);
            Vector3 pullHorizontal = new Vector3(pullDir.x, 0f, pullDir.z);

            Vector3 bodyHorizontalVelocity = torsoBody.linearVelocity;
            bodyHorizontalVelocity.y = 0f;
            float velocityAlongPull = Vector3.Dot(bodyHorizontalVelocity, pullHorizontal.normalized);
            float horizontalScale = torsoPullHorizontalScale *
                                    Mathf.Clamp01(1f - velocityAlongPull / Mathf.Max(maxAllowedHorizontalBoost, 0.1f));

            // ป้องกันแขนกดตัวเองจมพื้นเวลาล้มหรือกำลังพยายามลุก
            if (pullVertical.y < 0f && IsTorsoDown) pullVertical.y *= 0.1f;

            // Force × mass ไม่ใช่ Acceleration — เหตุผลเดียวกับ LegController (แรงบิดต้องหารความเฉื่อยลำตัว)
            torsoBody.AddForceAtPosition((pullVertical + pullHorizontal * horizontalScale) * (torsoPullForce * torsoBody.mass),
                                         PivotPosition, ForceMode.Force);
            Torso.ServerAddStress(torsoPullForce * Time.fixedDeltaTime * 0.5f);
        }

        // ================================================================
        //  โซ่ข้อต่อแขน
        // ================================================================

        private void ProtectJointChain(bool protect)
        {
            if (!protect)
            {
                foreach (KeyValuePair<Joint, Vector2> saved in protectedJointLimits)
                {
                    if (saved.Key == null) continue;
                    saved.Key.breakForce = saved.Value.x;
                    saved.Key.breakTorque = saved.Value.y;
                }
                protectedJointLimits.Clear();
                chainProtected = false;
                RestoreChainMasses();
                return;
            }

            bool stabilizing = stabilizeArmChainWhileGrabbing && grip.IsSupporting;

            // เลิกจับแล้ว → คืนมวลท่อนแขนเป็นค่าดั้งเดิม (เดิมมวลถูกเขียนทับถาวรตั้งแต่ปีนครั้งแรก)
            if (!stabilizing)
            {
                RestoreChainMasses();
                chainStabilized = false;
            }

            // ⚡ ตั้งครบแล้วและไม่มีอะไรเปลี่ยน → ไม่ต้องเดินโซ่ซ้ำทุก tick (GetComponents = GC)
            if (chainProtected && (!stabilizing || chainStabilized)) return;

            bool foundAnyJoint = false;
            foreach (Rigidbody chainBody in CollectChainBodies(grip.GrabJoint))
            {
                if (stabilizing)
                {
                    if (!originalChainMasses.ContainsKey(chainBody))
                        originalChainMasses.Add(chainBody, chainBody.mass);

                    chainBody.mass = Mathf.Max(0.01f, stabilizedArmMass);
                    chainBody.solverIterations = Mathf.Max(chainBody.solverIterations, stabilizedArmSolverIterations);
                    chainBody.solverVelocityIterations = Mathf.Max(chainBody.solverVelocityIterations, stabilizedArmSolverVelocityIterations);
                }

                foreach (Joint joint in chainBody.GetComponents<Joint>())
                {
                    if (joint == null) continue;
                    if (!protectedJointLimits.ContainsKey(joint))
                        protectedJointLimits.Add(joint, new Vector2(joint.breakForce, joint.breakTorque));

                    joint.breakForce = Mathf.Infinity;
                    joint.breakTorque = Mathf.Infinity;
                    foundAnyJoint = true;
                }
            }

            if (foundAnyJoint) chainProtected = true;
            if (stabilizing) chainStabilized = true;
        }

        private void RestoreChainMasses()
        {
            if (originalChainMasses.Count == 0) return;
            foreach (KeyValuePair<Rigidbody, float> saved in originalChainMasses)
                if (saved.Key != null) saved.Key.mass = saved.Value;
            originalChainMasses.Clear();
        }

        // ================================================================
        //  Respawn
        // ================================================================

        public override void ServerResetForRespawn()
        {
            if (!IsServer) return;

            grip.ServerRelease();
            punch.Reset();
            SyncPunchState();
            ClimbPull = 0f;
            ResetHandTargetToRest();

            RequestOwnerAimReset();
        }

        // ================================================================
        //  Gizmos
        // ================================================================

        private void OnDrawGizmosSelected()
        {
            if (pivot != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(PivotPosition, maxArmLength);
            }

            if (!showAimAssistGizmos || !punch.enableAimAssist || punch.aimAssistAngle <= 0f) return;

            Vector3 origin = PivotPosition;
            float range = Mathf.Max(0.01f, maxArmLength + punch.extraReach);
            // ก่อน spawn เป้ายังเป็นจุดกำเนิดโลก — ใช้ทิศไหล่→มือจริงแทน กรวยจะได้ไม่ชี้ถอยหลัง
            Vector3 rawDirection = Application.isPlaying
                ? smoothedHandOffset
                : (body != null ? body.position - origin : transform.position - origin);
            if (rawDirection.sqrMagnitude < 0.0001f) rawDirection = transform.forward;
            rawDirection.Normalize();

            Gizmos.color = aimRangeColor;
            Gizmos.DrawWireSphere(origin, range);
            Gizmos.color = rawAimColor;
            Gizmos.DrawLine(origin, origin + rawDirection * range);

            Vector3 referenceUp = Mathf.Abs(Vector3.Dot(rawDirection, Vector3.up)) > 0.98f ? Vector3.right : Vector3.up;
            Vector3 coneRight = Vector3.Cross(rawDirection, referenceUp).normalized;
            Vector3 coneUp = Vector3.Cross(coneRight, rawDirection).normalized;
            float coneRadians = punch.aimAssistAngle * Mathf.Deg2Rad;
            float coneRadius = Mathf.Sin(coneRadians) * range;
            Vector3 coneCenter = origin + rawDirection * (Mathf.Cos(coneRadians) * range);
            int segments = Mathf.Max(4, aimConeSegments);
            Vector3 previous = coneCenter + coneRight * coneRadius;

            for (int i = 1; i <= segments; i++)
            {
                float radians = (i / (float)segments) * Mathf.PI * 2f;
                Vector3 point = coneCenter + (coneRight * Mathf.Cos(radians) + coneUp * Mathf.Sin(radians)) * coneRadius;
                Gizmos.DrawLine(previous, point);
                if (i % Mathf.Max(1, segments / 4) == 0) Gizmos.DrawLine(origin, point);
                previous = point;
            }

            if (punch.TryFindAimAssistTarget(origin, rawDirection, maxArmLength, out Vector3 assisted, out Vector3 targetPosition))
            {
                Gizmos.color = assistedAimColor;
                Gizmos.DrawLine(origin, origin + assisted * range);
                Gizmos.DrawWireSphere(targetPosition, Mathf.Max(0.1f, range * 0.04f));
            }
        }
    }
}
