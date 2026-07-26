using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace NscGame.Pvp
{
    /// <summary>
    /// สมองของโหมด PVP — วางบน GameObject ที่มี NetworkObject ตัวเดียวในฉาก PVP
    ///
    /// หน้าที่:
    ///   1. เก็บว่าใครอยู่ทีมไหน + จองชิ้นส่วนอะไรของหุ่นทีมตัวเอง (NetworkList = ทุกเครื่องเห็นตรงกัน)
    ///   2. Host กด START → โอน ownership ชิ้นส่วนให้เจ้าของ + เสียบกล้อง → เริ่มสู้
    ///   3. รับรายงานว่าหุ่นทีมไหนพัง → ประกาศผู้ชนะ
    ///
    /// โครงหุ่นในโปรเจกต์นี้: ผู้เล่นหนึ่งคน = ชิ้นส่วนหนึ่งชิ้นของหุ่น (แขนซ้าย/ขวา, ขาซ้าย/ขวา)
    /// โหมด PVP จึงคือ "หุ่นแดง vs หุ่นน้ำเงิน" ที่แต่ละตัวมีคนช่วยกันคุมได้สูงสุด 4 คน
    ///
    /// ⚠️ ฉาก PVP ต้องไม่มี LobbyManager อยู่ด้วย เพราะ LobbyManager จะ "ปิดหุ่นตัวเกิน"
    ///    อัตโนมัติ (DisableExtraRobots) ทำให้เหลือหุ่นตัวเดียว สู้กันไม่ได้
    /// </summary>
    public class PvpTeamManager : NetworkBehaviour
    {
        public static PvpTeamManager Instance { get; private set; }

        #region Inspector

        [Header("Robots (ลาก root ของหุ่นแต่ละทีมมาใส่)")]
        [SerializeField] private Transform redRobotRoot;
        [SerializeField] private Transform blueRobotRoot;

        [Header("Rules")]
        [Tooltip("จำนวนผู้เล่นสูงสุดต่อทีม (หุ่นมี 4 ชิ้นส่วน)")]
        [SerializeField, Range(1, 4)] private int maxPlayersPerTeam = 4;

        [Tooltip("ต้องมีคนอยู่ครบทั้งสองทีมถึงจะกด START ได้")]
        [SerializeField] private bool requireBothTeams = true;

        [Tooltip("ต้องจองชิ้นส่วนก่อนถึงจะนับว่าพร้อม (ปิด = เลือกแค่ทีมก็เริ่มได้ แต่จะไม่ได้คุมอะไร)")]
        [SerializeField] private bool requireLimbSelection = true;

        [Header("Debug")]
        [SerializeField] private bool debugLog = true;

        #endregion

        #region Network State

        // แหล่งความจริงเดียวของ "ใครอยู่ทีมไหน จองชิ้นไหน"
        private readonly NetworkList<PvpPlayerEntry> players = new NetworkList<PvpPlayerEntry>();

        private readonly NetworkVariable<PvpMatchState> netMatchState = new NetworkVariable<PvpMatchState>(
            PvpMatchState.TeamSelect,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<PvpTeam> netWinner = new NetworkVariable<PvpTeam>(
            PvpTeam.None,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        #endregion

        #region Public API / Events

        /// <summary>ยิงทุกครั้งที่ roster เปลี่ยน (มีคนเลือกทีม/ชิ้นส่วน/เข้า/ออก) — UI ใช้ refresh</summary>
        public event Action OnRosterChanged;
        public event Action<PvpMatchState> OnMatchStateChanged;
        public event Action<PvpTeam> OnWinnerDecided;

        public PvpMatchState MatchState => netMatchState.Value;

        /// <summary>แมตช์กำลังสู้กันอยู่หรือเปล่า — ใช้เป็นสวิตช์ว่าดาเมจนับหรือไม่</summary>
        public bool IsFighting => netMatchState.Value == PvpMatchState.Fighting;

        public PvpTeam Winner => netWinner.Value;
        public int PlayerCount => players.Count;
        public int MaxPlayersPerTeam => maxPlayersPerTeam;

        public PvpPlayerEntry GetEntryAt(int index) => players[index];

        public PvpTeam GetTeam(ulong clientId)
        {
            int i = FindIndex(clientId);
            return i < 0 ? PvpTeam.None : players[i].team;
        }

        public int GetLimbIndex(ulong clientId)
        {
            int i = FindIndex(clientId);
            return i < 0 ? -1 : players[i].limbIndex;
        }

        public PvpTeam LocalTeam => NetworkManager.Singleton != null
            ? GetTeam(NetworkManager.Singleton.LocalClientId)
            : PvpTeam.None;

        public int LocalLimbIndex => NetworkManager.Singleton != null
            ? GetLimbIndex(NetworkManager.Singleton.LocalClientId)
            : -1;

        public int CountTeam(PvpTeam team)
        {
            int n = 0;
            for (int i = 0; i < players.Count; i++)
                if (players[i].team == team) n++;
            return n;
        }

        /// <summary>ค่าที่ <see cref="GetLimbOwner"/> คืนเมื่อชิ้นส่วนนั้นยังไม่มีใครจอง</summary>
        public const ulong NoOwner = ulong.MaxValue;

        /// <summary>ชิ้นส่วนนี้ของทีมนี้ถูกใครจองไว้? คืน <see cref="NoOwner"/> ถ้ายังว่าง</summary>
        public ulong GetLimbOwner(PvpTeam team, int limbIndex)
        {
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].team == team && players[i].limbIndex == limbIndex)
                    return players[i].clientId;
            }
            return NoOwner;
        }

        /// <summary>รายชื่อผู้เล่นในทีม (ใช้วาด roster บน UI)</summary>
        public List<PvpPlayerEntry> GetTeamRoster(PvpTeam team)
        {
            List<PvpPlayerEntry> result = new List<PvpPlayerEntry>();
            for (int i = 0; i < players.Count; i++)
                if (players[i].team == team) result.Add(players[i]);
            return result;
        }

        /// <summary>Host กด START ได้หรือยัง (ใช้ทั้งบน UI และ validate ซ้ำบน server)</summary>
        public bool CanStartMatch(out string reason)
        {
            if (netMatchState.Value != PvpMatchState.TeamSelect)
            {
                reason = "Match already started";
                return false;
            }

            int red = CountTeam(PvpTeam.Red);
            int blue = CountTeam(PvpTeam.Blue);

            if (requireBothTeams && (red == 0 || blue == 0))
            {
                reason = "Both teams need at least one player";
                return false;
            }

            if (red == 0 && blue == 0)
            {
                reason = "Nobody has picked a team yet";
                return false;
            }

            if (requireLimbSelection)
            {
                for (int i = 0; i < players.Count; i++)
                {
                    if (players[i].team != PvpTeam.None && !PvpLimb.IsValid(players[i].limbIndex))
                    {
                        reason = "Someone hasn't selected a part";
                        return false;
                    }
                }
            }

            reason = "Ready";
            return true;
        }

        #endregion

        #region Unity / Netcode Lifecycle

        private void Awake()
        {
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            players.OnListChanged += HandleRosterChanged;
            netMatchState.OnValueChanged += HandleMatchStateChanged;
            netWinner.OnValueChanged += HandleWinnerChanged;

            if (IsServer)
            {
                NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

                foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
                {
                    if (FindIndex(clientId) < 0)
                        players.Add(PvpPlayerEntry.Create(clientId));
                }
            }

            // ทุกเครื่องส่งชื่อจริงของตัวเองขึ้น server (แหล่งเดียวกับ LobbyManager)
            SubmitNameRpc(PlayerPrefs.GetString("PlayerName", "Player"));

            OnRosterChanged?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            players.OnListChanged -= HandleRosterChanged;
            netMatchState.OnValueChanged -= HandleMatchStateChanged;
            netWinner.OnValueChanged -= HandleWinnerChanged;

            if (IsServer && NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            }

            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        private void HandleRosterChanged(NetworkListEvent<PvpPlayerEntry> _) => OnRosterChanged?.Invoke();

        private void HandleMatchStateChanged(PvpMatchState _, PvpMatchState newState)
        {
            OnMatchStateChanged?.Invoke(newState);
            OnRosterChanged?.Invoke();
        }

        private void HandleWinnerChanged(PvpTeam _, PvpTeam newWinner)
        {
            if (newWinner != PvpTeam.None) OnWinnerDecided?.Invoke(newWinner);
        }

        private void OnClientConnected(ulong clientId)
        {
            if (!IsServer) return;
            if (FindIndex(clientId) < 0)
                players.Add(PvpPlayerEntry.Create(clientId));
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (!IsServer) return;
            int i = FindIndex(clientId);
            if (i >= 0) players.RemoveAt(i); // ชิ้นส่วนที่จองไว้ว่างเองอัตโนมัติ เพราะ ownership ผูกกับ entry
        }

        private int FindIndex(ulong clientId)
        {
            for (int i = 0; i < players.Count; i++)
                if (players[i].clientId == clientId) return i;
            return -1;
        }

        #endregion

        #region Server RPC — เลือกทีม / เลือกชิ้นส่วน

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SubmitNameRpc(string playerName, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;

            // 🛡️ Server validation — ห้ามเชื่อ string จาก client ตรงๆ (FixedString64 มีขีดจำกัด)
            if (string.IsNullOrWhiteSpace(playerName)) playerName = "Player";
            playerName = playerName.Trim();
            if (playerName.Length > 20) playerName = playerName.Substring(0, 20);

            int i = FindIndex(sender);
            if (i < 0)
            {
                PvpPlayerEntry fresh = PvpPlayerEntry.Create(sender, playerName);
                players.Add(fresh);
                return;
            }

            PvpPlayerEntry entry = players[i];
            entry.playerName = playerName;
            players[i] = entry;
        }

        /// <summary>ผู้เล่นกดปุ่มเลือกทีม — server เป็นคนตัดสินว่าเข้าได้ไหม</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestTeamRpc(PvpTeam wanted, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;

            if (netMatchState.Value != PvpMatchState.TeamSelect)
            {
                Log($"ปฏิเสธการเลือกทีมของ {sender} — แมตช์เริ่มไปแล้ว");
                return;
            }

            if (wanted != PvpTeam.Red && wanted != PvpTeam.Blue && wanted != PvpTeam.None)
                return;

            int i = FindIndex(sender);
            if (i < 0)
            {
                players.Add(PvpPlayerEntry.Create(sender));
                i = players.Count - 1;
            }

            PvpPlayerEntry entry = players[i];
            if (entry.team == wanted) return;

            // 🛡️ ทีมเต็มแล้วเข้าไม่ได้
            if (wanted != PvpTeam.None && CountTeam(wanted) >= maxPlayersPerTeam)
            {
                Log($"ทีม {wanted.DisplayName()} เต็มแล้ว — ปฏิเสธ client {sender}");
                return;
            }

            // ย้ายทีม = ปล่อยชิ้นส่วนที่จองไว้ในทีมเดิมเสมอ (index ของแต่ละทีมเป็นคนละชุดกัน)
            entry.team = wanted;
            entry.limbIndex = -1;
            players[i] = entry;

            Log($"Client {sender} เข้าทีม {wanted.DisplayName()}");
        }

        /// <summary>ผู้เล่นกดจองชิ้นส่วนของหุ่นทีมตัวเอง</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestLimbRpc(int limbIndex, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;

            if (netMatchState.Value != PvpMatchState.TeamSelect) return;

            // 🛡️ ห้ามเชื่อ index จาก client
            if (!PvpLimb.IsValid(limbIndex))
            {
                Debug.LogWarning($"[PVP] ปฏิเสธ limb index {limbIndex} จาก client {sender}");
                return;
            }

            int i = FindIndex(sender);
            if (i < 0) return;

            PvpPlayerEntry entry = players[i];
            if (entry.team == PvpTeam.None)
            {
                Log($"Client {sender} ยังไม่ได้เลือกทีม — จองชิ้นส่วนไม่ได้");
                return;
            }

            ulong currentOwner = GetLimbOwner(entry.team, limbIndex);
            if (currentOwner != ulong.MaxValue && currentOwner != sender)
            {
                Log($"ชิ้นส่วน {PvpLimb.Name(limbIndex)} ของทีม {entry.team.DisplayName()} ถูกจองแล้ว");
                return;
            }

            entry.limbIndex = entry.limbIndex == limbIndex ? -1 : limbIndex; // กดซ้ำ = ยกเลิก
            players[i] = entry;

            Log($"Client {sender} จอง {(entry.limbIndex < 0 ? "ยกเลิก" : PvpLimb.Name(entry.limbIndex))} " +
                $"ในทีม {entry.team.DisplayName()}");
        }

        /// <summary>Host กดปุ่ม START</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestStartMatchRpc(RpcParams rpcParams = default)
        {
            // 🛡️ เฉพาะ Host เท่านั้นที่เริ่มแมตช์ได้
            if (rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId)
            {
                Debug.LogWarning($"[PVP] Client {rpcParams.Receive.SenderClientId} พยายามสั่งเริ่มแมตช์ — ปฏิเสธ");
                return;
            }

            if (!CanStartMatch(out string reason))
            {
                Log($"เริ่มแมตช์ไม่ได้: {reason}");
                return;
            }

            ServerStartMatch();
        }

        #endregion

        #region Server — Match Flow

        private void ServerStartMatch()
        {
            if (!IsServer) return;

            netWinner.Value = PvpTeam.None;
            netMatchState.Value = PvpMatchState.Fighting;

            // ล้างสถานะแพ้ + อ่านรายชื่อชิ้นส่วนใหม่ก่อนเริ่ม
            // (เลือดไม่ต้องรีเซ็ตเอง — RobotHealth เดิมดูแลของมันเอง)
            foreach (PvpTeam team in new[] { PvpTeam.Red, PvpTeam.Blue })
            {
                PvpRobotTeam robot = PvpRobotTeam.FindByTeam(team);
                if (robot != null) robot.ServerResetDefeat();
            }

            // โอน ownership ชิ้นส่วนให้เจ้าของที่จองไว้ — client ถึงจะส่ง input คุมชิ้นนั้นได้
            for (int i = 0; i < players.Count; i++)
            {
                PvpPlayerEntry entry = players[i];
                if (entry.team == PvpTeam.None || !PvpLimb.IsValid(entry.limbIndex)) continue;

                GameObject limb = GetLimb(entry.team, entry.limbIndex);
                if (limb == null)
                {
                    Debug.LogWarning($"[PVP] หาชิ้นส่วน {PvpLimb.Name(entry.limbIndex)} ของทีม {entry.team.DisplayName()} ไม่เจอ");
                    continue;
                }

                NetworkObject no = limb.GetComponent<NetworkObject>();
                if (no != null && no.IsSpawned) no.ChangeOwnership(entry.clientId);
            }


            StartMatchClientRpc();
            Log("🔔 แมตช์เริ่มแล้ว!");
        }

        /// <summary>[SERVER] เรียกจาก <see cref="PvpRobotTeam"/> เมื่อหุ่นทีมหนึ่งชิ้นส่วนหลุดครบตามเงื่อนไข</summary>
        public void ServerReportDefeat(PvpTeam defeatedTeam)
        {
            if (!IsServer) return;
            if (netMatchState.Value != PvpMatchState.Fighting) return;

            netWinner.Value = defeatedTeam.Opposite();
            netMatchState.Value = PvpMatchState.Finished;

            Log($"🏆 ทีม {netWinner.Value.DisplayName()} ชนะ!");
        }

        [ClientRpc]
        private void StartMatchClientRpc()
        {
            StartCoroutine(AssignLimbsToOwners());
        }

        /// <summary>
        /// เสียบกล้อง + เปิดสคริปต์ควบคุมให้เจ้าของชิ้นส่วน
        /// retry เพราะ PlayerObject ของบางเครื่องอาจ spawn ไม่ทันจังหวะ RPC มาถึง
        /// </summary>
        private IEnumerator AssignLimbsToOwners(int maxAttempts = 10, float retryDelay = 0.2f)
        {
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                if (TryAssignLocalLimb()) break;
                yield return new WaitForSeconds(retryDelay);
            }
        }

        private bool TryAssignLocalLimb()
        {
            if (NetworkManager.Singleton == null) return false;

            ulong localId = NetworkManager.Singleton.LocalClientId;
            int index = FindIndex(localId);
            if (index < 0) return false;

            PvpPlayerEntry entry = players[index];
            if (entry.team == PvpTeam.None || !PvpLimb.IsValid(entry.limbIndex))
                return true; // ไม่ได้เลือกอะไร = ไม่มีอะไรให้เสียบ ถือว่าจบ

            GameObject playerObj = GetPlayerObject(localId);
            if (playerObj == null) return false; // ยัง spawn ไม่เสร็จ — ลองใหม่

            GameObject limb = GetLimb(entry.team, entry.limbIndex);
            if (limb == null) return false;

            Camera playerCam = playerObj.GetComponentInChildren<Camera>();
            if (playerCam == null) playerCam = Camera.main;

            PlayerCam camScript = playerObj.GetComponent<PlayerCam>();
            if (camScript != null) camScript.followTarget = limb.transform;

            if (PvpLimb.IsArm(entry.limbIndex))
            {
                PlayerHandMovement hand = limb.GetComponent<PlayerHandMovement>();
                if (hand != null)
                {
                    hand.playerCamera = playerCam;
                    hand.enabled = true;
                }
            }
            else
            {
                PlayerFootForRobot foot = limb.GetComponent<PlayerFootForRobot>();
                if (foot != null)
                {
                    foot.playerCamera = playerCam;
                    foot.enabled = true;
                }
            }

            Log($"เสียบกล้อง/คอนโทรลให้ {PvpLimb.Name(entry.limbIndex)} ของทีม {entry.team.DisplayName()} แล้ว");
            return true;
        }

        private static GameObject GetPlayerObject(ulong clientId)
        {
            if (NetworkManager.Singleton == null) return null;

            if (clientId == NetworkManager.Singleton.LocalClientId &&
                NetworkManager.Singleton.LocalClient?.PlayerObject != null)
                return NetworkManager.Singleton.LocalClient.PlayerObject.gameObject;

            foreach (NetworkObject netObj in NetworkManager.Singleton.SpawnManager.SpawnedObjectsList)
                if (netObj.IsPlayerObject && netObj.OwnerClientId == clientId)
                    return netObj.gameObject;

            return null;
        }

        #endregion

        #region Robot / Limb Resolution

        private GameObject[] _redLimbs;
        private GameObject[] _blueLimbs;

        public Transform GetRobotRoot(PvpTeam team) => team == PvpTeam.Red ? redRobotRoot : blueRobotRoot;

        /// <summary>คืน GameObject ของชิ้นส่วน index นั้นในหุ่นของทีมที่ระบุ</summary>
        public GameObject GetLimb(PvpTeam team, int limbIndex)
        {
            if (!PvpLimb.IsValid(limbIndex)) return null;

            GameObject[] cache = team == PvpTeam.Red ? _redLimbs : _blueLimbs;
            if (cache == null || cache[limbIndex] == null)
            {
                cache = ResolveLimbs(GetRobotRoot(team));
                if (team == PvpTeam.Red) _redLimbs = cache; else _blueLimbs = cache;
            }

            return cache?[limbIndex];
        }

        /// <summary>
        /// จับคู่ชิ้นส่วนจากชื่อ (_L / Left / _R / Right) — ถ้าชื่อไม่เข้า convention
        /// จะเติมตามลำดับที่เจอแทน เพื่อไม่ให้หุ่นที่ตั้งชื่อไม่ตรงมาตรฐานพังทั้งระบบ
        /// </summary>
        private GameObject[] ResolveLimbs(Transform root)
        {
            if (root == null) return null;

            GameObject[] result = new GameObject[PvpLimb.Count];

            AssignByName(root.GetComponentsInChildren<PlayerHandMovement>(true),
                result, PvpLimb.LeftArm, PvpLimb.RightArm);
            AssignByName(root.GetComponentsInChildren<PlayerFootForRobot>(true),
                result, PvpLimb.LeftLeg, PvpLimb.RightLeg);

            Log($"หุ่น '{root.name}' → L.ARM={Nm(result[0])} R.ARM={Nm(result[1])} " +
                $"L.LEG={Nm(result[2])} R.LEG={Nm(result[3])}");

            return result;

            static string Nm(GameObject go) => go != null ? go.name : "?";
        }

        private static void AssignByName<T>(T[] parts, GameObject[] result,
            int leftSlot, int rightSlot) where T : Component
        {
            List<GameObject> leftovers = new List<GameObject>();

            foreach (T part in parts)
            {
                if (part == null) continue;
                GameObject go = part.gameObject;
                string n = go.name;

                if ((n.Contains("_L") || n.Contains("Left")) && result[leftSlot] == null)
                    result[leftSlot] = go;
                else if ((n.Contains("_R") || n.Contains("Right")) && result[rightSlot] == null)
                    result[rightSlot] = go;
                else
                    leftovers.Add(go);
            }

            // เติมช่องที่ยังว่างด้วยชิ้นที่ชื่อไม่เข้า convention
            foreach (GameObject go in leftovers)
            {
                if (result[leftSlot] == null) result[leftSlot] = go;
                else if (result[rightSlot] == null) result[rightSlot] = go;
                else break;
            }
        }

        #endregion


        private void Log(string message)
        {
            if (debugLog) Debug.Log($"[PVP] {message}");
        }
    }
}
