using System.IO;
using UnityEditor;
using UnityEngine;

namespace Nsc.City.EditorTools
{
    // Procedural textures for the city map (no outside art). Everything tiles seamlessly.
    // Facades: one tile = 8 window bays x 8 floors (32 m x 32 m), base + normal + lit-window emission.
    public static class CityTextures
    {
        public const string TexDir = "Assets/nok/scene/MegaCity/Generated/Textures";
        public const float FacadeTile = 32f;
        public static readonly string[] FacadeStyles = { "Glass", "Office", "Residential", "Tower" };

        public static Texture2D Load(string name) =>
            AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/" + name + ".png");

        public static bool Exists() => Load("T_Billboard_2") != null;

        public static void GenerateAll()
        {
            Directory.CreateDirectory(TexDir);
            try
            {
                Step("Asphalt", 0.05f); Asphalt();
                Step("Paving", 0.15f); Paving();
                Step("Concrete", 0.25f); Concrete();
                Step("Roof / grass", 0.32f); Roof(); Grass();
                for (int i = 0; i < FacadeStyles.Length; i++)
                {
                    Step("Facade " + FacadeStyles[i], 0.40f + i * 0.1f);
                    Facade(FacadeStyles[i], i);
                }
                Step("Storefront", 0.82f); Storefront();
                Step("Billboards", 0.9f);
                for (int i = 0; i < 3; i++) Billboard(i);
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        static void Step(string what, float p) => EditorUtility.DisplayProgressBar("City textures", what, p);

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

        static float VNoise(float x, float y, int px, int py, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = S(x - xi), fy = S(y - yi);
            int x0 = Wrap(xi, px), x1 = Wrap(xi + 1, px), y0 = Wrap(yi, py), y1 = Wrap(yi + 1, py);
            float a = Hash(x0, y0, seed), b = Hash(x1, y0, seed), c = Hash(x0, y1, seed), d = Hash(x1, y1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Fbm(float u, float v, int f, int oct, int seed, float gain = 0.5f)
        {
            float sum = 0, amp = 1, norm = 0;
            int fx = f, fy = f;
            for (int o = 0; o < oct; o++)
            {
                sum += VNoise(u * fx, v * fy, fx, fy, seed + o * 31) * amp;
                norm += amp; amp *= gain; fx *= 2; fy *= 2;
            }
            return sum / norm;
        }

        static Color[] NormalFromHeight(float[] h, int w, int hh, float strength)
        {
            var c = new Color[w * hh];
            for (int y = 0; y < hh; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = (h[y * w + Wrap(x + 1, w)] - h[y * w + Wrap(x - 1, w)]) * strength;
                float dy = (h[Wrap(y + 1, hh) * w + x] - h[Wrap(y - 1, hh) * w + x]) * strength;
                Vector3 n = new Vector3(-dx, -dy, 1f).normalized;
                c[y * w + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }
            return c;
        }

        static void Save(string name, Color[] px, int w, int h, bool normal)
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
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.anisoLevel = 8;
            ti.mipmapEnabled = true;
            ti.maxTextureSize = Mathf.Max(w, h);
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();
        }

        static Color Grey(float v) => new Color(v, v, v, 1f);

        // ---------------- ground ----------------

        // 8 m tile: dark aggregate, tar patches, fine cracks
        static void Asphalt()
        {
            const int N = 512;
            var col = new Color[N * N];
            var h = new float[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = x / (float)N, v = y / (float)N;
                float big = Fbm(u, v, 3, 4, 11);
                float grit = Hash(x, y, 7);
                float crack = Mathf.Abs(Fbm(u, v, 6, 3, 21) - 0.5f);
                float c = 0.17f + (big - 0.5f) * 0.07f + (grit - 0.5f) * 0.06f;
                bool cracked = crack < 0.005f && big > 0.55f; // hairline cracks, only in the worn patches
                if (cracked) c *= 0.82f;
                col[y * N + x] = new Color(c * 0.98f, c, c * 1.03f, 1f);
                h[y * N + x] = grit * 0.4f + big * 0.6f - (cracked ? 0.3f : 0f);
            }
            Save("T_Asphalt", col, N, N, false);
            Save("T_Asphalt_N", NormalFromHeight(h, N, N, 2.5f), N, N, true);
        }

        // 4 m tile: 1 m square slabs with grout, slight tone change per slab
        static void Paving()
        {
            const int N = 512, slabs = 4;
            int s = N / slabs;
            var col = new Color[N * N];
            var h = new float[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int sx = x / s, sy = y / s, lx = x % s, ly = y % s;
                int edge = Mathf.Min(Mathf.Min(lx, s - 1 - lx), Mathf.Min(ly, s - 1 - ly));
                float tone = 0.58f + (Hash(sx, sy, 3) - 0.5f) * 0.08f + (Fbm(x / (float)N, y / (float)N, 8, 3, 5) - 0.5f) * 0.08f;
                bool grout = edge < 3;
                float c = grout ? 0.33f : tone + (Hash(x, y, 9) - 0.5f) * 0.03f;
                col[y * N + x] = new Color(c, c * 0.98f, c * 0.95f, 1f);
                h[y * N + x] = grout ? 0f : Mathf.Min(1f, edge / 6f);
            }
            Save("T_CityPaving", col, N, N, false);
            Save("T_CityPaving_N", NormalFromHeight(h, N, N, 3f), N, N, true);
        }

        // 6 m tile: cast concrete with form-tie dots and water stains
        static void Concrete()
        {
            const int N = 512;
            var col = new Color[N * N];
            var h = new float[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = x / (float)N, v = y / (float)N;
                float n = Fbm(u, v, 4, 5, 33);
                float streak = Fbm(u * 1f, v * 0.15f, 16, 2, 41);
                float c = 0.6f + (n - 0.5f) * 0.12f - Mathf.Max(0f, streak - 0.6f) * 0.25f;
                int tx = x % 128, ty = y % 128;
                bool tie = (tx - 64) * (tx - 64) + (ty - 64) * (ty - 64) < 9;
                if (tie) c *= 0.75f;
                col[y * N + x] = new Color(c, c, c * 0.98f, 1f);
                h[y * N + x] = n + (tie ? -0.6f : 0f);
            }
            Save("T_Concrete", col, N, N, false);
            Save("T_Concrete_N", NormalFromHeight(h, N, N, 1.5f), N, N, true);
        }

        // 8 m tile: gravel roof membrane
        static void Roof()
        {
            const int N = 256;
            var col = new Color[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float c = 0.38f + (Hash(x, y, 77) - 0.5f) * 0.12f + (Fbm(x / (float)N, y / (float)N, 4, 3, 78) - 0.5f) * 0.1f;
                col[y * N + x] = new Color(c, c * 0.98f, c * 0.96f, 1f);
            }
            Save("T_Roof", col, N, N, false);
        }

        // 6 m tile: mown lawn
        static void Grass()
        {
            const int N = 256;
            var col = new Color[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = x / (float)N, v = y / (float)N;
                float n = Fbm(u, v, 4, 4, 51), blade = Hash(x, y, 52);
                float stripe = Mathf.Sin(u * Mathf.PI * 2f * 3f) > 0f ? 0.03f : -0.03f; // mowing stripes
                col[y * N + x] = new Color(0.20f + n * 0.08f, 0.40f + n * 0.12f + blade * 0.06f + stripe, 0.14f + n * 0.05f, 1f);
            }
            Save("T_Grass", col, N, N, false);
        }

        // ---------------- facades ----------------

        // 1024 px = 32 m: 8 bays x 8 floors of 128 px (4 m). Window rectangle per style, lit-window emission map.
        static void Facade(string style, int seed)
        {
            const int N = 1024, cell = 128;
            var col = new Color[N * N];
            var h = new float[N * N];
            var em = new Color[N * N];

            Color frame, wall, glassA, glassB;
            int marginX, sill, head, mullion;
            switch (style)
            {
                case "Glass": // full-height curtain wall, thin mullions, dark spandrel at each slab
                    frame = new Color(0.12f, 0.13f, 0.15f); wall = new Color(0.15f, 0.17f, 0.2f);
                    glassA = new Color(0.16f, 0.26f, 0.36f); glassB = new Color(0.42f, 0.52f, 0.62f);
                    marginX = 3; sill = 18; head = 125; mullion = 2; break;
                case "Office": // concrete frame with ribbon windows
                    frame = new Color(0.2f, 0.21f, 0.23f); wall = new Color(0.66f, 0.65f, 0.62f);
                    glassA = new Color(0.10f, 0.14f, 0.19f); glassB = new Color(0.30f, 0.38f, 0.46f);
                    marginX = 10; sill = 36; head = 108; mullion = 3; break;
                case "Residential": // punched windows, balcony slab line
                    frame = new Color(0.85f, 0.84f, 0.8f); wall = new Color(0.78f, 0.72f, 0.64f);
                    glassA = new Color(0.12f, 0.15f, 0.18f); glassB = new Color(0.32f, 0.38f, 0.44f);
                    marginX = 30; sill = 34; head = 104; mullion = 3; break;
                default: // "Tower": dark metal panels with vertical fins (hero skyscrapers)
                    frame = new Color(0.07f, 0.08f, 0.1f); wall = new Color(0.1f, 0.11f, 0.13f);
                    glassA = new Color(0.1f, 0.17f, 0.25f); glassB = new Color(0.36f, 0.46f, 0.6f);
                    marginX = 14; sill = 10; head = 122; mullion = 4; break;
            }

            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int bx = x / cell, fy = y / cell, lx = x % cell, ly = y % cell;
                float grime = (Fbm(x / (float)N, y / (float)N, 4, 4, 100 + seed) - 0.5f) * 0.08f;
                bool inWin = lx >= marginX && lx < cell - marginX && ly >= sill && ly < head;
                // residential: two narrower windows per bay
                if (style == "Residential" && inWin && Mathf.Abs(lx - cell / 2) < 8) inWin = false;
                bool mull = inWin && (style == "Glass" || style == "Tower") && Mathf.Abs(lx - cell / 2) < mullion;
                bool rim = inWin && (lx < marginX + mullion || lx >= cell - marginX - mullion || ly < sill + mullion || ly >= head - mullion);

                Color c; float ht;
                if (inWin && !mull && !rim)
                {
                    // sky reflection: lighter toward the top of each pane, a bit different per pane
                    float g = (ly - sill) / (float)(head - sill);
                    float pane = Hash(bx, fy, 200 + seed);
                    c = Color.Lerp(glassA, glassB, g * 0.6f + pane * 0.25f);
                    ht = 0f;
                }
                else if (inWin) { c = frame; ht = 0.6f; }
                else
                {
                    c = wall;
                    ht = 1f;
                    if (style == "Residential" && ly < 10) { c = frame; ht = 1.3f; } // balcony slab edge
                    if (style == "Tower" && (lx < 4 || lx >= cell - 4)) { c = frame * 0.8f; ht = 1.6f; } // vertical fins
                    if (style == "Office" && ly < 6) { c = wall * 0.85f; ht = 0.8f; } // floor joint
                }
                c = new Color(c.r + grime, c.g + grime, c.b + grime, 1f);
                col[y * N + x] = c;
                h[y * N + x] = ht;

                // lit windows: ~45% of panes, warm or cool, brighter under the ceiling, some with blinds
                Color e = Color.black;
                if (inWin && !mull && !rim)
                {
                    float r = Hash(bx * 7 + (lx > cell / 2 ? 3 : 0), fy, 300 + seed);
                    if (r < 0.45f)
                    {
                        float warm = Hash(bx, fy, 400 + seed);
                        Color lamp = warm < 0.65f ? new Color(1f, 0.78f, 0.5f) : new Color(0.75f, 0.88f, 1f);
                        float g = (ly - sill) / (float)(head - sill);
                        float k = 0.55f + 0.45f * g;
                        if (Hash(bx, fy, 500 + seed) < 0.3f && ((ly / 5) % 2 == 0)) k *= 0.45f; // blinds
                        k *= 0.6f + 0.4f * Hash(bx, fy, 600 + seed);
                        e = lamp * k;
                        e.a = 1f;
                    }
                }
                em[y * N + x] = e;
            }
            Save("T_Facade_" + style, col, N, N, false);
            Save("T_Facade_" + style + "_N", NormalFromHeight(h, N, N, 1.2f), N, N, true);
            Save("T_Facade_" + style + "_E", em, N, N, false);
        }

        // 16 m x 8 m (512 x 256): ground-floor shop windows with a sign band above
        static void Storefront()
        {
            const int W = 512, H = 256;
            var col = new Color[W * H];
            var h = new float[W * H];
            var em = new Color[W * H];
            Color[] signs =
            {
                new Color(1f, 0.25f, 0.45f), new Color(0.2f, 0.85f, 1f), new Color(1f, 0.7f, 0.15f), new Color(0.6f, 1f, 0.35f)
            };
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int shop = x / 128, lx = x % 128;
                bool pier = lx < 8 || lx >= 120;
                Color c; float ht; Color e = Color.black;
                if (y < 6) { c = Grey(0.3f); ht = 1f; }                       // plinth
                else if (y < 176 && !pier)
                {
                    bool door = lx > 50 && lx < 78 && y < 140;
                    bool frameBar = y > 140 && y < 146;
                    c = frameBar ? Grey(0.15f) : door ? new Color(0.1f, 0.12f, 0.14f) : new Color(0.14f, 0.18f, 0.22f);
                    ht = frameBar ? 0.5f : 0f;
                    if (!frameBar)
                    {
                        float g = y / 176f;
                        e = new Color(1f, 0.83f, 0.6f) * (0.35f + 0.5f * g) * (0.7f + 0.3f * Hash(shop, 0, 9));
                    }
                }
                else if (y >= 190 && y < 236 && !pier)
                {
                    // sign band: dark panel with a bright lettering bar
                    c = Grey(0.08f); ht = 0.8f;
                    bool letters = y > 202 && y < 224 && lx > 18 && lx < 110 && ((lx / 7) % 3 != 2);
                    if (letters) { e = signs[shop % signs.Length] * 1.2f; c = signs[shop % signs.Length] * 0.5f; }
                }
                else { c = Grey(0.45f + (Fbm(x / (float)W, y / (float)H, 4, 3, 12) - 0.5f) * 0.1f); ht = 1f; }
                c.a = 1f; e.a = 1f;
                col[y * W + x] = c; h[y * W + x] = ht; em[y * W + x] = e;
            }
            Save("T_Storefront", col, W, H, false);
            Save("T_Storefront_N", NormalFromHeight(h, W, H, 1.2f), W, H, true);
            Save("T_Storefront_E", em, W, H, false);
        }

        // 512 x 256 neon ad panels (emissive only)
        static void Billboard(int i)
        {
            const int W = 512, H = 256;
            var col = new Color[W * H];
            Color a = i == 0 ? new Color(1f, 0.15f, 0.55f) : i == 1 ? new Color(0.1f, 0.6f, 1f) : new Color(1f, 0.55f, 0.08f);
            Color b = i == 0 ? new Color(0.3f, 0.1f, 1f) : i == 1 ? new Color(0.1f, 1f, 0.75f) : new Color(1f, 0.1f, 0.2f);
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = x / (float)W, v = y / (float)H;
                Color c = Color.Lerp(a, b, u * 0.7f + v * 0.3f) * 0.55f;
                float dx = u - 0.72f, dy = (v - 0.5f) * 0.5f;
                float ring = Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - 0.16f);
                if (ring < 0.012f) c = Color.white;                                     // logo ring
                if (u > 0.08f && u < 0.5f && v > 0.55f && v < 0.72f && ((x / 9) % 4 != 3)) c = Color.white;   // headline
                if (u > 0.08f && u < 0.38f && v > 0.3f && v < 0.4f && ((x / 6) % 3 != 2)) c = Color.Lerp(c, Color.white, 0.7f); // tagline
                if (x < 6 || x >= W - 6 || y < 6 || y >= H - 6) c = Color.Lerp(a, Color.white, 0.4f); // frame glow
                float scan = (y % 4 == 0) ? 0.85f : 1f;
                col[y * W + x] = new Color(c.r * scan, c.g * scan, c.b * scan, 1f);
            }
            Save("T_Billboard_" + i, col, W, H, false);
        }
    }
}
