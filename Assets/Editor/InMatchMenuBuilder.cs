using System.Collections.Generic;
using NscGame.Pvp;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// สร้างเมนูระหว่างเล่น (กด ESC) ในฉากปัจจุบัน แล้วต่อ reference ให้ InMatchMenu อัตโนมัติ
///
/// เมนู: Tools ▸ NSC ▸ UI ▸ Build In-Match Menu
///
/// ใช้สไปรท์กรอบ HUD ชุดเดียวกับหน้า Select Parts (PvpUiSpriteFactory) หน้าตาจึงเป็นชุดเดียวกัน
/// ต้องสั่งในทุกฉากที่เล่นได้ (Boss / Parkour / PVP) — ตัวเมนูเป็นของ local ล้วน ไม่ยุ่งกับ netcode
/// </summary>
public static class InMatchMenuBuilder
{
    private const string FontPath = "Assets/Fonts/Oswald/Oswald-Regular SDF.asset";

    private static readonly Color ScreenDim  = new Color(0.012f, 0.020f, 0.035f, 0.72f);
    private static readonly Color CardFill   = new Color(0.030f, 0.045f, 0.070f, 0.97f);
    private static readonly Color BoxFill    = new Color(0.075f, 0.090f, 0.120f, 0.95f);
    private static readonly Color TrackFill  = new Color(0.100f, 0.120f, 0.160f, 1f);
    private static readonly Color EdgeSoft   = new Color(0.45f, 0.58f, 0.76f, 0.50f);
    private static readonly Color EdgeFaint  = new Color(0.45f, 0.55f, 0.70f, 0.22f);
    private static readonly Color Accent     = new Color(0.24f, 0.58f, 0.98f, 1f);
    private static readonly Color AccentSoft = new Color(0.10f, 0.24f, 0.42f, 0.95f);
    private static readonly Color Danger     = new Color(0.90f, 0.24f, 0.28f, 1f);
    private static readonly Color DangerSoft = new Color(0.28f, 0.06f, 0.07f, 0.90f);
    private static readonly Color Gold       = new Color(0.99f, 0.78f, 0.20f, 1f);
    private static readonly Color GoldSoft   = new Color(0.35f, 0.20f, 0.05f, 0.85f);
    private static readonly Color Green      = new Color(0.24f, 0.84f, 0.44f, 1f);
    private static readonly Color TextWhite  = new Color(0.94f, 0.96f, 0.99f, 1f);
    private static readonly Color TextGrey   = new Color(0.59f, 0.65f, 0.73f, 1f);
    private static readonly Color TextDim    = new Color(0.39f, 0.44f, 0.52f, 1f);
    private static readonly Color MeterOff   = new Color(0.16f, 0.19f, 0.24f, 1f);

    private const float CardW = 780f;
    private const float CardH = 900f;
    private const float ContentX = -350f;
    private const float ContentW = 700f;
    private const int MicBarCount = 26;
    private const int VoiceRowCount = 4;

