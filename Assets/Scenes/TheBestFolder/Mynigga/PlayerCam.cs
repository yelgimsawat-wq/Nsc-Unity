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
    [Tooltip("How smoothly the camera follows")]
    public float smoothSpeed = 10f;

    [Header("Vertical Angle Limits")]
    public float minVerticalAngle = -30f;
    public float maxVerticalAngle = 80f;

    [HideInInspector] public float yaw = 0f;
    private float pitch = 25f;
    
    public Transform followTarget; // Assigned by LobbyManager

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            playercam.enabled = true;
            playeral.enabled = true;
            playercam.gameObject.tag = "MainCamera";

            // Keep cursor visible and unlocked
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // Initialize yaw from current rotation
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
        if (!IsOwner) return;
        if (playercam == null) return;

        // Orbit with right mouse button held
        if (Input.GetMouseButton(1))
        {
            yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
            pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
            pitch = Mathf.Clamp(pitch, minVerticalAngle, maxVerticalAngle);
        }

        // Zoom with scroll wheel
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            distance -= scroll * zoomSpeed;
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
        }

        // Calculate desired camera position
        Vector3 pivotPos = followTarget != null ? followTarget.position : transform.position;
        Vector3 pivot = pivotPos + targetOffset;
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 desiredPosition = pivot - (rotation * Vector3.forward * distance);

        // Smooth follow
        playercam.transform.position = Vector3.Lerp(playercam.transform.position, desiredPosition, smoothSpeed * Time.deltaTime);
        playercam.transform.LookAt(pivot);

        transform.position = followTarget != null ? followTarget.position : transform.position;
    }
}
