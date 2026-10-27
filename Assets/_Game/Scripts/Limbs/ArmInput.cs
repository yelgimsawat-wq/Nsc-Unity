using Nsc.Combat;
using Nsc.Robots;
using Unity.Netcode;
using UnityEngine;

namespace Nsc.Limbs
{
    /// <summary>
    /// input ของแขนฝั่งเจ้าของเท่านั้น: เมาส์ = ลากมือบนระนาบจอ, ล้อ = ยื่นออก/ดึงเข้า
    /// (เปิด pointAndClick = โหมดต่อสู้: ชี้เมาส์ = มือยกการ์ดหันไปทางนั้น),
    /// คลิกซ้าย = ต่อย, F ค้าง = จับ, Q = ลุก, R ค้าง = ดึงแขนที่หลุดกลับ
    /// ส่งแค่เจตนาผ่าน RPC ของ ArmController / ArmGrip / LimbAttachment — ไม่แตะฟิสิกส์เอง
    ///
    /// ทำงานเมื่อเครื่องนี้เป็นเจ้าของ และ LimbControlBinder ผูกกล้องให้แล้ว (Camera != null)
    /// </summary>
    [RequireComponent(typeof(ArmController))]
    public class ArmInput : NetworkBehaviour
    {
        // GetAxis("Mouse ScrollWheel") คืนราว ±0.1 ต่อการหมุนล้อ 1 คลิก
        private const float ScrollNotch = 0.1f;
        // maxHandDepth แบบ auto กันไว้ต่ำกว่าความยาวแขนเล็กน้อย เป้าจะได้ไม่ไปนั่งบนผิว soft-clamp ตลอดเวลา
        private const float ArmDepthSafety = 0.95f;
        private const float RpcSendThreshold = 0.05f;
        private const float PunchRetryInterval = 0.1f;

        [Header("Camera (LimbControlBinder ใส่ให้ตอนเริ่มแมตช์)")]
        [SerializeField] private Camera playerCamera;

        [Header("Combat: ชี้แล้วต่อย (แบบเดียวกับขา)")]
        [Tooltip("ชี้เมาส์ที่ไหน มือยกการ์ดหันไปทางนั้น คลิก = ต่อยไปตรงนั้น — เคอร์เซอร์ไม่ล็อก ไม่ต้องลากมือเอง\n" +
                 "สำหรับโหมดต่อสู้ ปิด = โหมดเดิม (ลากมือบนระนาบจอ + ล้อ) ที่ยังต้องใช้ปีน/จับของ")]
        [SerializeField] private bool pointAndClick = false;
        [Tooltip("สิ่งที่ ray จากเมาส์ชนได้ (ชิ้นส่วนหุ่นตัวเองถูกข้ามให้เอง)")]
        [SerializeField] private LayerMask aimLayers = ~0;

        [Header("Virtual Cursor")]
        [Tooltip("ล็อกเคอร์เซอร์จริงไว้กลางจอแล้วใช้ mouse delta ขยับจุดเล็งเสมือน — ไม่มีขอบจอ")]
        [SerializeField] private bool useVirtualCursor = true;
        [SerializeField] private float mouseSensitivity = 1.5f;
        [Tooltip("วาด crosshair ที่จุดเล็ง ให้เห็นตลอดว่ามือกำลังจะไปไหน")]
        [SerializeField] private bool showCrosshair = true;

        [Header("Mouse Sensitivity (ระนาบหน้าจอ)")]
        [SerializeField] private float mouseReachX = 3f;
        [SerializeField] private float mouseReachY = 3f;
        [Tooltip("สเกลความไวตามระยะกล้อง — 'มือขยับกี่พิกเซลบนจอ' คงที่ทุกระดับซูม")]
        [SerializeField] private bool scaleAimWithCameraDistance = true;
        [Tooltip("ระยะกล้อง (เมตร) ที่ความไว = mouseReachX/Y เป๊ะๆ — แนะนำให้ตรงกับ PlayerCam.maxDistance")]
        [Min(0.1f)] [SerializeField] private float aimReferenceCameraDistance = 20f;
        [Tooltip("กันมือทะลุพื้น")]
        [SerializeField] private LayerMask groundLayer;

