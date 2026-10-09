using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ไล่แปะ AudioCategory ให้ AudioSource ทุกตัวในโปรเจกต์ แล้วเดาหมวดให้จากชื่อคลิป/ที่อยู่ไฟล์
///
/// เมนู: Tools ▸ NSC ▸ Audio ▸ Tag Audio Sources
///
/// ทำกับ prefab ทั้งโปรเจกต์ + ฉากที่เปิดอยู่ตอนนี้เท่านั้น
/// (ไม่ไล่เปิดทุกฉากให้เอง เพราะครึ่งหนึ่งเป็นฉากเดโมของ asset pack ที่ไม่ควรไปยุ่ง)
///
/// ตัวที่แปะไว้แล้วจะไม่ถูกเปลี่ยนหมวดซ้ำ — ปรับเองใน Inspector ได้ตามใจ
/// </summary>
public static class AudioCategoryTagger
{
    // เดาจากชื่อคลิปหรือชื่อ object — คำพวกนี้คือเสียงที่ดังยาวๆ เป็นบรรยากาศ
    private static readonly string[] AmbientHints =
    {
        "wind", "ambient", "portal", "tinnitus", "building", "power", "atmos", "rain", "loop"
    };

    private static readonly string[] UiHints =
    {
        "menu", "click", "button", "hover", "roar"
    };

    [MenuItem("Tools/NSC/Audio/Tag Audio Sources")]
    public static void TagAll()
    {
        StringBuilder log = new StringBuilder();
        int prefabCount = TagPrefabs(log);
        int sceneCount = TagOpenScenes(log);

        AssetDatabase.SaveAssets();

        // ไม่ใช้ DisplayDialog — กล่อง modal จะค้างรอคนกด ทำให้สั่งงานจากภายนอก (MCP) ค้างไปด้วย
        Debug.Log($"[AudioTag] แปะป้ายเสียงเสร็จแล้ว — prefab {prefabCount} ตัว, ในฉากที่เปิดอยู่ {sceneCount} ตัว " +
                  "(อย่าลืมเซฟฉากถ้ามีการแก้ในฉาก)\n" + log);
    }

    private static int TagPrefabs(StringBuilder log)
    {
        int tagged = 0;
        string[] guids = AssetDatabase.FindAssets("t:Prefab");

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            // ข้ามของในแพ็กเกจ/เดโมของ asset pack — ไม่ใช่เสียงของเกมเรา
            if (!path.StartsWith("Assets/")) continue;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            AudioSource[] sources = prefab.GetComponentsInChildren<AudioSource>(true);
            if (sources.Length == 0) continue;

            bool changed = false;
            foreach (AudioSource source in sources)
                changed |= Tag(source, path, log);

            if (!changed) continue;

            EditorUtility.SetDirty(prefab);
            PrefabUtility.SavePrefabAsset(prefab);
            tagged += sources.Length;
        }

        return tagged;
    }

    private static int TagOpenScenes(StringBuilder log)
    {
        int tagged = 0;

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;

            bool changed = false;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (AudioSource source in root.GetComponentsInChildren<AudioSource>(true))
                {
                    if (Tag(source, scene.name, log)) { changed = true; tagged++; }
                }
            }

            if (changed) EditorSceneManager.MarkSceneDirty(scene);
        }

        return tagged;
    }

    /// <returns>true = เพิ่งแปะให้ใหม่</returns>
    private static bool Tag(AudioSource source, string where, StringBuilder log)
    {
        if (source == null) return false;
        if (source.GetComponent<AudioCategory>() != null) return false; // แปะไว้แล้ว ไม่ยุ่ง

        AudioCategory category = Undo.AddComponent<AudioCategory>(source.gameObject);
        AudioBus bus = Guess(source);
        category.EditorSetBus(bus);

        log.AppendLine($"  [{bus}] {source.gameObject.name}  ({where})");
        return true;
    }

    private static AudioBus Guess(AudioSource source)
    {
        string clip = source.clip != null ? source.clip.name.ToLowerInvariant() : string.Empty;
        string objectName = source.gameObject.name.ToLowerInvariant();
        string clipPath = source.clip != null
            ? AssetDatabase.GetAssetPath(source.clip).ToLowerInvariant()
            : string.Empty;

        string haystack = clip + " " + objectName + " " + clipPath;

        foreach (string hint in UiHints)
            if (haystack.Contains(hint)) return AudioBus.Ui;

        foreach (string hint in AmbientHints)
            if (haystack.Contains(hint)) return AudioBus.Ambient;

        // เสียงที่วนซ้ำตลอดโดยไม่มีคำใบ้อื่น ก็นับเป็นบรรยากาศ
        if (source.loop && source.playOnAwake) return AudioBus.Ambient;

        return AudioBus.Sfx;
    }
}
