using UnityEngine;

// ระบบเล็งแขน (Aim System) - ลากกระดูกไหล่ และเป้าหมายมือมาใส่
public class ArmAimSystem : MonoBehaviour
{
    [Header("Arm Setup")]
    [Tooltip("จุดหมุนแขนหลักที่ไหล่ (ลากกระดูกหัวไหล่มาใส่)")]
    public Transform shoulder;
    
    [Tooltip("จุดเป้าหมายที่มือจะเคลื่อนไปหา (สร้าง Empty GameObject แยกไว้ข้างนอก)")]
    public Transform handTarget;

    [Header("Aim Settings")]
    [Tooltip("เลเยอร์ของพื้น (Ground) เพื่อหาตำแหน่งใน 3D")]
    public LayerMask groundLayer;
    [Tooltip("ความเร็วในการหมุนแขนให้ดูนุ่มนวล")]
    public float rotateSpeed = 8f;

    [Header("Rotation Correction (แก้ทิศทางโมเดล)")]
    [Tooltip("ปรับแก้แกนหมุนของโมเดลที่มาจาก Blender เช่น x:0, y:180, z:0 ให้แขนหันหน้าถูกทิศ")]
    public Vector3 rotationOffset = new Vector3(0, 180, 0);

    // ตำแหน่งที่เมาส์ชี้บนพื้นโลกจริง
    public Vector3 CurrentMouseWorldPoint { get; private set; }

    private Camera mainCam;

    private void Start()
    {
        mainCam = Camera.main;
        
        if (handTarget == null)
        {
            Debug.LogWarning("ยังไม่ได้ลาก handTarget มาใส่ใน ArmAimSystem ครับ!");
        }
    }

    private void Update()
    {
        UpdateHandTargetPosition();
        RotateShoulderToHand();
    }

    // 1. หาตำแหน่งเมาส์ใน World Space แล้วย้าย handTarget ไปตรงนั้น
    private void UpdateHandTargetPosition()
    {
        if (handTarget == null || mainCam == null) return;

        // รองรับ New Input System
        Vector2 mousePos = UnityEngine.InputSystem.Mouse.current != null 
            ? UnityEngine.InputSystem.Mouse.current.position.ReadValue() 
            : Vector2.zero;
            
        Ray ray = mainCam.ScreenPointToRay(mousePos);
        
        // --- วิธีแก้ปัญหาชี้เมาส์แล้วไม่ขยับ ---
        // ใช้ "พื้นจำลองทางคณิตศาสตร์" (Plane) ที่ความสูง Y = 0 (ระดับเดียวกับพื้นโลก)
        // วิธีนี้จะแม่นยำ 100% โดยไม่ต้องพึ่งหา Collider หรือตั้งค่า LayerMask เลย
        Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
        
        // ถ้ายิง Ray ของเมาส์ไปชนพื้นจำลอง
        if (groundPlane.Raycast(ray, out float enterDistance))
        {
            // หาพิกัด 3D ที่จุดชนของมันมาเก็บไว้
            CurrentMouseWorldPoint = ray.GetPoint(enterDistance);
        }
        else
        {
            // ถ้าไม่โดนพื้น (เช่นหันกล้องขึ้นฟ้า) ให้ตั้งระยะห่างออกไปแทน
            CurrentMouseWorldPoint = ray.GetPoint(10f);
        }

        // ย้ายจุดเป้าหมาย (มือ) ไปที่ตำแหน่งเมาส์ทันที
        handTarget.position = CurrentMouseWorldPoint;
    }

    // 2. หมุนไหล่ให้หันไปหาเป้าหมายที่มือ
    private void RotateShoulderToHand()
    {
        if (shoulder == null || handTarget == null) return;

        // คำนวณหาทิศทางที่ไหล่ต้องหันไป (จากไหล่ ไปหา มือ)
        Vector3 aimDirection = handTarget.position - shoulder.position;

        // ป้องกัน Error ในกรณีที่มือขยับมาทับไหล่เป๊ะๆ
        if (aimDirection.sqrMagnitude > 0.001f)
        {
            // คำนวณหา Rotation แบบบังคับหันตรงๆ ไปหาเป้าหมาย
            Quaternion targetRotation = Quaternion.LookRotation(aimDirection);
            
            // คูณปรับทิศทางแก้ปัญหาโมเดลผิดแกนจาก Blender
            targetRotation *= Quaternion.Euler(rotationOffset);

            // ใช้ Slerp เพื่อให้การหมุนมีความนุ่มนวล
            shoulder.rotation = Quaternion.Slerp(shoulder.rotation, targetRotation, Time.deltaTime * rotateSpeed);
        }
    }
}
