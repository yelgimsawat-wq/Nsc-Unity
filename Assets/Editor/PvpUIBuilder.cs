using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace NscGame.Pvp
{
    /// <summary>
    /// สร้าง UI ของโหมด PVP ทั้งชุดในฉากปัจจุบัน แล้วต่อ reference ให้อัตโนมัติ
    /// (ต่อ reference มือเปล่า ~60 ช่องมันเสียเวลาและพลาดง่าย)
    ///
    /// เมนู: Tools ▸ NSC ▸ PVP ▸ Build PVP UI
    ///
    /// เลย์เอาต์: การ์ดผู้เล่นสองข้าง + หุ่นประกอบกลางจอ + ช่องเสียบชิ้นส่วนข้างหุ่น + แถบสถานะล่าง
    ///
    /// หุ่นกลางจอประกอบจากสไปรท์ Assets/MenuUI/Lobby/ — ลำตัวจาก Robot.png, แขนจาก Arm.png,
    /// ขาจาก Leg.png ทั้งสามไฟล์เป็นแคนวาสขนาดเดียวกันและชิ้นส่วนอยู่ตำแหน่งกายวิภาคเดิม
    /// จึงวางตาม rect ของสไปรท์ตรงๆ แล้วมันประกอบกลับเอง
    ///
    /// กรอบทุกอันใช้สไปรท์ 9-slice ที่ PvpUiSpriteFactory เจนไว้ (มุมตัด 45° + เส้นเรือง)
    /// </summary>
    public static class PvpUIBuilder
    {
        private const string FontPath = "Assets/Fonts/Oswald/Oswald-Regular SDF.asset";
        private const string PlayerHudPrefabPath = "Assets/Yelmee/PlayerUI/PlayerHUD.prefab";

        private const string RobotSheetPath = "Assets/MenuUI/Lobby/Robot.png";
        private const string ArmSheetPath   = "Assets/MenuUI/Lobby/Arm.png";
        private const string LegSheetPath   = "Assets/MenuUI/Lobby/Leg.png";
        private const string YellowBtnPath  = "Assets/Yelmee/UI/BottonYellow.png";

        // ---------- สี ----------
        private static readonly Color ScreenBg    = new Color(0.012f, 0.016f, 0.024f, 0.985f);
        private static readonly Color PanelFill   = new Color(0.031f, 0.043f, 0.063f, 0.88f);
        private static readonly Color SlotFill    = new Color(0.055f, 0.071f, 0.098f, 0.92f);
        private static readonly Color HeadFill    = new Color(0.043f, 0.055f, 0.082f, 0.92f);
        private static readonly Color EdgeSoft    = new Color(0.45f, 0.55f, 0.70f, 0.30f);
        private static readonly Color EdgeFaint   = new Color(0.45f, 0.55f, 0.70f, 0.18f);
        private static readonly Color RedTeam     = new Color(0.90f, 0.24f, 0.28f, 1f);
        private static readonly Color BlueTeam    = new Color(0.24f, 0.58f, 0.98f, 1f);
        private static readonly Color TextWhite   = new Color(0.94f, 0.96f, 0.99f, 1f);
        private static readonly Color TextGrey    = new Color(0.60f, 0.67f, 0.77f, 1f);
        private static readonly Color TextDim     = new Color(0.38f, 0.44f, 0.53f, 1f);
        private static readonly Color LimbNeutral = new Color(0.55f, 0.60f, 0.68f, 1f);
        private static readonly Color SocketIdle  = new Color(0.45f, 0.55f, 0.70f, 0.45f);

        // ---------- เลย์เอาต์ (อ้างอิงจอ 1920×1080, พิกัดนับจากกลางจอ) ----------
        private const float TitleY       = 470f;
        private const float TitleHeight  = 112f;
        private const float PanelX       = 690f;
        private const float PanelY       = 5f;
        private const float PanelWidth   = 470f;
        private const float PanelHeight  = 640f;
        private const float TeamLabelY   = 372f;
        private const float SlotWidth    = 430f;
        private const float SlotHeight   = 140f;
        private const float SlotGap      = 15f;
        private const float RobotY       = 46f;
        private const float RobotScale   = 0.258f;
        private const float SocketX      = 345f;
        private const float SocketSize   = 104f;
        private const float ArmRowY      = 105f;  // ใช้เฉพาะตอนหาสไปรท์ไม่เจอ — ปกติอ่านจากตำแหน่งชิ้นส่วนจริง
        private const float LegRowY      = -152f;
        private const float StatusY      = -330f;
        private const float FooterY      = -425f;

        [MenuItem("Tools/NSC/PVP/Build PVP UI")]
        public static void Build()
        {
            PvpUiSpriteFactory.EnsureGenerated();

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

            bool artMissing = BuildTeamSelectPanel(root.transform, selectUI, font);
            BuildResultBanner(root.transform, resultUI, font);

            EnsureEventSystem();
            EnsurePlayerHud();

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Debug.Log("[PVP] สร้าง PVP UI เสร็จแล้ว — เหลือแค่ใส่ PvpTeamManager + PvpRobotTeam ให้หุ่นสองตัว " +
                      "(ดู Assets/nok/PVP/README_PVP.md)");

            if (artMissing)
                Debug.LogWarning("[PVP] หาสไปรท์หุ่นใน Assets/MenuUI/Lobby/ ไม่ครบ — ใช้กล่องสีแทนไปก่อน " +
                                 "เช็กว่า Robot/Arm/Leg.png ตั้ง Sprite Mode = Multiple แล้วตัดชิ้นแล้วหรือยัง");
        }

        #region Team Select Panel

        /// <returns>true = หาสไปรท์หุ่นไม่ครบ (ใช้ของแทนไปก่อน)</returns>
        private static bool BuildTeamSelectPanel(Transform parent, PvpTeamSelectUI ui, TMP_FontAsset font)
        {
            GameObject panel = CreateUiObject(parent, "TeamSelectPanel");
            Stretch(panel.GetComponent<RectTransform>());
            AddImage(panel, ScreenBg);
            panel.AddComponent<CanvasGroup>();
            ui.selectionPanel = panel;

            // ---------- หัวข้อ ----------
            GameObject titleBar = CreatePanelBox(panel.transform, "TitleBar", new Vector2(0f, TitleY),
                new Vector2(780f, TitleHeight), HeadFill, EdgeSoft, Frame.Full);

            TextMeshProUGUI title = CreateText(titleBar.transform, "Title", "SELECT PARTS - PVP",
                font, 52, TextWhite);
            Anchor(title.rectTransform, Center, new Vector2(0f, 20f), new Vector2(740f, 62f));
            title.fontStyle = FontStyles.Bold;
            title.characterSpacing = 2f;

            // ขีดสั้นๆ ซ้ายขวาหัวข้อ ให้ดูเป็นแถบ HUD
            CreateTick(panel.transform, "TitleTickLeft", new Vector2(-455f, TitleY), new Vector2(80f, 3f));
            CreateTick(panel.transform, "TitleTickRight", new Vector2(455f, TitleY), new Vector2(80f, 3f));

            // บรรทัดรองอยู่ในกรอบหัวข้อเลย — ย้ายลงมาข้างล่างจะไปเบียดหัวหุ่น
            TextMeshProUGUI subtitle = CreateText(titleBar.transform, "Subtitle", "2 TEAMS  /  8 PLAYERS",
                font, 25, TextGrey);
            Anchor(subtitle.rectTransform, Center, new Vector2(0f, -32f), new Vector2(700f, 34f));
            subtitle.characterSpacing = 7f;
            ui.subtitleText = subtitle;

            // ---------- การ์ดผู้เล่นสองข้าง ----------
            ui.redSlots = BuildTeamColumn(panel.transform, "RedTeamColumn", "RED TEAM", RedTeam, -PanelX,
                1, font, out Button redButton, out Image redHeader);
            ui.blueSlots = BuildTeamColumn(panel.transform, "BlueTeamColumn", "BLUE TEAM", BlueTeam, PanelX,
                5, font, out Button blueButton, out Image blueHeader);

            ui.redTeamButton  = redButton;
            ui.blueTeamButton = blueButton;
            ui.redTeamHeader  = redHeader;
            ui.blueTeamHeader = blueHeader;

            // roster เก่าเป็นก้อนข้อความ — ตอนนี้ใช้การ์ดรายคนแทน
            ui.redRosterText  = null;
            ui.blueRosterText = null;

            // ---------- หุ่น + ช่องเสียบชิ้นส่วน ----------
            bool artMissing = BuildRobotAssembly(panel.transform, ui, font);

            // ---------- แถบล่าง ----------
            BuildFooter(panel.transform, ui, font);

            return artMissing;
        }

        /// <summary>คอลัมน์ทีมหนึ่งข้าง — ชื่อทีม+ตราด้านบนกดเพื่อเข้าทีม, ข้างล่างเป็นช่องผู้เล่น 4 ช่อง</summary>
        private static PvpPlayerSlotView[] BuildTeamColumn(Transform parent, string name, string teamName,
            Color teamColor, float x, int firstSlotNumber, TMP_FontAsset font,
            out Button joinButton, out Image headerImage)
        {
            GameObject column = CreateUiObject(parent, name);
            Anchor(column.GetComponent<RectTransform>(), Center, Vector2.zero, new Vector2(PanelWidth, 1000f));

            // --- แถบชื่อทีม (กดเพื่อเข้าทีม) วางลอยเหนือการ์ด เหมือนในดีไซน์ ---
            GameObject header = CreateUiObject(column.transform, "TeamHeader");
            Anchor(header.GetComponent<RectTransform>(), Center, new Vector2(x, TeamLabelY),
                new Vector2(PanelWidth, 64f));

            headerImage = AddImage(header, new Color(teamColor.r, teamColor.g, teamColor.b, 0.10f));
            headerImage.sprite = LoadHudSprite(PvpUiSpriteFactory.FillPath);
            headerImage.type = Image.Type.Sliced;

            joinButton = header.AddComponent<Button>();
            joinButton.targetGraphic = headerImage;
            joinButton.transition = Selectable.Transition.ColorTint;
            ApplyButtonColors(joinButton);

            AddFrame(header.transform, teamColor, Frame.Corners);

            GameObject emblem = CreateUiObject(header.transform, "Emblem");
            Anchor(emblem.GetComponent<RectTransform>(), Center, new Vector2(-135f, 0f), new Vector2(46f, 46f));
            Image emblemImage = AddImage(emblem, teamColor);
            emblemImage.sprite = LoadHudSprite(PvpUiSpriteFactory.EmblemPath);
            emblemImage.preserveAspect = true;
            emblemImage.raycastTarget = false;

            TextMeshProUGUI headerText = CreateText(header.transform, "Title", teamName, font, 34, teamColor);
            Anchor(headerText.rectTransform, Center, new Vector2(45f, 0f), new Vector2(300f, 50f));
            headerText.fontStyle = FontStyles.Bold;
            headerText.characterSpacing = 6f;
            headerText.alignment = TextAlignmentOptions.Left;

            // --- การ์ดรวมช่องผู้เล่น ---
            GameObject card = CreatePanelBox(column.transform, "SlotPanel", new Vector2(x, PanelY),
                new Vector2(PanelWidth, PanelHeight), PanelFill,
                new Color(teamColor.r, teamColor.g, teamColor.b, 0.45f), Frame.Full);

            int slotCount = 4;
            float firstY = PanelHeight * 0.5f - 18f - SlotHeight * 0.5f;

            PvpPlayerSlotView[] slots = new PvpPlayerSlotView[slotCount];
            for (int i = 0; i < slotCount; i++)
            {
                float y = firstY - i * (SlotHeight + SlotGap);
                slots[i] = BuildPlayerSlot(card.transform, $"Slot{firstSlotNumber + i}",
                    firstSlotNumber + i, new Vector2(0f, y), teamColor, font);
            }

            return slots;
        }

        /// <summary>ช่องผู้เล่นหนึ่งคน: [เลข] [รูป] ชื่อ / ชิ้นส่วน / สถานะ</summary>
        private static PvpPlayerSlotView BuildPlayerSlot(Transform parent, string name, int number,
            Vector2 position, Color teamColor, TMP_FontAsset font)
        {
            PvpPlayerSlotView slot = new PvpPlayerSlotView();

            GameObject row = CreatePanelBox(parent, name, position, new Vector2(SlotWidth, SlotHeight),
                SlotFill, EdgeFaint, Frame.Full);
            slot.root = row;
            slot.background = row.GetComponent<Image>();

            Transform frameTransform = row.transform.Find("Frame");
            slot.frame = frameTransform != null ? frameTransform.GetComponent<Image>() : null;

            // เลขประจำช่อง
            GameObject chip = CreateUiObject(row.transform, "IndexChip");
            Anchor(chip.GetComponent<RectTransform>(), Center, new Vector2(-192f, 0f), new Vector2(40f, 40f));
            slot.accent = AddImage(chip, new Color(teamColor.r, teamColor.g, teamColor.b, 0.30f));
            slot.accent.sprite = LoadHudSprite(PvpUiSpriteFactory.FillPath);
            slot.accent.type = Image.Type.Sliced;
            slot.accent.raycastTarget = false;

            slot.indexLabel = CreateText(chip.transform, "Number", number.ToString(), font, 24, TextWhite);
            Stretch(slot.indexLabel.rectTransform);

            // กรอบรูปผู้เล่น — ยังไม่มีรูปจริง ใช้ไอคอนคนไปก่อน
            GameObject avatar = CreateUiObject(row.transform, "Avatar");
            Anchor(avatar.GetComponent<RectTransform>(), Center, new Vector2(-130f, 0f), new Vector2(84f, 84f));
            Image avatarBg = AddImage(avatar, new Color(0.09f, 0.11f, 0.15f, 1f));
            avatarBg.sprite = LoadHudSprite(PvpUiSpriteFactory.FillPath);
            avatarBg.type = Image.Type.Sliced;
            avatarBg.raycastTarget = false;
            slot.avatarFrame = AddFrame(avatar.transform, EdgeFaint, Frame.Corners);

            GameObject person = CreateUiObject(avatar.transform, "Person");
            Anchor(person.GetComponent<RectTransform>(), Center, Vector2.zero, new Vector2(52f, 52f));
            Image personImage = AddImage(person, TextDim);
            personImage.sprite = LoadHudSprite(PvpUiSpriteFactory.PersonPath);
            personImage.preserveAspect = true;
            personImage.raycastTarget = false;
            slot.avatarIcon = personImage;

            // ชื่อผู้เล่น
            slot.nameLabel = CreateText(row.transform, "Name", "EMPTY", font, 30, TextWhite);
            Anchor(slot.nameLabel.rectTransform, Center, new Vector2(45f, 28f), new Vector2(250f, 40f));
            slot.nameLabel.alignment = TextAlignmentOptions.Left;
            slot.nameLabel.fontStyle = FontStyles.Bold;

            // ไอคอน + ชื่อชิ้นส่วน
            GameObject partIcon = CreateUiObject(row.transform, "PartIcon");
            Anchor(partIcon.GetComponent<RectTransform>(), Center, new Vector2(-62f, -26f), new Vector2(26f, 48f));
            slot.partIcon = AddImage(partIcon, new Color(1f, 1f, 1f, 0f));
            slot.partIcon.preserveAspect = true;
            slot.partIcon.raycastTarget = false;

            slot.partLabel = CreateText(row.transform, "Part", "-", font, 20, TextGrey);
            Anchor(slot.partLabel.rectTransform, Center, new Vector2(45f, -26f), new Vector2(150f, 30f));
            slot.partLabel.alignment = TextAlignmentOptions.Left;
            slot.partLabel.characterSpacing = 4f;

            // สถานะ + ติ๊กถูก
            slot.stateLabel = CreateText(row.transform, "State", "", font, 20, TextDim);
            Anchor(slot.stateLabel.rectTransform, Center, new Vector2(118f, -26f), new Vector2(120f, 30f));
            slot.stateLabel.alignment = TextAlignmentOptions.Right;
            slot.stateLabel.characterSpacing = 3f;

            GameObject check = CreateUiObject(row.transform, "ReadyIcon");
            Anchor(check.GetComponent<RectTransform>(), Center, new Vector2(196f, -26f), new Vector2(26f, 26f));
            slot.readyIcon = AddImage(check, new Color(1f, 1f, 1f, 0f));
            slot.readyIcon.sprite = LoadHudSprite(PvpUiSpriteFactory.CheckPath);
            slot.readyIcon.preserveAspect = true;
            slot.readyIcon.raycastTarget = false;

            return slot;
        }

        /// <summary>
        /// หุ่นกลางจอ + ช่องเสียบชิ้นส่วนสี่ช่องข้างตัว
        /// ตำแหน่ง/ขนาดของชิ้นส่วนอ่านจาก rect ของสไปรท์เอง จึงประกอบกลับเป็นหุ่นเต็มตัวอัตโนมัติ
        /// กดได้ทั้งที่ตัวหุ่นและที่ช่องเสียบ — ปุ่มสองตัวสั่งงานชิ้นเดียวกัน
        /// </summary>
        private static bool BuildRobotAssembly(Transform parent, PvpTeamSelectUI ui, TMP_FontAsset font)
        {
            GameObject rig = CreateUiObject(parent, "RobotAssembly");
            Anchor(rig.GetComponent<RectTransform>(), Center, new Vector2(0f, RobotY), new Vector2(920f, 800f));

            Sprite torso = LoadSheetSprite(RobotSheetPath, "Robot_0", 0);
            Sprite armL  = LoadSheetSprite(ArmSheetPath, "Arm_0", 0);
            Sprite armR  = LoadSheetSprite(ArmSheetPath, "Arm_2", 1);
            Sprite legL  = LoadSheetSprite(LegSheetPath, "Leg_4", 0);
            Sprite legR  = LoadSheetSprite(LegSheetPath, "Leg_5", 1);

            bool missing = torso == null || armL == null || armR == null || legL == null || legR == null;

            // ลำตัว — โชว์อย่างเดียว กดไม่ได้ (ไม่มีบทบาท "ลำตัว" ให้เลือก)
            GameObject torsoGo = CreateUiObject(rig.transform, "Torso");
            Image torsoImage = AddImage(torsoGo, new Color(0.88f, 0.91f, 0.96f, 1f));
            torsoImage.raycastTarget = false;
            torsoImage.preserveAspect = true;
            if (torso != null)
            {
                torsoImage.sprite = torso;
                Anchor(torsoGo.GetComponent<RectTransform>(), Center,
                    SpriteLocalPosition(torso, RobotScale), SpriteLocalSize(torso, RobotScale));
            }
            else
            {
                Anchor(torsoGo.GetComponent<RectTransform>(), Center, new Vector2(0f, 130f), new Vector2(200f, 300f));
            }
            ui.torsoIcon = torsoImage;

            Sprite[] limbSprites = { armL, armR, legL, legR };
            Vector2[] fallbackPos = {
                new Vector2(-150f, 95f), new Vector2(150f, 95f),
                new Vector2(-100f, -135f), new Vector2(80f, -135f)
            };
            Vector2[] fallbackSize = {
                new Vector2(124f, 260f), new Vector2(124f, 260f),
                new Vector2(152f, 347f), new Vector2(152f, 347f)
            };

            ui.limbButtons     = new Button[PvpLimb.Count];
            ui.limbAltButtons  = new Button[PvpLimb.Count];
            ui.limbLabels      = new TextMeshProUGUI[PvpLimb.Count];
            ui.limbIcons       = new Image[PvpLimb.Count];
            ui.limbOwnerLabels = new TextMeshProUGUI[PvpLimb.Count];
            ui.limbSocketFrames = new Image[PvpLimb.Count];
            ui.limbSocketIcons  = new Image[PvpLimb.Count];
            ui.limbPartSprites  = limbSprites;

            for (int i = 0; i < PvpLimb.Count; i++)
            {
                Sprite sprite = limbSprites[i];
                string limbName = PvpLimb.Name(i);
                string safeName = limbName.Replace(" ", "");
                bool leftSide = i == PvpLimb.LeftArm || i == PvpLimb.LeftLeg;
                bool isArm = PvpLimb.IsArm(i);
                float sideSign = leftSide ? -1f : 1f;

                // ตำแหน่ง/ขนาดของชิ้นส่วนบนตัวหุ่น — ช่องเสียบกับเส้นโยงอ้างอิงค่านี้
                // จะได้ขยับตามเวลาปรับ RobotScale โดยไม่ต้องไล่แก้เลขทีละอัน
                Vector2 limbPos = sprite != null ? SpriteLocalPosition(sprite, RobotScale) : fallbackPos[i];
                Vector2 limbSize = sprite != null ? SpriteLocalSize(sprite, RobotScale) : fallbackSize[i];
                float rowY = sprite != null ? limbPos.y : (isArm ? ArmRowY : LegRowY);

                // --- ชิ้นส่วนบนตัวหุ่น (กดได้) ---
                GameObject node = CreateUiObject(rig.transform, $"Limb_{safeName}");
                Image image = AddImage(node, LimbNeutral);
                image.preserveAspect = true;
                if (sprite != null) image.sprite = sprite;
                Anchor(node.GetComponent<RectTransform>(), Center, limbPos, limbSize);

                Button altButton = node.AddComponent<Button>();
                altButton.targetGraphic = image;
                altButton.transition = Selectable.Transition.None; // สีถูกคุมโดย RefreshLimbButtons เอง
                ui.limbAltButtons[i] = altButton;
                ui.limbIcons[i] = image;

                // --- ช่องเสียบข้างหุ่น (ปุ่มหลัก) ---
                GameObject socket = CreateUiObject(rig.transform, $"Socket_{safeName}");
                Anchor(socket.GetComponent<RectTransform>(), Center,
                    new Vector2(sideSign * SocketX, rowY), new Vector2(SocketSize, SocketSize));

                Image socketFill = AddImage(socket, new Color(0.055f, 0.071f, 0.098f, 0.75f));
                socketFill.sprite = LoadHudSprite(PvpUiSpriteFactory.FillPath);
                socketFill.type = Image.Type.Sliced;

                Button socketButton = socket.AddComponent<Button>();
                socketButton.targetGraphic = socketFill;
                socketButton.transition = Selectable.Transition.None;
                ui.limbButtons[i] = socketButton;

                ui.limbSocketFrames[i] = AddFrame(socket.transform, SocketIdle, Frame.Corners);

                GameObject socketIcon = CreateUiObject(socket.transform, "Icon");
                Anchor(socketIcon.GetComponent<RectTransform>(), Center, Vector2.zero, new Vector2(52f, 76f));
                Image socketIconImage = AddImage(socketIcon, LimbNeutral);
                socketIconImage.preserveAspect = true;
                socketIconImage.raycastTarget = false;
                if (sprite != null) socketIconImage.sprite = sprite;
                ui.limbSocketIcons[i] = socketIconImage;

                // --- เส้นโยงจากช่องเสียบเข้าหาตัวหุ่น (เริ่มจากขอบนอกของชิ้นส่วนจริง) ---
                float lineInner = Mathf.Abs(limbPos.x) + limbSize.x * 0.5f + 10f;
                float lineOuter = SocketX - SocketSize * 0.5f;
                float lineLength = Mathf.Max(20f, lineOuter - lineInner);
                CreateTick(rig.transform, $"Link_{safeName}",
                    new Vector2(sideSign * (lineInner + lineLength * 0.5f), rowY),
                    new Vector2(lineLength, 2f));

                // --- ป้ายชื่อชิ้นส่วน + เจ้าของ ---
                TextMeshProUGUI label = CreateText(rig.transform, $"Label_{safeName}",
                    limbName.ToUpperInvariant(), font, 22, TextGrey);
                Anchor(label.rectTransform, Center,
                    new Vector2(sideSign * SocketX, rowY + SocketSize * 0.5f + 24f), new Vector2(200f, 30f));
                label.characterSpacing = 4f;
                ui.limbLabels[i] = label;

                TextMeshProUGUI owner = CreateText(rig.transform, $"Owner_{safeName}",
                    "", font, 20, TextDim);
                Anchor(owner.rectTransform, Center,
                    new Vector2(sideSign * SocketX, rowY - SocketSize * 0.5f - 22f), new Vector2(200f, 28f));
                ui.limbOwnerLabels[i] = owner;
            }

            return missing;
        }

        private static void BuildFooter(Transform parent, PvpTeamSelectUI ui, TMP_FontAsset font)
        {
            // --- ข้อความสถานะ (บรรทัดเดียวเหนือแถบล่าง) ---
            TextMeshProUGUI status = CreateText(parent, "StatusText", "CHOOSE YOUR TEAM", font, 28, TextWhite);
            Anchor(status.rectTransform, Center, new Vector2(0f, StatusY), new Vector2(1000f, 44f));
            ui.statusText = status;

            // --- กล่อง TIPS ซ้ายล่าง ---
            GameObject tips = CreatePanelBox(parent, "TipsBox", new Vector2(-PanelX, FooterY),
                new Vector2(PanelWidth, 130f), PanelFill, EdgeFaint, Frame.Corners);

            TextMeshProUGUI tipsTitle = CreateText(tips.transform, "TipsTitle", "TIPS", font, 20, TextGrey);
            Anchor(tipsTitle.rectTransform, Center, new Vector2(0f, 38f), new Vector2(PanelWidth - 44f, 28f));
            tipsTitle.alignment = TextAlignmentOptions.Left;
            tipsTitle.characterSpacing = 8f;

            TextMeshProUGUI tipsBody = CreateText(tips.transform, "TipsBody",
                "Each part can only be selected by one player.\nWork with your team and dominate the battlefield!",
                font, 19, TextDim);
            Anchor(tipsBody.rectTransform, Center, new Vector2(0f, -14f), new Vector2(PanelWidth - 44f, 62f));
            tipsBody.alignment = TextAlignmentOptions.TopLeft;

            // --- แถบจำนวนผู้เล่นกลางล่าง ---
            GameObject counter = CreatePanelBox(parent, "PlayerCounter", new Vector2(0f, FooterY),
                new Vector2(620f, 130f), PanelFill, EdgeSoft, Frame.Full);

            GameObject peopleIcon = CreateUiObject(counter.transform, "PeopleIcon");
            Anchor(peopleIcon.GetComponent<RectTransform>(), Center, new Vector2(-220f, 20f), new Vector2(42f, 42f));
            Image people = AddImage(peopleIcon, TextGrey);
            people.sprite = LoadHudSprite(PvpUiSpriteFactory.PersonPath);
            people.preserveAspect = true;
            people.raycastTarget = false;

            TextMeshProUGUI connected = CreateText(counter.transform, "Connected",
                "0 / 8  PLAYERS CONNECTED", font, 34, TextWhite);
            Anchor(connected.rectTransform, Center, new Vector2(48f, 20f), new Vector2(470f, 46f));
            connected.fontStyle = FontStyles.Bold;
            connected.alignment = TextAlignmentOptions.Left;
            ui.playersConnectedText = connected;

            TextMeshProUGUI hint = CreateText(counter.transform, "Hint",
                "PRESS START WHEN ALL PLAYERS ARE READY", font, 19, TextDim);
            Anchor(hint.rectTransform, Center, new Vector2(0f, -30f), new Vector2(580f, 30f));
            hint.characterSpacing = 3f;
            ui.playersHintText = hint;

            // --- ปุ่ม START ขวาล่าง ---
            Sprite yellow = LoadSheetSprite(YellowBtnPath, "Play (4)_0", 0);

            GameObject startGo = CreateUiObject(parent, "StartButton");
            Anchor(startGo.GetComponent<RectTransform>(), Center, new Vector2(PanelX, FooterY),
                new Vector2(440f, 112f));

            Image startImage = AddImage(startGo, yellow != null ? Color.white : new Color(0.18f, 0.70f, 0.35f, 1f));
            if (yellow != null)
            {
                startImage.sprite = yellow;
                startImage.preserveAspect = true;
            }

            Button startButton = startGo.AddComponent<Button>();
            startButton.targetGraphic = startImage;
            startButton.transition = Selectable.Transition.ColorTint;
            ApplyButtonColors(startButton);

            TextMeshProUGUI startLabel = CreateText(startGo.transform, "Label", "START", font, 42,
                new Color(0.99f, 0.86f, 0.25f, 1f));
            Anchor(startLabel.rectTransform, Center, Vector2.zero, new Vector2(400f, 70f));
            startLabel.fontStyle = FontStyles.Bold | FontStyles.Italic;

            ui.startButton = startButton;
            ui.startButtonLabel = startLabel;
        }

        #endregion

        #region HUD

        /// <summary>
        /// หน้าจอจบแมตช์: VICTORY/DEFEAT + ปุ่มออกกลับเมนู (โครงเดียวกับหน้าจบของโหมดบอส)
        ///
        /// ⚠️ ไม่มีหลอดเลือดในนี้ — เลือดใช้ PlayerHUD เดิม (HullRingUI/LimbStatusUI)
        ///    ที่ผูกกับหุ่นของผู้เล่นเอง
        ///
        /// ฉากมืดคลุมทั้งจอเป็นลูกของ panel แต่ตัวที่เด้ง (popTarget) คือ Content ข้างใน
        /// ไม่งั้นตอนเด้งจาก 0.8 เท่า ฉากมืดจะย่อตามจนเห็นขอบจอโผล่
        /// </summary>
        private static void BuildResultBanner(Transform parent, PvpResultUI ui, TMP_FontAsset font)
        {
            GameObject result = CreateUiObject(parent, "ResultPanel");
            Stretch(result.GetComponent<RectTransform>());

            // ฉากมืด — กินคลิกด้วย กันกดทะลุไปโดน UI ข้างหลัง
            AddImage(result, new Color(0.01f, 0.015f, 0.025f, 0.82f));
            CanvasGroup group = result.AddComponent<CanvasGroup>();

            GameObject content = CreateUiObject(result.transform, "Content");
            Stretch(content.GetComponent<RectTransform>());

            // ---------- กล่องผลการแข่ง ----------
            GameObject box = CreatePanelBox(content.transform, "ResultBox", new Vector2(0f, 70f),
                new Vector2(940f, 300f), new Color(0.02f, 0.03f, 0.05f, 0.95f), EdgeSoft, Frame.Full);

            TextMeshProUGUI resultText = CreateText(box.transform, "ResultText", "VICTORY", font, 96, TextWhite);
            Anchor(resultText.rectTransform, Center, new Vector2(0f, 52f), new Vector2(880f, 140f));
            resultText.fontStyle = FontStyles.Bold;
            resultText.characterSpacing = 6f;

            TextMeshProUGUI subtitle = CreateText(box.transform, "Subtitle", "RED TEAM WINS", font, 34, TextGrey);
            Anchor(subtitle.rectTransform, Center, new Vector2(0f, -66f), new Vector2(880f, 52f));
            subtitle.characterSpacing = 8f;

            // ---------- ปุ่มล่างกล่อง ----------
            Button exitButton = CreateActionButton(content.transform, "ExitButton", "EXIT TO MENU",
                new Vector2(-180f, -175f), TextWhite, font);

            Button rematchButton = CreateActionButton(content.transform, "RematchButton", "REMATCH",
                new Vector2(180f, -175f), new Color(0.99f, 0.86f, 0.25f, 1f), font);

            // client เห็นข้อความนี้แทนปุ่ม REMATCH (เล่นใหม่ได้เฉพาะ Host)
            TextMeshProUGUI waiting = CreateText(content.transform, "WaitingForHostText",
                "WAITING FOR HOST...", font, 24, TextDim);
            Anchor(waiting.rectTransform, Center, new Vector2(180f, -175f), new Vector2(340f, 50f));
            waiting.characterSpacing = 4f;

            ui.resultPanel = result;
            ui.resultGroup = group;
            ui.popTarget = content.transform;
            ui.resultText = resultText;
            ui.subtitleText = subtitle;
            ui.exitButton = exitButton;
            ui.rematchButton = rematchButton;
            ui.waitingForHostText = waiting.gameObject;

            result.SetActive(false);
        }

        /// <summary>ปุ่มสี่เหลี่ยมมุมตัดพร้อมป้ายข้อความ — ใช้กับปุ่มบนหน้าจบแมตช์</summary>
        private static Button CreateActionButton(Transform parent, string name, string label,
            Vector2 position, Color labelColor, TMP_FontAsset font)
        {
            GameObject go = CreateUiObject(parent, name);
            Anchor(go.GetComponent<RectTransform>(), Center, position, new Vector2(340f, 96f));

            Image fill = AddImage(go, new Color(0.055f, 0.071f, 0.098f, 0.95f));
            fill.sprite = LoadHudSprite(PvpUiSpriteFactory.FillPath);
            fill.type = Image.Type.Sliced;

            Button button = go.AddComponent<Button>();
            button.targetGraphic = fill;
            button.transition = Selectable.Transition.ColorTint;
            ApplyButtonColors(button);

            AddFrame(go.transform, EdgeSoft, Frame.Full);

            TextMeshProUGUI text = CreateText(go.transform, "Label", label, font, 30, labelColor);
            Anchor(text.rectTransform, Center, Vector2.zero, new Vector2(320f, 60f));
            text.fontStyle = FontStyles.Bold;
            text.characterSpacing = 3f;

            return button;
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

        private static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

        private enum Frame { Full, Corners }

        /// <summary>กล่องพื้นหลัง 9-slice มุมตัด + กรอบซ้อนบน — โครงหลักของทุกแผงในหน้านี้</summary>
        private static GameObject CreatePanelBox(Transform parent, string name, Vector2 position, Vector2 size,
            Color fill, Color border, Frame frame)
        {
            GameObject go = CreateUiObject(parent, name);
            Anchor(go.GetComponent<RectTransform>(), Center, position, size);

            Image image = AddImage(go, fill);
            image.sprite = LoadHudSprite(PvpUiSpriteFactory.FillPath);
            image.type = Image.Type.Sliced;

            AddFrame(go.transform, border, frame);
            return go;
        }

        /// <summary>กรอบเส้น (หรือวงเล็บมุม) ซ้อนทับกล่อง — แยกชิ้นเพื่อย้อมสีทีหลังได้อิสระจากพื้น</summary>
        private static Image AddFrame(Transform parent, Color color, Frame frame)
        {
            GameObject go = CreateUiObject(parent, "Frame");
            Stretch(go.GetComponent<RectTransform>());

            Image image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            image.sprite = LoadHudSprite(frame == Frame.Full
                ? PvpUiSpriteFactory.FramePath
                : PvpUiSpriteFactory.CornersPath);
            image.type = Image.Type.Sliced;
            return image;
        }

        /// <summary>ขีดตกแต่งเส้นบางๆ — ใช้เป็นเส้นโยงและขีดข้างหัวข้อ</summary>
        private static void CreateTick(Transform parent, string name, Vector2 position, Vector2 size)
        {
            GameObject go = CreateUiObject(parent, name);
            Anchor(go.GetComponent<RectTransform>(), Center, position, size);

            Image image = go.AddComponent<Image>();
            image.color = EdgeSoft;
            image.raycastTarget = false;
        }

        private static void ApplyButtonColors(Button button)
        {
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.4f);
            colors.fadeDuration = 0.12f;
            button.colors = colors;
        }

        private static Sprite LoadHudSprite(string path)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>ตำแหน่งของสไปรท์เทียบกับกึ่งกลางเท็กซ์เจอร์ — ใช้ประกอบชิ้นส่วนหุ่นให้กลับเข้าที่เดิม</summary>
        private static Vector2 SpriteLocalPosition(Sprite sprite, float scale)
        {
            Rect r = sprite.rect;
            float cx = r.x + r.width * 0.5f - sprite.texture.width * 0.5f;
            float cy = r.y + r.height * 0.5f - sprite.texture.height * 0.5f;
            return new Vector2(cx, cy) * scale;
        }

        private static Vector2 SpriteLocalSize(Sprite sprite, float scale)
        {
            return new Vector2(sprite.rect.width, sprite.rect.height) * scale;
        }

        /// <summary>ดึงสไปรท์ชิ้นย่อยจากชีต — เอาชื่อก่อน ถ้าไม่ตรงค่อยไล่จากซ้ายไปขวา</summary>
        private static Sprite LoadSheetSprite(string sheetPath, string spriteName, int fallbackIndex)
        {
            List<Sprite> sprites = new List<Sprite>();
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(sheetPath))
            {
                if (asset is Sprite sprite) sprites.Add(sprite);
            }

            if (sprites.Count == 0) return null;

            foreach (Sprite sprite in sprites)
            {
                if (sprite.name == spriteName) return sprite;
            }

            sprites.Sort((a, b) => a.rect.x.CompareTo(b.rect.x));
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
}
