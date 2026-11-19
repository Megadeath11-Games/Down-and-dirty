using System.Collections.Generic;
using FPSBase.City;
using FPSBase.Core;
using FPSBase.Mining;
using FPSBase.World;
using UnityEditor;
using UnityEngine;

namespace FPSBase.EditorTools
{
    /// <summary>
    /// The town over the hill east of the mine: a second terrain tile, a road through a pass in the hills, and
    /// a flat valley with three streets of houses. The yards are a second diggable ground (driveways to gravel,
    /// ditches to dig), and a landscape supply on the south side sells gravel, sand and topsoil.
    /// </summary>
    public static partial class ProjectBootstrap
    {
        static readonly Vector3 CityCenter = new Vector3(470f, 0f, 10f);
        const float CityFieldSize = 224f, CityFlatR = 128f;
        static Vector3 CityFieldOrigin => new Vector3(CityCenter.x - CityFieldSize / 2f, -8f, CityCenter.z - CityFieldSize / 2f);
        static Rect CityFieldRect => new Rect(CityFieldOrigin.x, CityFieldOrigin.z, CityFieldSize, CityFieldSize);
        // the road from the mine's yard east through the hills to the town's west avenue
        static readonly Vector2 RoadA = new Vector2(216.5f, 22f);
        static Vector2 RoadB => new Vector2(CityCenter.x - 104f, CityCenter.z);
        const float StreetGap = 52f, LotW = 22f, StreetHalf = 4f, Walk = 1.5f;
        static readonly string[] StreetNames = { "Birch Ln", "Maple St", "Cedar Ave" };

        static Terrain eastTile;

