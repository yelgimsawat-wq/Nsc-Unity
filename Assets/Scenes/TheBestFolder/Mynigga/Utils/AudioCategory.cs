using UnityEngine;

/// <summary>
/// บอกว่า AudioSource ตัวนี้อยู่หมวดไหน แล้วคูณความดังตามสไลเดอร์ของหมวดนั้น
///
/// วิธีคิดความดัง:  ความดังจริง = ค่าที่คนตั้งไว้/สคริปต์อื่นสั่ง × ความดังของหมวด
///
/// ⚠️ ทำไมต้องมี LateUpdate คอยจับ:
///    บางสคริปต์เขียน volume เองทุกเฟรม (เช่น PlayerFootMovementAudio ที่ค่อยๆ fade เสียงเดิน)
///    ถ้าเราตั้งค่าครั้งเดียวตอน Enable มันจะโดนเขียนทับแล้วสไลเดอร์ไม่มีผลกับเสียงนั้นเลย
///    จึงเช็คทุกเฟรมว่าค่าถูกใครเปลี่ยนหรือเปล่า ถ้าใช่ให้ถือเป็น "ค่าฐาน" ใหม่แล้วคูณของเราทับอีกที
/// </summary>
[RequireComponent(typeof(AudioSource))]
[DisallowMultipleComponent]
public class AudioCategory : MonoBehaviour
{
    [Tooltip("หมวดของเสียงนี้ — ใช้เลือกว่าจะโดนสไลเดอร์ตัวไหนคุม")]
    [SerializeField] private AudioBus bus = AudioBus.Sfx;

    public AudioBus Bus => bus;

    private AudioSource source;
    private float baseVolume = 1f;
    private float lastApplied = -1f;

    private void Awake()
    {
        source = GetComponent<AudioSource>();
        if (source != null) baseVolume = source.volume;
    }

    private void OnEnable()
    {
        GameAudio.Register(this);
        ApplyVolume();
    }

    private void OnDisable()
    {
        GameAudio.Unregister(this);
    }

    private void LateUpdate()
    {
        if (source == null) return;

        // ค่าไม่ตรงกับที่เราเขียนไว้ล่าสุด = มีคนอื่นเปลี่ยน ให้ยึดค่าใหม่เป็นฐาน
        if (lastApplied >= 0f && !Mathf.Approximately(source.volume, lastApplied))
            baseVolume = source.volume;

        ApplyVolume();
    }

    public void ApplyVolume()
    {
        if (source == null) return;

        lastApplied = Mathf.Clamp01(baseVolume) * GameAudio.GetBusVolume(bus);
        source.volume = lastApplied;
    }

    /// <summary>ตั้งค่าฐานเอง (เผื่อสคริปต์อื่นอยากสั่งตรงๆ โดยไม่ให้ตัวจับเข้าใจผิด)</summary>
    public void SetBaseVolume(float value)
    {
        baseVolume = Mathf.Clamp01(value);
        ApplyVolume();
    }

#if UNITY_EDITOR
    /// <summary>ให้สคริปต์ตัวติดป้ายตั้งหมวดได้ตอน build</summary>
    public void EditorSetBus(AudioBus value) => bus = value;
#endif
}
