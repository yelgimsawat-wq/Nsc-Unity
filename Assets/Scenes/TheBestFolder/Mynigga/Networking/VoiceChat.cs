using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// VoiceChat.cs — ระบบเสียงพูดในเกมแบบเรียบง่าย ไม่ต้องลงแพ็กเกจเพิ่ม
///
/// ทำไมไม่เป็น NetworkBehaviour:
///   NetworkBehaviour ต้องอยู่บน NetworkObject ที่ Server สั่ง spawn และต้องลงทะเบียนเป็น
///   network prefab ก่อน = วางในทุกฉากเองไม่ได้ ตัวนี้เลยใช้ CustomMessagingManager แทน
///   (ส่งข้อความชื่อ ๆ ผ่านการเชื่อมต่อเดิมได้เลย ไม่ต้องมี NetworkObject) ทำให้ตัวเองเป็นแค่
///   MonoBehaviour ที่ DontDestroyOnLoad และเกิดเองอัตโนมัติทุกฉากผ่าน RuntimeInitializeOnLoadMethod
///
/// เส้นทางเสียง:
///   ไมค์ -> อ่านตัวอย่างใหม่ทุกเฟรม -> เช็คว่าพูดอยู่ไหม (VAD/ปุ่ม PTT) -> คูณ MicVolume
///        -> บีบเป็น μ-law 8-bit -> ส่งหา Server
///   Server -> แปะ id คนพูด -> กระจายต่อให้ทุกคนยกเว้นคนพูดเอง
///   ผู้รับ -> คลาย μ-law -> ยัดลง ring buffer -> AudioSource แบบ streaming ดูดไปเล่น
///
/// แบนด์วิดท์: 8000 Hz × 1 byte = 8 KB/s (~64 kbps) ต่อ "คนที่กำลังพูด" หนึ่งคน
///   คนที่เงียบไม่ส่งอะไรเลย ห้อง 8 คนพูดพร้อมกันหมดถึงจะแตะ ~512 kbps
/// </summary>
[DisallowMultipleComponent]
public class VoiceChat : MonoBehaviour
{
    public enum TalkMode
    {
        OpenMic = 0,      // เปิดไมค์ค้าง ส่งเฉพาะตอนตรวจเจอเสียงพูด
        PushToTalk = 1    // กดปุ่มค้างถึงจะส่ง
    }

    // ── ค่าคงที่ของสายเสียง ─────────────────────────────────────────
    // 8 kHz = คุณภาพวิทยุสื่อสาร ชัดพอคุยกันรู้เรื่อง และประหยัดแบนด์วิดท์ที่สุด
    public const int SampleRate = 8000;

    // 480 ตัวอย่าง = 60 ms ต่อแพ็กเก็ต -> 480 bytes + header ยังห่างจาก MTU 1296 เยอะ
    // ถ้าเล็กกว่านี้จำนวนแพ็กเก็ต/วิ จะเยอะขึ้นโดยได้ latency ดีขึ้นนิดเดียว
    private const int ChunkSamples = 480;

    private const string MsgUpstream = "NscVoiceUp";     // client -> server
    private const string MsgDownstream = "NscVoiceDown"; // server -> clients

    private const int PlaybackBufferSamples = SampleRate * 2; // กันกระตุก 2 วินาที

    // ── PlayerPrefs keys ────────────────────────────────────────────
    private const string PrefMicOn = "Voice_MicOn";
    private const string PrefSpeakerOn = "Voice_SpeakerOn";
    private const string PrefMicVolume = "Voice_MicVolume";
    private const string PrefSpeakerVolume = "Voice_SpeakerVolume";
    private const string PrefTalkMode = "Voice_TalkMode";
    private const string PrefMicDevice = "Voice_MicDevice";
    private const string PrefVadThreshold = "Voice_VadThreshold";

    public static VoiceChat Instance { get; private set; }

    /// <summary>ยิงเมื่อสถานะเปิด/ปิดไมค์ หรือ หูฟัง เปลี่ยน — UI เอาไปอัปเดตปุ่มได้</summary>
    public event Action OnVoiceStateChanged;

    // ── สถานะที่ผู้เล่นปรับได้ ───────────────────────────────────────
    private bool micEnabled = true;
    private bool speakerEnabled = true;
    private float micVolume = 1f;
    private float speakerVolume = 1f;
    private TalkMode talkMode = TalkMode.OpenMic;
    private string micDevice = string.Empty;
    private float vadThreshold = 0.02f;

