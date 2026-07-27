using NscGame.Pvp;
using UnityEngine;

namespace NscUnity.Items
{
    /// <summary>
    /// ปืนกระสุนพาร์ติเคิลแบบชาร์จพลัง — กดค้างเพื่อชาร์จ ยิ่งชาร์จนานลูกยิ่งโตและแรงขึ้น
    /// ปล่อยเมาส์เพื่อยิง กระสุนพุ่งจริงมีระยะเวลาเดินทาง (ผ่าน Projectile) ไม่ใช่ Hitscan
    ///
    /// ใช้เอฟเฟกต์ 2 ตัวแยกกัน:
    ///   - Charge Effect: เล่นที่ปากกระบอกตั้งแต่กดค้าง โตขึ้นตามเวลาชาร์จ ทำลายทิ้งตอนจบ (ยิงหรือยกเลิกก็ตาม)
    ///   - Shot Effect: สร้างขึ้นใหม่ตอนปล่อยมือ ติดไปกับกระสุนที่พุ่งออกไปจริง
    /// ส่วนการชนกับศัตรู/ดาเมจเป็นโค้ดของเราคุมเอง (แม่นยำกว่าใช้ Particle Collision ของระบบพาร์ติเคิลเอง)
    ///
    /// ทิศยิง = ทิศที่ Muzzle ชี้อยู่จริง (Muzzle.forward) เชื่อว่าระบบแขน/ปืนหมุนไปเล็งถูกทางอยู่แล้ว
    /// ไม่คำนวณจุดเล็งซ้ำเองจากกล้อง/เมาส์ — ปืนหันไปทางไหน ยิงไปทางนั้น
    ///
    /// รูปแบบการชาร์จล้อมาจาก PunchChargeController ที่มีอยู่แล้วในโปรเจกต์
    /// </summary>
    public class ChargeGunHeldItem : HeldItem
    {
        [Header("ปากกระบอกปืน")]
        [Tooltip("Empty GameObject ที่ปลายกระบอก แกน Z ต้องชี้ออกไปทางที่ยิง ถ้าเว้นว่างจะใช้ตัวปืนเอง")]
        [SerializeField] private Transform muzzle;

        [Tooltip("มุมแก้ทิศยิง ถ้ากระสุนพุ่งผิดทาง (เช่นยิงใส่ตัวเอง) — ลองใส่ Y = 180 ก่อน แล้วค่อยลอง X/Z\n" +
                 "เลือกปืนใน Scene จะเห็นเส้นสีแดงบอกทิศยิงจริง ปรับจนเส้นชี้ออกไปข้างหน้าถูกทาง")]
        [SerializeField] private Vector3 fireDirectionOffset;

        [Header("ภาพเอฟเฟกต์")]
        [Tooltip("เล่นที่ปากกระบอกตั้งแต่เริ่มชาร์จ โตขึ้นตามเวลาที่กดค้าง ทำลายทิ้งเองตอนจบ (ยิงหรือยกเลิกก็ตาม)")]
        [SerializeField] private GameObject chargeEffectPrefab;
        [Tooltip("มุมแก้ของ Charge Effect ถ้าตัวเอฟเฟกต์เอียงผิดด้าน (ด้านหน้าของมันไม่ได้ชี้แกน Z)")]
        [SerializeField] private Vector3 chargeEffectRotationOffset;
        [Tooltip("สร้างขึ้นใหม่ตอนปล่อยมือยิงจริง ติดไปกับกระสุนที่พุ่งออกไป")]
        [SerializeField] private GameObject shotEffectPrefab;
        [Tooltip("มุมแก้ของ Shot Effect ถ้าตัวเอฟเฟกต์เอียงผิดด้านตอนบิน ลองปรับ Y ก่อน (เช่น 90, 180, -90) แล้วค่อยลอง X/Z")]
        [SerializeField] private Vector3 shotEffectRotationOffset;
        [SerializeField] private GameObject impactEffectPrefab;

        [Header("การชาร์จ")]
        [SerializeField] private float maxChargeTime = 1.2f;
        [Tooltip("ต้องชาร์จอย่างน้อยสัดส่วนเท่านี้ก่อนถึงจะยิงได้ (0 = คลิกสั้นๆ ก็ยิงได้)")]
        [SerializeField, Range(0f, 1f)] private float minChargeToFire = 0.15f;

        [Header("ขนาดลูกตามชาร์จ")]
        [SerializeField] private float minSize = 0.4f;
        [SerializeField] private float maxSize = 2.2f;
        [Tooltip("ตัวคูณขนาดโดยรวม แยกต่างหากจากการชาร์จ — ปรับตัวนี้เพื่อย่อ/ขยายเอฟเฟกต์ทั้งหมดโดยไม่กระทบสัดส่วนชาร์จ")]
        [SerializeField] private float sizeMultiplier = 1f;

