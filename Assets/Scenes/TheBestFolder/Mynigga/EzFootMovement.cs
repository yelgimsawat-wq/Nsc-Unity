using UnityEngine;
using Unity.Netcode;
using System.ComponentModel;

public class EZFootMovement : NetworkBehaviour
{
    [Header("Movement Settings")]
    public float speed = 8f;
    public float pushForce = 50f;
    public float hitRadius = 0.2f;
    public LayerMask groundLayer;

    [Header("Jump Settings")]
    public float jumpForce = 400f;

    [Header("Leash Settings")]
    [SerializeField] public float range = 4f;
    [SerializeField] public Transform attachPart;

    [Header("Max Range (from follower)")]
    [Tooltip("Maximum distance the player can be from its physical follower limb. 0 = no limit.")]
    [SerializeField] public float maxRange = 15f;

    [Header("References")]
    [SerializeField]private Rigidbody torsoRb;
    public Transform physicalFootTransform;

    private float pushTimer = 0f;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        CacheReferences();
    }

    private void CacheReferences()
    {
        if (attachPart == null)
        {
            GameObject torsoObject = GameObject.FindGameObjectWithTag("Body");
            if (torsoObject != null)
            {
                attachPart = torsoObject.transform;
            }
        }

        if (attachPart != null)
        {
            torsoRb = attachPart.GetComponent<Rigidbody>();
        }
    }

    void Update()
    {
        if (!IsOwner) return;

        if (attachPart == null)
        {
            GameObject torsoObject = GameObject.FindGameObjectWithTag("Body");
            if (torsoObject != null)
            {
                attachPart = torsoObject.transform;
                torsoRb = attachPart.GetComponent<Rigidbody>();
            }
        }

        if (torsoRb == null) 
        {
            torsoRb = GameObject.FindGameObjectWithTag("Body").GetComponent<Rigidbody>();
        }

        if (attachPart == null) return;

        // Get horizontal input relative to camera yaw
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        float yInput = 0f;
        if (Input.GetKey(KeyCode.Q)) yInput = 1f;
        else if (Input.GetKey(KeyCode.E)) yInput = -1f;

        // Rotate horizontal input by camera yaw
        PlayerCam cam = GetComponentInParent<PlayerCam>();
        if (cam == null) cam = GetComponent<PlayerCam>();
        float camYaw = cam != null ? cam.yaw : 0f;
        Quaternion camRot = Quaternion.Euler(0f, camYaw, 0f);
        Vector3 flatDir = camRot * new Vector3(h, 0f, v);
        Vector3 moveDir = new Vector3(flatDir.x, yInput, flatDir.z).normalized;
        bool isPushingIntoSurface = false;
        bool isJumping = Input.GetKey(KeyCode.Space);

        if (moveDir.magnitude > 0.1f)
        {
            if (Physics.SphereCast(transform.position - moveDir * 0.1f, hitRadius, moveDir, out RaycastHit hit, 0.3f, groundLayer))
            {
                Debug.Log("Hit ground 0");
                if (Vector3.Dot(moveDir, hit.normal) < -0.05f)
                {
                    Debug.Log("Hit ground 1");
                    isPushingIntoSurface = true;

                    if (isJumping)
                    {
                        Debug.Log("Hit ground 2");
                        // Spacebar held: apply full jump force instantly, no ramp-up
                        pushTimer = 0f;
                        ApplyPushForceServerRpc(-moveDir * jumpForce);
                    }
                    else
                    {
                        Debug.Log("Hit ground 3");
                        // Normal push: ramp up from 0 to pushForce over 2 seconds
                        if (pushTimer < 2f)
                        {
                            pushTimer += Time.deltaTime;
                        }

                        float currentForce = Mathf.Lerp(0f, pushForce, pushTimer / 2f);
                        ApplyPushForceServerRpc(-moveDir * currentForce);
                    }
                }
            }
        }

        if (isPushingIntoSurface)
        {
            // Pushing state — hand stays still
        }
        else
        {
            // Reset timer when not pushing against a surface
            pushTimer = 0f;

            if (moveDir.magnitude > 0.1f)
            {
                // Moving state
                transform.Translate(moveDir * speed * Time.deltaTime, Space.World);
            }
        }

        ApplyLeash();
        ClampToFollower();
    }

    private void ApplyLeash()
    {
        if (attachPart == null) return;

        Vector3 offset = transform.position - attachPart.position;
        if (offset.magnitude > range)
        {
            transform.position = attachPart.position + (offset.normalized * range);
        }
    }

    /// <summary>
    /// Prevents the target point from going too far from its physical follower.
    /// This keeps the player body within maxRange of the follower limb.
    /// </summary>
    private void ClampToFollower()
    {
        if (physicalFootTransform == null || maxRange <= 0f) return;

        Vector3 offset = transform.position - physicalFootTransform.position;
        if (offset.magnitude > maxRange)
        {
            transform.position = physicalFootTransform.position + (offset.normalized * maxRange);
        }
    }

    [ServerRpc]
    void ApplyPushForceServerRpc(Vector3 force)
    {
        Debug.Log($"Pushing with {force} 0");
        if (torsoRb == null)
        {
            Debug.Log($"Pushing with {force} 1");
            if (attachPart != null)
            {
                Debug.Log($"Pushing with {force} 2");
                torsoRb = attachPart.GetComponent<Rigidbody>();
            }
            else
            {
                Debug.Log($"Pushing with {force} 3");
                GameObject torsoObject = GameObject.FindGameObjectWithTag("Body");
                if (torsoObject != null) torsoRb = torsoObject.GetComponent<Rigidbody>();
            }
        }

        if (torsoRb != null)
        {
            Debug.Log($"Pushing with {force} 4");
            torsoRb.AddForce(force, ForceMode.Acceleration);
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, hitRadius);

        // Visualize max range from follower
        if (physicalFootTransform != null && maxRange > 0f)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(physicalFootTransform.position, maxRange);
        }
    }
}