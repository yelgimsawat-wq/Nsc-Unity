using UnityEngine;
using Unity.Netcode;
using NscGame.Enemy;

namespace NscUnity.Items
{
    /// <summary>
    /// กระสุนที่พุ่งจริง มีระยะเวลาเดินทาง (ไม่ใช่ยิงติดทันทีแบบ Hitscan)
    /// ถูกสร้างขึ้นตอนรันไทม์โดยไอเทมที่ยิงกระสุน (เช่น ChargeGunHeldItem) — ไม่ต้องเตรียม Prefab ของตัวนี้เอง
    ///
    /// ส่งดาเมจผ่านอินเทอร์เฟซเดียวกับ GunHeldItem: EnemyHealth ก่อน แล้วค่อย IHittable
    /// ⚠️ Multiplayer: คำนวณดาเมจแบบ local เหมือน GunHeldItem — ได้ผลจริงเฉพาะเครื่องที่เป็น Host/Server
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        private Vector3 direction;
        private float speed;
        private float damage;
        private float knockback;
        private float maxDistance;
        private float hitRadius;
        private LayerMask hitLayers;
        private GameObject impactEffectPrefab;

        private float travelled;

        public void Init(Vector3 direction, float speed, float damage, float knockback, float maxDistance, float hitRadius, LayerMask hitLayers, GameObject impactEffectPrefab)
        {
            this.direction = direction.normalized;
            this.speed = speed;
            this.damage = damage;
            this.knockback = knockback;
            this.maxDistance = maxDistance;
            this.hitRadius = hitRadius;
            this.hitLayers = hitLayers;
            this.impactEffectPrefab = impactEffectPrefab;

            transform.rotation = Quaternion.LookRotation(this.direction);
        }

        private void Update()
        {
            float step = speed * Time.deltaTime;

            // ใช้ SphereCast แทน Raycast เส้นเดียว เพื่อกันกระสุนความเร็วสูงทะลุของบางๆ ไปโดยไม่โดนตรวจจับ
            if (Physics.SphereCast(transform.position, hitRadius, direction, out RaycastHit hit, step, hitLayers, QueryTriggerInteraction.Ignore))
            {
                HandleHit(hit);
                return;
            }

            transform.position += direction * step;
            travelled += step;

            if (travelled >= maxDistance)
            {
                Destroy(gameObject);
            }
        }

        private void HandleHit(RaycastHit hit)
        {
            ApplyDamage(hit);

            if (impactEffectPrefab != null)
            {
                Instantiate(impactEffectPrefab, hit.point, Quaternion.LookRotation(hit.normal));
            }

            Destroy(gameObject);
        }

        /// <summary>ส่งดาเมจผ่านอินเทอร์เฟซที่โปรเจกต์นี้มีอยู่แล้ว — เหมือน GunHeldItem.ApplyDamage() ทุกประการ</summary>
        private void ApplyDamage(RaycastHit hit)
        {
            bool isServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
            if (!isServer) return;

            EnemyHealth enemyHealth = hit.collider.GetComponentInParent<EnemyHealth>();
            if (enemyHealth != null)
            {
                enemyHealth.ServerTakeHit(damage, direction, knockback, hit.point);
                return;
            }

            IHittable hittable = hit.collider.GetComponentInParent<IHittable>();
            hittable?.ServerTakeDamage(damage, AttackType.LightPunch, direction);
        }
    }
}
