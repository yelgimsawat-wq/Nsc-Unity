namespace Nsc.Robots
{
    /// <summary>หุ่นล้มเพราะอะไร — log ทุกครั้งที่ล้มบอกเหตุผลได้ และเทสกฎแต่ละข้อแยกกันได้</summary>
    public enum FallReason : byte
    {
        None,
        /// <summary>แรงดึงสะสมเกินเพดาน (แขนดึงลำตัวแรงเกิน)</summary>
        StressOverload,
        /// <summary>กติกาหลักของเกม: สองขาก้าวพร้อมกันนานเกิน grace</summary>
        BothFeetStepping,
        /// <summary>ทั้งสองเท้าลอยและไม่มีเท้าไหนสมดุล</summary>
        BothFeetOffBalance,
        /// <summary>ไม่มีเท้าแตะพื้นเลยนานเกิน (ตกที่สูง)</summary>
        AirborneTooLong,
        /// <summary>ตัวเอียงเกินองศาขณะไม่มีอะไรพยุง</summary>
        TiltedInAir,
        /// <summary>ขาหลุดจากตัว</summary>
        LegDetached,
        /// <summary>ถูกสั่งล้มจากภายนอก (ท่าไม้ตายของบอส)</summary>
        KnockedDown
    }
}
