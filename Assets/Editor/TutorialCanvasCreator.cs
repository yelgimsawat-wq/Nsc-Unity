#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Editor tool: Menu → GameObject → UI → Tutorial Canvas (Auto Setup)
/// Creates the entire Tutorial UI hierarchy with TutorialManager wired up automatically.
/// </summary>
public class TutorialCanvasCreator
{
    [MenuItem("Tools/Create Tutorial Canvas")]
    [MenuItem("GameObject/UI/Tutorial Canvas (Auto Setup)", false, 10)]
    public static void CreateTutorialCanvas()
    {
        // ============================================================
        //  1. Canvas (Screen Space - Overlay, high sort order)
        // ============================================================
        GameObject canvasGO = new GameObject("TutorialCanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100; // on top of everything

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        // ============================================================
        //  2. Tutorial Panel (bottom-right, semi-transparent dark bg)
        // ============================================================
        GameObject panelGO = new GameObject("TutorialPanel");
        panelGO.transform.SetParent(canvasGO.transform, false);

        RectTransform panelRect = panelGO.AddComponent<RectTransform>();
        // Anchor: bottom-right
        panelRect.anchorMin = new Vector2(1f, 0f);
        panelRect.anchorMax = new Vector2(1f, 0f);
        panelRect.pivot     = new Vector2(1f, 0f);
        panelRect.anchoredPosition = new Vector2(-20f, 20f);
        panelRect.sizeDelta = new Vector2(440f, 240f);

        Image panelImage = panelGO.AddComponent<Image>();
        panelImage.color = new Color(0f, 0f, 0f, 0.75f);

        CanvasGroup panelCG = panelGO.AddComponent<CanvasGroup>();
        panelCG.alpha = 0f; // starts hidden

        // ============================================================
        //  3. Counter Text (top of panel, e.g. "1 / 8")
        // ============================================================
        GameObject counterGO = new GameObject("CounterText");
        counterGO.transform.SetParent(panelGO.transform, false);

        RectTransform counterRect = counterGO.AddComponent<RectTransform>();
        counterRect.anchorMin = new Vector2(0f, 1f);
        counterRect.anchorMax = new Vector2(1f, 1f);
        counterRect.pivot     = new Vector2(0.5f, 1f);
        counterRect.anchoredPosition = new Vector2(0f, -8f);
        counterRect.sizeDelta = new Vector2(0f, 28f);

        TextMeshProUGUI counterTMP = counterGO.AddComponent<TextMeshProUGUI>();
        counterTMP.text = "1 / 8";
        counterTMP.fontSize = 14;
        counterTMP.alignment = TextAlignmentOptions.Center;
        counterTMP.color = new Color(0.7f, 0.7f, 0.7f, 1f);

        // ============================================================
        //  4. Step Text (main area)
        // ============================================================
        GameObject stepGO = new GameObject("StepText");
        stepGO.transform.SetParent(panelGO.transform, false);

        RectTransform stepRect = stepGO.AddComponent<RectTransform>();
        // stretch with margins: top 40, bottom 56, left/right 16
        stepRect.anchorMin = Vector2.zero;
        stepRect.anchorMax = Vector2.one;
        stepRect.offsetMin = new Vector2(16f, 56f);  // left, bottom
        stepRect.offsetMax = new Vector2(-16f, -40f); // right, top (negative = inward)

        TextMeshProUGUI stepTMP = stepGO.AddComponent<TextMeshProUGUI>();
        stepTMP.text = "Tutorial text will appear here...";
        stepTMP.fontSize = 18;
        stepTMP.alignment = TextAlignmentOptions.Center;
        stepTMP.color = Color.white;
        stepTMP.enableWordWrapping = true;

        // ============================================================
        //  5. Skip Button (bottom-right of panel)
        // ============================================================
        GameObject btnGO = new GameObject("SkipButton");
        btnGO.transform.SetParent(panelGO.transform, false);

        RectTransform btnRect = btnGO.AddComponent<RectTransform>();
        btnRect.anchorMin = new Vector2(1f, 0f);
        btnRect.anchorMax = new Vector2(1f, 0f);
        btnRect.pivot     = new Vector2(1f, 0f);
        btnRect.anchoredPosition = new Vector2(-12f, 10f);
        btnRect.sizeDelta = new Vector2(130f, 38f);

        Image btnImage = btnGO.AddComponent<Image>();
        btnImage.color = new Color(0.2f, 0.75f, 0.35f, 1f); // green accent

        Button btnComponent = btnGO.AddComponent<Button>();
        ColorBlock cb = btnComponent.colors;
        cb.highlightedColor = new Color(0.25f, 0.85f, 0.4f, 1f);
        cb.pressedColor     = new Color(0.15f, 0.6f, 0.25f, 1f);
        btnComponent.colors = cb;

        // Button label
        GameObject btnLabelGO = new GameObject("Text (TMP)");
        btnLabelGO.transform.SetParent(btnGO.transform, false);

        RectTransform btnLabelRect = btnLabelGO.AddComponent<RectTransform>();
        btnLabelRect.anchorMin = Vector2.zero;
        btnLabelRect.anchorMax = Vector2.one;
        btnLabelRect.offsetMin = Vector2.zero;
        btnLabelRect.offsetMax = Vector2.zero;

        TextMeshProUGUI btnLabelTMP = btnLabelGO.AddComponent<TextMeshProUGUI>();
        btnLabelTMP.text = "Skip  ›";
        btnLabelTMP.fontSize = 18;
        btnLabelTMP.fontStyle = FontStyles.Bold;
        btnLabelTMP.alignment = TextAlignmentOptions.Center;
        btnLabelTMP.color = Color.white;

        // ============================================================
        //  6. TutorialManager component (on the canvas root)
        // ============================================================
        TutorialManager mgr = canvasGO.AddComponent<TutorialManager>();

        // Wire serialized fields via SerializedObject
        SerializedObject so = new SerializedObject(mgr);
        so.FindProperty("tutorialPanel").objectReferenceValue    = panelCG;
        so.FindProperty("stepText").objectReferenceValue         = stepTMP;
        so.FindProperty("counterText").objectReferenceValue      = counterTMP;
        so.FindProperty("skipButton").objectReferenceValue       = btnComponent;
        so.FindProperty("skipButtonText").objectReferenceValue   = btnLabelTMP;
        so.ApplyModifiedProperties();

        // ============================================================
        //  7. Select the new canvas in hierarchy
        // ============================================================
        Undo.RegisterCreatedObjectUndo(canvasGO, "Create Tutorial Canvas");
        Selection.activeGameObject = canvasGO;

        Debug.Log("[TutorialCanvasCreator] ✅ Tutorial Canvas created and fully wired! " +
                  "It will activate when LobbyManager starts the game.");
    }
}
#endif
