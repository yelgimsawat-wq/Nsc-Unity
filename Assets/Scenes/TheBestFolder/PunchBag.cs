using UnityEngine;
using Unity.Netcode;
using NscGame.Enemy;
public class PunchBag : NetworkBehaviour , IHittable
{
    private Rigidbody rb;
    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }
    public void ServerTakeDamage(float amount, AttackType source)
    {
        if (!IsServer) return;
        Debug.Log($"PunchBag took {amount} damage from {source}");
    }

    public void ServerTakeDamage(float amount, AttackType source, Vector3 direction)
    {
        if (!IsServer) return;
        Debug.Log($"PunchBag took {amount} damage from {source} with direction {direction}");
        rb.AddForce(direction * amount * 1.2f, ForceMode.Impulse);
    }
}
