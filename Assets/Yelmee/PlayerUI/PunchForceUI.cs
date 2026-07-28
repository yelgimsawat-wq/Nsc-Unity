using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// หลอดพลังหมัดแบบช่องๆ — แสดง "เฉพาะมือที่ผู้เล่นคนนี้คุม" เท่านั้น
/// หมัดของมืออีกข้าง (เพื่อนคุม) ไม่ขยับแถบเรา และผู้เล่นที่คุมขาจะไม่เห็นแถบนี้เลย
/// </summary>
public class PunchForceUI : MonoBehaviour
{
    [SerializeField] private LocalRobotBinder binder;

    [Tooltip("กลุ่ม visual ทั้งหมดของแถบ — ถูกปิดเมื่อผู้เล่นคนนี้ไม่ได้คุมมือ")]
    [SerializeField] private GameObject content;

    [SerializeField] private Image[] segments;

    [Header("Colors")]
    [SerializeField] private Color filledColor = new Color(1f, 0.72f, 0.2f, 1f);
    [SerializeField] private Color emptyColor = new Color(1f, 1f, 1f, 0.16f);

    private PlayerHandCombat ownedHand;
    private int litSegments = -1;

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
    }

    private void HandleBound()
    {
        ownedHand = binder.OwnedHand;

        // คุมขา (ไม่ได้เป็นเจ้าของมือสักข้าง) → ไม่มีแถบพลังหมัด
        if (content != null)
            content.SetActive(ownedHand != null);

        litSegments = -1;
        ApplyForce(0f);
    }

    private void Update()
    {
        if (ownedHand == null)
            return;

        ApplyForce(ownedHand.IsPunching ? ownedHand.NormalizedPunchForce : 0f);
    }

    private void ApplyForce(float normalizedForce)
    {
        if (segments == null || segments.Length == 0)
            return;

        int lit = normalizedForce <= 0f
            ? 0
            : Mathf.Clamp(Mathf.CeilToInt(normalizedForce * segments.Length), 0, segments.Length);

        if (lit == litSegments)
            return;

        litSegments = lit;

        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i] != null)
                segments[i].color = i < lit ? filledColor : emptyColor;
        }
    }
}
