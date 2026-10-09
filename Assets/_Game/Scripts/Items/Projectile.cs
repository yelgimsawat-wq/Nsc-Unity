using Nsc.Combat;
using UnityEngine;

namespace NscUnity.Items
{
    /// <summary>
    /// กระสุนที่พุ่งจริง มีระยะเวลาเดินทาง (ไม่ใช่ hitscan) — ถูกสร้างตอนรันไทม์โดยไอเทมที่ยิงกระสุน
    /// ทุกเครื่องจำลองเหมือนกันเพื่อภาพ ส่วนดาเมจผ่าน DamageRouter (server เท่านั้น กติกาทีมอยู่ที่นั่น)
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
        private Team shooterTeam;

        private float travelled;

        public void Init(Vector3 direction, float speed, float damage, float knockback, float maxDistance,
                         float hitRadius, LayerMask hitLayers, GameObject impactEffectPrefab, Team shooterTeam = Team.None)
        {
            this.direction = direction.normalized;
            this.speed = speed;
            this.damage = damage;
            this.knockback = knockback;
            this.maxDistance = maxDistance;
            this.hitRadius = hitRadius;
            this.hitLayers = hitLayers;
            this.impactEffectPrefab = impactEffectPrefab;
            this.shooterTeam = shooterTeam;

            transform.rotation = Quaternion.LookRotation(this.direction);
        }

        private void Update()
        {
            float step = speed * Time.deltaTime;

            // SphereCast แทน Raycast เส้นเดียว — กันกระสุนเร็วทะลุของบางๆ
            if (Physics.SphereCast(transform.position, hitRadius, direction, out RaycastHit hit, step, hitLayers,
                                   QueryTriggerInteraction.Ignore))
            {
                HandleHit(hit);
                return;
            }

            transform.position += direction * step;
            travelled += step;
            if (travelled >= maxDistance) Destroy(gameObject);
        }

        private void HandleHit(RaycastHit hit)
        {
            var info = new DamageInfo(damage, hit.point, direction, DamageSource.Projectile, shooterTeam)
            {
                knockback = knockback
            };
            DamageRouter.TryApply(hit.collider, info);

            if (impactEffectPrefab != null)
                Instantiate(impactEffectPrefab, hit.point, Quaternion.LookRotation(hit.normal));

            Destroy(gameObject);
        }
    }
}
