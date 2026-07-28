using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace NscGame.Pvp
{
    /// <summary>
    /// สร้าง UI ของโหมด PVP ทั้งชุดในฉากปัจจุบัน แล้วต่อ reference ให้อัตโนมัติ
    /// (ต่อ reference มือเปล่า ~20 ช่องมันเสียเวลาและพลาดง่าย)
    ///
    /// เมนู: Tools ▸ NSC ▸ PVP ▸ Build PVP UI
    /// </summary>
    public static class PvpUIBuilder
    {
        private const string FontPath = "Assets/Fonts/Oswald/Oswald-Regular SDF.asset";
        private const string PlayerHudPrefabPath = "Assets/Yelmee/PlayerUI/PlayerHUD.prefab";

        private static readonly Color PanelBg   = new Color(0.03f, 0.05f, 0.09f, 0.92f);
        private static readonly Color CardBg    = new Color(0.08f, 0.11f, 0.16f, 0.95f);
        private static readonly Color RedTeam   = new Color(0.90f, 0.22f, 0.24f, 1f);
        private static readonly Color BlueTeam  = new Color(0.20f, 0.55f, 0.95f, 1f);
        private static readonly Color LimbBtn   = new Color(0.16f, 0.20f, 0.27f, 1f);
        private static readonly Color StartBtn  = new Color(0.18f, 0.70f, 0.35f, 1f);
        private static readonly Color TextWhite = new Color(0.95f, 0.96f, 0.98f, 1f);
        private static readonly Color TextGrey  = new Color(0.70f, 0.76f, 0.83f, 1f);

        [MenuItem("Tools/NSC/PVP/Build PVP UI")]
        public static void Build()
        {
            // ล้างของเดิมในฉากก่อน กันซ้อนกันหลายชุด
            foreach (PvpTeamSelectUI old in Object.FindObjectsByType<PvpTeamSelectUI>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(old.gameObject);

            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

            GameObject root = new GameObject("PvpUI", typeof(RectTransform));
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;

            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            root.AddComponent<GraphicRaycaster>();

            PvpTeamSelectUI selectUI = root.AddComponent<PvpTeamSelectUI>();
            PvpResultUI resultUI = root.AddComponent<PvpResultUI>();

            BuildTeamSelectPanel(root.transform, selectUI, font);
            BuildResultBanner(root.transform, resultUI, font);

            EnsureEventSystem();
            EnsurePlayerHud();

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Debug.Log("[PVP] สร้าง PVP UI เสร็จแล้ว — เหลือแค่ใส่ PvpTeamManager + PvpRobotTeam ให้หุ่นสองตัว " +
                      "(ดู Assets/nok/PVP/README_PVP.md)");
        }

        #region Team Select Panel

        private static void BuildTeamSelectPanel(Transform parent, PvpTeamSelectUI ui, TMP_FontAsset font)
        {
            GameObject panel = CreateUiObject(parent, "TeamSelectPanel");
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            Stretch(panelRect);
            AddImage(panel, PanelBg);
            panel.AddComponent<CanvasGroup>();
            ui.selectionPanel = panel;

            // --- หัวข้อ ---
            TextMeshProUGUI title = CreateText(panel.transform, "Title", "CHOOSE YOUR TEAM", font, 64, TextWhite);
            RectTransform titleRect = title.rectTransform;
            Anchor(titleRect, new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(900f, 80f));

            // --- การ์ดสองทีม ---
            Button redButton = BuildTeamCard(panel.transform, "RedTeamCard", "RED TEAM", RedTeam, font,
                new Vector2(-320f, 40f), out TextMeshProUGUI redRoster);
            Button blueButton = BuildTeamCard(panel.transform, "BlueTeamCard", "BLUE TEAM", BlueTeam, font,
                new Vector2(320f, 40f), out TextMeshProUGUI blueRoster);

            ui.redTeamButton  = redButton;
            ui.blueTeamButton = blueButton;
            ui.redRosterText  = redRoster;
            ui.blueRosterText = blueRoster;

            // --- แถวปุ่มชิ้นส่วน ---
            TextMeshProUGUI limbHeader = CreateText(panel.transform, "LimbHeader",
                "SELECT YOUR PART", font, 28, TextGrey);
            Anchor(limbHeader.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -170f), new Vector2(800f, 40f));

            ui.limbButtons = new Button[PvpLimb.Count];
            ui.limbLabels  = new TextMeshProUGUI[PvpLimb.Count];

            const float limbWidth = 230f;
            const float limbGap = 16f;
            float totalWidth = PvpLimb.Count * limbWidth + (PvpLimb.Count - 1) * limbGap;
            float startX = -totalWidth * 0.5f + limbWidth * 0.5f;

            for (int i = 0; i < PvpLimb.Count; i++)
            {
                float x = startX + i * (limbWidth + limbGap);
                Button limbButton = CreateButton(panel.transform, $"Limb_{PvpLimb.Name(i).Replace(" ", "")}",
                    PvpLimb.Name(i), font, 26, LimbBtn, TextWhite,
                    new Vector2(0.5f, 0.5f), new Vector2(x, -235f), new Vector2(limbWidth, 70f),
                    out TextMeshProUGUI limbLabel);

                ui.limbButtons[i] = limbButton;
                ui.limbLabels[i]  = limbLabel;
            }

            // --- สถานะ ---
            TextMeshProUGUI status = CreateText(panel.transform, "StatusText",
                "CHOOSE YOUR TEAM", font, 30, TextWhite);
            Anchor(status.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 200f), new Vector2(1200f, 50f));
            ui.statusText = status;

            // --- ปุ่ม START (Host เท่านั้น) ---
            Button startButton = CreateButton(panel.transform, "StartButton", "START FIGHT", font, 34,
                StartBtn, TextWhite,
                new Vector2(0.5f, 0f), new Vector2(0f, 110f), new Vector2(420f, 74f),
                out TextMeshProUGUI startLabel);
            ui.startButton = startButton;
            ui.startButtonLabel = startLabel;
        }

        private static Button BuildTeamCard(Transform parent, string name, string title, Color teamColor,
            TMP_FontAsset font, Vector2 position, out TextMeshProUGUI rosterText)
        {
            GameObject card = CreateUiObject(parent, name);
            RectTransform rect = card.GetComponent<RectTransform>();
            Anchor(rect, new Vector2(0.5f, 0.5f), position, new Vector2(560f, 300f));

            Image bg = AddImage(card, CardBg);
            Button button = card.AddComponent<Button>();
            button.targetGraphic = bg;
            button.transition = Selectable.Transition.ColorTint;

            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.fadeDuration = 0.12f;
            button.colors = colors;

            // แถบสีทีมด้านบนการ์ด
            GameObject stripe = CreateUiObject(card.transform, "TeamStripe");
            RectTransform stripeRect = stripe.GetComponent<RectTransform>();
            stripeRect.anchorMin = new Vector2(0f, 1f);
            stripeRect.anchorMax = new Vector2(1f, 1f);
            stripeRect.pivot = new Vector2(0.5f, 1f);
            stripeRect.anchoredPosition = Vector2.zero;
            stripeRect.sizeDelta = new Vector2(0f, 70f);
            AddImage(stripe, teamColor);

            TextMeshProUGUI titleText = CreateText(stripe.transform, "Title", title, font, 38, Color.white);
            Stretch(titleText.rectTransform);
            titleText.alignment = TextAlignmentOptions.Center;

            rosterText = CreateText(card.transform, "Roster", "<i>Empty</i>", font, 24, TextGrey);
            RectTransform rosterRect = rosterText.rectTransform;
            Stretch(rosterRect);
            rosterRect.offsetMin = new Vector2(24f, 24f);
            rosterRect.offsetMax = new Vector2(-24f, -84f);
            rosterText.alignment = TextAlignmentOptions.TopLeft;

            return button;
        }

        #endregion

        #region HUD

        /// <summary>
        /// ป้ายประกาศผู้ชนะอย่างเดียว — ไม่มีหลอดเลือดในนี้
        /// เลือดใช้ PlayerHUD เดิม (HullRingUI/LimbStatusUI) ที่ผูกกับหุ่นของผู้เล่นเอง
        /// </summary>
        private static void BuildResultBanner(Transform parent, PvpResultUI ui, TMP_FontAsset font)
        {
            GameObject result = CreateUiObject(parent, "ResultPanel");
            Anchor(result.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 220f));
            AddImage(result, PanelBg);

            TextMeshProUGUI resultText = CreateText(result.transform, "ResultText", "WINNER", font, 72, TextWhite);
            Stretch(resultText.rectTransform);
            resultText.alignment = TextAlignmentOptions.Center;

            ui.resultPanel = result;
            ui.resultText = resultText;
            result.SetActive(false);
        }

        /// <summary>
        /// วาง PlayerHUD เดิมลงฉากถ้ายังไม่มี แล้วแปะ PvpPlayerHudGate ให้
        /// (เกตเดิมรอ LobbyManager ซึ่งฉาก PVP ห้ามมี → HUD จะโผล่ทับหน้าจอเลือกทีม)
        /// </summary>
        private static void EnsurePlayerHud()
        {
            LocalRobotBinder existing = Object.FindFirstObjectByType<LocalRobotBinder>(FindObjectsInactive.Include);
            GameObject hud;

            if (existing != null)
            {
                hud = existing.gameObject;
            }
            else
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerHudPrefabPath);
                if (prefab == null)
                {
                    Debug.LogWarning($"[PVP] หา PlayerHUD prefab ไม่เจอที่ {PlayerHudPrefabPath} — " +
                                     "ต้องลากเข้าฉากเองแล้วแปะ PvpPlayerHudGate");
                    return;
                }

                hud = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                Undo.RegisterCreatedObjectUndo(hud, "Add PlayerHUD");
            }

            if (hud.GetComponent<CanvasGroup>() == null) hud.AddComponent<CanvasGroup>();
            if (hud.GetComponent<PvpPlayerHudGate>() == null)
                Undo.AddComponent<PvpPlayerHudGate>(hud);

            // บอก LocalRobotBinder ว่าหุ่นไหนของเรา — ไม่มีตัวนี้ HUD จะไปเกาะหุ่นศัตรูได้
            if (hud.GetComponent<PvpHudRobotBinder>() == null)
                Undo.AddComponent<PvpHudRobotBinder>(hud);
        }

        #endregion

        #region Primitives

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
            image.raycastTarget = true;
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

        private static Button CreateButton(Transform parent, string name, string labelText,
            TMP_FontAsset font, float fontSize, Color bgColor, Color textColor,
            Vector2 anchor, Vector2 position, Vector2 size, out TextMeshProUGUI label)
        {
            GameObject go = CreateUiObject(parent, name);
            Anchor(go.GetComponent<RectTransform>(), anchor, position, size);

            Image bg = AddImage(go, bgColor);
            Button button = go.AddComponent<Button>();
            button.targetGraphic = bg;
            button.transition = Selectable.Transition.ColorTint;

            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.5f);
            colors.fadeDuration = 0.12f;
            button.colors = colors;

            label = CreateText(go.transform, "Label", labelText, font, fontSize, textColor);
            Stretch(label.rectTransform);

            return button;
        }

        private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
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
}
