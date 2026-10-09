using Nsc.Match;
using Nsc.Robots;
using Unity.Netcode;
using UnityEngine;

namespace Nsc.Limbs
{
    /// <summary>
    /// input ของขาฝั่งเจ้าของเท่านั้น: เมาส์ = ทิศก้าว, ล้อ = ระยะก้าว, คลิกซ้ายค้าง = ยกขาก้าว,
    /// Space = กระโดด, Q ค้าง = ยันตัวลุก, Shift = ชาร์จเตะ, R ค้าง = ดึงขาที่หลุดกลับ
    /// ส่งแค่เจตนาผ่าน RPC ของ LegController / LimbAttachment
    ///
    /// ไฟล์นี้ใช้ GUID เดิมของ PlayerLegCombat (คอมโพเนนต์บนเท้าตัวเดิม) — ค่าจูนท่าเตะย้ายไป LegController.kick
    /// </summary>
    [RequireComponent(typeof(LegController))]
    public class LegInput : NetworkBehaviour
    {
        // GetAxis("Mouse ScrollWheel") คืนราว ±0.1 ต่อการหมุนล้อ 1 คลิก
        private const float ScrollNotch = 0.1f;
        private const float AimStickDeadzoneSqr = 0.0025f; // 0.05²
        private const float RpcSendThresholdSqr = 0.05f * 0.05f;

        [Header("Camera (LimbControlBinder ใส่ให้ตอนเริ่มแมตช์)")]
        [SerializeField] private Camera playerCamera;

        [Header("Camera-Independent Aim")]
        [Tooltip("ล็อกเคอร์เซอร์กลางจอแล้วใช้ mouse delta หมุนทิศเท้าในแกนโลก — หันกล้องแล้วทิศไม่กวาดตาม")]
        [SerializeField] private bool useVirtualCursor = true;
        [SerializeField] private float mouseSensitivity = 1.5f;
        [Tooltip("วาด marker ที่จุดเล็งเท้า")]
        [SerializeField] private bool showCrosshair = false;

        [Header("Step (คลิกซ้ายค้าง)")]
        [Tooltip("ความเร็วยกเท้า 'กี่เท่าของ clickLiftHeight ต่อวินาที' — เวลายกเท่ากันทุกสเกลหุ่น")]
        [Min(0.1f)] [SerializeField] private float stepLiftSpeed = 8f;
        [Tooltip("ความสูงที่เท้าลอยพ้นพื้นระหว่างก้าว — ปล่อยคลิกแล้วเป้ากลับลงพื้นแล้วปักเท้า")]
        [Min(0f)] [SerializeField] private float clickLiftHeight = 0.35f;

        [Header("Keys")]
        [SerializeField] private KeyCode kickKey = KeyCode.LeftShift;
        [SerializeField] private KeyCode jumpKey = KeyCode.Space;
        [SerializeField] private KeyCode recoveryKey = KeyCode.Q;
        [SerializeField] private KeyCode pullKey = KeyCode.R;

        private LegController leg;
        private RobotLimb limb;

        // ทิศ (เมาส์) กับระยะ (ล้อ) อิสระจากกันสนิท: หมุนเมาส์ไม่เปลี่ยนระยะ เลื่อนล้อไม่เปลี่ยนทิศ
        private Vector3 footDirection = Vector3.forward;
        private Vector3 footAimStick;
        private float footDistance;
        private float liftOffset;
        private Vector3 footMarkerWorld;

        private Vector3 lastSentTarget, lastSentBalance, lastSentDetached;
        private bool stepping;
        private bool pushingUp;
        private bool jumpLock;
        private bool ignoreStepUntilRelease;
        private bool pullRequested;

        private bool localCharging;
        private float localChargeStartedAt;

        private static GUIStyle markerStyle;
        private static GUIStyle reachStyle;

        public Camera PlayerCamera => playerCamera;
        public string PullKeyLabel => pullKey.ToString();
        public Vector3 FootDirection => footDirection;
        /// <summary>[เจ้าของ] กดคลิกซ้ายค้างยกขาก้าวอยู่ — รู้ทันทีไม่ต้องรอ server</summary>
        public bool IsStepping => stepping;
        public float CurrentFootDistance => footDistance;
        public float NormalizedFootReach => leg.MaxFootReach > leg.MinFootReach
            ? Mathf.Clamp01((footDistance - leg.MinFootReach) / (leg.MaxFootReach - leg.MinFootReach))
            : 0f;