        [Header("Hand Depth (ล้อเมาส์ = ยื่นออก/ดึงเข้า)")]
        [Tooltip("คำนวณ min/maxHandDepth จากความยาวแขนให้อัตโนมัติ")]
        [SerializeField] private bool autoHandDepthFromArmLength = true;
        [Min(0.01f)] [SerializeField] private float minHandDepth = 0.3f;
        [Min(0.02f)] [SerializeField] private float maxHandDepth = 1.7f;
        [Range(0f, 1f)] [SerializeField] private float defaultHandDepth = 0.5f;
        [Tooltip("สัดส่วนของช่วง min→max ที่เปลี่ยนต่อการหมุนล้อ 1 คลิก (ค่าต่อเนื่อง ไม่ใช่เป็นขั้น)")]
        [Min(0.001f)] [SerializeField] private float handDepthScrollSpeed = 0.12f;

        [Header("Keys")]
        [SerializeField] private KeyCode grabKey = KeyCode.F;
        [SerializeField] private KeyCode recoveryKey = KeyCode.Q;
        [SerializeField] private KeyCode pullKey = KeyCode.R;
        [Tooltip("กด F ค้างแล้วยังไม่มีอะไรให้จับ → ลองใหม่ทุกๆ กี่วินาที (ไม่สแปม RPC ทุกเฟรม)")]
        [Min(0.05f)] [SerializeField] private float grabRetryInterval = 0.2f;
        [Tooltip("คลิกตอน server ยังอยู่ใน Recovering ของหมัดก่อน → ลองส่งซ้ำได้ภายในช่วงนี้ (ไม่ใช่ต่อยรัว)")]
        [Min(0f)] [SerializeField] private float punchRequestWindow = 0.35f;

        private ArmController arm;
        private ArmGrip grip;
        private RobotLimb limb;

        private Vector3 lastSentTarget;
        private bool ignoreClickUntilRelease;
        private bool grabHeld;
        private float nextGrabRetryTime;
        private bool punchHeld;
        private bool punchRequestPending;
        private float punchRequestExpireTime;
        private float nextPunchRequestTime;
        private bool pullRequested;
        private Vector3 guardDirection = Vector3.forward;
        // เป้าเล็งโหมดต่อสู้ (HUD): ชี้โดนของที่ตีได้ไหม / อยู่ในระยะหมัดไหม / กดง้างมานานเท่าไหร่
        private bool aimOnTarget;
        private bool aimInReach;
        private float chargeStartTime = -1f;
        private static readonly RaycastHit[] AimHits = new RaycastHit[16];
        private static GUIStyle crosshairStyle;

        private bool LocksCursor => useVirtualCursor && !pointAndClick;

        public Camera PlayerCamera => playerCamera;
        /// <summary>แขนนี้อยู่โหมดต่อสู้ (ชี้แล้วต่อย) — PlayerCam ใช้มุมกล้องต่อสู้เฉพาะตอนนี้</summary>
        public bool IsCombatMode => pointAndClick;
        public string PullKeyLabel => pullKey.ToString();

        /// <summary>ระยะไหล่ถึงเป้ามือตอนนี้ (เมตร) และในสเกล 0..1 — สำหรับ HUD</summary>
        public float CurrentHandDepth => LocalAim.ArmOffsetWorld.magnitude;
        public float NormalizedHandDepth => maxHandDepth > minHandDepth
            ? Mathf.Clamp01((CurrentHandDepth - minHandDepth) / (maxHandDepth - minHandDepth))
            : 0f;

        /// <summary>LimbControlBinder ผูกกล้อง = เปิดให้แขนนี้รับ input / null = ปิด</summary>
        public void Bind(Camera camera)
        {
            // ถูกถอดกลางแมตช์ (สลับไปคุมชิ้นอื่น) — ปล่อยของที่จับ/หมัดที่ค้าง ไม่งั้นไม่มีใครส่งคำสั่งปล่อย
            if (camera == null && playerCamera != null && IsSpawned && IsOwner) CancelHeldActions();
            // ได้คุมแขนนี้ (รวมสลับมาจากแขนอีกข้าง) — จุดเล็งที่แชร์กันยังชี้ฝั่งแขนเดิม ต้องเริ่มจากท่ามือนี้
            if (camera != null && playerCamera == null && IsSpawned && IsOwner) SeedAimFromCurrentHandPose();
            playerCamera = camera;
            // ชี้แล้วต่อยต้องเห็นเมาส์ — สลับมาจากชิ้นที่ล็อกเมาส์กลางจอต้องปลดให้
            if (camera != null && pointAndClick && IsOwner)
            {
                LocalAim.Release();
                // เริ่มการ์ดหันไปทางที่กล้องมอง (กล้องอยู่หลังหุ่น) จนกว่าจะขยับเมาส์
                Vector3 forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up);
                if (forward.sqrMagnitude > 0.0001f) guardDirection = forward.normalized;
            }
        }

