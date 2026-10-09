using Nsc.Robots;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// วงแหวน HULL ซ้ายล่าง — แสดงเลือดของ "ชิ้นส่วนที่ผู้เล่นคนนี้คุม"
/// ถ้าคุมหลายชิ้น (เทสคนเดียว Host คุมครบ 4) จะโชว์ชิ้นที่เลือดต่ำสุด
/// เพื่อให้เห็นดาเมจเสมอไม่ว่าโดนต่อยชิ้นไหน — ตอนเล่นจริงคุมชิ้นเดียว พฤติกรรมเหมือนเดิม
/// </summary>
public class HullRingUI : MonoBehaviour
{
    [SerializeField] private LocalRobotBinder binder;
    [SerializeField] private Image fillImage;
    [SerializeField] private TMP_Text valueText;

    [Tooltip("ป้ายชื่อชิ้นส่วนที่เลือดกำลังโชว์อยู่ (L-ARM / R-ARM / L-LEG / R-LEG)")]
    [SerializeField] private TMP_Text limbLabel;

    [Header("Colors")]
    [SerializeField] private Color fullColor = new Color(0.35f, 0.85f, 1f, 1f);
    [SerializeField] private Color lowColor = new Color(1f, 0.25f, 0.2f, 1f);

    private readonly List<LimbHealth> boundHealths = new List<LimbHealth>();

    private void Awake()
    {
        if (binder == null)
            binder = GetComponentInParent<LocalRobotBinder>();
    }

    private void OnEnable()
    {
        if (binder == null)
            return;

        binder.OnBound += HandleBound;

        if (binder.IsBound)
            HandleBound();
    }

    private void OnDisable()
    {
        if (binder != null)
            binder.OnBound -= HandleBound;

        DetachHealth();
    }

    private void HandleBound()
    {
        DetachHealth();

        foreach (LimbHealth health in binder.OwnedHealths)
        {
            if (health == null || boundHealths.Contains(health))
                continue;

            // รวมเพดานเลือดที่ลดตอนชิ้นหลุด → วงแหวนต้องคำนวณใหม่
            health.HealthChanged += Refresh;
            boundHealths.Add(health);
        }

        Refresh();
    }

    private void DetachHealth()
    {
        foreach (LimbHealth health in boundHealths)
        {
            if (health != null)
                health.HealthChanged -= Refresh;
        }

        boundHealths.Clear();
    }

    private void Refresh()
    {
        // โชว์ชิ้นที่เลือดต่ำสุดในบรรดาชิ้นที่เราคุม (คุมชิ้นเดียว = ชิ้นนั้นตรงๆ)
        LimbHealth worst = null;
        float worstNormalized = float.MaxValue;

        foreach (LimbHealth health in boundHealths)
        {
            if (health == null)
                continue;

            // เทียบกับ "เพดานปัจจุบัน" ที่ sync จาก Server — MaxHp ตายตัวจะเพี้ยนหลังชิ้นเคยหลุด
            float normalized = Mathf.Clamp01(health.Hp / Mathf.Max(1f, health.MaxHp));

            if (normalized < worstNormalized)
            {
                worstNormalized = normalized;
                worst = health;
            }
        }

        if (worst == null)
            return;

        float maxHp = Mathf.Max(1f, worst.MaxHp);
        float currentHp = Mathf.Clamp(worst.Hp, 0f, maxHp);
        float normalizedHp = currentHp / maxHp;

        if (fillImage != null)
        {
            fillImage.fillAmount = normalizedHp;
            fillImage.color = Color.Lerp(lowColor, fullColor, normalizedHp);
        }

        if (valueText != null)
            valueText.text = Mathf.CeilToInt(currentHp).ToString();

        // ป้ายบอกว่าเลือดที่โชว์อยู่เป็นของชิ้นไหน (เปลี่ยนตามชิ้นที่เลือดต่ำสุด)
        if (limbLabel != null && binder != null)
        {
            string label = binder.GetLimbLabel(worst);
            if (limbLabel.text != label)
                limbLabel.text = label;
        }
    }
}
