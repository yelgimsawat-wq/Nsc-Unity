using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// OnlineNetworkUI.cs
/// Unity 6 + Netcode for GameObjects + com.unity.services.multiplayer
///
/// Flow:
///   1. connectPanel — Host clicks "Create Room" or Client enters code and clicks "Join"
///   2. waitingPanel — Everyone waits in Lobby, player count updates real-time
///   3. Host clicks "Start" → LoadScene for all Clients simultaneously
/// </summary>
public class OnlineNetworkUI : NetworkBehaviour
{
    [Header("--- Connect Panel (Step 1) ---")]
    [SerializeField] private GameObject connectPanel;
    [SerializeField] private GameObject mainMenuGroup;
    [SerializeField] private GameObject modeSelectGroup;
    [SerializeField] private GameObject onlineSelectGroup;
    [SerializeField] private GameObject joinCodeGroup;
    [SerializeField] private Button playButton;
    [SerializeField] private Button onlineButton;
    [SerializeField] private Button offlineButton;
    [SerializeField] private Button exitButton;
    [SerializeField] private Button settingsButton;    // Opens Settings panel
    [SerializeField] private Button backButton;
    [SerializeField] private Button hostButton;        // Reused as Online -> Host
    [SerializeField] private Button joinButton;        // Reused as Online -> Join (open code input)
    [SerializeField] private Button joinConfirmButton; // Confirms Join by code
    [SerializeField] private TMP_InputField codeInputField;
    [SerializeField] private TextMeshProUGUI statusLabel;

    [Header("--- Waiting Panel (Step 2: Lobby) ---")]
    [SerializeField] private GameObject waitingPanel;
    [SerializeField] private TextMeshProUGUI codeDisplay;
    [SerializeField] private TextMeshProUGUI playerCountLabel;  // "Players: 2/4"
    [SerializeField] private Button startButton;                // Host only
    [SerializeField] private TextMeshProUGUI waitingLabel;      // "Waiting for Host..." (Visible to Client)
    [SerializeField] private Button copyCodeButton;
    [SerializeField] private Button leaveRoomButton;

    [Header("--- Map Selection (Host after Start) ---")]
    [SerializeField] private GameObject mapSelectPanel;
    [SerializeField] private Button bossMapButton;
    [SerializeField] private Button parkourMapButton;
    [SerializeField] private Button cancelMapSelectButton;
    [SerializeField] private string bossSceneName = "91626425186";
    [SerializeField] private string parkourSceneName = "MAP";

    [Header("--- Room Size Select (หลังกด Host) ---")]
    [Tooltip("แผงเลือกขนาดห้อง — เปิดขึ้นตอนกด Host ก่อนสร้างห้องจริง\n" +
             "ปล่อยว่าง = ข้ามขั้นนี้ สร้างห้องขนาดปกติเลย (พฤติกรรมเดิม)")]
    [SerializeField] private GameObject roomSizePanel;

    [Tooltip("ปุ่ม \"4 คน\" — ห้องโหมดปกติ (Boss/Parkour)")]
    [SerializeField] private Button room4Button;

    [Tooltip("ปุ่ม \"8 คน\" — ห้อง PVP กด Start แล้วเข้าฉาก PVP ตรงๆ ไม่ต้องเลือกแมพ")]
    [SerializeField] private Button room8Button;

    [SerializeField] private Button cancelRoomSizeButton;

    [Header("--- PVP Mode ---")]
    [Tooltip("ชื่อฉาก PVP (ต้องเปิดใน Build Settings)")]
    [SerializeField] private string pvpSceneName = "PVP";

    [Tooltip("ความจุห้อง PVP — 2 ทีม × 4 ชิ้นส่วน = 8")]
    [Min(2)] [SerializeField] private int maxPlayersPvp = 8;

    [Header("--- References ---")]
    [SerializeField] private Camera lobbyCam;

    [Header("--- Settings ---")]
    [Tooltip("ความจุห้องโหมดปกติ (Boss/Parkour) — โหมด PVP ใช้ maxPlayersPvp แทน")]
    [SerializeField] private int maxPlayers = 4;
    [Tooltip("Set to 1 for solo testing. Increase this only when testing multiplayer requirements.")]
    [Min(1)] [SerializeField] private int minimumPlayersToStart = 1;
    [SerializeField] private string nextSceneName = "91626425186"; // Scene name in Build Settings

    [Header("--- UI Animation ---")]
    [SerializeField] private float uiFadeDuration = 0.22f;
    [SerializeField] private float uiScaleFrom = 0.96f;
    [SerializeField] private Ease uiEase = Ease.OutCubic;

    [Header("--- Button Hover ---")]
    [SerializeField] private Color buttonHoverColor = new Color(0.2f, 0.85f, 0.3f, 1f);
    [SerializeField] private Color buttonPressedColor = new Color(0.1f, 0.65f, 0.2f, 1f);
    [SerializeField] private float buttonHoverFadeDuration = 0.12f;

    [Header("--- Menu Feel ---")]
    [SerializeField] private Transform menuLookTarget;
    [SerializeField] private bool menuFeelEnabled = true;
    [SerializeField] private float menuTiltMaxYaw = 7f;
    [SerializeField] private float menuTiltMaxPitch = 3f;
    [SerializeField] private float menuTiltMaxRoll = 1.5f;
    [SerializeField] private float menuTiltFollowSpeed = 7f;
    [SerializeField] private float menuIdleScaleAmount = 0.012f;
    [SerializeField] private float menuIdleSpeed = 1.6f;
    [SerializeField] private float buttonClickScale = 1.06f;
    [SerializeField] private float buttonClickDuration = 0.12f;