    [MenuItem("Tools/NSC/UI/Build In-Match Menu")]
    public static void Build()
    {
        PvpUiSpriteFactory.EnsureGenerated();

        foreach (InMatchMenu old in Object.FindObjectsByType<InMatchMenu>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
            Object.DestroyImmediate(old.gameObject);

        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

        GameObject root = new GameObject("InMatchMenuUI", typeof(RectTransform));
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40; // ต้องสูงกว่า PvpUI (30) และ HUD ผู้เล่น

        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        root.AddComponent<GraphicRaycaster>();
        InMatchMenu menu = root.AddComponent<InMatchMenu>();

        BuildPanel(root.transform, menu, font);
        BuildConfirm(root.transform, menu, font);

        EnsureEventSystem();

        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log("[InMatchMenu] สร้างเมนูระหว่างเล่นเสร็จแล้ว — กด ESC ตอนอยู่ในฉากเกมเพื่อเปิด " +
                  "(ในฉากเมนูหลักจะไม่เปิด ดูช่อง Menu Scene Name)");
    }

    #region Panel

    private static void BuildPanel(Transform parent, InMatchMenu menu, TMP_FontAsset font)
    {
        GameObject panel = CreateUiObject(parent, "Panel");
        Stretch(panel.GetComponent<RectTransform>());
        panel.AddComponent<CanvasGroup>();
        menu.panelRoot = panel;

        // ฉากมืดคลุมจอ — บางกว่าหน้าเลือกทีม เพราะต้องเห็นเกมที่ยังเดินอยู่ข้างหลัง
        GameObject dim = CreateUiObject(panel.transform, "Dim");
        Stretch(dim.GetComponent<RectTransform>());
        AddImage(dim, ScreenDim);

        GameObject card = CreateBox(panel.transform, "Card", Vector2.zero,
            new Vector2(CardW, CardH), CardFill, EdgeSoft, Frame.Full);

        // ---------- หัว ----------
        TextMeshProUGUI title = CreateText(card.transform, "Title", "MENU", font, 40, TextWhite);
        Anchor(title.rectTransform, Center, new Vector2(ContentX + 150f, 399f), new Vector2(300f, 54f));
        title.alignment = TextAlignmentOptions.Left;
        title.fontStyle = FontStyles.Bold;

        GameObject badge = CreateBox(card.transform, "LiveBadge", new Vector2(190f, 398f),
            new Vector2(320f, 44f), GoldSoft, Gold, Frame.Corners);
        TextMeshProUGUI badgeText = CreateText(badge.transform, "Label",
            "MATCH IS STILL RUNNING", font, 19, Gold);
        Stretch(badgeText.rectTransform);

        CreateDivider(card.transform, "Divider1", 361f);

        // ---------- สไลเดอร์เสียง ----------
        menu.masterSlider = CreateSlider(card.transform, "MasterSlider", "MASTER", 318f, font,
            Accent, out menu.masterValue);
        menu.sfxSlider = CreateSlider(card.transform, "SfxSlider", "SFX", 264f, font,
            Accent, out menu.sfxValue);
        menu.voiceSlider = CreateSlider(card.transform, "VoiceSlider", "VOICE", 210f, font,
            Green, out menu.voiceValue);

        menu.sfxNote = CreateText(card.transform, "SfxNote",
            "SFX slider needs the audio mixer - next step", font, 17, TextDim);
        Anchor(menu.sfxNote.rectTransform, Center, new Vector2(0f, 168f), new Vector2(ContentW, 26f));
        menu.sfxNote.alignment = TextAlignmentOptions.Left;

        CreateDivider(card.transform, "Divider2", 146f);

        // ---------- ไมค์ / ลำโพง ----------
        menu.micToggle = CreatePill(card.transform, "MicToggle", "MICROPHONE", -180f, 102f, font);
        menu.speakerToggle = CreatePill(card.transform, "SpeakerToggle", "HEAR OTHERS", 180f, 102f, font);

        // ---------- เลือกไมค์ ----------
        TextMeshProUGUI micDeviceCaption = CreateText(card.transform, "MicDeviceCaption",
            "MIC DEVICE", font, 18, TextDim);
        Anchor(micDeviceCaption.rectTransform, Center, new Vector2(ContentX + 70f, 46f), new Vector2(140f, 30f));
        micDeviceCaption.alignment = TextAlignmentOptions.Left;

        menu.micPrevButton = CreateButton(card.transform, "MicPrev", "<",
            new Vector2(-190f, 46f), new Vector2(34f, 34f), TrackFill, EdgeSoft, TextGrey, font, 20, Frame.Corners);
        menu.micNextButton = CreateButton(card.transform, "MicNext", ">",
            new Vector2(150f, 46f), new Vector2(34f, 34f), TrackFill, EdgeSoft, TextGrey, font, 20, Frame.Corners);

        menu.micDeviceLabel = CreateText(card.transform, "MicDeviceLabel", "Default", font, 20, TextWhite);
        Anchor(menu.micDeviceLabel.rectTransform, Center, new Vector2(-20f, 46f), new Vector2(300f, 32f));

        // ---------- เลือกลำโพง/หูฟัง (สั่ง Windows ย้าย default ให้) ----------
        TextMeshProUGUI outCaption = CreateText(card.transform, "OutputDeviceCaption",
            "OUTPUT", font, 18, TextDim);
        Anchor(outCaption.rectTransform, Center, new Vector2(ContentX + 70f, 0f), new Vector2(140f, 30f));
        outCaption.alignment = TextAlignmentOptions.Left;

        menu.outputPrevButton = CreateButton(card.transform, "OutputPrev", "<",
            new Vector2(-190f, 0f), new Vector2(34f, 34f), TrackFill, EdgeSoft, TextGrey, font, 20, Frame.Corners);
        menu.outputNextButton = CreateButton(card.transform, "OutputNext", ">",
            new Vector2(150f, 0f), new Vector2(34f, 34f), TrackFill, EdgeSoft, TextGrey, font, 20, Frame.Corners);

        menu.outputDeviceLabel = CreateText(card.transform, "OutputDeviceLabel", "Default", font, 20, TextWhite);
        Anchor(menu.outputDeviceLabel.rectTransform, Center, new Vector2(-20f, 0f), new Vector2(300f, 32f));

        menu.outputDeviceNote = CreateText(card.transform, "OutputDeviceNote",
            "changing output switches the Windows default for every app", font, 15, TextDim);
        Anchor(menu.outputDeviceNote.rectTransform, Center, new Vector2(0f, -34f), new Vector2(ContentW, 24f));

        // ---------- มิเตอร์ไมค์ ----------
        TextMeshProUGUI micLabel = CreateText(card.transform, "MicLevelLabel", "MIC LEVEL", font, 18, TextDim);
        Anchor(micLabel.rectTransform, Center, new Vector2(ContentX + 75f, -72f), new Vector2(150f, 26f));
        micLabel.alignment = TextAlignmentOptions.Left;

        menu.micLevelBars = new Image[MicBarCount];
        for (int i = 0; i < MicBarCount; i++)
        {
            GameObject bar = CreateUiObject(card.transform, $"MicBar{i}");
            Anchor(bar.GetComponent<RectTransform>(), Center,
                new Vector2(ContentX + 130f + i * 14f, -72f), new Vector2(9f, 20f));
            Image image = AddImage(bar, MeterOff);
            image.raycastTarget = false;
            menu.micLevelBars[i] = image;
        }

        // ---------- รายชื่อผู้เล่น ----------
        menu.playersHeader = CreateText(card.transform, "PlayersHeader", "PLAYERS", font, 22, TextWhite);
        Anchor(menu.playersHeader.rectTransform, Center, new Vector2(ContentX + 150f, -112f), new Vector2(300f, 32f));
        menu.playersHeader.alignment = TextAlignmentOptions.Left;
        menu.playersHeader.fontStyle = FontStyles.Bold;

        TextMeshProUGUI muteNote = CreateText(card.transform, "MuteNote",
            "mute is local - only you stop hearing them", font, 16, TextDim);
        Anchor(muteNote.rectTransform, Center, new Vector2(150f, -112f), new Vector2(400f, 32f));
        muteNote.alignment = TextAlignmentOptions.Right;

        menu.voiceRows = new InMatchMenu.VoiceRow[VoiceRowCount];
        for (int i = 0; i < VoiceRowCount; i++)
            menu.voiceRows[i] = CreateVoiceRow(card.transform, i, -156f - i * 48f, font);

        // ---------- ปุ่มล่าง ----------
        menu.backButton = CreateButton(card.transform, "BackButton", "BACK TO GAME",
            new Vector2(-125f, -386f), new Vector2(240f, 64f), AccentSoft, Accent, TextWhite, font, 23, Frame.Full);
        menu.leaveButton = CreateButton(card.transform, "LeaveButton", "LEAVE MATCH",
            new Vector2(125f, -386f), new Vector2(240f, 64f), DangerSoft, Danger, Danger, font, 23, Frame.Full);

        TextMeshProUGUI hint = CreateText(panel.transform, "EscHint",
            "ESC  -  CLOSE MENU AND KEEP PLAYING", font, 21, TextDim);
        Anchor(hint.rectTransform, Center, new Vector2(0f, -CardH * 0.5f - 34f), new Vector2(900f, 34f));
    }

    #endregion

    #region Confirm dialog

    private static void BuildConfirm(Transform parent, InMatchMenu menu, TMP_FontAsset font)
    {
        // อยู่นอก panelRoot ไม่ได้ — ต้องหายไปพร้อมเมนูตอนกดปิด
        GameObject confirm = CreateUiObject(menu.panelRoot.transform, "ConfirmLeave");
        Stretch(confirm.GetComponent<RectTransform>());
        confirm.AddComponent<CanvasGroup>();
        menu.confirmRoot = confirm;

        GameObject dim = CreateUiObject(confirm.transform, "Dim");
        Stretch(dim.GetComponent<RectTransform>());
        AddImage(dim, new Color(0.008f, 0.012f, 0.024f, 0.75f));

        GameObject dialog = CreateBox(confirm.transform, "Dialog", Vector2.zero,
            new Vector2(660f, 388f), new Color(0.05f, 0.03f, 0.04f, 0.99f), Danger, Frame.Full);

        TextMeshProUGUI title = CreateText(dialog.transform, "Title", "LEAVE MATCH?", font, 40, TextWhite);
        Anchor(title.rectTransform, Center, new Vector2(0f, 138f), new Vector2(600f, 56f));
        title.fontStyle = FontStyles.Bold;

        // แถบเตือนโชว์เฉพาะตอนเราเป็น Host — ออกแล้วห้องสลายทั้งห้อง
        GameObject warnBox = CreateBox(dialog.transform, "HostWarning", new Vector2(0f, 66f),
            new Vector2(580f, 52f), new Color(0.32f, 0.07f, 0.08f, 1f), Danger, Frame.Corners);
        menu.confirmHostWarning = CreateText(warnBox.transform, "Label", "!  YOU ARE THE HOST", font, 25, Danger);
        Stretch(menu.confirmHostWarning.rectTransform);
        menu.confirmHostWarning.fontStyle = FontStyles.Bold;

        menu.confirmBodyLine1 = CreateText(dialog.transform, "Line1",
            "You will go back to the main menu.", font, 23, TextGrey);
        Anchor(menu.confirmBodyLine1.rectTransform, Center, new Vector2(0f, 6f), new Vector2(600f, 34f));

        menu.confirmBodyLine2 = CreateText(dialog.transform, "Line2",
            "The match keeps running without you.", font, 23, TextGrey);
        Anchor(menu.confirmBodyLine2.rectTransform, Center, new Vector2(0f, -28f), new Vector2(600f, 34f));

        menu.confirmCancelButton = CreateButton(dialog.transform, "CancelButton", "CANCEL",
            new Vector2(-137f, -128f), new Vector2(250f, 62f), BoxFill, EdgeSoft, TextGrey, font, 24, Frame.Corners);

        Button leave = CreateButton(dialog.transform, "ConfirmLeaveButton", "LEAVE",
            new Vector2(137f, -128f), new Vector2(250f, 62f), new Color(0.32f, 0.07f, 0.08f, 1f),
            Danger, Danger, font, 24, Frame.Full);
        menu.confirmLeaveButton = leave;
        menu.confirmLeaveLabel = leave.GetComponentInChildren<TextMeshProUGUI>();
    }

    #endregion

    #region Widgets

    private static Slider CreateSlider(Transform parent, string name, string label, float y,
        TMP_FontAsset font, Color fillColor, out TextMeshProUGUI valueLabel)
    {
        TextMeshProUGUI labelText = CreateText(parent, name + "Label", label, font, 24, TextGrey);
        Anchor(labelText.rectTransform, Center, new Vector2(ContentX + 75f, y), new Vector2(150f, 34f));
        labelText.alignment = TextAlignmentOptions.Left;

        GameObject go = CreateUiObject(parent, name);
        Anchor(go.GetComponent<RectTransform>(), Center, new Vector2(-10f, y), new Vector2(380f, 26f));

        Slider slider = go.AddComponent<Slider>();
        slider.transition = Selectable.Transition.ColorTint;

        GameObject background = CreateUiObject(go.transform, "Background");
        RectTransform bgRect = background.GetComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0f, 0.5f);
        bgRect.anchorMax = new Vector2(1f, 0.5f);
        bgRect.pivot = Center;
        bgRect.anchoredPosition = Vector2.zero;
        bgRect.sizeDelta = new Vector2(0f, 14f);
        Image bgImage = AddImage(background, TrackFill);
        bgImage.sprite = LoadHud(PvpUiSpriteFactory.FillPath);
        bgImage.type = Image.Type.Sliced;

        GameObject fillArea = CreateUiObject(go.transform, "Fill Area");
        RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
        fillAreaRect.anchorMin = new Vector2(0f, 0.5f);
        fillAreaRect.anchorMax = new Vector2(1f, 0.5f);
        fillAreaRect.pivot = Center;
        fillAreaRect.anchoredPosition = Vector2.zero;
        fillAreaRect.sizeDelta = new Vector2(-16f, 14f);

        GameObject fill = CreateUiObject(fillArea.transform, "Fill");
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.pivot = Center;
        fillRect.sizeDelta = new Vector2(16f, 0f);
        Image fillImage = AddImage(fill, fillColor);
        fillImage.sprite = LoadHud(PvpUiSpriteFactory.FillPath);
        fillImage.type = Image.Type.Sliced;

        GameObject handleArea = CreateUiObject(go.transform, "Handle Slide Area");
        RectTransform handleAreaRect = handleArea.GetComponent<RectTransform>();
        handleAreaRect.anchorMin = new Vector2(0f, 0f);
        handleAreaRect.anchorMax = new Vector2(1f, 1f);
        handleAreaRect.pivot = Center;
        handleAreaRect.anchoredPosition = Vector2.zero;
        handleAreaRect.sizeDelta = new Vector2(-20f, 0f);

        GameObject handle = CreateUiObject(handleArea.transform, "Handle");
        RectTransform handleRect = handle.GetComponent<RectTransform>();
        handleRect.anchorMin = new Vector2(0f, 0.5f);
        handleRect.anchorMax = new Vector2(0f, 0.5f);
        handleRect.pivot = Center;
        handleRect.sizeDelta = new Vector2(22f, 22f);
        Image handleImage = AddImage(handle, Color.white);
        handleImage.sprite = LoadKnob();

        slider.fillRect = fillRect;
        slider.handleRect = handleRect;
        slider.targetGraphic = handleImage;
        slider.direction = Slider.Direction.LeftToRight;

        valueLabel = CreateText(parent, name + "Value", "100%", font, 22, TextWhite);
        Anchor(valueLabel.rectTransform, Center, new Vector2(242f, y), new Vector2(96f, 34f));
        valueLabel.alignment = TextAlignmentOptions.Right;

        return slider;
    }

