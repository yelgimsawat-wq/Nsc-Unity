using System.Collections.Generic;
using Nsc.Arena.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Nsc.City.EditorTools
{
    // Builds the giant-robot downtown map into its own scene (Assets/nok/scene/MegaCity/MegaCity.unity).
    // Tools ▸ NSC ▸ City ▸ Build City Scene — rebuilds everything under "MegaCity" (same art direction as the colosseum:
    // generated meshes + textures, toon shading, outlines, bloom). Tools ▸ NSC ▸ City ▸ Lighting ▸ Day / Sunset / Night.
    //
    // Layout (metres, robots are ~18 m tall): a 10 x 10 grid of 150 m blocks. Road lines k = -5..5 run along both axes:
    //   k = 0      grand boulevard 72 m with a planted median
    //   k = ±3     avenues 52 m
    //   others     streets 34 m
    // The four blocks around the centre are merged into Central Plaza (~370 m square) — the open fighting ground.
    // Towers rise toward the plaza (up to ~330 m), the outskirts are mid-rise; a low-detail skyline rings the horizon.
    public static class CityBuilder
    {
        const string RootDir = "Assets/nok/scene/MegaCity";
        const string ScenePath = RootDir + "/MegaCity.unity";
        const string Dir = RootDir + "/Generated";
        const string MeshDir = Dir + "/Meshes";
        const string MatDir = Dir + "/Materials";
        const string VolumePath = RootDir + "/MegaCity_Volume.asset";
        const string SkyDir = "Assets/map/ligth/AllSkyFree/";
        const string RootName = "MegaCity";

        public enum Look { Day, Sunset, Night }

        const int Lines = 5;            // road lines -5..5
        const float Block = 150f;
        const float Sidewalk = 9f;      // pavement band inside each block edge
        const float CurbH = 0.25f;

        static float[] lineC;           // centre of road line k at lineC[k + Lines]

        static Material mAsphalt, mPaint, mPaintYellow, mCurb, mPave, mPlazaPave, mRoof, mGrass, mConcrete, mMetal, mTrim,
            mBark, mLeaves, mWater, mStorefront, mLampHead, mSignalRed, mSignalGreen, mSignalAmber, mAviation, mGlowCyan,
            mGlowWarm, mOutskirts;
        static Material[] mFacade;      // Glass, Office, Residential, Tower
        static Material[] mBillboard;

        // ================= menus =================

        [MenuItem("Tools/NSC/City/Build City Scene")]
        public static void BuildMenu() => Build(false);

        [MenuItem("Tools/NSC/City/Build City Scene (regenerate textures)")]
        public static void BuildWithTextures() => Build(true);

        [MenuItem("Tools/NSC/City/Lighting/Day")]
        public static void LookDay() => ApplyLook(Look.Day);

        [MenuItem("Tools/NSC/City/Lighting/Sunset")]
        public static void LookSunset() => ApplyLook(Look.Sunset);

        [MenuItem("Tools/NSC/City/Lighting/Night")]
        public static void LookNight() => ApplyLook(Look.Night);

        public static void Build(bool regenerateTextures)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EnsureFolder(Dir); EnsureFolder(MatDir);
            if (regenerateTextures || !CityTextures.Exists()) CityTextures.GenerateAll();
            AssetDatabase.DeleteAsset(MeshDir);
            EnsureFolder(MeshDir);

            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null
                ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            CreateMaterials();
            InitLines();

            var old = GameObject.Find(RootName);
            if (old != null) Object.DestroyImmediate(old);
            var defaultLight = GameObject.Find("Directional Light");
            if (defaultLight != null) Object.DestroyImmediate(defaultLight);
            var root = new GameObject(RootName);

            var rng = new System.Random(20261030);
            streetLightPositions.Clear();
            try
            {
                Progress("Ground", 0.05f); BuildGround(Group(root.transform, "Ground"));
                Progress("Roads", 0.12f); BuildRoads(Group(root.transform, "Roads"));
                Progress("Blocks", 0.25f); BuildBlocks(Group(root.transform, "Blocks"), rng);
                Progress("Plaza", 0.70f); BuildPlaza(Group(root.transform, "CentralPlaza"));
                Progress("Skyline", 0.80f); BuildSkyline(Group(root.transform, "Skyline"), rng);
                Progress("Lighting", 0.90f); BuildLighting(root);
                BuildSpawns(root.transform);
                SetupCamera();
            }
            finally { EditorUtility.ClearProgressBar(); }

            ApplyLook(Look.Sunset);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[City] Built " + ScenePath + " — city " + (2f * CityHalf()).ToString("0") + " m across. " +
                      "Lighting presets: Tools ▸ NSC ▸ City ▸ Lighting.");
        }

        static void Progress(string what, float p) => EditorUtility.DisplayProgressBar("Build City", what, p);

        static Transform Group(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int i = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, i));
            AssetDatabase.CreateFolder(path.Substring(0, i), path.Substring(i + 1));
        }

        // ================= layout =================

        static float RoadW(int k) => k == 0 ? 72f : Mathf.Abs(k) == 3 ? 52f : 34f;
        static float LineC(int k) => lineC[k + Lines];
        static float CityHalf() => LineC(Lines) + RoadW(Lines) * 0.5f;

        static void InitLines()
        {
            lineC = new float[2 * Lines + 1];
            for (int k = 1; k <= Lines; k++)
            {
                float c = LineC(k - 1) + RoadW(k - 1) * 0.5f + Block + RoadW(k) * 0.5f;
                lineC[k + Lines] = c;
                lineC[-k + Lines] = -c;
            }
        }

        // block b (-5..4) spans between line b and line b + 1
        static float BlockMin(int b) => LineC(b) + RoadW(b) * 0.5f;
        static float BlockMax(int b) => LineC(b + 1) - RoadW(b + 1) * 0.5f;
        static bool IsPlazaBlock(int bx, int bz) => (bx == -1 || bx == 0) && (bz == -1 || bz == 0);
        static bool IsPark(int bx, int bz) => (bx == -4 && bz == 2) || (bx == 3 && bz == -3) || (bx == 2 && bz == 3);
        static float PlazaMin() => BlockMin(-1);
        static float PlazaMax() => BlockMax(0);
        static bool InPlaza(float x, float z) => x > PlazaMin() && x < PlazaMax() && z > PlazaMin() && z < PlazaMax();

        // ================= ground + roads =================

        static void BuildGround(Transform parent)
        {
            // asphalt under the whole city in 6 x 6 chunks (blocks and plaza sit on top as raised slabs)
            float half = CityHalf() + 30f;
            int n = 6;
            int ground = GroundLayer();
            for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                float x0 = -half + 2f * half * i / n, x1 = -half + 2f * half * (i + 1) / n;
                float z0 = -half + 2f * half * j / n, z1 = -half + 2f * half * (j + 1) / n;
                var mb = new MeshB();
                FlatQuad(mb, mAsphalt, x0, z0, x1, z1, 0f);
                MakeObj("Asphalt_" + i + "_" + j, parent, mb, true, ground);
            }

            // outskirts out to the horizon, under the skyline ring
            var outer = new MeshB();
            float[] radii = { half, 900f + half, 2200f, 4000f, 7000f };
            for (int r = 0; r < radii.Length - 1; r++)
                SquareRing(outer, mOutskirts, radii[r], radii[r + 1], -0.05f);
            var o = MakeObj("Outskirts", parent, outer, false, 0, false);
            var col = o.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, -1f, 0f);
            col.size = new Vector3(14000f, 2f, 14000f);
            o.layer = ground;
        }

        static void SquareRing(MeshB mb, Material m, float r0, float r1, float y)
        {
            FlatQuad(mb, m, -r1, -r1, r1, -r0, y);
            FlatQuad(mb, m, -r1, r0, r1, r1, y);
            FlatQuad(mb, m, -r1, -r0, -r0, r0, y);
            FlatQuad(mb, m, r0, -r0, r1, r0, y);
        }

        static void FlatQuad(MeshB mb, Material m, float x0, float z0, float x1, float z1, float y)
        {
            Vector3 p0 = new Vector3(x0, y, z0), p1 = new Vector3(x1, y, z0), p2 = new Vector3(x1, y, z1), p3 = new Vector3(x0, y, z1);
            mb.Quad(m, p0, p1, p2, p3, Vector3.up, new Vector2(x0, z0), new Vector2(x1, z0), new Vector2(x1, z1), new Vector2(x0, z1));
        }

        // paint as thin quads 2 cm above the asphalt; `along` = road axis (true = runs along z)
        static void Paint(MeshB mb, Material m, bool alongZ, float across, float s0, float s1, float width)
        {
            float a0 = across - width * 0.5f, a1 = across + width * 0.5f;
            if (alongZ) FlatQuad(mb, m, a0, s0, a1, s1, 0.02f);
            else FlatQuad(mb, m, s0, a0, s1, a1, 0.02f);
        }

        static void BuildRoads(Transform parent)
        {
            for (int axis = 0; axis < 2; axis++)
            for (int k = -Lines; k <= Lines; k++)
            {
                bool alongZ = axis == 0; // axis 0: line at x = c runs along z
                float c = LineC(k), w = RoadW(k);
                var mb = new MeshB();
                var median = new MeshB();
                for (int m = -Lines; m < Lines; m++)
                {
                    float s0 = BlockMin(m), s1 = BlockMax(m); // segment between cross lines m and m + 1
                    bool plaza = Mathf.Abs(c) < 1f && (m == -1 || m == 0);
                    if (plaza) continue; // boulevard is paved over inside the plaza
                    RoadSegment(mb, median, alongZ, k, c, w, s0, s1, m);
                }
                MakeObj((alongZ ? "RoadNS_" : "RoadEW_") + k, parent, mb, false, 0, false);
                if (k == 0) MakeObj((alongZ ? "MedianNS" : "MedianEW"), parent, median, true, GroundLayer());
            }
        }

        static void RoadSegment(MeshB mb, MeshB median, bool alongZ, int k, float c, float w, float s0, float s1, int m)
        {
            float half = w * 0.5f;
            const float crossing = 6f, stopGap = 1.2f;
            float r0 = s0 + crossing + stopGap, r1 = s1 - crossing - stopGap; // lane markings stop before the crosswalks

            // edge lines
            Paint(mb, mPaint, alongZ, c - half + 1f, r0, r1, 0.25f);
            Paint(mb, mPaint, alongZ, c + half - 1f, r0, r1, 0.25f);

            float centreHalf;
            if (k == 0)
            {
                // planted median: raised curb + grass + trees
                centreHalf = 4f;
                Kerb(median, alongZ, c - centreHalf, c + centreHalf, r0 - 1f, r1 + 1f, 0.3f, mGrass);
                for (float s = r0 + 6f; s < r1 - 4f; s += 18f)
                    Tree(median, alongZ ? new Vector3(c, 0.3f, s) : new Vector3(s, 0.3f, c), 0.9f + 0.2f * Mathf.Sin(s), (int)s);
            }
            else
            {
                // double yellow centre line
                centreHalf = 0.3f;
                Paint(mb, mPaintYellow, alongZ, c - 0.22f, r0, r1, 0.16f);
                Paint(mb, mPaintYellow, alongZ, c + 0.22f, r0, r1, 0.16f);
            }

            // dashed lane dividers, lanes ~4.2 m
            int lanes = Mathf.Max(1, Mathf.FloorToInt((half - centreHalf - 1f) / 4.2f));
            float laneW = (half - centreHalf - 1f) / lanes;
            for (int side = -1; side <= 1; side += 2)
            for (int l = 1; l < lanes; l++)
            {
                float across = c + side * (centreHalf + l * laneW);
                for (float s = r0; s < r1 - 3f; s += 9f) Paint(mb, mPaint, alongZ, across, s, s + 3f, 0.15f);
            }

            // stop lines + zebra crossings at both ends
            foreach (float end in new[] { s0, s1 })
            {
                float dir = end == s0 ? 1f : -1f;
                float stop = end + dir * (crossing + stopGap * 0.5f);
                // stop line only across the incoming half (right-hand traffic)
                float inSide = dir > 0f ? -1f : 1f;
                StripAcross(mb, mPaint, alongZ, c + inSide * centreHalf, c + inSide * (half - 0.5f), stop, 0.5f);
                for (float a = c - half + 1.5f; a < c + half - 1.5f; a += 1.2f)
                {
                    if (Mathf.Abs(a - c) < centreHalf + 0.3f) continue;
                    float z0 = end + dir * 0.6f, z1 = end + dir * crossing;
                    Paint(mb, mPaint, alongZ, a + 0.3f, Mathf.Min(z0, z1), Mathf.Max(z0, z1), 0.6f);
                }
            }
        }

        static void StripAcross(MeshB mb, Material m, bool alongZ, float a0, float a1, float s, float width)
        {
            float lo = Mathf.Min(a0, a1), hi = Mathf.Max(a0, a1);
            if (alongZ) FlatQuad(mb, m, lo, s - width * 0.5f, hi, s + width * 0.5f, 0.02f);
            else FlatQuad(mb, m, s - width * 0.5f, lo, s + width * 0.5f, hi, 0.02f);
        }

        // raised slab with concrete curb sides and a top surface
        static void Kerb(MeshB mb, bool alongZ, float a0, float a1, float s0, float s1, float h, Material top)
        {
            float x0 = alongZ ? a0 : s0, x1 = alongZ ? a1 : s1, z0 = alongZ ? s0 : a0, z1 = alongZ ? s1 : a1;
            Slab(mb, x0, z0, x1, z1, h, top);
        }

        static void Slab(MeshB mb, float x0, float z0, float x1, float z1, float h, Material top)
        {
            mb.Box(mCurb, new Vector3((x0 + x1) * 0.5f, h * 0.5f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, h, z1 - z0), false);
            FlatQuad(mb, top, x0 + 0.45f, z0 + 0.45f, x1 - 0.45f, z1 - 0.45f, h + 0.01f);
        }

        // ================= blocks =================

        static void BuildBlocks(Transform parent, System.Random rng)
        {
            int ground = GroundLayer();
            int done = 0;
            for (int bx = -Lines; bx < Lines; bx++)
            for (int bz = -Lines; bz < Lines; bz++)
            {
                Progress("Block " + bx + "," + bz, 0.25f + 0.45f * (done++ / 100f));
                if (IsPlazaBlock(bx, bz)) continue;
                float x0 = BlockMin(bx), x1 = BlockMax(bx), z0 = BlockMin(bz), z1 = BlockMax(bz);
                var blk = Group(parent, "Block_" + bx + "_" + bz);

                var ground_ = new MeshB();
                bool park = IsPark(bx, bz);
                Slab(ground_, x0, z0, x1, z1, CurbH, mPave);
                var props = new MeshB();
                var lamps = new List<Vector3>();
                StreetFurniture(props, x0, z0, x1, z1, lamps);
                if (park) Park(ground_, props, x0, z0, x1, z1, rng);
                MakeObj("Ground", blk, ground_, true, ground);
                MakeObj("Props", blk, props, false, 0);

                if (!park)
                {
                    var bld = new MeshB();
                    Vector2 centre = new Vector2((x0 + x1) * 0.5f, (z0 + z1) * 0.5f);
                    Lots(bld, x0 + Sidewalk, z0 + Sidewalk, x1 - Sidewalk, z1 - Sidewalk, centre.magnitude, rng);
                    MakeObj("Buildings", blk, bld, true, ground);
                }

                // real lights only near the centre (Forward+ handles them, the far ones are just glowing heads)
                if (new Vector2((x0 + x1) * 0.5f, (z0 + z1) * 0.5f).magnitude < 720f)
                    for (int i = 0; i < lamps.Count; i += 4)
                        AddStreetLight(lamps[i]);
            }
        }

        static readonly List<Vector3> streetLightPositions = new List<Vector3>();

        static void AddStreetLight(Vector3 lampHead) => streetLightPositions.Add(lampHead);

        // street lamps (arm over the road) every ~32 m, a few planters and bus-stop shelters on the pavement
        static void StreetFurniture(MeshB mb, float x0, float z0, float x1, float z1, List<Vector3> lamps)
        {
            var edges = new[]
            {
                (new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z0), new Vector3(0f, 0f, -1f)),
                (new Vector3(x1, 0f, z0), new Vector3(x1, 0f, z1), new Vector3(1f, 0f, 0f)),
                (new Vector3(x1, 0f, z1), new Vector3(x0, 0f, z1), new Vector3(0f, 0f, 1f)),
                (new Vector3(x0, 0f, z1), new Vector3(x0, 0f, z0), new Vector3(-1f, 0f, 0f)),
            };
            foreach (var (a, b, outward) in edges)
            {
                Vector3 along = (b - a).normalized;
                float len = Vector3.Distance(a, b);
                int count = Mathf.Max(2, Mathf.RoundToInt(len / 32f));
                for (int i = 0; i < count; i++)
                {
                    float s = (i + 0.5f) * len / count;
                    Vector3 basePos = a + along * s - outward * 1.4f + Vector3.up * CurbH;
                    lamps.Add(StreetLamp(mb, basePos, outward));
                    if (i % 2 == 1) Planter(mb, a + along * (s + len / count * 0.5f) - outward * 3.5f + Vector3.up * CurbH);
                }
                // traffic signal on the corner where this edge starts, arm reaching over the road ahead
                bool eastWest = Mathf.Abs(along.x) > 0.5f;
                Signal(mb, a + (along - outward) * 2f + Vector3.up * CurbH, outward, eastWest);
            }
        }

        // pole + arm over the road + signal head; green for traffic along x, red along z (all signals agree)
        static void Signal(MeshB mb, Vector3 p, Vector3 outward, bool green)
        {
            const float h = 7.5f, reach = 9f;
            mb.Cylinder(mMetal, p, 0.28f, 0.22f, h, 8, true);
            Vector3 top = p + Vector3.up * (h - 0.4f);
            Vector3 tip = top + outward * reach;
            mb.Beam(mMetal, top, tip, 0.25f);
            var save = mb.M;
            mb.M = Matrix4x4.TRS(tip - Vector3.up * 1.4f, Quaternion.LookRotation(outward, Vector3.up), Vector3.one);
            mb.Box(mMetal, Vector3.zero, new Vector3(0.9f, 2.6f, 0.7f));
            float[] ys = { 0.8f, 0f, -0.8f };
            Material[] lamp = { green ? mMetal : mSignalRed, mMetal, green ? mSignalGreen : mMetal };
            for (int i = 0; i < 3; i++) mb.Box(lamp[i], new Vector3(0f, ys[i], 0.38f), new Vector3(0.55f, 0.55f, 0.08f));
            mb.M = save;
        }

        static Vector3 StreetLamp(MeshB mb, Vector3 p, Vector3 outward)
        {
            const float h = 12f, reach = 5.5f;
            mb.Cylinder(mMetal, p, 0.32f, 0.2f, h, 8, true);
            mb.Box(mMetal, p + Vector3.up * 0.4f, new Vector3(0.9f, 0.8f, 0.9f));
            Vector3 top = p + Vector3.up * h;
            Vector3 tip = top + outward * reach + Vector3.up * 0.6f;
            mb.Beam(mMetal, top - Vector3.up * 0.3f, tip, 0.22f);
            // head: long dark housing with a glowing lens underneath
            Vector3 head = tip + outward * 0.9f;
            var save = mb.M;
            mb.M = Matrix4x4.TRS(head, Quaternion.LookRotation(outward, Vector3.up), Vector3.one);
            mb.Box(mMetal, new Vector3(0f, 0.12f, 0f), new Vector3(0.8f, 0.25f, 2.0f));
            mb.Box(mLampHead, new Vector3(0f, -0.04f, 0f), new Vector3(0.6f, 0.08f, 1.7f));
            mb.M = save;
            return head - Vector3.up * 0.2f;
        }

        static void Planter(MeshB mb, Vector3 p)
        {
            mb.Box(mConcrete, p + Vector3.up * 0.45f, new Vector3(2.4f, 0.9f, 2.4f));
            mb.Box(mGrass, p + Vector3.up * 0.92f, new Vector3(2.1f, 0.05f, 2.1f));
            Tree(mb, p + Vector3.up * 0.9f, 0.55f, (int)(p.x * 3f + p.z));
        }

        // low-poly tree: tapered trunk + layered rounded canopy
        static void Tree(MeshB mb, Vector3 p, float s, int seed)
        {
            float h = 6f * s, r = 4.2f * s;
            mb.Cylinder(mBark, p, 0.55f * s, 0.35f * s, h + 1f, 7, false);
            float wob = Mathf.Abs(Mathf.Sin(seed * 12.9898f)) * 0.3f;
            var prof = new List<Vector2>
            {
                new Vector2(0.05f, 0f), new Vector2(r * 0.8f, 0.6f * s), new Vector2(r * (1f + wob * 0.3f), 2.6f * s),
                new Vector2(r * 0.85f, 5.2f * s), new Vector2(r * 0.45f, 7.4f * s), new Vector2(0.05f, 8.4f * s)
            };
            mb.Lathe(mLeaves, p + Vector3.up * (h - 1f), prof, 9);
        }

        static void Park(MeshB ground, MeshB props, float x0, float z0, float x1, float z1, System.Random rng)
        {
            float m = 9f;
            // lawn quadrants split by two paved paths, a pond in one quadrant
            float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;
            foreach (var (a0, b0, a1, b1) in new[]
                     {
                         (x0 + m, z0 + m, cx - 3f, cz - 3f), (cx + 3f, z0 + m, x1 - m, cz - 3f),
                         (x0 + m, cz + 3f, cx - 3f, z1 - m), (cx + 3f, cz + 3f, x1 - m, z1 - m)
                     })
                Slab(ground, a0, b0, a1, b1, CurbH + 0.2f, mGrass);
            float pr = 18f;
            Vector3 pond = new Vector3(cx + 32f, CurbH + 0.21f, cz + 32f);
            ground.Disc(mWater, pond + Vector3.up * 0.02f, pr, 40, true);
            ground.Annulus(mCurb, pond + Vector3.up * 0.05f, pr, pr + 0.8f, 40, true);
            for (int i = 0; i < 34; i++)
            {
                float x = Mathf.Lerp(x0 + m + 4f, x1 - m - 4f, (float)rng.NextDouble());
                float z = Mathf.Lerp(z0 + m + 4f, z1 - m - 4f, (float)rng.NextDouble());
                if (Mathf.Abs(x - cx) < 6f || Mathf.Abs(z - cz) < 6f) continue;
                if (Vector2.Distance(new Vector2(x, z), new Vector2(pond.x, pond.z)) < pr + 5f) continue;
                Tree(props, new Vector3(x, CurbH + 0.2f, z), 0.9f + (float)rng.NextDouble() * 0.7f, i * 31);
            }
        }

        // ================= buildings =================

        // split a lot into 1, 2 or 4 parcels, one building each
        static void Lots(MeshB mb, float x0, float z0, float x1, float z1, float dist, System.Random rng)
        {
            float t = Mathf.InverseLerp(950f, 220f, dist);   // 0 = outskirts, 1 = next to the plaza
            double r = rng.NextDouble();
            if (t > 0.75f && r < 0.45) { Building(mb, x0, z0, x1, z1, t, rng, true); return; } // one hero tower
            if (r < 0.35)
            {
                Building(mb, x0, z0, x1, z1, t, rng, false);
                return;
            }
            float gap = 6f;
            if (r < 0.65)
            {
                bool splitX = rng.NextDouble() < 0.5;
                float f = Mathf.Lerp(0.4f, 0.6f, (float)rng.NextDouble());
                if (splitX)
                {
                    float xm = Mathf.Lerp(x0, x1, f);
                    Building(mb, x0, z0, xm - gap * 0.5f, z1, t, rng, false);
                    Building(mb, xm + gap * 0.5f, z0, x1, z1, t, rng, false);
                }
                else
                {
                    float zm = Mathf.Lerp(z0, z1, f);
                    Building(mb, x0, z0, x1, zm - gap * 0.5f, t, rng, false);
                    Building(mb, x0, zm + gap * 0.5f, x1, z1, t, rng, false);
                }
                return;
            }
            float xc = Mathf.Lerp(x0, x1, 0.5f), zc = Mathf.Lerp(z0, z1, 0.5f);
            Building(mb, x0, z0, xc - gap * 0.5f, zc - gap * 0.5f, t, rng, false);
            Building(mb, xc + gap * 0.5f, z0, x1, zc - gap * 0.5f, t, rng, false);
            Building(mb, x0, zc + gap * 0.5f, xc - gap * 0.5f, z1, t, rng, false);
            Building(mb, xc + gap * 0.5f, zc + gap * 0.5f, x1, z1, t, rng, false);
        }

        static float Rand(System.Random rng, float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());
        static float Floors(float h) => Mathf.Max(4f, Mathf.Round(h / 4f) * 4f);

        static void Building(MeshB mb, float x0, float z0, float x1, float z1, float t, System.Random rng, bool hero)
        {
            float maxH = Mathf.Lerp(44f, 250f, Mathf.Pow(t, 1.3f));
            float H = hero ? Rand(rng, 230f, 330f) : Floors(Rand(rng, 0.45f, 1f) * maxH);
            H = Floors(Mathf.Max(H, 24f));

            int style;
            if (hero) style = 3;
            else if (H > 120f) style = rng.NextDouble() < 0.7 ? 0 : 1;
            else if (H > 60f) style = rng.NextDouble() < 0.5 ? 1 : 0;
            else style = rng.NextDouble() < 0.6 ? 2 : 1;
            Material facade = mFacade[style];

            // podium: shops on the ground floor, full parcel
            const float podiumH = 8f;
            Prism(mb, mStorefront, mRoof, x0, z0, x1, z1, 0f, podiumH);
            Cornice(mb, x0, z0, x1, z1, podiumH, 0.8f);
            if (rng.NextDouble() < 0.25) Billboard(mb, x0, z0, x1, z1, podiumH, rng);

            // shaft: inset from the podium, 1-3 tiers stepping back
            float inset = Rand(rng, 2f, 6f);
            float bx0 = x0 + inset, bx1 = x1 - inset, bz0 = z0 + inset, bz1 = z1 - inset;
            int tiers = H > 170f ? 3 : H > 90f ? 2 : 1;
            float[] split = tiers == 3 ? new[] { 0.58f, 0.86f, 1f } : tiers == 2 ? new[] { 0.72f, 1f } : new[] { 1f };
            float y = podiumH;
            bool squareish = Mathf.Abs((bx1 - bx0) - (bz1 - bz0)) < 12f;
            double shape = rng.NextDouble();
            int sides = !squareish || hero ? 0 : style == 0 && shape < 0.18 ? 32 : style <= 1 && shape < 0.33 ? 8 : 0; // round / octagonal towers
            for (int i = 0; i < tiers; i++)
            {
                float yTop = Floors(podiumH + (H - podiumH) * split[i]);
                if (yTop <= y + 4f) continue;
                if (sides > 0)
                {
                    // round or octagonal shaft (octagon turned 22.5° so its flat faces line up with the streets)
                    float rr = Mathf.Min(bx1 - bx0, bz1 - bz0) * 0.5f / (sides == 8 ? Mathf.Cos(Mathf.PI / 8f) : 1f);
                    Vector3 c = new Vector3((bx0 + bx1) * 0.5f, y, (bz0 + bz1) * 0.5f);
                    var save = mb.M;
                    mb.M = Matrix4x4.TRS(c, Quaternion.Euler(0f, sides == 8 ? 22.5f : 0f, 0f), Vector3.one);
                    mb.Lathe(facade, Vector3.zero, new List<Vector2> { new Vector2(rr, 0f), new Vector2(rr, yTop - y) }, sides);
                    mb.Disc(mRoof, Vector3.up * (yTop - y), rr, sides, true);
                    mb.Annulus(mTrim, Vector3.up * (yTop - y + 0.6f), rr - 0.6f, rr + 0.3f, sides, true);
                    mb.M = save;
                }
                else
                {
                    Prism(mb, facade, mRoof, bx0, bz0, bx1, bz1, y, yTop);
                    Parapet(mb, bx0, bz0, bx1, bz1, yTop);
                    if (hero) CornerLights(mb, bx0, bz0, bx1, bz1, y, yTop);
                    // office slabs read as horizontal ledges every 4 floors (shadow lines on the facade)
                    if (style == 1) for (float ly = y + 16f; ly < yTop - 2f; ly += 16f) Cornice(mb, bx0, bz0, bx1, bz1, ly, 0.5f);
                    // tall glass towers get a glowing crown band under the parapet
                    if (style == 0 && i == tiers - 1 && H > 140f) CrownBand(mb, bx0, bz0, bx1, bz1, yTop - 3f);
                }
                y = yTop;
                float step = Mathf.Min(bx1 - bx0, bz1 - bz0) * 0.12f;
                bx0 += step; bx1 -= step; bz0 += step; bz1 -= step;
            }

            Crown(mb, bx0 - 0f, bz0, bx1, bz1, y, H, style, hero, rng);
        }

        // four walls (metre UVs, floors line up across the whole city) + flat roof
        static void Prism(MeshB mb, Material side, Material roof, float x0, float z0, float x1, float z1, float y0, float y1)
        {
            Wall(mb, side, new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z0), y0, y1, Vector3.back);
            Wall(mb, side, new Vector3(x1, 0f, z0), new Vector3(x1, 0f, z1), y0, y1, Vector3.right);
            Wall(mb, side, new Vector3(x1, 0f, z1), new Vector3(x0, 0f, z1), y0, y1, Vector3.forward);
            Wall(mb, side, new Vector3(x0, 0f, z1), new Vector3(x0, 0f, z0), y0, y1, Vector3.left);
            FlatQuad(mb, roof, x0, z0, x1, z1, y1);
        }

        static void Wall(MeshB mb, Material m, Vector3 a, Vector3 b, float y0, float y1, Vector3 n)
        {
            // u runs along the wall from a facade-tile-aligned origin so window bays never get cut at corners
            float len = Vector3.Distance(a, b);
            float u0 = Mathf.Round(Vector3.Dot(a, (b - a).normalized) / 4f) * 4f;
            mb.Quad(m, a + Vector3.up * y0, b + Vector3.up * y0, b + Vector3.up * y1, a + Vector3.up * y1, n,
                new Vector2(u0, y0), new Vector2(u0 + len, y0), new Vector2(u0 + len, y1), new Vector2(u0, y1));
        }

        static void Cornice(MeshB mb, float x0, float z0, float x1, float z1, float y, float th)
        {
            const float o = 0.6f;
            mb.Box(mTrim, new Vector3((x0 + x1) * 0.5f, y - th * 0.5f, z0 - o * 0.5f), new Vector3(x1 - x0 + 2f * o, th, o));
            mb.Box(mTrim, new Vector3((x0 + x1) * 0.5f, y - th * 0.5f, z1 + o * 0.5f), new Vector3(x1 - x0 + 2f * o, th, o));
            mb.Box(mTrim, new Vector3(x0 - o * 0.5f, y - th * 0.5f, (z0 + z1) * 0.5f), new Vector3(o, th, z1 - z0));
            mb.Box(mTrim, new Vector3(x1 + o * 0.5f, y - th * 0.5f, (z0 + z1) * 0.5f), new Vector3(o, th, z1 - z0));
        }

        static void Parapet(MeshB mb, float x0, float z0, float x1, float z1, float y)
        {
            const float h = 1.4f, t = 0.5f;
            mb.Box(mTrim, new Vector3((x0 + x1) * 0.5f, y + h * 0.5f, z0 + t * 0.5f), new Vector3(x1 - x0, h, t));
            mb.Box(mTrim, new Vector3((x0 + x1) * 0.5f, y + h * 0.5f, z1 - t * 0.5f), new Vector3(x1 - x0, h, t));
            mb.Box(mTrim, new Vector3(x0 + t * 0.5f, y + h * 0.5f, (z0 + z1) * 0.5f), new Vector3(t, h, z1 - z0 - 2f * t));
            mb.Box(mTrim, new Vector3(x1 - t * 0.5f, y + h * 0.5f, (z0 + z1) * 0.5f), new Vector3(t, h, z1 - z0 - 2f * t));
        }

        // thin glowing strips up the corners of hero towers (the "high-tech" look of the arena)
        static void CornerLights(MeshB mb, float x0, float z0, float x1, float z1, float y0, float y1)
        {
            foreach (var p in new[] { new Vector2(x0, z0), new Vector2(x1, z0), new Vector2(x1, z1), new Vector2(x0, z1) })
                mb.Box(mGlowCyan, new Vector3(p.x, (y0 + y1) * 0.5f, p.y), new Vector3(0.5f, y1 - y0, 0.5f));
            mb.Box(mGlowCyan, new Vector3((x0 + x1) * 0.5f, y1 - 2f, z0 - 0.1f), new Vector3(x1 - x0, 0.6f, 0.3f));
            mb.Box(mGlowCyan, new Vector3((x0 + x1) * 0.5f, y1 - 2f, z1 + 0.1f), new Vector3(x1 - x0, 0.6f, 0.3f));
        }

        static void CrownBand(MeshB mb, float x0, float z0, float x1, float z1, float y)
        {
            const float o = 0.25f, h = 0.8f;
            mb.Box(mGlowWarm, new Vector3((x0 + x1) * 0.5f, y, z0 - o), new Vector3(x1 - x0, h, 0.3f));
            mb.Box(mGlowWarm, new Vector3((x0 + x1) * 0.5f, y, z1 + o), new Vector3(x1 - x0, h, 0.3f));
            mb.Box(mGlowWarm, new Vector3(x0 - o, y, (z0 + z1) * 0.5f), new Vector3(0.3f, h, z1 - z0));
            mb.Box(mGlowWarm, new Vector3(x1 + o, y, (z0 + z1) * 0.5f), new Vector3(0.3f, h, z1 - z0));
        }

        static void Billboard(MeshB mb, float x0, float z0, float x1, float z1, float y, System.Random rng)
        {
            Material m = mBillboard[rng.Next(mBillboard.Length)];
            int face = rng.Next(4);
            float w = Mathf.Min(24f, (face % 2 == 0 ? x1 - x0 : z1 - z0) * 0.7f), h = w * 0.5f;
            float yb = y + 1f;
            Vector3 c, n, right;
            switch (face)
            {
                case 0: c = new Vector3((x0 + x1) * 0.5f, 0f, z0 - 0.4f); n = Vector3.back; right = Vector3.right; break;
                case 1: c = new Vector3(x1 + 0.4f, 0f, (z0 + z1) * 0.5f); n = Vector3.right; right = Vector3.forward; break;
                case 2: c = new Vector3((x0 + x1) * 0.5f, 0f, z1 + 0.4f); n = Vector3.forward; right = Vector3.left; break;
                default: c = new Vector3(x0 - 0.4f, 0f, (z0 + z1) * 0.5f); n = Vector3.left; right = Vector3.back; break;
            }
            Vector3 bl = c - right * w * 0.5f + Vector3.up * yb, br = c + right * w * 0.5f + Vector3.up * yb;
            mb.Box(mMetal, (bl + br) * 0.5f + Vector3.up * h * 0.5f - n * 0.3f,
                new Vector3(Mathf.Abs(right.x) * w + Mathf.Abs(n.x) * 0.5f + 0.6f, h + 0.6f, Mathf.Abs(right.z) * w + Mathf.Abs(n.z) * 0.5f + 0.6f));
            mb.Quad(m, bl + n * 0.05f, br + n * 0.05f, br + Vector3.up * h + n * 0.05f, bl + Vector3.up * h + n * 0.05f, n,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f));
        }

        static void Crown(MeshB mb, float x0, float z0, float x1, float z1, float y, float H, int style, bool hero, System.Random rng)
        {
            float w = x1 - x0, d = z1 - z0;
            Vector3 c = new Vector3((x0 + x1) * 0.5f, y, (z0 + z1) * 0.5f);
            if (hero)
            {
                // stepped crown + spire with aviation light
                mb.Box(mMetal, c + Vector3.up * 4f, new Vector3(w * 0.6f, 8f, d * 0.6f));
                mb.Box(mGlowCyan, c + Vector3.up * 8.3f, new Vector3(w * 0.62f, 0.6f, d * 0.62f));
                mb.Box(mMetal, c + Vector3.up * 11f, new Vector3(w * 0.35f, 6f, d * 0.35f));
                mb.Cylinder(mMetal, c + Vector3.up * 14f, 1.6f, 0.25f, 46f, 8, false);
                mb.Box(mAviation, c + Vector3.up * 60.5f, new Vector3(1.2f, 1.2f, 1.2f));
                return;
            }
            // mechanical plant, vents, water tank on residential, helipad on some tall glass towers
            int boxes = 2 + rng.Next(3);
            for (int i = 0; i < boxes; i++)
            {
                float bw = Rand(rng, 4f, Mathf.Max(5f, w * 0.35f)), bd = Rand(rng, 4f, Mathf.Max(5f, d * 0.35f)), bh = Rand(rng, 2.5f, 6f);
                float px = Rand(rng, x0 + bw * 0.5f + 1.5f, x1 - bw * 0.5f - 1.5f), pz = Rand(rng, z0 + bd * 0.5f + 1.5f, z1 - bd * 0.5f - 1.5f);
                mb.Box(mMetal, new Vector3(px, y + bh * 0.5f, pz), new Vector3(bw, bh, bd), false);
            }
            if (style == 2 && rng.NextDouble() < 0.6)
            {
                Vector3 tank = new Vector3(x0 + 5f, y, z1 - 5f);
                for (int i = 0; i < 4; i++)
                    mb.Beam(mMetal, tank + new Vector3(i % 2 == 0 ? -1.5f : 1.5f, 0f, i < 2 ? -1.5f : 1.5f), tank + new Vector3(i % 2 == 0 ? -1.2f : 1.2f, 4f, i < 2 ? -1.2f : 1.2f), 0.25f);
                mb.Cylinder(mBark, tank + Vector3.up * 4f, 2.3f, 2.3f, 4.5f, 12, true);
            }
            if (style == 0 && H > 140f && rng.NextDouble() < 0.45 && w > 26f && d > 26f)
            {
                mb.Cylinder(mConcrete, c, 11f, 11f, 1.2f, 24, true);
                mb.Annulus(mPaintYellow, c + Vector3.up * 1.22f, 9f, 9.6f, 24, true);
            }
            if (H > 110f)
            {
                Vector3 mast = new Vector3(x1 - 3f, y, z0 + 3f);
                mb.Cylinder(mMetal, mast, 0.35f, 0.15f, 18f, 6, false);
                mb.Box(mAviation, mast + Vector3.up * 18.4f, new Vector3(0.8f, 0.8f, 0.8f));
            }
        }

        // ================= central plaza =================

        static void BuildPlaza(Transform parent)
        {
            float p0 = PlazaMin(), p1 = PlazaMax();
            int ground = GroundLayer();
            var mb = new MeshB();
            Slab(mb, p0, p0, p1, p1, CurbH, mPlazaPave);

            // concentric inlays + radial glow lines (arena style), all flush with the paving
            for (int i = 1; i <= 3; i++)
            {
                float r = 40f * i + 20f;
                mb.Annulus(i == 3 ? mGlowCyan : mTrim, new Vector3(0f, CurbH + 0.03f, 0f), r, r + (i == 3 ? 0.8f : 1.6f), 96, true);
            }
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 s = dir * 62f + Vector3.up * (CurbH + 0.03f), e = dir * 150f + Vector3.up * (CurbH + 0.03f);
                Vector3 side = Vector3.Cross(Vector3.up, dir) * 0.4f;
                mb.Quad(mGlowCyan, s - side, s + side, e + side, e - side, Vector3.up, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
            }

            // fountain in the middle
            Vector3 c = new Vector3(0f, CurbH, 0f);
            mb.Cylinder(mConcrete, c, 26f, 26f, 1.2f, 64, false);
            mb.Annulus(mTrim, c + Vector3.up * 1.2f, 24.5f, 26.2f, 64, true);
            mb.Disc(mWater, c + Vector3.up * 0.8f, 24.5f, 64, true);
            mb.Cylinder(mConcrete, c + Vector3.up * 0.8f, 6f, 4.5f, 5f, 24, true);
            mb.Cylinder(mMetal, c + Vector3.up * 5.8f, 1.6f, 0.6f, 14f, 12, true);
            mb.Annulus(mGlowWarm, c + Vector3.up * 0.85f, 23.5f, 24.3f, 64, true);

            // planters and benches around the edge
            for (int i = 0; i < 24; i++)
            {
                float a = i * Mathf.PI * 2f / 24f;
                Vector3 p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 168f + Vector3.up * CurbH;
                if (Mathf.Abs(p.x) > p1 - 12f || Mathf.Abs(p.z) > p1 - 12f) continue;
                Planter(mb, p);
            }

            // four light masts at the corners
            foreach (var corner in new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) })
            {
                Vector3 p = new Vector3(corner.x * (p1 - 22f), CurbH, corner.y * (p1 - 22f));
                mb.Cylinder(mMetal, p, 1.6f, 0.9f, 48f, 10, true);
                Vector3 inward = -new Vector3(corner.x, 0f, corner.y).normalized;
                var save = mb.M;
                mb.M = Matrix4x4.TRS(p + Vector3.up * 48f + inward * 1.5f, Quaternion.LookRotation(inward, Vector3.up) * Quaternion.Euler(25f, 0f, 0f), Vector3.one);
                mb.Box(mMetal, Vector3.zero, new Vector3(10f, 4f, 1.6f));
                mb.Box(mLampHead, new Vector3(0f, 0f, 0.85f), new Vector3(9f, 3.2f, 0.1f));
                mb.M = save;
            }

            // two giant LED screens on steel frames at the north and south edges, facing the centre (like the arena screens)
            for (int i = 0; i < 2; i++)
            {
                float z = (i == 0 ? 1f : -1f) * (p1 - 14f);
                var save = mb.M;
                mb.M = Matrix4x4.TRS(new Vector3(0f, CurbH, z), Quaternion.LookRotation(new Vector3(0f, 0f, -z), Vector3.up), Vector3.one);
                const float sw = 64f, sh = 32f, legH = 26f;
                foreach (float lx in new[] { -sw * 0.38f, sw * 0.38f })
                {
                    mb.Box(mMetal, new Vector3(lx, legH * 0.5f, -1.5f), new Vector3(2.4f, legH, 2.4f));
                    mb.Beam(mMetal, new Vector3(lx, 0f, -9f), new Vector3(lx, legH * 0.8f, -1.5f), 1.2f);
                }
                mb.Box(mMetal, new Vector3(0f, legH + sh * 0.5f, -1.5f), new Vector3(sw + 3f, sh + 3f, 2.2f));
                mb.Quad(mBillboard[i], new Vector3(sw * 0.5f, legH, 0f), new Vector3(-sw * 0.5f, legH, 0f),
                    new Vector3(-sw * 0.5f, legH + sh, 0f), new Vector3(sw * 0.5f, legH + sh, 0f), Vector3.forward,
                    new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f));
                mb.Box(mGlowCyan, new Vector3(0f, legH + sh + 1.8f, -1f), new Vector3(sw + 3f, 0.5f, 0.5f));
                mb.M = save;
            }

            // low bollards around the plaza edge every 12 m
            for (float a = p0 + 6f; a < p1 - 4f; a += 12f)
                foreach (var b in new[] { new Vector3(a, 0f, p0 + 3f), new Vector3(a, 0f, p1 - 3f), new Vector3(p0 + 3f, 0f, a), new Vector3(p1 - 3f, 0f, a) })
                {
                    mb.Cylinder(mMetal, b + Vector3.up * CurbH, 0.45f, 0.4f, 1.3f, 8, true);
                    mb.Annulus(mGlowCyan, b + Vector3.up * (CurbH + 1.1f), 0.41f, 0.47f, 8, true);
                }

            MakeObj("Plaza", parent, mb, true, ground);
        }

        // ================= skyline ring =================

        static void BuildSkyline(Transform parent, System.Random rng)
        {
            const int sectors = 8;
            float r0 = CityHalf() + 420f, r1 = r0 + 1500f;
            for (int s = 0; s < sectors; s++)
            {
                var mb = new MeshB();
                for (int i = 0; i < 70; i++)
                {
                    float a = (s + (float)rng.NextDouble()) * Mathf.PI * 2f / sectors;
                    float r = Mathf.Lerp(r0, r1, Mathf.Pow((float)rng.NextDouble(), 0.7f));
                    float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
                    float w = Rand(rng, 30f, 70f), d = Rand(rng, 30f, 70f);
                    float h = Floors(Rand(rng, 40f, 220f) * Mathf.Lerp(1f, 0.5f, (r - r0) / (r1 - r0)));
                    Prism(mb, mFacade[rng.Next(3)], mRoof, x - w * 0.5f, z - d * 0.5f, x + w * 0.5f, z + d * 0.5f, 0f, h);
                    if (h > 150f) mb.Box(mAviation, new Vector3(x, h + 1.5f, z), new Vector3(1.5f, 1.5f, 1.5f));
                }
                MakeObj("Skyline_" + s, parent, mb, false, 0, false);
            }
        }

        // ================= lights / atmosphere =================

        static void BuildLighting(GameObject root)
        {
            var lt = Group(root.transform, "Lighting");
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(lt, false);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;

            var probeGo = new GameObject("ReflectionProbe");
            probeGo.transform.SetParent(lt, false);
            probeGo.transform.position = new Vector3(0f, 60f, 0f);
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.size = new Vector3(2.4f * CityHalf(), 700f, 2.4f * CityHalf());
            probe.resolution = 256;
            probe.farClipPlane = 6000f;

            // street lights: spot down from each lamp head (only the inner city, every fourth lamp — Forward+ shows up to ~256)
            var sl = Group(lt, "StreetLights");
            int n = 0;
            foreach (var p in streetLightPositions)
            {
                var go = new GameObject("StreetLight_" + n++);
                go.transform.SetParent(sl, false);
                go.transform.position = p;
                go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot;
                l.spotAngle = 120f;
                l.innerSpotAngle = 70f;
                l.range = 38f;
                l.color = new Color(1f, 0.78f, 0.52f);
                l.shadows = LightShadows.None;
            }
            streetLightPositions.Clear();

            // plaza floodlights from the four masts, aimed at the centre
            var fl = Group(lt, "PlazaFloodlights");
            float p1 = PlazaMax();
            foreach (var corner in new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) })
            {
                Vector3 p = new Vector3(corner.x * (p1 - 22f), 50f, corner.y * (p1 - 22f));
                var go = new GameObject("Floodlight");
                go.transform.SetParent(fl, false);
                go.transform.position = p;
                go.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 0f, 0f) - p);
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot;
                l.spotAngle = 70f;
                l.innerSpotAngle = 40f;
                l.range = 420f;
                l.color = new Color(0.85f, 0.92f, 1f);
                l.shadows = LightShadows.Soft;
            }

            if (root.GetComponent<Nsc.Arena.ArenaShadowDistance>() == null) root.AddComponent<Nsc.Arena.ArenaShadowDistance>();

            // global volume with its own profile (same grade family as the colosseum)
            var volGo = GameObject.Find("Global Volume");
            if (volGo == null) volGo = new GameObject("Global Volume");
            volGo.transform.SetParent(root.transform, false);
            var vol = volGo.GetComponent<Volume>();
            if (vol == null) vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumePath);
            }
            vol.sharedProfile = profile;
            SetupVolume(profile);
        }

        static T VolumeComp<T>(VolumeProfile p) where T : VolumeComponent
        {
            if (!p.TryGet(out T c))
            {
                c = p.Add<T>();
                c.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(c, p);
            }
            c.active = true;
            return c;
        }

        static void SetupVolume(VolumeProfile p)
        {
            VolumeComp<Tonemapping>(p).mode.Override(TonemappingMode.ACES);
            var bloom = VolumeComp<Bloom>(p);
            bloom.threshold.Override(0.95f);
            bloom.intensity.Override(0.8f);
            bloom.scatter.Override(0.72f);
            var ca = VolumeComp<ColorAdjustments>(p);
            ca.contrast.Override(18f);
            ca.saturation.Override(6f);
            var vg = VolumeComp<Vignette>(p);
            vg.intensity.Override(0.22f);
            vg.smoothness.Override(0.45f);
            var smh = VolumeComp<ShadowsMidtonesHighlights>(p);
            smh.shadows.Override(new Vector4(0.92f, 0.97f, 1.1f, 0f));
            smh.highlights.Override(new Vector4(1.05f, 1.0f, 0.95f, 0f));

            var ol = VolumeComp<ToonShadersPro.URP.OutlineSettings>(p);
            ol.outlineType.Override(ToonShadersPro.URP.OutlineType.DepthNormalOutlines);
            ol.outlineColor.Override(new Color(0.05f, 0.05f, 0.08f, 1f));
            ol.colorSensitivity.Override(1f);
            ol.colorStrength.Override(0f);
            ol.depthSensitivity.Override(0.0006f);
            ol.depthStrength.Override(1f);
            ol.normalSensitivity.Override(0.25f);
            ol.normalStrength.Override(0.8f);
            ol.depthThreshold.Override(0.9f);
            EditorUtility.SetDirty(p);
        }

        struct LookDef
        {
            public string sky;
            public Vector3 sunEuler;
            public Color sun, ambSky, ambEq, ambGround, fog;
            public float sunI, fogStart, fogEnd, windows, shops, lamps, streetLights, exposure;
        }

        static LookDef Def(Look look)
        {
            switch (look)
            {
                case Look.Day:
                    return new LookDef
                    {
                        sky = "Cartoon Base BlueSky/Day_BlueSky_Nothing", sunEuler = new Vector3(52f, 35f, 0f), sun = new Color(1f, 0.96f, 0.9f), sunI = 1.9f,
                        ambSky = new Color(0.72f, 0.82f, 1f), ambEq = new Color(0.75f, 0.76f, 0.78f), ambGround = new Color(0.42f, 0.4f, 0.38f),
                        fog = new Color(0.7f, 0.79f, 0.9f), fogStart = 600f, fogEnd = 4800f,
                        windows = 0.05f, shops = 0.4f, lamps = 0.1f, streetLights = 0f, exposure = 0f
                    };
                case Look.Night:
                    return new LookDef
                    {
                        sky = "Cold Night/Cold Night", sunEuler = new Vector3(38f, 150f, 0f), sun = new Color(0.55f, 0.66f, 1f), sunI = 0.22f,
                        ambSky = new Color(0.12f, 0.15f, 0.26f), ambEq = new Color(0.1f, 0.1f, 0.15f), ambGround = new Color(0.05f, 0.05f, 0.06f),
                        fog = new Color(0.06f, 0.08f, 0.14f), fogStart = 250f, fogEnd = 3200f,
                        windows = 2.2f, shops = 2.4f, lamps = 4f, streetLights = 1f, exposure = 0.35f
                    };
                default:
                    return new LookDef
                    {
                        sky = "Cold Sunset/Cold Sunset", sunEuler = new Vector3(11f, 238f, 0f), sun = new Color(1f, 0.72f, 0.48f), sunI = 1.5f,
                        ambSky = new Color(0.62f, 0.62f, 0.8f), ambEq = new Color(0.82f, 0.6f, 0.5f), ambGround = new Color(0.32f, 0.27f, 0.25f),
                        fog = new Color(0.78f, 0.6f, 0.5f), fogStart = 380f, fogEnd = 3800f,
                        windows = 0.8f, shops = 1.4f, lamps = 2.5f, streetLights = 0.6f, exposure = 0f
                    };
            }
        }

        public static void ApplyLook(Look look)
        {
            var root = GameObject.Find(RootName);
            if (root == null) { Debug.LogWarning("[City] Open MegaCity first (Tools ▸ NSC ▸ City ▸ Build City Scene)."); return; }
            LoadMaterials();
            LookDef d = Def(look);

            var sunT = root.transform.Find("Lighting/Sun");
            if (sunT != null)
            {
                sunT.rotation = Quaternion.Euler(d.sunEuler);
                var sun = sunT.GetComponent<Light>();
                sun.color = d.sun;
                sun.intensity = d.sunI;
                sun.shadowStrength = look == Look.Night ? 0.6f : 0.9f;
                RenderSettings.sun = sun;
            }

            var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyDir + d.sky + ".mat");
            if (sky != null) RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = d.ambSky;
            RenderSettings.ambientEquatorColor = d.ambEq;
            RenderSettings.ambientGroundColor = d.ambGround;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = d.fogStart;
            RenderSettings.fogEndDistance = d.fogEnd;
            RenderSettings.fogColor = d.fog;

            foreach (var m in mFacade) SetEmission(m, Color.white * d.windows);
            SetEmission(mStorefront, Color.white * d.shops);
            SetEmission(mLampHead, new Color(1f, 0.85f, 0.6f) * d.lamps);
            foreach (var m in mBillboard) SetEmission(m, Color.white * Mathf.Max(1.2f, d.windows * 1.5f));
            SetEmission(mAviation, new Color(1f, 0.08f, 0.05f) * (look == Look.Day ? 1.5f : 6f));
            SetEmission(mGlowCyan, new Color(0.1f, 0.75f, 1.6f) * (look == Look.Day ? 0.8f : 2.5f));
            SetEmission(mGlowWarm, new Color(1.6f, 0.85f, 0.35f) * (look == Look.Day ? 0.5f : 2f));

            foreach (var name in new[] { "Lighting/StreetLights", "Lighting/PlazaFloodlights" })
            {
                var g = root.transform.Find(name);
                if (g == null) continue;
                bool flood = name.EndsWith("Floodlights");
                foreach (var l in g.GetComponentsInChildren<Light>(true))
                {
                    l.enabled = d.streetLights > 0f;
                    l.intensity = (flood ? 60000f : 180f) * d.streetLights;
                }
            }

            var vol = root.GetComponentInChildren<Volume>();
            if (vol != null && vol.sharedProfile != null && vol.sharedProfile.TryGet(out ColorAdjustments ca))
            {
                ca.postExposure.Override(d.exposure);
                EditorUtility.SetDirty(vol.sharedProfile);
            }

            DynamicGI.UpdateEnvironment();
            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log("[City] Lighting: " + look);
        }

        static void SetEmission(Material m, Color c)
        {
            if (m == null) return;
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(m);
        }

        static void BuildSpawns(Transform root)
        {
            var sp = Group(root, "SpawnPoints");
            foreach (var (name, x) in new[] { ("SpawnPoint_P1_Blue", -110f), ("SpawnPoint_P2_Red", 110f) })
            {
                var t = new GameObject(name).transform;
                t.SetParent(sp, false);
                t.position = new Vector3(x, CurbH + 1f, 0f);
                t.rotation = Quaternion.LookRotation(new Vector3(-x, 0f, 0f));
            }
        }

        static void SetupCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;
            cam.transform.position = new Vector3(0f, 34f, -900f);   // above the boulevard median, looking at the plaza and the hero towers
            cam.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 95f, 0f) - cam.transform.position);
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 8000f;
            var data = cam.GetComponent<UniversalAdditionalCameraData>();
            if (data == null) data = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        }

        // ================= materials / objects =================

        static Shader lit, toon;
        static readonly bool UseToon = true;

        static int GroundLayer()
        {
            int l = LayerMask.NameToLayer("Ground");
            return l >= 0 ? l : 0;
        }

        static Material Lit(string name, string tex, string nrm, Color tint, float tileX, float tileY, float smooth, float metal = 0f)
        {
            string path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(lit);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = lit;
            m.SetTexture("_BaseMap", tex != null ? CityTextures.Load(tex) : null);
            m.SetColor("_BaseColor", tint);
            m.SetTextureScale("_BaseMap", new Vector2(1f / tileX, 1f / tileY));
            var n = nrm != null ? CityTextures.Load(nrm) : null;
            m.SetTexture("_BumpMap", n);
            if (n != null) m.EnableKeyword("_NORMALMAP"); else m.DisableKeyword("_NORMALMAP");
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            m.DisableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.black);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        // lit-window / glow materials stay on URP Lit (the toon shader has no emission)
        static Material Glow(string name, string tex, string emis, Color tint, float tileX, float tileY, float smooth, string nrm = null)
        {
            var m = Lit(name, tex, nrm, tint, tileX, tileY, smooth);
            m.SetTexture("_EmissionMap", emis != null ? CityTextures.Load(emis) : null);
            SetEmission(m, Color.white);
            return m;
        }

        static Material Toon(string name, string tex, string nrm, Color tint, float tile, float smooth, float metal = 0f, bool shiny = false)
        {
            var m = Lit(name, tex, nrm, tint, tile, tile, smooth, metal);
            if (!UseToon || toon == null) return m;
            m.shader = toon;
            m.SetTexture("_BaseMap", tex != null ? CityTextures.Load(tex) : null);
            m.SetTextureScale("_BaseMap", new Vector2(1f / tile, 1f / tile));
            m.SetTexture("_BumpMap", nrm != null ? CityTextures.Load(nrm) : null);
            m.SetFloat("_BumpScale", 0.6f);
            m.SetColor("_LightTint", new Color(1f, 0.98f, 0.95f));
            m.SetColor("_MiddleTint", new Color(0.7f, 0.7f, 0.74f));
            m.SetColor("_ShadowTint", Color.black);
            m.SetFloat("_UseSecondThreshold", 1f);
            m.EnableKeyword("_USE_SECOND_THRESHOLD");
            m.SetVector("_DiffuseThresholds", new Vector4(-0.02f, 0.02f, 0.42f, 0.46f));
            m.SetVector("_ShadowThresholds", new Vector4(0.35f, 0.4f, 0f, 0f));
            m.SetFloat("_ReceiveShadows", 1f);
            m.EnableKeyword("_RECEIVE_SHADOWS_ON");
            m.SetFloat("_GIStrength", 1f);
            m.SetColor("_RimColor", shiny ? new Color(0.35f, 0.32f, 0.3f) : Color.black);
            m.SetVector("_RimThresholds", new Vector4(0.75f, 0.77f, 0f, 0f));
            m.SetColor("_SpecularColor", shiny ? new Color(0.8f, 0.8f, 0.8f) : Color.black);
            m.SetVector("_SpecularThresholds", new Vector4(0.01f, 0.02f, 0f, 0f));
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void CreateMaterials()
        {
            lit = Shader.Find("Universal Render Pipeline/Lit");
            toon = Shader.Find("Toon Shaders Pro/URP/Toon");

            mAsphalt = Toon("M_Asphalt", "T_Asphalt", "T_Asphalt_N", Color.white, 8f, 0.15f);
            mPaint = Toon("M_PaintWhite", null, null, new Color(0.9f, 0.9f, 0.88f), 1f, 0.2f);
            mPaintYellow = Toon("M_PaintYellow", null, null, new Color(0.95f, 0.72f, 0.12f), 1f, 0.2f);
            mCurb = Toon("M_Curb", "T_Concrete", "T_Concrete_N", new Color(0.82f, 0.82f, 0.8f), 3f, 0.1f);
            mPave = Toon("M_Pavement", "T_CityPaving", "T_CityPaving_N", Color.white, 4f, 0.1f);
            mPlazaPave = Toon("M_PlazaPaving", "T_CityPaving", "T_CityPaving_N", new Color(0.92f, 0.9f, 0.86f), 6f, 0.15f);
            mRoof = Toon("M_Roof", "T_Roof", null, Color.white, 8f, 0f);
            mGrass = Toon("M_Grass", "T_Grass", null, Color.white, 6f, 0f);
            mConcrete = Toon("M_Concrete", "T_Concrete", "T_Concrete_N", Color.white, 6f, 0.1f);
            mMetal = Toon("M_Metal", null, null, new Color(0.26f, 0.28f, 0.31f), 1f, 0.55f, 0.6f, true);
            mTrim = Toon("M_Trim", "T_Concrete", null, new Color(0.88f, 0.87f, 0.84f), 4f, 0.1f);
            mBark = Toon("M_Bark", null, null, new Color(0.3f, 0.22f, 0.16f), 1f, 0f);
            mLeaves = Toon("M_Leaves", null, null, new Color(0.24f, 0.5f, 0.2f), 1f, 0f);
            mOutskirts = Toon("M_Outskirts", "T_Asphalt", null, new Color(0.6f, 0.58f, 0.56f), 40f, 0f);
            mWater = Lit("M_Water", null, null, new Color(0.08f, 0.16f, 0.2f), 1f, 1f, 0.95f);

            mFacade = new Material[CityTextures.FacadeStyles.Length];
            for (int i = 0; i < mFacade.Length; i++)
            {
                string s = CityTextures.FacadeStyles[i];
                float smooth = s == "Glass" || s == "Tower" ? 0.85f : 0.35f;
                mFacade[i] = Glow("M_Facade_" + s, "T_Facade_" + s, "T_Facade_" + s + "_E", Color.white,
                    CityTextures.FacadeTile, CityTextures.FacadeTile, smooth, "T_Facade_" + s + "_N");
                mFacade[i].SetTextureScale("_BaseMap", new Vector2(1f / CityTextures.FacadeTile, 1f / CityTextures.FacadeTile));
            }
            mStorefront = Glow("M_Storefront", "T_Storefront", "T_Storefront_E", Color.white, 16f, 8f, 0.6f, "T_Storefront_N");
            mBillboard = new Material[3];
            for (int i = 0; i < 3; i++)
            {
                mBillboard[i] = Glow("M_Billboard_" + i, "T_Billboard_" + i, "T_Billboard_" + i, Color.white, 1f, 1f, 0.3f);
            }
            mLampHead = Glow("M_LampHead", null, null, new Color(1f, 0.95f, 0.85f), 1f, 1f, 0.5f);
            mAviation = Glow("M_AviationLight", null, null, new Color(0.6f, 0.05f, 0.03f), 1f, 1f, 0.5f);
            mGlowCyan = Glow("M_GlowCyan", null, null, new Color(0.05f, 0.35f, 0.5f), 1f, 1f, 0.5f);
            mGlowWarm = Glow("M_GlowWarm", null, null, new Color(0.5f, 0.3f, 0.12f), 1f, 1f, 0.5f);
            mSignalRed = Glow("M_SignalRed", null, null, new Color(0.6f, 0.05f, 0.03f), 1f, 1f, 0.5f);
            mSignalGreen = Glow("M_SignalGreen", null, null, new Color(0.05f, 0.6f, 0.2f), 1f, 1f, 0.5f);
            mSignalAmber = Glow("M_SignalAmber", null, null, new Color(0.6f, 0.4f, 0.05f), 1f, 1f, 0.5f);
            SetEmission(mSignalRed, new Color(3f, 0.2f, 0.1f));
            SetEmission(mSignalGreen, new Color(0.1f, 2.6f, 0.8f));
            SetEmission(mSignalAmber, new Color(2.6f, 1.4f, 0.1f));
        }

        // re-read the emissive materials from disk (the lighting menu runs without a rebuild)
        static void LoadMaterials()
        {
            Material L(string n) => AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/" + n + ".mat");
            mFacade = new Material[CityTextures.FacadeStyles.Length];
            for (int i = 0; i < mFacade.Length; i++) mFacade[i] = L("M_Facade_" + CityTextures.FacadeStyles[i]);
            mBillboard = new[] { L("M_Billboard_0"), L("M_Billboard_1"), L("M_Billboard_2") };
            mStorefront = L("M_Storefront");
            mLampHead = L("M_LampHead");
            mAviation = L("M_AviationLight");
            mGlowCyan = L("M_GlowCyan");
            mGlowWarm = L("M_GlowWarm");
        }

        static GameObject MakeObj(string name, Transform parent, MeshB mb, bool collider, int layer = 0, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.layer = layer;
            var mesh = mb.ToMesh(name);
            AssetDatabase.CreateAsset(mesh, MeshDir + "/" + parent.name + "_" + name + ".asset");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mb.Mats;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic |
                                                       StaticEditorFlags.OccluderStatic | StaticEditorFlags.ReflectionProbeStatic);
            return go;
        }
    }
}
