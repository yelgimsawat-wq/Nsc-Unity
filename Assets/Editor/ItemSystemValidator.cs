using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace NscUnity.Items.Editor
{
    /// <summary>
    /// ตรวจสอบว่าระบบไอเทมในฉากปัจจุบันตั้งค่าครบหรือยัง แล้วรายงานเป็นรายการว่าอะไรขาด
    /// ใช้แทนการไล่เดาทีละจุดเวลาเก็บของไม่ได้/ยิงไม่ออก/วงล้อไม่ขึ้น
    ///
    /// เมนู: Tools ▸ NSC ▸ Item System ▸ Validate Setup
    /// </summary>
    public static class ItemSystemValidator
    {
        [MenuItem("Tools/NSC/Item System/Validate Setup")]
        public static void Validate()
        {
            List<string> problems = new List<string>();
            List<string> passed = new List<string>();

            CheckDatabase(problems, passed);
            CheckSceneUI(problems, passed);
            CheckNetworkManager(problems, passed);
            CheckArms(problems, passed);
            CheckStrayComponents(problems, passed);
            CheckWorldItems(problems, passed);

            StringBuilder report = new StringBuilder();

            if (problems.Count == 0)
            {
                report.AppendLine("✅ ตั้งค่าครบทุกอย่างแล้ว!\n");
            }
            else
            {
                report.AppendLine($"❌ เจอปัญหา {problems.Count} จุด:\n");
                foreach (string problem in problems) report.AppendLine($"• {problem}");
                report.AppendLine();
            }

            if (passed.Count > 0)
            {
                report.AppendLine("ผ่านแล้ว:");
                foreach (string ok in passed) report.AppendLine($"✓ {ok}");
            }

            Debug.Log($"[Item System Validate]\n{report}");
            EditorUtility.DisplayDialog("Item System — ตรวจสอบการตั้งค่า", report.ToString(), "โอเค");
        }

        private static void CheckDatabase(List<string> problems, List<string> passed)
        {
            string[] guids = AssetDatabase.FindAssets("t:ItemDatabase");

            if (guids.Length == 0)
            {
                problems.Add("ไม่มี Item Database ในโปรเจกต์เลย — สร้างที่ Create ▸ Nsc ▸ Item Database");
                return;
            }

            if (guids.Length > 1)
            {
                problems.Add($"มี Item Database {guids.Length} ไฟล์ — ควรมีไฟล์เดียว ไม่งั้นแต่ละแขนอาจใช้คนละไฟล์แล้วซิงค์ผิดไอเทม");
            }

            ItemDatabase database = AssetDatabase.LoadAssetAtPath<ItemDatabase>(AssetDatabase.GUIDToAssetPath(guids[0]));
            if (database == null) return;

            if (database.Count == 0)
            {
                problems.Add("Item Database ว่างเปล่า — ต้องลาก ItemDefinition ทุกชิ้นใส่ลิสต์ Items ก่อน");
                return;
            }

            passed.Add($"Item Database มีไอเทม {database.Count} ชนิด");

            // ไอเทมที่วางในฉากแต่ไม่ได้อยู่ใน Database จะเก็บไม่ได้เลย (TryAddServerSide ปฏิเสธ)
            // เป็นสาเหตุที่พบบ่อยที่สุดของอาการ "กดเก็บแล้วไม่มีอะไรเกิดขึ้น"
            HashSet<ItemDefinition> missing = new HashSet<ItemDefinition>();

            foreach (WorldItem item in Object.FindObjectsByType<WorldItem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (item.Definition != null && database.GetIndex(item.Definition) < 0)
                    missing.Add(item.Definition);
            }

            foreach (PlayerInventory inventory in Object.FindObjectsByType<PlayerInventory>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                SerializedObject so = new SerializedObject(inventory);
                SerializedProperty startingItems = so.FindProperty("startingItems");

                for (int i = 0; i < startingItems.arraySize; i++)
                {
                    ItemDefinition item = startingItems.GetArrayElementAtIndex(i).objectReferenceValue as ItemDefinition;
                    if (item != null && database.GetIndex(item) < 0) missing.Add(item);
                }
            }

            if (missing.Count > 0)
            {
                problems.Add($"ไอเทมที่ใช้อยู่แต่ยังไม่ได้ใส่ใน Item Database: {string.Join(", ", missing.Select(m => m.DisplayName))} " +
                             "— เก็บขึ้นมือไม่ได้เลยจนกว่าจะเพิ่มลง Database");
            }
        }

        private static void CheckSceneUI(List<string> problems, List<string> passed)
        {
            bool hasWheel = Object.FindFirstObjectByType<ItemWheelUI>(FindObjectsInactive.Include) != null;
            bool hasPrompt = Object.FindFirstObjectByType<PickupPromptUI>(FindObjectsInactive.Include) != null;

            if (!hasWheel) problems.Add("ไม่มี ItemWheelUI ในฉาก — กด Tab จะไม่มีวงล้อขึ้น (สั่ง Setup On Robots In Scene จะสร้างให้)");
            else passed.Add("มี ItemWheelUI ในฉาก");

            if (!hasPrompt) problems.Add("ไม่มี PickupPromptUI ในฉาก — ป้าย '[E] เก็บ ...' จะไม่ขึ้น");
            else passed.Add("มี PickupPromptUI ในฉาก");
        }

        private static void CheckNetworkManager(List<string> problems, List<string> passed)
        {
            NetworkManager manager = Object.FindFirstObjectByType<NetworkManager>(FindObjectsInactive.Include);

            if (manager == null)
            {
                problems.Add("ไม่มี NetworkManager ในฉาก — ระบบออนไลน์ทั้งหมดทำงานไม่ได้");
                return;
            }

            passed.Add("มี NetworkManager ในฉาก");

            List<string> unregistered = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:ItemDefinition"))
            {
                ItemDefinition item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (item == null || item.worldPrefab == null) continue;

                if (item.worldPrefab.GetComponent<NetworkObject>() == null)
                    problems.Add($"World Prefab ของ '{item.DisplayName}' ไม่มี NetworkObject — เก็บ/โยนออนไลน์ไม่ได้");
                else if (!IsPrefabRegistered(manager, item.worldPrefab))
                    unregistered.Add(item.DisplayName);
            }

            if (unregistered.Count > 0)
                problems.Add($"World Prefab ยังไม่ได้ลงทะเบียนใน NetworkManager ▸ NetworkPrefabs: {string.Join(", ", unregistered)} (จำเป็นตอนโยนของทิ้ง)");
        }

        /// <summary>
        /// Netcode เก็บรายชื่อ prefab ได้ 2 ที่ — ลิสต์ในตัว NetworkManager เอง กับไฟล์ NetworkPrefabsList
        /// แยกต่างหาก (เช่น DefaultNetworkPrefabs.asset) ต้องเช็คทั้งคู่
        /// ตอนอยู่ใน Editor ลิสต์รวม (Prefabs.Prefabs) ยังไม่ถูกประมวลผล เลยเชื่อที่เดียวไม่ได้
        /// </summary>
        private static bool IsPrefabRegistered(NetworkManager manager, GameObject prefab)
        {
            if (manager.NetworkConfig?.Prefabs == null) return false;

            foreach (var entry in manager.NetworkConfig.Prefabs.Prefabs)
            {
                if (entry?.Prefab == prefab) return true;
            }

            foreach (var list in manager.NetworkConfig.Prefabs.NetworkPrefabsLists)
            {
                if (list != null && list.Contains(prefab)) return true;
            }

            return false;
        }

        private static void CheckArms(List<string> problems, List<string> passed)
        {
            PlayerHandMovement[] arms = Object.FindObjectsByType<PlayerHandMovement>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (arms.Length == 0)
            {
                problems.Add("ไม่เจอแขน (PlayerHandMovement) ในฉากเลย");
                return;
            }

            int fullySetUp = 0;

            foreach (PlayerHandMovement arm in arms)
            {
                string name = arm.name;
                bool ok = true;

                if (arm.GetComponent<NetworkObject>() == null)
                {
                    problems.Add($"แขน '{name}' ไม่มี NetworkObject");
                    ok = false;
                }

                PlayerInventory inventory = arm.GetComponent<PlayerInventory>();
                HandItemHolder hand = arm.GetComponent<HandItemHolder>();
                ItemPickupInteractor interactor = arm.GetComponent<ItemPickupInteractor>();

                if (inventory == null) { problems.Add($"แขน '{name}' ไม่มี PlayerInventory"); ok = false; }
                if (hand == null) { problems.Add($"แขน '{name}' ไม่มี HandItemHolder"); ok = false; }
                if (interactor == null) { problems.Add($"แขน '{name}' ไม่มี ItemPickupInteractor"); ok = false; }

                if (inventory != null)
                {
                    SerializedObject so = new SerializedObject(inventory);
                    if (so.FindProperty("itemDatabase").objectReferenceValue == null)
                    {
                        problems.Add($"แขน '{name}' ▸ PlayerInventory ยังไม่ได้ผูก Item Database");
                        ok = false;
                    }
                    if (so.FindProperty("hand").objectReferenceValue == null)
                    {
                        problems.Add($"แขน '{name}' ▸ PlayerInventory ยังไม่ได้ผูก Hand");
                        ok = false;
                    }
                }

                if (hand != null)
                {
                    SerializedObject so = new SerializedObject(hand);
                    if (so.FindProperty("holdPoint").objectReferenceValue == null)
                    {
                        problems.Add($"แขน '{name}' ▸ HandItemHolder ยังไม่ได้ผูก Hold Point — ไอเทมจะไม่โผล่ในมือ");
                        ok = false;
                    }
                }

                if (ok) fullySetUp++;
            }

            if (fullySetUp > 0) passed.Add($"แขนที่ตั้งค่าครบ {fullySetUp}/{arms.Length} ข้าง");
        }

        /// <summary>
        /// หา PlayerInventory / ItemPickupInteractor ที่ไปแปะอยู่บน GameObject ที่ไม่ใช่แขน
        /// (เช่นลำตัว จากการตั้งค่าเวอร์ชันเก่า) — ตัวที่ค้างอยู่จะทำให้วงล้อผูกกับกระเป๋าผิดใบได้
        /// </summary>
        private static void CheckStrayComponents(List<string> problems, List<string> passed)
        {
            int strays = 0;

            foreach (PlayerInventory inventory in Object.FindObjectsByType<PlayerInventory>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (inventory.GetComponent<PlayerHandMovement>() == null)
                {
                    problems.Add($"'{inventory.name}' มี PlayerInventory แต่ไม่ใช่แขน — ของค้างจากการตั้งค่าเวอร์ชันเก่า ให้ลบ component นี้ทิ้ง");
                    strays++;
                }
            }

            foreach (ItemPickupInteractor interactor in Object.FindObjectsByType<ItemPickupInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (interactor.GetComponent<PlayerHandMovement>() == null)
                {
                    problems.Add($"'{interactor.name}' มี ItemPickupInteractor แต่ไม่ใช่แขน — ของค้างจากการตั้งค่าเวอร์ชันเก่า ให้ลบ component นี้ทิ้ง");
                    strays++;
                }
            }

            if (strays == 0) passed.Add("ไม่มี component ค้างอยู่ผิดที่");
        }

        private static void CheckWorldItems(List<string> problems, List<string> passed)
        {
            WorldItem[] items = Object.FindObjectsByType<WorldItem>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (items.Length == 0)
            {
                problems.Add("ไม่มีไอเทมวางอยู่ในฉากเลย — ไม่มีอะไรให้เก็บ (ลาก World Prefab มาวางในฉาก)");
                return;
            }

            int ok = 0;

            foreach (WorldItem item in items)
            {
                bool valid = true;

                if (item.Definition == null)
                {
                    problems.Add($"ไอเทม '{item.name}' ในฉากยังไม่ได้ใส่ Item Definition");
                    valid = false;
                }

                if (item.GetComponent<NetworkObject>() == null)
                {
                    problems.Add($"ไอเทม '{item.name}' ในฉากไม่มี NetworkObject — เก็บออนไลน์ไม่ได้");
                    valid = false;
                }

                // ไม่ต้องเช็ค Collider แล้ว — ตัวตรวจจับวนจากลิสต์ WorldItem.Active ตรงๆ ไม่ได้ใช้ physics
                // (Collider ยังมีประโยชน์ให้ของวางบนพื้นได้ แต่ไม่จำเป็นต่อการเก็บของอีกต่อไป)

                if (valid) ok++;
            }

            if (ok > 0) passed.Add($"ไอเทมในฉากที่พร้อมเก็บ {ok}/{items.Length} ชิ้น");
        }
    }
}
