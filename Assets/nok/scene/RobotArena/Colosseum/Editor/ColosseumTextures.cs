using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Nsc.Arena.EditorTools
{
    // Procedural textures for the colosseum map, so the map has no outside art dependency.
    // Everything except the sand macro map tiles seamlessly.
    public static class ColosseumTextures
    {
        public const string TexDir = "Assets/nok/scene/RobotArena/Colosseum/Generated/Textures";

        public static Texture2D Load(string name) =>
            AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/" + name + ".png");

        public static bool Exists() => Load("T_SandMacro") != null;

        public static void GenerateAll(float arenaA, float arenaB, Vector2[] spawns)
        {
            Directory.CreateDirectory(TexDir);
            try
            {
                Step("Sand detail", 0.05f); SandDetail();
                Step("Sand macro", 0.15f); SandMacro(arenaA, arenaB, spawns);
                Step("Travertine", 0.45f); Travertine();
                Step("Marble", 0.55f); Marble();
                Step("Cloth", 0.70f); Velarium(); Banner("T_BannerRed", new Color(0.55f, 0.07f, 0.06f));
                Banner("T_BannerBlue", new Color(0.07f, 0.18f, 0.52f)); Banner("T_BannerPurple", new Color(0.30f, 0.07f, 0.32f));
                Step("Iron / wood", 0.80f); Iron(); Wood();
                Step("Paving / earth", 0.88f); Paving(); Earth();
                Step("Particles", 0.97f); SoftDot();
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        static void Step(string what, float p) => EditorUtility.DisplayProgressBar("Colosseum textures", what, p);

        // ---------------- noise ----------------
        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        static int Wrap(int a, int p) { int r = a % p; return r < 0 ? r + p : r; }
        static float S(float t) => t * t * (3f - 2f * t);

        // Value noise that tiles with integer periods px, py (x, y already scaled).
        static float VNoise(float x, float y, int px, int py, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = S(x - xi), fy = S(y - yi);
            int x0 = Wrap(xi, px), x1 = Wrap(xi + 1, px), y0 = Wrap(yi, py), y1 = Wrap(yi + 1, py);
            float a = Hash(x0, y0, seed), b = Hash(x1, y0, seed), c = Hash(x0, y1, seed), d = Hash(x1, y1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        // Tileable fBm over u, v in [0,1). fx / fy are the base frequencies (integers keep it tileable).
        static float Fbm(float u, float v, int fx, int fy, int oct, int seed, float gain = 0.5f)
        {
            float sum = 0, amp = 1, norm = 0;
            for (int o = 0; o < oct; o++)
            {
                sum += VNoise(u * fx, v * fy, fx, fy, seed + o * 31) * amp;
                norm += amp; amp *= gain; fx *= 2; fy *= 2;
            }
            return sum / norm;
        }

        static float Fbm(float u, float v, int f, int oct, int seed) => Fbm(u, v, f, f, oct, seed);

        static Color[] NormalFromHeight(float[] h, int w, int hh, float strength, bool wrap)
        {
            var c = new Color[w * hh];
            for (int y = 0; y < hh; y++)
            for (int x = 0; x < w; x++)
            {
                int xl = wrap ? Wrap(x - 1, w) : Mathf.Max(x - 1, 0), xr = wrap ? Wrap(x + 1, w) : Mathf.Min(x + 1, w - 1);
                int yd = wrap ? Wrap(y - 1, hh) : Mathf.Max(y - 1, 0), yu = wrap ? Wrap(y + 1, hh) : Mathf.Min(y + 1, hh - 1);
                float dx = (h[y * w + xr] - h[y * w + xl]) * strength;
                float dy = (h[yu * w + x] - h[yd * w + x]) * strength;
                Vector3 n = new Vector3(-dx, -dy, 1f).normalized;
                c[y * w + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }
            return c;
        }

        static void Save(string name, Color[] px, int w, int h, bool normal, bool clamp = false, bool alpha = false)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, normal);
            tex.SetPixels(px);
            tex.Apply();
            string path = TexDir + "/" + name + ".png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = !normal;
            ti.wrapMode = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            ti.anisoLevel = 8;
            ti.mipmapEnabled = true;
            ti.alphaIsTransparency = alpha;
            ti.maxTextureSize = Mathf.Max(w, h);
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();
        }

        static Color Mul(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        // ---------------- sand ----------------

        // Tiles every ~6 m. Neutral grey (0.5) so it works as a MULX2 detail map over the macro colour.
        static void SandDetail()
        {
            const int N = 1024;
            var alb = new Color[N * N];
            var h = new float[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = x / (float)N, v = y / (float)N;
                float warp = Fbm(u, v, 4, 3, 11);
                float rip = Mathf.Sin((3f * u + 14f * v + warp * 2.5f) * 2f * Mathf.PI) * 0.5f + 0.5f;
                rip = Mathf.Pow(rip, 1.6f);
                float ripMask = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.75f, Fbm(u, v, 3, 3, 17)));
                float fine = Fbm(u, v, 64, 3, 23);
                float grain = Hash(x, y, 5);
                float pebble = Hash(x >> 2, y >> 2, 9) > 0.996f ? 1f : 0f;
                int i = y * N + x;
                h[i] = rip * 0.6f * ripMask + fine * 0.3f + grain * 0.03f + pebble * 0.3f;
                float g = 0.5f + (fine - 0.5f) * 0.20f + (grain - 0.5f) * 0.10f - pebble * 0.16f + (rip - 0.5f) * 0.05f * ripMask;
                alb[i] = new Color(g, g, g * 0.985f, 1f);
            }
            Save("T_SandDetail", alb, N, N, false);
            Save("T_SandDetail_N", NormalFromHeight(h, N, N, 5f, true), N, N, true);
        }

        // One 2048 map stretched over the whole arena: colour variation, raked emblem, spawn rings,
        // giant robot footprints, drag gouges, scorch marks and oil stains.
        static void SandMacro(float A, float B, Vector2[] spawns)
        {
            const int N = 2048;
            var h = new float[N * N];
            var col = new Color[N * N];
            Color light = new Color(0.85f, 0.74f, 0.58f), dark = new Color(0.71f, 0.59f, 0.44f);
            float pxX = 2f * A / N, pxZ = 2f * B / N;

            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N, v = (y + 0.5f) / N;
                float wx = (u * 2f - 1f) * A, wz = (v * 2f - 1f) * B;
                float n1 = Fbm(u, v, 6, 5, 101), n2 = Fbm(u, v, 40, 3, 131);
                Color c = Color.Lerp(dark, light, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.7f, n1)));
                c = Mul(c, 0.94f + n2 * 0.12f);
                float height = n2 * 0.12f;

                // compacted, shadowed sand along the wall foot
                float e = Mathf.Sqrt((wx / A) * (wx / A) + (wz / B) * (wz / B));
                float edge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.86f, 0.995f, e));
                c = Color.Lerp(c, new Color(c.r * 0.80f, c.g * 0.78f, c.b * 0.78f), edge);

                // raked circle + 8-point star emblem in the centre
                float r = Mathf.Sqrt(wx * wx + wz * wz);
                float inner = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(31f, 34f, r));
                height += (Mathf.Sin(r * 2f * Mathf.PI / 1.4f) * 0.5f + 0.5f) * 0.07f * inner;
                c = Color.Lerp(c, Mul(c, 1.05f), inner * 0.6f);
                float ring = 1f - Mathf.Clamp01(Mathf.Abs(r - 34.5f) / 1.1f);
                float ring2 = 1f - Mathf.Clamp01(Mathf.Abs(r - 37.5f) / 0.5f);
                height -= ring * 0.35f + ring2 * 0.2f;
                c = Color.Lerp(c, Mul(c, 0.82f), Mathf.Max(ring, ring2 * 0.7f));
                float ang = Mathf.Atan2(wz, wx);
                float starR = 16f * (0.55f + 0.45f * Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * 4f)), 4f));
                float star = Mathf.Clamp01((starR - r) / 0.8f);
                float starLine = 1f - Mathf.Clamp01(Mathf.Abs(r - starR) / 0.6f);
                c = Color.Lerp(c, Mul(c, 1.12f), star * 0.55f);
                height += star * 0.08f - starLine * 0.3f;
                c = Color.Lerp(c, Mul(c, 0.85f), starLine);
                float hub = 1f - Mathf.Clamp01(Mathf.Abs(r - 4.5f) / 0.5f);
                c = Color.Lerp(c, Mul(c, 0.8f), hub); height -= hub * 0.4f;

                // ring grooves around both spawn pads
                foreach (var sp in spawns)
                {
                    float rs = Vector2.Distance(new Vector2(wx, wz), sp);
                    float g = 1f - Mathf.Clamp01(Mathf.Abs(rs - 16f) / 0.7f);
                    height -= g * 0.5f;
                    c = Color.Lerp(c, Mul(c, 0.8f), g);
                }

                c.a = 1f;
                int i = y * N + x;
                col[i] = c;
                h[i] = height;
            }

            var rnd = new System.Random(1977);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

            // Giant footprint trails (feet ~7 x 4 m, stride ~15 m).
            for (int t = 0; t < 7; t++)
            {
                Vector2 p = new Vector2(R(-A * 0.7f, A * 0.7f), R(-B * 0.7f, B * 0.7f));
                float head = R(0f, Mathf.PI * 2f), bend = R(-0.12f, 0.12f);
                int steps = rnd.Next(5, 11);
                for (int s = 0; s < steps; s++)
                {
                    Vector2 dir = new Vector2(Mathf.Cos(head), Mathf.Sin(head));
                    Vector2 side = new Vector2(-dir.y, dir.x) * ((s & 1) == 0 ? 3.2f : -3.2f);
                    Vector2 c = p + side;
                    if ((c.x / A) * (c.x / A) + (c.y / B) * (c.y / B) < 0.85f)
                        StampFootprint(h, col, N, A, B, c, head, 7f, 4.2f);
                    p += dir * 15f; head += bend;
                }
            }

            // Drag gouges from robots being thrown.
            for (int g = 0; g < 6; g++)
            {
                Vector2 a = new Vector2(R(-A * 0.6f, A * 0.6f), R(-B * 0.6f, B * 0.6f));
                float ang = R(0f, Mathf.PI * 2f);
                Vector2 b = a + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * R(22f, 45f);
                StampGouge(h, col, N, A, B, a, b, R(1.2f, 2.4f));
            }

            // Scorch marks and oil stains.
            for (int s = 0; s < 7; s++)
                StampBlot(h, col, N, A, B, new Vector2(R(-A * 0.75f, A * 0.75f), R(-B * 0.75f, B * 0.75f)), R(6f, 13f),
                    new Color(0.17f, 0.14f, 0.12f), 0.85f, s * 7 + 300);
            for (int s = 0; s < 12; s++)
                StampBlot(h, col, N, A, B, new Vector2(R(-A * 0.8f, A * 0.8f), R(-B * 0.8f, B * 0.8f)), R(2f, 5f),
                    new Color(0.30f, 0.27f, 0.27f), 0.6f, s * 13 + 600);

            Save("T_SandMacro", col, N, N, false, clamp: true);
            Save("T_SandMacro_N", NormalFromHeight(h, N, N, 0.35f / Mathf.Min(pxX, pxZ), false), N, N, true, clamp: true);
        }

        static void PixelBox(float A, float B, int N, Vector2 c, float rad, out int x0, out int x1, out int y0, out int y1)
        {
            x0 = Mathf.Clamp(Mathf.FloorToInt(((c.x - rad) / A + 1f) * 0.5f * N), 0, N - 1);
            x1 = Mathf.Clamp(Mathf.CeilToInt(((c.x + rad) / A + 1f) * 0.5f * N), 0, N - 1);
            y0 = Mathf.Clamp(Mathf.FloorToInt(((c.y - rad) / B + 1f) * 0.5f * N), 0, N - 1);
            y1 = Mathf.Clamp(Mathf.CeilToInt(((c.y + rad) / B + 1f) * 0.5f * N), 0, N - 1);
        }

        static Vector2 World(int x, int y, int N, float A, float B) =>
            new Vector2(((x + 0.5f) / N * 2f - 1f) * A, ((y + 0.5f) / N * 2f - 1f) * B);

        static void StampFootprint(float[] h, Color[] col, int N, float A, float B, Vector2 c, float ang, float len, float wid)
        {
            PixelBox(A, B, N, c, len, out int x0, out int x1, out int y0, out int y1);
            float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                Vector2 d = World(x, y, N, A, B) - c;
                float lx = d.x * ca + d.y * sa, ly = -d.x * sa + d.y * ca;
                // rounded-rectangle signed distance
                float qx = Mathf.Abs(lx) - (len * 0.5f - 1.4f), qy = Mathf.Abs(ly) - (wid * 0.5f - 1.4f);
                float sd = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0) - 1.4f;
                float inside = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.4f, -0.4f, sd));
                float rim = Mathf.Exp(-Mathf.Pow((sd - 0.7f) / 0.6f, 2f));
                // toe treads across the sole
                float tread = inside * (Mathf.Sin(lx * 2.2f) * 0.5f + 0.5f) * 0.25f;
                int i = y * N + x;
                h[i] += -0.6f * inside + rim * 0.2f - tread * 0.5f;
                col[i] = Color.Lerp(col[i], Mul(col[i], 0.93f), inside);
                col[i] = Color.Lerp(col[i], Mul(col[i], 1.06f), rim * 0.5f);
            }
        }

        static void StampGouge(float[] h, Color[] col, int N, float A, float B, Vector2 a, Vector2 b, float w)
        {
            Vector2 mid = (a + b) * 0.5f;
            PixelBox(A, B, N, mid, Vector2.Distance(a, b) * 0.5f + w * 3f, out int x0, out int x1, out int y0, out int y1);
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                Vector2 p = World(x, y, N, A, B);
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                float taper = Mathf.Sin(t * Mathf.PI) * 0.7f + 0.3f;
                float d = Vector2.Distance(p, a + ab * t) / (w * taper);
                float groove = Mathf.Exp(-d * d * 1.5f);
                float rim = Mathf.Exp(-Mathf.Pow(d - 1.4f, 2f) * 3f);
                int i = y * N + x;
                h[i] += -0.9f * groove + 0.3f * rim;
                col[i] = Color.Lerp(col[i], Mul(col[i], 0.78f), groove);
            }
        }

        static void StampBlot(float[] h, Color[] col, int N, float A, float B, Vector2 c, float rad, Color tint, float strength, int seed)
        {
            PixelBox(A, B, N, c, rad * 1.6f, out int x0, out int x1, out int y0, out int y1);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                Vector2 p = World(x, y, N, A, B);
                float n = Fbm((p.x + 500f) / 1000f, (p.y + 500f) / 1000f, 64, 4, seed);
                float d = Vector2.Distance(p, c) / rad + (n - 0.5f) * 0.9f;
                float m = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, 0.25f, d)) * strength;
                int i = y * N + x;
                Color k = new Color(col[i].r * tint.r * 2f, col[i].g * tint.g * 2f, col[i].b * tint.b * 2f, 1f);
                col[i] = Color.Lerp(col[i], k, m);
                h[i] -= m * 0.1f;
            }
        }

        // ---------------- stone ----------------

        // Travertine ashlar, tile = 8 m: 4 courses of 2 m, 2-4 blocks per course.
        static void Travertine()
        {
            const int N = 1024;
            var alb = new Color[N * N];
            var h = new float[N * N];
            for (int y = 0; y < N; y++)
            {
                int course = y * 4 / N;
                float cv = y * 4f / N - course;
                float off = Hash(course, 0, 7);
                int count = 2 + (int)(Hash(course, 1, 7) * 2.999f);
                for (int x = 0; x < N; x++)
                {
                    float u = x / (float)N, v = y / (float)N;
                    float cu = (u + off) * count;
                    int bi = Mathf.FloorToInt(cu);
                    float bu = cu - bi;
                    int bid = Wrap(bi, count) + course * 10;
                    float dj = Mathf.Min(Mathf.Min(bu, 1f - bu) * (8f / count), Mathf.Min(cv, 1f - cv) * 2f);
                    float joint = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.025f, 0.07f, dj));
                    float bevel = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.025f, 0.2f, dj));

                    float tone = 0.86f + 0.22f * Hash(bid, 3, 9);
                    float warm = Hash(bid, 5, 9) - 0.5f;
                    Color block = new Color(0.80f * tone * (1f + warm * 0.06f), 0.73f * tone, 0.62f * tone * (1f - warm * 0.08f));
                    float streak = Fbm(u, v, 48, 3, 2, 41);
                    float pits = Fbm(u, v, 48, 160, 2, 53);
                    float pit = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.70f, 0.80f, pits));
                    float rough = Fbm(u, v, 32, 4, 61);
                    float grime = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.25f, cv)) * 0.12f;

                    Color c = Color.Lerp(new Color(0.50f, 0.45f, 0.38f), block, joint);
                    c = Mul(c, (1f - pit * 0.35f) * (1f - Mathf.Max(0f, streak - 0.45f) * 0.5f) * (0.92f + rough * 0.16f) * grime);
                    c.a = 1f;
                    int i = y * N + x;
                    alb[i] = c;
                    h[i] = bevel - pit * 0.4f + rough * 0.25f;
                }
            }
            Save("T_Travertine", alb, N, N, false);
            Save("T_Travertine_N", NormalFromHeight(h, N, N, 3f, true), N, N, true);
        }

        // Polished marble cladding, tile = 6 m with 3 x 3 slabs.
        static void Marble()
        {
            const int N = 1024;
            var alb = new Color[N * N];
            var h = new float[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = x / (float)N, v = y / (float)N;
                float su = u * 3f - Mathf.Floor(u * 3f), sv = v * 3f - Mathf.Floor(v * 3f);
                float dj = Mathf.Min(Mathf.Min(su, 1f - su), Mathf.Min(sv, 1f - sv)) * 2f;
                float joint = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.006f, 0.02f, dj));
                float warp = Fbm(u, v, 4, 5, 71);
                float vein = Mathf.Abs(Mathf.Sin((u * 3f + v * 2f + warp * 2.2f) * 2f * Mathf.PI));
                float veinLine = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.07f, vein));
                float cloud = Fbm(u, v, 8, 4, 79);
                int slab = Mathf.FloorToInt(u * 3f) + Mathf.FloorToInt(v * 3f) * 3;
                float tone = 0.94f + Hash(slab, 2, 3) * 0.08f;
                Color c = new Color(0.90f, 0.87f, 0.81f);
                c = Mul(c, tone * (0.93f + cloud * 0.1f));
                c = Color.Lerp(c, new Color(0.62f, 0.58f, 0.53f), veinLine * 0.45f);
                c = Color.Lerp(new Color(0.45f, 0.42f, 0.38f), c, joint);
                c.a = 1f;
                int i = y * N + x;
                alb[i] = c;
                h[i] = joint;
            }
            Save("T_Marble", alb, N, N, false);
            Save("T_Marble_N", NormalFromHeight(h, N, N, 1.5f, true), N, N, true);
        }

        // ---------------- cloth ----------------

        static void Velarium()
        {
            const int N = 512;
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = x / (float)N, v = y / (float)N;
                Color c = new Color(0.90f, 0.83f, 0.68f);
                float band = Mathf.Min(u, 1f - u);
                if (band > 0.04f && band < 0.11f) c = new Color(0.62f, 0.13f, 0.09f);
                if (Mathf.Abs(u - 0.5f) < 0.015f) c = new Color(0.62f, 0.13f, 0.09f);
                float weave = (Hash(x, y >> 1, 3) - 0.5f) * 0.05f + (Fbm(u, v, 6, 3, 9) - 0.5f) * 0.12f;
                c = Mul(c, 1f + weave);
                c.a = 1f;
                px[y * N + x] = c;
            }
            Save("T_Velarium", px, N, N, false);
        }

        static void Banner(string name, Color baseCol)
        {
            const int W = 512, H = 1024;
            var px = new Color[W * H];
            Color gold = new Color(0.86f, 0.66f, 0.26f);
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = x / (float)W, v = y / (float)H;
                float fold = Mathf.Cos(u * Mathf.PI * 6f) * 0.06f;
                Color c = Mul(baseCol, 1f + fold + (Hash(x, y, 2) - 0.5f) * 0.04f);
                float edge = Mathf.Min(Mathf.Min(u, 1f - u), (1f - v) * 0.5f);
                if (edge < 0.035f) c = gold;
                else if (edge > 0.05f && edge < 0.06f) c = gold;
                // fringe at the bottom
                if (v < 0.05f) c = (x / 6) % 2 == 0 ? gold : Mul(gold, 0.6f);
                // emblem: ring with crossed blades
                Vector2 p = new Vector2((u - 0.5f) * 2f, (v - 0.62f) * 4f);
                float r = p.magnitude;
                if (Mathf.Abs(r - 0.62f) < 0.07f) c = gold;
                if (r < 0.55f && (Mathf.Abs(p.x - p.y) < 0.09f || Mathf.Abs(p.x + p.y) < 0.09f)) c = gold;
                if (r < 0.14f) c = Mul(gold, 1.1f);
                // chevrons below the emblem
                float cy = v - 0.25f - Mathf.Abs(u - 0.5f) * 0.4f;
                if (cy > 0 && cy < 0.10f && ((int)(cy * 50f)) % 3 == 0 && edge > 0.06f) c = Mul(gold, 0.9f);
                c.a = 1f;
                px[y * W + x] = c;
            }
            Save(name, px, W, H, false, clamp: true);
        }

        // ---------------- misc ----------------

        static void Iron()
        {
            const int N = 512;
            var px = new Color[N * N];
            var h = new float[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = x / (float)N, v = y / (float)N;
                float rust = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 0.75f, Fbm(u, v, 6, 5, 201)));
                float n = Fbm(u, v, 32, 3, 211);
                Color c = Color.Lerp(new Color(0.17f, 0.16f, 0.15f), new Color(0.36f, 0.20f, 0.10f), rust);
                c = Mul(c, 0.85f + n * 0.3f);
                c.a = 1f - rust * 0.7f; // alpha drives smoothness in URP Lit (albedo alpha source)
                px[y * N + x] = c;
                h[y * N + x] = n * 0.5f + rust * 0.3f;
            }
            Save("T_Iron", px, N, N, false);
            Save("T_Iron_N", NormalFromHeight(h, N, N, 4f, true), N, N, true);
        }

        static void Wood()
        {
            const int N = 512;
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = x / (float)N, v = y / (float)N;
                int plank = (int)(u * 4f);
                float grain = Fbm(u, v, 64, 2, 3, 301 + plank);
                float pu = u * 4f - plank;
                Color c = Mul(new Color(0.40f, 0.26f, 0.15f), 0.8f + grain * 0.4f + Hash(plank, 0, 5) * 0.15f);
                if (pu < 0.03f || pu > 0.97f) c = Mul(c, 0.4f);
                c.a = 1f;
                px[y * N + x] = c;
            }
            Save("T_Wood", px, N, N, false);
        }

        // Large flagstones for the plaza, tile = 12 m.
        static void Paving()
        {
            const int N = 1024, G = 5;
            var px = new Color[N * N];
            var h = new float[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = x / (float)N * G, v = y / (float)N * G;
                int cx = Mathf.FloorToInt(u), cy = Mathf.FloorToInt(v);
                float f1 = 9, f2 = 9; int id = 0;
                for (int oy = -1; oy <= 1; oy++)
                for (int ox = -1; ox <= 1; ox++)
                {
                    int gx = cx + ox, gy = cy + oy;
                    int wx = Wrap(gx, G), wy = Wrap(gy, G);
                    float px2 = gx + 0.2f + Hash(wx, wy, 401) * 0.6f, py2 = gy + 0.2f + Hash(wx, wy, 402) * 0.6f;
                    float d = Mathf.Max(Mathf.Abs(u - px2), Mathf.Abs(v - py2) * 1.15f);
                    if (d < f1) { f2 = f1; f1 = d; id = wx + wy * 17; }
                    else if (d < f2) f2 = d;
                }
                float gap = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.015f, 0.05f, f2 - f1));
                float n = Fbm(x / (float)N, y / (float)N, 16, 4, 411);
                Color stone = Mul(new Color(0.70f, 0.65f, 0.57f), 0.85f + Hash(id, 1, 9) * 0.22f + (n - 0.5f) * 0.2f);
                Color c = Color.Lerp(new Color(0.36f, 0.31f, 0.24f), stone, gap);
                c.a = 1f;
                px[y * N + x] = c;
                h[y * N + x] = gap + n * 0.2f;
            }
            Save("T_Paving", px, N, N, false);
            Save("T_Paving_N", NormalFromHeight(h, N, N, 3f, true), N, N, true);
        }

        static void Earth()
        {
            const int N = 1024;
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = x / (float)N, v = y / (float)N;
                float n = Fbm(u, v, 4, 6, 501);
                float grass = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.48f, 0.66f, Fbm(u, v, 3, 5, 511)));
                Color c = Color.Lerp(new Color(0.58f, 0.48f, 0.34f), new Color(0.47f, 0.47f, 0.28f), grass);
                c = Mul(c, 0.82f + n * 0.3f + (Hash(x, y, 9) - 0.5f) * 0.06f);
                c.a = 1f;
                px[y * N + x] = c;
            }
            Save("T_Earth", px, N, N, false);
        }

        static void SoftDot()
        {
            const int N = 128;
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = new Vector2((x + 0.5f) / N - 0.5f, (y + 0.5f) / N - 0.5f).magnitude * 2f;
                float a = Mathf.Pow(Mathf.Clamp01(1f - d), 2.2f);
                px[y * N + x] = new Color(1f, 1f, 1f, a);
            }
            Save("T_SoftDot", px, N, N, false, clamp: true, alpha: true);
        }
    }
}
