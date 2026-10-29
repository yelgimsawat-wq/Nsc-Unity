using UnityEngine;

namespace NscGame.Enemy
{
    /// <summary>
    /// เส้นหมุนการ์ตูนตอนกลิ้ง (ภาพล้วน ทุกเครื่องทำเอง) — แยกออกจาก KaijuSpecials ที่ดูแลการเลือก/จับเวลาท่า
    /// อยู่ในระนาบกลิ้ง ผ่านกลางลูกบอล 4 เส้น: 2 เส้นหลัง-บนวิ่งข้ามหัวไปหน้า, 2 เส้นด้านหน้าวิ่งลงพื้น (ทิศเดียวกับที่ลูกบอลหมุน)
    /// แต่ละเส้นเกิด → วิ่งไปตามวงไม่เต็มวง → หดบางหาย แล้วเริ่มใหม่ (เหลื่อมจังหวะกัน)
    /// </summary>
    public sealed class KaijuSpinLines
    {
        private static readonly Vector2[] Arcs =
        {
            new Vector2(165f, 95f), new Vector2(165f, 95f),   // หลัง-บน → ข้ามหัว
            new Vector2(35f, -35f), new Vector2(35f, -35f),   // หน้า → ลงพื้น
        };
        private static Material fallbackMaterial; // สร้างครั้งเดียว ไม่สร้างใหม่ทุกครั้งที่กลิ้ง

        private readonly LineRenderer[] lines = new LineRenderer[Arcs.Length];
        private readonly Transform owner, hips, head;
        private readonly float width, radiusScale, life;

        public KaijuSpinLines(Transform owner, Animator animator, Material material, Color color, float width, float radiusScale, float life)
        {
            this.owner = owner;
            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            this.width = width;
            this.radiusScale = radiusScale;
            this.life = Mathf.Max(0.05f, life);

            if (material == null)
            {
                if (fallbackMaterial == null) fallbackMaterial = new Material(Shader.Find("Sprites/Default"));
                material = fallbackMaterial;
            }
            for (int i = 0; i < lines.Length; i++)
            {
                LineRenderer lr = new GameObject("SpinLine").AddComponent<LineRenderer>();
                lr.sharedMaterial = material;
                lr.useWorldSpace = true;
                lr.positionCount = 12;
                lr.numCapVertices = 4;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.startColor = lr.endColor = color;
                // หางเรียว หัวหนา (จุดท้ายของเส้นคือหัวที่วิ่งนำ) เหมือนเส้นความเร็วการ์ตูน
                lr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.05f), new Keyframe(0.8f, 1f), new Keyframe(1f, 0.6f));
                lr.enabled = false;
                lines[i] = lr;
            }
        }

        /// <param name="t">วินาทีนับจากเริ่มท่ากลิ้ง (ใช้เหลื่อมจังหวะแต่ละเส้น)</param>
        public void Update(bool show, float t)
        {
            if (!show || hips == null)
            {
                foreach (LineRenderer lr in lines) if (lr) lr.enabled = false;
                return;
            }
            Vector3 center = head ? (hips.position + head.position) * 0.5f : hips.position;
            float r = (head ? Vector3.Distance(hips.position, head.position) : 3f) * radiusScale;
            Vector3 fwd = Vector3.ProjectOnPlane(owner.forward, Vector3.up).normalized, up = Vector3.up;
            for (int i = 0; i < lines.Length; i++)
            {
                LineRenderer lr = lines[i];
                if (!lr) continue;
                bool outer = i % 2 == 1;
                float u = Mathf.Repeat(t / life + i * 0.25f, 1f);            // แต่ละเส้นเริ่มคนละช่วงของรอบ
                Vector2 path = Arcs[i];
                float head0 = Mathf.Lerp(path.x, path.y, u);                // หัวเส้นวิ่งไปตามทิศหมุน
                float span = 40f * Mathf.Min(1f, u * 3f);                   // เส้นยืดออกตอนเกิด
                float radius = r * (outer ? 1.2f : 1f);
                // เส้นทึบแบบหมึก: เกิดแล้วหนาขึ้นทันที ช่วงท้ายหดบางจนหาย (ไม่ใช้โปร่งใส ภาพจะเทาจาง)
                float grow = Mathf.Min(1f, u * 8f) * (1f - Mathf.SmoothStep(0.7f, 1f, u));
                lr.enabled = grow > 0.02f;
                lr.widthMultiplier = r * width * (outer ? 0.75f : 1f) * grow;
                for (int k = 0; k < lr.positionCount; k++)
                {
                    // จุดแรก = หาง (มุมที่ผ่านมาแล้ว) → จุดสุดท้าย = หัว
                    float a = (head0 + span * (1f - k / (lr.positionCount - 1f)) * Mathf.Sign(path.x - path.y)) * Mathf.Deg2Rad;
                    lr.SetPosition(k, center + (fwd * Mathf.Cos(a) + up * Mathf.Sin(a)) * radius);
                }
            }
        }

        public void Destroy()
        {
            foreach (LineRenderer lr in lines) if (lr) Object.Destroy(lr.gameObject);
        }
    }
}
