using UnityEngine;
using Unity.Netcode;

// 1. Inherit from NetworkBehaviour instead of MonoBehaviour
public class EZMovement : NetworkBehaviour
{
    public NetworkVariable<int> hp = new NetworkVariable<int>(100);

    public float speed = 5f;

    void Update()
    {
        // 2. The IsOwner check
        // If this instance of the script does NOT belong to the local player, ignore it.
        // This prevents you from controlling other players' characters!
        if (!IsOwner) return;

        // 3. Normal movement logic
        float moveX = Input.GetAxis("Horizontal");
        float moveZ = Input.GetAxis("Vertical");
        float moveY = 0f;
        if (Input.GetKey(KeyCode.Q))
        {
            moveY = 1f;
        }
        else if (Input.GetKey(KeyCode.E))
        {
            moveY = -1f;
        }
        Vector3 move = new Vector3(moveX, moveY, moveZ) * speed * Time.deltaTime;
        transform.Translate(move);

        if (Input.GetKeyDown(KeyCode.Space))
        {
            TakeDamageServerRpc(10); // Example: Take 10 damage when space is pressed
        }
    }
    [ServerRpc]
    void TakeDamageServerRpc(int damage)
    {
        hp.Value -= damage;
    }
}
