using UnityEngine;

/// <summary>
/// ค่าความไวเมาส์ (sensitivity) ที่ใช้ร่วมกันทั้งเกม
///
/// ทำไมต้องเป็น static ไม่ผูกกับ SettingsManager:
///   สคริปต์มือ/เท้า/กล้อง อยู่บนหุ่นในฉากเกม ส่วนแผงตั้งค่าอยู่คนละที่และอาจยังไม่เกิด
///   ถ้าให้ไปหา SettingsManager.Instance ทุกเฟรมจะทั้งช้าและพังตอนเทสฉากเกมตรงๆ
///   ตัวนี้อ่านจาก PlayerPrefs เองได้เลย ไม่ต้องมีใครมาป้อนให้
///
/// วิธีใช้ (ฝั่งที่รับอินพุตเมาส์):
///   delta * (mouseSensitivity * MouseSettings.Multiplier)
///   คือเอาค่าที่ตั้งไว้ใน Inspector เป็น "ค่าพื้นฐานของชิ้นส่วนนั้น" แล้วคูณด้วยค่าที่ผู้เล่นปรับ
///   จะได้ไม่ต้องไปไล่แก้ค่าใน Inspector ของทุกหุ่นทุกฉาก
/// </summary>
public static class MouseSettings
{
    public const string PrefsKey = "MouseSensitivity";

    public const float Default = 1f;
    public const float Min = 0.2f;
    public const float Max = 3f;

    // -1 = ยังไม่เคยอ่าน — อ่าน PlayerPrefs ครั้งเดียวแล้วจำไว้ (อ่านทุกเฟรมมันช้า)
    private static float cached = -1f;

    /// <summary>ตัวคูณความไวเมาส์ที่ผู้เล่นตั้งไว้ (1 = ค่ามาตรฐาน)</summary>
    public static float Multiplier
    {
        get
        {
            if (cached < 0f)
                cached = Mathf.Clamp(PlayerPrefs.GetFloat(PrefsKey, Default), Min, Max);
            return cached;
        }
    }

    /// <summary>ตั้งค่าใหม่ + เซฟลง PlayerPrefs — เรียกจากสไลเดอร์ในหน้าตั้งค่า</summary>
    public static void Set(float value)
    {
        cached = Mathf.Clamp(value, Min, Max);
        PlayerPrefs.SetFloat(PrefsKey, cached);
        PlayerPrefs.Save();
    }

    /// <summary>ข้อความโชว์ข้างสไลเดอร์ เช่น "1.25x"</summary>
    public static string Format(float value) => $"{value:0.00}x";

    // ค่า static ค้างข้ามการกด Play ได้ถ้าปิด Domain Reload — ล้างทุกครั้งที่เริ่มเล่นใหม่
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => cached = -1f;
}
