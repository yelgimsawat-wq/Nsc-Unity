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

    [Header("Hit Reaction VFX/SFX")]
    [Tooltip("Particle ที่เกิดตรงจุดโดนต่อย — Server สั่งให้เกิดพร้อมกันทุกเครื่องผ่าน ClientRpc\n" +
             "ถ้าเว้นว่างไว้จะไม่มีเอฟเฟค (ยังมีของฝั่งมือจาก PhysicsDamageSender อยู่)")]
    [SerializeField] private GameObject hitVfxPrefab;

    [Tooltip("เสียงตอนโดนต่อย")]
    [SerializeField] private AudioClip sfxHit;

    [Tooltip("เว้นว่างได้ — จะไปหา AudioSource บน GameObject นี้ให้เองตอน Awake")]
    [SerializeField] private AudioSource audioSource;

    private float timer = 0;
    private bool regening = true;

    private void Awake()
    {
        // ไม่เขียนทับตัวที่ลากใส่ใน Inspector ไว้แล้ว (AudioSource อาจอยู่บนลูก)
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
    }

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

    /// <summary>
    /// [SERVER] Overload ที่รับ "จุดปะทะจริง" เพิ่มมา — ใช้ตอนหุ่นอีกทีมต่อยโดน (PVP)
    ///
    /// ทำไมต้องมีตัวนี้: เดิม PhysicsDamageSender สร้างเอฟเฟคเองแบบ local
    /// → คนที่เห็นมีแค่เครื่องที่ตรวจเจอการชน (ฝั่งคนต่อย) ส่วนคนโดนกับคนอื่นในห้อง
    /// ไม่เห็นอะไรเลย ตัวนี้เลยยิง ClientRpc ให้เอฟเฟคขึ้นพร้อมกันทุกจอ
    /// ตามแบบเดียวกับ EnemyHealth.ServerTakeHit ฝั่งบอส
    /// </summary>
    /// <param name="hitPoint">จุดที่หมัด/เท้าปะทะจริงในโลก (Collision.contacts[0].point)</param>
    public void ServerTakeDamage(float amount, AttackType source, Vector3 direction, Vector3 hitPoint)
    {
        if (!IsServer) return;

        // ยิงเอฟเฟคก่อนคิดดาเมจ เพื่อให้ "หมัดสุดท้ายที่ทำให้ชิ้นหลุด" ยังมีเอฟเฟคให้เห็น
        // (ถ้ายิงทีหลัง ApplyDamage อาจ Break() ไปแล้วจน CanTakeDamage เป็น false)
        if (CanTakeDamage())
            PlayHitEffectsClientRpc(hitPoint, direction);

        ServerTakeDamage(amount, source, direction);
    }

    /// <summary>สร้าง VFX + เล่นเสียงตรงจุดโดนต่อย — รันบนทุกเครื่อง</summary>
    [ClientRpc]
    private void PlayHitEffectsClientRpc(Vector3 impactPosition, Vector3 hitDirection)
    {
        if (hitVfxPrefab != null)
        {
            // หัน particle ตามทิศหมัด — ถ้าทิศเป็นศูนย์ (กันหาร 0) ใช้หมุนเปล่า
            Quaternion rotation = hitDirection.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(hitDirection)
                : Quaternion.identity;

            GameObject vfx = Instantiate(hitVfxPrefab, impactPosition, rotation);

            // เก็บกวาดเองหลังเล่นจบ — ไม่งั้นซากเอฟเฟคค้างในฉากเรื่อยๆ
            ParticleSystem ps = vfx.GetComponentInChildren<ParticleSystem>();
            Destroy(vfx, ps != null ? ps.main.duration + 1f : 2f);
        }

        if (sfxHit != null && audioSource != null)
            audioSource.PlayOneShot(sfxHit);
    }

    /// <summary>
    /// ชิ้นนี้ยังกินดาเมจได้อยู่ไหม — แยกออกมาเป็นฟังก์ชันเพื่อให้เอฟเฟคตอนโดนต่อย
    /// ใช้เงื่อนไขชุดเดียวกับดาเมจเป๊ะๆ (ชิ้นที่หลุด/พังแล้วจะได้ไม่เด้งเอฟเฟคลอยๆ)
    /// </summary>
    private bool CanTakeDamage()
    {
        // ✅ ชิ้นที่หลุดไปแล้วไม่รับดาเมจ — ไม่งั้นเลือดที่ regen ระหว่างหลุด
        // จะโดนตีจนหมดซ้ำ → Break() ซ้ำ → เพดานเลือดโดนหักรัวๆ ทั้งที่หลุดไปแล้ว
        if (Jpar != null && !Jpar.IsConnected) return false;

        if (currentHp.Value <= 0) return false; // พังไปแล้ว ไม่ต้องลบเลือดซ้ำ

        return true;
    }

    private void ApplyDamage(float amount)
    {
        if (!CanTakeDamage()) return;

        currentHp.Value -= amount;
        regening = false;
        timer = 0;

        if (currentHp.Value <= 0)
        {
            currentHp.Value = 0;
            Break();
        }
    }

    /// <summary>
    /// [SERVER] บังคับให้ชิ้นนี้หลุดทันทีโดยไม่สนเลือดที่เหลือ — ใช้กับท่าไม้ตายของบอส
    /// ที่กำหนดว่า "โดนแล้วหลุดเลย"
    ///
    /// เดินผ่าน Break() ตัวเดิมโดยตั้งใจ เพื่อให้เพดานเลือด (currentMaxHp) ถูกหัก
    /// ตามกติกาเดียวกับการหลุดจากดาเมจปกติ ถ้าเรียก Jpar.ForceBreakJoint() ตรงๆ
    /// ชิ้นจะหลุดแต่เพดานไม่ลด = ต่อกลับมาแล้วแข็งแรงเท่าเดิม ผู้เล่นไม่เปราะขึ้นเลย
    /// </summary>
    public void ServerBreakPart()
    {
        if (!IsServer) return;

        // หลุดไปแล้วไม่ต้องหักเพดานซ้ำ
        if (Jpar != null && !Jpar.IsConnected) return;

        currentHp.Value = 0;
        Break();
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