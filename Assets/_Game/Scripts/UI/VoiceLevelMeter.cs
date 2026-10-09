using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// หลอดวัดระดับไมค์แบบขีดๆ (เหมือน VU meter) — ใช้แทน Slider เพราะอ่านง่ายกว่ามาก
/// เห็นได้ทันทีว่าเสียงเข้าแรงแค่ไหน โดยไม่ต้องเทียบกับหัวสไลเดอร์
///
/// SettingsManager เป็นคนป้อนค่าให้ผ่าน SetLevel() ทุกเฟรมตอนเปิดหน้าตั้งค่า
/// </summary>
[DisallowMultipleComponent]
public class VoiceLevelMeter : MonoBehaviour
{
    [Tooltip("ขีดทั้งหมด เรียงจากซ้ายไปขวา")]
    [SerializeField] private Image[] segments;

    [Header("Colors")]
    [Tooltip("สีขีดที่ติดแล้ว (ช่วงปกติ)")]
    [SerializeField] private Color activeColor = Color.white;

    [Tooltip("สีขีดช่วงท้ายๆ — เตือนว่าเสียงเริ่มดังเกิน")]
    [SerializeField] private Color hotColor = new Color(1f, 0.45f, 0.3f, 1f);

    [Tooltip("สีขีดที่ยังไม่ติด")]
    [SerializeField] private Color idleColor = new Color(1f, 1f, 1f, 0.18f);

    [Tooltip("สัดส่วนจากท้ายหลอดที่ถือว่า 'ดังเกิน' แล้วเปลี่ยนเป็น hotColor")]
    [Range(0f, 1f)]
    [SerializeField] private float hotThreshold = 0.8f;

    [Header("Gate Marker")]
    [Tooltip("สีขีดที่ทำหน้าที่เป็นเส้นบอกเกณฑ์ 'ดังพอจะส่งเสียง' — ผู้เล่นจะได้รู้ว่าต้องพูดดังแค่ไหน")]
    [SerializeField] private Color thresholdColor = new Color(1f, 1f, 1f, 0.75f);

    [Tooltip("สีขีดตอนกำลังส่งเสียงออกจริง")]
    [SerializeField] private Color transmittingColor = new Color(0.29f, 0.87f, 0.50f, 1f);

    private int litCount = -1;
    private int thresholdIndex = -1;
    private bool transmitting;
    private bool dirty = true;

    /// <param name="normalized">ระดับเสียง 0-1</param>
    public void SetLevel(float normalized)
    {
        if (segments == null || segments.Length == 0)
            return;

        int lit = normalized <= 0f
            ? 0
            : Mathf.Clamp(Mathf.CeilToInt(Mathf.Clamp01(normalized) * segments.Length), 0, segments.Length);

        if (lit != litCount) { litCount = lit; dirty = true; }
        if (dirty) Redraw();
    }

    /// <summary>ตำแหน่งเกณฑ์เปิดไมค์ (0-1) — วาดเป็นขีดสว่างให้เห็นว่าต้องพูดดังเกินเส้นนี้</summary>
    public void SetThresholdMarker(float normalized)
    {
        if (segments == null || segments.Length == 0) return;

        int idx = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(normalized) * (segments.Length - 1)),
                              0, segments.Length - 1);
        if (idx != thresholdIndex) { thresholdIndex = idx; dirty = true; }
    }

    /// <summary>กำลังส่งเสียงออกอยู่ไหม — ใช้เปลี่ยนสีหลอดเป็นเขียว</summary>
    public void SetTransmitting(bool value)
    {
        if (value != transmitting) { transmitting = value; dirty = true; }
    }

    private void Redraw()
    {
        dirty = false;
        int hotStart = Mathf.CeilToInt(segments.Length * hotThreshold);
        Color lit = transmitting ? transmittingColor : activeColor;

        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i] == null) continue;

            if (i < litCount)
                segments[i].color = i >= hotStart ? hotColor : lit;
            else if (i == thresholdIndex)
                segments[i].color = thresholdColor;   // เส้นเกณฑ์ยังเห็นแม้ขีดยังไม่ถึง
            else
                segments[i].color = idleColor;
        }
    }

    /// <summary>ให้ตัวสร้าง UI ฝั่ง Editor ยัดขีดเข้ามาได้</summary>
    public void SetSegments(Image[] value)
    {
        segments = value;
        litCount = -1;
    }
}
