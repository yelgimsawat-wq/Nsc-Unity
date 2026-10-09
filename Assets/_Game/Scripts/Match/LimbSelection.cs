using System;
using System.Collections.Generic;
using Nsc.Robots;
using Unity.Netcode;
using UnityEngine;

namespace Nsc.Match
{
    /// <summary>
    /// "ใครคุมแขนขาชิ้นไหนของหุ่นตัวไหน และแมตช์เริ่มเมื่อไหร่" — แหล่งความจริงเดียวของการจองชิ้นส่วน
    /// ใช้ร่วมกันทั้ง co-op (หุ่นตัวเดียว) และ PVP (เลือกหุ่น = เลือกทีม) แทนโค้ดสองชุดเดิม
    /// (LobbyManager กับ PvpTeamManager) ที่ทำเรื่องเดียวกัน
    ///
    /// รับผิดชอบ: รายชื่อผู้เล่น + ที่นั่งรอต่อกลับ, การจอง, คำขอเริ่ม, โอน ownership ตอนเริ่ม,
    /// แช่แข็งหุ่นระหว่างเตรียมตัว — ไม่มี UI ในนี้ (LobbyManager / PvpTeamSelectUI เป็นแค่หน้าจอ)
    /// </summary>
    public class LimbSelection : NetworkBehaviour
    {
        public const ulong NoClient = ulong.MaxValue;

        public static LimbSelection Current { get; private set; }

        [Header("Rules")]
        [Tooltip("จำนวนผู้เล่นสูงสุดต่อหุ่นหนึ่งตัว (หุ่นมี 4 ชิ้นส่วน)")]
        [SerializeField, Range(1, 4)] private int maxPlayersPerRobot = 4;
        [Tooltip("PVP: ทุกหุ่นต้องมีคนอย่างน้อยหนึ่งคนถึงจะเริ่มได้")]
        [SerializeField] private bool requireEveryRobotManned;
        [Tooltip("PVP: ทุกคนที่เลือกหุ่นแล้วต้องจองชิ้นส่วนก่อนถึงจะเริ่มได้")]
        [SerializeField] private bool requireLimbForEveryPlayer;

        [Header("Robots")]
        [Tooltip("co-op: หุ่นที่ผู้เล่นคุม — ว่างไว้ = หุ่นตัวแรกที่ active ในฉาก")]
        [SerializeField] private Robot primaryRobot;
        [Tooltip("co-op: ปิดหุ่นตัวเกินในฉากอัตโนมัติ เหลือแค่หุ่นหลัก (ก็อปหุ่นไว้ทดลองในฉากได้โดยไม่ต้องนั่งปิดเอง)")]
        [SerializeField] private bool autoDisableExtraRobots = true;
        [Tooltip("แช่แข็งฟิสิกส์หุ่นจนกว่า Host กด Start (กันหุ่นล้ม/ไหลระหว่างเลือก)")]
        [SerializeField] private bool freezeRobotsWhilePreparing = true;

        [Header("Reconnect")]
        [Tooltip("กันชิ้นส่วนไว้ให้คนที่หลุดนานกี่วินาที ก่อนปล่อยให้คนอื่นเลือกได้")]
        [SerializeField, Min(0f)] private float seatHoldSeconds = 90f;

        private NetworkList<LimbAssignment> assignments;
        private NetworkList<SelectionPlayer> players;

        // server เท่านั้น: playerId → เวลาที่ที่นั่งจะหมดอายุ
        private readonly Dictionary<string, float> seatExpiry = new Dictionary<string, float>();
        private MatchSession session;

        /// <summary>ยิงบนทุกเครื่องเมื่อรายชื่อหรือการจองเปลี่ยน (UI ใช้ refresh)</summary>
        public event Action AssignmentsChanged;

        public int PlayerCount => players.Count;
        public SelectionPlayer GetPlayer(int index) => players[index];
        public int MaxPlayersPerRobot => maxPlayersPerRobot;

