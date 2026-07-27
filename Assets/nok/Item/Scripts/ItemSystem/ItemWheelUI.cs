using UnityEngine;
using UnityEngine.UI;

namespace NscUnity.Items
{
    /// <summary>
    /// วงล้อเลือกไอเทมสไตล์เกนชิน — กด Tab ค้าง วงล้อเด้งขึ้นมากลางจอ
    /// เลื่อนเมาส์ไปทางไอเทมที่ต้องการ แล้วปล่อย Tab เพื่อสลับไอเทมนั้นขึ้นมือ
    ///
    /// UI ทั้งหมดถูกสร้างด้วยโค้ดตอนรันไทม์ ไม่ต้องเตรียม Canvas หรือ Prefab ใดๆ
    /// แค่แปะสคริปต์นี้ไว้บน Empty GameObject ในฉาก แล้วลาก PlayerInventory มาใส่
    ///
    /// ⚠️ เคยมีระบบสโลว์โมชัน (ลด Time.timeScale + Time.fixedDeltaTime ตอนวงล้อเปิด) แต่เอาออกแล้ว
    /// เพราะทำให้เกมค้าง — ฉากนี้มีฟิสิกส์เยอะ (ข้อต่อ/ragdoll หุ่นยนต์) การลด fixedDeltaTime ลงพร้อมกับ
    /// timeScale ทำให้ Unity ต้องเรียก FixedUpdate ถี่ขึ้นมากเพื่อไล่ตามเวลาจริง จนเข้า spiral of death ค้างไปเลย
    /// </summary>
    public class ItemWheelUI : MonoBehaviour
    {
        [Header("การเชื่อมต่อ")]
        [SerializeField] private PlayerInventory inventory;

        [Header("ปุ่ม")]
        [SerializeField] private KeyCode wheelKey = KeyCode.Tab;
        [Tooltip("เปิด = กดค้างเพื่อเปิดวงล้อ ปล่อยเพื่อเลือก (แบบเกนชิน) / ปิด = กดเปิด กดปิด")]
        [SerializeField] private bool holdToOpen = true;
        [Tooltip("ให้ล้อเมาส์สลับไอเทมได้ด้วยตอนไม่ได้เปิดวงล้อ")]
        [SerializeField] private bool scrollWheelSwitching = true;

        [Header("แอนิเมชัน")]
        [SerializeField, Range(0.02f, 0.5f)] private float openDuration = 0.12f;

        [Header("รูปทรงวงล้อ (หน่วยอิงจอ 1920x1080)")]
        [SerializeField] private float outerRadius = 270f;
        [SerializeField, Range(0.2f, 0.8f)] private float innerRadiusRatio = 0.52f;
        [Tooltip("ช่องว่างระหว่างชิ้นวงล้อ (องศา)")]
        [SerializeField, Range(0f, 15f)] private float gapDegrees = 3f;
        [Tooltip("รัศมีวงกลางที่ถือว่า 'ไม่เลือกอะไร' — เลื่อนเมาส์เข้ามาตรงนี้แล้วปล่อยคือยกเลิก")]
        [SerializeField] private float deadZoneRadius = 70f;
        [SerializeField] private float iconSize = 74f;

        [Header("สี")]
        [SerializeField] private Color segmentColor = new Color(0.07f, 0.09f, 0.13f, 0.80f);
        [SerializeField] private Color segmentEquippedColor = new Color(0.32f, 0.37f, 0.46f, 0.88f);
        [SerializeField] private Color dimColor = new Color(0f, 0f, 0f, 0.72f);
        [SerializeField] private Color emptySlotIconColor = new Color(1f, 1f, 1f, 0.12f);

        [Header("ตัวอักษร")]
        [Tooltip("ลากไฟล์ฟอนต์ .ttf ที่มีภาษาไทยมาใส่ ถ้าเว้นว่างจะใช้ฟอนต์ติดตั้งมากับ Unity (ไทยจะเป็นสี่เหลี่ยม)")]
        [SerializeField] private Font labelFont;

        /// <summary>มีวงล้อบานไหนเปิดอยู่หรือเปล่า — สคริปต์อื่นใช้เช็คเพื่อบล็อกปุ่มยิง/เก็บของ</summary>
        public static bool IsAnyOpen => openCount > 0;
        private static int openCount;

        // เคลียร์ค่า static ทุกครั้งที่เริ่มเล่นใหม่ เผื่อโปรเจกต์ปิด Domain Reload ไว้
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => openCount = 0;

