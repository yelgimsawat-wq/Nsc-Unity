using DG.Tweening;
using UnityEngine;

namespace NscGame.Pvp
{
    /// <summary>
    /// เกตเปิด/ปิด PlayerHUD เดิมให้เข้ากับจังหวะของโหมด PVP
    ///
    /// ทำไมต้องมี:
    ///   HudVisibilityGate เดิมตัดสินใจจาก LobbyManager.GameStarted
    ///   แต่ฉาก PVP "ห้ามมี" LobbyManager (มันจะปิดหุ่นตัวเกินทิ้ง เหลือตัวเดียวสู้ไม่ได้)
    ///   → เกตเดิมจึงเข้าเคส "ไม่มี lobby = โชว์เลย" ทำให้ HUD ลอยทับหน้าจอเลือกทีม
    ///
    /// วิธีแก้:
    ///   ปิด HudVisibilityGate ทิ้ง แล้วให้ตัวนี้คุม CanvasGroup แทน — มีเจ้าของคนเดียว
    ///   ไม่งั้นสองตัวจะแย่งกันเขียน alpha แล้วกระพริบ
    ///
    /// วางไว้ที่ไหน: บน GameObject เดียวกับ PlayerHUD (ตัวที่มี CanvasGroup)
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class PvpPlayerHudGate : MonoBehaviour
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private float fadeDuration = 0.35f;

        private PvpTeamManager manager;
        private bool subscribed;
        private bool visible;
        private Tween fadeTween;

        private void Awake()
        {
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();

            // ปิดเกตเดิมไม่ให้มาแย่งคุม alpha
            HudVisibilityGate legacyGate = GetComponent<HudVisibilityGate>();
            if (legacyGate != null) legacyGate.enabled = false;

            ApplyInstant(false);
        }

        private void Update()
        {
            if (!subscribed) TrySubscribe();

            bool shouldShow = manager != null && manager.IsSpawned
                              && manager.MatchState != PvpMatchState.TeamSelect;

            if (shouldShow == visible) return;
            visible = shouldShow;

            fadeTween?.Kill();
            fadeTween = canvasGroup
                .DOFade(visible ? 1f : 0f, fadeDuration)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true);
        }

        private void TrySubscribe()
        {
            manager = PvpTeamManager.Instance;
            if (manager != null && manager.IsSpawned) subscribed = true;
        }

        private void ApplyInstant(bool show)
        {
            visible = show;
            if (canvasGroup == null) return;
            canvasGroup.alpha = show ? 1f : 0f;
        }

        private void OnDestroy()
        {
            fadeTween?.Kill();
        }
    }
}
