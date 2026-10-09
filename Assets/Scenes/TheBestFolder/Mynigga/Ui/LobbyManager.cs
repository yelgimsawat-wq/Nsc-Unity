using System.Collections.Generic;
using Nsc.Match;
using Nsc.Robots;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DG.Tweening;
using TMPro;

/// <summary>
/// LobbyManager.cs — หน้าจอเลือกชิ้นส่วน (co-op) ที่ทับบนฉากเกมเพลย์ แสดงผลอย่างเดียว
///
/// การจองชิ้นส่วน รายชื่อผู้เล่น ที่นั่งรอต่อกลับ และการเริ่มแมตช์อยู่ที่ LimbSelection
/// การผูกกล้อง/คอนโทรลตอนเริ่มอยู่ที่ LimbControlBinder / เฟสของแมตช์อยู่ที่ MatchSession
/// ตัวนี้เหลือแค่: วาดรายชื่อ วาดสถานะชิ้นส่วน ส่งคำขอเมื่อกด และเริ่มบทสอนเล่นหลังผูกเสร็จ
///
/// UI ช่องที่ i (0-3) = LimbSlot เดียวกัน (แขนซ้าย, แขนขวา, ขาซ้าย, ขาขวา ของตัวหุ่น)
///
/// Features ported from OnlineNetworkUI.cs:
///   • DOTween fade/scale panel transitions
///   • Button click scale-punch feedback
///   • Mouse-parallax "menu feel" tilt effect
///   • Interactive robot-part images (clickable anatomy)
///   • State-based coloring: dimmed → lit + tinted on selection
/// </summary>
public class LobbyManager : MonoBehaviour
{
    public static LobbyManager Instance;

    [Header("UI Panels")]
    public GameObject selectionPanel;

    [Header("Player List UI")]
    [Tooltip("4 Text elements for P1 to P4 names")]
    [SerializeField] private TextMeshProUGUI[] playerNameTexts;
    [Tooltip("4 Text elements for P1 to P4 status")]
    [SerializeField] private TextMeshProUGUI[] playerStatusTexts;
    [Tooltip("4 Images for avatars to dim when empty")]
    [SerializeField] private Image[] playerAvatarImages;

    [Header("Part Selection UI")]
    [Tooltip("The 4 texts pointing to parts (Left Arm, Right Arm, Left Leg, Right Leg)")]
    [SerializeField] private TextMeshProUGUI[] partSelectionTexts;
    [Tooltip("The bottom status text")]
    [SerializeField] private TextMeshProUGUI bottomStatusText;

    [Header("Buttons")]
    [SerializeField] private Button[] limbButtons;
    [SerializeField] private Button startButton; // Host only

    [Header("Robot Part Images (Clickable Anatomy)")]
    [Tooltip("Assign the 4 limb UI Images in order: Left Arm, Right Arm, Left Leg, Right Leg.\n" +
             "These images act as clickable buttons — players hover/click them directly to select.")]
    [SerializeField] private Image[] robotPartImages;

    [Header("Always-Visible Robot Parts")]
    [Tooltip("Head, Torso, or any part that should always stay fully visible (alpha = 1).")]
    [SerializeField] private Image[] alwaysVisibleParts;

    [Header("Part Coloring")]
    [SerializeField] private Color unassignedColor  = new Color(1f, 1f, 1f, 1f);
    [SerializeField] private Color myPartColor      = new Color(0.2f, 0.85f, 0.4f, 1f);
    [SerializeField] private Color otherPartColor   = new Color(0.85f, 0.2f, 0.2f, 1f);
    [SerializeField] private Color hoverTintColor   = new Color(0.6f, 0.9f, 1f, 0.55f);
    [SerializeField] private float colorFadeDuration = 0.25f;

    [Header("UI Animation (from OnlineNetworkUI)")]
    [SerializeField] private float uiFadeDuration = 0.0f; // ปิด Fade กันภาพล่องหน
    [SerializeField] private float uiScaleFrom    = 1.0f;
    [SerializeField] private Ease  uiEase         = Ease.OutCubic;