    private static InMatchMenu.PillToggle CreatePill(Transform parent, string name, string label,
        float x, float y, TMP_FontAsset font)
    {
        InMatchMenu.PillToggle pill = new InMatchMenu.PillToggle();

        GameObject go = CreateBox(parent, name, new Vector2(x, y), new Vector2(340f, 56f),
            new Color(0.08f, 0.10f, 0.14f, 0.95f), EdgeFaint, Frame.Corners);

        pill.button = go.AddComponent<Button>();
        pill.button.targetGraphic = go.GetComponent<Image>();
        pill.button.transition = Selectable.Transition.ColorTint;
        ApplyButtonColors(pill.button);

        pill.label = CreateText(go.transform, "Label", label, font, 22, TextWhite);
        Anchor(pill.label.rectTransform, Center, new Vector2(-60f, 0f), new Vector2(200f, 40f));
        pill.label.alignment = TextAlignmentOptions.Left;

        GameObject track = CreateUiObject(go.transform, "Track");
        Anchor(track.GetComponent<RectTransform>(), Center, new Vector2(126f, 0f), new Vector2(48f, 26f));
        pill.track = AddImage(track, Green);
        pill.track.sprite = LoadHud(PvpUiSpriteFactory.FillPath);
        pill.track.type = Image.Type.Sliced;
        pill.track.raycastTarget = false;

        GameObject knob = CreateUiObject(track.transform, "Knob");
        Anchor(knob.GetComponent<RectTransform>(), Center, new Vector2(11f, 0f), new Vector2(20f, 20f));
        Image knobImage = AddImage(knob, Color.white);
        knobImage.sprite = LoadKnob();
        knobImage.raycastTarget = false;
        pill.knob = knob.GetComponent<RectTransform>();

        return pill;
    }