    // NetworkVariable: Server writes, everyone reads — Fixes count not updating on Client side
    private NetworkVariable<int> playerCount = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ความจุ + โหมดของห้องต้อง sync ให้ Client ด้วย ไม่งั้น Client จะโชว์ "Players: 2/4"
    // ในห้อง PVP ที่จริงๆ จุ 8 (Client อ่านค่าจากฟิลด์ของตัวเองซึ่งเป็นค่าโหมดปกติ)
    private NetworkVariable<int> roomCapacity = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<bool> roomIsPvp = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // จำไว้ตอนกดสร้างห้อง แล้วค่อยเขียนลง NetworkVariable ตอน spawn
    // (เขียนก่อน spawn ไม่ได้ — Netcode จะ throw)
    private int pendingRoomCapacity;
    private bool pendingRoomIsPvp;

    /// <summary>ความจุห้องที่ใช้โชว์ UI — ก่อน spawn ยังไม่มีค่า ให้ fallback เป็นค่าโหมดปกติ</summary>
    private int RoomCapacity => roomCapacity.Value > 0 ? roomCapacity.Value : maxPlayers;

    private ISession session;
    private bool servicesReady;
    // true ระหว่าง Host()/Join() กำลังทำงาน — กันกดซ้ำแล้วสอง attempt ทับกัน
    private bool isConnecting;
    private string currentRoomCode = string.Empty;
    // true = ผู้เล่นกด Leave เอง (UI ถูกจัดการใน OnLeaveRoomClicked แล้ว)
    // false = โดนตัดจากอีกฝั่ง เช่น Host ปิดห้อง → OnNetworkStopped ต้องพากลับเมนูเอง
    private bool intentionalLeave;
    private readonly Dictionary<GameObject, Tween> runningUiTweens = new Dictionary<GameObject, Tween>();
    private readonly Dictionary<Transform, Vector3> originalUiScales = new Dictionary<Transform, Vector3>();
    private readonly Dictionary<Transform, Tween> buttonClickTweens = new Dictionary<Transform, Tween>();
    private readonly Dictionary<Button, UnityAction> buttonClickFeedbackListeners = new Dictionary<Button, UnityAction>();
    private Quaternion menuTargetBaseRotation;
    private Vector3 menuTargetBaseScale;
    private bool menuTargetPoseCaptured;

    private enum ConnectState
    {
        MainMenu,
        ModeSelect,
        OnlineSelect,
        JoinCodeInput
    }

    private ConnectState currentConnectState = ConnectState.MainMenu;

    // ================================================================
    //  UNITY LIFECYCLE
    // ================================================================

    async void Start()
    {
        // ✅ ตรวจสอบว่า SettingsManager ยังอยู่ไหม ถ้าหายให้สร้างใหม่
        EnsureSettingsManagerExists();

        // Keep lobbyCam always on top — depth 100 beats any Player camera (default depth = -1)
        if (lobbyCam != null) lobbyCam.depth = 100;

        SetVisibleInstant(waitingPanel, false);
        SetVisibleInstant(mapSelectPanel, false);
        SetVisibleInstant(roomSizePanel, false);
        SetVisibleInstant(connectPanel, true);
        CaptureMenuFeelBasePose();

        BindConnectPanelButtons();
        BindWaitingPanelButtons();
        ApplyButtonHoverColors();
        SetConnectState(ConnectState.MainMenu);
        SetStatus("Connecting...");
        SetButtons(false);

        try
        {
            await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            servicesReady = true;
            SetStatus("Ready - Press Play");
            SetButtons(true);
        }
        catch (Exception e)
        {
            SetStatus("Failed to connect to Services: " + e.Message);
            Debug.LogError(e);
        }
    }

    private void Update()
    {
        UpdateMenuFeel();
    }

    public override void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientStopped -= OnNetworkStopped;

        UnbindConnectPanelButtons();
        UnbindWaitingPanelButtons();
        ClearButtonClickFeedback();
        KillAllButtonClickTweens();
        KillAllUiTweens();
        originalUiScales.Clear();

