using System;
using Nsc.Limbs;
using Unity.Netcode;
using UnityEngine;

namespace Nsc.Robots
{
    /// <summary>
    /// ลำตัวของหุ่น: ตัดสินว่ายืนหรือล้ม (ผ่าน FallRules) และออกแรงพยุงตัว กระโดด ลุก
    ///
    /// เดิมชื่อ TorsoMovement — ไฟล์นี้ใช้ GUID เดิม prefab จึงไม่หลุด
    /// สิ่งที่เปลี่ยน:
    ///   • ไม่มีการลงทะเบียนเท้า/มือแล้ว อ่านแขนขาทั้ง 4 ชิ้นจาก Robot ทุก tick
    ///   • คลาสอื่นสั่งล้มได้ทางเดียวคือ ServerKnockDown(reason) — ห้ามเขียน state ตรงๆ
    ///   • แรงดึงตอนปีนอ่านจาก ArmController.ClimbPull เอง (ค่าที่มากกว่าของสองแขน)
    ///     เดิมแขนทั้งสองเขียนทับค่าเดียวกันทุก tick ค่าที่ใช้จริงจึงเป็นของแขนที่รันทีหลัง
    /// </summary>
    public class TorsoBalance : NetworkBehaviour
    {
        // ลำดับการเรียกใน FixedUpdate ต้องคงเดิม — ค่าที่ prefab ใช้จริง: stress 50, เอียง 45°, ลอย 0.5 วิ
        private const float LandingCheckDelay = 0.15f;

        [Header("Network State")]
        [Tooltip("ล้มอยู่ไหม — ค่าเริ่มต้นใน prefab คือ true (หุ่นเกิดมานอนอยู่ ต้องกด Q ลุก)")]
        [SerializeField]
        private NetworkVariable<bool> isRagdoll = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        [Header("Fake Hover & Posture")]
        [SerializeField] private float targetTorsoHeight = 1.6f;
        [SerializeField] private float heightSpringForce = 300f;
        [SerializeField] private float heightDamper = 30f;
        [SerializeField] private float uprightSpring = 800f;
        [SerializeField] private float uprightDamper = 60f;
        [SerializeField] private float autoCenterGravityForce = 250f;
        [SerializeField] private float minCenterForceMultiplier = 0.15f;

        [Header("Fall Rules")]
        [SerializeField] private FallRules rules = new FallRules();

        [Header("Break Force (Stress)")]
        [SerializeField] private float stressDecayRate = 100f;

        [Header("Ragdoll Recovery (Q)")]
        [Tooltip("แรงดึงลำตัวให้ตั้งตรงขณะกดลุก (ช่วยให้ไม่ลอยขึ้นแล้วยังล้มอยู่)")]
        [SerializeField] private float recoveryUprightTorque = 600f;
        [Tooltip("สัดส่วนความสูงที่ต้องถึงก่อน snap กลับเป็นยืน (0.5 = ครึ่งความสูงปกติ)")]
        [SerializeField] private float recoveryHeightThreshold = 0.5f;
        [SerializeField] private float continuousRecoveryForce = 800f;
        [Tooltip("ความเร็วขาขึ้นสูงสุดที่ยอมให้แรงลุกดันต่อ (m/s) — กันหุ่นพุ่งฟ้าตอนหลายคนกด Q พร้อมกัน")]
        [SerializeField] private float maxRecoveryUpVelocity = 12f;
        [Tooltip("ติ๊ก = เท้าข้างเดียวยันพื้นก็ดีดกลับมายืนได้ทันที | เอาออก = ต้องสูงถึงเกณฑ์ก่อน")]
        [SerializeField] private bool testSingleFootRecovery = true;

        [Header("Jump System (Co-op)")]
        [Tooltip("หลังกระโดด พักการตัดสินล้มจากสถานะเท้าไว้นานเท่านี้ — ลอยเพราะกระโดด ≠ กำลังล้ม")]
        [SerializeField] private float jumpGraceDuration = 1.2f;
        [Tooltip("สองขากด Space ห่างกันไม่เกินนี้ = กระโดดพร้อมกัน (ได้แรงโบนัส)")]
        [SerializeField] private float coopJumpWindow = 0.3f;
        [SerializeField] private float soloJumpForce = 250f;
        [SerializeField] private float coopJumpBonusForce = 450f;
        [Tooltip("เพดานความเร็วขาขึ้นจากการกระโดด — แรงซ้อนกี่ทางก็ไม่ทะลุ")]
        [SerializeField] private float maxJumpUpVelocity = 10f;

