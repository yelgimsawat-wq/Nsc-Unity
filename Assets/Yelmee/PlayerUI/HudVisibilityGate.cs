using DG.Tweening;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// เกตเปิด/ปิด HUD ทั้งชุด: ซ่อนไว้ระหว่างอยู่ในหน้าเลือกชิ้นส่วน (Lobby)
/// แล้วค่อย fade ขึ้นเมื่อ Host กด Start (LobbyManager.GameStarted = true)
/// โหมดทดสอบที่ไม่มี network/ไม่มี LobbyManager ใน scene → โชว์ทันที
/// </summary>
public class HudVisibilityGate : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private float fadeDuration = 0.35f;

    private LobbyManager lobby;
    private float nextLobbySearchTime;
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

        visible = false;
    }

    private void Update()
    {
        bool shouldShow = ShouldShow();
        if (shouldShow == visible)
            return;

        visible = shouldShow;

        if (canvasGroup == null)
            return;

        fadeTween?.Kill();
        fadeTween = canvasGroup
            .DOFade(visible ? 1f : 0f, fadeDuration)
            .SetEase(Ease.OutCubic)
            .SetUpdate(true);
    }

    private bool ShouldShow()
    {
        if (lobby == null && Time.unscaledTime >= nextLobbySearchTime)
        {
            nextLobbySearchTime = Time.unscaledTime + 1f;
            lobby = FindFirstObjectByType<LobbyManager>(FindObjectsInactive.Include);
        }

        // scene นี้ไม่มี lobby (เข้าเกมตรงๆ) → ไม่มีอะไรต้องรอ
        if (lobby == null)
            return true;

        // ไม่มี NetworkManager เลย = เทส offline ล้วนๆ → โชว์
        // มี NetworkManager แต่ยังไม่ host/join = ยังอยู่หน้าเมนู → ซ่อนรอ
        if (NetworkManager.Singleton == null)
            return true;
        if (!NetworkManager.Singleton.IsListening)
            return false;

        return lobby.GameStarted;
    }

    private void OnDestroy()
    {
        fadeTween?.Kill();
    }
}
