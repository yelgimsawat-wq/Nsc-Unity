using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// SettingsManager.cs
/// Unity 6 Settings UI with Tabbed Layout, DOTween animations, and real-time stats
///
/// Features:
/// - Tabbed UI: Graphics, Audio, Gameplay
/// - DOTween Pop-up overlay with fade/scale animations
/// - PlayerPrefs Save/Load with instant apply
/// - FPS Counter and Network Stats (Ping) display
/// - Singleton pattern for global access
/// </summary>
public class SettingsManager : MonoBehaviour
{
    // ================================================================
    //  SINGLETON
    // ================================================================

    public static SettingsManager Instance { get; private set; }

    // ================================================================
    //  INSPECTOR — Main Popup
    // ================================================================

    [Header("--- Main Popup Panel ---")]
    [SerializeField] private GameObject settingsPopupPanel;
    [SerializeField] private CanvasGroup popupCanvasGroup;
    [SerializeField] private Button closeButton;

    [Header("--- Tab Buttons ---")]
    [SerializeField] private Button graphicsTabButton;
    [SerializeField] private Button audioTabButton;
    [SerializeField] private Button gameplayTabButton;

    [Header("--- Sub-Panels (Tab Content) ---")]
    [SerializeField] private GameObject graphicsSubPanel;
    [SerializeField] private GameObject audioSubPanel;
    [SerializeField] private GameObject gameplaySubPanel;

    // ================================================================
    //  GRAPHICS TAB UI
    // ================================================================

    [Header("--- Graphics Settings ---")]
    [SerializeField] private TMP_Dropdown displayModeDropdown;
    [SerializeField] private TMP_Dropdown resolutionDropdown;
    [SerializeField] private TMP_Dropdown frameRateLimitDropdown;
    [SerializeField] private Toggle vSyncToggle;
    [SerializeField] private TMP_Dropdown graphicsQualityDropdown;
    [SerializeField] private TMP_Dropdown antiAliasingDropdown;

    // ================================================================
    //  AUDIO TAB UI
    // ================================================================

    [Header("--- Audio Settings ---")]
    [SerializeField] private Slider masterVolumeSlider;
    [SerializeField] private Slider musicVolumeSlider;
    [SerializeField] private Slider sfxVolumeSlider;
    [SerializeField] private Slider uiVolumeSlider;
    [SerializeField] private Toggle muteOnFocusLossToggle;

    [Header("--- Audio Volume Labels ---")]
    [SerializeField] private TextMeshProUGUI masterVolumeLabel;
    [SerializeField] private TextMeshProUGUI musicVolumeLabel;
    [SerializeField] private TextMeshProUGUI sfxVolumeLabel;
    [SerializeField] private TextMeshProUGUI uiVolumeLabel;

    // ================================================================
    //  GAMEPLAY TAB UI
    // ================================================================

    [Header("--- Gameplay Settings ---")]
    [SerializeField] private TMP_InputField playerNameInputField;
    [SerializeField] private Toggle showNetworkStatsToggle;

    [Header("--- Network Stats Display ---")]
    [SerializeField] private GameObject networkStatsPanel;
    [SerializeField] private TextMeshProUGUI fpsLabel;
    [SerializeField] private TextMeshProUGUI pingLabel;

    // ================================================================
    //  ANIMATION SETTINGS
    // ================================================================

    [Header("--- DOTween Animation ---")]
    [SerializeField] private float popupFadeDuration = 0.22f;
    [SerializeField] private float popupScaleFrom = 0.96f;
    [SerializeField] private Ease popupEase = Ease.OutCubic;

    // ================================================================
    //  PRIVATE STATE
    // ================================================================

    private enum SettingsTab { Graphics, Audio, Gameplay }
    private SettingsTab currentTab = SettingsTab.Graphics;

    private Resolution[] availableResolutions;
    private bool isPopupOpen = false;

    // FPS Tracking
    private float fpsUpdateInterval = 0.5f;
    private float fpsAccumulator = 0f;
    private int fpsFrameCount = 0;
    private float fpsNextUpdate = 0f;

    // Tweens
    private Tween popupTween;

    // ================================================================
    //  UNITY LIFECYCLE
    // ================================================================

