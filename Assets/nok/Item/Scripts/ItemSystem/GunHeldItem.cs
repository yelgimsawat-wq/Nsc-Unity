using Nsc.Combat;
using Nsc.Robots;
using UnityEngine;

namespace NscUnity.Items
{
    /// <summary>
    /// ปืนยิงทันที (hitscan) — แปะไว้บน heldPrefab ของ ItemDefinition ที่เป็นปืน
    ///
    /// ยิงฝั่ง server: เจ้าของกดปุ่ม → server ยิง Raycast ตามทิศที่ปากกระบอกชี้อยู่จริง
    /// (ปืนหันไปทางไหน ยิงไปทางนั้น — แบบเดียวกับ ChargeGunHeldItem) ดาเมจผ่าน DamageRouter
    /// แล้วประกาศนัดให้ทุกเครื่องเล่นแสงปากกระบอก/เสียง/รอยกระสุนตรงกัน
    /// (เดิมคิดดาเมจในเครื่องคนยิง ได้ผลเฉพาะตอนคนยิงเป็น Host)
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

        private float nextRequestTime;

        private Transform Muzzle => muzzle != null ? muzzle : transform;
        private float FireInterval => 1f / Mathf.Max(0.01f, fireRate);

        public override void OnUseStart() => RequestShot();

        public override void OnUseHold(float deltaTime)
        {
            if (automatic) RequestShot();
        }

        private void RequestShot()
        {
            if (Time.time < nextRequestTime || Holder == null) return;
            nextRequestTime = Time.time + FireInterval;
            Holder.RequestUseStart();
            Holder.RequestUseRelease(); // ปืนไม่มีการชาร์จ — ปิดรอบการใช้งานทันที server จะได้รับนัดถัดไป
        }

        /// <summary>[SERVER] ยิงหนึ่งนัด — cooldown ตามอัตรายิงถูกบังคับที่ HandItemHolder</summary>
        public override void ServerOnUseStart()
        {
            Transform origin = Muzzle;
            Vector3 direction = origin.forward;
            Robot shooter = Robot.FromTransform(Holder.transform);
            Team team = shooter != null ? shooter.GetTeam() : Team.None;

            float distance = range;
            if (Physics.Raycast(origin.position, direction, out RaycastHit hit, range, hitLayers, QueryTriggerInteraction.Ignore))
            {
                distance = hit.distance;
                var info = new DamageInfo(damage, hit.point, direction, DamageSource.Projectile, team) { knockback = knockback };
                DamageRouter.TryApply(hit.collider, info);
            }

            Holder.ServerBroadcastShot(new FireData
            {
                origin = origin.position,
                direction = direction,
                damage = damage,
                maxDistance = distance,
                shooterTeam = team
            }, FireInterval);
        }

        /// <summary>[ทุกเครื่อง] แสงปากกระบอก เสียง และรอยกระสุนที่ปลายทาง</summary>
        public override void OnShot(FireData data)
        {
            if (muzzleFlash != null) muzzleFlash.Play();
            if (fireSound != null) fireSound.Play();

            if (impactEffect != null && data.maxDistance < range &&
                Physics.Raycast(data.origin, data.direction, out RaycastHit hit, data.maxDistance + 0.1f, hitLayers,
                                QueryTriggerInteraction.Ignore))
                Instantiate(impactEffect, hit.point, Quaternion.LookRotation(hit.normal));

            Debug.DrawRay(data.origin, data.direction * data.maxDistance, Color.red, 0.4f);
        }
    }
}
