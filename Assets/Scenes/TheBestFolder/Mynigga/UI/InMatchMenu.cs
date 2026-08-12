using System.Collections.Generic;
using DG.Tweening;
using NscUnity.Items;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// เมนูระหว่างเล่น — กด ESC เปิด/ปิด
///
/// ⚠️ นี่ไม่ใช่ Pause: เกมออนไลน์หยุดไม่ได้ (netcode เดินต่อ ฟิสิกส์เดินต่อ คนอื่นยังตีเราได้)
///    จึงห้ามแตะ Time.timeScale เด็ดขาด — ItemWheelUI เคยลองแล้วเกมค้าง (ดูคอมเมนต์ในไฟล์นั้น)
///    หน้าที่ของหน้านี้คือให้คนเล่นจัดการ "เสียง" กับ "ออกจากแมตช์" ได้โดยไม่ต้องจบเกมก่อน
///
/// ทำงานร่วมกับ UiFocus: ตอนเปิดจะ Push ตัวเองเข้าไป สคริปต์ผู้เล่นจะได้ไม่แย่งล็อกเมาส์กลับ
/// ปุ่ม ESC ปิดทีละชั้น — กล่องยืนยัน → เมนู → (ถ้าไม่มีอะไรเปิด) เปิดเมนู
/// ถ้าชั้นบนสุดเป็นของคนอื่น (เช่นวงล้อไอเทม) จะปล่อยให้เจ้าของจัดการ ESC เอง
/// </summary>
[DisallowMultipleComponent]
public class InMatchMenu : MonoBehaviour
{
    #region Nested views

    /// <summary>สวิตช์เปิด/ปิดทรงแคปซูล — ปุ่ม + รางสี + ลูกบิดที่เลื่อนซ้ายขวา</summary>
    [System.Serializable]
    public class PillToggle
    {
        public Button button;
        public Image track;
        public RectTransform knob;
        public TextMeshProUGUI label;
    }

    /// <summary>หนึ่งแถวในรายชื่อผู้เล่น = ปิดเสียงคนนั้นได้</summary>
    [System.Serializable]
    public class VoiceRow
    {
        public GameObject root;
        public TextMeshProUGUI nameLabel;
        public Button muteButton;
        public TextMeshProUGUI muteLabel;
        public Image muteFrame;

        [System.NonSerialized] public int peerId;
    }

    #endregion

    #region Inspector

    [Header("Scene")]
    [Tooltip("อยู่ฉากนี้จะกด ESC เปิดเมนูไม่ได้ (เมนูหลักมีปุ่มของตัวเองอยู่แล้ว)")]
    public string menuSceneName = "-MenuNOk";

    [Header("Panel")]
    public GameObject panelRoot;

    [Header("Audio Sliders")]
    public Slider masterSlider;
    public TextMeshProUGUI masterValue;
    public Slider sfxSlider;
    public TextMeshProUGUI sfxValue;
    public TextMeshProUGUI sfxNote;
    public Slider voiceSlider;
    public TextMeshProUGUI voiceValue;

    [Header("Voice")]
    public PillToggle micToggle;
    public PillToggle speakerToggle;
    public Image[] micLevelBars;
    public TextMeshProUGUI playersHeader;
    public VoiceRow[] voiceRows;

    [Header("Audio Devices")]
    public Button micPrevButton;
    public Button micNextButton;
    public TextMeshProUGUI micDeviceLabel;
    public Button outputPrevButton;
    public Button outputNextButton;
    public TextMeshProUGUI outputDeviceLabel;
    [Tooltip("ข้อความเตือนว่าการเปลี่ยนลำโพง/หูฟังมีผลทั้งเครื่อง ไม่ใช่แค่เกมนี้")]
    public TextMeshProUGUI outputDeviceNote;

    [Header("Buttons")]
    public Button backButton;

    [Tooltip("เปิดแผงตั้งค่าเต็ม (ตัวเดียวกับในเมนูหลัก) — ซ่อนเองถ้าหาแผงไม่เจอ")]
    public Button settingsButton;
    public Button leaveButton;

