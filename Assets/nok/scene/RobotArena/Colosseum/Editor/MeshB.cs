using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Nsc.Arena.EditorTools
{
    // Small mesh builder for the colosseum generator: one submesh per material,
    // every shape is emitted through M (local -> mesh space). UVs are in metres unless noted.
    public class MeshB
    {
        public Matrix4x4 M = Matrix4x4.identity;

        readonly List<Vector3> v = new List<Vector3>();
        readonly List<Vector3> n = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<Material> mats = new List<Material>();
        readonly List<List<int>> tris = new List<List<int>>();

        public Material[] Mats => mats.ToArray();

        List<int> T(Material m)
        {
            int i = mats.IndexOf(m);
            if (i < 0) { mats.Add(m); tris.Add(new List<int>()); i = mats.Count - 1; }
            return tris[i];
        }

        public Mesh ToMesh(string name)
        {
            // weld identical vertices (every quad is emitted with its own four) to keep the mesh assets small
            var remap = new int[v.Count];
            var lookup = new Dictionary<(Vector3Int, Vector3Int, Vector2Int), int>();
            var wv = new List<Vector3>(); var wn = new List<Vector3>(); var wuv = new List<Vector2>();
            for (int i = 0; i < v.Count; i++)
            {
                var key = (Vector3Int.RoundToInt(v[i] * 1000f), Vector3Int.RoundToInt(n[i] * 200f),
                    Vector2Int.RoundToInt(uv[i] * 2000f));
                if (!lookup.TryGetValue(key, out int w))
                {
                    w = wv.Count;
                    lookup[key] = w;
                    wv.Add(v[i]); wn.Add(n[i]); wuv.Add(uv[i]);
                }
                remap[i] = w;
            }

            var mesh = new Mesh { name = name };
            mesh.indexFormat = wv.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(wv);
            mesh.SetNormals(wn);
            mesh.SetUVs(0, wuv);
            mesh.subMeshCount = mats.Count;
            for (int i = 0; i < mats.Count; i++)
            {
                var t = tris[i];
                var wt = new int[t.Count];
                for (int k = 0; k < t.Count; k++) wt[k] = remap[t[k]];
                mesh.SetTriangles(wt, i);
            }
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        // ---------- primitives ----------

        // Winding is chosen so the face front matches the given normals.
        public void Quad(Material m, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
            Vector3 n0, Vector3 n1, Vector3 n2, Vector3 n3, Vector2 u0, Vector2 u1, Vector2 u2, Vector2 u3)
        {
            p0 = M.MultiplyPoint3x4(p0); p1 = M.MultiplyPoint3x4(p1); p2 = M.MultiplyPoint3x4(p2); p3 = M.MultiplyPoint3x4(p3);
            n0 = M.MultiplyVector(n0).normalized; n1 = M.MultiplyVector(n1).normalized;
            n2 = M.MultiplyVector(n2).normalized; n3 = M.MultiplyVector(n3).normalized;
            int b = v.Count;
            v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3);
            n.Add(n0); n.Add(n1); n.Add(n2); n.Add(n3);
            uv.Add(u0); uv.Add(u1); uv.Add(u2); uv.Add(u3);
            Vector3 face = Vector3.Cross(p1 - p0, p2 - p0);
            if (face.sqrMagnitude < 1e-10f) face = Vector3.Cross(p2 - p0, p3 - p0);
            var t = T(m);
            if (Vector3.Dot(face, n0 + n1 + n2 + n3) >= 0f)
            {
                t.Add(b); t.Add(b + 1); t.Add(b + 2);
                t.Add(b); t.Add(b + 2); t.Add(b + 3);
            }
            else
            {
                t.Add(b); t.Add(b + 2); t.Add(b + 1);
                t.Add(b); t.Add(b + 3); t.Add(b + 2);
            }
        }

        public void Quad(Material m, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 nn,
            Vector2 u0, Vector2 u1, Vector2 u2, Vector2 u3) =>
            Quad(m, p0, p1, p2, p3, nn, nn, nn, nn, u0, u1, u2, u3);

        // Planar quad with UVs projected onto its own axes.
        void PlanarQuad(Material m, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 nn)
        {
            Vector3 ax = (p1 - p0).sqrMagnitude > 1e-8f ? (p1 - p0).normalized : (p2 - p3).normalized;
            Vector3 ay = Vector3.Cross(nn, ax).normalized;
            Quad(m, p0, p1, p2, p3, nn,
                new Vector2(Vector3.Dot(p0, ax), Vector3.Dot(p0, ay)), new Vector2(Vector3.Dot(p1, ax), Vector3.Dot(p1, ay)),
                new Vector2(Vector3.Dot(p2, ax), Vector3.Dot(p2, ay)), new Vector2(Vector3.Dot(p3, ax), Vector3.Dot(p3, ay)));
        }

        public void Box(Material m, Vector3 c, Vector3 s, bool bottom = true)
        {
            Vector3 h = s * 0.5f;
            Vector3 X = Vector3.right, Y = Vector3.up, Z = Vector3.forward;
            Face(m, c, X, Z, Y, h.x, h.z, h.y);
            Face(m, c, -X, Z, Y, h.x, h.z, h.y);
            Face(m, c, Z, X, Y, h.z, h.x, h.y);
            Face(m, c, -Z, X, Y, h.z, h.x, h.y);
            Face(m, c, Y, X, Z, h.y, h.x, h.z);
            if (bottom) Face(m, c, -Y, X, Z, h.y, h.x, h.z);
        }

        void Face(Material m, Vector3 c, Vector3 nrm, Vector3 ax, Vector3 ay, float dn, float ex, float ey)
        {
            Vector3 o = c + nrm * dn;
            Vector3 p0 = o - ax * ex - ay * ey, p1 = o + ax * ex - ay * ey, p2 = o + ax * ex + ay * ey, p3 = o - ax * ex + ay * ey;
            Quad(m, p0, p1, p2, p3, nrm,
                new Vector2(Vector3.Dot(p0, ax), Vector3.Dot(p0, ay)), new Vector2(Vector3.Dot(p1, ax), Vector3.Dot(p1, ay)),
                new Vector2(Vector3.Dot(p2, ax), Vector3.Dot(p2, ay)), new Vector2(Vector3.Dot(p3, ax), Vector3.Dot(p3, ay)));
        }

        // Convex hexahedron: corners 0-3 bottom ring, 4-7 top ring (same order). Flat shaded.
        public void Hex(Material m, Vector3[] c)
        {
            Vector3 centre = Vector3.zero;
            foreach (var p in c) centre += p;
            centre /= 8f;
            int[][] faces =
            {
                new[] { 0, 1, 2, 3 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 },
                new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 }
            };
            foreach (var f in faces)
            {
                Vector3 a = c[f[0]], b = c[f[1]], d = c[f[2]], e = c[f[3]];
                Vector3 nrm = Vector3.Cross(b - a, d - a);
                if (nrm.sqrMagnitude < 1e-8f) nrm = Vector3.Cross(d - a, e - a);
                if (nrm.sqrMagnitude < 1e-8f) nrm = Vector3.Cross(d - b, e - b);
                if (nrm.sqrMagnitude < 1e-8f) continue;
                nrm.Normalize();
                if (Vector3.Dot(nrm, (a + b + d + e) * 0.25f - centre) < 0f) nrm = -nrm;
                PlanarQuad(m, a, b, d, e, nrm);
            }
        }

        // Thin square beam between two points.
        public void Beam(Material m, Vector3 p0, Vector3 p1, float th)
        {
            Vector3 d = (p1 - p0).normalized;
            Vector3 u = Vector3.Cross(d, Vector3.up);
            if (u.sqrMagnitude < 1e-6f) u = Vector3.Cross(d, Vector3.right);
            u = u.normalized * th * 0.5f;
            Vector3 w = Vector3.Cross(u, d).normalized * th * 0.5f;
            Hex(m, new[] { p0 - u - w, p0 + u - w, p0 + u + w, p0 - u + w, p1 - u - w, p1 + u - w, p1 + u + w, p1 - u + w });
        }

        // Surface of revolution around local +y. Profile points are (radius, height);
        // walking upward faces outward, walking inward faces up.
        public void Lathe(Material m, Vector3 origin, List<Vector2> prof, int sides)
        {
            float cum = 0f;
            for (int k = 0; k < prof.Count - 1; k++)
            {
                Vector2 a = prof[k], b = prof[k + 1];
                Vector2 dir = b - a;
                float len = dir.magnitude;
                if (len < 1e-5f) continue;
                dir /= len;
                Vector2 n2 = new Vector2(dir.y, -dir.x);
                for (int s = 0; s < sides; s++)
                {
                    float t0 = 2f * Mathf.PI * s / sides, t1 = 2f * Mathf.PI * (s + 1) / sides;
                    Vector3 c0 = new Vector3(Mathf.Cos(t0), 0f, Mathf.Sin(t0)), c1 = new Vector3(Mathf.Cos(t1), 0f, Mathf.Sin(t1));
                    float rr = Mathf.Max(a.x, b.x, 0.5f);
                    Quad(m,
                        origin + c0 * a.x + Vector3.up * a.y, origin + c1 * a.x + Vector3.up * a.y,
                        origin + c1 * b.x + Vector3.up * b.y, origin + c0 * b.x + Vector3.up * b.y,
                        (c0 * n2.x + Vector3.up * n2.y).normalized, (c1 * n2.x + Vector3.up * n2.y).normalized,
                        (c1 * n2.x + Vector3.up * n2.y).normalized, (c0 * n2.x + Vector3.up * n2.y).normalized,
                        new Vector2(t0 * rr, cum), new Vector2(t1 * rr, cum), new Vector2(t1 * rr, cum + len), new Vector2(t0 * rr, cum + len));
                }
                cum += len;
            }
        }

        public void Cylinder(Material m, Vector3 baseCentre, float r0, float r1, float h, int sides, bool caps)
        {
            Lathe(m, baseCentre, new List<Vector2> { new Vector2(r0, 0f), new Vector2(r1, h) }, sides);
            if (!caps) return;
            Disc(m, baseCentre + Vector3.up * h, r1, sides, true);
            Disc(m, baseCentre, r0, sides, false);
        }

        public void Disc(Material m, Vector3 c, float r, int sides, bool up) => Annulus(m, c, 0f, r, sides, up);

        public void Annulus(Material m, Vector3 c, float r0, float r1, int sides, bool up = true)
        {
            Vector3 nn = up ? Vector3.up : Vector3.down;
            for (int s = 0; s < sides; s++)
            {
                float t0 = 2f * Mathf.PI * s / sides, t1 = 2f * Mathf.PI * (s + 1) / sides;
                Vector3 c0 = new Vector3(Mathf.Cos(t0), 0f, Mathf.Sin(t0)), c1 = new Vector3(Mathf.Cos(t1), 0f, Mathf.Sin(t1));
                Vector3 p0 = c + c0 * r0, p1 = c + c1 * r0, p2 = c + c1 * r1, p3 = c + c0 * r1;
                Quad(m, p0, p1, p2, p3, nn, new Vector2(p0.x, p0.z), new Vector2(p1.x, p1.z), new Vector2(p2.x, p2.z), new Vector2(p3.x, p3.z));
            }
        }

        // Torus in the local XY plane (axis = local z) around the local origin.
        public void Torus(Material m, float R, float r, int segs, int tube)
        {
            for (int i = 0; i < segs; i++)
            for (int j = 0; j < tube; j++)
            {
                float a0 = 2f * Mathf.PI * i / segs, a1 = 2f * Mathf.PI * (i + 1) / segs;
                float b0 = 2f * Mathf.PI * j / tube, b1 = 2f * Mathf.PI * (j + 1) / tube;
                Quad(m, TP(R, r, a0, b0), TP(R, r, a1, b0), TP(R, r, a1, b1), TP(R, r, a0, b1),
                    TN(a0, b0), TN(a1, b0), TN(a1, b1), TN(a0, b1),
                    new Vector2(a0 * R, b0 * r), new Vector2(a1 * R, b0 * r), new Vector2(a1 * R, b1 * r), new Vector2(a0 * R, b1 * r));
            }
        }

        static Vector3 TP(float R, float r, float a, float b) =>
            new Vector3((R + r * Mathf.Cos(b)) * Mathf.Cos(a), (R + r * Mathf.Cos(b)) * Mathf.Sin(a), r * Mathf.Sin(b));

        static Vector3 TN(float a, float b) => new Vector3(Mathf.Cos(b) * Mathf.Cos(a), Mathf.Cos(b) * Mathf.Sin(a), Mathf.Sin(b));

        // Hanging cloth (banner / flag) in the local XY plane, top edge centred on `top`, bulging toward -z.
        // UVs 0..1 (v = 1 at the top).
        public void Cloth(Material m, Vector3 top, float w, float h, float bulge, bool flag = false)
        {
            const int nx = 4, ny = 10;
            var p = new Vector3[nx + 1, ny + 1];
            for (int i = 0; i <= nx; i++)
            for (int k = 0; k <= ny; k++)
            {
                float u = i / (float)nx, vv = k / (float)ny;
                float z = flag
                    ? -Mathf.Sin(u * Mathf.PI * 2.2f + vv) * bulge * u
                    : -Mathf.Sin(u * Mathf.PI) * bulge * (0.35f + 0.65f * vv) - Mathf.Sin(u * Mathf.PI * 3f) * bulge * 0.3f * vv;
                p[i, k] = top + new Vector3((u - 0.5f) * w, -vv * h, z);
            }
            for (int i = 0; i < nx; i++)
            for (int k = 0; k < ny; k++)
            {
                Vector3 a = p[i, k], b = p[i + 1, k], c = p[i + 1, k + 1], d = p[i, k + 1];
                Vector3 nn = Vector3.Cross(c - a, b - d).normalized;
                if (nn.z > 0f) nn = -nn;
                Quad(m, a, b, c, d, nn,
                    new Vector2(i / (float)nx, 1f - k / (float)ny), new Vector2((i + 1) / (float)nx, 1f - k / (float)ny),
                    new Vector2((i + 1) / (float)nx, 1f - (k + 1) / (float)ny), new Vector2(i / (float)nx, 1f - (k + 1) / (float)ny));
            }
        }

        // ---------- arches ----------

        // Opening outline: left jamb up, semicircle over, right jamb down. Local XY, base at y = 0.
        public static List<Vector2> ArchOutline(float ow, float sh, int segs)
        {
            float r = ow * 0.5f;
            var l = new List<Vector2> { new Vector2(-r, 0f) };
            for (int i = 0; i <= segs; i++)
            {
                float a = Mathf.PI - Mathf.PI * i / segs;
                l.Add(new Vector2(r * Mathf.Cos(a), sh + r * Mathf.Sin(a)));
            }
            l.Add(new Vector2(r, 0f));
            return l;
        }

        // Inner surface of an arch opening between z0 and z1 (faces into the opening).
        public void Soffit(Material m, List<Vector2> outline, float sh, float z0, float z1)
        {
            Vector2 c = new Vector2(0f, sh);
            float cum = 0f;
            int last = outline.Count - 2;
            for (int k = 0; k <= last; k++)
            {
                Vector2 a = outline[k], b = outline[k + 1];
                float len = Vector2.Distance(a, b);
                if (len < 1e-5f) continue;
                Vector3 na, nb;
                if (k == 0) na = nb = Vector3.right;
                else if (k == last) na = nb = Vector3.left;
                else
                {
                    Vector2 da = (c - a).normalized, db = (c - b).normalized;
                    na = new Vector3(da.x, da.y, 0f); nb = new Vector3(db.x, db.y, 0f);
                }
                Quad(m, new Vector3(a.x, a.y, z0), new Vector3(b.x, b.y, z0), new Vector3(b.x, b.y, z1), new Vector3(a.x, a.y, z1),
                    na, nb, nb, na,
                    new Vector2(cum, z0), new Vector2(cum + len, z0), new Vector2(cum + len, z1), new Vector2(cum, z1));
                cum += len;
            }
        }

        // Fills an arch-shaped outline at depth z (door); zSign = -1 faces -z.
        public void FanFill(Material m, List<Vector2> outline, float z, float zSign)
        {
            Vector2 c = Vector2.zero;
            foreach (var p in outline) c += p;
            c /= outline.Count;
            Vector3 nn = new Vector3(0f, 0f, zSign);
            for (int k = 0; k < outline.Count; k++)
            {
                Vector2 a = outline[k], b = outline[(k + 1) % outline.Count];
                Quad(m, new Vector3(c.x, c.y, z), new Vector3(a.x, a.y, z), new Vector3(b.x, b.y, z), new Vector3(c.x, c.y, z), nn,
                    c, a, b, c);
            }
        }

        // Wall panel w x h x thick with an arched opening. Local: x across, y up, front face at z = 0 facing -z.
        // Adds an archivolt band, keystone and impost blocks in `trim` on the front.
        public void ArchPanel(Material m, Material trim, float w, float h, float ow, float sh, float thick, int segs, bool caps)
        {
            float hw = w * 0.5f, r = ow * 0.5f;
            var arc = new List<Vector2>();
            for (int i = 0; i <= segs; i++)
            {
                float a = Mathf.PI - Mathf.PI * i / segs;
                arc.Add(new Vector2(r * Mathf.Cos(a), sh + r * Mathf.Sin(a)));
            }
            int nSide = segs / 4, nTop = segs - 2 * nSide;
            var outer = new List<Vector2>();
            for (int i = 0; i <= nSide; i++) outer.Add(new Vector2(-hw, sh + (h - sh) * i / nSide));
            for (int i = 1; i <= nTop; i++) outer.Add(new Vector2(-hw + w * i / nTop, h));
            for (int i = 1; i <= nSide; i++) outer.Add(new Vector2(hw, h - (h - sh) * i / nSide));

            foreach (float z in new[] { 0f, thick })
            {
                Vector3 nn = z == 0f ? Vector3.back : Vector3.forward;
                for (int i = 0; i < segs; i++)
                    Quad(m, V(arc[i], z), V(arc[i + 1], z), V(outer[i + 1], z), V(outer[i], z), nn,
                        arc[i], arc[i + 1], outer[i + 1], outer[i]);
                Quad(m, new Vector3(-hw, 0f, z), new Vector3(-r, 0f, z), new Vector3(-r, sh, z), new Vector3(-hw, sh, z), nn,
                    new Vector2(-hw, 0f), new Vector2(-r, 0f), new Vector2(-r, sh), new Vector2(-hw, sh));
                Quad(m, new Vector3(r, 0f, z), new Vector3(hw, 0f, z), new Vector3(hw, sh, z), new Vector3(r, sh, z), nn,
                    new Vector2(r, 0f), new Vector2(hw, 0f), new Vector2(hw, sh), new Vector2(r, sh));
            }
            Soffit(m, ArchOutline(ow, sh, segs), sh, 0f, thick);

            if (caps)
            {
                PlanarQuad(m, new Vector3(-hw, h, 0f), new Vector3(hw, h, 0f), new Vector3(hw, h, thick), new Vector3(-hw, h, thick), Vector3.up);
                PlanarQuad(m, new Vector3(-hw, 0f, 0f), new Vector3(-hw, h, 0f), new Vector3(-hw, h, thick), new Vector3(-hw, 0f, thick), Vector3.left);
                PlanarQuad(m, new Vector3(hw, 0f, 0f), new Vector3(hw, h, 0f), new Vector3(hw, h, thick), new Vector3(hw, 0f, thick), Vector3.right);
            }

            // archivolt
            float band = Mathf.Max(1f, ow * 0.08f), proud = 0.35f;
            Vector2 c = new Vector2(0f, sh);
            for (int i = 0; i < segs; i++)
            {
                Vector2 d0 = (arc[i] - c).normalized, d1 = (arc[i + 1] - c).normalized;
                Vector2 i0 = arc[i], i1 = arc[i + 1], o0 = c + d0 * (r + band), o1 = c + d1 * (r + band);
                Quad(trim, V(i0, -proud), V(i1, -proud), V(o1, -proud), V(o0, -proud), Vector3.back, i0, i1, o1, o0);
                Quad(trim, V(o0, -proud), V(o1, -proud), V(o1, 0f), V(o0, 0f),
                    new Vector3(d0.x, d0.y, 0f), new Vector3(d1.x, d1.y, 0f), new Vector3(d1.x, d1.y, 0f), new Vector3(d0.x, d0.y, 0f),
                    new Vector2(i * 0.5f, 0f), new Vector2((i + 1) * 0.5f, 0f), new Vector2((i + 1) * 0.5f, proud), new Vector2(i * 0.5f, proud));
                Quad(trim, V(i0, -proud), V(i1, -proud), V(i1, 0f), V(i0, 0f),
                    new Vector3(-d0.x, -d0.y, 0f), new Vector3(-d1.x, -d1.y, 0f), new Vector3(-d1.x, -d1.y, 0f), new Vector3(-d0.x, -d0.y, 0f),
                    new Vector2(i * 0.5f, 0f), new Vector2((i + 1) * 0.5f, 0f), new Vector2((i + 1) * 0.5f, proud), new Vector2(i * 0.5f, proud));
            }
            Box(trim, new Vector3(0f, sh + r + band * 0.4f, -0.3f), new Vector3(band * 1.6f, band * 2.2f, 0.9f));
            Box(trim, new Vector3(-(r + band * 0.5f), sh - band * 0.3f, -0.2f), new Vector3(band * 1.8f, band * 0.6f, 0.7f));
            Box(trim, new Vector3(r + band * 0.5f, sh - band * 0.3f, -0.2f), new Vector3(band * 1.8f, band * 0.6f, 0.7f));
        }

        static Vector3 V(Vector2 p, float z) => new Vector3(p.x, p.y, z);
    }
}
