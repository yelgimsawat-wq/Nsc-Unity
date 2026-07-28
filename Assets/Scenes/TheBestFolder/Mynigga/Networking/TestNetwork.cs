using UnityEngine;
using Unity.Netcode;
public class TestNetwork : NetworkBehaviour
{
    void Update()
    {
        if (!IsServer) return;
        var client = NetworkManager.Singleton.ConnectedClientsList;
        if (client.Count < 2) return;

        Transform player1 = client[0].PlayerObject.transform;
        Transform player2 = client[1].PlayerObject.transform;

        Vector3 mid = (player1.position + player2.position) / 2f;
        transform.position = mid;
    }
}