        [Header("พลังตามชาร์จ")]
        [Tooltip("ชิ้นส่วนหุ่น PVP มีเลือด 500 — ตั้งไว้ให้ชาร์จเต็ม 2 นัดพอดีหักชิ้นส่วนที่เลือดเต็ม (260×2 = 520)")]
        [SerializeField] private float minDamage = 60f;
        [SerializeField] private float maxDamage = 260f;
        [SerializeField] private float minKnockback = 8f;
        [SerializeField] private float maxKnockback = 25f;

        [Header("การเดินทางของกระสุน")]
        [SerializeField] private float speed = 25f;
        [SerializeField] private float maxDistance = 60f;
        [Tooltip("รัศมีตรวจจับการชนของกระสุนขนาดเล็กสุด (ที่ minSize) ยิ่งชาร์จเต็มยิ่งขยายตามขนาดลูก")]
        [SerializeField] private float baseHitRadius = 0.15f;
        [SerializeField] private LayerMask hitLayers = ~0;
        [SerializeField] private float fireCooldown = 0.15f;

        private bool isCharging;
        private float chargeStartTime;
        private float nextFireTime;

        /// <summary>อินสแตนซ์ Charge Effect ที่กำลังเล่นอยู่ที่ปากกระบอกตอนนี้ (null = ไม่ได้ชาร์จอยู่)</summary>
        private GameObject activeCharge;

        private Transform Muzzle => muzzle != null ? muzzle : transform;

        /// <summary>ทิศที่กระสุนจะพุ่งไปจริง = ทิศของ Muzzle บวกมุมแก้ที่ตั้งไว้ใน Inspector</summary>
        private Vector3 FireDirection => Muzzle.rotation * Quaternion.Euler(fireDirectionOffset) * Vector3.forward;

        public override void OnUnequipped()
        {
            CancelCharge();
        }

        public override void OnUseStart()
        {
            if (Time.time < nextFireTime) return;

            isCharging = true;
            chargeStartTime = Time.time;

            if (chargeEffectPrefab != null)
            {
                activeCharge = Instantiate(chargeEffectPrefab, Muzzle);
                activeCharge.transform.localPosition = Vector3.zero;
                activeCharge.transform.localRotation = Quaternion.Euler(chargeEffectRotationOffset);
                activeCharge.transform.localScale = Vector3.one * ResolveSize(0f);
            }
        }

        public override void OnUseHold(float deltaTime)
        {
            if (!isCharging) return;

            float chargePercent = ResolveChargePercent();

            if (activeCharge != null)
            {
                activeCharge.transform.localScale = Vector3.one * ResolveSize(chargePercent);
            }

            // ชาร์จเต็ม 100% แล้ว ยิงออกไปทันทีโดยไม่ต้องรอให้ปล่อยมือ — มีขีดจำกัด ชาร์จค้างไปเรื่อยๆ ไม่ได้
            if (chargePercent >= 1f)
            {
                CompleteCharge(chargePercent);
            }
        }

        public override void OnUseEnd()
        {
            if (!isCharging) return;

            float chargePercent = ResolveChargePercent();
            if (chargePercent < minChargeToFire)
            {
                CancelCharge();
                return;
            }

            CompleteCharge(chargePercent);
        }

