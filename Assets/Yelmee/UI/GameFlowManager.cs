using Unity.Netcode;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// GameFlowManager — วงจรจบเกมของโหมด Boss (วางในซีนบอสเท่านั้น)
///
/// ชนะ: EnemyHealth.ServerDie() เรียก TriggerVictory() → โชว์ WinPanel ทุกเครื่อง
/// แพ้:  ชิ้นส่วนหุ่นหลุด (Detached) พร้อมกันครบตามจำนวนที่ตั้ง → โชว์ GameOverPanel
///
/// ปุ่ม Restart/Exit โชว์เฉพาะ Host — Restart ใช้วิธี "โหลดซีนเดิมซ้ำ" ผ่าน
/// Netcode SceneManager (กลไกเดียวกับตอน Host เลือกแมพ) ทุกระบบรีเซ็ตเองหมด
/// ไม่ต้องเขียนโค้ดรีเซ็ตอะไรเพิ่มเลย / Exit = โหลดซีนเมนูกลับไปหน้าห้อง
///
/// สร้าง UI ทั้งชุดด้วยเมนู: Tools/NSC/Build Boss GameFlow UI
/// </summary>
public class GameFlowManager : NetworkBehaviour
{
    public static GameFlowManager Instance { get; private set; }

    // จบเกมแล้วหรือยัง — ระบบ input มือ/เท้าเช็คตัวนี้เพื่อหยุดล็อกเมาส์กลับ
    // (static เพื่อให้เช็คได้โดยไม่ต้องมี reference — reset ใน Awake ทุกครั้งที่โหลดซีน
    //  setter เปิด public เพราะ ParkourFlowManager ใช้ flag เดียวกันในซีน Parkour)
    public static bool GameEnded { get; set; }

    public enum GameState : byte { Playing, Victory, Defeat }

    [Header("Panels (สร้างโดย Builder)")]
    [SerializeField] private CanvasGroup winPanel;
    [SerializeField] private CanvasGroup gameOverPanel;
    [SerializeField] private GameObject winHostButtons;
    [SerializeField] private GameObject winWaitingText;
    [SerializeField] private GameObject gameOverHostButtons;
    [SerializeField] private GameObject gameOverWaitingText;
    [SerializeField] private Button winRestartButton;
    [SerializeField] private Button winExitButton;
    [SerializeField] private Button gameOverRestartButton;
    [SerializeField] private Button gameOverExitButton;
    [SerializeField] private TextMeshProUGUI winTimeText;      // ตัวเลขเวลา (02:47.63) บน WinPanel
    [SerializeField] private TextMeshProUGUI gameOverTimeText; // ตัวเลขเวลาบน GameOverPanel

    [Header("Scenes")]
    [Tooltip("ชื่อซีนเมนูหลักใน Build Settings — ใช้ตอนกด EXIT TO MENU")]
    [SerializeField] private string menuSceneName = "-Menu";

    [Header("Defeat Rule")]
    [Tooltip("จำนวนชิ้นส่วนที่หลุด (Detached) พร้อมกันแล้วนับว่าแพ้\nตั้ง 0 = ปิดการเช็คแพ้")]
    [SerializeField] private int defeatDetachedLimbs = 4;
    [SerializeField] private float defeatCheckInterval = 1f;

    [Header("Victory Cutscene (ไม่บังคับ)")]
    [Tooltip("ลาก PlayableDirector ของ ending cutscene มาใส่ — บอสตายจะเล่นก่อนโชว์ WinPanel\nเว้นว่าง = โชว์ WinPanel ทันที")]
    [SerializeField] private PlayableDirector endingCutscene;
    [Tooltip("หน่วงเวลาก่อนโชว์ WinPanel (วินาที) — ตั้งเท่าความยาว cutscene")]
    [SerializeField] private float victoryPanelDelay = 0f;

    [Header("Animation")]
    [SerializeField] private float fadeDuration = 0.45f;