        [Header("Landing Assist")]
        [SerializeField] private float landingAssistDuration = 0.4f;
        [SerializeField] private float landingAssistDamping = 4f;

        [Header("References")]
        [SerializeField] private Rigidbody torsoRb;
        [SerializeField] private Transform groundRaycastOrigin;
        [SerializeField] private LayerMask groundLayer;
        [SerializeField] private ConfigurableJoint[] hipJoints;

        private Robot robot;
        private float stress;
        private float jumpGraceTimer;
        private float landingAssistTimer;
        private LimbSlot? pendingJumpSlot;
        private float pendingJumpTime = -999f;
        private float lastRecoveryTick = -1f;
        private bool? lastHipJointRagdoll;
        private Vector3 groundedFootCenter;

        /// <summary>ยิงบนทุกเครื่องเมื่อหุ่นล้ม (true) หรือลุกขึ้น (false)</summary>
        public event Action<bool> RagdollChanged;

        public bool IsRagdoll => isRagdoll.Value;
        public Rigidbody Body => torsoRb;
        public float Stress => stress;
        public float MaxStress => rules.maxStress;
        public float MaxRecoveryUpVelocity => maxRecoveryUpVelocity;
        public FallRules Rules => rules;

        private void Awake()
        {
            robot = GetComponent<Robot>();
            if (robot == null)
                Debug.LogError($"[TorsoBalance] '{name}' ไม่มี Robot อยู่บน GameObject เดียวกัน — อ่านแขนขาไม่ได้", this);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            isRagdoll.OnValueChanged += OnRagdollValueChanged;
        }

        public override void OnNetworkDespawn()
        {
            isRagdoll.OnValueChanged -= OnRagdollValueChanged;
            base.OnNetworkDespawn();
        }

        private void OnRagdollValueChanged(bool previous, bool current) => RagdollChanged?.Invoke(current);

        private void FixedUpdate()
        {
            if (!IsServer || torsoRb == null) return;

            float dt = Time.fixedDeltaTime;
            stress = Mathf.Max(0f, stress - stressDecayRate * dt);
            TickJumpAndLanding(dt);

            if (lastHipJointRagdoll != isRagdoll.Value)
            {
                lastHipJointRagdoll = isRagdoll.Value;
                UnlockHipJoints();
            }

            // ✅ [ตามดีไซน์] ล้มแล้วต้องกด Q เท่านั้นถึงลุกได้ — ไม่มีลุกอัตโนมัติ
            if (isRagdoll.Value) return;

            BalanceInput input = ReadBalanceInput();
            FallReason reason = rules.Evaluate(input, dt);
            if (reason != FallReason.None)
            {
                ServerKnockDown(reason);
                return;
            }

            ApplyPosture(input);
        }

        // ================================================================
        //  อ่านสถานะแขนขาจาก Robot (ไม่มีรายชื่อที่ต้องลงทะเบียน/ถอนเอง)
        // ================================================================

        private BalanceInput ReadBalanceInput()
        {
            BalanceInput input = new BalanceInput
            {
                stress = stress,
                tiltAngle = Vector3.Angle(torsoRb.transform.up, Vector3.up),
                jumpProtected = jumpGraceTimer > 0f || landingAssistTimer > 0f,
                handSupport = HasSupportingHandGrab()
            };

            Vector3 footSum = Vector3.zero;
            foreach (LimbSlot slot in LegSlots)
            {
                if (!TryGetCountedLeg(slot, out LegController leg)) continue;

                input.footCount++;
                if (leg.IsWalkStepping) input.walkSteppingFeet++;
                // เฉพาะช่วงเตะจริงซึ่งจบเองใน ~0.5 วิ — จงใจไม่รวมช่วงง้าง
                // ไม่งั้นกด Shift ค้างจะกลายเป็นเกราะกันล้มถาวร
                if (leg.IsKickMotionActive) input.kickInProgress = true;
                if (leg.IsBalanced) input.balancedFeet++;
                if (!leg.IsGrounded || leg.Body == null) continue;

                input.groundedFeet++;
                footSum += leg.Body.position;
                if (leg.CanActAsSupport) input.supportFoot = true;
            }

            groundedFootCenter = input.groundedFeet > 0 ? footSum / input.groundedFeet : Vector3.zero;
            return input;
        }