        private void Awake()
        {
            arm = GetComponent<ArmController>();
            grip = GetComponent<ArmGrip>();
            limb = GetComponent<RobotLimb>();
            arm.AimResetRequested += OnAimResetRequested;
        }

        public override void OnDestroy()
        {
            if (arm != null) arm.AimResetRequested -= OnAimResetRequested;
            ReleaseCursorIfOwner(); // เผื่อถูกทำลายโดยไม่ผ่าน despawn (unload scene ตรงๆ)
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            ApplyAutoHandDepth();
            if (IsOwner) SeedAimFromCurrentHandPose();
            lastSentTarget = arm.Body != null ? arm.Body.position : arm.PivotPosition;
        }

        public override void OnNetworkDespawn()
        {
            // คืนเคอร์เซอร์เมื่อหุ่นหายจากเกม (กลับเมนู) — ไม่งั้นคลิกเมนูไม่ได้
            ReleaseCursorIfOwner();
            base.OnNetworkDespawn();
        }

        private void ReleaseCursorIfOwner()
        {
            if (IsOwner && LocksCursor && playerCamera != null) LocalAim.Release();
        }

        private void Update()
        {
            if (!IsOwner) return;

            // R ค้าง = ดึงแขนที่หลุดกลับ — ใช้ได้กับทุกชิ้นที่เครื่องนี้เป็นเจ้าของ ไม่ต้องผูกกล้อง
            // (Host เป็นเจ้าของชิ้นที่ไม่มีใครเลือก จึงดึงชิ้นพวกนั้นกลับมาต่อได้เหมือนเดิม)
            HandlePullBack();

            if (playerCamera == null || !limb.IsAttached) return;
            if (LocksCursor && LocalAim.HandleCursorLock())
            {
                // คลิกนี้ใช้ดึงเมาส์กลับเข้าเกม ห้ามนับเป็นหมัด — ต้องปล่อยคลิกก่อน
                SeedAimFromCurrentHandPose();
                ignoreClickUntilRelease = true;
            }

            // เคอร์เซอร์ปลดอยู่ (Esc ไปเมนู) / ชี้แล้วต่อยแต่มีเมนูเปิด → หยุดรับ input มือทั้งหมด
            // ✅ ต้องปล่อยของที่จับค้างและหมัดที่ค้างก่อน ไม่งั้นปล่อยนิ้วตอนอยู่ในเมนูแล้วเราไม่เห็น KeyUp = ค้างถาวร
            if (LocksCursor ? !LocalAim.IsLocked : pointAndClick && !LegInput.PointerInGame())
            {
                CancelHeldActions();
                return;
            }

            if (pointAndClick) UpdateGuardAim(Input.mousePosition);
            else
            {
                LocalAim.UpdateVirtualCursor(mouseSensitivity); // ปืน/ขาอ่านผ่าน LocalAim.AimNormalized
                UpdateHandAim();
            }
            HandlePunchInput(); // ก่อนส่งเป้า — เฟรมที่คลิก เป้าที่ส่งต้องเป็นแนวหมัดแล้ว
            SendHandTarget();
            HandleGrabInput();

            if (Input.GetKeyDown(recoveryKey) && limb.Owner != null && limb.Owner.Torso != null &&
                limb.Owner.Torso.IsRagdoll)
                arm.RequestRecoveryPushRpc();
        }

        private void CancelHeldActions()
        {
            if (grabHeld)
            {
                grabHeld = false;
                grip.ClearLocalConfirmation();
                grip.RequestReleaseRpc();
            }

            if (punchHeld)
            {
                punchHeld = false;
                arm.RequestPunchRpc(false);
            }
            punchRequestPending = false;
        }

        // ================================================================
        //  เล็งมือ — เมาส์ = ระนาบหน้าจอ (2 แกน) | ล้อ = ความลึกไหล่→มือ (แกนที่ 3)
        //  delta ถูก "ตีความ" ด้วยแกนกล้อง ณ เฟรมนั้น แล้วสะสมลงเวกเตอร์แกนโลก
        //  → หมุนกล้องแล้วมือค้างที่เดิมในโลก / ปล่อยคลิกขวาแล้วคุมต่อจากเดิมด้วยมุมกล้องใหม่ ไม่มีกระชาก
        // ================================================================

