using UnityEngine;

/// <summary>
/// เส้นชัยโหมด Parkour — วางบน GameObject ที่มี Collider (isTrigger = true) ไว้บนยอดด่าน
/// ชิ้นส่วนใดของหุ่นแตะ → จบด่าน โชว์ HIGH SCORE panel (ผ่าน ParkourFlowManager)
/// </summary>
[RequireComponent(typeof(Collider))]
public class ParkourGoalZone : MonoBehaviour
{
    private bool triggered;

    private void OnTriggerEnter(Collider other)
    {
        if (triggered) return;
        if (NetworkCheck.IsServerOrHost() == false) return;
        if (!RobotBodyCheck.IsRobotBodyPart(other)) return;

        if (ParkourFlowManager.Instance == null)
        {
            Debug.LogWarning("[ParkourGoalZone] ParkourFlowManager ไม่อยู่ในฉาก — จบด่านไม่ทำงาน");
            return;
        }

        triggered = true;
        Debug.Log("[ParkourGoalZone] 🏁 robot reached the goal!");
        ParkourFlowManager.Instance.TriggerRunEnd();
    }
}
