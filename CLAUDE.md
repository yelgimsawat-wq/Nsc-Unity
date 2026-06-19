# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Nsc-Unity** is a Unity 6 multiplayer robot combat game using Unity Netcode for GameObjects. Players connect via Unity Gaming Services Multiplayer, select robot parts in a lobby, then battle with Enemy AI. The project uses DOTween for UI animations and follows a networked architecture with Server-authoritative gameplay.

**Unity Version:** 6000.3.6f1 (Unity 6)  
**Render Pipeline:** Universal Render Pipeline (URP)

## Core Architecture

### Network Stack
- **Unity Netcode for GameObjects** (v2.12.0) - All multiplayer logic
- **Unity Services Multiplayer** (v2.2.1) - Relay/Lobby backend
- Flow: Anonymous sign-in → Create/Join Session by code → NetworkManager Start Host/Client → Synchronized scene loading

### Key Networking Patterns
1. **NetworkVariable<T>** for state sync (Server writes, Clients read)
   - Example: `EnemyController.cs` uses `NetworkVariable<EnemyState>` and `NetworkVariable<float>` for animation parameters
   - All Clients subscribe via `.OnValueChanged` callbacks to update local state

2. **Server-Authoritative AI**
   - Enemy decision loops run only on Server (`if (!IsServer) return`)
   - State changes write to NetworkVariables → broadcast to all Clients
   - Example: `EnemyController.ServerDecisionLoop()` coroutine

3. **ClientRpc / ServerRpc**
   - `[ClientRpc]` methods execute on all Clients (e.g., `TriggerRagdollClientRpc`)
   - Server validates actions before broadcasting

### Code Structure

**Main Game Scripts** (`Assets/Scenes/TheBestFolder/Mynigga/`)
- `OnlineNetworkUI.cs` - Main menu multiplayer flow (Create/Join room, waiting lobby)
- `LobbyManager.cs` - In-game part selection lobby (robot anatomy selection)
- `SettingsManager.cs` - Settings UI with tabbed layout (Graphics/Audio/Gameplay)
- `RobotManager.cs` - Player robot part management
- `PlayerCam.cs`, `PlayerHandMovement.cs`, `PlayerFootForRobot.cs`, `TorsoMovement.cs` - Player control
- `ClientNetworkTransform.cs` - Custom network transform for player movement

**Enemy AI System** (`Assets/nok/Enemy/Scripts/EnemyAI/`)
- `EnemyController.cs` - Main AI brain (State machine: Idle → Walk → Roll → Attack → Dead)
- `EnemyCombat.cs` - Attack execution
- `EnemyHealth.cs` - Health and damage system
- `EnemyRagdoll.cs` - Death physics
- `EnemyStateData.cs` - Shared state enums

**Dependencies:**
- **DOTween** (via `Assets/Plugins/Demigiant/`) - All UI animations use DOTween
- **TextMesh Pro** - All UI text
- **Unity NavMesh** - Enemy AI pathfinding
- **Cinemachine** (v3.1.6) - Camera system
- **Animation Rigging** (v1.4.1) - Character IK

## UI Animation Standards

All UI uses **DOTween** with consistent patterns (established in `OnlineNetworkUI.cs`):

```csharp
// Pop-up pattern
Sequence sequence = DOTween.Sequence().SetUpdate(true);
sequence.Join(canvasGroup.DOFade(1f, duration).SetEase(Ease.OutCubic));
sequence.Join(transform.DOScale(Vector3.one, duration).SetEase(Ease.OutCubic));
```

Key practices:
- Always use `.SetUpdate(true)` for UI (unscaled time)
- Track tweens in dictionaries for cleanup: `Dictionary<GameObject, Tween> runningUiTweens`
- Kill tweens in `OnDestroy()` to prevent leaks
- Store original scales: `Dictionary<Transform, Vector3> originalUiScales`
- Button click feedback: `DOPunchScale` with ~1.06x scale
- Fade duration: 0.22s, Scale from: 0.96, Ease: OutCubic

## Enemy AI Architecture

`EnemyController.cs` implements a **coroutine-based decision loop** on Server:

**State Priority (checked every frame):**
1. **Attack** - If within `stopDistance` (1.5m), choose attack type randomly
2. **Roll** - If within `rollTriggerDistance` (8m), continuous dash toward player
3. **Walk** - If within `walkThreshold` (12m), NavMesh walk
4. **Idle** - Beyond detection range

**Animation Blend Tree Setup:**
- Layer 0: Speed blend (Idle @ 0.0, Walk @ 0.5) via `netSpeed` NetworkVariable
- Layer 1: Roll 2D directional blend via `netRollDir` NetworkVariable (Vector2)
- Layer 2: Attack triggers (LightPunch, BarragePunch, Kick)