    // ปุ่มลัด — ตัว ~VoiceChat ถูกสร้างจากโค้ดตอนรัน ไม่ได้อยู่ในฉาก จึงไม่มี Inspector ให้ปรับ
    // ถ้าอยากให้ผู้เล่นตั้งปุ่มเองทีหลัง ให้ทำเป็น PlayerPrefs แบบเดียวกับค่าอื่นในไฟล์นี้
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
    public KeyCode PushToTalkKey => pushToTalkKey;
    public KeyCode ToggleMicKey => toggleMicKey;
    public KeyCode ToggleSpeakerKey => toggleSpeakerKey;

    /// <summary>true เมื่อไมค์กำลังส่งเสียงออกจริง ๆ ณ ตอนนี้ (เอาไปทำไฟแสดงสถานะได้)</summary>
    public bool IsTransmitting { get; private set; }

    /// <summary>ระดับเสียงเข้าไมค์ 0-1 ล่าสุด (เอาไปทำหลอดวัดระดับตอนตั้งค่าได้)</summary>
    public float CurrentMicLevel { get; private set; }

    private AudioClip micClip;
    private int lastMicPosition;
    private readonly float[] chunkBuffer = new float[ChunkSamples];
    private readonly byte[] encodeBuffer = new byte[ChunkSamples];

    private readonly Dictionary<ulong, VoicePeer> peers = new Dictionary<ulong, VoicePeer>();
    private bool handlersRegistered;