        public bool IsCharging => localCharging || leg.CurrentKickState == KickState.Charging;

        /// <summary>ระดับชาร์จที่กดค้างอยู่ 0..1 (ฝั่งเจ้าของ)</summary>
        public float NormalizedKickCharge => localCharging
            ? Mathf.Clamp01((Time.unscaledTime - localChargeStartedAt) / Mathf.Max(0.05f, leg.Kick.chargeTime))
            : 0f;

        /// <summary>HUD โชว์แรงจริง ไม่ใช่แค่เวลาที่กด — ขาที่บาดเจ็บเติมเกจได้ไม่เต็ม</summary>
        public float EffectiveNormalizedKickCharge => NormalizedKickCharge * leg.Kick.HealthPowerMultiplier;

        /// <summary>LimbControlBinder ผูกกล้อง = เปิดให้ขานี้รับ input / null = ปิด</summary>
        public void Bind(Camera camera) => playerCamera = camera;

        private void Awake()
        {
            leg = GetComponent<LegController>();
            limb = GetComponent<RobotLimb>();
            leg.AimResetRequested += OnAimResetRequested;
        }

        public override void OnDestroy()
        {
            if (leg != null) leg.AimResetRequested -= OnAimResetRequested;
            ReleaseCursorIfOwner();
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            ResetFootAim();
        }

        public override void OnNetworkDespawn()
        {
            localCharging = false;
            ReleaseCursorIfOwner();
            base.OnNetworkDespawn();
        }

        private void ReleaseCursorIfOwner()
        {
            if (IsOwner && useVirtualCursor && playerCamera != null) LocalAim.Release();
        }

        private void Update()
        {
            if (!IsOwner || leg.Body == null || leg.Pivot == null) return;

            HandlePullBack();
            if (playerCamera == null) return;

            HandleFootInput();
            HandleKickInput();
        }

        // ================================================================
        //  เท้า
        // ================================================================

        private void HandleFootInput()
        {
            if (useVirtualCursor && LocalAim.HandleCursorLock())
            {
                SeedAimFromFootPose();
                ignoreStepUntilRelease = true; // คลิกนี้ใช้ดึงเมาส์กลับ ห้ามเริ่มก้าว
            }

            // เคอร์เซอร์ปลดอยู่ (Esc ไปเมนู) → หยุดรับ input เท้า
            // ✅ เคลียร์ state ที่ค้างก่อน ไม่งั้น server ไม่มีวันได้คำสั่งหยุด เท้าเดินค้างตลอดที่อยู่ในเมนู
            if (useVirtualCursor && !LocalAim.IsLocked)
            {
                SetStepping(false);
                SetPushingUp(false);
                return;
            }

            UpdateFootAim();
            Vector3 aimOffset = footDirection * footDistance;
            Vector3 hip = leg.Pivot.position;
            LayerMask ground = leg.GroundLayer;

            if (!limb.IsAttached)
            {
                // เท้าหลุด: ฐานจุดเล็งต้องเป็นสะโพก (จุดนิ่ง) ห้ามใช้ตัวเท้า ไม่งั้นเป้าวิ่งหนีตามเท้าจนปลิวหาย
                Vector3 offset = aimOffset * leg.DetachedReachMultiplier;
                if (Physics.Raycast(hip + offset + Vector3.up * 5f, Vector3.down, out RaycastHit detachedHit, 10f, ground))
                {
                    footMarkerWorld = detachedHit.point;
                    if ((detachedHit.point - lastSentDetached).sqrMagnitude > RpcSendThresholdSqr)
                    {
                        lastSentDetached = detachedHit.point;
                        leg.SetDetachedTargetRpc(detachedHit.point);
                    }
                }
                return;
            }

            footMarkerWorld = Physics.Raycast(hip + aimOffset + Vector3.up * 5f, Vector3.down, out RaycastHit markerHit, 60f, ground)
                ? markerHit.point
                : hip + aimOffset;

            // จุดถ่ายน้ำหนักตามทิศที่เล็ง แต่คุมด้วยรัศมีเดิม — ค่านี้ถูกคูณเป็นแรงดันลำตัว โตแล้วท่ายืนเพี้ยน
            Vector3 balance = hip + Vector3.ClampMagnitude(aimOffset, leg.BalanceReach);
            if ((balance - lastSentBalance).sqrMagnitude > RpcSendThresholdSqr)
            {
                lastSentBalance = balance;
                leg.SetBalanceShiftRpc(balance);
            }

            if (leg.TorsoDown) HandleDownedFoot(hip, aimOffset, ground);
            else HandleStandingFoot(hip, aimOffset, ground);
        }

