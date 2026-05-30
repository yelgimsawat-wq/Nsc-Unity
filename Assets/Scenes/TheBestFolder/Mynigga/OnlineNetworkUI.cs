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
    [SerializeField] private Button hostButton;
    [SerializeField] private Button joinButton;
    [SerializeField] private TMP_InputField codeInputField;
    [SerializeField] private TextMeshProUGUI statusLabel;

    [Header("--- Waiting Panel (Step 2: Lobby) ---")]
    [SerializeField] private GameObject waitingPanel;
    [SerializeField] private TextMeshProUGUI codeDisplay;
    [SerializeField] private TextMeshProUGUI playerCountLabel;  // "Players: 2/4"
    [SerializeField] private Button startButton;                // Host only
    [SerializeField] private TextMeshProUGUI waitingLabel;      // "Waiting for Host..." (Visible to Client)

    [Header("--- References ---")]
    [SerializeField] private Camera lobbyCam;

    [Header("--- Settings ---")]
    [SerializeField] private int maxPlayers = 4;
    [SerializeField] private string nextSceneName = "SelectPart"; // Scene name in Build Settings

    // NetworkVariable: Server writes, everyone reads — Fixes count not updating on Client side
    private NetworkVariable<int> playerCount = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private ISession session;

    // ================================================================
    //  UNITY LIFECYCLE
    // ================================================================

    async void Start()
    {
        if (waitingPanel != null) waitingPanel.SetActive(false);
        if (connectPanel != null) connectPanel.SetActive(true);

        SetStatus("Connecting...");
        SetButtons(false);

        try
        {
            await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            SetStatus("Ready");
            SetButtons(true);

            hostButton.onClick.AddListener(() => _ = Host());
            joinButton.onClick.AddListener(() => _ = Join());
        }
        catch (Exception e)
        {
            SetStatus("Failed to connect to Services: " + e.Message);
            Debug.LogError(e);
        }
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
        Debug.Log($"[Server] Client {clientId} joined. Players: {playerCount.Value}/{maxPlayers}");
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (!IsServer) return;

        // ConnectedClientsIds still counts the disconnecting client, so we subtract 1
        int count = NetworkManager.Singleton.ConnectedClientsIds.Count - 1;
        playerCount.Value = Mathf.Max(0, count);
        Debug.Log($"[Server] Client {clientId} disconnected. Players: {playerCount.Value}/{maxPlayers}");
    }

    // ================================================================
    //  NetworkVariable CALLBACK — Runs on all Clients when value changes
    // ================================================================

    private void OnPlayerCountChanged(int oldValue, int newValue)
    {
        UpdatePlayerCountUI(newValue);
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

        if (codeDisplay != null)
        {
            codeDisplay.text = "Room Code: " + roomCode;
            codeDisplay.gameObject.SetActive(true);
        }
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
    }

    // ================================================================
    //  Start Game — Host only
    // ================================================================

    private void OnStartButtonClicked()
    {
        if (!IsServer) return;

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
        if (hostButton != null) hostButton.interactable = on;
        if (joinButton != null) joinButton.interactable = on;
    }
}