    // ================================================================
    //  เกิดเองทุกฉาก — ไม่ต้องลากอะไรใส่ scene เลย
    // ================================================================

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
        // AutoCreate ยิงครั้งเดียวก็จริง แต่กันเคสมีคนเผลอลากใส่ฉากไว้ด้วย
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        LoadSettings();
    }

    private void OnDestroy()
    {
        if (Instance != this) return;

        StopMicrophone();
        UnregisterHandlers();
        UnhookNetworkManager();

        foreach (VoicePeer peer in peers.Values)
            peer.Dispose();
        peers.Clear();

        Instance = null;
    }

    private void Update()
    {
        HookNetworkManagerIfNeeded();
        HandleHotkeys();
        PumpMicrophone();
    }

    // ================================================================
    //  SETTINGS — โหลด/บันทึกลง PlayerPrefs ทันทีที่เปลี่ยน
    // ================================================================

    private void LoadSettings()
    {
        micEnabled = PlayerPrefs.GetInt(PrefMicOn, 1) == 1;
        speakerEnabled = PlayerPrefs.GetInt(PrefSpeakerOn, 1) == 1;
        micVolume = PlayerPrefs.GetFloat(PrefMicVolume, 1f);
        speakerVolume = PlayerPrefs.GetFloat(PrefSpeakerVolume, 1f);
        talkMode = (TalkMode)PlayerPrefs.GetInt(PrefTalkMode, (int)TalkMode.OpenMic);
        micDevice = PlayerPrefs.GetString(PrefMicDevice, string.Empty);
        vadThreshold = PlayerPrefs.GetFloat(PrefVadThreshold, 0.02f);

        // อุปกรณ์ที่จำไว้อาจถูกถอดไปแล้ว — ตกกลับไปใช้ตัว default ของเครื่อง
        if (!string.IsNullOrEmpty(micDevice) && Array.IndexOf(Microphone.devices, micDevice) < 0)
            micDevice = string.Empty;
    }

    public void SetMicEnabled(bool value)
    {
        micEnabled = value;
        PlayerPrefs.SetInt(PrefMicOn, value ? 1 : 0);
        PlayerPrefs.Save();

        if (!value) StopMicrophone();
        OnVoiceStateChanged?.Invoke();
    }

    public void SetSpeakerEnabled(bool value)
    {
        speakerEnabled = value;
        PlayerPrefs.SetInt(PrefSpeakerOn, value ? 1 : 0);
        PlayerPrefs.Save();

        ApplySpeakerVolume();
        OnVoiceStateChanged?.Invoke();
    }

    public void ToggleMic() => SetMicEnabled(!micEnabled);
    public void ToggleSpeaker() => SetSpeakerEnabled(!speakerEnabled);

    public void SetMicVolume(float value)
    {
        micVolume = Mathf.Clamp(value, 0f, 2f);
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
        vadThreshold = Mathf.Clamp(value, 0.001f, 0.5f);
        PlayerPrefs.SetFloat(PrefVadThreshold, vadThreshold);
        PlayerPrefs.Save();
    }

    /// <summary>เปลี่ยนไมค์ — ส่ง string.Empty เพื่อใช้ตัว default ของเครื่อง</summary>
    public void SetMicDevice(string device)
    {
        if (!string.IsNullOrEmpty(device) && Array.IndexOf(Microphone.devices, device) < 0)
            device = string.Empty;

        micDevice = device ?? string.Empty;
        PlayerPrefs.SetString(PrefMicDevice, micDevice);
        PlayerPrefs.Save();

        // ต้องเปิดใหม่ให้ไปจับอุปกรณ์ตัวใหม่
        StopMicrophone();
        OnVoiceStateChanged?.Invoke();
    }

    /// <summary>รายชื่อไมค์ที่เครื่องมี — ช่องแรกคือ "ค่าเริ่มต้นของระบบ"</summary>
    public static string[] GetMicDeviceOptions()
    {
        string[] devices = Microphone.devices;
        string[] options = new string[devices.Length + 1];
        options[0] = "Default";
        Array.Copy(devices, 0, options, 1, devices.Length);
        return options;
    }

    private void ApplySpeakerVolume()
    {
        float volume = speakerEnabled ? speakerVolume : 0f;
        foreach (VoicePeer peer in peers.Values)
            peer.SetVolume(volume);
    }

    // ================================================================
    //  HOTKEYS ในเกม
    // ================================================================

    private void HandleHotkeys()
    {
        // เคอร์เซอร์ปลดล็อกอยู่ = ผู้เล่นกำลังพิมพ์/กดเมนู อย่าไปกินปุ่ม
        if (Input.GetKeyDown(toggleMicKey)) ToggleMic();
        if (Input.GetKeyDown(toggleSpeakerKey)) ToggleSpeaker();
    }

    // ================================================================
    //  MICROPHONE CAPTURE
    // ================================================================

    private bool ShouldCapture()
    {
        if (!micEnabled) return false;

        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) return false;

        // ห้องมีคนเดียว (ตัวเอง) ก็ไม่ต้องเปลืองไมค์/แบนด์วิดท์
        return nm.ConnectedClientsIds.Count > 1 || !nm.IsServer;
    }

    private void EnsureMicrophoneRunning()
    {
        if (micClip != null) return;
        if (Microphone.devices.Length == 0) return;

        string device = string.IsNullOrEmpty(micDevice) ? null : micDevice;

        // loop = true, ยาว 1 วินาที — เราไล่อ่านตามหัวอ่านที่วนไปเรื่อย ๆ
        micClip = Microphone.Start(device, true, 1, SampleRate);
        lastMicPosition = 0;
    }

    private void StopMicrophone()
    {
        if (micClip == null) return;

        string device = string.IsNullOrEmpty(micDevice) ? null : micDevice;
        if (Microphone.IsRecording(device))
            Microphone.End(device);

        Destroy(micClip);
        micClip = null;
        lastMicPosition = 0;
        IsTransmitting = false;
        CurrentMicLevel = 0f;
    }

    private void PumpMicrophone()
    {
        if (!ShouldCapture())
        {
            StopMicrophone();
            return;
        }

        EnsureMicrophoneRunning();
        if (micClip == null) return;

        int micPosition = Microphone.GetPosition(string.IsNullOrEmpty(micDevice) ? null : micDevice);
        if (micPosition < 0 || micPosition >= micClip.samples) return;

        int available = micPosition - lastMicPosition;
        if (available < 0) available += micClip.samples; // หัวอ่านวนกลับไปต้น

        // ส่งทีละก้อนเท่า ๆ กัน ที่เหลือเก็บไว้รอบหน้า
        while (available >= ChunkSamples)
        {
            micClip.GetData(chunkBuffer, lastMicPosition);

            lastMicPosition += ChunkSamples;
            if (lastMicPosition >= micClip.samples) lastMicPosition -= micClip.samples;
            available -= ChunkSamples;

            ProcessChunk();
        }
    }

    private void ProcessChunk()
    {
        // ระดับเสียงวัดจากค่า RMS ก่อนคูณ gain — เกณฑ์ VAD จะได้ไม่เพี้ยนตามระดับที่ผู้เล่นตั้ง
        float sumSquares = 0f;
        for (int i = 0; i < ChunkSamples; i++)
            sumSquares += chunkBuffer[i] * chunkBuffer[i];

        float rms = Mathf.Sqrt(sumSquares / ChunkSamples);
        CurrentMicLevel = rms;

        bool wantsToTalk = talkMode == TalkMode.PushToTalk
            ? Input.GetKey(pushToTalkKey)
            : rms >= vadThreshold;

        IsTransmitting = wantsToTalk;
        if (!wantsToTalk) return;

        for (int i = 0; i < ChunkSamples; i++)
            encodeBuffer[i] = MuLaw.Encode(chunkBuffer[i] * micVolume);

        SendChunkToServer(encodeBuffer);
    }

    // ================================================================
    //  NETWORK — CustomMessagingManager (ไม่ต้องมี NetworkObject)
    // ================================================================

    private NetworkManager hookedManager;

    private void HookNetworkManagerIfNeeded()
    {
        NetworkManager nm = NetworkManager.Singleton;

        if (hookedManager != nm)
        {
            UnhookNetworkManager();

            hookedManager = nm;
            if (hookedManager != null)
            {
                hookedManager.OnClientStarted += OnNetworkStarted;
                hookedManager.OnServerStarted += OnNetworkStarted;
                hookedManager.OnClientStopped += OnNetworkStopped;
                hookedManager.OnServerStopped += OnNetworkStopped;
                // ⚠️ ห้ามใช้ OnClientDisconnectCallback ตรงนี้ — มันยิงเฉพาะฝั่ง Server
                // (และฝั่งตัวเองตอนเราหลุด) ฝั่ง Client จะไม่มีวันรู้ว่า "คนอื่น" ออกจากห้อง
                // ผลคือ VoicePeer ของคนที่ออกไปแล้วค้างอยู่ตลอด ทั้ง AudioSource ที่ยัง Play
                // และ ring buffer 64 KB ต่อคน — OnConnectionEvent ยิงครบทุกเครื่อง
                hookedManager.OnConnectionEvent += OnConnectionEvent;
            }
        }

        // ต่อสายเสร็จแล้วแต่ยังไม่ได้ลงทะเบียน handler (เช่นเข้ามากลางคัน)
        if (nm != null && nm.IsListening && !handlersRegistered)
            RegisterHandlers();
        else if ((nm == null || !nm.IsListening) && handlersRegistered)
            UnregisterHandlers();
    }

    private void UnhookNetworkManager()
    {
        if (hookedManager == null) return;

        hookedManager.OnClientStarted -= OnNetworkStarted;
        hookedManager.OnServerStarted -= OnNetworkStarted;
        hookedManager.OnClientStopped -= OnNetworkStopped;
        hookedManager.OnServerStopped -= OnNetworkStopped;
        hookedManager.OnConnectionEvent -= OnConnectionEvent;
        hookedManager = null;
    }

    private void OnNetworkStarted() => RegisterHandlers();

    private void OnNetworkStopped(bool _)
    {
        UnregisterHandlers();
        StopMicrophone();
        ClearAllPeers();
    }

    /// <summary>
    /// ยิงทุกเครื่อง: ClientDisconnected = เราหลุด / PeerDisconnected = คนอื่นหลุด
    /// Host จะได้รับทั้งสองแบบสำหรับ id เดียวกัน ซึ่งไม่เป็นไร RemovePeer กันซ้ำอยู่แล้ว
    /// </summary>
    private void OnConnectionEvent(NetworkManager manager, ConnectionEventData data)
    {
        if (data.EventType == ConnectionEvent.ClientDisconnected ||
            data.EventType == ConnectionEvent.PeerDisconnected)
        {
            RemovePeer(data.ClientId);
        }
    }

    private void RegisterHandlers()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || nm.CustomMessagingManager == null || handlersRegistered) return;

        // Server รับขาขึ้น / ทุกเครื่องรับขาลง (Host เป็นทั้งสองอย่าง จึงลงทะเบียนทั้งคู่)
        if (nm.IsServer)
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgUpstream, OnUpstreamReceived);

        nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgDownstream, OnDownstreamReceived);
        handlersRegistered = true;
    }

    private void UnregisterHandlers()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm != null && nm.CustomMessagingManager != null && handlersRegistered)
        {
            nm.CustomMessagingManager.UnregisterNamedMessageHandler(MsgUpstream);
            nm.CustomMessagingManager.UnregisterNamedMessageHandler(MsgDownstream);
        }

        handlersRegistered = false;
    }

    private void SendChunkToServer(byte[] payload)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || nm.CustomMessagingManager == null) return;

        using FastBufferWriter writer = new FastBufferWriter(payload.Length + 4, Allocator.Temp);
        writer.WriteValueSafe(payload.Length);
        writer.WriteBytesSafe(payload, payload.Length);

        // Unreliable — เสียงตกหล่นดีกว่าเสียงมาช้าเป็นพรืด
        nm.CustomMessagingManager.SendNamedMessage(
            MsgUpstream, NetworkManager.ServerClientId, writer, NetworkDelivery.Unreliable);
    }

    /// <summary>[SERVER] รับก้อนเสียงจากคนหนึ่ง แล้วกระจายต่อให้คนอื่นทั้งห้อง</summary>
    private void OnUpstreamReceived(ulong senderClientId, FastBufferReader reader)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        if (!TryReadPayload(reader, out byte[] payload)) return;

        // Host ได้ยินเสียงตัวเองไม่ได้ ต้องเล่นเฉพาะของคนอื่น -> ตัดผู้ส่งออกจากรายชื่อ
        List<ulong> targets = new List<ulong>();
        foreach (ulong id in nm.ConnectedClientsIds)
            if (id != senderClientId) targets.Add(id);

        if (targets.Count == 0) return;

        using FastBufferWriter writer =
            new FastBufferWriter(payload.Length + 12, Allocator.Temp);
        writer.WriteValueSafe(senderClientId);
        writer.WriteValueSafe(payload.Length);
        writer.WriteBytesSafe(payload, payload.Length);

        nm.CustomMessagingManager.SendNamedMessage(
            MsgDownstream, targets, writer, NetworkDelivery.Unreliable);
    }

    /// <summary>[ทุกเครื่อง] รับเสียงคนอื่นมาเล่น</summary>
    private void OnDownstreamReceived(ulong _, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ulong speakerId);
        if (!TryReadPayload(reader, out byte[] payload)) return;

        // ปิดหูอยู่ = ทิ้งทันที ไม่ต้องเสียแรง decode
        if (!speakerEnabled) return;

        VoicePeer peer = GetOrCreatePeer(speakerId);
        peer.Enqueue(payload);
    }

    private static bool TryReadPayload(FastBufferReader reader, out byte[] payload)
    {
        payload = null;

        if (!reader.TryBeginRead(sizeof(int))) return false;
        reader.ReadValueSafe(out int length);

        if (length <= 0 || length > ChunkSamples * 4) return false;
        if (!reader.TryBeginRead(length)) return false;

        payload = new byte[length];
        reader.ReadBytesSafe(ref payload, length);
        return true;
    }

    // ================================================================
    //  PLAYBACK — หนึ่ง AudioSource ต่อหนึ่งคนพูด
    // ================================================================

    private VoicePeer GetOrCreatePeer(ulong clientId)
    {
        if (peers.TryGetValue(clientId, out VoicePeer existing))
            return existing;

        VoicePeer peer = new VoicePeer(transform, clientId);
        peer.SetVolume(speakerEnabled ? speakerVolume : 0f);
        peers[clientId] = peer;
        return peer;
    }

    private void RemovePeer(ulong clientId)
    {
        if (!peers.TryGetValue(clientId, out VoicePeer peer)) return;

        peer.Dispose();
        peers.Remove(clientId);
    }

    private void ClearAllPeers()
    {
        foreach (VoicePeer peer in peers.Values)
            peer.Dispose();
        peers.Clear();
    }

    /// <summary>
    /// ช่องเสียงของผู้พูดหนึ่งคน
    /// ⚠️ PCMReaderCallback ถูกเรียกจาก "audio thread" ไม่ใช่ main thread
    ///    การอ่าน/เขียน ring buffer จึงต้องล็อกทุกครั้ง ห้ามแตะ Unity API ใน callback
    /// </summary>
    private sealed class VoicePeer
    {
        private readonly GameObject host;
        private readonly AudioSource source;
        private readonly AudioClip clip;

        private readonly float[] ring = new float[PlaybackBufferSamples];
        private readonly object gate = new object();
        private int writeIndex;
        private int available;

        public VoicePeer(Transform parent, ulong clientId)
        {
            host = new GameObject($"VoicePeer_{clientId}");
            host.transform.SetParent(parent, false);

            source = host.AddComponent<AudioSource>();
            source.spatialBlend = 0f;   // 2D — ได้ยินเท่ากันหมด ไม่อิงตำแหน่งในโลก
            source.loop = true;
            source.playOnAwake = false;

            // streaming clip: Unity จะเรียก ReadPcm มาดูดข้อมูลเองเรื่อย ๆ
            clip = AudioClip.Create($"Voice_{clientId}", SampleRate, 1, SampleRate, true, ReadPcm);
            source.clip = clip;
            source.Play();
        }

        public void SetVolume(float volume)
        {
            if (source != null) source.volume = Mathf.Clamp(volume, 0f, 2f);
        }

        // ค้างได้มากสุด 0.25 วิ ก่อนตัดทิ้ง — ค่านี้คือ "ดีเลย์สูงสุดที่ยอมได้"
        private const int MaxBacklogSamples = SampleRate / 4;

        public void Enqueue(byte[] muLaw)
        {
            lock (gate)
            {
                for (int i = 0; i < muLaw.Length; i++)
                {
                    ring[writeIndex] = MuLaw.Decode(muLaw[i]);
                    writeIndex = (writeIndex + 1) % ring.Length;
                }

                available = Mathf.Min(available + muLaw.Length, ring.Length);

                // ⚠️ ถ้าเกมค้าง (เช่นโหลดฉาก) แพ็กเก็ตจะกองมาถึงทีเดียวเป็นพรืด
                // ถ้าปล่อยไว้ ring จะบวมแล้ว "ดีเลย์ค้างสูง" ตลอดจนกว่าคนพูดจะหยุด
                // เพราะ ReadPcm ดูดออกได้เท่าเวลาจริงเสมอ ไล่ตามไม่มีวันทัน
                // ทิ้งของเก่าให้เหลือแค่ backlog ล่าสุด = ยอมขาดหายนิดเดียวแลกกับพูดแล้วได้ยินทันที
                if (available > MaxBacklogSamples)
                    available = MaxBacklogSamples;
            }
        }

        // รันบน audio thread
        private void ReadPcm(float[] data)
        {
            lock (gate)
            {
                int toRead = Mathf.Min(data.Length, available);
                int readIndex = writeIndex - available;
                if (readIndex < 0) readIndex += ring.Length;

                for (int i = 0; i < toRead; i++)
                {
                    data[i] = ring[readIndex];
                    readIndex = (readIndex + 1) % ring.Length;
                }

                // ไม่มีข้อมูลพอ = เติมความเงียบ ดีกว่าปล่อยค่าค้างแล้วได้เสียงหึ่ง
                for (int i = toRead; i < data.Length; i++)
                    data[i] = 0f;

                available -= toRead;
            }
        }

        public void Dispose()
        {
            if (source != null) source.Stop();
            if (clip != null) UnityEngine.Object.Destroy(clip);
            if (host != null) UnityEngine.Object.Destroy(host);
        }
    }

    /// <summary>
    /// G.711 μ-law — บีบ 16-bit ให้เหลือ 8-bit โดยเก็บรายละเอียดช่วงเสียงเบาไว้ดี
    /// (หูคนไวกับความต่างของเสียงเบามากกว่าเสียงดัง) ได้อัตราบีบครึ่งหนึ่งแบบไม่ต้องพึ่ง codec
    /// </summary>
    private static class MuLaw
    {
        private const int Bias = 0x84;
        private const int Clip = 32635;

        public static byte Encode(float sample)
        {
            int pcm = Mathf.Clamp(Mathf.RoundToInt(sample * 32767f), -32768, 32767);

            int sign = (pcm >> 8) & 0x80;
            if (sign != 0) pcm = -pcm;
            if (pcm > Clip) pcm = Clip;

            pcm += Bias;

            int exponent = 7;
            for (int mask = 0x4000; (pcm & mask) == 0 && exponent > 0; exponent--, mask >>= 1) { }

            int mantissa = (pcm >> (exponent + 3)) & 0x0F;
            return (byte)~(sign | (exponent << 4) | mantissa);
        }

        public static float Decode(byte value)
        {
            int u = ~value;
            int sign = u & 0x80;
            int exponent = (u >> 4) & 0x07;
            int mantissa = u & 0x0F;

            int sample = ((mantissa << 3) + Bias) << exponent;
            sample -= Bias;

            if (sign != 0) sample = -sample;
            return Mathf.Clamp(sample / 32768f, -1f, 1f);
        }
    }
}
