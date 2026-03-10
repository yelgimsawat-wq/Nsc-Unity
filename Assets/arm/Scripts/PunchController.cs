using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// ระบบควบคุมการชก (Punch Controller)
/// หน้าที่: รับ Input จากผู้เล่น (คลิกซ้าย หรือปุ่ม F) เพื่อสั่งให้ Animator เล่นท่าชก
/// </summary>
[RequireComponent(typeof(Animator))] // บังคับว่า GameObject นี้ต้องมี Animator แปะอยู่ด้วย
public class PunchController : MonoBehaviour
{
    [Header("Animator Settings")]
    [Tooltip("ชื่อ Parameter ประเภท Trigger ใน Animator (เช่น 'Punch')")]
    public string punchTriggerName = "Punch";

    // ตัวแปรเก็บ Component Animator
    private Animator animator;

    // ตัวแปรสำหรับแปลงชื่อ string เป็น int ID (ใช้เพื่อเพิ่มประสิทธิภาพการทำงานของ Animator)
    private int punchTriggerHash;

    private void Start()
    {
        // 1. หา Animator component ที่แปะอยู่บน GameObject เดียวกัน
        animator = GetComponent<Animator>();

        if (animator == null)
        {
            Debug.LogError("[PunchController] ไม่พบ Animator Component บน GameObject นี้!");
            return;
        }

        // แปลงชื่อ Parameter เป็น Hash ID เพื่อลดภาระการประมวลผล string ทุกครั้งที่กดชก
        punchTriggerHash = Animator.StringToHash(punchTriggerName);
    }

    private void Update()
    {
        // 2. ตรวจสอบเงื่อนไขปุ่มกด (Trigger Input)
        if (CheckPunchInput())
        {
            ExecutePunch();
        }
    }

    /// <summary>
    /// ฟังก์ชันเช็คว่าผู้เล่นกดปุ่มชกหรือไม่
    /// (รองรับทั้ง New Input System และระบบ Input แบบเก่า)
    /// </summary>
    private bool CheckPunchInput()
    {
        bool isPunching = false;

#if ENABLE_INPUT_SYSTEM
        // สำหรับโปรเจกต์ที่ใช้ New Input System
        if (UnityEngine.InputSystem.Mouse.current != null && UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame)
        {
            isPunching = true;
        }
        else if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.fKey.wasPressedThisFrame)
        {
            isPunching = true;
        }
#endif

        // ถ้ายังไม่ได้กดผ่าน New Input System หรือไม่ได้ใช้งาน ให้ fallback มาเช็ค Input เดิม
        if (!isPunching)
        {
            // คลิกซ้าย (0) หรือ ปุ่ม F
            if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.F))
            {
                isPunching = true;
            }
        }

        return isPunching;
    }

    /// <summary>
    /// สั่ง Execute ให้ Animator เล่นท่าชก
    /// </summary>
    private void ExecutePunch()
    {
        if (animator == null) return;

        // 3. เรียกใช้ animator.SetTrigger() เพื่อกระตุ้น State ใน Animator ให้เปลี่ยน
        // (เช่น ไหลจาก Idle -> ง้าง -> Punch)
        animator.SetTrigger(punchTriggerHash);
        
        Debug.Log("[PunchController] สั่งชก! (SetTrigger: " + punchTriggerName + ")");
    }
}