        /// <summary>หุ่นที่ผู้เล่นคุมในโหมด co-op (หุ่นตัวเดียวของฉาก)</summary>
        public Robot PrimaryRobot
        {
            get
            {
                if (primaryRobot != null && primaryRobot.isActiveAndEnabled) return primaryRobot;
                primaryRobot = null;
                foreach (Robot robot in Robot.All)
                    if (robot != null && robot.isActiveAndEnabled) return primaryRobot = robot;
                return null;
            }
        }

        private void Awake()
        {
            Current = this;
            // NetworkList ต้องสร้างก่อน OnNetworkSpawn
            assignments = new NetworkList<LimbAssignment>();
            players = new NetworkList<SelectionPlayer>();
        }

        private void Start()
        {
            // ทำใน Start — ตอน Awake หุ่นบางตัวยังไม่ได้ลงทะเบียน
            if (freezeRobotsWhilePreparing && (session == null || session.Phase == MatchPhase.Preparing))
                SetRobotsSimulation(false);
        }

        public override void OnDestroy()
        {
            if (Current == this) Current = null;
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            session = MatchSession.Current;
            if (session != null) session.PhaseChanged += OnPhaseChanged;

            assignments.OnListChanged += OnAssignmentsListChanged;
            players.OnListChanged += OnPlayersListChanged;

            if (IsServer)
            {
                DisableExtraRobots();
                NetworkManager.OnClientConnectedCallback += OnClientConnected;
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;

                // ชื่อ default ไว้ก่อน — เดี๋ยวแต่ละเครื่องส่งชื่อจริงตามมา
                foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
                    if (FindPlayer(clientId) < 0) players.Add(NewPlayer(clientId));
            }

            if (freezeRobotsWhilePreparing && (session == null || session.Phase == MatchPhase.Preparing))
                SetRobotsSimulation(false);

            // ทุกเครื่อง (รวม Host) ส่งชื่อจาก Settings + ตัวตนถาวรขึ้น server
            SubmitPlayerRpc(PlayerPrefs.GetString("PlayerName", "Player"), SessionService.LocalPlayerId);
            AssignmentsChanged?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            if (session != null) session.PhaseChanged -= OnPhaseChanged;
            assignments.OnListChanged -= OnAssignmentsListChanged;
            players.OnListChanged -= OnPlayersListChanged;

            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            }
            base.OnNetworkDespawn();
        }

        private void Update() => SweepExpiredSeats();

        private void OnAssignmentsListChanged(NetworkListEvent<LimbAssignment> _) => AssignmentsChanged?.Invoke();
        private void OnPlayersListChanged(NetworkListEvent<SelectionPlayer> _) => AssignmentsChanged?.Invoke();

        private void OnPhaseChanged(MatchPhase phase)
        {
            // ทุกเครื่อง: เริ่มเล่น = ปลดแช่แข็ง
            if (phase == MatchPhase.Playing && freezeRobotsWhilePreparing) SetRobotsSimulation(true);
        }

        // ================================================================
        //  คำถาม (ทุกเครื่อง)
        // ================================================================

        /// <summary>ชิ้นนี้ของหุ่นนี้ถูกใครจองไว้ — NoClient ถ้ายังว่าง</summary>
        public ulong GetController(ulong robotId, LimbSlot slot)
        {
            foreach (LimbAssignment assignment in assignments)
                if (assignment.robotId == robotId && assignment.slot == slot) return assignment.clientId;
            return NoClient;
        }

        public bool TryGetAssignment(ulong clientId, out Robot robot, out LimbSlot slot)
        {
            foreach (LimbAssignment assignment in assignments)
            {
                if (assignment.clientId != clientId) continue;
                robot = Robot.FindById(assignment.robotId);
                slot = assignment.slot;
                return robot != null;
            }

            robot = null;
            slot = default;
            return false;
        }

        public bool TryGetLocalAssignment(out Robot robot, out LimbSlot slot) =>
            TryGetAssignment(NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : NoClient,
                             out robot, out slot);

        /// <summary>หุ่นที่ผู้เล่นคนนี้เลือกอยู่ (ทีมใน PVP) — null ถ้ายังไม่เลือก</summary>
        public Robot GetRobotChoice(ulong clientId)
        {
            int index = FindPlayer(clientId);
            return index >= 0 && players[index].robotId != 0 ? Robot.FindById(players[index].robotId) : null;
        }

