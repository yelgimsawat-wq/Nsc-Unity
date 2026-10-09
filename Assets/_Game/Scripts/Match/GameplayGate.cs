using UnityEngine;

namespace Nsc.Match
{
    /// <summary>
    /// คำตอบเดียวของคำถาม "ตอนนี้ผู้เล่นทำอะไรได้ / ทำดาเมจได้หรือยัง"
    /// แทนแฟล็ก 4 ตัวเดิม (GameFlowManager.GameEnded, LobbyManager.GameStarted,
    /// PvpTeamManager.IsFighting, ParkourFlowManager.netEnded)
    ///
    /// ผู้เขียนมีคนเดียวคือ MatchSession — ทุกคนอ่านได้ (แขนขา ไอเทม HUD DamageRouter)
    /// ซีนที่ไม่มี MatchSession (ซีนเทส) = เปิดตลอด เหมือนพฤติกรรมเดิมที่ไม่มีตัวจัดการโหมด
    /// </summary>
    public static class GameplayGate
    {
        /// <summary>ผู้เล่นบังคับหุ่น/ใช้ไอเทม/ล็อกเมาส์เข้าเกมได้ไหม</summary>
        public static bool CanAct { get; private set; } = true;

        /// <summary>การชน/การยิงนับดาเมจไหม</summary>
        public static bool CanDamage { get; private set; } = true;

        /// <summary>เรียกจาก MatchSession เท่านั้น</summary>
        internal static void Set(bool canAct, bool canDamage)
        {
            CanAct = canAct;
            CanDamage = canDamage;
        }

        /// <summary>คืนสภาพเปิด — MatchSession เรียกตอนถูกทำลาย จะได้ไม่ค้างข้ามซีน</summary>
        internal static void Open() => Set(true, true);

        // static ค้างข้าม Play Mode ได้ถ้าปิด Domain Reload — ล้างทุกครั้งที่เริ่มเกม
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad() => Open();
    }
}