    private static InMatchMenu.VoiceRow CreateVoiceRow(Transform parent, int index, float y, TMP_FontAsset font)
    {
        InMatchMenu.VoiceRow row = new InMatchMenu.VoiceRow();

        GameObject go = CreateBox(parent, $"VoiceRow{index}", new Vector2(0f, y),
            new Vector2(ContentW, 44f), BoxFill, EdgeFaint, Frame.Corners);
        row.root = go;

        GameObject icon = CreateUiObject(go.transform, "Icon");
        Anchor(icon.GetComponent<RectTransform>(), Center, new Vector2(-320f, 0f), new Vector2(28f, 28f));
        Image iconImage = AddImage(icon, TextDim);
        iconImage.sprite = LoadHud(PvpUiSpriteFactory.PersonPath);
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;

        row.nameLabel = CreateText(go.transform, "Name", "Player", font, 23, TextWhite);
        Anchor(row.nameLabel.rectTransform, Center, new Vector2(-160f, 0f), new Vector2(280f, 44f));
        row.nameLabel.alignment = TextAlignmentOptions.Left;
        row.nameLabel.fontStyle = FontStyles.Bold;

        GameObject button = CreateBox(go.transform, "MuteButton", new Vector2(290f, 0f),
            new Vector2(110f, 34f), TrackFill, EdgeSoft, Frame.Corners);
        row.muteButton = button.AddComponent<Button>();
        row.muteButton.targetGraphic = button.GetComponent<Image>();
        row.muteButton.transition = Selectable.Transition.ColorTint;
        ApplyButtonColors(row.muteButton);

        Transform frame = button.transform.Find("Frame");
        row.muteFrame = frame != null ? frame.GetComponent<Image>() : null;

        row.muteLabel = CreateText(button.transform, "Label", "MUTE", font, 19, TextDim);
        Stretch(row.muteLabel.rectTransform);

        return row;
    }

