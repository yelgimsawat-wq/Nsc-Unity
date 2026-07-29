using TMPro;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// สร้าง UI จบเกมโหมด Boss (WinPanel + GameOverPanel) ด้วยเมนูเดียว:
/// Tools/NSC/Build Boss GameFlow UI — เปิดซีนบอสไว้ก่อนแล้วค่อยกด
/// กดซ้ำได้เสมอ: ลบของเก่าแล้ว regenerate ใหม่ทับ prefab เดิม (สไตล์เดียวกับ PlayerHudBuilder)
///
/// ใช้อาร์ตจาก Assets/Yelmee/UI: WinPanel.png, GameOverPanel.png, BottonTrimmed.png
/// ฟอนต์: Oswald-Regular SDF (ข้อความอังกฤษล้วนตามที่ตกลง)
/// </summary>
public static class GameFlowPanelBuilder
{
    private const string ArtFolder = "Assets/Yelmee/UI";
    private const string PrefabPath = ArtFolder + "/BossGameFlowUI.prefab";
    private const string FontPath = "Assets/Fonts/Oswald/Oswald-Regular SDF.asset";

    private static readonly Color BackdropColor = new Color(0f, 0f, 0f, 0.82f);
    private static readonly Color WaitingGrey = new Color(0.75f, 0.82f, 0.88f, 0.9f);
    private static readonly Color WinGreen = new Color(0.45f, 0.92f, 0.5f, 1f);   // "MISSION COMPLETE" ตามต้นแบบ
    private static readonly Color FailRed = new Color(1f, 0.4f, 0.4f, 1f);        // "MISSION FAILED"
    private static readonly Color TimeLabelBlue = new Color(0.4f, 0.7f, 1f, 1f);  // ป้าย "TIME" (ต้นแบบ "เวลา")
    private static readonly Color TimeDigitsBlue = new Color(0.72f, 0.85f, 1f, 1f); // ตัวเลขเวลา
    private static readonly Color GoldLabel = new Color(1f, 0.78f, 0.25f, 1f);    // ธีมทอง Parkour
    private static readonly Color GoldDigits = new Color(1f, 0.85f, 0.4f, 1f);

    [MenuItem("Tools/NSC/Build Boss GameFlow UI")]
    public static void Build()
    {
        // ลบของเก่าก่อน regenerate — builder เป็นเจ้าของ layout ทั้งหมด
        GameFlowManager[] oldManagers = Object.FindObjectsByType<GameFlowManager>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (GameFlowManager old in oldManagers)
            Object.DestroyImmediate(old.gameObject);

        TMP_FontAsset font = LoadFont();
        Sprite winArt = LoadSprite(ArtFolder + "/WinPanel.png");
        Sprite gameOverArt = LoadSprite(ArtFolder + "/GameOverPanel.png");
        Sprite buttonArt = LoadButtonSprite(ArtFolder + "/Botton.png", ArtFolder + "/BottonTrimmed.png");

        // ── Root: Canvas + NetworkObject + GameFlowManager ──
        GameObject root = new GameObject("BossGameFlowUI", typeof(RectTransform));

        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50; // ทับ PlayerHUD (sortingOrder 10)

        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        root.AddComponent<GraphicRaycaster>();
        root.AddComponent<NetworkObject>(); // scene object — NGO spawn ให้อัตโนมัติ
        GameFlowManager manager = root.AddComponent<GameFlowManager>();

        // ── สร้างสอง panel (subtitle ตามต้นแบบ "ภารกิจสำเร็จ" — แปลงเป็นอังกฤษตามที่ตกลง) ──
        PanelRefs win = BuildEndPanel(root.transform, "WinPanel", winArt, buttonArt, font,
            "MISSION COMPLETE", WinGreen);
        PanelRefs lose = BuildEndPanel(root.transform, "GameOverPanel", gameOverArt, buttonArt, font,
            "MISSION FAILED", FailRed);

        // ⏱️ บล็อกเวลา (ป้าย TIME + ตัวเลขใหญ่กลางจอ) — ทั้งสองหน้า
        TextMeshProUGUI winTime = BuildTimeBlock(win.canvasGroup.transform, font);
        TextMeshProUGUI loseTime = BuildTimeBlock(lose.canvasGroup.transform, font);

        // ── โยง references เข้า GameFlowManager ──
        SerializedObject so = new SerializedObject(manager);
        so.FindProperty("winPanel").objectReferenceValue = win.canvasGroup;
        so.FindProperty("gameOverPanel").objectReferenceValue = lose.canvasGroup;
        so.FindProperty("winHostButtons").objectReferenceValue = win.hostButtons;
        so.FindProperty("winWaitingText").objectReferenceValue = win.waitingText;
        so.FindProperty("gameOverHostButtons").objectReferenceValue = lose.hostButtons;
        so.FindProperty("gameOverWaitingText").objectReferenceValue = lose.waitingText;
        so.FindProperty("winRestartButton").objectReferenceValue = win.restartButton;
        so.FindProperty("winExitButton").objectReferenceValue = win.exitButton;
        so.FindProperty("gameOverRestartButton").objectReferenceValue = lose.restartButton;
        so.FindProperty("gameOverExitButton").objectReferenceValue = lose.exitButton;
        so.FindProperty("winTimeText").objectReferenceValue = winTime;
        so.FindProperty("gameOverTimeText").objectReferenceValue = loseTime;
        so.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAssetAndConnect(root, PrefabPath, InteractionMode.UserAction);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;

        Debug.Log($"[GameFlowUI] built and saved to {PrefabPath} — instance placed in the open scene.\n" +
                  "อย่าลืม: 1) hook TriggerVictory ใน EnemyHealth ถูกเพิ่มให้แล้ว 2) เช็คชื่อซีนเมนูใน Inspector (menuSceneName)");
    }