        public int CountPlayersOn(ulong robotId)
        {
            int count = 0;
            foreach (SelectionPlayer player in players)
                if (player.robotId == robotId) count++;
            return count;
        }

        public List<SelectionPlayer> PlayersOn(ulong robotId)
        {
            var result = new List<SelectionPlayer>();
            foreach (SelectionPlayer player in players)
                if (player.robotId == robotId) result.Add(player);
            return result;
        }

        /// <summary>ชิ้นที่ผู้เล่นคนนี้จองอยู่ (null = ยังไม่จอง)</summary>
        public LimbSlot? GetSlot(ulong clientId)
        {
            foreach (LimbAssignment assignment in assignments)
                if (assignment.clientId == clientId) return assignment.slot;
            return null;
        }

        /// <summary>Host กด Start ได้หรือยัง (ใช้ทั้งบน UI และตรวจซ้ำบน server)</summary>
        public bool CanStart(out string reason)
        {
            if (session != null && session.Phase != MatchPhase.Preparing)
            {
                reason = "Match already started";
                return false;
            }

            if (requireEveryRobotManned)
            {
                int manned = 0, robotsInScene = 0;
                foreach (Robot robot in Robot.All)
                {
                    if (robot == null || !robot.IsSpawned) continue;
                    robotsInScene++;
                    if (CountPlayersOn(robot.NetworkObjectId) > 0) manned++;
                }

                if (manned == 0)
                {
                    reason = "Nobody has picked a team yet";
                    return false;
                }
                if (manned < robotsInScene)
                {
                    reason = "Both teams need at least one player";
                    return false;
                }
            }

            if (requireLimbForEveryPlayer)
            {
                foreach (SelectionPlayer player in players)
                {
                    if (player.robotId != 0 && !GetSlot(player.clientId).HasValue)
                    {
                        reason = "Someone hasn't selected a part";
                        return false;
                    }
                }
            }

            reason = "Ready";
            return true;
        }

        // ================================================================
        //  คำขอจาก client (server ตัดสิน — ห้ามเชื่อค่าที่ client ส่งมา)
        // ================================================================

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SubmitPlayerRpc(string playerName, string playerId, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;

            if (string.IsNullOrWhiteSpace(playerName)) playerName = "Player";
            playerName = playerName.Trim();
            if (playerName.Length > 20) playerName = playerName.Substring(0, 20);
            playerId ??= string.Empty;
            if (playerId.Length > 60) playerId = playerId.Substring(0, 60);

            // ── [Reconnect] คนนี้เคยอยู่ในห้องแล้วหลุดไปหรือเปล่า ──
            int previous = FindPlayerByPlayerId(playerId);
            if (!string.IsNullOrEmpty(playerId) && previous >= 0 && players[previous].clientId != sender)
            {
                SelectionPlayer old = players[previous];
                ulong oldClientId = old.clientId;

                // ย้ายชิ้นส่วนที่กันไว้มาผูกกับ clientId ใหม่ — แมตช์เล่นอยู่ก็คืน ownership ให้ด้วย
                for (int i = 0; i < assignments.Count; i++)
                {
                    if (assignments[i].clientId != oldClientId) continue;
                    LimbAssignment moved = assignments[i];
                    moved.clientId = sender;
                    assignments[i] = moved;
                    if (session != null && session.IsPlaying) GiveOwnership(moved);
                }

                old.clientId = sender;
                old.playerName = playerName;
                old.connected = true;
                players[previous] = old;
                seatExpiry.Remove(playerId);

                // ลบ entry ซ้ำที่ OnClientConnected เพิ่งใส่ไว้ให้ clientId ใหม่
                for (int i = players.Count - 1; i >= 0; i--)
                    if (i != previous && players[i].clientId == sender) players.RemoveAt(i);

                Debug.Log($"[LimbSelection] ✅ playerId {playerId} กลับเข้าห้องแล้ว — คืนชิ้นส่วนเดิมให้ Client {sender}");
                return;
            }

            int index = FindPlayer(sender);
            SelectionPlayer entry = index >= 0 ? players[index] : NewPlayer(sender);
            entry.playerName = playerName;
            entry.playerId = playerId;
            entry.connected = true;
            if (index >= 0) players[index] = entry;
            else players.Add(entry);
        }