        private sealed class SlotView
        {
            public Image segment;
            public RectTransform iconPivot;
            public Image icon;
            public float hover;      // ค่าที่ไล่เข้าหา 0 หรือ 1 เพื่อทำแอนิเมชันนุ่มๆ
            public float baseAngle;  // องศาตามเข็มนาฬิกาจากด้านบน
        }

        private Canvas canvas;
        private CanvasGroup group;
        private RectTransform wheelRoot;
        private Text nameText;
        private Text descriptionText;
        private Text hintText;
        private SlotView[] views;

        private bool isOpen;
        private float openAmount;
        private int hoveredIndex = -1;

        private CursorLockMode previousCursorLock;
        private bool previousCursorVisible;

        private bool isInitialized;

        private void Awake()
        {
            TryInitialize();
        }

        /// <summary>
        /// ในเกมออนไลน์ ผู้เล่น (NetworkObject) สปอว์นหลังฉากโหลดเสร็จ — ตอน Awake ของ UI นี้อาจยังไม่มี
        /// PlayerInventory ของเราเกิดขึ้นเลย จึงต้องลองหาใหม่ทุกเฟรมใน Update จนกว่าจะเจอ แล้วค่อย build UI จริง
        /// ต้องหาเฉพาะของ "ตัวเราเอง" (IsOwner) ไม่ใช่ตัวแรกที่เจอ ไม่งั้นอาจไปคุมกระเป๋าของผู้เล่นคนอื่น
        /// </summary>
        private void TryInitialize()
        {
            if (isInitialized) return;

            inventory = FindOwnedInventory();
            if (inventory == null) return;

            BuildUI();
            RefreshSlots();

            inventory.OnSlotsChanged += RefreshSlots;
            inventory.OnEquippedChanged += HandleEquippedChanged;

            group.alpha = 0f;
            group.gameObject.SetActive(false);

            isInitialized = true;
        }

        /// <summary>
        /// ฝั่ง Client ข้อมูลช่องเก็บของ (NetworkList) ซิงค์มาถึงช้ากว่าตอนที่ UI เริ่มทำงาน —
        /// วงล้อที่สร้างไปตอนแรกเลยได้จำนวนช่องผิด (มักเป็น 0 หรือ 2 ช่อง) ต้องคอยเช็คแล้วสร้างใหม่เมื่อค่าจริงมาถึง
        /// </summary>
        private void RebuildIfSlotCountChanged()
        {
            if (views == null) return;

            int actual = Mathf.Max(2, inventory.SlotCount);
            if (actual == views.Length) return;

            if (isOpen) Close(false);

            if (canvas != null) Destroy(canvas.gameObject);

            BuildUI();
            RefreshSlots();

            group.alpha = 0f;
            group.gameObject.SetActive(false);
            openAmount = 0f;
        }

        /// <summary>หากระเป๋าของ "แขนที่เราคุมอยู่จริง" — ดูรายละเอียดเรื่องกับดัก IsOwner ที่ LocalItemOwner</summary>
        private PlayerInventory FindOwnedInventory() => LocalItemOwner.Find<PlayerInventory>();

        /// <summary>
        /// LobbyManager โอน ownership ของแขนให้ผู้เล่น "หลัง" กดเริ่มเกม และผู้เล่นเปลี่ยนชิ้นที่เลือกได้
        /// ระหว่างอยู่ในลอบบี้ — ต้องคอยเช็คทุกเฟรมว่ายังผูกอยู่กับแขนที่เราคุมจริงหรือเปล่า
        /// (แนวทางเดียวกับ LocalRobotBinder.NeedsRebind ที่โปรเจกต์นี้ใช้อยู่แล้ว)
        /// </summary>
        private void RebindIfOwnershipChanged()
        {
            PlayerInventory owned = FindOwnedInventory();
            if (owned == null || owned == inventory) return;

            if (isOpen) Close(false);

            if (inventory != null)
            {
                inventory.OnSlotsChanged -= RefreshSlots;
                inventory.OnEquippedChanged -= HandleEquippedChanged;
            }

            inventory = owned;
            inventory.OnSlotsChanged += RefreshSlots;
            inventory.OnEquippedChanged += HandleEquippedChanged;

            RefreshSlots();
        }

        private void HandleEquippedChanged(int index) => RefreshSlots();

