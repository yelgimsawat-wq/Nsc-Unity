using UnityEngine;

/// <summary>
/// ติดบน root ของหุ่นแต่ละตัว บอกว่าหุ่นตัวนี้อยู่ทีมไหน (ใช้ในโหมด PVP)
///
/// ทำไมเป็น MonoBehaviour ธรรมดา ไม่ใช่ NetworkBehaviour:
/// หุ่นทั้ง 2 ตัวถูกวางไว้ในฉากตั้งแต่แรก (ไม่ได้ spawn ตอนรันไทม์) → เลขทีมเป็นค่าคงที่
/// ของฉาก ทุกเครื่องอ่านได้ตรงกันอยู่แล้ว ไม่ต้อง sync ผ่าน network ให้เปลืองแบนด์วิดท์
/// </summary>
public class RobotTeam : MonoBehaviour
{
    [Tooltip("หมายเลขทีมของหุ่นตัวนี้ — ตั้งในฉาก หุ่นคนละฝ่ายต้องตั้งเลขต่างกัน (เช่น 0 กับ 1)\n" +
             "-1 = ยังไม่กำหนด (ถือว่าเป็นศัตรูกับทุกตัว — โหมด PVE ปกติใช้ค่านี้)")]
    [SerializeField] private int teamId = -1;

    public int TeamId => teamId;

    /// <summary>กำหนดทีมตอนรันไทม์ — เผื่อ LobbyManager อยากจัดทีมเอง</summary>
    public void SetTeam(int id) => teamId = id;

    /// <summary>
    /// หา RobotTeam จากชิ้นส่วน/collider ใดๆ ของหุ่น
    /// ชิ้นที่หลุดออกไปแล้วอาจถูกย้าย parent ออกนอกตัวหุ่น จึงต้องโยงกลับผ่าน
    /// joint.targetBody (จุดที่ชิ้นนั้นต่ออยู่ ซึ่งไม่เคยหลุดจากหุ่น) เหมือนที่
    /// LocalRobotBinder.ResolveRobotRoot ทำ
    /// </summary>
    public static RobotTeam Find(Transform part)
    {
        if (part == null) return null;

        RobotTeam team = part.GetComponentInParent<RobotTeam>();
        if (team != null) return team;

        JointPullAndReconnect joint = part.GetComponentInParent<JointPullAndReconnect>();
        if (joint != null && joint.targetBody != null)
            return joint.targetBody.GetComponentInParent<RobotTeam>();

        return null;
    }

    /// <summary>
    /// true = คนละทีม ตีกันได้
    /// ถ้าฝั่งใดฝั่งหนึ่งไม่มี RobotTeam หรือยังไม่กำหนดทีม (-1) ให้ถือว่าตีได้ตามปกติ
    /// → โหมด PVE เดิมไม่กระทบเลย และ 1v1 ที่ลืมตั้งเลขทีมก็ยังเล่นได้
    /// (การตีชิ้นส่วนหุ่นตัวเองถูกกันด้วย root check ใน PhysicsDamageSender อยู่แล้ว)
    /// </summary>
    public static bool AreEnemies(Transform attacker, Transform victim)
    {
        RobotTeam a = Find(attacker);
        RobotTeam b = Find(victim);

        if (a == null || b == null) return true;
        if (a.TeamId < 0 || b.TeamId < 0) return true;

        return a.TeamId != b.TeamId;
    }
}