        private void UpdateHandAim()
        {
            // มือสองข้างแชร์จุดเล็งเดียวกัน — กันบวก delta ซ้ำในเฟรมเดียว
            if (!LocalAim.IsLocked || !LocalAim.TryClaimArmAimFrame()) return;

            // คลิกขวาค้าง = โหมดกล้องเต็มตัว ห้ามแตะ offset และห้ามจองล้อ
            if (LocalAim.CameraModeHeld) return;

            Transform cameraTransform = playerCamera.transform;
            Vector2 delta = LocalAim.MouseDelta(mouseSensitivity);

            // ✅ [Screen-Constant Aim] สเกลด้วยระยะกล้องจริง → "มือขยับกี่พิกเซลบนจอ" คงที่ทุกระดับซูม
            float gain = CameraDistanceGain();
            LocalAim.ArmOffsetWorld += (cameraTransform.right * (delta.x * mouseReachX) +
                                        cameraTransform.up * (delta.y * mouseReachY)) * gain;

            // ล้อ: ยื่น/ดึงตามแนวไหล่→มือ — จงใจไม่ใช้ camera forward ไม่งั้นมือเหวี่ยงตามกล้อง
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.0001f && LocalAim.ArmOffsetWorld.sqrMagnitude > 0.0001f)
            {
                float step = scroll / ScrollNotch * handDepthScrollSpeed * (maxHandDepth - minHandDepth) * gain;
                LocalAim.ArmOffsetWorld += LocalAim.ArmOffsetWorld.normalized * step;
            }

            // จองล้อทุกเฟรมที่มือถือสิทธิ์อยู่ — ธงจะได้นิ่ง ไม่กะพริบจนกล้องแอบซูมแทรก
            MouseWheelFocus.Claim();

            // รวมกันแล้วอาจเลยระยะที่แขนเอื้อมถึง — ดึงกลับเข้าช่วง [min, max] โดยคงทิศเดิม
            float depth = LocalAim.ArmOffsetWorld.magnitude;
            if (depth < 0.0001f)
            {
                LocalAim.ArmOffsetWorld = Vector3.down * minHandDepth; // เป้าทับไหล่พอดี — กลับท่าพัก
                return;
            }

