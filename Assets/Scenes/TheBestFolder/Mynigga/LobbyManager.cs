using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DG.Tweening;
using TMPro;

/// <summary>
/// LobbyManager.cs
/// Canvas ทับบน Gameplay Scene — เลือก Part แล้ว Host กด Start
/// ปิด Panel + Unfreeze Physics ให้เกมเริ่ม (ไม่มี LoadScene)
///
/// Features ported from OnlineNetworkUI.cs:
///   • DOTween fade/scale panel transitions
///   • Button click scale-punch feedback
///   • Mouse-parallax "menu feel" tilt effect
///   • Interactive robot-part images (clickable anatomy)
///   • State-based coloring: dimmed → lit + tinted on selection
/// </summary>
public class LobbyManager : NetworkBehaviour
{
    public static LobbyManager Instance;

    [Header("UI Panels")]
    public GameObject selectionPanel;
    public GameObject robotContainer;

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

    [Header("Robot Targets")]
    public GameObject leftArm;
    public GameObject rightArm;
    public GameObject leftLeg;
    public GameObject rightLeg;

    [Header("Buttons")]
    [SerializeField] private Button[] limbButtons; // ✅ นำกลับมาเพื่อให้ปุ่มเดิมทำงานได้
    [SerializeField] private Button startButton; // Host only

    [Header("Robot Part Images (Clickable Anatomy)")]
    [Tooltip("Assign the 4 limb UI Images in order: Left Arm, Right Arm, Left Leg, Right Leg.\n" +
             "These images act as clickable buttons — players hover/click them directly to select.")]
    [SerializeField] private Image[] robotPartImages;

    [Header("Always-Visible Robot Parts")]
    [Tooltip("Head, Torso, or any part that should always stay fully visible (alpha = 1).")]
    [SerializeField] private Image[] alwaysVisibleParts;

    [Header("Part Coloring")]
    [SerializeField] private Color unassignedColor  = new Color(1f, 1f, 1f, 1f); // ✅ เปลี่ยนให้ภาพปกติ ไม่จางหาย
    [SerializeField] private Color myPartColor      = new Color(0.2f, 0.85f, 0.4f, 1f);
    [SerializeField] private Color otherPartColor   = new Color(0.85f, 0.2f, 0.2f, 1f);
    [SerializeField] private Color hoverTintColor   = new Color(0.6f, 0.9f, 1f, 0.55f);
    [SerializeField] private float colorFadeDuration = 0.25f;

    [Header("UI Animation (from OnlineNetworkUI)")]
    [SerializeField] private float uiFadeDuration = 0.0f; // ✅ ปิด Fade กันภาพล่องหน
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

    // NetworkList ต้อง init ระดับ field (ก่อน OnNetworkSpawn)
    private NetworkList<ulong> limbOwners = new NetworkList<ulong>(
        new ulong[] { ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue });

    // เก็บรายชื่อ ClientId ที่ต่อเข้ามา เพื่อเอาไปแมปกับ Slot P1, P2, P3, P4
    private NetworkList<ulong> connectedClients = new NetworkList<ulong>();

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

    // ✅ [Startup Race Fix] true ก็ต่อเมื่อ OnNetworkSpawn ทำงานแล้วเท่านั้น
    // (การันตีว่า NetworkManager กำลัง Listening อยู่จริง — ปุ่มใน Awake() ต่อสายให้กดได้เร็วเกินไป
    // ถ้าคลิกก่อน NetworkManager พร้อม ServerRpc จะโดนปัดทิ้งพร้อม error "can only be invoked after starting")
    private bool _networkReady = false;

    // ทางเข้าเดียวที่อนุญาตให้ยิง RequestLimbServerRpc — กันคลิกทะลุก่อนเน็ตพร้อม
    private void TryRequestLimb(int index)
    {
        if (!_networkReady)
        {
            Debug.LogWarning("[Lobby] ยังเชื่อมต่อเครือข่ายไม่เสร็จ รอสักครู่แล้วลองกดใหม่");
            if (bottomStatusText != null) bottomStatusText.text = "CONNECTING... PLEASE WAIT";
            return;
        }
        RequestLimbServerRpc(index);
    }

    // ================================================================
    //  UNITY LIFECYCLE
    // ================================================================

