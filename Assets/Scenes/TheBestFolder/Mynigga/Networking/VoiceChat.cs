using System;
using System.Collections.Generic;
using UnityEngine;

// ⚠️ ห้ามครอบด้วย #if UNITY_NETCODE_GAMEOBJECTS —
// define ตัวนั้นมาจาก versionDefines ใน asmdef ของ UniVoice จึงมีผลเฉพาะ assembly ของมันเอง
// ไม่ตกมาถึง Assembly-CSharp ที่ไฟล์นี้อยู่ ครอบแล้วโค้ดจะถูกตัดทิ้งเงียบๆ ทั้งบล็อก
// (โปรเจกต์นี้ใช้ NGO เป็นหลักอยู่แล้ว จึงไม่ต้องมี guard)
using Adrenak.UniMic;
using Adrenak.UniVoice;
using Adrenak.UniVoice.Networks;
using Adrenak.UniVoice.Inputs;
using Adrenak.UniVoice.Outputs;
using Adrenak.UniVoice.Filters;

/// <summary>
/// VoiceChat.cs — ระบบเสียงพูดในเกม ขับเคลื่อนด้วย UniVoice (Opus ผ่าน Concentus)
///
/// ⚠️ คลาสนี้จงใจคง public API เดิมไว้ทุกตัว (MicEnabled / SpeakerEnabled / MicVolume /
///    SpeakerVolume / CurrentTalkMode / MicDevice / CurrentMicLevel / IsTransmitting /
///    CurrentVadOpenThreshold / OnVoiceStateChanged ฯลฯ) เพื่อให้ SettingsManager,
///    VoiceLevelMeter และ UI ทั้งหมดที่ต่อไว้แล้วใช้งานต่อได้โดยไม่ต้องแก้อะไรเลย
///    เปลี่ยนแค่ "เครื่องยนต์" ข้างในจาก ADPCM ที่เขียนเอง มาเป็น Opus ของ UniVoice
///
/// ทำไมถึงเปลี่ยน: Opus เป็น codec ระดับเดียวกับที่ Discord ใช้ คุณภาพห่างจาก ADPCM 4-bit มาก
/// และ UniVoice มี VAD, jitter buffer, pitch-correction ให้พร้อม ไม่ต้องดูแลเอง
///
/// สิ่งที่ยังไม่มี: ตัดเสียงสะท้อน (AEC) — ยังต้องใส่หูฟัง
/// ตัดเสียงรบกวนทำได้เพิ่มด้วย RNNoise4Unity (ลงแยก + เปิด define UNIVOICE_FILTER_RNNOISE4UNITY)
/// </summary>
[DisallowMultipleComponent]
public class VoiceChat : MonoBehaviour
{
    public enum TalkMode
    {
        OpenMic = 0,
        PushToTalk = 1
    }

    private const string PrefMicOn = "Voice_MicOn";
    private const string PrefSpeakerOn = "Voice_SpeakerOn";
    private const string PrefMicVolume = "Voice_MicVolume";
    private const string PrefSpeakerVolume = "Voice_SpeakerVolume";
    private const string PrefTalkMode = "Voice_TalkMode";
    private const string PrefMicDevice = "Voice_MicDevice";
    private const string PrefVadThreshold = "Voice_VadThreshold_v2";
    private const string PrefVadAuto = "Voice_VadAuto";

    public static VoiceChat Instance { get; private set; }

    public event Action OnVoiceStateChanged;

    private bool micEnabled = true;
    private bool speakerEnabled = true;
    private float micVolume = 1f;
    private float speakerVolume = 1f;
    private TalkMode talkMode = TalkMode.OpenMic;
    private string micDevice = string.Empty;
    private float vadThreshold = 0.005f;
    private bool vadAutoCalibrate = true;

    private KeyCode pushToTalkKey = KeyCode.V;
    private KeyCode toggleMicKey = KeyCode.M;
    private KeyCode toggleSpeakerKey = KeyCode.N;

