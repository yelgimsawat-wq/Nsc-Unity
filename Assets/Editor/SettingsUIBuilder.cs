using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;
using System.IO;

/// <summary>
/// SettingsUIBuilder.cs
/// Editor Tool สำหรับสร้าง Settings UI ตาม Design.png แบบอัตโนมัติ
///
/// วิธีใช้: Tools → Create Settings UI → Generate Settings Panel
/// </summary>
public class SettingsUIBuilder : EditorWindow
{
    private static Color goldColor = new Color(0.831f, 0.686f, 0.216f, 1f); // #D4AF37
    private static Color darkGrayColor = new Color(0.3f, 0.3f, 0.3f, 1f);
    private static Color blackColor = Color.black;
    private static Color whiteColor = Color.white;

    [MenuItem("Tools/Create Settings UI/Generate Settings Panel")]
    public static void CreateSettingsUI()
    {
        if (EditorUtility.DisplayDialog("Create Settings UI",
            "สร้าง Settings UI Panel ตาม Design.png?\n\nจะสร้าง GameObject hierarchy ทั้งหมดและผูก SettingsManager.cs อัตโนมัติ",
            "สร้างเลย!", "ยกเลิก"))
        {
            BuildSettingsPanel();
        }
    }

    private static void BuildSettingsPanel()
    {
        // หา Canvas หรือสร้างใหม่
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasObj = new GameObject("Canvas");
            canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();
            Debug.Log("[SettingsUIBuilder] สร้าง Canvas ใหม่");
        }

        // สร้าง Main Panel
        GameObject panelObj = CreateMainPanel(canvas.transform);

        // สร้าง Header
        CreateHeader(panelObj.transform);

        // สร้าง Tab Buttons
        GameObject tabButtonsGroup = CreateTabButtons(panelObj.transform);

        // สร้าง Content Container
        GameObject contentContainer = CreateContentContainer(panelObj.transform);

        // สร้าง Sub-Panels
        GameObject graphicsPanel = CreateGraphicsSubPanel(contentContainer.transform);
        GameObject audioPanel = CreateAudioSubPanel(contentContainer.transform);
        GameObject gameplayPanel = CreateGameplaySubPanel(contentContainer.transform);

        // สร้าง Bottom Buttons
        CreateBottomButtons(panelObj.transform);

        // เพิ่ม SettingsManager และผูกทุกอย่าง
        SetupSettingsManager(panelObj, tabButtonsGroup, graphicsPanel, audioPanel, gameplayPanel);

        // ซ่อน Panel ไว้ก่อน
        panelObj.SetActive(false);

        // Select object
        Selection.activeGameObject = panelObj;

