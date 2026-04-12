using UnityEngine;
using Unity.Netcode;

public class EZFootMovement : NetworkBehaviour
{
    [Header("Settings")]
    public float speed = 5f;
    public float pushForce = 100f; // แรงที่ใช้ยันตัวเครื่อง
    public float detectionRadius = 1f; // รัศมีตรวจสอบการชนที่เท้า
    public LayerMask groundLayer; // ตั้งค่า Layer ของพื้นและผนัง

    [Header("References")]
    private Rigidbody torsoRb;
    private Transform physicalFootTransform; // ตัวแปรอ้างอิงไปยังเท้าจริงใน RobotContainer
    [SerializeField] GameObject torso;

    public void press()
    {
        // 1. ค้นหา Rigidbody ของตัวเครื่องจาก "ชื่อ" แทนการใช้ RobotBrain
        GameObject torsoObject = GameObject.FindGameObjectWithTag("Body");

        if (torsoObject != null)
        {
            torsoRb = torsoObject.GetComponent<Rigidbody>();
        }
        else
        {
            Debug.LogError("หาชิ้นส่วน 'Torso' ไม่เจอ! รบกวนตั้งชื่อตัวเครื่องหุ่นยนต์หลักใน Hierarchy ว่า 'Torso' ด้วยครับ");
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

        // 1. การเคลื่อนที่ของตัว Player Object (ตัวล่องหน)
        float moveX = Input.GetAxis("Horizontal");
        float moveZ = Input.GetAxis("Vertical");
        float moveY = 0f;

        if (Input.GetKey(KeyCode.Q)) moveY = 1f;
        else if (Input.GetKey(KeyCode.E)) moveY = -1f;

        Vector3 moveDir = new Vector3(moveX, moveY, moveZ);
        transform.Translate(moveDir * speed * Time.deltaTime);

        // 2. ระบบการ "ยัน" (Pushing Logic)
        if (physicalFootTransform != null && moveDir.magnitude > 0.1f)
        {
            HandlePushing(moveDir);
        }
    }

    private void HandlePushing(Vector3 moveInput)
    {
        // สร้างทรงกลมล่องหนเช็คการชนรอบๆ เท้าจริง
        Collider[] colliders = Physics.OverlapSphere(physicalFootTransform.position, detectionRadius, groundLayer);

        if (colliders.Length > 0)
        {
            // ส่งแรงผลักไปยังทิศทางตรงกันข้ามกับที่เรากด (Reaction Force)
            Vector3 reactionDirection = -moveInput.normalized;

            // ส่งคำสั่งไปรันที่ Server เพื่อผลักตัวเครื่อง
            ApplyPushForceServerRpc(reactionDirection * pushForce);
        }
    }

    [ServerRpc]
    void ApplyPushForceServerRpc(Vector3 force)
    {
        if (torsoRb != null)
        {
            // ผลักตัวเครื่อง (Torso) ในทิศตรงข้าม
            torsoRb.AddForce(force, ForceMode.Force);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (physicalFootTransform != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(physicalFootTransform.position, detectionRadius);
        }
    }
}