        private void HandleStandingFoot(Vector3 hip, Vector3 aimOffset, LayerMask ground)
        {
            bool grounded = leg.IsGrounded;
            if (!jumpLock && Input.GetKeyDown(jumpKey) && grounded)
            {
                jumpLock = true;
                leg.JumpRpc();
            }
            if (jumpLock && grounded) jumpLock = false;

            bool holdingClick = Input.GetMouseButton(0);
            if (ignoreStepUntilRelease)
            {
                if (!holdingClick) ignoreStepUntilRelease = false;
                holdingClick = false;
            }

            // คลิกซ้ายค้าง = ยกขาแล้วเคลื่อนเท้าไปยังเป้า | ปล่อย = ปักเท้าลง
            if (holdingClick != stepping)
            {
                SetStepping(holdingClick);
                liftOffset = 0f; // เริ่มจากระดับพื้นแล้วไต่ขึ้นเอง ไม่ snap
            }

            if (stepping)
            {
                // ⬆️ [Auto Lift] ผู้เล่นเลือกแค่ทิศกับระยะ ส่วนแกน Y เป็นหน้าที่ของระบบ
                float liftTarget = Mathf.Clamp(clickLiftHeight, 0f, leg.MaxLegLength);
                liftOffset = Mathf.MoveTowards(liftOffset, liftTarget, liftTarget * stepLiftSpeed * Time.deltaTime);

                Vector3 planar = hip + aimOffset;
                Vector3 target = Physics.Raycast(planar + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 50f, ground)
                    ? new Vector3(planar.x, hit.point.y + liftOffset + leg.FootThickness, planar.z)
                    : planar + Vector3.down * leg.MaxLegLength;
                SendFootTarget(target);
            }

            SetPushingUp(false);
        }

        private void HandleDownedFoot(Vector3 hip, Vector3 aimOffset, LayerMask ground)
        {
            if (stepping)
            {
                SetStepping(false);
                liftOffset = 0f;
            }

            // + ความหนาเท้า ไม่งั้นเท้ามุดพื้นตอนล้ม
            Vector3 target = Physics.Raycast(hip + aimOffset + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 10f, ground)
                ? hit.point + Vector3.up * leg.FootThickness
                : hip + aimOffset;
            SendFootTarget(target);

            // กด Q และเท้าเหยียบพื้นอยู่ = ลุกได้ทันที (ไม่มีเงื่อนไขระยะ)
            SetPushingUp(Input.GetKey(recoveryKey) && leg.IsGrounded);
        }

        private void SendFootTarget(Vector3 target)
        {
            if ((target - lastSentTarget).sqrMagnitude <= RpcSendThresholdSqr) return;
            lastSentTarget = target;
            leg.SetFootTargetRpc(target);
        }

        private void SetStepping(bool on)
        {
            if (stepping == on) return;
            stepping = on;
            if (!on) liftOffset = 0f;
            leg.SetSteppingRpc(on);
        }

        private void SetPushingUp(bool on)
        {
            if (pushingUp == on) return;
            pushingUp = on;
            leg.SetPushingUpRpc(on);
        }

