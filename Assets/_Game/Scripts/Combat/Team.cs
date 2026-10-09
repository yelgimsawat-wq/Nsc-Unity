using UnityEngine;

namespace Nsc.Combat
{
    /// <summary>
    /// ฝ่ายของผู้โจมตีและผู้รับดาเมจ — DamageRouter ใช้ตัดสินว่า "ตีกันได้ไหม"
    /// None = ไม่มีทีม (หุ่น co-op, ตึก, กระสอบทราย) ตีได้และโดนได้จากทุกฝ่าย
    ///
    /// ⚠️ ค่าตัวเลขต้องคงเดิม: Red = 1 / Blue = 2 ตรงกับ PvpTeam เดิมที่ถูก serialize ไว้ในซีน PVP
    /// </summary>
    public enum Team : byte
    {
        None  = 0,
        Red   = 1,
        Blue  = 2,
        Enemy = 3
    }

    public static class TeamExtensions
    {
        public static Team Opposite(this Team team) => team switch
        {
            Team.Red  => Team.Blue,
            Team.Blue => Team.Red,
            _         => Team.None
        };

        public static string DisplayName(this Team team) => team switch
        {
            Team.Red   => "RED",
            Team.Blue  => "BLUE",
            Team.Enemy => "ENEMY",
            _          => "NONE"
        };

        public static Color DisplayColor(this Team team) => team switch
        {
            Team.Red  => new Color(0.90f, 0.22f, 0.24f, 1f),
            Team.Blue => new Color(0.20f, 0.55f, 0.95f, 1f),
            _         => new Color(0.6f, 0.6f, 0.6f, 1f)
        };

        /// <summary>สองฝ่ายนี้ทำดาเมจใส่กันได้ไหม — ฝ่ายเดียวกัน (ที่ไม่ใช่ None) ตีกันไม่เข้า</summary>
        public static bool IsHostileTo(this Team attacker, Team target) =>
            attacker == Team.None || target == Team.None || attacker != target;
    }
}
