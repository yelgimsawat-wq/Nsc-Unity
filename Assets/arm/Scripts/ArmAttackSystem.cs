using UnityEngine;

// ระบบโจมตี (Attack System)
public class ArmAttackSystem : MonoBehaviour
{
    private ArmAimSystem aimSystem;
    
    [Header("Combat Settings")]
    [Tooltip("ปลายกระบอกปืน/มือที่ใช้ปล่อยพลัง")]
    public Transform gunBarrel;

    private void Start()
    {
        // ดึงสคริปต์เล็งมาเก็บไว้
        aimSystem = GetComponent<ArmAimSystem>();
        
        if (aimSystem == null)
        {
            Debug.LogWarning("ArmAttackSystem ต้องการดึงข้อมูลจาก ArmAimSystem แต่หาไม่เจอ! กรุณาใส่ ArmAimSystem ไว้ที่ GameObject เดียวกัน");
        }
    }

    private void Update()
    {
        // เช็คคลิกซ้าย (รองรับ New Input System)
        if (UnityEngine.InputSystem.Mouse.current != null && UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame)
        {
            Fire();
        }
    }

    private void Fire()
    {
        if (aimSystem == null || gunBarrel == null) return;

        // อ่านค่าจุดเล็งปัจจุบันมาจาก ArmAimSystem (เปลี่ยนเป็นชื่อตัวแปรใหม่ CurrentMouseWorldPoint)
        Vector3 target = aimSystem.CurrentMouseWorldPoint;
        
        // ทิศทางที่กระสุนจะต้องพุ่งไป
        Vector3 shootDirection = (target - gunBarrel.position).normalized;

        Debug.Log("ยิงไปที่ทิศทาง: " + shootDirection);
        // ... โค้ดสร้างกระสุน (Instantiate) และยิงไปทิศทางนั้น
    }
}