    void Awake()
    {
        Instance = this;

        // Freeze physics early to prevent objects from falling before clicking start
        ResolveActiveRobot(); // หาหุ่นตัวที่ active ก่อน — กัน freeze/อ้างอิงผิดตัว
        FreezeAllPhysics();

        // ✅ ตรวจสอบว่า SettingsManager ยังอยู่ไหม ถ้าหายให้สร้างใหม่
        EnsureSettingsManagerExists();

        // ✅ บังคับเปิด selectionPanel ทันทีตอนเริ่ม (ไม่ต้องรอ Network Spawn)
        if (selectionPanel != null)
        {
            selectionPanel.SetActive(true);

            // ถ้ามี CanvasGroup อยู่แล้ว ต้องให้ alpha = 1 ด้วย
            CanvasGroup cg = selectionPanel.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.alpha = 1f;
                cg.interactable = true;
                cg.blocksRaycasts = true;
            }
        }

        // Initialize Part Selection Texts
        InitPartSelectionTexts();

        // Wire clickable robot-part images
        WirePartImageClicks();

        // Ensure always-visible parts are opaque
        InitAlwaysVisibleParts();

        // Apply hover colors & click punch to all normal buttons
        ApplyButtonHoverColors();

        // ✅ นำระบบผูกปุ่มแบบเก่ากลับมา
        if (limbButtons != null)
        {
            for (int i = 0; i < limbButtons.Length; i++)
            {
                int captured = i;
                if (limbButtons[captured] != null)
                {
                    limbButtons[captured].onClick.RemoveAllListeners();
                    limbButtons[captured].onClick.AddListener(() => TryRequestLimb(captured));
                }
            }
        }

