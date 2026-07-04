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

            yaw = transform.eulerAngles.y;
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
        Vector3 rawPivotPos = followTarget != null ? followTarget.position : transform.position;
        Vector3 rawPivot    = rawPivotPos + targetOffset;

        // [Camera Fix 2] Exponential smooth -- framerate-independent กว่า Lerp * deltaTime
        // สูตร: 1 - exp(-k*dt) ทำให้ผลสม่ำเสมอทุก framerate
        float pt = 1f - Mathf.Exp(-positionSmoothSpeed * Time.deltaTime);
        if (!_pivotInitialized) { _smoothedPivot = rawPivot; _pivotInitialized = true; }
        else _smoothedPivot = Vector3.Lerp(_smoothedPivot, rawPivot, pt);

        // Calculate desired camera position from smoothed pivot
        Quaternion orbitRot   = Quaternion.Euler(pitch, yaw, 0f);
        Vector3    desiredPos = _smoothedPivot - (orbitRot * Vector3.forward * distance);
        playercam.transform.position = Vector3.Lerp(playercam.transform.position, desiredPos, pt);

        // [Camera Fix 3] Slerp แทน LookAt() ตรงๆ
        // LookAt() หักมุมทันที เมื่อ pivot กระตุก กล้องก็กระตุกตามทันที
        Vector3 lookDir = _smoothedPivot - playercam.transform.position;
        if (lookDir.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(lookDir);
            float rt = 1f - Mathf.Exp(-rotationSmoothSpeed * Time.deltaTime);
            playercam.transform.rotation = Quaternion.Slerp(playercam.transform.rotation, targetRot, rt);
        }

        // Sync NetworkBehaviour transform position
        transform.position = rawPivotPos;
    }
}
