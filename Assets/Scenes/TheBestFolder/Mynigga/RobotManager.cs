using System.Linq;
using Unity.Netcode;
using UnityEngine;

public class RobotManager : NetworkBehaviour
{
    public Following[] limps;
    public GameObject[] Player;
    public override void OnNetworkSpawn()
    {
    }
}