        /// <summary>
        /// จบการชาร์จ ไม่ว่าจะมาจากปล่อยมือเองหรือชาร์จเต็มแล้วยิงอัตโนมัติ
        /// คำนวณค่าทั้งหมดที่นี่ (ฝั่งเจ้าของเท่านั้น) แล้วส่งผ่าน HandItemHolder ให้ทุกเครื่องยิงพร้อมกัน
        /// (ดู HandItemHolder.RequestFire/FireRpc — ดาเมจจริงคิดเฉพาะฝั่ง Server ผ่าน Projectile.ApplyDamage)
        /// </summary>
        private void CompleteCharge(float chargePercent)
        {
            isCharging = false;
            ClearChargeEffect();

            Transform origin = Muzzle;
            float size = ResolveSize(chargePercent);

            // หาว่าเรายิงในนามหุ่นทีมไหน (ถ้าอยู่ในแมตช์ PVP) — ส่งไปกับกระสุนเพื่อกันยิงเพื่อนร่วมทีม (รวมถึงตัวเอง)
            // ต้องหาจาก Holder (HandItemHolder ที่อยู่บนแขนจริงในฉากตั้งแต่ต้น) ไม่ใช่จาก Muzzle
            // เพราะ Muzzle เป็นลูกของปืนที่เพิ่ง Instantiate ขึ้นมาใหม่ ลำดับชั้นอาจไต่ไปไม่ถึง root ที่ลงทะเบียนไว้
            // (ทำให้หาทีมไม่เจอ กลายเป็น None แล้วเช็คกันยิงตัวเอง/เพื่อนร่วมทีมพลาดไป)
            PvpRobotTeam shooterRobot = PvpRobotTeam.FindByPart(Holder.transform);

            if (shooterRobot == null && PvpTeamManager.Instance != null && PvpTeamManager.Instance.IsFighting)
            {
                Debug.LogWarning($"[ChargeGunHeldItem] หาทีมของ '{Holder.name}' ไม่เจอทั้งที่กำลังสู้กันอยู่ — " +
                                  "จะยิงโดยไม่มีทีม (shooterTeam = None) เสี่ยงยิงเพื่อนร่วมทีม/ตัวเองเข้าโดยไม่ตั้งใจ " +
                                  "เช็คว่า Holder อยู่ใต้ robotRoot ที่ PvpRobotTeam ลงทะเบียนไว้จริงไหม", Holder);
            }

            FireData data = new FireData
            {
                origin = origin.position,
                direction = FireDirection,
                speed = speed,
                damage = Mathf.Lerp(minDamage, maxDamage, chargePercent),
                knockback = Mathf.Lerp(minKnockback, maxKnockback, chargePercent),
                maxDistance = maxDistance,
                hitRadius = baseHitRadius * (size / Mathf.Max(0.01f, minSize * sizeMultiplier)),
                visualSize = size,
                shooterTeam = shooterRobot != null ? shooterRobot.Team : PvpTeam.None,
                shooterRobotId = shooterRobot != null ? shooterRobot.NetworkObjectId : 0
            };

            Holder.RequestFire(data);
            nextFireTime = Time.time + fireCooldown;
        }

        /// <summary>สัดส่วนชาร์จ 0-1 มีเพดานเสมอที่ Max Charge Time — ใช้ทั้งขนาด ดาเมจ น็อคแบ็ก และเงื่อนไขต่ำสุดที่จะยิงได้</summary>
        private float ResolveChargePercent()
        {
            float held = Time.time - chargeStartTime;
            return Mathf.Clamp01(held / Mathf.Max(0.01f, maxChargeTime));
        }

        /// <summary>ขนาดจริงที่ใช้แสดงผล = ช่วง Min/Max Size ตามสัดส่วนชาร์จ (มีเพดานที่ Max Size) คูณด้วย Size Multiplier โดยรวม</summary>
        private float ResolveSize(float chargePercent)
        {
            return Mathf.Lerp(minSize, maxSize, chargePercent) * sizeMultiplier;
        }

        /// <summary>ยกเลิกการชาร์จกลางคัน (สลับไอเทมก่อนปล่อยมือ) — ทำลาย Charge Effect ที่ค้างอยู่</summary>
        private void CancelCharge()
        {
            isCharging = false;
            ClearChargeEffect();
        }

        private void ClearChargeEffect()
        {
            if (activeCharge != null) Destroy(activeCharge);
            activeCharge = null;
        }

        /// <summary>
        /// ยิงจริง — เรียกจาก HandItemHolder.FireRpc บนทุกเครื่อง (รวมเครื่องเจ้าของเอง) พร้อมค่าที่เจ้าของคำนวณไว้แล้ว
        /// ดาเมจจริงจะถูกคิดเฉพาะฝั่ง Server เท่านั้น (Projectile เช็ค IsServer เองอยู่แล้วก่อนเรียก TakeDamage)
        /// </summary>
        public void ExecuteFire(FireData data)
        {
            GameObject projectileRoot = new GameObject("Projectile");
            projectileRoot.transform.SetPositionAndRotation(data.origin, Quaternion.LookRotation(data.direction));

            Projectile projectile = projectileRoot.AddComponent<Projectile>();
            projectile.Init(data.direction, data.speed, data.damage, data.knockback, data.maxDistance, data.hitRadius, hitLayers, impactEffectPrefab, data.shooterTeam, data.shooterRobotId);

            if (shotEffectPrefab != null)
            {
                GameObject visual = Instantiate(shotEffectPrefab, projectileRoot.transform);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.Euler(shotEffectRotationOffset);
                visual.transform.localScale = Vector3.one * data.visualSize;
            }
        }

        /// <summary>วาดเส้นบอกทิศยิงจริงใน Scene view — ใช้ปรับ Fire Direction Offset ให้ตรงโดยไม่ต้องเดา</summary>
        private void OnDrawGizmosSelected()
        {
            Transform origin = Muzzle;
            if (origin == null) return;

            Gizmos.color = Color.red;
            Gizmos.DrawRay(origin.position, FireDirection * 3f);
            Gizmos.DrawWireSphere(origin.position, 0.05f);
        }
    }
}
