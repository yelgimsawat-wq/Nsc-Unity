using Nsc.Combat;
using Unity.Netcode;
using UnityEngine;

/// <summary>กระสอบทรายทดสอบหมัด — รับดาเมจได้ทุกฝ่าย แค่ log ค่าแล้วกระเด็นตามแรง</summary>
public class PunchBag : NetworkBehaviour, IDamageable
{
    private Rigidbody rb;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    public Team GetTeam() => Team.None;

    public bool ServerApplyDamage(DamageInfo info)
    {
        Debug.Log($"PunchBag took {info.amount} damage from {info.source}");
        if (rb != null) rb.AddForce(info.direction * info.amount * 1.2f, ForceMode.Impulse);
        return true;
    }
}
