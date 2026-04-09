using UnityEngine;
using Unity.Netcode;

// 1. Inherit from NetworkBehaviour instead of MonoBehaviour
public class EZMovement : NetworkBehaviour
{
    void Start()
    {
        
    }

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

        Vector3 move = new Vector3(moveX, 0, moveZ) * speed * Time.deltaTime;
        transform.Translate(move);
    }
}
