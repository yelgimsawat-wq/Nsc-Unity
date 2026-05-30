using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// LobbyManager.cs
/// Canvas ทับบน Gameplay Scene — เลือก Part แล้ว Host กด Start
/// ปิด Panel + Unfreeze Physics ให้เกมเริ่ม (ไม่มี LoadScene)
/// </summary>
public class LobbyManager : NetworkBehaviour
{
    public static LobbyManager Instance;

    [Header("UI Panels")]
    public GameObject selectionPanel;
    public GameObject robotContainer;

    [Header("Robot Targets")]
    public Following leftArm;
    public Following rightArm;
    public Following leftLeg;
    public Following rightLeg;

    [SerializeField] private Button[] limbButtons;
    [SerializeField] private Button startButton; // Host only

    // NetworkList ต้อง init ระดับ field (ก่อน OnNetworkSpawn)
    private NetworkList<ulong> limbOwners = new NetworkList<ulong>(
        new ulong[] { ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue });

    // ================================================================
    //  UNITY LIFECYCLE
    // ================================================================

    void Awake()
    {
        Instance = this;

        for (int i = 0; i < limbButtons.Length; i++)
        {
            int captured = i;
            limbButtons[captured].onClick.AddListener(() => RequestLimbServerRpc(captured));
        }
    }

    // ================================================================
    //  NETWORK SPAWN — Freeze physics, wire UI, subscribe list changes
    // ================================================================

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Freeze ทุก Rigidbody ตอนเปิด Panel ทั้ง Host และ Client
        FreezeAllPhysics();

        // Subscribe NetworkList → อัปเดต UI ปุ่มทุกครั้งที่มีคนจอง
        limbOwners.OnListChanged += OnLimbOwnersChanged;

        // Refresh ปุ่มให้ตรงกับ state ปัจจุบัน (กรณี Client join หลัง Host จองไปแล้ว)
        RefreshAllButtonUI();

        if (startButton != null)
        {
            startButton.gameObject.SetActive(IsServer);
            if (IsServer)
            {
                startButton.onClick.RemoveAllListeners();
                startButton.onClick.AddListener(OnStartButtonClicked);
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        limbOwners.OnListChanged -= OnLimbOwnersChanged;
        base.OnNetworkDespawn();
    }

    // ================================================================
    //  NetworkList callback → update button visuals
    // ================================================================

    private void OnLimbOwnersChanged(NetworkListEvent<ulong> changeEvent)
    {
        RefreshAllButtonUI();
    }

    private void RefreshAllButtonUI()
    {
        for (int i = 0; i < limbButtons.Length; i++)
        {
            if (limbButtons[i] == null) continue;

            ulong owner  = limbOwners[i];
            bool isTaken = owner != ulong.MaxValue;
            bool isMine  = isTaken && owner == NetworkManager.Singleton.LocalClientId;

            // กดได้ถ้า: ว่างอยู่ หรือ เป็นของตัวเอง (เพื่อสลับ)
            // กดไม่ได้ถ้า: คนอื่นจองไปแล้ว
            limbButtons[i].interactable = !isTaken || isMine;

            var label = limbButtons[i].GetComponentInChildren<TMPro.TextMeshProUGUI>();
            if (label != null)
            {
                if (isMine)       label.text = "✓ You";
                else if (isTaken) label.text = "Taken";
                else              label.text = GetDefaultLimbName(i);
            }
        }
    }

    private string GetDefaultLimbName(int index) => index switch
    {
        0 => "Left Arm",
        1 => "Right Arm",
        2 => "Left Leg",
        3 => "Right Leg",
        _ => "?"
    };

    // ================================================================
    //  Select Part — Send to Server to check availability
    // ================================================================

    [ServerRpc(RequireOwnership = false)]
    public void RequestLimbServerRpc(int index, ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;

        // ถ้าอันที่กดอยู่แล้วของคนอื่น → ไม่ทำอะไร
        if (limbOwners[index] != ulong.MaxValue && limbOwners[index] != clientId)
        {
            Debug.Log($"[Server] Part {index} already reserved by Client {limbOwners[index]}");
            return;
        }

        // ยกเลิกอันเก่าของตัวเองก่อน (ถ้าเคยจองไว้)
        for (int i = 0; i < limbOwners.Count; i++)
        {
            if (limbOwners[i] == clientId)
            {
                limbOwners[i] = ulong.MaxValue;
                Debug.Log($"[Server] Client {clientId} released Part {i}");
                break;
            }
        }

        // จองอันใหม่
        limbOwners[index] = clientId;
        Debug.Log($"[Server] Client {clientId} reserved Part {index}");
    }

    // ================================================================
    //  Host clicks Start — ปิด Panel + Unfreeze (ไม่มี LoadScene)
    // ================================================================

    private void OnStartButtonClicked()
    {
        if (!IsServer) return;
        if (startButton != null) startButton.interactable = false;

        // ยิง ClientRpc ไปทุกคนพร้อมกัน:
        // 1. Assign limb → player
        // 2. ปิด selectionPanel
        // 3. Unfreeze physics → เกมเริ่ม
        StartGameClientRpc();

        Debug.Log("[Server] Game started — panel closed, physics unfrozen.");
    }

    // ================================================================
    //  Start Game — runs on ALL Clients (including Host)
    // ================================================================

    [ClientRpc]
    void StartGameClientRpc()
    {
        // Assign limb → player ก่อน (retry เผื่อ PlayerObject spawn ไม่ทัน)
        StartCoroutine(AssignLimbsThenUnfreeze());
    }

    private IEnumerator AssignLimbsThenUnfreeze(int maxAttempts = 10, float retryDelay = 0.2f)
    {
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            bool allResolved = TryAssignAllLimbs();

            if (allResolved)
            {
                Debug.Log("[Client] All limbs assigned.");
                break;
            }

            Debug.Log($"[Client] Limb assign attempt {attempt + 1}/{maxAttempts} — retrying...");
            yield return new WaitForSeconds(retryDelay);
        }

        // ปิด Panel + Unfreeze ไม่ว่า assign สำเร็จครบหรือเปล่า
        // (เพื่อไม่ให้เกมค้างถ้า PlayerObject หาไม่เจอ)
        if (selectionPanel != null) selectionPanel.SetActive(false);
        UnfreezeAllPhysics();
    }

