using UnityEngine;
using Unity.Netcode;

public class PlayerCam : NetworkBehaviour
{
    [SerializeField] private Camera playercam;
    [SerializeField] private AudioListener playeral;
    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            playercam.enabled = true;
            playeral.enabled = true;

            playercam.gameObject.tag = "MainCamera";
        }
        else
        {
            playercam.enabled = false;
            playeral.enabled = false;
        }
    }
}
