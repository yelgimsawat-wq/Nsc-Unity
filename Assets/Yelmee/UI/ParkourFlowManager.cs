using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// ParkourFlowManager — วงจรจบด่านของโหมด Parkour (วางในซีน Parkour เท่านั้น)
///
/// จบด่านเมื่อ: ParkourGoalZone (วางไว้บนยอด) ถูกชิ้นส่วนหุ่นแตะ หรือระบบอื่นเรียก
/// TriggerRunEnd() → โชว์ HIGH SCORE panel (TIME + HEIGHT) ทุกเครื่อง
///
/// Server วัดความสูงสูงสุดของลำตัวจากจุดเริ่ม (netMaxHeight) sync ให้ทุกคนตลอดเกม
/// ปุ่ม RETRY/EXIT โชว์เฉพาะ Host — ใช้กลไกโหลดซีนเดิมซ้ำแบบเดียวกับฝั่ง Boss
///
/// สร้าง UI ทั้งชุดด้วยเมนู: Tools/NSC/Build Parkour GameFlow UI
/// </summary>
public class ParkourFlowManager : NetworkBehaviour
{
    public static ParkourFlowManager Instance { get; private set; }

    [Header("Panel (สร้างโดย Builder)")]
    [SerializeField] private CanvasGroup highScorePanel;
    [SerializeField] private GameObject hostButtons;
    [SerializeField] private GameObject waitingText;
    [SerializeField] private Button retryButton;
    [SerializeField] private Button exitButton;
    [SerializeField] private TextMeshProUGUI timeText;
    [SerializeField] private TextMeshProUGUI heightText;

    [Header("Scenes")]
    [Tooltip("ชื่อซีนเมนูหลักใน Build Settings — ใช้ตอนกด EXIT")]
    [SerializeField] private string menuSceneName = "-Menu";

    [Header("Height Tracking")]
    [Tooltip("บันทึกความสูงเพิ่มทีละกี่เมตร (กัน NetworkVariable ส่งถี่เกิน)")]
    [SerializeField] private float heightSyncStep = 0.5f;

    [Header("Animation")]
    [SerializeField] private float fadeDuration = 0.45f;

    // ประกาศก่อน netEnded — delta ถึง client ก่อน state เปลี่ยน (ลำดับ field สำคัญ)
    private NetworkVariable<float> netFinalTime = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<float> netMaxHeight = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> netEnded = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private bool matchTimerStarted;
    private float matchStartTime;
    private Transform trackedTorso;
    private float baselineY;
    private float peakHeight;
    private Tween fadeTween;

    private void Awake()
    {
        Instance = this;
        GameFlowManager.GameEnded = false; // ใช้ flag กลางร่วมกับฝั่ง Boss (มือ/เท้าเช็คตัวนี้)

        HidePanelInstant();
        if (retryButton != null) retryButton.onClick.AddListener(RestartLevel);
        if (exitButton != null) exitButton.onClick.AddListener(ExitToMenu);
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
        netEnded.OnValueChanged += OnEndedChanged;

        // เผื่อ client เข้ามาช้าหลังจบไปแล้ว
        if (netEnded.Value)
            OnEndedChanged(false, true);
    }

    public override void OnNetworkDespawn()
    {
        netEnded.OnValueChanged -= OnEndedChanged;
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (IsServer && !netEnded.Value)
        {
            // เริ่มจับเวลา + ตั้ง baseline ความสูง เมื่อเกมเริ่มจริง (Host กด Start ใน Lobby)
            if (!matchTimerStarted &&
                (LobbyManager.Instance == null || LobbyManager.Instance.GameStarted))
            {
                TorsoMovement torso = FindFirstObjectByType<TorsoMovement>();
                if (torso != null && torso.torsoRb != null)
                {
                    matchTimerStarted = true;
                    matchStartTime = Time.time;
                    trackedTorso = torso.torsoRb.transform;
                    baselineY = trackedTorso.position.y;
                }
            }

            // วัดความสูงสูงสุด — sync เป็นสเต็ปกัน traffic ถี่
            if (matchTimerStarted && trackedTorso != null)
            {
                float height = trackedTorso.position.y - baselineY;
                if (height > peakHeight)
                {
                    peakHeight = height;
                    if (peakHeight - netMaxHeight.Value >= heightSyncStep)
                        netMaxHeight.Value = peakHeight;
                }
            }
        }

        // ระหว่างจบด่าน บังคับเมาส์หลุดล็อกตลอด
        if (GameFlowManager.GameEnded && Cursor.lockState != CursorLockMode.None)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    // ================================================================
    //  TRIGGER (server เท่านั้น) — เรียกจาก ParkourGoalZone หรือระบบอื่น
    // ================================================================

    public void TriggerRunEnd()
    {
        if (!IsServer || netEnded.Value) return;

        netFinalTime.Value = matchTimerStarted ? Time.time - matchStartTime : 0f;
        netMaxHeight.Value = peakHeight; // ค่าเป๊ะสุดท้าย ไม่ติดสเต็ป
        netEnded.Value = true;
        Debug.Log($"[ParkourFlow] 🏁 RUN END — time {FormatTime(netFinalTime.Value)}, height {peakHeight:F1}m");
    }

    private void OnEndedChanged(bool oldValue, bool ended)
    {
        if (!ended) return;

        GameFlowManager.GameEnded = true;

        if (hostButtons != null) hostButtons.SetActive(IsServer);
        if (waitingText != null) waitingText.SetActive(!IsServer);
        if (timeText != null) timeText.text = FormatTime(netFinalTime.Value);
        if (heightText != null)
            heightText.text = $"{Mathf.FloorToInt(Mathf.Max(0f, netMaxHeight.Value))}<size=55%>m</size>";

        if (highScorePanel == null) return;
        highScorePanel.gameObject.SetActive(true);
        highScorePanel.blocksRaycasts = true;
        highScorePanel.interactable = true;

        fadeTween?.Kill();
        fadeTween = highScorePanel.DOFade(1f, fadeDuration).SetEase(Ease.OutCubic).SetUpdate(true);
    }

    private void HidePanelInstant()
    {
        if (highScorePanel == null) return;
        highScorePanel.alpha = 0f;
        highScorePanel.blocksRaycasts = false;
        highScorePanel.interactable = false;
        highScorePanel.gameObject.SetActive(false);
    }

    private static string FormatTime(float seconds)
    {
        seconds = Mathf.Max(0f, seconds);
        int minutes = (int)(seconds / 60f);
        int secs = (int)(seconds % 60f);
        int cents = (int)(seconds * 100f) % 100;
        return $"{minutes:00}:{secs:00}<size=55%>.{cents:00}</size>";
    }

    // ================================================================
    //  BUTTONS (host เท่านั้น)
    // ================================================================

    private void RestartLevel()
    {
        if (!IsServer || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening) return;
        string currentScene = gameObject.scene.name;
        Debug.Log($"[ParkourFlow] 🔄 Host restarting level: {currentScene}");
        NetworkManager.Singleton.SceneManager.LoadScene(currentScene, LoadSceneMode.Single);
    }

    private void ExitToMenu()
    {
        if (!IsServer || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening) return;

        if (!Application.CanStreamedLevelBeLoaded(menuSceneName))
        {
            Debug.LogError($"[ParkourFlow] Menu scene '{menuSceneName}' is not in Build Settings!");
            return;
        }

        Debug.Log($"[ParkourFlow] 🚪 Host exiting to menu: {menuSceneName}");
        NetworkManager.Singleton.SceneManager.LoadScene(menuSceneName, LoadSceneMode.Single);
    }
}