**Network Sync Flow:**
```
Server: DecisionLoop → Set netState/netSpeed/netRollDir → NetworkVariable broadcast
Client: OnValueChanged callbacks → Update Animator parameters
```

## Settings System

`SettingsManager.cs` is a **Singleton** with:
- **Tabbed UI:** Graphics, Audio, Gameplay (one sub-panel active at a time)
- **Instant Save:** All changes auto-save to PlayerPrefs via `.onValueChanged`
- **DOTween Pop-up:** Fade + Scale animation matching `OnlineNetworkUI.cs` style
- **Real-time Stats:** FPS counter (color-coded) and Ping display when toggle enabled

Access from anywhere: `SettingsManager.Instance.OpenSettings()`

## Development Commands

### Opening the Project
```bash
# Unity Hub method (recommended)
# Open Unity Hub → Add → Select this folder
# Unity 6000.3.6f1 required

# Direct Unity launch
/path/to/Unity.exe -projectPath "C:\path\to\Nsc-Unity"
```

### Building
Unity Editor: File → Build Settings → Select target platform → Build

### Testing Multiplayer Locally
1. Build a standalone executable
2. Run the build (acts as Client)
3. Play in Editor (acts as Host)
4. Or use ParrelSync to clone the project and run 2 Editor instances

### Debugging Network Issues
- Check Console for `[Server]` / `[Client]` logs from `EnemyController.cs`, `OnlineNetworkUI.cs`
- Unity Netcode has a built-in Network Profiler: Window → Multiplayer → Netcode Profiler
- Enable "Show Network Stats" toggle in Settings for in-game FPS/Ping overlay

## Coding Conventions

### Namespace Usage
Enemy AI uses `namespace NscGame.Enemy` - other systems do not use namespaces currently

### Network Script Pattern
```csharp
public class MyScript : NetworkBehaviour
{
    private NetworkVariable<T> netVar = new NetworkVariable<T>(
        defaultValue, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Server
    );

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        netVar.OnValueChanged += OnVarChanged;
        
        if (IsServer)
        {
            // Server-only setup
        }
    }

    public override void OnNetworkDespawn()
    {
        netVar.OnValueChanged -= OnVarChanged;
        base.OnNetworkDespawn();
    }

    private void OnVarChanged(T oldVal, T newVal)
    {
        // Runs on all Clients when Server writes to netVar
    }
}
```

### DOTween UI Pattern
```csharp
// In class scope
private Dictionary<GameObject, Tween> runningUiTweens = new Dictionary<GameObject, Tween>();

// Before animating
private void KillUiTween(GameObject target)
{
    if (runningUiTweens.TryGetValue(target, out Tween tween) && tween != null && tween.IsActive())
        tween.Kill(false);
    runningUiTweens.Remove(target);
}

// In OnDestroy
private void OnDestroy()
{
    foreach (Tween tween in runningUiTweens.Values)
        if (tween != null && tween.IsActive()) tween.Kill(false);
    runningUiTweens.Clear();
}
```

### Event Listener Cleanup
Always unbind in `OnDestroy()`:
```csharp
private void Start()
{
    button.onClick.AddListener(OnButtonClicked);
}

private void OnDestroy()
{
    if (button != null) button.onClick.RemoveListener(OnButtonClicked);
}
```

## Scene Structure

- **-Menu/** - Main menu scene with `OnlineNetworkUI`
- **SelectPart** - Lobby scene with `LobbyManager` (loaded after room created)
- **TheBestFolder/** - Main gameplay scenes

## Assets Organization

- `Assets/nok/` - Core gameplay (Enemy, Rope, Particle, Robot, Weapon)
- `Assets/map/` - Environment (city, buildings, shaders, materials)
- `Assets/Scenes/TheBestFolder/Mynigga/` - Main C# scripts
- `Assets/MenuUI/` - Menu-specific UI prefabs
- `Assets/Plugins/Demigiant/` - DOTween library
- `Assets/Something/` - Shared resources (TMP, shaders, tutorials)

## Common Pitfalls

1. **NetworkVariable changes not syncing:** Ensure you're writing on Server (`if (!IsServer) return`)
2. **UI not animating:** Check DOTween is imported and `.SetUpdate(true)` is used
3. **Memory leaks:** Always kill tweens and unbind events in `OnDestroy()`
4. **Animation not playing:** Check Animator parameter names match string constants in code (e.g., `"Speed"`, `"IsRolling"`)
5. **Player count stuck:** Use `NetworkVariable<int>` with callbacks, not local state
6. **Settings not saving:** Ensure `PlayerPrefs.Save()` is called after `PlayerPrefs.Set*()`

## MCP Integration

This project has MCP (Model Context Protocol) configured in `.mcp.json`:
- **filesystem** - Access project files
- **github** - Integration with repository (requires `GITHUB_TOKEN` env var)
- **memory** - Context persistence across sessions

Settings in `.claude/settings.json` enable all project MCP servers.
