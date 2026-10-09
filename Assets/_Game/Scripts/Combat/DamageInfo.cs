using UnityEngine;

namespace Nsc.Combat
{
    /// <summary>
    /// ดาเมจหนึ่งครั้ง — ผู้โจมตีทุกแบบ (หมัด เตะ ดาบ กระสุน บอส หลุมดำ) สร้างก้อนนี้แล้วส่งให้ DamageRouter
    /// ผู้รับ (IDamageable) อ่านเฉพาะช่องที่ตัวเองใช้
    /// </summary>
    public struct DamageInfo
    {
        public float amount;
        /// <summary>แรงผลักพื้นฐาน — บอสใช้ตรงๆ / ชิ้นส่วนหุ่นคิดแรงกระเด็นจาก amount เหมือนเดิม</summary>
        public float knockback;
        /// <summary>จุดปะทะจริงในโลก (ใช้วาง VFX)</summary>
        public Vector3 point;
        /// <summary>ทิศที่ดาเมจพุ่งเข้าหาเป้า</summary>
        public Vector3 direction;
        public DamageSource source;
        public Team attackerTeam;
        /// <summary>true = ชิ้นที่โดนต้องหลุดทันทีไม่สนเลือดที่เหลือ (ท่าไม้ตายบอส)</summary>
        public bool detachLimb;

        public DamageInfo(float amount, Vector3 point, Vector3 direction, DamageSource source, Team attackerTeam)
        {
            this.amount = amount;
            this.point = point;
            this.direction = direction;
            this.source = source;
            this.attackerTeam = attackerTeam;
            knockback = 0f;
            detachLimb = false;
        }
    }
}
