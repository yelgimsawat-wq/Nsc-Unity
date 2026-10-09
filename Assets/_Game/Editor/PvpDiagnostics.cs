using System.Text;
using Nsc.Combat;
using Nsc.Match;
using Nsc.Robots;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace NscGame.Pvp
{
    /// <summary>
    /// เครื่องมือตรวจสถานะ PVP ตอนรันจริง — แยกให้ชัดว่า "เลือดไม่ลด" พังตรงไหน:
    ///   1. ดาเมจไม่ถูกส่ง (เล็งพลาด / ทีมผิด / แมตช์ยังไม่เริ่ม)
    ///   2. ดาเมจส่งแล้วแต่ไม่มีชิ้นรับ (ชิ้นหลุดไปแล้ว)
    ///   3. เลือดลดจริงแต่ UI ไม่อัปเดต
    /// เมนู Report = ดูสถานะทุกอย่าง | เมนู Damage = ยิงดาเมจผ่าน DamageRouter ข้ามระบบเล็ง
    /// </summary>
    public static class PvpDiagnostics
    {
        [MenuItem("Tools/NSC/PVP/Diagnose (ตอน Play เท่านั้น)")]
        public static void Report()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("PVP Diagnose", "ใช้ได้เฉพาะตอนกด Play อยู่", "โอเค");
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("========== PVP DIAGNOSE ==========");

            NetworkManager nm = NetworkManager.Singleton;
            sb.AppendLine($"NetworkManager : listening={nm != null && nm.IsListening} " +
                          $"server={nm != null && nm.IsServer} client={nm != null && nm.IsClient}");

            MatchSession session = MatchSession.Current;
            sb.AppendLine(session == null
                ? "MatchSession   : ❌ ไม่มีในฉาก — GameplayGate เปิดตลอด"
                : $"MatchSession   : phase={session.Phase} canDamage={GameplayGate.CanDamage}");
            if (session != null && !session.IsPlaying)
                sb.AppendLine("                 ⚠️ ยังไม่ Playing → ดาเมจทุกทางถูกปฏิเสธ ต้องกด START");

            foreach (Robot robot in Robot.All)
            {
                sb.AppendLine();
                sb.AppendLine($"[{robot.GetTeam().DisplayName()}] root='{(robot.Root != null ? robot.Root.name : "null")}' " +
                              $"spawned={robot.IsSpawned} ต่ออยู่={robot.AttachedLimbCount}/4 " +
                              $"ลำตัวรับดาเมจ={(robot.GetComponent<RobotBodyDamageRelay>() != null ? "ได้" : "ไม่ได้")}");

                foreach (LimbSlot slot in LimbSlots.All)
                {
                    RobotLimb limb = robot.GetLimb(slot);
                    if (limb == null)
                    {
                        sb.AppendLine($"             • {slot,-9} ❌ ไม่มี RobotLimb");
                        continue;
                    }
                    string hp = limb.Health != null ? $"{limb.Health.Hp,6:F0}/{limb.Health.MaxHp,-6:F0}" : "ไม่มีเลือด";
                    sb.AppendLine($"             • {slot,-9} hp={hp} {(limb.IsAttached ? "ต่ออยู่" : "หลุดแล้ว")}");
                }
            }

            LocalRobotBinder binder = Object.FindFirstObjectByType<LocalRobotBinder>(FindObjectsInactive.Include);
            sb.AppendLine();
            sb.AppendLine(binder == null
                ? "PlayerHUD      : ❌ ไม่มี LocalRobotBinder ในฉาก"
                : $"PlayerHUD      : bound={binder.IsBound} หุ่นที่เกาะ='{(binder.RobotRoot != null ? binder.RobotRoot.name : "null")}' " +
                  $"ชิ้นที่คุม={(binder.OwnedLimb != null ? binder.OwnedLimb.Slot.ToString() : "none")}");

            sb.AppendLine("==================================");
            Debug.Log(sb.ToString());
        }

        [MenuItem("Tools/NSC/PVP/Test Damage: ตัดเลือดทีมน้ำเงิน 100")]
        public static void DamageBlue() => TestDamage(Team.Blue, 100f);

        [MenuItem("Tools/NSC/PVP/Test Damage: ตัดเลือดทีมแดง 100")]
        public static void DamageRed() => TestDamage(Team.Red, 100f);

        /// <summary>ยิงดาเมจเข้าลำตัวผ่าน DamageRouter (เส้นทางเดียวกับต่อยลำตัว) — ข้ามระบบเล็ง/ชน</summary>
        private static void TestDamage(Team team, float damage)
        {
            if (!Application.isPlaying || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                Debug.LogWarning("[PVP] Test Damage ต้องรันตอน Play บนเครื่องที่เป็น Host/Server เท่านั้น");
                return;
            }

            foreach (Robot robot in Robot.All)
            {
                if (robot.GetTeam() != team) continue;

                IDamageable body = robot.GetComponent<RobotBodyDamageRelay>();
                var info = new DamageInfo(damage, robot.transform.position, Vector3.forward, DamageSource.Punch, Team.None);
                bool applied = DamageRouter.TryApply(body, info);
                Debug.Log(applied
                    ? $"[PVP] 🧪 ตัดเลือดทีม {team.DisplayName()} {damage} — ถ้า UI ไม่ขยับ = ปัญหาอยู่ที่ UI"
                    : $"[PVP] 🧪 ❌ ไม่สำเร็จ — แมตช์ยังไม่เริ่ม / ไม่มี RobotBodyDamageRelay / ชิ้นหลุดหมด");
                return;
            }

            Debug.LogWarning($"[PVP] ไม่เจอหุ่นทีม {team.DisplayName()}");
        }
    }
}
