using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace NscGame.Pvp
{
    /// <summary>
    /// สร้างแผง "เลือกจำนวนคน" ในฉากเมนู แล้วต่อ reference ให้ OnlineNetworkUI อัตโนมัติ
    ///
    /// เมนู: Tools ▸ NSC ▸ PVP ▸ Build Room Size Panel
    ///
    /// ใช้ตอนไหน: เปิดฉากเมนู (ตัวที่มี OnlineNetworkUI) แล้วสั่งเมนูนี้ครั้งเดียว
    /// ผลลัพธ์: กด Host แล้วจะมีให้เลือก 4 คน (ปกติ) หรือ 8 คน (PVP) ก่อนสร้างห้องจริง
    ///
    /// หน้าตายกมาจากจอ "Select Mode" (Map Select Panel) ทั้งดุ้น — ฉากมืดคลุมจอ,
    /// หัวข้อกลางบน, ปุ่มแบนเนอร์สองตัวซ้าย-ขวา, ปุ่ม BACK มุมขวาล่างที่เดิม
    /// ต่างกันแค่สไปรท์ปุ่มที่ใช้ชีต 4Player / 8Player(PVP)
    /// </summary>
    public static class PvpRoomSizePanelBuilder
    {
        private const string FontPath = "Assets/Fonts/Oswald/Oswald-Regular SDF.asset";
        private const string PanelName = "RoomSizePanel";

        // ชีตปุ่มที่วาดมาให้แล้ว — ชิ้นบน = 4Player, ชิ้นล่าง = 8Player(PVP)
        private const string PlayerCountSheetPath = "Assets/Yelmee/UI/Play (5).png";
        private const string Room4SpriteName = "Play (5)_0";
        private const string Room8SpriteName = "Play (5)_1";

        // ปุ่ม BACK ตัวเดียวกับที่เมนูใช้ วางทับตำแหน่งเดิมเป๊ะ จะได้ดูเหมือนปุ่มไม่ขยับไปไหน
        private const string BackSheetPath = "Assets/MenuUI/Play/10.png";
        private const string BackSpriteName = "10_0";

        // ตัวเลขทั้งหมดวัดจาก Map Select Panel ในฉากเมนู (BOSS/PAKOUR อยู่ที่ ±460, สูง ~198)
        // Canvas เป็น Constant Pixel Size → เลขพวกนี้คือพิกเซลจริงบนจอ 1920×1080
        private static readonly Vector2 ButtonSize = new Vector2(600f, 210f);
        private static readonly Vector2 ButtonOffset = new Vector2(420f, 0f);
        private static readonly Vector2 BackSize = new Vector2(202f, 73f);
        private static readonly Vector2 BackPosition = new Vector2(748f, -434f);
        private const float TitleY = 324f;
        private const float HintY = 252f;
        private const float CaptionY = -145f;

        private static readonly Color Dim       = new Color(0f, 0f, 0f, 0.945f); // เท่ากับ RawImage ของจอเลือกโหมด
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

            // ชิ้นบนของชีต = 4 คน, ชิ้นล่าง = 8 คน (เผื่อถูกตัดใหม่จนชื่อไม่ตรง จึงมี fallback เรียงตามตำแหน่ง)
            Sprite room4Sprite = LoadSheetSprite(PlayerCountSheetPath, Room4SpriteName, 0);
            Sprite room8Sprite = LoadSheetSprite(PlayerCountSheetPath, Room8SpriteName, 1);
            Sprite backSprite = LoadSheetSprite(BackSheetPath, BackSpriteName, 0);

            // ---------- แผงคลุมจอ ----------
            GameObject panel = CreateUiObject(canvas.transform, PanelName);
            Stretch(panel.GetComponent<RectTransform>());
            AddImage(panel, Dim); // ทึบเกือบดำเหมือนจอเลือกโหมด + กันคลิกทะลุไปโดนปุ่ม HOST/JOIN ข้างหลัง
            panel.AddComponent<CanvasGroup>();
            panel.transform.SetAsLastSibling(); // ต้องอยู่บนสุด ไม่งั้นโดนปุ่มเมนูบัง
            Undo.RegisterCreatedObjectUndo(panel, "Create Room Size Panel");

            // ---------- หัวข้อ ----------
            // ขนาดเท่าหัวข้อ "Select Mode" (36 × สเกล 2.27 ≈ 82)
            TextMeshProUGUI title = CreateText(panel.transform, "Title", "Select Players", font, 82, TextWhite);
            title.fontStyle = FontStyles.Bold;
            Anchor(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, TitleY), new Vector2(1200f, 110f));

            // ฟอนต์ Oswald ไม่มีสระไทย ใส่ไทยแล้วขึ้นเป็นสี่เหลี่ยม — ข้อความในแผงนี้เลยเป็นอังกฤษล้วน
            TextMeshProUGUI hint = CreateText(panel.transform, "Hint",
                "Room size is locked once the room is created", font, 28, TextGrey);
            Anchor(hint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, HintY), new Vector2(1200f, 44f));

            // ---------- ปุ่มสองตัวเลือก ----------
            Button btn4 = CreateBannerButton(panel.transform, "Room4Button", room4Sprite,
                new Vector2(-ButtonOffset.x, ButtonOffset.y), "4 PLAYERS", CoopBtn, font);
            Button btn8 = CreateBannerButton(panel.transform, "Room8Button", room8Sprite,
                new Vector2(ButtonOffset.x, ButtonOffset.y), "8 PLAYERS", PvpBtn, font);

            CreateCaption(panel.transform, "Room4Caption", "CO-OP  /  BOSS  /  PARKOUR", font,
                new Vector2(-ButtonOffset.x, ButtonOffset.y + CaptionY));
            CreateCaption(panel.transform, "Room8Caption", "PVP  —  2 TEAMS OF 4", font,
                new Vector2(ButtonOffset.x, ButtonOffset.y + CaptionY));

            // ---------- ปุ่มถอย ----------
            // วางทับปุ่ม BACK ของเมนูพอดี — คนเล่นเห็นปุ่มเดิมที่เดิม แต่กดแล้วปิดแผงนี้แทน
            // (ปุ่ม BACK จริงอยู่ใต้ฉากมืด กดไม่โดนอยู่แล้ว)
            // ยืดเต็มกรอบ (ไม่ preserveAspect) เพราะปุ่ม BACK ตัวจริงในฉากก็โดนสเกลยืดแบบนี้ ถึงจะทับกันสนิท
            Button cancel = CreateBannerButton(panel.transform, "CancelRoomSizeButton", backSprite,
                BackPosition, "BACK", CancelBtn, font, BackSize, 26, false);

            // ---------- ต่อ reference ----------
            so.FindProperty("roomSizePanel").objectReferenceValue = panel;
            so.FindProperty("room4Button").objectReferenceValue = btn4;
            so.FindProperty("room8Button").objectReferenceValue = btn8;
            so.FindProperty("cancelRoomSizeButton").objectReferenceValue = cancel;
            so.ApplyModifiedProperties();

            panel.SetActive(false); // ซ่อนไว้ก่อน เปิดตอนกด Host

            Selection.activeGameObject = panel;
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            bool spritesMissing = room4Sprite == null || room8Sprite == null || backSprite == null;
            EditorUtility.DisplayDialog("Room Size Panel",
                "เสร็จแล้ว!\n\n" +
                "กด Host ในเกมจะมีให้เลือก 4 คน หรือ 8 คน (PVP) ก่อนสร้างห้อง\n\n" +
                (spritesMissing
                    ? "⚠ หาสไปรท์ปุ่มไม่เจอ เลยใช้ปุ่มสีล้วนไปก่อน\n" +
                      $"เช็กว่า {PlayerCountSheetPath} ตั้ง Sprite Mode = Multiple แล้วตัดเป็น 2 ชิ้นหรือยัง\n\n"
                    : "") +
                "อย่าลืมตั้งช่อง Pvp Scene Name ให้ตรงกับชื่อฉาก PVP\n" +
                "และเพิ่มฉากนั้นเข้า Build Settings",
                "โอเค");
        }

        #region Primitives

        /// <summary>
        /// ปุ่มแบนเนอร์สไปรท์ทรงเดียวกับ HOST / JOIN / BOSS — ไม่มีตัวหนังสือซ้อน เพราะตัวอักษรอยู่ในรูปแล้ว
        /// หาสไปรท์ไม่เจอค่อยตกไปเป็นปุ่มสีล้วน + ป้ายข้อความ จะได้ยังกดสร้างห้องได้อยู่
        /// </summary>
        private static Button CreateBannerButton(Transform parent, string name, Sprite sprite,
            Vector2 position, string fallbackLabel, Color fallbackColor, TMP_FontAsset font,
            Vector2? size = null, int fallbackFontSize = 40, bool preserveAspect = true)
        {
            GameObject go = CreateUiObject(parent, name);
            Anchor(go.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), position, size ?? ButtonSize);

            Image image = AddImage(go, Color.white);
            Button button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;

            // ค่าเดียวกับปุ่ม HOST/JOIN ในฉาก — ตอนรัน OnlineNetworkUI จะทับด้วยสีเขียวของเมนูอยู่ดี
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(0.96f, 0.96f, 0.96f, 1f);
            colors.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            colors.selectedColor = new Color(0.96f, 0.96f, 0.96f, 1f);
            colors.fadeDuration = 0.1f;
            button.colors = colors;

            if (sprite != null)
            {
                image.sprite = sprite;
                image.preserveAspect = preserveAspect; // สองชิ้นในชีตสัดส่วนต่างกันนิดหน่อย ปล่อยให้มันจัดเอง
                return button;
            }

            image.color = fallbackColor;
            TextMeshProUGUI label = CreateText(go.transform, "Label", fallbackLabel, font, fallbackFontSize, TextWhite);
            Stretch(label.rectTransform);
            return button;
        }

        private static void CreateCaption(Transform parent, string name, string content,
            TMP_FontAsset font, Vector2 position)
        {
            TextMeshProUGUI caption = CreateText(parent, name, content, font, 26, TextGrey);
            Anchor(caption.rectTransform, new Vector2(0.5f, 0.5f), position, new Vector2(ButtonSize.x, 40f));
        }

        /// <summary>
        /// ดึงสไปรท์ชิ้นย่อยจากชีต — เอาชื่อก่อน ถ้าไม่ตรง (โดนตัดใหม่/เปลี่ยนชื่อ) ค่อยไล่จากบนลงล่าง
        /// </summary>
        private static Sprite LoadSheetSprite(string sheetPath, string spriteName, int fallbackIndex)
        {
            List<Sprite> sprites = new List<Sprite>();
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(sheetPath))
            {
                if (asset is Sprite sprite) sprites.Add(sprite);
            }

            if (sprites.Count == 0)
            {
                Debug.LogWarning($"[RoomSizePanel] ไม่เจอสไปรท์ใน {sheetPath} " +
                                 "— ตั้ง Texture Type = Sprite, Sprite Mode = Multiple แล้วตัดเป็นชิ้นๆ ก่อน");
                return null;
            }

            foreach (Sprite sprite in sprites)
            {
                if (sprite.name == spriteName) return sprite;
            }

            sprites.Sort((a, b) => b.rect.y.CompareTo(a.rect.y)); // ชิ้นบนสุดของรูปมาก่อน
            return fallbackIndex < sprites.Count ? sprites[fallbackIndex] : sprites[0];
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
