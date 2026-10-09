using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Nsc.Match
{
    /// <summary>
    /// เจ้าของเฟสของแมตช์ในฉากนี้คนเดียว (เตรียมตัว → เล่น → จบ) และผลลัพธ์
    /// เป็นผู้เขียน GameplayGate คนเดียว — แขนขา ไอเทม HUD อ่าน gate ไม่ต้องรู้จักลอบบี้หรือโหมด
    ///
    /// ใครเริ่มแมตช์: LimbSelection (Host กด Start) / ใครจบ: ModeRules ของโหมดนั้น
    /// ฉากที่ไม่มี LimbSelection (ซีนเทส) เริ่มเล่นทันทีเหมือนพฤติกรรมเดิม
    /// </summary>
    public class MatchSession : NetworkBehaviour
    {
        /// <summary>หนึ่งตัวต่อฉาก (ผู้ประสานงานของฉาก)</summary>
        public static MatchSession Current { get; private set; }

        // ⚠️ result ต้องประกาศ "ก่อน" phase เสมอ — NGO ส่ง delta ตามลำดับ field
        // ผลลัพธ์จะถึง client ก่อนเฟสเปลี่ยน หน้าผลลัพธ์อ่านค่าได้ครบตอน PhaseChanged ยิง
        private readonly NetworkVariable<MatchResult> result = new NetworkVariable<MatchResult>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<MatchPhase> phase = new NetworkVariable<MatchPhase>(
            MatchPhase.Preparing, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private float startTime;

        public MatchPhase Phase => phase.Value;
        public MatchResult Result => result.Value;
        public bool IsPlaying => phase.Value == MatchPhase.Playing;

        /// <summary>ยิงบนทุกเครื่องเมื่อเฟสเปลี่ยน (รวมตอนเข้าห้องช้าแล้วเฟสไม่ใช่ Preparing)</summary>
        public event Action<MatchPhase> PhaseChanged;

        private void Awake()
        {
            Current = this;
            ApplyGate(MatchPhase.Preparing);
        }

        public override void OnDestroy()
        {
            if (Current == this)
            {
                Current = null;
                GameplayGate.Open(); // ซีนต่อไปอาจไม่มี MatchSession — ห้ามค้างสถานะปิด
            }
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            phase.OnValueChanged += OnPhaseValueChanged;
            ApplyGate(phase.Value);

            // client ที่เข้าห้องช้า — แจ้งเฟสปัจจุบันทันที
            if (phase.Value != MatchPhase.Preparing) PhaseChanged?.Invoke(phase.Value);

            if (IsServer) StartCoroutine(StartIfNobodyPrepares());
        }

        public override void OnNetworkDespawn()
        {
            phase.OnValueChanged -= OnPhaseValueChanged;
            base.OnNetworkDespawn();
        }

        /// <summary>ซีนที่ไม่มีลอบบี้ = เริ่มเล่นทันที (รอหนึ่งเฟรมให้ LimbSelection ในฉากตื่นก่อน)</summary>
        private IEnumerator StartIfNobodyPrepares()
        {
            yield return null;
            if (LimbSelection.Current == null) ServerBeginPlaying();
        }

        private void OnPhaseValueChanged(MatchPhase previous, MatchPhase current)
        {
            ApplyGate(current);
            PhaseChanged?.Invoke(current);
        }

        private static void ApplyGate(MatchPhase current) =>
            GameplayGate.Set(canAct: current == MatchPhase.Playing, canDamage: current == MatchPhase.Playing);

        /// <summary>[SERVER] เริ่มเล่น — เริ่มจับเวลาตอนนี้</summary>
        public void ServerBeginPlaying()
        {
            if (!IsServer || phase.Value != MatchPhase.Preparing) return;

            startTime = Time.time;
            phase.Value = MatchPhase.Playing;
            Debug.Log("[Match] 🔔 เริ่มเล่น");
        }

        /// <summary>[SERVER] จบแมตช์ด้วยผลนี้ — เรียกได้ครั้งเดียว</summary>
        public void ServerEnd(MatchResult matchResult)
        {
            if (!IsServer || phase.Value != MatchPhase.Playing) return;

            result.Value = matchResult;
            phase.Value = MatchPhase.Ended;
            Debug.Log($"[Match] 🏁 จบแมตช์ — victory={matchResult.victory} team={matchResult.winningTeam} time={matchResult.time:F2}");
        }

        /// <summary>[SERVER] เวลาที่เล่นมาแล้ว (0 ถ้ายังไม่เริ่ม)</summary>
        public float ElapsedTime() => phase.Value == MatchPhase.Preparing ? 0f : Time.time - startTime;

        /// <summary>[SERVER] Host เล่นด่านเดิมใหม่ — โหลดฉากเดิมซ้ำผ่าน NGO ทุกเครื่องตามมา ทุกระบบรีเซ็ตเอง</summary>
        public void ServerRestart()
        {
            NetworkManager network = NetworkManager.Singleton;
            if (network == null || !network.IsListening || !network.IsServer) return;

            Debug.Log($"[Match] 🔄 Host เล่นใหม่: {gameObject.scene.name}");
            network.SceneManager.LoadScene(gameObject.scene.name, LoadSceneMode.Single);
        }

        /// <summary>[SERVER] Host พาทุกคนกลับฉากเมนู (ห้องยังอยู่)</summary>
        public void ServerReturnEveryoneToMenu()
        {
            NetworkManager network = NetworkManager.Singleton;
            if (network == null || !network.IsListening || !network.IsServer) return;

            if (!Application.CanStreamedLevelBeLoaded(GameScenes.Menu))
            {
                Debug.LogError($"[MatchSession] Menu scene '{GameScenes.Menu}' is not in Build Settings!");
                return;
            }

            Debug.Log($"[MatchSession] 🚪 Host exiting to menu: {GameScenes.Menu}");
            network.SceneManager.LoadScene(GameScenes.Menu, LoadSceneMode.Single);
        }
    }
}