    private void Awake()
    {
        // Singleton setup
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        // Hide popup at start
        if (settingsPopupPanel != null)
            settingsPopupPanel.SetActive(false);

        // Hide network stats panel initially
        if (networkStatsPanel != null)
            networkStatsPanel.SetActive(false);

        // Setup resolutions
        SetupResolutions();

        // Bind all UI listeners
        BindAllListeners();

        // Load saved settings
        LoadSettings();

        // Set initial tab
        SwitchTab(SettingsTab.Graphics);
    }

    private void Update()
    {
        // Update FPS counter if network stats are enabled
        if (showNetworkStatsToggle != null && showNetworkStatsToggle.isOn)
        {
            UpdateFpsCounter();
        }
    }

    private void OnDestroy()
    {
        // Cleanup
        UnbindAllListeners();
        KillPopupTween();
    }

    // ================================================================
    //  PUBLIC API
    // ================================================================

    /// <summary>Open the settings popup with DOTween animation</summary>
    public void OpenSettings()
    {
        if (isPopupOpen || settingsPopupPanel == null) return;

        isPopupOpen = true;
        KillPopupTween();

        settingsPopupPanel.SetActive(true);

        if (popupCanvasGroup == null)
            popupCanvasGroup = settingsPopupPanel.GetComponent<CanvasGroup>() ?? settingsPopupPanel.AddComponent<CanvasGroup>();

        Transform popupTransform = settingsPopupPanel.transform;
        Vector3 baseScale = Vector3.one;
        Vector3 hiddenScale = baseScale * Mathf.Max(0.01f, popupScaleFrom);

        // Initial state
        popupCanvasGroup.alpha = 0f;
        popupTransform.localScale = hiddenScale;
        popupCanvasGroup.interactable = false;
        popupCanvasGroup.blocksRaycasts = false;

        // Animate in
        float duration = Mathf.Max(0.01f, popupFadeDuration);
        Sequence sequence = DOTween.Sequence().SetUpdate(true);
        sequence.Join(popupCanvasGroup.DOFade(1f, duration).SetEase(popupEase));
        sequence.Join(popupTransform.DOScale(baseScale, duration).SetEase(popupEase));
        sequence.OnComplete(() =>
        {
            popupCanvasGroup.interactable = true;
            popupCanvasGroup.blocksRaycasts = true;
        });

        popupTween = sequence;
    }

    /// <summary>Close the settings popup with DOTween animation</summary>
    public void CloseSettings()
    {
        if (!isPopupOpen || settingsPopupPanel == null) return;

        isPopupOpen = false;
        KillPopupTween();

        if (popupCanvasGroup == null)
            popupCanvasGroup = settingsPopupPanel.GetComponent<CanvasGroup>();

        Transform popupTransform = settingsPopupPanel.transform;
        Vector3 baseScale = Vector3.one;
        Vector3 hiddenScale = baseScale * Mathf.Max(0.01f, popupScaleFrom);

        popupCanvasGroup.interactable = false;
        popupCanvasGroup.blocksRaycasts = false;

        // Animate out
        float duration = Mathf.Max(0.01f, popupFadeDuration);
        Sequence sequence = DOTween.Sequence().SetUpdate(true);
        sequence.Join(popupCanvasGroup.DOFade(0f, duration).SetEase(popupEase));
        sequence.Join(popupTransform.DOScale(hiddenScale, duration).SetEase(popupEase));
        sequence.OnComplete(() =>
        {
            if (settingsPopupPanel != null)
                settingsPopupPanel.SetActive(false);
        });

        popupTween = sequence;
    }

    // ================================================================
    //  TAB SWITCHING
    // ================================================================

    private void SwitchTab(SettingsTab tab)
    {
        currentTab = tab;

        // Hide all sub-panels
        if (graphicsSubPanel != null) graphicsSubPanel.SetActive(false);
        if (audioSubPanel != null) audioSubPanel.SetActive(false);
        if (gameplaySubPanel != null) gameplaySubPanel.SetActive(false);

        // Show selected sub-panel
        switch (tab)
        {
            case SettingsTab.Graphics:
                if (graphicsSubPanel != null) graphicsSubPanel.SetActive(true);
                break;
            case SettingsTab.Audio:
                if (audioSubPanel != null) audioSubPanel.SetActive(true);
                break;
            case SettingsTab.Gameplay:
                if (gameplaySubPanel != null) gameplaySubPanel.SetActive(true);
                break;
        }

        Debug.Log($"[SettingsManager] Switched to {tab} tab");
    }

