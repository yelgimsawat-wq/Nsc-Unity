using Nsc.Combat;
using UnityEngine;

namespace Nsc.Robots
{
    /// <summary>
    /// ทำให้ลำตัวหุ่นรับดาเมจได้ — ลำตัวไม่มีเลือดของตัวเอง (ไม่มีข้อต่อให้หลุด)
    /// แต่ผู้เล่นย่อมเล็งลำตัวเพราะเป็นเป้าใหญ่สุด จึงส่งดาเมจเต็มจำนวนต่อให้ชิ้นที่ "ใกล้จุดปะทะที่สุด"
    /// (เดิมอยู่ใน PvpRobotTeam.ServerApplyBodyDamage — ใช้ GUID เดิม วางบนลำตัวของหุ่นในซีน PVP)
    ///
    /// ⚠️ ห้ามหารกระจายให้ทุกชิ้นเท่าๆ กัน — ทุกชิ้นเลือดเท่ากัน ถ้าโดนเท่ากันทุกหมัดจะถึง 0 พร้อมกัน
    ///    แล้วหลุดหมดทั้ง 4 ชิ้นในหมัดเดียว = จบเกมทันที
    /// </summary>
    public class RobotBodyDamageRelay : MonoBehaviour, IDamageable
    {
        [SerializeField] private bool debugLog = true;

        private Robot robot;

        private void Awake()
        {
            robot = GetComponent<Robot>();
            if (robot == null)
                Debug.LogError($"[RobotBodyDamageRelay] '{name}' ต้องอยู่บนลำตัวคู่กับ Robot", this);
        }

        public Team GetTeam() => robot != null ? robot.GetTeam() : Team.None;

        public bool ServerApplyDamage(DamageInfo info)
        {
            LimbHealth nearest = NearestAttachedLimb(info.point);
            if (nearest == null) return false;

            float hpBefore = nearest.Hp;
            bool applied = nearest.ServerApplyDamage(info);

            if (applied && debugLog)
                Debug.Log($"[RobotBody] 🫀 ลำตัวทีม {GetTeam().DisplayName()} รับ {info.amount:F1} " +
                          $"→ ลงที่ '{nearest.name}' (ชิ้นใกล้จุดปะทะสุด) | เลือด {hpBefore:F0} → {nearest.Hp:F0} / {nearest.MaxHp:F0}");
            return applied;
        }

        private LimbHealth NearestAttachedLimb(Vector3 point)
        {
            if (robot == null) return null;

            LimbHealth nearest = null;
            float nearestSqr = float.MaxValue;
            foreach (RobotLimb limb in robot.AttachedLimbs())
            {
                LimbHealth health = limb.Health;
                if (health == null || health.Hp <= 0f) continue;

                float sqr = (health.transform.position - point).sqrMagnitude;
                if (sqr < nearestSqr)
                {
                    nearestSqr = sqr;
                    nearest = health;
                }
            }
            return nearest;
        }
    }
}