        private void OnDestroy()
        {
            if (inventory != null)
            {
                inventory.OnSlotsChanged -= RefreshSlots;
                inventory.OnEquippedChanged -= HandleEquippedChanged;
            }

            // กันเวลาค้างช้าถ้าวัตถุถูกทำลายตอนวงล้อยังเปิดอยู่
            if (isOpen)
            {
                isOpen = false;
                openCount = Mathf.Max(0, openCount - 1);
                RestoreWorldState();
            }
        }

        private void OnDisable()
        {
            if (isOpen) Close(false);
        }

        // ==========================================================
        // Input
        // ==========================================================

        private void Update()
        {
            if (!isInitialized)
            {
                TryInitialize();
                if (!isInitialized) return;
            }

            RebindIfOwnershipChanged();
            RebuildIfSlotCountChanged();

            HandleOpenCloseInput();

            if (isOpen) UpdateHoveredIndex();
            else if (scrollWheelSwitching) HandleScrollSwitching();

            AnimateVisuals(Time.unscaledDeltaTime);
        }

        private void HandleOpenCloseInput()
        {
            if (holdToOpen)
            {
                if (InputCompat.GetKeyDown(wheelKey))
                {
                    Open();
                }
                else if (isOpen && InputCompat.GetKeyUp(wheelKey))
                {
                    Close(true);
                }
                else if (isOpen && !InputCompat.GetKey(wheelKey))
                {
                    // กันวงล้อค้างเวลาสลับหน้าต่างแล้ว Unity ไม่ได้รับ event ปล่อยปุ่ม
                    Close(true);
                }
            }
            else if (InputCompat.GetKeyDown(wheelKey))
            {
                if (isOpen) Close(true);
                else Open();
            }

            if (isOpen && InputCompat.GetKeyDown(KeyCode.Escape))
            {
                Close(false);
            }
        }

        private void HandleScrollSwitching()
        {
            float scroll = InputCompat.ScrollDelta;
            if (Mathf.Abs(scroll) < 0.01f) return;

            inventory.RequestEquipRelative(scroll > 0f ? -1 : 1);
        }

        /// <summary>แปลงทิศของเมาส์รอบกลางจอเป็นหมายเลขช่องที่กำลังชี้อยู่</summary>
        private void UpdateHoveredIndex()
        {
            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 direction = InputCompat.MousePosition - screenCenter;

            float scaleFactor = canvas != null ? canvas.scaleFactor : 1f;
            if (direction.magnitude < deadZoneRadius * scaleFactor)
            {
                hoveredIndex = -1; // อยู่ในวงกลางจอ = ยังไม่เลือกอะไร
                return;
            }

            // มุมแบบตามเข็มนาฬิกา โดยนับ 0 องศาที่ด้านบนจอ ให้ตรงกับการวางช่องในวงล้อ
            float angle = Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg;
            if (angle < 0f) angle += 360f;

            float step = 360f / views.Length;
            hoveredIndex = Mathf.RoundToInt(angle / step) % views.Length;
        }

        // ==========================================================
        // Open / Close
        // ==========================================================

        private void Open()
        {
            if (isOpen) return;

            isOpen = true;
            openCount++;

            hoveredIndex = inventory.EquippedIndex;

            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            RefreshSlots();
            group.gameObject.SetActive(true);
        }

        private void Close(bool applySelection)
        {
            if (!isOpen) return;

            isOpen = false;
            openCount = Mathf.Max(0, openCount - 1);

            if (applySelection && hoveredIndex >= 0)
            {
                inventory.RequestEquip(hoveredIndex);
            }

            RestoreWorldState();
        }

        private void RestoreWorldState()
        {
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
        }

        // ==========================================================
        // Visuals
        // ==========================================================

        private void AnimateVisuals(float deltaTime)
        {
            float target = isOpen ? 1f : 0f;
            float speed = deltaTime / Mathf.Max(0.01f, openDuration);
            openAmount = Mathf.MoveTowards(openAmount, target, speed);

            bool visible = openAmount > 0.001f;
            if (group.gameObject.activeSelf != visible) group.gameObject.SetActive(visible);
            if (!visible) return;

            // ease-out ให้วงล้อเด้งเข้ามาแบบมีน้ำหนัก
            float eased = 1f - (1f - openAmount) * (1f - openAmount);
            group.alpha = openAmount;
            wheelRoot.localScale = Vector3.one * Mathf.Lerp(0.82f, 1f, eased);

            for (int i = 0; i < views.Length; i++)
            {
                SlotView view = views[i];
                float hoverTarget = (isOpen && i == hoveredIndex) ? 1f : 0f;
                view.hover = Mathf.MoveTowards(view.hover, hoverTarget, deltaTime * 8f);

                ItemDefinition item = inventory.GetSlot(i);
                Color idle = i == inventory.EquippedIndex ? segmentEquippedColor : segmentColor;
                Color highlight = item != null ? WithAlpha(item.accentColor, 0.92f) : new Color(0.5f, 0.55f, 0.6f, 0.6f);

                view.segment.color = Color.Lerp(idle, highlight, view.hover);
                view.segment.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, 1.06f, view.hover);

                float pushOut = Mathf.Lerp(0f, 12f, view.hover);
                view.iconPivot.anchoredPosition = AngleToPosition(view.baseAngle, IconRadius + pushOut);
                view.icon.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, 1.18f, view.hover);
            }

