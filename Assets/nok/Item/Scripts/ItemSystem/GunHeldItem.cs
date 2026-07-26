using UnityEngine;
using Unity.Netcode;
using NscGame.Enemy;

namespace NscUnity.Items
{
    /// <summary>
    /// ตัวอย่างไอเทมประเภทปืน — แปะไว้บน heldPrefab ของ ItemDefinition ที่เป็นปืน
    ///
    /// เล็งโดยอ่านจุดเล็งจาก PlayerHandMovement.AimNormalized (ระบบ virtual cursor ที่โปรเจกต์นี้ใช้อยู่แล้ว
    /// สำหรับบังคับมือ) แล้วยิง Raycast จากกล้องผ่านจุดนั้นเข้าไปในโลก
    ///
    /// ⚠️ Multiplayer: ตอนนี้ยิงและคิดดาเมจแบบ local (เรียก ServerTakeHit/ServerTakeDamage ตรงๆ)
    /// ซึ่งจะได้ผลจริงเฉพาะตอนเครื่องที่ยิงเป็น Host/Server เท่านั้น — เข้ากับตัวเลือก "เล่นคนเดียว/host ก่อน"
    /// ถ้าจะให้ client ยิงแล้วเห็นผลบนเครื่องอื่นด้วย ต้องห่อ TryFire() ด้วย [Rpc(SendTo.Server)] ทีหลัง
    /// </summary>
    public class GunHeldItem : HeldItem
    {
        [Header("ปากกระบอกปืน")]
        [Tooltip("Empty GameObject ที่ปลายกระบอก ถ้าเว้นว่างจะใช้ตัวปืนเอง")]
        [SerializeField] private Transform muzzle;

        [Header("ค่าพลัง")]
        [SerializeField] private float damage = 10f;
        [SerializeField] private float knockback = 4f;
        [SerializeField] private float range = 60f;
        [Tooltip("นัดต่อวินาที")]
        [SerializeField] private float fireRate = 6f;
        [SerializeField] private bool automatic = false;
        [SerializeField] private LayerMask hitLayers = ~0;

        [Header("เอฟเฟกต์")]
        [SerializeField] private ParticleSystem muzzleFlash;
        [SerializeField] private GameObject impactEffect;
        [SerializeField] private AudioSource fireSound;

        private PlayerHandMovement ownerHand;
        private Camera aimCamera;
        private float nextFireTime;

        private Transform Muzzle => muzzle != null ? muzzle : transform;

        public override void OnEquipped()
        {
            // หา PlayerHandMovement ของผู้เล่นที่ถือปืนนี้อยู่ เพื่ออ่านจุดเล็ง (virtual cursor) กับกล้อง
            ownerHand = GetComponentInParent<PlayerHandMovement>();
            if (ownerHand == null) ownerHand = FindFirstObjectByType<PlayerHandMovement>();

            aimCamera = ownerHand != null && ownerHand.playerCamera != null ? ownerHand.playerCamera : Camera.main;
        }

        public override void OnUseStart()
        {
            TryFire();
        }

        public override void OnUseHold(float deltaTime)
        {
            if (automatic) TryFire();
        }

        private void TryFire()
        {
            if (Time.time < nextFireTime) return;
            nextFireTime = Time.time + 1f / Mathf.Max(0.01f, fireRate);

            Transform origin = Muzzle;
            Vector3 direction = ResolveAimDirection(origin);

            if (muzzleFlash != null) muzzleFlash.Play();
            if (fireSound != null) fireSound.Play();

            if (Physics.Raycast(origin.position, direction, out RaycastHit hit, range, hitLayers, QueryTriggerInteraction.Ignore))
            {
                if (impactEffect != null)
                {
                    Instantiate(impactEffect, hit.point, Quaternion.LookRotation(hit.normal));
                }

                ApplyDamage(hit, direction);

                Debug.DrawLine(origin.position, hit.point, Color.red, 0.4f);
            }
            else
            {
                Debug.DrawRay(origin.position, direction * range, Color.yellow, 0.4f);
            }
        }

        /// <summary>ส่งดาเมจผ่านอินเทอร์เฟซที่โปรเจกต์นี้มีอยู่แล้ว — EnemyHealth ก่อน แล้วค่อย IHittable</summary>
        private void ApplyDamage(RaycastHit hit, Vector3 direction)
        {
            bool isServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
            if (!isServer) return; // ดาเมจต้องมาจาก Server เท่านั้น (server-authoritative เหมือนระบบต่อยเดิม)

            EnemyHealth enemyHealth = hit.collider.GetComponentInParent<EnemyHealth>();
            if (enemyHealth != null)
            {
                enemyHealth.ServerTakeHit(damage, direction, knockback, hit.point);
                return;
            }

            IHittable hittable = hit.collider.GetComponentInParent<IHittable>();
            hittable?.ServerTakeDamage(damage, AttackType.LightPunch, direction);
        }

        /// <summary>แปลงจุดเล็ง (virtual cursor แบบ normalized) เป็นทิศยิงในโลกจริง</summary>
        private Vector3 ResolveAimDirection(Transform origin)
        {
            if (aimCamera != null)
            {
                Vector2 normalized = PlayerHandMovement.AimNormalized; // [-1,1] ทั้งสองแกน
                Vector2 viewport = (normalized + Vector2.one) * 0.5f;
                Ray aimRay = aimCamera.ViewportPointToRay(new Vector3(viewport.x, viewport.y, 0f));

                Vector3 aimPoint = Physics.Raycast(aimRay, out RaycastHit aimHit, range, hitLayers, QueryTriggerInteraction.Ignore)
                    ? aimHit.point
                    : aimRay.GetPoint(range);

                Vector3 toTarget = aimPoint - origin.position;
                if (toTarget.sqrMagnitude > 0.01f) return toTarget.normalized;
            }

            return origin.forward;
        }
    }
}
