using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// OnlineNetworkUI.cs
/// Designed for Unity 6 + com.unity.services.multiplayer
/// 
/// Usage:
///   - Host: Click "Create Room" → Get a 6-digit Code → Send to friends.
///   - Client: Enter the Code and click "Join".
/// </summary>
public class OnlineNetworkUI : MonoBehaviour
{
    [Header("--- UI Elements ---")]
    [SerializeField] private GameObject connectPanel;       // Main panel containing all buttons
    [SerializeField] private Button hostButton;             // "Create Room" button
    [SerializeField] private Button joinButton;             // "Join Room" button
    [SerializeField] private TMP_InputField codeInputField; // Input field for the join code
    [SerializeField] private TextMeshProUGUI codeDisplay;   // Displays the host's code for copying
    [SerializeField] private TextMeshProUGUI statusLabel;   // Status message label
    [SerializeField] private Camera lobbyCam;               // Lobby camera (disabled when game starts)

    [Header("--- Settings ---")]
    [SerializeField] private int maxPlayers = 4;

    private ISession session;

    // ================================================================
    async void Start()
    {
        SetStatus("Connecting...");
        SetButtons(false);

        try
        {
            // Initialize Unity Gaming Services
            await UnityServices.InitializeAsync();

            // Anonymous Sign-in
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

            // Display Join Code for the host to share
            string code = session.Code;
            if (codeDisplay != null)
            {
                codeDisplay.text = "Your Code: " + code;
                codeDisplay.gameObject.SetActive(true);
            }

            NetworkManager.Singleton.StartHost();
            SetStatus("Room Created! Code: " + code);
            HidePanel();
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
            SetStatus("Join Successful!");
            HidePanel();
        }
        catch (Exception e)
        {
            SetStatus("Failed to join: " + e.Message);
            SetButtons(true);
            Debug.LogError(e);
        }
    }

    // ================================================================
    //  Helpers
    // ================================================================
    void HidePanel()
    {
        if (connectPanel != null) connectPanel.SetActive(false);
        if (lobbyCam != null) lobbyCam.gameObject.SetActive(false);
    }

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

    async void OnDestroy()
    {
        if (session != null)
        {
            try { await session.LeaveAsync(); }
            catch { }
        }
    }
}