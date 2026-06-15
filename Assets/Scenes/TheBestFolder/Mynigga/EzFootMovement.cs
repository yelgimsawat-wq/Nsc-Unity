using UnityEngine;
using Unity.Netcode;

public class EZFootMovement : NetworkBehaviour
{
    [Header("Movement Settings")]
    public float speed = 8f;
    public float hitRadius = 0.2f;
    public LayerMask groundLayer;
    public LayerMask graplayer;

    [Header("Jump Settings")]
    public float jumpForce = 400f;

    [Header("Spring Damper Settings (Stand Up Fast)")]
    [Tooltip("แรงดีดตัวให้หุ่นลุกยืนขึ้นอย่างรวดเร็ว")]
    public float springForce = 180f;
    [Tooltip("แรงเบรก/ตัวหน่วง ยิ่งเยอะหุ่นยิ่งไม่ลอยพ้นพื้น (แนะนำค่า 10-20)")]
    public float damperForce = 15f;

    [Header("Leash Settings")]
    [SerializeField] public float range = 4f;
    [SerializeField] public Transform attachPart;

    [Header("Max Range (from follower)")]
    [Tooltip("Maximum distance the player can be from its physical follower limb. 0 = no limit.")]
    [SerializeField] public float maxRange = 15f;

    [Header("References")]
    [SerializeField] private Rigidbody torsoRb;
    public Transform physicalFootTransform;

