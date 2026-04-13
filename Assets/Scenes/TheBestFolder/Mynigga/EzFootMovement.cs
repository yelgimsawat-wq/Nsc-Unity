using UnityEngine;
using Unity.Netcode;

public class EZFootMovement : NetworkBehaviour
{
    [Header("Movement Settings")]
    public float speed = 8f;
    public float pushForce = 50f;
    public float hitRadius = 0.2f; // รัศมีลูกบอลสำหรับเช็คการชน (เล็กๆ พอ)
    public LayerMask groundLayer;

    [Header("Leash Settings")]
    public float maxLeashLength = 4f;

    [Header("References")]
    private Rigidbody torsoRb;
    public Transform physicalFootTransform;

    public void press()
    {
        GameObject torsoObject = GameObject.FindGameObjectWithTag("Body");
        if (torsoObject != null) torsoRb = torsoObject.GetComponent<Rigidbody>();

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
        if (!IsOwner || torsoRb == null) return;

        // 1. รับค่า Input
        Vector3 inputDir = new Vector3(Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical"));
        if (Input.GetKey(KeyCode.Q)) inputDir.y = 1f;
        else if (Input.GetKey(KeyCode.E)) inputDir.y = -1f;

        Vector3 moveDir = inputDir.normalized;
        bool isPushingIntoSurface = false;

        // 2. ตรวจสอบการ "ดัน" เข้าหาพื้น (Solid Interaction)
        if (moveDir.magnitude > 0.1f)
        {
            // ถอยหลังจุดยิงนิดนึง (- moveDir * 0.1f) เพื่อป้องกันบัคกรณีที่ตัวเราจมอยู่ในพื้นผิวไปแล้ว
            if (Physics.SphereCast(transform.position - moveDir * 0.1f, hitRadius, moveDir, out RaycastHit hit, 0.3f, groundLayer))
            {
                // เช็ค Dot Product: ถ้าค่าน้อยกว่าศูนย์ (ติดลบ) แปลว่าแรงกดกำลัง "พุ่งเข้าหา" หรือ "กดเฉียงเข้าหา" ผิวหน้านั้น
                if (Vector3.Dot(moveDir, hit.normal) < -0.05f)
                {
                    isPushingIntoSurface = true;

                    // ส่งแรงถีบสวนทางกับทิศทางที่กด
                    ApplyPushForceServerRpc(-moveDir * pushForce);
                }
            }
        }

        // 3. จัดการการเคลื่อนที่
        if (isPushingIntoSurface)
        {
            // [สถานะ: ยันพื้น]
            // ไม่ต้องทำอะไรเลย ปล่อยให้ moveDir ไม่ถูกนำไปใช้ขยับตำแหน่ง (หยุดนิ่งกึก)
        }
        else if (moveDir.magnitude > 0.1f)
        {
            // [สถานะ: บินอิสระ]
            // ถ้ากดขนานไปกับพื้น หรือกดดึงตัวออก (Dot ไม่ติดลบ) จะสามารถขยับได้ทันที
            // ไม่มีระบบ Snap ดูดติดพื้นแล้ว ทำให้ลอยออกได้ง่ายมาก
            transform.Translate(moveDir * speed * Time.deltaTime, Space.World);
        }

        // 4. ให้เชือกคอยลากตัวเราตาม Body เสมอ
        ApplyLeash();
    }

    private void ApplyLeash()
    {
        Vector3 offset = transform.position - torsoRb.position;
        if (offset.magnitude > maxLeashLength)
        {
            transform.position = torsoRb.position + (offset.normalized * maxLeashLength);
        }
    }

    [ServerRpc]
    void ApplyPushForceServerRpc(Vector3 force)
    {
        if (torsoRb != null)
        {
            torsoRb.AddForce(force, ForceMode.Acceleration);
        }
    }

    private void OnDrawGizmos()
    {
        // วาดให้เห็นรัศมีการชน
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, hitRadius);
    }
}