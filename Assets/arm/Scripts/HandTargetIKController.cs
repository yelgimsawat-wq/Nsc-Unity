using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// สคริปต์สำหรับหุ่นยนต์ที่ใช้ Animation Rigging (Two Bone IK)
// หน้าที่: บังคับให้ HandTarget วิ่งไถลตามเป้าเมาส์ในโลก 3D
public class HandTargetIKController : MonoBehaviour
{
    [Header("IK Targets")]
    [Tooltip("ลาก GameObject HandTarget ของระบบ IK มาใส่ที่นี่")]
    public Transform handTarget;

    [Header("Tracking Settings")]
    [Tooltip("ระดับความสูงของมือจากพื้น (ยกสูงขึ้นเผื่อไม่ให้มือจมดิน)")]
    public float heightOffset = 1.2f;
    
    [Tooltip("เลเยอร์ของพื้น ที่ต้องการให้เมาส์สามารถชี้ได้ (ตั้งให้ตรงกับ Layer ของพื้นในฉาก)")]
    public LayerMask groundLayer = ~0; // ค่าเริ่มต้นเป็น Everything

    private Camera mainCam;

    private void Start()
    {
        // 1. ดึง Camera.main มาเก็บไว้ใช้
        mainCam = Camera.main;

        if (mainCam == null)
        {
            Debug.LogError("[HandTargetIKController] ไม่พบ Camera.main ในฉาก!");
        }
        
        if (handTarget == null)
        {
            Debug.LogError("[HandTargetIKController] ยังไม่ได้ลาก handTarget มาใส่ช่อง Inspector");
        }
    }

    private void Update()
    {
        if (mainCam == null || handTarget == null) return;

        UpdateTargetPosition();
    }

    // เลื่อน HandTarget ไปอยู่จุดที่เมาส์ชี้บนพื้น 3D
    private void UpdateTargetPosition()
    {
        // ยิง Ray จากกล้องไปยังตำแหน่งเมาส์บนหน้าจอ
        Ray ray = mainCam.ScreenPointToRay(GetMousePosition());

        // ตรวจสอบว่า Raycast ชนกับวัตถุที่มี Layer ตรงกับ groundLayer หรือไม่
        if (Physics.Raycast(ray, out RaycastHit hit, 1000f, groundLayer))
        {
            // ถ้าชน ให้ดึงพิกัดที่ชนออกมา
            Vector3 targetPosition = hit.point;
            
            // ยกความสูงขึ้นตามค่า heightOffset 
            // (เพื่อให้แน่ใจว่ามือจะลอยอยู่เหนือพื้น ไม่จมดิน)
            targetPosition.y += heightOffset;

            // ย้าย HandTarget ไปยังตำแหน่งนั้นทันที
            handTarget.position = targetPosition;

            // วาดเส้นเขียว Debug ในหน้าจอ Scene ให้เห็นว่าจุดเล็งอยู่ตรงไหน
            Debug.DrawLine(ray.origin, hit.point, Color.green);
        }
    }

    // ฟังก์ชันช่วยหา Mouse Position รองรับทั้ง Input เก่าและใหม่ (เสถียรสุด)
    private Vector2 GetMousePosition()
    {
#if ENABLE_INPUT_SYSTEM
        if (UnityEngine.InputSystem.Mouse.current != null)
        {
            return UnityEngine.InputSystem.Mouse.current.position.ReadValue();
        }
#endif
        return Input.mousePosition;
    }
}
