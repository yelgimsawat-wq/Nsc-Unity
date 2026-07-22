using NscGame.Enemy;
using UnityEngine;
using Unity.Netcode; // ✅ ต้องใช้ Netcode

public class RobotHealth : NetworkBehaviour, IHittable
{
    [Header("Health Settings")]
    public float MaxHp = 500f;

    [Tooltip("เพดานเลือดลดลงเท่านี้ทุกครั้งที่ชิ้นส่วนหลุด — ยิ่งหลุดบ่อยยิ่งเปราะ ผู้เล่นถึงแพ้ได้")]
    public float hpLossPerBreak = 125f;

    [Tooltip("เพดานเลือดต่ำสุด (ลดจนต่ำกว่านี้ไม่ได้)")]
    public float minMaxHp = 50f;

    // ✅ ใช้ NetworkVariable เพื่อให้เลือดตรงกันทุกจอ
    public NetworkVariable<float> currentHp = new NetworkVariable<float>(500f);

    // เพดานเลือดปัจจุบัน — เริ่มที่ MaxHp แล้วลดลงทีละ hpLossPerBreak ทุกครั้งที่หลุด
    // เป็น NetworkVariable เพื่อให้ HUD ทุกเครื่องคำนวณ % เลือดจากเพดานจริง ไม่ใช่ MaxHp ตายตัว
    public NetworkVariable<float> currentMaxHp = new NetworkVariable<float>(
        500f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    [Header("References")]
    public JointPullAndReconnect Jpar;

    private float timer = 0;
    private bool regening = true;

    public override void OnNetworkSpawn()
    {
        // ให้ Server เป็นคนกำหนดเลือดเริ่มต้นตอนเกิด
        if (IsServer)
        {
            currentMaxHp.Value = MaxHp;
            currentHp.Value = currentMaxHp.Value;

            // ✅ ต่อชิ้นส่วนกลับ (กด R ดึงคืน) → เลือดเต็ม "ตามเพดานปัจจุบัน"
            // ซึ่งลดลงทุกครั้งที่หลุด — หลุดบ่อยยิ่งเปราะ ผู้เล่นถึงแพ้ได้
            if (Jpar != null)
                Jpar.OnConnectionStateChanged += OnPartConnectionChanged;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && Jpar != null)
            Jpar.OnConnectionStateChanged -= OnPartConnectionChanged;
        base.OnNetworkDespawn();
    }

    private void OnPartConnectionChanged(bool connected)
    {
        if (!IsServer) return;
        if (connected)
        {
            currentHp.Value = currentMaxHp.Value;
            regening = true;
            timer = 0;
        }
    }

    private void Update()
    {
        // ✅ ให้ Server เป็นคนคำนวณเลือดเท่านั้น Client มีหน้าที่แค่รอรับค่าไปโชว์ UI
        if (!IsServer) return;

        if (regening)
        {
            // ✅ regen ได้ไม่เกิน "เพดานปัจจุบัน" (ลดลงทุกครั้งที่ชิ้นเคยหลุด)
            if (currentHp.Value < currentMaxHp.Value)
            {
                currentHp.Value = Mathf.Min(currentMaxHp.Value, currentHp.Value + (2f * Time.deltaTime));
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
        // ✅ ชิ้นที่หลุดไปแล้วไม่รับดาเมจ — ไม่งั้นเลือดที่ regen ระหว่างหลุด
        // จะโดนตีจนหมดซ้ำ → Break() ซ้ำ → เพดานเลือดโดนหักรัวๆ ทั้งที่หลุดไปแล้ว
        if (Jpar != null && !Jpar.IsConnected) return;

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
        // ทุกครั้งที่หลุด เพดานเลือดของชิ้นนี้ลดถาวร (ต่อกลับก็ได้แค่เพดานใหม่)
        currentMaxHp.Value = Mathf.Max(minMaxHp, currentMaxHp.Value - hpLossPerBreak);
        Debug.Log($"[RobotHealth] 💔 {gameObject.name} หลุด! เพดานเลือดเหลือ {currentMaxHp.Value}");

        if (Jpar != null)
        {
            Jpar.ForceBreakJoint();
        }
    }
}