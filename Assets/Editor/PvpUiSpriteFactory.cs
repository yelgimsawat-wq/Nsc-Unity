using System.IO;
using UnityEditor;
using UnityEngine;

namespace NscGame.Pvp
{
    /// <summary>
    /// เจนสไปรท์กรอบ HUD ของหน้าจอ PVP เป็นไฟล์ PNG จริงในโปรเจกต์
    ///
    /// ทำไมต้องเจนเอง: กรอบที่มีอยู่ (MenuUI/Setting/SettingPanel.png) มีคำว่า "SETTINGS"
    /// ฝังอยู่ในภาพ เอามาใช้ซ้ำไม่ได้ ส่วนการวาดกรอบด้วยแถบสี่เหลี่ยมสี่เส้นก็ได้แค่มุมฉากทื่อๆ
    /// ตัวนี้วาดเป็นแปดเหลี่ยม (มุมตัด 45°) + เส้นเรืองแสง แล้วตั้ง 9-slice ให้เลย
    /// จึงยืดเป็นแผงขนาดไหนก็ได้โดยมุมไม่บิด
    ///
    /// ทุกชิ้นเป็นสีขาวล้วน (รูปทรงอยู่ใน alpha) ตั้งใจให้ไปย้อมสีเอาเองด้วย Image.color
    ///
    /// เมนู: Tools ▸ NSC ▸ PVP ▸ Regenerate UI Sprites (สั่งเองเมื่ออยากได้ของใหม่)
    /// ปกติ PvpUIBuilder จะเรียก EnsureGenerated() ให้อัตโนมัติตอนสร้าง UI
    /// </summary>
    public static class PvpUiSpriteFactory
    {
        public const string Folder = "Assets/Yelmee/UI/PvpHud";

        public const string FillPath    = Folder + "/HudFill.png";
        public const string FramePath   = Folder + "/HudFrame.png";
        public const string CornersPath = Folder + "/HudCorners.png";
        public const string PersonPath  = Folder + "/HudPerson.png";
        public const string CheckPath   = Folder + "/HudCheck.png";
        public const string EmblemPath  = Folder + "/HudEmblem.png";

        // กรอบใช้เท็กซ์เจอร์ 96×96 ตัดมุม 18px — 9-slice ที่ 30 จึงกินมุมทั้งหมดพอดี
        private const int FrameSize = 96;
        private const float Chamfer = 18f;
        private const float Stroke = 3f;
        private const float GlowWidth = 5f;
        private const float GlowAlpha = 0.28f;
        private const int SliceBorder = 30;
        private const float CornerLength = 26f; // ความยาวขาวงเล็บมุมของ HudCorners

        [MenuItem("Tools/NSC/PVP/Regenerate UI Sprites")]
        public static void Regenerate()
        {
            Generate(true);
            EditorUtility.DisplayDialog("PVP HUD Sprites",
                $"เจนสไปรท์กรอบใหม่แล้วที่\n{Folder}\n\n" +
                "ถ้าเปิดหน้าจอ PVP อยู่ ให้สั่ง Build PVP UI ซ้ำอีกทีเพื่อให้ใช้ของใหม่",
                "โอเค");
        }

        /// <summary>สร้างให้ถ้ายังไม่มี — เรียกจาก PvpUIBuilder ก่อนประกอบ UI</summary>
        public static void EnsureGenerated()
        {
            Generate(false);
        }

        private static void Generate(bool force)
        {
            if (!Directory.Exists(Folder))
            {
                Directory.CreateDirectory(Folder);
                AssetDatabase.Refresh();
            }

            bool wroteAny = false;
            wroteAny |= WriteIfNeeded(FillPath,    force, () => BuildOctagon(FrameSize, FrameSize, Chamfer, Shape.Fill),    SliceBorder);
            wroteAny |= WriteIfNeeded(FramePath,   force, () => BuildOctagon(FrameSize, FrameSize, Chamfer, Shape.Frame),   SliceBorder);
            wroteAny |= WriteIfNeeded(CornersPath, force, () => BuildOctagon(FrameSize, FrameSize, Chamfer, Shape.Corners), SliceBorder);
            wroteAny |= WriteIfNeeded(PersonPath,  force, BuildPerson, 0);
            wroteAny |= WriteIfNeeded(CheckPath,   force, BuildCheck,  0);
            wroteAny |= WriteIfNeeded(EmblemPath,  force, BuildEmblem, 0);

            if (wroteAny) AssetDatabase.Refresh();
        }

        private static bool WriteIfNeeded(string path, bool force, System.Func<Texture2D> build, int sliceBorder)
        {
            if (!force && File.Exists(path)) return false;

            Texture2D texture = build();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            ApplyImportSettings(path, sliceBorder);
            return true;
        }

        private static void ApplyImportSettings(string path, int sliceBorder)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = new Vector4(sliceBorder, sliceBorder, sliceBorder, sliceBorder);
            importer.spritePixelsPerUnit = 100f;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        #region Shape building

        private enum Shape { Fill, Frame, Corners }

        /// <summary>
        /// ระยะจากขอบแปดเหลี่ยม (ติดลบ = อยู่ข้างใน) — เอาไว้ทำขอบเนียนและเส้นเรือง
        /// เป็นสี่เหลี่ยมที่ถูกเฉือนมุม 45° ด้วยระนาบ |x|+|y|
        /// </summary>
        private static float OctagonDistance(float x, float y, float hx, float hy, float chamfer)
        {
            float ax = Mathf.Abs(x);
            float ay = Mathf.Abs(y);
            float edge = Mathf.Max(ax - hx, ay - hy);
            float cut = (ax + ay - (hx + hy - chamfer)) * 0.70710678f;
            return Mathf.Max(edge, cut);
        }