    // ================================================================
    //  🏁 Parkour: HIGH SCORE panel (ธีมทอง — TIME + HEIGHT)
    // ================================================================

    private const string ParkourPrefabPath = ArtFolder + "/ParkourGameFlowUI.prefab";

    [MenuItem("Tools/NSC/Build Parkour GameFlow UI")]
    public static void BuildParkour()
    {
        ParkourFlowManager[] oldManagers = Object.FindObjectsByType<ParkourFlowManager>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (ParkourFlowManager old in oldManagers)
            Object.DestroyImmediate(old.gameObject);

        TMP_FontAsset font = LoadFont();
        Sprite panelArt = LoadSprite(ArtFolder + "/HighScorePanel.png");
        Sprite buttonArt = LoadButtonSprite(ArtFolder + "/BottonYellow.png", ArtFolder + "/BottonYellowTrimmed.png");

        GameObject root = new GameObject("ParkourGameFlowUI", typeof(RectTransform));

        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;

        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        root.AddComponent<GraphicRaycaster>();
        root.AddComponent<NetworkObject>();
        ParkourFlowManager manager = root.AddComponent<ParkourFlowManager>();

        // ── Panel: Backdrop → Art → TIME/HEIGHT → ปุ่ม ──
        RectTransform panel = CreateRect(root.transform, "HighScorePanel");
        Stretch(panel);

        CanvasGroup group = panel.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;
        panel.gameObject.SetActive(false);

        Image backdrop = CreateImage(panel, "Backdrop", null, BackdropColor);
        Stretch(backdrop.rectTransform);
        backdrop.raycastTarget = true;

        Image artImage = CreateImage(panel, "Art", panelArt, Color.white);
        Stretch(artImage.rectTransform);
        artImage.preserveAspect = true;

        // TIME (บน) — ตำแหน่งตามต้นแบบ: label + ตัวเลขใหญ่
        TextMeshProUGUI timeLabel = CreateText(panel, "TimeLabel", "TIME", font, 40f, GoldLabel);
        Center(timeLabel.rectTransform, new Vector2(400f, 50f));
        timeLabel.rectTransform.anchoredPosition = new Vector2(0f, 190f);
        timeLabel.characterSpacing = 12f;

        TextMeshProUGUI timeValue = CreateText(
            panel, "TimeValue", "00:00<size=55%>.00</size>", font, 110f, GoldDigits);
        Center(timeValue.rectTransform, new Vector2(900f, 130f));
        timeValue.rectTransform.anchoredPosition = new Vector2(0f, 90f);
        timeValue.fontStyle = FontStyles.Bold;
        timeValue.characterSpacing = 6f;

        // HEIGHT (ล่าง)
        TextMeshProUGUI heightLabel = CreateText(panel, "HeightLabel", "HEIGHT", font, 40f, GoldLabel);
        Center(heightLabel.rectTransform, new Vector2(400f, 50f));
        heightLabel.rectTransform.anchoredPosition = new Vector2(0f, -40f);
        heightLabel.characterSpacing = 12f;

        TextMeshProUGUI heightValue = CreateText(
            panel, "HeightValue", "0<size=55%>m</size>", font, 100f, GoldDigits);
        Center(heightValue.rectTransform, new Vector2(700f, 120f));
        heightValue.rectTransform.anchoredPosition = new Vector2(0f, -140f);
        heightValue.fontStyle = FontStyles.Bold;

        // ปุ่ม RETRY / EXIT (Host) — ตามต้นแบบใช้คำ RETRY
        RectTransform hostButtons = CreateRect(panel, "HostButtons");
        Center(hostButtons, new Vector2(1400f, 200f));
        hostButtons.anchoredPosition = new Vector2(0f, -330f);

        Button retry = CreateArtButton(hostButtons, "RetryButton", "RETRY", buttonArt, font);
        retry.GetComponent<RectTransform>().anchoredPosition = new Vector2(-330f, 0f);

        Button exit = CreateArtButton(hostButtons, "ExitButton", "EXIT", buttonArt, font);
        exit.GetComponent<RectTransform>().anchoredPosition = new Vector2(330f, 0f);

        TextMeshProUGUI waiting = CreateText(
            panel, "WaitingText", "WAITING FOR HOST...", font, 36f, WaitingGrey);
        Center(waiting.rectTransform, new Vector2(800f, 70f));
        waiting.rectTransform.anchoredPosition = new Vector2(0f, -330f);
        waiting.characterSpacing = 8f;
        waiting.gameObject.SetActive(false);

        // ── Wire references ──
        SerializedObject so = new SerializedObject(manager);
        so.FindProperty("highScorePanel").objectReferenceValue = group;
        so.FindProperty("hostButtons").objectReferenceValue = hostButtons.gameObject;
        so.FindProperty("waitingText").objectReferenceValue = waiting.gameObject;
        so.FindProperty("retryButton").objectReferenceValue = retry;
        so.FindProperty("exitButton").objectReferenceValue = exit;
        so.FindProperty("timeText").objectReferenceValue = timeValue;
        so.FindProperty("heightText").objectReferenceValue = heightValue;
        so.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAssetAndConnect(root, ParkourPrefabPath, InteractionMode.UserAction);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;

        Debug.Log($"[ParkourGameFlowUI] built and saved to {ParkourPrefabPath} — instance placed in the open scene.\n" +
                  "อย่าลืม: วาง ParkourGoalZone (Collider isTrigger) ไว้บนยอดด่าน + เช็ค menuSceneName ใน Inspector");
    }

