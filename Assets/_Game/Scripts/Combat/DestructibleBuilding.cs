using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Nsc.Combat
{
    /// <summary>
    /// ตึกที่พังได้ (เดิมชื่อ explobuilding, GUID เดิม) — เลือดหมดแล้วสั่นจมลงพื้นแล้วหายไป
    ///
    /// รับดาเมจสองทาง (ทั้งคู่ผ่าน DamageRouter):
    ///   • ท่าโจมตีจริง (หมัด/เตะ/ดาบ/หลุมดำ) — ผู้โจมตีส่งมาเอง
    ///   • ชิ้นส่วนหุ่นที่ "ชนเฉยๆ" แรงพอ (เดินชน, ล้มทับ) — ตึกคิดจากแรงปะทะจริงเอง (F = impulse/Δt)
    ///     เพราะเมืองพังได้จากตัวหุ่นยักษ์ ไม่ใช่แค่จากหมัด
    /// </summary>
    public class DestructibleBuilding : NetworkBehaviour, IDamageable
    {
        [Header("Building Health Settings")]
        [SerializeField] private float maxHealth = 100f;

        [Header("VFX & SFX")]
        [SerializeField] private GameObject explosionVfxPrefab;
        [SerializeField] private AudioClip explosionSfx;

        [Header("Hit Effect")]
        [SerializeField] private GameObject hitVfxPrefab;

        [Header("Collapse Settings")]
        [SerializeField] private float shakeDuration = 1.5f;
        [SerializeField] private float shakeStrength = 0.2f;
        [SerializeField] private float sinkDistance = 5f;

        private readonly NetworkVariable<float> health = new NetworkVariable<float>(
            100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private AudioSource audioSource;
        private float offlineHealth;
        private bool isDestroyed;

        public float Health => IsSpawned ? health.Value : offlineHealth;

        private void Awake()
        {
            offlineHealth = maxHealth;
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
            if (IsServer) health.Value = maxHealth;
        }

        public Team GetTeam() => Team.None;

        public bool ServerApplyDamage(DamageInfo info)
        {
            if (isDestroyed || info.amount <= 0f) return false;

            float remaining = Mathf.Max(0f, Health - info.amount);
            if (IsSpawned) health.Value = remaining;
            else offlineHealth = remaining;

            Debug.Log($"Building HP : {remaining}/{maxHealth}");

            if (remaining <= 0f)
            {
                isDestroyed = true;
                TriggerDestruction();
            }
            return true;
        }

        /// <summary>ชิ้นส่วนหุ่นชนตึกโดยไม่ได้อยู่ในท่าโจมตี — คิดดาเมจจากแรงปะทะจริง</summary>
        private void OnCollisionEnter(Collision collision)
        {
            if (isDestroyed) return;

            LimbStrike strike = collision.collider.GetComponentInParent<LimbStrike>();
            // ท่าโจมตีที่กำลังทำดาเมจได้ LimbStrike ส่งมาทาง DamageRouter อยู่แล้ว — ห้ามนับซ้ำ
            if (strike == null || strike.IsStriking) return;

            float damage = strike.ImpactDamage(collision);
            if (damage <= 0f) return;

            ContactPoint contact = collision.contactCount > 0 ? collision.GetContact(0) : default;
            var info = new DamageInfo(damage, contact.point, -contact.normal, DamageSource.Punch, Team.None);
            if (!DamageRouter.TryApply(this, info)) return;

            if (hitVfxPrefab != null && collision.contactCount > 0)
                Instantiate(hitVfxPrefab, contact.point, Quaternion.LookRotation(contact.normal));
        }

        // ================================================================
        //  พัง
        // ================================================================

        private void TriggerDestruction()
        {
            if (NetworkObject != null && NetworkObject.IsSpawned)
            {
                PlayCollapseRpc();
                StartCoroutine(ServerDespawnSequence());
            }
            else
            {
                StartCoroutine(OfflineDestructionSequence());
            }
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        private void PlayCollapseRpc()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            StartCoroutine(CollapseSequence());
        }

        private IEnumerator ServerDespawnSequence()
        {
            DisableColliders();
            yield return new WaitForSeconds(shakeDuration + 0.2f);
            if (NetworkObject != null && NetworkObject.IsSpawned) NetworkObject.Despawn(true);
        }

        private IEnumerator OfflineDestructionSequence()
        {
            yield return StartCoroutine(CollapseSequence());
            Destroy(gameObject);
        }

        private IEnumerator CollapseSequence()
        {
            DisableColliders();

            if (explosionVfxPrefab != null)
                Instantiate(explosionVfxPrefab, transform.position, Quaternion.identity);
            if (explosionSfx != null && audioSource != null)
                audioSource.PlayOneShot(explosionSfx);

            Vector3 startPos = transform.position;
            Vector3 endPos = startPos + Vector3.down * sinkDistance;
            for (float timer = 0f; timer < shakeDuration; timer += Time.deltaTime)
            {
                float t = timer / shakeDuration;
                Vector3 shake = Random.insideUnitSphere * shakeStrength * (1f - t);
                shake.y *= 0.2f;
                transform.position = Vector3.Lerp(startPos, endPos, t) + shake;
                yield return null;
            }
            transform.position = endPos;
        }

        private void DisableColliders()
        {
            foreach (Collider col in GetComponentsInChildren<Collider>())
                col.enabled = false;

            // ✅ [NavMesh] ตึกเจาะรูบน NavMesh ด้วย NavMeshObstacle (carving) — ปิดพร้อม collider ทันที
            // ไม่งั้นรูจมตามตัวตึกแล้ว re-carve ทุกเฟรม พื้นตรงนั้นยังเดินไม่ได้ทั้งที่ตึกพังแล้ว
            foreach (NavMeshObstacle obstacle in GetComponentsInChildren<NavMeshObstacle>())
            {
                obstacle.carving = false;
                obstacle.enabled = false;
            }
        }
    }
}
