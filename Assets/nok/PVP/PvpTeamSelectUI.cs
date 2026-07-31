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
    /// ช่องผู้เล่นหนึ่งคนในการ์ดทีม — ตัวอ้างอิงล้วนๆ ไม่มีลอจิก
    /// ปล่อยช่องไหนว่างใน Inspector ได้ ตัวที่ไม่มีจะถูกข้าม
    /// </summary>
    [System.Serializable]
    public class PvpPlayerSlotView
    {
        public GameObject root;
        public Image background;
        public Image frame;           // กรอบรอบช่อง — สว่างเป็นสีทีมเมื่อเป็นช่องของเราเอง
        public Image accent;          // ชิปเลขประจำช่อง
        public Image avatarFrame;
        public Image avatarIcon;
        public TextMeshProUGUI avatarGlyph; // เลย์เอาต์เก่าใช้ตัวอักษรแทนรูป — ปล่อยว่างได้
        public TextMeshProUGUI indexLabel;
        public TextMeshProUGUI nameLabel;
        public Image partIcon;
        public TextMeshProUGUI partLabel;
        public TextMeshProUGUI stateLabel;
        public Image readyIcon;
    }

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

        [Header("Team Rosters (แบบก้อนข้อความ — เลย์เอาต์ใหม่ใช้ Player Slots แทน ปล่อยว่างได้)")]
        public TextMeshProUGUI redRosterText;
        public TextMeshProUGUI blueRosterText;

        [Header("Player Slots (การ์ดรายคน ฝั่งละ 4 ช่อง)")]
        public PvpPlayerSlotView[] redSlots;
        public PvpPlayerSlotView[] blueSlots;

        [Header("Team Headers (แถบสีหัวการ์ด — ใช้ไฮไลต์ทีมที่เราอยู่)")]
        public Image redTeamHeader;
        public Image blueTeamHeader;

        [Header("Limb Buttons (เรียง: แขนซ้าย, แขนขวา, ขาซ้าย, ขาขวา)")]
        public Button[] limbButtons = new Button[PvpLimb.Count];
        public TextMeshProUGUI[] limbLabels = new TextMeshProUGUI[PvpLimb.Count];

        [Header("Robot Assembly (หุ่นกลางจอ — ปล่อยว่างได้ถ้าใช้เลย์เอาต์เก่า)")]
        public Image torsoIcon;
        public Image[] limbIcons = new Image[PvpLimb.Count];
        public TextMeshProUGUI[] limbOwnerLabels = new TextMeshProUGUI[PvpLimb.Count];

        [Header("Limb Sockets (ช่องเสียบข้างหุ่น — กดได้เหมือนกดที่ตัวหุ่น)")]
        [Tooltip("ปุ่มสำรองบนตัวหุ่น สั่งงานชิ้นส่วนเดียวกับ limbButtons")]
        public Button[] limbAltButtons = new Button[PvpLimb.Count];
        public Image[] limbSocketFrames = new Image[PvpLimb.Count];
        public Image[] limbSocketIcons = new Image[PvpLimb.Count];
        [Tooltip("ไอคอนชิ้นส่วนที่เอาไปโชว์ในการ์ดผู้เล่น — เรียงตาม PvpLimb")]
        public Sprite[] limbPartSprites = new Sprite[PvpLimb.Count];

        [Header("Status / Start")]
        public TextMeshProUGUI statusText;
        public TextMeshProUGUI subtitleText;
        public TextMeshProUGUI playersConnectedText;
        public TextMeshProUGUI playersHintText;
        public Button startButton;
        public TextMeshProUGUI startButtonLabel;

        [Header("Colors")]
        public Color selectedTeamColor = new Color(1f, 1f, 1f, 1f);
        public Color idleTeamColor     = new Color(1f, 1f, 1f, 0.45f);
        public Color limbFreeColor     = new Color(0.86f, 0.90f, 0.96f, 1f);
        public Color limbMineColor     = new Color(0.20f, 0.85f, 0.40f, 1f);
        public Color limbTakenColor    = new Color(0.35f, 0.40f, 0.48f, 1f);
        public Color limbLockedColor   = new Color(0.55f, 0.60f, 0.68f, 1f);
        public Color socketFreeColor   = new Color(0.55f, 0.72f, 0.95f, 0.80f);
        public Color socketLockedColor = new Color(0.45f, 0.55f, 0.70f, 0.28f);

        [Header("Slot Colors")]
        public Color slotEmptyColor = new Color(0.047f, 0.055f, 0.071f, 0.70f);
        public Color slotFilledColor = new Color(0.075f, 0.090f, 0.118f, 0.95f);
        public Color slotMineColor  = new Color(0.11f, 0.14f, 0.19f, 1f);
        public Color slotTextColor  = new Color(0.95f, 0.96f, 0.98f, 1f);
        public Color slotDimColor   = new Color(0.42f, 0.47f, 0.55f, 1f);
        public Color pickingColor   = new Color(0.95f, 0.72f, 0.25f, 1f);

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

            WireLimbArray(limbButtons);
            WireLimbArray(limbAltButtons); // กดที่ชิ้นส่วนบนตัวหุ่นก็ได้ผลเดียวกับกดที่ช่องเสียบ
        }

        private void WireLimbArray(Button[] buttons)
        {
            if (buttons == null) return;

            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null) continue;
                int captured = i;
                buttons[i].onClick.AddListener(() => OnLimbClicked(captured));
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

            if (limbAltButtons != null)
                foreach (Button b in limbAltButtons)
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
            RefreshSlots();
            RefreshLimbButtons(myTeam, myLimb);
            RefreshCounter();
            RefreshStartButton(isHost);
            RefreshStatus(myTeam, myLimb);
        }

        private void RefreshTeamButtons(PvpTeam myTeam)
        {
            ApplyTeamTint(redTeamButton,  redTeamHeader,  PvpTeam.Red,  myTeam);
            ApplyTeamTint(blueTeamButton, blueTeamHeader, PvpTeam.Blue, myTeam);

            // ทีมเต็มแล้วกดไม่ได้ (ยกเว้นทีมที่ตัวเองอยู่)
            if (redTeamButton != null)
                redTeamButton.interactable =
                    myTeam == PvpTeam.Red || manager.CountTeam(PvpTeam.Red) < manager.MaxPlayersPerTeam;
            if (blueTeamButton != null)
                blueTeamButton.interactable =
                    myTeam == PvpTeam.Blue || manager.CountTeam(PvpTeam.Blue) < manager.MaxPlayersPerTeam;
        }

        /// <summary>
        /// แถบสีหัวการ์ด: ทีมที่เราอยู่สว่างเต็ม ทีมอื่นหรี่ลง
        /// เลย์เอาต์เก่า (ไม่มี header) ยังใช้ค่าขาว/ขาวจางเหมือนเดิม จะได้ไม่ย้อมการ์ดผิดสี
        /// </summary>
        private void ApplyTeamTint(Button button, Image header, PvpTeam team, PvpTeam myTeam)
        {
            Color factor = myTeam == team ? selectedTeamColor : idleTeamColor;
            TintButton(button, header != null ? team.DisplayColor() * factor : factor);
        }

        private void RefreshRosters()
        {
            if (redRosterText != null)  redRosterText.text  = BuildRoster(PvpTeam.Red);
            if (blueRosterText != null) blueRosterText.text = BuildRoster(PvpTeam.Blue);
        }

        private void RefreshSlots()
        {
            FillSlots(redSlots,  PvpTeam.Red);
            FillSlots(blueSlots, PvpTeam.Blue);
        }

        /// <summary>เติมการ์ดรายคนตามลำดับใน roster — ช่องที่เหลือปล่อยเป็นช่องว่างหรี่ๆ ไว้</summary>
        private void FillSlots(PvpPlayerSlotView[] slots, PvpTeam team)
        {
            if (slots == null || slots.Length == 0) return;

            List<PvpPlayerEntry> roster = manager.GetTeamRoster(team);
            ulong localId = NetworkManager.Singleton.LocalClientId;
            Color teamColor = team.DisplayColor();

            for (int i = 0; i < slots.Length; i++)
            {
                PvpPlayerSlotView slot = slots[i];
                if (slot == null) continue;

                if (i >= roster.Count)
                {
                    SetSlotEmpty(slot);
                    continue;
                }

                PvpPlayerEntry entry = roster[i];
                bool isMe = entry.clientId == localId;
                bool isHost = entry.clientId == NetworkManager.ServerClientId;
                bool ready = PvpLimb.IsValid(entry.limbIndex);

                string playerName = entry.playerName.ToString();
                if (string.IsNullOrEmpty(playerName)) playerName = $"Player {entry.clientId}";
                if (isHost) playerName += " [H]";

                SetText(slot.nameLabel, isMe ? $"{playerName} (You)" : playerName, slotTextColor);
                SetText(slot.partLabel,
                    ready ? PvpLimb.Name(entry.limbIndex).ToUpperInvariant() : "NO PART YET",
                    ready ? teamColor : slotDimColor);
                SetText(slot.stateLabel, ready ? "READY" : "PICKING...", ready ? teamColor : pickingColor);
                SetText(slot.avatarGlyph, playerName.Substring(0, 1).ToUpperInvariant(), slotTextColor);

                if (slot.background != null)
                    slot.background.color = isMe ? slotMineColor : slotFilledColor;
                if (slot.frame != null)
                    slot.frame.color = isMe
                        ? teamColor
                        : new Color(teamColor.r, teamColor.g, teamColor.b, 0.30f);
                if (slot.accent != null)
                    slot.accent.color = new Color(teamColor.r, teamColor.g, teamColor.b, isMe ? 1f : 0.45f);
                if (slot.avatarFrame != null)
                    slot.avatarFrame.color = new Color(teamColor.r, teamColor.g, teamColor.b, isMe ? 0.75f : 0.30f);
                if (slot.avatarIcon != null)
                    slot.avatarIcon.color = isMe ? slotTextColor : slotDimColor;
                if (slot.readyIcon != null)
                    slot.readyIcon.color = ready ? teamColor : new Color(1f, 1f, 1f, 0f);

                SetPartIcon(slot.partIcon, ready ? GetLimbSprite(entry.limbIndex) : null, teamColor);
            }
        }

        private void SetSlotEmpty(PvpPlayerSlotView slot)
        {
            SetText(slot.nameLabel, "EMPTY", slotDimColor);
            SetText(slot.partLabel, "-", slotDimColor);
            SetText(slot.stateLabel, "", slotDimColor);
            SetText(slot.avatarGlyph, "?", slotDimColor);

            if (slot.background != null) slot.background.color = slotEmptyColor;
            if (slot.frame != null) slot.frame.color = new Color(0.45f, 0.55f, 0.70f, 0.14f);
            if (slot.accent != null) slot.accent.color = new Color(1f, 1f, 1f, 0.08f);
            if (slot.avatarFrame != null) slot.avatarFrame.color = new Color(0.45f, 0.55f, 0.70f, 0.14f);
            if (slot.avatarIcon != null) slot.avatarIcon.color = new Color(0.30f, 0.35f, 0.42f, 1f);
            if (slot.readyIcon != null) slot.readyIcon.color = new Color(1f, 1f, 1f, 0f);

            SetPartIcon(slot.partIcon, null, Color.white);
        }

        private Sprite GetLimbSprite(int limbIndex)
        {
            if (limbPartSprites == null || !PvpLimb.IsValid(limbIndex)) return null;
            return limbIndex < limbPartSprites.Length ? limbPartSprites[limbIndex] : null;
        }

        private static void SetPartIcon(Image icon, Sprite sprite, Color tint)
        {
            if (icon == null) return;

            icon.sprite = sprite;
            icon.color = sprite != null ? tint : new Color(1f, 1f, 1f, 0f);
        }

        private static void SetText(TextMeshProUGUI label, string value, Color color)
        {
            if (label == null) return;
            label.text = value;
            label.color = color;
        }

        private void RefreshCounter()
        {
            int max = Mathf.Max(1, manager.MaxPlayersPerTeam) * 2;

            if (playersConnectedText != null)
                playersConnectedText.text = $"{manager.PlayerCount} / {max}  PLAYERS CONNECTED";

            if (subtitleText != null)
                subtitleText.text = $"2 TEAMS  /  {max} PLAYERS";
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

                bool selectable = hasTeam && (!taken || mine);
                limbButtons[i].interactable = selectable;

                Button alt = ArrayAt(limbAltButtons, i);
                if (alt != null) alt.interactable = selectable;

                Color target = !hasTeam ? limbLockedColor
                             : mine     ? limbMineColor
                             : taken    ? limbTakenColor
                                        : limbFreeColor;

                Image robotLimb = ArrayAt(limbIcons, i);
                if (robotLimb != null)
                    TintImage(robotLimb, target);          // ชิ้นส่วนบนตัวหุ่น
                else
                    TintButton(limbButtons[i], target);    // เลย์เอาต์เก่า: ปุ่มคือชิ้นส่วน

                // ช่องเสียบข้างหุ่น: กรอบบอกสถานะ ไอคอนข้างในหรี่กว่าตัวหุ่นนิดหน่อย
                Image socketFrame = ArrayAt(limbSocketFrames, i);
                if (socketFrame != null)
                {
                    TintImage(socketFrame, mine ? limbMineColor
                                         : taken ? limbTakenColor
                                         : hasTeam ? socketFreeColor
                                                   : socketLockedColor);
                }

                Image socketIcon = ArrayAt(limbSocketIcons, i);
                if (socketIcon != null)
                    TintImage(socketIcon, taken ? target : new Color(target.r, target.g, target.b, 0.5f));

                bool hasOwnerLabel = limbOwnerLabels != null && i < limbOwnerLabels.Length
                                                             && limbOwnerLabels[i] != null;

                if (limbLabels != null && i < limbLabels.Length && limbLabels[i] != null)
                {
                    // มีป้ายเจ้าของแยกแล้ว (เลย์เอาต์หุ่นกลางจอ) → ป้ายชื่อชิ้นส่วนอยู่นิ่งๆ
                    limbLabels[i].text = hasOwnerLabel
                        ? PvpLimb.Name(i).ToUpperInvariant()
                        : mine  ? $"{PvpLimb.Name(i)}\n<size=70%>(You)</size>"
                        : taken ? $"{PvpLimb.Name(i)}\n<size=70%>(Taken)</size>"
                                : PvpLimb.Name(i);
                }

                if (hasOwnerLabel)
                {
                    limbOwnerLabels[i].text = !hasTeam ? "PICK A TEAM"
                                            : mine     ? "YOU"
                                            : taken    ? TeamPlayerName(myTeam, owner)
                                                       : "AVAILABLE";
                    limbOwnerLabels[i].color = mine ? limbMineColor
                                             : taken ? limbTakenColor
                                                     : slotDimColor;
                }
            }
        }

        /// <summary>หาชื่อคนที่จองชิ้นส่วนไว้ — เจ้าของอยู่ในทีมเดียวกับเราเสมอ (limb แยกตามทีม)</summary>
        private string TeamPlayerName(PvpTeam team, ulong clientId)
        {
            foreach (PvpPlayerEntry entry in manager.GetTeamRoster(team))
            {
                if (entry.clientId != clientId) continue;

                string name = entry.playerName.ToString();
                return string.IsNullOrEmpty(name) ? $"Player {clientId}" : name;
            }

            return "TAKEN";
        }

        private void RefreshStartButton(bool isHost)
        {
            bool canStart = manager.CanStartMatch(out string reason);

            // เหตุผลที่ยังกดไม่ได้ไปโชว์ใต้ตัวนับผู้เล่น ปุ่มจะได้ไม่ต้องยัดข้อความยาวๆ
            if (playersHintText != null)
            {
                playersHintText.text = !canStart
                    ? reason.ToUpperInvariant()
                    : isHost ? "PRESS START WHEN ALL PLAYERS ARE READY"
                             : "WAITING FOR HOST TO START";
            }

            if (startButton == null) return;

            startButton.gameObject.SetActive(isHost);
            if (!isHost) return;

            startButton.interactable = canStart;

            if (startButtonLabel != null)
            {
                // มีที่โชว์เหตุผลแยกแล้ว → ปุ่มเก็บคำเดียวสั้นๆ ไว้
                startButtonLabel.text = playersHintText != null
                    ? "START"
                    : canStart ? "START FIGHT" : reason;
            }
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

        /// <summary>อ่านสมาชิก array แบบไม่ต้องกลัว null/สั้นเกิน — ช่องใน Inspector ปล่อยว่างได้ทุกอัน</summary>
        private static T ArrayAt<T>(T[] array, int index) where T : class
        {
            if (array == null || index < 0 || index >= array.Length) return null;
            return array[index];
        }

        private void TintButton(Button button, Color target)
        {
            if (button == null) return;

            Image image = button.targetGraphic as Image;
            if (image == null) image = button.GetComponent<Image>();
            TintImage(image, target);
        }

        private void TintImage(Image image, Color target)
        {
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
