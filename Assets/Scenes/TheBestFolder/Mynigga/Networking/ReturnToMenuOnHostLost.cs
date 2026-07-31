using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ตัวคุมการหลุด/ต่อกลับของฝั่ง Client — สร้างตัวเองตอนเกมเริ่มและอยู่ข้ามทุก scene
///
/// ทำไมต้องอยู่ตรงนี้ ไม่ใช่ใน OnlineNetworkUI:
///   OnlineNetworkUI อยู่เฉพาะในฉากเมนู พอกด Start เข้าเกมแล้วมันถูก unload ไป
///   ถ้าใส่ลอจิก reconnect ไว้ที่นั่น จะต่อกลับได้เฉพาะตอนอยู่ lobby เท่านั้น
///   ตัวนี้ DontDestroyOnLoad จึงคุมได้ทั้งตอนอยู่ lobby และตอนอยู่กลางเกม
///
/// ลำดับการทำงานเมื่อ Client หลุด:
///   1. ลองต่อกลับห้องเดิมด้วยรหัสห้องที่จำไว้ (backoff 2 → 4 → 8 วินาที)
///   2. ต่อกลับได้ → NGO sync ฉากจาก Host ให้เอง / LobbyManager คืนชิ้นส่วนเดิมให้
///   3. ครบจำนวนครั้งแล้วยังไม่ได้ → เด้งกลับเมนู (พฤติกรรมเดิม)
///
/// ⚠️ ใช้ได้เฉพาะฝั่ง Client — ถ้าเครื่องที่เป็น Host หลุด ห้องตายไปแล้ว
///    NGO ไม่รองรับ host migration จึงไม่มีทางกู้ห้องกลับมาได้
/// </summary>
public class ReturnToMenuOnHostLost : MonoBehaviour
{
    private const string MenuSceneName = "-MenuNOk";

    /// <summary>
    /// รหัสห้องล่าสุดที่เข้าไป — OnlineNetworkUI เป็นคนเซ็ตตอน Host/Join สำเร็จ
    /// เก็บเป็น static เพราะต้องรอดข้ามการเปลี่ยนฉากไปกับตัว service นี้
    /// </summary>
    public static string LastRoomCode { get; set; } = string.Empty;

    /// <summary>
    /// id ของ session ล่าสุด — ต่างจากรหัสห้องที่ผู้เล่นพิมพ์
    /// ⚠️ ReconnectToSessionAsync ต้องใช้ตัวนี้ ไม่ใช่ Code
    /// </summary>
    public static string LastSessionId { get; set; } = string.Empty;

    /// <summary>ผู้เล่นกดออกเอง — ห้ามพยายามต่อกลับ</summary>
    public static bool LeavingIntentionally { get; set; }

    /// <summary>true ระหว่างกำลังพยายามต่อกลับ (UI เอาไปโชว์สถานะได้)</summary>
    public static bool IsReconnecting { get; private set; }

    /// <summary>ยิงทุกครั้งที่สถานะเปลี่ยน — (กำลังต่อกลับ, ครั้งที่, จากทั้งหมด, ข้อความ)</summary>
    public static event Action<bool, int, int, string> OnReconnectStateChanged;

    /// <summary>ให้ UI สั่งยกเลิกการต่อกลับได้</summary>
    public static void CancelReconnect() => cancelRequested = true;

    /// <summary>
    /// ออกจากแมตช์เอง แล้วกลับเมนูหลัก — ใช้จากปุ่ม LEAVE MATCH ในเมนูระหว่างเล่น
    ///
    /// ตั้ง LeavingIntentionally ก่อนตัด เพื่อไม่ให้ระบบต่อกลับอัตโนมัติทำงาน
    /// (ไม่งั้นกดออกเองแล้วมันจะพยายามลากกลับเข้าห้องเดิมให้)
    ///
    /// ⚠️ ถ้าเครื่องนี้เป็น Host การตัดนี้ทำให้ห้องสลาย ทุกคนใน้องจะถูกเด้งกลับเมนูตาม
    ///    ผู้เรียกต้องถามยืนยันก่อนเสมอ
    /// </summary>
    public static async Task LeaveMatchAsync()
    {
        LeavingIntentionally = true;
        cancelRequested = true;

        NetworkManager nm = NetworkManager.Singleton;
        if (nm != null && nm.IsListening && !nm.ShutdownInProgress)
            nm.Shutdown();

        for (int i = 0; i < 300 && nm != null && (nm.IsListening || nm.ShutdownInProgress); i++)
            await Task.Yield();

        await LeaveStaleSessionsAsync();

        LastRoomCode = string.Empty;
        LastSessionId = string.Empty;
        LeavingIntentionally = false;

        UiFocus.Clear();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (SceneManager.GetActiveScene().name != MenuSceneName)
            SceneManager.LoadScene(MenuSceneName, LoadSceneMode.Single);
    }