        Debug.Log("[SettingsUIBuilder] ✅ สร้าง Settings UI สำเร็จ!");
        EditorUtility.DisplayDialog("สำเร็จ!",
            "✅ สร้าง Settings UI เสร็จแล้ว!\n\n" +
            "ขั้นตอนถัดไป:\n" +
            "1. ผูก Sprite images (SettingPanel.png, ScollAndButton.png)\n" +
            "2. ตั้งค่าสีตาม Theme\n" +
            "3. ปรับ Layout ตามต้องการ\n" +
            "4. ทดสอบด้วย SettingsManager.Instance.OpenSettings()",
            "เข้าใจแล้ว");
    }

    // ================================================================
    //  CREATE MAIN PANEL
    // ================================================================

    private static GameObject CreateMainPanel(Transform parent)
    {
        GameObject panel = new GameObject("SettingsPopup_Panel");
        panel.transform.SetParent(parent, false);

        // Add RectTransform
        RectTransform rect = panel.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(800, 600);
        rect.anchoredPosition = Vector2.zero;

        // Add Image (Panel background)
        Image image = panel.AddComponent<Image>();
        image.color = blackColor;

        // Add CanvasGroup
        panel.AddComponent<CanvasGroup>();

        Debug.Log("[Builder] ✅ สร้าง Main Panel");
        return panel;
    }

    // ================================================================
    //  CREATE HEADER
    // ================================================================

    private static void CreateHeader(Transform parent)
    {
        GameObject header = new GameObject("Header_Section");
        header.transform.SetParent(parent, false);

        RectTransform headerRect = header.AddComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(1f, 1f);
        headerRect.sizeDelta = new Vector2(0, 60);
        headerRect.anchoredPosition = new Vector2(0, -30);

        // Title Text
        GameObject titleObj = new GameObject("Title_Text");
        titleObj.transform.SetParent(header.transform, false);

        RectTransform titleRect = titleObj.AddComponent<RectTransform>();
        titleRect.anchorMin = Vector2.zero;
        titleRect.anchorMax = Vector2.one;
        titleRect.sizeDelta = Vector2.zero;
        titleRect.anchoredPosition = Vector2.zero;

        TextMeshProUGUI titleText = titleObj.AddComponent<TextMeshProUGUI>();
        titleText.text = "SETTINGS";
        titleText.fontSize = 32;
        titleText.fontStyle = FontStyles.Bold;
        titleText.color = whiteColor;
        titleText.alignment = TextAlignmentOptions.Center;

        Debug.Log("[Builder] ✅ สร้าง Header");
    }

    // ================================================================
    //  CREATE TAB BUTTONS
    // ================================================================

    private static GameObject CreateTabButtons(Transform parent)
    {
        GameObject tabGroup = new GameObject("TabButtons_Group");
        tabGroup.transform.SetParent(parent, false);

        RectTransform groupRect = tabGroup.AddComponent<RectTransform>();
        groupRect.anchorMin = new Vector2(0f, 1f);
        groupRect.anchorMax = new Vector2(1f, 1f);
        groupRect.sizeDelta = new Vector2(-40, 50);
        groupRect.anchoredPosition = new Vector2(0, -90);

        HorizontalLayoutGroup layout = tabGroup.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        // สร้าง 3 Tab Buttons
        CreateTabButton(tabGroup.transform, "GraphicsTab_Button", "GRAPHICS");
        CreateTabButton(tabGroup.transform, "AudioTab_Button", "AUDIO");
        CreateTabButton(tabGroup.transform, "GameplayTab_Button", "GAMEPLAY");

        Debug.Log("[Builder] ✅ สร้าง Tab Buttons");
        return tabGroup;
    }

    private static GameObject CreateTabButton(Transform parent, string name, string label)
    {
        GameObject button = new GameObject(name);
        button.transform.SetParent(parent, false);

        // Background Image
        Image bgImage = button.AddComponent<Image>();
        bgImage.color = darkGrayColor;

        // Button component
        Button btn = button.AddComponent<Button>();

        // Text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(button.transform, false);

        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        textRect.anchoredPosition = Vector2.zero;

        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 18;
        text.fontStyle = FontStyles.Bold;
        text.color = whiteColor;
        text.alignment = TextAlignmentOptions.Center;

        return button;
    }

    // ================================================================
    //  CREATE CONTENT CONTAINER
    // ================================================================

    private static GameObject CreateContentContainer(Transform parent)
    {
        GameObject container = new GameObject("TabContent_Container");
        container.transform.SetParent(parent, false);

        RectTransform rect = container.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.offsetMin = new Vector2(20, 80); // Left, Bottom
        rect.offsetMax = new Vector2(-20, -150); // Right, Top

        Debug.Log("[Builder] ✅ สร้าง Content Container");
        return container;
    }

    // ================================================================
    //  CREATE GRAPHICS SUB-PANEL
    // ================================================================

    private static GameObject CreateGraphicsSubPanel(Transform parent)
    {
        GameObject panel = new GameObject("Graphics_SubPanel");
        panel.transform.SetParent(parent, false);

        RectTransform rect = panel.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;

        VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 15;
        layout.padding = new RectOffset(10, 10, 10, 10);
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;

        // Display Mode Row
        CreateDropdownRow(panel.transform, "DisplayMode_Row", "DISPLAY MODE");

        // Resolution Row
        CreateSliderRow(panel.transform, "Resolution_Row", "RESOLUTION");

        // V-Sync Row
        CreateToggleRow(panel.transform, "VSync_Row", "V-SYNC");

        // Quality Preset Row
        CreateSliderRow(panel.transform, "QualityPreset_Row", "QUALITY PRESET");

        Debug.Log("[Builder] ✅ สร้าง Graphics SubPanel");
        return panel;
    }

    // ================================================================
    //  CREATE AUDIO SUB-PANEL
    // ================================================================

    private static GameObject CreateAudioSubPanel(Transform parent)
    {
        GameObject panel = new GameObject("Audio_SubPanel");
        panel.transform.SetParent(parent, false);

        RectTransform rect = panel.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;

        VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 15;
        layout.padding = new RectOffset(10, 10, 10, 10);
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;

        // Volume Sliders (เฉพาะ Master เท่านั้น)
        CreateVolumeSliderRow(panel.transform, "MasterVolume_Row", "MASTER VOLUME");

        panel.SetActive(false); // ซ่อนไว้ก่อน

        Debug.Log("[Builder] ✅ สร้าง Audio SubPanel");
        return panel;
    }

    // ================================================================
    //  CREATE GAMEPLAY SUB-PANEL
    // ================================================================

    private static GameObject CreateGameplaySubPanel(Transform parent)
    {
        GameObject panel = new GameObject("Gameplay_SubPanel");
        panel.transform.SetParent(parent, false);

        RectTransform rect = panel.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;

        VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 15;
        layout.padding = new RectOffset(10, 10, 10, 10);
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;

        // Player Name
        CreateInputFieldRow(panel.transform, "PlayerName_Row", "PLAYER NAME");

        // Show Network Stats
        CreateToggleRow(panel.transform, "ShowNetworkStats_Row", "SHOW NETWORK STATS");

        panel.SetActive(false); // ซ่อนไว้ก่อน

        Debug.Log("[Builder] ✅ สร้าง Gameplay SubPanel");
        return panel;
    }

    // ================================================================
    //  CREATE UI ROWS (HELPERS)
    // ================================================================

    private static void CreateDropdownRow(Transform parent, string name, string labelText)
    {
        GameObject row = new GameObject(name);
        row.transform.SetParent(parent, false);

        RectTransform rect = row.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(0, 40);

        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10;
        layout.childControlWidth = false;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        // Label
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(row.transform, false);
        LayoutElement labelLayout = labelObj.AddComponent<LayoutElement>();
        labelLayout.preferredWidth = 200;

        TextMeshProUGUI label = labelObj.AddComponent<TextMeshProUGUI>();
        label.text = labelText;
        label.fontSize = 14;
        label.color = whiteColor;
        label.alignment = TextAlignmentOptions.MidlineLeft;

        // Dropdown
        GameObject dropdownObj = new GameObject("Dropdown");
        dropdownObj.transform.SetParent(row.transform, false);
        LayoutElement dropdownLayout = dropdownObj.AddComponent<LayoutElement>();
        dropdownLayout.flexibleWidth = 1;

        TMP_Dropdown dropdown = dropdownObj.AddComponent<TMP_Dropdown>();
        dropdown.options.Add(new TMP_Dropdown.OptionData("Option 1"));
        dropdown.options.Add(new TMP_Dropdown.OptionData("Option 2"));
    }

    private static void CreateSliderRow(Transform parent, string name, string labelText)
    {
        GameObject row = new GameObject(name);
        row.transform.SetParent(parent, false);

        RectTransform rect = row.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(0, 40);

        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10;
        layout.childControlWidth = false;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        // Label
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(row.transform, false);
        LayoutElement labelLayout = labelObj.AddComponent<LayoutElement>();
        labelLayout.preferredWidth = 200;

        TextMeshProUGUI label = labelObj.AddComponent<TextMeshProUGUI>();
        label.text = labelText;
        label.fontSize = 14;
        label.color = whiteColor;
        label.alignment = TextAlignmentOptions.MidlineLeft;

        // Slider
        GameObject sliderObj = new GameObject("Slider");
        sliderObj.transform.SetParent(row.transform, false);
        LayoutElement sliderLayout = sliderObj.AddComponent<LayoutElement>();
        sliderLayout.flexibleWidth = 1;

        Slider slider = sliderObj.AddComponent<Slider>();
        slider.minValue = 0;
        slider.maxValue = 100;
        slider.value = 70;
        slider.wholeNumbers = true;

        // Background
        GameObject bg = new GameObject("Background");
        bg.transform.SetParent(sliderObj.transform, false);
        RectTransform bgRect = bg.AddComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.sizeDelta = Vector2.zero;
        Image bgImage = bg.AddComponent<Image>();
        bgImage.color = darkGrayColor;

        // Fill Area
        GameObject fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(sliderObj.transform, false);
        RectTransform fillRect = fillArea.AddComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.sizeDelta = new Vector2(-20, 0);
        fillRect.anchoredPosition = new Vector2(-10, 0);

        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        RectTransform fillRectChild = fill.AddComponent<RectTransform>();
        fillRectChild.anchorMin = Vector2.zero;
        fillRectChild.anchorMax = Vector2.one;
        fillRectChild.sizeDelta = Vector2.zero;
        Image fillImage = fill.AddComponent<Image>();
        fillImage.color = goldColor;

        slider.fillRect = fillRectChild;

        // Handle Slide Area
        GameObject handleArea = new GameObject("Handle Slide Area");
        handleArea.transform.SetParent(sliderObj.transform, false);
        RectTransform handleAreaRect = handleArea.AddComponent<RectTransform>();
        handleAreaRect.anchorMin = Vector2.zero;
        handleAreaRect.anchorMax = Vector2.one;
        handleAreaRect.sizeDelta = new Vector2(-20, 0);
        handleAreaRect.anchoredPosition = new Vector2(0, 0);

        GameObject handle = new GameObject("Handle");
        handle.transform.SetParent(handleArea.transform, false);
        RectTransform handleRect = handle.AddComponent<RectTransform>();
        handleRect.sizeDelta = new Vector2(20, 20);
        Image handleImage = handle.AddComponent<Image>();
        handleImage.color = goldColor;
        handleImage.raycastTarget = true; // ✅ เปิด Raycast เพื่อให้ลากได้

        slider.targetGraphic = handleImage;
        slider.handleRect = handleRect;
        slider.interactable = true; // ✅ ให้ Slider ใช้งานได้
        slider.transition = Selectable.Transition.ColorTint; // ✅ เปิด Color transition

        // Value Label
        GameObject valueObj = new GameObject("ValueLabel");
        valueObj.transform.SetParent(row.transform, false);
        LayoutElement valueLayout = valueObj.AddComponent<LayoutElement>();
        valueLayout.preferredWidth = 50;

        TextMeshProUGUI valueLabel = valueObj.AddComponent<TextMeshProUGUI>();
        valueLabel.text = "70";
        valueLabel.fontSize = 14;
        valueLabel.color = whiteColor;
        valueLabel.alignment = TextAlignmentOptions.MidlineRight;
    }

    private static void CreateVolumeSliderRow(Transform parent, string name, string labelText)
    {
        GameObject row = new GameObject(name);
        row.transform.SetParent(parent, false);

        RectTransform rect = row.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(0, 40);

        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10;
        layout.childControlWidth = false;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        // Label
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(row.transform, false);
        LayoutElement labelLayout = labelObj.AddComponent<LayoutElement>();
        labelLayout.preferredWidth = 200;

        TextMeshProUGUI label = labelObj.AddComponent<TextMeshProUGUI>();
        label.text = labelText;
        label.fontSize = 14;
        label.color = whiteColor;
        label.alignment = TextAlignmentOptions.MidlineLeft;

        // Slider
        GameObject sliderObj = new GameObject("Slider");
        sliderObj.transform.SetParent(row.transform, false);
        LayoutElement sliderLayout = sliderObj.AddComponent<LayoutElement>();
        sliderLayout.flexibleWidth = 1;

        Slider slider = sliderObj.AddComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;  // แก้เป็น 1.0 แทน 100
        slider.value = 1f;
        slider.wholeNumbers = false; // ให้ใช้ทศนิยมได้

        // Background
        GameObject bg = new GameObject("Background");
        bg.transform.SetParent(sliderObj.transform, false);
        RectTransform bgRect = bg.AddComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.sizeDelta = Vector2.zero;
        Image bgImage = bg.AddComponent<Image>();
        bgImage.color = darkGrayColor;

        // Fill Area
        GameObject fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(sliderObj.transform, false);
        RectTransform fillRect = fillArea.AddComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.sizeDelta = new Vector2(-20, 0);
        fillRect.anchoredPosition = new Vector2(-10, 0);

        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        RectTransform fillRectChild = fill.AddComponent<RectTransform>();
        fillRectChild.anchorMin = Vector2.zero;
        fillRectChild.anchorMax = Vector2.one;
        fillRectChild.sizeDelta = Vector2.zero;
        Image fillImage = fill.AddComponent<Image>();
        fillImage.color = goldColor;

        slider.fillRect = fillRectChild;

        // Handle Slide Area
        GameObject handleArea = new GameObject("Handle Slide Area");
        handleArea.transform.SetParent(sliderObj.transform, false);
        RectTransform handleAreaRect = handleArea.AddComponent<RectTransform>();
        handleAreaRect.anchorMin = Vector2.zero;
        handleAreaRect.anchorMax = Vector2.one;
        handleAreaRect.sizeDelta = new Vector2(-20, 0);
        handleAreaRect.anchoredPosition = new Vector2(0, 0);

        GameObject handle = new GameObject("Handle");
        handle.transform.SetParent(handleArea.transform, false);
        RectTransform handleRect = handle.AddComponent<RectTransform>();
        handleRect.sizeDelta = new Vector2(20, 20);
        Image handleImage = handle.AddComponent<Image>();
        handleImage.color = goldColor;
        handleImage.raycastTarget = true; // ✅ เปิด Raycast เพื่อให้ลากได้

        slider.targetGraphic = handleImage;
        slider.handleRect = handleRect;
        slider.interactable = true; // ✅ ให้ Slider ใช้งานได้
        slider.transition = Selectable.Transition.ColorTint; // ✅ เปิด Color transition

        // Value Label
        GameObject valueObj = new GameObject("ValueLabel");
        valueObj.transform.SetParent(row.transform, false);
        LayoutElement valueLayout = valueObj.AddComponent<LayoutElement>();
        valueLayout.preferredWidth = 50;

        TextMeshProUGUI valueLabel = valueObj.AddComponent<TextMeshProUGUI>();
        valueLabel.text = "100%";
        valueLabel.fontSize = 14;
        valueLabel.color = whiteColor;
        valueLabel.alignment = TextAlignmentOptions.MidlineRight;
    }

    private static void CreateToggleRow(Transform parent, string name, string labelText)
    {
        GameObject row = new GameObject(name);
        row.transform.SetParent(parent, false);

        RectTransform rect = row.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(0, 40);

        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10;
        layout.childControlWidth = false;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        // Label
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(row.transform, false);
        LayoutElement labelLayout = labelObj.AddComponent<LayoutElement>();
        labelLayout.preferredWidth = 200;

        TextMeshProUGUI label = labelObj.AddComponent<TextMeshProUGUI>();
        label.text = labelText;
        label.fontSize = 14;
        label.color = whiteColor;
        label.alignment = TextAlignmentOptions.MidlineLeft;

        // Toggle
        GameObject toggleObj = new GameObject("Toggle");
        toggleObj.transform.SetParent(row.transform, false);

        Toggle toggle = toggleObj.AddComponent<Toggle>();

        // Background
        GameObject bg = new GameObject("Background");
        bg.transform.SetParent(toggleObj.transform, false);
        RectTransform bgRect = bg.AddComponent<RectTransform>();
        bgRect.sizeDelta = new Vector2(40, 40);
        Image bgImage = bg.AddComponent<Image>();
        bgImage.color = darkGrayColor;

        // Checkmark
        GameObject checkmark = new GameObject("Checkmark");
        checkmark.transform.SetParent(bg.transform, false);
        RectTransform checkRect = checkmark.AddComponent<RectTransform>();
        checkRect.anchorMin = Vector2.zero;
        checkRect.anchorMax = Vector2.one;
        checkRect.sizeDelta = new Vector2(-10, -10);
        Image checkImage = checkmark.AddComponent<Image>();
        checkImage.color = goldColor;

        toggle.targetGraphic = bgImage;
        toggle.graphic = checkImage;
    }

    private static void CreateInputFieldRow(Transform parent, string name, string labelText)
    {
        GameObject row = new GameObject(name);
        row.transform.SetParent(parent, false);

        RectTransform rect = row.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(0, 40);

        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10;
        layout.childControlWidth = false;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        // Label
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(row.transform, false);
        LayoutElement labelLayout = labelObj.AddComponent<LayoutElement>();
        labelLayout.preferredWidth = 200;

        TextMeshProUGUI label = labelObj.AddComponent<TextMeshProUGUI>();
        label.text = labelText;
        label.fontSize = 14;
        label.color = whiteColor;
        label.alignment = TextAlignmentOptions.MidlineLeft;

        // InputField
        GameObject inputObj = new GameObject("InputField");
        inputObj.transform.SetParent(row.transform, false);
        LayoutElement inputLayout = inputObj.AddComponent<LayoutElement>();
        inputLayout.flexibleWidth = 1;

        TMP_InputField inputField = inputObj.AddComponent<TMP_InputField>();
        inputField.text = "Player";

        // Background
        Image bgImage = inputObj.AddComponent<Image>();
        bgImage.color = darkGrayColor;

        // Text Area
        GameObject textArea = new GameObject("Text Area");
        textArea.transform.SetParent(inputObj.transform, false);
        RectTransform textAreaRect = textArea.AddComponent<RectTransform>();
        textAreaRect.anchorMin = Vector2.zero;
        textAreaRect.anchorMax = Vector2.one;
        textAreaRect.offsetMin = new Vector2(10, 0);
        textAreaRect.offsetMax = new Vector2(-10, 0);

        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(textArea.transform, false);
        TextMeshProUGUI textComp = textObj.AddComponent<TextMeshProUGUI>();
        textComp.fontSize = 14;
        textComp.color = whiteColor;

        inputField.textComponent = textComp;
    }

    // ================================================================
    //  CREATE BOTTOM BUTTONS
    // ================================================================

    private static void CreateBottomButtons(Transform parent)
    {
        GameObject buttonGroup = new GameObject("BottomButtons_Group");
        buttonGroup.transform.SetParent(parent, false);

        RectTransform rect = buttonGroup.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.sizeDelta = new Vector2(-40, 50);
        rect.anchoredPosition = new Vector2(0, 30);

        HorizontalLayoutGroup layout = buttonGroup.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        // SAVE & CLOSE Button
        GameObject saveBtn = CreateButton(buttonGroup.transform, "SaveAndClose_Button", "SAVE & CLOSE", goldColor, blackColor);

        // CLOSE X Button
        GameObject closeBtn = CreateButton(buttonGroup.transform, "Close_Button", "CLOSE ✕", whiteColor, blackColor);

        Debug.Log("[Builder] ✅ สร้าง Bottom Buttons");
    }

    private static GameObject CreateButton(Transform parent, string name, string label, Color bgColor, Color textColor)
    {
        GameObject button = new GameObject(name);
        button.transform.SetParent(parent, false);

        Image bgImage = button.AddComponent<Image>();
        bgImage.color = bgColor;

        Button btn = button.AddComponent<Button>();
        btn.targetGraphic = bgImage;

        // Text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(button.transform, false);

        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        textRect.anchoredPosition = Vector2.zero;

        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 16;
        text.fontStyle = FontStyles.Bold;
        text.color = textColor;
        text.alignment = TextAlignmentOptions.Center;

        return button;
    }

    // ================================================================
    //  SETUP SETTINGS MANAGER & BIND EVERYTHING
    // ================================================================

    private static void SetupSettingsManager(GameObject panelObj, GameObject tabGroup,
        GameObject graphicsPanel, GameObject audioPanel, GameObject gameplayPanel)
    {
        // เพิ่ม SettingsManager component
        SettingsManager manager = panelObj.AddComponent<SettingsManager>();

        // ผูก Main Popup
        SerializedObject so = new SerializedObject(manager);

        so.FindProperty("settingsPopupPanel").objectReferenceValue = panelObj;
        so.FindProperty("popupCanvasGroup").objectReferenceValue = panelObj.GetComponent<CanvasGroup>();

        // ผูก Buttons
        so.FindProperty("saveAndCloseButton").objectReferenceValue =
            panelObj.transform.Find("BottomButtons_Group/SaveAndClose_Button")?.GetComponent<Button>();
        so.FindProperty("closeButton").objectReferenceValue =
            panelObj.transform.Find("BottomButtons_Group/Close_Button")?.GetComponent<Button>();

        // ผูก Tab Buttons
        Transform graphics = tabGroup.transform.Find("GraphicsTab_Button");
        Transform audio = tabGroup.transform.Find("AudioTab_Button");
        Transform gameplay = tabGroup.transform.Find("GameplayTab_Button");

        so.FindProperty("graphicsTabButton").objectReferenceValue = graphics?.GetComponent<Button>();
        so.FindProperty("audioTabButton").objectReferenceValue = audio?.GetComponent<Button>();
        so.FindProperty("gameplayTabButton").objectReferenceValue = gameplay?.GetComponent<Button>();

        so.FindProperty("graphicsTabImage").objectReferenceValue = graphics?.GetComponent<Image>();
        so.FindProperty("audioTabImage").objectReferenceValue = audio?.GetComponent<Image>();
        so.FindProperty("gameplayTabImage").objectReferenceValue = gameplay?.GetComponent<Image>();

        so.FindProperty("graphicsTabText").objectReferenceValue = graphics?.Find("Text")?.GetComponent<TextMeshProUGUI>();
        so.FindProperty("audioTabText").objectReferenceValue = audio?.Find("Text")?.GetComponent<TextMeshProUGUI>();
        so.FindProperty("gameplayTabText").objectReferenceValue = gameplay?.Find("Text")?.GetComponent<TextMeshProUGUI>();

        // ผูก Sub-Panels
        so.FindProperty("graphicsSubPanel").objectReferenceValue = graphicsPanel;
        so.FindProperty("audioSubPanel").objectReferenceValue = audioPanel;
        so.FindProperty("gameplaySubPanel").objectReferenceValue = gameplayPanel;

        // ผูก Graphics Settings
        BindGraphicsSettings(so, graphicsPanel.transform);

        // ผูก Audio Settings
        BindAudioSettings(so, audioPanel.transform);

        // ผูก Gameplay Settings
        BindGameplaySettings(so, gameplayPanel.transform);

        so.ApplyModifiedProperties();

        Debug.Log("[Builder] ✅ ผูก SettingsManager สำเร็จ!");
    }

    private static void BindGraphicsSettings(SerializedObject so, Transform graphicsPanel)
    {
        Transform displayRow = graphicsPanel.Find("DisplayMode_Row");
        if (displayRow != null)
            so.FindProperty("displayModeDropdown").objectReferenceValue = displayRow.Find("Dropdown")?.GetComponent<TMP_Dropdown>();

        Transform resRow = graphicsPanel.Find("Resolution_Row");
        if (resRow != null)
        {
            so.FindProperty("resolutionSlider").objectReferenceValue = resRow.Find("Slider")?.GetComponent<Slider>();
            so.FindProperty("resolutionValueLabel").objectReferenceValue = resRow.Find("ValueLabel")?.GetComponent<TextMeshProUGUI>();
        }

        Transform vsyncRow = graphicsPanel.Find("VSync_Row");
        if (vsyncRow != null)
            so.FindProperty("vSyncToggle").objectReferenceValue = vsyncRow.Find("Toggle")?.GetComponent<Toggle>();

        Transform qualityRow = graphicsPanel.Find("QualityPreset_Row");
        if (qualityRow != null)
        {
            so.FindProperty("qualityPresetSlider").objectReferenceValue = qualityRow.Find("Slider")?.GetComponent<Slider>();
            so.FindProperty("qualityPresetValueLabel").objectReferenceValue = qualityRow.Find("ValueLabel")?.GetComponent<TextMeshProUGUI>();
        }
    }

    private static void BindAudioSettings(SerializedObject so, Transform audioPanel)
    {
        Transform masterRow = audioPanel.Find("MasterVolume_Row");
        if (masterRow != null)
        {
            so.FindProperty("masterVolumeSlider").objectReferenceValue = masterRow.Find("Slider")?.GetComponent<Slider>();
            so.FindProperty("masterVolumeLabel").objectReferenceValue = masterRow.Find("ValueLabel")?.GetComponent<TextMeshProUGUI>();
        }
    }

    private static void BindGameplaySettings(SerializedObject so, Transform gameplayPanel)
    {
        Transform nameRow = gameplayPanel.Find("PlayerName_Row");
        if (nameRow != null)
            so.FindProperty("playerNameInputField").objectReferenceValue = nameRow.Find("InputField")?.GetComponent<TMP_InputField>();

        Transform statsRow = gameplayPanel.Find("ShowNetworkStats_Row");
        if (statsRow != null)
            so.FindProperty("showNetworkStatsToggle").objectReferenceValue = statsRow.Find("Toggle")?.GetComponent<Toggle>();
    }
}