using Unity.Netcode;
using UnityEngine;

public class NetworkUI : MonoBehaviour
{
    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 300, 300));

        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            if (GUILayout.Button("Start Host"))
            {
                NetworkManager.Singleton.StartHost();
            }
            if (GUILayout.Button("Start Client"))
            {
                NetworkManager.Singleton.StartClient();
            }
        }

        GUILayout.EndArea();
    }
}