    private static void CreateDivider(Transform parent, string name, float y)
    {
        GameObject go = CreateUiObject(parent, name);
        Anchor(go.GetComponent<RectTransform>(), Center, new Vector2(0f, y), new Vector2(ContentW, 2f));
        Image image = AddImage(go, new Color(0.35f, 0.43f, 0.55f, 0.28f));
        image.raycastTarget = false;
    }

    #endregion

    #region Primitives

    private static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

    private enum Frame { Full, Corners }

    private static GameObject CreateBox(Transform parent, string name, Vector2 position, Vector2 size,
        Color fill, Color border, Frame frame)
    {
        GameObject go = CreateUiObject(parent, name);
        Anchor(go.GetComponent<RectTransform>(), Center, position, size);

        Image image = AddImage(go, fill);
        image.sprite = LoadHud(PvpUiSpriteFactory.FillPath);
        image.type = Image.Type.Sliced;

        GameObject frameGo = CreateUiObject(go.transform, "Frame");
        Stretch(frameGo.GetComponent<RectTransform>());
        Image frameImage = frameGo.AddComponent<Image>();
        frameImage.color = border;
        frameImage.raycastTarget = false;
        frameImage.sprite = LoadHud(frame == Frame.Full
            ? PvpUiSpriteFactory.FramePath
            : PvpUiSpriteFactory.CornersPath);
        frameImage.type = Image.Type.Sliced;

        return go;
    }

