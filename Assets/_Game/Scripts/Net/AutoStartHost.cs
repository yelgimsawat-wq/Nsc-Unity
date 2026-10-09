using UnityEngine;
using Unity.Netcode;

/// <summary>
/// สคริปต์สำหรับ Auto Start Host อัตโนมัติเมื่อกด Play
/// ให้เอาไปแปะไว้ที่ GameObject ว่างๆ ใน Scene ที่ต้องการใช้เทสต์
/// </summary>
public class AutoStartHost : MonoBehaviour
{
    void Start()
    {
        // ตรวจสอบว่ามี NetworkManager ใน Scene หรือไม่ และยังไม่ได้เชื่อมต่อใดๆ
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer && !NetworkManager.Singleton.IsClient)
        {
            NetworkManager.Singleton.StartHost();
            Debug.Log("[AutoStartHost] 🟢 Start Host อัตโนมัติสำหรับการเทสต์เรียบร้อย!");
        }
        else if (NetworkManager.Singleton == null)
        {
            Debug.LogWarning("[AutoStartHost] 🔴 ไม่พบ NetworkManager ใน Scene นี้! กรุณาตรวจสอบว่ามี NetworkManager อยู่หรือไม่");
        }
    }
}
