using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;

/// <summary>
/// Host หลุด/ปิดเกม/เน็ตขาด → เครื่อง Client เด้งกลับ Scene เมนูอัตโนมัติ
/// ไม่ต้องลากแปะใน scene ไหนทั้งนั้น — สคริปต์สร้างตัวเองตอนเกมเริ่ม
/// แล้วเกาะ event ของ NetworkManager รอไว้ (อยู่ข้ามทุก scene ด้วย DontDestroyOnLoad)
/// </summary>
public class ReturnToMenuOnHostLost : MonoBehaviour
{
    // ชื่อ scene เมนูหลัก (ตัวที่ enabled ใน Build Settings)
    private const string MenuSceneName = "-MenuNOk";

    private NetworkManager subscribedManager;
    private bool returning;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        GameObject go = new GameObject("[ReturnToMenuOnHostLost]");
        DontDestroyOnLoad(go);
        go.AddComponent<ReturnToMenuOnHostLost>();
    }

    private void Update()
    {
        // NetworkManager เกิดทีหลังตอนเข้าเมนูได้ — คอยเช็คแล้วเกาะ event ให้ทันเสมอ
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || nm == subscribedManager) return;

        Unsubscribe();
        nm.OnClientStopped += OnClientStopped;
        subscribedManager = nm;
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    private void Unsubscribe()
    {
        if (subscribedManager != null)
            subscribedManager.OnClientStopped -= OnClientStopped;
        subscribedManager = null;
    }

    /// <summary>
    /// ยิงบนเครื่องนี้เมื่อการเชื่อมต่อจบลงทุกกรณี — Host ปิดเกม, โดนเตะ, เน็ตหลุด
    /// </summary>
    private void OnClientStopped(bool wasHost)
    {
        // เครื่องที่เป็น Host เองมีวิธีออกของมันอยู่แล้ว — จัดการเฉพาะฝั่ง Client แท้ๆ
        if (wasHost) return;

        if (returning) return;

        // ถ้าอยู่เมนูอยู่แล้ว (เช่น join ห้องไม่สำเร็จ) ไม่ต้องโหลดซ้ำ
        if (SceneManager.GetActiveScene().name == MenuSceneName) return;

        returning = true;
        Debug.Log("[ReturnToMenu] การเชื่อมต่อกับ Host จบลง — กลับสู่เมนูหลัก");

        // ปลดเมาส์เผื่อ scene เกมล็อกไว้ จะได้กดปุ่มในเมนูได้
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        SceneManager.LoadScene(MenuSceneName, LoadSceneMode.Single);
        returning = false;
    }
}
