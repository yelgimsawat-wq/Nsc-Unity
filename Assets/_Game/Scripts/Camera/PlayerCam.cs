using Nsc.Feel;
using Nsc.Limbs;
using Nsc.Robots;
using UnityEngine;
using Unity.Netcode;

public class PlayerCam : NetworkBehaviour
{
    // จุดที่กล้องมองต่ำกว่าลำตัวลงมาเกือบครึ่งตัว — พื้นที่เท้าจะลงอยู่กลางจอ ไม่ใช่หัวหุ่น
    private const float RobotLookDropRatio = 0.45f;
    // ความสูงหุ่นโดยประมาณ = ความยาวขาจริง × ค่านี้ (ลำตัว + หัว) — วัดจากข้อต่อ ไม่ขึ้นกับว่าหุ่นนอนอยู่
    private const float RobotHeightPerLegLength = 1.4f;

    [SerializeField] private Camera playercam;
    [SerializeField] private AudioListener playeral;

    [Header("High Angle (มุมสูงตามตัวหุ่น)")]
    [Tooltip("กล้องตามลำตัวหุ่น ไม่ใช่แขน/ขาที่คุม — เห็นทั้งตัวและพื้นรอบๆ เล็งเท้าง่าย")]
    public bool followRobotBody = true;
    [Tooltip("มุมก้มเริ่มต้น (องศา) — สูง = มองลงจากบน")]
    public float startPitch = 55f;
    [Tooltip("ระยะกล้อง = ความสูงหุ่น × ค่านี้ หุ่นสเกลไหนก็เห็นเท่ากัน (0 = ใช้ distance ตรงๆ)")]
    public float distancePerRobotHeight = 2.2f;
    [Tooltip("กำแพง/ตึกบังระหว่างหุ่นกับกล้อง → ดึงกล้องเข้ามาหน้าสิ่งนั้น (ไม่นับชิ้นส่วนหุ่นตัวเอง)")]
    public bool avoidOcclusion = true;
    public LayerMask occlusionMask = ~0;
    [Tooltip("ล็อกมุมกล้อง: ไม่หันตามหุ่นที่โยก มุมเปลี่ยนได้ทางเดียวคือคลิกขวาลาก\n" +
             "กล้องหันตาม = จุดบนพื้นใต้เมาส์เลื่อนเอง เท้าที่เล็งอยู่เลยแกว่งไปมา")]
    public bool lockRotation = true;
    [Tooltip("หุ่นขยับไม่เกินระยะนี้ (สัดส่วนความสูงหุ่น) กล้องอยู่นิ่ง — ตัวโยกตอนก้าวไม่ลากกล้องตาม")]
    [Range(0f, 1f)] public float followDeadZone = 0.25f;

    [Header("Orbit Settings")]
    [Tooltip("Mouse sensitivity for orbiting")]
    public float mouseSensitivity = 3f;
    [Tooltip("Distance from the player")]
    public float distance = 8f;
    [Tooltip("Minimum zoom distance")]
    public float minDistance = 2f;
    [Tooltip("Maximum zoom distance")]
    public float maxDistance = 20f;
    [Tooltip("Scroll wheel zoom speed")]
    public float zoomSpeed = 2f;
    [Tooltip("Height offset above the player pivot")]
    public Vector3 targetOffset = new Vector3(0f, 1.5f, 0f);

    [Header("Smoothing")]
    [Tooltip("ความเร็วในการ Smooth ตาม followTarget (สูง = เกาะแน่น, ต่ำ = ลอยตาม)")]
    public float positionSmoothSpeed = 12f;
    [Tooltip("ความเร็วในการ Smooth การหมุนกล้อง (แก้กระตุกตอน LookAt)")]
    public float rotationSmoothSpeed = 15f;

    [Header("Vertical Angle Limits")]
    public float minVerticalAngle = -30f;
    public float maxVerticalAngle = 80f;

    [HideInInspector] public float yaw = 0f;
    private float pitch = 25f;

    // [Camera Fix] Smooth pivot เพื่อลด stutter จาก Rigidbody ที่อัปเดตใน FixedUpdate
    private Vector3 _smoothedPivot;
    private bool _pivotInitialized = false;