        private static readonly LimbSlot[] LegSlots = { LimbSlot.LeftLeg, LimbSlot.RightLeg };
        private static readonly LimbSlot[] ArmSlots = { LimbSlot.LeftArm, LimbSlot.RightArm };

        private bool TryGetCountedLeg(LimbSlot slot, out LegController leg)
        {
            leg = null;
            RobotLimb limb = robot != null ? robot.GetLimb(slot) : null;
            if (limb == null || !rules.countDetachedLegs && !limb.IsAttached) return false;

            leg = limb.Controller as LegController;
            return leg != null;
        }

        private bool TryGetArm(LimbSlot slot, out ArmController arm)
        {
            RobotLimb limb = robot != null ? robot.GetLimb(slot) : null;
            arm = limb != null ? limb.Controller as ArmController : null;
            return arm != null;
        }

        private bool HasSupportingHandGrab()
        {
            foreach (LimbSlot slot in ArmSlots)
                if (TryGetArm(slot, out ArmController arm) && arm.Grip != null && arm.Grip.IsSupporting)
                    return true;
            return false;
        }

        /// <summary>แรงดึงตอนปีน 0..1 — ค่าที่มากกว่าของสองแขน (แขนที่ไม่ได้ปีนคืน 0)</summary>
        private float ClimbPull()
        {
            float pull = 0f;
            foreach (LimbSlot slot in ArmSlots)
                if (TryGetArm(slot, out ArmController arm))
                    pull = Mathf.Max(pull, arm.ClimbPull);
            return pull;
        }

        // ================================================================
        //  แรงพยุงตัว (ทำเฉพาะตอนยืนและกฎการล้มไม่ทำงานใน tick นี้)
        // ================================================================

        private void ApplyPosture(in BalanceInput input)
        {
            // ⚖️ ดึงลำตัวให้อยู่เหนือฐานเท้า — ทำงานทุกครั้งที่มีเท้าแตะพื้น
            if (input.groundedFeet > 0)
            {
                Vector3 flatError = new Vector3(groundedFootCenter.x - torsoRb.position.x, 0f,
                                                groundedFootCenter.z - torsoRb.position.z);
                float centerScale = Mathf.Lerp(1f, minCenterForceMultiplier, ClimbPull());
                torsoRb.AddForce(flatError * (autoCenterGravityForce * centerScale), ForceMode.Acceleration);
            }

            // ✅ [Fly-away Fix] ชดเชยแรงโน้มถ่วงเฉพาะตอนมีพื้นในระยะ — หลุดพ้นพื้นแล้วตกตามธรรมชาติ
            // 🦘 ปิดสปริงความสูงระหว่างช่วงกระโดด ไม่งั้นแรงกระโดดโดนกดกลับตั้งแต่เฟรมแรก
            if (jumpGraceTimer <= 0f && TryGetGroundHeight(out float height))
            {
                torsoRb.AddForce(-Physics.gravity, ForceMode.Acceleration);
                float heightError = targetTorsoHeight - height;
                torsoRb.AddForce(Vector3.up * ((heightError * heightSpringForce) - (torsoRb.linearVelocity.y * heightDamper)),
                                 ForceMode.Acceleration);
            }

            if (TryGetUprightCorrection(out float angle, out Vector3 axis))
                torsoRb.AddTorque((axis * (angle * uprightSpring)) - (torsoRb.angularVelocity * uprightDamper), ForceMode.Acceleration);
        }

        private bool TryGetGroundHeight(out float height)
        {
            height = 0f;
            if (groundRaycastOrigin == null) return false;
            if (!Physics.Raycast(groundRaycastOrigin.position, Vector3.down, out RaycastHit hit,
                                 targetTorsoHeight * 2f, groundLayer))
                return false;

            height = groundRaycastOrigin.position.y - hit.point.y;
            return true;
        }

