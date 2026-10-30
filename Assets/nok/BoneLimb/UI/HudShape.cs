using UnityEngine;
using UnityEngine.UI;

namespace Nsc.BoneLimb
{
    /// <summary>
    /// สี่เหลี่ยมด้านขนาน (ขอบบนเอียงไปขวา) สำหรับ HUD สไตล์หุ่นยนต์ — ไม่ต้องใช้สไปรต์
    /// fill 0..1 = ตัดความกว้างจากซ้าย (หลอดเลือด) ยังเอียงเท่าเดิม
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class HudShape : MaskableGraphic
    {
        [Tooltip("ขอบบนเลื่อนไปขวากี่ส่วนของความสูง (0.25 ≈ เอียง 14°)")]
        public float slant = 0.25f;
        [Range(0f, 1f)] [SerializeField] private float fill = 1f;

        public float Fill
        {
            get => fill;
            set
            {
                value = Mathf.Clamp01(value);
                if (Mathf.Approximately(value, fill)) return;
                fill = value;
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (fill <= 0f) return;
            Rect r = GetPixelAdjustedRect();
            float skew = slant * r.height;
            float span = Mathf.Max(0f, r.width - skew) * fill;
            Color32 c = color;
            vh.AddVert(new Vector3(r.xMin, r.yMin), c, Vector4.zero);
            vh.AddVert(new Vector3(r.xMin + skew, r.yMax), c, Vector4.zero);
            vh.AddVert(new Vector3(r.xMin + skew + span, r.yMax), c, Vector4.zero);
            vh.AddVert(new Vector3(r.xMin + span, r.yMin), c, Vector4.zero);
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
        }
    }
}