            float clamped = Mathf.Clamp(depth, minHandDepth, maxHandDepth);
            if (!Mathf.Approximately(depth, clamped)) LocalAim.ArmOffsetWorld *= clamped / depth;
        }

        private float CameraDistanceGain()
        {
            if (!scaleAimWithCameraDistance || playerCamera == null) return 1f;
            float cameraDistance = Vector3.Distance(playerCamera.transform.position, arm.PivotPosition);
            return cameraDistance / Mathf.Max(0.1f, aimReferenceCameraDistance);
        }

        /// <summary>
        /// ชี้แล้วต่อย: ray จากกล้องผ่านเมาส์ → ทิศการ์ดจากไหล่ไปจุดนั้น (หมัดพุ่งตามแนวการ์ด = ต่อยไปตรงที่ชี้)
        /// คลิกขวาค้าง (หมุนกล้อง) = การ์ดค้างทิศเดิม ไม่กวาดตามเมาส์
        /// </summary>
        private void UpdateGuardAim(Vector3 screenPoint)
        {
            if (LocalAim.CameraModeHeld) return;

            Vector3 pivot = arm.PivotPosition;
            Ray ray = playerCamera.ScreenPointToRay(screenPoint);
            Vector3 aimPoint = ray.GetPoint(playerCamera.farClipPlane);
            bool onTarget = false;
            float nearest = float.MaxValue;
            Robot self = limb.Owner;

            int count = Physics.RaycastNonAlloc(ray, AimHits, playerCamera.farClipPlane, aimLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = AimHits[i];
                // ข้ามหุ่นตัวเอง — เทียบด้วย Robot ไม่ใช่ transform.root (ฉากที่หุ่นหลายตัวอยู่ใต้ parent เดียวกันจะข้ามศัตรูไปด้วย)
                if (hit.distance >= nearest || (self != null && Robot.FromCollider(hit.collider) == self)) continue;
                nearest = hit.distance;
                aimPoint = hit.point;
                onTarget = hit.collider.GetComponentInParent<IDamageable>() != null;
            }

            // ชี้พื้น = ต่อยระดับไหล่ (กล้องก้ม ~55° ไม่งั้นทุกหมัดทุบดิน) | ชี้ของที่ตีได้ = ต่อยไปที่ตัวมันจริง
            if (!onTarget) aimPoint.y = pivot.y;
            Vector3 direction = aimPoint - pivot;
            if (direction.sqrMagnitude > 0.0001f) guardDirection = direction.normalized;
            aimOnTarget = onTarget;
            aimInReach = direction.magnitude <= arm.PunchReach;
        }

        private void SendHandTarget()
        {
            Vector3 target = arm.PivotPosition + LocalAim.ArmOffsetWorld;
            if (pointAndClick)
            {
                // ต่อยอยู่ = ส่งแนวหมัดตรงไปที่เล็ง (การ์ดยก/หุบมือ ถ้าใช้เป็นทิศหมัด หมัดจะเฉียงขึ้นและเข้ากลางตัว)
                bool punching = punchHeld || arm.CurrentPunchState != PunchState.Idle;
                target = arm.PivotPosition + (punching ? guardDirection * arm.MaxArmLength : arm.GuardOffset(guardDirection));
            }

            // กันมือทะลุพื้น
            if (Physics.Raycast(target + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 5f, groundLayer) &&
                target.y < hit.point.y)
                target.y = hit.point.y;

            if (Vector3.Distance(lastSentTarget, target) <= RpcSendThreshold) return;
            lastSentTarget = target;
            arm.SetHandTargetRpc(target);
        }

        private void ApplyAutoHandDepth()
        {
            if (!autoHandDepthFromArmLength) return;
            maxHandDepth = Mathf.Max(0.1f, arm.MaxArmLength * ArmDepthSafety);
            minHandDepth = Mathf.Clamp(arm.MaxArmLength * 0.12f, 0.05f, maxHandDepth - 0.05f);
        }

        /// <summary>ตั้งจุดเล็งจากท่ามือจริง — ใช้ตอน spawn / ล็อกเคอร์เซอร์ / respawn มือจึงไม่กระโดดตำแหน่ง</summary>
        private void SeedAimFromCurrentHandPose()
        {
            Vector3 handOffset = (arm.Body != null ? arm.Body.position : arm.PivotPosition) - arm.PivotPosition;
            if (handOffset.sqrMagnitude < 0.0001f)
                handOffset = Vector3.down * Mathf.Lerp(minHandDepth, maxHandDepth, defaultHandDepth);

            LocalAim.ArmOffsetWorld = handOffset.normalized * Mathf.Clamp(handOffset.magnitude, minHandDepth, maxHandDepth);
        }

        private void OnAimResetRequested()
        {
            grabHeld = false;
            grip.ClearLocalConfirmation();
            ignoreClickUntilRelease = true; // ต้องปล่อยคลิกซ้ายก่อน ถึงจะต่อยครั้งใหม่ได้
            ApplyAutoHandDepth();
            SeedAimFromCurrentHandPose();
            lastSentTarget = Vector3.positiveInfinity; // เป้าใหม่หลัง teleport ต้องถูกส่งทันที
        }

        // ================================================================
        //  จับ (F ค้าง) — "ยังกด F อยู่ = ยังจับอยู่" / ปล่อย F = ปล่อยทันที
        // ================================================================

        private void HandleGrabInput()
        {
            if (Input.GetKeyDown(grabKey))
            {
                grabHeld = true;
                nextGrabRetryTime = 0f;
            }

            if (Input.GetKeyUp(grabKey) && grabHeld)
            {
                grabHeld = false;
                grip.ClearLocalConfirmation();
                // ส่งเสมอ — กันเคสที่ server จับติดไปแล้วแต่ผลยังไม่ถึงเรา
                grip.RequestReleaseRpc();
                return;
            }

            if (!grabHeld || grip.IsGrabConfirmed) return;

            // ตัวล้มอยู่ = เริ่มจับใหม่ไม่ได้ (server ปฏิเสธอยู่แล้ว ไม่ต้องยิง RPC ที่รู้ผล)
            TorsoBalance torso = limb.Owner != null ? limb.Owner.Torso : null;
            if (torso != null && torso.IsRagdoll) return;

            if (Time.time < nextGrabRetryTime) return;
            nextGrabRetryTime = Time.time + grabRetryInterval;
            grip.RequestGrabRpc();
        }

        // ================================================================
        //  ต่อย (คลิกซ้าย) — 1 คลิก = 1 หมัด
        // ================================================================

        private void HandlePunchInput()
        {
            if (ignoreClickUntilRelease)
            {
                if (!Input.GetMouseButton(0)) ignoreClickUntilRelease = false;
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                punchHeld = true;
                punchRequestPending = true;
                punchRequestExpireTime = Time.time + punchRequestWindow;
                nextPunchRequestTime = 0f;
            }

            if (Input.GetMouseButtonUp(0))
            {
                punchHeld = false;
                punchRequestPending = false;
                arm.RequestPunchRpc(false);
            }

            if (!punchRequestPending) return;

            if (arm.CurrentPunchState != PunchState.Idle)
            {
                // server ยังไม่ว่าง (Recovering ของหมัดก่อน) — ลองใหม่จนหมด window แล้วทิ้ง
                if (Time.time > punchRequestExpireTime) punchRequestPending = false;
                return;
            }

            if (Time.time < nextPunchRequestTime) return;
            nextPunchRequestTime = Time.time + PunchRetryInterval;
            punchRequestPending = false; // ⭐ กินคำขอทิ้งที่นี่ = กดครั้งเดียวต่อยซ้ำไม่ได้
            arm.RequestPunchRpc(true);
        }

        // ================================================================
        //  ดึงแขนที่หลุดกลับ (R ค้าง)
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
        //  Crosshair — ฉายจากจุดเล็งจริงในโลก หันกล้องแล้ว crosshair เลื่อนตามจุดเดิมในโลก
        // ================================================================

        private void OnGUI()
        {
            if (IsOwner && pointAndClick && playerCamera != null && limb.IsAttached && LegInput.PointerInGame())
            {
                DrawCombatReticle();
                return;
            }
            if (!IsOwner || !LocksCursor || !showCrosshair || playerCamera == null) return;
            if (!LocalAim.IsLocked || !LocalAim.TryClaimCrosshairFrame()) return;

            if (crosshairStyle == null)
                crosshairStyle = new GUIStyle(GUI.skin.label)
                    { fontSize = 30, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };

            Vector3 screen = playerCamera.WorldToScreenPoint(arm.PivotPosition + LocalAim.ArmOffsetWorld);
            if (screen.z <= 0f) return;

            float x = screen.x, y = Screen.height - screen.y;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.Label(new Rect(x - 19f, y - 19f, 40f, 40f), "+", crosshairStyle);
            GUI.color = Color.white;
            GUI.Label(new Rect(x - 20f, y - 20f, 40f, 40f), "+", crosshairStyle);
        }

        /// <summary>
        /// เป้าเล็งโหมดต่อสู้ที่เมาส์: แดง = ชี้โดนเป้าในระยะหมัด, ขาว = ในระยะ, ส้มจาง = ไกลเกินหมัดถึง
        /// กล้องหลังหุ่นกะระยะหน้า-หลังด้วยตาไม่ได้ ตัวนี้บอกแทน | กดค้างโหมดชาร์จ = แถบแรงใต้เป้า
        /// </summary>
        private void DrawCombatReticle()
        {
            if (crosshairStyle == null)
                crosshairStyle = new GUIStyle(GUI.skin.label)
                    { fontSize = 30, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };

            float x = Input.mousePosition.x, y = Screen.height - Input.mousePosition.y;
            Color color = !aimInReach ? new Color(1f, 0.6f, 0.2f, 0.6f) : aimOnTarget ? new Color(1f, 0.25f, 0.2f) : Color.white;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.Label(new Rect(x - 29f, y - 29f, 60f, 60f), "◎", crosshairStyle);
            GUI.color = color;
            GUI.Label(new Rect(x - 30f, y - 30f, 60f, 60f), "◎", crosshairStyle);

            bool charging = arm.ChargeMode && punchHeld;
            if (charging && chargeStartTime < 0f) chargeStartTime = Time.time;
            if (!charging) { chargeStartTime = -1f; GUI.color = Color.white; return; }

            float charge = Mathf.Clamp01((Time.time - chargeStartTime) / Mathf.Max(arm.FullChargeTime, 0.01f));
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(x - 30f, y + 26f, 60f, 8f), Texture2D.whiteTexture);
            GUI.color = Color.Lerp(new Color(1f, 0.8f, 0.2f), new Color(1f, 0.2f, 0.1f), charge);
            GUI.DrawTexture(new Rect(x - 30f, y + 26f, 60f * charge, 8f), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
