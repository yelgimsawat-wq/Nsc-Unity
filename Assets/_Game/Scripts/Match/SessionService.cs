using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Nsc.Match
{
    /// <summary>
    /// วงจรชีวิตของห้องออนไลน์ (Unity Gaming Services) — อยู่ตลอดอายุแอป (DontDestroyOnLoad)
    /// เดิมกระจายอยู่ใน OnlineNetworkUI (ล็อกอิน สร้าง/เข้า/ออกห้อง) กับ ReturnToMenuOnHostLost (ต่อกลับ)
    /// ซึ่งแชร์สถานะกันผ่าน static ที่ใครก็แก้ได้ — ตอนนี้สถานะอยู่ที่นี่ที่เดียว เมนูเป็นแค่หน้าจอ
    ///
    /// ต่อกลับ (ฝั่ง client เท่านั้น — Host หลุดแล้วห้องตาย NGO ไม่มี host migration):
    ///   1. ลองต่อกลับห้องเดิม (backoff 2 → 4 → 8 วินาที)
    ///   2. สำเร็จ → NGO sync ฉากจาก Host ให้เอง / LimbSelection คืนชิ้นส่วนเดิมให้
    ///   3. ครบจำนวนครั้งแล้วยังไม่ได้ → กลับเมนู
    /// </summary>
    public class SessionService : MonoBehaviour
    {
        private const int MaxReconnectAttempts = 5;
        private const float FirstReconnectDelay = 2f;
        private const string LocalPlayerIdKey = "Lobby_LocalPlayerId";

        public static SessionService Instance { get; private set; }

        private ISession session;
        private string joinCode = string.Empty;
        private string sessionId = string.Empty;
        private bool leavingIntentionally;
        private bool cancelReconnect;
        private NetworkManager subscribedManager;

        public ISession Session => session;
        public string JoinCode => joinCode;
        public bool IsSignedIn { get; private set; }
        public bool IsReconnecting { get; private set; }
        public bool InRoom => !string.IsNullOrWhiteSpace(joinCode);

        /// <summary>สถานะห้องเปลี่ยน (เข้า/ออก/ล็อกอิน)</summary>
        public event Action StateChanged;

        /// <summary>(กำลังต่อกลับ, ครั้งที่, จากทั้งหมด, ข้อความ)</summary>
        public event Action<bool, int, int, string> ReconnectStateChanged;

        /// <summary>หลุดจากห้องโดยไม่ได้ตั้งใจและจะไม่ต่อกลับ (wasHost = ห้องตัวเองปิด)</summary>
        public event Action<bool> ConnectionLost;

        /// <summary>
        /// ตัวตนถาวรของเครื่องนี้ — คงเดิมข้ามการต่อใหม่ ต่างจาก clientId
        /// ใช้บัญชี anonymous ของ UGS ถ้าพร้อม ไม่งั้นใช้ id ที่ปั่นเองแล้วเก็บไว้ในเครื่อง
        /// </summary>
        public static string LocalPlayerId
        {
            get
            {
                try
                {
                    var auth = AuthenticationService.Instance;
                    if (auth != null && auth.IsSignedIn && !string.IsNullOrEmpty(auth.PlayerId))
                        return auth.PlayerId;
                }
                catch { /* Services ยังไม่พร้อม */ }

                string saved = PlayerPrefs.GetString(LocalPlayerIdKey, string.Empty);
                if (string.IsNullOrEmpty(saved))
                {
                    saved = Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString(LocalPlayerIdKey, saved);
                    PlayerPrefs.Save();
                }
                return saved;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            GameObject go = new GameObject("[SessionService]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<SessionService>();
        }

        private void Update()
        {
            HandleDebugKeys();

            // NetworkManager เกิดทีหลังตอนเข้าเมนูได้ — คอยเช็คแล้วเกาะ event ให้ทันเสมอ
            NetworkManager network = NetworkManager.Singleton;
            if (network == null || network == subscribedManager) return;

            Unsubscribe();
            network.OnClientStopped += OnClientStopped;
            subscribedManager = network;
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (Instance == this) Instance = null;
        }

        private void Unsubscribe()
        {
            if (subscribedManager != null) subscribedManager.OnClientStopped -= OnClientStopped;
            subscribedManager = null;
        }

        // ================================================================
        //  ล็อกอิน / สร้างห้อง / เข้าห้อง / ออกห้อง
        // ================================================================

        public async Task EnsureSignedInAsync()
        {
            if (IsSignedIn) return;

            await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            IsSignedIn = true;
            StateChanged?.Invoke();
        }

        /// <summary>สร้างห้อง — ความจุถูกล็อกตอนสร้าง เปลี่ยนทีหลังไม่ได้</summary>
        public async Task<string> HostAsync(int maxPlayers)
        {
            // ห้ามสร้าง session บนสถานะ Netcode ที่ค้างจากครั้งก่อน
            await ResetNetworkAsync();

            try
            {
                var options = new SessionOptions { MaxPlayers = Mathf.Max(1, maxPlayers) }.WithRelayNetwork();
                session = await MultiplayerService.Instance.CreateSessionAsync(options);

                // Sessions API (WithRelayNetwork) start NetworkManager ให้เอง ถ้าไม่ start
                // แปลว่า transport ไม่มีข้อมูล relay — StartHost เองก็ไม่มีผล ถือว่าล้มเหลว
                if (!NetworkManager.Singleton.IsListening)
                    throw new Exception("Session created but Netcode did not start.");

                Remember(session);
                return joinCode;
            }
            catch
            {
                await ResetNetworkAsync();
                throw;
            }
        }

        public async Task JoinAsync(string code)
        {
            // attempt ก่อนที่ล้มเหลวทิ้ง NGO ค้างบน relay ที่ SDK ปิดไปแล้ว — join ซ้อนจะได้ client ที่ไม่ได้ต่อจริง
            await ResetNetworkAsync();

            try
            {
                session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
                if (!NetworkManager.Singleton.IsListening)
                    throw new Exception("Session joined but Netcode did not start.");

                Remember(session);
            }
            catch
            {
                await ResetNetworkAsync();
                throw;
            }
        }

        /// <summary>
        /// ออกจากห้องโดยตั้งใจ (ไม่ต่อกลับ) — Host = ลบห้องทิ้ง ทุกคนถูกเด้งตาม / Client = ออกเฉยๆ
        /// returnToMenu = โหลดฉากเมนูหลังออก (ปุ่ม LEAVE MATCH กลางเกม)
        /// ⚠️ ถ้าเครื่องนี้เป็น Host ผู้เรียกต้องถามยืนยันก่อนเสมอ
        /// </summary>
        public async Task LeaveAsync(bool returnToMenu = true)
        {
            cancelReconnect = true;
            await ResetNetworkAsync();
            await LeaveStaleSessionsAsync();

            joinCode = string.Empty;
            sessionId = string.Empty;
            StateChanged?.Invoke();

            UiFocus.Clear();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (returnToMenu && SceneManager.GetActiveScene().name != GameScenes.Menu)
                SceneManager.LoadScene(GameScenes.Menu, LoadSceneMode.Single);
        }

        public void CancelReconnect() => cancelReconnect = true;

        private void Remember(ISession joined)
        {
            joinCode = joined.Code;
            sessionId = joined.Id; // ReconnectToSessionAsync ใช้ Id ไม่ใช่ Code
            StateChanged?.Invoke();
        }

        // ================================================================
        //  หลุด / ต่อกลับ
        // ================================================================

        private void OnClientStopped(bool wasHost)
        {
            if (leavingIntentionally) return;

            if (wasHost)
            {
                _ = LeaveSessionAsync();
                joinCode = string.Empty;
                ConnectionLost?.Invoke(true);
                return;
            }

            if (IsReconnecting) return;

            if (!InRoom)
            {
                ConnectionLost?.Invoke(false);
                GoToMenu("ไม่มีรหัสห้องให้ต่อกลับ");
                return;
            }

            _ = ReconnectLoopAsync(joinCode);
        }

        private async Task ReconnectLoopAsync(string roomCode)
        {
            IsReconnecting = true;
            cancelReconnect = false;

            float delay = FirstReconnectDelay;
            bool success = false;

            for (int attempt = 1; attempt <= MaxReconnectAttempts && !cancelReconnect; attempt++)
            {
                NotifyReconnect(true, attempt, $"หลุดจากห้อง กำลังต่อกลับ... ({attempt}/{MaxReconnectAttempts})");

                float waitUntil = Time.unscaledTime + delay;
                while (Time.unscaledTime < waitUntil && !cancelReconnect)
                    await Task.Yield();
                if (cancelReconnect) break;

                try
                {
                    await LeaveStaleSessionsAsync();
                    await ResetNetworkAsync();

                    ISession rejoined = null;

                    // ✅ ReconnectToSessionAsync ก่อนเสมอ — คืนที่นั่งเดิมใน session ให้
                    // (JoinSessionByCodeAsync ทำเหมือนเข้าห้องใหม่ เคยได้ clientId=0 = กลายเป็น Host เอง)
                    if (!string.IsNullOrWhiteSpace(sessionId))
                    {
                        try
                        {
                            rejoined = await MultiplayerService.Instance.ReconnectToSessionAsync(
                                sessionId, new ReconnectSessionOptions());
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning("[Reconnect] ReconnectToSessionAsync ไม่ผ่าน: " + e.Message);
                        }
                    }

                    if (rejoined == null && !string.IsNullOrWhiteSpace(roomCode))
                        rejoined = await MultiplayerService.Instance.JoinSessionByCodeAsync(roomCode);

                    if (rejoined != null)
                    {
                        session = rejoined;
                        Remember(rejoined);
                    }

                    NetworkManager network = NetworkManager.Singleton;
                    if (network != null && network.IsListening)
                    {
                        // ต้องเป็น client จริง — กลายเป็น server แปลว่าไปสร้างห้องใหม่ ไม่ได้กลับห้องเดิม
                        if (network.IsServer)
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
                NotifyReconnect(false, MaxReconnectAttempts, "ต่อกลับสำเร็จ");
                return;
            }

            NotifyReconnect(false, MaxReconnectAttempts, cancelReconnect ? "ยกเลิกแล้ว" : "ต่อกลับไม่สำเร็จ");
            await ResetNetworkAsync();
            await LeaveSessionAsync();
            joinCode = string.Empty;
            StateChanged?.Invoke();
            GoToMenu(cancelReconnect ? "ผู้เล่นยกเลิกการต่อกลับ" : "ต่อกลับไม่สำเร็จ");
        }

        /// <summary>Host = ลบห้อง (คนอื่น join ต่อไม่ได้) / Client = ออกจากห้อง</summary>
        private async Task LeaveSessionAsync()
        {
            if (session == null) return;

            ISession leaving = session;
            session = null; // กันเรียกซ้ำระหว่าง await
            try
            {
                if (leaving.IsHost) await leaving.AsHost().DeleteAsync();
                else await leaving.LeaveAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Session] Failed to leave session cleanly: " + e.Message);
            }
        }

        /// <summary>
        /// ออกจาก session ค้างที่ SDK ยังถืออยู่ก่อนต่อใหม่
        /// ถ้าไม่ทำ SDK คิดว่ายังอยู่ห้องเดิม แล้ว join รอบใหม่ได้สถานะเพี้ยน (เคยเจอ client กลายเป็น Host)
        /// </summary>
        private async Task LeaveStaleSessionsAsync()
        {
            await LeaveSessionAsync();
            try
            {
                var sessions = MultiplayerService.Instance?.Sessions;
                if (sessions == null || sessions.Count == 0) return;

                foreach (ISession stale in new List<ISession>(sessions.Values))
                {
                    if (stale == null) continue;
                    try { await stale.LeaveAsync(); }
                    catch (Exception e) { Debug.LogWarning("[Session] ออกจาก session เก่าไม่สำเร็จ: " + e.Message); }
                }
            }
            catch (Exception e) { Debug.LogWarning("[Session] อ่านรายการ session ไม่ได้: " + e.Message); }
        }

        /// <summary>
        /// ล้างสถานะ NGO ให้สะอาด — ถ้ายังทำงานค้าง Sessions SDK จะข้ามการ start เอง
        /// แล้วได้ client ที่ไม่ได้ต่อกับ Host จริง (NGO ปิดจริงตอนท้ายเฟรม ต้องรอจนเสร็จ)
        /// </summary>
        private async Task ResetNetworkAsync()
        {
            NetworkManager network = NetworkManager.Singleton;
            if (network == null || (!network.IsListening && !network.ShutdownInProgress)) return;

            leavingIntentionally = true; // shutdown ของเราเอง — ห้ามพาไปต่อกลับ
            if (!network.ShutdownInProgress) network.Shutdown();

            for (int i = 0; i < 300 && (network.IsListening || network.ShutdownInProgress); i++)
                await Task.Yield();

            if (network.IsListening)
                Debug.LogWarning("[Session] NetworkManager did not shut down in time.");
            leavingIntentionally = false;
        }

        private void NotifyReconnect(bool reconnecting, int attempt, string message)
        {
            Debug.Log("[Reconnect] " + message);
            try { ReconnectStateChanged?.Invoke(reconnecting, attempt, MaxReconnectAttempts, message); }
            catch (Exception e) { Debug.LogWarning("[Reconnect] ผู้ฟัง event พัง: " + e.Message); }
        }

        private static void GoToMenu(string reason)
        {
            if (SceneManager.GetActiveScene().name == GameScenes.Menu) return;

            Debug.Log("[Session] " + reason + " — กลับสู่เมนูหลัก");
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SceneManager.LoadScene(GameScenes.Menu, LoadSceneMode.Single);
        }

        /// <summary>
        /// F9 = จำลองเน็ตหลุดแบบไม่ตั้งใจ ทดสอบ reconnect คนเดียวได้ — เฉพาะ Editor/Development Build
        /// </summary>
        private void HandleDebugKeys()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!Input.GetKeyDown(KeyCode.F9)) return;

            NetworkManager network = NetworkManager.Singleton;
            if (network == null || !network.IsListening)
            {
                Debug.Log("[Reconnect][F9] ยังไม่ได้ต่อห้อง — ไม่มีอะไรให้ตัด");
                return;
            }
            if (network.IsServer)
            {
                Debug.LogWarning("[Reconnect][F9] เครื่องนี้เป็น Host — ตัดแล้วห้องตาย ต่อกลับไม่ได้ ให้กด F9 บน Client แทน");
                return;
            }

            Debug.Log("[Reconnect][F9] 🔌 จำลองเน็ตหลุด — ระบบต่อกลับควรเริ่มทำงานทันที");
            network.Shutdown();
#endif
        }
    }
}
