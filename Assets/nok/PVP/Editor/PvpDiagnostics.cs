using System.Text;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace NscGame.Pvp
{
    /// <summary>
    /// เครื่องมือตรวจสถานะ PVP ตอนรันจริง — ใช้แยกให้ชัดว่า "เลือดไม่ลด" พังตรงไหน
    ///
    /// ปัญหาเลือดไม่ลดมีได้ 3 จุด แต่จากภายนอกดูเหมือนกันหมด:
    ///   1. ดาเมจไม่ถูกส่ง (เล็งพลาด / ทีมผิด / แมตช์ยังไม่เริ่ม)
    ///   2. ดาเมจส่งแล้วแต่ RobotHealth ไม่รับ (ชิ้นหลุดไปแล้ว / หาชิ้นไม่เจอ)
    ///   3. เลือดลดจริงแต่ UI ไม่อัปเดต
    ///
    /// เมนู Report = ดูสถานะทุกอย่าง | เมนู Damage = ยิงดาเมจตรงๆ ข้ามระบบเล็ง
    /// ถ้า Damage แล้ว UI ขยับ → ปัญหาอยู่ที่ข้อ 1 (ระบบเล็ง/ตี)
    /// ถ้า Damage แล้ว UI ไม่ขยับ แต่ Report บอกว่าเลือดลด → ปัญหาอยู่ที่ข้อ 3 (UI)
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

            PvpTeamManager mgr = PvpTeamManager.Instance;
            if (mgr == null)
            {
                sb.AppendLine("PvpTeamManager : ❌ ไม่มีในฉาก — ดาเมจจะไม่นับเลย");
            }
            else
            {
                sb.AppendLine($"PvpTeamManager : spawned={mgr.IsSpawned} state={mgr.MatchState} " +
                              $"fighting={mgr.IsFighting} ผู้เล่น={mgr.PlayerCount} ทีมเรา={mgr.LocalTeam.DisplayName()}");
                if (!mgr.IsFighting)
                    sb.AppendLine("                 ⚠️ ยังไม่ Fighting → ดาเมจทุกทางถูกปฏิเสธ ต้องกด START FIGHT");
            }

            foreach (PvpTeam team in new[] { PvpTeam.Red, PvpTeam.Blue })
            {
                sb.AppendLine();
                PvpRobotTeam robot = PvpRobotTeam.FindByTeam(team);

                if (robot == null)
                {
                    sb.AppendLine($"[{team.DisplayName()}] ❌ ไม่มี PvpRobotTeam — สั่ง Setup Robots In Scene");
                    continue;
                }

                sb.AppendLine($"[{team.DisplayName()}] root='{(robot.RobotRoot != null ? robot.RobotRoot.name : "null")}' " +
                              $"spawned={robot.IsSpawned} แพ้แล้ว={robot.IsDefeated}");
                sb.AppendLine($"           ชิ้นที่หลุดได้={robot.LimbsTotal}  ยังต่ออยู่={robot.LimbsRemaining}");

                if (robot.LimbsTotal == 0)
                    sb.AppendLine("           ⚠️ หาชิ้นส่วนไม่เจอเลย → robotRoot ชี้ผิด หรือหุ่นไม่มี JointPullAndReconnect");

                if (robot.TryGetTotalHp(out float cur, out float max))
                    sb.AppendLine($"           เลือดรวม {cur:F0} / {max:F0}");
                else
                    sb.AppendLine("           ⚠️ TryGetTotalHp คืน false → ไม่มี RobotHealth สักชิ้น " +
                                  "→ แถบเลือดจะนิ่งตลอดและดาเมจลงลำตัวจะไม่มีที่ลง");

                if (robot.RobotRoot != null)
                {
                    foreach (RobotHealth h in robot.RobotRoot.GetComponentsInChildren<RobotHealth>(true))
                    {
                        bool detached = h.Jpar != null && !h.Jpar.IsConnected;
                        sb.AppendLine($"             • {h.name,-16} hp={h.currentHp.Value,6:F0}/{h.currentMaxHp.Value,-6:F0} " +
                                      $"jpar={(h.Jpar != null ? (detached ? "หลุดแล้ว" : "ต่ออยู่") : "❌ ไม่ได้ต่อ")}");
                    }
                }
            }

            LocalRobotBinder binder = Object.FindFirstObjectByType<LocalRobotBinder>(FindObjectsInactive.Include);
            sb.AppendLine();
            sb.AppendLine(binder == null
                ? "PlayerHUD      : ❌ ไม่มี LocalRobotBinder ในฉาก"
                : $"PlayerHUD      : bound={binder.IsBound} " +
                  $"หุ่นที่เกาะ='{(binder.RobotRoot != null ? binder.RobotRoot.name : "null")}' " +
                  $"ชิ้นที่คุม={binder.OwnedHealths.Count}");

            sb.AppendLine("==================================");
            Debug.Log(sb.ToString());
        }

        [MenuItem("Tools/NSC/PVP/Test Damage: ตัดเลือดทีมน้ำเงิน 100")]
        public static void DamageBlue() => TestDamage(PvpTeam.Blue, 100f);

        [MenuItem("Tools/NSC/PVP/Test Damage: ตัดเลือดทีมแดง 100")]
        public static void DamageRed() => TestDamage(PvpTeam.Red, 100f);

        /// <summary>
        /// ยิงดาเมจเข้าหุ่นตรงๆ ข้ามระบบเล็ง/ชน/เช็คทีมทั้งหมด
        /// ใช้พิสูจน์ว่า RobotHealth + UI ทำงานไหม โดยไม่ต้องเล็งให้โดน
        /// </summary>
        private static void TestDamage(PvpTeam team, float damage)
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("PVP Test Damage", "ใช้ได้เฉพาะตอนกด Play อยู่", "โอเค");
                return;
            }

            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                Debug.LogWarning("[PVP] Test Damage ต้องรันบนเครื่องที่เป็น Host/Server เท่านั้น");
                return;
            }

            PvpRobotTeam robot = PvpRobotTeam.FindByTeam(team);
            if (robot == null)
            {
                Debug.LogWarning($"[PVP] ไม่เจอหุ่นทีม {team.DisplayName()}");
                return;
            }

            robot.TryGetTotalHp(out float before, out _);

            // ยิงเข้าที่ตำแหน่งลำตัว → ระบบจะเลือกชิ้นใกล้สุดให้เอง (เส้นทางเดียวกับต่อยลำตัว)
            Vector3 at = robot.RobotRoot != null ? robot.RobotRoot.position : Vector3.zero;
            bool applied = robot.ServerApplyBodyDamage(damage, NscGame.Enemy.AttackType.LightPunch, Vector3.forward, at);

            robot.TryGetTotalHp(out float after, out _);

            Debug.Log(applied
                ? $"[PVP] 🧪 ตัดเลือดทีม {team.DisplayName()} {damage} → เลือดรวม {before:F0} → {after:F0}\n" +
                  "ถ้าตัวเลขนี้ลดแต่ UI บนจอไม่ขยับ = ปัญหาอยู่ที่ UI ไม่ใช่ระบบดาเมจ"
                : $"[PVP] 🧪 ❌ ตัดเลือดไม่สำเร็จ — หุ่นทีม {team.DisplayName()} ไม่มีชิ้นส่วนที่รับดาเมจได้ " +
                  "(ชิ้นหลุดหมดแล้ว หรือไม่มี RobotHealth)");
        }
    }
}
