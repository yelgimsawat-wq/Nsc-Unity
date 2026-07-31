using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
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

    [Header("--- Voice Chat (แท็บ Audio) ---")]
    [Tooltip("ทุกช่องปล่อยว่างได้ — ระบบเสียงพูดยังทำงานปกติ แค่ตั้งค่าจากหน้านี้ไม่ได้\n" +
             "ตัว VoiceChat เกิดเองอัตโนมัติทุกฉาก ไม่ต้องลากใส่ scene")]
    [SerializeField] private TMP_Dropdown micDeviceDropdown;

    [Tooltip("โหมดพูด: Open Mic / Push-to-Talk")]
    [SerializeField] private TMP_Dropdown talkModeDropdown;

    [SerializeField] private Slider micVolumeSlider;
    [SerializeField] private TextMeshProUGUI micVolumeLabel;
    [SerializeField] private Slider voiceVolumeSlider;
    [SerializeField] private TextMeshProUGUI voiceVolumeLabel;

    [Tooltip("เกณฑ์ความดังที่ถือว่า 'กำลังพูด' (ใช้เฉพาะโหมด Open Mic)")]
    [SerializeField] private Slider vadThresholdSlider;

    [Tooltip("หลอดวัดระดับเสียงเข้าไมค์แบบขีดๆ อัปเดตเรียลไทม์ตอนเปิดหน้าตั้งค่า")]
    [SerializeField] private VoiceLevelMeter micLevelMeter;

    [SerializeField] private Toggle micEnabledToggle;
    [SerializeField] private Toggle voiceSpeakerToggle;
    [SerializeField] private TextMeshProUGUI voiceStatusLabel;

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

    /// <summary>แผงตั้งค่าเปิดอยู่ไหม — InMatchMenu ใช้ตัดสินว่า ESC ควรปิดแผงนี้ก่อน</summary>
    public bool IsOpen => isPopupOpen;

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

        UpdateVoiceMeter();
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

        // ไมค์อาจถูกเสียบ/ถอดไปตั้งแต่เปิดเกม — รีเฟรชรายชื่อทุกครั้งที่เปิดหน้าตั้งค่า
        RebuildMicDeviceOptions();
        RefreshVoiceUI();

        // เปิดไมค์เพื่อวัดระดับ แม้ยังไม่ได้เข้าห้อง — ไม่งั้นหลอด Mic Level ไม่มีทางขยับ
        VoiceChat.Instance?.SetMicTestActive(true);

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

        // ปิดหน้าตั้งค่าแล้วเลิกจับไมค์ (ถ้าอยู่ในห้องอยู่ VoiceChat จะเปิดต่อเอง)
        VoiceChat.Instance?.SetMicTestActive(false);
        smoothedMicLevel = 0f;

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

        BindVoiceControls();

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

        UnbindVoiceControls();

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
    //  VOICE CHAT (แท็บ Audio)
    // ================================================================

    /// <summary>
    /// ผูกคอนโทรลเสียงพูดทั้งหมด — ทุกช่องปล่อยว่างใน Inspector ได้
    /// VoiceChat เกิดเองอยู่แล้ว หน้านี้เป็นแค่ที่ปรับค่า ไม่ใช่ตัวเปิดระบบ
    /// </summary>
    private void BindVoiceControls()
    {
        VoiceChat voice = VoiceChat.Instance;

        RebuildMicDeviceOptions();

        if (talkModeDropdown != null)
        {
            talkModeDropdown.ClearOptions();
            talkModeDropdown.AddOptions(new List<string> { "Open Mic", "Push-to-Talk" });
            talkModeDropdown.SetValueWithoutNotify(voice != null ? (int)voice.CurrentTalkMode : 0);
            talkModeDropdown.onValueChanged.RemoveListener(OnTalkModeChanged);
            talkModeDropdown.onValueChanged.AddListener(OnTalkModeChanged);
        }

        BindVoiceSlider(micVolumeSlider, 0f, 4f, voice != null ? voice.MicVolume : 1f, OnMicVolumeChanged);
        BindVoiceSlider(voiceVolumeSlider, 0f, 2f, voice != null ? voice.SpeakerVolume : 1f, OnVoiceVolumeChanged);

        // ⚠️ สไลเดอร์เกณฑ์เสียงต้องเป็นสเกล dB ไม่ใช่ค่าดิบ —
        // ของเดิม 0.001-0.2 เชิงเส้น ทำให้ช่วงที่ใช้งานได้จริงกินแค่ 3.5% ของราง ที่เหลือคือ "ปิดไมค์"
        BindVoiceSlider(vadThresholdSlider, 0f, 1f,
            AmpToSlider(voice != null ? voice.VadThreshold : 0.005f), OnVadThresholdChanged);

        if (micEnabledToggle != null)
        {
            micEnabledToggle.SetIsOnWithoutNotify(voice == null || voice.MicEnabled);
            micEnabledToggle.onValueChanged.RemoveListener(OnMicEnabledChanged);
            micEnabledToggle.onValueChanged.AddListener(OnMicEnabledChanged);
        }

        if (voiceSpeakerToggle != null)
        {
            voiceSpeakerToggle.SetIsOnWithoutNotify(voice == null || voice.SpeakerEnabled);
            voiceSpeakerToggle.onValueChanged.RemoveListener(OnVoiceSpeakerChanged);
            voiceSpeakerToggle.onValueChanged.AddListener(OnVoiceSpeakerChanged);
        }

        if (voice != null)
        {
            voice.OnVoiceStateChanged -= RefreshVoiceUI;
            voice.OnVoiceStateChanged += RefreshVoiceUI;
        }

        RefreshVoiceUI();
    }

    private void BindVoiceSlider(Slider slider, float min, float max, float value, UnityAction<float> callback)
    {
        if (slider == null) return;

        slider.minValue = min;
        slider.maxValue = max;
        slider.SetValueWithoutNotify(Mathf.Clamp(value, min, max));
        slider.onValueChanged.RemoveListener(callback);
        slider.onValueChanged.AddListener(callback);
    }

    private void UnbindVoiceControls()
    {
        if (micDeviceDropdown != null) micDeviceDropdown.onValueChanged.RemoveListener(OnMicDeviceChanged);
        if (talkModeDropdown != null) talkModeDropdown.onValueChanged.RemoveListener(OnTalkModeChanged);
        if (micVolumeSlider != null) micVolumeSlider.onValueChanged.RemoveListener(OnMicVolumeChanged);
        if (voiceVolumeSlider != null) voiceVolumeSlider.onValueChanged.RemoveListener(OnVoiceVolumeChanged);
        if (vadThresholdSlider != null) vadThresholdSlider.onValueChanged.RemoveListener(OnVadThresholdChanged);
        if (micEnabledToggle != null) micEnabledToggle.onValueChanged.RemoveListener(OnMicEnabledChanged);
        if (voiceSpeakerToggle != null) voiceSpeakerToggle.onValueChanged.RemoveListener(OnVoiceSpeakerChanged);

        if (VoiceChat.Instance != null)
            VoiceChat.Instance.OnVoiceStateChanged -= RefreshVoiceUI;
    }

    /// <summary>รายชื่อไมค์เปลี่ยนได้ระหว่างเล่น (เสียบ/ถอดหูฟัง) — สร้างใหม่ทุกครั้งที่เปิดหน้าตั้งค่า</summary>
    private void RebuildMicDeviceOptions()
    {
        if (micDeviceDropdown == null) return;

        VoiceChat voice = VoiceChat.Instance;
        string[] options = VoiceChat.GetMicDeviceOptions();

        // ถอด listener ก่อนเสมอ ไม่งั้น ClearOptions/AddOptions จะยิง callback แล้วเซฟค่าผิด
        micDeviceDropdown.onValueChanged.RemoveListener(OnMicDeviceChanged);
        micDeviceDropdown.ClearOptions();
        micDeviceDropdown.AddOptions(new List<string>(options));

        int selected = 0; // ช่อง 0 = Default ของระบบ
        if (voice != null && !string.IsNullOrEmpty(voice.MicDevice))
        {
            int found = Array.IndexOf(options, voice.MicDevice);
            if (found > 0) selected = found;
        }

        micDeviceDropdown.SetValueWithoutNotify(selected);
        micDeviceDropdown.onValueChanged.AddListener(OnMicDeviceChanged);
    }

    private void OnMicDeviceChanged(int index)
    {
        if (VoiceChat.Instance == null) return;

        string[] options = VoiceChat.GetMicDeviceOptions();
        // index 0 = "Default" -> ส่งค่าว่างให้ VoiceChat ใช้ไมค์ default ของเครื่อง
        string device = (index > 0 && index < options.Length) ? options[index] : string.Empty;
        VoiceChat.Instance.SetMicDevice(device);
    }

    private void OnTalkModeChanged(int index)
    {
        VoiceChat.Instance?.SetTalkMode((VoiceChat.TalkMode)index);
        RefreshVoiceUI();
    }

    private void OnMicVolumeChanged(float value)
    {
        VoiceChat.Instance?.SetMicVolume(value);
        if (micVolumeLabel != null)
            micVolumeLabel.text = $"{Mathf.RoundToInt(value * 100)}%";
    }

    private void OnVoiceVolumeChanged(float value)
    {
        VoiceChat.Instance?.SetSpeakerVolume(value);
        if (voiceVolumeLabel != null)
            voiceVolumeLabel.text = $"{Mathf.RoundToInt(value * 100)}%";
    }

    // สเกล dB ให้ทั้งรางสไลเดอร์ใช้งานได้จริง และเทียบกับหลอดวัดระดับได้ตรงๆ
    private const float VadMinDb = -60f, VadMaxDb = -20f;

    private static float SliderToAmp(float t) =>
        Mathf.Pow(10f, Mathf.Lerp(VadMinDb, VadMaxDb, Mathf.Clamp01(t)) / 20f);

    private static float AmpToSlider(float amp) =>
        Mathf.InverseLerp(VadMinDb, VadMaxDb, 20f * Mathf.Log10(Mathf.Max(amp, 1e-5f)));

    private void OnVadThresholdChanged(float t)
    {
        VoiceChat.Instance?.SetVadThreshold(SliderToAmp(t));
        // ลากเกณฑ์เอง = ตั้งใจคุมเอง ปิดโหมดอัตโนมัติให้เลย ไม่งั้นค่าที่ลากจะไม่มีผล
        VoiceChat.Instance?.SetVadAutoCalibrate(false);
    }
    private void OnMicEnabledChanged(bool value) => VoiceChat.Instance?.SetMicEnabled(value);
    private void OnVoiceSpeakerChanged(bool value) => VoiceChat.Instance?.SetSpeakerEnabled(value);

    private void RefreshVoiceUI()
    {
        VoiceChat voice = VoiceChat.Instance;

        if (voice == null)
        {
            if (voiceStatusLabel != null)
                voiceStatusLabel.text = "Voice system not ready.";
            return;
        }

        if (micEnabledToggle != null) micEnabledToggle.SetIsOnWithoutNotify(voice.MicEnabled);
        if (voiceSpeakerToggle != null) voiceSpeakerToggle.SetIsOnWithoutNotify(voice.SpeakerEnabled);

        if (micVolumeLabel != null)
            micVolumeLabel.text = $"{Mathf.RoundToInt(voice.MicVolume * 100)}%";
        if (voiceVolumeLabel != null)
            voiceVolumeLabel.text = $"{Mathf.RoundToInt(voice.SpeakerVolume * 100)}%";

        // เกณฑ์ VAD ใช้เฉพาะโหมด Open Mic และเฉพาะตอนปิดโหมดอัตโนมัติ
        if (vadThresholdSlider != null)
        {
            vadThresholdSlider.interactable =
                voice.CurrentTalkMode == VoiceChat.TalkMode.OpenMic;

            // โหมดอัตโนมัติปรับเกณฑ์เองตามเสียงรบกวนห้อง — สะท้อนกลับมาที่สไลเดอร์ให้เห็น
            if (voice.VadAutoCalibrate)
                vadThresholdSlider.SetValueWithoutNotify(AmpToSlider(voice.CurrentVadOpenThreshold));
        }

        if (voiceStatusLabel != null)
        {
            if (Microphone.devices.Length == 0)
            {
                voiceStatusLabel.text = "No microphone detected.";
            }
            else
            {
                // rich text — ON เขียว OFF แดง อ่านสถานะได้จากหางตา ไม่ต้องอ่านคำ
                // ⚠️ <alpha> เป็นแท็กเดี่ยว ไม่มี </alpha> — ใส่แท็กปิดแล้วมันจะโผล่เป็นตัวอักษร
                voiceStatusLabel.text =
                    $"Mic: {StatusTag(voice.MicEnabled)}" +
                    $"<color=#FFFFFF44>   |   </color>" +
                    $"Voice: {StatusTag(voice.SpeakerEnabled)}";
            }
        }
    }

    /// <summary>ON เขียว / OFF แดง สำหรับป้ายสถานะเสียงพูด</summary>
    private static string StatusTag(bool on) =>
        on ? "<color=#4ADE80>ON</color>" : "<color=#F87171>OFF</color>";

    private float smoothedMicLevel;

    private void UpdateVoiceMeter()
    {
        if (micLevelMeter == null || !isPopupOpen) return;

        VoiceChat voice = VoiceChat.Instance;
        if (voice == null) return;

        // ⚠️ ห้ามใช้สเกลเชิงเส้น — วัดจริงแล้วพูดปกติได้ RMS ~0.014 คูณ 5 ติดแค่ 2/28 ขีด
        // เสียงมีช่วงไดนามิกกว้างมาก ต้องแปลงเป็น dB แบบเดียวกับ VU meter จริง
        const float MinDb = -60f, MaxDb = -6f;
        float rms = Mathf.Max(voice.CurrentMicLevel, 1e-5f);
        float db = 20f * Mathf.Log10(rms);
        float target = Mathf.Clamp01((db - MinDb) / (MaxDb - MinDb));
        float speed = target > smoothedMicLevel ? 25f : 8f;
        smoothedMicLevel = Mathf.Lerp(smoothedMicLevel, target, Time.unscaledDeltaTime * speed);

        micLevelMeter.SetLevel(smoothedMicLevel);

        // วาดเส้นเกณฑ์บนหลอดเดียวกัน — ผู้เล่นเห็นทันทีว่าต้องพูดให้ดังเกินเส้นไหน
        // ก่อนหน้านี้เส้นนี้มองไม่เห็น คนเลยเห็นหลอดขึ้น 43% แล้วนึกว่าไมค์ปกติ ทั้งที่ถูกตัดอยู่
        float thrDb = 20f * Mathf.Log10(Mathf.Max(voice.CurrentVadOpenThreshold, 1e-5f));
        micLevelMeter.SetThresholdMarker(Mathf.Clamp01((thrDb - MinDb) / (MaxDb - MinDb)));
        micLevelMeter.SetTransmitting(voice.IsTransmitting);
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