        /// <summary>
        /// ทิศ (เมาส์) + ระยะ (ล้อ) — ทิศเก็บในแกนโลก หันกล้องแล้วเท้าไม่กวาดตาม
        /// คลิกขวาค้าง = เมาส์และล้อเป็นของกล้องล้วน ทิศ/ระยะค้างไว้เป๊ะ ปล่อยแล้วคุมต่อจากเดิม
        /// </summary>
        private void UpdateFootAim()
        {
            Transform cameraTransform = playerCamera.transform;
            Vector3 cameraForward = cameraTransform.forward; cameraForward.y = 0f;
            Vector3 cameraRight = cameraTransform.right; cameraRight.y = 0f;
            // กล้องก้มดิ่ง 90° → forward แบนเหลือ ~0 ใช้ up แทนทิศ "หน้าจอด้านบน"
            if (cameraForward.sqrMagnitude < 0.0001f)
            {
                cameraForward = cameraTransform.up; cameraForward.y = 0f;
                if (cameraForward.sqrMagnitude < 0.0001f) cameraForward = Vector3.forward;
            }
            cameraForward.Normalize();
            cameraRight.Normalize();

            bool cameraMode = LocalAim.CameraModeHeld;

            if (useVirtualCursor && LocalAim.IsLocked)
            {
                if (!cameraMode)
                {
                    Vector2 delta = LocalAim.MouseDelta(mouseSensitivity);
                    footAimStick += cameraRight * delta.x + cameraForward * delta.y;
                    footAimStick.y = 0f;
                    // ก้านถูก clamp ที่รัศมี 1 — ดันจนสุดขอบแล้วการขยับต่อ = หมุนทิศรอบตัว
                    if (footAimStick.sqrMagnitude > 1f) footAimStick.Normalize();
                }
            }
            else
            {
                // fallback: ไม่ได้ใช้ virtual cursor → ตำแหน่งเมาส์บนจอเป็นก้านทิศทาง
                Vector2 mouse = LocalAim.AimNormalized;
                Vector3 stick = cameraRight * mouse.x + cameraForward * mouse.y;
                stick.y = 0f;
                footAimStick = stick.sqrMagnitude > 1f ? stick.normalized : stick;
            }

            // ก้านอยู่กลาง = คงทิศเดิม ห้ามสะบัดมั่วตอนผู้เล่นหยุดมือ
            if (footAimStick.sqrMagnitude > AimStickDeadzoneSqr) footDirection = footAimStick.normalized;

            if (!cameraMode)
            {
                float scroll = Input.GetAxis("Mouse ScrollWheel");
                if (Mathf.Abs(scroll) > 0.0001f)
                    footDistance += scroll / ScrollNotch * leg.FootReachScrollSpeed * (leg.MaxFootReach - leg.MinFootReach);

                // จองล้อทุกเฟรมที่ขาถือสิทธิ์ — ธงนิ่ง กล้องไม่แอบซูมแทรก
                MouseWheelFocus.Claim();
            }

            footDistance = Mathf.Clamp(footDistance, leg.MinFootReach, leg.MaxFootReach);
        }

        /// <summary>เริ่มทิศ+ระยะจากท่าเท้าจริง ณ ตอนล็อกเมาส์ — เท้าไม่กระโดดตำแหน่ง</summary>
        private void SeedAimFromFootPose()
        {
            Vector3 offset = leg.Body.position - leg.Pivot.position;
            Vector3 planar = new Vector3(offset.x, 0f, offset.z);
            if (planar.sqrMagnitude <= 0.0001f) return;

            footDirection = planar.normalized;
            footAimStick = footDirection;
            footDistance = Mathf.Clamp(planar.magnitude, leg.MinFootReach, leg.MaxFootReach);
        }

        private void ResetFootAim()
        {
            footDistance = Mathf.Lerp(leg.MinFootReach, leg.MaxFootReach, leg.DefaultFootReach);
            liftOffset = 0f;

            Vector3 forward = leg.Pivot != null ? leg.Pivot.forward : transform.forward;
            forward.y = 0f;
            footDirection = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            footAimStick = footDirection;
        }

        private void OnAimResetRequested()
        {
            stepping = false;
            pushingUp = false;
            localCharging = false;
            ignoreStepUntilRelease = true; // ต้องปล่อยคลิกซ้ายก่อน ถึงจะเริ่มก้าวใหม่ได้
            ResetFootAim();
            lastSentTarget = lastSentBalance = lastSentDetached = Vector3.positiveInfinity;
        }