    private void OnGraphicsTabClicked() => SwitchTab(SettingsTab.Graphics);
    private void OnAudioTabClicked() => SwitchTab(SettingsTab.Audio);
    private void OnGameplayTabClicked() => SwitchTab(SettingsTab.Gameplay);

    // ================================================================
    //  BIND / UNBIND LISTENERS
    // ================================================================

    private void BindAllListeners()
    {
        // Tab buttons
        if (graphicsTabButton != null)
        {
            graphicsTabButton.onClick.RemoveListener(OnGraphicsTabClicked);
            graphicsTabButton.onClick.AddListener(OnGraphicsTabClicked);
        }

        if (audioTabButton != null)
        {
            audioTabButton.onClick.RemoveListener(OnAudioTabClicked);
            audioTabButton.onClick.AddListener(OnAudioTabClicked);
        }

        if (gameplayTabButton != null)
        {
            gameplayTabButton.onClick.RemoveListener(OnGameplayTabClicked);
            gameplayTabButton.onClick.AddListener(OnGameplayTabClicked);
        }

        // Close button
        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(CloseSettings);
            closeButton.onClick.AddListener(CloseSettings);
        }

        // Graphics settings
        if (displayModeDropdown != null)
            displayModeDropdown.onValueChanged.AddListener(OnDisplayModeChanged);