            UpdateCenterLabels();
        }

        private void UpdateCenterLabels()
        {
            ItemDefinition hovered = hoveredIndex >= 0 ? inventory.GetSlot(hoveredIndex) : null;

            if (hoveredIndex < 0)
            {
                nameText.text = "ยกเลิก";
                descriptionText.text = "";
            }
            else if (hovered == null)
            {
                nameText.text = "มือเปล่า";
                descriptionText.text = "ช่องนี้ยังว่างอยู่";
            }
            else
            {
                nameText.text = hovered.DisplayName;
                descriptionText.text = hovered.description;
            }
        }

        private void RefreshSlots()
        {
            if (views == null) return;

            for (int i = 0; i < views.Length; i++)
            {
                ItemDefinition item = inventory.GetSlot(i);
                SlotView view = views[i];

                if (item != null && item.icon != null)
                {
                    view.icon.sprite = item.icon;
                    view.icon.color = Color.white;
                    view.icon.rectTransform.sizeDelta = new Vector2(iconSize, iconSize);
                }
                else if (item != null)
                {
                    // มีไอเทมแต่ไม่ได้ใส่ไอคอน — โชว์เป็นจุดสีประจำไอเทมแทน
                    view.icon.sprite = WheelSpriteFactory.Disc;
                    view.icon.color = item.accentColor;
                    view.icon.rectTransform.sizeDelta = new Vector2(iconSize * 0.45f, iconSize * 0.45f);
                }
                else
                {
                    view.icon.sprite = WheelSpriteFactory.Disc;
                    view.icon.color = emptySlotIconColor;
                    view.icon.rectTransform.sizeDelta = new Vector2(iconSize * 0.28f, iconSize * 0.28f);
                }
            }
        }

        // ==========================================================
        // สร้าง UI ทั้งหมดด้วยโค้ด
        // ==========================================================

        private float IconRadius => outerRadius * (1f + innerRadiusRatio) * 0.5f;

        private void BuildUI()
        {
            GameObject canvasObject = new GameObject("ItemWheelCanvas");
            canvasObject.transform.SetParent(transform, false);

            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform root = CreateRect("Root", canvasObject.transform);
            Stretch(root);
            group = root.gameObject.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;

            // 1. หรี่ฉากหลัง
            Image dim = CreateImage("Dim", root, WheelSpriteFactory.Vignette, dimColor);
            Stretch(dim.rectTransform);

            // 2. วงล้อ
            wheelRoot = CreateRect("Wheel", root);
            wheelRoot.anchorMin = wheelRoot.anchorMax = new Vector2(0.5f, 0.5f);
            wheelRoot.pivot = new Vector2(0.5f, 0.5f);
            wheelRoot.anchoredPosition = Vector2.zero;
            wheelRoot.sizeDelta = new Vector2(outerRadius * 2f, outerRadius * 2f);

            int slotCount = Mathf.Max(2, inventory.SlotCount);
            float step = 360f / slotCount;
            views = new SlotView[slotCount];

            for (int i = 0; i < slotCount; i++)
            {
                views[i] = BuildSlot(i, step);
            }

            // 3. วงกลางจอ (บอกว่าเข้ามาตรงนี้คือยกเลิก)
            Image center = CreateImage("Center", wheelRoot, WheelSpriteFactory.Disc, new Color(0f, 0f, 0f, 0.55f));
            CenterAnchor(center.rectTransform);
            center.rectTransform.sizeDelta = new Vector2(deadZoneRadius * 2f, deadZoneRadius * 2f);
            center.transform.SetAsFirstSibling();

            // 4. ตัวอักษร
            nameText = CreateText("ItemName", wheelRoot, 34, FontStyle.Bold, Color.white);
            CenterAnchor(nameText.rectTransform);
            nameText.rectTransform.sizeDelta = new Vector2(outerRadius * innerRadiusRatio * 2f - 20f, 60f);
            nameText.rectTransform.anchoredPosition = new Vector2(0f, 18f);

            descriptionText = CreateText("ItemDescription", wheelRoot, 20, FontStyle.Normal, new Color(1f, 1f, 1f, 0.62f));
            CenterAnchor(descriptionText.rectTransform);
            descriptionText.rectTransform.sizeDelta = new Vector2(outerRadius * innerRadiusRatio * 2f - 20f, 70f);
            descriptionText.rectTransform.anchoredPosition = new Vector2(0f, -32f);

            hintText = CreateText("Hint", root, 22, FontStyle.Normal, new Color(1f, 1f, 1f, 0.5f));
            hintText.rectTransform.anchorMin = hintText.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            hintText.rectTransform.pivot = new Vector2(0.5f, 0f);
            hintText.rectTransform.sizeDelta = new Vector2(900f, 40f);
            hintText.rectTransform.anchoredPosition = new Vector2(0f, 90f);
            hintText.text = holdToOpen
                ? "เลื่อนเมาส์เลือกไอเทม  •  ปล่อย Tab เพื่อยืนยัน  •  Esc ยกเลิก"
                : "เลื่อนเมาส์เลือกไอเทม  •  กด Tab อีกครั้งเพื่อยืนยัน  •  Esc ยกเลิก"; // ไม่มีสโลว์โมชันแล้ว
        }

        private SlotView BuildSlot(int index, float step)
        {
            float centerAngle = index * step;
            float startAngle = centerAngle - step * 0.5f + gapDegrees * 0.5f;

            Image segment = CreateImage($"Segment_{index}", wheelRoot, WheelSpriteFactory.Ring, segmentColor);
            CenterAnchor(segment.rectTransform);
            segment.rectTransform.sizeDelta = new Vector2(outerRadius * 2f, outerRadius * 2f);
            // หมุนทวนค่ามุมเริ่มต้น เพื่อให้ช่วงที่ถูก fill ไปตกอยู่ตรงตำแหน่งของช่องนี้พอดี
            segment.rectTransform.localEulerAngles = new Vector3(0f, 0f, -startAngle);

            segment.type = Image.Type.Filled;
            segment.fillMethod = Image.FillMethod.Radial360;
            segment.fillOrigin = (int)Image.Origin360.Top;
            segment.fillClockwise = true;
            segment.fillAmount = Mathf.Clamp01((step - gapDegrees) / 360f);

            // ไอคอนต้องอยู่นอกตัว segment ที่ถูกหมุน ไม่งั้นไอคอนจะเอียงตามไปด้วย
            RectTransform iconPivot = CreateRect($"IconPivot_{index}", wheelRoot);
            CenterAnchor(iconPivot);
            iconPivot.sizeDelta = new Vector2(iconSize, iconSize);
            iconPivot.anchoredPosition = AngleToPosition(centerAngle, IconRadius);

            Image icon = CreateImage($"Icon_{index}", iconPivot, null, Color.white);
            CenterAnchor(icon.rectTransform);
            icon.rectTransform.sizeDelta = new Vector2(iconSize, iconSize);
            icon.preserveAspect = true;

            return new SlotView
            {
                segment = segment,
                iconPivot = iconPivot,
                icon = icon,
                baseAngle = centerAngle
            };
        }

        /// <summary>แปลงมุม (องศา ตามเข็มนาฬิกา เริ่มจากด้านบน) + รัศมี ให้เป็นตำแหน่งใน UI</summary>
        private static Vector2 AngleToPosition(float degrees, float radius)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(radians) * radius, Mathf.Cos(radians) * radius);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private static RectTransform CreateRect(string objectName, Transform parent)
        {
            GameObject go = new GameObject(objectName, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Image CreateImage(string objectName, Transform parent, Sprite sprite, Color color)
        {
            RectTransform rect = CreateRect(objectName, parent);
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false; // อ่านตำแหน่งเมาส์เอง เลยไม่ต้องพึ่ง EventSystem
            return image;
        }

        private Text CreateText(string objectName, Transform parent, int fontSize, FontStyle style, Color color)
        {
            RectTransform rect = CreateRect(objectName, parent);
            CenterAnchor(rect);

            Text text = rect.gameObject.AddComponent<Text>();
            text.font = labelFont != null ? labelFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static void CenterAnchor(RectTransform rect)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
