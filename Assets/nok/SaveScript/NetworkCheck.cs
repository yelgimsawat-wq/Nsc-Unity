using Unity.Netcode;

/// <summary>
/// Static helper for non-NetworkBehaviour classes that need to query
/// whether the current machine is acting as a server or host.
/// Uses Netcode for GameObjects' NetworkManager.Singleton under the hood.
/// </summary>
public static class NetworkCheck
{
    /// <summary>
    /// Returns true when this machine is the authoritative server or host.
    /// Also returns true when NetworkManager is not present (offline / single-player),
    /// so gameplay still functions without a network session.
    /// </summary>
    public static bool IsServerOrHost()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return true;          // offline / no network session – allow
        return nm.IsServer || nm.IsHost;
    }
}