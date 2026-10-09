using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.UI;
using Unity.Netcode;

public class TimelineSkipController : NetworkBehaviour
{
    [Header("Timeline")]
    [SerializeField] private PlayableDirector director;

    [Header("UI")]
    [SerializeField] private Button skipButton;

    [Header("Target Scene")]
    [SerializeField] private string targetSceneName;

    [Header("Sync")]
    [Tooltip("ถ้าเวลา Timeline ของ Client เพี้ยนจาก Server เกินค่านี้ (วินาที) จะ snap กลับ")]
    [SerializeField] private float resyncThreshold = 0.3f;

    // เวลา (ServerTime) ที่ Server เริ่มเล่น Timeline — ทุกเครื่องใช้ค่านี้คำนวณตำแหน่งเล่นให้ตรงกัน
    private NetworkVariable<double> netStartTime = new NetworkVariable<double>(
        -1d,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private bool isLoadingScene;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        Debug.Log($"[TimelineSkip] OnNetworkSpawn - IsHost:{IsHost} IsServer:{IsServer} IsClient:{IsClient}");

        if (skipButton != null)
        {
            // แสดงปุ่ม Skip เฉพาะโฮสต์/เซิร์ฟเวอร์เท่านั้น
            skipButton.gameObject.SetActive(IsHost || IsServer);
            skipButton.onClick.AddListener(OnSkipPressed);
        }
        else
        {
            Debug.LogWarning("[TimelineSkip] ยังไม่ได้ลาก Skip Button ใส่ Inspector! (หากไม่มีปุ่ม จะไม่สามารถกดข้ามแบบ Manual ได้)");
        }

        if (director != null)
        {
            // กันไม่ให้ director เล่นเองก่อน sync เวลา
            director.playOnAwake = false;

            if (IsServer)
            {
                // Timeline จบเอง → เปลี่ยนซีน (Server เท่านั้น)
                director.stopped += OnTimelineStopped;

                // บันทึกเวลาเริ่มตาม Server clock แล้วเริ่มเล่น
                netStartTime.Value = NetworkManager.ServerTime.Time;
                director.time = 0;
                director.Play();
            }
            else
            {
                netStartTime.OnValueChanged += OnStartTimeChanged;

                // เผื่อกรณี NetworkVariable มาถึงก่อน spawn เสร็จ
                if (netStartTime.Value >= 0)
                    SyncAndPlay();
            }
        }
    }

    private void OnStartTimeChanged(double oldVal, double newVal)
    {
        if (newVal >= 0)
            SyncAndPlay();
    }

    // Client: เริ่มเล่นจากตำแหน่งที่ตรงกับ Server ณ ตอนนี้ (ชดเชยเวลาโหลด scene ที่ช้ากว่า)
    private void SyncAndPlay()
    {
        if (director == null || isLoadingScene) return;

        double elapsed = NetworkManager.ServerTime.Time - netStartTime.Value;
        if (elapsed < 0) elapsed = 0;
        if (elapsed > director.duration) elapsed = director.duration;

        director.time = elapsed;
        director.Play();
    }

    private void Update()
    {
        // แก้ drift: ถ้าเวลา Timeline ฝั่ง Client เพี้ยนเกิน threshold ให้ snap กลับ
        if (IsServer || isLoadingScene) return;
        if (director == null || netStartTime.Value < 0) return;
        if (director.state != PlayState.Playing) return;

        double expected = NetworkManager.ServerTime.Time - netStartTime.Value;
        if (expected < 0 || expected > director.duration) return;

        if (Mathf.Abs((float)(director.time - expected)) > resyncThreshold)
            director.time = expected;
    }

    public override void OnNetworkDespawn()
    {
        Cleanup();
        base.OnNetworkDespawn();
    }

    private void OnDestroy()
    {
        Cleanup();
    }

    private void Cleanup()
    {
        if (director != null)
            director.stopped -= OnTimelineStopped;

        netStartTime.OnValueChanged -= OnStartTimeChanged;

        if (skipButton != null)
            skipButton.onClick.RemoveListener(OnSkipPressed);
    }

    private void OnSkipPressed()
    {
        if (!IsHost && !IsServer) return;
        LoadTargetScene();
    }

    private void OnTimelineStopped(PlayableDirector aDirector)
    {
        if (!IsHost && !IsServer) return;
        LoadTargetScene();
    }

    private void LoadTargetScene()
    {
        // กันเรียกซ้ำ (กด Skip → director.stopped ยิงตาม → LoadScene ซ้อนกัน = กระตุก/error)
        if (isLoadingScene) return;

        if (string.IsNullOrEmpty(targetSceneName))
        {
            Debug.LogError("[TimelineSkip] ไม่ได้ระบุ Target Scene Name ใน Inspector!");
            return;
        }

        isLoadingScene = true;

        // หยุด Timeline ทุกเครื่องก่อน จะได้ไม่ evaluate ต่อระหว่างโหลดซีน
        PauseTimelineClientRpc();

        Debug.Log($"[TimelineSkip] กำลังเปลี่ยนซีนไปยัง: {targetSceneName}");
        NetworkManager.Singleton.SceneManager.LoadScene(
            targetSceneName,
            UnityEngine.SceneManagement.LoadSceneMode.Single
        );
    }

    [ClientRpc]
    private void PauseTimelineClientRpc()
    {
        isLoadingScene = true;
        if (director != null)
            director.Pause();
    }
}
