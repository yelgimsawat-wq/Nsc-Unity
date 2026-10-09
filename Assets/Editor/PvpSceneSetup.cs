using System.Collections.Generic;
using System.Linq;
using Nsc.Combat;
using Nsc.Match;
using Nsc.Robots;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NscGame.Pvp
{
    /// <summary>
    /// ติดตั้งโหมด PVP ให้ฉากปัจจุบันแบบอัตโนมัติ:
    ///   • หาหุ่นทุกตัวในฉาก (Robot) แล้วตั้งทีมแดง/น้ำเงินให้ + ใส่ RobotBodyDamageRelay ที่ลำตัว (ตีลำตัวได้)
    ///   • สร้างตัวคุมแมตช์ (NetworkObject + MatchSession + LimbSelection + LimbControlBinder + PvpModeRules)
    ///
    /// เมนู: Tools ▸ NSC ▸ PVP ▸ Setup Robots In Scene
    /// ⚠️ ต้องมีหุ่น "สองตัว" ที่ active อยู่ในฉาก
    /// </summary>
    public static class PvpSceneSetup
    {
        [MenuItem("Tools/NSC/PVP/Setup Robots In Scene")]
        public static void Setup()
        {
            List<Robot> robots = Object.FindObjectsByType<Robot>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .OrderBy(r => r.transform.parent != null ? r.transform.parent.name : r.name)
                .ToList();

            if (robots.Count != 2)
            {
                EditorUtility.DisplayDialog("PVP Setup",
                    $"เจอหุ่นในฉาก {robots.Count} ตัว — โหมด PVP ต้องมี 2 ตัวพอดี\n\n" +
                    "วิธีแก้: ก็อปหุ่นเดิมเป็นตัวที่สอง วางให้ห่างกัน แล้วสั่ง Setup ใหม่",
                    "เข้าใจแล้ว");
                return;
            }

            Team[] teams = { Team.Red, Team.Blue };
            for (int i = 0; i < robots.Count; i++)
            {
                SetupRobot(robots[i], teams[i]);
                Debug.Log($"[PVP Setup] หุ่น '{robots[i].transform.root.name}' → ทีม {teams[i].DisplayName()}", robots[i]);
            }

            SetupMatch();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            EditorUtility.DisplayDialog("PVP Setup",
                "เสร็จแล้ว!\n\nขั้นต่อไป: Tools ▸ NSC ▸ PVP ▸ Build PVP UI", "โอเค");
        }

        private static void SetupRobot(Robot robot, Team team)
        {
            SerializedObject so = new SerializedObject(robot);
            so.FindProperty("team.m_InternalValue").intValue = (int)team;
            so.ApplyModifiedProperties();

            if (robot.GetComponent<RobotBodyDamageRelay>() == null)
                Undo.AddComponent<RobotBodyDamageRelay>(robot.gameObject);

            if (robot.GetComponentsInChildren<LimbHealth>(true).Length == 0)
                Debug.LogWarning($"[PVP Setup] หุ่น '{robot.name}' ไม่มี LimbHealth สักชิ้น — ต่อยแล้วเลือดจะไม่ลด", robot);
        }

        /// <summary>ตัวคุมแมตช์ PVP: เลือกหุ่น = เลือกทีม / ไม่แช่แข็งหุ่น / ไม่ปิดหุ่นตัวเกิน</summary>
        private static void SetupMatch()
        {
            PvpModeRules rules = Object.FindFirstObjectByType<PvpModeRules>();
            GameObject go;
            if (rules == null)
            {
                go = new GameObject("PvpMatch");
                Undo.RegisterCreatedObjectUndo(go, "Create PvpMatch");
                go.AddComponent<NetworkObject>();
                rules = go.AddComponent<PvpModeRules>();
            }
            else
            {
                go = rules.gameObject;
                if (go.GetComponent<NetworkObject>() == null) Undo.AddComponent<NetworkObject>(go);
            }

            if (go.GetComponent<MatchSession>() == null) Undo.AddComponent<MatchSession>(go);
            if (go.GetComponent<LimbControlBinder>() == null) Undo.AddComponent<LimbControlBinder>(go);
            LimbSelection selection = go.GetComponent<LimbSelection>();
            if (selection == null) selection = Undo.AddComponent<LimbSelection>(go);

            SerializedObject so = new SerializedObject(selection);
            so.FindProperty("requireEveryRobotManned").boolValue = true;
            so.FindProperty("requireLimbForEveryPlayer").boolValue = true;
            so.FindProperty("autoDisableExtraRobots").boolValue = false;
            so.FindProperty("freezeRobotsWhilePreparing").boolValue = false;
            so.ApplyModifiedProperties();

            SerializedObject rulesSo = new SerializedObject(rules);
            rulesSo.FindProperty("defeatCheckInterval").floatValue = 0.25f;
            rulesSo.ApplyModifiedProperties();
        }
    }
}
