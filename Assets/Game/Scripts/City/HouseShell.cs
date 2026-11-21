using System.Collections.Generic;
using UnityEngine;

namespace FPSBase.City
{
    /// <summary>
    /// A house built to a plan's footprint, for when the plan has no house prefab of its own: siding walls round the
    /// outline (a storey of 2.8 m per floor), a foundation band, windows on every floor, a front door and step on the
    /// front, and a gable roof over each rectangle of the footprint, with a chimney. Its parts are "Walls" (from the
    /// floor up, with the wall colliders) and "Roof" (from the top of the walls up), so it can be shown going up.
    /// </summary>
    public static class HouseShell
    {
        public const float Storey = 2.8f;

        struct Palette { public Color siding, trim, roof, door; }
        static readonly Palette[] palettes =
        {
            new Palette { siding = new Color(0.86f, 0.82f, 0.72f), trim = new Color(0.95f, 0.95f, 0.93f), roof = new Color(0.32f, 0.33f, 0.36f), door = new Color(0.45f, 0.12f, 0.1f) },
            new Palette { siding = new Color(0.62f, 0.72f, 0.78f), trim = new Color(0.96f, 0.96f, 0.95f), roof = new Color(0.25f, 0.26f, 0.3f), door = new Color(0.15f, 0.22f, 0.4f) },
            new Palette { siding = new Color(0.78f, 0.67f, 0.52f), trim = new Color(0.93f, 0.9f, 0.82f), roof = new Color(0.4f, 0.24f, 0.18f), door = new Color(0.2f, 0.32f, 0.2f) },
            new Palette { siding = new Color(0.9f, 0.9f, 0.88f), trim = new Color(0.28f, 0.3f, 0.32f), roof = new Color(0.18f, 0.19f, 0.21f), door = new Color(0.55f, 0.1f, 0.12f) },
            new Palette { siding = new Color(0.66f, 0.74f, 0.6f), trim = new Color(0.95f, 0.95f, 0.92f), roof = new Color(0.36f, 0.3f, 0.26f), door = new Color(0.5f, 0.35f, 0.15f) },
            new Palette { siding = new Color(0.7f, 0.42f, 0.34f), trim = new Color(0.94f, 0.92f, 0.86f), roof = new Color(0.27f, 0.27f, 0.28f), door = new Color(0.12f, 0.12f, 0.14f) },
        };
        static readonly Color glass = new Color(0.22f, 0.32f, 0.42f), foundation = new Color(0.55f, 0.54f, 0.51f), brick = new Color(0.55f, 0.27f, 0.2f);

        static readonly Dictionary<Color, Material> mats = new();
        static Material Mat(Color c, float smooth = 0.25f)
        {
            if (mats.TryGetValue(c, out var m) && m != null) return m;
            m = new Material(Shader.Find("HDRP/Lit")) { name = "House paint" };
            m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            try { UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(m); } catch { }
            mats[c] = m;
            return m;
        }

        /// <summary>Flat-shaded mesh with a submesh per material.</summary>
        class Builder
        {
            readonly List<Vector3> v = new();
            readonly List<List<int>> t = new();
            readonly List<Material> m = new();
            List<int> Sub(Material mat) { int i = m.IndexOf(mat); if (i < 0) { m.Add(mat); t.Add(new List<int>()); i = m.Count - 1; } return t[i]; }

            public void Quad(Material mat, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                var l = Sub(mat); int o = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                l.Add(o); l.Add(o + 1); l.Add(o + 2); l.Add(o); l.Add(o + 2); l.Add(o + 3);
            }

            public void Tri2(Material mat, Vector3 a, Vector3 b, Vector3 c)
            {
                var l = Sub(mat); int o = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(a); v.Add(c); v.Add(b);
                l.Add(o); l.Add(o + 1); l.Add(o + 2); l.Add(o + 3); l.Add(o + 4); l.Add(o + 5);
            }

