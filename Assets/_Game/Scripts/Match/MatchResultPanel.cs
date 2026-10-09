using System;
using DG.Tweening;
using Nsc.Combat;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.UI;

namespace Nsc.Match
{
    /// <summary>
    /// หน้าจอจบแมตช์ตัวเดียวของทุกโหมด (แทน GameFlowManager / ParkourFlowManager / PvpResultUI ส่วนหน้าจอ
    /// ที่เคยก๊อปกันสามชุด — ใช้ GUID ของ PvpResultUI) แสดงผลอย่างเดียว อ่าน MatchResult จาก MatchSession
    ///
    /// ข้อความที่เห็นขึ้นกับเครื่องตัวเอง: PVP ทีมเราชนะ → VICTORY / แพ้ → DEFEAT / ไม่ได้อยู่ทีมไหน → MATCH OVER
    /// </summary>
    public class MatchResultPanel : MonoBehaviour
    {
        /// <summary>หน้าจอหนึ่งชุด — ช่องไหนไม่มีในเลย์เอาต์นั้นปล่อยว่างได้</summary>
        [Serializable]
        public class ResultView
        {
            [Tooltip("ตัว panel (เปิด/ปิด) — ว่าง = ใช้ GameObject ของ Group")]
            public GameObject root;
            [Tooltip("ใส่ไว้เพื่อค่อยๆ จางขึ้น")]
            public CanvasGroup group;
            [Tooltip("ตัวที่เด้งขึ้นมา (ว่าง = ไม่เด้ง) — ถ้าฉากมืดคลุมจอเป็นลูกของ panel ให้ชี้กล่องข้างใน")]
            public Transform popTarget;
            public TextMeshProUGUI titleText;
            public TextMeshProUGUI subtitleText;
            public TextMeshProUGUI timeText;
            public TextMeshProUGUI heightText;
            [Tooltip("ปุ่มที่เห็นเฉพาะ Host")]
            public GameObject hostButtons;
            [Tooltip("ข้อความ 'รอ Host' ที่เห็นเฉพาะ client")]
            public GameObject waitingText;
            [Tooltip("เล่นใหม่ — Host เท่านั้น")]
            public Button retryButton;
            public Button exitButton;

            public GameObject Root => root != null ? root : group != null ? group.gameObject : null;
        }

        public enum ExitMode
        {
            /// <summary>Host พาทุกคนกลับฉากเมนู ห้องยังอยู่ (Boss/Parkour)</summary>
            HostReturnsEveryoneToMenu,
            /// <summary>ใครกดก็ออกจากห้องเฉพาะเครื่องตัวเอง — Host กด = ห้องสลาย (PVP)</summary>
            LeaveSession
        }

        [Header("Views")]
        [SerializeField] private ResultView victoryView = new ResultView();
        [Tooltip("ว่าง = ใช้หน้าเดียวกับ Victory (Parkour/PVP)")]
        [SerializeField] private ResultView defeatView = new ResultView();

        [Header("Exit")]
        [SerializeField] private ExitMode exitMode = ExitMode.HostReturnsEveryoneToMenu;

        [Header("Colors (Title Text)")]
        [SerializeField] private Color victoryColor = new Color(0.30f, 0.95f, 0.55f, 1f);
        [SerializeField] private Color defeatColor = new Color(0.95f, 0.28f, 0.32f, 1f);

        [Header("Victory Cutscene (ไม่บังคับ)")]
        [Tooltip("บอสตายแล้วเล่นก่อนโชว์หน้าชนะ — ว่าง = โชว์ทันที")]
        [SerializeField] private PlayableDirector endingCutscene;
        [Tooltip("หน่วงก่อนโชว์หน้าชนะ (วินาที) — ตั้งเท่าความยาว cutscene")]
        [SerializeField] private float victoryPanelDelay;

        [Header("Animation")]
        [SerializeField] private float fadeDuration = 0.45f;
        [SerializeField] private float popDuration = 0.35f;

        private MatchSession session;
        private ResultView shownView;
        private float showAt = -1f;
        private ResultView pendingView;
        private bool leaving;
        private Tween fadeTween;
        private Tween popTween;

        private void Awake()
        {
            Hide(victoryView);
            Hide(defeatView);
            Wire(victoryView);
            if (defeatView != victoryView) Wire(defeatView);
        }

        private void Start()
        {
            session = MatchSession.Current;
            if (session == null)
            {
                Debug.LogWarning("[MatchResultPanel] ไม่มี MatchSession ในฉาก — หน้าผลลัพธ์จะไม่ขึ้น", this);
                return;
            }

            session.PhaseChanged += OnPhaseChanged;
            if (session.Phase == MatchPhase.Ended) OnPhaseChanged(MatchPhase.Ended); // เข้าห้องช้าหลังจบไปแล้ว
        }

        private void OnDestroy()
        {
            if (session != null) session.PhaseChanged -= OnPhaseChanged;
            Unwire(victoryView);
            Unwire(defeatView);
            if (shownView != null) UiFocus.Pop(this);
            fadeTween?.Kill();
            popTween?.Kill();
        }