    private bool TryAssignAllLimbs()
    {
        bool allDone = true;

        for (int i = 0; i < limbOwners.Count; i++)
        {
            if (limbOwners[i] == ulong.MaxValue) continue;

            ulong ownerId  = limbOwners[i];
            GameObject playerObj = GetPlayerObject(ownerId);

            if (playerObj == null)
            {
                allDone = false;
                continue;
            }

            Following targetLimb = GetLimbByIndex(i);
            if (targetLimb != null)
                targetLimb.targetPoint = playerObj.transform;

            if (ownerId != NetworkManager.Singleton.LocalClientId) continue;

            if (i >= 2) // Legs
            {
                var footMovement = playerObj.GetComponent<EZFootMovement>();
                if (footMovement != null)
                {
                    if (targetLimb?.pivotPoint != null)
                        footMovement.attachPart = targetLimb.pivotPoint;
                    if (targetLimb != null)
                        footMovement.physicalFootTransform = targetLimb.transform;
                    footMovement.enabled = true;
                }
            }
            else // Arms
            {
                var armMovement = playerObj.GetComponent<EZMovement>();
                if (armMovement != null)
                {
                    if (targetLimb?.pivotPoint != null)
                        armMovement.attachPart = targetLimb.pivotPoint;
                    if (targetLimb != null)
                        armMovement.physicalHandTransform = targetLimb.transform;
                    armMovement.enabled = true;
                }
            }
        }

        return allDone;
    }

    // ================================================================
    //  Freeze / Unfreeze Physics
    // ================================================================

    private void FreezeAllPhysics()
    {
        if (robotContainer == null) return;
        foreach (var rigid in robotContainer.GetComponentsInChildren<Rigidbody>())
        {
            rigid.isKinematic = true;
            rigid.Sleep();
        }
        Debug.Log("[Lobby] Physics frozen.");
    }

    private void UnfreezeAllPhysics()
    {
        if (robotContainer == null) return;
        foreach (var rigid in robotContainer.GetComponentsInChildren<Rigidbody>())
        {
            rigid.isKinematic = false;
            rigid.WakeUp();
        }
        Debug.Log("[Lobby] Physics unfrozen — Game running!");
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private Following GetLimbByIndex(int index)
    {
        return index switch { 0 => leftArm, 1 => rightArm, 2 => leftLeg, 3 => rightLeg, _ => null };
    }

    private GameObject GetPlayerObject(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            if (NetworkManager.Singleton.LocalClient?.PlayerObject != null)
                return NetworkManager.Singleton.LocalClient.PlayerObject.gameObject;
        }

        foreach (var netObj in NetworkManager.Singleton.SpawnManager.SpawnedObjectsList)
        {
            if (netObj.IsPlayerObject && netObj.OwnerClientId == clientId)
                return netObj.gameObject;
        }

        return null;
    }
}