    [Header("Confirm Leave")]
    public GameObject confirmRoot;
    public TextMeshProUGUI confirmHostWarning;
    public TextMeshProUGUI confirmBodyLine1;
    public TextMeshProUGUI confirmBodyLine2;
    public TextMeshProUGUI confirmLeaveLabel;
    public Button confirmCancelButton;
    public Button confirmLeaveButton;

    [Header("Colors")]
    public Color onColor = new Color(0.24f, 0.84f, 0.44f, 1f);
    public Color offColor = new Color(0.22f, 0.25f, 0.31f, 1f);
    public Color mutedColor = new Color(0.90f, 0.24f, 0.28f, 1f);
    public Color idleColor = new Color(0.45f, 0.55f, 0.70f, 0.5f);
    public Color textDim = new Color(0.42f, 0.47f, 0.55f, 1f);
    public Color textWhite = new Color(0.94f, 0.96f, 0.99f, 1f);
    public Color meterLit = new Color(0.24f, 0.84f, 0.44f, 1f);
    public Color meterHot = new Color(0.99f, 0.78f, 0.20f, 1f);
    public Color meterOff = new Color(0.16f, 0.19f, 0.24f, 1f);

    [Header("Animation")]
    public float uiFadeDuration = 0.18f;
    public float uiScaleFrom = 0.96f;
    public Ease uiEase = Ease.OutCubic;

    [Header("Mic Meter")]
    [Tooltip("คูณระดับไมค์ก่อนแปลงเป็นจำนวนขีด — ระดับดิบมันเบามาก")]
    public float micMeterGain = 12f;

    #endregion

    private const string PrefMasterVolume = "MasterVolume"; // คีย์เดียวกับ SettingsManager จะได้ไม่ตีกัน
    private const string PrefSfxVolume = "SfxVolume";

    private readonly List<int> peerBuffer = new List<int>();
    private List<WindowsAudioDevices.Device> outputDevices = new List<WindowsAudioDevices.Device>();
    private readonly Dictionary<GameObject, Tween> runningTweens = new Dictionary<GameObject, Tween>();

    private bool isOpen;
    private bool confirmOpen;
    private bool leaving;
    private CursorLockMode previousCursorLock = CursorLockMode.Locked;
    private bool previousCursorVisible;
    private float rowRefreshTimer;

    #region Lifecycle

    private void Awake()
    {
        SetVisibleInstant(panelRoot, false);
        SetVisibleInstant(confirmRoot, false);
        isOpen = false;
        confirmOpen = false;

        WireButtons();
        WireSliders();
    }

    private void OnDestroy()
    {
        UiFocus.Pop(this);
        KillAllTweens();
    }

    private void Update()
    {
        HandleEscape();

        if (!isOpen) return;

        UpdateMicMeter();

        rowRefreshTimer -= Time.unscaledDeltaTime;
        if (rowRefreshTimer <= 0f)
        {
            rowRefreshTimer = 0.4f;
            RefreshVoiceRows();
        }
    }

    private void HandleEscape()
    {
        if (leaving) return;
        if (!InputCompat.GetKeyDown(KeyCode.Escape)) return;

        // แผงตั้งค่าเต็มเปิดทับอยู่ — ปิดตัวนั้นก่อน ไม่งั้นเมนูข้างล่างหายไปแต่แผงยังค้าง
        SettingsManager settings = SettingsManager.Instance;
        if (settings != null && settings.IsOpen)
        {
            settings.CloseSettings();
            return;
        }

        if (confirmOpen) { CloseConfirm(); return; }
        if (isOpen) { Close(); return; }

        // มี UI อื่นเปิดอยู่ (วงล้อไอเทม) — ปล่อยให้เจ้าของนั้นกิน ESC ไป
        if (UiFocus.IsCaptured) return;
        if (!CanOpenHere()) return;

        Open();
    }

