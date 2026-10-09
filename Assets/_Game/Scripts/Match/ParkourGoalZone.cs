using Nsc.Robots;
using UnityEngine;

namespace Nsc.Match
{
    /// <summary>
    /// เส้นชัยโหมดพาร์คัวร์ — วางบน Collider (isTrigger) บนยอดด่าน
    /// ชิ้นส่วนใดของหุ่นแตะ → จบด่าน
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ParkourGoalZone : MonoBehaviour
    {
        private bool triggered;

        private void OnTriggerEnter(Collider other)
        {
            if (triggered || !NetworkCheck.IsServerOrHost()) return;
            if (Robot.FromCollider(other) == null) return;

            if (ParkourModeRules.Current == null)
            {
                Debug.LogWarning("[ParkourGoalZone] ParkourModeRules ไม่อยู่ในฉาก — จบด่านไม่ทำงาน");
                return;
            }

            triggered = true;
            Debug.Log("[ParkourGoalZone] 🏁 robot reached the goal!");
            ParkourModeRules.Current.ServerReachGoal();
        }
    }
}