        /// <summary>เลือกหุ่น (= เลือกทีมใน PVP) — 0 = ยกเลิก / ย้ายหุ่นแล้วชิ้นที่จองไว้ถูกปล่อย</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestRobotRpc(ulong robotId, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!IsPreparing() || (robotId != 0 && Robot.FindById(robotId) == null)) return;

            int index = FindPlayer(sender);
            if (index < 0)
            {
                players.Add(NewPlayer(sender));
                index = players.Count - 1;
            }

            SelectionPlayer entry = players[index];
            if (entry.robotId == robotId) return;

            if (robotId != 0 && CountPlayersOn(robotId) >= maxPlayersPerRobot)
            {
                Debug.Log($"[LimbSelection] หุ่น {robotId} เต็มแล้ว — ปฏิเสธ client {sender}");
                return;
            }

            ReleaseAssignmentOf(sender);
            entry.robotId = robotId;
            players[index] = entry;
        }

        /// <summary>
        /// จองชิ้นส่วน — ชิ้นที่คนอื่นจองแล้วจองไม่ได้ / จองชิ้นใหม่ = ปล่อยชิ้นเก่าอัตโนมัติ
        /// toggleIfSame = กดชิ้นที่จองอยู่ซ้ำแล้วยกเลิก (หน้า PVP) / ลอบบี้ co-op กดซ้ำแล้วคงไว้
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestLimbRpc(ulong robotId, LimbSlot slot, bool toggleIfSame, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!IsPreparing() || !LimbSlots.IsValid((int)slot)) return;

            Robot robot = Robot.FindById(robotId);
            if (robot == null || robot.GetLimb(slot) == null)
            {
                Debug.LogWarning($"[LimbSelection] ปฏิเสธการจอง {slot} ของหุ่น {robotId} จาก client {sender}");
                return;
            }

            ulong owner = GetController(robotId, slot);
            if (owner != NoClient && owner != sender)
            {
                Debug.Log($"[LimbSelection] {slot} ของหุ่น {robotId} ถูกจองแล้ว");
                return;
            }

            int index = FindPlayer(sender);
            if (index < 0) return;

            SelectionPlayer entry = players[index];
            if (entry.robotId != robotId)
            {
                if (CountPlayersOn(robotId) >= maxPlayersPerRobot) return;
                entry.robotId = robotId;
                players[index] = entry;
            }

            bool wasMine = owner == sender;
            ReleaseAssignmentOf(sender);
            if (wasMine && toggleIfSame) return;

            assignments.Add(new LimbAssignment { robotId = robotId, slot = slot, clientId = sender });
            Debug.Log($"[LimbSelection] Client {sender} จอง {slot} ของหุ่น {robotId}");
        }

        /// <summary>Host กด Start — โอน ownership ชิ้นส่วนให้คนที่จองไว้ แล้วเริ่มแมตช์</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestStartRpc(RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId)
            {
                Debug.LogWarning($"[LimbSelection] Client {rpcParams.Receive.SenderClientId} พยายามสั่งเริ่มแมตช์ — ปฏิเสธ");
                return;
            }

            if (!CanStart(out string reason))
            {
                Debug.Log($"[LimbSelection] เริ่มแมตช์ไม่ได้: {reason}");
                return;
            }

            foreach (LimbAssignment assignment in assignments) GiveOwnership(assignment);

            if (session != null) session.ServerBeginPlaying();
            Debug.Log("[LimbSelection] Game started — ownership transferred.");
        }

        // ================================================================
        //  Server internals
        // ================================================================

        private bool IsPreparing() => session == null || session.Phase == MatchPhase.Preparing;

        private static SelectionPlayer NewPlayer(ulong clientId) =>
            new SelectionPlayer { clientId = clientId, playerName = "Player", connected = true };

        private int FindPlayer(ulong clientId)
        {
            for (int i = 0; i < players.Count; i++)
                if (players[i].clientId == clientId) return i;
            return -1;
        }

        private int FindPlayerByPlayerId(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return -1;
            for (int i = 0; i < players.Count; i++)
                if (players[i].playerId.ToString() == playerId) return i;
            return -1;
        }

        private void ReleaseAssignmentOf(ulong clientId)
        {
            for (int i = assignments.Count - 1; i >= 0; i--)
                if (assignments[i].clientId == clientId) assignments.RemoveAt(i);
        }

        private static void GiveOwnership(LimbAssignment assignment)
        {
            Robot robot = Robot.FindById(assignment.robotId);
            RobotLimb limb = robot != null ? robot.GetLimb(assignment.slot) : null;
            NetworkObject limbObject = limb != null ? limb.GetComponent<NetworkObject>() : null;
            if (limbObject != null && limbObject.IsSpawned) limbObject.ChangeOwnership(assignment.clientId);
        }

        private void OnClientConnected(ulong clientId)
        {
            // ยังไม่รู้ playerId — client จะส่งตามมาใน SubmitPlayerRpc ตรงนั้นค่อยเช็คว่าเป็นคนเดิมที่หลุดไปไหม
            if (FindPlayer(clientId) < 0) players.Add(NewPlayer(clientId));
        }

        private void OnClientDisconnected(ulong clientId)
        {
            int index = FindPlayer(clientId);
            if (index < 0) return;

            SelectionPlayer entry = players[index];

            // ไม่รู้ว่าเป็นใคร (ยังไม่ทันส่ง playerId) → เอาออกเลย
            if (entry.playerId.Length == 0)
            {
                players.RemoveAt(index);
                ReleaseAssignmentOf(clientId);
                return;
            }

            // รู้ว่าเป็นใคร → กันที่ไว้ ไม่ปล่อยชิ้นส่วนทันที (UI ยังโชว์ว่าถูกจอง คนอื่นแย่งไม่ได้)
            entry.connected = false;
            players[index] = entry;
            seatExpiry[entry.playerId.ToString()] = Time.unscaledTime + seatHoldSeconds;
            Debug.Log($"[LimbSelection] Client {clientId} หลุด — กันที่ไว้ {seatHoldSeconds:F0} วินาที (playerId {entry.playerId})");
        }

        private void SweepExpiredSeats()
        {
            if (!IsServer || seatExpiry.Count == 0) return;

            List<string> expired = null;
            foreach (KeyValuePair<string, float> seat in seatExpiry)
                if (Time.unscaledTime >= seat.Value) (expired ??= new List<string>()).Add(seat.Key);
            if (expired == null) return;

            foreach (string playerId in expired)
            {
                seatExpiry.Remove(playerId);
                int index = FindPlayerByPlayerId(playerId);
                if (index < 0) continue;

                ulong stale = players[index].clientId;
                players.RemoveAt(index);
                ReleaseAssignmentOf(stale);
                Debug.Log($"[LimbSelection] หมดเวลารอ playerId {playerId} — ปล่อยชิ้นส่วนให้คนอื่นเลือกได้แล้ว");
            }
        }

        // ================================================================
        //  หุ่นในฉาก
        // ================================================================

        /// <summary>co-op: ถอดหุ่นตัวเกินออกจากเกม (server) — despawn ก่อนให้ client ทุกเครื่องเห็นตรงกัน แล้วปิด</summary>
        private void DisableExtraRobots()
        {
            if (!autoDisableExtraRobots) return;

            Robot keep = PrimaryRobot;
            if (keep == null) return;

            foreach (Robot robot in new List<Robot>(Robot.All))
            {
                if (robot == keep || robot.Root == null) continue;

                foreach (NetworkObject networkObject in robot.Root.GetComponentsInChildren<NetworkObject>(true))
                    if (networkObject.IsSpawned) networkObject.Despawn(false);

                robot.Root.gameObject.SetActive(false);
                Debug.Log($"[LimbSelection] 🤖 ปิดหุ่นตัวเกินอัตโนมัติ: {robot.Root.name}");
            }
        }

        private void SetRobotsSimulation(bool enabled)
        {
            if (autoDisableExtraRobots)
            {
                Robot robot = PrimaryRobot;
                if (robot != null) robot.SetSimulationEnabled(enabled);
                return;
            }

            foreach (Robot robot in Robot.All)
                if (robot != null) robot.SetSimulationEnabled(enabled);
        }
    }
}
