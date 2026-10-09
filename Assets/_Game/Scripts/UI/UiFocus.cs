using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ทะเบียนกลางว่าตอนนี้มี UI บานไหน "กินอินพุต" อยู่หรือเปล่า
///
/// ทำไมต้องมี:
///   สคริปต์ผู้เล่น (PlayerHandMovement / PlayerFootForRobot) จะล็อกเมาส์กลับทันที
///   ที่คลิกซ้ายตอนเคอร์เซอร์ไม่ล็อก — เจตนาคือ "คลิกเพื่อกลับเข้าเกม" แต่มันไม่รู้ว่า
///   ตอนนั้นมี UI เปิดอยู่หรือเปล่า พอเปิดวงล้อไอเทม/เมนูแล้วกดปุ่มแรก เคอร์เซอร์เลยหายทันที
///   ตัวนี้เป็นสวิตช์กลางให้ทุกฝ่ายดูค่าเดียวกัน
///
/// วิธีใช้ (ฝั่ง UI):
///   OnOpen  → UiFocus.Push(this)
///   OnClose → UiFocus.Pop(this)
///   อย่าลืม Pop ใน OnDisable/OnDestroy ด้วย เผื่อถูกปิดกลางคัน
///
/// วิธีใช้ (ฝั่งที่ต้องหลบ):
///   if (UiFocus.IsCaptured) return;   // อย่าไปแย่งเมาส์/รับอินพุตโลก
///
/// เก็บเป็น stack เพราะเปิดซ้อนกันได้ (เมนู → แผงตั้งค่า) ปุ่ม ESC จะได้ปิดทีละชั้นจากบนลงล่าง
/// เจ้าของที่ถูกทำลายไปแล้วจะถูกคัดออกเองตอนอ่านค่า — กัน UI ที่ลืม Pop ทำให้เมาส์ค้างถาวร
/// </summary>
public static class UiFocus
{
    private static readonly List<MonoBehaviour> stack = new List<MonoBehaviour>();

    /// <summary>มี UI เปิดอยู่ไหม — ตัวที่ต้องหลบเช็คตัวนี้ตัวเดียวพอ</summary>
    public static bool IsCaptured
    {
        get
        {
            Prune();
            return stack.Count > 0;
        }
    }

    /// <summary>จำนวนชั้นที่เปิดซ้อนอยู่ (ไว้ดีบัก)</summary>
    public static int Depth
    {
        get
        {
            Prune();
            return stack.Count;
        }
    }

    /// <summary>UI ชั้นบนสุด — ปุ่ม ESC ควรสั่งปิดตัวนี้ก่อน</summary>
    public static MonoBehaviour Top
    {
        get
        {
            Prune();
            return stack.Count > 0 ? stack[stack.Count - 1] : null;
        }
    }

    /// <summary>ตัวนี้อยู่ชั้นบนสุดหรือเปล่า — ใช้ตัดสินว่าใครควรกิน ESC</summary>
    public static bool IsTop(MonoBehaviour owner)
    {
        if (owner == null) return false;
        return ReferenceEquals(Top, owner);
    }

    public static void Push(MonoBehaviour owner)
    {
        if (owner == null) return;

        Prune();
        stack.Remove(owner);   // กันดันซ้ำ — ตัวเดิมย้ายขึ้นบนสุดแทนที่จะมีสองรายการ
        stack.Add(owner);
    }

    public static void Pop(MonoBehaviour owner)
    {
        if (owner == null) return;

        stack.Remove(owner);
        Prune();
    }

    /// <summary>ล้างทั้งหมด — ใช้ตอนเปลี่ยนฉากหรือหลุดออกจากแมตช์</summary>
    public static void Clear() => stack.Clear();

    /// <summary>คัดเจ้าของที่ถูก Destroy ไปแล้วออก (Unity override == null ให้ตรวจได้)</summary>
    private static void Prune()
    {
        for (int i = stack.Count - 1; i >= 0; i--)
        {
            if (stack[i] == null) stack.RemoveAt(i);
        }
    }

    // เคลียร์ค่า static ทุกครั้งที่เริ่มเล่นใหม่ เผื่อโปรเจกต์ปิด Domain Reload ไว้
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => stack.Clear();
}
