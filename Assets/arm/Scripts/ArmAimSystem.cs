using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// ระบบเล็งแขน (Aim System) แบบเสถียร 100% พร้อม Debug
public class ArmAimSystem : MonoBehaviour
{
    [Header("Arm Setup")]
    [Tooltip("จุดหมุนแขนหลักที่ไหล่ (ลากกระดูกหัวไหล่มาใส่)")]
    public Transform shoulder;
    
    [Tooltip("จุดเป้าหมายที่มือจะเคลื่อนไปหา (สร้าง Empty GameObject แยกไว้ข้างนอก)")]
    public Transform handTarget;

    [Header("Aim Settings")]
    [Tooltip("ความเร็วในการหมุนแขนให้ดูนุ่มนวล")]
    public float rotateSpeed = 15f;
    [Tooltip("ใช้ความสูง Y ของพื้นฐาน ถ้าเมาส์กวาดไปในอวกาศแล้วหาไม่เจอ")]
    public float floorHeight = 0f;

    [Header("Rotation Correction (แก้ทิศทางโมเดล)")]
    [Tooltip("ปรับแก้แกนหมุนของโมเดลที่มาจาก Blender เช่น x:0, y:180, z:0 ให้แขนหันหน้าถูกทิศ")]
    public Vector3 rotationOffset = new Vector3(0, 180, 0);

    // ตำแหน่งที่เมาส์ชี้บนพื้นโลกจริง
    public Vector3 CurrentMouseWorldPoint { get; private set; }

    private Camera mainCam;

    private void Start()
    {
        // 1. ค้นหากล้องหลัก
        mainCam = Camera.main;
        if (mainCam == null)
        {
            Debug.LogError("[ArmAimSystem] หา Camera.main ไม่เจอ! กรุณาเช็คว่ากล้องในซีนมี Tag เป็น MainCamera แล้วหรือยัง");
        }

        if (shoulder == null)
        {
            Debug.LogError("[ArmAimSystem] ยังไม่ได้ลากกระดูกมาใส่ช่อง Shoulder!");
        }

        if (handTarget == null)
        {
            Debug.LogError("[ArmAimSystem] ยังไม่ได้ลากเป้ามือมาใส่ช่อง HandTarget!");
        }
    }

    private void Update()
    {
        // 2. Debug ตามที่ขอ เพื่อให้รู้ว่าสคริปต์นี้ถูกรันจริงๆ ทุกเฟรม
        Debug.Log("ArmAim running");

        if (mainCam == null) return;
        
        UpdateHandTargetPosition();
        RotateShoulderToHand();
    }

    // ฟังก์ชันช่วยหา Mouse Position รองรับทั้ง Input เก่าและ Input System ใหม่
    private Vector2 GetMousePosition()
    {
#if ENABLE_INPUT_SYSTEM
        // 3. ถ้าใช้ New Input System
        if (UnityEngine.InputSystem.Mouse.current != null)
        {
            return UnityEngine.InputSystem.Mouse.current.position.ReadValue();
        }
#endif
        // 4. Fallback ไปใช้ Input แบบเก่าที่เสถียรกว่าในบาง Project
        return Input.mousePosition;
    }

    // 1. หาตำแหน่งเมาส์ใน World Space แล้วย้าย handTarget ไปตรงนั้น
    private void UpdateHandTargetPosition()
    {
        if (handTarget == null) return;

        // ดึงตำแหน่งเมาส์บนหน้าจอที่ปลอดภัยที่สุดไม่ว่าจะใช้ระบบ Input ไหน
        Vector2 mousePos = GetMousePosition();
        
        // สร้าง Ray ยิงจากกล้องทะลุเมาส์ลงในฉาก 3D
        Ray ray = mainCam.ScreenPointToRay(mousePos);
        
        // จำลองพื้น 수학(Plane) ที่ระดับ Y = floorHeight อัตโนมัติ (ไม่ต้องพึ่ง Collider)
        Plane groundPlane = new Plane(Vector3.up, new Vector3(0, floorHeight, 0));
        
        // ถ้ายิงจุดของเมาส์ไปชนพื้นจำลองสำเร็จ
        if (groundPlane.Raycast(ray, out float enterDistance))
        {
            // หาพิกัด 3D ที่จุดชนนั้น
            CurrentMouseWorldPoint = ray.GetPoint(enterDistance);
            
            // Debug วาดเส้นยาวยิงจากกล้องลงพื้น จะได้เห็นจุดตกของเมาส์ในหน้าต่าง Scene ด้วย
            Debug.DrawLine(ray.origin, CurrentMouseWorldPoint, Color.green);
        }
        else
        {
            // ถ้าไม่โดนอะไรเลย เช่น กล้องหงายขึ้นฟ้า ก็ให้เมาส์ผลักไปข้างหน้า 15 เมตรชั่วคราว
            CurrentMouseWorldPoint = ray.GetPoint(15f);
        }

        // 5. บังคับย้ายจุดเป้าหมาย (มือ) ไปที่ตำแหน่งเมาส์ทันที!
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
