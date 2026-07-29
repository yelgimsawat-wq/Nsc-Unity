using UnityEngine;
using UnityEditor;

public class HoleClearer : EditorWindow
{
    [MenuItem("Tools/City Hole Clearer")]
    public static void ShowWindow()
    {
        GetWindow<HoleClearer>("Hole Clearer");
    }

    void OnGUI()
    {
        GUILayout.Label("เครื่องมือเจาะรูเคลียร์ตึกตามการสัมผัส", EditorStyles.boldLabel);
        GUILayout.Space(10);

        if (GUILayout.Button("กดเพื่อลบตึกที่สัมผัส/ซ้อนทับกับวัตถุที่เลือก"))
        {
            ClearTouchingBuildings();
        }
    }

    private void ClearTouchingBuildings()
    {
        GameObject selectedObj = Selection.activeGameObject;
        if (selectedObj == null)
        {
            EditorUtility.DisplayDialog("เตือน", "กรุณาคลิกเลือกโมเดลรูในฉากก่อนกดปุ่มนี้ครับ!", "ตกลง");
            return;
        }

        // ดึงค่าขอบเขต (Bounds) ของโมเดลรูที่เลือก
        MeshRenderer holeRenderer = selectedObj.GetComponent<MeshRenderer>();
        Collider holeCollider = selectedObj.GetComponent<Collider>();

        Bounds holeBounds;

        if (holeCollider != null)
        {
            holeBounds = holeCollider.bounds;
        }
        else if (holeRenderer != null)
        {
            holeBounds = holeRenderer.bounds;
        }
        else
        {
            EditorUtility.DisplayDialog("เตือน", "โมเดลรูที่เลือกต้องมี MeshRenderer หรือ Collider ถึงจะคำนวณการสัมผัสได้ครับ!", "ตกลง");
            return;
        }

        // ค้นหาตึกทั้งหมดในฉาก
        MeshRenderer[] allBuildings = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
        int count = 0;

        foreach (MeshRenderer building in allBuildings)
        {
            if (building.gameObject == selectedObj) continue;

            // ดึงขอบเขตของตึกแต่ละชิ้น
            Bounds buildingBounds = building.bounds;

            // ตรวจสอบว่าขอบเขตของโมเดลรู และ ตึก มีการอินเตอร์เซก (สัมผัส/ชนกัน) หรือไม่
            if (holeBounds.Intersects(buildingBounds))
            {
                Undo.RecordObject(building.gameObject, "Clear Intersecting Building");
                building.gameObject.SetActive(false);
                count++;
            }
        }

        EditorUtility.DisplayDialog("สำเร็จ!", $"เคลียร์ตึกที่สัมผัสโดนรูออกไปได้ทั้งหมด {count} ชิ้นแล้วครับ!", "เย้!");
    }
}
