using UnityEngine;
using Unity.Netcode;
using NscGame.Enemy;
using NscGame.Pvp;

namespace NscUnity.Items
{
    /// <summary>
    /// กระสุนที่พุ่งจริง มีระยะเวลาเดินทาง (ไม่ใช่ยิงติดทันทีแบบ Hitscan)
    /// ถูกสร้างขึ้นตอนรันไทม์โดยไอเทมที่ยิงกระสุน (เช่น ChargeGunHeldItem) — ไม่ต้องเตรียม Prefab ของตัวนี้เอง
    ///
    /// ส่งดาเมจ 3 ทาง เรียงลำดับ: PvpRobotTeam (ยิงผู้เล่นในโหมด PVP) → EnemyHealth (บอส AI) → IHittable (ทั่วไป)
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
        private PvpTeam shooterTeam;
        private ulong shooterRobotId;

        private float travelled;

        public void Init(Vector3 direction, float speed, float damage, float knockback, float maxDistance, float hitRadius, LayerMask hitLayers, GameObject impactEffectPrefab, PvpTeam shooterTeam = PvpTeam.None, ulong shooterRobotId = 0)
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
            this.shooterRobotId = shooterRobotId;

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

        /// <summary>ส่งดาเมจผ่านอินเทอร์เฟซที่โปรเจกต์นี้มีอยู่แล้ว — เหมือน GunHeldItem.ApplyDamage() แต่เพิ่มเส้นทาง PVP</summary>
        private void ApplyDamage(RaycastHit hit)
        {
            bool isServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
            if (!isServer) return;

            // 1. ยิงหุ่นผู้เล่นในโหมด PVP — ต้องอยู่ในช่วงกำลังสู้กัน (Fighting)
            //    ยิงโดนตัวเอง (หุ่นตัวเดียวกับที่ยิง) อนุญาต — ยิงโดนเพื่อนร่วมทีมคนอื่น (ทีมเดียวกันแต่คนละตัว) กันไว้
            PvpRobotTeam targetRobot = PvpRobotTeam.FindByPart(hit.collider.transform);
            if (targetRobot != null)
            {
                bool isFighting = PvpTeamManager.Instance != null && PvpTeamManager.Instance.IsFighting;
                bool isSelf = shooterRobotId != 0 && targetRobot.NetworkObjectId == shooterRobotId;
                bool isTeammate = !isSelf && shooterTeam != PvpTeam.None && targetRobot.Team == shooterTeam;

                if (isFighting && !isTeammate)
                {
                    targetRobot.ServerApplyBodyDamage(damage, AttackType.LightPunch, direction, hit.point);
                }
                return; // เป็นหุ่น PVP แล้ว ไม่ต้องตกไปเช็ค EnemyHealth/IHittable ต่อ
            }

            // 2. บอส AI (PvE)
            EnemyHealth enemyHealth = hit.collider.GetComponentInParent<EnemyHealth>();
            if (enemyHealth != null)
            {
                enemyHealth.ServerTakeHit(damage, direction, knockback, hit.point);
                return;
            }

            // 3. อะไรก็ตามที่รับดาเมจได้แบบทั่วไป
            IHittable hittable = hit.collider.GetComponentInParent<IHittable>();
            hittable?.ServerTakeDamage(damage, AttackType.LightPunch, direction);
        }
    }
}