    public bool MicEnabled => micEnabled;
    public bool SpeakerEnabled => speakerEnabled;
    public float MicVolume => micVolume;
    public float SpeakerVolume => speakerVolume;
    public TalkMode CurrentTalkMode => talkMode;
    public string MicDevice => micDevice;
    public float VadThreshold => vadThreshold;
    public bool VadAutoCalibrate => vadAutoCalibrate;
    public KeyCode PushToTalkKey => pushToTalkKey;
    public KeyCode ToggleMicKey => toggleMicKey;
    public KeyCode ToggleSpeakerKey => toggleSpeakerKey;

    public bool IsTransmitting { get; private set; }
    public float CurrentMicLevel { get; private set; }
    public float CurrentVadOpenThreshold { get; private set; } = 0.005f;
    public float CurrentNoiseFloor { get; private set; } = 0.003f;

    private bool micTestActive;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (Instance != null) return;

        GameObject host = new GameObject("~VoiceChat");
        host.AddComponent<VoiceChat>();
        DontDestroyOnLoad(host);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        LoadSettings();
    }

    private void LoadSettings()
    {
        micEnabled = PlayerPrefs.GetInt(PrefMicOn, 1) == 1;
        speakerEnabled = PlayerPrefs.GetInt(PrefSpeakerOn, 1) == 1;
        micVolume = PlayerPrefs.GetFloat(PrefMicVolume, 1f);
        speakerVolume = PlayerPrefs.GetFloat(PrefSpeakerVolume, 1f);
        talkMode = (TalkMode)PlayerPrefs.GetInt(PrefTalkMode, (int)TalkMode.OpenMic);
        micDevice = PlayerPrefs.GetString(PrefMicDevice, string.Empty);
        vadThreshold = PlayerPrefs.GetFloat(PrefVadThreshold, 0.005f);
        vadAutoCalibrate = PlayerPrefs.GetInt(PrefVadAuto, 1) == 1;
        CurrentVadOpenThreshold = vadThreshold;

        if (!string.IsNullOrEmpty(micDevice) && Array.IndexOf(Microphone.devices, micDevice) < 0)
            micDevice = string.Empty;
    }

    public static string[] GetMicDeviceOptions()
    {
        string[] devices = Microphone.devices;
        string[] options = new string[devices.Length + 1];
        options[0] = "Default";
        Array.Copy(devices, 0, options, 1, devices.Length);
        return options;
    }

    private NGOClient audioClient;
    private IAudioServer<int> audioServer;
    private ClientSession<int> session;
    private UniMicInput micInput;
    private Mic.Device activeDevice;
    private bool voiceReady;

    private void Start()
    {
        SetupUniVoice();
    }

    private void SetupUniVoice()
    {
        if (voiceReady) return;

        try
        {
            audioClient = new NGOClient();
            audioServer = new NGOServer(audioClient);

            Mic.Init();
            IAudioInput input = CreateInput();

            session = new ClientSession<int>(audioClient, input, () =>
            {
                var output = StreamedAudioSourceOutput.New();
                output.gameObject.name = "VoicePeerOutput";
                // เสียงพูดต้องไม่ถูกกลืนด้วยเสียงเกม และไม่ตาม Master Volume
                var src = output.Stream != null ? output.Stream.UnityAudioSource : null;
                if (src != null)
                {
                    src.priority = 0;
                    src.ignoreListenerVolume = true;
                    src.ignoreListenerPause = true;
                    src.bypassEffects = true;
                    src.bypassListenerEffects = true;
                    src.bypassReverbZones = true;
                    src.spatialBlend = 0f;
                    src.volume = speakerEnabled ? Mathf.Clamp(speakerVolume, 0f, 2f) : 0f;
                }
                return output;
            });

            // VAD ของ UniVoice ตัดช่วงเงียบให้ก่อนเข้า encoder
            session.InputFilters.Add(new SimpleVadFilter(new SimpleVad()));

            // Opus — หัวใจของการเปลี่ยนมาใช้ UniVoice
            session.InputFilters.Add(new ConcentusEncodeFilter());
            session.AddOutputFilter<ConcentusDecodeFilter>(() => new ConcentusDecodeFilter());

            ApplyMicEnabled();
            ApplySpeakerEnabled();

            voiceReady = true;
            Debug.Log("[Voice] UniVoice (Opus) พร้อมใช้งาน");
        }
        catch (Exception e)
        {
            Debug.LogError("[Voice] ตั้งค่า UniVoice ไม่สำเร็จ: " + e);
        }
    }

    private IAudioInput CreateInput()
    {
        DetachDevice();

        var devices = Mic.AvailableDevices;
        if (devices == null || devices.Count == 0)
        {
            Debug.LogWarning("[Voice] เครื่องนี้ไม่มีไมโครโฟน — จะได้ยินคนอื่นอย่างเดียว");
            return new EmptyAudioInput();
        }

        Mic.Device chosen = null;
        if (!string.IsNullOrEmpty(micDevice))
        {
            foreach (var d in devices)
                if (d.Name == micDevice) { chosen = d; break; }
        }
        if (chosen == null) chosen = devices[0];

        if (!chosen.IsRecording) chosen.StartRecording(60);

        activeDevice = chosen;
        activeDevice.OnFrameCollected += OnMicFrame;

        micInput = new UniMicInput(chosen);
        return micInput;
    }

    private void DetachDevice()
    {
        if (activeDevice == null) return;
        activeDevice.OnFrameCollected -= OnMicFrame;
        activeDevice = null;
    }

    /// <summary>อ่านระดับเสียงดิบไว้ป้อนหลอดวัดใน Settings — ต้องดักก่อนเข้า Opus</summary>
    private void OnMicFrame(int channels, int frequency, float[] samples)
    {
        if (samples == null || samples.Length == 0) return;

        float sum = 0f;
        for (int i = 0; i < samples.Length; i++) sum += samples[i] * samples[i];
        float rms = Mathf.Sqrt(sum / samples.Length);
        CurrentMicLevel = rms;

        if (!micEnabled) { IsTransmitting = false; return; }

        // ประมาณเสียงรบกวนไว้วาดเส้นเกณฑ์บนหลอด (UniVoice ตัดสินใจ VAD เองข้างใน
        // ตัวเลขนี้จึงใช้แสดงผลอย่างเดียว ไม่ได้ไปคุมการส่งจริง)
        if (!IsTransmitting)
        {
            CurrentNoiseFloor = rms < CurrentNoiseFloor
                ? Mathf.Lerp(CurrentNoiseFloor, rms, 0.30f)
                : Mathf.Lerp(CurrentNoiseFloor, rms, 0.002f);
        }
        CurrentVadOpenThreshold = vadAutoCalibrate
            ? Mathf.Clamp(CurrentNoiseFloor * 2.5f, 0.003f, 0.02f)
            : vadThreshold;

        IsTransmitting = talkMode == TalkMode.PushToTalk
            ? Input.GetKey(pushToTalkKey)
            : rms >= CurrentVadOpenThreshold;
    }

    private void Update()
    {
        HandleHotkeys();

        if (!voiceReady) return;

        // Push-to-talk คุมที่ InputEnabled ตรงๆ ตามที่ UniVoice แนะนำ
        if (session != null && micEnabled)
        {
            bool shouldSend = talkMode != TalkMode.PushToTalk || Input.GetKey(pushToTalkKey);
            if (session.InputEnabled != shouldSend) session.InputEnabled = shouldSend;
        }
    }

    private void ApplyMicEnabled()
    {
        if (session != null) session.InputEnabled = micEnabled;
        if (!micEnabled) IsTransmitting = false;
    }

    private void ApplySpeakerEnabled()
    {
        if (session != null) session.OutputsEnabled = speakerEnabled;
        ApplySpeakerVolume();
    }

    private void ApplySpeakerVolume()
    {
        if (session == null) return;

        float v = speakerEnabled ? Mathf.Clamp(speakerVolume, 0f, 2f) : 0f;
        foreach (var kv in session.PeerOutputs)
        {
            if (kv.Value is StreamedAudioSourceOutput sso && sso.Stream != null)
            {
                var src = sso.Stream.UnityAudioSource;
                if (src != null) src.volume = v;
            }
        }
    }

    private void RebuildInput()
    {
        if (!voiceReady || session == null) return;

        try { session.Input = CreateInput(); }
        catch (Exception e) { Debug.LogError("[Voice] เปลี่ยนไมค์ไม่สำเร็จ: " + e); }
    }

    private void OnDestroy()
    {
        if (Instance != this) return;

        DetachDevice();
        try { session?.Dispose(); } catch { }
        session = null;
        Instance = null;
    }

    // ================================================================
    //  PUBLIC API — เหมือนเดิมทุกตัว UI ที่ต่อไว้แล้วใช้ต่อได้เลย
    // ================================================================

    public void SetMicEnabled(bool value)
    {
        micEnabled = value;
        PlayerPrefs.SetInt(PrefMicOn, value ? 1 : 0);
        PlayerPrefs.Save();
        ApplyMicEnabled();
        OnVoiceStateChanged?.Invoke();
    }

    public void SetSpeakerEnabled(bool value)
    {
        speakerEnabled = value;
        PlayerPrefs.SetInt(PrefSpeakerOn, value ? 1 : 0);
        PlayerPrefs.Save();
        ApplySpeakerEnabled();
        OnVoiceStateChanged?.Invoke();
    }

    public void ToggleMic() => SetMicEnabled(!micEnabled);
    public void ToggleSpeaker() => SetSpeakerEnabled(!speakerEnabled);

    public void SetMicVolume(float value)
    {
        micVolume = Mathf.Clamp(value, 0f, 4f);
        PlayerPrefs.SetFloat(PrefMicVolume, micVolume);
        PlayerPrefs.Save();
    }

    public void SetSpeakerVolume(float value)
    {
        speakerVolume = Mathf.Clamp(value, 0f, 2f);
        PlayerPrefs.SetFloat(PrefSpeakerVolume, speakerVolume);
        PlayerPrefs.Save();
        ApplySpeakerVolume();
    }

    public void SetTalkMode(TalkMode mode)
    {
        talkMode = mode;
        PlayerPrefs.SetInt(PrefTalkMode, (int)mode);
        PlayerPrefs.Save();
        OnVoiceStateChanged?.Invoke();
    }

    public void SetVadThreshold(float value)
    {
        vadThreshold = Mathf.Clamp(value, 0.0005f, 0.2f);
        PlayerPrefs.SetFloat(PrefVadThreshold, vadThreshold);
        PlayerPrefs.Save();
    }

    public void SetVadAutoCalibrate(bool value)
    {
        vadAutoCalibrate = value;
        PlayerPrefs.SetInt(PrefVadAuto, value ? 1 : 0);
        PlayerPrefs.Save();
        OnVoiceStateChanged?.Invoke();
    }

    public void SetMicDevice(string device)
    {
        if (!string.IsNullOrEmpty(device) && Array.IndexOf(Microphone.devices, device) < 0)
            device = string.Empty;

        micDevice = device ?? string.Empty;
        PlayerPrefs.SetString(PrefMicDevice, micDevice);
        PlayerPrefs.Save();

        RebuildInput();
        OnVoiceStateChanged?.Invoke();
    }

    /// <summary>
    /// UniVoice จับไมค์ค้างไว้ตลอดอยู่แล้ว หลอดวัดระดับจึงทำงานได้ทันทีแม้ยังไม่เข้าห้อง
    /// เมธอดนี้คงไว้เพื่อให้ SettingsManager เรียกได้เหมือนเดิม
    /// </summary>
    public void SetMicTestActive(bool value) => micTestActive = value;

    private void HandleHotkeys()
    {
        // กำลังพิมพ์ในช่องข้อความ (เช่นกรอกรหัสห้อง) ห้ามกินปุ่ม M/N
        var selected = UnityEngine.EventSystems.EventSystem.current != null
            ? UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject
            : null;
        if (selected != null && selected.GetComponent<TMPro.TMP_InputField>() != null)
            return;

        if (Input.GetKeyDown(toggleMicKey)) ToggleMic();
        if (Input.GetKeyDown(toggleSpeakerKey)) ToggleSpeaker();
    }
}