    // ================================================================
    //  Panel เดียว: Backdrop → Art เต็มจอ → ปุ่ม Host / ข้อความรอ Client
    // ================================================================

    private struct PanelRefs
    {
        public CanvasGroup canvasGroup;
        public GameObject hostButtons;
        public GameObject waitingText;
        public Button restartButton;
        public Button exitButton;
    }

    private static PanelRefs BuildEndPanel(
        Transform canvasRoot, string name, Sprite art, Sprite buttonSprite, TMP_FontAsset font,
        string subtitle, Color subtitleColor)
    {
        RectTransform panel = CreateRect(canvasRoot, name);
        Stretch(panel);

        CanvasGroup group = panel.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;
        panel.gameObject.SetActive(false); // GameFlowManager เปิดเองตอนจบเกม

        // ม่านดำกันคลิกทะลุ + ปิดช่องว่างจอที่ไม่ใช่ 16:9
        Image backdrop = CreateImage(panel, "Backdrop", null, BackdropColor);
        Stretch(backdrop.rectTransform);
        backdrop.raycastTarget = true;

        // อาร์ตเต็มจอ (มีหัวข้อ YOU WIN / GAME OVER ในตัว) — คงสัดส่วน 16:9
        Image artImage = CreateImage(panel, "Art", art, Color.white);
        Stretch(artImage.rectTransform);
        artImage.preserveAspect = true;

        // ── บรรทัดสถานะใต้หัวข้อ (ตามต้นแบบ "ภารกิจสำเร็จ") ──
        TextMeshProUGUI subtitleText = CreateText(panel, "Subtitle", subtitle, font, 44f, subtitleColor);
        Center(subtitleText.rectTransform, new Vector2(900f, 70f));
        subtitleText.rectTransform.anchoredPosition = new Vector2(0f, 210f);
        subtitleText.characterSpacing = 10f;

        // ── ปุ่มฝั่ง Host (ใหญ่สองปุ่มล่างจอ ตามสัดส่วนต้นแบบ — ตัดบล็อกเวลาออกตามสั่ง) ──
        RectTransform hostButtons = CreateRect(panel, "HostButtons");
        Center(hostButtons, new Vector2(1400f, 200f));
        hostButtons.anchoredPosition = new Vector2(0f, -330f);

        Button restart = CreateArtButton(hostButtons, "RestartButton", "RESTART", buttonSprite, font);
        restart.GetComponent<RectTransform>().anchoredPosition = new Vector2(-330f, 0f);

        Button exit = CreateArtButton(hostButtons, "ExitButton", "EXIT", buttonSprite, font);
        exit.GetComponent<RectTransform>().anchoredPosition = new Vector2(330f, 0f);

        // ── ข้อความฝั่ง Client ──
        TextMeshProUGUI waiting = CreateText(
            panel, "WaitingText", "WAITING FOR HOST...", font, 36f, WaitingGrey);
        Center(waiting.rectTransform, new Vector2(800f, 70f));
        waiting.rectTransform.anchoredPosition = new Vector2(0f, -330f);
        waiting.characterSpacing = 8f;
        waiting.gameObject.SetActive(false);

        return new PanelRefs
        {
            canvasGroup = group,
            hostButtons = hostButtons.gameObject,
            waitingText = waiting.gameObject,
            restartButton = restart,
            exitButton = exit,
        };
    }

