using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Static helper for non-NetworkBehaviour classes that need to query
/// whether the current machine is acting as a server or host.
/// Uses Netcode for GameObjects' NetworkManager.Singleton under the hood.
/// </summary>
public static class NetworkCheck
{
    /// <summary>
    /// Returns true when this machine is the authoritative server or host.
    /// Also returns true when NetworkManager is not present or not running
    /// (offline / single-player / test scene) so gameplay still functions
    /// without a network session.
    /// </summary>
    public static bool IsServerOrHost()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return true;          // offline / no network session – allow
        if (!nm.IsListening) return true;     // NetworkManager มีในฉากแต่ยังไม่ start = เทสออฟไลน์ – allow
        return nm.IsServer;                   // IsHost เป็นส่วนหนึ่งของ IsServer อยู่แล้ว
    }
}

/// <summary>
/// เช็กว่า Collider เป็นชิ้นส่วนของหุ่นผู้เล่นไหม — ดูจาก component จริง ไม่พึ่ง tag
/// (tag ลืมตั้งแม้ชิ้นเดียว = ระบบตายเงียบ / ส่วน component ติดมากับ prefab เสมอ)
/// ใช้ root ของ Rigidbody เพราะโครงหุ่นมี limb เป็น sibling กัน — GetComponentInParent
/// ตรงๆ จาก collider ของท่อนขาจะหา TorsoMovement ไม่เจอ
/// </summary>
public static class RobotBodyCheck
{
    public static bool IsRobotBodyPart(Collider other)
    {
        if (other == null) return false;

        Rigidbody rb = other.attachedRigidbody;
        if (rb == null) return false;

        return rb.transform.root.GetComponentInChildren<TorsoMovement>() != null;
    }
}