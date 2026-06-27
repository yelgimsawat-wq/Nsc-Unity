using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// Unity Editor Tool: Replace Selected Objects
/// แทนที่ GameObject ที่เลือกด้วย Prefab ที่กำหนด โดยคงตำแหน่ง องศา และค่าทุกอย่างใน Inspector (Component values) เดิมไว้
/// </summary>
public class ReplaceObjectTool : EditorWindow
{
    // ── Settings ──────────────────────────────────────────────
    private GameObject replacementPrefab;
    private bool keepPosition    = true;
    private bool keepRotation    = true;
    private bool keepScale       = false;
    private bool keepName        = false;
    private bool keepParent      = true;
    private bool selectNewObjects = true;
    private bool keepComponents   = true;   // คงค่าทุก Component (Inspector values) จาก object เดิม

    // ── UI State ──────────────────────────────────────────────
    private Vector2 scrollPos;
    private List<GameObject> selectedObjects = new List<GameObject>();
    private GUIStyle headerStyle;
    private GUIStyle sectionStyle;
    private GUIStyle countStyle;
    private bool stylesInitialized = false;

    // ── Menu Item ─────────────────────────────────────────────
    [MenuItem("Tools/Replace Object Tool  %#R")]   // Ctrl+Shift+R
    public static void ShowWindow()
    {
        var window = GetWindow<ReplaceObjectTool>("Replace Objects");
        window.minSize = new Vector2(320, 480);
        window.Show();
    }

    // ── Lifecycle ─────────────────────────────────────────────
    private void OnEnable()
    {
        Selection.selectionChanged += OnSelectionChanged;
        OnSelectionChanged();
    }

    private void OnDisable()
    {
        Selection.selectionChanged -= OnSelectionChanged;
    }

    private void OnSelectionChanged()
    {
        selectedObjects.Clear();
        foreach (var obj in Selection.gameObjects)
            selectedObjects.Add(obj);
        Repaint();
    }

    // ── Styles ────────────────────────────────────────────────
    private void InitStyles()
    {
        if (stylesInitialized) return;

        headerStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize  = 14,
            alignment = TextAnchor.MiddleCenter
        };

        sectionStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 11
        };

        countStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
        {
            fontSize = 10
        };

        stylesInitialized = true;
    }

    // ── GUI ───────────────────────────────────────────────────
    private void OnGUI()
    {
        InitStyles();
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        DrawHeader();
        GUILayout.Space(8);

        DrawPrefabSection();
        GUILayout.Space(8);

        DrawOptionsSection();
        GUILayout.Space(8);

        DrawSelectionSection();
        GUILayout.Space(12);

        DrawReplaceButton();
        GUILayout.Space(4);

        DrawHelpBox();

        EditorGUILayout.EndScrollView();
    }

    // ── Header ────────────────────────────────────────────────
    private void DrawHeader()
    {
        var rect = GUILayoutUtility.GetRect(0, 48, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f, 1f));
        GUI.Label(rect, "🔄  Replace Object Tool", headerStyle);
    }

    // ── Prefab Section ────────────────────────────────────────
    private void DrawPrefabSection()
    {
        GUILayout.Label("Target Prefab", sectionStyle);
        DrawSeparator();

        EditorGUI.BeginChangeCheck();
        replacementPrefab = (GameObject)EditorGUILayout.ObjectField(
            "Replace With",
            replacementPrefab,
            typeof(GameObject),
            false   // allowSceneObjects = false  → Prefab only
        );

        if (replacementPrefab != null && !IsPrefab(replacementPrefab))
        {
            EditorGUILayout.HelpBox(
                "โปรดใช้ Prefab เท่านั้น (ไม่ใช่ Scene Object)",
                MessageType.Warning);
        }
    }

    // ── Options Section ───────────────────────────────────────
    private void DrawOptionsSection()
    {
        GUILayout.Label("Options", sectionStyle);
        DrawSeparator();

        keepPosition  = EditorGUILayout.Toggle(new GUIContent("Keep Position",  "คงตำแหน่ง (Position) เดิม"),  keepPosition);
        keepRotation  = EditorGUILayout.Toggle(new GUIContent("Keep Rotation",  "คงการหมุน (Rotation) เดิม"),  keepRotation);
        keepScale     = EditorGUILayout.Toggle(new GUIContent("Keep Scale",     "คงขนาด (Scale) เดิม"),          keepScale);
        keepName      = EditorGUILayout.Toggle(new GUIContent("Keep Name",      "คงชื่อ Object เดิม"),           keepName);
        keepParent    = EditorGUILayout.Toggle(new GUIContent("Keep Parent",    "คงลำดับ Parent เดิม"),          keepParent);
        selectNewObjects = EditorGUILayout.Toggle(new GUIContent("Select New Objects", "เลือก Object ใหม่หลังแทนที่"), selectNewObjects);

        GUILayout.Space(4);
        keepComponents = EditorGUILayout.Toggle(
            new GUIContent("Keep Component Values",
                "คงค่าทุกอย่างที่ตั้งไว้ใน Inspector ของ Component ทุกตัว (เช่น Script, Collider, Rigidbody ฯลฯ) จาก object เดิม โดยเปลี่ยนแค่โมเดล/Prefab ภายนอก"),
            keepComponents);

        if (keepComponents)
        {
            EditorGUILayout.HelpBox(
                "จะคัดลอกค่า Component ทุกตัว (รวม Script ที่เขียนเอง) จาก object เดิมไปยัง object ใหม่\n" +
                "ถ้า Component ชนิดเดียวกันมีอยู่ใน Prefab ใหม่แล้ว จะ 'เขียนทับค่า' ด้วยค่าจาก object เดิม\n" +
                "ถ้าไม่มี จะ 'เพิ่ม Component' นั้นเข้าไปพร้อมค่าเดิมทั้งหมด\n\n" +
                "ยกเว้น: MeshFilter, MeshRenderer, SkinnedMeshRenderer, MeshCollider, SpriteRenderer, LODGroup\n" +
                "(จะไม่ถูกคัดลอกทับ เพื่อให้โมเดลใหม่แสดง mesh/material ของตัวเอง ไม่ล่องหน)",
                MessageType.Info);
        }
    }

    // ── Selection Section ─────────────────────────────────────
    private void DrawSelectionSection()
    {
        GUILayout.Label($"Selected Objects  ({selectedObjects.Count})", sectionStyle);
        DrawSeparator();

        if (selectedObjects.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "ยังไม่ได้เลือก GameObject ใดๆ\nกรุณาเลือกใน Scene หรือ Hierarchy",
                MessageType.Info);
            return;
        }

        // Show up to 8 items
        int shown = Mathf.Min(selectedObjects.Count, 8);
        for (int i = 0; i < shown; i++)
        {
            var obj = selectedObjects[i];
            using (new EditorGUILayout.HorizontalScope())
            {
                var icon = EditorGUIUtility.ObjectContent(obj, typeof(GameObject)).image;
                GUILayout.Label(new GUIContent(icon), GUILayout.Width(20), GUILayout.Height(18));
                GUILayout.Label(obj != null ? obj.name : "(null)", EditorStyles.label);
                GUILayout.FlexibleSpace();

                // Ping button
                if (GUILayout.Button("⌖", GUILayout.Width(24)))
                    EditorGUIUtility.PingObject(obj);
            }
        }

        if (selectedObjects.Count > 8)
            GUILayout.Label($"... และอีก {selectedObjects.Count - 8} รายการ", countStyle);
    }

    // ── Replace Button ────────────────────────────────────────
    private void DrawReplaceButton()
    {
        bool canReplace = replacementPrefab != null
                       && IsPrefab(replacementPrefab)
                       && selectedObjects.Count > 0;

        GUI.enabled = canReplace;

        var btnColor = GUI.backgroundColor;
        GUI.backgroundColor = canReplace ? new Color(0.3f, 0.8f, 0.4f) : Color.grey;

        if (GUILayout.Button($"Replace  {selectedObjects.Count}  Object(s)", GUILayout.Height(38)))
            ReplaceSelected();

        GUI.backgroundColor = btnColor;
        GUI.enabled = true;
    }

    // ── Help Box ──────────────────────────────────────────────
    private void DrawHelpBox()
    {
        EditorGUILayout.HelpBox(
            "วิธีใช้:\n" +
            "1. เลือก GameObject ใน Scene/Hierarchy\n" +
            "2. ลาก Prefab มาใส่ช่อง 'Replace With'\n" +
            "3. กดปุ่ม Replace\n\n" +
            "Shortcut: Ctrl+Shift+R",
            MessageType.None);
    }

    // ── Core Logic ────────────────────────────────────────────
    private void ReplaceSelected()
    {
        if (replacementPrefab == null || selectedObjects.Count == 0) return;

        // Register undo group for all operations
        Undo.SetCurrentGroupName("Replace Objects");
        int undoGroup = Undo.GetCurrentGroup();

        var newObjects = new List<GameObject>();

        foreach (var original in selectedObjects)
        {
            if (original == null) continue;

            // ── Snapshot transform ─────────────────────────────
            Vector3    origPos   = original.transform.position;
            Quaternion origRot   = original.transform.rotation;
            Vector3    origScale = original.transform.localScale;
            string     origName  = original.name;
            Transform  origParent = original.transform.parent;
            int        siblingIndex = original.transform.GetSiblingIndex();

            // ── Snapshot all components (เก็บไว้ก่อนลบ original) ──
            Component[] origComponents = keepComponents
                ? original.GetComponents<Component>()
                : null;

            // ── Instantiate replacement ────────────────────────
            GameObject newObj = (GameObject)PrefabUtility.InstantiatePrefab(replacementPrefab);
            Undo.RegisterCreatedObjectUndo(newObj, "Instantiate Replacement");

            // ── Apply transform ────────────────────────────────
            if (keepPosition) newObj.transform.position = origPos;
            if (keepRotation) newObj.transform.rotation = origRot;
            if (keepScale)    newObj.transform.localScale = origScale;
            if (keepName)     newObj.name = origName;

            // ── Set parent & sibling order ─────────────────────
            if (keepParent && origParent != null)
            {
                newObj.transform.SetParent(origParent, true);
                newObj.transform.SetSiblingIndex(siblingIndex);
            }
            else if (keepParent && origParent == null)
            {
                // Root object — keep at same sibling index in root
                newObj.transform.SetAsLastSibling();
            }

            // ── Copy ค่า component ทั้งหมดจาก object เดิม ──────
            if (keepComponents && origComponents != null)
                CopyAllComponents(origComponents, newObj);

            newObjects.Add(newObj);

            // ── Delete original ────────────────────────────────
            Undo.DestroyObjectImmediate(original);
        }

        // ── Select new objects ─────────────────────────────────
        if (selectNewObjects && newObjects.Count > 0)
            Selection.objects = newObjects.ToArray();

        Undo.CollapseUndoOperations(undoGroup);

        Debug.Log($"[Replace Object Tool] แทนที่ {newObjects.Count} object(s) ด้วย '{replacementPrefab.name}' สำเร็จ (คงค่า Component: {keepComponents})");
    }

    // ── Component Copy Helper ─────────────────────────────────
    /// <summary>
    /// คัดลอกค่าทุก field/property (Inspector values) ของแต่ละ Component จาก object เดิม
    /// ไปยัง object ใหม่ — ถ้ามี Component ชนิดเดียวกันอยู่แล้วจะเขียนทับค่า
    /// ถ้าไม่มีจะเพิ่ม Component นั้นเข้าไปก่อนแล้วค่อยเขียนค่า
    /// (Transform ถูกข้ามเพราะถูกจัดการแยกแล้วในขั้นตอนก่อนหน้า)
    /// </summary>
    // Component ที่เกี่ยวกับ "รูปลักษณ์/โมเดล" โดยตรง — ไม่ควรคัดลอกค่าทับ
    // เพราะจะทำให้ mesh/material ของ prefab ใหม่ถูกเขียนทับด้วยของโมเดลเก่า (โมเดลล่องหน/ผิดรูป)
    private static readonly System.Type[] ExcludedFromCopy = new System.Type[]
    {
        typeof(MeshFilter),
        typeof(MeshRenderer),
        typeof(SkinnedMeshRenderer),
        typeof(MeshCollider),      // shape ของ collider นี้ผูกกับ mesh ของโมเดลโดยตรง
        typeof(SpriteRenderer),
        typeof(LODGroup),
    };

    private static void CopyAllComponents(Component[] sourceComponents, GameObject target)
    {
        // ใช้ track ว่า component ชนิดไหนถูกใช้ไปแล้วบ้าง (กรณีมีหลายตัวชนิดเดียวกัน เช่น หลาย AudioSource)
        var usedTargets = new HashSet<Component>();

        foreach (var src in sourceComponents)
        {
            if (src == null) continue;

            System.Type type = src.GetType();

            // ข้าม Transform/RectTransform เพราะคุมตำแหน่ง/scale ไปแล้วก่อนหน้านี้
            if (type == typeof(Transform) || type == typeof(RectTransform))
                continue;

            // ข้าม component ที่เกี่ยวกับโมเดล/รูปลักษณ์ ปล่อยให้ prefab ใหม่ใช้ของตัวเอง
            if (System.Array.IndexOf(ExcludedFromCopy, type) >= 0)
                continue;

            // หา component ชนิดเดียวกันบน target ที่ยังไม่ถูกใช้
            Component dst = null;
            foreach (var candidate in target.GetComponents(type))
            {
                if (!usedTargets.Contains(candidate))
                {
                    dst = candidate;
                    break;
                }
            }

            // ถ้าไม่มี ให้เพิ่ม component ชนิดนี้เข้าไปใหม่
            if (dst == null)
            {
                dst = Undo.AddComponent(target, type);
            }

            if (dst == null) continue; // เผื่อบาง component เพิ่มไม่ได้ (เช่น ต้องพึ่ง component อื่นที่ขาด)

            // คัดลอกค่าทุก field ทับจาก source ไปยัง destination
            EditorUtility.CopySerialized(src, dst);

            usedTargets.Add(dst);
        }
    }

    // ── Helpers ───────────────────────────────────────────────
    private static bool IsPrefab(GameObject obj)
    {
        return PrefabUtility.GetPrefabAssetType(obj) != PrefabAssetType.NotAPrefab;
    }

    private static void DrawSeparator()
    {
        var rect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, new Color(0.4f, 0.4f, 0.4f, 0.5f));
        GUILayout.Space(4);
    }
}