    /// <summary>อยู่ในแมตช์จริงๆ ไหม — อยู่เมนูหลักไม่ต้องเปิด</summary>
    private bool CanOpenHere()
    {
        if (panelRoot == null) return false;
        return SceneManager.GetActiveScene().name != menuSceneName;
    }

    #endregion

    #region Open / Close

    public void Open()
    {
        if (isOpen) return;

        isOpen = true;
        UiFocus.Push(this);

        previousCursorLock = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        RefreshAll();
        SetVisibleAnimated(panelRoot, true);
    }

    public void Close()
    {
        if (!isOpen) return;

        if (confirmOpen) CloseConfirm();

        isOpen = false;
        UiFocus.Pop(this);

        SetVisibleAnimated(panelRoot, false);

        // คืนสถานะเมาส์ให้เหมือนก่อนเปิด — สคริปต์ผู้เล่นจะได้ทำงานต่อเหมือนเดิม
        Cursor.lockState = previousCursorLock;
        Cursor.visible = previousCursorVisible;
    }

    public void Toggle()
    {
        if (isOpen) Close();
        else if (CanOpenHere()) Open();
    }

    #endregion

    #region Wiring

    private void WireButtons()
    {
        if (backButton != null) backButton.onClick.AddListener(Close);
        if (settingsButton != null) settingsButton.onClick.AddListener(OpenSettings);
        if (leaveButton != null) leaveButton.onClick.AddListener(OpenConfirm);

        if (confirmCancelButton != null) confirmCancelButton.onClick.AddListener(CloseConfirm);
        if (confirmLeaveButton != null) confirmLeaveButton.onClick.AddListener(OnConfirmLeave);

        if (micToggle?.button != null) micToggle.button.onClick.AddListener(OnMicToggle);
        if (speakerToggle?.button != null) speakerToggle.button.onClick.AddListener(OnSpeakerToggle);

        if (micPrevButton != null) micPrevButton.onClick.AddListener(() => CycleMicDevice(-1));
        if (micNextButton != null) micNextButton.onClick.AddListener(() => CycleMicDevice(1));
        if (outputPrevButton != null) outputPrevButton.onClick.AddListener(() => CycleOutputDevice(-1));
        if (outputNextButton != null) outputNextButton.onClick.AddListener(() => CycleOutputDevice(1));

        if (voiceRows == null) return;
        for (int i = 0; i < voiceRows.Length; i++)
        {
            VoiceRow row = voiceRows[i];
            if (row?.muteButton == null) continue;

            VoiceRow captured = row;
            row.muteButton.onClick.AddListener(() => OnMuteRow(captured));
        }
    }

    private void WireSliders()
    {
        if (masterSlider != null)
        {
            masterSlider.minValue = 0f;
            masterSlider.maxValue = 1f;
            masterSlider.onValueChanged.AddListener(OnMasterChanged);
        }

        if (voiceSlider != null)
        {
            voiceSlider.minValue = 0f;
            voiceSlider.maxValue = 2f;
            voiceSlider.onValueChanged.AddListener(OnVoiceChanged);
        }

        if (sfxSlider != null)
        {
            sfxSlider.minValue = 0f;
            sfxSlider.maxValue = 1f;
            sfxSlider.onValueChanged.AddListener(OnSfxChanged);
        }
    }

    #endregion

    #region Refresh

    private void RefreshAll()
    {
        VoiceChat voice = VoiceChat.Instance;

        if (masterSlider != null) masterSlider.SetValueWithoutNotify(AudioListener.volume);
        UpdateSliderLabel(masterValue, AudioListener.volume, 1f);

        float sfx = GameAudio.GetBusVolume(AudioBus.Sfx);
        if (sfxSlider != null) sfxSlider.SetValueWithoutNotify(sfx);
        UpdateSliderLabel(sfxValue, sfx, 1f);

        // อธิบายความสัมพันธ์ให้ชัด — Master เป็นตัวหรี่รวม เสียงพูดก็โดนด้วย
        if (sfxNote != null)
        {
            sfxNote.gameObject.SetActive(true);
            sfxNote.text = "MASTER lowers everything including voice - VOICE only changes your friends";
        }

        if (voice != null)
        {
            if (voiceSlider != null) voiceSlider.SetValueWithoutNotify(voice.SpeakerVolume);
            UpdateSliderLabel(voiceValue, voice.SpeakerVolume, 2f);
        }

        // ไม่มีแผงตั้งค่าในเครื่องนี้ (เช่นกด Play จากฉากเกมตรงๆ ไม่ผ่านเมนูหลัก) — ปุ่มกดไปก็ไม่มีอะไรขึ้น
        if (settingsButton != null)
            settingsButton.interactable = SettingsManager.Instance != null;

        RefreshPills();
        RefreshMicDevice();
        RefreshOutputDevice();
        RefreshVoiceRows();
    }

