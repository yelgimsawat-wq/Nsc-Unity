using UnityEngine;

/// <summary>
/// Shared utility extensions for Vector3.
/// แยกไว้ไฟล์เดียวเพื่อให้ทุกคลาสในโปรเจกต์ใช้ร่วมกันได้
/// </summary>
public static class Vector3Extensions
{
    /// <summary>
    /// ตรวจสอบว่า Vector3 ไม่มีค่า NaN หรือ Infinity
    /// ใช้ validate ข้อมูลก่อน apply ฟิสิกส์หรือส่งผ่าน RPC
    /// </summary>
    public static bool IsValid(this Vector3 v) =>
        !float.IsNaN(v.x)      && !float.IsNaN(v.y)      && !float.IsNaN(v.z) &&
        !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
}
