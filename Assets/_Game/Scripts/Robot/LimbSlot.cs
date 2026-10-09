namespace Nsc.Robots
{
    /// <summary>
    /// แขนขาชิ้นไหนในสี่ชิ้น — ด้านซ้าย/ขวาเป็นของ "ตัวหุ่น" (ด้านซ้ายของหุ่นเอง)
    /// ไม่ใช่ด้านที่เห็นบนจอลอบบี้ (ลอบบี้หันหน้าเข้าหาผู้เล่น จึงกลับด้านที่ LimbSelectionUi ที่เดียว)
    ///
    /// ⚠️ ลำดับตัวเลขต้องคงเดิม (0-3) — ตรงกับ index ชิ้นส่วนเดิมของ PvpLimb
    /// </summary>
    public enum LimbSlot : byte
    {
        LeftArm  = 0,
        RightArm = 1,
        LeftLeg  = 2,
        RightLeg = 3
    }

    public static class LimbSlots
    {
        public const int Count = 4;

        public static readonly LimbSlot[] All = { LimbSlot.LeftArm, LimbSlot.RightArm, LimbSlot.LeftLeg, LimbSlot.RightLeg };

        public static bool IsArm(this LimbSlot slot) => slot == LimbSlot.LeftArm || slot == LimbSlot.RightArm;
        public static bool IsLeg(this LimbSlot slot) => !slot.IsArm();
        public static bool IsValid(int index) => index >= 0 && index < Count;

        public static string DisplayName(this LimbSlot slot) => slot switch
        {
            LimbSlot.LeftArm  => "Left Arm",
            LimbSlot.RightArm => "Right Arm",
            LimbSlot.LeftLeg  => "Left Leg",
            _                 => "Right Leg"
        };

        /// <summary>ชื่อย่อบน HUD</summary>
        public static string ShortLabel(this LimbSlot slot) => slot switch
        {
            LimbSlot.LeftArm  => "L-ARM",
            LimbSlot.RightArm => "R-ARM",
            LimbSlot.LeftLeg  => "L-LEG",
            _                 => "R-LEG"
        };
    }
}
