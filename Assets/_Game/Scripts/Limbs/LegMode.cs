namespace Nsc.Limbs
{
    /// <summary>
    /// ขาทำอะไรอยู่ใน physics tick นี้ — ค่าเดียวต่อ tick แทนแฟล็ก bool 4 ตัวที่ต้องอ่านรวมกัน
    /// LegController.ResolveMode() ตัดสินตามลำดับความสำคัญ: หลุด → ล้ม → เตะ → ก้าว → ปัก
    /// </summary>
    public enum LegMode : byte
    {
        /// <summary>เท้าปักพื้นแข็ง (kinematic) รั้งลำตัวไว้</summary>
        Planted,
        /// <summary>ยกเท้าก้าว / กระโดด / ปล่อยจุดปักตอนปีน</summary>
        Stepping,
        /// <summary>ท่าเตะเป็นเจ้าของเท้า (ง้าง เตะ ฟื้นท่า)</summary>
        Kicking,
        /// <summary>ลำตัวล้ม — ขาขยับเบาๆ ตามเมาส์</summary>
        Limp,
        /// <summary>ลำตัวล้มและเจ้าของกด Q ยันพื้นลุก</summary>
        PushingUp,
        /// <summary>ขาหลุดจากตัว</summary>
        Detached
    }
}