    /// <summary>
    /// ไมค์เลือกได้จริง (Unity เปิดอุปกรณ์อัดเสียงตามชื่อได้)
    /// แต่ลำโพง/หูฟังเลือกในเกมไม่ได้ — Unity ใช้อุปกรณ์ default ของ Windows เสมอ
    /// จึงมีแค่ปุ่มลัดไปหน้าตั้งค่าเสียงของระบบแทน
    /// </summary>
    private void RefreshMicDevice()
    {
        if (micDeviceLabel == null) return;

        VoiceChat voice = VoiceChat.Instance;
        string[] options = VoiceChat.GetMicDeviceOptions();
        int index = CurrentMicDeviceIndex(voice, options);

        string label = options.Length > 0 ? options[Mathf.Clamp(index, 0, options.Length - 1)] : "No microphone";
        if (label.Length > 30) label = label.Substring(0, 29) + "…";

        micDeviceLabel.text = label;
        micDeviceLabel.color = voice != null ? textWhite : textDim;

        bool canCycle = voice != null && options.Length > 1;
        if (micPrevButton != null) micPrevButton.interactable = canCycle;
        if (micNextButton != null) micNextButton.interactable = canCycle;
    }

    private static int CurrentMicDeviceIndex(VoiceChat voice, string[] options)
    {
        if (voice == null || string.IsNullOrEmpty(voice.MicDevice)) return 0; // 0 = "Default"

        for (int i = 0; i < options.Length; i++)
            if (options[i] == voice.MicDevice) return i;

        return 0;
    }

    private void CycleMicDevice(int step)
    {
        VoiceChat voice = VoiceChat.Instance;
        if (voice == null) return;

        string[] options = VoiceChat.GetMicDeviceOptions();
        if (options.Length <= 1) return;

        int index = CurrentMicDeviceIndex(voice, options);
        index = (index + step + options.Length) % options.Length;

        // ช่อง 0 คือ "Default" — ส่งค่าว่างไปให้ VoiceChat แปลว่าใช้ตัว default ของระบบ
        voice.SetMicDevice(index == 0 ? string.Empty : options[index]);
        RefreshMicDevice();
    }

    /// <summary>
    /// เลือกลำโพง/หูฟัง — Unity เลือกเองไม่ได้ จึงสั่ง Windows ย้าย default ให้แทน
    /// (ดู WindowsAudioDevices) ผลจึงมีทั้งเครื่อง ไม่ใช่แค่เกมนี้ ต้องบอกผู้เล่นให้ชัด
    /// </summary>
    private void RefreshOutputDevice()
    {
        if (outputDeviceLabel == null) return;

        outputDevices = WindowsAudioDevices.ListOutputs();

        bool available = outputDevices.Count > 0;
        if (outputPrevButton != null) outputPrevButton.interactable = outputDevices.Count > 1;
        if (outputNextButton != null) outputNextButton.interactable = outputDevices.Count > 1;

        if (!available)
        {
            outputDeviceLabel.text = WindowsAudioDevices.IsSupported ? "No output device" : "Windows only";
            outputDeviceLabel.color = textDim;
            if (outputDeviceNote != null) outputDeviceNote.gameObject.SetActive(false);
            return;
        }

        int index = CurrentOutputIndex();
        string label = outputDevices[index].Name;
        if (label.Length > 34) label = label.Substring(0, 33) + "…";

        outputDeviceLabel.text = label;
        outputDeviceLabel.color = textWhite;
        if (outputDeviceNote != null) outputDeviceNote.gameObject.SetActive(true);
    }

