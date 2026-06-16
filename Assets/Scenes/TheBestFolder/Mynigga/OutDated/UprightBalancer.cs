using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class UprightBalancer : NetworkBehaviour
{
    public float balanceStrength = 50f; // How hard it tries to stay upright
    public float balanceDampening = 5f; // Stops it from wobbling wildly

    private Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        // SECURITY: Only the Server runs this physics balancing
        if (!IsServer) return;

        // 1. Find the angle difference between "straight up" and how the robot is currently leaning
        Vector3 currentUp = transform.up;
        Vector3 targetUp = Vector3.up;

        // 2. Cross product gives us the "axle" we need to twist around to stand up
        Vector3 twistAxis = Vector3.Cross(currentUp, targetUp);

        // 3. Calculate the force needed to twist back to center
        Vector3 uprightTorque = (twistAxis * balanceStrength) - (rb.angularVelocity * balanceDampening);

        // 4. Apply the twisting force
        rb.AddTorque(uprightTorque, ForceMode.Acceleration);
    }
}