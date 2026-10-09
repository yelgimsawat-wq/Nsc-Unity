namespace Nsc.Robots
{
    /// <summary>
    /// สถานะแขนขาที่กฎการล้มต้องรู้ ณ physics tick หนึ่ง — TorsoBalance อ่านจาก Robot แล้วส่งให้ FallRules
    /// เป็นค่าล้วน (ไม่มี reference ไปหา component) กฎการล้มจึงเทสใน EditMode ได้โดยไม่ต้องเปิดฉาก
    /// </summary>
    public struct BalanceInput
    {
        /// <summary>จำนวนขาที่นับในกฎ (ขาที่หลุดยังนับ ถ้า FallRules.countDetachedLegs = true เหมือนเดิม)</summary>
        public int footCount;
        /// <summary>เท้าที่แตะพื้นจริง</summary>
        public int groundedFeet;
        /// <summary>เท้าที่ "ก้าวเดิน" จริง (ไม่นับกระโดด/เตะ/ยันตัวลุก)</summary>
        public int walkSteppingFeet;
        /// <summary>เท้าที่ยังสมดุล (แตะพื้น ไม่ได้กระโดด/ยันตัว/ปล่อยตอนปีน)</summary>
        public int balancedFeet;
        /// <summary>มีเท้าที่ยันพื้นอยู่และไม่ได้ก้าว/กระโดด/ลุก — "มีขายัน = ห้ามล้มจากระบบสมดุล"</summary>
        public bool supportFoot;
        /// <summary>มือที่จับของนิ่ง (kinematic) อยู่ — กันการล้มได้ทุกกฎรวม stress</summary>
        public bool handSupport;
        /// <summary>อยู่ในช่วงกระโดด/เพิ่งลงพื้น — ลอยเพราะกระโดด ไม่ใช่กำลังล้ม</summary>
        public bool jumpProtected;
        /// <summary>ขาข้างใดข้างหนึ่งอยู่ในจังหวะเตะจริง (ไม่รวมตอนง้าง)</summary>
        public bool kickInProgress;
        public float stress;
        /// <summary>มุมเอียงของลำตัวจากแนวตั้ง (องศา)</summary>
        public float tiltAngle;
    }
}
