using UnityEngine;
using UnityEngine.UI;

namespace NscUnity.Items
{
    /// <summary>
    /// ป้ายเล็กๆ กลางล่างจอ "[F] เก็บ ปืนพก" ที่โผล่ขึ้นมาเมื่อเดินเข้าใกล้ไอเทม
    /// สร้าง UI ด้วยโค้ดเหมือนวงล้อ — แค่แปะไว้บน Empty GameObject แล้วลาก ItemPickupInteractor มาใส่
    /// </summary>
    public class PickupPromptUI : MonoBehaviour
    {
        [SerializeField] private ItemPickupInteractor interactor;
        [SerializeField] private float fadeDuration = 0.12f;

        [Tooltip("ลากไฟล์ฟอนต์ .ttf ที่มีภาษาไทยมาใส่ ถ้าเว้นว่างจะใช้ฟอนต์ติดตั้งมากับ Unity")]
        [SerializeField] private Font labelFont;

        private CanvasGroup group;
        private Text label;
        private WorldItem focused;
        private bool isInitialized;

        private void OnEnable()
        {
            TryInitialize();
        }

        /// <summary>
        /// ในเกมออนไลน์ ผู้เล่นสปอว์นหลังฉากโหลดเสร็จ — ตอน OnEnable อาจยังไม่มี ItemPickupInteractor
        /// ของเราเกิดขึ้นเลย จึงลองหาใหม่ทุกเฟรมใน Update จนกว่าจะเจอ ต้องหาเฉพาะของ "ตัวเราเอง" (IsOwner)
        /// </summary>
        private void TryInitialize()
        {
            if (isInitialized) return;

            if (interactor == null)
            {
                foreach (ItemPickupInteractor candidate in FindObjectsByType<ItemPickupInteractor>(FindObjectsSortMode.None))
                {
                    if (candidate.IsOwner)
                    {
                        interactor = candidate;
                        break;
                    }
                }
            }

            if (interactor == null) return;

            BuildUI();
            interactor.OnFocusChanged += HandleFocusChanged;
            isInitialized = true;
        }

        private void OnDisable()
        {
            if (interactor != null) interactor.OnFocusChanged -= HandleFocusChanged;
        }

        private void HandleFocusChanged(WorldItem item)
        {
            focused = item;
            if (item != null) label.text = $"[{FormatKey(interactor.PickupKey)}]  เก็บ {item.PromptText}";
        }

        /// <summary>แปลง KeyCode เป็นข้อความสั้นๆ สำหรับโชว์บนป้าย เช่น E, F, Space</summary>
        private static string FormatKey(KeyCode key)
        {
            return key switch
            {
                KeyCode.Space => "Space",
                KeyCode.Mouse0 => "คลิกซ้าย",
                KeyCode.Mouse1 => "คลิกขวา",
                KeyCode.Mouse2 => "คลิกกลาง",
                _ => key.ToString()
            };
        }

        private void Update()
        {
            if (!isInitialized)
            {
                TryInitialize();
                if (!isInitialized) return;
            }

            bool show = focused != null && !ItemWheelUI.IsAnyOpen;
            float target = show ? 1f : 0f;
            group.alpha = Mathf.MoveTowards(group.alpha, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeDuration));
        }

        private void BuildUI()
        {
            GameObject canvasObject = new GameObject("PickupPromptCanvas");
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 400;

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject panel = new GameObject("Prompt", typeof(RectTransform));
            panel.transform.SetParent(canvasObject.transform, false);

            RectTransform rect = (RectTransform)panel.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(700f, 56f);
            rect.anchoredPosition = new Vector2(0f, 170f);

            group = panel.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            // ไม่ใส่ sprite = Image จะวาดเป็นสี่เหลี่ยมทึบสีเดียว ซึ่งพอดีกับป้ายแบบนี้
            Image background = panel.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.55f);
            background.raycastTarget = false;

            GameObject textObject = new GameObject("Label", typeof(RectTransform));
            textObject.transform.SetParent(panel.transform, false);

            RectTransform textRect = (RectTransform)textObject.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            label = textObject.AddComponent<Text>();
            label.font = labelFont != null ? labelFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 26;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
        }
    }
}