            public void Box(Material mat, Vector3 c, Vector3 size, Quaternion r)
            {
                Vector3 x = r * new Vector3(size.x * 0.5f, 0, 0), y = r * new Vector3(0, size.y * 0.5f, 0), z = r * new Vector3(0, 0, size.z * 0.5f);
                void F(Vector3 n, Vector3 u, Vector3 w) { Vector3 p = c + n; Quad(mat, p - u - w, p - u + w, p + u + w, p + u - w); }
                F(y, x, z); F(-y, z, x); F(x, z, y); F(-x, y, z); F(z, y, x); F(-z, x, y);
            }

            public void Attach(Transform parent)
            {
                if (v.Count == 0) return;
                var mesh = new Mesh { name = parent.name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.SetVertices(v);
                mesh.subMeshCount = t.Count;
                for (int i = 0; i < t.Count; i++) mesh.SetTriangles(t[i], i);
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                parent.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                parent.gameObject.AddComponent<MeshRenderer>().sharedMaterials = m.ToArray();
            }
        }

        /// <summary>Build the house for a plan (its origin the middle of the plan, at floor level).</summary>
        public static GameObject Build(HousePlan plan)
        {
            var pal = palettes[Mathf.Abs(plan.order) % palettes.Length];
            Material siding = Mat(pal.siding, 0.2f), trim = Mat(pal.trim, 0.35f), roofM = Mat(pal.roof, 0.15f), doorM = Mat(pal.door, 0.4f),
                glassM = Mat(glass, 0.85f), baseM = Mat(foundation, 0.1f), brickM = Mat(brick, 0.1f);
            var root = new GameObject($"House: {plan.title}");
            var walls = new GameObject("Walls").transform; walls.SetParent(root.transform, false);
            float H = Mathf.Max(1, plan.floors) * Storey;
            var roof = new GameObject("Roof").transform; roof.SetParent(root.transform, false); roof.localPosition = new Vector3(0f, H, 0f);

            var o = plan.outline;
            float signed = 0f;
            for (int i = 0; i < o.Length; i++) { var p = o[i]; var q = o[(i + 1) % o.Length]; signed += p.x * q.y - q.x * p.y; }
            float turn = signed >= 0f ? 1f : -1f; // (outward is to the right of each edge for a counter-clockwise outline)

            // the front door: on the longest wall facing the front (-z)
            int doorEdge = -1; float doorLen = 0f;
            for (int i = 0; i < o.Length; i++)
            {
                Vector2 a = o[i], b = o[(i + 1) % o.Length];
                Vector2 n = new Vector2(b.y - a.y, -(b.x - a.x)).normalized * turn;
                float len = Vector2.Distance(a, b);
                if (n.y < -0.9f && len > doorLen + 0.01f) { doorLen = len; doorEdge = i; }
            }

            var wb = new Builder();
            const float T = 0.22f;
            for (int i = 0; i < o.Length; i++)
            {
                Vector2 a = o[i], b = o[(i + 1) % o.Length];
                float len = Vector2.Distance(a, b);
                if (len < 0.05f) continue;
                Vector2 d2 = (b - a) / len, n2 = new Vector2(d2.y, -d2.x) * turn;
                Vector3 d = new Vector3(d2.x, 0f, d2.y), n = new Vector3(n2.x, 0f, n2.y);
                Vector3 mid = new Vector3((a.x + b.x) * 0.5f, 0f, (a.y + b.y) * 0.5f);
                var rot = Quaternion.LookRotation(d, Vector3.up);
                // the wall (outer face on the outline), its foundation band, the trim at the eaves and between floors
                wb.Box(siding, mid - n * (T * 0.5f) + Vector3.up * (H * 0.5f), new Vector3(T, H, len + T), rot);
                var col = new GameObject("Wall").AddComponent<BoxCollider>();
                col.transform.SetParent(walls, false);
                col.transform.localPosition = mid - n * (T * 0.5f) + Vector3.up * (H * 0.5f);
                col.transform.localRotation = rot;
                col.size = new Vector3(T, H, len + T);
                wb.Box(baseM, mid - n * (T * 0.5f - 0.03f) + Vector3.up * 0.18f, new Vector3(T + 0.06f, 0.36f, len + T + 0.06f), rot);
                wb.Box(trim, mid - n * (T * 0.5f - 0.03f) + Vector3.up * (H - 0.12f), new Vector3(T + 0.06f, 0.24f, len + T + 0.06f), rot);
                for (int f = 1; f < plan.floors; f++)
                    wb.Box(trim, mid - n * (T * 0.5f - 0.025f) + Vector3.up * (f * Storey), new Vector3(T + 0.05f, 0.12f, len + T + 0.05f), rot);
                // windows along each floor (and the door, on the front)
                float doorAt = i == doorEdge ? len * 0.5f : -99f;
                if (i == doorEdge)
                {
                    Vector3 dp = new Vector3(a.x, 0f, a.y) + d * doorAt;
                    wb.Box(trim, dp + n * 0.025f + Vector3.up * 1.1f, new Vector3(0.05f, 2.3f, 1.25f), rot);
                    wb.Box(doorM, dp + n * 0.05f + Vector3.up * 1.05f, new Vector3(0.05f, 2.1f, 1.0f), rot);
                    wb.Box(Mat(new Color(0.85f, 0.75f, 0.3f), 0.8f), dp + n * 0.09f + d * 0.35f + Vector3.up * 1.0f, new Vector3(0.06f, 0.06f, 0.06f), rot); // handle
                    wb.Box(baseM, dp + n * 0.45f + Vector3.up * 0.08f, new Vector3(0.9f, 0.16f, 1.7f), rot); // step
                    wb.Box(trim, dp + n * 0.6f + Vector3.up * 2.45f, new Vector3(1.2f, 0.08f, 1.9f), rot);   // door canopy
                }
                int nWin = Mathf.FloorToInt((len - 0.8f) / 2.6f);
                for (int f = 0; f < plan.floors; f++)
                    for (int k = 0; k < nWin; k++)
                    {
                        float at = (len - (nWin - 1) * 2.6f) * 0.5f + k * 2.6f;
                        if (f == 0 && Mathf.Abs(at - doorAt) < 1.4f) continue;
                        Vector3 wp = new Vector3(a.x, 0f, a.y) + d * at + Vector3.up * (f * Storey + 1.55f);
                        wb.Box(trim, wp + n * 0.02f, new Vector3(0.04f, 1.35f, 1.15f), rot);
                        wb.Box(glassM, wp + n * 0.04f, new Vector3(0.03f, 1.15f, 0.95f), rot);
                        wb.Box(trim, wp + n * 0.06f - Vector3.up * 0.66f, new Vector3(0.12f, 0.06f, 1.25f), rot); // sill
                    }
            }
            wb.Attach(walls);

            // the roof: a gable over each rectangle of the footprint, along its longer side
            var rb = new Builder();
            const float over = 0.45f, pitch = 30f;
            float tan = Mathf.Tan(pitch * Mathf.Deg2Rad);
            var rects = Rectangles(plan);
            for (int ri = 0; ri < rects.Count; ri++)
            {
                var r = rects[ri];
                bool alongX = r.width >= r.height;
                float half = (alongX ? r.height : r.width) * 0.5f, len = alongX ? r.width : r.height;
                float rise = half * tan;
                Vector3 c = new Vector3(r.center.x, 0f, r.center.y);
                Vector3 ax = alongX ? Vector3.right : Vector3.forward, ac = alongX ? Vector3.forward : Vector3.right;
                float slope = (half + over) / Mathf.Cos(pitch * Mathf.Deg2Rad);
                foreach (float sd in new[] { -1f, 1f })
                {
                    // each side of the roof: from the eave (over the wall) up to the ridge
                    Vector3 eave = c + ac * sd * (half + over) + Vector3.up * (-over * tan);
                    Vector3 ridge = c + Vector3.up * rise;
                    Vector3 mid = (eave + ridge) * 0.5f;
                    Vector3 up = (ridge - eave).normalized;
                    Vector3 nrm = Vector3.Cross(ax, up).normalized;
                    if (nrm.y < 0f) nrm = -nrm;
                    // (the panel's local z along the ridge, y out of the roof, x down the slope)
                    rb.Box(roofM, mid + nrm * 0.06f, new Vector3(slope, 0.12f, len + over * 2f), Quaternion.LookRotation(ax, nrm));
                }
                rb.Box(trim, c + Vector3.up * (rise + 0.05f), alongX ? new Vector3(len + over * 2f, 0.1f, 0.18f) : new Vector3(0.18f, 0.1f, len + over * 2f), Quaternion.identity); // ridge cap
                // the gable ends, in the siding
                foreach (float e in new[] { -1f, 1f })
                {
                    Vector3 end = c + ax * e * (len * 0.5f);
                    rb.Tri2(siding, end - ac * half, end + ac * half, end + Vector3.up * rise);
                }
                if (ri == 0)
                {
                    Vector3 ch = c + ax * (len * 0.25f) + ac * (half * 0.35f);
                    float chH = rise + 0.9f;
                    rb.Box(brickM, ch + Vector3.up * (chH * 0.5f - 0.3f), new Vector3(0.7f, chH + 0.6f, 0.7f), Quaternion.identity);
                    rb.Box(trim, ch + Vector3.up * (chH - 0.25f), new Vector3(0.8f, 0.1f, 0.8f), Quaternion.identity);
                }
            }
            rb.Attach(roof);
            return root;
        }

