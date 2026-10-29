using Nsc.Combat;
using Nsc.Feel;
using Nsc.Robots;
using UnityEngine;

namespace Nsc.BoneLimb
{
    /// <summary>
    /// ของที่โดนเตะ/โดนกระทืบลอย (Server ใส่ให้ตอนกระเด็น — RobotIKControl): ชนสิ่งที่รับดาเมจได้ครั้งแรก = ดาเมจ + เอฟเฟกต์ แล้วหมดฤทธิ์
    /// ชนพื้น/กำแพงไม่หมดฤทธิ์ (กระดอนไปโดนต่อได้) | ช้าลงจนเกือบหยุด หรือเกินเวลา = หมดฤทธิ์ | ชนหุ่นที่เตะเองไม่นับ
    /// </summary>
    public class KickedProp : MonoBehaviour
    {
        private float damage, until;
        private Team team;
        private Robot owner;
        private Rigidbody body;

        /// <summary>[SERVER] ใส่ (หรือต่อเวลา) ให้ของที่เพิ่งโดนเตะ</summary>
        public static void Launch(Rigidbody rb, Vector3 velocity, float damage, Team team, Robot owner, float life = 3f)
        {
            rb.linearVelocity = velocity;
            KickedProp p = rb.GetComponent<KickedProp>();
            if (!p) p = rb.gameObject.AddComponent<KickedProp>();
            p.body = rb;
            p.damage = damage;
            p.team = team;
            p.owner = owner;
            p.until = Time.time + life;
        }

        private void FixedUpdate()
        {
            // เกือบหยุด = ตกพื้นนิ่งแล้ว (เผื่อ 0.3 วิแรก ของยังไม่ทันพุ่ง)
            if (Time.time > until || (Time.time > until - 2.7f && body.linearVelocity.sqrMagnitude < 4f)) Destroy(this);
        }

        private void OnCollisionEnter(Collision collision)
        {
            Collider hit = collision.collider;
            if (damage <= 0f || (owner && Robot.FromCollider(hit) == owner)) return; // หมดฤทธิ์แล้ว (Destroy รอจบเฟรม — ชนสองชิ้นพร้อมกันไม่นับซ้ำ)
            Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : hit.ClosestPoint(transform.position);
            Vector3 dir = -collision.relativeVelocity;
            var info = new DamageInfo(damage, point, dir.sqrMagnitude > 1e-4f ? dir.normalized : transform.forward, DamageSource.Projectile, team)
            {
                knockback = Mathf.Clamp(collision.relativeVelocity.magnitude * 0.3f, 2f, 12f),
            };
            if (!DamageRouter.TryApply(hit, info)) return;
            ImpactFeel.PlayHit(point, -info.direction, damage);
            damage = 0f;
            Destroy(this);
        }
    }
}