    [Header("Button Hover")]
    [SerializeField] private Color buttonHoverColor       = new Color(0.2f, 0.85f, 0.3f, 1f);
    [SerializeField] private Color buttonPressedColor      = new Color(0.1f, 0.65f, 0.2f, 1f);
    [SerializeField] private float buttonHoverFadeDuration = 0.12f;

    [Header("Menu Feel (Mouse Parallax)")]
    [SerializeField] private Transform menuLookTarget;
    [SerializeField] private bool  menuFeelEnabled       = true;
    [SerializeField] private float menuTiltMaxYaw        = 7f;
    [SerializeField] private float menuTiltMaxPitch      = 3f;
    [SerializeField] private float menuTiltMaxRoll       = 1.5f;
    [SerializeField] private float menuTiltFollowSpeed   = 7f;
    [SerializeField] private float menuIdleScaleAmount   = 0.012f;
    [SerializeField] private float menuIdleSpeed         = 1.6f;
    [SerializeField] private float buttonClickScale      = 1.06f;
    [SerializeField] private float buttonClickDuration   = 0.12f;

    // DOTween tracking dictionaries (ported from OnlineNetworkUI)
    private readonly Dictionary<GameObject, Tween> runningUiTweens = new Dictionary<GameObject, Tween>();
    private readonly Dictionary<Transform, Vector3> originalUiScales = new Dictionary<Transform, Vector3>();
    private readonly Dictionary<Transform, Tween> buttonClickTweens = new Dictionary<Transform, Tween>();
    private readonly Dictionary<Button, UnityAction> buttonClickFeedbackListeners = new Dictionary<Button, UnityAction>();

    // Menu feel state
    private Quaternion menuTargetBaseRotation;
    private Vector3   menuTargetBaseScale;
    private bool      menuTargetPoseCaptured;

    // Part-image color tweens (per-image, so we can kill them cleanly)
    private readonly Dictionary<Image, Tween> partColorTweens = new Dictionary<Image, Tween>();

    // Hover state tracking for part images
    private int hoveredPartIndex = -1;

    private LimbSelection selection;
    private MatchSession session;
    private LimbControlBinder binder;
    private bool tutorialStarted;

    // ================================================================
    //  UNITY LIFECYCLE
    // ================================================================

    private void Awake()
    {
        Instance = this;
        EnsureSettingsManagerExists();

        // ✅ เปิด selectionPanel ทันทีตอนเริ่ม (ไม่ต้องรอ Network Spawn)
        if (selectionPanel != null)
        {
            selectionPanel.SetActive(true);
            CanvasGroup cg = selectionPanel.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.alpha = 1f;
                cg.interactable = true;
                cg.blocksRaycasts = true;
            }
        }

        InitPartSelectionTexts();
        WirePartImageClicks();
        InitAlwaysVisibleParts();
        ApplyButtonHoverColors();

        if (limbButtons != null)
        {
            for (int i = 0; i < limbButtons.Length; i++)
            {
                int captured = i;
                if (limbButtons[captured] == null) continue;
                limbButtons[captured].onClick.RemoveAllListeners();
                limbButtons[captured].onClick.AddListener(() => TryRequestLimb(captured));
            }
        }

        if (startButton != null)
        {
            startButton.gameObject.SetActive(false); // โชว์เฉพาะ Host หลังต่อเน็ตเสร็จ
            startButton.onClick.RemoveAllListeners();
            startButton.onClick.AddListener(OnStartButtonClicked);
            ApplyButtonHoverColor(startButton);
        }

