using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Nsc.Match
{
    /// <summary>
    /// ภาพของโหมดบอสบนทุกเครื่อง (สร้างเองตอนเริ่มฉาก ไม่ต้องวางในซีน): แถบ CORE กลางบนจอ + เซลล์พลังงานเรืองแสง
    /// ค่าทั้งหมดอ่านจาก BossModeRules (Server เป็นคนตัดสิน) — ตัวนี้แค่แสดง
    /// </summary>
    public class BossCoreView : MonoBehaviour
    {
        public static BossCoreView Instance { get; private set; }

        private BossModeRules rules;
        private RectTransform fill;
        private Image fillImage, frame;
        private TMP_Text label, prompt;
        private float shown = 1f, flash, promptUntil;

        private class CellView { public GameObject go; public Vector3 from, to; public float born; }
        private readonly Dictionary<int, CellView> cellViews = new Dictionary<int, CellView>();
        private readonly List<(Transform t, float born)> pops = new List<(Transform, float)>();
        private Material cellMaterial;

        private static readonly Color Full = new Color(0.25f, 0.9f, 1f), Mid = new Color(1f, 0.8f, 0.2f), Low = new Color(1f, 0.2f, 0.15f);

        public static void Create(BossModeRules rules)
        {
            if (Instance) return;
            var go = new GameObject("BossCoreView");
            Instance = go.AddComponent<BossCoreView>();
            Instance.rules = rules;
            Instance.Build();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (cellMaterial) Destroy(cellMaterial);
        }

        private void Build()
        {
            var canvas = new GameObject("CoreCanvas", typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
            canvas.transform.SetParent(transform);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            // ฉากที่มี RobotArenaHud วาดแถบ ROBOT ใหญ่เอง (nok/BoneLimb/UI) — ที่นี่วาดแถบเล็กสำรองเฉพาะฉากที่ไม่มี HUD นั้น
            if (!FindFirstObjectByType<Nsc.BoneLimb.RobotArenaHud>())
            {
                frame = Box(canvas.transform, "Frame", new Vector2(0.5f, 1f), new Vector2(0f, -178f), new Vector2(520f, 26f), new Color(0f, 0f, 0f, 0.6f));
                fillImage = Box(frame.transform, "Fill", new Vector2(0f, 0.5f), new Vector2(3f, 0f), new Vector2(514f, 20f), Full);
                fill = fillImage.rectTransform;
                fill.pivot = new Vector2(0f, 0.5f);
                label = Text(canvas.transform, new Vector2(0.5f, 1f), new Vector2(0f, -150f), 24f, "CORE 100%"); // ใต้แถบเลือดไคจู
            }
            prompt = Text(canvas.transform, new Vector2(0.5f, 0f), new Vector2(0f, 215f), 26f, ""); // เหนือแถบ ROBOT ด้านล่างกลาง
            prompt.color = Full;

            cellMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            cellMaterial.color = Full;
            if (cellMaterial.HasProperty("_BaseColor")) cellMaterial.SetColor("_BaseColor", Full * 1.6f);
        }

        private static Image Box(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Color color)
        {
            var img = new GameObject(name, typeof(Image)).GetComponent<Image>();
            img.transform.SetParent(parent, false);
            RectTransform r = img.rectTransform;
            r.anchorMin = r.anchorMax = anchor;
            r.anchoredPosition = pos;
            r.sizeDelta = size;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static TMP_Text Text(Transform parent, Vector2 anchor, Vector2 pos, float size, string text)
        {
            var t = new GameObject("Text", typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            t.transform.SetParent(parent, false);
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = anchor;
            t.rectTransform.anchoredPosition = pos;
            t.rectTransform.sizeDelta = new Vector2(900f, 40f);
            t.alignment = TextAlignmentOptions.Center;
            t.fontSize = size;
            t.fontStyle = FontStyles.Bold;
            t.text = text;
            t.raycastTarget = false;
            return t;
        }

        private void Update()
        {
            if (!rules) { Destroy(gameObject); return; }
            bool playing = MatchSession.Current == null || MatchSession.Current.IsPlaying;
            if (frame)
            {
                frame.gameObject.SetActive(playing);
                label.gameObject.SetActive(playing);
                float core = rules.Core01;
                if (core < shown - 0.002f) flash = 1f; // เพิ่งเสียแกน = กะพริบแดง
                shown = Mathf.MoveTowards(shown, core, Time.deltaTime * 0.8f);
                fill.sizeDelta = new Vector2(514f * shown, 20f);
                Color c = shown > 0.5f ? Color.Lerp(Mid, Full, (shown - 0.5f) * 2f) : Color.Lerp(Low, Mid, shown * 2f);
                flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime * 3f);
                fillImage.color = Color.Lerp(c, Color.white, flash * 0.6f);
                label.text = $"CORE {Mathf.CeilToInt(core * 100f)}%";
                label.color = core < 0.25f && Mathf.Repeat(Time.time, 0.6f) < 0.3f ? Low : Color.white; // ใกล้หมด = กะพริบเตือน
            }

            prompt.gameObject.SetActive(playing && Time.time < promptUntil);
            if (!playing && cellViews.Count > 0) // จบแมตช์: Server เลิกนับเซลล์ ไม่มีคำสั่งลบตามมา — เก็บเองไม่ให้ค้างหลังหน้าผลลัพธ์
            {
                foreach (CellView v in cellViews.Values) Destroy(v.go);
                cellViews.Clear();
            }
            UpdateCells();
        }

        public void AddCell(int id, Vector3 from, Vector3 to)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "EnergyCell";
            Destroy(go.GetComponent<Collider>());
            go.transform.localScale = Vector3.one * 1.6f;
            go.GetComponent<Renderer>().sharedMaterial = cellMaterial;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = Full;
            light.range = 14f;
            light.intensity = 4f;
            cellViews[id] = new CellView { go = go, from = from, to = to, born = Time.time };
            prompt.text = "ENERGY CELL DROPPED  ·  TOUCH IT WITH A HAND TO REPAIR";
            promptUntil = Time.time + 3f;
        }

        public void RemoveCell(int id, bool collected)
        {
            if (!cellViews.TryGetValue(id, out CellView v)) return;
            cellViews.Remove(id);
            if (collected) // ดูดเข้าหุ่น: ขยายวาบแล้วหาย + ประกาศฮีล
            {
                pops.Add((v.go.transform, Time.time));
                prompt.text = "+CORE  ·  REPAIRED";
                promptUntil = Time.time + 1.5f;
                flash = 0f;
            }
            else Destroy(v.go);
        }

        private void UpdateCells()
        {
            foreach (CellView v in cellViews.Values)
            {
                float age = Time.time - v.born;
                v.go.transform.position = BossModeRules.CellPosition(v.from, v.to, age);
                float pulse = 1.6f + Mathf.Sin(Time.time * 6f) * 0.15f;
                v.go.transform.localScale = Vector3.one * pulse;
            }
            for (int i = pops.Count - 1; i >= 0; i--)
            {
                float k = (Time.time - pops[i].born) / 0.35f;
                if (k >= 1f || !pops[i].t) { if (pops[i].t) Destroy(pops[i].t.gameObject); pops.RemoveAt(i); continue; }
                pops[i].t.localScale = Vector3.one * Mathf.Lerp(1.6f, 6f, k);
                if (pops[i].t.TryGetComponent(out Light l)) l.intensity = Mathf.Lerp(12f, 0f, k);
            }
        }
    }
}
