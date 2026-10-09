using DG.Tweening;
using Nsc.Match;
using UnityEngine;

/// <summary>
/// เกตเปิด/ปิด HUD ทั้งชุด — ซ่อนระหว่างเตรียมตัว (ลอบบี้/เลือกทีม) แล้ว fade ขึ้นเมื่อแมตช์เริ่ม
/// อ่านเฟสจาก MatchSession ตัวเดียว ใช้ได้ทั้ง co-op และ PVP (เดิมต้องมีเกตสองตัวแย่งกันคุม alpha)
/// ฉากที่ไม่มี MatchSession (ซีนเทส) → โชว์ทันที
/// </summary>
public class HudVisibilityGate : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private float fadeDuration = 0.35f;

    private MatchSession session;
    private bool visible;
    private Tween fadeTween;

    private void Awake()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
    }

    private void Start()
    {
        session = MatchSession.Current;
        if (session != null) session.PhaseChanged += OnPhaseChanged;
        SetVisible(session == null || session.Phase != MatchPhase.Preparing);
    }

    private void OnPhaseChanged(MatchPhase phase) => SetVisible(phase != MatchPhase.Preparing);

    private void SetVisible(bool show)
    {
        if (show == visible || canvasGroup == null)
            return;

        visible = show;
        fadeTween?.Kill();
        fadeTween = canvasGroup
            .DOFade(visible ? 1f : 0f, fadeDuration)
            .SetEase(Ease.OutCubic)
            .SetUpdate(true);
    }

    private void OnDestroy()
    {
        if (session != null) session.PhaseChanged -= OnPhaseChanged;
        fadeTween?.Kill();
    }
}
