using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Nsc.Arena.EditorTools
{
    // Builds the giant-robot colosseum in the open scene (RobotArena).
    // Tools ▸ NSC ▸ Arena ▸ Build Colosseum replaces the old map under "RobotArena" and keeps SpawnPoints.
    //
    // Layout: every ring piece is an offset d (metres) outward from the sand ellipse (A x B).
    //   d = 0..8      podium wall (24 m) with parapet, cornices and marble cladding
    //   d = 8..84     three seating bands (ima / media / summa) split by balteus walls
    //   d = 84..92    top gallery with colonnade
    //   d = 92..104   ambulatory + 3 arcaded storeys and an attic (100 m tall)
    // The ring is cut into 80 bays (equal arc length on the facade) and 16 sectors of 5 bays.
    // Sectors 7 and 15 hold the two team gates (blue west, red east).
    public static class ColosseumBuilder
    {
        const string Dir = "Assets/nok/scene/RobotArena/Colosseum/Generated";
        const string MeshDir = Dir + "/Meshes";
        const string MatDir = Dir + "/Materials";
        const string VolumePath = "Assets/nok/scene/RobotArena/RobotArena_Volume.asset";
        const string LightingPath = "Assets/nok/scene/RobotArena/RobotArena_Lighting.lighting";
        const string SkyPath = "Assets/map/ligth/AllSkyFree/Cold Sunset/Cold Sunset.mat";

        public const float A = 170f, B = 125f;
        const float FacadeOut = 104f;
        const int Bays = 80, Sub = 4, Steps = Bays * Sub, SectorSteps = 20, Sectors = Steps / SectorSteps;
        const int GateBlueSector = 7, GateRedSector = 15;
        const int PulvinarStep = 70, JudgesStep = 230;
        static readonly Vector2 SpawnBlue = new Vector2(-122f, 0f), SpawnRed = new Vector2(122f, 0f);
        static readonly Color TeamBlue = new Color(0.25f, 0.55f, 1f), TeamRed = new Color(1f, 0.22f, 0.12f);

        static float[] tS;
        static readonly Dictionary<int, float[]> arcCache = new Dictionary<int, float[]>();

        static Material mSand, mSeats, mTrav,mTravDark, mMarble, mIron, mGold, mBronze, mVelarium, mBanRed, mBanBlue,
            mBanPurple, mPave, mEarth, mHills, mVoid, mEmber, mPit, mGlowBlue, mGlowRed, mWood, mRope, mFire, mDust;

        [MenuItem("Tools/NSC/Arena/Build Colosseum")]
        public static void BuildMenu() => Build(false);

        [MenuItem("Tools/NSC/Arena/Build Colosseum (regenerate textures)")]
        public static void BuildWithTextures() => Build(true);

        public static void Build(bool regenerateTextures)
        {
            if (regenerateTextures || !ColosseumTextures.Exists())
                ColosseumTextures.GenerateAll(A, B, new[] { SpawnBlue, SpawnRed });

            EnsureFolder(Dir); EnsureFolder(MatDir);
            AssetDatabase.DeleteAsset(MeshDir);
            EnsureFolder(MeshDir);

            CreateMaterials();
            InitRing();

            var root = GameObject.Find("RobotArena") ?? new GameObject("RobotArena");
            string[] old =
            {
                "Environment", "BattleFloor", "CoverBlocks", "Barrier", "SeatingBowl", "Facade", "CrescentRoofs",
                "GiantScreens", "LightTowers", "ArenaLights", "CityBackdrop", "Colosseum"
            };
            foreach (var n in old)
            {
                var t = root.transform.Find(n);
                if (t != null) Object.DestroyImmediate(t.gameObject);
            }

            var col = new GameObject("Colosseum").transform;
            col.SetParent(root.transform, false);

            try
            {
                Progress("Sand", 0.05f); BuildSand(Group(col, "Arena"));
                Progress("Bowl", 0.20f); BuildSectors(Group(col, "Bowl"));
                Progress("Gates", 0.50f);
                var gates = Group(col, "Gates");
                BuildGate(gates, GateRedSector * SectorSteps, true);
                BuildGate(gates, GateBlueSector * SectorSteps, false);
                Progress("Boxes", 0.60f);
                var boxes = Group(col, "Boxes");
                BuildBox(boxes, PulvinarStep, 1f, "Pulvinar");
                BuildBox(boxes, JudgesStep, 0.8f, "JudgesBox");
                Progress("Velarium", 0.65f); BuildVelarium(Group(col, "Velarium"));
                Progress("Decor", 0.72f); BuildDecor(Group(col, "Decor"));
                Progress("Props", 0.80f); BuildProps(Group(col, "Props"));
                Progress("Surroundings", 0.86f); BuildSurroundings(Group(col, "Surroundings"));
                Progress("Lighting", 0.92f); BuildLighting(col, root);
                RestyleSpawns(root.transform);
                SetupCamera();
            }
            finally { EditorUtility.ClearProgressBar(); }

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log("[Colosseum] Built. Sand " + (2 * A) + " x " + (2 * B) + " m, outer " + (2 * (A + FacadeOut)) + " x " + (2 * (B + FacadeOut)) + " m.");
        }

        static void Progress(string what, float p) => EditorUtility.DisplayProgressBar("Build Colosseum", what, p);

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

        // ================= ring maths =================

        static void InitRing()
        {
            float a = A + FacadeOut, b = B + FacadeOut;
            const int K = 8192;
            var cum = new float[K + 1];
            Vector2 prev = new Vector2(a, 0f);
            for (int k = 1; k <= K; k++)
            {
                float t = 2f * Mathf.PI * k / K;
                var p = new Vector2(a * Mathf.Cos(t), b * Mathf.Sin(t));
                cum[k] = cum[k - 1] + Vector2.Distance(prev, p);
                prev = p;
            }
            float L = cum[K];
            float sStart = 2.5f * L / Bays; // step 0 starts at the edge of the red gate
            tS = new float[Steps + 1];
            for (int j = 0; j <= Steps; j++)
            {
                float s = sStart + j * L / Steps;
                bool wrapped = s >= L;
                float sw = wrapped ? s - L : s;
                int lo = 0, hi = K;
                while (hi - lo > 1) { int mid = (lo + hi) / 2; if (cum[mid] <= sw) lo = mid; else hi = mid; }
                float f = (sw - cum[lo]) / Mathf.Max(1e-6f, cum[hi] - cum[lo]);
                tS[j] = 2f * Mathf.PI * (lo + f) / K + (wrapped ? 2f * Mathf.PI : 0f);
            }
            arcCache.Clear();
        }

        static Vector3 Pt(float t, float d, float y) => new Vector3((A + d) * Mathf.Cos(t), y, (B + d) * Mathf.Sin(t));

        static Vector3 RadialN(float t, float d) =>
            new Vector3(Mathf.Cos(t) / (A + d), 0f, Mathf.Sin(t) / (B + d)).normalized;

        static Vector3 PN(float t, float d, Vector2 n2) => (RadialN(t, d) * n2.x + Vector3.up * n2.y).normalized;

        // Arc length along the ring at offset d from step 0 (UV u in metres, continuous across sectors).
        static float U(int j, float d)
        {
            int key = Mathf.RoundToInt(d * 100f);
            if (!arcCache.TryGetValue(key, out var arr))
            {
                arr = new float[Steps + 1];
                for (int i = 1; i <= Steps; i++)
                    arr[i] = arr[i - 1] + Vector3.Distance(Pt(tS[i - 1], d, 0f), Pt(tS[i], d, 0f));
                arcCache[key] = arr;
            }
            return arr[j];
        }

        // Local frames on the ring. FrameOut: local -z faces away from the arena. FrameIn: local -z faces the arena.
        static Matrix4x4 FrameOut(float t, float d, float y) =>
            Matrix4x4.TRS(Pt(t, d, y), Quaternion.LookRotation(-RadialN(t, d), Vector3.up), Vector3.one);

        static Matrix4x4 FrameIn(float t, float d, float y) =>
            Matrix4x4.TRS(Pt(t, d, y), Quaternion.LookRotation(RadialN(t, d), Vector3.up), Vector3.one);

        static int BayOf(int jc) => (((jc - 2) / Sub + 3) % Bays + Bays) % Bays;
        static int SectorOf(int j) => (j / SectorSteps) % Sectors;
        static bool IsGateSector(int sec) => sec == GateBlueSector || sec == GateRedSector;
        static bool NearBox(int bay) => Mathf.Abs(bay - 20) <= 2 || Mathf.Abs(bay - 60) <= 2;

        static float Hash01(int a, int b)
        {
            unchecked
            {
                uint h = (uint)(a * 73856093 ^ b * 19349663);
                h = (h ^ (h >> 13)) * 1274126177u;
                return (h & 0xFFFF) / 65535f;
            }
        }

        // ================= profile extrusion =================

        struct PP { public float d, y; public Material m; }

        // m is the material of the segment that STARTS at this point (null = no face).
        static PP P(float d, float y, Material m) => new PP { d = d, y = y, m = m };

        // Faces point to the right of the walking direction in (d = outward, y = up):
        // walking up faces outward, walking inward faces up, walking down faces the arena.
        static void Extrude(MeshB mb, List<PP> prof, int j0, int j1, float vScale = 1f)
        {
            float cum = 0f;
            for (int k = 0; k < prof.Count - 1; k++)
            {
                PP a = prof[k], b = prof[k + 1];
                Vector2 dir = new Vector2(b.d - a.d, b.y - a.y);
                float len = dir.magnitude;
                if (len < 1e-4f) continue;
                if (a.m != null)
                {
                    dir /= len;
                    Vector2 n2 = new Vector2(dir.y, -dir.x);
                    float va = -cum / vScale, vb = -(cum + len) / vScale;
                    for (int j = j0; j < j1; j++)
                    {
                        float t0 = tS[j], t1 = tS[j + 1];
                        mb.Quad(a.m,
                            Pt(t0, a.d, a.y), Pt(t1, a.d, a.y), Pt(t1, b.d, b.y), Pt(t0, b.d, b.y),
                            PN(t0, a.d, n2), PN(t1, a.d, n2), PN(t1, b.d, n2), PN(t0, b.d, n2),
                            new Vector2(U(j, a.d), va), new Vector2(U(j + 1, a.d), va),
                            new Vector2(U(j + 1, b.d), vb), new Vector2(U(j, b.d), vb));
                    }
                }
                cum += len;
            }
        }

        // Stepped seating, walked from the top-outer row down to (dStart, yStart).
        static List<PP> Rows(float dStart, int rows, float depth, float rise, float yStart, Material m)
        {
            var l = new List<PP>();
            for (int i = rows - 1; i >= 0; i--)
            {
                float d = dStart + i * depth, yt = yStart + (i + 1) * rise;
                l.Add(P(d + depth, yt, m));
                l.Add(P(d, yt, m));
            }
            l.Add(P(dStart, yStart, null));
            return l;
        }

        struct Band { public float dStart, depth, rise, yStart; public int rows; }
        static readonly Band Ima = new Band { dStart = 12f, rows = 13, depth = 2f, rise = 1.1f, yStart = 24f };
        static readonly Band Media = new Band { dStart = 42f, rows = 12, depth = 2f, rise = 1.25f, yStart = 42.5f };
        static readonly Band Summa = new Band { dStart = 70f, rows = 7, depth = 2f, rise = 1.4f, yStart = 62f };

        static void SeatBand(MeshB mb, Band b, int j0, int j1)
        {
            Extrude(mb, Rows(b.dStart, b.rows, b.depth, b.rise, b.yStart, mSeats), j0, j1);
            // radial stairs every 2 bays
            for (int j = j0; j < j1; j++)
            {
                if (j % 8 != 0) continue;
                for (int i = 0; i < b.rows; i++)
                {
                    float d = b.dStart + i * b.depth, yt = b.yStart + (i + 1) * b.rise;
                    mb.M = FrameIn(tS[j], d + b.depth * 0.25f, yt);
                    mb.Box(mMarble, new Vector3(0f, b.rise * 0.25f, 0f), new Vector3(2.6f, b.rise * 0.5f, b.depth * 0.5f), false);
                }
                mb.M = Matrix4x4.identity;
            }
        }

        // ================= sand =================

        static float SandRho(float x, float z) => Mathf.Sqrt((x / A) * (x / A) + (z / B) * (z / B));

        static float SandH(float x, float z)
        {
            float rho = SandRho(x, z);
            float n = (Mathf.PerlinNoise(x * 0.035f + 11f, z * 0.035f + 7f) - 0.5f) * 0.22f;
            float sinT = Mathf.Abs(z / B) / Mathf.Max(1e-4f, rho);
            float gateFade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, 0.3f, sinT));
            float bank = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.94f, 1.0f, rho)) * 1.1f * gateFade;
            return n + bank;
        }

        static Vector3 SandPt(float t, float rho)
        {
            float x = A * rho * Mathf.Cos(t), z = B * rho * Mathf.Sin(t);
            return new Vector3(x, SandH(x, z), z);
        }

        static Vector3 SandN(Vector3 p)
        {
            const float e = 0.5f;
            float hx = SandH(p.x + e, p.z) - SandH(p.x - e, p.z);
            float hz = SandH(p.x, p.z + e) - SandH(p.x, p.z - e);
            return new Vector3(-hx, 2f * e, -hz).normalized;
        }

        static Vector2 SandUV(Vector3 p) => new Vector2((p.x / A + 1f) * 0.5f, (p.z / B + 1f) * 0.5f);

        static void BuildSand(Transform parent)
        {
            const int rings = 42;
            int ground = LayerMask.NameToLayer("Ground");
            for (int sec = 0; sec < Sectors; sec++)
            {
                var mb = new MeshB();
                for (int j = sec * SectorSteps; j < (sec + 1) * SectorSteps; j++)
                for (int k = 0; k < rings; k++)
                {
                    float r0 = 1.03f * k / rings, r1 = 1.03f * (k + 1) / rings;
                    Vector3 p0 = SandPt(tS[j], r0), p1 = SandPt(tS[j + 1], r0), p2 = SandPt(tS[j + 1], r1), p3 = SandPt(tS[j], r1);
                    mb.Quad(mSand, p0, p1, p2, p3, SandN(p0), SandN(p1), SandN(p2), SandN(p3),
                        SandUV(p0), SandUV(p1), SandUV(p2), SandUV(p3));
                }
                var go = MakeObj("Sand_S" + sec.ToString("00"), parent, mb, true, ground >= 0 ? ground : 0, true, Gi.CopyUv0);
                go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        // ================= bowl =================

        static void BuildSectors(Transform parent)
        {
            for (int sec = 0; sec < Sectors; sec++)
            {
                int j0 = sec * SectorSteps, j1 = j0 + SectorSteps;
                var mb = new MeshB();
                bool gate = IsGateSector(sec);

                if (!gate)
                {
                    // podium: parapet, cornice, string course, plinth (faces the sand)
                    Extrude(mb, new List<PP>
                    {
                        P(12f, 24f, mTrav), P(1.5f, 24f, mMarble), P(1.5f, 27.5f, mMarble), P(-0.3f, 27.5f, mMarble),
                        P(-0.3f, 26.8f, mMarble), P(0f, 26.8f, mMarble), P(0f, 25.2f, mTrav), P(-1.6f, 25.2f, mTrav),
                        P(-1.6f, 24f, mTrav), P(0f, 23f, mMarble), P(0f, 12.6f, mTrav), P(-0.6f, 12.6f, mTrav),
                        P(-0.6f, 11.8f, mTrav), P(0f, 11.8f, mMarble), P(0f, 2.2f, mTrav), P(-1f, 2.2f, mTrav),
                        P(-1f, -0.6f, null)
                    }, j0, j1);
                    SeatBand(mb, Ima, j0, j1);
                    Extrude(mb, new List<PP> { P(42f, 42.5f, mTrav), P(38f, 42.5f, mTrav), P(38f, 38.3f, null) }, j0, j1);
                    SeatBand(mb, Media, j0, j1);
                    Extrude(mb, new List<PP> { P(70f, 62f, mTrav), P(66f, 62f, mTrav), P(66f, 57.5f, null) }, j0, j1);

                    // vomitoria in the balteus walls
                    for (int jc = j0 + 2; jc < j1; jc += Sub)
                    {
                        if (BayOf(jc) % 2 != 0) continue;
                        Doorway(mb, tS[jc], 38f, 38.3f, 3.2f, 3.6f);
                        Doorway(mb, tS[jc], 66f, 57.5f, 3.6f, 4.2f);
                    }
                }
                else
                {
                    // gatehouse: terrace over the tunnel, front wall above the gate arch, side walls
                    Extrude(mb, new List<PP>
                    {
                        P(70f, 62f, mTrav), P(1f, 62f, mTrav), P(1f, 64.5f, mTrav), P(-1.5f, 64.5f, mTrav),
                        P(-1.5f, 60.4f, mTrav), P(0f, 59.6f, mTrav), P(0f, 50f, null)
                    }, j0, j1);
                    foreach (int j in new[] { j0, j1 })
                    {
                        float t = tS[j];
                        Vector3 p0 = Pt(t, -1.5f, 0f), p1 = Pt(t, 70f, 0f), p2 = Pt(t, 70f, 62f), p3 = Pt(t, -1.5f, 62f);
                        Vector3 tan = (Pt(t + 0.001f, 0f, 0f) - Pt(t - 0.001f, 0f, 0f)).normalized;
                        Vector2 u0 = new Vector2(-1.5f, 0f), u1 = new Vector2(70f, 0f), u2 = new Vector2(70f, 62f), u3 = new Vector2(-1.5f, 62f);
                        mb.Quad(mTrav, p0, p1, p2, p3, tan, u0, u1, u2, u3);
                        mb.Quad(mTrav, p0, p1, p2, p3, -tan, u0, u1, u2, u3);
                    }
                }

                // upper seating + gallery (continuous over the gates)
                SeatBand(mb, Summa, j0, j1);
                Extrude(mb, new List<PP> { P(92f, 71.8f, mTrav), P(84f, 71.8f, null) }, j0, j1);
                Extrude(mb, new List<PP>
                {
                    P(104f, 72f, mTrav), P(104f, 96f, mTrav), P(106f, 96.6f, mTrav), P(106f, 100f, mTrav),
                    P(92f, 100f, mTrav), P(92f, 89.6f, mTrav), P(85f, 89.6f, mTrav), P(85f, 88.2f, mTrav),
                    P(92f, 88.2f, mMarble), P(92f, 71.8f, null)
                }, j0, j1);
                for (int j = j0; j < j1; j += 2)
                {
                    mb.M = FrameIn(tS[j], 86f, 71.8f);
                    mb.Box(mMarble, new Vector3(0f, 0.4f, 0f), new Vector3(2.2f, 0.8f, 2.2f));
                    mb.Cylinder(mMarble, new Vector3(0f, 0.8f, 0f), 0.75f, 0.65f, 14.8f, 10, false);
                    mb.Box(mMarble, new Vector3(0f, 16f, 0f), new Vector3(2.4f, 0.8f, 2.4f));
                }
                mb.M = Matrix4x4.identity;
                for (int jc = j0 + 2; jc < j1; jc += Sub)
                    Doorway(mb, tS[jc], 92f, 71.8f, 4f, 9f);

                // ambulatory behind the arcades
                Extrude(mb, new List<PP> { P(92f, 0f, mTravDark), P(92f, 71.8f, null) }, j0, j1);
                Extrude(mb, new List<PP> { P(105.5f, 0.02f, mTravDark), P(92f, 0.02f, null) }, j0, j1);
                foreach (float y in new[] { 24f, 48f, 72f })
                {
                    Extrude(mb, new List<PP> { P(92f, y - 1.2f, mTravDark), P(98f, y - 1.2f, null) }, j0, j1);
                    Extrude(mb, new List<PP> { P(98f, y, mTravDark), P(92f, y, null) }, j0, j1);
                }

                Facade(mb, j0, j1);
                MakeObj("Bowl_S" + sec.ToString("00"), parent, mb, true, 0, true, Gi.Unwrap, 0.6f);
            }
        }

        // Dark doorway with a stone lintel on a wall at offset d (facing the arena).
        static void Doorway(MeshB mb, float t, float d, float yBase, float w, float h)
        {
            mb.M = FrameIn(t, d, yBase);
            mb.Box(mVoid, new Vector3(0f, h * 0.5f, -0.06f), new Vector3(w, h, 0.12f), false);
            mb.Box(mTrav, new Vector3(0f, h + 0.35f, -0.25f), new Vector3(w + 1.2f, 0.7f, 0.5f));
            mb.M = Matrix4x4.identity;
        }

        static void Facade(MeshB mb, int j0, int j1)
        {
            // stylobate steps + entablatures between storeys
            Extrude(mb, new List<PP>
            {
                P(107f, -0.5f, mTrav), P(107f, 0.6f, mTrav), P(105.5f, 0.6f, mTrav), P(105.5f, 1.2f, mTrav), P(104f, 1.2f, null)
            }, j0, j1);
            for (int s = 0; s < 3; s++)
            {
                float y0 = s * 24f;
                Extrude(mb, new List<PP>
                {
                    P(104f, y0 + 20.6f, mTrav), P(105.3f, y0 + 21f, mTrav), P(105.3f, y0 + 23f, mTrav), P(104.6f, y0 + 23.6f, mTrav),
                    P(104f, y0 + 24f, null)
                }, j0, j1);
            }

            // arch panels, one per bay per storey
            for (int jc = j0 + 2; jc < j1; jc += Sub)
            {
                Vector3 pL = Pt(tS[jc - 2], FacadeOut, 0f), pR = Pt(tS[jc + 2], FacadeOut, 0f);
                Vector3 mid = (pL + pR) * 0.5f, xa = (pR - pL).normalized;
                Vector3 outward = Vector3.Cross(xa, Vector3.up);
                if (Vector3.Dot(outward, RadialN(tS[jc], FacadeOut)) < 0f) outward = -outward;
                float w = Vector3.Distance(pL, pR);
                for (int s = 0; s < 3; s++)
                {
                    float y0 = s == 0 ? 1.2f : s * 24f;
                    float h = s == 0 ? 22.8f : 24f;
                    mb.M = Matrix4x4.TRS(mid + Vector3.up * y0, Quaternion.LookRotation(-outward, Vector3.up), Vector3.one);
                    mb.ArchPanel(mTrav, mTrav, w, h, w * 0.58f, s == 0 ? 12.6f : 13.2f, 6f, 24, false);
                }

                int bay = BayOf(jc);
                mb.M = FrameOut(tS[jc], FacadeOut, 0f);
                if (bay % 2 == 0)
                {
                    mb.Box(mVoid, new Vector3(0f, 84f, -0.05f), new Vector3(3.4f, 5.6f, 0.1f), false);
                    mb.Box(mTrav, new Vector3(0f, 87.2f, -0.3f), new Vector3(5f, 0.7f, 0.6f));
                    mb.Box(mTrav, new Vector3(0f, 80.9f, -0.3f), new Vector3(5f, 0.5f, 0.6f));
                }
                foreach (float x in new[] { -5f, 0f, 5f })
                    mb.Box(mTrav, new Vector3(x, 74.6f, -0.7f), new Vector3(1.2f, 1.4f, 1.4f));
                mb.M = Matrix4x4.identity;
            }

            // engaged columns (Tuscan / Ionic / Corinthian) and attic pilasters at bay boundaries
            for (int jb = j0; jb < j1; jb += Sub)
            {
                float t = tS[jb];
                for (int s = 0; s < 3; s++)
                {
                    float y0 = s == 0 ? 1.2f : s * 24f, top = s * 24f + 20.6f;
                    mb.M = FrameOut(t, FacadeOut + 0.5f, 0f);
                    mb.Box(mTrav, new Vector3(0f, y0 + 0.7f, 0f), new Vector3(3f, 1.4f, 3f));
                    mb.Cylinder(mTrav, new Vector3(0f, y0 + 1.4f, 0f), 1.2f, 1.05f, top - 1.2f - (y0 + 1.4f), 12, false);
                    float capH = s == 0 ? 0.8f : s == 1 ? 1.0f : 1.6f, capW = s == 0 ? 3f : s == 1 ? 3.6f : 3.3f;
                    mb.Box(mTrav, new Vector3(0f, top - capH * 0.5f, 0f), new Vector3(capW, capH, capW));
                }
                mb.M = FrameOut(t, FacadeOut, 0f);
                mb.Box(mTrav, new Vector3(0f, 84f, -0.3f), new Vector3(2.4f, 23.5f, 0.6f));
                if (jb % 8 == 0) // velarium mast
                {
                    mb.M = FrameOut(t, FacadeOut + 1.1f, 0f);
                    mb.Cylinder(mWood, new Vector3(0f, 73f, 0f), 0.55f, 0.45f, 50f, 8, true);
                    mb.Box(mIron, new Vector3(0f, 123.4f, 0f), new Vector3(1.2f, 0.8f, 1.2f));
                }
                mb.M = Matrix4x4.identity;
            }
        }

        // ================= gates =================

        static void BuildGate(Transform parent, int jA, bool red)
        {
            int jB = jA + SectorSteps;
            Vector3 pL = Pt(tS[jA], 0f, 0f), pR = Pt(tS[jB], 0f, 0f);
            Vector3 mid = (pL + pR) * 0.5f, xa = (pR - pL).normalized;
            Vector3 outward = Vector3.Cross(xa, Vector3.up);
            if (Vector3.Dot(outward, mid) < 0f) outward = -outward;
            float w = Vector3.Distance(pL, pR);
            const float ow = 38f, sh = 28f, depth = 96f;
            Color team = red ? TeamRed : TeamBlue;
            var glow = red ? mGlowRed : mGlowBlue;
            string side = red ? "Red" : "Blue";

            var root = new GameObject("Gate_" + side).transform;
            root.SetParent(parent, false);
            root.position = mid;
            root.rotation = Quaternion.LookRotation(outward, Vector3.up); // local -z faces the arena

            var mb = new MeshB();
            mb.ArchPanel(mTrav, mMarble, w, 50f, ow, sh, 10f, 32, true);
            var outline = MeshB.ArchOutline(ow, sh, 32);
            mb.Soffit(mTravDark, outline, sh, 10f, depth);
            mb.FanFill(mWood, outline, depth, -1f);
            foreach (float y in new[] { 6f, 16f, 26f, 36f })
                mb.Box(mIron, new Vector3(0f, y, depth - 0.3f), new Vector3(ow, 1.2f, 0.5f));

            // raised portcullis
            for (float x = -18f; x <= 18.01f; x += 3f)
            {
                mb.Box(mIron, new Vector3(x, 39f, 3f), new Vector3(0.6f, 18f, 0.6f));
                mb.Hex(mIron, new[]
                {
                    new Vector3(x - 0.3f, 30f, 2.7f), new Vector3(x + 0.3f, 30f, 2.7f), new Vector3(x + 0.3f, 30f, 3.3f), new Vector3(x - 0.3f, 30f, 3.3f),
                    new Vector3(x, 28.4f, 3f), new Vector3(x, 28.4f, 3f), new Vector3(x, 28.4f, 3f), new Vector3(x, 28.4f, 3f)
                });
            }
            foreach (float y in new[] { 32f, 38f, 44f })
                mb.Box(mIron, new Vector3(0f, y, 3f), new Vector3(ow, 0.8f, 0.8f));

            // pylons with glowing team crests
            foreach (float sx in new[] { -1f, 1f })
            {
                float x = sx * (w * 0.5f + 5f);
                mb.Box(mTrav, new Vector3(x, 1.5f, 2f), new Vector3(12f, 3f, 18f));
                mb.Box(mMarble, new Vector3(x, 34.5f, 2f), new Vector3(10f, 63f, 16f));
                mb.Box(mTrav, new Vector3(x, 67.25f, 2f), new Vector3(12f, 2.5f, 18f));
                mb.Box(mTrav, new Vector3(x, 24f, 2f), new Vector3(10.8f, 1.2f, 16.8f));
                var m0 = mb.M;
                mb.M = m0 * Matrix4x4.TRS(new Vector3(x, 42f, -6f), Quaternion.Euler(-90f, 0f, 0f), Vector3.one);
                mb.Cylinder(mGold, Vector3.zero, 4.2f, 4.2f, 0.5f, 32, true);
                mb.Cylinder(glow, Vector3.zero, 3.2f, 3.2f, 0.8f, 32, true);
                mb.Cylinder(mIron, Vector3.zero, 1.4f, 1.4f, 1.2f, 16, true);
                mb.M = m0;
            }
            MakeObj("GateStructure", root, mb, true);

            // tunnel floor (walkable)
            var floor = new MeshB();
            floor.Quad(mPave, new Vector3(-ow * 0.5f, 0.02f, 0f), new Vector3(ow * 0.5f, 0.02f, 0f),
                new Vector3(ow * 0.5f, 0.02f, depth), new Vector3(-ow * 0.5f, 0.02f, depth), Vector3.up,
                new Vector2(-ow * 0.5f, 0f), new Vector2(ow * 0.5f, 0f), new Vector2(ow * 0.5f, depth), new Vector2(-ow * 0.5f, depth));
            int ground = LayerMask.NameToLayer("Ground");
            MakeObj("TunnelFloor", root, floor, true, ground >= 0 ? ground : 0);

            // lights: team glow deep in the tunnel + team-coloured braziers on the pylons
            AddLight(root, "TunnelLight", new Vector3(0f, 14f, 45f), team, 500f, 110f);
            AddLight(root, "TunnelDoorLight", new Vector3(0f, 12f, 85f), team, 200f, 60f);
            AddLight(root, "TunnelMouthLight", new Vector3(0f, 22f, 18f), new Color(1f, 0.62f, 0.32f), 150f, 60f);
            foreach (float sx in new[] { -1f, 1f })
            {
                var br = Brazier(root, "PylonBrazier", Vector3.zero, 2f, team);
                br.transform.localPosition = new Vector3(sx * (w * 0.5f + 5f), 68.5f, 2f);
            }
        }

        // ================= imperial / judges box =================

        static void BuildBox(Transform parent, int jc, float s, string name)
        {
            float t = tS[jc];
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.position = Pt(t, 0f, 0f);
            root.rotation = Quaternion.LookRotation(RadialN(t, 0f), Vector3.up);
            root.localScale = Vector3.one * s;

            var mb = new MeshB();
            mb.Box(mMarble, new Vector3(0f, 15f, 2f), new Vector3(44f, 30f, 16f));
            mb.Box(mTrav, new Vector3(0f, 2f, 1.8f), new Vector3(45.5f, 4f, 16.6f));
            mb.Box(mTrav, new Vector3(0f, 29.2f, 1.6f), new Vector3(46f, 1.6f, 17.2f));
            mb.Box(mTrav, new Vector3(0f, 9.5f, -6.3f), new Vector3(44f, 0.8f, 0.6f));
            var m0 = mb.M;
            mb.M = Matrix4x4.TRS(new Vector3(0f, 17f, -6f), Quaternion.Euler(-90f, 0f, 0f), Vector3.one);
            mb.Cylinder(mGold, Vector3.zero, 6.5f, 6.5f, 0.6f, 40, true);
            mb.Cylinder(mIron, Vector3.zero, 5f, 5f, 1.0f, 40, true);
            mb.Cylinder(mGold, Vector3.zero, 1.6f, 1.2f, 1.8f, 20, true);
            mb.M = m0;
            for (int i = 0; i < 6; i++)
            {
                float x = -20f + 8f * i;
                mb.Box(mMarble, new Vector3(x, 30.6f, -3f), new Vector3(3f, 1.2f, 3f));
                mb.Cylinder(mMarble, new Vector3(x, 31.2f, -3f), 1.25f, 1.1f, 13.6f, 14, false);
                mb.Box(mGold, new Vector3(x, 45.5f, -3f), new Vector3(3.4f, 1.0f, 3.4f));
            }
            mb.Box(mMarble, new Vector3(0f, 38f, 9f), new Vector3(44f, 16f, 2f));
            mb.Box(mMarble, new Vector3(-21.5f, 32f, 2f), new Vector3(1f, 3f, 14f));
            mb.Box(mMarble, new Vector3(21.5f, 32f, 2f), new Vector3(1f, 3f, 14f));
            mb.Box(mTrav, new Vector3(0f, 47.5f, 2f), new Vector3(46f, 3f, 17f));
            mb.Hex(mGold, new[]
            {
                new Vector3(-23f, 49f, -6.5f), new Vector3(23f, 49f, -6.5f), new Vector3(23f, 49f, 10.5f), new Vector3(-23f, 49f, 10.5f),
                new Vector3(0f, 57f, -6.5f), new Vector3(0f, 57f, -6.5f), new Vector3(0f, 57f, 10.5f), new Vector3(0f, 57f, 10.5f)
            });
            foreach (float x in new[] { -14f, 14f })
                mb.Cloth(mBanPurple, new Vector3(x, 28.4f, -6.4f), 8f, 22f, 0.5f);
            mb.Cloth(mBanPurple, new Vector3(0f, 45f, 7.9f), 40f, 14f, 0.3f);
            MakeObj(name + "Mesh", root, mb, true);

            foreach (float x in new[] { -19f, 19f })
            {
                var br = Brazier(root, "Brazier", Vector3.zero, 0.8f, new Color(1f, 0.6f, 0.25f));
                br.transform.localPosition = new Vector3(x, 30f, -4.5f);
            }
        }

        // ================= velarium =================

        static void BuildVelarium(Transform parent)
        {
            var sails = new MeshB();
            var ropes = new MeshB();
            for (int m = 0; m < Steps / 8; m++)
            {
                int ja = m * 8, jb = ja + 8;
                float ta = tS[ja], tb = tS[jb];
                Vector3 oa = Pt(ta, FacadeOut + 1.1f, 121f), ob = Pt(tb, FacadeOut + 1.1f, 121f);
                Vector3 ia = Pt(ta, 30f, 95f), ib = Pt(tb, 30f, 95f);
                bool south = oa.z < -60f && ob.z < -60f;
                if (south && m % 9 != 4)
                {
                    const int nu = 6, nv = 10;
                    var grid = new Vector3[nu + 1, nv + 1];
                    for (int i = 0; i <= nu; i++)
                    for (int k = 0; k <= nv; k++)
                    {
                        float u = i / (float)nu, v = k / (float)nv;
                        Vector3 p = Vector3.Lerp(Vector3.Lerp(oa, ob, u), Vector3.Lerp(ia, ib, u), v);
                        p.y -= Mathf.Sin(u * Mathf.PI) * (1.2f + 2.5f * v) + Mathf.Sin(v * Mathf.PI) * 3.5f;
                        grid[i, k] = p;
                    }
                    for (int i = 0; i < nu; i++)
                    for (int k = 0; k < nv; k++)
                    {
                        Vector3 p0 = grid[i, k], p1 = grid[i + 1, k], p2 = grid[i + 1, k + 1], p3 = grid[i, k + 1];
                        Vector3 n = Vector3.Cross(p1 - p0, p3 - p0).normalized;
                        if (n.y < 0f) n = -n;
                        sails.Quad(mVelarium, p0, p1, p2, p3, n,
                            new Vector2(i / (float)nu, k / (float)nv), new Vector2((i + 1) / (float)nu, k / (float)nv),
                            new Vector2((i + 1) / (float)nu, (k + 1) / (float)nv), new Vector2(i / (float)nu, (k + 1) / (float)nv));
                    }
                    ropes.Beam(mRope, oa, ia - Vector3.up * 3.5f, 0.35f);
                    ropes.Beam(mRope, ia - Vector3.up * 3.5f, ib - Vector3.up * 3.5f, 0.35f);
                }
                // a flag on every other uncovered mast
                if (!south && m % 2 == 0)
                {
                    bool redSide = oa.x > 0f;
                    Vector3 top = Pt(ta, FacadeOut + 1.1f, 122.5f);
                    Vector3 along = (Pt(tb, FacadeOut + 1.1f, 0f) - Pt(ta, FacadeOut + 1.1f, 0f)).normalized;
                    sails.M = Matrix4x4.TRS(top, Quaternion.LookRotation(Vector3.Cross(along, Vector3.up), Vector3.up), Vector3.one);
                    sails.Cloth(redSide ? mBanRed : mBanBlue, new Vector3(3.6f, 0f, 0f), 7f, 4.2f, 0.6f, true);
                    sails.M = Matrix4x4.identity;
                }
            }
            MakeObj("Sails", parent, sails, false, 0, true, Gi.None);
            MakeObj("Ropes", parent, ropes, false, 0, true, Gi.None);
        }

        // ================= decor: banners, rings, braziers =================

        static void BuildDecor(Transform parent)
        {
            var mb = new MeshB();
            var braziers = Group(parent, "Braziers");
            for (int jc = 2; jc < Steps; jc += Sub)
            {
                int bay = BayOf(jc);
                if (IsGateSector(SectorOf(jc)) || NearBox(bay)) continue;
                float t = tS[jc];
                bool blue = Pt(t, 0f, 0f).x < 0f;
                if (bay % 4 == 2)
                {
                    mb.M = FrameIn(t, -0.9f, 0f);
                    mb.Cloth(blue ? mBanBlue : mBanRed, new Vector3(0f, 23.6f, 0f), 7f, 17.6f, 0.45f);
                    mb.Box(mIron, new Vector3(0f, 23.7f, 0.2f), new Vector3(8f, 0.4f, 0.4f));
                }
                if (bay % 4 == 0)
                {
                    mb.M = FrameIn(t, 0f, 0f);
                    mb.Box(mIron, new Vector3(0f, 9.6f, -0.15f), new Vector3(1.6f, 2.2f, 0.3f));
                    mb.M = FrameIn(t, 0f, 0f) * Matrix4x4.Translate(new Vector3(0f, 8f, -0.6f));
                    mb.Torus(mIron, 1.7f, 0.3f, 28, 8);
                    var br = Brazier(braziers, "Brazier", Vector3.zero, 1f, new Color(1f, 0.6f, 0.25f));
                    br.transform.position = Pt(t, 0.6f, 27.5f);
                }
            }
            // great team banners over the gates
            foreach (var (sec, red) in new[] { (GateRedSector, true), (GateBlueSector, false) })
            {
                float t = tS[sec * SectorSteps + SectorSteps / 2];
                mb.M = FrameIn(t, -5f, 0f);
                mb.Cloth(red ? mBanRed : mBanBlue, new Vector3(0f, 59.5f, 0f), 15f, 27f, 0.8f);
                mb.Box(mIron, new Vector3(0f, 59.7f, 2.5f), new Vector3(1f, 1f, 6f));
                mb.Box(mGold, new Vector3(0f, 59.8f, 0f), new Vector3(17f, 0.8f, 0.8f));
            }
            mb.M = Matrix4x4.identity;
            MakeObj("BannersAndRings", parent, mb, false, 0, true, Gi.None);
        }

        static Mesh brazierMesh;

        static GameObject Brazier(Transform parent, string name, Vector3 pos, float s, Color fire)
        {
            if (brazierMesh == null)
            {
                var b = new MeshB();
                b.Lathe(mIron, Vector3.zero, new List<Vector2>
                {
                    new Vector2(1.8f, 0f), new Vector2(1.8f, 0.5f), new Vector2(1.0f, 0.8f), new Vector2(0.7f, 1.2f),
                    new Vector2(0.6f, 3.0f), new Vector2(1.1f, 3.3f), new Vector2(2.8f, 4.4f), new Vector2(3.0f, 5.0f),
                    new Vector2(2.6f, 5.0f), new Vector2(1.6f, 4.3f)
                }, 20);
                b.Disc(mEmber, new Vector3(0f, 4.55f, 0f), 2.35f, 20, true);
                brazierMesh = b.ToMesh("Brazier");
                SaveMesh(brazierMesh, "Brazier", Gi.None);
            }
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = Vector3.one * s;
            go.AddComponent<MeshFilter>().sharedMesh = brazierMesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { mIron, mEmber };
            AddFire(go.transform, new Vector3(0f, 4.6f, 0f), 1f, fire);
            AddLight(go.transform, "FireLight", new Vector3(0f, 9f, 0f), Color.Lerp(fire, new Color(1f, 0.6f, 0.3f), 0.5f), 60f, 42f);
            return go;
        }

        // ================= props on the sand =================

        static void BuildProps(Transform parent)
        {
            // giant swords stuck in the sand
            var sword = new MeshB();
            sword.Hex(mIron, new[]
            {
                new Vector3(-0.5f, -4f, -0.12f), new Vector3(0.5f, -4f, -0.12f), new Vector3(0.5f, -4f, 0.12f), new Vector3(-0.5f, -4f, 0.12f),
                new Vector3(-1.5f, 21f, -0.3f), new Vector3(1.5f, 21f, -0.3f), new Vector3(1.5f, 21f, 0.3f), new Vector3(-1.5f, 21f, 0.3f)
            });
            sword.Box(mGold, new Vector3(0f, 21.8f, 0f), new Vector3(11f, 1.6f, 1.8f));
            sword.Box(mIron, new Vector3(0f, 21.8f, 0f), new Vector3(12.5f, 0.8f, 1.2f));
            sword.Cylinder(mWood, new Vector3(0f, 22.6f, 0f), 0.8f, 0.75f, 7f, 12, false);
            sword.Lathe(mGold, new Vector3(0f, 29.4f, 0f), new List<Vector2>
            {
                new Vector2(0.01f, 0f), new Vector2(0.9f, 0.2f), new Vector2(1.5f, 1.1f), new Vector2(1.2f, 2.1f), new Vector2(0.01f, 2.5f)
            }, 14);
            var swordMesh = sword.ToMesh("GiantSword");
            SaveMesh(swordMesh, "GiantSword");
            var swords = new[]
            {
                (new Vector3(-58f, 0f, 72f), new Vector3(14f, 30f, -9f)),
                (new Vector3(86f, 0f, -70f), new Vector3(-10f, 120f, 16f)),
                (new Vector3(-22f, 0f, -98f), new Vector3(18f, -60f, 6f))
            };
            for (int i = 0; i < swords.Length; i++)
            {
                var go = PropObj("GiantSword_" + i, parent, swordMesh, new[] { mIron, mGold, mWood });
                go.transform.position = swords[i].Item1;
                go.transform.rotation = Quaternion.Euler(swords[i].Item2);
            }

            // shields leaning on the podium
            var shield = new MeshB();
            shield.Lathe(mBronze, Vector3.zero, new List<Vector2>
            {
                new Vector2(7f, -0.3f), new Vector2(7f, 0.2f), new Vector2(5.5f, 0.75f), new Vector2(3f, 1.15f), new Vector2(0.01f, 1.3f)
            }, 36);
            shield.Disc(mBronze, new Vector3(0f, -0.3f, 0f), 7f, 36, false);
            shield.Lathe(mGold, new Vector3(0f, 1.1f, 0f), new List<Vector2>
            {
                new Vector2(1.8f, 0f), new Vector2(1.5f, 0.6f), new Vector2(0.8f, 1.0f), new Vector2(0.01f, 1.1f)
            }, 20);
            shield.M = Matrix4x4.Rotate(Quaternion.Euler(90f, 0f, 0f));
            shield.Torus(mIron, 7f, 0.45f, 40, 8);
            shield.M = Matrix4x4.identity;
            var shieldMesh = shield.ToMesh("GiantShield");
            SaveMesh(shieldMesh, "GiantShield");
            foreach (var (jc, i) in new[] { (110, 0), (262, 1) })
            {
                var go = PropObj("GiantShield_" + i, parent, shieldMesh, new[] { mBronze, mGold, mIron });
                var f = FrameIn(tS[jc], -2.2f, 6.8f);
                go.transform.position = f.GetColumn(3);
                go.transform.rotation = f.rotation * Quaternion.Euler(-75f, 0f, 0f);
            }

            // fallen column + rubble clusters
            Rubble(parent, "Rubble_West", new Vector3(-118f, 0f, -62f), 5);
            Rubble(parent, "Rubble_East", new Vector3(105f, 0f, 72f), 9);

            // hypogeum grates with forge glow underneath
            var grate = new MeshB();
            const float gw = 16f, gd = 10f;
            grate.Quad(mPit, new Vector3(-gw / 2, 0.16f, -gd / 2), new Vector3(gw / 2, 0.16f, -gd / 2), new Vector3(gw / 2, 0.16f, gd / 2),
                new Vector3(-gw / 2, 0.16f, gd / 2), Vector3.up, Vector2.zero, new Vector2(1, 0), Vector2.one, new Vector2(0, 1));
            grate.Box(mIron, new Vector3(0f, 0.25f, -gd / 2), new Vector3(gw + 1f, 0.5f, 1f));
            grate.Box(mIron, new Vector3(0f, 0.25f, gd / 2), new Vector3(gw + 1f, 0.5f, 1f));
            grate.Box(mIron, new Vector3(-gw / 2, 0.25f, 0f), new Vector3(1f, 0.5f, gd));
            grate.Box(mIron, new Vector3(gw / 2, 0.25f, 0f), new Vector3(1f, 0.5f, gd));
            for (float x = -gw / 2 + 1.2f; x < gw / 2 - 0.5f; x += 1.2f)
                grate.Box(mIron, new Vector3(x, 0.3f, 0f), new Vector3(0.35f, 0.3f, gd));
            grate.Box(mIron, new Vector3(0f, 0.32f, 0f), new Vector3(gw, 0.25f, 0.35f));
            var grateMesh = grate.ToMesh("HypogeumGrate");
            SaveMesh(grateMesh, "HypogeumGrate");
            var grates = new[] { (new Vector3(-72f, 0f, 42f), 25f), (new Vector3(72f, 0f, -42f), 25f), (new Vector3(-30f, 0f, -82f), -10f), (new Vector3(32f, 0f, 82f), -10f) };
            for (int i = 0; i < grates.Length; i++)
            {
                var go = PropObj("Grate_" + i, parent, grateMesh, new[] { mPit, mIron });
                go.transform.position = grates[i].Item1;
                go.transform.rotation = Quaternion.Euler(0f, grates[i].Item2, 0f);
                Object.DestroyImmediate(go.GetComponent<MeshCollider>());
                AddLight(go.transform, "ForgeGlow", new Vector3(0f, 1.5f, 0f), new Color(1f, 0.45f, 0.15f), 30f, 14f);
            }
        }

        static void Rubble(Transform parent, string name, Vector3 at, int seed)
        {
            var rnd = new System.Random(seed);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            var mb = new MeshB();
            float yaw = R(0f, 360f);
            for (int i = 0; i < 3; i++)
            {
                mb.M = Matrix4x4.TRS(new Vector3(i * 6.2f - 4f, 3.1f, R(-0.6f, 0.6f)), Quaternion.Euler(R(-4f, 4f), R(-6f, 6f), 90f), Vector3.one);
                mb.Cylinder(mMarble, Vector3.zero, 3.1f, 3.1f, 5.6f, 18, true);
            }
            mb.M = Matrix4x4.TRS(new Vector3(18f, 2.5f, 3f), Quaternion.Euler(R(-10f, 10f), R(0f, 90f), R(15f, 25f)), Vector3.one);
            mb.Box(mMarble, Vector3.zero, new Vector3(8f, 3.2f, 8f));
            mb.M = Matrix4x4.identity;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = new Vector3(R(-12f, 22f), 0f, R(-9f, 11f));
                float s = R(1.5f, 4.5f);
                var corners = new Vector3[8];
                for (int k = 0; k < 8; k++)
                {
                    float x = (k == 1 || k == 2 || k == 5 || k == 6) ? 1f : -1f;
                    float z = (k == 2 || k == 3 || k == 6 || k == 7) ? 1f : -1f;
                    float y = k < 4 ? -0.4f : 1f;
                    corners[k] = c + new Vector3(x * s * R(0.6f, 1.1f), y * s * R(0.5f, 0.9f), z * s * R(0.6f, 1.1f));
                }
                mb.Hex(i % 3 == 0 ? mMarble : mTrav, corners);
            }
            var mesh = mb.ToMesh(name);
            SaveMesh(mesh, name);
            var go = PropObj(name, parent, mesh, mb.Mats);
            go.transform.position = at;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        static GameObject PropObj(string name, Transform parent, Mesh mesh, Material[] mats)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            SetStatic(go, mr, true);
            return go;
        }

        // ================= outside =================

        static void BuildSurroundings(Transform parent)
        {
            var plaza = new MeshB();
            var ground = new MeshB();
            for (int sec = 0; sec < Sectors; sec++)
            {
                int j0 = sec * SectorSteps, j1 = j0 + SectorSteps;
                FlatRing(plaza, mPave, new[] { 107f, 130f, 160f, 190f }, 0f, j0, j1);
                FlatRing(ground, mEarth, new[] { 190f, 260f, 380f, 600f, 1000f, 1700f, 2900f }, -0.05f, j0, j1);
            }
            int gl = LayerMask.NameToLayer("Ground");
            MakeObj("Plaza", parent, plaza, true, gl >= 0 ? gl : 0);
            var g = MakeObj("Ground", parent, ground, false, 0, true, Gi.None);
            g.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var col = g.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, -1f, 0f);
            col.size = new Vector3(6000f, 2f, 6000f);

            // distant hills to frame the horizon
            var hills = new MeshB();
            float[] radii = { 1300f, 1600f, 1900f, 2200f, 2550f, 2900f };
            int n = 160;
            var pts = new Vector3[n + 1, radii.Length];
            for (int i = 0; i <= n; i++)
            {
                float t = 2f * Mathf.PI * i / n;
                for (int k = 0; k < radii.Length; k++)
                {
                    float ridge = Mathf.PerlinNoise(Mathf.Cos(t) * 2.4f + 30f + k * 0.35f, Mathf.Sin(t) * 2.4f + 30f);
                    float bump = Mathf.Sin(Mathf.PI * k / (radii.Length - 1));
                    float h = (k == 0 ? 0f : bump * (60f + ridge * ridge * 380f)) - 2f;
                    pts[i, k] = new Vector3(Mathf.Cos(t) * radii[k], h, Mathf.Sin(t) * radii[k]);
                }
            }
            for (int i = 0; i < n; i++)
            for (int k = 0; k < radii.Length - 1; k++)
            {
                Vector3 p0 = pts[i, k], p1 = pts[i + 1, k], p2 = pts[i + 1, k + 1], p3 = pts[i, k + 1];
                Vector3 nn = Vector3.Cross(p1 - p0, p3 - p0).normalized;
                if (nn.y < 0f) nn = -nn;
                hills.Quad(mHills, p0, p1, p2, p3, nn, new Vector2(p0.x, p0.z), new Vector2(p1.x, p1.z), new Vector2(p2.x, p2.z), new Vector2(p3.x, p3.z));
            }
            var h2 = MakeObj("Hills", parent, hills, false, 0, true, Gi.None);
            h2.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        // Flat ring with world-planar UVs (ring arc-length UVs shear badly over wide rings).
        static void FlatRing(MeshB mb, Material m, float[] ds, float y, int j0, int j1)
        {
            for (int j = j0; j < j1; j++)
            for (int k = 0; k < ds.Length - 1; k++)
            {
                Vector3 p0 = Pt(tS[j], ds[k], y), p1 = Pt(tS[j + 1], ds[k], y), p2 = Pt(tS[j + 1], ds[k + 1], y), p3 = Pt(tS[j], ds[k + 1], y);
                mb.Quad(m, p0, p1, p2, p3, Vector3.up, new Vector2(p0.x, p0.z), new Vector2(p1.x, p1.z), new Vector2(p2.x, p2.z), new Vector2(p3.x, p3.z));
            }
        }

        // ================= lighting / post =================

        static void BuildLighting(Transform col, GameObject root)
        {
            var lt = Group(col, "Lighting");
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(lt, false);
            sunGo.transform.rotation = Quaternion.Euler(31f, 58f, 0f);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.86f, 0.70f);
            sun.intensity = 1.7f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.92f;
            sun.lightmapBakeType = LightmapBakeType.Mixed; // direct sun stays realtime (toon bands + crisp shadows), bounce is baked
            RenderSettings.sun = sun;

            var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
            if (sky != null) RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.80f, 0.85f, 1.0f);
            RenderSettings.ambientEquatorColor = new Color(0.88f, 0.76f, 0.64f);
            RenderSettings.ambientGroundColor = new Color(0.58f, 0.48f, 0.38f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 380f;
            RenderSettings.fogEndDistance = 3400f;
            RenderSettings.fogColor = new Color(0.78f, 0.66f, 0.56f);

            var probeGo = new GameObject("ReflectionProbe");
            probeGo.transform.SetParent(lt, false);
            probeGo.transform.position = new Vector3(0f, 40f, 0f);
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Baked;
            probe.size = new Vector3(720f, 260f, 620f);
            probe.resolution = 256;
            probe.farClipPlane = 3000f;

            // light probes so robots and other moving things pick up the baked bounce light
            var lpGo = new GameObject("LightProbes");
            lpGo.transform.SetParent(lt, false);
            var pts = new List<Vector3>();
            for (float x = -160f; x <= 160f; x += 20f)
            for (float z = -120f; z <= 120f; z += 20f)
            {
                if ((x / A) * (x / A) + (z / B) * (z / B) > 0.93f) continue;
                foreach (float y in new[] { 1.5f, 12f, 28f, 45f }) pts.Add(new Vector3(x, y, z));
            }
            foreach (float sx in new[] { -1f, 1f })
            for (float d = 10f; d <= 90f; d += 20f)
            foreach (float z in new[] { -12f, 12f })
            foreach (float y in new[] { 2f, 20f })
                pts.Add(new Vector3(sx * (A + d), y, z));
            lpGo.AddComponent<LightProbeGroup>().probePositions = pts.ToArray();

            SetupLightingSettings();

            // floating dust in the low sun
            var fx = Group(col, "FX");
            var dust = new GameObject("DustMotes");
            dust.transform.SetParent(fx, false);
            dust.transform.position = new Vector3(0f, 16f, 0f);
            var ps = dust.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(10f, 16f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.9f, 0.75f, 0.35f), new Color(1f, 0.8f, 0.6f, 0.15f));
            main.maxParticles = 900;
            main.prewarm = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 60f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(320f, 30f, 230f);
            var nz = ps.noise;
            nz.enabled = true;
            nz.strength = 0.6f;
            nz.frequency = 0.15f;
            var col2 = ps.colorOverLifetime;
            col2.enabled = true;
            col2.color = Fade(Color.white, Color.white);
            var r = dust.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mDust;
            r.shadowCastingMode = ShadowCastingMode.Off;

            if (root.GetComponent<ArenaShadowDistance>() == null) root.AddComponent<ArenaShadowDistance>();

            var vol = Object.FindFirstObjectByType<Volume>();
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
            if (vol != null && profile != null) vol.sharedProfile = profile;
            if (profile != null) SetupVolume(profile);
        }

        // Baked Indirect: lightmaps hold ambient + bounce + braziers, the sun stays realtime.
        static void SetupLightingSettings()
        {
            var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingPath);
            if (ls == null)
            {
                ls = new LightingSettings { name = "RobotArena_Lighting" };
                AssetDatabase.CreateAsset(ls, LightingPath);
            }
            ls.bakedGI = true;
            ls.realtimeGI = false;
            ls.mixedBakeMode = MixedLightingMode.IndirectOnly;
            ls.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            ls.lightmapResolution = 1f;
            ls.lightmapPadding = 4;
            ls.lightmapMaxSize = 2048;
            ls.lightmapCompression = LightmapCompression.NormalQuality;
            ls.directionalityMode = LightmapsMode.NonDirectional;
            ls.ao = true;
            ls.aoMaxDistance = 6f;
            ls.aoExponentIndirect = 1f;
            ls.aoExponentDirect = 0f;
            ls.maxBounces = 2;
            ls.directSampleCount = 32;
            ls.indirectSampleCount = 256;
            ls.environmentSampleCount = 128;
            ls.filteringMode = LightingSettings.FilterMode.Auto;
            EditorUtility.SetDirty(ls);
            Lightmapping.lightingSettings = ls;
        }

        [MenuItem("Tools/NSC/Arena/Bake Colosseum Lighting")]
        public static void BakeLighting()
        {
            var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingPath);
            if (ls != null) Lightmapping.lightingSettings = ls;
            Lightmapping.BakeAsync();
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
            var tm = VolumeComp<Tonemapping>(p);
            tm.mode.Override(TonemappingMode.ACES);
            var bloom = VolumeComp<Bloom>(p);
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.55f);
            bloom.scatter.Override(0.7f);
            bloom.tint.Override(new Color(1f, 0.9f, 0.78f));
            var ca = VolumeComp<ColorAdjustments>(p);
            ca.postExposure.Override(-0.1f);
            ca.contrast.Override(22f);
            ca.saturation.Override(4f);
            ca.colorFilter.Override(new Color(1f, 0.97f, 0.93f));
            var vg = VolumeComp<Vignette>(p);
            vg.intensity.Override(0.24f);
            vg.smoothness.Override(0.45f);
            vg.color.Override(Color.black);
            var wb = VolumeComp<WhiteBalance>(p);
            wb.temperature.Override(0f);
            wb.tint.Override(2f);
            var smh = VolumeComp<ShadowsMidtonesHighlights>(p);
            smh.shadows.Override(new Vector4(0.94f, 0.98f, 1.08f, 0f));
            smh.highlights.Override(new Vector4(1.05f, 1.0f, 0.94f, 0f));

            // Toon Shaders Pro outlines: the OutlineFeature already sits on PC_Renderer and only draws where a volume turns it on
            var ol = VolumeComp<ToonShadersPro.URP.OutlineSettings>(p);
            ol.outlineType.Override(ToonShadersPro.URP.OutlineType.DepthNormalOutlines);
            ol.outlineColor.Override(new Color(0.07f, 0.045f, 0.03f, 1f));
            ol.colorSensitivity.Override(1f);
            ol.colorStrength.Override(0f);
            ol.depthSensitivity.Override(0.0006f);
            ol.depthStrength.Override(1f);
            ol.normalSensitivity.Override(0.25f);
            ol.normalStrength.Override(1f);
            ol.depthThreshold.Override(0.9f);
            EditorUtility.SetDirty(p);
        }

        static void RestyleSpawns(Transform root)
        {
            var sp = root.Find("SpawnPoints");
            if (sp == null) return;
            for (int i = sp.childCount - 1; i >= 0; i--)
            {
                var c = sp.GetChild(i);
                if (c.name.StartsWith("SpawnPad") || c.name.StartsWith("SpawnRing")) Object.DestroyImmediate(c.gameObject);
            }
            foreach (var (name, pos, glow) in new[] { ("P1_Blue", SpawnBlue, mGlowBlue), ("P2_Red", SpawnRed, mGlowRed) })
            {
                var point = sp.Find("SpawnPoint_" + name);
                if (point != null) point.position = new Vector3(pos.x, 1f, pos.y);
                var mb = new MeshB();
                mb.Annulus(mMarble, new Vector3(0f, 0.14f, 0f), 12.6f, 14.4f, 64);
                mb.Annulus(glow, new Vector3(0f, 0.15f, 0f), 12.0f, 12.6f, 64);
                var pad = MakeObj("SpawnPad_" + name, sp, mb, false, 0, false, Gi.None);
                pad.transform.position = new Vector3(pos.x, 0f, pos.y);
            }
        }

        static void SetupCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;
            cam.transform.position = new Vector3(-20f, 22f, -90f);
            cam.transform.rotation = Quaternion.Euler(6f, 8f, 0f);
            cam.fieldOfView = 55f;
            cam.farClipPlane = 4000f;
            cam.nearClipPlane = 0.3f;
            var data = cam.GetComponent<UniversalAdditionalCameraData>();
            if (data != null) data.renderPostProcessing = true;
        }

        // ================= FX helpers =================

        static Gradient Fade(Color a, Color b)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        static void AddFire(Transform parent, Vector3 localPos, float s, Color fire)
        {
            var go = new GameObject("Fire");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.0f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f * s, 5f * s);
            main.startSize = new ParticleSystem.MinMaxCurve(2.2f * s, 4f * s);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(Color.Lerp(fire, Color.white, 0.35f), fire);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 120;
            main.prewarm = true;
            var em = ps.emission;
            em.rateOverTime = 45f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 10f;
            sh.radius = 1.5f * s;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.7f, 0.5f), 0.45f), new GradientColorKey(new Color(0.5f, 0.2f, 0.12f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.12f), new GradientAlphaKey(0.45f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.25f));
            var nz = ps.noise;
            nz.enabled = true;
            nz.strength = 0.8f * s;
            nz.frequency = 0.8f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mFire;
            r.shadowCastingMode = ShadowCastingMode.Off;

            var eg = new GameObject("Embers");
            eg.transform.SetParent(go.transform, false);
            var es = eg.AddComponent<ParticleSystem>();
            var em2 = es.main;
            em2.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            em2.startSpeed = new ParticleSystem.MinMaxCurve(4f * s, 9f * s);
            em2.startSize = new ParticleSystem.MinMaxCurve(0.12f * s, 0.3f * s);
            em2.startColor = new ParticleSystem.MinMaxGradient(Color.Lerp(fire, Color.white, 0.5f));
            em2.simulationSpace = ParticleSystemSimulationSpace.World;
            em2.scalingMode = ParticleSystemScalingMode.Hierarchy;
            em2.maxParticles = 60;
            em2.prewarm = true;
            var ee = es.emission;
            ee.rateOverTime = 7f;
            var esh = es.shape;
            esh.shapeType = ParticleSystemShapeType.Cone;
            esh.angle = 18f;
            esh.radius = 1.2f * s;
            var en = es.noise;
            en.enabled = true;
            en.strength = 2f * s;
            en.frequency = 0.6f;
            var ec = es.colorOverLifetime;
            ec.enabled = true;
            ec.color = Fade(Color.white, new Color(1f, 0.4f, 0.1f));
            var er = eg.GetComponent<ParticleSystemRenderer>();
            er.sharedMaterial = mFire;
            er.shadowCastingMode = ShadowCastingMode.Off;
        }

        // Point lights are baked into the lightmap (no per-object light limit, no runtime cost).
        // Baking uses inverse-square falloff, hence the large intensities.
        static void AddLight(Transform parent, string name, Vector3 localPos, Color c, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.Soft;
            l.lightmapBakeType = LightmapBakeType.Baked;
        }

        // ================= materials / objects =================

        static Shader lit, toon;

        // false falls back to URP Lit everywhere (no toon bands).
        static readonly bool UseToon = true;

        static Material Lit(string name, string tex, string nrm, Color tint, float tile, float smooth, float metal = 0f, float bump = 1f)
        {
            string path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(lit);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = lit;
            m.SetTexture("_BaseMap", tex != null ? ColosseumTextures.Load(tex) : null);
            m.SetColor("_BaseColor", tint);
            m.SetTextureScale("_BaseMap", new Vector2(1f / tile, 1f / tile));
            var n = nrm != null ? ColosseumTextures.Load(nrm) : null;
            m.SetTexture("_BumpMap", n);
            m.SetFloat("_BumpScale", bump);
            if (n != null) m.EnableKeyword("_NORMALMAP"); else m.DisableKeyword("_NORMALMAP");
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            m.SetFloat("_Cull", 2f);
            m.doubleSidedGI = false;
            m.DisableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.black);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static void Emissive(Material m, Color c)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        static void TwoSided(Material m)
        {
            m.SetFloat("_Cull", 0f);
            m.doubleSidedGI = true;
        }

        static Material Particle(string name, bool additive)
        {
            string path = MatDir + "/" + name + ".mat";
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(sh);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = sh;
            m.SetTexture("_BaseMap", ColosseumTextures.Load("T_SoftDot"));
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        static void CreateMaterials()
        {
            lit = Shader.Find("Universal Render Pipeline/Lit");
            toon = Shader.Find("Toon Shaders Pro/URP/Toon");
            mSand = Toon("M_Sand", "T_SandMacro", "T_SandMacro_N", new Color(0.92f, 0.94f, 1f), 1f, 0.05f, 0f, 0.25f); // soft normals: strong ones break the toon bands into blotches
            mSand.SetTextureScale("_BaseMap", Vector2.one);

            mTrav = Toon("M_Travertine", "T_Travertine", "T_Travertine_N", Color.white, 8f, 0.1f);
            mTravDark = Toon("M_TravertineShade", "T_Travertine", "T_Travertine_N", new Color(0.55f, 0.5f, 0.45f), 8f, 0.05f);
            mMarble = Toon("M_Marble", "T_Marble", "T_Marble_N", Color.white, 6f, 0.3f);
            mSeats = Toon("M_Seats", "T_Marble", "T_Marble_N", new Color(0.78f, 0.74f, 0.68f), 6f, 0.1f);
            mIron = Toon("M_Iron", "T_Iron", "T_Iron_N", Color.white, 4f, 0.45f, 0.5f, 1f, true);
            mGold = Toon("M_Gold", null, null, new Color(1f, 0.76f, 0.33f), 1f, 0.7f, 0.6f, 1f, true);
            mBronze = Toon("M_Bronze", "T_Iron", "T_Iron_N", new Color(0.85f, 0.55f, 0.32f), 6f, 0.5f, 0.4f, 0.6f, true);
            mVelarium = Toon("M_Velarium", "T_Velarium", null, Color.white, 1f, 0f);
            TwoSided(mVelarium);
            mBanRed = Toon("M_BannerRed", "T_BannerRed", null, Color.white, 1f, 0.1f);
            mBanBlue = Toon("M_BannerBlue", "T_BannerBlue", null, Color.white, 1f, 0.1f);
            mBanPurple = Toon("M_BannerPurple", "T_BannerPurple", null, Color.white, 1f, 0.1f);
            TwoSided(mBanRed); TwoSided(mBanBlue); TwoSided(mBanPurple);
            mPave = Toon("M_Paving", "T_Paving", "T_Paving_N", Color.white, 12f, 0.1f);
            mEarth = Toon("M_Earth", "T_Earth", null, Color.white, 24f, 0f);
            mHills = Toon("M_Hills", "T_Earth", null, new Color(0.85f, 0.8f, 0.75f), 160f, 0f);
            mVoid = Toon("M_Void", null, null, new Color(0.02f, 0.018f, 0.015f), 1f, 0f);
            mWood = Toon("M_Wood", "T_Wood", null, Color.white, 4f, 0.1f);
            mRope = Toon("M_Rope", null, null, new Color(0.45f, 0.36f, 0.24f), 1f, 0f);

            // the toon shader has no emission, so glowing things stay on URP Lit
            mEmber = Lit("M_Embers", null, null, new Color(0.15f, 0.05f, 0.02f), 1f, 0.2f);
            Emissive(mEmber, new Color(1f, 0.35f, 0.08f) * 6f);
            mPit = Lit("M_HypogeumPit", null, null, new Color(0.02f, 0.015f, 0.01f), 1f, 0f);
            Emissive(mPit, new Color(1f, 0.32f, 0.08f) * 0.9f);
            mGlowBlue = Lit("M_GlowBlue", null, null, TeamBlue * 0.3f, 1f, 0.2f);
            Emissive(mGlowBlue, new Color(0.08f, 0.42f, 2.4f));
            mGlowRed = Lit("M_GlowRed", null, null, TeamRed * 0.3f, 1f, 0.2f);
            Emissive(mGlowRed, new Color(2.4f, 0.16f, 0.05f));
            mFire = Particle("M_Fire", true);
            mDust = Particle("M_Dust", false);
        }

        // Toon Shaders Pro: three hard bands (shadow / middle / lit), cast shadows cut hard,
        // ambient + bounce come from the baked lightmap (GI).
        static Material Toon(string name, string tex, string nrm, Color tint, float tile, float smooth,
            float metal = 0f, float bump = 1f, bool shiny = false)
        {
            var m = Lit(name, tex, nrm, tint, tile, smooth, metal, bump);
            if (!UseToon) return m;
            m.shader = toon;
            m.SetTexture("_BaseMap", tex != null ? ColosseumTextures.Load(tex) : null);
            m.SetTextureScale("_BaseMap", new Vector2(1f / tile, 1f / tile));
            m.SetTexture("_BumpMap", nrm != null ? ColosseumTextures.Load(nrm) : null);
            m.SetFloat("_BumpScale", bump);
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
            m.SetColor("_RimColor", shiny ? new Color(0.35f, 0.3f, 0.25f) : Color.black);
            m.SetVector("_RimThresholds", new Vector4(0.75f, 0.77f, 0f, 0f));
            m.SetColor("_SpecularColor", shiny ? new Color(0.9f, 0.8f, 0.6f) : Color.black);
            m.SetVector("_SpecularThresholds", new Vector4(0.01f, 0.02f, 0f, 0f));
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            EditorUtility.SetDirty(m);
            return m;
        }

        enum Gi { None, Unwrap, CopyUv0 }

        // Static meshes get a lightmap UV set: generated, or copied when UV0 is already a 0..1 layout (sand macro).
        static void SaveMesh(Mesh mesh, string name, Gi gi = Gi.Unwrap)
        {
            if (gi == Gi.Unwrap)
            {
                UnwrapParam.SetDefaults(out var up);
                up.hardAngle = 80f;
                Unwrapping.GenerateSecondaryUVSet(mesh, up);
            }
            else if (gi == Gi.CopyUv0) mesh.uv2 = mesh.uv;
            AssetDatabase.CreateAsset(mesh, MeshDir + "/" + name + ".asset");
        }

        static void SetStatic(GameObject go, MeshRenderer mr, bool gi, float lightmapScale = 1f)
        {
            var flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic |
                        StaticEditorFlags.OccluderStatic | StaticEditorFlags.ReflectionProbeStatic;
            if (gi) flags |= StaticEditorFlags.ContributeGI;
            GameObjectUtility.SetStaticEditorFlags(go, flags);
            mr.receiveGI = gi ? ReceiveGI.Lightmaps : ReceiveGI.LightProbes;
            if (gi && !Mathf.Approximately(lightmapScale, 1f))
            {
                var so = new SerializedObject(mr);
                so.FindProperty("m_ScaleInLightmap").floatValue = lightmapScale;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static GameObject MakeObj(string name, Transform parent, MeshB mb, bool collider, int layer = 0, bool shadows = true,
            Gi gi = Gi.Unwrap, float lightmapScale = 1f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.layer = layer;
            var mesh = mb.ToMesh(name);
            SaveMesh(mesh, parent.name + "_" + name, gi);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mb.Mats;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            SetStatic(go, mr, gi != Gi.None, lightmapScale);
            return go;
        }
    }
}