    // ⏱️ เวลาสุดท้ายตอนชนะ — ประกาศ "ก่อน" netState เสมอ: NGO ส่ง delta ตามลำดับ field
    // เวลาจะถึง client ก่อน state เปลี่ยน → ตอน ShowPanel อ่านค่าได้ครบแน่นอน
    private NetworkVariable<float> netFinalTime = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private NetworkVariable<GameState> netState = new NetworkVariable<GameState>(
        GameState.Playing,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private float nextDefeatCheckTime;
    private int _lastLoggedDetached = -1;

    // ⏱️ จับเวลาภารกิจ (server) — เริ่มนับเมื่อ Host กด Start ในหน้าเลือกชิ้นส่วน
    private bool matchTimerStarted;
    private float matchStartTime;
    private float showPanelAtTime = -1f;
    private GameState pendingPanelState;
    private Tween fadeTween;

    // ================================================================
    //  LIFECYCLE
    // ================================================================

    private void Awake()
    {
        Instance = this;
        GameEnded = false; // static ค้างข้ามซีนได้ — ต้องล้างทุกครั้งที่โหลดซีนใหม่

        HidePanelInstant(winPanel);
        HidePanelInstant(gameOverPanel);
        WireButtons();
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        fadeTween?.Kill();
        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        netState.OnValueChanged += OnStateChanged;

        // เผื่อ client เข้ามาช้าหลังเกมจบไปแล้ว
        if (netState.Value != GameState.Playing)
            OnStateChanged(GameState.Playing, netState.Value);
    }

    public override void OnNetworkDespawn()
    {
        netState.OnValueChanged -= OnStateChanged;
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        // ── Server: เริ่มจับเวลาภารกิจเมื่อเกมเริ่มจริง (Host กด Start ใน Lobby) ──
        // ซีนที่ไม่มี LobbyManager (เทสตรงๆ) = เริ่มนับทันที
        if (IsServer && !matchTimerStarted && netState.Value == GameState.Playing &&
            (LobbyManager.Instance == null || LobbyManager.Instance.GameStarted))
        {
            matchTimerStarted = true;
            matchStartTime = Time.time;
        }

        // ── Server: เช็คเงื่อนไขแพ้เป็นระยะ ──
        if (IsServer && netState.Value == GameState.Playing &&
            defeatDetachedLimbs > 0 && Time.time >= nextDefeatCheckTime)
        {
            nextDefeatCheckTime = Time.time + Mathf.Max(0.2f, defeatCheckInterval);
            CheckDefeatCondition();
        }

        // ── ทุกเครื่อง: ถึงเวลาโชว์ panel ที่หน่วงไว้ (รอ cutscene) ──
        if (showPanelAtTime >= 0f && Time.unscaledTime >= showPanelAtTime)
        {
            showPanelAtTime = -1f;
            ShowPanel(pendingPanelState);
        }

        // ── ทุกเครื่อง: ระหว่างจบเกม บังคับเมาส์หลุดล็อกตลอด (กันสคริปต์อื่นล็อกกลับ) ──
        if (GameEnded && Cursor.lockState != CursorLockMode.None)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    // ================================================================
    //  TRIGGERS (server เท่านั้น)
    // ================================================================

    /// <summary>เรียกจาก EnemyHealth ตอนบอสตาย</summary>
    public void TriggerVictory()
    {
        if (!IsServer || netState.Value != GameState.Playing) return;

        // เซ็ตเวลาก่อนเปลี่ยน state (ลำดับสำคัญ — ดูคอมเมนต์ที่ netFinalTime)
        netFinalTime.Value = matchTimerStarted ? Time.time - matchStartTime : 0f;
        netState.Value = GameState.Victory;
        Debug.Log($"[GameFlow] 🏆 VICTORY — boss defeated in {FormatTime(netFinalTime.Value)}");
    }

    public void TriggerDefeat()
    {
        if (!IsServer || netState.Value != GameState.Playing) return;

        netFinalTime.Value = matchTimerStarted ? Time.time - matchStartTime : 0f;
        netState.Value = GameState.Defeat;
        Debug.Log($"[GameFlow] 💀 DEFEAT — robot destroyed at {FormatTime(netFinalTime.Value)}");
    }

    private void CheckDefeatCondition()
    {
        // ⚠️ สแกนสดทุกรอบ ไม่ cache ตอน spawn — จังหวะ spawn ตรงกับช่วงที่ LobbyManager
        // กำลังสลับ/ปิดหุ่นตัวเกิน (ResolveActiveRobot/DisableExtraRobots) cache อาจว่าง
        // หรือชี้หุ่นตัวที่ถูกปิดไปแล้ว → เช็คแพ้เงียบหายทั้งเกม (สแกน 1 ครั้ง/วิ ต้นทุนจิ๋ว
        // และการหา "เฉพาะตัว active" ทำให้ไม่นับหุ่นตัวเกินที่ถูกปิดด้วย)
        int totalLimbs = 0;
        int detached = 0;

        foreach (var foot in FindObjectsByType<PlayerFootForRobot>(FindObjectsSortMode.None))
        {
            totalLimbs++;
            if (foot.currentState.Value == PlayerFootForRobot.FootState.Detached) detached++;
        }
        foreach (var hand in FindObjectsByType<PlayerHandMovement>(FindObjectsSortMode.None))
        {
            totalLimbs++;
            if (hand.currentState.Value == PlayerHandMovement.HandState.Detached) detached++;
        }

        // log เฉพาะตอนตัวเลขเปลี่ยน — เอาไว้วินิจฉัยว่าระบบมองเห็นการหลุดไหม
        if (detached != _lastLoggedDetached)
        {
            _lastLoggedDetached = detached;
            Debug.Log($"[GameFlow] limbs detached: {detached}/{totalLimbs} (need {Mathf.Min(defeatDetachedLimbs, Mathf.Max(1, totalLimbs))} to lose)");
        }

        if (totalLimbs == 0) return;

        // เพดานตามจำนวนชิ้นจริงในซีน — ตั้ง 4 แต่ซีนเทสมี 2 ชิ้นก็ยังแพ้ได้ถูกต้อง
        int required = Mathf.Min(defeatDetachedLimbs, totalLimbs);
        if (detached >= required)
            TriggerDefeat();
    }

    // ================================================================
    //  STATE CHANGE → แสดงผลทุกเครื่อง
    // ================================================================

    private void OnStateChanged(GameState oldState, GameState newState)
    {
        if (newState == GameState.Playing) return;

        GameEnded = true;

        if (newState == GameState.Victory && (endingCutscene != null || victoryPanelDelay > 0f))
        {
            // เล่น ending cutscene ก่อน แล้วค่อยโชว์ WinPanel ตาม delay
            if (endingCutscene != null) endingCutscene.Play();
            pendingPanelState = newState;
            showPanelAtTime = Time.unscaledTime + Mathf.Max(0f, victoryPanelDelay);
        }
        else
        {
            ShowPanel(newState);
        }
    }

    private void ShowPanel(GameState state)
    {
        CanvasGroup panel = state == GameState.Victory ? winPanel : gameOverPanel;
        GameObject hostButtons = state == GameState.Victory ? winHostButtons : gameOverHostButtons;
        GameObject waitingText = state == GameState.Victory ? winWaitingText : gameOverWaitingText;

        if (panel == null) return;

        // Host เห็นปุ่ม / Client เห็น "WAITING FOR HOST..."
        if (hostButtons != null) hostButtons.SetActive(IsServer);
        if (waitingText != null) waitingText.SetActive(!IsServer);

        // ⏱️ เวลาภารกิจ — โชว์ทั้งสองหน้า (ฝั่งแพ้ = เวลาที่ยืนระยะมาได้)
        TextMeshProUGUI timeText = state == GameState.Victory ? winTimeText : gameOverTimeText;
        if (timeText != null)
            timeText.text = FormatTime(netFinalTime.Value);

        panel.gameObject.SetActive(true);
        panel.blocksRaycasts = true;
        panel.interactable = true;

        fadeTween?.Kill();
        fadeTween = panel.DOFade(1f, fadeDuration).SetEase(Ease.OutCubic).SetUpdate(true);
    }

    /// <summary>แปลงวินาทีเป็น mm:ss.hh ตามต้นแบบ (เศษวินาทีย่อเล็กด้วย rich text)</summary>
    private static string FormatTime(float seconds)
    {
        seconds = Mathf.Max(0f, seconds);
        int minutes = (int)(seconds / 60f);
        int secs = (int)(seconds % 60f);
        int cents = (int)(seconds * 100f) % 100;
        return $"{minutes:00}:{secs:00}<size=55%>.{cents:00}</size>";
    }

    private void HidePanelInstant(CanvasGroup panel)
    {
        if (panel == null) return;
        panel.alpha = 0f;
        panel.blocksRaycasts = false;
        panel.interactable = false;
        panel.gameObject.SetActive(false);
    }

    // ================================================================
    //  BUTTONS (host เท่านั้น — ปุ่มถูกซ่อนบน client อยู่แล้ว แต่กันซ้ำอีกชั้น)
    // ================================================================

    private void WireButtons()
    {
        if (winRestartButton != null) winRestartButton.onClick.AddListener(RestartLevel);
        if (gameOverRestartButton != null) gameOverRestartButton.onClick.AddListener(RestartLevel);
        if (winExitButton != null) winExitButton.onClick.AddListener(ExitToMenu);
        if (gameOverExitButton != null) gameOverExitButton.onClick.AddListener(ExitToMenu);
    }

    private void RestartLevel()
    {
        if (!IsServer || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening) return;

        // โหลดซีนเดิมซ้ำผ่าน NGO — ทุกเครื่องตามมาพร้อมกัน ทุกระบบรีเซ็ตเองทั้งหมด
        string currentScene = gameObject.scene.name;
        Debug.Log($"[GameFlow] 🔄 Host restarting level: {currentScene}");
        NetworkManager.Singleton.SceneManager.LoadScene(currentScene, LoadSceneMode.Single);
    }

    private void ExitToMenu()
    {
        if (!IsServer || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening) return;

        if (!Application.CanStreamedLevelBeLoaded(menuSceneName))
        {
            Debug.LogError($"[GameFlow] Menu scene '{menuSceneName}' is not in Build Settings!");
            return;
        }

        Debug.Log($"[GameFlow] 🚪 Host exiting to menu: {menuSceneName}");
        NetworkManager.Singleton.SceneManager.LoadScene(menuSceneName, LoadSceneMode.Single);
    }
}
