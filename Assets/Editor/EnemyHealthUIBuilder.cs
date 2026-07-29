using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace NscGame.Enemy
{
    public static class EnemyHealthUIBuilder
    {
        private const string PrefabFolder = "Assets/nok/Enemy/Prefabs";
        private const string PrefabPath = PrefabFolder + "/EnemyHealthUI.prefab";
        private const string FontPath = "Assets/Fonts/Oswald/Oswald-Regular SDF.asset";

        // Premium Color Palette
        private static readonly Color BgColor = new Color(0.04f, 0.07f, 0.11f, 0.75f); // Semi-transparent dark blue/grey
        private static readonly Color CatchUpColor = new Color(0.9f, 0.5f, 0.15f, 1.0f); // Amber catch-up bar
        private static readonly Color MainHealthColor = new Color(0.9f, 0.15f, 0.15f, 1.0f); // Bright red main bar
        private static readonly Color TextWhite = new Color(0.95f, 0.95f, 0.95f, 1.0f);
        private static readonly Color LabelGrey = new Color(0.75f, 0.82f, 0.88f, 0.85f);

        [MenuItem("Tools/NSC/Build Enemy Health UI")]
        public static void Build()
        {
            // Clear existing EnemyHealthUI instances from the active scene first
            EnemyHealthUI[] oldUis = Object.FindObjectsByType<EnemyHealthUI>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (EnemyHealthUI old in oldUis)
            {
                Object.DestroyImmediate(old.gameObject);
            }

            // Create canvas root
            GameObject root = new GameObject("EnemyHealthUI", typeof(RectTransform));
            
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20; // Above player HUD (10) but below GameFlow Panels (50)

            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            root.AddComponent<GraphicRaycaster>();
            EnemyHealthUI uiScript = root.AddComponent<EnemyHealthUI>();

            // Setup main container at the top center
            RectTransform container = CreateRect(root.transform, "BossHealthContainer");
            container.anchorMin = new Vector2(0.5f, 1f);
            container.anchorMax = new Vector2(0.5f, 1f);
            container.pivot = new Vector2(0.5f, 1f);
            container.anchoredPosition = new Vector2(0f, -40f); // 40px down from top
            container.sizeDelta = new Vector2(1000f, 60f); // Long bar

            // 1. Background Frame
            Sprite backgroundSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            Image bgFrame = CreateImage(container, "BackgroundFrame", backgroundSprite, BgColor);
            Stretch(bgFrame.rectTransform);

            // 2. Catch Up Health Bar (Slider underneath)
            GameObject catchUpObj = new GameObject("CatchUpSlider", typeof(RectTransform));
            catchUpObj.transform.SetParent(container, false);
            Slider catchUpSlider = catchUpObj.AddComponent<Slider>();
            catchUpSlider.interactable = false;
            catchUpSlider.transition = Selectable.Transition.None;
            catchUpSlider.navigation = new Navigation { mode = Navigation.Mode.None };
            RectTransform catchUpRect = catchUpObj.GetComponent<RectTransform>();
            Stretch(catchUpRect);
            // Inset slightly to look like a bar inside a border
            catchUpRect.offsetMin = new Vector2(4f, 4f);
            catchUpRect.offsetMax = new Vector2(-4f, -4f);

            GameObject catchUpFillArea = new GameObject("Fill Area", typeof(RectTransform));
            catchUpFillArea.transform.SetParent(catchUpObj.transform, false);
            RectTransform catchUpFillAreaRect = catchUpFillArea.GetComponent<RectTransform>();
            Stretch(catchUpFillAreaRect);
            
            GameObject catchUpFill = new GameObject("Fill", typeof(RectTransform));
            catchUpFill.transform.SetParent(catchUpFillArea.transform, false);
            Image catchUpFillImg = catchUpFill.AddComponent<Image>();
            catchUpFillImg.color = CatchUpColor;
            catchUpFillImg.raycastTarget = false;
            RectTransform catchUpFillRect = catchUpFill.GetComponent<RectTransform>();
            Stretch(catchUpFillRect);
            catchUpSlider.fillRect = catchUpFillRect;

            // 3. Main Health Bar (Slider on top)
            GameObject mainSliderObj = new GameObject("MainHealthSlider", typeof(RectTransform));
            mainSliderObj.transform.SetParent(container, false);
            Slider mainSlider = mainSliderObj.AddComponent<Slider>();
            mainSlider.interactable = false;
            mainSlider.transition = Selectable.Transition.None;
            mainSlider.navigation = new Navigation { mode = Navigation.Mode.None };
            RectTransform mainSliderRect = mainSliderObj.GetComponent<RectTransform>();
            Stretch(mainSliderRect);
            mainSliderRect.offsetMin = new Vector2(4f, 4f);
            mainSliderRect.offsetMax = new Vector2(-4f, -4f);

            GameObject mainFillArea = new GameObject("Fill Area", typeof(RectTransform));
            mainFillArea.transform.SetParent(mainSliderObj.transform, false);
            RectTransform mainFillAreaRect = mainFillArea.GetComponent<RectTransform>();
            Stretch(mainFillAreaRect);

            GameObject mainFill = new GameObject("Fill", typeof(RectTransform));
            mainFill.transform.SetParent(mainFillArea.transform, false);
            Image mainFillImg = mainFill.AddComponent<Image>();
            mainFillImg.color = MainHealthColor;
            mainFillImg.raycastTarget = false;
            RectTransform mainFillRect = mainFill.GetComponent<RectTransform>();
            Stretch(mainFillRect);
            mainSlider.fillRect = mainFillRect;

            // 4. Texts (Name & HP values)
            TMP_FontAsset font = LoadFont();

            // Boss Name Text (Top Left above the bar)
            TextMeshProUGUI nameText = CreateText(container, "BossNameText", "BOSS", font, 24f, TextWhite);
            nameText.alignment = TextAlignmentOptions.Left;
            nameText.rectTransform.anchorMin = new Vector2(0f, 1f);
            nameText.rectTransform.anchorMax = new Vector2(0f, 1f);
            nameText.rectTransform.pivot = new Vector2(0f, 0f);
            nameText.rectTransform.anchoredPosition = new Vector2(4f, 4f);
            nameText.rectTransform.sizeDelta = new Vector2(500f, 30f);
            nameText.fontStyle = FontStyles.Bold;

            // HP Text (Top Right above the bar)
            TextMeshProUGUI hpText = CreateText(container, "HPValueText", "100 / 100", font, 22f, LabelGrey);
            hpText.alignment = TextAlignmentOptions.Right;
            hpText.rectTransform.anchorMin = new Vector2(1f, 1f);
            hpText.rectTransform.anchorMax = new Vector2(1f, 1f);
            hpText.rectTransform.pivot = new Vector2(1f, 0f);
            hpText.rectTransform.anchoredPosition = new Vector2(-4f, 4f);
            hpText.rectTransform.sizeDelta = new Vector2(500f, 30f);

            // Connect references to the script
            SerializedObject so = new SerializedObject(uiScript);
            so.FindProperty("mainHealthSlider").objectReferenceValue = mainSlider;
            so.FindProperty("catchUpHealthSlider").objectReferenceValue = catchUpSlider;
            so.FindProperty("bossNameText").objectReferenceValue = nameText;
            so.FindProperty("hpValueText").objectReferenceValue = hpText;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Save Prefab
            if (!AssetDatabase.IsValidFolder(PrefabFolder))
            {
                // Create intermediate folders if they don't exist
                if (!AssetDatabase.IsValidFolder("Assets/nok"))
                    AssetDatabase.CreateFolder("Assets", "nok");
                if (!AssetDatabase.IsValidFolder("Assets/nok/Enemy"))
                    AssetDatabase.CreateFolder("Assets/nok", "Enemy");
                AssetDatabase.CreateFolder("Assets/nok/Enemy", "Prefabs");
            }

            PrefabUtility.SaveAsPrefabAssetAndConnect(root, PrefabPath, InteractionMode.UserAction);
            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;

            Debug.Log($"[EnemyHealthUI] Successfully built and saved to {PrefabPath} and placed in the active scene!");
        }

        private static TMP_FontAsset LoadFont()
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (font != null) return font;

            // Fallback to searching
            foreach (string guid in AssetDatabase.FindAssets("Oswald t:TMP_FontAsset"))
            {
                font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (font != null) return font;
            }

            return TMP_Settings.defaultFontAsset;
        }

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
            image.type = Image.Type.Sliced;
            image.raycastTarget = false;
            return image;
        }

        private static TextMeshProUGUI CreateText(Transform parent, string name, string text, TMP_FontAsset font, float size, Color color)
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
    }
}
