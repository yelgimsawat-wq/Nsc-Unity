using UnityEngine;

/// <summary>
/// ตัวตัดสินว่า "ล้อเมาส์เฟรมนี้เป็นของใคร" — กันสองระบบแย่งล้อกันเงียบๆ
///
/// เดิมล้อเมาส์เป็นของกล้องอย่างเดียว (ซูมเข้า/ออก) แต่ตอนนี้ระบบควบคุมแขนขาจองไปใช้:
///   • PlayerFootForRobot  → ปรับ "ระยะก้าว" ของเท้า
///   • PlayerHandMovement  → ปรับ "ความลึก" ของมือ (เข้า/ออกจากตัว)
///
/// กติกา: limb ที่ผู้เล่นเครื่องนี้เป็นเจ้าของจะ Claim() ทุกเฟรมที่มันถือสิทธิ์อยู่
/// แล้ว PlayerCam จะซูมได้ก็ต่อเมื่อ "ไม่มีใครจอง" (เช่นผู้เล่นยังไม่ได้ spawn/อยู่ในเมนู)
/// หรือ "กำลังกดคลิกขวาค้าง" ซึ่งเป็นโหมดกล้องเต็มตัว
///
/// ⏱️ ลำดับเวลาปลอดภัยเสมอ: limb อ่าน input ใน Update() ซึ่งจบก่อน LateUpdate() ของกล้อง
/// เผื่ออีก 1 เฟรม (frameCount - 1) กันเคสที่ limb ถูกปิด/เปิดสลับกันชั่วขณะแล้วธงกะพริบ
/// จนกล้องแอบซูมแทรกระหว่างการหมุนล้อสองคลิกติดกัน
/// </summary>
public static class MouseWheelFocus
{
    private static int _claimFrame = -1;

    /// <summary>เรียกทุกเฟรมที่ระบบแขน/ขาถือสิทธิ์ล้อเมาส์อยู่</summary>
    public static void Claim() => _claimFrame = Time.frameCount;

    /// <summary>มี limb จองล้อเมาส์อยู่ไหมในเฟรมนี้</summary>
    public static bool IsClaimed => _claimFrame >= Time.frameCount - 1;
}
