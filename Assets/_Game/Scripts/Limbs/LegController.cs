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
        // จุดปักเท้าอยู่ในระยะ 90% ของรัศมีที่ขาเอื้อม — maxFootReach แบบ auto ใช้ค่าเดียวกัน
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
        public bool IsWalkStepping => stepping && !jumping && !pushingUp && !kick.IsControllingFoot;

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

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            footNetworkTransform = GetComponent<NetworkTransform>();

            // รันทุกเครื่อง — ฝั่ง owner ใช้ความหนาเท้าและระยะก้าวคำนวณเป้าด้วย
            if (autoMeasureFootThickness) MeasureFootThickness();
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

        /// <summary>ผูกช่วงระยะก้าวเข้ากับความยาวขาจริง — หุ่นสเกล 1.5 หรือ 14 ใช้ค่าเดียวกันได้</summary>
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
        public void SetSteppingRpc(bool on) => stepping = on;

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

            mode = ResolveMode();
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
                    DriveStep();
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
            if (releasedForClimb || stepping || jumping) return LegMode.Stepping;
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

        private void DriveStep()
        {
            body.isKinematic = false;
            Vector3 target = targetFootPos;
            Vector3 fromPivot = target - pivot.position;
            float distance = fromPivot.magnitude;
            if (distance < minLegLength)
                target = pivot.position + (distance > 0.01f ? fromPivot.normalized : pivot.forward) * minLegLength;
            else if (distance > maxLegLength)
                target = pivot.position + fromPivot.normalized * maxLegLength;

            DriveToward(target, footMoveSpeed, legDamper);
        }

        /// <summary>
        /// 🔒 [Hard Ground Lock] ตอนยืน เท้าต้องติดพื้นจริงเสมอ — kinematic + ปักจุดเดิม
        /// แล้วรั้งลำตัวเหมือนเชือกล่ามกับจุดปัก: ก้าวขาเดียวได้ไกลสุดหนึ่งช่วงขาแล้วหยุด อยากไปต่อต้องสลับขา
        /// </summary>
        private void Plant()
        {
            Rigidbody torsoBody = TorsoBody;
            if (torsoBody == null || IsTorsoDown) return;

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
                isPlantedSet = true;
                TeleportFootTo(plantedPosition + Vector3.up * footThicknessOffset);
            }

            body.MovePosition(plantedPosition + Vector3.up * footThicknessOffset);
            body.rotation = Quaternion.Euler(0f, body.rotation.eulerAngles.y, 0f);

            Vector3 offset = (balanceShiftPos - pivot.position) * balanceShiftMultiplier;
            Vector3 pullDir = (body.position + Vector3.up * maxLegLength + offset) - pivot.position;
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