    private const int MaxAttempts = 5;
    private const float FirstDelaySeconds = 2f;

    private static bool cancelRequested;
    private NetworkManager subscribedManager;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        GameObject go = new GameObject("[ReconnectService]");
        DontDestroyOnLoad(go);
        go.AddComponent<ReturnToMenuOnHostLost>();
    }

    private void Update()
    {
        HandleDebugKeys();

        // NetworkManager เกิดทีหลังตอนเข้าเมนูได้ — คอยเช็คแล้วเกาะ event ให้ทันเสมอ
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || nm == subscribedManager) return;

        Unsubscribe();
        nm.OnClientStopped += OnClientStopped;
        subscribedManager = nm;
    }

    /// <summary>
    /// F9 = จำลองเน็ตหลุดแบบไม่ตั้งใจ (ตัดการเชื่อมต่อโดยไม่ตั้ง LeavingIntentionally)
    /// ใช้ทดสอบ reconnect คนเดียวได้โดยไม่ต้องถอดสาย LAN จริง
    /// ⚠️ ทำงานเฉพาะใน Editor และ Development Build — ไม่ติดไปกับเกมจริง
    /// </summary>
    private void HandleDebugKeys()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!Input.GetKeyDown(KeyCode.F9)) return;

        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening)
        {
            Debug.Log("[Reconnect][F9] ยังไม่ได้ต่อห้อง — ไม่มีอะไรให้ตัด");
            return;
        }

        if (nm.IsHost || nm.IsServer)
        {
            Debug.LogWarning("[Reconnect][F9] เครื่องนี้เป็น Host — ตัดแล้วห้องตาย ต่อกลับไม่ได้ " +
                             "ให้กด F9 บนหน้าต่าง Virtual Player ที่เป็น Client แทน");
            return;
        }

        Debug.Log("[Reconnect][F9] 🔌 จำลองเน็ตหลุด — ระบบต่อกลับควรเริ่มทำงานทันที");
        LeavingIntentionally = false;   // ย้ำว่า "ไม่ได้ตั้งใจออก" เพื่อให้ reconnect ทำงาน
        nm.Shutdown();
