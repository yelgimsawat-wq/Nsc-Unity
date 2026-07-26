using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace NscGame.Pvp
{
    /// <summary>
    /// สร้างแผง "เลือกขนาดห้อง" ในฉากเมนู แล้วต่อ reference ให้ OnlineNetworkUI อัตโนมัติ
    ///
    /// เมนู: Tools ▸ NSC ▸ PVP ▸ Build Room Size Panel
    ///
    /// ใช้ตอนไหน: เปิดฉากเมนู (ตัวที่มี OnlineNetworkUI) แล้วสั่งเมนูนี้ครั้งเดียว
    /// ผลลัพธ์: กด Host แล้วจะมีให้เลือก 4 คน (ปกติ) หรือ 8 คน (PVP) ก่อนสร้างห้องจริง
    /// </summary>
    public static class PvpRoomSizePanelBuilder
    {
        private const string FontPath = "Assets/Fonts/Oswald/Oswald-Regular SDF.asset";
        private const string PanelName = "RoomSizePanel";

        private static readonly Color Dim       = new Color(0.02f, 0.03f, 0.06f, 0.85f);
        private static readonly Color CardBg    = new Color(0.08f, 0.11f, 0.16f, 0.98f);
        private static readonly Color CoopBtn   = new Color(0.18f, 0.55f, 0.85f, 1f);
        private static readonly Color PvpBtn    = new Color(0.85f, 0.25f, 0.28f, 1f);
        private static readonly Color CancelBtn = new Color(0.22f, 0.25f, 0.31f, 1f);
        private static readonly Color TextWhite = new Color(0.95f, 0.96f, 0.98f, 1f);
        private static readonly Color TextGrey  = new Color(0.68f, 0.74f, 0.82f, 1f);

        [MenuItem("Tools/NSC/PVP/Build Room Size Panel")]
        public static void Build()
        {
            OnlineNetworkUI ui = Object.FindFirstObjectByType<OnlineNetworkUI>(FindObjectsInactive.Include);
            if (ui == null)
            {
                EditorUtility.DisplayDialog("Room Size Panel",
                    "ฉากนี้ไม่มี OnlineNetworkUI\n\nต้องเปิดฉากเมนูก่อน (ฉากที่มีปุ่ม Create Room / Join)",
                    "เข้าใจแล้ว");
                return;
            }

            SerializedObject so = new SerializedObject(ui);

            // หา Canvas ที่จะเอาแผงไปแปะ — ใช้ตัวเดียวกับ connectPanel จะได้สเกลตรงกัน
            GameObject connectPanel = so.FindProperty("connectPanel").objectReferenceValue as GameObject;
            Canvas canvas = connectPanel != null
                ? connectPanel.GetComponentInParent<Canvas>()
                : Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);

            if (canvas == null)
            {
                EditorUtility.DisplayDialog("Room Size Panel",
                    "หา Canvas ในฉากไม่เจอ — ต่อช่อง connectPanel ใน OnlineNetworkUI ก่อน",
                    "เข้าใจแล้ว");
                return;
            }

            // ล้างของเดิมกันสร้างซ้อน
            Transform existing = canvas.transform.Find(PanelName);
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

            // ---------- แผงคลุมจอ ----------
            GameObject panel = CreateUiObject(canvas.transform, PanelName);
            Stretch(panel.GetComponent<RectTransform>());
            AddImage(panel, Dim);
            panel.AddComponent<CanvasGroup>();
            panel.transform.SetAsLastSibling(); // ต้องอยู่บนสุด ไม่งั้นโดนปุ่มเมนูบัง
            Undo.RegisterCreatedObjectUndo(panel, "Create Room Size Panel");

            // ---------- การ์ดกลางจอ ----------
            GameObject card = CreateUiObject(panel.transform, "Card");
            Anchor(card.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(920f, 460f));
            AddImage(card, CardBg);

            TextMeshProUGUI title = CreateText(card.transform, "Title", "ROOM SIZE", font, 56, TextWhite);
            Anchor(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(800f, 70f));

            TextMeshProUGUI hint = CreateText(card.transform, "Hint",
                "เลือกขนาดห้อง — เปลี่ยนทีหลังไม่ได้", font, 24, TextGrey);
            Anchor(hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(800f, 40f));

            // ---------- ปุ่มสองตัวเลือก ----------
            Button btn4 = CreateBigButton(card.transform, "Room4Button", "4 PLAYERS", "Co-op / Boss / Parkour",
                CoopBtn, font, new Vector2(-215f, -10f));
            Button btn8 = CreateBigButton(card.transform, "Room8Button", "8 PLAYERS", "PVP — 2 ทีม ทีมละ 4",
                PvpBtn, font, new Vector2(215f, -10f));

            Button cancel = CreateBigButton(card.transform, "CancelRoomSizeButton", "CANCEL", null,
                CancelBtn, font, new Vector2(0f, -175f), new Vector2(300f, 60f), 26);

            // ---------- ต่อ reference ----------
            so.FindProperty("roomSizePanel").objectReferenceValue = panel;
            so.FindProperty("room4Button").objectReferenceValue = btn4;
            so.FindProperty("room8Button").objectReferenceValue = btn8;
            so.FindProperty("cancelRoomSizeButton").objectReferenceValue = cancel;
            so.ApplyModifiedProperties();

            panel.SetActive(false); // ซ่อนไว้ก่อน เปิดตอนกด Host

            Selection.activeGameObject = panel;
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            EditorUtility.DisplayDialog("Room Size Panel",
                "เสร็จแล้ว!\n\n" +
                "กด Host ในเกมจะมีให้เลือก 4 คน หรือ 8 คน (PVP) ก่อนสร้างห้อง\n\n" +
                "อย่าลืมตั้งช่อง Pvp Scene Name ให้ตรงกับชื่อฉาก PVP\n" +
                "และเพิ่มฉากนั้นเข้า Build Settings",
                "โอเค");
        }

        #region Primitives

        private static Button CreateBigButton(Transform parent, string name, string label, string subLabel,
            Color color, TMP_FontAsset font, Vector2 position, Vector2? size = null, int fontSize = 34)
        {
            GameObject go = CreateUiObject(parent, name);
            Anchor(go.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), position, size ?? new Vector2(400f, 180f));

            Image bg = AddImage(go, color);
            Button button = go.AddComponent<Button>();
            button.targetGraphic = bg;
            button.transition = Selectable.Transition.ColorTint;

            ColorBlock colors = button.colors;
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.fadeDuration = 0.12f;
            button.colors = colors;

            TextMeshProUGUI text = CreateText(go.transform, "Label", label, font, fontSize, TextWhite);
            if (string.IsNullOrEmpty(subLabel))
            {
                Stretch(text.rectTransform);
            }
            else
            {
                Anchor(text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 18f), new Vector2(380f, 60f));

                TextMeshProUGUI sub = CreateText(go.transform, "SubLabel", subLabel, font, 20,
                    new Color(1f, 1f, 1f, 0.75f));
                Anchor(sub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -28f), new Vector2(380f, 40f));
            }

            return button;
        }

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
            text.raycastTarget = false; // ไม่งั้นข้อความบังคลิกของปุ่มที่มันอยู่ข้างใน
            if (font != null) text.font = font;
            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        #endregion
    }
}
