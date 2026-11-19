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

            // the houses
            var roof = RoofMesh(11.8f, 8.8f, 2.3f);
            Color[] wallC = { new(0.93f, 0.89f, 0.78f), new(0.68f, 0.79f, 0.86f), new(0.71f, 0.79f, 0.64f), new(0.86f, 0.78f, 0.62f), new(0.95f, 0.88f, 0.58f), new(0.86f, 0.86f, 0.84f), new(0.90f, 0.70f, 0.62f), new(0.75f, 0.72f, 0.68f) };
            Color[] roofC = { new(0.25f, 0.25f, 0.27f), new(0.38f, 0.24f, 0.17f), new(0.45f, 0.17f, 0.14f), new(0.26f, 0.32f, 0.40f) };
            var walls = new Material[wallC.Length]; for (int i = 0; i < wallC.Length; i++) walls[i] = ColorMat($"House_Wall_{i}", wallC[i], 0f, 0.1f);
            var roofs = new Material[roofC.Length]; for (int i = 0; i < roofC.Length; i++) roofs[i] = ColorMat($"House_Roof_{i}", roofC[i], 0f, 0.15f);
            var trim = ColorMat("House_Trim", new Color(0.95f, 0.95f, 0.93f), 0f, 0.2f);
            var door = ColorMat("House_Door", new Color(0.36f, 0.2f, 0.12f), 0f, 0.3f);
            var glass = ColorMat("House_Window", new Color(0.16f, 0.22f, 0.28f), 0.2f, 0.9f);
            var brick = ColorMat("House_Chimney", new Color(0.52f, 0.27f, 0.2f), 0f, 0.1f);
            var postMat = ColorMat("Mailbox_Post", new Color(0.35f, 0.25f, 0.16f), 0f, 0.2f);
            var boxMat = ColorMat("Mailbox", new Color(0.2f, 0.2f, 0.22f), 0.4f, 0.4f);
            var trunk = Flat("Bark", new Color(0.4f, 0.25f, 0.14f));
            var leaf = Flat("Leaves", new Color(0.36f, 0.62f, 0.2f));
            var tree = AssetDatabase.LoadAssetAtPath<Mesh>($"{Gen}/Meshes/Broadleaf_11.asset");

            string[] firsts = { "Linda", "Mike", "Sarah", "Dave", "Karen", "Tom", "Jenny", "Bob", "Maria", "Steve", "Amy", "Greg", "Nancy", "Paul", "Lisa", "Rick", "Donna", "Jim", "Carol", "Gary", "Beth", "Frank", "Julie", "Ron", "Tina", "Ed", "Megan", "Hank", "Rosa", "Doug", "Kelly", "Walt", "Sue", "Luis", "Pam", "Ray", "Joy", "Earl", "Ann", "Vic" };
            string[] lasts = { "Miller", "Johnson", "Garcia", "Brown", "Wilson", "Taylor", "Anderson", "Thomas", "Moore", "Martin", "Jackson", "White", "Harris", "Clark", "Lewis", "Walker", "Hall", "Young", "King", "Wright", "Lopez", "Hill", "Scott", "Green", "Adams", "Baker", "Nelson", "Carter", "Mitchell", "Perez", "Roberts", "Turner", "Phillips", "Campbell", "Parker", "Evans", "Edwards", "Collins", "Stewart", "Morris" };
            var rng = new System.Random(77);
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
                        float dx = ((j + (side > 0 ? 0 : 1)) % 2 == 0) ? -7f : 7f;   // the driveway's side of the lot
                        float hx0 = dx < 0 ? -9.5f : -1.5f, hx1 = hx0 + 11f, hz0 = 9f, hz1 = 17f, hcx = (hx0 + hx1) / 2f;
                        var wallM = walls[rng.Next(walls.Length)];
                        var body = Part(lot, "Walls", new Vector3(hcx, 1.5f, (hz0 + hz1) / 2f), new Vector3(11f, 3f, 8f), wallM, true);
                        GameObjectUtility.SetStaticEditorFlags(body, StaticEditorFlags.BatchingStatic);
                        var rf = new GameObject("Roof"); rf.transform.SetParent(lot, false);
                        rf.transform.localPosition = new Vector3(hcx, 3f, (hz0 + hz1) / 2f);
                        rf.AddComponent<MeshFilter>().sharedMesh = roof;
                        rf.AddComponent<MeshRenderer>().sharedMaterial = roofs[rng.Next(roofs.Length)];
                        GameObjectUtility.SetStaticEditorFlags(rf, StaticEditorFlags.BatchingStatic);
                        Part(lot, "Garage Door", new Vector3(dx, 1.15f, hz0 - 0.04f), new Vector3(3.2f, 2.3f, 0.1f), trim);
                        float doorX = dx < 0 ? hx0 + 6.6f : hx1 - 6.6f, winX = dx < 0 ? hx0 + 9.2f : hx1 - 9.2f;
                        Part(lot, "Front Door", new Vector3(doorX, 1.05f, hz0 - 0.04f), new Vector3(1f, 2.1f, 0.1f), door);
                        Part(lot, "Window", new Vector3(winX, 1.6f, hz0 - 0.04f), new Vector3(1.7f, 1.2f, 0.1f), glass);
                        Part(lot, "Window Back", new Vector3(hcx, 1.6f, hz1 + 0.04f), new Vector3(1.7f, 1.2f, 0.1f), glass);
                        Part(lot, "Doorstep", new Vector3(doorX, 0.08f, hz0 - 0.6f), new Vector3(1.6f, 0.16f, 1.1f), walk);
                        if (rng.NextDouble() < 0.45) Part(lot, "Chimney", new Vector3(hx0 + (dx < 0 ? 8.8f : 2.2f), 4.6f, hz1 - 2.2f), new Vector3(0.8f, 2.4f, 0.8f), brick);
                        // the mailbox at the curb, by the driveway
                        var mb = new GameObject("Mailbox").transform; mb.SetParent(lot, false);
                        mb.localPosition = new Vector3(dx + (dx < 0 ? 2.8f : -2.8f), 0f, 0.5f);
                        Part(mb, "Post", new Vector3(0f, 0.55f, 0f), new Vector3(0.1f, 1.1f, 0.1f), postMat, true);
                        Part(mb, "Box", new Vector3(0f, 1.2f, 0f), new Vector3(0.28f, 0.26f, 0.5f), boxMat, true);
                        // a tree out back
                        if (tree != null && rng.NextDouble() < 0.6)
                        {
                            var tr = new GameObject("Yard Tree"); tr.transform.SetParent(lot, false);
                            tr.transform.localPosition = new Vector3(-dx * 0.25f, 0f, 19.2f);
                            tr.transform.localRotation = Quaternion.Euler(0f, rng.Next(360), 0f);
                            tr.transform.localScale = Vector3.one * (0.8f + (float)rng.NextDouble() * 0.5f);
                            tr.AddComponent<MeshFilter>().sharedMesh = tree;
                            tr.AddComponent<MeshRenderer>().sharedMaterials = new[] { trunk, leaf };
                            var cc = tr.AddComponent<CapsuleCollider>(); cc.radius = 0.3f; cc.height = 3f; cc.center = new Vector3(0f, 1.5f, 0f);
                        }

                        var house = lot.gameObject.AddComponent<House>();
                        house.index = index;
                        house.first = firsts[rng.Next(firsts.Length)]; house.last = lasts[rng.Next(lasts.Length)];
                        house.street = StreetNames[s + 1];
                        house.number = side > 0 ? 101 + j * 2 : 100 + j * 2 + 2;
                        house.mailbox = mb;
                        house.driveway = WorldRect(lot, new Vector3(dx - 2f, 0f, 0f), new Vector3(dx + 2f, 0f, hz0));
                        float ditchX = dx < 0 ? 9.6f : -9.6f;
                        house.ditchA = lot.TransformPoint(new Vector3(ditchX, 0f, 1.2f));
                        house.ditchB = lot.TransformPoint(new Vector3(ditchX, 0f, 19.2f));
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
            new GameObject("Customer Jobs").AddComponent<Jobs>();
        }

        static Rect WorldRect(Transform t, Vector3 a, Vector3 b)
        {
            Vector3 p = t.TransformPoint(a), q = t.TransformPoint(b);
            return Rect.MinMaxRect(Mathf.Min(p.x, q.x), Mathf.Min(p.z, q.z), Mathf.Max(p.x, q.x), Mathf.Max(p.z, q.z));
        }

        /// <summary>A gable roof (ridge along X) with eaves, centred on its base, `w` x `d`, `rise` high.</summary>
        static Mesh RoofMesh(float w, float d, float rise)
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
            return mb.Save("House_Roof");
        }

        static void BuildCityField(List<Rect> hard)
        {
            var surf = FieldMaterials();
            const int res = 449; // 0.5 m cells over 224 m
            var data = new TerrainData { heightmapResolution = res, size = new Vector3(CityFieldSize, FieldHeight, CityFieldSize) };
            float cell = CityFieldSize / (res - 1);
            var o = CityFieldOrigin;
            var hs = new float[res, res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    var w = o + new Vector3(x * cell, 0f, z * cell);
                    float y = eastTile.SampleHeight(w) + eastTile.transform.position.y;
                    bool edge = x == 0 || z == 0 || x == res - 1 || z == res - 1;
                    if (!edge) y += (Mathf.PerlinNoise(w.x * 0.4f + 5f, w.z * 0.4f + 2f) - 0.5f) * 0.05f;
                    hs[z, x] = (y - o.y) / FieldHeight;
                }
            data.SetHeights(0, 0, hs);
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
