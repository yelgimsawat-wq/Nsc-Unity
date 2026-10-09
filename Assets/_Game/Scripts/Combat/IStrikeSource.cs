namespace Nsc.Combat
{
    /// <summary>
    /// ท่าที่ทำดาเมจด้วยการชนจริง (หมัด เตะ ดาบ) — LimbStrike อ่านค่าพวกนี้ตอนชิ้นส่วนชนอะไรเข้า
    /// ดาเมจคิดจาก "ความเร็วพีคของท่านี้" ไม่ใช่ impulse ตอนชน: ชนเฉียง/เป้าถอยหนี ดาเมจไม่หาย
    /// </summary>
    public interface IStrikeSource
    {
        /// <summary>ท่ากำลังทำดาเมจได้อยู่ไหม (กำลังพุ่ง และยังไม่เคยเข้าเป้าในรอบนี้)</summary>
        bool CanDealDamage();

        /// <summary>ความเร็วสูงสุดของท่ารอบนี้ (m/s)</summary>
        float PeakSpeed();

        DamageSource Source();

        /// <summary>
        /// LimbStrike แจ้งผลการชนกลับ — landed = เข้าเป้าที่รับดาเมจ (ล็อกหนึ่งท่าหนึ่งดาเมจ)
        /// / false = ชนของแข็งแรงพอจะจบท่า แต่ยังไม่ล็อกดาเมจ (ปัดไปโดนเป้าในช่วงผ่อนผันยังนับ)
        /// </summary>
        void ResolveStrike(bool landed);
    }
}
