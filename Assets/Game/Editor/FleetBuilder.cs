using System;
using System.Collections.Generic;
using FPSBase.Mining;
using FPSBase.Vehicles;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FPSBase.EditorTools
{
    /// <summary>
    /// The heavy fleet: five tracked excavators (5 t to 90 t) and two high-drive dozers, built procedurally from
    /// the dimensions in the user's fleet visualizer (heavy_equipment_fleet_3d_visualizer.html), ported to Unity:
    /// real track frames and shoes, swing house with counterweight, cab, catwalks, kinked boom, stick, backhoe bucket
    /// with a working four-bar linkage, and hydraulic cylinders that follow the joints. Dozers get push arms,
    /// a U/SU blade with lift and tilt rams, and a parallelogram ripper.
    ///
    /// Source coordinates are X forward, Y up, Z right; Unity is Z forward, Y up, X right. F() maps a source
    /// point, FS() a source box size, RZ() a source rotation about its Z (= our X, sign flipped for handedness).
    /// Each rigid part of a machine is baked into one flat-shaded mesh with one submesh per colour.
    /// </summary>
    public static partial class ProjectBootstrap
    {
        // ------------------------------------------------------------------ configs (metres, from the visualizer)

        class ExcavatorCfg
        {
            public string id, name; public Color primary, accent;
            public float trackLength, trackGauge, trackHeight, shoeWidth;
            public float houseLength, houseWidth, houseHeight, cabLength, cabWidth, cabHeight;
            public float boomLength, boomDepth, stickLength, stickDepth, bucketRadius, bucketWidth;
            public bool dualBoomCylinders, hasDozerBlade, catwalks, miningShroud;
            public float capacity, tonnes, fuel, workBurn, drive;
        }

        class DozerCfg
        {
            public string id, name; public Color primary, accent;
            public float trackLength, trackGauge, trackHeight, sprocketElevation, shoeWidth;
            public float hullLength, hullWidth, hullHeight, cabLength, cabWidth, cabHeight;
            public float pushArmLength, bladeWidth, bladeHeight, bladeCurvature, ripperScale;
            public int ripperShanks;
            public float bladeCapacity, tonnes, fuel, workBurn, drive;
        }

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

        static readonly ExcavatorCfg[] ExcavatorFleet =
        {
            new ExcavatorCfg { id = "Exc05", name = "Compact Excavator 5 t", primary = Hex(0xf59e0b), accent = Hex(0x1e293b),
                trackLength = 2.45f, trackGauge = 1.56f, trackHeight = 0.56f, shoeWidth = 0.40f,
                houseLength = 2.15f, houseWidth = 1.88f, houseHeight = 0.88f, cabLength = 1.32f, cabWidth = 0.94f, cabHeight = 1.72f,
                boomLength = 2.85f, boomDepth = 0.34f, stickLength = 1.55f, stickDepth = 0.24f, bucketRadius = 0.36f, bucketWidth = 0.62f,
                dualBoomCylinders = false, hasDozerBlade = true, catwalks = false, miningShroud = false,
                capacity = 0.18f, tonnes = 5.2f, fuel = 75f, workBurn = 7f, drive = 1.9f },
            new ExcavatorCfg { id = "Exc20", name = "Excavator 20 t", primary = Hex(0xf59e0b), accent = Hex(0x1e293b),
                trackLength = 4.18f, trackGauge = 2.38f, trackHeight = 0.92f, shoeWidth = 0.60f,
                houseLength = 3.75f, houseWidth = 2.78f, houseHeight = 1.24f, cabLength = 1.75f, cabWidth = 1.04f, cabHeight = 1.88f,
                boomLength = 5.45f, boomDepth = 0.58f, stickLength = 2.85f, stickDepth = 0.42f, bucketRadius = 0.74f, bucketWidth = 1.24f,
                dualBoomCylinders = true, hasDozerBlade = true, capacity = 1.35f, tonnes = 21.4f, fuel = 340f, workBurn = 20f, drive = 1.6f },
            new ExcavatorCfg { id = "Exc50", name = "Heavy Excavator 50 t", primary = Hex(0xeab308), accent = Hex(0x0f172a),
                trackLength = 5.35f, trackGauge = 2.95f, trackHeight = 1.18f, shoeWidth = 0.75f,
                houseLength = 4.85f, houseWidth = 3.35f, houseHeight = 1.58f, cabLength = 1.92f, cabWidth = 1.12f, cabHeight = 1.98f,
                boomLength = 6.75f, boomDepth = 0.76f, stickLength = 3.35f, stickDepth = 0.54f, bucketRadius = 1.06f, bucketWidth = 1.78f,
                dualBoomCylinders = true, catwalks = true, capacity = 3.6f, tonnes = 51.8f, fuel = 700f, workBurn = 42f, drive = 1.4f },
            new ExcavatorCfg { id = "Exc70", name = "Mass Excavator 70 t", primary = Hex(0xeab308), accent = Hex(0x0f172a),
                trackLength = 5.88f, trackGauge = 3.35f, trackHeight = 1.32f, shoeWidth = 0.85f,
                houseLength = 5.45f, houseWidth = 3.78f, houseHeight = 1.82f, cabLength = 2.02f, cabWidth = 1.18f, cabHeight = 2.06f,
                boomLength = 7.45f, boomDepth = 0.88f, stickLength = 3.55f, stickDepth = 0.62f, bucketRadius = 1.28f, bucketWidth = 2.18f,
                dualBoomCylinders = true, catwalks = true, miningShroud = true, capacity = 5.4f, tonnes = 72.5f, fuel = 950f, workBurn = 56f, drive = 1.3f },
            new ExcavatorCfg { id = "Exc90", name = "Mining Excavator 90 t", primary = Hex(0xf59e0b), accent = Hex(0x111827),
                trackLength = 6.45f, trackGauge = 3.68f, trackHeight = 1.46f, shoeWidth = 0.92f,
                houseLength = 6.05f, houseWidth = 4.18f, houseHeight = 2.06f, cabLength = 2.12f, cabWidth = 1.24f, cabHeight = 2.14f,
                boomLength = 8.15f, boomDepth = 0.98f, stickLength = 3.85f, stickDepth = 0.68f, bucketRadius = 1.52f, bucketWidth = 2.55f,
                dualBoomCylinders = true, catwalks = true, miningShroud = true, capacity = 7.8f, tonnes = 94.5f, fuel = 1300f, workBurn = 72f, drive = 1.2f },
            new ExcavatorCfg { id = "Exc150", name = "Mining Shovel 150 t", primary = Hex(0xeab308), accent = Hex(0x111827),
                trackLength = 7.65f, trackGauge = 4.45f, trackHeight = 1.78f, shoeWidth = 1.12f,
                houseLength = 7.3f, houseWidth = 5.05f, houseHeight = 2.5f, cabLength = 2.3f, cabWidth = 1.38f, cabHeight = 2.25f,
                boomLength = 9.4f, boomDepth = 1.2f, stickLength = 4.35f, stickDepth = 0.82f, bucketRadius = 1.9f, bucketWidth = 3.15f,
                dualBoomCylinders = true, catwalks = true, miningShroud = true, capacity = 12.5f, tonnes = 152f, fuel = 2200f, workBurn = 110f, drive = 1.0f },
        };

        static readonly DozerCfg[] DozerFleet =
        {
            new DozerCfg { id = "Dozer22", name = "Medium Dozer 22 t", primary = Hex(0xf59e0b), accent = Hex(0x1e293b),
                trackLength = 3.35f, trackGauge = 1.96f, trackHeight = 1.18f, sprocketElevation = 1.28f, shoeWidth = 0.56f,
                hullLength = 3.85f, hullWidth = 1.54f, hullHeight = 1.32f, cabLength = 1.58f, cabWidth = 1.38f, cabHeight = 1.52f,
                pushArmLength = 2.55f, bladeWidth = 3.45f, bladeHeight = 1.38f, bladeCurvature = 0.68f, ripperScale = 0.78f, ripperShanks = 3,
                bladeCapacity = 5.6f, tonnes = 22f, fuel = 400f, workBurn = 24f, drive = 2.9f },
            new DozerCfg { id = "Dozer66", name = "Mining Dozer 66.5 t", primary = Hex(0xeab308), accent = Hex(0x0f172a),
                trackLength = 4.55f, trackGauge = 2.55f, trackHeight = 1.68f, sprocketElevation = 1.88f, shoeWidth = 0.72f,
                hullLength = 5.25f, hullWidth = 2.15f, hullHeight = 1.86f, cabLength = 1.85f, cabWidth = 1.62f, cabHeight = 1.74f,
                pushArmLength = 3.45f, bladeWidth = 4.95f, bladeHeight = 2.08f, bladeCurvature = 0.96f, ripperScale = 1.15f, ripperShanks = 1,
                bladeCapacity = 18.5f, tonnes = 66.5f, fuel = 1100f, workBurn = 65f, drive = 2.7f },
        };

        // ------------------------------------------------------------------ materials

        class FleetMats
        {
            public Material under, dark, shoe, wear, pin, chrome, glass, amber, rail, dirt;
            readonly Dictionary<Color, Material> paints = new();
            public Material Paint(Color c)
            {
                if (!paints.TryGetValue(c, out var m))
                    paints[c] = m = Flat("Fleet_Paint_" + ColorUtility.ToHtmlStringRGB(c), c, 0.35f);
                return m;
            }
        }

        static FleetMats BuildFleetMats(Material dirt) => new FleetMats
        {
            under = Flat("Fleet_Undercarriage", new Color(0.17f, 0.17f, 0.19f), 0.2f),
            dark = Flat("Fleet_DarkSteel", new Color(0.22f, 0.23f, 0.25f), 0.3f, 0.3f),
            shoe = Flat("Fleet_TrackShoe", new Color(0.11f, 0.11f, 0.12f), 0.15f),
            wear = Flat("Fleet_WearPlate", new Color(0.46f, 0.47f, 0.49f), 0.35f, 0.4f),
            pin = Flat("Fleet_PinSteel", new Color(0.7f, 0.71f, 0.73f), 0.5f, 0.7f),
            chrome = Flat("Fleet_ChromeRod", new Color(0.86f, 0.87f, 0.9f), 0.8f, 0.9f),
            glass = CabGlass("Fleet_CabGlass", new Color(0.6f, 0.74f, 0.8f), 0.25f),
            amber = Flat("Fleet_Amber", new Color(1f, 0.55f, 0.1f), 0.4f),
            rail = Flat("Fleet_Handrail", new Color(0.98f, 0.82f, 0.12f), 0.3f),
            dirt = dirt,
        };

        // ------------------------------------------------------------------ geometry kit

        static Vector3 F(float x, float y, float z) => new Vector3(z, y, x);
        static Vector3 FS(float x, float y, float z) => new Vector3(z, y, x);
        static Quaternion RZ(float rad) => Quaternion.Euler(-rad * Mathf.Rad2Deg, 0f, 0f);
        static readonly Quaternion AxisLat = Quaternion.Euler(0f, 0f, 90f); // cylinder axis across the machine
        static readonly Quaternion AxisFwd = Quaternion.Euler(90f, 0f, 0f); // cylinder axis along +Z

        /// <summary>Collects flat-shaded triangles for one rigid part, one submesh per material.</summary>
        class Geo
        {
            readonly List<Vector3> v = new();
            readonly List<List<int>> t = new();
            readonly List<Material> mats = new();

            List<int> Sub(Material m)
            {
                int i = mats.IndexOf(m);
                if (i < 0) { mats.Add(m); t.Add(new List<int>()); i = mats.Count - 1; }
                return t[i];
            }

            public void Tri(Material m, Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) (b, c) = (c, b);
                var l = Sub(m); int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c);
                l.Add(i); l.Add(i + 1); l.Add(i + 2);
            }

            public void Quad(Material m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
            {
                Tri(m, a, b, c, outward); Tri(m, a, c, d, outward);
            }

            public void Box(Material m, Vector3 c, Vector3 size, Quaternion r)
            {
                Vector3 h = size * 0.5f;
                Vector3 P(float x, float y, float z) => c + r * new Vector3(x * h.x, y * h.y, z * h.z);
                Quad(m, P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), r * Vector3.right);
                Quad(m, P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), P(-1, -1, 1), r * Vector3.left);
                Quad(m, P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1), r * Vector3.up);
                Quad(m, P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), r * Vector3.down);
                Quad(m, P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), r * Vector3.forward);
                Quad(m, P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), r * Vector3.back);
            }
            public void Box(Material m, Vector3 c, Vector3 size) => Box(m, c, size, Quaternion.identity);

            /// <summary>Faceted cylinder / frustum / cone along r*up, centred on c. rBottom at -h/2, rTop at +h/2.</summary>
            public void Cyl(Material m, Vector3 c, Quaternion r, float rBottom, float rTop, float h, int sides = 10)
            {
                Vector3 ax = r * Vector3.up;
                var lo = new Vector3[sides]; var hi = new Vector3[sides]; var dirs = new Vector3[sides];
                for (int i = 0; i < sides; i++)
                {
                    float a = (i + 0.5f) * Mathf.PI * 2f / sides;
                    dirs[i] = r * new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    lo[i] = c - ax * h * 0.5f + dirs[i] * rBottom;
                    hi[i] = c + ax * h * 0.5f + dirs[i] * rTop;
                }
                Vector3 cb = c - ax * h * 0.5f, ct = c + ax * h * 0.5f;
                for (int i = 0; i < sides; i++)
                {
                    int j = (i + 1) % sides;
                    Vector3 o = (dirs[i] + dirs[j]).normalized;
                    if (rTop <= 1e-5f) Tri(m, lo[i], lo[j], ct, o);
                    else if (rBottom <= 1e-5f) Tri(m, hi[i], hi[j], cb, o);
                    else Quad(m, lo[i], lo[j], hi[j], hi[i], o);
                    if (rBottom > 1e-5f) Tri(m, cb, lo[i], lo[j], -ax);
                    if (rTop > 1e-5f) Tri(m, ct, hi[i], hi[j], ax);
                }
            }

            /// <summary>Extrude a 2D outline between depths d0 and d1; `map(point, depth)` places it in this part's space.</summary>
            public void Prism(Material m, List<Vector2> poly, Func<Vector2, float, Vector3> map, float d0, float d1)
            {
                int n = poly.Count;
                float area = 0f;
                for (int i = 0; i < n; i++) { var p = poly[i]; var q = poly[(i + 1) % n]; area += p.x * q.y - q.x * p.y; }
                bool ccw = area > 0f;
                Vector3 up = map(poly[0], d1) - map(poly[0], d0);
                foreach (var tri in EarClip(poly, ccw))
                {
                    Tri(m, map(poly[tri.x], d1), map(poly[tri.y], d1), map(poly[tri.z], d1), up);
                    Tri(m, map(poly[tri.x], d0), map(poly[tri.y], d0), map(poly[tri.z], d0), -up);
                }
                float dm = (d0 + d1) * 0.5f;
                for (int i = 0; i < n; i++)
                {
                    Vector2 p = poly[i], q = poly[(i + 1) % n], e = q - p;
                    Vector2 nrm = ccw ? new Vector2(e.y, -e.x) : new Vector2(-e.y, e.x);
                    Vector3 o = map(p + nrm.normalized * 0.01f, dm) - map(p, dm);
                    Quad(m, map(p, d0), map(q, d0), map(q, d1), map(p, d1), o);
                }
            }

            static List<Vector3Int> EarClip(List<Vector2> poly, bool ccw)
            {
                var res = new List<Vector3Int>();
                var idx = new List<int>();
                for (int i = 0; i < poly.Count; i++) idx.Add(i);
                float sgn = ccw ? 1f : -1f;
                int guard = 0;
                while (idx.Count > 3 && guard++ < 5000)
                {
                    bool clipped = false;
                    for (int k = 0; k < idx.Count; k++)
                    {
                        int ia = idx[(k + idx.Count - 1) % idx.Count], ib = idx[k], ic = idx[(k + 1) % idx.Count];
                        Vector2 a = poly[ia], b = poly[ib], c = poly[ic];
                        float cr = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                        if (cr * sgn <= 1e-9f) continue; // reflex or flat
                        bool inside = false;
                        foreach (int o in idx)
                        {
                            if (o == ia || o == ib || o == ic) continue;
                            if (InTri(poly[o], a, b, c)) { inside = true; break; }
                        }
                        if (inside) continue;
                        res.Add(new Vector3Int(ia, ib, ic));
                        idx.RemoveAt(k);
                        clipped = true;
                        break;
                    }
                    if (!clipped) break;
                }
                for (int k = 1; k + 1 < idx.Count; k++) res.Add(new Vector3Int(idx[0], idx[k], idx[k + 1]));
                return res;
            }

            static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
            {
                float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
                float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
                float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
                bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
                return !(neg && pos);
            }

            /// <summary>Bake into a mesh asset on a "Mesh" child of `part`.</summary>
            public void Attach(Transform part, string assetName)
            {
                if (v.Count == 0) return;
                var mesh = new Mesh { name = assetName };
                if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(v);
                mesh.subMeshCount = t.Count;
                for (int i = 0; i < t.Count; i++) mesh.SetTriangles(t[i], i);
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                EnsureFolder(Gen + "/Meshes");
                string path = $"{Gen}/Meshes/{assetName}.asset";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(mesh, path);
                var go = new GameObject("Mesh");
                go.transform.SetParent(part, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = mats.ToArray();
            }
        }

        /// <summary>A collider on a working tool (boom, stick, bucket, blade): pushes vehicles, passes into the dirt.</summary>
        static void ToolBox(Transform parent, string name, Vector3 center, Vector3 size, Quaternion rot)
        {
            var go = new GameObject("Tool " + name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localRotation = rot;
            go.AddComponent<BoxCollider>().size = size;
        }

        static void Pin(Geo g, FleetMats fm, Vector3 at, float r, float len)
        {
            g.Cyl(fm.pin, at, AxisLat, r, r, len, 8);
            g.Cyl(fm.dark, at + Vector3.right * len * 0.5f, AxisLat, r * 1.35f, r * 1.35f, r * 0.35f, 8);
            g.Cyl(fm.dark, at - Vector3.right * len * 0.5f, AxisLat, r * 1.35f, r * 1.35f, r * 0.35f, 8);
        }

        static void Quadratic(List<Vector2> l, Vector2 ctrl, Vector2 end, int steps = 4)
        {
            Vector2 start = l[^1];
            for (int i = 1; i <= steps; i++)
            {
                float u = i / (float)steps;
                l.Add((1 - u) * (1 - u) * start + 2 * (1 - u) * u * ctrl + u * u * end);
            }
        }

        static Transform Node(Transform parent, string name, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }

        /// <summary>A pending hydraulic ram: the pins are known, the stroke is measured after the rig is built.</summary>
        class Ram
        {
            public string name; public Transform parent, a, b; public float barrelR, rodR;
            public float min = float.MaxValue, max;
            public void Measure() { float d = Vector3.Distance(a.position, b.position); min = Mathf.Min(min, d); max = Mathf.Max(max, d); }
        }

        static void BuildRam(Ram r, FleetMats fm, Material barrelMat, string assetPrefix)
        {
            float rr = r.barrelR;
            float safeMin = Mathf.Max(rr * 2.4f, r.min), safeMax = Mathf.Max(safeMin + 0.05f, r.max);
            float barrelLen = Mathf.Max(rr * 2.2f, safeMin * 0.8f);
            float rodLen = Mathf.Max(barrelLen * 0.9f, safeMax - barrelLen + rr * 2.4f);

            var root = new GameObject(r.name).transform;
            root.SetParent(r.parent, false);
            var barrel = Node(root, "Barrel", Vector3.zero);
            var rod = Node(root, "Rod", Vector3.zero);

            var gb = new Geo();
            gb.Cyl(barrelMat, Vector3.zero, AxisLat, rr * 1.05f, rr * 1.05f, rr * 1.65f, 10);
            gb.Cyl(fm.dark, new Vector3(0f, 0f, rr * 0.65f), AxisFwd, rr * 1.06f, rr * 1.06f, rr * 0.7f, 10);
            gb.Cyl(barrelMat, new Vector3(0f, 0f, barrelLen * 0.5f), AxisFwd, rr, rr, barrelLen, 10);
            gb.Cyl(fm.dark, new Vector3(0f, 0f, barrelLen), AxisFwd, rr * 1.18f, rr * 1.18f, rr * 1.25f, 10);
            gb.Box(fm.dark, new Vector3(0f, rr * 1.05f, rr * 1.5f), new Vector3(rr * 0.85f, rr * 0.75f, rr * 1.2f));
            gb.Attach(barrel, assetPrefix + "_" + r.name.Replace(' ', '_') + "_Barrel");

            var gr = new Geo();
            gr.Cyl(fm.dark, Vector3.zero, AxisLat, r.rodR * 1.48f, r.rodR * 1.48f, rr * 1.55f, 10);
            gr.Cyl(fm.chrome, new Vector3(0f, 0f, rodLen * 0.5f), AxisFwd, r.rodR, r.rodR, rodLen, 10);
            gr.Attach(rod, assetPrefix + "_" + r.name.Replace(' ', '_') + "_Rod");

            var hc = root.gameObject.AddComponent<HydraulicCylinder>();
            hc.a = r.a; hc.b = r.b; hc.barrel = barrel; hc.rod = rod;
            hc.Apply();
        }

        // ------------------------------------------------------------------ excavator

        static GameObject BuildFleetExcavator(ExcavatorCfg c, FleetMats fm)
        {
            var paint = fm.Paint(c.primary);
            var accent = fm.Paint(c.accent);
            string id = "Fleet_" + c.id;
            var root = new GameObject(c.name);
            var rams = new List<Ram>();

            // --- A. undercarriage & track frames
            var gU = new Geo();
            float TL = c.trackLength, TG = c.trackGauge, TH = c.trackHeight, SW = c.shoeWidth;
            gU.Box(fm.under, F(0f, TH * 0.52f, 0f), FS(TL * 0.42f, TH * 0.42f, TG * 0.85f));
            float slewY = TH * 0.86f;
            gU.Cyl(fm.dark, F(0f, slewY, 0f), Quaternion.identity, TG * 0.36f, TG * 0.34f, TH * 0.24f, 16);
            float wheelR = TH * 0.44f, span = TL - wheelR * 2f;
            int shoeCount = Mathf.Max(22, Mathf.RoundToInt(TL * 11f));
            foreach (int side in new[] { -1, 1 })
            {
                float zs = side * TG * 0.5f;
                gU.Box(fm.under, F(0f, wheelR + 0.04f, zs), FS(span * 1.05f, wheelR * 0.92f, SW * 0.62f));
                gU.Cyl(fm.dark, F(-span * 0.5f, wheelR + 0.04f, zs), AxisLat, wheelR * 0.88f, wheelR * 0.88f, SW * 0.68f, 12);
                gU.Cyl(paint, F(span * 0.5f, wheelR + 0.04f, zs), AxisLat, wheelR * 0.88f, wheelR * 0.88f, SW * 0.68f, 12);
                int rollers = TL > 5f ? 7 : 5;
                for (int r = 0; r < rollers; r++)
                {
                    float rx = -span * 0.4f + r / (float)(rollers - 1) * span * 0.8f;
                    gU.Cyl(fm.dark, F(rx, wheelR * 0.32f, zs), AxisLat, wheelR * 0.24f, wheelR * 0.24f, SW * 0.72f, 8);
                }
                float perim = 2f * span + 2f * Mathf.PI * wheelR, pitch = perim / shoeCount;
                float shoeT = Mathf.Max(0.03f, TH * 0.065f);
                // the shoes are live: they run round the belt as the machine moves (TrackAnimator)
                TrackAnimator.AddBelt(root, fm.shoe, zs, SW, shoeT, pitch,
                    new Vector3(-span * 0.5f, wheelR + 0.04f, wheelR), new Vector3(span * 0.5f, wheelR + 0.04f, wheelR));
            }

            // --- optional backfill blade (5 t)
            Transform bladePivot = null, bladeEdge = null;
            if (c.hasDozerBlade)
            {
                bladePivot = Node(root.transform, "BladePivot", F(TL * 0.25f, TH * 0.45f, 0f));
                var gB = new Geo();
                float armLen = TL * 0.48f, bladeW = TG + SW + 0.08f, bladeH = TH * 0.72f;
                foreach (int side in new[] { -1, 1 })
                {
                    gB.Box(fm.dark, F(armLen * 0.5f, -0.08f, side * TG * 0.42f), FS(armLen, 0.11f, 0.09f));
                    Pin(gB, fm, F(0f, -0.08f, side * TG * 0.42f), 0.026f, 0.14f);
                }
                gB.Box(paint, F(armLen + 0.05f, -0.05f, 0f), FS(0.12f, bladeH, bladeW));
                gB.Box(fm.wear, F(armLen + 0.06f, -0.05f - bladeH * 0.5f, 0f), FS(0.15f, 0.07f, bladeW + 0.02f));
                gB.Box(fm.dark, F(armLen + 0.02f, 0.10f, 0f), FS(0.12f, 0.14f, 0.10f));
                Pin(gB, fm, F(armLen + 0.02f, 0.14f, 0f), 0.028f, 0.14f);
                gB.Attach(bladePivot, id + "_Blade");
                ToolBox(bladePivot, "Blade", F(armLen + 0.05f, -0.05f, 0f), FS(0.14f, bladeH, bladeW), Quaternion.identity);
                bladeEdge = Node(bladePivot, "BladeEdge", F(armLen + 0.12f, -0.05f - bladeH * 0.5f - 0.03f, 0f));
                gU.Box(fm.dark, F(TL * 0.34f, TH * 0.72f, 0f), FS(0.14f, 0.14f, 0.10f));
                Pin(gU, fm, F(TL * 0.36f, TH * 0.76f, 0f), 0.032f, 0.16f);
                var ra = Node(root.transform, "BladeRamBase", F(TL * 0.36f, TH * 0.76f, 0f));
                var rb2 = Node(bladePivot, "BladeRamRod", F(armLen + 0.02f, 0.14f, 0f));
                rams.Add(new Ram { name = "Blade Ram", parent = root.transform, a = ra, b = rb2, barrelR = 0.048f, rodR = 0.028f });
            }
            gU.Attach(root.transform, id + "_Undercarriage");

            // --- B. upper structure
            float HL = c.houseLength, HW = c.houseWidth, HH = c.houseHeight;
            var upper = Node(root.transform, "Upper (swing)", F(0f, slewY + TH * 0.12f, 0f));
            var gH = new Geo();
            float dT = HH * 0.16f;
            gH.Box(fm.dark, F(-HL * 0.12f, dT * 0.5f, 0f), FS(HL * 0.84f, dT, HW));
            gH.Box(fm.dark, F(HL * 0.24f, dT * 0.55f, 0f), FS(HL * 0.42f, dT * 1.1f, HW * 0.52f));
            float hoodLen = HL * 0.56f, hoodH = HH * 0.78f, hoodW = HW * 0.58f;
            gH.Box(paint, F(-HL * 0.08f, dT + hoodH * 0.5f, HW * 0.5f - hoodW * 0.5f), FS(hoodLen, hoodH, hoodW));
            gH.Cyl(fm.dark, F(-HL * 0.14f, dT + hoodH + HH * 0.14f, HW * 0.22f), Quaternion.identity, HW * 0.03f, HW * 0.025f, HH * 0.34f, 8);

            // cast counterweight: rounded plan at the rear, extruded upward
            float cwH = HH * 0.88f, cwHalf = HW * 0.5f, cwFront = -HL * 0.36f, cwDepth = HL * 0.22f, cwBack = cwFront - cwDepth;
            var cw = new List<Vector2> { new(cwFront, -cwHalf), new(cwBack + cwDepth * 0.38f, -cwHalf) };
            Quadratic(cw, new Vector2(cwBack, -cwHalf * 0.92f), new Vector2(cwBack, -cwHalf * 0.55f), 3);
            cw.Add(new Vector2(cwBack, cwHalf * 0.55f));
            Quadratic(cw, new Vector2(cwBack, cwHalf * 0.92f), new Vector2(cwBack + cwDepth * 0.38f, cwHalf), 3);
            cw.Add(new Vector2(cwFront, cwHalf));
            gH.Prism(accent, cw, (p, z) => new Vector3(p.y, dT + z, p.x), 0f, cwH);
            gH.Box(paint, F(cwBack - 0.01f, dT + cwH * 0.5f, 0f), FS(cwDepth * 0.25f, cwH * 0.18f, cwHalf * 1.35f));

            // operator cab, left front
            float CL = c.cabLength, CW = c.cabWidth, CH = c.cabHeight;
            Vector3 cab = F(HL * 0.14f, dT, -HW * 0.5f + CW * 0.52f);
            gH.Box(accent, cab + F(0f, CH * 0.12f, 0f), FS(CL, CH * 0.24f, CW));
            gH.Box(paint, cab + F(0f, CH * 0.96f, 0f), FS(CL * 1.04f, CH * 0.08f, CW * 1.04f));
            gH.Box(fm.glass, cab + F(0f, CH * 0.58f, 0f), FS(CL * 0.94f, CH * 0.68f, CW * 0.92f));
            float pr = Mathf.Max(0.022f, CW * 0.025f);
            foreach (var (px, pz) in new[] { (-0.46f, -0.45f), (0.46f, -0.45f), (-0.46f, 0.45f), (0.46f, 0.45f) })
                gH.Cyl(accent, cab + F(CL * px, CH * 0.58f, CW * pz), Quaternion.identity, pr, pr, CH * 0.72f, 6);
            gH.Cyl(fm.amber, cab + F(-CL * 0.32f, CH * 1.05f, 0f), Quaternion.identity, 0.06f, 0.05f, 0.11f, 8);
            var seat = Node(upper, "CabView", cab + F(-CL * 0.06f, Mathf.Min(CH * 0.7f, 1.3f), 0f));
            CabInterior(seat, CabStyle.Excavator, CabMatsFrom(fm, accent), id);

            if (c.catwalks)
                foreach (int cs in new[] { -1, 1 })
                {
                    float walkW = 0.42f, walkLen = HL * 0.74f, railH = 1.05f, railZ = cs * (HW * 0.5f + walkW * 0.85f);
                    gH.Box(fm.wear, F(-HL * 0.08f, dT + 0.03f, cs * (HW * 0.5f + walkW * 0.45f)), FS(walkLen, 0.06f, walkW));
                    gH.Box(fm.rail, F(-HL * 0.08f, dT + railH, railZ), FS(walkLen, 0.035f, 0.035f));
                    gH.Box(fm.rail, F(-HL * 0.08f, dT + railH * 0.52f, railZ), FS(walkLen, 0.025f, 0.025f));
                    for (int p = 0; p < 5; p++)
                        gH.Cyl(fm.rail, F(-HL * 0.08f - walkLen * 0.46f + p / 4f * walkLen * 0.92f, dT + railH * 0.5f, railZ), Quaternion.identity, 0.018f, 0.018f, railH, 6);
                }
            // dark engine hood panel and side louvres on the engine bay
            gH.Box(accent, F(-HL * 0.08f, dT + hoodH + HH * 0.03f, HW * 0.5f - hoodW * 0.5f), FS(hoodLen * 0.86f, HH * 0.06f, hoodW * 0.84f));
            gH.Box(accent, F(-HL * 0.1f, dT + hoodH * 0.55f, HW * 0.5f + 0.005f), FS(hoodLen * 0.6f, hoodH * 0.5f, 0.02f));

            // boom foot towers and boom-ram trunnions
            float BL = c.boomLength, BD = c.boomDepth, SL = c.stickLength, SD = c.stickDepth;
            float footX = HL * 0.24f, footY = dT + HH * 0.52f, boomHalfW = BD * 0.36f;
            float[] cylZ = c.dualBoomCylinders ? new[] { -(boomHalfW + BD * 0.24f), boomHalfW + BD * 0.24f } : new[] { 0f };
            foreach (int s in new[] { -1, 1 })
                gH.Box(paint, F(footX - BD * 0.12f, dT + HH * 0.32f, s * (boomHalfW + BD * 0.14f)), FS(BD * 1.15f, HH * 0.64f, BD * 0.22f));

            float boomCylR = BD * 0.145f, boomRodR = boomCylR * 0.62f;
            float baseX = footX + BD * 0.42f, baseY = dT + HH * 0.16f;
            float elbowX = BL * 0.52f, elbowY = BL * 0.16f;
            float lugX = elbowX * 0.86f, lugY = elbowY * 0.86f - BD * 0.48f;
            Pin(gH, fm, F(baseX, baseY, 0f), boomCylR * 0.72f, (Mathf.Abs(cylZ[0]) + boomCylR * 1.4f) * 2f);

            // --- C. boom
            var boomPivot = Node(upper, "BoomPivot", F(footX, footY, 0f));
            var gBoom = new Geo();
            float lowerLen = Mathf.Sqrt(elbowX * elbowX + elbowY * elbowY), lowerAng = Mathf.Atan2(elbowY, elbowX);
            float upDx = BL - elbowX, upDy = -elbowY, upLen = Mathf.Sqrt(upDx * upDx + upDy * upDy), upAng = Mathf.Atan2(upDy, upDx);
            var boomPoly = new List<Vector2>
            {
                new(-BD * 0.3f, -BD * 0.4f), new(elbowX, elbowY - BD * 0.46f), new(BL + BD * 0.12f, -BD * 0.3f),
                new(BL + BD * 0.12f, BD * 0.3f), new(elbowX, elbowY + BD * 0.48f), new(-BD * 0.3f, BD * 0.4f),
            };
            gBoom.Prism(paint, boomPoly, (p, z) => F(p.x, p.y, z), -boomHalfW, boomHalfW);
            gBoom.Box(accent, F(elbowX, elbowY, 0f), FS(BD * 0.5f, BD * 0.5f, boomHalfW * 2.04f), RZ(upAng * 0.5f + lowerAng * 0.5f));
            ToolBox(boomPivot, "Boom Lower", F(elbowX * 0.5f, elbowY * 0.5f, 0f), FS(lowerLen, BD * 0.82f, boomHalfW * 2f), RZ(lowerAng));
            ToolBox(boomPivot, "Boom Upper", F(elbowX + upDx * 0.5f, elbowY + upDy * 0.5f, 0f), FS(upLen, BD * 0.66f, boomHalfW * 1.82f), RZ(upAng));
            Pin(gBoom, fm, Vector3.zero, BD * 0.16f, (boomHalfW + BD * 0.28f) * 2f);
            Pin(gBoom, fm, F(BL, 0f, 0f), SD * 0.18f, boomHalfW * 2.3f);
            Pin(gBoom, fm, F(lugX, lugY, 0f), boomCylR * 0.68f, (Mathf.Abs(cylZ[0]) + boomCylR * 1.3f) * 2f);
            for (int k = 0; k < cylZ.Length; k++)
            {
                float z = cylZ[k];
                gH.Box(fm.dark, F(baseX, baseY - HH * 0.06f, z), FS(boomCylR * 2.4f, HH * 0.24f, boomCylR * 0.65f));
                gBoom.Box(paint, F(lugX, lugY + BD * 0.16f, z), FS(boomCylR * 2.2f, BD * 0.42f, boomCylR * 0.65f));
                var ha = Node(upper, "BoomRamBase" + k, F(baseX, baseY, z));
                var hb = Node(boomPivot, "BoomRamRod" + k, F(lugX, lugY, z));
                rams.Add(new Ram { name = "Boom Ram " + k, parent = upper, a = ha, b = hb, barrelR = boomCylR, rodR = boomRodR });
            }

            // --- D. stick + over-boom stick ram
            var stickPivot = Node(boomPivot, "StickPivot", F(BL, 0f, 0f));
            var gS = new Geo();
            float rearEar = SL * 0.24f;
            var stickPoly = new List<Vector2>
            {
                new(-rearEar, SD * 0.3f - SD * 0.36f), new(0f, -SD * 0.44f), new(SL, -SD * 0.3f),
                new(SL, SD * 0.3f), new(0f, SD * 0.5f), new(-rearEar, SD * 0.3f + SD * 0.36f),
            };
            gS.Prism(paint, stickPoly, (p, z) => F(p.x, p.y, z), -SD * 0.29f, SD * 0.29f);
            ToolBox(stickPivot, "Stick", F((SL - rearEar) * 0.5f, SD * 0.1f, 0f), FS(SL + rearEar, SD * 1.0f, SD * 0.6f), Quaternion.identity);
            float sBaseX = elbowX * 0.96f, sBaseY = elbowY + BD * 0.64f;
            gBoom.Box(paint, F(sBaseX, elbowY + BD * 0.36f, 0f), FS(BD * 0.62f, BD * 0.56f, SD * 0.52f));
            Pin(gBoom, fm, F(sBaseX, sBaseY, 0f), SD * 0.14f, SD * 0.72f);
            var sA = Node(boomPivot, "StickRamBase", F(sBaseX, sBaseY, 0f));
            float sRodX = -rearEar * 0.8f, sRodY = SD * 0.3f;
            Pin(gS, fm, F(sRodX, sRodY, 0f), SD * 0.14f, SD * 0.72f);
            var sB = Node(stickPivot, "StickRamRod", F(sRodX, sRodY, 0f));
            rams.Add(new Ram { name = "Stick Ram", parent = upper, a = sA, b = sB, barrelR = SD * 0.18f, rodR = SD * 0.11f });

            // --- E. bucket + four-bar linkage
            var bucketPivot = Node(stickPivot, "BucketPivot", F(SL, 0f, 0f));
            var gPin = new Geo();
            Pin(gPin, fm, Vector3.zero, SD * 0.16f, SD * 0.95f);
            gPin.Attach(bucketPivot, id + "_BucketPin");
            // our bucket angle 0 = bucket shape rotated 140° from the visualizer's zero (so the game's dig/dump angles line up)
            var shape = Node(bucketPivot, "Bucket", Vector3.zero);
            shape.localRotation = Quaternion.Euler(-140f, 0f, 0f);
            var gK = new Geo();
            float bR = c.bucketRadius, bW = c.bucketWidth, st = Mathf.Max(0.028f, bR * 0.082f);
            Vector2 V(float x, float y) => new Vector2(x * bR, y * bR);
            var shell = new List<Vector2> { V(0.10f, 0.42f), V(0.46f, 0.36f) };
            Quadratic(shell, V(1.18f, 0.24f), V(1.22f, -0.44f));
            Quadratic(shell, V(1.16f, -1.04f), V(0.42f, -1.14f));
            shell.Add(V(-0.48f, -1.10f)); shell.Add(V(-0.48f, -1.02f)); shell.Add(V(0.38f, -1.05f));
            Quadratic(shell, V(1.04f, -0.96f), V(1.10f, -0.44f));
            Quadratic(shell, V(1.06f, 0.16f), V(0.42f, 0.28f));
            shell.Add(V(0.10f, 0.34f));
            Func<Vector2, float, Vector3> bmap = (p, z) => new Vector3(z, p.y, p.x);
            gK.Prism(c.miningShroud ? fm.wear : paint, shell, bmap, -bW * 0.5f, bW * 0.5f);
            gK.Box(fm.dark, F(bR * 0.24f, bR * 0.34f, 0f), FS(bR * 0.46f, bR * 0.2f, bW * 1.02f));
            foreach (float yn in new[] { -0.28f, 0.28f })
                gK.Box(fm.wear, F(bR * 1.17f, yn * bR - bR * 0.28f, 0f), FS(bR * 0.14f, bR * 0.38f, bW * 0.94f), RZ(yn > 0 ? 0.24f : -0.22f));
            var cheek = new List<Vector2> { V(-0.16f, 0.12f), V(0.10f, 0.42f), V(0.46f, 0.36f) };
            Quadratic(cheek, V(1.18f, 0.24f), V(1.22f, -0.44f));
            Quadratic(cheek, V(1.16f, -1.04f), V(0.42f, -1.14f));
            cheek.Add(V(-0.48f, -1.10f)); cheek.Add(V(-0.08f, -0.46f));
            foreach (float zs in new[] { -bW * 0.5f, bW * 0.5f - st * 1.15f })
                gK.Prism(paint, cheek, bmap, zs, zs + st * 1.15f);
            foreach (float zs in new[] { -bW * 0.5f, bW * 0.5f })
                gK.Box(fm.wear, F(-bR * 0.28f, -bR * 0.76f, zs), FS(bR * 0.2f, bR * 0.64f, st * 1.65f), RZ(-0.52f));
            gK.Box(fm.wear, F(-bR * 0.42f, -bR * 1.06f, 0f), FS(bR * 0.38f, st * 1.9f, bW * 1.04f));
            int teeth = bW > 2f ? 6 : bW > 1.1f ? 5 : 4;
            for (int tI = 0; tI < teeth; tI++)
            {
                float tz = -bW * 0.42f + tI / (float)(teeth - 1) * bW * 0.84f;
                float lead = (1f - Mathf.Abs(tI / (float)(teeth - 1) * 2f - 1f)) * bR * 0.08f;
                gK.Box(fm.dark, F(-bR * 0.56f - lead, -bR * 1.06f, tz), FS(bR * 0.26f, st * 2.25f, bW / (teeth * 2.3f)));
                gK.Cyl(fm.amber, F(-bR * 0.82f - lead, -bR * 1.06f, tz), Quaternion.Euler(-90f, 0f, 0f), st * 1.7f, 0f, bR * 0.48f, 4);
            }
            float earR = Mathf.Max(SD * 0.62f, bR * 0.42f);
            Vector2 ear = new Vector2(earR * 0.36f, earR * 0.92f);
            foreach (float ze in new[] { -SD * 0.24f, SD * 0.24f })
                gK.Box(fm.dark, F(ear.x * 0.55f, ear.y * 0.55f, ze), FS(earR * 1.05f, earR * 1.35f, SD * 0.11f));
            Pin(gK, fm, F(ear.x, ear.y, 0f), SD * 0.12f, SD * 0.72f);
            gK.Attach(shape, id + "_Bucket");
            ToolBox(shape, "Bucket", F(bR * 0.4f, -bR * 0.42f, 0f), FS(bR * 1.4f, bR * 1.3f, bW * 0.95f), Quaternion.identity);
            var earMarker = Node(shape, "BucketEar", F(ear.x, ear.y, 0f));
            var tip = Node(shape, "BucketTip", F(-bR * 1.12f, -bR * 1.06f, 0f));
            var mound = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(mound.GetComponent<Collider>());
            mound.name = "Bucket Load (dirt)";
            mound.transform.SetParent(shape, false);
            mound.transform.localPosition = F(bR * 0.42f, -bR * 0.42f, 0f);
            mound.transform.localScale = FS(bR * 1.25f, bR * 1.05f, bW * 0.88f);
            mound.GetComponent<MeshRenderer>().sharedMaterial = fm.dirt;
            mound.SetActive(false);

            // stick-side linkage pins
            Vector2 bBase = new Vector2(SL * 0.14f, SD * 0.64f);
            gS.Box(paint, F(bBase.x, SD * 0.4f, 0f), FS(SD * 0.68f, SD * 0.52f, SD * 0.46f));
            Pin(gS, fm, F(bBase.x, bBase.y, 0f), SD * 0.13f, SD * 0.66f);
            var bA = Node(stickPivot, "BucketRamBase", F(bBase.x, bBase.y, 0f));
            Vector2 hPos = new Vector2(SL * 0.8f, SD * 0.24f);
            gS.Box(fm.dark, F(hPos.x, SD * 0.14f, 0f), FS(SD * 0.44f, SD * 0.36f, SD * 0.42f));
            Pin(gS, fm, F(hPos.x, hPos.y, 0f), SD * 0.12f, SD * 0.68f);
            float hLen = Mathf.Max(SD * 0.82f, earR * 1.15f), dogLen = Mathf.Max(SD * 0.8f, earR * 1.12f);
            var hLink = Node(stickPivot, "H-Link", F(hPos.x, hPos.y, 0f));
            var gL = new Geo();
            foreach (int s in new[] { -1, 1 }) gL.Box(fm.dark, F(hLen * 0.5f, 0f, s * SD * 0.24f), FS(hLen, SD * 0.22f, SD * 0.1f));
            Pin(gL, fm, F(hLen, 0f, 0f), SD * 0.11f, SD * 0.66f);
            gL.Attach(hLink, id + "_HLink");
            var joint = Node(hLink, "LinkJoint", F(hLen, 0f, 0f));
            var dog = Node(stickPivot, "DogBone", Vector3.zero);
            var gD = new Geo();
            foreach (int s in new[] { -1, 1 }) gD.Box(paint, F(dogLen * 0.5f, 0f, s * SD * 0.15f), FS(dogLen, SD * 0.2f, SD * 0.09f));
            gD.Attach(dog, id + "_DogBone");
            rams.Add(new Ram { name = "Bucket Ram", parent = upper, a = bA, b = joint, barrelR = SD * 0.16f, rodR = SD * 0.095f });

            gH.Attach(upper, id + "_House");
            gBoom.Attach(boomPivot, id + "_Boom");
            gS.Attach(stickPivot, id + "_Stick");

            var link = root.AddComponent<ExcavatorLinkage>();
            link.stick = stickPivot; link.bucketEar = earMarker; link.hLink = hLink; link.dogbone = dog;
            link.hLinkLength = hLen; link.dogboneLength = dogLen;

            // --- machine script
            var ex = root.AddComponent<MiniExcavator>();
            float heavy = Mathf.InverseLerp(5f, 95f, c.tonnes);
            ex.displayName = c.name;
            ex.sizeScale = Mathf.Max(1f, TH / 0.45f);
            ex.centerOfMassHeight = TH * 0.45f;
            ex.fuelCapacity = c.fuel; ex.fuel = c.fuel * 0.8f; ex.idleBurn = c.workBurn * 0.25f; ex.workBurn = c.workBurn;
            ex.bucketCapacity = c.capacity; ex.digRate = c.capacity * 1.1f; ex.digRadius = bW * 0.45f; ex.dumpRate = c.capacity * 2.5f;
            ex.driveSpeed = c.drive; ex.turnSpeed = Mathf.Lerp(32f, 18f, heavy);
            ex.boomSpeed = Mathf.Lerp(28f, 16f, heavy); ex.stickSpeed = Mathf.Lerp(38f, 22f, heavy);
            ex.bucketSpeed = Mathf.Lerp(65f, 40f, heavy); ex.swingSpeed = Mathf.Lerp(40f, 20f, heavy);
            ex.upper = upper; ex.boomPivot = boomPivot; ex.stickPivot = stickPivot; ex.bucketPivot = bucketPivot;
            ex.bucketTip = tip; ex.bucketLoadVisual = mound.transform;
            // the bucket opens toward the lip (source -x), a little up: the back is +x, the bottom -y
            ex.bucketMouth = F(-0.95f, 0.3f, 0f).normalized;
            if (bladePivot != null) { ex.blade = bladePivot; ex.bladeEdge = bladeEdge; ex.bladeWidth = TG + SW; }

            // stroke of every ram over the joints' full travel
            void Pose(float boom, float stick, float bucket, float blade)
            {
                boomPivot.localRotation = Quaternion.Euler(-boom, 0f, 0f);
                stickPivot.localRotation = Quaternion.Euler(-stick, 0f, 0f);
                bucketPivot.localRotation = Quaternion.Euler(-bucket, 0f, 0f);
                if (bladePivot != null) bladePivot.localRotation = Quaternion.Euler(-blade, 0f, 0f);
                link.Apply();
                foreach (var r in rams) r.Measure();
            }
            for (int i = 0; i <= 16; i++)
            {
                float u = i / 16f;
                Pose(Mathf.Lerp(ex.boomRange.x, ex.boomRange.y, u), Mathf.Lerp(ex.stickRange.x, ex.stickRange.y, u),
                     Mathf.Lerp(ex.bucketRange.x, ex.bucketRange.y, u), Mathf.Lerp(ex.bladeRange.x, ex.bladeRange.y, u));
                Pose(Mathf.Lerp(ex.boomRange.y, ex.boomRange.x, u), Mathf.Lerp(ex.stickRange.x, ex.stickRange.y, u),
                     Mathf.Lerp(ex.bucketRange.y, ex.bucketRange.x, u), 0f);
            }
            Pose(15f, -95f, -60f, 8f); // the script's start pose
            foreach (var r in rams) BuildRam(r, fm, r.name.StartsWith("Blade") ? fm.dark : paint, id);

            // physics + interaction
            var rb = root.GetComponent<Rigidbody>(); if (rb == null) rb = root.AddComponent<Rigidbody>();
            rb.mass = c.tonnes * 1000f; rb.angularDamping = 3f; rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, TH * 0.5f, 0f); col.size = new Vector3(TG + SW, TH, TL);
            var hc = new GameObject("HouseCollider");
            hc.transform.SetParent(upper, false);
            var hbox = hc.AddComponent<BoxCollider>();
            float houseTop = Mathf.Max(HH, CH) + dT;
            hbox.center = new Vector3(0f, houseTop * 0.5f, -HL * 0.08f);
            hbox.size = new Vector3(HW, houseTop, HL * 0.95f);

            ex.exitPoint = Marker(root.transform, "ExitPoint", new Vector3(-(TG * 0.5f + SW + 1.2f), 0f, 0f));
            ex.vehicleCamera = VehicleCam(root.transform, upper, seat, BL * 1.4f + 4f, HH + CH + 1f);
            return root;
        }

        // ------------------------------------------------------------------ dozer

        static GameObject BuildFleetDozer(DozerCfg c, FleetMats fm)
        {
            var paint = fm.Paint(c.primary);
            var accent = fm.Paint(c.accent);
            string id = "Fleet_" + c.id;
            var root = new GameObject(c.name);
            var t = root.transform;
            var rams = new List<Ram>();
            var g = new Geo();

            float TL = c.trackLength, TG = c.trackGauge, TH = c.trackHeight, SW = c.shoeWidth;
            float HuL = c.hullLength, HuW = c.hullWidth, HuH = c.hullHeight;

            // --- A. high-drive triangular track frames
            float idR = TH * 0.26f, spR = TH * 0.28f, half = TL * 0.44f, spX = -half * 0.58f, spY = c.sprocketElevation;
            foreach (int side in new[] { -1, 1 })
            {
                float zs = side * TG * 0.5f;
                g.Box(fm.under, F(0f, idR + 0.04f, zs), FS(half * 2.05f, idR * 1.15f, SW * 0.62f));
                g.Box(fm.under, F(spX, (spY + idR) * 0.5f, zs), FS(spR * 1.6f, spY - idR * 0.5f, SW * 0.66f));
                g.Cyl(fm.dark, F(half, idR + 0.04f, zs), AxisLat, idR, idR, SW * 0.68f, 12);
                g.Cyl(fm.dark, F(-half, idR + 0.04f, zs), AxisLat, idR, idR, SW * 0.68f, 12);
                g.Cyl(paint, F(spX, spY, zs), AxisLat, spR, spR, SW * 0.68f, 12);
                float shoeT = Mathf.Max(0.04f, TH * 0.045f);
                // high-drive belt round both idlers and the raised sprocket; the shoes run round it live
                TrackAnimator.AddBelt(root, fm.shoe, zs, SW, shoeT, 0.22f,
                    new Vector3(half, idR + 0.04f, idR), new Vector3(-half, idR + 0.04f, idR), new Vector3(spX, spY, spR));
            }

            // --- B. chassis, sloped hood, ROPS cab
            float cb = idR * 1.65f;
            g.Box(accent, F(-HuL * 0.04f, cb + HuH * 0.31f, 0f), FS(HuL * 0.85f, HuH * 0.62f, HuW));
            g.Box(paint, F(HuL * 0.16f, cb + HuH * 0.72f, 0f), FS(HuL * 0.52f, HuH * 0.58f, HuW * 0.82f), RZ(-0.09f));
            float noseX = HuL * 0.44f;
            g.Box(paint, F(noseX, cb + HuH * 0.52f, 0f), FS(HuL * 0.14f, HuH * 0.96f, HuW * 0.88f));
            foreach (float zo in new[] { -0.28f, 0.28f })
                g.Cyl(fm.dark, F(HuL * 0.24f, cb + HuH * 1.12f, zo * HuW), Quaternion.identity, HuW * 0.038f, HuW * 0.032f, HuH * 0.45f, 8);
            float CL = c.cabLength, CW = c.cabWidth, CH = c.cabHeight;
            Vector3 cab = F(-HuL * 0.24f, cb + HuH * 0.62f, 0f);
            g.Box(fm.glass, cab + F(0f, CH * 0.44f, 0f), FS(CL, CH * 0.88f, CW));
            g.Box(paint, cab + F(0f, CH * 0.94f, 0f), FS(CL * 1.15f, CH * 0.12f, CW * 1.24f));
            foreach (int sz in new[] { -1, 1 })
                g.Box(paint, cab + F(-CL * 0.15f, CH * 0.48f, sz * CW * 0.56f), FS(CL * 0.16f, CH * 0.96f, CW * 0.12f));
            g.Cyl(fm.amber, cab + F(-CL * 0.3f, CH * 1.03f, 0f), Quaternion.identity, 0.07f, 0.06f, 0.12f, 8);
            var seat = Node(t, "CabView", cab + F(-CL * 0.08f, Mathf.Min(CH * 0.62f, 1.25f), 0f));
            CabInterior(seat, CabStyle.Dozer, CabMatsFrom(fm, accent), id);

            // --- C. push arms, blade, lift + tilt rams
            float trX = -half * 0.18f, trY = idR * 1.22f, armZ = TG * 0.5f + SW * 0.56f;
            float noseFront = noseX + HuL * 0.09f, curv = c.bladeCurvature, bh = c.bladeHeight;
            float armLen = Mathf.Max(c.pushArmLength, noseFront - trX + curv * 0.65f);
            var arms = Node(t, "PushArms", F(trX, trY, 0f));
            var gA = new Geo();
            gA.Box(fm.dark, F(armLen * 0.88f, 0f, 0f), FS(HuH * 0.18f, HuH * 0.18f, armZ * 2f));
            float tiltBaseX = armLen * 0.42f, tiltBaseY = HuH * 0.12f;
            var armAnchors = new Transform[2];
            int ai = 0;
            foreach (int s in new[] { -1, 1 })
            {
                g.Box(fm.dark, F(trX, trY, s * (armZ - SW * 0.16f)), FS(HuH * 0.24f, HuH * 0.22f, SW * 0.36f));
                g.Cyl(fm.pin, F(trX, trY, s * armZ), AxisLat, HuH * 0.11f, HuH * 0.11f, HuH * 0.2f, 10);
                gA.Box(paint, F(armLen * 0.5f, 0f, s * armZ), FS(armLen, HuH * 0.16f, SW * 0.22f));
                gA.Box(fm.dark, F(armLen - HuH * 0.04f, bh * 0.16f, s * armZ), FS(HuH * 0.18f, bh * 0.42f, SW * 0.22f));
                gA.Box(paint, F(armLen * 0.45f, HuH * 0.14f, s * armZ), FS(HuH * 0.24f, HuH * 0.26f, SW * 0.2f));
                Pin(gA, fm, F(tiltBaseX, tiltBaseY, s * armZ), HuH * 0.04f, SW * 0.26f);
                armAnchors[ai++] = Node(arms, "TiltRamBase" + s, F(tiltBaseX, tiltBaseY, s * armZ));
            }
            var bladeGrp = Node(arms, "Blade", F(armLen, bh * 0.34f, 0f));
            var gBl = new Geo();
            float centerW = c.bladeWidth * 0.58f, wingW = c.bladeWidth * 0.24f;
            gBl.Box(paint, Vector3.zero, FS(curv * 0.42f, bh, centerW));
            gBl.Box(fm.dark, F(0.05f, bh * 0.58f, 0f), FS(0.12f, bh * 0.24f, centerW * 0.96f), RZ(-0.3f));
            foreach (int s in new[] { -1, 1 })
            {
                var wr = Quaternion.Euler(0f, -s * 20.6f, 0f); // U-blade wings angle forward
                gBl.Box(paint, F(wingW * 0.22f, -bh * 0.02f, s * (centerW * 0.5f + wingW * 0.44f)), FS(curv * 0.4f, bh * 0.96f, wingW), wr);
                gBl.Box(fm.wear, F(wingW * 0.4f, -bh * 0.04f, s * (centerW * 0.5f + wingW * 0.88f)), FS(curv * 0.65f, bh * 0.92f, 0.06f), wr);
            }
            gBl.Box(fm.wear, F(0.08f, -bh * 0.5f, 0f), FS(curv * 0.5f, bh * 0.12f, centerW + 0.04f));
            var bladeEdge = Node(bladeGrp, "BladeEdge", F(curv * 0.21f, -bh * 0.56f, 0f));
            ToolBox(bladeGrp, "Blade", F(0.05f, 0f, 0f), FS(curv * 0.5f, bh * 0.95f, c.bladeWidth * 0.92f), Quaternion.identity);

            float liftNoseX = noseX + HuL * 0.08f, liftNoseY = cb + HuH * 0.86f, liftNoseZ = HuW * 0.48f;
            float earX = -curv * 0.28f, liftEarY = bh * 0.36f, tiltEarY = bh * 0.44f, tiltEarZ = armZ * 0.56f;
            ai = 0;
            foreach (int s in new[] { -1, 1 })
            {
                g.Box(fm.dark, F(liftNoseX, liftNoseY, s * liftNoseZ), FS(HuH * 0.22f, HuH * 0.24f, HuW * 0.14f));
                Pin(g, fm, F(liftNoseX, liftNoseY, s * liftNoseZ), HuH * 0.048f, HuW * 0.18f);
                gBl.Box(fm.dark, F(earX * 0.65f, liftEarY, s * liftNoseZ), FS(curv * 0.38f, bh * 0.18f, HuW * 0.12f));
                Pin(gBl, fm, F(earX, liftEarY, s * liftNoseZ), HuH * 0.045f, HuW * 0.16f);
                var na = Node(t, "LiftRamBase" + s, F(liftNoseX, liftNoseY, s * liftNoseZ));
                var nb = Node(bladeGrp, "LiftRamRod" + s, F(earX, liftEarY, s * liftNoseZ));
                rams.Add(new Ram { name = "Lift Ram " + (s < 0 ? "L" : "R"), parent = t, a = na, b = nb, barrelR = HuH * 0.065f, rodR = HuH * 0.04f });

                gBl.Box(fm.dark, F(earX * 0.65f, tiltEarY, s * tiltEarZ), FS(curv * 0.38f, bh * 0.2f, SW * 0.18f));
                Pin(gBl, fm, F(earX, tiltEarY, s * tiltEarZ), HuH * 0.04f, SW * 0.24f);
                var tb = Node(bladeGrp, "TiltRamRod" + s, F(earX, tiltEarY, s * tiltEarZ));
                rams.Add(new Ram { name = "Tilt Ram " + (s < 0 ? "L" : "R"), parent = t, a = armAnchors[ai++], b = tb, barrelR = HuH * 0.055f, rodR = HuH * 0.034f });
            }
            gA.Attach(arms, id + "_PushArms");
            gBl.Attach(bladeGrp, id + "_Blade");

            // --- D. parallelogram ripper
            float rs = c.ripperScale, rearX = -HuL * 0.46f;
            float lowY = cb + HuH * 0.22f, pitchY = cb + HuH * 0.68f, liftY = cb + HuH * 0.86f;
            float beamLen = 1.18f * rs, mastH = 0.72f * rs, carW = HuW * 0.88f;
            g.Box(fm.dark, F(rearX - 0.08f * rs, cb + HuH * 0.48f, 0f), FS(0.28f * rs, HuH * 0.78f, carW * 0.92f));
            var ripPivot = Node(t, "RipperArms", F(rearX - 0.14f * rs, lowY, 0f));
            var gR = new Geo();
            foreach (int s in new[] { -1, 1 })
            {
                gR.Box(fm.dark, F(-beamLen * 0.5f, 0f, s * carW * 0.42f), FS(beamLen, 0.22f * rs, 0.16f * rs));
                Pin(gR, fm, F(0f, 0f, s * carW * 0.42f), 0.048f * rs, 0.24f * rs);
            }
            gR.Attach(ripPivot, id + "_RipperArms");
            var shankGrp = Node(ripPivot, "RipperShanks", F(-beamLen, 0f, 0f));
            var gSh = new Geo();
            gSh.Box(paint, Vector3.zero, FS(0.34f * rs, 0.34f * rs, carW));
            foreach (int s in new[] { -1, 1 })
                gSh.Box(paint, F(0f, mastH * 0.5f, s * carW * 0.36f), FS(0.26f * rs, mastH, 0.16f * rs));
            var shankZ = c.ripperShanks == 1 ? new[] { 0f } : new[] { -carW * 0.36f, 0f, carW * 0.36f };
            var tips = new List<Transform>();
            foreach (float z in shankZ)
            {
                gSh.Box(fm.wear, F(-0.08f * rs, -0.35f * rs, z), FS(0.28f * rs, 1.35f * rs, 0.12f * rs), RZ(-0.18f));
                gSh.Cyl(fm.pin, F(0.08f * rs, -1.02f * rs, z), RZ(-Mathf.PI * 0.5f - 0.3f), 0.11f * rs, 0f, 0.34f * rs, 4);
                tips.Add(Node(shankGrp, "RipperTip", F(0.24f * rs, -1.07f * rs, z)));
            }
            float zc = carW * 0.36f;
            foreach (int s in new[] { -1, 1 })
            {
                Pin(g, fm, F(rearX - 0.16f * rs, liftY, s * zc), 0.05f * rs, 0.24f * rs);
                Pin(gSh, fm, F(0.08f * rs, 0.22f * rs, s * zc), 0.048f * rs, 0.22f * rs);
                Pin(g, fm, F(rearX - 0.16f * rs, pitchY, s * zc), 0.046f * rs, 0.24f * rs);
                Pin(gSh, fm, F(0.06f * rs, mastH * 0.92f, s * zc), 0.044f * rs, 0.22f * rs);
                rams.Add(new Ram { name = "Ripper Lift Ram " + (s < 0 ? "L" : "R"), parent = t, barrelR = 0.082f * rs, rodR = 0.05f * rs,
                    a = Node(t, "RipLiftBase" + s, F(rearX - 0.16f * rs, liftY, s * zc)), b = Node(shankGrp, "RipLiftRod" + s, F(0.08f * rs, 0.22f * rs, s * zc)) });
                rams.Add(new Ram { name = "Ripper Pitch Ram " + (s < 0 ? "L" : "R"), parent = t, barrelR = 0.074f * rs, rodR = 0.045f * rs,
                    a = Node(t, "RipPitchBase" + s, F(rearX - 0.16f * rs, pitchY, s * zc)), b = Node(shankGrp, "RipPitchRod" + s, F(0.06f * rs, mastH * 0.92f, s * zc)) });
            }
            gSh.Attach(shankGrp, id + "_RipperShanks");
            g.Attach(t, id + "_Body");

            // --- machine script
            var dz = root.AddComponent<Bulldozer>();
            dz.displayName = c.name;
            dz.fuelCapacity = c.fuel; dz.fuel = c.fuel * 0.8f; dz.idleBurn = c.workBurn * 0.2f; dz.workBurn = c.workBurn;
            dz.driveSpeed = c.drive; dz.reverseSpeed = c.drive * 1.15f; dz.turnSpeed = c.tonnes > 40f ? 22f : 30f; dz.turnInPlace = true;
            dz.acceleration = c.tonnes > 40f ? 1.6f : 2.2f;
            dz.centerOfMass = new Vector3(0f, cb + HuH * 0.3f, 0f);
            dz.blade = bladeGrp; dz.bladeEdge = bladeEdge; dz.liftPivot = arms;
            dz.bladeWidth = c.bladeWidth * 0.94f; dz.heapCapacity = c.bladeCapacity; dz.pushAhead = curv * 0.8f + 0.45f;
            dz.bladeRange = c.tonnes > 40f ? new Vector2(-0.6f, 1.5f) : new Vector2(-0.4f, 1.1f);
            dz.bladeSpeed = c.tonnes > 40f ? 0.6f : 0.55f;
            dz.ripperPivot = ripPivot; dz.ripperShank = shankGrp; dz.ripperTips = tips.ToArray();
            dz.ripRadius = c.ripperShanks == 1 ? 0.55f : 0.32f;

            // ram strokes: blade lift (arm swing) with the blade tilted and angled both ways, ripper over its travel
            dz.bladeSwivel = bladeGrp;
            dz.maxAngle = c.tonnes > 40f ? 18f : 25f;
            for (int i = 0; i <= 16; i++)
            {
                float u = i / 16f;
                arms.localRotation = Quaternion.Euler(-Mathf.Lerp(-16f, 30f, u), 0f, 0f);
                float rip = Mathf.Lerp(dz.ripperRange.x, dz.ripperRange.y, u);
                ripPivot.localRotation = Quaternion.Euler(-rip, 0f, 0f);
                shankGrp.localRotation = Quaternion.Euler(rip, 0f, 0f);
                foreach (float ang in new[] { -dz.maxAngle, 0f, dz.maxAngle })
                    foreach (float tl in new[] { -dz.maxTilt, 0f, dz.maxTilt })
                    {
                        bladeGrp.localRotation = Quaternion.Euler(0f, ang, 0f) * Quaternion.Euler(0f, 0f, tl);
                        foreach (var r in rams) r.Measure();
                    }
                bladeGrp.localRotation = Quaternion.identity;
            }
            arms.localRotation = Quaternion.identity;
            ripPivot.localRotation = Quaternion.Euler(-dz.ripperRange.x, 0f, 0f);
            shankGrp.localRotation = Quaternion.Euler(dz.ripperRange.x, 0f, 0f);
            foreach (var r in rams) BuildRam(r, fm, fm.dark, id);

            var rb = root.GetComponent<Rigidbody>(); if (rb == null) rb = root.AddComponent<Rigidbody>();
            rb.mass = c.tonnes * 1000f; rb.angularDamping = 3f; rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var col = root.AddComponent<BoxCollider>();
            float top = cb + HuH + CH * 0.6f;
            col.center = new Vector3(0f, top * 0.5f, -HuL * 0.04f);
            col.size = new Vector3(TG + SW, top, Mathf.Max(half * 2f + idR * 2f, HuL * 0.95f));

            dz.exitPoint = Marker(t, "ExitPoint", new Vector3(-(TG * 0.5f + SW + 1.3f), 0f, -HuL * 0.2f));
            dz.vehicleCamera = VehicleCam(t, t, seat, c.bladeWidth * 2.4f + 4f, HuH + CH + 1.5f);
            return root;
        }
    }
}