        base.OnDestroy();
    }

    // ================================================================
    //  NETWORK SPAWN — subscribe events + NetworkVariable
    // ================================================================

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        intentionalLeave = false;

        // ทุกเครื่อง (รวม Host) ฟังตอน "การเชื่อมต่อฝั่งเราหยุด" — ครอบคลุมทั้ง
        // Host กดออก, Host หลุด, และเน็ตหลุด → Client จะเด้งกลับหน้าเมนูอัตโนมัติ
        // (ห้าม unsubscribe ใน OnNetworkDespawn เพราะ despawn เกิดก่อน event นี้ยิง)
        NetworkManager.Singleton.OnClientStopped -= OnNetworkStopped;
        NetworkManager.Singleton.OnClientStopped += OnNetworkStopped;

        // All Clients listen to NetworkVariable to update UI
        playerCount.OnValueChanged += OnPlayerCountChanged;
        roomCapacity.OnValueChanged += OnRoomCapacityChanged;

        if (IsServer)
        {
            // Server subscribes to callbacks to count players
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

            // Initial count (Host is counted as 1)
            playerCount.Value = NetworkManager.Singleton.ConnectedClientsIds.Count;

            ApplyPendingRoomInfo();
        }

        // Update UI immediately with current value
        UpdatePlayerCountUI(playerCount.Value);
        RefreshStartButtonState();

        // Setup UI based on Role (Host/Client)
        SetupWaitingPanelRoles();
    }

    public override void OnNetworkDespawn()
    {
        playerCount.OnValueChanged -= OnPlayerCountChanged;
        roomCapacity.OnValueChanged -= OnRoomCapacityChanged;

        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        base.OnNetworkDespawn();
    }

    // ================================================================
    //  SERVER CALLBACKS — Update NetworkVariable<int>
    // ================================================================

    private void OnClientConnected(ulong clientId)
    {
        if (!IsServer) return;
        playerCount.Value = NetworkManager.Singleton.ConnectedClientsIds.Count;
        RefreshStartButtonState();
        Debug.Log($"[Server] Client {clientId} joined. Players: {playerCount.Value}/{maxPlayers}");
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (!IsServer) return;

        // Count directly, excluding the disconnecting client — whether NGO has removed it
        // from ConnectedClientsIds at callback time varies by version, so never assume -1.
        int count = 0;
        foreach (ulong id in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (id != clientId) count++;
        }
        playerCount.Value = count;
        RefreshStartButtonState();
        Debug.Log($"[Server] Client {clientId} disconnected. Players: {playerCount.Value}/{maxPlayers}");
    }

    // ================================================================
    //  NetworkVariable CALLBACK — Runs on all Clients when value changes
    // ================================================================

    private void OnPlayerCountChanged(int oldValue, int newValue)
    {
        UpdatePlayerCountUI(newValue);
        RefreshStartButtonState();
    }

    // ความจุมาถึง Client ทีหลัง playerCount ได้ — ต้องวาดป้ายใหม่ตอนมันมา
    // ไม่งั้น Client ค้างที่ "Players: 2/4" ทั้งที่ห้อง PVP จุ 8
    private void OnRoomCapacityChanged(int oldValue, int newValue)
    {
        UpdatePlayerCountUI(playerCount.Value);
    }

    private void UpdatePlayerCountUI(int count)
    {
        if (playerCountLabel != null)
            playerCountLabel.text = $"Players: {count}/{RoomCapacity}";
    }

    /// <summary>[SERVER] เขียนโหมด/ความจุห้องลง NetworkVariable — เรียกได้หลัง spawn เท่านั้น</summary>
    private void ApplyPendingRoomInfo()
    {
        if (!IsServer || !IsSpawned || pendingRoomCapacity <= 0) return;

        roomCapacity.Value = pendingRoomCapacity;
        roomIsPvp.Value = pendingRoomIsPvp;
    }

    // ================================================================
    //  Create Room (HOST)
    // ================================================================

    /// <summary>
    /// กด Host → เปิดแผงเลือกขนาดห้องก่อน ยังไม่สร้างห้องจริง
    /// (ความจุถูกล็อกตอน CreateSessionAsync เปลี่ยนทีหลังไม่ได้ จึงต้องถามก่อน)
    /// </summary>
    private void OnHostClicked()
    {
        if (isConnecting) return;

        // ยังไม่ได้ต่อแผงใน Inspector → ทำตัวเหมือนเดิม สร้างห้องขนาดปกติเลย
        if (roomSizePanel == null || (room4Button == null && room8Button == null))
        {
            _ = Host(false);
            return;
        }

        if (room4Button != null) room4Button.interactable = true;
        if (room8Button != null) room8Button.interactable = true;
        if (cancelRoomSizeButton != null) cancelRoomSizeButton.interactable = true;

        SetVisibleAnimated(roomSizePanel, true);
        SetStatus("Select room size: 4 players (Co-op) or 8 players (PVP)");
    }

    private void OnRoomSizeChosen(bool pvp)
    {
        if (isConnecting) return;

        SetVisibleAnimated(roomSizePanel, false);
        _ = Host(pvp);
    }

    private void OnCancelRoomSizeClicked()
    {
        if (isConnecting) return;

        SetVisibleAnimated(roomSizePanel, false);
        SetStatus("Room creation cancelled.");
    }

    /// <param name="pvp">true = ห้อง PVP (จุ 8 คน, กด Start แล้วเข้าฉาก PVP ตรงๆ)</param>
    async Task Host(bool pvp)
    {
        if (isConnecting) return;

        isConnecting = true;
        SetButtons(false);
        SetStatus(pvp ? "Creating PVP room..." : "Creating room...");

        // ความจุถูกล็อกตอน CreateSessionAsync เปลี่ยนทีหลังไม่ได้
        // จึงต้องรู้โหมดตั้งแต่ตอนนี้ — เป็นเหตุผลที่แยกปุ่มสร้างห้อง PVP ออกมา
        int capacity = pvp ? Mathf.Max(2, maxPlayersPvp) : Mathf.Max(1, maxPlayers);
        pendingRoomCapacity = capacity;
        pendingRoomIsPvp = pvp;

        try
        {
            // Never build a session on top of leftover Netcode state — see ResetNetworkStateAsync.
            await ResetNetworkStateAsync();

            var options = new SessionOptions { MaxPlayers = capacity }.WithRelayNetwork();
            session = await MultiplayerService.Instance.CreateSessionAsync(options);

            string code = session.Code;
            currentRoomCode = code;

            // Sessions API (WithRelayNetwork) starts the NetworkManager itself when the NGO
            // integration is present. If it did not, the transport has no relay data and a
            // manual StartHost() would bind to nothing — treat it as a failed attempt instead.
            if (!NetworkManager.Singleton.IsListening)
                throw new Exception("Session created but Netcode did not start.");

            ApplyPendingRoomInfo(); // เผื่อ spawn เสร็จไปแล้วระหว่างรอ await

            ShowWaitingPanel(code);
            SetStatus($"{(pvp ? "PVP room" : "Room")} created successfully! Code: " + code);
        }
        catch (Exception e)
        {
            await ResetNetworkStateAsync();
            currentRoomCode = string.Empty;
            SetStatus("Failed to create room: " + e.Message);
            SetButtons(true);
            Debug.LogError(e);
        }
        finally
        {
            isConnecting = false;
        }
    }

    // ================================================================
    //  Join Room (CLIENT)
    // ================================================================

    async Task Join()
    {
        string code = codeInputField != null ? codeInputField.text.Trim().ToUpperInvariant() : "";

        if (string.IsNullOrEmpty(code))
        {
            SetStatus("Please enter a Code");
            return;
        }

        if (isConnecting) return;

        isConnecting = true;
        SetButtons(false);
        SetStatus("Joining...");

        try
        {
            // A previous attempt that failed at "start NetworkManager" leaves NGO listening on a
            // relay allocation the Sessions SDK has already torn down. Joining again on top of it
            // makes the SDK log "NetworkManager is already connected", skip its own start, and
            // report success for a client that is not actually connected to the Host.
            await ResetNetworkStateAsync();

            session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
            currentRoomCode = code;

            // Same caveat as Host() — the SDK owns the start, so no manual StartClient().
            if (!NetworkManager.Singleton.IsListening)
                throw new Exception("Session joined but Netcode did not start.");

            ShowWaitingPanel(code);
            SetStatus("Joined successfully! Waiting for Host to start...");
        }
        catch (Exception e)
        {
            await ResetNetworkStateAsync();
            currentRoomCode = string.Empty;
            SetStatus("Failed to join: " + e.Message);
            SetButtons(true);
            Debug.LogError(e);
        }
        finally
        {
            isConnecting = false;
        }
    }

    // ================================================================
    //  WAITING PANEL
    // ================================================================

    private void ShowWaitingPanel(string roomCode)
    {
        ShowConnectPanelAsBackdrop();
        HideConnectFlowControlsForWaiting();
        SetVisibleAnimated(waitingPanel, true);
        currentRoomCode = roomCode;

        if (codeDisplay != null)
        {
            codeDisplay.text = "Room Code: " + roomCode;
            codeDisplay.gameObject.SetActive(true);
        }

        if (copyCodeButton != null) copyCodeButton.interactable = true;
        if (leaveRoomButton != null) leaveRoomButton.interactable = true;
        if (backButton != null) backButton.gameObject.SetActive(false);

        RefreshStartButtonState();
    }

    /// <summary>
    /// Set up UI based on Role after Network Spawn
    /// Host sees Start button / Client sees "Waiting for Host..."
    /// </summary>
    private void SetupWaitingPanelRoles()
    {
        SetVisibleInstant(mapSelectPanel, false);

        if (startButton != null)
        {
            startButton.gameObject.SetActive(IsServer); // Client will not see this button

            if (IsServer)
            {
                startButton.onClick.RemoveAllListeners();
                startButton.onClick.AddListener(OnStartButtonClicked);
                ApplyButtonHoverColor(startButton);
            }
        }

        if (waitingLabel != null)
            waitingLabel.gameObject.SetActive(!IsServer); // Client sees / Host does not see

        if (copyCodeButton != null)
            copyCodeButton.gameObject.SetActive(true);

        if (leaveRoomButton != null)
            leaveRoomButton.gameObject.SetActive(true);

        RefreshStartButtonState();
    }

    // ================================================================
    //  Start Game — Host only
    // ================================================================

    private void OnStartButtonClicked()
    {
        if (!IsServer) return;
        if (!CanStartGame())
        {
            int connectedPlayers = NetworkManager.Singleton != null
                ? NetworkManager.Singleton.ConnectedClientsIds.Count
                : 0;
            SetStatus($"Cannot start yet. Need at least {minimumPlayersToStart} player(s). Current: {connectedPlayers}.");
            return;
        }

        // ห้อง PVP เลือกโหมดไปตั้งแต่ตอนสร้างห้องแล้ว → ไม่ต้องถามแมพซ้ำ เข้าเลย
        if (roomIsPvp.Value)
        {
            if (startButton != null) startButton.interactable = false;
            StartSelectedMap(pvpSceneName, "PVP");
            return;
        }

        if (mapSelectPanel == null || bossMapButton == null || parkourMapButton == null)
        {
            SetStatus("Map Select UI is not assigned. Please assign the panel and both map buttons.");
            return;
        }

        if (startButton != null) startButton.interactable = false;
        // Host/Join temporarily disables every menu button while connecting.
        // Re-enable the map choices explicitly when this panel is opened.
        bossMapButton.interactable = true;
        parkourMapButton.interactable = true;
        if (cancelMapSelectButton != null) cancelMapSelectButton.interactable = true;
        SetVisibleAnimated(mapSelectPanel, true);
        SetStatus("Select Map: Boss or Parkour");
    }

    private void OnBossMapClicked()
    {
        StartSelectedMap(bossSceneName, "Boss");
    }

    private void OnParkourMapClicked()
    {
        StartSelectedMap(parkourSceneName, "Parkour");
    }

    private void OnCancelMapSelectClicked()
    {
        if (!IsServer) return;
        SetVisibleAnimated(mapSelectPanel, false);
        RefreshStartButtonState();
        SetStatus("Map selection cancelled.");
    }

    private void StartSelectedMap(string sceneName, string mapDisplayName)
    {
        if (!IsServer || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening) return;

        if (string.IsNullOrWhiteSpace(sceneName) || !Application.CanStreamedLevelBeLoaded(sceneName))
        {
            SetStatus($"Cannot load {mapDisplayName}: Scene '{sceneName}' is not enabled in Build Settings.");
            RefreshStartButtonState();
            return;
        }

        SetVisibleAnimated(mapSelectPanel, false);
        if (bossMapButton != null) bossMapButton.interactable = false;
        if (parkourMapButton != null) parkourMapButton.interactable = false;
        if (cancelMapSelectButton != null) cancelMapSelectButton.interactable = false;

        if (lobbyCam != null) lobbyCam.gameObject.SetActive(false);

        // Load Scene for all Clients simultaneously via Netcode SceneManager
        NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);

        Debug.Log($"[Server] Loading {mapDisplayName} Scene: {sceneName}");
    }

    // ================================================================
    //  Helpers
    // ================================================================

    void SetStatus(string msg)
    {
        if (statusLabel != null) statusLabel.text = msg;
        Debug.Log("[Network] " + msg);
    }

    void SetButtons(bool on)
    {
        if (playButton != null) playButton.interactable = on;
        if (onlineButton != null) onlineButton.interactable = on;
        if (offlineButton != null) offlineButton.interactable = on;
        if (exitButton != null) exitButton.interactable = on;
        if (settingsButton != null) settingsButton.interactable = on;
        if (backButton != null) backButton.interactable = on;
        if (hostButton != null) hostButton.interactable = on;
        if (room4Button != null) room4Button.interactable = on;
        if (room8Button != null) room8Button.interactable = on;
        if (cancelRoomSizeButton != null) cancelRoomSizeButton.interactable = on;
        if (joinButton != null) joinButton.interactable = on;
        if (joinConfirmButton != null) joinConfirmButton.interactable = on;
        if (codeInputField != null) codeInputField.interactable = on;
        if (copyCodeButton != null) copyCodeButton.interactable = on;
        if (leaveRoomButton != null) leaveRoomButton.interactable = on;
        if (bossMapButton != null) bossMapButton.interactable = on;
        if (parkourMapButton != null) parkourMapButton.interactable = on;
        if (cancelMapSelectButton != null) cancelMapSelectButton.interactable = on;
    }

    private void ApplyButtonHoverColors()
    {
        ApplyButtonHoverColor(playButton);
        ApplyButtonHoverColor(onlineButton);
        ApplyButtonHoverColor(offlineButton);
        ApplyButtonHoverColor(exitButton);
        ApplyButtonHoverColor(settingsButton);
        ApplyButtonHoverColor(backButton);
        ApplyButtonHoverColor(hostButton);
        ApplyButtonHoverColor(room4Button);
        ApplyButtonHoverColor(room8Button);
        ApplyButtonHoverColor(cancelRoomSizeButton);
        ApplyButtonHoverColor(joinButton);
        ApplyButtonHoverColor(joinConfirmButton);
        ApplyButtonHoverColor(startButton);
        ApplyButtonHoverColor(copyCodeButton);
        ApplyButtonHoverColor(leaveRoomButton);
        ApplyButtonHoverColor(bossMapButton);
        ApplyButtonHoverColor(parkourMapButton);
        ApplyButtonHoverColor(cancelMapSelectButton);
    }

    private void ApplyButtonHoverColor(Button button)
    {
        if (button == null) return;

        button.transition = Selectable.Transition.ColorTint;

        ColorBlock colors = button.colors;
        colors.highlightedColor = buttonHoverColor;
        colors.selectedColor = buttonHoverColor;
        colors.pressedColor = buttonPressedColor;
        colors.fadeDuration = Mathf.Max(0f, buttonHoverFadeDuration);
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
        foreach (KeyValuePair<Button, UnityAction> listener in buttonClickFeedbackListeners)
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
        foreach (KeyValuePair<Transform, Tween> clickTween in buttonClickTweens)
        {
            if (clickTween.Value != null && clickTween.Value.IsActive())
                clickTween.Value.Kill(false);

            if (clickTween.Key != null)
                clickTween.Key.localScale = GetOriginalScale(clickTween.Key);
        }

        buttonClickTweens.Clear();
    }

    private void CaptureMenuFeelBasePose()
    {
        if (menuLookTarget == null || menuTargetPoseCaptured) return;

        menuTargetBaseRotation = menuLookTarget.localRotation;
        menuTargetBaseScale = menuLookTarget.localScale;
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
            float screenWidth = Mathf.Max(1f, Screen.width);
            float screenHeight = Mathf.Max(1f, Screen.height);
            float normalizedX = Mathf.Clamp(((mousePosition.x / screenWidth) - 0.5f) * 2f, -1f, 1f);
            float normalizedY = Mathf.Clamp(((mousePosition.y / screenHeight) - 0.5f) * 2f, -1f, 1f);

            Quaternion mouseTilt = Quaternion.Euler(
                -normalizedY * menuTiltMaxPitch,
                normalizedX * menuTiltMaxYaw,
                -normalizedX * menuTiltMaxRoll);

            float idleScale = 1f + Mathf.Sin(Time.unscaledTime * menuIdleSpeed) * menuIdleScaleAmount;
            targetRotation = menuTargetBaseRotation * mouseTilt;
            targetScale = menuTargetBaseScale * idleScale;
        }

        menuLookTarget.localRotation = Quaternion.Slerp(menuLookTarget.localRotation, targetRotation, followT);
        menuLookTarget.localScale = Vector3.Lerp(menuLookTarget.localScale, targetScale, followT);
    }

    private bool ShouldReactToMenuMouse()
    {
        if (!menuFeelEnabled || connectPanel == null || !connectPanel.activeInHierarchy)
            return false;

        return waitingPanel == null || !waitingPanel.activeInHierarchy;
    }

    private void ShowConnectPanelAsBackdrop()
    {
        if (connectPanel == null) return;

        KillUiTween(connectPanel);

        CanvasGroup canvasGroup = GetOrAddCanvasGroup(connectPanel);
        Transform targetTransform = connectPanel.transform;
        Vector3 baseScale = GetOriginalScale(targetTransform);

        connectPanel.SetActive(true);
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        targetTransform.localScale = baseScale;
    }

    private void HideConnectFlowControlsForWaiting()
    {
        SetVisibleAnimated(mainMenuGroup, false);
        SetVisibleAnimated(modeSelectGroup, false);
        SetVisibleAnimated(onlineSelectGroup, false);
        SetVisibleAnimated(joinCodeGroup, false);

        if (joinConfirmButton != null)
            joinConfirmButton.gameObject.SetActive(false);

        if (codeInputField != null)
            codeInputField.gameObject.SetActive(false);

        if (hostButton != null)
            hostButton.gameObject.SetActive(false);

        SetVisibleInstant(roomSizePanel, false);

        if (joinButton != null)
            joinButton.gameObject.SetActive(false);

        if (backButton != null)
            backButton.gameObject.SetActive(false);
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

    private void BindConnectPanelButtons()
    {
        if (playButton != null)
        {
            playButton.onClick.RemoveListener(OnPlayClicked);
            playButton.onClick.AddListener(OnPlayClicked);
        }

        if (onlineButton != null)
        {
            onlineButton.onClick.RemoveListener(OnOnlineModeClicked);
            onlineButton.onClick.AddListener(OnOnlineModeClicked);
        }

        if (offlineButton != null)
        {
            offlineButton.onClick.RemoveListener(OnOfflineModeClicked);
            offlineButton.onClick.AddListener(OnOfflineModeClicked);
        }

        if (exitButton != null)
        {
            exitButton.onClick.RemoveListener(OnExitOrBackClicked);
            exitButton.onClick.AddListener(OnExitOrBackClicked);
        }

        if (settingsButton != null)
        {
            settingsButton.onClick.RemoveListener(OnSettingsClicked);
            settingsButton.onClick.AddListener(OnSettingsClicked);
        }

        if (backButton != null)
        {
            backButton.onClick.RemoveListener(OnExitOrBackClicked);
            backButton.onClick.AddListener(OnExitOrBackClicked);
        }

        if (hostButton != null)
        {
            hostButton.onClick.RemoveAllListeners();
            hostButton.onClick.AddListener(OnHostClicked);
        }

        if (room4Button != null)
        {
            room4Button.onClick.RemoveAllListeners();
            room4Button.onClick.AddListener(() => OnRoomSizeChosen(false));
        }

        if (room8Button != null)
        {
            room8Button.onClick.RemoveAllListeners();
            room8Button.onClick.AddListener(() => OnRoomSizeChosen(true));
        }

        if (cancelRoomSizeButton != null)
        {
            cancelRoomSizeButton.onClick.RemoveAllListeners();
            cancelRoomSizeButton.onClick.AddListener(OnCancelRoomSizeClicked);
        }

        if (joinButton != null)
        {
            joinButton.onClick.RemoveAllListeners();
            joinButton.onClick.AddListener(OnJoinFlowClicked);
        }

        if (joinConfirmButton != null)
        {
            joinConfirmButton.onClick.RemoveAllListeners();
            joinConfirmButton.onClick.AddListener(() => _ = Join());
        }
    }

    private void BindWaitingPanelButtons()
    {
        if (copyCodeButton != null)
        {
            copyCodeButton.onClick.RemoveListener(OnCopyCodeClicked);
            copyCodeButton.onClick.AddListener(OnCopyCodeClicked);
        }

        if (leaveRoomButton != null)
        {
            leaveRoomButton.onClick.RemoveListener(OnLeaveRoomClicked);
            leaveRoomButton.onClick.AddListener(OnLeaveRoomClicked);
        }

        if (bossMapButton != null)
        {
            bossMapButton.onClick.RemoveListener(OnBossMapClicked);
            bossMapButton.onClick.AddListener(OnBossMapClicked);
        }

        if (parkourMapButton != null)
        {
            parkourMapButton.onClick.RemoveListener(OnParkourMapClicked);
            parkourMapButton.onClick.AddListener(OnParkourMapClicked);
        }

        if (cancelMapSelectButton != null)
        {
            cancelMapSelectButton.onClick.RemoveListener(OnCancelMapSelectClicked);
            cancelMapSelectButton.onClick.AddListener(OnCancelMapSelectClicked);
        }

    }

    private void UnbindConnectPanelButtons()
    {
        if (playButton != null) playButton.onClick.RemoveListener(OnPlayClicked);
        if (onlineButton != null) onlineButton.onClick.RemoveListener(OnOnlineModeClicked);
        if (offlineButton != null) offlineButton.onClick.RemoveListener(OnOfflineModeClicked);
        if (exitButton != null) exitButton.onClick.RemoveListener(OnExitOrBackClicked);
        if (settingsButton != null) settingsButton.onClick.RemoveListener(OnSettingsClicked);
        if (backButton != null) backButton.onClick.RemoveListener(OnExitOrBackClicked);
        if (hostButton != null) hostButton.onClick.RemoveAllListeners();
        if (joinButton != null) joinButton.onClick.RemoveAllListeners();
        if (joinConfirmButton != null) joinConfirmButton.onClick.RemoveAllListeners();
    }

    private void UnbindWaitingPanelButtons()
    {
        if (copyCodeButton != null) copyCodeButton.onClick.RemoveListener(OnCopyCodeClicked);
        if (leaveRoomButton != null) leaveRoomButton.onClick.RemoveListener(OnLeaveRoomClicked);
        if (bossMapButton != null) bossMapButton.onClick.RemoveListener(OnBossMapClicked);
        if (parkourMapButton != null) parkourMapButton.onClick.RemoveListener(OnParkourMapClicked);
        if (cancelMapSelectButton != null) cancelMapSelectButton.onClick.RemoveListener(OnCancelMapSelectClicked);
    }

    private void OnPlayClicked()
    {
        if (!servicesReady)
        {
            SetStatus("Still connecting to Services...");
            return;
        }

        SetConnectState(ConnectState.ModeSelect);
    }

    private void OnOnlineModeClicked()
    {
        if (!servicesReady)
        {
            SetStatus("Services are not ready yet.");
            return;
        }

        SetConnectState(ConnectState.OnlineSelect);
    }

    private void OnOfflineModeClicked()
    {
        SetStatus("Offline mode is not available yet.");
    }

    private void OnSettingsClicked()
    {
        if (SettingsManager.Instance != null)
        {
            SettingsManager.Instance.OpenSettings();
        }
        else
        {
            SetStatus("Settings Manager not found!");
            Debug.LogWarning("[OnlineNetworkUI] SettingsManager.Instance is null. Make sure SettingsManager is in the scene.");
        }
    }

    private void OnJoinFlowClicked()
    {
        SetConnectState(ConnectState.JoinCodeInput);
    }

    private void OnExitOrBackClicked()
    {
        switch (currentConnectState)
        {
            case ConnectState.JoinCodeInput:
                SetConnectState(ConnectState.OnlineSelect);
                break;
            case ConnectState.OnlineSelect:
                SetConnectState(ConnectState.ModeSelect);
                break;
            case ConnectState.ModeSelect:
                SetConnectState(ConnectState.MainMenu);
                break;
            default:
                SetStatus("Exiting game...");
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
                break;
        }
    }

    private void SetConnectState(ConnectState state)
    {
        currentConnectState = state;

        bool showMain = state == ConnectState.MainMenu;
        bool showMode = state == ConnectState.ModeSelect;
        bool showOnline = state == ConnectState.OnlineSelect;
        bool showJoinCode = state == ConnectState.JoinCodeInput;

        SetVisibleAnimated(mainMenuGroup, showMain);
        SetVisibleAnimated(modeSelectGroup, showMode);
        SetVisibleAnimated(onlineSelectGroup, showOnline);
        SetVisibleAnimated(joinCodeGroup, showJoinCode);

        if (joinConfirmButton != null)
            joinConfirmButton.gameObject.SetActive(showJoinCode);

        if (codeInputField != null)
            codeInputField.gameObject.SetActive(showJoinCode);

        // Fallback for old one-screen layout: keep host/join buttons synced with online step
        if (hostButton != null) hostButton.gameObject.SetActive(showOnline);
        if (!showOnline) SetVisibleInstant(roomSizePanel, false);
        if (joinButton != null) joinButton.gameObject.SetActive(showOnline);

        if (backButton != null) backButton.gameObject.SetActive(!showMain);

        string statusMessage = state switch
        {
            ConnectState.MainMenu => servicesReady ? "Ready - Press Play" : "Connecting...",
            ConnectState.ModeSelect => "Select Mode",
            ConnectState.OnlineSelect => "Online: choose Host or Join",
            ConnectState.JoinCodeInput => "Enter Room Code to Join",
            _ => "Ready"
        };

        SetStatus(statusMessage);
    }

    private void OnCopyCodeClicked()
    {
        if (string.IsNullOrWhiteSpace(currentRoomCode))
        {
            SetStatus("No room code available.");
            return;
        }

        GUIUtility.systemCopyBuffer = currentRoomCode;
        SetStatus("Copied room code: " + currentRoomCode);
    }

    private async void OnLeaveRoomClicked()
    {
        // บอก OnNetworkStopped ว่านี่คือการออกโดยตั้งใจ จะได้ไม่จัดการ UI ซ้ำ
        intentionalLeave = true;

        // Host: Shutdown ตัดการเชื่อมต่อ → Client ทุกเครื่องจะได้ event OnClientStopped
        // ของฝั่งตัวเองแล้วเด้งกลับเมนูอัตโนมัติ (ดู OnNetworkStopped)
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();

        await LeaveSessionAsync();

        ReturnToMainMenu("Returned to Connect Panel.");
    }

    /// <summary>
    /// เรียกเมื่อการเชื่อมต่อ Netcode ฝั่งเราหยุดลง — ไม่ว่าจะเพราะ Host ปิดห้อง,
    /// Host หลุด, หรือเน็ตเราหลุดเอง ถ้าไม่ใช่การกด Leave เองให้พากลับหน้าเมนู
    /// </summary>
    private void OnNetworkStopped(bool wasHost)
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientStopped -= OnNetworkStopped;

        if (intentionalLeave)
        {
            intentionalLeave = false;
            return; // OnLeaveRoomClicked จัดการ UI เองแล้ว
        }

        // โดนตัดจากอีกฝั่ง — เคลียร์ session บน Services แล้วกลับเมนู
        _ = LeaveSessionAsync();
        ReturnToMainMenu(wasHost ? "Room closed." : "Disconnected: Host closed the room.");
    }

    /// <summary>
    /// ออกจาก Session บน Unity Services ให้เรียบร้อย
    /// Host = ลบห้องทิ้ง (คนอื่น join ต่อไม่ได้) / Client = ออกจากห้องเฉย ๆ
    /// </summary>
    private async Task LeaveSessionAsync()
    {
        if (session == null) return;

        ISession leavingSession = session;
        session = null; // กันเรียกซ้ำระหว่าง await

        try
        {
            if (leavingSession.IsHost)
                await leavingSession.AsHost().DeleteAsync();
            else
                await leavingSession.LeaveAsync();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Network] Failed to leave session cleanly: " + e.Message);
        }
    }

    /// <summary>
    /// เคลียร์สถานะเน็ตเวิร์กให้สะอาดก่อนเริ่ม Host/Join ครั้งใหม่ และหลังจาก attempt ที่ล้มเหลว
    /// ถ้าไม่ทำ NetworkManager ที่ค้างอยู่จาก attempt เก่าจะทำให้ Sessions SDK ข้ามการ start
    /// (ขึ้น log "NetworkManager is already connected") แล้วได้ Client ที่ไม่ได้ต่อกับ Host จริง
    /// </summary>
    private async Task ResetNetworkStateAsync()
    {
        await LeaveSessionAsync();

        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || (!nm.IsListening && !nm.ShutdownInProgress)) return;

        // Shutdown ที่นี่เป็นการล้างของเราเอง — อย่าให้ OnNetworkStopped พา UI กลับเมนูซ้ำ
        intentionalLeave = true;

        if (!nm.ShutdownInProgress) nm.Shutdown();

        // NGO ปิดจริงตอนท้ายเฟรม — รอจนปิดเสร็จก่อน ไม่งั้น attempt ถัดไปเจอสถานะค้างอีก
        for (int i = 0; i < 300 && (nm.IsListening || nm.ShutdownInProgress); i++)
            await Task.Yield();

        if (nm.IsListening)
            Debug.LogWarning("[Network] NetworkManager did not shut down in time before the next attempt.");
    }

    /// <summary>
    /// รีเซ็ต UI กลับหน้าเมนูหลัก (ใช้ทั้งตอนกด Leave เองและตอนโดน Host ปิดห้อง)
    /// </summary>
    private void ReturnToMainMenu(string statusMessage)
    {
        currentRoomCode = string.Empty;

        if (codeInputField != null)
            codeInputField.text = string.Empty;

        SetVisibleAnimated(waitingPanel, false);
        SetVisibleInstant(mapSelectPanel, false);
        SetVisibleAnimated(connectPanel, true);

        SetConnectState(ConnectState.MainMenu);
        SetButtons(servicesReady);
        SetStatus(statusMessage);
    }

    private bool CanStartGame()
    {
        if (!IsServer || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            return false;

        int requiredPlayers = Mathf.Clamp(minimumPlayersToStart, 1, Mathf.Max(1, RoomCapacity));
        return NetworkManager.Singleton.ConnectedClientsIds.Count >= requiredPlayers;
    }

    private void RefreshStartButtonState()
    {
        if (startButton == null) return;
        startButton.interactable = CanStartGame();
    }

    // ================================================================
    //  SETTINGS MANAGER AUTO-CREATE
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
            Debug.LogWarning("[OnlineNetworkUI] SettingsManager.Instance is null! " +
                "Place the SettingsManager prefab in this scene — an auto-created instance would have no UI references.");
        }
    }
}