        static float RoadDist(float wx, float wz)
        {
            Vector2 p = new Vector2(wx, wz), a = RoadA, b = RoadB, ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>
        /// The landscape's height (0..1 of the 60 m terrain height) at any world point: flat in the middle (mine,
        /// outpost), hills all round, a pass for the road east, and the town's flat valley beyond.
        /// </summary>
        static float WorldHeight01(float wx, float wz)
        {
            float ix = (wx + 300f) / 600f * 512f, iy = (wz + 300f) / 600f * 512f; // the main terrain's heightmap samples
            float n = Mathf.PerlinNoise(ix * 0.008f + 11f, iy * 0.008f + 7f) * 0.6f + Mathf.PerlinNoise(ix * 0.03f, iy * 0.03f) * 0.15f;
            float dist = Mathf.Sqrt(wx * wx + wz * wz);
            float hill = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(150f, 240f, dist));
            float dc = Vector2.Distance(new Vector2(wx, wz), new Vector2(CityCenter.x, CityCenter.z));
            hill *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(CityFlatR, CityFlatR + 70f, dc));  // the valley
            hill *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(9f, 45f, RoadDist(wx, wz)));       // the pass
            return 2f / 60f + n * hill * 0.9f;
        }

        // ------------------------------------------------------------------ terrain east of the mine

        static Terrain BuildEastTerrain()
        {
            const int res = 513;
            var data = new TerrainData { heightmapResolution = res, size = new Vector3(600f, 60f, 600f) };
            var hs = new float[res, res];
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                    hs[y, x] = WorldHeight01(300f + x / (float)(res - 1) * 600f, -300f + y / (float)(res - 1) * 600f);
            data.SetHeights(0, 0, hs);
            AssetDatabase.CreateAsset(data, $"{Gen}/TerrainDataEast.asset");
            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain East (town)";
            go.transform.position = new Vector3(300f, -2f, -300f);
            var t = go.GetComponent<Terrain>();
            t.heightmapPixelError = 3f; t.basemapDistance = 400f; t.drawInstanced = true;
            var lp = go.AddComponent<LowPolyTerrain>();
            lp.materials = new[]
            {
                Flat("Terrain_GrassA", new Color(0.33f, 0.53f, 0.2f)),
                Flat("Terrain_GrassB", new Color(0.39f, 0.58f, 0.22f)),
                Flat("Terrain_GrassDark", new Color(0.26f, 0.45f, 0.17f)),
                Flat("Terrain_Rock", new Color(0.5f, 0.5f, 0.52f)),
            };
            lp.excludeRect = CityFieldRect;
            lp.grassArea = new Rect(0f, 0f, 0f, 0f); // the town ground draws its own grass
            t.drawHeightmap = false;
            eastTile = t;
            return t;
        }

        /// <summary>Trees, rocks and bushes on the hills round the town (called from ScatterNature).</summary>
        static void ScatterEast(Transform root, System.Func<string, Mesh, Material[], Vector3, float, GameObject> spawnAt,
            Mesh[] pines, Mesh[] broad, Mesh[] rocks, Mesh[] bushes, Material trunk, Material pineA, Material pineB, Material leafA, Material leafB, Material rockMat, Material bushMat)
        {
            if (eastTile == null) return;
            var rng = new System.Random(4321);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var fr = CityFieldRect;
            bool Blocked(Vector3 p, float pad) =>
                (p.x > fr.xMin - pad && p.x < fr.xMax + pad && p.z > fr.yMin - pad && p.z < fr.yMax + pad) ||
                RoadDist(p.x, p.z) < 10f + pad || p.x < 303f || p.x > 892f || Mathf.Abs(p.z) > 292f;
            int trees = 0;
            for (int i = 0; i < 9000 && trees < 1300; i++)
            {
                var p = new Vector3(R(303f, 892f), 0f, R(-292f, 292f));
                if (Blocked(p, 5f)) continue;
                float forest = Mathf.PerlinNoise(p.x * 0.012f + 40f, p.z * 0.012f + 17f);
                if (!(forest > 0.47f || rng.NextDouble() < 0.04)) continue;
                bool pine = rng.NextDouble() < 0.7;
                var go = pine
                    ? spawnAt("Pine", pines[rng.Next(pines.Length)], new[] { trunk, rng.NextDouble() < 0.5 ? pineA : pineB }, p, R(0.85f, 1.6f))
                    : spawnAt("Tree", broad[rng.Next(broad.Length)], new[] { trunk, rng.NextDouble() < 0.7 ? leafA : leafB }, p, R(0.85f, 1.6f));
                var cc = go.AddComponent<CapsuleCollider>(); cc.radius = 0.3f; cc.height = 3f; cc.center = new Vector3(0f, 1.5f, 0f);
                trees++;
            }
            for (int i = 0; i < 200; i++)
            {
                var p = new Vector3(R(303f, 892f), 0f, R(-292f, 292f));
                if (Blocked(p, 3f)) continue;
                var go = spawnAt("Boulder", rocks[rng.Next(rocks.Length)], new[] { rockMat }, p, R(0.4f, 2.2f));
                var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh; mc.convex = true;
            }
            for (int i = 0; i < 400; i++)
            {
                var p = new Vector3(R(303f, 892f), 0f, R(-292f, 292f));
                if (Blocked(p, 2f)) continue;
                spawnAt("Bush", bushes[rng.Next(bushes.Length)], new[] { bushMat }, p, R(0.7f, 1.5f));
            }
        }

        /// <summary>Height of whichever big terrain is under a point.</summary>
        static float LandY(Terrain main, Vector3 p)
        {
            var t = eastTile != null && p.x >= 300f ? eastTile : main;
            return t.SampleHeight(p) + t.transform.position.y;
        }

        // ------------------------------------------------------------------ the town

        static void BuildCity(Mats m, Terrain main)
        {
            if (eastTile == null) return;
            var root = new GameObject("Town").transform;
            var hard = new List<Rect>();

            var asphalt = Flat("Asphalt", new Color(0.19f, 0.19f, 0.2f), 0.15f);
            var walk = Flat("Sidewalk", new Color(0.66f, 0.64f, 0.6f));
            // the road over from the mine, through the pass
            {
                Vector2 a = RoadA, b = RoadB, d = b - a;
                var road = Block(root, "Road to Town", new Vector3((a.x + b.x) / 2f, 0.06f - 0.15f, (a.y + b.y) / 2f), new Vector3(9f, 0.3f, d.magnitude + 4f), asphalt, 4f);
                road.transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg, 0f);
                GameObjectUtility.SetStaticEditorFlags(road, StaticEditorFlags.BatchingStatic);
            }

            // streets: three east-west, an avenue at each end
            float x0 = CityCenter.x - 104f, x1 = CityCenter.x + 96f;
            void Street(string name, Vector3 c, Vector3 size, float top, bool walks)
            {
                var s = Block(root, name, new Vector3(c.x, top - 0.15f, c.z), new Vector3(size.x, 0.3f, size.z), asphalt, 4f);
                GameObjectUtility.SetStaticEditorFlags(s, StaticEditorFlags.BatchingStatic);
                var st = s.AddComponent<Street>();
                st.streetName = name;
                st.rect = new Rect(c.x - size.x / 2f, c.z - size.z / 2f, size.x, size.z);
                hard.Add(st.rect);
                if (!walks) return;
                for (int k = -1; k <= 1; k += 2)
                {
                    float sx = (x1 - StreetHalf - Walk / 2f) - (x0 + StreetHalf + Walk / 2f) + Walk;
                    var w = Block(root, name + " sidewalk", new Vector3((x0 + x1) / 2f, top + 0.02f - 0.15f, c.z + k * (StreetHalf + Walk / 2f)), new Vector3(sx - 4f, 0.3f, Walk), walk, 2f);
                    GameObjectUtility.SetStaticEditorFlags(w, StaticEditorFlags.BatchingStatic);
                    hard.Add(new Rect(w.transform.position.x - w.transform.localScale.x / 2f, w.transform.position.z - Walk / 2f, w.transform.localScale.x, Walk));
                }
            }
            for (int s = -1; s <= 1; s++)
                Street(StreetNames[s + 1], new Vector3((x0 + x1) / 2f, 0f, CityCenter.z + s * StreetGap), new Vector3(x1 - x0, 0f, StreetHalf * 2f), 0.065f, true);
            Street("West Ave", new Vector3(x0, 0f, CityCenter.z - 22f), new Vector3(StreetHalf * 2f, 0f, 168f), 0.06f, false);
            Street("East Ave", new Vector3(x1, 0f, CityCenter.z), new Vector3(StreetHalf * 2f, 0f, StreetGap * 2f + StreetHalf * 2f), 0.06f, false);

            // street signs at the corners, and a welcome board where the road comes in
            var signGreen = ColorMat("Street_Sign", new Color(0.1f, 0.42f, 0.2f), 0f, 0.4f);
            var signPost = ColorMat("Street_Sign_Post", new Color(0.55f, 0.56f, 0.58f), 0.6f, 0.4f);
            for (int s = -1; s <= 1; s++)
            {
                float zs = CityCenter.z + s * StreetGap;
                foreach (var (ax, avenue) in new[] { (x0 + StreetHalf + 1.1f, "West Ave"), (x1 - StreetHalf - 1.1f, "East Ave") })
                {
                    var sg = new GameObject($"Street Sign {StreetNames[s + 1]} / {avenue}").transform; sg.SetParent(root, false);
                    sg.position = new Vector3(ax, 0f, zs + StreetHalf + 1.0f);
                    Part(sg, "Post", new Vector3(0f, 1.3f, 0f), new Vector3(0.08f, 2.6f, 0.08f), signPost, true);
                    Part(sg, "Blade", new Vector3(0f, 2.55f, 0f), new Vector3(1.5f, 0.24f, 0.04f), signGreen);
                    Part(sg, "Blade 2", new Vector3(0f, 2.25f, 0f), new Vector3(0.04f, 0.24f, 1.5f), signGreen);
                    sg.gameObject.AddComponent<StreetSign>().text = $"{StreetNames[s + 1]}  ·  {avenue}";
                }
            }
            {
                var wb = new GameObject("Welcome Board").transform; wb.SetParent(root, false);
                wb.position = new Vector3(x0 - 9f, 0f, CityCenter.z + 9f);
                wb.rotation = Quaternion.Euler(0f, -90f, 0f);
                var boardM = ColorMat("Welcome_Board", new Color(0.42f, 0.29f, 0.16f), 0f, 0.3f);
                foreach (float bx in new[] { -1.5f, 1.5f }) Part(wb, "Post", new Vector3(bx, 1.1f, 0f), new Vector3(0.16f, 2.2f, 0.16f), boardM, true);
                Part(wb, "Board", new Vector3(0f, 2.1f, 0f), new Vector3(3.6f, 1.5f, 0.12f), boardM, true);
                var ss = wb.gameObject.AddComponent<StreetSign>();
                ss.welcome = true; ss.showWithin = 90f;
                ss.text = "Welcome to Pine Hollow\n\nCedar Ave: turn left on West Ave\nMaple St: straight on\nBirch Ln: turn right on West Ave";
            }

            // the houses: every lot its own (size, storeys, roof, garage, colours, porch, fence, hedge, shed, trees)
            Color[] wallC = { new(0.93f, 0.89f, 0.78f), new(0.68f, 0.79f, 0.86f), new(0.71f, 0.79f, 0.64f), new(0.86f, 0.78f, 0.62f), new(0.95f, 0.88f, 0.58f), new(0.86f, 0.86f, 0.84f),
                              new(0.90f, 0.70f, 0.62f), new(0.75f, 0.72f, 0.68f), new(0.62f, 0.36f, 0.28f), new(0.55f, 0.62f, 0.72f), new(0.97f, 0.97f, 0.95f), new(0.80f, 0.66f, 0.48f) };
            Color[] roofC = { new(0.25f, 0.25f, 0.27f), new(0.38f, 0.24f, 0.17f), new(0.45f, 0.17f, 0.14f), new(0.26f, 0.32f, 0.40f), new(0.32f, 0.38f, 0.26f), new(0.55f, 0.52f, 0.48f) };
            Color[] doorC = { new(0.36f, 0.2f, 0.12f), new(0.55f, 0.1f, 0.1f), new(0.12f, 0.22f, 0.38f), new(0.15f, 0.32f, 0.2f), new(0.92f, 0.92f, 0.9f), new(0.1f, 0.1f, 0.11f) };
            Color[] shutC = { new(0.15f, 0.22f, 0.35f), new(0.2f, 0.32f, 0.2f), new(0.12f, 0.12f, 0.13f), new(0.5f, 0.14f, 0.12f) };
            Color[] boxC = { new(0.2f, 0.2f, 0.22f), new(0.92f, 0.92f, 0.9f), new(0.6f, 0.12f, 0.1f), new(0.16f, 0.3f, 0.2f) };
            Material[] Mats(string name, Color[] cs, float sm) { var r = new Material[cs.Length]; for (int i = 0; i < cs.Length; i++) r[i] = ColorMat($"{name}_{i}", cs[i], 0f, sm); return r; }
            var walls = Mats("House_Wall", wallC, 0.1f); var roofs = Mats("House_Roof", roofC, 0.15f); var doors = Mats("House_Door", doorC, 0.3f);
            var shutters = Mats("House_Shutter", shutC, 0.2f); var boxMats = Mats("Mailbox", boxC, 0.4f);
            var trims = new[] { ColorMat("House_Trim", new Color(0.95f, 0.95f, 0.93f), 0f, 0.2f), ColorMat("House_Trim_Cream", new Color(0.9f, 0.85f, 0.7f), 0f, 0.2f), ColorMat("House_Trim_Dark", new Color(0.3f, 0.28f, 0.26f), 0f, 0.2f) };
            var glass = ColorMat("House_Window", new Color(0.16f, 0.22f, 0.28f), 0.2f, 0.9f);
            var brick = ColorMat("House_Chimney", new Color(0.52f, 0.27f, 0.2f), 0f, 0.1f);
            var postMat = ColorMat("Mailbox_Post", new Color(0.35f, 0.25f, 0.16f), 0f, 0.2f);
            var fenceW = ColorMat("Fence_White", new Color(0.94f, 0.94f, 0.92f), 0f, 0.2f);
            var fenceB = ColorMat("Fence_Wood", new Color(0.55f, 0.4f, 0.26f), 0f, 0.15f);
            var hedgeM = Flat("Hedge", new Color(0.18f, 0.38f, 0.14f));
            var shedM = ColorMat("Shed", new Color(0.6f, 0.5f, 0.38f), 0f, 0.15f);
            var bed = Flat("Flower_Bed", new Color(0.32f, 0.2f, 0.12f));
            var flowers = new[] { Flat("Flowers_Red", new Color(0.85f, 0.2f, 0.25f)), Flat("Flowers_Yellow", new Color(0.95f, 0.8f, 0.25f)), Flat("Flowers_Purple", new Color(0.55f, 0.35f, 0.8f)) };
            var trunk = Flat("Bark", new Color(0.4f, 0.25f, 0.14f));
            var leafs = new[] { Flat("Leaves", new Color(0.36f, 0.62f, 0.2f)), Flat("Leaves_Dark", new Color(0.24f, 0.48f, 0.18f)), Flat("Leaves_Autumn", new Color(0.78f, 0.45f, 0.15f)) };
            var needles = Flat("Pine_Needles", new Color(0.16f, 0.36f, 0.2f));
            var treeMeshes = new List<(Mesh mesh, bool pine)>();
            foreach (var (nm, pine) in new[] { ("Broadleaf_11", false), ("Broadleaf_12", false), ("Pine_1", true), ("Pine_2", true) })
            { var tm = AssetDatabase.LoadAssetAtPath<Mesh>($"{Gen}/Meshes/{nm}.asset"); if (tm != null) treeMeshes.Add((tm, pine)); }
            var roofCache = new Dictionary<string, Mesh>();
            Mesh RoofOf(bool hip, float w, float d, float rise)
            {
                string key = $"{(hip ? "Hip" : "Gable")}_{w:0.0}_{d:0.0}_{rise:0.0}";
                if (!roofCache.TryGetValue(key, out var mm)) roofCache[key] = mm = hip ? HipRoofMesh(w, d, rise, "House_Roof_" + key) : RoofMesh(w, d, rise, "House_Roof_" + key);
                return mm;
            }
            void Dmg(GameObject go, House h, string id, string what, float pen, float fine, Transform tip = null)
            {
                var d = go.AddComponent<Damageable>();
                d.house = h; d.id = id; d.what = what; d.penalty = pen; d.fine = fine; d.tipsOver = tip != null; d.tipRoot = tip;
            }

            string[] firsts = { "Linda", "Mike", "Sarah", "Dave", "Karen", "Tom", "Jenny", "Bob", "Maria", "Steve", "Amy", "Greg", "Nancy", "Paul", "Lisa", "Rick", "Donna", "Jim", "Carol", "Gary", "Beth", "Frank", "Julie", "Ron", "Tina", "Ed", "Megan", "Hank", "Rosa", "Doug", "Kelly", "Walt", "Sue", "Luis", "Pam", "Ray", "Joy", "Earl", "Ann", "Vic" };
            string[] lasts = { "Miller", "Johnson", "Garcia", "Brown", "Wilson", "Taylor", "Anderson", "Thomas", "Moore", "Martin", "Jackson", "White", "Harris", "Clark", "Lewis", "Walker", "Hall", "Young", "King", "Wright", "Lopez", "Hill", "Scott", "Green", "Adams", "Baker", "Nelson", "Carter", "Mitchell", "Perez", "Roberts", "Turner", "Phillips", "Campbell", "Parker", "Evans", "Edwards", "Collins", "Stewart", "Morris" };
            var rng = new System.Random(77);
            float R() => (float)rng.NextDouble();
            const float LW = 11f, LD = 20.5f;
            int index = 0;
            for (int s = -1; s <= 1; s++)
            {
                float zs = CityCenter.z + s * StreetGap;
                foreach (int side in new[] { 1, -1 })
                    for (int j = 0; j < 8; j++)
                    {
                        float lotX = CityCenter.x - 92f + LotW / 2f + LotW * j;
                        var lot = new GameObject($"House {index}").transform;
                        lot.SetParent(root, false);
                        lot.position = new Vector3(lotX, 0f, zs + side * (StreetHalf + Walk));
                        lot.rotation = Quaternion.Euler(0f, side > 0 ? 0f : 180f, 0f);
                        var house = lot.gameObject.AddComponent<House>();

                        float dx = ((j + (side > 0 ? 0 : 1)) % 2 == 0) ? -7f : 7f;   // the driveway's side of the lot
                        int ys = dx < 0 ? 1 : -1;                                     // the open yard's side
                        bool dbl = R() < 0.25f;
                        float dw = dbl ? 5.6f : 4f, dcx = dx - (dbl ? ys * 0.6f : 0f);
                        float w = 9.5f + R() * 2.5f, hz0 = 8f + R() * 2.5f, hz1 = Mathf.Min(hz0 + 7f + R() * 2.5f, 18.3f), dep = hz1 - hz0;
                        float hx0, hx1;
                        if (ys > 0) { hx0 = dcx - dw / 2f - 0.6f; hx1 = hx0 + w; } else { hx1 = dcx + dw / 2f + 0.6f; hx0 = hx1 - w; }
                        float hcx = (hx0 + hx1) / 2f, hcz = (hz0 + hz1) / 2f;
                        bool two = R() < 0.3f;
                        float wallH = two ? 5.6f : 3f;
                        float rs = R(); int roofStyle = rs < 0.45f ? 0 : rs < 0.7f ? 1 : rs < 0.9f ? 2 : 3;
                        float rise = 1.6f + R() * 1.4f;
                        var wallM = walls[rng.Next(walls.Length)]; var roofM = roofs[rng.Next(roofs.Length)];
                        var trim = trims[rng.Next(trims.Length)]; var doorM = doors[rng.Next(doors.Length)];

                        var body = Part(lot, "Walls", new Vector3(hcx, wallH / 2f, hcz), new Vector3(w, wallH, dep), wallM, true);
                        GameObjectUtility.SetStaticEditorFlags(body, StaticEditorFlags.BatchingStatic);
                        Dmg(body, house, "house", "house", 0.2f, 1500f);
                        // the roof: gable (ridge along the street or facing it), hip, or flat with a parapet
                        if (roofStyle == 3)
                        {
                            Part(lot, "Roof", new Vector3(hcx, wallH + 0.15f, hcz), new Vector3(w + 0.4f, 0.3f, dep + 0.4f), roofM);
                            Part(lot, "Parapet", new Vector3(hcx, wallH + 0.45f, hz0 - 0.1f), new Vector3(w + 0.4f, 0.3f, 0.2f), trim);
                        }
                        else
                        {
                            var rf = new GameObject("Roof"); rf.transform.SetParent(lot, false);
                            rf.transform.localPosition = new Vector3(hcx, wallH, hcz);
                            Mesh rm = roofStyle == 1 ? RoofOf(false, dep + 0.8f, w + 0.8f, rise) : RoofOf(roofStyle == 2, w + 0.8f, dep + 0.8f, rise);
                            if (roofStyle == 1) rf.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                            rf.AddComponent<MeshFilter>().sharedMesh = rm;
                            rf.AddComponent<MeshRenderer>().sharedMaterial = roofM;
                            GameObjectUtility.SetStaticEditorFlags(rf, StaticEditorFlags.BatchingStatic);
                        }
                        if (R() < 0.45f) Part(lot, "Chimney", new Vector3(hx0 + (ys > 0 ? w - 2f : 2f), wallH + rise * 0.55f + 0.6f, hz1 - 2.2f), new Vector3(0.8f, rise + 1.2f, 0.8f), brick);
                        // garage, front door, windows (and shutters), a second storey's windows
                        Part(lot, "Garage Door", new Vector3(dcx, 1.15f, hz0 - 0.04f), new Vector3(dbl ? 5.2f : 3.2f, 2.3f, 0.1f), trim);
                        float g0 = ys > 0 ? dcx + dw / 2f + 0.6f : hx0, g1 = ys > 0 ? hx1 : dcx - dw / 2f - 0.6f; // the part of the front that isn't garage
                        float doorX = Mathf.Lerp(g0, g1, ys > 0 ? 0.3f : 0.7f);
                        Part(lot, "Front Door", new Vector3(doorX, 1.05f, hz0 - 0.04f), new Vector3(1f, 2.1f, 0.1f), doorM);
                        bool shut = R() < 0.4f; var shutM = shutters[rng.Next(shutters.Length)];
                        for (float wx = g0 + 1.2f; wx < g1 - 0.9f; wx += 2.4f)
                        {
                            if (Mathf.Abs(wx - doorX) < 1.3f) continue;
                            Part(lot, "Window", new Vector3(wx, 1.6f, hz0 - 0.04f), new Vector3(1.5f, 1.2f, 0.1f), glass);
                            if (shut) { Part(lot, "Shutter", new Vector3(wx - 0.95f, 1.6f, hz0 - 0.06f), new Vector3(0.35f, 1.3f, 0.06f), shutM); Part(lot, "Shutter", new Vector3(wx + 0.95f, 1.6f, hz0 - 0.06f), new Vector3(0.35f, 1.3f, 0.06f), shutM); }
                        }
                        if (two) for (float wx = hx0 + 1.4f; wx < hx1 - 1f; wx += 2.6f) Part(lot, "Window Upstairs", new Vector3(wx, 4.3f, hz0 - 0.04f), new Vector3(1.4f, 1.1f, 0.1f), glass);
                        Part(lot, "Window Back", new Vector3(hcx - 2f, 1.6f, hz1 + 0.04f), new Vector3(1.5f, 1.2f, 0.1f), glass);
                        Part(lot, "Window Back", new Vector3(hcx + 2f, 1.6f, hz1 + 0.04f), new Vector3(1.5f, 1.2f, 0.1f), glass);
                        // a porch, or just a step
                        if (R() < 0.35f)
                        {
                            Part(lot, "Porch", new Vector3(doorX, 0.12f, hz0 - 1.0f), new Vector3(3f, 0.24f, 2f), walk);
                            Part(lot, "Porch Roof", new Vector3(doorX, 2.65f, hz0 - 1.0f), new Vector3(3.2f, 0.15f, 2.2f), roofM);
                            foreach (float px in new[] { -1.35f, 1.35f })
                                Dmg(Part(lot, "Porch Post", new Vector3(doorX + px, 1.35f, hz0 - 1.9f), new Vector3(0.15f, 2.5f, 0.15f), trim, true), house, px < 0 ? "porch post L" : "porch post R", "porch", 0.1f, 600f);
                        }
                        else Part(lot, "Doorstep", new Vector3(doorX, 0.08f, hz0 - 0.6f), new Vector3(1.6f, 0.16f, 1.1f), walk);
                        if (R() < 0.3f)
                        {
                            float bx0 = Mathf.Min(g0, g1) + 0.3f, bx1 = Mathf.Max(g0, g1) - 0.3f;
                            Part(lot, "Flower Bed", new Vector3((bx0 + bx1) / 2f, 0.08f, hz0 - 0.55f), new Vector3(bx1 - bx0, 0.16f, 0.7f), bed);
                            var fl = flowers[rng.Next(flowers.Length)];
                            for (float fx = bx0 + 0.3f; fx < bx1 - 0.2f; fx += 0.7f) if (Mathf.Abs(fx - doorX) > 0.9f) Part(lot, "Flowers", new Vector3(fx, 0.3f, hz0 - 0.55f), new Vector3(0.35f, 0.3f, 0.35f), fl);
                        }

                        // the mailbox at the curb, by the driveway
                        var mb = new GameObject("Mailbox").transform; mb.SetParent(lot, false);
                        mb.localPosition = new Vector3(dcx + ys * (dw / 2f + 0.8f), 0f, 0.5f);
                        Dmg(Part(mb, "Post", new Vector3(0f, 0.55f, 0f), new Vector3(0.1f, 1.1f, 0.1f), postMat, true), house, "mailbox", "mailbox", 0.1f, 150f, mb);
                        Dmg(Part(mb, "Box", new Vector3(0f, 1.2f, 0f), new Vector3(0.28f, 0.26f, 0.5f), boxMats[rng.Next(boxMats.Length)], true), house, "mailbox", "mailbox", 0.1f, 150f, mb);
                        // a hedge along the front of the yard
                        if (R() < 0.3f)
                        {
                            float e0 = dcx + ys * (dw / 2f + 2.6f), e1 = ys * (LW - 0.7f);
                            if (Mathf.Abs(e1 - e0) > 2f)
                                Dmg(Part(lot, "Hedge", new Vector3((e0 + e1) / 2f, 0.45f, 1.4f), new Vector3(Mathf.Abs(e1 - e0), 0.9f, 0.8f), hedgeM, true), house, "hedge", "hedge", 0.05f, 300f);
                        }
                        // a fence round the back yard (white pickets or wood), in sections that get knocked over
                        if (R() < 0.4f)
                        {
                            var fm = R() < 0.5f ? fenceW : fenceB;
                            int n = 0;
                            for (float fx = -LW + 0.3f; fx < LW - 0.3f; fx += 2.2f)
                            {
                                float sx = Mathf.Min(2.2f, LW - 0.3f - fx);
                                var sec = new GameObject($"Fence {n}").transform; sec.SetParent(lot, false);
                                sec.localPosition = new Vector3(fx + sx / 2f, 0f, LD - 0.25f);
                                Dmg(Part(sec, "Fence", new Vector3(0f, 0.55f, 0f), new Vector3(sx - 0.05f, 1.1f, 0.08f), fm, true), house, $"fence {n}", "fence", 0.06f, 200f, sec);
                                n++;
                            }
                            for (float fz = hz1 + 0.5f; fz < LD - 0.6f; fz += 2.2f)
                            {
                                float sz = Mathf.Min(2.2f, LD - 0.5f - fz);
                                var sec = new GameObject($"Fence {n}").transform; sec.SetParent(lot, false);
                                sec.localPosition = new Vector3(ys * (LW - 0.25f), 0f, fz + sz / 2f);
                                Dmg(Part(sec, "Fence", new Vector3(0f, 0.55f, 0f), new Vector3(0.08f, 1.1f, sz - 0.05f), fm, true), house, $"fence {n}", "fence", 0.06f, 200f, sec);
                                n++;
                            }
                        }
                        // a garden shed in the back corner
                        if (R() < 0.25f && hz1 < LD - 4f)
                        {
                            var sh = Part(lot, "Shed", new Vector3(ys * (LW - 2.4f), 1.1f, LD - 2.2f), new Vector3(2.6f, 2.2f, 2f), shedM, true);
                            Dmg(sh, house, "shed", "shed", 0.12f, 800f);
                            Part(lot, "Shed Roof", new Vector3(ys * (LW - 2.4f), 2.3f, LD - 2.2f), new Vector3(2.9f, 0.18f, 2.3f), roofM);
                            hard.Add(WorldRect(lot, new Vector3(ys * (LW - 2.4f) - 1.4f, 0f, LD - 3.3f), new Vector3(ys * (LW - 2.4f) + 1.4f, 0f, LD - 1.1f)));
                        }
                        // trees: out back and in the front yard
                        int trees = rng.Next(3), tn = 0;
                        for (int t = 0; t < trees && treeMeshes.Count > 0; t++)
                        {
                            bool back = (LD - hz1) > 3.2f && R() < 0.6f;
                            Vector3 tp = back ? new Vector3((R() * 2f - 1f) * (LW - 2.5f), 0f, Mathf.Lerp(hz1 + 1.4f, LD - 1.3f, R()))
                                              : new Vector3(ys * (3.5f + R() * (LW - 6f)), 0f, 3.2f + R() * Mathf.Max(0.1f, hz0 - 5.2f));
                            if (!back && Mathf.Abs(tp.x - dcx) < dw / 2f + 1.5f) continue;
                            var (tmesh, pine) = treeMeshes[rng.Next(treeMeshes.Count)];
                            var tr = new GameObject($"Yard Tree {tn}"); tr.transform.SetParent(lot, false);
                            tr.transform.localPosition = tp;
                            tr.transform.localRotation = Quaternion.Euler(0f, rng.Next(360), 0f);
                            tr.transform.localScale = Vector3.one * (pine ? 0.55f + R() * 0.35f : 0.75f + R() * 0.5f);
                            tr.AddComponent<MeshFilter>().sharedMesh = tmesh;
                            tr.AddComponent<MeshRenderer>().sharedMaterials = new[] { trunk, pine ? needles : leafs[rng.Next(leafs.Length)] };
                            var cc = tr.AddComponent<CapsuleCollider>(); cc.radius = 0.3f; cc.height = 3f; cc.center = new Vector3(0f, 1.5f, 0f);
                            Dmg(tr, house, $"tree {tn}", "tree", 0.1f, 500f);
                            tn++;
                        }

                        house.index = index;
                        house.first = firsts[rng.Next(firsts.Length)]; house.last = lasts[rng.Next(lasts.Length)];
                        house.street = StreetNames[s + 1];
                        house.number = side > 0 ? 101 + j * 2 : 100 + j * 2 + 2;
                        house.mailbox = mb;
                        house.houseLocal = Rect.MinMaxRect(hx0, hz0, hx1, hz1);
                        house.drivewayLocal = new Rect(dcx - dw / 2f, 0f, dw, hz0);
                        house.lotHalfWidth = LW; house.lotDepth = LD; house.yardSide = ys; house.northSide = side > 0;
                        house.driveway = WorldRect(lot, new Vector3(dcx - dw / 2f, 0f, 0f), new Vector3(dcx + dw / 2f, 0f, hz0));
                        house.ditchA = lot.TransformPoint(new Vector3(ys * 9.6f, 0f, 1.2f));
                        house.ditchB = lot.TransformPoint(new Vector3(ys * 9.6f, 0f, 19.2f));
                        var foot = WorldRect(lot, new Vector3(hx0 - 0.4f, 0f, hz0 - 0.4f), new Vector3(hx1 + 0.4f, 0f, hz1 + 0.4f));
                        hard.Add(foot);
                        index++;
                    }
            }

            // the landscape supply: a concrete pad off the west avenue with an overhead bin to drive under
            var supply = new Vector3(CityCenter.x - 82f, 0f, CityCenter.z - 96f);
            {
                var pad = Block(root, "Landscape Supply Pad", supply + new Vector3(0.5f, 0.07f - 0.15f, 0f), new Vector3(37f, 0.3f, 22f), m.stone, 3f);
                GameObjectUtility.SetStaticEditorFlags(pad, StaticEditorFlags.BatchingStatic);
                hard.Add(new Rect(supply.x - 18f, supply.z - 11f, 37f, 22f));
                var bin = new GameObject("Landscape Supply Bin").transform;
                bin.SetParent(root, false);
                bin.position = supply + new Vector3(0f, 0.07f, 0f);
                var steelM = ColorMat("Steel", new Color(0.55f, 0.55f, 0.56f), 0.6f, 0.35f);
                var yellow = ColorMat("Paint_ExcavatorYellow", new Color(0.85f, 0.62f, 0.08f), 0f, 0.45f);
                foreach (var lx in new[] { -2.6f, 2.6f })
                    foreach (var lz in new[] { -3.6f, 3.6f })
                        Part(bin, "Leg", new Vector3(lx, 3.4f, lz), new Vector3(0.35f, 6.8f, 0.35f), steelM, true);
                Part(bin, "Hopper", new Vector3(0f, 7.6f, 0f), new Vector3(5.8f, 3.2f, 7.6f), yellow, true);
                Part(bin, "Hopper Funnel", new Vector3(0f, 5.6f, 0f), new Vector3(2.4f, 0.9f, 2.4f), yellow, true);
                Part(bin, "Chute", new Vector3(0f, 4.95f, 0f), new Vector3(1.1f, 0.5f, 1.1f), steelM, true);
                var chute = Marker(bin, "Chute Mouth", new Vector3(0f, 4.55f, 0f));
                var panel = Part(bin, "Control Box", new Vector3(3.6f, 1.25f, -4.6f), new Vector3(0.7f, 0.9f, 0.4f), steelM, true);
                Part(bin, "Control Post", new Vector3(3.6f, 0.45f, -4.6f), new Vector3(0.15f, 0.9f, 0.15f), steelM, true);
                // piles of what it sells, for show
                Part(bin, "Gravel Heap", new Vector3(-10f, 0.8f, 7f), new Vector3(6f, 1.6f, 4.5f), Flat("Field_Gravel", new Color(0.55f, 0.52f, 0.47f), 0.12f), true).transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
                Part(bin, "Sand Heap", new Vector3(-2f, 0.7f, 8f), new Vector3(5f, 1.4f, 3.5f), Flat("Field_Sand", new Color(0.83f, 0.71f, 0.47f)), true);
                Part(bin, "Topsoil Heap", new Vector3(6f, 0.7f, 8f), new Vector3(5f, 1.4f, 3.5f), Flat("Field_Topsoil", new Color(0.5f, 0.31f, 0.17f)), true);
                var gb = bin.gameObject.AddComponent<GravelBin>();
                gb.chute = chute;
                MapMark(bin, "Landscape supply", new Color(0.55f, 0.85f, 0.4f), new Vector3(6f, 0f, -8f));
            }

            // the yards: a second diggable ground
            BuildCityField(hard);

            var town = new GameObject("Town (marker)").transform;
            town.SetParent(root, false);
            town.position = new Vector3(x0 + 8f, 0f, CityCenter.z + 6f);
            MapMark(town, "Town", new Color(0.95f, 0.75f, 0.55f), Vector3.zero);
            var jobsC = new GameObject("Customer Jobs").AddComponent<Jobs>();
            jobsC.trenchMaterial = CabGlass("Trench_Marker", new Color(0.35f, 0.75f, 1f), 0.3f);
            jobsC.beaconMaterial = CabGlass("Job_Beacon", new Color(1f, 0.55f, 0.1f), 0.28f);
            // a soft glow so they read in the grass and from across town
            jobsC.trenchMaterial.SetColor("_EmissiveColor", new Color(0.25f, 0.6f, 1f) * 0.5f);
            jobsC.beaconMaterial.SetColor("_EmissiveColor", new Color(1f, 0.5f, 0.1f) * 0.8f);
            EditorUtility.SetDirty(jobsC.trenchMaterial); EditorUtility.SetDirty(jobsC.beaconMaterial);
        }

        static Rect WorldRect(Transform t, Vector3 a, Vector3 b)
        {
            Vector3 p = t.TransformPoint(a), q = t.TransformPoint(b);
            return Rect.MinMaxRect(Mathf.Min(p.x, q.x), Mathf.Min(p.z, q.z), Mathf.Max(p.x, q.x), Mathf.Max(p.z, q.z));
        }

        /// <summary>A gable roof (ridge along X) with eaves, centred on its base, `w` x `d`, `rise` high.</summary>
        static Mesh RoofMesh(float w, float d, float rise, string name = "House_Roof")
        {
            var mb = new MeshBuilder(1);
            float hw = w / 2f, hd = d / 2f;
            Vector3 a = new(-hw, 0, -hd), b = new(hw, 0, -hd), c = new(hw, 0, hd), e = new(-hw, 0, hd);
            Vector3 r0 = new(-hw, rise, 0), r1 = new(hw, rise, 0);
            Vector3 front = new(0, 1, -1), back = new(0, 1, 1);
            mb.Tri(0, a, b, r1, front); mb.Tri(0, a, r1, r0, front);
            mb.Tri(0, e, c, r1, back); mb.Tri(0, e, r1, r0, back);
            mb.Tri(0, a, e, r0, Vector3.left); mb.Tri(0, b, c, r1, Vector3.right);
            mb.Tri(0, a, b, c, Vector3.down); mb.Tri(0, a, c, e, Vector3.down);
            return mb.Save(name);
        }

        /// <summary>A hip roof: sloped on all four sides, a short ridge along X (a pyramid when it's square).</summary>
        static Mesh HipRoofMesh(float w, float d, float rise, string name)
        {
            var mb = new MeshBuilder(1);
            float hw = w / 2f, hd = d / 2f, rx = Mathf.Max(0f, hw - hd);
            Vector3 a = new(-hw, 0, -hd), b = new(hw, 0, -hd), c = new(hw, 0, hd), e = new(-hw, 0, hd);
            Vector3 r0 = new(-rx, rise, 0), r1 = new(rx, rise, 0);
            mb.Tri(0, a, b, r1, new Vector3(0, 1, -1)); mb.Tri(0, a, r1, r0, new Vector3(0, 1, -1));
            mb.Tri(0, e, c, r1, new Vector3(0, 1, 1)); mb.Tri(0, e, r1, r0, new Vector3(0, 1, 1));
            mb.Tri(0, a, e, r0, new Vector3(-1, 1, 0)); mb.Tri(0, b, c, r1, new Vector3(1, 1, 0));
            mb.Tri(0, a, b, c, Vector3.down); mb.Tri(0, a, c, e, Vector3.down);
            return mb.Save(name);
        }

        /// <summary>The town ground's heights: the hills under it (`ground` = world height at x, z), with gentle facets.</summary>
        static float[,] TownFieldHeights(int res, System.Func<float, float, float> ground)
        {
            float cell = CityFieldSize / (res - 1);
            var o = CityFieldOrigin;
            var hs = new float[res, res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    var w = o + new Vector3(x * cell, 0f, z * cell);
                    float y = ground(w.x, w.z);
                    bool edge = x == 0 || z == 0 || x == res - 1 || z == res - 1;
                    if (!edge) y += (Mathf.PerlinNoise(w.x * 0.4f + 5f, w.z * 0.4f + 2f) - 0.5f) * 0.05f;
                    hs[z, x] = (y - o.y) / FieldHeight;
                }
            return hs;
        }

        /// <summary>
        /// Fix the saved town ground in place (keeps the scene as it is): it was made 449 a side, which Unity rounded
        /// to 513, so the hills were squeezed toward one corner and the east and north strips were left 8 m down.
        /// </summary>
        public static void RepairTownField()
        {
            var east = AssetDatabase.LoadAssetAtPath<TerrainData>($"{Gen}/TerrainDataEast.asset");
            var town = AssetDatabase.LoadAssetAtPath<TerrainData>($"{Gen}/TownFieldData.asset");
            Vector3 ep = new Vector3(300f, -2f, -300f);
            float Ground(float x, float z) => east.GetInterpolatedHeight((x - ep.x) / east.size.x, (z - ep.z) / east.size.z) + ep.y;
            town.SetHeights(0, 0, TownFieldHeights(town.heightmapResolution, Ground));
            EditorUtility.SetDirty(town);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Repair] town ground rebuilt: {town.heightmapResolution} a side over {town.size.x} m");
        }

        static void BuildCityField(List<Rect> hard)
        {
            var surf = FieldMaterials();
            // (a heightmap is 2^n + 1 samples a side: ask for anything else and Unity rounds it up, so read back
            // what it really is: 513 over 224 m, 0.44 m cells)
            var data = new TerrainData { heightmapResolution = 513, size = new Vector3(CityFieldSize, FieldHeight, CityFieldSize) };
            var o = CityFieldOrigin;
            data.SetHeights(0, 0, TownFieldHeights(data.heightmapResolution, (x, z) => eastTile.SampleHeight(new Vector3(x, 0f, z)) + eastTile.transform.position.y));
            AssetDatabase.CreateAsset(data, $"{Gen}/TownFieldData.asset");
            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Town Ground (diggable yards)";
            go.transform.position = o;
            go.GetComponent<Terrain>().drawHeightmap = false;
            var site = go.AddComponent<DigSite>();
            site.primary = false;
            site.surfaceMaterials = surf;
            site.pileMaterial = surf[2];
            site.rockMaterial = Flat("Boulder_Rock", new Color(0.5f, 0.48f, 0.45f), 0.15f);
            site.grassTuftMesh = tuftMesh; site.grassTuftMaterial = tuftMat;
            site.claimRect = new Rect(0f, 0f, 0f, 0f);
            site.seed = 4242;
            site.nuggetCount = 0;
            site.boulderDensity = 0.6f;
            site.hardRects = hard;

            // a hole in the east terrain where the yards are
            var md = eastTile.terrainData;
            int hr = md.holesResolution;
            Vector3 mp = eastTile.transform.position;
            float hc = md.size.x / hr;
            int i0 = Mathf.CeilToInt((o.x - mp.x) / hc), i1 = Mathf.FloorToInt((o.x + CityFieldSize - mp.x) / hc) - 1;
            int j0 = Mathf.CeilToInt((o.z - mp.z) / hc), j1 = Mathf.FloorToInt((o.z + CityFieldSize - mp.z) / hc) - 1;
            md.SetHoles(i0, j0, new bool[j1 - j0 + 1, i1 - i0 + 1]);
            EditorUtility.SetDirty(md);
        }
    }
}