    [Header("Auto Stand Assist (Safe)")]
    public float targetBodyHeight = 1.6f;
    public float standSpringForce = 60f;
    public float standDamperForce = 10f;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        CheckAndCacheTorso();
    }

    private void CheckAndCacheTorso()
    {
        if (attachPart == null)
        {
            GameObject torsoObject = GameObject.FindGameObjectWithTag("Body");
            if (torsoObject != null) attachPart = torsoObject.transform;
        }

        if (attachPart != null && torsoRb == null)
        {
            torsoRb = attachPart.GetComponent<Rigidbody>();
        }
        if (torsoRb == null)
        {
            torsoRb = GameObject.FindGameObjectWithTag("Body").GetComponent<Rigidbody>();
        }
    }

    void FixedUpdate()
    {
        if (!IsServer || torsoRb == null) return;

        Vector3 footPos = physicalFootTransform != null ? physicalFootTransform.position : transform.position;

        // หาระยะความสูงปัจจุบัน
        float currentHeight = torsoRb.worldCenterOfMass.y - footPos.y;
        float heightError = targetBodyHeight - currentHeight;

        // คำนวณแรงสปริงในแนวตั้ง (ดันขึ้น)
        float verticalVelocity = torsoRb.linearVelocity.y;
        float upwardForce = (heightError * standSpringForce) - (verticalVelocity * standDamperForce);

        // ชดเชยแรงโน้มถ่วงเสมอ เพื่อให้ตัวไม่หนักและทรุดลงไป
        upwardForce += Mathf.Abs(Physics.gravity.y);

        // จำกัดแรงไว้ที่ 200f สูงสุด เพื่อป้องกันอาการ "หุ่นระเบิด" หรือเด้งรุนแรงเกินไป
        upwardForce = Mathf.Clamp(upwardForce, 0f, 200f);

        // ใช้ ForceMode.Acceleration (นุ่มนวลกว่า VelocityChange ปลอดภัยกับข้อต่อ)
        torsoRb.AddForce(Vector3.up * upwardForce, ForceMode.Acceleration);
    }

    // -------------------------------------------------------
    //  UPRIGHT CORRECTION — ดันให้ Torso ตั้งตรงแบบเสถียรที่สุด (ยืนหล่อๆ)
    // -------------------------------------------------------
    void ApplyUprightCorrection()
    {
        if (torsoRb == null) return;

        // ล็อคแกนหมุน X และ Z ของ Rigidbody เพื่อไม่ให้ล้ม 100% 
        // วิธีนี้จะทำให้มันยืนตรงเป๊ะๆ เหมือนตัวละครเกมทั่วไป
        torsoRb.constraints |= RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

        // รีเซ็ตความเร็วการหมุนที่อาจจะค้างอยู่
        torsoRb.angularVelocity = new Vector3(0f, torsoRb.angularVelocity.y, 0f);

        // จัดให้โมเดลตั้งตรง
        torsoRb.MoveRotation(Quaternion.Euler(0f, torsoRb.rotation.eulerAngles.y, 0f));
    }

    void Update()
    {
        if (!IsOwner) return;

        CheckAndCacheTorso();
        if (attachPart == null) return;

        ApplyUprightCorrection();

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
            RaycastHit hit;
            if (Physics.SphereCast(transform.position - moveDir * 0.1f, hitRadius, moveDir, out hit, 0.3f, groundLayer) ||
                Physics.SphereCast(transform.position - moveDir * 0.1f, hitRadius, moveDir, out hit, 0.3f, graplayer))
            {
                if (Vector3.Dot(moveDir, hit.normal) < -0.05f)
                {
                    isPushingIntoSurface = true;

                    if (isJumping)
                    {
                        // 1. ถ้ากด Spacebar ยิงแรงกระโดดเคลียร์แบบไร้ตัวหน่วง
                        ApplyJumpForceRpc(-moveDir * jumpForce);
                    }
                    else
                    {
                        // 2. ถ้ายืนเฉยๆ ส่งทิศทางแนวตั้ง (ดีดตัวขึ้น) ไปคำนวณสปริงตัวหน่วงบน Server
                        ApplyStandingSpringDamperRpc(-moveDir.normalized);
                    }
                }
            }

            if (!isPushingIntoSurface)
            {
                // Moving state (Normal walk)
                transform.Translate(moveDir * speed * Time.deltaTime, Space.World);
            }
        }
        else
        {
            // ถ้าไม่ได้กดปุ่มเดินใดๆ เลย ให้ส่งคำสั่งเบรกไปที่ Server เพื่อป้องกันตัวละครไหล
            ApplyBrakeRpc();
        }

        ApplyLeash();
        ClampToFollower();
    }

    // --- RPC สำหรับการเบรก (หยุดไหล) ---
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void ApplyBrakeRpc()
    {
        CheckAndCacheTorso();
        if (torsoRb != null)
        {
            Vector3 vel = torsoRb.linearVelocity;
            Vector3 horizontalVel = new Vector3(vel.x, 0f, vel.z);

            // ออกแรงต้านสวนทางกับความเร็วแนวนอน
            if (horizontalVel.magnitude > 0.1f)
            {
                torsoRb.AddForce(-horizontalVel * 15f, ForceMode.Acceleration);
            }
            else
            {
                // ถ้าความเร็วน้อยมากๆ ให้หยุดสนิทเลย จะได้ไม่ไหล
                torsoRb.linearVelocity = new Vector3(0f, vel.y, 0f);
            }
        }
    }

    // --- RPC สำหรับการกระโดดปกติ (ใส่แรงเต็มพิกัด) ---
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void ApplyJumpForceRpc(Vector3 force)
    {
        CheckAndCacheTorso();
        if (torsoRb != null)
        {
            torsoRb.AddForce(force, ForceMode.Acceleration);
        }
    }

    // --- RPC สำหรับการลุกยืน (ระบบ Spring-Damper) ---
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void ApplyStandingSpringDamperRpc(Vector3 pushDirection)
    {
        CheckAndCacheTorso();
        if (torsoRb != null)
        {
            // คำนวณความเร็วปัจจุบันของลำตัวในทิศทางที่พุ่งไป (ใช้ linearVelocity สำหรับ Unity รุ่นใหม่)
            float currentVelocityInDir = Vector3.Dot(torsoRb.linearVelocity, pushDirection);

            // สูตร Spring-Damper: แรงดันผลลัพธ์ = แรงสปริงดันขึ้น - (ความเร็วปัจจุบัน * ตัวหน่วงเบรก)
            float totalForce = springForce - (currentVelocityInDir * damperForce);

            // ป้องกันไม่ให้ตัวหน่วงดึงหุ่นจมลงดิน (แรงต้องไม่ติดลบ)
            if (totalForce < 0f) totalForce = 0f;

            torsoRb.AddForce(pushDirection * totalForce, ForceMode.Acceleration);
        }
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

    private void ClampToFollower()
    {
        if (physicalFootTransform == null || maxRange <= 0f) return;
        Vector3 offset = transform.position - physicalFootTransform.position;
        if (offset.magnitude > maxRange)
        {
            transform.position = physicalFootTransform.position + (offset.normalized * maxRange);
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, hitRadius);

        if (physicalFootTransform != null && maxRange > 0f)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(physicalFootTransform.position, maxRange);
        }
    }
}