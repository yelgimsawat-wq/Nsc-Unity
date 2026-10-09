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