        private static Texture2D BuildOctagon(int width, int height, float chamfer, Shape shape)
        {
            Texture2D texture = NewTexture(width, height);
            Color[] pixels = new Color[width * height];

            float hx = width * 0.5f;
            float hy = height * 0.5f;

            for (int py = 0; py < height; py++)
            {
                for (int px = 0; px < width; px++)
                {
                    float x = px + 0.5f - hx;
                    float y = py + 0.5f - hy;
                    float d = OctagonDistance(x, y, hx, hy, chamfer);

                    float alpha;
                    if (shape == Shape.Fill)
                    {
                        alpha = Mathf.Clamp01(0.5f - d);
                    }
                    else
                    {
                        // วงแหวนหนา Stroke ไล่จากขอบเข้าใน + เรืองออกนอกนิดหน่อย
                        float ring = Mathf.Clamp01(Mathf.Min(-d, d + Stroke) + 0.5f);
                        float glow = d > 0f ? GlowAlpha * Mathf.Clamp01(1f - d / GlowWidth) : 0f;
                        alpha = Mathf.Max(ring, glow);

                        if (shape == Shape.Corners)
                        {
                            // เก็บเฉพาะช่วงมุม — ตรงกลางด้านปล่อยว่าง 9-slice จะได้ยืดแล้วไม่เพี้ยน
                            bool nearCorner = Mathf.Abs(x) > hx - CornerLength &&
                                              Mathf.Abs(y) > hy - CornerLength;
                            if (!nearCorner) alpha = 0f;
                        }
                    }

                    pixels[py * width + px] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>ไอคอนคน — หัวกลม + ไหล่ ใช้แทนรูปโปรไฟล์ที่ยังไม่มี</summary>
        private static Texture2D BuildPerson()
        {
            const int size = 72;
            Texture2D texture = NewTexture(size, size);
            Color[] pixels = new Color[size * size];

            float cx = size * 0.5f;
            float headY = size * 0.66f;
            float headR = size * 0.17f;
            float bodyY = size * 0.16f;
            float bodyRx = size * 0.30f;
            float bodyRy = size * 0.26f;

            for (int py = 0; py < size; py++)
            {
                for (int px = 0; px < size; px++)
                {
                    float x = px + 0.5f;
                    float y = py + 0.5f;

                    float head = Vector2.Distance(new Vector2(x, y), new Vector2(cx, headY)) - headR;

                    // ไหล่เป็นครึ่งวงรีล่าง ตัดส่วนบนทิ้ง
                    float nx = (x - cx) / bodyRx;
                    float ny = (y - bodyY) / bodyRy;
                    float body = (Mathf.Sqrt(nx * nx + ny * ny) - 1f) * Mathf.Min(bodyRx, bodyRy);
                    if (y > bodyY + bodyRy * 0.75f) body = 1f;

                    float d = Mathf.Min(head, body);
                    pixels[py * size + px] = new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - d));
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>เครื่องหมายถูกในวงกลม — ใช้ต่อท้ายคำว่า READY</summary>
        private static Texture2D BuildCheck()
        {
            const int size = 48;
            Texture2D texture = NewTexture(size, size);
            Color[] pixels = new Color[size * size];

            Vector2 centre = new Vector2(size * 0.5f, size * 0.5f);
            float radius = size * 0.44f;
            float ringWidth = 3f;

            // เส้นหักของเครื่องหมายถูก
            Vector2 a = new Vector2(size * 0.30f, size * 0.50f);
            Vector2 b = new Vector2(size * 0.44f, size * 0.35f);
            Vector2 c = new Vector2(size * 0.72f, size * 0.66f);

            for (int py = 0; py < size; py++)
            {
                for (int px = 0; px < size; px++)
                {
                    Vector2 p = new Vector2(px + 0.5f, py + 0.5f);

                    float circle = Mathf.Abs(Vector2.Distance(p, centre) - radius) - ringWidth * 0.5f;
                    float tick = Mathf.Min(SegmentDistance(p, a, b), SegmentDistance(p, b, c)) - 2.2f;

                    float d = Mathf.Min(circle, tick);
                    pixels[py * size + px] = new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - d));
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>ตราทีม — ปีกเหลี่ยมทรงเมคา วางข้างชื่อทีม</summary>
        private static Texture2D BuildEmblem()
        {
            const int size = 64;
            Texture2D texture = NewTexture(size, size);
            Color[] pixels = new Color[size * size];

            Vector2 top = new Vector2(size * 0.5f, size * 0.86f);
            Vector2 bottom = new Vector2(size * 0.5f, size * 0.16f);
            Vector2 leftTip = new Vector2(size * 0.08f, size * 0.62f);
            Vector2 rightTip = new Vector2(size * 0.92f, size * 0.62f);

            for (int py = 0; py < size; py++)
            {
                for (int px = 0; px < size; px++)
                {
                    Vector2 p = new Vector2(px + 0.5f, py + 0.5f);

                    // ปีกซ้าย/ขวา = แถบหนาจากปลายปีกลงมาหาจุดล่าง, แกนกลาง = แถบตั้ง
                    float wingL = SegmentDistance(p, leftTip, bottom) - 4.5f;
                    float wingR = SegmentDistance(p, rightTip, bottom) - 4.5f;
                    float spine = SegmentDistance(p, top, bottom) - 3.2f;

                    float d = Mathf.Min(spine, Mathf.Min(wingL, wingR));
                    pixels[py * size + px] = new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - d));
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSq = ab.sqrMagnitude;
            if (lengthSq < 0.0001f) return Vector2.Distance(p, a);

            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSq);
            return Vector2.Distance(p, a + ab * t);
        }

        private static Texture2D NewTexture(int width, int height)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        }

        #endregion
    }
}
