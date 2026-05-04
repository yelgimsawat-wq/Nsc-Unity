using UnityEngine;
using Unity.Netcode;

public class EZMovement : NetworkBehaviour
{
    [Header("Movement Settings")]
    public float speed = 8f;
    public float hitRadius = 0.2f;
    public LayerMask grabLayer;

    [Header("Grab Settings")]
    public float grabCheckRadius = 0.35f;
    public float grabHoldForce = 40f;   // force to drag non-kinematic objects
    public float grabPushForce = 50f;   // force applied to body when pushing off kinematic surfaces

    [Header("Leash Settings")]
    [SerializeField] public float range = 4f;
    [SerializeField] public Transform attachPart;

    [Header("References")]
    private Rigidbody torsoRb;
    public Transform physicalHandTransform;

    // --- Grab state (synced so the server can act on it) ---
    private NetworkVariable<bool> isGrabbing = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // Client-side grab tracking
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
        //  GRAB LOGIC (Hold F)
        // =====================
        if (Input.GetKey(KeyCode.F))
        {
            HandleGrab(moveDir);
        }
        else
        {
            // Released E — drop anything we were holding
            if (grabbedRb != null)
            {
                ReleaseGrabServerRpc();
                grabbedRb = null;
                grabbedIsKinematic = false;
                pushTimer = 0f;
            }
            isGrabbing.Value = false;

            // Normal free movement
            if (moveDir.magnitude > 0.1f)
            {
                transform.Translate(moveDir * speed * Time.deltaTime, Space.World);
            }
        }

        ApplyLeash();
    }

    // ------------------------------------------------------------------
    //  GRAB: detect surface, latch on, then move object OR push body
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
        if (grabbedRb == null)
        {
            if (moveDir.magnitude > 0.1f)
            {
                transform.Translate(moveDir * speed * Time.deltaTime, Space.World);
            }
            return;
        }

        // ------- We ARE grabbing something -------
        if (!grabbedIsKinematic)
        {
            // NON-KINEMATIC: Move the hand freely, drag the grabbed object along
            if (moveDir.magnitude > 0.1f)
            {
                transform.Translate(moveDir * speed * Time.deltaTime, Space.World);
            }

            // Pull the object toward the physical hand via server force
            if (physicalHandTransform != null)
            {
                DragObjectServerRpc(physicalHandTransform.position);
            }
        }
        else
        {
            // KINEMATIC surface: hand is "stuck" — push the body instead
            // Don't translate the hand; it stays locked on the surface
            if (moveDir.magnitude > 0.1f)
            {
                if (pushTimer < 2f) pushTimer += Time.deltaTime;
                float currentForce = Mathf.Lerp(0f, grabPushForce, pushTimer / 2f);
                ApplyPushForceServerRpc(-moveDir * currentForce);
            }
            else
            {
                pushTimer = 0f;
            }
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
        // Re-find the grabbed rigidbody on the server side
        // (the owner detected it via overlap, server validates with the same check)
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
    /// Push the body (torso) in a direction — used when grabbing a kinematic surface.
    /// Same pattern as EZFootMovement's push.
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
    }
}
