using Nsc.Combat;
using Nsc.Robots;
using UnityEngine;

namespace Nsc.Match
{
    /// <summary>
    /// กติกาโหมด PVP (หุ่นแดง vs หุ่นน้ำเงิน) — เดิมกระจายอยู่ใน PvpTeamManager + PvpRobotTeam (GUID ของ PvpTeamManager)
    /// หุ่นที่แขนขาหลุดครบตามจำนวนที่ตั้ง = แพ้ อีกทีมชนะ
    /// การเลือกทีม/ชิ้นส่วนย้ายไปอยู่ใน LimbSelection (ทีม = หุ่นที่เลือก)
    /// </summary>
    public class PvpModeRules : ModeRules
    {
        [Header("Defeat Rule")]
        [Tooltip("ชิ้นส่วนต้องหลุดกี่ชิ้นถึงนับว่าแพ้ (4 = ต้องหลุดครบทุกชิ้น)")]
        [SerializeField, Range(1, 4)] private int limbsLostToLose = 4;

        [SerializeField] private bool debugLog = true;

        protected override void EvaluateDefeat()
        {
            foreach (Robot robot in robots)
            {
                if (robot.GetTeam() == Team.None) continue;

                int total = 0, lost = 0;
                foreach (LimbSlot slot in LimbSlots.All)
                {
                    RobotLimb limb = robot.GetLimb(slot);
                    if (limb == null || limb.Attachment == null) continue;
                    total++;
                    if (!limb.IsAttached) lost++;
                }

                if (total == 0 || lost < Mathf.Min(limbsLostToLose, total)) continue;

                if (debugLog)
                    Debug.Log($"[PVP] ☠️ หุ่นทีม {robot.GetTeam().DisplayName()} แพ้แล้ว — ชิ้นส่วนหลุด {lost}/{total} ชิ้น");

                winner = robot.GetTeam().Opposite();
                EndMatch(true);
                return;
            }
        }

        private Team winner;

        protected override MatchResult BuildResult(bool victory)
        {
            MatchResult result = base.BuildResult(victory);
            result.winningTeam = winner;
            if (debugLog) Debug.Log($"[PVP] 🏆 ทีม {winner.DisplayName()} ชนะ!");
            return result;
        }
    }
}