        private void Update()
        {
            if (showAt >= 0f && Time.unscaledTime >= showAt)
            {
                showAt = -1f;
                Show(pendingView);
            }

            // ระหว่างจบเกมบังคับเมาส์หลุดล็อกตลอด — ผู้เล่นต้องกดปุ่มบนหน้าจอได้
            if (shownView != null && Cursor.lockState != CursorLockMode.None)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void OnPhaseChanged(MatchPhase phase)
        {
            if (phase != MatchPhase.Ended)
            {
                HideAll();
                return;
            }

            MatchResult result = session.Result;
            bool pvp = result.winningTeam != Team.None;
            Team localTeam = LocalTeam();
            bool spectator = pvp && localTeam == Team.None;
            bool won = pvp ? localTeam == result.winningTeam : result.victory;

            ResultView view = won || spectator || !HasContent(defeatView) ? victoryView : defeatView;
            Fill(view, result, pvp, won, spectator);

            // บอสตาย → เล่น ending cutscene ก่อน แล้วค่อยโชว์หน้าชนะตาม delay
            if (won && !pvp && (endingCutscene != null || victoryPanelDelay > 0f))
            {
                if (endingCutscene != null) endingCutscene.Play();
                pendingView = view;
                showAt = Time.unscaledTime + Mathf.Max(0f, victoryPanelDelay);
                return;
            }

            Show(view);
        }

        private static Team LocalTeam()
        {
            LimbControlBinder binder = LimbControlBinder.Current;
            if (binder != null && binder.LocalRobot != null) return binder.LocalRobot.GetTeam();

            LimbSelection selection = LimbSelection.Current;
            NetworkManager network = NetworkManager.Singleton;
            if (selection == null || network == null) return Team.None;

            Robots.Robot choice = selection.GetRobotChoice(network.LocalClientId);
            return choice != null ? choice.GetTeam() : Team.None;
        }

        private void Fill(ResultView view, MatchResult result, bool pvp, bool won, bool spectator)
        {
            if (view.titleText != null)
            {
                view.titleText.text = spectator ? "MATCH OVER" : won ? "VICTORY" : "DEFEAT";
                view.titleText.color = spectator ? Color.white : won ? victoryColor : defeatColor;
            }

            if (view.subtitleText != null && pvp)
            {
                view.subtitleText.text = $"{result.winningTeam.DisplayName().ToUpperInvariant()} TEAM WINS";
                view.subtitleText.color = result.winningTeam.DisplayColor();
            }

            if (view.timeText != null) view.timeText.text = FormatTime(result.time);
            if (view.heightText != null)
                view.heightText.text = $"{Mathf.FloorToInt(Mathf.Max(0f, result.height))}<size=55%>m</size>";

            bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
            if (view.hostButtons != null) view.hostButtons.SetActive(isHost);
            if (view.waitingText != null) view.waitingText.SetActive(!isHost);
            if (view.retryButton != null) view.retryButton.gameObject.SetActive(isHost);
        }

        // ================================================================
        //  แสดง/ซ่อน
        // ================================================================

        private void Show(ResultView view)
        {
            GameObject root = view?.Root;
            if (root == null) return;

            shownView = view;
            root.SetActive(true);
            UiFocus.Push(this);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (view.popTarget != null)
            {
                popTween?.Kill();
                view.popTarget.localScale = Vector3.one * 0.8f;
                popTween = view.popTarget.DOScale(Vector3.one, Mathf.Max(0.01f, popDuration))
                                         .SetEase(Ease.OutBack).SetUpdate(true);
            }

            if (view.group == null) return;
            view.group.alpha = 0f;
            view.group.blocksRaycasts = true;
            view.group.interactable = true;
            fadeTween?.Kill();
            fadeTween = view.group.DOFade(1f, Mathf.Max(0.01f, fadeDuration)).SetEase(Ease.OutCubic).SetUpdate(true);
        }

        private void HideAll()
        {
            showAt = -1f;
            Hide(victoryView);
            Hide(defeatView);
            if (shownView == null) return;
            shownView = null;
            UiFocus.Pop(this);
        }

        private static void Hide(ResultView view)
        {
            if (view == null) return;
            if (view.group != null)
            {
                view.group.alpha = 0f;
                view.group.blocksRaycasts = false;
                view.group.interactable = false;
            }
            if (view.Root != null) view.Root.SetActive(false);
        }

        private static bool HasContent(ResultView view) => view != null && view.Root != null;

        /// <summary>วินาที → mm:ss.hh (เศษวินาทีย่อเล็กด้วย rich text)</summary>
        private static string FormatTime(float seconds)
        {
            seconds = Mathf.Max(0f, seconds);
            int minutes = (int)(seconds / 60f);
            int secs = (int)(seconds % 60f);
            int cents = (int)(seconds * 100f) % 100;
            return $"{minutes:00}:{secs:00}<size=55%>.{cents:00}</size>";
        }

        // ================================================================
        //  ปุ่ม
        // ================================================================

        private void Wire(ResultView view)
        {
            if (view == null) return;
            if (view.retryButton != null) view.retryButton.onClick.AddListener(Retry);
            if (view.exitButton != null) view.exitButton.onClick.AddListener(ExitToMenu);
        }

        private void Unwire(ResultView view)
        {
            if (view == null) return;
            if (view.retryButton != null) view.retryButton.onClick.RemoveListener(Retry);
            if (view.exitButton != null) view.exitButton.onClick.RemoveListener(ExitToMenu);
        }

        /// <summary>Host เล่นใหม่ — โหลดฉากเดิมซ้ำ ทุกเครื่องตามมาพร้อมกัน</summary>
        public void Retry()
        {
            if (leaving || session == null) return;
            UiFocus.Pop(this);
            session.ServerRestart();
        }

        public void ExitToMenu()
        {
            if (leaving) return;

            if (exitMode == ExitMode.HostReturnsEveryoneToMenu)
            {
                if (session != null) session.ServerReturnEveryoneToMenu();
                return;
            }

            leaving = true; // กันกดรัวระหว่างรอ shutdown
            UiFocus.Pop(this);
            if (SessionService.Instance != null) _ = SessionService.Instance.LeaveAsync();
        }
    }
}