    private int CurrentOutputIndex()
    {
        for (int i = 0; i < outputDevices.Count; i++)
            if (outputDevices[i].IsDefault) return i;

        return 0;
    }

    private void CycleOutputDevice(int step)
    {
        if (outputDevices.Count <= 1) return;

        int index = (CurrentOutputIndex() + step + outputDevices.Count) % outputDevices.Count;
        WindowsAudioDevices.SetDefaultOutput(outputDevices[index].Id);
        RefreshOutputDevice();
    }


    private void RefreshPills()
    {
        VoiceChat voice = VoiceChat.Instance;
        bool micOn = voice != null && voice.MicEnabled;
        bool speakerOn = voice != null && voice.SpeakerEnabled;

        ApplyPill(micToggle, micOn, voice != null);
        ApplyPill(speakerToggle, speakerOn, voice != null);
    }

    private void ApplyPill(PillToggle pill, bool on, bool available)
    {
        if (pill == null) return;

        if (pill.track != null) pill.track.color = available ? (on ? onColor : offColor) : offColor;
        if (pill.label != null) pill.label.color = available ? textWhite : textDim;
        if (pill.button != null) pill.button.interactable = available;

        if (pill.knob != null)
        {
            // รางกว้างเท่าไหร่ก็เลื่อนลูกบิดไปสุดขอบตามนั้น ไม่ต้องฮาร์ดโค้ดระยะ
            float travel = pill.track != null
                ? (pill.track.rectTransform.rect.width - pill.knob.rect.width) * 0.5f - 3f
                : 11f;
            Vector2 pos = pill.knob.anchoredPosition;
            pos.x = on ? travel : -travel;
            pill.knob.anchoredPosition = pos;
        }
    }

    private void RefreshVoiceRows()
    {
        if (voiceRows == null) return;

        VoiceChat voice = VoiceChat.Instance;
        peerBuffer.Clear();
        voice?.GetPeerIds(peerBuffer);

        if (playersHeader != null)
            playersHeader.text = peerBuffer.Count > 0 ? $"PLAYERS  ({peerBuffer.Count})" : "PLAYERS";

        for (int i = 0; i < voiceRows.Length; i++)
        {
            VoiceRow row = voiceRows[i];
            if (row?.root == null) continue;

            bool used = i < peerBuffer.Count;
            row.root.SetActive(used);
            if (!used) continue;

            row.peerId = peerBuffer[i];
            bool muted = voice != null && voice.IsPeerMuted(row.peerId);

            if (row.nameLabel != null)
            {
                row.nameLabel.text = $"Player {row.peerId}";
                row.nameLabel.color = muted ? textDim : textWhite;
            }

            if (row.muteLabel != null)
            {
                row.muteLabel.text = muted ? "MUTED" : "MUTE";
                row.muteLabel.color = muted ? mutedColor : textDim;
            }

            if (row.muteFrame != null)
                row.muteFrame.color = muted ? mutedColor : idleColor;
        }

        // ไม่มีใครในห้อง — บอกให้รู้ว่าไม่ใช่ UI พัง
        if (peerBuffer.Count == 0 && voiceRows.Length > 0 && voiceRows[0]?.root != null)
        {
            voiceRows[0].root.SetActive(true);
            voiceRows[0].peerId = -1;
            if (voiceRows[0].nameLabel != null)
            {
                voiceRows[0].nameLabel.text = "No one else in the room";
                voiceRows[0].nameLabel.color = textDim;
            }
            if (voiceRows[0].muteLabel != null) voiceRows[0].muteLabel.text = string.Empty;
            if (voiceRows[0].muteFrame != null) voiceRows[0].muteFrame.color = new Color(1f, 1f, 1f, 0f);
        }
    }