        private bool TryGetUprightCorrection(out float angle, out Vector3 axis)
        {
            Quaternion targetRot = Quaternion.Euler(0f, torsoRb.rotation.eulerAngles.y, 0f);
            Quaternion deltaRot = targetRot * Quaternion.Inverse(torsoRb.rotation);
            deltaRot.ToAngleAxis(out angle, out axis);
            if (angle > 180f) angle -= 360f;
            return Mathf.Abs(angle) > 0.01f;
        }

        // ================================================================
        //  กระโดด
        // ================================================================

        private void TickJumpAndLanding(float dt)
        {
            if (jumpGraceTimer > 0f)
            {
                jumpGraceTimer -= dt;

                // ลงพื้นแล้ว → จบ grace เริ่ม landing assist
                // ⚠️ เริ่มเช็คหลังพ้นช่วงต้นของ grace — เฟรมแรกๆ แรงกระโดดยังไม่ทันออกฤทธิ์
                float graceElapsed = jumpGraceDuration - jumpGraceTimer;
                if (graceElapsed >= LandingCheckDelay && torsoRb.linearVelocity.y <= 0.5f && AnyFootGrounded())
                {
                    jumpGraceTimer = 0f;
                    landingAssistTimer = landingAssistDuration;
                }
            }
            else if (landingAssistTimer > 0f)
            {
                landingAssistTimer -= dt;
                // หน่วงความเร็วลำตัวช่วงสั้นๆ หลังลงพื้น — กันเด้งกลับ/ไถลจนเสียสมดุล
                torsoRb.AddForce(-torsoRb.linearVelocity * landingAssistDamping, ForceMode.Acceleration);
            }
        }

        private bool AnyFootGrounded()
        {
            foreach (LimbSlot slot in LegSlots)
                if (TryGetCountedLeg(slot, out LegController leg) && leg.IsGrounded) return true;
            return false;
        }

        /// <summary>
        /// [SERVER] ขาข้างหนึ่งเพิ่งกระโดด — ลำตัวตัดสินว่าเป็น hop ขาเดียว
        /// หรือ Co-op Jump (สองขากดภายใน coopJumpWindow ได้แรงโบนัส) ภายใต้เพดาน maxJumpUpVelocity
        /// </summary>
        public void ServerApplyJump(LimbSlot from)
        {
            if (!IsServer || torsoRb == null || isRagdoll.Value) return;

            // เข้าช่วง grace: การตัดสินล้มจากสถานะเท้าถูกพักไว้จนกว่าจะลงพื้น/หมดเวลา
            jumpGraceTimer = jumpGraceDuration;
            landingAssistTimer = 0f;

            bool isCoopJump = pendingJumpSlot.HasValue && pendingJumpSlot.Value != from &&
                              (Time.fixedTime - pendingJumpTime) <= coopJumpWindow;

            if (isCoopJump)
            {
                pendingJumpSlot = null; // ใช้คู่นี้ไปแล้ว
                ApplyJumpBoost(coopJumpBonusForce);
                Debug.Log("[Server] 🚀 Co-op Jump! สองขากดพร้อมกัน — ได้แรงโบนัส");
            }
            else
            {
                pendingJumpSlot = from;
                pendingJumpTime = Time.fixedTime;
                ApplyJumpBoost(soloJumpForce);
            }
        }

        private void ApplyJumpBoost(float force)
        {
            // คิดเป็น Δv แล้ว clamp ตาม headroom ที่เหลือ — ความเร็วสุดท้ายไม่เกินเพดานเสมอ
            float headroom = maxJumpUpVelocity - torsoRb.linearVelocity.y;
            if (headroom <= 0f) return;

            float deltaV = Mathf.Min(force * Time.fixedDeltaTime, headroom);
            torsoRb.AddForce(Vector3.up * deltaV, ForceMode.VelocityChange);
        }

        // ================================================================
        //  ล้ม / ลุก / respawn
        // ================================================================

        /// <summary>[SERVER] ทางเดียวที่ทำให้หุ่นล้ม — เหตุผลถูก log ไว้ทุกครั้ง</summary>
        public void ServerKnockDown(FallReason reason)
        {
            if (!IsServer || isRagdoll.Value) return;

            isRagdoll.Value = true;
            Debug.Log($"[TorsoBalance] 🤖 '{transform.root.name}' ล้ม — {reason}", this);
        }

