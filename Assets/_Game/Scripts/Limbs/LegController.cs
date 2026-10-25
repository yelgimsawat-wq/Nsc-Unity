using System.Collections.Generic;
using Nsc.Combat;
using Nsc.Robots;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace Nsc.Limbs
{
    /// <summary>
    /// ตัวขับฟิสิกส์ของขาฝั่ง server: ปักเท้า (Standing Foot Lock), ก้าว, กระโดด, ยันตัวลุก, ขาหลุด
    /// และให้ KickSkill เป็นเจ้าของเท้าระหว่างเตะ
    ///
    /// เดิมคือ PlayerFootForRobot (GUID เดิม) — input ของเจ้าของย้ายไป LegInput
    /// ขาส่งสถานะให้ลำตัวผ่าน property อ่านอย่างเดียว (IsGrounded, IsWalkStepping, ...) ลำตัวอ่านเองทุก tick
    /// </summary>
    [RequireComponent(typeof(LimbStrike))]
    public class LegController : LimbController
    {
        private const float SnapSpeedThreshold = 0.1f;
        private const float JumpHoldDuration = 0.3f;
        private const float ServerReachMargin = 1.5f;
        // จุดปักเท้าอยู่ในระยะ 90% ของรัศมีที่ขาเอื้อม — maxFootReach แบบ auto ไม่เกินค่านี้
        // ผู้เล่นจึงเลื่อนล้อไปยังระยะที่พอปล่อยคลิกแล้วโดนดึงกลับไม่ได้
        private const float PlantReachSafety = 0.9f;
        private static readonly LimbSlot[] ArmSlots = { LimbSlot.LeftArm, LimbSlot.RightArm };

        [Header("Movement & IK")]
        [SerializeField] private float maxLegLength = 1.5f;
        [SerializeField] private float minLegLength = 0.4f;
        [SerializeField] private float footMoveSpeed = 15f;
        [SerializeField] private float balanceShiftMultiplier = 0.3f;
        [SerializeField] private float legDamper = 30f;
        [SerializeField] private LayerMask groundLayer;
        [Tooltip("รัศมีของจุดถ่ายน้ำหนัก (และเพดาน validate ฝั่ง server) — ไม่ได้กำหนดระยะก้าว")]
        [SerializeField] private float mouseReachX = 2f;
        [SerializeField] private float mouseReachY = 2f;

        [Header("Leg Length & Stance")]
        [Tooltip("วัดความยาวขาจากข้อต่อจริงตอน spawn แล้วเขียนทับ maxLegLength — ปรับสเกลหุ่นแล้วไม่ต้องจูนใหม่")]
        [SerializeField] private bool autoMeasureLegLength = true;
        [Tooltip("ความสูงสะโพกที่ดึงไปตอนยืน เป็นสัดส่วนของความยาวขา (TorsoBalance ใช้ตั้งความสูงลำตัวด้วย)\n" +
                 "ต้องต่ำกว่า 1 — ดึงเกินความยาวขาแล้วขาตึงเป็นเชือก ก้าวไม่ออก\n" +
                 "หุ่นยักษ์ตอนนี้ข้อเท้าจำกัด −20..30° เข่าเลยงอจริงได้น้อย ส่วนที่เกินจะเป็นแรงกดลำตัวลงแทน")]
        [Range(0.6f, 1f)] [SerializeField] private float standingHeightRatio = 0.85f;
        [Tooltip("มุมเป้าของ HingeJoint ข้อเข่าตอนยืน/ก้าว — ต้องเลยมุมที่ขาเหยียดตรง (หุ่นยักษ์ ~30°)\n" +
                 "ไม่มีแรงนี้เข่าโดนน้ำหนักกดเข้าลิมิตแล้วล็อกตรง ลำตัวย่อลงไม่ได้ ความสูงจริงให้ TorsoBalance ตัดสิน")]
        [Range(0f, 120f)] [SerializeField] private float kneeBendAngle = 70f;
        [Tooltip("ความแรงสปริงข้อเข่าตอนยืน (0 = ปิด ใช้ค่าใน HingeJoint เดิม) — 500 อ่อนเกิน เข่าไม่ขยับ")]
        [Min(0f)] [SerializeField] private float kneeSpring = 5000f;
        [Min(0f)] [SerializeField] private float kneeDamper = 100f;

        [Header("Step Swing (คลิก = ก้าวเป็นเส้นโค้งจนเท้าปักเอง)")]
        [Tooltip("เวลาเหวี่ยงเท้าหนึ่งก้าว (วินาที) — ยาวขึ้น = ดูหนักขึ้น")]
        [Min(0.1f)] [SerializeField] private float stepSwingTime = 0.6f;
        [Tooltip("ความสูงยอดโค้งของก้าว เป็นสัดส่วนของความยาวขา")]
        [Range(0f, 0.5f)] [SerializeField] private float stepArcHeightRatio = 0.2f;
        [Tooltip("เท้าเกาะเส้นโค้งแน่นแค่ไหน — ต่ำ = เท้าย้วยตามหลัง สูง = เกาะเส้นเป๊ะ")]
        [Min(0.1f)] [SerializeField] private float swingTrackSpeed = 12f;

        [Header("Foot Facing (ท่าองอาจ)")]
        [Tooltip("เท้าชี้ไปทางลำตัวเสมอ: ลอยอยู่ค่อยๆ หมุนกลับ ลงพื้นแล้วตรงเป๊ะ — ปิดแล้วเท้าหมุนไปตามแรง ขาบิดเอง")]
        [SerializeField] private bool lockFootYaw = true;
        [Tooltip("ความเร็วหมุนเท้ากลับหาทิศลำตัวระหว่างก้าว (ต่อวินาที ต่อหน่วยมุมที่เหลือ)")]
        [Min(0f)] [SerializeField] private float footTurnSpeed = 8f;
        [Tooltip("ก้าวเฉียงไปทางไหน เท้าลงหันไปทางนั้นครึ่งหนึ่ง ไม่เกินค่านี้ต่อก้าว — ลำตัวหันตามเท้า = เลี้ยว (0 = เดินทิศเดิมตลอด)")]
        [Range(0f, 60f)] [SerializeField] private float maxTurnPerStep = 25f;

        [Header("Foot Reach (ล้อเมาส์ = ระยะใกล้/ไกล)")]
        [Tooltip("คำนวณ min/maxFootReach จากความยาวขาให้อัตโนมัติ")]
        [SerializeField] private bool autoFootReachFromLegLength = true;
        [Min(0.01f)] [SerializeField] private float minFootReach = 0.4f;
        [Min(0.02f)] [SerializeField] private float maxFootReach = 1.35f;
        [Range(0f, 1f)] [SerializeField] private float defaultFootReach = 0.5f;
        [Min(0.001f)] [SerializeField] private float footReachScrollSpeed = 0.12f;
        [Tooltip("ตัวคูณระยะเอื้อมตอนเท้าหลุด")]
        [Min(1f)] [SerializeField] private float detachedReachMultiplier = 2.5f;

        [Header("Detached")]
        [SerializeField] private float detachedMoveSpeed = 20f;
        [Tooltip("เพดานความเร็วเท้าตอนหลุด — กันเท้าปลิวหายจากสปริงไล่เป้า")]
        [SerializeField] private float maxDetachedSpeed = 8f;

        [Header("Jump")]
        [SerializeField] private float footJumpForce = 15f;
        [Tooltip("เพดานความเร็วเท้าตอนดีดตัว — เท้าไม่พุ่งเร็วกว่าลำตัวจนขาเหยียดสุดกระชากข้อต่อ")]
        [SerializeField] private float maxFootJumpVelocity = 7f;

        [Header("Standing")]
        [SerializeField] private float standingUpwardPull = 25f;
        [Tooltip("แรงสปริงรั้งลำตัวกลับเมื่อถูกลากเกินระยะขาที่ปักอยู่ — เดินขาเดียวแล้วไม่ล้มและไม่ไหล")]
        [SerializeField] private float legStretchSpring = 80f;
        [SerializeField] private float legStretchDamper = 10f;
        [Tooltip("ระยะยิง Raycast ลงหาพื้นตอนปักเท้า (เผื่อหุ่นสเกลใหญ่)")]
        [SerializeField] private float standingGroundRayLength = 50f;
        [Tooltip("snap ตำแหน่งแบบไม่ interpolate ตอนปักเท้าใหม่ — client รู้สึกเท้าดูดพื้นเท่า host")]
        [SerializeField] private bool teleportOnPlant = true;

        [Header("Foot Thickness")]
        [Tooltip("ยกจุดตรึงเท้าขึ้นชดเชยครึ่งความหนาของโมเดลเท้า กันจมพื้น")]
        [SerializeField] private float footThicknessOffset = 0.2f;
        [Tooltip("วัดความหนาเท้าจาก collider ตอน spawn แล้วเขียนทับค่าบน")]
        [SerializeField] private bool autoMeasureFootThickness = true;

        [Header("Recovery (Q)")]
        [Tooltip("แรงงัดลำตัวขึ้นตรงๆ ตอนยันตัวลุก")]
        [SerializeField] private float upwardRecoveryBoost = 500f;
        [Tooltip("แรงขยับเท้าตอนล้ม — เบาให้ขยับได้แต่ไม่ดันลำตัวจนไหล")]
        [SerializeField] private float ragdollFootMoveSpeed = 3f;

        [Header("Physics Safety")]
        [SerializeField] private float maxFootVelocity = 25f;
        [SerializeField] private float groundCheckDistance = 0.6f;
        [Tooltip("ข้อต่อทั้งโซ่ขาไม่มีวันแตกจากแรงฟิสิกส์")]
        [SerializeField] private bool makeLegJointsUnbreakable = true;
        [Tooltip("ปิดการชนเฉพาะ ขา ↔ ขาอีกข้าง (เดินไขว้แล้วเกี่ยวกันจนล้ม) — ขายังชนลำตัว/พื้นปกติ")]
        [SerializeField] private bool ignoreSelfCollision = true;

        [Header("Kick")]
        [SerializeField] private KickSkill kick = new KickSkill();

        private readonly NetworkVariable<KickState> kickState = new NetworkVariable<KickState>(
            KickState.Idle, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private LegMode mode;
        private bool stepping;
        private bool pushingUp;
        private bool jumping;
        private float jumpCooldownTimer;
        private bool releasedForClimb;
        private bool isPlantedSet;
        private Vector3 plantedPosition;
        private Vector3 targetFootPos;
        private Vector3 balanceShiftPos;
        private Vector3 detachedTargetPos;
        private bool selfCollisionConfigured;
        private bool legJointsUnbreakableApplied;
        private NetworkTransform footNetworkTransform;

        // ก้าวเป็นเส้นโค้ง (server) — เริ่มตอนเจ้าของกดคลิก จบตอนเท้าลงถึงพื้นแม้ปล่อยคลิกไปแล้ว
        private bool swingActive;
        private float swingProgress;
        private float swingLift01;
        private Vector3 swingStart;
        private Vector3 lastSwingPoint;

        private float footYawOffset;
        private bool footYawOffsetSet;
        private float plantedYaw;
        private bool landingFromSwing;
        private float facingYawOffset;
        private bool facingYawOffsetSet;

        private HingeJoint knee;
        private JointSpring kneeRestSpring;
        private bool kneeRestUseSpring;
        private bool kneeBraced;

        public LegMode Mode => mode;
        /// <summary>[SERVER] เจ้าของกดคลิกซ้ายค้างอยู่ (ยกขาก้าว)</summary>
        public bool IsStepping => stepping;
        public KickSkill Kick => kick;
        public KickState CurrentKickState => kickState.Value;

        /// <summary>เท้าแตะพื้นจริง (capsule จากสะโพกถึงเท้า)</summary>
        public bool IsGrounded =>
            body != null && pivot != null &&
            Physics.CheckCapsule(pivot.position, body.position, groundCheckDistance, groundLayer);

        /// <summary>
        /// "ก้าวเดิน" ในความหมายของกฎการล้มเท่านั้น — ไม่นับกระโดด เตะ หรือยันตัวลุก
        /// (ธงก้าวดิบถูกเซ็ตจากหลายระบบที่ไม่ใช่การเดิน ใช้ตัดสินล้มตรงๆ ไม่ได้)
        /// </summary>
        public bool IsWalkStepping => (stepping || swingActive) && !jumping && !pushingUp && !kick.IsControllingFoot;

        /// <summary>เท้านี้ทำหน้าที่ "ขายัน" ได้ไหม (ไม่รวมการเช็คพื้น — ลำตัวเช็คเองครั้งเดียวต่อ tick)</summary>
        public bool CanActAsSupport =>
            limb.IsAttached && !IsWalkStepping && !jumping && !pushingUp && !releasedForClimb;

        /// <summary>
        /// ยังสมดุลอยู่ไหม — แตะพื้นจริงก็นับว่าสมดุลแม้กำลังก้าว
        /// (เดิม stepping ตัดสิทธิ์ทันที สองคนเดินพร้อมกันแล้วธงซ้อนกันเฟรมเดียวก็ล้ม)
        /// </summary>
        public bool IsBalanced => !releasedForClimb && !jumping && !pushingUp && IsGrounded;

        public bool IsKickMotionActive => kick.IsMotionActive;
        public bool IsKickControllingFoot => kick.IsControllingFoot;

        // ค่าที่ LegInput ใช้คำนวณเป้า — ต้องตรงกับที่ server ใช้ตรวจ
        public float MaxLegLength => maxLegLength;
        /// <summary>ความสูงสะโพกเหนือจุดกลางเท้าตอนยืนย่อเข่า</summary>
        public float StandingHipHeight => maxLegLength * standingHeightRatio;
        public float FootThickness => footThicknessOffset;
        public LayerMask GroundLayer => groundLayer;
        public float BalanceReach => Mathf.Max(mouseReachX, mouseReachY);
        public float MinFootReach => minFootReach;
        public float MaxFootReach => maxFootReach;
        public float DefaultFootReach => defaultFootReach;
        public float FootReachScrollSpeed => footReachScrollSpeed;
        public float DetachedReachMultiplier => detachedReachMultiplier;
        public Rigidbody TorsoBody => Torso != null ? Torso.Body : null;
        public bool TorsoDown => IsTorsoDown;

        protected override void Awake()
        {
            base.Awake();
            kick.Init(this, GetComponent<LimbStrike>());
            LimbStrike strike = GetComponent<LimbStrike>();
            if (strike != null) strike.SetSource(kick);
        }

        /// <summary>
        /// จับทิศเท้าและทิศหน้าหุ่นเทียบลำตัว ณ ท่าที่โมเดลวางไว้ — Start มาก่อน FixedUpdate แรก ฟิสิกส์ยังไม่ขยับ
        /// (หุ่นเกิดมาล้มนอน จับทีหลังท่านอนบิดจนค่าเพี้ยน) และตอนนี้ Robot ผูกขาครบสองข้างแล้ว
        /// </summary>
        private void Start()
        {
            Rigidbody torsoBody = TorsoBody;
            if (body == null || torsoBody == null) return;

            float torsoYaw = torsoBody.rotation.eulerAngles.y;
            footYawOffset = Mathf.DeltaAngle(torsoYaw, body.rotation.eulerAngles.y);
            footYawOffsetSet = true;
            if (TryGetHipFacing(out Vector3 facing))
            {
                facingYawOffset = Mathf.DeltaAngle(torsoYaw, Quaternion.LookRotation(facing).eulerAngles.y);
                facingYawOffsetSet = true;
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            footNetworkTransform = GetComponent<NetworkTransform>();

            // รันทุกเครื่อง — ฝั่ง owner ใช้ความหนาเท้าและระยะก้าวคำนวณเป้าด้วย
            if (autoMeasureFootThickness) MeasureFootThickness();
            if (autoMeasureLegLength) MeasureLegLength();
            ApplyAutoFootReach();

            // ✅ [Rest Pose] กันเป้าเท้าเริ่มที่ (0,0,0) — หุ่นล้มก่อน RPC แรกมาถึงแล้วเท้าพุ่งไปจุดกำเนิดโลก
            if (IsServer && body != null) ResetTargetsToCurrentPose();
        }

        private void ResetTargetsToCurrentPose()
        {
            targetFootPos = body.position;
            detachedTargetPos = body.position;
            balanceShiftPos = pivot != null ? pivot.position : body.position;
        }

        /// <summary>วัดระยะจากจุดกำเนิด rigidbody ถึงจุดต่ำสุดของ collider เท้า = ความหนาที่ต้องยกจริง</summary>
        private void MeasureFootThickness()
        {
            if (body == null) return;

            float lowestPoint = float.MaxValue;
            foreach (Collider col in body.GetComponentsInChildren<Collider>())
                if (col != null && col.attachedRigidbody == body)
                    lowestPoint = Mathf.Min(lowestPoint, col.bounds.min.y);

            if (lowestPoint == float.MaxValue) return;

            float measured = body.position.y - lowestPoint;
            if (measured > 0.01f && measured < 2f)
            {
                footThicknessOffset = measured;
                Debug.Log($"[Foot] 📏 Auto-measured footThicknessOffset = {measured:F3}m ({name})");
            }
        }

        /// <summary>
        /// ความยาวขา = ระยะจากเท้าไล่ anchor ของข้อต่อ (ข้อเท้า → เข่า → สะโพก) ตอนเหยียดตรง
        /// ไม่ขึ้นกับท่าตอน spawn (หุ่นเกิดมานอนอยู่)
        /// </summary>
        private void MeasureLegLength()
        {
            if (body == null) return;

            List<Rigidbody> chain = CollectChainBodies();
            Vector3 previous = body.position;
            float length = 0f;
            foreach (Rigidbody chainBody in chain)
            {
                Joint joint = null;
                foreach (Joint candidate in chainBody.GetComponents<Joint>())
                    if (candidate != null && candidate.connectedBody != null) { joint = candidate; break; }
                if (joint == null) return;

                Vector3 anchor = chainBody.transform.TransformPoint(joint.anchor);
                length += Vector3.Distance(previous, anchor);
                previous = anchor;
            }

            if (length > minLegLength + 0.05f && length < 200f)
            {
                maxLegLength = length;
                Debug.Log($"[Foot] 📏 Auto-measured maxLegLength = {length:F2}m ({name})");
            }
        }

        /// <summary>
        /// ผูกช่วงระยะก้าวเข้ากับความยาวขาจริง — หุ่นสเกล 1.5 หรือ 14 ใช้ค่าเดียวกันได้
        /// ก้าวไกลสุดเกือบเต็มช่วงขา: ระหว่างก้าวลำตัวไหลไปหาจุดที่เท้าจะลง (TorsoBalance) เท้าจึงเอื้อมถึง
        /// </summary>
        private void ApplyAutoFootReach()
        {
            if (!autoFootReachFromLegLength) return;
            minFootReach = Mathf.Max(0.05f, minLegLength);
            maxFootReach = Mathf.Max(minFootReach + 0.05f, maxLegLength * PlantReachSafety);
        }

        // ================================================================
        //  RPC จากเจ้าของ (LegInput)
        // ================================================================

        // Reliable: แพ็กเก็ตหลุดตอนเดินแล้ว server ค้างเป้าเก่า พอแพ็กเก็ตใหม่มาเท้าจะกระโดดข้าม
        // อัตราส่งถูกจำกัดด้วยระยะขั้นต่ำอยู่แล้ว ต้นทุน bandwidth จึงต่ำ
        [Rpc(SendTo.Server, Delivery = RpcDelivery.Reliable, InvokePermission = RpcInvokePermission.Owner)]
        public void SetFootTargetRpc(Vector3 target)
        {
            if (!target.IsValid()) return;
            if (pivot != null)
            {
                // 🛡️ สองชั้น: รัศมีทรงกลม (ขาเอื้อมถึงไหม) + รัศมีแนวราบ (เกินระยะที่ล้อเลื่อนได้ไหม)
                Vector3 fromPivot = target - pivot.position;
                float limit = maxLegLength * ServerReachMargin;
                if (fromPivot.magnitude > limit) target = pivot.position + fromPivot.normalized * limit;

                Vector3 flat = new Vector3(target.x - pivot.position.x, 0f, target.z - pivot.position.z);
                float flatLimit = maxFootReach * ServerReachMargin;
                if (flat.magnitude > flatLimit)
                {
                    Vector3 clamped = flat.normalized * flatLimit;
                    target = new Vector3(pivot.position.x + clamped.x, target.y, pivot.position.z + clamped.z);
                }
            }
            targetFootPos = target;
        }

        /// <summary>จุดถ่ายน้ำหนัก — clamp เพราะค่านี้ถูกคูณเป็นแรงดันลำตัว ส่งไกลๆ มาได้แรงมหาศาล</summary>
        [Rpc(SendTo.Server, Delivery = RpcDelivery.Reliable, InvokePermission = RpcInvokePermission.Owner)]
        public void SetBalanceShiftRpc(Vector3 target)
        {
            if (!target.IsValid()) return;
            if (pivot != null)
            {
                Vector3 fromPivot = target - pivot.position;
                float limit = BalanceReach * ServerReachMargin;
                if (fromPivot.magnitude > limit) target = pivot.position + fromPivot.normalized * limit;
            }
            balanceShiftPos = target;
        }

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Owner)]
        public void SetDetachedTargetRpc(Vector3 target)
        {
            if (target.IsValid()) detachedTargetPos = target;
        }

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Reliable, InvokePermission = RpcInvokePermission.Owner)]
        public void SetSteppingRpc(bool on)
        {
            if (on && !Nsc.Match.GameplayGate.CanAct) return;
            stepping = on;
            // เริ่มโค้งที่นี่ด้วย ไม่งั้นคลิกสั้นที่กด+ปล่อยมาถึงใน tick เดียวกันจะไม่เกิดก้าวเลย
            // ก้าวที่ยังลอยอยู่ไม่เริ่มใหม่ — กดซ้ำระหว่างเท้ากำลังลง = ยกค้างต่อจากตรงนั้น
            if (on && !swingActive && body != null) BeginSwing();
        }

        /// <summary>[SERVER] คนคุมขานี้เปลี่ยนไป — ปล่อยปุ่มที่ค้าง (ก้าวที่ลอยอยู่ลงพื้นเอง, ยกเลิกชาร์จเตะ)</summary>
        public void ServerReleaseInput()
        {
            if (!IsServer) return;
            stepping = false;
            pushingUp = false;
            if (kick.State == KickState.Charging) kick.CancelCharge();
            SyncKickState();
        }

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Reliable, InvokePermission = RpcInvokePermission.Owner)]
        public void SetPushingUpRpc(bool on) => pushingUp = on;

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Reliable, InvokePermission = RpcInvokePermission.Owner)]
        public void JumpRpc()
        {
            // 🛡️ เช็กซ้ำฝั่ง server เสมอ — กัน client ยิงรัวให้หุ่นลอยขึ้นฟ้า
            if (body == null || jumping || jumpCooldownTimer > 0f || !IsGrounded) return;

            jumping = true;
            jumpCooldownTimer = JumpHoldDuration;
            body.isKinematic = false; // kinematic body ไม่รับแรง — ปลดก่อน AddForce
            body.AddForce(Vector3.up * Mathf.Min(footJumpForce, maxFootJumpVelocity), ForceMode.VelocityChange);

            // แรงลำตัวให้ลำตัวตัดสินเอง: hop ขาเดียว / Co-op Jump / เพดานความเร็ว + Jump Grace
            if (Torso != null) Torso.ServerApplyJump(limb.Slot);
        }

        /// <summary>
        /// เริ่มชาร์จเตะ — ส่งทิศเล็งมาด้วยเพราะ server อ่านจุดเล็งเองไม่ได้ (อยู่ฝั่ง owner)
        /// ⚠️ ห้ามเซ็ตธงก้าวที่นี่: เตะจะถูกนับเป็นก้าวในกฎสองขาก้าวพร้อมกัน และไม่มีทางไหนเคลียร์กลับ
        /// </summary>
        [Rpc(SendTo.Server, Delivery = RpcDelivery.Reliable, InvokePermission = RpcInvokePermission.Owner)]
        public void StartKickRpc(Vector3 aimDirection)
        {
            if (kick.State != KickState.Idle || !CanUseKick()) return;
            kick.BeginCharge(aimDirection);
            SyncKickState();
        }

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Reliable, InvokePermission = RpcInvokePermission.Owner)]
        public void ReleaseKickRpc(Vector3 aimDirection)
        {
            if (kick.State != KickState.Charging || !CanKeepKicking()) kick.Reset();
            else kick.Release(aimDirection);
            SyncKickState();
        }

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Reliable, InvokePermission = RpcInvokePermission.Owner)]
        public void CancelKickRpc()
        {
            kick.CancelCharge();
            SyncKickState();
        }

        private bool CanUseKick() => CanKeepKicking() && !jumping;

        private bool CanKeepKicking() => limb.IsAttached && !IsTorsoDown;

        private void SyncKickState()
        {
            if (IsSpawned && kickState.Value != kick.State) kickState.Value = kick.State;
        }

        // ================================================================
        //  ฟิสิกส์ (server)
        // ================================================================

        private void FixedUpdate()
        {
            if (!IsServer) return;

            ConfigureLegSelfCollision();
            if (makeLegJointsUnbreakable && !legJointsUnbreakableApplied && body != null)
                legJointsUnbreakableApplied = MakeLegJointChainUnbreakable();
            if (body == null || pivot == null) return;

            if (!IsKickMotionActive && body.linearVelocity.sqrMagnitude > maxFootVelocity * maxFootVelocity)
            {
                float excess = body.linearVelocity.magnitude - maxFootVelocity;
                body.AddForce(-body.linearVelocity.normalized * excess * 10f, ForceMode.Acceleration);
            }

            if (jumpCooldownTimer > 0f) jumpCooldownTimer -= Time.fixedDeltaTime;
            else if (jumping && body.linearVelocity.y <= SnapSpeedThreshold && IsGrounded) jumping = false;

            if (limb.IsAttached)
            {
                if (IsTorsoDown) releasedForClimb = false;
                else if (!kick.IsControllingFoot) UpdateClimbRelease();
            }

            // กระโดด/ปีนใช้สปริงก้าวเดิม — กดคลิกค้างอยู่ตอนกลับมายืน = เริ่มโค้งใหม่จากตรงนั้น
            if (jumping || releasedForClimb) swingActive = false;
            else if (stepping && !swingActive) BeginSwing();

            mode = ResolveMode();
            // เตะ ล้ม ขาหลุด = ทิ้งก้าวที่ค้างอยู่ ไม่งั้นพอกลับมายืนเท้าจะไหลต่อโค้งเก่า
            if (mode != LegMode.Stepping) swingActive = false;
            SetKneeBraced(mode == LegMode.Planted || mode == LegMode.Stepping);

            switch (mode)
            {
                case LegMode.Detached:
                    // เท้าที่หลุดตอนปักพื้นอยู่ยังเป็น kinematic — ไม่ปลดจะลากกลับ/ดึงต่อกลับ (R) ไม่ได้เลย
                    isPlantedSet = false;
                    body.isKinematic = false;
                    // ตอนกดดึงกลับ (R) หยุดสปริงไล่เมาส์ ไม่งั้นสองแรงสู้กันแล้วเท้าไม่มีวันถึง socket
                    if (limb.Attachment == null || !limb.Attachment.IsBeingPulled) DriveDetached();
                    break;
                case LegMode.PushingUp: PushUp(); break;
                case LegMode.Limp:
                    isPlantedSet = false;
                    DriveLimp();
                    break;
                case LegMode.Kicking:
                    // ท่าเตะเป็นเจ้าของ rigidbody — ห้ามตัวล็อกยืน/สปริงก้าวยกเลิกความเร็วเตะ
                    isPlantedSet = false;
                    body.isKinematic = false;
                    break;
                case LegMode.Stepping:
                    isPlantedSet = false;
                    if (swingActive) DriveSwing();
                    else DriveStep();
                    break;
                default:
                    Plant();
                    break;
            }

            // ท่าเตะ (เดิมเป็นคอมโพเนนต์แยกที่รันหลังเท้า) — ล้ม/ขาหลุดกลางท่า = ยกเลิก
            if (kick.State != KickState.Idle && !CanKeepKicking()) kick.Reset();
            else kick.FixedTick(Time.fixedDeltaTime);
            SyncKickState();
        }

        private LegMode ResolveMode()
        {
            if (!limb.IsAttached) return LegMode.Detached;
            if (IsTorsoDown) return pushingUp ? LegMode.PushingUp : LegMode.Limp;
            if (kick.IsControllingFoot) return LegMode.Kicking;
            if (releasedForClimb || stepping || swingActive || jumping) return LegMode.Stepping;
            return LegMode.Planted;
        }

        /// <summary>
        /// ปีนอยู่ (มือจับของนิ่ง): ปักเท้าค้างไว้ตราบที่ขายังเอื้อมถึง พอลำตัวปีนสูงเกินก็ปล่อยจุดปัก
        /// โดยไม่ปลดข้อต่อ — solver จะได้ไม่ถูกบังคับให้ฉีกโมเดลขาด
        /// </summary>
        private void UpdateClimbRelease()
        {
            bool supportingClimb = HasSupportingHandGrab();

            if (supportingClimb && isPlantedSet)
            {
                Vector3 plantedWorld = plantedPosition + Vector3.up * footThicknessOffset;
                if (Vector3.Distance(pivot.position, plantedWorld) > maxLegLength * 0.95f)
                {
                    releasedForClimb = true;
                    isPlantedSet = false;
                }
            }
            else if (!supportingClimb && releasedForClimb && IsGrounded)
            {
                releasedForClimb = false;
            }
        }

        private bool HasSupportingHandGrab()
        {
            Robot robot = limb.Owner;
            if (robot == null) return false;
            foreach (LimbSlot slot in ArmSlots)
            {
                RobotLimb arm = robot.GetLimb(slot);
                if (arm != null && arm.Controller is ArmController controller &&
                    controller.Grip != null && controller.Grip.IsSupporting)
                    return true;
            }
            return false;
        }

        /// <summary>กด Q ยันตัวลุก — แช่แข็งเท้าติดพื้นแล้วดันสะโพกเข้าหาศูนย์กลาง</summary>
        private void PushUp()
        {
            releasedForClimb = false;
            body.isKinematic = false; // 🔓 ปล่อยให้ข้อต่อทำงาน กัน solver รวน
            FreezeFoot(isRecovering: true);

            Torso.ServerApplyRecoveryPush(pivot.position, 1f);

            // 🚀 แรงงัดขึ้น — หยุดอัดเมื่อตัวพุ่งขึ้นเร็วพอแล้ว (หลายคนกด Q พร้อมกันจะพุ่งฟ้า)
            Rigidbody torsoBody = Torso.Body;
            if (torsoBody != null && torsoBody.linearVelocity.y < Torso.MaxRecoveryUpVelocity)
                torsoBody.AddForce(Vector3.up * upwardRecoveryBoost, ForceMode.Acceleration);
        }

        /// <summary>เท้าเพิ่งปักใหม่ → ทุกเครื่องได้ยินเสียงตึ้ง เห็นฝุ่น กล้องสั่น (กันยิงถี่ตอนเท้าเด้งปักซ้ำ)</summary>
        private void BroadcastFootstep(float landingSpeed)
        {
            if (Time.time - lastFootstepTime < FootstepMinInterval) return;
            lastFootstepTime = Time.time;

            Nsc.Feel.FeelConfig config = Nsc.Feel.FeelConfig.Instance;
            float fullSpeed = config != null ? Mathf.Max(0.1f, config.footstepFullSpeed) : 12f;
            float minStrength = config != null ? config.footstepMinStrength : 0.45f;
            float strength = Mathf.Lerp(minStrength, 1f, Mathf.Clamp01(landingSpeed / fullSpeed));
            ServerBroadcastFootstep(plantedPosition, strength);
        }

        private const float FootstepMinInterval = 0.2f;
        private float lastFootstepTime = -10f;

        private void FreezeFoot(bool isRecovering)
        {
            if (!isPlantedSet)
            {
                if (Physics.Raycast(body.position + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 20f, groundLayer))
                    plantedPosition = hit.point;
                else if (Physics.Raycast(pivot.position, Vector3.down, out RaycastHit pivotHit, 20f, groundLayer))
                    plantedPosition = new Vector3(body.position.x, pivotHit.point.y, body.position.z);
                else
                    plantedPosition = body.position;

                isPlantedSet = true;
                TeleportFootTo(plantedPosition + Vector3.up * footThicknessOffset);
            }

            // เบรกเท้าให้นิ่ง — เซ็ต velocity ได้เฉพาะตอน dynamic
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                if (!isRecovering) body.angularVelocity = Vector3.zero;
            }

            body.MovePosition(plantedPosition + Vector3.up * footThicknessOffset);
            if (!isRecovering) body.rotation = Quaternion.Euler(0f, body.rotation.eulerAngles.y, 0f);
        }

        private void DriveLimp()
        {
            body.isKinematic = false;
            Vector3 target = targetFootPos;
            Vector3 fromPivot = target - pivot.position;
            if (fromPivot.magnitude > maxLegLength) target = pivot.position + fromPivot.normalized * maxLegLength;

            // สปริงเบา — เท้าขยับตามเมาส์ได้นิดหน่อยเพื่อลุกง่ายขึ้น
            Vector3 force = SpringForce(target, ragdollFootMoveSpeed, legDamper);

            // ✅ [No Torso Push] ตัดแรงส่วนที่ชี้เข้าหาลำตัว — ห้ามเอาเท้าจ่อตัวแล้วดันหุ่นไหล
            if (TorsoBody != null)
            {
                Vector3 toTorso = (TorsoBody.position - body.position).normalized;
                float into = Vector3.Dot(force, toTorso);
                if (into > 0f) force -= toTorso * into;
            }

            body.AddForce(force, ForceMode.Acceleration);
        }

        /// <summary>สปริงไล่เป้าตรงๆ — ใช้ตอนกระโดดและปีน (ก้าวเดินใช้ DriveSwing)</summary>
        private void DriveStep()
        {
            body.isKinematic = false;
            DriveToward(ClampToLegReach(targetFootPos), footMoveSpeed, legDamper);
        }

        /// <summary>
        /// ยืน/ก้าว = สปริงข้อเข่าดันไปทางงอ (ฝั่งที่ลิมิตเปิดให้) | ล้ม/หลุด/เตะ = คืนค่าเดิมของ HingeJoint ขาปวกเปียกตามเดิม
        /// ตั้งเฉพาะตอนสถานะเปลี่ยน — เซ็ต spring ทุก tick ปลุก rigidbody ทั้งโซ่
        /// </summary>
        private void SetKneeBraced(bool braced)
        {
            if (kneeSpring <= 0f) braced = false;
            if (braced == kneeBraced) return;

            if (knee == null)
            {
                if (!braced) return;
                knee = FindKneeHinge();
                if (knee == null) { kneeSpring = 0f; return; }
                kneeRestSpring = knee.spring;
                kneeRestUseSpring = knee.useSpring;
            }

            kneeBraced = braced;
            if (braced)
            {
                // ลิมิตบอกทิศงอของเข่าตัวนี้ (หุ่นนี้ 0..220 = งอทางบวก)
                float bend = knee.useLimits && knee.limits.max <= 0f ? -kneeBendAngle : kneeBendAngle;
                knee.spring = new JointSpring { spring = kneeSpring, damper = kneeDamper, targetPosition = bend };
                knee.useSpring = true;
            }
            else
            {
                knee.spring = kneeRestSpring;
                knee.useSpring = kneeRestUseSpring;
            }
        }

        private HingeJoint FindKneeHinge()
        {
            if (body == null) return null;
            foreach (Rigidbody chainBody in CollectChainBodies())
                if (chainBody.TryGetComponent(out HingeJoint hinge)) return hinge;
            return null;
        }

        private Vector3 ClampToLegReach(Vector3 target)
        {
            Vector3 fromPivot = target - pivot.position;
            float distance = fromPivot.magnitude;
            if (distance < minLegLength)
                return pivot.position + (distance > 0.01f ? fromPivot.normalized : pivot.forward) * minLegLength;
            if (distance > maxLegLength)
                return pivot.position + fromPivot.normalized * maxLegLength;
            return target;
        }

        private void BeginSwing()
        {
            swingActive = true;
            swingProgress = 0f;
            swingLift01 = 0f;
            swingStart = body.position;
            lastSwingPoint = body.position;
        }

        /// <summary>
        /// 🦶 [Step Swing] ก้าวเป็นเส้นโค้ง: แนวราบไหลจากจุดเริ่มไปเป้าแบบ ease in-out ส่วนความสูงยกแล้วกระแทกลง
        /// คลิกสั้น = ก้าวครบโค้งแล้วปักเอง | กดค้าง = ลอยค้างที่ยอดโค้งตามเป้า ปล่อยแล้วค่อยลง
        /// ผู้เล่นเลือกแค่ "ไปไหน" ระบบเป็นคนทำให้ก้าวสวย
        /// </summary>
        private void DriveSwing()
        {
            body.isKinematic = false;
            float dt = Time.fixedDeltaTime;

            swingProgress = Mathf.MoveTowards(swingProgress, 1f, dt / stepSwingTime);
            bool rising = stepping || swingProgress < 0.5f;
            swingLift01 = Mathf.MoveTowards(swingLift01, rising ? 1f : 0f, dt / (stepSwingTime * 0.5f));

            if (!stepping && swingProgress >= 1f && swingLift01 <= 0f)
            {
                swingActive = false; // เท้าถึงพื้นแล้ว — tick หน้า Plant() ปักและยิงเสียงตึ้ง
                landingFromSwing = true;
                return;
            }

            float travel = Mathf.SmoothStep(0f, 1f, swingProgress);
            // sin ¼ รอบ: ยกเร็วแล้วชะลอที่ยอด ขาลงกลับกัน = ค้างที่ยอดแล้วเร่งกระแทกพื้นเหมือนของหนัก
            float lift = Mathf.Sin(swingLift01 * Mathf.PI * 0.5f) * maxLegLength * stepArcHeightRatio;
            Vector3 point = ClampSwingReach(Vector3.Lerp(swingStart, targetFootPos, travel) + Vector3.up * lift);

            // feed-forward ความเร็วของเส้นโค้ง — สปริงอย่างเดียวเท้าจะลากตามหลังจุดบนเส้นจนโค้งแบน
            Vector3 pathVelocity = Vector3.ClampMagnitude((point - lastSwingPoint) / dt, maxFootVelocity);
            lastSwingPoint = point;

            Vector3 desiredVelocity = pathVelocity + (point - body.position) * swingTrackSpeed;
            body.AddForce((desiredVelocity - body.linearVelocity) * legDamper, ForceMode.Acceleration);

            if (lockFootYaw) TurnFootToward(DesiredFootYaw());
        }

        /// <summary>
        /// ตัดเส้นโค้งแค่ระยะแนวราบ ไม่ใช่ทรงกลมความยาวขา — ขาหุ่นงอแทบไม่ได้ ตัดด้วยทรงกลมแล้วเท้าโดนดึงกลับมาใต้ตัวทุกก้าว
        /// เท้าที่เอื้อมไปข้างหน้าพร้อมกับลำตัวที่ไหลตาม = ก้าวยาว
        /// </summary>
        private Vector3 ClampSwingReach(Vector3 point)
        {
            Vector3 flat = point - pivot.position;
            flat.y = 0f;
            float maxFlat = maxLegLength * PlantReachSafety;
            if (flat.magnitude <= maxFlat) return point;
            Vector3 clamped = pivot.position + flat.normalized * maxFlat;
            return new Vector3(clamped.x, point.y, clamped.z);
        }

        /// <summary>[SERVER] กำลังก้าว — จุดที่เท้าจะลง ให้ TorsoBalance ถ่ายน้ำหนักลำตัวไปหา</summary>
        public bool TryGetStepDestination(out Vector3 destination)
        {
            destination = default;
            if (!swingActive || jumping || body == null || pivot == null) return false;
            destination = ClampSwingReach(targetFootPos);
            return true;
        }

        // ================================================================
        //  ทิศเท้า — เท้าชี้ไปทางเดียวกับลำตัวเสมอ ขาไม่บิดหมุนเอง
        // ================================================================

        private float DesiredFootYaw()
        {
            Rigidbody torsoBody = TorsoBody;
            if (torsoBody == null || !footYawOffsetSet) return body.rotation.eulerAngles.y;
            return torsoBody.rotation.eulerAngles.y + footYawOffset;
        }

        /// <summary>
        /// ทิศเท้าตอนลงพื้น = ทิศลำตัว + หันตามทิศที่ก้าวไปครึ่งหนึ่ง (ไม่เกิน maxTurnPerStep)
        /// ก้าวตรง = ไม่หัน | ก้าวเฉียง = ค่อยๆ เลี้ยว | ก้าวสั้นหรือถอยหลัง = ไม่หัน
        /// </summary>
        private float LandingYaw()
        {
            float yaw = DesiredFootYaw();
            if (!landingFromSwing || maxTurnPerStep <= 0f || !TryGetRobotFacingYaw(out float facingYaw)) return yaw;

            Vector3 step = plantedPosition - swingStart;
            step.y = 0f;
            if (step.magnitude < maxLegLength * 0.2f) return yaw;

            float angle = Vector3.SignedAngle(Quaternion.Euler(0f, facingYaw, 0f) * Vector3.forward, step, Vector3.up);
            if (Mathf.Abs(angle) > 110f) return yaw;
            return yaw + Mathf.Clamp(angle * 0.5f, -maxTurnPerStep, maxTurnPerStep);
        }

        /// <summary>
        /// ทิศหน้าหุ่น = ทิศลำตัว + ส่วนต่างที่จับจากท่าโมเดล (TorsoBalance คุมลำตัวไม่ให้บิดแล้ว ค่านี้จึงนิ่ง)
        /// ไม่ใช้แนวสะโพกตรงๆ — จุดอ้างอิงสะโพกอยู่บนต้นขา แกว่ง ±45° ตามขาที่เหวี่ยง
        /// </summary>
        public bool TryGetRobotFacingYaw(out float yaw)
        {
            Rigidbody torsoBody = TorsoBody;
            yaw = torsoBody != null ? torsoBody.rotation.eulerAngles.y + facingYawOffset : 0f;
            return facingYawOffsetSet && torsoBody != null;
        }

        /// <summary>ทิศหน้าจากแนวสะโพกซ้าย→ขวา — ใช้แค่ตอนจับค่าจากท่าโมเดล (forward ของลำตัวโมเดลนี้ชี้ด้านข้าง)</summary>
        private bool TryGetHipFacing(out Vector3 facing)
        {
            facing = Vector3.zero;
            Robot robot = limb.Owner;
            if (robot == null) return false;
            LegController left = robot.GetLimb(LimbSlot.LeftLeg)?.Controller as LegController;
            LegController right = robot.GetLimb(LimbSlot.RightLeg)?.Controller as LegController;
            if (left == null || right == null || left.pivot == null || right.pivot == null) return false;

            Vector3 hipRight = right.pivot.position - left.pivot.position;
            hipRight.y = 0f;
            if (hipRight.sqrMagnitude < 0.0001f) return false;
            facing = Vector3.Cross(hipRight, Vector3.up).normalized;
            return true;
        }

        /// <summary>[SERVER] เท้าปักอยู่ — ทิศที่ลำตัวควรหัน (ทิศเท้าลบส่วนต่างจากท่าโมเดล) ให้ TorsoBalance คุมไม่ให้ลำตัวบิด</summary>
        public bool TryGetPlantedTorsoYaw(out float torsoYaw)
        {
            torsoYaw = plantedYaw - footYawOffset;
            return lockFootYaw && isPlantedSet && mode == LegMode.Planted;
        }

        /// <summary>เท้าลอยอยู่ (dynamic) — หมุนกลับหาทิศลำตัวด้วยความเร็วเชิงมุม ไม่ snap</summary>
        private void TurnFootToward(float yaw)
        {
            Quaternion error = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Inverse(body.rotation);
            error.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (float.IsNaN(axis.x) || Mathf.Abs(angle) < 0.01f) return;
            body.angularVelocity = axis * (angle * Mathf.Deg2Rad * footTurnSpeed);
        }

        /// <summary>
        /// 🔒 [Hard Ground Lock] ตอนยืน เท้าต้องติดพื้นจริงเสมอ — kinematic + ปักจุดเดิม
        /// แล้วรั้งลำตัวเหมือนเชือกล่ามกับจุดปัก: ก้าวขาเดียวได้ไกลสุดหนึ่งช่วงขาแล้วหยุด อยากไปต่อต้องสลับขา
        /// </summary>
        private void Plant()
        {
            Rigidbody torsoBody = TorsoBody;
            if (torsoBody == null || IsTorsoDown) return;

            // ความเร็วตอนเท้าลง ต้องอ่านก่อนสลับเป็น kinematic
            float landingSpeed = body.isKinematic ? 0f : Mathf.Max(0f, -body.linearVelocity.y);
            body.isKinematic = true;

            if (!isPlantedSet)
            {
                Vector3 groundPos;
                if (Physics.Raycast(body.position + Vector3.up * 2f, Vector3.down, out RaycastHit hit, standingGroundRayLength, groundLayer))
                    groundPos = hit.point;
                else if (Physics.Raycast(pivot.position, Vector3.down, out RaycastHit pivotHit, standingGroundRayLength, groundLayer))
                    groundPos = new Vector3(body.position.x, pivotHit.point.y, body.position.z);
                else
                    groundPos = body.position;

                // จุดปักต้องอยู่ในระยะที่ขาเอื้อมถึงเสมอ — กันขาเหยียดค้างตั้งแต่แรก
                plantedPosition = ClampPlantWithinReach(groundPos);
                // เท้าลงพื้นชี้ตรงไปทางลำตัว แล้วค้างทิศนั้นจนกว่าจะก้าวใหม่ — ทิศเดิมที่ไหลมาจากก้าวก่อนสะสมจนขาบิด
                plantedYaw = lockFootYaw ? LandingYaw() : body.rotation.eulerAngles.y;
                landingFromSwing = false;
                body.rotation = Quaternion.Euler(0f, plantedYaw, 0f);
                isPlantedSet = true;
                TeleportFootTo(plantedPosition + Vector3.up * footThicknessOffset);
                BroadcastFootstep(landingSpeed);
            }

            body.MovePosition(plantedPosition + Vector3.up * footThicknessOffset);
            body.rotation = Quaternion.Euler(0f, plantedYaw, 0f);

            Vector3 offset = (balanceShiftPos - pivot.position) * balanceShiftMultiplier;
            // ดึงสะโพกไปที่ความสูงยืนย่อเข่า — ดึงถึงความยาวขาเต็มแล้วขาเหยียดตรงตลอด ก้าวไม่ออก
            Vector3 pullDir = (body.position + Vector3.up * StandingHipHeight + offset) - pivot.position;
            torsoBody.AddForceAtPosition(pullDir * standingUpwardPull, pivot.position, ForceMode.Acceleration);

            // 🪢 [Anchor Tether] ลำตัวถูกลากเกินระยะขาที่ปัก → สปริงดึงกลับ + หน่วงเฉพาะความเร็วขาออก
            Vector3 plantedWorld = plantedPosition + Vector3.up * footThicknessOffset;
            Vector3 away = pivot.position - plantedWorld;
            Vector2 flatAway = new Vector2(away.x, away.z);
            float excess = flatAway.magnitude - maxLegLength;

            if (excess > 0f && flatAway.sqrMagnitude > 0.0001f)
            {
                Vector3 pullBackDir = new Vector3(-flatAway.x, 0f, -flatAway.y).normalized;
                Vector3 torsoVelocity = torsoBody.linearVelocity;
                float outwardSpeed = Vector3.Dot(new Vector3(torsoVelocity.x, 0f, torsoVelocity.z), -pullBackDir);

                Vector3 tether = pullBackDir * (excess * legStretchSpring);
                if (outwardSpeed > 0f) tether += pullBackDir * (outwardSpeed * legStretchDamper);
                torsoBody.AddForce(tether, ForceMode.Acceleration);
            }
        }

        /// <summary>
        /// รัศมีแนวราบสูงสุด = maxLegLength ตรงๆ
        /// ⚠️ ห้ามหักความสูงสะโพกแบบ Pythagoras — สปริงพยุงลอยสะโพกเกือบเท่าความยาวขา
        /// ระยะแนวราบจะเหลือ ~0 แล้วจุดปักโดนดึงมาใต้ตัว = "เดินไม่ไปข้างหน้า"
        /// </summary>
        private Vector3 ClampPlantWithinReach(Vector3 groundPos)
        {
            Vector3 delta = groundPos - pivot.position;
            Vector2 flat = new Vector2(delta.x, delta.z);
            float maxHorizontal = maxLegLength * PlantReachSafety;
            if (flat.magnitude <= maxHorizontal) return groundPos;

            Vector2 clamped = flat.normalized * maxHorizontal;
            Vector3 target = new Vector3(pivot.position.x + clamped.x, groundPos.y, pivot.position.z + clamped.y);

            // จุดใหม่อาจอยู่คนละระดับพื้น (ทางลาด/ขอบ) — หาความสูงพื้นจริงอีกรอบ
            if (Physics.Raycast(target + Vector3.up * 2f, Vector3.down, out RaycastHit hit, standingGroundRayLength, groundLayer))
                target.y = hit.point.y;
            return target;
        }

        private void DriveDetached()
        {
            // + ความหนาเท้า — เป้าถูกส่งมาเป็นจุดบนพื้นตรงๆ ไม่ยกขึ้นเท้าจะมุดลงไปอยู่ระดับพื้น
            Vector3 target = detachedTargetPos + Vector3.up * footThicknessOffset;
            DriveToward(target, detachedMoveSpeed, legDamper, maxDetachedSpeed);
        }

        /// <summary>✅ [Host/Client Parity] snap แบบไม่ interpolate ตอนปักเท้าลงพื้นใหม่</summary>
        private void TeleportFootTo(Vector3 worldPosition)
        {
            if (teleportOnPlant && footNetworkTransform != null && footNetworkTransform.IsSpawned)
                footNetworkTransform.Teleport(worldPosition, body.rotation, body.transform.localScale);
        }

        // ================================================================
        //  โซ่ข้อต่อขา
        // ================================================================

        /// <summary>
        /// ปิดการชนเฉพาะ "ขา ↔ ขาอีกข้าง" — ปิดทั้งตัวแล้วตอนล้มลำตัวทะลุขาลงไปกองพื้น ดูหนักผิดปกติ
        /// ตั้งครั้งเดียวเมื่อขาทั้งคู่พร้อม
        /// </summary>
        private void ConfigureLegSelfCollision()
        {
            if (selfCollisionConfigured || !ignoreSelfCollision || limb.Owner == null) return;

            LimbSlot otherSlot = limb.Slot == LimbSlot.LeftLeg ? LimbSlot.RightLeg : LimbSlot.LeftLeg;
            RobotLimb other = limb.Owner.GetLimb(otherSlot);
            if (other == null || !(other.Controller is LegController otherLeg) || otherLeg.Body == null) return;

            List<Collider> mine = ChainColliders(CollectChainBodies());
            List<Collider> theirs = ChainColliders(otherLeg.CollectChainBodies());
            foreach (Collider a in mine)
                foreach (Collider b in theirs)
                    if (a != null && b != null && a != b) Physics.IgnoreCollision(a, b, true);

            selfCollisionConfigured = true;
        }

        private static List<Collider> ChainColliders(List<Rigidbody> chain)
        {
            var colliders = new List<Collider>();
            foreach (Rigidbody chainBody in chain) colliders.AddRange(chainBody.GetComponents<Collider>());
            return colliders;
        }

        /// <summary>คืน true เมื่อเจอข้อต่ออย่างน้อย 1 ตัว (สำเร็จแล้วเลิกเรียกซ้ำ) / false = โซ่ยังไม่พร้อม ลองใหม่</summary>
        private bool MakeLegJointChainUnbreakable()
        {
            bool foundAnyJoint = false;
            foreach (Rigidbody chainBody in CollectChainBodies())
            {
                foreach (Joint joint in chainBody.GetComponents<Joint>())
                {
                    if (joint == null) continue;
                    joint.breakForce = Mathf.Infinity;
                    joint.breakTorque = Mathf.Infinity;
                    foundAnyJoint = true;
                }
            }
            return foundAnyJoint;
        }

        // ================================================================
        //  Respawn
        // ================================================================

        /// <summary>
        /// สำคัญสุดคือล้างจุดปัก — plantedPosition เก่ายังชี้จุดตกเหว ถ้าไม่ล้าง
        /// เฟรมถัดไปจะ MovePosition ลากเท้ากลับไปก้นเหวทันที
        /// </summary>
        public override void ServerResetForRespawn()
        {
            if (!IsServer) return;

            isPlantedSet = false;
            releasedForClimb = false;
            stepping = false;
            swingActive = false;
            jumping = false;
            pushingUp = false;
            jumpCooldownTimer = 0f;
            kick.Reset();
            SyncKickState();

            if (body != null) ResetTargetsToCurrentPose();

            // owner ถือทิศ/ระยะ/สถานะปุ่มไว้เอง — ถ้าไม่สั่งล้าง owner ที่ยังกดคลิกค้างจะไม่ส่ง
            // SetSteppingRpc ซ้ำ (ฝั่งมันไม่เห็นว่าค่าเปลี่ยน) → เท้าค้างไม่ยอมก้าวอีกเลย
            RequestOwnerAimReset();
        }
    }
}