    // ท่ากล้องก่อนใส่แรงสั่น — ต้องแยกเก็บ ไม่งั้นแรงสั่นไหลเข้าไปใน smoothing แล้วกล้องลอยเพี้ยน
    private Vector3 _basePosition;
    private Quaternion _baseRotation;
    private bool _baseInitialized;
    private float _baseFov;

    // followTarget ถูกเซ็ตโดย LobbyManager = "limb ที่ผู้เล่นเลือก" (แขน/ขา) หลัง spawn เสร็จ
    public Transform followTarget;

    // ลำตัวของหุ่นที่ followTarget สังกัด — จัดมุมกล้องใหม่ทุกครั้งที่ผูกกับชิ้นใหม่
    private Transform framedFor;
    private Transform robotBody;
    private float robotLookDrop;
    private float robotDeadZone;
    private Vector3 deadZoneAnchor;
    private bool deadZoneAnchorSet;

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            playercam.enabled = true;
            playeral.enabled = true;
            playercam.gameObject.tag = "MainCamera";

            // กันเคส AudioListener ค้างจากซีนก่อนหน้า (เช่น Main Camera ของ Menu) หรือจากผู้เล่นคนอื่น
            // ให้เหลือ AudioListener ที่ enabled อยู่ในซีนแค่ตัวเดียวเสมอ
            EnsureSingleAudioListener();

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // [Client Fix 3] ดึง yaw เริ่มต้นจากเป้าหมายจริงถ้ามีแล้ว (fallback เป็น transform)
            // หมายเหตุ: LobbyManager มักเซ็ต followTarget ทีหลัง ตอนนี้จึงอาจยังเป็น null
            // การรอ + guard ใน LateUpdate คือตัวจัดการหลัก บรรทัดนี้แค่กันเผื่อ target พร้อมก่อน
            Transform yawSource = followTarget != null ? followTarget : transform;
            yaw = yawSource.eulerAngles.y;
            pitch = Mathf.Clamp(startPitch, minVerticalAngle, maxVerticalAngle);
        }
        else
        {
            playercam.enabled = false;
            playeral.enabled = false;
        }
    }

    private void EnsureSingleAudioListener()
    {
        AudioListener[] listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
        foreach (AudioListener listener in listeners)
        {
            if (listener != playeral) listener.enabled = false;
        }
    }

    void LateUpdate()
    {
        if (!IsOwner || playercam == null) return;

        // [Client Fix 1] รอจนกว่า LobbyManager จะเซ็ต followTarget (limb ที่ครอบครอง) ให้เสร็จ
        // ฝั่ง Client กล้อง spawn ก่อนที่ limb จะถูกจัดสรร — กันไม่ให้ไปอ้างอิง transform ที่เพี้ยน
        if (followTarget == null) return;
        if (followTarget != framedFor) FrameRobot();

        // Orbit with right mouse button held
        if (Input.GetMouseButton(1))
        {
            // ตัวคูณจากหน้าตั้งค่า (MouseSettings) — ค่าใน Inspector คือความไวพื้นฐานของกล้องตัวนี้
            float sens = mouseSensitivity * MouseSettings.Multiplier;
            yaw   += Input.GetAxis("Mouse X") * sens;
            pitch -= Input.GetAxis("Mouse Y") * sens;
            pitch  = Mathf.Clamp(pitch, minVerticalAngle, maxVerticalAngle);
        }

        // Zoom with scroll wheel
        // 🖱️ [Scroll Ownership] ล้อเมาส์ถูก limb ที่ผู้เล่นคุมจองไว้ (ดู MouseWheelFocus):
        //   ขา → ปรับระยะก้าว | แขน → ปรับความลึกมือเข้า/ออกจากตัว
        // ล้อกลับมาเป็นของกล้องทันทีที่กดคลิกขวาค้าง = โหมดกล้องเต็มตัว (หมุน + ซูม)
        // ลำดับเวลาปลอดภัย: limb อ่าน input ใน Update ซึ่งจบก่อน LateUpdate ของกล้องเสมอ
        bool scrollOwnedByLimb = MouseWheelFocus.IsClaimed && !Input.GetMouseButton(1);
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (!scrollOwnedByLimb && Mathf.Abs(scroll) > 0.01f)
        {
            distance -= scroll * zoomSpeed;
            distance  = Mathf.Clamp(distance, minDistance, maxDistance);
        }

        // [Camera Fix 1] Smooth pivot แยกก่อน ลด stutter จาก Rigidbody/FixedUpdate
        // [Optimization] followTarget การันตีไม่ null แล้วจาก guard ด้านบน ดึง position ครั้งเดียว
        Vector3 rawPivot = robotBody != null
            ? FollowWithDeadZone(robotBody.position + Vector3.down * robotLookDrop + targetOffset)
            : followTarget.position + targetOffset;
        Transform camT = playercam.transform;
        if (!_baseInitialized)
        {
            _basePosition = camT.position;
            _baseRotation = camT.rotation;
            _baseFov = playercam.fieldOfView;
            _baseInitialized = true;
        }

        // 💥 hit-stop ฝั่งภาพ: หมัดเข้าเป้า → กล้องค้างนิ่งแวบหนึ่ง (โลกยังเดินปกติ ไม่แตะ timeScale)
        if (CameraShake.IsFrozen)
        {
            ApplyShake(camT);
            return;
        }

        // [Camera Fix 2] Exponential smooth -- framerate-independent กว่า Lerp * deltaTime
        // สูตร: 1 - exp(-k*dt) ทำให้ผลสม่ำเสมอทุก framerate
        float pt = 1f - Mathf.Exp(-positionSmoothSpeed * Time.deltaTime);
        if (!_pivotInitialized) { _smoothedPivot = rawPivot; _pivotInitialized = true; }
        else _smoothedPivot = Vector3.Lerp(_smoothedPivot, rawPivot, pt);

        // Calculate desired camera position from smoothed pivot
        Quaternion orbitRot   = Quaternion.Euler(pitch, yaw, 0f);
        Vector3    desiredPos = AvoidOcclusion(_smoothedPivot, _smoothedPivot - (orbitRot * Vector3.forward * distance));
        _basePosition = Vector3.Lerp(_basePosition, desiredPos, pt);

        // [Camera Fix 3] Slerp แทน LookAt() ตรงๆ
        // LookAt() หักมุมทันที เมื่อ pivot กระตุก กล้องก็กระตุกตามทันที
        // 🔒 ล็อกมุม: ใช้ pitch/yaw ตรงๆ — กล้องเลื่อนตามอย่างเดียว ไม่หัน
        Vector3 lookDir = _smoothedPivot - _basePosition;
        if (lockRotation && robotBody != null)
            _baseRotation = orbitRot;
        else if (lookDir.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(lookDir);
            float rt = 1f - Mathf.Exp(-rotationSmoothSpeed * Time.deltaTime);
            _baseRotation = Quaternion.Slerp(_baseRotation, targetRot, rt);
        }

        ApplyShake(camT);
        // [Client Fix 2] ลบ `transform.position = rawPivotPos;` ทิ้ง
        // กล้องเคลื่อนที่ที่ playercam.transform เรียบร้อยแล้ว การเขียนทับ Player Root ทุกเฟรม
        // จะไปตีกับ NetworkTransform (server เป็นเจ้าของตำแหน่ง) ทำให้ฝั่ง Client เพี้ยน
    }

    /// <summary>
    /// ผูกกับชิ้นใหม่ → กล้องไปอยู่ข้างหลังหุ่น มุมสูง ระยะตามขนาดหุ่น
    /// ทิศหน้า = ตั้งฉากกับแนวสะโพกซ้าย→ขวา — ถูกต้องแม้หุ่นนอนอยู่ (ลุกขึ้นแล้วหันทางเดิม)
    /// </summary>
    private void FrameRobot()
    {
        framedFor = followTarget;
        Transform previousBody = robotBody;
        robotBody = null;
        if (!followRobotBody) return;

        Robot robot = followTarget.TryGetComponent(out RobotLimb limb) ? limb.Owner : null;
        if (robot == null) return;
        robotBody = robot.Torso != null && robot.Torso.Body != null ? robot.Torso.Body.transform : robot.transform;
        // สลับไปคุมชิ้นอื่นของหุ่นตัวเดิม — กล้องอยู่ที่เดิม ไม่กระโดดมุม
        if (robotBody == previousBody) return;

        LegController left = robot.GetLimb(LimbSlot.LeftLeg)?.Controller as LegController;
        LegController right = robot.GetLimb(LimbSlot.RightLeg)?.Controller as LegController;
        LegController anyLeg = left != null ? left : right;
        if (anyLeg == null) return;

        float robotHeight = anyLeg.MaxLegLength * RobotHeightPerLegLength;
        robotLookDrop = robotHeight * RobotLookDropRatio;
        robotDeadZone = robotHeight * followDeadZone;
        deadZoneAnchorSet = false;
        if (distancePerRobotHeight > 0f)
        {
            distance = robotHeight * distancePerRobotHeight;
            maxDistance = Mathf.Max(maxDistance, distance * 1.5f);
        }

        if (left != null && right != null && left.Pivot != null && right.Pivot != null)
        {
            Vector3 hipRight = right.Pivot.position - left.Pivot.position;
            Vector3 forward = Vector3.Cross(hipRight, Vector3.up);
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.0001f) yaw = Quaternion.LookRotation(forward).eulerAngles.y;
        }
        pitch = Mathf.Clamp(startPitch, minVerticalAngle, maxVerticalAngle);
        yaw = FindClearYaw(robotBody.position + Vector3.down * robotLookDrop + targetOffset, yaw);
        _pivotInitialized = false; // กระโดดไปมุมใหม่ทันที ไม่ไหลข้ามฉากจากท่าเดิม
    }

    /// <summary>
    /// จุดที่กล้องมองขยับก็ต่อเมื่อหุ่นออกนอกวงนิ่ง — แล้วเลื่อนแค่พอให้หุ่นอยู่ที่ขอบวง
    /// หุ่นโยกตอนก้าว/ยืน = จอนิ่ง จุดใต้เมาส์ไม่ไหล เท้าที่เล็งค้างไว้ไม่แกว่ง
    /// </summary>
    private Vector3 FollowWithDeadZone(Vector3 target)
    {
        if (!deadZoneAnchorSet || robotDeadZone <= 0f)
        {
            deadZoneAnchor = target;
            deadZoneAnchorSet = true;
            return target;
        }

        Vector3 offset = target - deadZoneAnchor;
        float excess = offset.magnitude - robotDeadZone;
        if (excess > 0f) deadZoneAnchor += offset.normalized * excess;
        return deadZoneAnchor;
    }

    /// <summary>ข้างหลังหุ่นมีกำแพงบังตอนเริ่ม → หมุนหามุมที่โล่งใกล้ทิศเดิมที่สุด กล้องจะได้ไม่โดนดันเข้ามาชิดตัวหุ่น</summary>
    private float FindClearYaw(Vector3 pivot, float preferredYaw)
    {
        if (!avoidOcclusion) return preferredYaw;
        for (int step = 0; step <= 8; step++)
        {
            float offset = (step + 1) / 2 * 45f * (step % 2 == 0 ? 1f : -1f);
            float candidate = preferredYaw + offset;
            Vector3 desired = pivot - Quaternion.Euler(pitch, candidate, 0f) * Vector3.forward * distance;
            if ((AvoidOcclusion(pivot, desired) - desired).sqrMagnitude < 0.01f) return candidate;
        }
        return preferredYaw;
    }

    private Vector3 AvoidOcclusion(Vector3 pivot, Vector3 desired)
    {
        if (!avoidOcclusion) return desired;
        Vector3 toCamera = desired - pivot;
        float length = toCamera.magnitude;
        if (length < 0.01f) return desired;

        Transform self = robotBody != null ? robotBody : followTarget;
        int mask = occlusionMask & ~(1 << self.gameObject.layer);
        Vector3 direction = toCamera / length;
        if (!Physics.Raycast(pivot, direction, out RaycastHit hit, length, mask, QueryTriggerInteraction.Ignore))
            return desired;
        return pivot + direction * Mathf.Max(minDistance, hit.distance - minDistance);
    }

    /// <summary>ท่ากล้องจริง = ท่าฐาน + แรงสั่นจากก้าวเท้า/การปะทะ (CameraShake) + ซูมเข้าแวบหนึ่งตอนหมัดหนัก</summary>
    private void ApplyShake(Transform camT)
    {
        CameraShake.Sample(_smoothedPivot, out Vector3 positionOffset, out Vector3 rotationOffset);
        camT.position = _basePosition + _baseRotation * positionOffset;
        camT.rotation = _baseRotation * Quaternion.Euler(rotationOffset);
        playercam.fieldOfView = _baseFov - CameraShake.FovOffset;
    }
}
