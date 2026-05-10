using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// Unity Editor Tool: Replace Selected Objects
/// แทนที่ GameObject ที่เลือกด้วย Prefab ที่กำหนด โดยคงตำแหน่งและองศาเดิมไว้
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

            newObjects.Add(newObj);

            // ── Delete original ────────────────────────────────
            Undo.DestroyObjectImmediate(original);
        }

        // ── Select new objects ─────────────────────────────────
        if (selectNewObjects && newObjects.Count > 0)
            Selection.objects = newObjects.ToArray();

        Undo.CollapseUndoOperations(undoGroup);

        Debug.Log($"[Replace Object Tool] แทนที่ {newObjects.Count} object(s) ด้วย '{replacementPrefab.name}' สำเร็จ");
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