        public void ServerAddStress(float amount) =>
            stress = Mathf.Min(stress + amount, rules.maxStress * 1.5f);

        /// <summary>
        /// [SERVER] แรงลุกขึ้นจาก ragdoll (กด Q) — ทำงานครั้งเดียวต่อ physics tick ไม่ว่ากี่คนกด
        /// เดิม 2 เท้า + 2 มือเรียกพร้อมกันได้ แรงคูณหลายเท่า → หุ่นพุ่งขึ้นฟ้า
        /// </summary>
        public void ServerApplyRecoveryPush(Vector3 forcePosition, float strengthMultiplier = 1f)
        {
            if (!IsServer || torsoRb == null || !isRagdoll.Value) return;

            if (Mathf.Approximately(lastRecoveryTick, Time.fixedTime)) return;
            lastRecoveryTick = Time.fixedTime;

            // ตัวกำลังพุ่งขึ้นเร็วอยู่แล้ว → หยุดอัดเพิ่ม กันสะสมความเร็วจนทะยาน
            if (torsoRb.linearVelocity.y > maxRecoveryUpVelocity) return;

            // ดันสะโพกขึ้นแรงๆ ให้งัดตัวเองตั้งไข่ได้ทันทีที่กด + แรงเสริมกลางลำตัว
            torsoRb.AddForceAtPosition(Vector3.up * (continuousRecoveryForce * strengthMultiplier * 2.5f),
                                       forcePosition, ForceMode.Acceleration);
            torsoRb.AddForce(Vector3.up * (continuousRecoveryForce * strengthMultiplier), ForceMode.Acceleration);

            // กันตัวลอยขึ้นแต่ยังเอียงอยู่ → ไม่ผ่านเกณฑ์ความสูงแล้วล้มซ้ำ
            if (TryGetUprightCorrection(out float angle, out Vector3 axis))
                torsoRb.AddTorque(axis * (angle * recoveryUprightTorque), ForceMode.Acceleration);

            if (TryGetGroundHeight(out float height) &&
                (testSingleFootRecovery || height >= targetTorsoHeight * recoveryHeightThreshold))
            {
                rules.Reset(); // กันลุกปุ๊บโดนตัดสินว่าเอียง/ลอยค้างแล้วล้มซ้ำ
                isRagdoll.Value = false;
            }
        }

        /// <summary>[SERVER] หลัง teleport กลับเช็คพอยต์ — ล้าง stress/timer ทั้งหมดแล้วตั้งเป็นท่ายืน</summary>
        public void ServerResetForRespawn()
        {
            if (!IsServer) return;

            stress = 0f;
            rules.Reset();
            jumpGraceTimer = 0f;
            landingAssistTimer = 0f;
            pendingJumpSlot = null;

            if (torsoRb != null && !torsoRb.isKinematic)
            {
                torsoRb.linearVelocity = Vector3.zero;
                torsoRb.angularVelocity = Vector3.zero;
            }

            isRagdoll.Value = false;
        }

        // ปลดล็อก hipJoints เป็น Free เสมอ ไม่ใช้ Locked เพื่อไม่ให้แช่แข็งผิดท่า
        // อาศัย Angular Drive (Spring) ของข้อต่อดึงกลับให้ตรงแทน — ทำเฉพาะตอนสถานะเปลี่ยน
        private void UnlockHipJoints()
        {
            if (hipJoints == null) return;
            foreach (ConfigurableJoint hip in hipJoints)
            {
                if (hip == null) continue;
                hip.angularXMotion = ConfigurableJointMotion.Free;
                hip.angularYMotion = ConfigurableJointMotion.Free;
                hip.angularZMotion = ConfigurableJointMotion.Free;
            }
        }

        private void OnDrawGizmosSelected()
        {
            Transform origin = groundRaycastOrigin != null ? groundRaycastOrigin : transform;
            Vector3 start = origin.position;

            Gizmos.color = new Color(1f, 0.5f, 0f);
            Vector3 end = start + Vector3.down * targetTorsoHeight;
            Gizmos.DrawLine(start, end);
            Gizmos.DrawWireCube(end, new Vector3(1f, 0.05f, 1f));

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(start + Vector3.down * (targetTorsoHeight * recoveryHeightThreshold),
                                new Vector3(0.5f, 0.05f, 0.5f));
        }
    }
}
