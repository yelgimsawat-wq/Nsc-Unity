using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// สร้าง Player HUD prefab ทั้งชุดด้วยการกดเมนูครั้งเดียว: Tools/NSC/Build Player HUD
/// กดซ้ำได้เสมอ — จะลบ HUD เก่าใน scene แล้ว regenerate ใหม่ทับ prefab เดิม
/// (สเต็ปถัดไปจะขยาย builder นี้: Punch Force → Status Prompt → Limb Status Cards)
/// </summary>
public static class PlayerHudBuilder
{
    private const string PrefabFolder = "Assets/Yelmee/PlayerUI";
    private const string PrefabPath = PrefabFolder + "/PlayerHUD.prefab";

    // โทนสีตามภาพ mockup
    private static readonly Color PanelDark = new Color(0.04f, 0.07f, 0.11f, 0.55f);
    private static readonly Color PanelDarkSolid = new Color(0.05f, 0.09f, 0.13f, 0.9f);
    private static readonly Color HullCyan = new Color(0.35f, 0.85f, 1f, 1f);
    private static readonly Color LabelGrey = new Color(0.75f, 0.82f, 0.88f, 0.85f);
    private static readonly Color PunchAmber = new Color(1f, 0.72f, 0.2f, 1f);
    private static readonly Color SegmentEmpty = new Color(1f, 1f, 1f, 0.16f);

    [MenuItem("Tools/NSC/Build Player HUD")]
    public static void Build()
    {
        // ลบ HUD เก่าใน scene ก่อน regenerate (builder เป็นเจ้าของ layout ทั้งหมด)
        LocalRobotBinder[] oldHuds = Object.FindObjectsByType<LocalRobotBinder>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (LocalRobotBinder old in oldHuds)
            Object.DestroyImmediate(old.gameObject);

        GameObject root = new GameObject("PlayerHUD", typeof(RectTransform));

        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        LocalRobotBinder binder = root.AddComponent<LocalRobotBinder>();

        // ปุ่มทดสอบบังคับชิ้นหลุด (1-4) — ปิด enableDebugKeys ก่อนปล่อยเกมจริง
        DebugLimbBreaker debugBreaker = root.AddComponent<DebugLimbBreaker>();
        SerializedObject debugSo = new SerializedObject(debugBreaker);
        debugSo.FindProperty("binder").objectReferenceValue = binder;
        debugSo.ApplyModifiedPropertiesWithoutUndo();

        BuildHullRing(root.transform, binder);
        BuildPunchForce(root.transform, binder);
        BuildStatusPrompt(root.transform, binder);
        BuildLimbStatusPanel(root.transform, binder);

        if (!AssetDatabase.IsValidFolder(PrefabFolder))
            AssetDatabase.CreateFolder("Assets/Yelmee", "PlayerUI");

        PrefabUtility.SaveAsPrefabAssetAndConnect(root, PrefabPath, InteractionMode.UserAction);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;

        Debug.Log($"[PlayerHUD] built and saved to {PrefabPath} — instance placed in the open scene.");
    }

    // ================================================================
    //  สเต็ป 1: วงแหวน HULL ซ้ายล่าง
    // ================================================================