    private void UpdateMicMeter()
    {
        if (micLevelBars == null || micLevelBars.Length == 0) return;

        VoiceChat voice = VoiceChat.Instance;
        float level = voice != null && voice.MicEnabled ? voice.CurrentMicLevel * micMeterGain : 0f;
        int lit = Mathf.Clamp(Mathf.RoundToInt(level * micLevelBars.Length), 0, micLevelBars.Length);

        for (int i = 0; i < micLevelBars.Length; i++)
        {
            if (micLevelBars[i] == null) continue;

            bool on = i < lit;
            bool hot = i >= micLevelBars.Length - 4;
            micLevelBars[i].color = on ? (hot ? meterHot : meterLit) : meterOff;
        }
    }

    private void UpdateSliderLabel(TextMeshProUGUI label, float value, float max)
    {
        if (label == null) return;
        label.text = $"{Mathf.RoundToInt(value / Mathf.Max(0.0001f, max) * 100f)}%";
    }

    #endregion

    #region Button handlers

    private void OnMasterChanged(float value)
    {
        AudioListener.volume = value;
        PlayerPrefs.SetFloat(PrefMasterVolume, value);
        PlayerPrefs.Save();
        UpdateSliderLabel(masterValue, value, 1f);
    }

    private void OnSfxChanged(float value)
    {
        GameAudio.SetBusVolume(AudioBus.Sfx, value);
        UpdateSliderLabel(sfxValue, value, 1f);
    }

    private void OnVoiceChanged(float value)
    {
        VoiceChat.Instance?.SetSpeakerVolume(value);
        UpdateSliderLabel(voiceValue, value, 2f);
    }

    private void OnMicToggle()
    {
        VoiceChat.Instance?.ToggleMic();
        RefreshPills();
    }

    private void OnSpeakerToggle()
    {
        VoiceChat.Instance?.ToggleSpeaker();
        RefreshPills();
    }

    /// <summary>
    /// เปิดแผงตั้งค่าเต็ม — ตัวเดียวกับที่ใช้ในเมนูหลัก (SettingsManager อยู่ข้ามฉากมาให้แล้ว)
    /// เมนูนี้ไม่ปิดตาม เพราะกดปิดแผงตั้งค่าแล้วต้องเจอเมนูเดิมรออยู่ ไม่ใช่เด้งกลับเข้าเกมเลย
    /// </summary>
    private void OpenSettings()
    {
        SettingsManager settings = SettingsManager.Instance;
        if (settings == null)
        {
            Debug.LogWarning("[InMatchMenu] ไม่เจอ SettingsManager — ต้องเข้าเกมผ่านเมนูหลักก่อน " +
                             "แผงตั้งค่าถึงจะตามมาด้วย (ดู SettingsManager.MakePersistent)");
            return;
        }

        settings.OpenSettings();
    }

    private void OnMuteRow(VoiceRow row)
    {
        if (row == null || row.peerId < 0) return;

        VoiceChat.Instance?.TogglePeerMuted(row.peerId);
        RefreshVoiceRows();
    }


    #endregion

    #region Leave flow

    private void OpenConfirm()
    {
        if (confirmRoot == null) return;

        confirmOpen = true;
        BuildConfirmText();
        SetVisibleAnimated(confirmRoot, true);
    }

    private void CloseConfirm()
    {
        if (!confirmOpen) return;

        confirmOpen = false;
        SetVisibleAnimated(confirmRoot, false);
    }

