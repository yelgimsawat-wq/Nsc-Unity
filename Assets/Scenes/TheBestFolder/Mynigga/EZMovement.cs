using UnityEngine;
using Unity.Netcode;

public class EZMovement : NetworkBehaviour
{
    [Header("Movement Settings")]
    public float speed = 8f;
    public float hitRadius = 0.2f;
    public LayerMask groundLayer;

    [Header("Push Settings (surface contact)")]
    public float pushForce = 50f;

    [Header("Grab Settings (Hold F)")]
    public LayerMask grabLayer;
    public float grabCheckRadius = 0.35f;
    public float grabHoldForce = 40f;   // force to drag non-kinematic objects
    public float grabPushForce = 50f;   // force applied to body when grabbing

    [Header("Leash Settings")]
    [SerializeField] public float range = 4f;
    [SerializeField] public Transform attachPart;

    [Header("References")]
    private Rigidbody torsoRb;
    public Transform physicalHandTransform;

    // --- Grab state (synced) ---
    private NetworkVariable<bool> isGrabbing = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // Client-side tracking
    private Rigidbody grabbedRb;
    private bool grabbedIsKinematic;
    private float pushTimer = 0f;

    // -------------------------------------------------------
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

        // Find the physical hand that follows this target point
        Following[] followers = GameObject.FindObjectsByType<Following>(FindObjectsSortMode.None);
        foreach (var f in followers)
        {
            if (f.targetPoint == this.transform)
            {
                physicalHandTransform = f.transform;
                break;
            }
        }
    }

    void Update()
    {
        if (!IsOwner) return;

        // --- Lazy-init references if missing ---
        if (attachPart == null)
        {
            GameObject torsoObject = GameObject.FindGameObjectWithTag("Body");
            if (torsoObject != null)
            {
                attachPart = torsoObject.transform;
                torsoRb = attachPart.GetComponent<Rigidbody>();
            }
        }
        if (attachPart == null) return;

        // --- Input ---
        Vector3 inputDir = new Vector3(Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical"));
        if (Input.GetKey(KeyCode.Q)) inputDir.y = 1f;
        else if (Input.GetKey(KeyCode.E)) inputDir.y = -1f;

        Vector3 moveDir = inputDir.normalized;

        // =====================
        //  GRAB MODE (Hold F)
        // =====================
        if (Input.GetKey(KeyCode.F))
        {
            HandleGrab(moveDir);
        }
        else
        {
            // Released F — drop anything we were holding
            if (grabbedRb != null)
            {
                ReleaseGrabServerRpc();
                grabbedRb = null;
                grabbedIsKinematic = false;
            }
            isGrabbing.Value = false;

            // =====================
            //  NORMAL MODE — push off surfaces like EzFootMovement
            // =====================
            HandleNormalMovement(moveDir);
        }

        ApplyLeash();
    }

    // ------------------------------------------------------------------
    //  NORMAL MOVEMENT: SphereCast to detect surfaces.
    //  If pushing into a wall/ground → hand stops, body moves opposite.
    //  Otherwise → hand moves freely.
    // ------------------------------------------------------------------
    private void HandleNormalMovement(Vector3 moveDir)
    {
        bool isPushingIntoSurface = false;

        if (moveDir.magnitude > 0.1f)
        {
            // SphereCast in the movement direction to see if we're about to hit a wall/ground
            if (Physics.SphereCast(transform.position - moveDir * 0.1f, hitRadius, moveDir, out RaycastHit hit, 0.3f, groundLayer))
            {
                // Check if we're moving INTO the surface (not sliding along it)
                if (Vector3.Dot(moveDir, hit.normal) < -0.05f)
                {
                    isPushingIntoSurface = true;

                    if (pushTimer < 2f)
                    {
                        pushTimer += Time.deltaTime;
                    }

                    // Force ramps up from 0 to pushForce over 2 seconds
                    float currentForce = Mathf.Lerp(0f, pushForce, pushTimer / 2f);

                    // Action = Reaction: push body in OPPOSITE direction
                    ApplyPushForceServerRpc(-moveDir * currentForce);
                }
            }
        }

        if (isPushingIntoSurface)
        {
            // Hand is blocked by surface — don't translate
        }
        else
        {
            // Reset timer when not pushing against a surface
            pushTimer = 0f;

            if (moveDir.magnitude > 0.1f)
            {
                // Free movement
                transform.Translate(moveDir * speed * Time.deltaTime, Space.World);
            }
        }
    }

    // ------------------------------------------------------------------
    //  GRAB: Hold F to latch onto a Rigidbody surface.
    //  Hand is LOCKED — movement input pushes body in opposite direction.
    //  Non-kinematic objects get dragged along with the hand.
    // ------------------------------------------------------------------
    private void HandleGrab(Vector3 moveDir)
    {
        // Use the physical hand position for overlap detection when available
        Vector3 checkPos = physicalHandTransform != null
            ? physicalHandTransform.position
            : transform.position;

        // If we don't have a grab target yet, try to find one
        if (grabbedRb == null)
        {
            Collider[] hits = Physics.OverlapSphere(checkPos, grabCheckRadius, grabLayer);
            foreach (var col in hits)
            {
                Rigidbody rb = col.attachedRigidbody;
                if (rb != null)
                {
                    grabbedRb = rb;
                    grabbedIsKinematic = rb.isKinematic;
                    isGrabbing.Value = true;
                    GrabStartServerRpc(rb.gameObject.name, grabbedIsKinematic);
                    break;
                }
            }
        }

        // If we still have nothing to grab, keep free-moving the hand
        // (but still check for wall collision)
        if (grabbedRb == null)
        {
            HandleNormalMovement(moveDir);
            return;
        }

        // ------- We ARE grabbing something -------
        // Hand is LOCKED — do NOT translate it.
        // Movement input → Action = Reaction → push body in opposite direction.

        if (moveDir.magnitude > 0.1f)
        {
            if (pushTimer < 2f) pushTimer += Time.deltaTime;
            float currentForce = Mathf.Lerp(0f, grabPushForce, pushTimer / 2f);

            // Push body in opposite direction of input
            ApplyPushForceServerRpc(-moveDir * currentForce);

            // If grabbed object is non-kinematic, also drag it toward the hand
            if (!grabbedIsKinematic && physicalHandTransform != null)
            {
                DragObjectServerRpc(physicalHandTransform.position);
            }
        }
        else
        {
            pushTimer = 0f;
        }
    }

    // ------------------------------------------------------------------
    //  LEASH — keep hand within range of its attach part
    // ------------------------------------------------------------------
    private void ApplyLeash()
    {
        if (attachPart == null) return;

        Vector3 offset = transform.position - attachPart.position;
        if (offset.magnitude > range)
        {
            transform.position = attachPart.position + (offset.normalized * range);
        }
    }

    // ==================================================================
    //  SERVER RPCs — all physics mutation happens on the server
    // ==================================================================

    /// <summary>
    /// Notify the server that a grab started (for logging / future sync).
    /// </summary>
    [ServerRpc]
    void GrabStartServerRpc(string objectName, bool isKinematic)
    {
        Debug.Log($"[Server] Player grabbed '{objectName}' (kinematic={isKinematic})");
    }

    /// <summary>
    /// Notify the server the grab ended.
    /// </summary>
    [ServerRpc]
    void ReleaseGrabServerRpc()
    {
        Debug.Log("[Server] Player released grab");
    }

    /// <summary>
    /// Drag a non-kinematic grabbed object toward a world position.
    /// Runs on the server so physics is authoritative.
    /// </summary>
    [ServerRpc]
    void DragObjectServerRpc(Vector3 handWorldPos)
    {
        Collider[] hits = Physics.OverlapSphere(handWorldPos, grabCheckRadius * 2f, grabLayer);
        foreach (var col in hits)
        {
            Rigidbody rb = col.attachedRigidbody;
            if (rb != null && !rb.isKinematic)
            {
                Vector3 pullDir = handWorldPos - rb.position;
                rb.AddForce(pullDir * grabHoldForce, ForceMode.Acceleration);
                break;
            }
        }
    }

    /// <summary>
    /// Push the body (torso) in a direction.
    /// Used both for surface pushing (like EzFootMovement) and grab pushing.
    /// </summary>
    [ServerRpc]
    void ApplyPushForceServerRpc(Vector3 force)
    {
        if (torsoRb == null)
        {
            if (attachPart != null)
            {
                torsoRb = attachPart.GetComponent<Rigidbody>();
            }
            else
            {
                GameObject torsoObject = GameObject.FindGameObjectWithTag("Body");
                if (torsoObject != null) torsoRb = torsoObject.GetComponent<Rigidbody>();
            }
        }

        if (torsoRb != null)
        {
            torsoRb.AddForce(force, ForceMode.Acceleration);
        }
    }

    // ------------------------------------------------------------------
    //  GIZMOS
    // ------------------------------------------------------------------
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, grabCheckRadius);

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, hitRadius);
    }
}