        // Capture base pose for parallax
        CaptureMenuFeelBasePose();
    }

    void Update()
    {
        UpdateMenuFeel();
    }

    void OnDestroy()
    {
        ClearButtonClickFeedback();
        KillAllButtonClickTweens();
        KillAllUiTweens();
        KillAllPartColorTweens();
        originalUiScales.Clear();
    }

    // ================================================================
    //  NETWORK SPAWN — Freeze physics, wire UI, subscribe list changes
    // ================================================================

    public override void OnNetworkSpawn()
{
    base.OnNetworkSpawn();

    // ✅ NetworkManager listening แน่นอนแล้ว ณ จุดนี้ — ปลดล็อกให้กดเลือกชิ้นส่วนได้
    _networkReady = true;
    if (bottomStatusText != null) bottomStatusText.text = "PLAYER STATUS: AWAITING SELECTION";

    // Freeze ทุก Rigidbody ตอนเปิด Panel ทั้ง Host และ Client
    ResolveActiveRobot();  // เผื่อหุ่นถูกสลับ/ย้ายหลัง Awake
    DisableExtraRobots();  // server ถอดหุ่นตัวเกินออกจากเกมอัตโนมัติ
    FreezeAllPhysics();

    // ✅ เปิด Panel ตอนเริ่ม
    if (selectionPanel != null)
        SetVisibleAnimated(selectionPanel, true);

    if (IsServer)
    {
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        
        // Add already connected clients
        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (!connectedClients.Contains(clientId))
                connectedClients.Add(clientId);
        }
    }

    // Subscribe NetworkList → อัปเดต UI ปุ่มทุกครั้งที่มีคนจอง
    limbOwners.OnListChanged += OnLimbOwnersChanged;
    connectedClients.OnListChanged += OnConnectedClientsChanged;

    // Refresh ปุ่มให้ตรงกับ state ปัจจุบัน (กรณี Client join หลัง Host จองไปแล้ว)
    RefreshAllButtonUI();

    if (startButton != null)
    {
        startButton.gameObject.SetActive(IsServer);
        if (IsServer)
        {
            startButton.onClick.RemoveAllListeners();
            startButton.onClick.AddListener(OnStartButtonClicked);
            ApplyButtonHoverColor(startButton);
        }
    }
}

    public override void OnNetworkDespawn()
    {
        limbOwners.OnListChanged -= OnLimbOwnersChanged;
        connectedClients.OnListChanged -= OnConnectedClientsChanged;

        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        base.OnNetworkDespawn();
    }

    private void OnClientConnected(ulong clientId)
    {
        if (IsServer && !connectedClients.Contains(clientId))
            connectedClients.Add(clientId);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (IsServer)
        {
            connectedClients.Remove(clientId);
            // ถ้าคนนั้นจอง limb ไว้ ให้เอาออกด้วย
            for (int i = 0; i < limbOwners.Count; i++)
            {
                if (limbOwners[i] == clientId)
                {
                    limbOwners[i] = ulong.MaxValue;
                }
            }
        }
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

        // Slight scale bump on hover
        Transform t = partImage.transform;
        Vector3 baseScale = GetOriginalScale(t);
        Vector3 targetScale = hovered ? baseScale * 1.05f : baseScale;

        t.DOScale(targetScale, 0.15f).SetUpdate(true).SetEase(Ease.OutCubic);

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
    //  NetworkList callback → update button + image visuals
    // ================================================================

    private void OnLimbOwnersChanged(NetworkListEvent<ulong> changeEvent)
    {
        RefreshAllButtonUI();
    }

    private void OnConnectedClientsChanged(NetworkListEvent<ulong> changeEvent)
    {
        RefreshAllButtonUI();
    }

    private void RefreshAllButtonUI()
    {
        // Refresh all robot-part images to match ownership state
        RefreshAllPartImages();
        
        // Refresh player list
        RefreshPlayerListUI();
        
        // Refresh bottom status text
        RefreshBottomStatusText();

        // ✅ อัปเดตข้อความปุ่มแบบเก่าให้กลับมาใช้งานได้
        if (limbButtons != null)
        {
            for (int i = 0; i < limbButtons.Length; i++)
            {
                if (limbButtons[i] == null) continue;

                ulong owner  = limbOwners[i];
                bool isTaken = owner != ulong.MaxValue;
                bool isMine  = isTaken && owner == NetworkManager.Singleton.LocalClientId;

                limbButtons[i].interactable = !isTaken || isMine;

                // ✅ แก้ไขให้ดึง Text จาก Part Selection Texts ที่คุณตั้งไว้ แทนที่จะหาในปุ่ม (เพราะ UI ของคุณแยก Text ออกมา)
                var label = (partSelectionTexts != null && i < partSelectionTexts.Length) ? partSelectionTexts[i] : null;
                if (label != null)
                {
                    if (isMine)       label.text = "You";
                    else if (isTaken) label.text = "Taken";
                    else              label.text = GetDefaultLimbName(i);
                }
            }
        }

        // Start button logic
        if (IsServer && startButton != null)
        {
            // อาจจะเช็คว่าพร้อมทุกคนไหม หรือ Host เริ่มได้เลย
            // เพื่อความเรียบง่าย Host สามารถเริ่มได้เลย
        }
    }

    private void RefreshPlayerListUI()
    {
        for (int i = 0; i < 4; i++)
        {
            bool hasPlayer = i < connectedClients.Count;
            ulong clientId = hasPlayer ? connectedClients[i] : ulong.MaxValue;

            // 1. Avatar Color
            if (playerAvatarImages != null && i < playerAvatarImages.Length && playerAvatarImages[i] != null)
            {
                playerAvatarImages[i].color = hasPlayer ? Color.white : new Color(1f, 1f, 1f, 0.3f);
            }

            // 2. Name Text
            if (playerNameTexts != null && i < playerNameTexts.Length && playerNameTexts[i] != null)
            {
                if (hasPlayer)
                {
                    // ✅ ถ้าเป็นตัวเอง ให้แสดงชื่อจาก Settings + เลข Slot
                    if (clientId == NetworkManager.Singleton.LocalClientId)
                    {
                        string myName = PlayerPrefs.GetString("PlayerName", "Player");
                        playerNameTexts[i].text = $"{myName} (P{i + 1})";
                    }
                    else if (clientId == NetworkManager.ServerClientId)
                    {
                        playerNameTexts[i].text = $"Host (P{i + 1})";
                    }
                    else
                    {
                        playerNameTexts[i].text = $"Player (P{i + 1})";
                    }
                }
                else
                {
                    playerNameTexts[i].text = $"P{i + 1}: Empty";
                }
            }

            // 3. Status Text
            if (playerStatusTexts != null && i < playerStatusTexts.Length && playerStatusTexts[i] != null)
            {
                if (hasPlayer)
                {
                    // Check if this client owns any limb
                    int ownedLimbIndex = -1;
                    for (int j = 0; j < limbOwners.Count; j++)
                    {
                        if (limbOwners[j] == clientId)
                        {
                            ownedLimbIndex = j;
                            break;
                        }
                    }

                    if (ownedLimbIndex != -1)
                        playerStatusTexts[i].text = $"(SELECTED {GetDefaultLimbName(ownedLimbIndex).ToUpper()})";
                    else
                        playerStatusTexts[i].text = "(UNASSIGNED)";
                }
                else
                {
                    playerStatusTexts[i].text = "(PENDING JOIN)...";
                }
            }
        }
    }

    private void RefreshBottomStatusText()
    {
        if (bottomStatusText == null) return;

        ulong myId = NetworkManager.Singleton.LocalClientId;
        bool iHaveSelected = false;

        for (int i = 0; i < limbOwners.Count; i++)
        {
            if (limbOwners[i] == myId)
            {
                iHaveSelected = true;
                break;
            }
        }

        if (iHaveSelected)
            bottomStatusText.text = "PLAYER STATUS: AWAITING DEPLOYMENT";
        else
            bottomStatusText.text = "PLAYER STATUS: AWAITING SELECTION";
    }

    /// <summary>
    /// Update every robot-part image color based on current ownership.
    /// </summary>
    private void RefreshAllPartImages()
    {
        if (robotPartImages == null) return;

        for (int i = 0; i < robotPartImages.Length && i < limbOwners.Count; i++)
        {
            RefreshSinglePartImage(i);
        }
    }

    /// <summary>
    /// Update a single part image's color/alpha based on ownership.
    /// </summary>
    private void RefreshSinglePartImage(int index)
    {
        if (robotPartImages == null || index < 0 || index >= robotPartImages.Length) return;

        Image img = robotPartImages[index];
        if (img == null) return;

        ulong owner  = limbOwners[index];
        bool isTaken = owner != ulong.MaxValue;
        bool isMine  = isTaken && owner == NetworkManager.Singleton.LocalClientId;

        Color targetColor;

        if (!isTaken)
        {
            // Unassigned → dimmed / faded out (missing component feel)
            targetColor = unassignedColor;
        }
        else if (isMine)
        {
            // My part → bright green (local player highlight)
            targetColor = myPartColor;
        }
        else
        {
            // Other client's part → red tint
            targetColor = otherPartColor;
        }

        // ✅ เอาการเช็ค Hover ออก เพื่อให้มันเปลี่ยนเป็นสีเขียวทันทีที่กด (ของเดิมพอกดแล้วสีไม่เปลี่ยนจนกว่าจะเอาเมาส์ออก)
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
    //  Select Part — Send to Server to check availability
    // ================================================================

    [ServerRpc(RequireOwnership = false)]
    public void RequestLimbServerRpc(int index, ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;

        // ถ้าอันที่กดอยู่แล้วของคนอื่น → ไม่ทำอะไร
        if (limbOwners[index] != ulong.MaxValue && limbOwners[index] != clientId)
        {
            Debug.Log($"[Server] Part {index} already reserved by Client {limbOwners[index]}");
            return;
        }

        // ยกเลิกอันเก่าของตัวเองก่อน (ถ้าเคยจองไว้)
        for (int i = 0; i < limbOwners.Count; i++)
        {
            if (limbOwners[i] == clientId)
            {
                limbOwners[i] = ulong.MaxValue;
                Debug.Log($"[Server] Client {clientId} released Part {i}");
                break;
            }
        }

        // จองอันใหม่
        limbOwners[index] = clientId;
        Debug.Log($"[Server] Client {clientId} reserved Part {index}");
    }

    // ================================================================
    //  Host clicks Start — ปิด Panel + Unfreeze (ไม่มี LoadScene)
    // ================================================================

    private void OnStartButtonClicked()
    {
        if (!IsServer) return;
        if (startButton != null) startButton.interactable = false;

        ResolveActiveRobot(); // การันตีว่าโอน ownership ให้หุ่นตัวที่ active จริง

        // --- NEW: Transfer ownership to the players who selected the limbs ---
        for (int i = 0; i < limbOwners.Count; i++)
        {
            if (limbOwners[i] != ulong.MaxValue)
            {
                GameObject targetLimb = GetLimbByIndex(i);
                if (targetLimb != null)
                {
                    NetworkObject no = targetLimb.GetComponent<NetworkObject>();
                    if (no != null) no.ChangeOwnership(limbOwners[i]);
                }
            }
        }

        // ยิง ClientRpc ไปทุกคนพร้อมกัน:
        // 1. Assign limb → player
        // 2. ปิด selectionPanel (animated)
        // 3. Unfreeze physics → เกมเริ่ม
        StartGameClientRpc();

        Debug.Log("[Server] Game started — panel closed, physics unfrozen, ownership transferred.");
    }

    // ================================================================
    //  Start Game — runs on ALL Clients (including Host)
    // ================================================================

    [ClientRpc]
    void StartGameClientRpc()
    {
        // Assign limb → player ก่อน (retry เผื่อ PlayerObject spawn ไม่ทัน)
        StartCoroutine(AssignLimbsThenUnfreeze());
    }

    private IEnumerator AssignLimbsThenUnfreeze(int maxAttempts = 10, float retryDelay = 0.2f)
    {
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            bool allResolved = TryAssignAllLimbs();

            if (allResolved)
            {
                Debug.Log("[Client] All limbs assigned.");
                break;
            }

            Debug.Log($"[Client] Limb assign attempt {attempt + 1}/{maxAttempts} — retrying...");
            yield return new WaitForSeconds(retryDelay);
        }

        // ปิด Panel + Unfreeze (animated fade-out instead of instant)
        if (selectionPanel != null)
            SetVisibleAnimated(selectionPanel, false);

        UnfreezeAllPhysics();
    }

    private bool TryAssignAllLimbs()
    {
        ResolveActiveRobot(); // ฝั่ง client ก็ต้องชี้หุ่นตัวเดียวกับ server ก่อนเสียบกล้อง

        bool allDone = true;

        for (int i = 0; i < limbOwners.Count; i++)
        {
            if (limbOwners[i] == ulong.MaxValue) continue;

            ulong ownerId  = limbOwners[i];
            GameObject playerObj = GetPlayerObject(ownerId);

            if (playerObj == null)
            {
                allDone = false;
                continue;
            }

            GameObject targetLimb = GetLimbByIndex(i);
            if (targetLimb == null) continue;

            // Only the specific client who owns this limb sets up their local camera
            if (ownerId == NetworkManager.Singleton.LocalClientId)
            {
                Camera playerCam = playerObj.GetComponentInChildren<Camera>();
                if (playerCam == null) playerCam = Camera.main; // fallback
                
                // Tell the PlayerCam to orbit the assigned limb
                var camScript = playerObj.GetComponent<PlayerCam>();
                if (camScript != null) camScript.followTarget = targetLimb.transform;
                
                if (i >= 2) // Legs
                {
                    var footMovement = targetLimb.GetComponent<PlayerFootForRobot>();
                    if (footMovement != null)
                    {
                        footMovement.playerCamera = playerCam;
                        footMovement.enabled = true;
                    }
                }
                else // Arms
                {
                    var handMovement = targetLimb.GetComponent<PlayerHandMovement>();
                    if (handMovement != null)
                    {
                        handMovement.playerCamera = playerCam;
                        handMovement.enabled = true;
                    }
                }
            }
        }

        return allDone;
    }

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
    /// ตรวจสอบและสร้าง SettingsManager ถ้ายังไม่มี (DontDestroyOnLoad หายไปตอนเปลี่ยน Scene)
    /// </summary>
    private void EnsureSettingsManagerExists()
    {
        if (SettingsManager.Instance == null)
        {
            Debug.LogWarning("[LobbyManager] SettingsManager.Instance is null! Creating new instance...");

            // สร้าง GameObject ใหม่พร้อม SettingsManager component
            GameObject settingsObj = new GameObject("SettingsManager");
            settingsObj.AddComponent<SettingsManager>();

            Debug.Log("[LobbyManager] ✅ SettingsManager created successfully!");
        }
        else
        {
            Debug.Log("[LobbyManager] ✅ SettingsManager already exists.");
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

    // ================================================================
    //  Freeze / Unfreeze Physics
    // ================================================================

    private void FreezeAllPhysics()
    {
        if (robotContainer == null) return;
        foreach (var rigid in robotContainer.GetComponentsInChildren<Rigidbody>())
        {
            rigid.isKinematic = true;
            rigid.Sleep();
        }
        Debug.Log("[Lobby] Physics frozen.");
    }

    private void UnfreezeAllPhysics()
    {
        if (robotContainer == null) return;
        foreach (var rigid in robotContainer.GetComponentsInChildren<Rigidbody>())
        {
            rigid.isKinematic = false;
            rigid.WakeUp();
        }
        Debug.Log("[Lobby] Physics unfrozen — Game running!");
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private GameObject GetLimbByIndex(int index)
    {
        return index switch { 0 => leftArm, 1 => rightArm, 2 => leftLeg, 3 => rightLeg, _ => null };
    }

    [Header("Robot Auto-Management")]
    [Tooltip("ปิดหุ่นตัวเกินในฉากอัตโนมัติตอนเริ่มเกม เหลือเฉพาะตัวที่ระบบเลือกใช้\n" +
             "จะได้ก็อป/ย้าย/ทดลองหุ่นหลายตัวในฉากได้โดยไม่ต้องนั่งปิดเอง")]
    public bool autoDisableExtraRobots = true;

    // ✅ [Auto Disable] ถอดหุ่นตัวเกินออกจากเกม (server เท่านั้น)
    // ตัวที่ถูกเลือกโดย ResolveActiveRobot = ตัวจริง / ตัวอื่นที่ active ค้าง = ถูกถอด
    private void DisableExtraRobots()
    {
        if (!autoDisableExtraRobots || robotContainer == null) return;
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        foreach (var torso in FindObjectsByType<TorsoMovement>(FindObjectsSortMode.None))
        {
            GameObject root = torso.transform.root.gameObject;
            if (root == robotContainer) continue;

            // ถอดจาก network ก่อน (ให้ client ทุกเครื่องเห็นตรงกัน) แล้วค่อยปิด
            foreach (var no in root.GetComponentsInChildren<NetworkObject>(true))
                if (no.IsSpawned) no.Despawn(false);

            root.SetActive(false);
            Debug.Log($"[Lobby] 🤖 ปิดหุ่นตัวเกินอัตโนมัติ: {root.name}");
        }
    }

    // ✅ [Dynamic Robot Resolve] หา "หุ่นตัวที่ active จริง" ตอนรันไทม์
    // แก้ปัญหา: ย้าย/ก็อป/สลับตัวหุ่นในฉากแล้ว reference เดิมยังชี้ตัวเก่าที่ปิดอยู่
    // → freeze/ownership/กล้อง ไปลงหุ่นผิดตัวที่ตำแหน่งเดิม (อาการ "spawn ไม่ตรง")
    private void ResolveActiveRobot()
    {
        // ถ้า container เดิมตาย/ถูกปิด → หาใหม่จาก TorsoMovement ที่ active อยู่
        if (robotContainer == null || !robotContainer.activeInHierarchy)
        {
            TorsoMovement torso = FindFirstObjectByType<TorsoMovement>();
            if (torso != null) robotContainer = torso.transform.root.gameObject;
        }
        if (robotContainer == null)
        {
            Debug.LogWarning("[Lobby] ไม่พบหุ่นที่ active ในฉากเลย!");
            return;
        }

        // ถ้าช่องชิ้นส่วนยังชี้ของ active ครบทุกช่อง ก็ไม่ต้องทำอะไร
        bool limbsValid = leftArm  != null && leftArm.activeInHierarchy  &&
                          rightArm != null && rightArm.activeInHierarchy &&
                          leftLeg  != null && leftLeg.activeInHierarchy  &&
                          rightLeg != null && rightLeg.activeInHierarchy;
        if (limbsValid) return;

        // จับคู่ใหม่จากชิ้นส่วนของหุ่นตัวที่ active — ใช้ convention เดิมตามที่ต่อไว้ใน Inspector
        // (มุมมอง UI หันหน้าเข้าหาผู้เล่น: ช่อง leftArm = มือขวาของหุ่น ฯลฯ)
        foreach (var hand in robotContainer.GetComponentsInChildren<PlayerHandMovement>(true))
        {
            string n = hand.gameObject.name;
            if (n.Contains("_R") || n.Contains("Right"))     leftArm  = hand.gameObject;
            else if (n.Contains("_L") || n.Contains("Left")) rightArm = hand.gameObject;
        }
        foreach (var foot in robotContainer.GetComponentsInChildren<PlayerFootForRobot>(true))
        {
            string n = foot.gameObject.name;
            if (n.Contains("_R") || n.Contains("Right"))     leftLeg  = foot.gameObject;
            else if (n.Contains("_L") || n.Contains("Left")) rightLeg = foot.gameObject;
        }

        Debug.Log($"[Lobby] Resolved robot '{robotContainer.name}' | L.ARM→{(leftArm ? leftArm.name : "?")} " +
                  $"R.ARM→{(rightArm ? rightArm.name : "?")} L.LEG→{(leftLeg ? leftLeg.name : "?")} R.LEG→{(rightLeg ? rightLeg.name : "?")}");
    }

    private GameObject GetPlayerObject(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            if (NetworkManager.Singleton.LocalClient?.PlayerObject != null)
                return NetworkManager.Singleton.LocalClient.PlayerObject.gameObject;
        }

        foreach (var netObj in NetworkManager.Singleton.SpawnManager.SpawnedObjectsList)
        {
            if (netObj.IsPlayerObject && netObj.OwnerClientId == clientId)
                return netObj.gameObject;
        }

        return null;
    }
}