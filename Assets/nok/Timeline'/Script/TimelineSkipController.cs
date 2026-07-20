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

    private void Awake()
    {
        // เล่น Timeline ได้เลยไม่ต้องรอ network (ถ้าอยากให้เล่นพร้อมกันทุกเครื่อง)
        if (director != null)
            director.Play();
    }

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

        // เมื่อ Timeline เล่นจบเอง ให้ทำการเปลี่ยนซีนอัตโนมัติ (เฉพาะโฮสต์/เซิร์ฟเวอร์เป็นคนสั่ง)
        if (director != null)
        {
            director.stopped += OnTimelineStopped;
        }
    }

    private void OnDestroy()
    {
        if (director != null)
        {
            director.stopped -= OnTimelineStopped;
        }
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
        if (string.IsNullOrEmpty(targetSceneName))
        {
            Debug.LogError("[TimelineSkip] ไม่ได้ระบุ Target Scene Name ใน Inspector!");
            return;
        }

        Debug.Log($"[TimelineSkip] กำลังเปลี่ยนซีนไปยัง: {targetSceneName}");
        NetworkManager.Singleton.SceneManager.LoadScene(
            targetSceneName,
            UnityEngine.SceneManagement.LoadSceneMode.Single
        );
    }
}