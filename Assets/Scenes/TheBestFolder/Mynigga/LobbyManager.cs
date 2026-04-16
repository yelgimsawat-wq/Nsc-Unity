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

    private NetworkList<long> limbOwners;

    void Awake()
    {
        Instance = this;
        limbOwners = new NetworkList<long>(new long[] { -1, -1, -1, -1 });

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
        long clientId = (long)rpcParams.Receive.SenderClientId;
        if (limbOwners[index] == -1) limbOwners[index] = clientId;
    }

    [ServerRpc(RequireOwnership = false)]
    public void LaunchGameServerRpc()
    {
        StartGameClientRpc();
    }

    [ClientRpc]
    void StartGameClientRpc()
    {
        selectionPanel.SetActive(false);
        robotContainer.SetActive(true);

        if (IsServer && robotContainer != null)
        {
            foreach (var rigid in robotContainer.GetComponentsInChildren<Rigidbody>())
            {
                rigid.isKinematic = false;
                rigid.WakeUp();
            }
        }

        // วนลูปเช็คว่าใครเป็นเจ้าของชิ้นส่วนไหนบ้าง
        for (int i = 0; i < limbOwners.Count; i++)
        {
            if (limbOwners[i] == -1) continue;

            ulong ownerId = (ulong)limbOwners[i];
            GameObject playerObj = GetPlayerObject(ownerId); // ใช้ฟังก์ชันใหม่ในการหาตัวผู้เล่น

            if (playerObj != null)
            {
                // 1. ให้ทุกเครื่องเซ็ตเป้าหมาย (ทุกคนจะเห็นชิ้นส่วนวิ่งไปหาคนควบคุมถูกต้อง)
                Following targetLimb = GetLimbByIndex(i);
                if (targetLimb != null)
                {
                    targetLimb.targetPoint = playerObj.transform;
                }

                // 2. ถ้าคนนี้คือ "เครื่องเราเอง" ค่อยเปิดสคริปต์ควบคุมให้ขยับได้
                if (ownerId == NetworkManager.Singleton.LocalClientId)
                {
                    if (i >= 2) // ถ้าเป็น ขา
                    {
                        var footMovement = playerObj.GetComponent<EZFootMovement>();
                        if (footMovement != null)
                        {
                            footMovement.press();
                            footMovement.enabled = true;
                        }
                    }
                    else // ถ้าเป็น แขน
                    {
                        var armMovement = playerObj.GetComponent<EZMovement>();
                        if (armMovement != null)
                        {
                            armMovement.enabled = true;
                        }
                    }
                }
            }
            else
            {
                Debug.LogWarning($"ไม่พบ Player Object ของ Client ID {ownerId}");
            }
        }
    }

    private Following GetLimbByIndex(int index)
    {
        return index switch { 0 => leftArm, 1 => rightArm, 2 => leftLeg, 3 => rightLeg, _ => null };
    }

    // --- ฟังก์ชันใหม่: ใช้หา Player Object ที่ถูกต้อง ไม่หลง ID แน่นอน ---
    private GameObject GetPlayerObject(ulong clientId)
    {
        // 1. ถ้าเป็นตัวเราเอง (เร็วที่สุด)
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            if (NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
                return NetworkManager.Singleton.LocalClient.PlayerObject.gameObject;
        }

        // 2. ถ้าเป็นคนอื่น ให้ควานหาจากรายชื่อ Object ที่ถูก Spawn ทั้งหมด
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