    /// <summary>ข้อความในกล่องยืนยันต่างกันตามบทบาท — Host ออกแล้วห้องสลาย ต้องเตือนให้ชัด</summary>
    private void BuildConfirmText()
    {
        NetworkManager nm = NetworkManager.Singleton;
        bool isHost = nm != null && nm.IsListening && nm.IsServer;

        int others = 0;
        if (nm != null && nm.IsListening)
        {
            others = nm.IsServer
                ? Mathf.Max(0, nm.ConnectedClientsIds.Count - 1)
                : VoiceChat.Instance != null ? VoiceChat.Instance.PeerCount : 0;
        }

        if (confirmHostWarning != null)
        {
            confirmHostWarning.gameObject.SetActive(isHost);
            confirmHostWarning.text = "!  YOU ARE THE HOST";
        }

        if (confirmBodyLine1 != null)
        {
            confirmBodyLine1.text = isHost
                ? "The room closes when you leave."
                : "You will go back to the main menu.";
        }

        if (confirmBodyLine2 != null)
        {
            confirmBodyLine2.text = isHost
                ? (others > 0
                    ? $"All {others} other players get sent back to the main menu."
                    : "No one else is in the room right now.")
                : "The match keeps running without you.";
        }

        if (confirmLeaveLabel != null)
            confirmLeaveLabel.text = isHost ? "CLOSE THE ROOM" : "LEAVE";
    }

    private async void OnConfirmLeave()
    {
        if (leaving) return;

        leaving = true;
        if (confirmLeaveButton != null) confirmLeaveButton.interactable = false;
        if (confirmCancelButton != null) confirmCancelButton.interactable = false;
        if (confirmLeaveLabel != null) confirmLeaveLabel.text = "LEAVING...";

        await ReturnToMenuOnHostLost.LeaveMatchAsync();
        // ฉากถูกโหลดใหม่แล้ว — object นี้อยู่ในฉากเกม จึงถูกทำลายไปพร้อมกัน ไม่ต้องเก็บกวาดต่อ
    }

    #endregion

    #region Tween helpers (แพทเทิร์นเดียวกับ OnlineNetworkUI / PvpTeamSelectUI)

    private void SetVisibleInstant(GameObject target, bool visible)
    {
        if (target == null) return;

        KillTween(target);
        CanvasGroup group = GetOrAddGroup(target);
        target.SetActive(visible);
        group.alpha = visible ? 1f : 0f;
        group.interactable = visible;
        group.blocksRaycasts = visible;
        target.transform.localScale = Vector3.one;
    }

    private void SetVisibleAnimated(GameObject target, bool visible)
    {
        if (target == null) return;

        KillTween(target);

        if (!isActiveAndEnabled || uiFadeDuration <= 0f)
        {
            SetVisibleInstant(target, visible);
            return;
        }

        CanvasGroup group = GetOrAddGroup(target);
        Transform t = target.transform;

        if (visible && !target.activeSelf)
        {
            target.SetActive(true);
            group.alpha = 0f;
            t.localScale = Vector3.one * uiScaleFrom;
        }

        group.interactable = false;
        group.blocksRaycasts = false;

        float endAlpha = visible ? 1f : 0f;
        Vector3 endScale = visible ? Vector3.one : Vector3.one * uiScaleFrom;

        Sequence sequence = DOTween.Sequence().SetUpdate(true);
        sequence.Join(group.DOFade(endAlpha, uiFadeDuration).SetEase(uiEase));
        sequence.Join(t.DOScale(endScale, uiFadeDuration).SetEase(uiEase));
        sequence.OnComplete(() =>
        {
            runningTweens.Remove(target);
            if (target == null) return;

            group.alpha = endAlpha;
            group.interactable = visible;
            group.blocksRaycasts = visible;
            t.localScale = Vector3.one;
            if (!visible) target.SetActive(false);
        });

        runningTweens[target] = sequence;
    }

    private static CanvasGroup GetOrAddGroup(GameObject target)
    {
        if (!target.TryGetComponent(out CanvasGroup group))
            group = target.AddComponent<CanvasGroup>();
        return group;
    }

    private void KillTween(GameObject target)
    {
        if (runningTweens.TryGetValue(target, out Tween tween) && tween != null && tween.IsActive())
            tween.Kill(false);
        runningTweens.Remove(target);
    }

    private void KillAllTweens()
    {
        foreach (Tween tween in runningTweens.Values)
            if (tween != null && tween.IsActive()) tween.Kill(false);
        runningTweens.Clear();
    }

    #endregion
}
