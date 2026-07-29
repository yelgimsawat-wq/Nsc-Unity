using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NscUnity.Items.Editor
{
    /// <summary>
    /// ติดตั้งระบบไอเทมให้หุ่นทุกตัวในฉากแบบอัตโนมัติ
    ///
    /// เกมนี้ "ผู้เล่น 1 คน = แขน 1 ข้าง" — LobbyManager โอน ownership ของ NetworkObject แต่ละ limb
    /// ให้ผู้เล่นที่จองไว้ ดังนั้นระบบไอเทมทั้งชุดต้องอยู่ที่ "แขน" ไม่ใช่ลำตัว เพราะ:
    ///   • ลำตัวมีเจ้าของแค่คนเดียว ถ้าเอากระเป๋าไปไว้ที่นั่น ผู้เล่นคนอื่นจะเปิดวงล้อของตัวเองไม่ได้เลย (IsOwner = false)
    ///   • แต่ละคนต้องมีกระเป๋า/วงล้อ/ปุ่มเก็บของ ของตัวเอง แยกอิสระจากกันสมบูรณ์
    ///
    /// แปะให้แขนทุกข้างที่เจอ (ข้างละชุด): PlayerInventory + HandItemHolder + ItemPickupInteractor
    /// พร้อมสร้าง HoldPoint ให้อัตโนมัติ และผูกสายทุกช่องให้เรียบร้อย
    ///
    /// เมนู: Tools ▸ NSC ▸ Item System ▸ Setup On Robots In Scene
    ///
    /// ⚠️ เครื่องมือนี้แค่เตือนถ้าแขนไม่มี NetworkObject ไม่ได้เพิ่มให้เอง เพราะกระทบระบบ Lobby/PVP ที่ตั้งไว้อยู่แล้ว
    /// </summary>
    public static class ItemSystemSetup
    {
        [MenuItem("Tools/NSC/Item System/Setup On Robots In Scene")]
        public static void Setup()
        {
            List<Transform> robots = FindRobotRoots();

            if (robots.Count == 0)
            {
                EditorUtility.DisplayDialog("Item System Setup",
                    "หาหุ่นในฉากไม่เจอเลย (นับจาก TorsoMovement ที่ active อยู่)",
                    "เข้าใจแล้ว");
                return;
            }

            ItemDatabase database = FindSingleItemDatabase();

            int armCount = 0, armWarnCount = 0;

            foreach (Transform root in robots)
            {
                SetupResult result = SetupRobot(root, database);
                armCount += result.armsSetUp;
                armWarnCount += result.armsMissingNetworkObject;
            }

            bool createdUI = EnsureItemUI();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            string warnings = "";
            if (armWarnCount > 0) warnings += $"\n⚠️ แขน {armWarnCount} ข้างไม่มี NetworkObject — เล่นออนไลน์ไม่ได้ ต้องเพิ่มเอง";
            if (database == null) warnings += "\n⚠️ ไม่พบ Item Database ในโปรเจกต์ (หรือมีมากกว่า 1 ไฟล์) — ต้องลากผูกเองใน PlayerInventory ของแต่ละแขน";

            EditorUtility.DisplayDialog("Item System Setup",
                $"ติดตั้งเสร็จแล้ว!\n\n" +
                $"• หุ่น {robots.Count} ตัว\n" +
                $"• แขน {armCount} ข้าง (แต่ละข้างมีกระเป๋า/วงล้อ/ปุ่มเก็บของเป็นของตัวเองแยกกัน)\n" +
                $"• UI ในฉาก: {(createdUI ? "สร้าง 'ItemUI' ให้ใหม่" : "มีอยู่แล้ว")}" +
                warnings,
                "โอเค");
        }

        /// <summary>
        /// สร้าง GameObject 'ItemUI' พร้อม ItemWheelUI + PickupPromptUI ถ้ายังไม่มีในฉาก
        /// (UI สองตัวนี้เป็นของ local ไม่ต้องมี NetworkObject — มันหาแขนที่เราคุมอยู่เองตอนรัน)
        /// คืน true ถ้าสร้างใหม่
        /// </summary>
        private static bool EnsureItemUI()
        {
            ItemWheelUI wheel = Object.FindFirstObjectByType<ItemWheelUI>(FindObjectsInactive.Include);
            PickupPromptUI prompt = Object.FindFirstObjectByType<PickupPromptUI>(FindObjectsInactive.Include);

            if (wheel != null && prompt != null) return false;

            GameObject uiRoot = wheel != null ? wheel.gameObject
                              : prompt != null ? prompt.gameObject
                              : null;

            bool created = false;
            if (uiRoot == null)
            {
                uiRoot = new GameObject("ItemUI");
                Undo.RegisterCreatedObjectUndo(uiRoot, "Create ItemUI");
                created = true;
            }

            if (wheel == null) Undo.AddComponent<ItemWheelUI>(uiRoot);
            if (prompt == null) Undo.AddComponent<PickupPromptUI>(uiRoot);

            return created;
        }

        private struct SetupResult
        {
            public int armsSetUp;
            public int armsMissingNetworkObject;
        }

        #region Steps

        /// <summary>หา root ของหุ่นทุกตัวในฉาก โดยนับจาก TorsoMovement (ตรรกะเดียวกับ PvpSceneSetup)</summary>
        private static List<Transform> FindRobotRoots()
        {
            return Object.FindObjectsByType<TorsoMovement>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Select(t => ResolveRobotRoot(t.transform))
                .Distinct()
                .OrderBy(t => t.name)
                .ToList();
        }

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

        private static SetupResult SetupRobot(Transform root, ItemDatabase database)
        {
            SetupResult result = new SetupResult();

            PlayerHandMovement[] arms = root.GetComponentsInChildren<PlayerHandMovement>(true);
            if (arms.Length == 0)
            {
                Debug.LogWarning($"[Item Setup] หุ่น '{root.name}' ไม่มี PlayerHandMovement เลย — ไม่มีแขนให้ถือของ", root);
                return result;
            }

            // ติดตั้งให้แขน "ทุกข้าง" เพราะแต่ละข้างคือผู้เล่นคนละคน ต้องมีของตัวเองแยกกันสมบูรณ์
            foreach (PlayerHandMovement arm in arms)
            {
                if (SetupArm(arm.transform, database, out bool missingNetworkObject))
                {
                    result.armsSetUp++;
                    if (missingNetworkObject) result.armsMissingNetworkObject++;
                }
            }

            Debug.Log($"[Item Setup] หุ่น '{root.name}' → ติดตั้งระบบไอเทมให้แขน {result.armsSetUp} ข้าง", root);
            return result;
        }

        /// <summary>ติดตั้งชุดระบบไอเทมครบชุดให้แขน 1 ข้าง (กระเป๋า + มือถือของ + ตัวเก็บของ อยู่ด้วยกันหมด)</summary>
        private static bool SetupArm(Transform arm, ItemDatabase database, out bool missingNetworkObject)
        {
            missingNetworkObject = arm.GetComponent<NetworkObject>() == null;
            if (missingNetworkObject)
            {
                Debug.LogWarning($"[Item Setup] แขน '{arm.name}' ไม่มี NetworkObject — เล่นออนไลน์ไม่ได้ ต้องเพิ่มเอง", arm);
            }

            PlayerInventory inventory = arm.GetComponent<PlayerInventory>();
            if (inventory == null) inventory = Undo.AddComponent<PlayerInventory>(arm.gameObject);

            HandItemHolder hand = arm.GetComponent<HandItemHolder>();
            if (hand == null) hand = Undo.AddComponent<HandItemHolder>(arm.gameObject);

            ItemPickupInteractor interactor = arm.GetComponent<ItemPickupInteractor>();
            if (interactor == null) interactor = Undo.AddComponent<ItemPickupInteractor>(arm.gameObject);

            Transform holdPoint = arm.Find("HoldPoint");
            if (holdPoint == null)
            {
                GameObject holdPointObject = new GameObject("HoldPoint");
                Undo.RegisterCreatedObjectUndo(holdPointObject, "Create HoldPoint");
                holdPoint = holdPointObject.transform;
                holdPoint.SetParent(arm, false);
            }

            SerializedObject handSo = new SerializedObject(hand);
            if (handSo.FindProperty("holdPoint").objectReferenceValue == null)
                handSo.FindProperty("holdPoint").objectReferenceValue = holdPoint;
            handSo.ApplyModifiedProperties();

            SerializedObject interactorSo = new SerializedObject(interactor);
            if (interactorSo.FindProperty("inventory").objectReferenceValue == null)
                interactorSo.FindProperty("inventory").objectReferenceValue = inventory;
            interactorSo.ApplyModifiedProperties();

            SerializedObject inventorySo = new SerializedObject(inventory);
            if (inventorySo.FindProperty("hand").objectReferenceValue == null)
                inventorySo.FindProperty("hand").objectReferenceValue = hand;
            if (database != null && inventorySo.FindProperty("itemDatabase").objectReferenceValue == null)
                inventorySo.FindProperty("itemDatabase").objectReferenceValue = database;
            inventorySo.ApplyModifiedProperties();

            return true;
        }

        /// <summary>หา Item Database ตัวเดียวในโปรเจกต์ คืน null ถ้าไม่มีหรือมีมากกว่า 1 ไฟล์ (ให้ผู้ใช้เลือกเอง)</summary>
        private static ItemDatabase FindSingleItemDatabase()
        {
            string[] guids = AssetDatabase.FindAssets("t:ItemDatabase");
            if (guids.Length != 1) return null;

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<ItemDatabase>(path);
        }

        #endregion
    }
}
