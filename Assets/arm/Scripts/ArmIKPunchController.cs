using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// ระบบควบคุมการชกและการเล็งของแขนที่ใช้ IK (Arm IK Punch Controller)
/// ทำหน้าที่รับตำแหน่งเมาส์ ขยับเป้าหมาย IK (HandTarget) ให้ตามเมาส์
/// และสั่งเล่น Animator ท่าชก (Punch) พร้อมๆ กัน
/// </summary>
[RequireComponent(typeof(Animator))]
public class ArmIKPunchController : MonoBehaviour
{
    [Header("IK References")]
    [Tooltip("ใส่ Transform ของตัวเป้าหมายที่ IK ข้อมือวิ่งตาม")]
    public Transform handTarget;

    [Header("Aim Settings")]
    [Tooltip("ความสูงของลูกศรเมาส์ตอนตกกระทบพื้น")]
    public float aimHeightOffset = 1.0f;
    [Tooltip("Layer ของพื้นที่ใช้รับการยิงเมาส์")]
    public LayerMask groundLayer = ~0; // ค่าเริ่มต้นเป็น All Layers

    [Header("Animator Settings")]
    [Tooltip("พารามิเตอร์ Trigger สำหรับท่าชก")]
    public string punchTriggerParam = "Punch";

    private Animator animator;
    private int punchTriggerHash;
    private Camera mainCamera;

    // ตัวแปรเก็บตำแหน่งเป้าหมายล่าสุด
    private Vector3 targetWorldPosition;

    private void Start()
    {
        animator = GetComponent<Animator>();
        mainCamera = Camera.main;

        if (animator == null)
        {
            Debug.LogError("[ArmIKPunchController] ไม่พบ Animator Component!");
        }

        if (handTarget == null)
        {
            Debug.LogError("[ArmIKPunchController] ยังไม่ได้ใส่ HandTarget!");
        }

        if (mainCamera == null)
        {
            Debug.LogError("[ArmIKPunchController] ไม่พบ Main Camera ใน Scene!");
        }

        punchTriggerHash = Animator.StringToHash(punchTriggerParam);
    }

    private void Update()
    {
        // 1. อ่าน Input เมาส์และการคลิก
        HandleInput();
    }

    /// <summary>
    /// ทำงานหลักจากอนิเมชั่น (Animator ทำงานใน Update เสร็จแล้ว)
    /// ใช้ LateUpdate เพื่อเขียนทับ (Override) ตำแหน่งของมือด้วย IK 
    /// เพื่อให้มือชี้ตามเมาส์เสมอ แม้ตัว Animator จะพยายามดึงมือไปทางอื่น
    /// </summary>
    private void LateUpdate()
    {
        // 2. อัปเดตตำแหน่ง HandTarget ไปที่ตำแหน่งเมาส์
        if (handTarget != null)
        {
            handTarget.position = targetWorldPosition;
        }

        // หมายเหตุของ Unity Animation Rigging:
        // ส่วนแพ็คเกจยึดกระดูก (เช่น Two Bone IK) มันจะไปดึงกระดูกมือให้มาหา HandTarget นี้เองอีกทีอัตโนมัติ
        // ผลลัพธ์คือ Animator คุมรอยพับแขน แต่ IK สั่งดึงข้อมือมาที่เมาส์เสมอ
    }

    private void HandleInput()
    {
        // --- ส่วนที่ 1: การเล็งเมาส์ (Aiming) ---
        Vector2 mousePos = Vector2.zero;
        bool isPunchPressed = false;

#if ENABLE_INPUT_SYSTEM
        // ใช้ New Input System
        if (UnityEngine.InputSystem.Mouse.current != null)
        {
            mousePos = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
            isPunchPressed = UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame;
        }
#else
        // ใช้ Legacy Input
        mousePos = Input.mousePosition;
        isPunchPressed = Input.GetMouseButtonDown(0);
#endif

        // ยิง Raycast จากกล้องไปที่เมาส์เพื่อหาตำแหน่งในโลก 3 มิติ
        if (mainCamera != null)
        {
            Ray ray = mainCamera.ScreenPointToRay(mousePos);
            
            // สร้างระนาบสมมติ (Plane) ที่ความสูงที่กำหนด เพื่อรับเมาส์ไม่ให้ตกทะลุแมพ
            Plane groundPlane = new Plane(Vector3.up, new Vector3(0, aimHeightOffset, 0));
            float distance;

            if (groundPlane.Raycast(ray, out distance))
            {
                // บันทึกตำแหน่งจุดตัดบนระนาบ
                targetWorldPosition = ray.GetPoint(distance);
            }
        }

        // --- ส่วนที่ 2: การสั่งต่อย (Punching) ---
        if (isPunchPressed && animator != null)
        {
            ExecutePunch();
        }
    }

    private void ExecutePunch()
    {
        // สั่งกระตุ้น Animator ให้เล่นท่าชก
        animator.SetTrigger(punchTriggerHash);
        // Debug.Log("[ArmIKPunchController] สั่งชก!");
    }
}