        /// <summary>The footprint split into rectangles (strips across x, merged where they match).</summary>
        public static List<Rect> Rectangles(HousePlan plan)
        {
            var xs = new SortedSet<float>(); var zs = new SortedSet<float>();
            foreach (var p in plan.outline) { xs.Add(Mathf.Round(p.x * 100f) / 100f); zs.Add(Mathf.Round(p.y * 100f) / 100f); }
            var xl = new List<float>(xs); var zl = new List<float>(zs);
            var strips = new List<(float x0, float x1, List<Vector2> iv)>();
            for (int i = 0; i + 1 < xl.Count; i++)
            {
                float mx = (xl[i] + xl[i + 1]) * 0.5f;
                var iv = new List<Vector2>();
                for (int j = 0; j + 1 < zl.Count; j++)
                {
                    if (!plan.Inside(new Vector2(mx, (zl[j] + zl[j + 1]) * 0.5f))) continue;
                    if (iv.Count > 0 && Mathf.Approximately(iv[^1].y, zl[j])) iv[^1] = new Vector2(iv[^1].x, zl[j + 1]);
                    else iv.Add(new Vector2(zl[j], zl[j + 1]));
                }
                if (strips.Count > 0 && Same(strips[^1].iv, iv)) strips[^1] = (strips[^1].x0, xl[i + 1], iv);
                else strips.Add((xl[i], xl[i + 1], iv));
            }
            var rects = new List<Rect>();
            foreach (var s in strips) foreach (var iv in s.iv) rects.Add(Rect.MinMaxRect(s.x0, iv.x, s.x1, iv.y));
            rects.Sort((a, b) => (b.width * b.height).CompareTo(a.width * a.height)); // the biggest first (it gets the chimney)
            return rects;
        }

        static bool Same(List<Vector2> a, List<Vector2> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (!Mathf.Approximately(a[i].x, b[i].x) || !Mathf.Approximately(a[i].y, b[i].y)) return false;
            return true;
        }
    }
}
