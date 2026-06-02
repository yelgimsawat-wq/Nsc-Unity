using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

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

    [Header("--- References ---")]
    [SerializeField] private Camera lobbyCam;

    [Header("--- Settings ---")]
    [SerializeField] private int maxPlayers = 4;
    [SerializeField] private string nextSceneName = "SelectPart"; // Scene name in Build Settings

    // NetworkVariable: Server writes, everyone reads — Fixes count not updating on Client side
    private NetworkVariable<int> playerCount = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private ISession session;
    private bool servicesReady;
    private string currentRoomCode = string.Empty;

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
        if (waitingPanel != null) waitingPanel.SetActive(false);
        if (connectPanel != null) connectPanel.SetActive(true);

        BindConnectPanelButtons();
        BindWaitingPanelButtons();
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

    private void OnDestroy()
    {
        UnbindConnectPanelButtons();
        UnbindWaitingPanelButtons();
    }

    // ================================================================
    //  NETWORK SPAWN — subscribe events + NetworkVariable
    // ================================================================

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // All Clients listen to NetworkVariable to update UI
        playerCount.OnValueChanged += OnPlayerCountChanged;

        if (IsServer)
        {
            // Server subscribes to callbacks to count players
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

            // Initial count (Host is counted as 1)
            playerCount.Value = NetworkManager.Singleton.ConnectedClientsIds.Count;
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

        // ConnectedClientsIds still counts the disconnecting client, so we subtract 1
        int count = NetworkManager.Singleton.ConnectedClientsIds.Count - 1;
        playerCount.Value = Mathf.Max(0, count);
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

    private void UpdatePlayerCountUI(int count)
    {
        if (playerCountLabel != null)
            playerCountLabel.text = $"Players: {count}/{maxPlayers}";
    }

    // ================================================================
    //  Create Room (HOST)
    // ================================================================

    async Task Host()
    {
        SetButtons(false);
        SetStatus("Creating room...");

        try
        {
            var options = new SessionOptions { MaxPlayers = maxPlayers }.WithRelayNetwork();
            session = await MultiplayerService.Instance.CreateSessionAsync(options);

            string code = session.Code;
            currentRoomCode = code;

            NetworkManager.Singleton.StartHost();

            ShowWaitingPanel(code);
            SetStatus("Room created successfully! Code: " + code);
        }
        catch (Exception e)
        {
            SetStatus("Failed to create room: " + e.Message);
            SetButtons(true);
            Debug.LogError(e);
        }
    }

    // ================================================================
    //  Join Room (CLIENT)
    // ================================================================

    async Task Join()
    {
        string code = codeInputField != null ? codeInputField.text.Trim().ToUpper() : "";

        if (string.IsNullOrEmpty(code))
        {
            SetStatus("Please enter a Code");
            return;
        }

        SetButtons(false);
        SetStatus("Joining...");

        try
        {
            session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
            currentRoomCode = code;

            NetworkManager.Singleton.StartClient();

            ShowWaitingPanel(code);
            SetStatus("Joined successfully! Waiting for Host to start...");
        }
        catch (Exception e)
        {
            SetStatus("Failed to join: " + e.Message);
            SetButtons(true);
            Debug.LogError(e);
        }
    }

    // ================================================================
    //  WAITING PANEL
    // ================================================================

    private void ShowWaitingPanel(string roomCode)
    {
        if (connectPanel != null) connectPanel.SetActive(false);
        if (waitingPanel != null) waitingPanel.SetActive(true);
        currentRoomCode = roomCode;

        if (codeDisplay != null)
        {
            codeDisplay.text = "Room Code: " + roomCode;
            codeDisplay.gameObject.SetActive(true);
        }

        RefreshStartButtonState();
    }

    /// <summary>
    /// Set up UI based on Role after Network Spawn
    /// Host sees Start button / Client sees "Waiting for Host..."
    /// </summary>
    private void SetupWaitingPanelRoles()
    {
        if (startButton != null)
        {
            startButton.gameObject.SetActive(IsServer); // Client will not see this button

            if (IsServer)
            {
                startButton.onClick.RemoveAllListeners();
                startButton.onClick.AddListener(OnStartButtonClicked);
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
            SetStatus("Cannot start yet. Need exactly 4 players.");
            return;
        }

        if (startButton != null) startButton.interactable = false; // Prevent double-clicking

        if (lobbyCam != null) lobbyCam.gameObject.SetActive(false);

        // Load Scene for all Clients simultaneously via Netcode SceneManager
        NetworkManager.Singleton.SceneManager.LoadScene(nextSceneName, LoadSceneMode.Single);

        Debug.Log($"[Server] Loading Scene: {nextSceneName}");
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
        if (backButton != null) backButton.interactable = on;
        if (hostButton != null) hostButton.interactable = on;
        if (joinButton != null) joinButton.interactable = on;
        if (joinConfirmButton != null) joinConfirmButton.interactable = on;
        if (codeInputField != null) codeInputField.interactable = on;
        if (copyCodeButton != null) copyCodeButton.interactable = on;
        if (leaveRoomButton != null) leaveRoomButton.interactable = on;
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

        if (backButton != null)
        {
            backButton.onClick.RemoveListener(OnExitOrBackClicked);
            backButton.onClick.AddListener(OnExitOrBackClicked);
        }

        if (hostButton != null)
        {
            hostButton.onClick.RemoveAllListeners();
            hostButton.onClick.AddListener(() => _ = Host());
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

    }

    private void UnbindConnectPanelButtons()
    {
        if (playButton != null) playButton.onClick.RemoveListener(OnPlayClicked);
        if (onlineButton != null) onlineButton.onClick.RemoveListener(OnOnlineModeClicked);
        if (offlineButton != null) offlineButton.onClick.RemoveListener(OnOfflineModeClicked);
        if (exitButton != null) exitButton.onClick.RemoveListener(OnExitOrBackClicked);
        if (backButton != null) backButton.onClick.RemoveListener(OnExitOrBackClicked);
        if (hostButton != null) hostButton.onClick.RemoveAllListeners();
        if (joinButton != null) joinButton.onClick.RemoveAllListeners();
        if (joinConfirmButton != null) joinConfirmButton.onClick.RemoveAllListeners();
    }

    private void UnbindWaitingPanelButtons()
    {
        if (copyCodeButton != null) copyCodeButton.onClick.RemoveListener(OnCopyCodeClicked);
        if (leaveRoomButton != null) leaveRoomButton.onClick.RemoveListener(OnLeaveRoomClicked);
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
                Application.Quit();
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

        if (mainMenuGroup != null) mainMenuGroup.SetActive(showMain);
        if (modeSelectGroup != null) modeSelectGroup.SetActive(showMode);
        if (onlineSelectGroup != null) onlineSelectGroup.SetActive(showOnline);
        if (joinCodeGroup != null) joinCodeGroup.SetActive(showJoinCode);

        if (joinConfirmButton != null)
            joinConfirmButton.gameObject.SetActive(showJoinCode);

        if (codeInputField != null)
            codeInputField.gameObject.SetActive(showJoinCode);

        // Fallback for old one-screen layout: keep host/join buttons synced with online step
        if (hostButton != null) hostButton.gameObject.SetActive(showOnline);
        if (joinButton != null) joinButton.gameObject.SetActive(showOnline);

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

    private void OnLeaveRoomClicked()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();

        session = null;
        currentRoomCode = string.Empty;

        if (codeInputField != null)
            codeInputField.text = string.Empty;

        if (waitingPanel != null) waitingPanel.SetActive(false);
        if (connectPanel != null) connectPanel.SetActive(true);

        SetConnectState(ConnectState.MainMenu);
        SetButtons(servicesReady);
        SetStatus("Returned to Connect Panel.");
    }

    private bool CanStartGame()
    {
        return IsServer && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    }

    private void RefreshStartButtonState()
    {
        if (startButton == null) return;
        startButton.interactable = CanStartGame();
    }
}