        CaptureMenuFeelBasePose();
    }

    private void Start()
    {
        selection = LimbSelection.Current;
        session = MatchSession.Current;
        binder = LimbControlBinder.Current;

        if (selection != null) selection.AssignmentsChanged += RefreshAllButtonUI;
        if (session != null) session.PhaseChanged += OnPhaseChanged;
        if (binder != null) binder.Bound += OnLocalLimbBound;

        if (selection == null)
            Debug.LogWarning("[Lobby] ไม่มี LimbSelection ในฉาก — หน้าเลือกชิ้นส่วนจะกดไม่ได้", this);

        if (session != null && session.Phase != MatchPhase.Preparing)
            SetVisibleInstant(selectionPanel, false);

        RefreshAllButtonUI();
    }

    private void Update()
    {
        UpdateMenuFeel();

        // ปุ่ม Start โชว์เฉพาะ Host — รู้ได้ก็ต่อเมื่อเน็ตเริ่มแล้ว
        if (startButton != null)
        {
            bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
            if (startButton.gameObject.activeSelf != isHost) startButton.gameObject.SetActive(isHost);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;

        if (selection != null) selection.AssignmentsChanged -= RefreshAllButtonUI;
        if (session != null) session.PhaseChanged -= OnPhaseChanged;
        if (binder != null) binder.Bound -= OnLocalLimbBound;

        ClearButtonClickFeedback();
        KillAllButtonClickTweens();
        KillAllUiTweens();
        KillAllPartColorTweens();
        originalUiScales.Clear();
    }

    // ================================================================
    //  คำขอ — ส่งต่อให้ LimbSelection (server ตัดสิน)
    // ================================================================

    /// <summary>ทางเข้าเดียวที่ยิงคำขอจอง — กันคลิกทะลุก่อนเน็ตพร้อม (RPC ก่อน spawn ถูกปัดทิ้ง)</summary>
    private void TryRequestLimb(int index)
    {
        Robot robot = selection != null ? selection.PrimaryRobot : null;
        if (selection == null || !selection.IsSpawned || robot == null || !robot.IsSpawned)
        {
            Debug.LogWarning("[Lobby] ยังเชื่อมต่อเครือข่ายไม่เสร็จ รอสักครู่แล้วลองกดใหม่");
            if (bottomStatusText != null) bottomStatusText.text = "CONNECTING... PLEASE WAIT";
            return;
        }

        selection.RequestLimbRpc(robot.NetworkObjectId, (LimbSlot)index, false);
    }

    private void OnStartButtonClicked()
    {
        if (selection == null || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (startButton != null) startButton.interactable = false;
        selection.RequestStartRpc();
    }

    private void OnPhaseChanged(MatchPhase phase)
    {
        // ปิด Panel (animated fade-out) เมื่อแมตช์เริ่ม — ฟิสิกส์ปลดแช่แข็งโดย LimbSelection
        if (phase != MatchPhase.Preparing && selectionPanel != null)
            SetVisibleAnimated(selectionPanel, false);
    }

    /// <summary>ผูกกล้อง/คอนโทรลเสร็จแล้ว → เริ่มบทสอนเล่นตามบทบาท แขน/ขา</summary>
    private void OnLocalLimbBound(Robot robot, LimbSlot slot)
    {
        if (tutorialStarted || TutorialManager.Instance == null) return;
        tutorialStarted = true;

        TutorialManager.LimbRole role = slot.IsLeg() ? TutorialManager.LimbRole.Leg : TutorialManager.LimbRole.Arm;
        TutorialManager.Instance.SetRole(role);
        TutorialManager.Instance.StartTutorial();
        Debug.Log($"[Client] Tutorial started automatically for local limb {slot} ({role}).");
    }

    // ================================================================
    //  INTERACTIVE ROBOT PART IMAGES — Wire clicks & hover via EventTrigger
    // ================================================================

    private void WirePartImageClicks()
    {
        if (robotPartImages == null) return;

        for (int i = 0; i < robotPartImages.Length; i++)
        {
            if (robotPartImages[i] == null) continue;

            int captured = i;
            Image partImage = robotPartImages[captured];
            GameObject go = partImage.gameObject;

            // Make sure the image can receive raycasts
            partImage.raycastTarget = true;

            // Add or get EventTrigger
            EventTrigger trigger = go.GetComponent<EventTrigger>();
            if (trigger == null) trigger = go.AddComponent<EventTrigger>();
            trigger.triggers.Clear();

            // --- PointerClick → select this limb ---
            EventTrigger.Entry clickEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            clickEntry.callback.AddListener((_) =>
            {
                TryRequestLimb(captured);
                PlayPartClickFeedback(partImage);
            });
            trigger.triggers.Add(clickEntry);

            // --- PointerEnter → hover highlight ---
            EventTrigger.Entry enterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enterEntry.callback.AddListener((_) =>
            {
                hoveredPartIndex = captured;
                ApplyHoverHighlight(partImage, true);
                HighlightPartSelectionText(captured, true);
            });
            trigger.triggers.Add(enterEntry);

            // --- PointerExit → remove hover ---
            EventTrigger.Entry exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exitEntry.callback.AddListener((_) =>
            {
                if (hoveredPartIndex == captured) hoveredPartIndex = -1;
                ApplyHoverHighlight(partImage, false);
                HighlightPartSelectionText(captured, false);
                // Re-apply the correct ownership color after hover ends
                RefreshSinglePartImage(captured);
            });
            trigger.triggers.Add(exitEntry);
        }
    }

    private void PlayPartClickFeedback(Image partImage)
    {
        if (partImage == null) return;

        Transform t = partImage.transform;
        float punchAmount = Mathf.Max(1f, buttonClickScale) - 1f;
        float duration = Mathf.Max(0f, buttonClickDuration);
        if (punchAmount <= 0f || duration <= 0f) return;

        if (buttonClickTweens.TryGetValue(t, out Tween running) && running != null && running.IsActive())
            running.Kill(false);

        Vector3 baseScale = GetOriginalScale(t);
        t.localScale = baseScale;

        Tween clickTween = t
            .DOPunchScale(baseScale * punchAmount, duration, 1, 0.5f)
            .SetUpdate(true)
            .OnComplete(() =>
            {
                if (t != null) t.localScale = baseScale;
                buttonClickTweens.Remove(t);
            });

        buttonClickTweens[t] = clickTween;
    }

    private void HighlightPartSelectionText(int index, bool hovered)
    {
        if (partSelectionTexts == null || index < 0 || index >= partSelectionTexts.Length) return;
        var textUI = partSelectionTexts[index];
        if (textUI == null) return;

        if (hovered)
        {
            textUI.color = hoverTintColor;
            textUI.fontStyle |= FontStyles.Bold;
        }
        else
        {
            textUI.color = Color.white; // Or original color
            textUI.fontStyle &= ~FontStyles.Bold;
        }
    }

    private void ApplyHoverHighlight(Image partImage, bool hovered)
    {
        if (partImage == null) return;

        // Slight scale bump on hover — track in buttonClickTweens so it never
        // fights the click-punch tween on the same transform and gets killed in OnDestroy
        Transform t = partImage.transform;
        Vector3 baseScale = GetOriginalScale(t);
        Vector3 targetScale = hovered ? baseScale * 1.05f : baseScale;

        if (buttonClickTweens.TryGetValue(t, out Tween running) && running != null && running.IsActive())
            running.Kill(false);

        Tween hoverTween = t.DOScale(targetScale, 0.15f)
            .SetUpdate(true)
            .SetEase(Ease.OutCubic)
            .OnComplete(() => buttonClickTweens.Remove(t));

        buttonClickTweens[t] = hoverTween;

        // Additive brightness tint on hover (only if not already lit by ownership)
        if (hovered)
        {
            TweenPartColor(partImage, hoverTintColor, 0.12f);
        }
    }

    // ================================================================
    //  ALWAYS-VISIBLE PARTS (Head, Torso)
    // ================================================================

    private void InitPartSelectionTexts()
    {
        if (partSelectionTexts == null) return;
        for (int i = 0; i < partSelectionTexts.Length; i++)
        {
            if (partSelectionTexts[i] != null)
            {
                partSelectionTexts[i].text = GetDefaultLimbName(i);
            }
        }
    }

    private void InitAlwaysVisibleParts()
    {
        if (alwaysVisibleParts == null) return;
        foreach (var img in alwaysVisibleParts)
        {
            if (img == null) continue;
            Color c = img.color;
            c.a = 1f;
            img.color = c;
        }
    }

    // ================================================================
    //  Refresh — วาดสถานะจาก LimbSelection
    // ================================================================

    private ulong LocalClientId => NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : ulong.MaxValue;

    /// <summary>เจ้าของชิ้นที่ index — NoClient ถ้ายังว่าง</summary>
    private ulong OwnerOf(int index)
    {
        Robot robot = selection != null ? selection.PrimaryRobot : null;
        if (robot == null || !robot.IsSpawned || !LimbSlots.IsValid(index)) return LimbSelection.NoClient;
        return selection.GetController(robot.NetworkObjectId, (LimbSlot)index);
    }

    private void RefreshAllButtonUI()
    {
        RefreshAllPartImages();
        RefreshPlayerListUI();
        RefreshBottomStatusText();

        if (limbButtons == null) return;
        for (int i = 0; i < limbButtons.Length && i < LimbSlots.Count; i++)
        {
            if (limbButtons[i] == null) continue;

            ulong owner  = OwnerOf(i);
            bool isTaken = owner != LimbSelection.NoClient;
            bool isMine  = isTaken && owner == LocalClientId;

            limbButtons[i].interactable = !isTaken || isMine;

            // ป้ายอยู่ใน Part Selection Texts (UI แยก Text ออกมาจากปุ่ม)
            var label = (partSelectionTexts != null && i < partSelectionTexts.Length) ? partSelectionTexts[i] : null;
            if (label != null)
            {
                if (isMine)       label.text = "You";
                else if (isTaken) label.text = "Taken";
                else              label.text = GetDefaultLimbName(i);
            }
        }
    }

    private void RefreshPlayerListUI()
    {
        int playerCount = selection != null && selection.IsSpawned ? selection.PlayerCount : 0;

        for (int i = 0; i < 4; i++)
        {
            bool hasPlayer = i < playerCount;
            SelectionPlayer player = hasPlayer ? selection.GetPlayer(i) : default;

            if (playerAvatarImages != null && i < playerAvatarImages.Length && playerAvatarImages[i] != null)
                playerAvatarImages[i].color = hasPlayer ? Color.white : new Color(1f, 1f, 1f, 0.3f);

            // ชื่อจริงที่ sync มาจากเครื่องของแต่ละคน + ป้าย [Host]
            if (playerNameTexts != null && i < playerNameTexts.Length && playerNameTexts[i] != null)
            {
                string hostTag = hasPlayer && player.clientId == NetworkManager.ServerClientId ? " [Host]" : "";
                playerNameTexts[i].text = hasPlayer ? $"{player.DisplayName} (P{i + 1}){hostTag}" : $"P{i + 1}: Empty";
            }

            if (playerStatusTexts != null && i < playerStatusTexts.Length && playerStatusTexts[i] != null)
            {
                if (!hasPlayer)
                {
                    playerStatusTexts[i].text = "(PENDING JOIN)...";
                    continue;
                }

                LimbSlot? slot = selection.GetSlot(player.clientId);
                playerStatusTexts[i].text = slot.HasValue
                    ? $"(SELECTED {GetDefaultLimbName((int)slot.Value).ToUpper()})"
                    : "(UNASSIGNED)";
            }
        }
    }

    private void RefreshBottomStatusText()
    {
        if (bottomStatusText == null || selection == null || !selection.IsSpawned) return;

        bool iHaveSelected = selection.GetSlot(LocalClientId).HasValue;
        bottomStatusText.text = iHaveSelected
            ? "PLAYER STATUS: AWAITING DEPLOYMENT"
            : "PLAYER STATUS: AWAITING SELECTION";
    }

    private void RefreshAllPartImages()
    {
        if (robotPartImages == null) return;
        for (int i = 0; i < robotPartImages.Length && i < LimbSlots.Count; i++)
            RefreshSinglePartImage(i);
    }

    private void RefreshSinglePartImage(int index)
    {
        if (robotPartImages == null || index < 0 || index >= robotPartImages.Length) return;

        Image img = robotPartImages[index];
        if (img == null) return;

        ulong owner  = OwnerOf(index);
        bool isTaken = owner != LimbSelection.NoClient;
        bool isMine  = isTaken && owner == LocalClientId;

        // ว่าง = สีปกติ / ของเรา = เขียว / ของคนอื่น = แดง — เปลี่ยนทันทีที่กด ไม่รอเอาเมาส์ออก
        Color targetColor = !isTaken ? unassignedColor : isMine ? myPartColor : otherPartColor;
        TweenPartColor(img, targetColor, colorFadeDuration);
    }

    private void TweenPartColor(Image img, Color targetColor, float duration)
    {
        if (img == null) return;

        // Kill existing color tween for this image
        if (partColorTweens.TryGetValue(img, out Tween existing) && existing != null && existing.IsActive())
            existing.Kill(false);

        if (duration <= 0f)
        {
            img.color = targetColor;
            return;
        }

        Tween tween = img.DOColor(targetColor, duration)
            .SetUpdate(true)
            .SetEase(Ease.OutCubic)
            .OnComplete(() => partColorTweens.Remove(img));

        partColorTweens[img] = tween;
    }

    private void KillAllPartColorTweens()
    {
        foreach (var kv in partColorTweens)
        {
            if (kv.Value != null && kv.Value.IsActive())
                kv.Value.Kill(false);
        }
        partColorTweens.Clear();
    }

    private string GetDefaultLimbName(int index) => index switch
    {
        0 => "Left Arm",
        1 => "Right Arm",
        2 => "Left Leg",
        3 => "Right Leg",
        _ => "?"
    };

    // ================================================================
    //  DOTween UI Transitions (ported from OnlineNetworkUI.cs)
    // ================================================================

    private void SetVisibleAnimated(GameObject target, bool visible)
    {
        if (target == null) return;

        KillUiTween(target);

        if (!isActiveAndEnabled || uiFadeDuration <= 0f || (!visible && !target.activeSelf))
        {
            SetVisibleInstant(target, visible);
            return;
        }

        CanvasGroup canvasGroup = GetOrAddCanvasGroup(target);
        Transform targetTransform = target.transform;
        Vector3 baseScale = GetOriginalScale(targetTransform);
        float scaleFrom = Mathf.Max(0.01f, uiScaleFrom);
        Vector3 hiddenScale = baseScale * scaleFrom;

        if (visible && !target.activeSelf)
        {
            target.SetActive(true);
            canvasGroup.alpha = 0f;
            targetTransform.localScale = hiddenScale;
        }

        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        float endAlpha = visible ? 1f : 0f;
        Vector3 endScale = visible ? baseScale : hiddenScale;
        float duration = Mathf.Max(0.01f, uiFadeDuration);

        Sequence sequence = DOTween.Sequence().SetUpdate(true);
        sequence.Join(canvasGroup.DOFade(endAlpha, duration).SetEase(uiEase));
        sequence.Join(targetTransform.DOScale(endScale, duration).SetEase(uiEase));
        sequence.OnComplete(() =>
        {
            if (target == null)
            {
                runningUiTweens.Remove(target);
                return;
            }

            canvasGroup.alpha = endAlpha;
            targetTransform.localScale = baseScale;
            canvasGroup.interactable = visible;
            canvasGroup.blocksRaycasts = visible;

            if (!visible)
                target.SetActive(false);

            runningUiTweens.Remove(target);
        });

        runningUiTweens[target] = sequence;
    }

    private void SetVisibleInstant(GameObject target, bool visible)
    {
        if (target == null) return;

        KillUiTween(target);

        CanvasGroup canvasGroup = GetOrAddCanvasGroup(target);
        Transform targetTransform = target.transform;
        Vector3 baseScale = GetOriginalScale(targetTransform);

        target.SetActive(visible);
        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;
        targetTransform.localScale = baseScale;
    }

    // ================================================================
    //  Button Hover Colors & Click Punch (ported from OnlineNetworkUI.cs)
    // ================================================================

    private void ApplyButtonHoverColors()
    {
        ApplyButtonHoverColor(startButton);
    }

    private void ApplyButtonHoverColor(Button button)
    {
        if (button == null) return;

        button.transition = Selectable.Transition.ColorTint;

        ColorBlock colors = button.colors;
        colors.highlightedColor = buttonHoverColor;
        colors.selectedColor    = buttonHoverColor;
        colors.pressedColor     = buttonPressedColor;
        colors.fadeDuration     = Mathf.Max(0f, buttonHoverFadeDuration);
        button.colors = colors;

        RegisterButtonClickFeedback(button);
    }

    private void RegisterButtonClickFeedback(Button button)
    {
        if (button == null) return;

        if (buttonClickFeedbackListeners.TryGetValue(button, out UnityAction existingAction))
            button.onClick.RemoveListener(existingAction);

        UnityAction clickFeedback = () => PlayButtonClickFeedback(button);
        buttonClickFeedbackListeners[button] = clickFeedback;
        button.onClick.AddListener(clickFeedback);
    }

    private void ClearButtonClickFeedback()
    {
        foreach (var listener in buttonClickFeedbackListeners)
        {
            if (listener.Key != null && listener.Value != null)
                listener.Key.onClick.RemoveListener(listener.Value);
        }
        buttonClickFeedbackListeners.Clear();
    }

    private void PlayButtonClickFeedback(Button button)
    {
        if (button == null || button.transform == null || !button.gameObject.activeInHierarchy) return;

        float punchAmount = Mathf.Max(1f, buttonClickScale) - 1f;
        float duration = Mathf.Max(0f, buttonClickDuration);
        if (punchAmount <= 0f || duration <= 0f) return;

        Transform buttonTransform = button.transform;

        if (buttonClickTweens.TryGetValue(buttonTransform, out Tween runningTween) && runningTween != null && runningTween.IsActive())
            runningTween.Kill(false);

        Vector3 baseScale = GetOriginalScale(buttonTransform);
        buttonTransform.localScale = baseScale;

        Tween clickTween = buttonTransform
            .DOPunchScale(baseScale * punchAmount, duration, 1, 0.5f)
            .SetUpdate(true)
            .OnComplete(() =>
            {
                if (buttonTransform != null)
                    buttonTransform.localScale = baseScale;

                buttonClickTweens.Remove(buttonTransform);
            });

        buttonClickTweens[buttonTransform] = clickTween;
    }

    private void KillAllButtonClickTweens()
    {
        foreach (var clickTween in buttonClickTweens)
        {
            if (clickTween.Value != null && clickTween.Value.IsActive())
                clickTween.Value.Kill(false);

            if (clickTween.Key != null)
                clickTween.Key.localScale = GetOriginalScale(clickTween.Key);
        }
        buttonClickTweens.Clear();
    }

    // ================================================================
    //  Menu Feel — Mouse Parallax Tilt (ported from OnlineNetworkUI.cs)
    // ================================================================

    private void CaptureMenuFeelBasePose()
    {
        if (menuLookTarget == null || menuTargetPoseCaptured) return;

        menuTargetBaseRotation = menuLookTarget.localRotation;
        menuTargetBaseScale    = menuLookTarget.localScale;
        menuTargetPoseCaptured = true;
    }

    private void UpdateMenuFeel()
    {
        if (menuLookTarget == null) return;

        CaptureMenuFeelBasePose();
        if (!menuTargetPoseCaptured) return;

        float followSpeed = Mathf.Max(0.01f, menuTiltFollowSpeed);
        float followT = 1f - Mathf.Exp(-followSpeed * Time.unscaledDeltaTime);
        Quaternion targetRotation = menuTargetBaseRotation;
        Vector3 targetScale = menuTargetBaseScale;

        if (ShouldReactToMenuMouse())
        {
            Vector3 mousePosition = Input.mousePosition;
            float screenWidth  = Mathf.Max(1f, Screen.width);
            float screenHeight = Mathf.Max(1f, Screen.height);
            float normalizedX = Mathf.Clamp(((mousePosition.x / screenWidth)  - 0.5f) * 2f, -1f, 1f);
            float normalizedY = Mathf.Clamp(((mousePosition.y / screenHeight) - 0.5f) * 2f, -1f, 1f);

            Quaternion mouseTilt = Quaternion.Euler(
                -normalizedY * menuTiltMaxPitch,
                 normalizedX * menuTiltMaxYaw,
                -normalizedX * menuTiltMaxRoll);

            float idleScale = 1f + Mathf.Sin(Time.unscaledTime * menuIdleSpeed) * menuIdleScaleAmount;
            targetRotation = menuTargetBaseRotation * mouseTilt;
            targetScale    = menuTargetBaseScale * idleScale;
        }

        menuLookTarget.localRotation = Quaternion.Slerp(menuLookTarget.localRotation, targetRotation, followT);
        menuLookTarget.localScale    = Vector3.Lerp(menuLookTarget.localScale, targetScale, followT);
    }

    private bool ShouldReactToMenuMouse()
    {
        if (!menuFeelEnabled) return false;
        if (selectionPanel == null || !selectionPanel.activeInHierarchy) return false;
        return true;
    }

    // ================================================================
    //  DOTween Utility Helpers
    // ================================================================

    /// <summary>
    /// ตรวจสอบว่ามี SettingsManager ในฉากไหม
    /// หมายเหตุ: ห้ามสร้างด้วย AddComponent เพราะ SettingsManager พึ่งพา UI references
    /// ที่ต้องลากใน Inspector — ตัวที่สร้างสด ๆ จะ references เป็น null ทั้งหมดและใช้งานไม่ได้
    /// ต้องวาง Prefab ของ SettingsManager ไว้ในฉากแทน
    /// </summary>
    private void EnsureSettingsManagerExists()
    {
        if (SettingsManager.Instance == null)
        {
            Debug.LogWarning("[LobbyManager] SettingsManager.Instance is null! " +
                "Place the SettingsManager prefab in this scene — an auto-created instance would have no UI references.");
        }
    }

    private CanvasGroup GetOrAddCanvasGroup(GameObject target)
    {
        if (!target.TryGetComponent(out CanvasGroup canvasGroup))
            canvasGroup = target.AddComponent<CanvasGroup>();
        return canvasGroup;
    }

    private Vector3 GetOriginalScale(Transform targetTransform)
    {
        if (!originalUiScales.TryGetValue(targetTransform, out Vector3 baseScale))
        {
            baseScale = targetTransform.localScale;
            originalUiScales[targetTransform] = baseScale;
        }
        return baseScale;
    }

    private void KillUiTween(GameObject target)
    {
        if (runningUiTweens.TryGetValue(target, out Tween runningTween) && runningTween != null && runningTween.IsActive())
            runningTween.Kill(false);
        runningUiTweens.Remove(target);
    }

    private void KillAllUiTweens()
    {
        foreach (Tween runningTween in runningUiTweens.Values)
        {
            if (runningTween != null && runningTween.IsActive())
                runningTween.Kill(false);
        }
        runningUiTweens.Clear();
    }
}
