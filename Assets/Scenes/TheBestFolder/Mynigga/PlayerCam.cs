using UnityEngine;
using Unity.Netcode;

public class PlayerCam : NetworkBehaviour
{
    [SerializeField] private Camera playercam;
    [SerializeField] private AudioListener playeral;

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

    // followTarget ถูกเซ็ตโดย LobbyManager = "limb ที่ผู้เล่นเลือก" (แขน/ขา) หลัง spawn เสร็จ
    public Transform followTarget;

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            playercam.enabled = true;
            playeral.enabled = true;
            playercam.gameObject.tag = "MainCamera";

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // [Client Fix 3] ดึง yaw เริ่มต้นจากเป้าหมายจริงถ้ามีแล้ว (fallback เป็น transform)
            // หมายเหตุ: LobbyManager มักเซ็ต followTarget ทีหลัง ตอนนี้จึงอาจยังเป็น null
            // การรอ + guard ใน LateUpdate คือตัวจัดการหลัก บรรทัดนี้แค่กันเผื่อ target พร้อมก่อน
            Transform yawSource = followTarget != null ? followTarget : transform;
            yaw = yawSource.eulerAngles.y;
        }
        else
        {
            playercam.enabled = false;
            playeral.enabled = false;
        }
    }

    void LateUpdate()
    {
        if (!IsOwner || playercam == null) return;

        // [Client Fix 1] รอจนกว่า LobbyManager จะเซ็ต followTarget (limb ที่ครอบครอง) ให้เสร็จ
        // ฝั่ง Client กล้อง spawn ก่อนที่ limb จะถูกจัดสรร — กันไม่ให้ไปอ้างอิง transform ที่เพี้ยน
        if (followTarget == null) return;

        // Orbit with right mouse button held
        if (Input.GetMouseButton(1))
        {
            yaw   += Input.GetAxis("Mouse X") * mouseSensitivity;
            pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
            pitch  = Mathf.Clamp(pitch, minVerticalAngle, maxVerticalAngle);
        }

        // Zoom with scroll wheel
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            distance -= scroll * zoomSpeed;
            distance  = Mathf.Clamp(distance, minDistance, maxDistance);
        }

        // [Camera Fix 1] Smooth pivot แยกก่อน ลด stutter จาก Rigidbody/FixedUpdate
        // [Optimization] followTarget การันตีไม่ null แล้วจาก guard ด้านบน ดึง position ครั้งเดียว
        Vector3 rawPivot = followTarget.position + targetOffset;

        // [Camera Fix 2] Exponential smooth -- framerate-independent กว่า Lerp * deltaTime
        // สูตร: 1 - exp(-k*dt) ทำให้ผลสม่ำเสมอทุก framerate
        float pt = 1f - Mathf.Exp(-positionSmoothSpeed * Time.deltaTime);
        if (!_pivotInitialized) { _smoothedPivot = rawPivot; _pivotInitialized = true; }
        else _smoothedPivot = Vector3.Lerp(_smoothedPivot, rawPivot, pt);

        // [Optimization] cache transform ของกล้อง ลดการเรียก property .transform ซ้ำ
        Transform camT = playercam.transform;

        // Calculate desired camera position from smoothed pivot
        Quaternion orbitRot   = Quaternion.Euler(pitch, yaw, 0f);
        Vector3    desiredPos = _smoothedPivot - (orbitRot * Vector3.forward * distance);
        camT.position = Vector3.Lerp(camT.position, desiredPos, pt);

        // [Camera Fix 3] Slerp แทน LookAt() ตรงๆ
        // LookAt() หักมุมทันที เมื่อ pivot กระตุก กล้องก็กระตุกตามทันที
        Vector3 lookDir = _smoothedPivot - camT.position;
        if (lookDir.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(lookDir);
            float rt = 1f - Mathf.Exp(-rotationSmoothSpeed * Time.deltaTime);
            camT.rotation = Quaternion.Slerp(camT.rotation, targetRot, rt);
        }

        // [Client Fix 2] ลบ `transform.position = rawPivotPos;` ทิ้ง
        // กล้องเคลื่อนที่ที่ playercam.transform เรียบร้อยแล้ว การเขียนทับ Player Root ทุกเฟรม
        // จะไปตีกับ NetworkTransform (server เป็นเจ้าของตำแหน่ง) ทำให้ฝั่ง Client เพี้ยน
    }
}
