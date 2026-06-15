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

        // --- ป้องกัน Physics Explosion ---
        // จำกัดระยะห่างสูงสุดที่นำมาคิดแรงดึง (ถ้าเมาส์หรือเป้าหมายอยู่ไกลเกิน มันจะไม่ดึงแรงจนข้อต่อระเบิด)
        distanceToTarget = Vector3.ClampMagnitude(distanceToTarget, 2f);

        // 3. The "Spring" Math
        Vector3 springForce = (distanceToTarget * springStrength) - (rb.velocity * dampening);

        // จำกัดแรงสูงสุดที่กระทำต่อชิ้นส่วนเพื่อป้องกันการทะลุหรือระเบิดกระจาย
        springForce = Vector3.ClampMagnitude(springForce, 300f);

        // 4. Apply the physical force
        rb.AddForce(springForce, ForceMode.Acceleration);
    }
}