#endif
    }

    private void OnDestroy() => Unsubscribe();

    private void Unsubscribe()
    {
        if (subscribedManager != null)
            subscribedManager.OnClientStopped -= OnClientStopped;
        subscribedManager = null;
    }

    private void OnClientStopped(bool wasHost)
    {
        // เครื่องที่เป็น Host เองมีวิธีออกของมันอยู่แล้ว และห้องตายไปแล้ว ต่อกลับไม่ได้
        if (wasHost) return;

        if (LeavingIntentionally)
        {
            LeavingIntentionally = false;
            return;
        }

        if (IsReconnecting) return;

        if (string.IsNullOrWhiteSpace(LastRoomCode))
        {
            GoToMenu("ไม่มีรหัสห้องให้ต่อกลับ");
            return;
        }

        _ = ReconnectLoopAsync(LastRoomCode);
    }

    private async Task ReconnectLoopAsync(string roomCode)
    {
        IsReconnecting = true;
        cancelRequested = false;

        float delay = FirstDelaySeconds;
        bool success = false;

        for (int attempt = 1; attempt <= MaxAttempts && !cancelRequested; attempt++)
        {
            Notify(true, attempt, MaxAttempts, $"หลุดจากห้อง กำลังต่อกลับ... ({attempt}/{MaxAttempts})");

            // รอแบบ backoff แต่ยังกดยกเลิกได้ระหว่างรอ
            float waitUntil = Time.unscaledTime + delay;
            while (Time.unscaledTime < waitUntil && !cancelRequested)
                await Task.Yield();
            if (cancelRequested) break;

            try
            {
                await LeaveStaleSessionsAsync();
                await ResetNetworkAsync();

                ISession rejoined = null;

                // ✅ ใช้ ReconnectToSessionAsync ก่อนเสมอ — เป็น API ที่ Unity ทำมาเพื่อการต่อกลับ
                //    โดยเฉพาะ มันคืนที่นั่งเดิมใน session ให้ ต่างจาก JoinSessionByCodeAsync
                //    ที่ทำเหมือนเข้าห้องใหม่ (เคยทดสอบแล้วได้ clientId=0 คือกลายเป็น Host เอง)
                if (!string.IsNullOrWhiteSpace(LastSessionId))
                {
                    try
                    {
                        rejoined = await MultiplayerService.Instance
                            .ReconnectToSessionAsync(LastSessionId, new ReconnectSessionOptions());
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[Reconnect] ReconnectToSessionAsync ไม่ผ่าน: " + e.Message);
                    }
                }

                // สำรอง: ถ้าไม่มี session id หรือ reconnect ไม่ผ่าน ค่อยเข้าด้วยรหัสห้องแบบปกติ
                if (rejoined == null && !string.IsNullOrWhiteSpace(roomCode))
                    rejoined = await MultiplayerService.Instance.JoinSessionByCodeAsync(roomCode);

                if (rejoined != null)
                {
                    LastSessionId = rejoined.Id;
                    LastRoomCode = rejoined.Code;
                }

                var nm = NetworkManager.Singleton;
                if (nm != null && nm.IsListening)
                {
                    // ⚠️ ต้องเป็น "client" จริงๆ — ถ้ากลายเป็น server แปลว่าไปสร้างห้องใหม่
                    //    ไม่ได้กลับเข้าห้องเดิม ถือว่าล้มเหลว ต้องลองใหม่
                    if (nm.IsServer)
                    {
                        Debug.LogWarning("[Reconnect] ต่อแล้วกลายเป็น Host เอง — ไม่ใช่การกลับห้องเดิม ลองใหม่");
                        await ResetNetworkAsync();
                    }
                    else
                    {
                        success = true;
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Reconnect] ครั้งที่ {attempt} ไม่สำเร็จ: {e.Message}");
            }

            delay = Mathf.Min(delay * 2f, 8f);
        }

        IsReconnecting = false;

        if (success)
        {
            Notify(false, MaxAttempts, MaxAttempts, "ต่อกลับสำเร็จ");
            Debug.Log("[Reconnect] ✅ กลับเข้าห้องเดิมสำเร็จ — Host จะ sync ฉากและคืนชิ้นส่วนให้เอง");
        }
        else
        {
            Notify(false, MaxAttempts, MaxAttempts, cancelRequested ? "ยกเลิกแล้ว" : "ต่อกลับไม่สำเร็จ");
            await ResetNetworkAsync();
            LastRoomCode = string.Empty;
            GoToMenu(cancelRequested ? "ผู้เล่นยกเลิกการต่อกลับ" : "ต่อกลับไม่สำเร็จ");
        }
    }

    /// <summary>
    /// ออกจาก session ค้างที่ SDK ยังถืออยู่ก่อนต่อใหม่
    /// ⚠️ ถ้าไม่ทำ SDK จะยังคิดว่าเราอยู่ในห้องเดิม แล้วการ join รอบใหม่จะได้สถานะเพี้ยน
    ///    (เคยทดสอบแล้วเจอ client กลายเป็น Host ของห้องตัวเอง)
    /// </summary>
    private static async Task LeaveStaleSessionsAsync()
    {
        try
        {
            var sessions = MultiplayerService.Instance?.Sessions;
            if (sessions == null || sessions.Count == 0) return;

            var stale = new List<ISession>(sessions.Values);
            foreach (var s in stale)
            {
                if (s == null) continue;
                try { await s.LeaveAsync(); }
                catch (Exception e) { Debug.LogWarning("[Reconnect] ออกจาก session เก่าไม่สำเร็จ: " + e.Message); }
            }
        }
        catch (Exception e) { Debug.LogWarning("[Reconnect] อ่านรายการ session ไม่ได้: " + e.Message); }
    }

    /// <summary>
    /// ล้างสถานะ NGO ให้สะอาดก่อนต่อใหม่ — ถ้าไม่ทำ Sessions SDK จะเห็นว่า
    /// NetworkManager ยังทำงานอยู่ แล้วข้ามการ start เอง สุดท้ายได้ client ที่ไม่ได้ต่อกับ Host จริง
    /// </summary>
    private static async Task ResetNetworkAsync()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || (!nm.IsListening && !nm.ShutdownInProgress)) return;

        LeavingIntentionally = true;   // กัน OnClientStopped ของการ shutdown นี้มายิงซ้ำ
        if (!nm.ShutdownInProgress) nm.Shutdown();

        for (int i = 0; i < 300 && (nm.IsListening || nm.ShutdownInProgress); i++)
            await Task.Yield();

        LeavingIntentionally = false;
    }

    private static void Notify(bool reconnecting, int attempt, int total, string message)
    {
        Debug.Log("[Reconnect] " + message);
        try { OnReconnectStateChanged?.Invoke(reconnecting, attempt, total, message); }
        catch (Exception e) { Debug.LogWarning("[Reconnect] ผู้ฟัง event พัง: " + e.Message); }
    }

    private void GoToMenu(string reason)
    {
        if (SceneManager.GetActiveScene().name == MenuSceneName) return;

        Debug.Log("[ReturnToMenu] " + reason + " — กลับสู่เมนูหลัก");

        // ปลดเมาส์เผื่อ scene เกมล็อกไว้ จะได้กดปุ่มในเมนูได้
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        SceneManager.LoadScene(MenuSceneName, LoadSceneMode.Single);
    }
}
