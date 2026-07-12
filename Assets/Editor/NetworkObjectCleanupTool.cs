#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// เครื่องมือแก้ปัญหา StartHost ล้มเพราะ scene-placed NetworkObject มี GlobalObjectIdHash ซ้ำกัน
/// (เกิดจาก props แผนที่ เช่น Props_Street Light ถูกใส่ NetworkObject โดยไม่จำเป็น)
///
/// props ตกแต่งในแผนที่ไม่ควรเป็น NetworkObject — มันเป็นฉากประดับ static ไม่ต้อง sync ข้ามเครือข่าย
/// tool นี้ถอด NetworkObject/NetworkTransform/NetworkRigidbody ออกจากทุกอย่างใน Scene ที่เปิดอยู่
/// "ยกเว้น" ตัวที่จำเป็นจริง (หุ่น + ตัวจัดการเครือข่าย) — เก็บได้ด้วย whitelist ตามคอมโพเนนต์
///
/// Undo ได้ (Ctrl+Z) และไม่บันทึกให้อัตโนมัติ — ตรวจผลก่อนแล้วค่อย Ctrl+S เอง
/// </summary>
public static class NetworkObjectCleanupTool
{
    // เก็บ NetworkObject ไว้ถ้าตัวมันเอง หรือ "ตัวเอง/ลูก/หลาน" มีสคริปต์เหล่านี้อยู่
    // (หุ่น = PlayerHandMovement/PlayerFootForRobot/TorsoMovement | ล็อบบี้ = LobbyManager | เมนู = OnlineNetworkUI)
    private static readonly string[] KeepIfHasComponent =
    {
        "PlayerHandMovement",
        "PlayerFootForRobot",
        "TorsoMovement",
        "LobbyManager",
        "OnlineNetworkUI",
        "PlayerCam",
    };

    [MenuItem("Tools/Netcode/ตรวจหา NetworkObject ที่ hash ซ้ำ (ไม่แก้อะไร)")]
    public static void ScanDuplicates()
    {
        var all = CollectSceneNetworkObjects();
        var byHash = new Dictionary<uint, List<NetworkObject>>();
        foreach (var no in all)
        {
            uint h = GetHash(no);
            if (!byHash.TryGetValue(h, out var list)) { list = new List<NetworkObject>(); byHash[h] = list; }
            list.Add(no);
        }

        var sb = new StringBuilder();
        int dupGroups = 0, dupTotal = 0;
        foreach (var kv in byHash)
        {
            if (kv.Value.Count <= 1) continue;
            dupGroups++;
            dupTotal += kv.Value.Count;
            sb.AppendLine($"hash {kv.Key} ซ้ำ {kv.Value.Count} ตัว: {string.Join(", ", kv.Value.Take(4).Select(n => n.name))}{(kv.Value.Count > 4 ? " ..." : "")}");
        }

        if (dupGroups == 0)
            Debug.Log($"[NetCleanup] ✅ ไม่พบ hash ซ้ำ — NetworkObject ในฉากทั้งหมด {all.Count} ตัวมี id ไม่ชนกัน");
        else
            Debug.LogWarning($"[NetCleanup] ⚠️ พบ hash ซ้ำ {dupGroups} กลุ่ม ({dupTotal} ตัว) — นี่คือสาเหตุ StartHost ล้ม\n{sb}");
    }

    [MenuItem("Tools/Netcode/ถอด NetworkObject ออกจาก Props แผนที่ (เก็บเฉพาะหุ่น+ล็อบบี้)")]
    public static void StripFromMapProps()
    {
        var all = CollectSceneNetworkObjects();
        var toStrip = all.Where(no => !ShouldKeep(no)).ToList();

        if (toStrip.Count == 0)
        {
            EditorUtility.DisplayDialog("Netcode Cleanup",
                "ไม่มี NetworkObject ที่ต้องถอด — props แผนที่สะอาดแล้ว", "OK");
            return;
        }

        bool ok = EditorUtility.DisplayDialog("Netcode Cleanup",
            $"จะถอด NetworkObject (+ NetworkTransform/NetworkRigidbody) ออกจาก {toStrip.Count} object\n" +
            $"เก็บไว้ {all.Count - toStrip.Count} ตัว (หุ่น + ล็อบบี้)\n\n" +
            "Undo ได้ด้วย Ctrl+Z | ยังไม่เซฟให้ ตรวจแล้วค่อย Ctrl+S เอง\n\nดำเนินการ?",
            "ถอดเลย", "ยกเลิก");
        if (!ok) return;

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Strip NetworkObjects from map props");
        int group = Undo.GetCurrentGroup();

        int removed = 0;
        foreach (var no in toStrip)
        {
            GameObject go = no.gameObject;

            // ถอด NetworkBehaviour ที่เกาะ NetworkObject ก่อน (NetworkTransform/NetworkRigidbody)
            foreach (var nt in go.GetComponents<NetworkTransform>())
                Undo.DestroyObjectImmediate(nt);
            foreach (var nr in go.GetComponents<NetworkRigidbody>())
                Undo.DestroyObjectImmediate(nr);

            Undo.DestroyObjectImmediate(no);
            removed++;
        }

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        Debug.Log($"[NetCleanup] ✅ ถอด NetworkObject ออก {removed} ตัว — เหลือเฉพาะหุ่น+ล็อบบี้ ({all.Count - removed} ตัว)\n" +
                  "กด Ctrl+S เพื่อบันทึก Scene แล้วลอง Play ใหม่");
    }

    // ── helpers ──────────────────────────────────────────────

    private static bool ShouldKeep(NetworkObject no)
    {
        // เก็บถ้าตัวเอง หรือ parent chain หรือ children มีสคริปต์ whitelist
        Transform root = no.transform;
        // ไต่ขึ้นหา root ของ prefab หุ่น (RobotContainer) เผื่อ NetworkObject อยู่บนชิ้นส่วนลูก
        foreach (var comp in no.GetComponentsInParent<MonoBehaviour>(true))
            if (comp != null && IsWhitelisted(comp)) return true;
        foreach (var comp in no.GetComponentsInChildren<MonoBehaviour>(true))
            if (comp != null && IsWhitelisted(comp)) return true;
        return false;
    }

    private static bool IsWhitelisted(MonoBehaviour comp)
    {
        string typeName = comp.GetType().Name;
        for (int i = 0; i < KeepIfHasComponent.Length; i++)
            if (typeName == KeepIfHasComponent[i]) return true;
        return false;
    }

    private static List<NetworkObject> CollectSceneNetworkObjects()
    {
        // เฉพาะ object ที่อยู่ในฉาก (ไม่ใช่ prefab asset)
        return Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(no => no.gameObject.scene.IsValid())
            .ToList();
    }

    private static uint GetHash(NetworkObject no)
    {
        // GlobalObjectIdHash เป็น field ที่ serialize ไว้ อ่านผ่าน SerializedObject กันเวอร์ชัน API ต่างกัน
        var so = new SerializedObject(no);
        var prop = so.FindProperty("GlobalObjectIdHash");
        return prop != null ? (uint)prop.longValue : 0u;
    }
}
#endif
