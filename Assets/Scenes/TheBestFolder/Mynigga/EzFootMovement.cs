using UnityEngine;
using Unity.Netcode;

public class EZFootMovement : NetworkBehaviour
{
    [Header("Movement Settings")]
    public float speed = 8f;
    public float pushForce = 50f;
    public float hitRadius = 0.2f;
    public LayerMask groundLayer;

    [Header("Leash Settings")]
    [SerializeField] public float range = 4f;
    [SerializeField] public Transform attachPart;

    [Header("References")]
    private Rigidbody torsoRb;
    public Transform physicalFootTransform;

    private bool wasPushing = false;

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

        Following[] followers = GameObject.FindObjectsByType<Following>(FindObjectsSortMode.None);
        foreach (var f in followers)
        {
            if (f.targetPoint == this.transform)
            {
                physicalFootTransform = f.transform;
                break;
            }
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

        if (attachPart == null) return;

        Vector3 inputDir = new Vector3(Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical"));
        if (Input.GetKey(KeyCode.Q)) inputDir.y = 1f;
        else if (Input.GetKey(KeyCode.E)) inputDir.y = -1f;

        Vector3 moveDir = inputDir.normalized;
        bool isPushingIntoSurface = false;

        if (moveDir.magnitude > 0.1f)
        {
            if (Physics.SphereCast(transform.position - moveDir * 0.1f, hitRadius, moveDir, out RaycastHit hit, 0.3f, groundLayer))
            {
                if (Vector3.Dot(moveDir, hit.normal) < -0.05f)
                {
                    isPushingIntoSurface = true;

                    if (!wasPushing)
                    {
                        ApplyPushForceServerRpc(-moveDir * pushForce);
                    }
                }
            }
        }

        if (isPushingIntoSurface)
        {
            // Pushing state
        }
        else if (moveDir.magnitude > 0.1f)
        {
            // Moving state
            transform.Translate(moveDir * speed * Time.deltaTime, Space.World);
        }

        ApplyLeash();
        wasPushing = isPushingIntoSurface;
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

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, hitRadius);
    }
}
