using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class Following : NetworkBehaviour
{
    public Transform targetPoint; // The invisible point the mouse controls
    public Transform pivotPoint; // The physical hinge/pivot part of the limb

    public float springStrength = 75f;
    public float dampening = 5f; // Stops it from bouncing forever

    public Rigidbody HighterLimp;
    private Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        HighterLimp = gameObject.GetComponent<CharacterJoint>().connectedBody;
    }
    public override void OnNetworkSpawn()
    {
        //if (!IsServer) return;
        //targetPoint = GameObject.Find("TargetPoint").transform;
        //targetPoint.position = transform.position;
    }
    void FixedUpdate()
    {
        // 1. SECURITY: Only the Server runs physics math!
        if (!IsServer) return;

        if (targetPoint == null) return;

        // 2. Calculate the distance to the target
        Vector3 distanceToTarget = targetPoint.position - transform.position;

        // 3. The "Spring" Math
        // We pull the arm toward the target, but subtract the current velocity 
        // (dampening) so it doesn't fly out of control.
        Vector3 springForce = (distanceToTarget * springStrength) - (rb.velocity * dampening);

        // 4. Apply the physical force
        rb.AddForce(springForce, ForceMode.Acceleration);
    }
}
