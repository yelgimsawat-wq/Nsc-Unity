using System.Collections;
using UnityEngine;
using Unity.Netcode;
using NscGame.Enemy;

public class explobuilding : NetworkBehaviour, IHittable
{
    [Header("Building Health Settings")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float currentHealth = 100f;

    [Header("VFX & SFX")]
    [SerializeField] private GameObject explosionVfxPrefab;
    [SerializeField] private AudioClip explosionSfx;
    [SerializeField] private float shrinkDuration = 2f;

    [Header("Hit Effect")]
    [SerializeField] private GameObject hitVfxPrefab;

    [Header("Collapse Settings")]
    [SerializeField] private float shakeDuration = 1.5f;
    [SerializeField] private float shakeStrength = 0.2f;
    [SerializeField] private float sinkDistance = 5f;

    private AudioSource audioSource;
    private bool isDestroyed = false;

    private void Awake()
    {
        currentHealth = maxHealth;

        audioSource = GetComponent<AudioSource>();

        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            currentHealth = maxHealth;
        }
    }

    // =============================
    // IHittable
    // =============================

    public void ServerTakeDamage(float amount, AttackType source)
    {
        if (!NetworkCheck.IsServerOrHost()) return;
        TakeDamage(amount);
    }

    public void ServerTakeDamage(float amount, AttackType source, Vector3 direction)
    {
        if (!NetworkCheck.IsServerOrHost()) return;
        TakeDamage(amount);
    }

    // =============================
    // Damage
    // =============================

    public void TakeDamage(float amount)
    {
        if (isDestroyed) return;
        if (!NetworkCheck.IsServerOrHost()) return;

        currentHealth -= amount;

        Debug.Log($"Building HP : {currentHealth}/{maxHealth}");

        if (currentHealth <= 0)
        {
            currentHealth = 0;
            isDestroyed = true;
            TriggerDestruction();
        }
    }

    // =============================
    // Collision
    // =============================

    private void OnCollisionEnter(Collision collision)
    {
        if (!NetworkCheck.IsServerOrHost()) return;
        if (isDestroyed) return;

        PhysicsDamageSender sender = collision.gameObject.GetComponent<PhysicsDamageSender>();

        if (sender == null)
        {
            sender = collision.gameObject.GetComponentInParent<PhysicsDamageSender>();
        }

        if (sender == null)
            return;

        float impactSpeed = collision.relativeVelocity.magnitude;

        if (impactSpeed < sender.minVelocityThreshold)
            return;

        float damage = 0f;

        PlayerHandCombat combat = collision.gameObject.GetComponent<PlayerHandCombat>();

        if (combat == null)
        {
            combat = collision.gameObject.GetComponentInParent<PlayerHandCombat>();
        }

        if (combat != null && combat.CanDealDamage)
        {
            damage = Mathf.Min(
                combat.PeakPunchSpeed * sender.speedToDamage,
                sender.maxDamagePerHit
            );
        }
        else
        {
            float impactForce = collision.impulse.magnitude / Time.fixedDeltaTime;

            damage = Mathf.Min(
                impactForce * sender.forceToDamage,
                sender.maxDamagePerHit
            );
        }

        if (damage <= 0f)
            return;

        if (hitVfxPrefab != null && collision.contactCount > 0)
        {
            ContactPoint hit = collision.GetContact(0);

            Instantiate(
                hitVfxPrefab,
                hit.point,
                Quaternion.LookRotation(hit.normal)
            );
        }

        TakeDamage(damage);
    }
        // =============================
    // Destroy
    // =============================

    private void TriggerDestruction()
    {
        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            TriggerExplosionClientRpc();
            StartCoroutine(ServerDespawnSequence());
        }
        else
        {
            StartCoroutine(OfflineDestructionSequence());
        }
    }

    [ClientRpc]
    private void TriggerExplosionClientRpc()
    {
        if (SystemInfo.graphicsDeviceType ==
            UnityEngine.Rendering.GraphicsDeviceType.Null)
            return;

        StartCoroutine(ClientCollapseSequence());
    }

    private IEnumerator ServerDespawnSequence()
    {
        DisableColliders();

        yield return new WaitForSeconds(shakeDuration + 0.2f);

        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn(true);
        }
    }

    private IEnumerator OfflineDestructionSequence()
    {
        DisableColliders();

        if (explosionVfxPrefab != null)
        {
            Instantiate(explosionVfxPrefab, transform.position, Quaternion.identity);
        }

        if (explosionSfx != null && audioSource != null)
        {
            audioSource.PlayOneShot(explosionSfx);
        }

        yield return StartCoroutine(CollapseAnimation());

        Destroy(gameObject);
    }

    private IEnumerator ClientCollapseSequence()
    {
        DisableColliders();

        if (explosionVfxPrefab != null)
        {
            Instantiate(explosionVfxPrefab, transform.position, Quaternion.identity);
        }

        if (explosionSfx != null && audioSource != null)
        {
            audioSource.PlayOneShot(explosionSfx);
        }

        yield return StartCoroutine(CollapseAnimation());
    }

    private IEnumerator CollapseAnimation()
    {
        Vector3 startPos = transform.position;
        Vector3 endPos = startPos + Vector3.down * sinkDistance;

        float timer = 0f;

        while (timer < shakeDuration)
        {
            timer += Time.deltaTime;

            float t = timer / shakeDuration;

            Vector3 shake =
                Random.insideUnitSphere * shakeStrength * (1f - t);

            shake.y *= 0.2f;

            transform.position =
                Vector3.Lerp(startPos, endPos, t) + shake;

            yield return null;
        }

        transform.position = endPos;
    }

    private void DisableColliders()
    {
        foreach (Collider col in GetComponentsInChildren<Collider>())
        {
            col.enabled = false;
        }

        // ✅ [NavMesh] ตึกเจาะรูบน NavMesh ด้วย NavMeshObstacle (carving) ตอน runtime
        // ต้องปิดพร้อมกับ collider ทันทีที่เริ่มพัง ไม่ใช่รอ Despawn ท้ายอนิเมชัน
        // ไม่งั้นรูจะจมตามตัวตึกลงไป 5 หน่วยแล้ว re-carve ทุกเฟรม = พื้นตรงนั้นยังเดินไม่ได้
        // ทั้งที่ตึกพังแล้ว และเสีย perf ฟรีตลอดช่วง shake
        foreach (UnityEngine.AI.NavMeshObstacle obstacle in GetComponentsInChildren<UnityEngine.AI.NavMeshObstacle>())
        {
            obstacle.carving = false;
            obstacle.enabled = false;
        }
    }
}