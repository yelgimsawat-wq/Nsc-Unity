using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

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
    [SerializeField] private Button startButton;

    private NetworkList<ulong> limbOwners;

    void Awake()
    {
        Instance = this;
        limbOwners = new NetworkList<ulong>(new ulong[] { ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue });

        limbButtons[0].onClick.AddListener(() => RequestLimbServerRpc(0));
        limbButtons[1].onClick.AddListener(() => RequestLimbServerRpc(1));
        limbButtons[2].onClick.AddListener(() => RequestLimbServerRpc(2));
        limbButtons[3].onClick.AddListener(() => RequestLimbServerRpc(3));

        if (startButton != null)
        {
            startButton.onClick.AddListener(() =>
            {
                if (IsServer) LaunchGameServerRpc();
            });
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestLimbServerRpc(int index, ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        if (limbOwners[index] == ulong.MaxValue) limbOwners[index] = clientId;
    }

    [ServerRpc(RequireOwnership = false)]
    public void LaunchGameServerRpc(ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId) return;

        WakeUpPhysicsServer();
        StartGameClientRpc();
    }

    private void WakeUpPhysicsServer()
    {
        if (robotContainer != null)
        {
            foreach (var rigid in robotContainer.GetComponentsInChildren<Rigidbody>())
            {
                rigid.isKinematic = false;
                rigid.WakeUp();
            }
        }
    }

    [ClientRpc]
    void StartGameClientRpc()
    {
        selectionPanel.SetActive(false);
        robotContainer.SetActive(true);

        for (int i = 0; i < limbOwners.Count; i++)
        {
            if (limbOwners[i] == ulong.MaxValue) continue;

            ulong ownerId = limbOwners[i];
            GameObject playerObj = GetPlayerObject(ownerId);

            if (playerObj != null)
            {
                Following targetLimb = GetLimbByIndex(i);
                if (targetLimb != null)
                {
                    targetLimb.targetPoint = playerObj.transform;
                }

                if (ownerId == NetworkManager.Singleton.LocalClientId)
                {
                    if (i >= 2) // Legs
                    {
                        var footMovement = playerObj.GetComponent<EZFootMovement>();
                        if (footMovement != null)
                        {
                            if (targetLimb != null && targetLimb.pivotPoint != null)
                            {
                                footMovement.attachPart = targetLimb.pivotPoint;
                            }
                            if (targetLimb != null)
                            {
                                footMovement.physicalFootTransform = targetLimb.transform;
                            }
                            footMovement.enabled = true;
                        }
                    }
                    else // Arms
                    {
                        var armMovement = playerObj.GetComponent<EZMovement>();
                        if (armMovement != null)
                        {
                            if (targetLimb != null && targetLimb.pivotPoint != null)
                            {
                                armMovement.attachPart = targetLimb.pivotPoint;
                            }
                            if (targetLimb != null)
                            {
                                armMovement.physicalHandTransform = targetLimb.transform;
                            }
                            armMovement.enabled = true;
                        }
                    }
                }
            }
            else
            {
                Debug.LogWarning($"Could not find Player Object for Client ID {ownerId}");
            }
        }
    }

    private Following GetLimbByIndex(int index)
    {
        return index switch { 0 => leftArm, 1 => rightArm, 2 => leftLeg, 3 => rightLeg, _ => null };
    }

    private GameObject GetPlayerObject(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            if (NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
                return NetworkManager.Singleton.LocalClient.PlayerObject.gameObject;
        }

        foreach (var netObj in NetworkManager.Singleton.SpawnManager.SpawnedObjectsList)
        {
            if (netObj.IsPlayerObject && netObj.OwnerClientId == clientId)
            {
                return netObj.gameObject;
            }
        }

        return null;
    }
}
