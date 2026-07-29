using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NscGame.Pvp
{
    /// <summary>
    /// ติดตั้งโหมด PVP ให้ฉากปัจจุบันแบบอัตโนมัติ:
    ///   • หาหุ่นทุกตัวในฉาก (นับจาก TorsoMovement) แล้วแปะ PvpRobotTeam ให้ตัวละทีม
    ///   • แปะ PvpDamageSender ให้ทุกมือ/เท้า (มือ = ต่อย, เท้า = เตะ)
    ///   • สร้าง PvpTeamManager (พร้อม NetworkObject) แล้วต่อ root ของหุ่นสองตัวให้
    ///
    /// เมนู: Tools ▸ NSC ▸ PVP ▸ Setup Robots In Scene
    ///
    /// ⚠️ ต้องมีหุ่น "สองตัว" ที่ active อยู่ในฉาก และฉากนี้ต้องไม่มี LobbyManager
    ///    (LobbyManager จะปิดหุ่นตัวเกินทิ้งอัตโนมัติ เหลือตัวเดียวสู้กันไม่ได้)
    /// </summary>
    public static class PvpSceneSetup
    {
        [MenuItem("Tools/NSC/PVP/Setup Robots In Scene")]
        public static void Setup()
        {
            List<Transform> robots = FindRobotRoots();

            if (robots.Count != 2)
            {
                EditorUtility.DisplayDialog("PVP Setup",
                    $"เจอหุ่นในฉาก {robots.Count} ตัว — โหมด PVP ต้องมี 2 ตัวพอดี\n\n" +
                    "วิธีแก้: ก็อปหุ่นเดิมเป็นตัวที่สอง วางให้ห่างกัน แล้วสั่ง Setup ใหม่\n" +
                    "(นับหุ่นจาก TorsoMovement ที่ active อยู่ในฉาก)",
                    "เข้าใจแล้ว");
                return;
            }

            if (Object.FindFirstObjectByType<LobbyManager>() != null)
            {
                bool proceed = EditorUtility.DisplayDialog("PVP Setup",
                    "ฉากนี้มี LobbyManager อยู่ — มันจะปิดหุ่นตัวเกินทิ้งอัตโนมัติตอนเริ่มเกม " +
                    "ทำให้เหลือหุ่นตัวเดียวและสู้กันไม่ได้\n\nแนะนำให้ลบ/ปิด LobbyManager ในฉาก PVP",
                    "ทำต่อไปก่อน", "ยกเลิก");
                if (!proceed) return;
            }

            PvpTeam[] teams = { PvpTeam.Red, PvpTeam.Blue };
            int handCount = 0, footCount = 0;

            for (int i = 0; i < robots.Count; i++)
            {
                Transform root = robots[i];
                PvpTeam team = teams[i];

                SetupRobotTeam(root, team);
                handCount += SetupDamageSenders<PlayerHandMovement>(root);
                footCount += SetupDamageSenders<PlayerFootForRobot>(root);

                Debug.Log($"[PVP Setup] หุ่น '{root.name}' → ทีม {team.DisplayName()}", root);
            }

            SetupManager(robots[0], robots[1]);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            EditorUtility.DisplayDialog("PVP Setup",
                $"เสร็จแล้ว!\n\n" +
                $"• หุ่นแดง: {robots[0].name}\n" +
                $"• หุ่นน้ำเงิน: {robots[1].name}\n" +
                $"• ใส่ PvpDamageSender ให้มือ {handCount} ชิ้น / เท้า {footCount} ชิ้น\n\n" +
                "ขั้นต่อไป: Tools ▸ NSC ▸ PVP ▸ Build PVP UI",
                "โอเค");
        }

        #region Steps

        /// <summary>หา root ของหุ่นทุกตัวในฉาก โดยนับจาก TorsoMovement (หนึ่งลำตัว = หนึ่งหุ่น)</summary>
        private static List<Transform> FindRobotRoots()
        {
            return Object.FindObjectsByType<TorsoMovement>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Select(t => ResolveRobotRoot(t.transform))
                .Distinct()
                .OrderBy(t => t.name)
                .ToList();
        }

        /// <summary>
        /// ไต่ขึ้นไปให้สูงสุดเท่าที่ยังครอบ "ลำตัวเดียว" อยู่ — ใช้ transform.root ตรงๆ ไม่ได้
        /// เพราะถ้าเอาหุ่นสองตัวไปไว้ใต้ parent เดียวกัน ทั้งคู่จะได้ root เดียวกัน
        /// (ตรรกะเดียวกับ PvpRobotTeam.ResolveRobotRoot)
        /// </summary>
        private static Transform ResolveRobotRoot(Transform from)
        {
            Transform best = from;

            for (Transform t = from; t != null; t = t.parent)
            {
                if (t.GetComponentsInChildren<TorsoMovement>(true).Length > 1) break;
                best = t;
            }

            return best;
        }

        /// <summary>
        /// PvpRobotTeam ต้องอยู่บน GameObject ที่มี NetworkObject — ลำตัวคือที่ที่เหมาะที่สุด
        /// (root ของหุ่นในโปรเจกต์นี้ไม่มี NetworkObject แต่ละ limb มีของตัวเอง)
        /// </summary>
        private static void SetupRobotTeam(Transform root, PvpTeam team)
        {
            TorsoMovement torso = root.GetComponentInChildren<TorsoMovement>(true);
            if (torso == null)
            {
                Debug.LogError($"[PVP Setup] หุ่น '{root.name}' ไม่มี TorsoMovement — ข้าม", root);
                return;
            }

            if (torso.GetComponent<NetworkObject>() == null)
                Debug.LogWarning($"[PVP Setup] ลำตัว '{torso.name}' ไม่มี NetworkObject — " +
                                 "สถานะแพ้ชนะจะ sync ไม่ได้ ต้องเพิ่ม NetworkObject เอง", torso);

            PvpRobotTeam robot = torso.GetComponent<PvpRobotTeam>();
            if (robot == null)
                robot = Undo.AddComponent<PvpRobotTeam>(torso.gameObject);

            SerializedObject so = new SerializedObject(robot);
            so.FindProperty("team").enumValueIndex = System.Array.IndexOf(
                new[] { PvpTeam.None, PvpTeam.Red, PvpTeam.Blue }, team);
            so.FindProperty("robotRoot").objectReferenceValue = root;
            so.ApplyModifiedProperties();

            // เตือนถ้าหุ่นไม่มีระบบเลือดเดิม — PVP ยิงดาเมจเข้า RobotHealth ไม่ได้เลย
            int healthCount = root.GetComponentsInChildren<RobotHealth>(true).Length;
            if (healthCount == 0)
                Debug.LogWarning($"[PVP Setup] หุ่น '{root.name}' ไม่มี RobotHealth สักชิ้น — " +
                                 "ต่อยแล้วเลือดจะไม่ลด (PVP ใช้ระบบเลือดเดิมต่อชิ้นส่วน)", root);
        }

        /// <summary>แปะ PvpDamageSender ให้ทุกชิ้นส่วนชนิด T ที่มี Rigidbody</summary>
        private static int SetupDamageSenders<T>(Transform root) where T : Component
        {
            int count = 0;

            foreach (T part in root.GetComponentsInChildren<T>(true))
            {
                if (part.GetComponent<Rigidbody>() == null) continue;
                if (part.GetComponent<PvpDamageSender>() != null) { count++; continue; }

                Undo.AddComponent<PvpDamageSender>(part.gameObject);
                count++;
            }

            return count;
        }

        private static void SetupManager(Transform redRoot, Transform blueRoot)
        {
            PvpTeamManager manager = Object.FindFirstObjectByType<PvpTeamManager>();

            if (manager == null)
            {
                GameObject go = new GameObject("PvpTeamManager");
                Undo.RegisterCreatedObjectUndo(go, "Create PvpTeamManager");
                go.AddComponent<NetworkObject>();
                manager = go.AddComponent<PvpTeamManager>();
            }
            else if (manager.GetComponent<NetworkObject>() == null)
            {
                Undo.AddComponent<NetworkObject>(manager.gameObject);
            }

            SerializedObject so = new SerializedObject(manager);
            so.FindProperty("redRobotRoot").objectReferenceValue = redRoot;
            so.FindProperty("blueRobotRoot").objectReferenceValue = blueRoot;
            so.ApplyModifiedProperties();
        }

        #endregion
    }
}
