using System.Collections.Generic;
using System.Text;
using DG.Tweening;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace NscGame.Pvp
{
    /// <summary>
    /// หน้าจอเลือกทีม + เลือกชิ้นส่วน ก่อนเริ่มแมตช์ PVP
    ///
    /// ขั้นตอนของผู้เล่น:
    ///   1. กด RED หรือ BLUE เพื่อเลือกทีม
    ///   2. กดชิ้นส่วนที่อยากคุม (แขนซ้าย/ขวา, ขาซ้าย/ขวา) — ชิ้นที่เพื่อนร่วมทีมจองแล้วจะกดไม่ได้
    ///   3. Host กด START FIGHT → panel ปิด ฟิสิกส์ปลดล็อก เริ่มสู้
    ///
    /// ใช้แพทเทิร์น DOTween เดียวกับ OnlineNetworkUI/LobbyManager:
    ///   SetUpdate(true) ทุก tween, เก็บ tween ในดิกชันนารีเพื่อ kill ตอน OnDestroy
    /// </summary>
    public class PvpTeamSelectUI : MonoBehaviour
    {
        #region Inspector

        [Header("Panel")]
        public GameObject selectionPanel;

        [Header("Team Buttons")]
        public Button redTeamButton;
        public Button blueTeamButton;

        [Header("Team Rosters")]
        public TextMeshProUGUI redRosterText;
        public TextMeshProUGUI blueRosterText;

        [Header("Limb Buttons (เรียง: แขนซ้าย, แขนขวา, ขาซ้าย, ขาขวา)")]
        public Button[] limbButtons = new Button[PvpLimb.Count];
        public TextMeshProUGUI[] limbLabels = new TextMeshProUGUI[PvpLimb.Count];

        [Header("Status / Start")]
        public TextMeshProUGUI statusText;
        public Button startButton;
        public TextMeshProUGUI startButtonLabel;

        [Header("Colors")]
        public Color selectedTeamColor = new Color(1f, 1f, 1f, 1f);
        public Color idleTeamColor     = new Color(1f, 1f, 1f, 0.45f);
        public Color limbFreeColor     = new Color(1f, 1f, 1f, 1f);
        public Color limbMineColor     = new Color(0.20f, 0.85f, 0.40f, 1f);
        public Color limbTakenColor    = new Color(0.85f, 0.20f, 0.20f, 1f);

        [Header("Animation")]
        public float uiFadeDuration  = 0.22f;
        public float uiScaleFrom     = 0.96f;
        public Ease  uiEase          = Ease.OutCubic;
        public float buttonClickScale    = 1.06f;
        public float buttonClickDuration = 0.12f;
        public float colorFadeDuration   = 0.2f;

        #endregion

        private readonly Dictionary<GameObject, Tween> runningUiTweens = new Dictionary<GameObject, Tween>();
        private readonly Dictionary<Transform, Vector3> originalUiScales = new Dictionary<Transform, Vector3>();
        private readonly Dictionary<Transform, Tween> buttonClickTweens = new Dictionary<Transform, Tween>();
        private readonly Dictionary<Graphic, Tween> colorTweens = new Dictionary<Graphic, Tween>();

        private PvpTeamManager manager;
        private bool subscribed;

        #region Lifecycle

        private void Start()
        {
            WireButtons();
            TrySubscribe();
            Refresh();
        }

        private void Update()
        {
            // PvpTeamManager spawn ทีหลัง UI ได้ (NetworkObject รอ NetworkManager เริ่มก่อน)
            if (!subscribed) TrySubscribe();
        }

        private void TrySubscribe()
        {
            if (subscribed) return;

            manager = PvpTeamManager.Instance;
            if (manager == null) return;

            manager.OnRosterChanged += Refresh;
            manager.OnMatchStateChanged += OnMatchStateChanged;
            subscribed = true;

            Refresh();
        }

        private void OnDestroy()
        {
            if (manager != null)
            {
                manager.OnRosterChanged -= Refresh;
                manager.OnMatchStateChanged -= OnMatchStateChanged;
            }

            UnwireButtons();
            KillAllTweens();
        }

        #endregion

        #region Button Wiring

        private void WireButtons()
        {
            if (redTeamButton != null)
                redTeamButton.onClick.AddListener(OnRedClicked);
            if (blueTeamButton != null)
                blueTeamButton.onClick.AddListener(OnBlueClicked);
            if (startButton != null)
                startButton.onClick.AddListener(OnStartClicked);

            if (limbButtons != null)
            {
                for (int i = 0; i < limbButtons.Length; i++)
                {
                    if (limbButtons[i] == null) continue;
                    int captured = i;
                    limbButtons[i].onClick.AddListener(() => OnLimbClicked(captured));
                }
            }
        }

        private void UnwireButtons()
        {
            if (redTeamButton != null) redTeamButton.onClick.RemoveListener(OnRedClicked);
            if (blueTeamButton != null) blueTeamButton.onClick.RemoveListener(OnBlueClicked);
            if (startButton != null) startButton.onClick.RemoveListener(OnStartClicked);

            // limb listener เป็น lambda — ล้างทั้งหมดทีเดียวตอนถูกทำลาย
            if (limbButtons != null)
                foreach (Button b in limbButtons)
                    if (b != null) b.onClick.RemoveAllListeners();
        }

        private void OnRedClicked()  => RequestTeam(PvpTeam.Red,  redTeamButton);
        private void OnBlueClicked() => RequestTeam(PvpTeam.Blue, blueTeamButton);

        private void RequestTeam(PvpTeam team, Button source)
        {
            if (!EnsureManagerReady()) return;
            PlayClickFeedback(source);
            manager.RequestTeamRpc(team);
        }

        private void OnLimbClicked(int index)
        {
            if (!EnsureManagerReady()) return;

            if (manager.LocalTeam == PvpTeam.None)
            {
                SetStatus("Pick a team before selecting a part");
                return;
            }

            PlayClickFeedback(limbButtons != null && index < limbButtons.Length ? limbButtons[index] : null);
            manager.RequestLimbRpc(index);
        }

        private void OnStartClicked()
        {
            if (!EnsureManagerReady()) return;

            if (!manager.CanStartMatch(out string reason))
            {
                SetStatus(reason);
                return;
            }

            PlayClickFeedback(startButton);
            manager.RequestStartMatchRpc();
        }

        private bool EnsureManagerReady()
        {
            if (manager == null) TrySubscribe();

            if (manager == null || !manager.IsSpawned)
            {
                SetStatus("Connecting... please wait");
                return false;
            }
            return true;
        }

        #endregion

        #region Refresh

        private void OnMatchStateChanged(PvpMatchState state)
        {
            SetVisibleAnimated(selectionPanel, state == PvpMatchState.TeamSelect);
        }

        private void Refresh()
        {
            if (manager == null || NetworkManager.Singleton == null) return;

            PvpTeam myTeam = manager.LocalTeam;
            int myLimb = manager.LocalLimbIndex;
            bool isHost = NetworkManager.Singleton.IsServer;

            RefreshTeamButtons(myTeam);
            RefreshRosters();
            RefreshLimbButtons(myTeam, myLimb);
            RefreshStartButton(isHost);
            RefreshStatus(myTeam, myLimb);
        }

        private void RefreshTeamButtons(PvpTeam myTeam)
        {
            TintButton(redTeamButton,  myTeam == PvpTeam.Red  ? selectedTeamColor : idleTeamColor);
            TintButton(blueTeamButton, myTeam == PvpTeam.Blue ? selectedTeamColor : idleTeamColor);

            // ทีมเต็มแล้วกดไม่ได้ (ยกเว้นทีมที่ตัวเองอยู่)
            if (redTeamButton != null)
                redTeamButton.interactable =
                    myTeam == PvpTeam.Red || manager.CountTeam(PvpTeam.Red) < manager.MaxPlayersPerTeam;
            if (blueTeamButton != null)
                blueTeamButton.interactable =
                    myTeam == PvpTeam.Blue || manager.CountTeam(PvpTeam.Blue) < manager.MaxPlayersPerTeam;
        }

        private void RefreshRosters()
        {
            if (redRosterText != null)  redRosterText.text  = BuildRoster(PvpTeam.Red);
            if (blueRosterText != null) blueRosterText.text = BuildRoster(PvpTeam.Blue);
        }

        private string BuildRoster(PvpTeam team)
        {
            List<PvpPlayerEntry> roster = manager.GetTeamRoster(team);
            if (roster.Count == 0) return "<i>Empty</i>";

            StringBuilder sb = new StringBuilder();
            ulong localId = NetworkManager.Singleton.LocalClientId;

            foreach (PvpPlayerEntry entry in roster)
            {
                string name = entry.playerName.ToString();
                if (string.IsNullOrEmpty(name)) name = "Player";

                string me   = entry.clientId == localId ? " (You)" : "";
                string host = entry.clientId == NetworkManager.ServerClientId ? " [Host]" : "";
                string limb = PvpLimb.IsValid(entry.limbIndex)
                    ? PvpLimb.Name(entry.limbIndex)
                    : "No part selected";

                sb.AppendLine($"{name}{host}{me} — {limb}");
            }

            return sb.ToString();
        }

        private void RefreshLimbButtons(PvpTeam myTeam, int myLimb)
        {
            if (limbButtons == null) return;

            bool hasTeam = myTeam != PvpTeam.None;
            ulong localId = NetworkManager.Singleton.LocalClientId;

            for (int i = 0; i < limbButtons.Length && i < PvpLimb.Count; i++)
            {
                if (limbButtons[i] == null) continue;

                ulong owner = hasTeam ? manager.GetLimbOwner(myTeam, i) : ulong.MaxValue;
                bool taken  = owner != ulong.MaxValue;
                bool mine   = taken && owner == localId;

                limbButtons[i].interactable = hasTeam && (!taken || mine);

                Color target = !hasTeam ? idleTeamColor
                             : mine     ? limbMineColor
                             : taken    ? limbTakenColor
                                        : limbFreeColor;
                TintButton(limbButtons[i], target);

                if (limbLabels != null && i < limbLabels.Length && limbLabels[i] != null)
                {
                    limbLabels[i].text = mine  ? $"{PvpLimb.Name(i)}\n<size=70%>(You)</size>"
                                       : taken ? $"{PvpLimb.Name(i)}\n<size=70%>(Taken)</size>"
                                               : PvpLimb.Name(i);
                }
            }
        }

        private void RefreshStartButton(bool isHost)
        {
            if (startButton == null) return;

            startButton.gameObject.SetActive(isHost);
            if (!isHost) return;

            bool canStart = manager.CanStartMatch(out string reason);
            startButton.interactable = canStart;

            if (startButtonLabel != null)
                startButtonLabel.text = canStart ? "START FIGHT" : reason;
        }

        private void RefreshStatus(PvpTeam myTeam, int myLimb)
        {
            if (statusText == null) return;

            if (myTeam == PvpTeam.None)
            {
                SetStatus("Pick your team: <color=#E63A3D>RED</color> or <color=#338CF2>BLUE</color>");
                return;
            }

            if (!PvpLimb.IsValid(myLimb))
            {
                SetStatus($"{myTeam.DisplayName()} team — select the part you want to control");
                return;
            }

            bool isHost = NetworkManager.Singleton.IsServer;
            SetStatus($"{myTeam.DisplayName()} team — controlling {PvpLimb.Name(myLimb)} | " +
                      (isHost ? "Press START FIGHT to begin" : "Waiting for Host to start..."));
        }

        private void SetStatus(string message)
        {
            if (statusText != null) statusText.text = message;
        }

        #endregion

        #region DOTween Helpers (แพทเทิร์นเดียวกับ OnlineNetworkUI/LobbyManager)

        private void TintButton(Button button, Color target)
        {
            if (button == null) return;

            Image image = button.targetGraphic as Image;
            if (image == null) image = button.GetComponent<Image>();
            if (image == null) return;

            if (colorTweens.TryGetValue(image, out Tween existing) && existing != null && existing.IsActive())
                existing.Kill(false);

            if (colorFadeDuration <= 0f)
            {
                image.color = target;
                return;
            }

            Tween tween = image.DOColor(target, colorFadeDuration)
                .SetUpdate(true)
                .SetEase(Ease.OutCubic)
                .OnComplete(() => colorTweens.Remove(image));

            colorTweens[image] = tween;
        }

        private void PlayClickFeedback(Button button)
        {
            if (button == null || !button.gameObject.activeInHierarchy) return;

            float punchAmount = Mathf.Max(1f, buttonClickScale) - 1f;
            float duration = Mathf.Max(0f, buttonClickDuration);
            if (punchAmount <= 0f || duration <= 0f) return;

            Transform t = button.transform;

            if (buttonClickTweens.TryGetValue(t, out Tween running) && running != null && running.IsActive())
                running.Kill(false);

            Vector3 baseScale = GetOriginalScale(t);
            t.localScale = baseScale;

            Tween clickTween = t.DOPunchScale(baseScale * punchAmount, duration, 1, 0.5f)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    if (t != null) t.localScale = baseScale;
                    buttonClickTweens.Remove(t);
                });

            buttonClickTweens[t] = clickTween;
        }

        private void SetVisibleAnimated(GameObject target, bool visible)
        {
            if (target == null) return;

            KillUiTween(target);

            CanvasGroup cg = GetOrAddCanvasGroup(target);
            Transform t = target.transform;
            Vector3 baseScale = GetOriginalScale(t);
            Vector3 hiddenScale = baseScale * Mathf.Max(0.01f, uiScaleFrom);

            if (!isActiveAndEnabled || uiFadeDuration <= 0f)
            {
                target.SetActive(visible);
                cg.alpha = visible ? 1f : 0f;
                cg.interactable = visible;
                cg.blocksRaycasts = visible;
                t.localScale = baseScale;
                return;
            }

            if (visible && !target.activeSelf)
            {
                target.SetActive(true);
                cg.alpha = 0f;
                t.localScale = hiddenScale;
            }

            cg.interactable = false;
            cg.blocksRaycasts = false;

            float endAlpha = visible ? 1f : 0f;
            Vector3 endScale = visible ? baseScale : hiddenScale;
            float duration = Mathf.Max(0.01f, uiFadeDuration);

            Sequence sequence = DOTween.Sequence().SetUpdate(true);
            sequence.Join(cg.DOFade(endAlpha, duration).SetEase(uiEase));
            sequence.Join(t.DOScale(endScale, duration).SetEase(uiEase));
            sequence.OnComplete(() =>
            {
                runningUiTweens.Remove(target);
                if (target == null) return;

                cg.alpha = endAlpha;
                t.localScale = baseScale;
                cg.interactable = visible;
                cg.blocksRaycasts = visible;
                if (!visible) target.SetActive(false);
            });

            runningUiTweens[target] = sequence;
        }

        private static CanvasGroup GetOrAddCanvasGroup(GameObject target)
        {
            if (!target.TryGetComponent(out CanvasGroup cg))
                cg = target.AddComponent<CanvasGroup>();
            return cg;
        }

        private Vector3 GetOriginalScale(Transform t)
        {
            if (!originalUiScales.TryGetValue(t, out Vector3 scale))
            {
                scale = t.localScale;
                originalUiScales[t] = scale;
            }
            return scale;
        }

        private void KillUiTween(GameObject target)
        {
            if (runningUiTweens.TryGetValue(target, out Tween tween) && tween != null && tween.IsActive())
                tween.Kill(false);
            runningUiTweens.Remove(target);
        }

        private void KillAllTweens()
        {
            foreach (Tween tween in runningUiTweens.Values)
                if (tween != null && tween.IsActive()) tween.Kill(false);
            runningUiTweens.Clear();

            foreach (Tween tween in buttonClickTweens.Values)
                if (tween != null && tween.IsActive()) tween.Kill(false);
            buttonClickTweens.Clear();

            foreach (Tween tween in colorTweens.Values)
                if (tween != null && tween.IsActive()) tween.Kill(false);
            colorTweens.Clear();

            originalUiScales.Clear();
        }

        #endregion
    }
}