        /// <summary>ทิศเตะ = ทิศที่เล็งไว้ตรงๆ (แนวราบล้วน — เงยขึ้นแล้วทั้งหุ่นกระโดดแทนที่จะเป็นการถีบ)</summary>
        private Vector3 KickAimDirection()
        {
            if (footDirection.sqrMagnitude > 0.001f) return footDirection.normalized;
            Vector3 forward = leg.Pivot.forward;
            forward.y = 0f;
            return forward.sqrMagnitude < 0.001f ? Vector3.forward : forward.normalized;
        }

        // ================================================================
        //  เตะ (Shift) — กดชาร์จ ปล่อยเตะ ไม่ต้องยกเท้าก่อน
        // ================================================================

        private void HandleKickInput()
        {
            bool canKick = CanKickLocally();

            if (!localCharging && canKick && Input.GetKeyDown(kickKey))
            {
                localCharging = true;
                localChargeStartedAt = Time.unscaledTime;
                // ส่งทิศเล็งตอนเริ่มชาร์จไปด้วย — server ใช้กำหนดทิศง้างถอยหลัง
                leg.StartKickRpc(KickAimDirection());
            }

            if (!localCharging) return;

            if (!canKick)
            {
                localCharging = false;
                leg.CancelKickRpc();
                return;
            }

            if (Input.GetKeyUp(kickKey))
            {
                localCharging = false;
                leg.ReleaseKickRpc(KickAimDirection());
            }
        }

        private bool CanKickLocally()
        {
            if (!GameplayGate.CanAct) return false;
            if (useVirtualCursor && !LocalAim.IsLocked) return false;
            if (!limb.IsAttached || leg.TorsoDown) return false;

            // วงจร กด→ชาร์จ→ปล่อย บังคับให้ต้องปล่อย Shift ก่อนเตะใหม่อยู่แล้ว server ยังกันซ้ำอีกชั้น
            KickState state = leg.CurrentKickState;
            return state == KickState.Idle || (localCharging && state == KickState.Charging);
        }

        // ================================================================
        //  ดึงขาที่หลุดกลับ (R ค้าง)
        // ================================================================

        private void HandlePullBack()
        {
            LimbAttachment attachment = limb.Attachment;
            if (attachment == null) return;

            if (attachment.IsAttached)
            {
                pullRequested = false;
                return;
            }

            bool wanted = attachment.RemainingReconnectCooldown <= 0f && Input.GetKey(pullKey);
            if (wanted == pullRequested) return;
            pullRequested = wanted;
            attachment.RequestPullBack(wanted);
        }

        // ================================================================
        //  Marker ที่จุดเล็งเท้า + ระยะที่เลือก (%)
        // ================================================================

        private void OnGUI()
        {
            if (!IsOwner || !useVirtualCursor || !showCrosshair || playerCamera == null || !LocalAim.IsLocked) return;

            markerStyle ??= new GUIStyle(GUI.skin.label)
                { fontSize = 26, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            reachStyle ??= new GUIStyle(GUI.skin.label)
                { fontSize = 13, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };

            Vector3 screen = playerCamera.WorldToScreenPoint(footMarkerWorld);
            if (screen.z <= 0f) return;
            float x = screen.x, y = Screen.height - screen.y;

            Color shadow = new Color(0f, 0f, 0f, 0.6f), tint = new Color(0.5f, 0.85f, 1f);
            GUI.color = shadow;
            GUI.Label(new Rect(x - 13f, y - 13f, 30f, 30f), "◈", markerStyle);
            GUI.color = tint;
            GUI.Label(new Rect(x - 14f, y - 14f, 30f, 30f), "◈", markerStyle);

            // ไม่มีตัวเลขนี้ผู้เล่นไม่รู้ว่าหมุนล้อไปถึงไหนแล้ว
            string reach = $"{Mathf.RoundToInt(NormalizedFootReach * 100f)}%";
            GUI.color = shadow;
            GUI.Label(new Rect(x - 29f, y + 13f, 60f, 20f), reach, reachStyle);
            GUI.color = tint;
            GUI.Label(new Rect(x - 30f, y + 12f, 60f, 20f), reach, reachStyle);
        }
    }
}