    private static void BuildHullRing(Transform canvasRoot, LocalRobotBinder binder)
    {
        RectTransform ring = CreateRect(canvasRoot, "HullRing");
        ring.anchorMin = Vector2.zero;
        ring.anchorMax = Vector2.zero;
        ring.pivot = Vector2.zero;
        ring.anchoredPosition = new Vector2(60f, 50f);
        ring.sizeDelta = new Vector2(210f, 210f);

        Sprite knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

        // จานหลังมืดโปร่งแสง
        Image baseDisc = CreateImage(ring, "BaseDisc", knob, PanelDark);
        Stretch(baseDisc.rectTransform);

        // วงเติมเลือด (radial fill จากด้านบน ตามเข็ม)
        Image fill = CreateImage(ring, "RingFill", knob, HullCyan);
        Stretch(fill.rectTransform);
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Radial360;
        fill.fillOrigin = (int)Image.Origin360.Top;
        fill.fillClockwise = true;
        fill.fillAmount = 1f;

        // จานในทึบ — บังตรงกลางให้ fill กลายเป็น "วงแหวน"
        Image innerDisc = CreateImage(ring, "InnerDisc", knob, PanelDarkSolid);
        Center(innerDisc.rectTransform, new Vector2(162f, 162f));

        TMP_FontAsset font = FindHudFont();

        // ป้ายบน = ชื่อชิ้นที่เลือดกำลังโชว์ (HullRingUI อัปเดตให้ตอนรัน)
        TextMeshProUGUI limbLabel = CreateText(ring, "LimbLabel", "L-ARM", font, 20f, LabelGrey);
        Center(limbLabel.rectTransform, new Vector2(140f, 26f));
        limbLabel.rectTransform.anchoredPosition = new Vector2(0f, 52f);
        limbLabel.characterSpacing = 6f;

        TextMeshProUGUI value = CreateText(ring, "ValueText", "500", font, 58f, Color.white);
        Center(value.rectTransform, new Vector2(150f, 70f));
        value.fontStyle = FontStyles.Bold;

        TextMeshProUGUI bottomLabel = CreateText(ring, "BottomLabel", "HP", font, 20f, LabelGrey);
        Center(bottomLabel.rectTransform, new Vector2(120f, 26f));
        bottomLabel.rectTransform.anchoredPosition = new Vector2(0f, -52f);
        bottomLabel.characterSpacing = 8f;

        HullRingUI hullUI = ring.gameObject.AddComponent<HullRingUI>();
        SerializedObject so = new SerializedObject(hullUI);
        so.FindProperty("binder").objectReferenceValue = binder;
        so.FindProperty("fillImage").objectReferenceValue = fill;
        so.FindProperty("valueText").objectReferenceValue = value;
        so.FindProperty("limbLabel").objectReferenceValue = limbLabel;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ================================================================
    //  สเต็ป 2: หลอดพลังหมัด (แสดงเฉพาะมือที่เราคุม — PunchForceUI เป็นคนซ่อน/โชว์)
    // ================================================================

    private const int PunchSegmentCount = 8;

    private static void BuildPunchForce(Transform canvasRoot, LocalRobotBinder binder)
    {
        RectTransform group = CreateRect(canvasRoot, "PunchForce");
        group.anchorMin = Vector2.zero;
        group.anchorMax = Vector2.zero;
        group.pivot = Vector2.zero;
        group.anchoredPosition = new Vector2(310f, 95f);
        group.sizeDelta = new Vector2(360f, 70f);

        // กลุ่ม visual แยกไว้ให้ PunchForceUI ปิดทั้งก้อนเมื่อผู้เล่นคุมขา
        RectTransform content = CreateRect(group, "Content");
        Stretch(content);

        TMP_FontAsset font = FindHudFont();

        TextMeshProUGUI label = CreateText(content, "Label", "PUNCH FORCE", font, 22f, LabelGrey);
        label.rectTransform.anchorMin = new Vector2(0f, 1f);
        label.rectTransform.anchorMax = new Vector2(0f, 1f);
        label.rectTransform.pivot = new Vector2(0f, 1f);
        label.rectTransform.anchoredPosition = Vector2.zero;
        label.rectTransform.sizeDelta = new Vector2(300f, 26f);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.characterSpacing = 4f;

        Sprite uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

        Image[] segments = new Image[PunchSegmentCount];
        for (int i = 0; i < PunchSegmentCount; i++)
        {
            Image segment = CreateImage(content, $"Segment{i}", uiSprite, SegmentEmpty);
            segment.type = Image.Type.Sliced;

            RectTransform segmentRect = segment.rectTransform;
            segmentRect.anchorMin = Vector2.zero;
            segmentRect.anchorMax = Vector2.zero;
            segmentRect.pivot = Vector2.zero;
            segmentRect.anchoredPosition = new Vector2(i * 42f, 0f);
            segmentRect.sizeDelta = new Vector2(36f, 28f);

            segments[i] = segment;
        }

        PunchForceUI punchUI = group.gameObject.AddComponent<PunchForceUI>();
        SerializedObject so = new SerializedObject(punchUI);
        so.FindProperty("binder").objectReferenceValue = binder;
        so.FindProperty("content").objectReferenceValue = content.gameObject;
        so.FindProperty("filledColor").colorValue = PunchAmber;
        so.FindProperty("emptyColor").colorValue = SegmentEmpty;

        SerializedProperty segmentsProp = so.FindProperty("segments");
        segmentsProp.arraySize = segments.Length;
        for (int i = 0; i < segments.Length; i++)
            segmentsProp.GetArrayElementAtIndex(i).objectReferenceValue = segments[i];

        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ================================================================
    //  สเต็ป 3: กล่องข้อความสถานะกลางล่าง (Q ลุก / R ดึงชิ้นส่วน)
    // ================================================================

    private static void BuildStatusPrompt(Transform canvasRoot, LocalRobotBinder binder)
    {
        RectTransform group = CreateRect(canvasRoot, "StatusPrompt");
        group.anchorMin = new Vector2(0.5f, 0f);
        group.anchorMax = new Vector2(0.5f, 0f);
        group.pivot = new Vector2(0.5f, 0f);
        group.anchoredPosition = new Vector2(0f, 95f);
        group.sizeDelta = new Vector2(560f, 64f);

        CanvasGroup canvasGroup = group.gameObject.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        Sprite uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

        // พื้นหลังทรงแคปซูล — ไม่เข้า layout (วางเต็มกล่อง)
        Image background = CreateImage(group, "Background", uiSprite, new Color(0.04f, 0.07f, 0.11f, 0.72f));
        background.type = Image.Type.Sliced;
        Stretch(background.rectTransform);
        LayoutElement backgroundLayout = background.gameObject.AddComponent<LayoutElement>();
        backgroundLayout.ignoreLayout = true;

        // เรียงเนื้อหากลางกล่อง: "กด" [R] "ค้าง · ดึงชิ้นส่วนกลับ"
        HorizontalLayoutGroup layout = group.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.spacing = 12f;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        TMP_FontAsset font = FindHudFont();

        TextMeshProUGUI prefix = CreateText(group, "PrefixText", "HOLD", font, 26f, Color.white);
        prefix.rectTransform.sizeDelta = new Vector2(84f, 40f);
        prefix.alignment = TextAlignmentOptions.MidlineRight;
        prefix.characterSpacing = 2f;

        Image keyCap = CreateImage(group, "KeyCap", uiSprite, new Color(0.16f, 0.38f, 0.48f, 1f));
        keyCap.type = Image.Type.Sliced;
        keyCap.rectTransform.sizeDelta = new Vector2(42f, 42f);
        TextMeshProUGUI keyLetter = CreateText(keyCap.transform, "KeyText", "R", font, 24f, Color.white);
        Center(keyLetter.rectTransform, new Vector2(42f, 42f));
        keyLetter.fontStyle = FontStyles.Bold;

        TextMeshProUGUI instruction = CreateText(group, "InstructionText", "· PULL LIMB BACK", font, 26f, Color.white);
        instruction.rectTransform.sizeDelta = new Vector2(340f, 40f);
        instruction.alignment = TextAlignmentOptions.MidlineLeft;
        instruction.characterSpacing = 2f;

        StatusPromptUI promptUI = group.gameObject.AddComponent<StatusPromptUI>();
        SerializedObject so = new SerializedObject(promptUI);
        so.FindProperty("binder").objectReferenceValue = binder;
        so.FindProperty("canvasGroup").objectReferenceValue = canvasGroup;
        so.FindProperty("keyText").objectReferenceValue = keyLetter;
        so.FindProperty("instructionText").objectReferenceValue = instruction;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ================================================================
    //  สเต็ป 4: การ์ดสถานะแขนขา 4 ใบขวาล่าง
    // ================================================================

    private static readonly string[] LimbCardLabels = { "L-ARM", "R-ARM", "L-LEG", "R-LEG" };

    private static void BuildLimbStatusPanel(Transform canvasRoot, LocalRobotBinder binder)
    {
        RectTransform panel = CreateRect(canvasRoot, "LimbStatusPanel");
        panel.anchorMin = new Vector2(1f, 0f);
        panel.anchorMax = new Vector2(1f, 0f);
        panel.pivot = new Vector2(1f, 0f);
        panel.anchoredPosition = new Vector2(-60f, 50f);
        panel.sizeDelta = new Vector2(4 * 96f + 3 * 12f, 110f);

        HorizontalLayoutGroup layout = panel.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleRight;
        layout.spacing = 12f;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        TMP_FontAsset font = FindHudFont();
        Sprite uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        Sprite knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

        for (int i = 0; i < LimbCardLabels.Length; i++)
            BuildLimbCard(panel, binder, i, LimbCardLabels[i], font, uiSprite, knob);
    }

    private static void BuildLimbCard(
        Transform parent, LocalRobotBinder binder, int slotIndex, string label,
        TMP_FontAsset font, Sprite uiSprite, Sprite knob)
    {
        RectTransform card = CreateRect(parent, $"LimbCard_{label}");
        card.sizeDelta = new Vector2(96f, 110f);

        CanvasGroup canvasGroup = card.gameObject.AddComponent<CanvasGroup>();

        // ขอบ (เต็มการ์ด) + พื้นหลัง (หดเข้า 3px ให้เห็นขอบ)
        Image border = CreateImage(card, "Border", uiSprite, new Color(1f, 1f, 1f, 0.15f));
        border.type = Image.Type.Sliced;
        Stretch(border.rectTransform);

        Image background = CreateImage(card, "Background", uiSprite, PanelDarkSolid);
        background.type = Image.Type.Sliced;
        Stretch(background.rectTransform);
        background.rectTransform.offsetMin = new Vector2(3f, 3f);
        background.rectTransform.offsetMax = new Vector2(-3f, -3f);

        TextMeshProUGUI labelText = CreateText(card, "Label", label, font, 18f, LabelGrey);
        Center(labelText.rectTransform, new Vector2(90f, 24f));
        labelText.rectTransform.anchoredPosition = new Vector2(0f, 8f);
        labelText.characterSpacing = 3f;

        Image dot = CreateImage(card, "StatusDot", knob, new Color(0.3f, 0.9f, 0.45f, 1f));
        Center(dot.rectTransform, new Vector2(12f, 12f));
        dot.rectTransform.anchoredPosition = new Vector2(0f, -28f);

        LimbStatusUI cardUI = card.gameObject.AddComponent<LimbStatusUI>();
        SerializedObject so = new SerializedObject(cardUI);
        so.FindProperty("binder").objectReferenceValue = binder;
        so.FindProperty("slot").enumValueIndex = slotIndex;
        so.FindProperty("borderImage").objectReferenceValue = border;
        so.FindProperty("backgroundImage").objectReferenceValue = background;
        so.FindProperty("statusDot").objectReferenceValue = dot;
        so.FindProperty("labelText").objectReferenceValue = labelText;
        so.FindProperty("canvasGroup").objectReferenceValue = canvasGroup;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ================================================================
    //  Helpers (ใช้ต่อในทุกสเต็ป)
    // ================================================================

    private static RectTransform CreateRect(Transform parent, string name)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static Image CreateImage(Transform parent, string name, Sprite sprite, Color color)
    {
        RectTransform rect = CreateRect(parent, name);
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static TextMeshProUGUI CreateText(
        Transform parent, string name, string text, TMP_FontAsset font, float size, Color color)
    {
        RectTransform rect = CreateRect(parent, name);
        TextMeshProUGUI tmp = rect.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        if (font != null)
            tmp.font = font;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        return tmp;
    }

    /// <summary>ยืดเต็ม parent</summary>
    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// <summary>ยึดกลาง parent ด้วยขนาดคงที่</summary>
    private static void Center(RectTransform rect, Vector2 size)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;
    }

    /// <summary>
    /// หา TMP font ใน Assets/Fonts — ชอบตัวที่รองรับไทย (ชื่อมี Thai) ก่อน
    /// ถ้ามีแค่ไฟล์ .ttf ไทยแต่ยังไม่มี TMP asset จะสร้าง dynamic asset ให้อัตโนมัติ
    /// ไม่เจออะไรเลยใช้ font default ของ TMP (ข้อความไทยจะเป็นกล่องสี่เหลี่ยม)
    /// </summary>
    private static TMP_FontAsset FindHudFont()
    {
        string[] guids = AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { "Assets/Fonts" });
        TMP_FontAsset first = null;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (font == null)
                continue;

            if (path.IndexOf("thai", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return font;

            if (first == null)
                first = font;
        }

        TMP_FontAsset createdThaiFont = TryCreateThaiFontAsset();
        if (createdThaiFont != null)
            return createdThaiFont;

        return first != null ? first : TMP_Settings.defaultFontAsset;
    }

    /// <summary>
    /// สร้าง TMP font asset (Dynamic atlas) จากไฟล์ฟอนต์ .ttf/.otf ที่ชื่อมี "Thai"
    /// ใต้ Assets/Fonts — วางไฟล์ฟอนต์แล้วกด Build ใหม่ ข้อความไทยจะใช้ได้ทันที
    /// </summary>
    private static TMP_FontAsset TryCreateThaiFontAsset()
    {
        string[] fontGuids = AssetDatabase.FindAssets("t:Font", new[] { "Assets/Fonts" });

        foreach (string guid in fontGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.IndexOf("thai", System.StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(path);
            if (sourceFont == null)
                continue;

            TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
                sourceFont, 64, 6,
                UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                512, 512,
                AtlasPopulationMode.Dynamic);
            if (fontAsset == null)
                continue;

            string assetPath = System.IO.Path.ChangeExtension(path, null) + " SDF.asset";
            AssetDatabase.CreateAsset(fontAsset, assetPath);
            fontAsset.atlasTextures[0].name = fontAsset.name + " Atlas";
            fontAsset.material.name = fontAsset.name + " Material";
            AssetDatabase.AddObjectToAsset(fontAsset.atlasTextures[0], fontAsset);
            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            AssetDatabase.SaveAssets();

            Debug.Log($"[PlayerHUD] created Thai TMP font asset → {assetPath}");
            return fontAsset;
        }

        return null;
    }
}
