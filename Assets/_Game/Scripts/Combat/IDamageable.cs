namespace Nsc.Combat
{
    /// <summary>
    /// อะไรก็ตามที่รับดาเมจได้: เลือดของแขนขา, ลำตัวหุ่น (ส่งต่อให้แขนขา), บอส, ตึก, กระสอบทราย
    /// ห้ามเรียก ServerApplyDamage ตรงๆ จากผู้โจมตี — ผ่าน DamageRouter.TryApply เสมอ
    /// จะได้มีกติกาทีมและเฟสของแมตช์อยู่ที่เดียว
    /// </summary>
    public interface IDamageable
    {
        Team GetTeam();

        /// <summary>[SERVER] คืน true ถ้าดาเมจถูกรับจริง (ชิ้นที่หลุดแล้ว/ตายแล้วคืน false)</summary>
        bool ServerApplyDamage(DamageInfo info);
    }
}
