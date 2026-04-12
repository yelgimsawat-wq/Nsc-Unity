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
    // เก็บ ID ผู้เล่นที่จอง limb (0-3)
    private NetworkList<long> limbOwners;

    void Awake()
    {
        Instance = this;
        limbOwners = new NetworkList<long>(new long[] { -1, -1, -1, -1 });
        limbButtons[0].onClick.AddListener(() => RequestLimbServerRpc(0));
        limbButtons[1].onClick.AddListener(() => RequestLimbServerRpc(1));
        limbButtons[2].onClick.AddListener(() => RequestLimbServerRpc(2));
        limbButtons[3].onClick.AddListener(() => RequestLimbServerRpc(3));

    }

    // --- STEP 1: ผู้เล่นกดเลือกแขนขา ---
    [ServerRpc]
    public void RequestLimbServerRpc(int index, ServerRpcParams rpcParams = default)
    {
        long clientId = (long)rpcParams.Receive.SenderClientId;
        if (limbOwners[index] == -1) limbOwners[index] = clientId;
    }

    // --- STEP 2: Host กดปุ่มเริ่มเกม ---
    [ServerRpc]
    public void LaunchGameServerRpc()
    {
        AssignPlayersToLimbs(); // มอบหมาย Transform ให้ Following
        StartGameClientRpc();   // ปิด UI และเปิดหุ่นให้ทุกคน
    }

    // --- STEP 3: ฟังก์ชันหัวใจหลัก (มอบหมาย Player ไปที่ Target) ---
    private void AssignPlayersToLimbs()
    {
        for (int i = 0; i < limbOwners.Count; i++)
        {
            if (limbOwners[i] == -1) continue;

            if (NetworkManager.Singleton.ConnectedClients.TryGetValue((ulong)limbOwners[i], out var client))
            {
                // ดึง Transform ของ Player และส่งไปให้ Following ตามลำดับ i
                Following targetLimb = GetLimbByIndex(i);
                if (targetLimb != null)
                {
                    targetLimb.targetPoint = client.PlayerObject.transform;
                }
            }
        }
    }

    private Following GetLimbByIndex(int index)
    {
        return index switch { 0 => leftArm, 1 => rightArm, 2 => leftLeg, 3 => rightLeg, _ => null };
    }

    [ClientRpc]
    void StartGameClientRpc()
    {
        selectionPanel.SetActive(false);
        robotContainer.SetActive(true);
    }
}