namespace Nsc.Combat
{
    /// <summary>
    /// ดาเมจมาจากอะไร — แยกออกจาก AttackType ของบอส (ซึ่งเป็นรายชื่อท่า ไม่ใช่ที่มาของดาเมจ)
    /// ผู้รับใช้ค่านี้เลือกเอฟเฟกต์ได้ เช่นชิ้นส่วนหุ่นเล่น VFX เฉพาะตอนโดนหมัด/เตะ
    /// </summary>
    public enum DamageSource : byte
    {
        Punch,
        Kick,
        MeleeWeapon,
        Projectile,
        EnemyMelee,
        EnemyUltimate
    }
}
