using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// SettingsManager.cs
/// Unity 6 Settings UI matching Design.png mockup
///
/// Design Features:
/// - Black panel with white border and rounded corners
/// - Gold/Yellow accent color (#D4AF37) for active elements
/// - 3 Tab buttons: GRAPHICS | AUDIO | GAMEPLAY
/// - Bottom buttons: SAVE & CLOSE (gold) and CLOSE X (white)
/// - Sliders with gold handles
/// - Clean, minimal dark theme
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
    [SerializeField] private Button saveAndCloseButton;
    [SerializeField] private Button closeButton;

    [Header("--- Tab Buttons ---")]
    [SerializeField] private Button graphicsTabButton;
    [SerializeField] private Button audioTabButton;
    [SerializeField] private Button gameplayTabButton;

    [Header("--- Tab Button Visuals ---")]
    [SerializeField] private Image graphicsTabImage;
    [SerializeField] private Image audioTabImage;
    [SerializeField] private Image gameplayTabImage;
    [SerializeField] private TextMeshProUGUI graphicsTabText;
    [SerializeField] private TextMeshProUGUI audioTabText;
    [SerializeField] private TextMeshProUGUI gameplayTabText;

    [Header("--- Sub-Panels (Tab Content) ---")]
    [SerializeField] private GameObject graphicsSubPanel;
    [SerializeField] private GameObject audioSubPanel;
    [SerializeField] private GameObject gameplaySubPanel;

    // ================================================================
    //  GRAPHICS TAB UI
    // ================================================================

    [Header("--- Graphics Settings ---")]
    [SerializeField] private TMP_Dropdown displayModeDropdown;
    [SerializeField] private Slider resolutionSlider;
    [SerializeField] private TextMeshProUGUI resolutionValueLabel;
    [SerializeField] private Toggle vSyncToggle;
    [SerializeField] private Slider qualityPresetSlider;
    [SerializeField] private TextMeshProUGUI qualityPresetValueLabel;

    // ================================================================
    //  AUDIO TAB UI
    // ================================================================

    [Header("--- Audio Settings ---")]
    [SerializeField] private Slider masterVolumeSlider;
    [SerializeField] private TextMeshProUGUI masterVolumeLabel;

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
    //  THEME COLORS (matching Design.png)
    // ================================================================

    [Header("--- Theme Colors ---")]
    [SerializeField] private Color activeTabColor = new Color(0.831f, 0.686f, 0.216f, 1f); // Gold #D4AF37
    [SerializeField] private Color inactiveTabColor = new Color(0.3f, 0.3f, 0.3f, 1f);     // Dark gray
    [SerializeField] private Color textActiveColor = Color.white;
    [SerializeField] private Color textInactiveColor = new Color(0.7f, 0.7f, 0.7f, 1f);

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

        // Setup Display Mode Dropdown Options
        SetupDisplayModeDropdown();

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

    /// <summary>Save and close - called by SAVE & CLOSE button</summary>
    public void SaveAndClose()
    {
        SaveSettings();
        CloseSettings();
    }

    // ================================================================
    //  TAB SWITCHING WITH VISUAL FEEDBACK
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
                UpdateTabVisuals(graphicsTabButton, graphicsTabImage, graphicsTabText, true);
                UpdateTabVisuals(audioTabButton, audioTabImage, audioTabText, false);
                UpdateTabVisuals(gameplayTabButton, gameplayTabImage, gameplayTabText, false);
                break;
            case SettingsTab.Audio:
                if (audioSubPanel != null) audioSubPanel.SetActive(true);
                UpdateTabVisuals(graphicsTabButton, graphicsTabImage, graphicsTabText, false);
                UpdateTabVisuals(audioTabButton, audioTabImage, audioTabText, true);
                UpdateTabVisuals(gameplayTabButton, gameplayTabImage, gameplayTabText, false);
                break;
            case SettingsTab.Gameplay:
                if (gameplaySubPanel != null) gameplaySubPanel.SetActive(true);
                UpdateTabVisuals(graphicsTabButton, graphicsTabImage, graphicsTabText, false);
                UpdateTabVisuals(audioTabButton, audioTabImage, audioTabText, false);
                UpdateTabVisuals(gameplayTabButton, gameplayTabImage, gameplayTabText, true);
                break;
        }

        Debug.Log($"[SettingsManager] Switched to {tab} tab");
    }

    private void UpdateTabVisuals(Button button, Image image, TextMeshProUGUI text, bool isActive)
    {
        if (button == null) return;

        Color bgColor = isActive ? activeTabColor : inactiveTabColor;
        Color txtColor = isActive ? textActiveColor : textInactiveColor;

        if (image != null) image.color = bgColor;
        if (text != null) text.color = txtColor;
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

        // Bottom buttons
        if (saveAndCloseButton != null)
        {
            saveAndCloseButton.onClick.RemoveListener(SaveAndClose);
            saveAndCloseButton.onClick.AddListener(SaveAndClose);
        }

        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(CloseSettings);
            closeButton.onClick.AddListener(CloseSettings);
        }

        // Graphics settings
        if (displayModeDropdown != null)
            displayModeDropdown.onValueChanged.AddListener(OnDisplayModeChanged);

        if (resolutionSlider != null)
            resolutionSlider.onValueChanged.AddListener(OnResolutionSliderChanged);

        if (vSyncToggle != null)
            vSyncToggle.onValueChanged.AddListener(OnVSyncChanged);

        if (qualityPresetSlider != null)
            qualityPresetSlider.onValueChanged.AddListener(OnQualityPresetSliderChanged);

        // Audio settings
        if (masterVolumeSlider != null)
            masterVolumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);

        // Gameplay settings
        if (playerNameInputField != null)
            playerNameInputField.onValueChanged.AddListener(OnPlayerNameChanged);

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

        // Bottom buttons
        if (saveAndCloseButton != null)
            saveAndCloseButton.onClick.RemoveListener(SaveAndClose);

        if (closeButton != null)
            closeButton.onClick.RemoveListener(CloseSettings);

        // Graphics settings
        if (displayModeDropdown != null)
            displayModeDropdown.onValueChanged.RemoveListener(OnDisplayModeChanged);

        if (resolutionSlider != null)
            resolutionSlider.onValueChanged.RemoveListener(OnResolutionSliderChanged);

        if (vSyncToggle != null)
            vSyncToggle.onValueChanged.RemoveListener(OnVSyncChanged);

        if (qualityPresetSlider != null)
            qualityPresetSlider.onValueChanged.RemoveListener(OnQualityPresetSliderChanged);

        // Audio settings
        if (masterVolumeSlider != null)
            masterVolumeSlider.onValueChanged.RemoveListener(OnMasterVolumeChanged);

        // Gameplay settings
        if (playerNameInputField != null)
            playerNameInputField.onValueChanged.RemoveListener(OnPlayerNameChanged);

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
        Debug.Log($"[Settings] Display Mode changed to {index}");
    }

    private void OnResolutionSliderChanged(float value)
    {
        if (availableResolutions == null || availableResolutions.Length == 0) return;

        int index = Mathf.RoundToInt(value);
        index = Mathf.Clamp(index, 0, availableResolutions.Length - 1);

        Resolution res = availableResolutions[index];
        Screen.SetResolution(res.width, res.height, Screen.fullScreenMode);

        // ✅ แสดงความละเอียดจริง
        if (resolutionValueLabel != null)
            resolutionValueLabel.text = $"{res.width}x{res.height}";

        PlayerPrefs.SetInt("ResolutionIndex", index);
        Debug.Log($"[Settings] Resolution changed to {res.width}x{res.height}");
    }

    private void OnVSyncChanged(bool enabled)
    {
        QualitySettings.vSyncCount = enabled ? 1 : 0;
        PlayerPrefs.SetInt("VSync", enabled ? 1 : 0);
        Debug.Log($"[Settings] VSync {(enabled ? "enabled" : "disabled")}");
    }

    private void OnQualityPresetSliderChanged(float value)
    {
        int qualityLevel = Mathf.RoundToInt(value / 25f); // 0-100 mapped to 0-4
        qualityLevel = Mathf.Clamp(qualityLevel, 0, QualitySettings.names.Length - 1);

        // ✅ ตั้งค่า Quality Level พร้อมบังคับ Apply ทันที
        QualitySettings.SetQualityLevel(qualityLevel, true);

        // ✅ แสดงชื่อระดับ Quality จริง (Low, Medium, High, Ultra)
        if (qualityPresetValueLabel != null)
            qualityPresetValueLabel.text = QualitySettings.names[qualityLevel];

        PlayerPrefs.SetInt("GraphicsQuality", qualityLevel);
        PlayerPrefs.SetFloat("QualitySliderValue", value);
        Debug.Log($"[Settings] Quality Preset changed to {QualitySettings.names[qualityLevel]} (Level {qualityLevel})");
        Debug.Log($"[Settings]   → Shadows: {QualitySettings.shadows}, Distance: {QualitySettings.shadowDistance}");
    }

    // ================================================================
    //  AUDIO CALLBACKS
    // ================================================================

    private void OnMasterVolumeChanged(float value)
    {
        AudioListener.volume = value;
        PlayerPrefs.SetFloat("MasterVolume", value);

        if (masterVolumeLabel != null)
            masterVolumeLabel.text = $"{Mathf.RoundToInt(value * 100)}%";
    }

    // ================================================================
    //  GAMEPLAY CALLBACKS
    // ================================================================

    private void OnPlayerNameChanged(string playerName)
    {
        PlayerPrefs.SetString("PlayerName", playerName);
        Debug.Log($"[Settings] Player Name changed to '{playerName}'");
    }

    private void OnShowNetworkStatsChanged(bool enabled)
    {
        if (networkStatsPanel != null)
            networkStatsPanel.SetActive(enabled);

        PlayerPrefs.SetInt("ShowNetworkStats", enabled ? 1 : 0);
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
            pingLabel.text = "Ping: <20ms";
            pingLabel.color = Color.green;
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
        PlayerPrefs.Save();
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

        if (resolutionSlider != null)
        {
            int resIndex = PlayerPrefs.GetInt("ResolutionIndex", availableResolutions.Length - 1);
            resolutionSlider.value = Mathf.Clamp(resIndex, 0, availableResolutions.Length - 1);
        }

        if (vSyncToggle != null)
        {
            bool vSync = PlayerPrefs.GetInt("VSync", 1) == 1;
            vSyncToggle.isOn = vSync;
        }

        if (qualityPresetSlider != null)
        {
            float qualityValue = PlayerPrefs.GetFloat("QualitySliderValue", 70f);
            qualityPresetSlider.value = qualityValue;
            OnQualityPresetSliderChanged(qualityValue);
        }

        // Audio
        if (masterVolumeSlider != null)
        {
            float masterVol = PlayerPrefs.GetFloat("MasterVolume", 1f);
            masterVolumeSlider.value = masterVol;
            OnMasterVolumeChanged(masterVol);
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

        if (resolutionSlider == null) return;

        resolutionSlider.minValue = 0;
        resolutionSlider.maxValue = availableResolutions.Length - 1;
        resolutionSlider.wholeNumbers = true;

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

        resolutionSlider.value = currentResolutionIndex;

        // ✅ แสดงความละเอียดจริงตั้งแต่เริ่มต้น
        if (resolutionValueLabel != null && currentResolutionIndex < availableResolutions.Length)
        {
            Resolution res = availableResolutions[currentResolutionIndex];
            resolutionValueLabel.text = $"{res.width}x{res.height}";
        }
    }

    // ================================================================
    //  DISPLAY MODE SETUP
    // ================================================================

    private void SetupDisplayModeDropdown()
    {
        if (displayModeDropdown == null) return;

        displayModeDropdown.ClearOptions();

        System.Collections.Generic.List<string> options = new System.Collections.Generic.List<string>
        {
            "Windowed",
            "Fullscreen",
            "Borderless Fullscreen"
        };

        displayModeDropdown.AddOptions(options);
        displayModeDropdown.value = 0;
        displayModeDropdown.RefreshShownValue();

        Debug.Log("[Settings] Display Mode dropdown setup complete");
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