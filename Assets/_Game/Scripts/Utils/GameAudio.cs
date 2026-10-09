using System.Collections.Generic;
using UnityEngine;

/// <summary>หมวดของเสียงในเกม — ใช้แยกสไลเดอร์ความดัง</summary>
public enum AudioBus
{
    /// <summary>เสียงต่อสู้/กระแทก/ปืน — ของที่ดังเป็นครั้งๆ</summary>
    Sfx = 0,
    /// <summary>เสียงบรรยากาศ ลม พอร์ทัล เสียงหวีดในหู — ของที่ดังยาวๆ อยู่เบื้องหลัง</summary>
    Ambient = 1,
    /// <summary>เสียงเมนู/UI</summary>
    Ui = 2
}

/// <summary>
/// คุมความดังแยกเป็นหมวด โดยไม่ต้องใช้ AudioMixer
///
/// ทำไมไม่ใช้ AudioMixer:
///   ไฟล์ .mixer สร้างจากสคริปต์ไม่ได้ (ต้องกดสร้างในหน้าต่าง Unity แล้ว expose ค่าด้วยมือทีละตัว)
///   โปรเจกต์นี้มี AudioSource แค่ไม่กี่ตัว การคุมความดังตรงๆ จึงคุ้มกว่าและทำอัตโนมัติได้ทั้งหมด
///   ⚠️ ข้อแลก: ทำเอฟเฟกต์แบบ reverb/ducking/compressor ไม่ได้ ถ้าวันหลังต้องการต้องย้ายไป AudioMixer
///
/// Master ไม่ได้อยู่ในนี้ — ตัวนั้นใช้ AudioListener.volume ซึ่งคุมทุกอย่างอยู่แล้ว
/// เสียงพูดก็ไม่อยู่ในนี้ — VoiceChat คุมของตัวเองและตั้งใจข้าม AudioListener
/// </summary>
public static class GameAudio
{
    private const string PrefPrefix = "Audio_Bus_";
    private const int BusCount = 3;

    private static readonly List<AudioCategory> registered = new List<AudioCategory>();
    private static readonly float[] busVolume = { 1f, 1f, 1f };
    private static bool loaded;

    public static float GetBusVolume(AudioBus bus)
    {
        EnsureLoaded();
        return busVolume[(int)bus];
    }

    public static void SetBusVolume(AudioBus bus, float value, bool save = true)
    {
        EnsureLoaded();

        value = Mathf.Clamp01(value);
        busVolume[(int)bus] = value;

        if (save)
        {
            PlayerPrefs.SetFloat(PrefPrefix + bus, value);
            PlayerPrefs.Save();
        }

        // แจ้งทุกตัวที่ลงทะเบียนไว้ให้คิดความดังใหม่ทันที
        for (int i = registered.Count - 1; i >= 0; i--)
        {
            if (registered[i] == null) { registered.RemoveAt(i); continue; }
            if (registered[i].Bus == bus) registered[i].ApplyVolume();
        }
    }

    public static void Register(AudioCategory category)
    {
        if (category == null || registered.Contains(category)) return;
        registered.Add(category);
    }

    public static void Unregister(AudioCategory category)
    {
        if (category == null) return;
        registered.Remove(category);
    }

    private static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;

        for (int i = 0; i < BusCount; i++)
            busVolume[i] = PlayerPrefs.GetFloat(PrefPrefix + (AudioBus)i, 1f);
    }

    // static ค้างข้ามการกด Play ได้ถ้าโปรเจกต์ปิด Domain Reload — ต้องล้างทุกครั้ง
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        registered.Clear();
        loaded = false;
    }
}