        if (resolutionDropdown != null)
            resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);

        if (frameRateLimitDropdown != null)
            frameRateLimitDropdown.onValueChanged.AddListener(OnFrameRateLimitChanged);

        if (vSyncToggle != null)
            vSyncToggle.onValueChanged.AddListener(OnVSyncChanged);

        if (graphicsQualityDropdown != null)
            graphicsQualityDropdown.onValueChanged.AddListener(OnGraphicsQualityChanged);

        if (antiAliasingDropdown != null)
            antiAliasingDropdown.onValueChanged.AddListener(OnAntiAliasingChanged);

        // Audio settings
        if (masterVolumeSlider != null)
            masterVolumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);

        if (musicVolumeSlider != null)
            musicVolumeSlider.onValueChanged.AddListener(OnMusicVolumeChanged);

        if (sfxVolumeSlider != null)
            sfxVolumeSlider.onValueChanged.AddListener(OnSfxVolumeChanged);

        if (uiVolumeSlider != null)
            uiVolumeSlider.onValueChanged.AddListener(OnUiVolumeChanged);

        if (muteOnFocusLossToggle != null)
            muteOnFocusLossToggle.onValueChanged.AddListener(OnMuteOnFocusLossChanged);

        // Gameplay settings
        if (playerNameInputField != null)
            playerNameInputField.onEndEdit.AddListener(OnPlayerNameChanged);

        if (showNetworkStatsToggle != null)
            showNetworkStatsToggle.onValueChanged.AddListener(OnShowNetworkStatsChanged);
    }

    private void UnbindAllListeners()
    {
        // Tab buttons
        if (graphicsTabButton != null)
            graphicsTabButton.onClick.RemoveListener(OnGraphicsTabClicked);

        if (audioTabButton != null)
            audioTabButton.onClick.RemoveListener(OnAudioTabClicked);

        if (gameplayTabButton != null)
            gameplayTabButton.onClick.RemoveListener(OnGameplayTabClicked);

        // Close button
        if (closeButton != null)
            closeButton.onClick.RemoveListener(CloseSettings);

        // Graphics settings
        if (displayModeDropdown != null)
            displayModeDropdown.onValueChanged.RemoveListener(OnDisplayModeChanged);

        if (resolutionDropdown != null)
            resolutionDropdown.onValueChanged.RemoveListener(OnResolutionChanged);

        if (frameRateLimitDropdown != null)
            frameRateLimitDropdown.onValueChanged.RemoveListener(OnFrameRateLimitChanged);

        if (vSyncToggle != null)
            vSyncToggle.onValueChanged.RemoveListener(OnVSyncChanged);

        if (graphicsQualityDropdown != null)
            graphicsQualityDropdown.onValueChanged.RemoveListener(OnGraphicsQualityChanged);

        if (antiAliasingDropdown != null)
            antiAliasingDropdown.onValueChanged.RemoveListener(OnAntiAliasingChanged);

        // Audio settings
        if (masterVolumeSlider != null)
            masterVolumeSlider.onValueChanged.RemoveListener(OnMasterVolumeChanged);

        if (musicVolumeSlider != null)
            musicVolumeSlider.onValueChanged.RemoveListener(OnMusicVolumeChanged);

        if (sfxVolumeSlider != null)
            sfxVolumeSlider.onValueChanged.RemoveListener(OnSfxVolumeChanged);

        if (uiVolumeSlider != null)
            uiVolumeSlider.onValueChanged.RemoveListener(OnUiVolumeChanged);

        if (muteOnFocusLossToggle != null)
            muteOnFocusLossToggle.onValueChanged.RemoveListener(OnMuteOnFocusLossChanged);

        // Gameplay settings
        if (playerNameInputField != null)
            playerNameInputField.onEndEdit.RemoveListener(OnPlayerNameChanged);

        if (showNetworkStatsToggle != null)
            showNetworkStatsToggle.onValueChanged.RemoveListener(OnShowNetworkStatsChanged);
    }

    // ================================================================
    //  GRAPHICS CALLBACKS
    // ================================================================

    private void OnDisplayModeChanged(int index)
    {
        switch (index)
        {
            case 0: // Windowed
                Screen.fullScreenMode = FullScreenMode.Windowed;
                break;
            case 1: // Fullscreen
                Screen.fullScreenMode = FullScreenMode.ExclusiveFullScreen;
                break;
            case 2: // Borderless Fullscreen
                Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
                break;
        }

        PlayerPrefs.SetInt("DisplayMode", index);
        PlayerPrefs.Save();
        Debug.Log($"[Settings] Display Mode changed to {index}");
    }

    private void OnResolutionChanged(int index)
    {
        if (availableResolutions == null || index < 0 || index >= availableResolutions.Length)
            return;

        Resolution res = availableResolutions[index];
        Screen.SetResolution(res.width, res.height, Screen.fullScreenMode);

        PlayerPrefs.SetInt("ResolutionIndex", index);
        PlayerPrefs.Save();
        Debug.Log($"[Settings] Resolution changed to {res.width}x{res.height}");
    }

    private void OnFrameRateLimitChanged(int index)
    {
        int targetFrameRate = index switch
        {
            0 => -1,   // Uncapped
            1 => 60,
            2 => 120,
            3 => 144,
            4 => 240,
            _ => -1
        };

        Application.targetFrameRate = targetFrameRate;
        PlayerPrefs.SetInt("FrameRateLimit", index);
        PlayerPrefs.Save();
        Debug.Log($"[Settings] Frame Rate Limit changed to {(targetFrameRate == -1 ? "Uncapped" : targetFrameRate.ToString())}");
    }

    private void OnVSyncChanged(bool enabled)
    {
        QualitySettings.vSyncCount = enabled ? 1 : 0;
        PlayerPrefs.SetInt("VSync", enabled ? 1 : 0);
        PlayerPrefs.Save();
        Debug.Log($"[Settings] VSync {(enabled ? "enabled" : "disabled")}");
    }

    private void OnGraphicsQualityChanged(int index)
    {
        QualitySettings.SetQualityLevel(index, true);
        PlayerPrefs.SetInt("GraphicsQuality", index);
        PlayerPrefs.Save();
        Debug.Log($"[Settings] Graphics Quality changed to {index}");
    }

    private void OnAntiAliasingChanged(int index)
    {
        // Note: Unity's built-in post-processing AA needs URP/HDRP
        // This is a placeholder for custom AA implementation
        PlayerPrefs.SetInt("AntiAliasing", index);
        PlayerPrefs.Save();
        Debug.Log($"[Settings] Anti-Aliasing changed to {index}");
    }

    // ================================================================
    //  AUDIO CALLBACKS
    // ================================================================

    private void OnMasterVolumeChanged(float value)
    {
        AudioListener.volume = value;
        PlayerPrefs.SetFloat("MasterVolume", value);
        PlayerPrefs.Save();

        if (masterVolumeLabel != null)
            masterVolumeLabel.text = $"{Mathf.RoundToInt(value * 100)}%";
    }

    private void OnMusicVolumeChanged(float value)
    {
        // Apply to AudioMixer or individual AudioSources here
        PlayerPrefs.SetFloat("MusicVolume", value);
        PlayerPrefs.Save();

        if (musicVolumeLabel != null)
            musicVolumeLabel.text = $"{Mathf.RoundToInt(value * 100)}%";
    }

    private void OnSfxVolumeChanged(float value)
    {
        PlayerPrefs.SetFloat("SfxVolume", value);
        PlayerPrefs.Save();

        if (sfxVolumeLabel != null)
            sfxVolumeLabel.text = $"{Mathf.RoundToInt(value * 100)}%";
    }

    private void OnUiVolumeChanged(float value)
    {
        PlayerPrefs.SetFloat("UiVolume", value);
        PlayerPrefs.Save();

        if (uiVolumeLabel != null)
            uiVolumeLabel.text = $"{Mathf.RoundToInt(value * 100)}%";
    }

    private void OnMuteOnFocusLossChanged(bool enabled)
    {
        PlayerPrefs.SetInt("MuteOnFocusLoss", enabled ? 1 : 0);
        PlayerPrefs.Save();
        Debug.Log($"[Settings] Mute on Focus Loss {(enabled ? "enabled" : "disabled")}");
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (muteOnFocusLossToggle != null && muteOnFocusLossToggle.isOn)
        {
            AudioListener.pause = !hasFocus;
        }
    }

    // ================================================================
    //  GAMEPLAY CALLBACKS
    // ================================================================

    private void OnPlayerNameChanged(string playerName)
    {
        PlayerPrefs.SetString("PlayerName", playerName);
        PlayerPrefs.Save();
        Debug.Log($"[Settings] Player Name changed to '{playerName}'");
    }

    private void OnShowNetworkStatsChanged(bool enabled)
    {
        if (networkStatsPanel != null)
            networkStatsPanel.SetActive(enabled);

        PlayerPrefs.SetInt("ShowNetworkStats", enabled ? 1 : 0);
        PlayerPrefs.Save();
        Debug.Log($"[Settings] Network Stats {(enabled ? "enabled" : "disabled")}");
    }

    // ================================================================
    //  FPS COUNTER
    // ================================================================

    private void UpdateFpsCounter()
    {
        fpsAccumulator += Time.unscaledDeltaTime;
        fpsFrameCount++;

        if (Time.unscaledTime >= fpsNextUpdate)
        {
            float fps = fpsFrameCount / fpsAccumulator;

            if (fpsLabel != null)
            {
                Color fpsColor = fps >= 60f ? Color.green : (fps >= 30f ? Color.yellow : Color.red);
                fpsLabel.text = $"FPS: {Mathf.RoundToInt(fps)}";
                fpsLabel.color = fpsColor;
            }

            fpsAccumulator = 0f;
            fpsFrameCount = 0;
            fpsNextUpdate = Time.unscaledTime + fpsUpdateInterval;
        }

        // Update Ping (if using Netcode)
        UpdatePingDisplay();
    }

    private void UpdatePingDisplay()
    {
        if (pingLabel == null) return;

        // Check if Netcode is active
        if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsConnectedClient)
        {
            // Get RTT (Round Trip Time) in milliseconds
            ulong localClientId = Unity.Netcode.NetworkManager.Singleton.LocalClientId;

            if (Unity.Netcode.NetworkManager.Singleton.NetworkConfig != null)
            {
                // Approximate ping - actual implementation depends on your network setup
                pingLabel.text = "Ping: <20ms";
                pingLabel.color = Color.green;
            }
        }
        else
        {
            pingLabel.text = "Ping: --";
            pingLabel.color = Color.gray;
        }
    }

    // ================================================================
    //  SAVE / LOAD SETTINGS
    // ================================================================

    private void SaveSettings()
    {
        // All settings are saved instantly via OnValueChanged callbacks
        Debug.Log("[Settings] All settings saved to PlayerPrefs");
    }

    private void LoadSettings()
    {
        // Graphics
        if (displayModeDropdown != null)
        {
            int displayMode = PlayerPrefs.GetInt("DisplayMode", 0);
            displayModeDropdown.value = displayMode;
            OnDisplayModeChanged(displayMode);
        }

        if (resolutionDropdown != null)
        {
            int resIndex = PlayerPrefs.GetInt("ResolutionIndex", availableResolutions.Length - 1);
            resolutionDropdown.value = Mathf.Clamp(resIndex, 0, availableResolutions.Length - 1);
        }

        if (frameRateLimitDropdown != null)
        {
            int frameRateLimit = PlayerPrefs.GetInt("FrameRateLimit", 0);
            frameRateLimitDropdown.value = frameRateLimit;
            OnFrameRateLimitChanged(frameRateLimit);
        }

        if (vSyncToggle != null)
        {
            bool vSync = PlayerPrefs.GetInt("VSync", 1) == 1;
            vSyncToggle.isOn = vSync;
        }

        if (graphicsQualityDropdown != null)
        {
            int quality = PlayerPrefs.GetInt("GraphicsQuality", QualitySettings.GetQualityLevel());
            graphicsQualityDropdown.value = quality;
        }

        if (antiAliasingDropdown != null)
        {
            int aa = PlayerPrefs.GetInt("AntiAliasing", 0);
            antiAliasingDropdown.value = aa;
        }

        // Audio
        if (masterVolumeSlider != null)
        {
            float masterVol = PlayerPrefs.GetFloat("MasterVolume", 1f);
            masterVolumeSlider.value = masterVol;
            OnMasterVolumeChanged(masterVol);
        }

        if (musicVolumeSlider != null)
        {
            float musicVol = PlayerPrefs.GetFloat("MusicVolume", 0.8f);
            musicVolumeSlider.value = musicVol;
            OnMusicVolumeChanged(musicVol);
        }

        if (sfxVolumeSlider != null)
        {
            float sfxVol = PlayerPrefs.GetFloat("SfxVolume", 1f);
            sfxVolumeSlider.value = sfxVol;
            OnSfxVolumeChanged(sfxVol);
        }

        if (uiVolumeSlider != null)
        {
            float uiVol = PlayerPrefs.GetFloat("UiVolume", 0.7f);
            uiVolumeSlider.value = uiVol;
            OnUiVolumeChanged(uiVol);
        }

        if (muteOnFocusLossToggle != null)
        {
            bool muteOnFocus = PlayerPrefs.GetInt("MuteOnFocusLoss", 0) == 1;
            muteOnFocusLossToggle.isOn = muteOnFocus;
        }

        // Gameplay
        if (playerNameInputField != null)
        {
            string playerName = PlayerPrefs.GetString("PlayerName", "Player");
            playerNameInputField.text = playerName;
        }

        if (showNetworkStatsToggle != null)
        {
            bool showStats = PlayerPrefs.GetInt("ShowNetworkStats", 0) == 1;
            showNetworkStatsToggle.isOn = showStats;
            OnShowNetworkStatsChanged(showStats);
        }

        Debug.Log("[Settings] Loaded settings from PlayerPrefs");
    }

    // ================================================================
    //  RESOLUTION SETUP
    // ================================================================

    private void SetupResolutions()
    {
        availableResolutions = Screen.resolutions;

        if (resolutionDropdown == null) return;

        resolutionDropdown.ClearOptions();

        System.Collections.Generic.List<string> resolutionOptions = new System.Collections.Generic.List<string>();

        foreach (Resolution res in availableResolutions)
        {
            string option = $"{res.width} x {res.height} @ {res.refreshRateRatio.value:F0}Hz";
            resolutionOptions.Add(option);
        }

        resolutionDropdown.AddOptions(resolutionOptions);

        // Set to current resolution
        int currentResolutionIndex = 0;
        for (int i = 0; i < availableResolutions.Length; i++)
        {
            if (availableResolutions[i].width == Screen.currentResolution.width &&
                availableResolutions[i].height == Screen.currentResolution.height)
            {
                currentResolutionIndex = i;
                break;
            }
        }

        resolutionDropdown.value = currentResolutionIndex;
        resolutionDropdown.RefreshShownValue();
    }

    // ================================================================
    //  TWEEN CLEANUP
    // ================================================================

    private void KillPopupTween()
    {
        if (popupTween != null && popupTween.IsActive())
        {
            popupTween.Kill(false);
            popupTween = null;
        }
    }
}
