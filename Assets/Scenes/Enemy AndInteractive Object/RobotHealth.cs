using NscGame.Enemy;
using UnityEngine;
using Unity.Netcode; // ✅ ต้องใช้ Netcode

public class RobotHealth : NetworkBehaviour, IHittable
{
    [Header("Health Settings")]
    public float MaxHp = 500f;

    // ✅ ใช้ NetworkVariable เพื่อให้เลือดตรงกันทุกจอ
    public NetworkVariable<float> currentHp = new NetworkVariable<float>(500f);

    [Header("References")]
    public JointPullAndReconnect Jpar;

    private float timer = 0;
    private bool regening = true;

    public override void OnNetworkSpawn()
    {
        // ให้ Server เป็นคนกำหนดเลือดเริ่มต้นตอนเกิด
        if (IsServer)
        {
            currentHp.Value = MaxHp;
        }
    }

    private void Update()
    {
        // ✅ ให้ Server เป็นคนคำนวณเลือดเท่านั้น Client มีหน้าที่แค่รอรับค่าไปโชว์ UI
        if (!IsServer) return;

        if (regening)
        {
            // ✅ กันไม่ให้เลือดเด้งเกิน MaxHp
            if (currentHp.Value < MaxHp)
            {
                currentHp.Value = Mathf.Min(MaxHp, currentHp.Value + (2f * Time.deltaTime));
            }
        }
        else
        {
            timer += Time.deltaTime;
            if (timer > 7.5f)
            {
                timer = 0;
                regening = true;
            }
        }
    }

    public void ServerTakeDamage(float amount, AttackType source)
    {
        if (!IsServer) return; // ล็อกความปลอดภัย
        ApplyDamage(amount);
    }

    // Overload ที่คุณสร้างเพิ่มสำหรับรับทิศทางกระเด็น
    public void ServerTakeDamage(float amount, AttackType source, Vector3 direction)
    {
        if (!IsServer) return; // ล็อกความปลอดภัย
        ApplyDamage(amount);

        // ✅ ปรับฟิสิกส์กระเด็นให้สมเหตุสมผลขึ้น (ใช้ ForceMode.Impulse สำหรับการถูกกระแทก)
        if (TryGetComponent<Rigidbody>(out Rigidbody Rb))
        {
            Rb.AddForce(direction * (amount * 2f), ForceMode.Impulse);
        }
    }

    private void ApplyDamage(float amount)
    {
        if (currentHp.Value <= 0) return; // พังไปแล้ว ไม่ต้องลบเลือดซ้ำ

        currentHp.Value -= amount;
        regening = false;
        timer = 0;

        if (currentHp.Value <= 0)
        {
            currentHp.Value = 0;
            Break();
        }
    }

    void Break()
    {
        if (Jpar != null)
        {
            Jpar.ForceBreakJoint();

            // (Optionally) Reset HP ให้มันฟื้นใหม่หลังชิ้นส่วนพัง
            // currentHp.Value = MaxHp; 
        }
    }
}