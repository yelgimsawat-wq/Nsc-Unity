using Nsc.Combat;
using Unity.Netcode;

namespace Nsc.Match
{
    /// <summary>เฟสของแมตช์ในฉากหนึ่ง — MatchSession เป็นเจ้าของคนเดียว</summary>
    public enum MatchPhase : byte
    {
        /// <summary>ลอบบี้/เลือกทีม — ยังบังคับหุ่นและทำดาเมจไม่ได้</summary>
        Preparing,
        Playing,
        /// <summary>มีผลแพ้ชนะแล้ว — หน้าผลลัพธ์ขึ้น ปลดเมาส์</summary>
        Ended
    }

    /// <summary>ผลของแมตช์ — sync ให้ทุกเครื่องผ่าน MatchSession</summary>
    public struct MatchResult : INetworkSerializeByMemcpy
    {
        /// <summary>co-op: ผู้เล่นชนะไหม / PVP: true เสมอเมื่อมีทีมชนะ (หน้าจอคิดชนะ/แพ้จากทีมของเครื่องตัวเอง)</summary>
        public bool victory;
        public Team winningTeam;
        /// <summary>เวลาที่ใช้ตั้งแต่เริ่มเล่น (วินาที)</summary>
        public float time;
        /// <summary>ความสูงสูงสุดที่ไต่ได้ (พาร์คัวร์)</summary>
        public float height;
    }

    /// <summary>ชื่อฉากที่ใช้ร่วมกัน — เดิมชื่อฉากเมนูถูกนิยาม 4 แบบใน 4 ไฟล์</summary>
    public static class GameScenes
    {
        public const string Menu = "-MenuNOk";
    }
}
