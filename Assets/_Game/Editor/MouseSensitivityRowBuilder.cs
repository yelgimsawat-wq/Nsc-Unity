using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// เพิ่มแถว "MOUSE SENSITIVITY" ลงในแท็บ Gameplay ของแผงตั้งค่า แล้วต่อสายให้ SettingsManager อัตโนมัติ
///
/// เมนู: Tools ▸ NSC ▸ UI ▸ Add Mouse Sensitivity Row
///
/// ทำไมต้อง "ก๊อปแถวเดิม" ไม่สร้างใหม่:
///   แผงตั้งค่าในฉากเมนูทำด้วยมือตามดีไซน์ ไม่ได้เจนจาก SettingsUIBuilder (ตัวนั้นล้าสมัยไปแล้ว)
///   ถ้าสร้างแถวใหม่เองจะได้กล่องเทาๆ หน้าตาไม่เข้าพวก — ก๊อปแถว MASTER VOLUME จากแท็บ Audio
///   มาแก้ข้อความแทน จะได้สไปรท์/สี/ขนาดเหมือนของเดิมเป๊ะโดยไม่ต้องรู้ว่าดีไซน์ใช้อะไรบ้าง
///
/// สั่งซ้ำได้ — ถ้ามีแถวเดิมอยู่แล้วจะลบทิ้งแล้วสร้างใหม่
/// </summary>
public static class MouseSensitivityRowBuilder
{
    private const string RowName = "MouseSensitivity_Row";

    [MenuItem("Tools/NSC/UI/Add Mouse Sensitivity Row")]
    public static void Build()
    {
        SettingsManager manager = Object.FindFirstObjectByType<SettingsManager>(FindObjectsInactive.Include);
        if (manager == null)
        {
            EditorUtility.DisplayDialog("หา SettingsManager ไม่เจอ",
                "เปิดฉากเมนูหลัก (-MenuNOk) ที่มีแผงตั้งค่าก่อน แล้วค่อยสั่งคำสั่งนี้อีกที", "โอเค");
            return;
        }

        SerializedObject so = new SerializedObject(manager);

        GameObject gameplayPanel = so.FindProperty("gameplaySubPanel").objectReferenceValue as GameObject;
        GameObject audioPanel = so.FindProperty("audioSubPanel").objectReferenceValue as GameObject;

        if (gameplayPanel == null)
        {
            EditorUtility.DisplayDialog("ยังไม่ได้ต่อ Gameplay Sub-Panel",
                "ช่อง Gameplay Sub Panel ใน SettingsManager ว่างอยู่ — ลากแผงของแท็บ GAMEPLAY มาใส่ก่อน", "โอเค");
            return;
        }

        GameObject template = FindSliderRow(so, audioPanel);
        if (template == null)
        {
            EditorUtility.DisplayDialog("หาแถวต้นแบบไม่เจอ",
                "ต้องมีแถวที่เป็นสไลเดอร์ในแท็บ AUDIO (เช่น MASTER VOLUME) ไว้ใช้เป็นต้นแบบ", "โอเค");
            return;
        }

        // สั่งซ้ำได้ — ของเดิมทิ้งไปก่อน กันได้สองแถวซ้อนกัน
        Transform existing = gameplayPanel.transform.Find(RowName);
        if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

        GameObject row = Object.Instantiate(template, gameplayPanel.transform);
        row.name = RowName;
        row.SetActive(true);
        row.transform.SetAsLastSibling();
        Undo.RegisterCreatedObjectUndo(row, "Add Mouse Sensitivity Row");

        Slider slider = row.GetComponentInChildren<Slider>(true);
        if (slider == null)
        {
            Undo.DestroyObjectImmediate(row);
            EditorUtility.DisplayDialog("แถวต้นแบบไม่มีสไลเดอร์", "ลองเลือกแถวอื่นเป็นต้นแบบ", "โอเค");
            return;
        }

        // สไลเดอร์ที่ก๊อปมาอาจมี listener ที่ผูกไว้ใน Inspector ของค่าเสียง — ลากแล้วเสียงจะเพี้ยนตาม
        for (int i = slider.onValueChanged.GetPersistentEventCount() - 1; i >= 0; i--)
            UnityEditor.Events.UnityEventTools.RemovePersistentListener(slider.onValueChanged, i);

        slider.minValue = MouseSettings.Min;
        slider.maxValue = MouseSettings.Max;
        slider.wholeNumbers = false;
        slider.value = MouseSettings.Default;

        FindLabels(row, slider, out TextMeshProUGUI caption, out TextMeshProUGUI valueLabel);

        if (caption != null) caption.text = "MOUSE SENSITIVITY";
        if (valueLabel != null) valueLabel.text = MouseSettings.Format(MouseSettings.Default);

        so.FindProperty("mouseSensitivitySlider").objectReferenceValue = slider;
        so.FindProperty("mouseSensitivityValueLabel").objectReferenceValue = valueLabel;
        so.ApplyModifiedProperties();

        EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        Selection.activeGameObject = row;

        Debug.Log($"[Settings] เพิ่มแถว MOUSE SENSITIVITY ในแท็บ Gameplay แล้ว " +
                  $"(ก๊อปหน้าตาจาก '{template.name}') — อย่าลืม Save ฉาก");
    }

    /// <summary>หาแถวที่ "เป็นลูกโดยตรงของแผง Audio" และข้างในมีสไลเดอร์ ใช้เป็นต้นแบบ</summary>
    private static GameObject FindSliderRow(SerializedObject so, GameObject audioPanel)
    {
        // ทางที่ชัวร์สุด: ไต่ขึ้นจากสไลเดอร์ Master Volume ที่ต่อสายไว้แล้ว
        Slider master = so.FindProperty("masterVolumeSlider").objectReferenceValue as Slider;
        if (master != null && audioPanel != null)
        {
            Transform t = master.transform;
            while (t != null && t.parent != audioPanel.transform) t = t.parent;
            if (t != null) return t.gameObject;
        }

        if (audioPanel == null) return null;

        foreach (Transform child in audioPanel.transform)
            if (child.GetComponentInChildren<Slider>(true) != null) return child.gameObject;

        return null;
    }

    /// <summary>
    /// ในหนึ่งแถวมีข้อความสองตัว: ชื่อหัวข้อ (ซ้าย) กับค่าตัวเลข (ขวา)
    /// ตัวที่อยู่ใต้สไลเดอร์ไม่นับ (บางดีไซน์เอา TMP ไปแปะบนหัวสไลเดอร์)
    /// </summary>
    private static void FindLabels(GameObject row, Slider slider,
        out TextMeshProUGUI caption, out TextMeshProUGUI valueLabel)
    {
        caption = null;
        valueLabel = null;

        foreach (TextMeshProUGUI text in row.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (text.transform.IsChildOf(slider.transform)) continue;

            if (caption == null) caption = text;
            else valueLabel = text;
        }
    }
}