    /// <summary>ป้าย TIME + ตัวเลขเวลาใหญ่กลางจอ ตามต้นแบบ — คืน text ตัวเลขไว้ให้ wire</summary>
    private static TextMeshProUGUI BuildTimeBlock(Transform panel, TMP_FontAsset font)
    {
        TextMeshProUGUI timeLabel = CreateText(panel, "TimeLabel", "TIME", font, 46f, TimeLabelBlue);
        Center(timeLabel.rectTransform, new Vector2(500f, 60f));
        timeLabel.rectTransform.anchoredPosition = new Vector2(0f, 70f);
        timeLabel.characterSpacing = 14f;

        TextMeshProUGUI timeValue = CreateText(
            panel, "TimeValue", "00:00<size=55%>.00</size>", font, 130f, TimeDigitsBlue);
        Center(timeValue.rectTransform, new Vector2(1000f, 160f));
        timeValue.rectTransform.anchoredPosition = new Vector2(0f, -50f);
        timeValue.fontStyle = FontStyles.Bold;
        timeValue.characterSpacing = 6f;

        return timeValue;
    }

    private static Button CreateArtButton(
        Transform parent, string name, string label, Sprite sprite, TMP_FontAsset font)
    {
        RectTransform rect = CreateRect(parent, name);
        Center(rect, new Vector2(560f, 160f)); // ปุ่มใหญ่ตามสัดส่วนต้นแบบ (สอดคล้อง sprite 639x186)

        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = Color.white;
        image.raycastTarget = true;
        image.preserveAspect = true;

        Button button = rect.gameObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
        colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        colors.fadeDuration = 0.1f;
        button.colors = colors;

        TextMeshProUGUI text = CreateText(rect, "Label", label, font, 44f, Color.white);
        Stretch(text.rectTransform);
        text.fontStyle = FontStyles.Bold;
        text.characterSpacing = 6f;

        return button;
    }

    // ================================================================
    //  Asset loading
    // ================================================================

    private static TMP_FontAsset LoadFont()
    {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font != null) return font;

        // fallback: หา Oswald ที่ไหนก็ได้ในโปรเจกต์
        foreach (string guid in AssetDatabase.FindAssets("Oswald t:TMP_FontAsset"))
        {
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
            if (font != null) return font;
        }

        Debug.LogWarning("[GameFlowUI] Oswald-Regular SDF not found — using TMP default font.");
        return TMP_Settings.defaultFontAsset;
    }

    /// <summary>
    /// โหลด sprite ปุ่ม: sub-sprite ที่ผู้ใช้ slice ไว้ใน texture หลักก่อน (เช่น "14_0")
    /// — LoadAllAssets ดึง sub-sprite ของ texture แบบ Multiple ออกมาได้
    /// fallback: เวอร์ชัน trim อัตโนมัติ
    /// </summary>
    private static Sprite LoadButtonSprite(string mainPath, string trimmedPath)
    {
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(mainPath))
        {
            if (asset is Sprite sliced)
                return sliced;
        }

        Sprite fallback = LoadSprite(trimmedPath);
        if (fallback == null)
            Debug.LogWarning($"[GameFlowUI] ไม่พบ sprite ปุ่มเลย ({mainPath}) — ปุ่มจะเป็นสี่เหลี่ยมสีพื้น");
        return fallback;
    }

    private static Sprite LoadSprite(string path)
    {
        // บังคับ import เป็น Sprite ก่อน (ภาพเพิ่งวางอาจยังเป็น Default texture)
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null && importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.SaveAndReimport();
        }

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
            Debug.LogWarning($"[GameFlowUI] sprite not found: {path}");
        return sprite;
    }

    // ================================================================
    //  Helpers (สไตล์เดียวกับ PlayerHudBuilder)
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
        if (font != null) tmp.font = font;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void Center(RectTransform rect, Vector2 size)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;
    }
}