    private static Button CreateButton(Transform parent, string name, string label, Vector2 position,
        Vector2 size, Color fill, Color border, Color textColor, TMP_FontAsset font, float fontSize, Frame frame)
    {
        GameObject go = CreateBox(parent, name, position, size, fill, border, frame);

        Button button = go.AddComponent<Button>();
        button.targetGraphic = go.GetComponent<Image>();
        button.transition = Selectable.Transition.ColorTint;
        ApplyButtonColors(button);

        TextMeshProUGUI text = CreateText(go.transform, "Label", label, font, fontSize, textColor);
        Stretch(text.rectTransform);
        text.fontStyle = FontStyles.Bold;

        return button;
    }

    private static void ApplyButtonColors(Button button)
    {
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
        colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        colors.selectedColor = Color.white;
        colors.disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.45f);
        colors.fadeDuration = 0.1f;
        button.colors = colors;
    }

    private static Sprite LoadHud(string path) => AssetDatabase.LoadAssetAtPath<Sprite>(path);

    /// <summary>ลูกบิดกลมของ Unity เอง — ใช้กับหัวสไลเดอร์และสวิตช์ ไม่ต้องเจนสไปรท์เพิ่ม</summary>
    private static Sprite LoadKnob() =>
        AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

    private static GameObject CreateUiObject(Transform parent, string name)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static Image AddImage(GameObject target, Color color)
    {
        Image image = target.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private static TextMeshProUGUI CreateText(Transform parent, string name, string content,
        TMP_FontAsset font, float size, Color color)
    {
        GameObject go = CreateUiObject(parent, name);
        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.text = content;
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        if (font != null) text.font = font;
        return text;
    }

    private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = Center;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = Center;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;

        GameObject es = new GameObject("EventSystem",
            typeof(UnityEngine.EventSystems.EventSystem),
            typeof(UnityEngine.EventSystems.StandaloneInputModule));
        Undo.RegisterCreatedObjectUndo(es, "Create EventSystem");
    }

    #endregion
}
