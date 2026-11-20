using System.Collections.Generic;
using FPSBase.Mining;
using FPSBase.World;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace FPSBase.EditorTools
{
    /// <summary>
    /// Stylized low-poly look: flat-colour materials, faceted terrain, generated low-poly trees/rocks/bushes,
    /// instanced grass tufts, bright sky. Also builds the deformable dirt field (the work area).
    /// </summary>
    public static partial class ProjectBootstrap
    {
        // The dirt field covers the claim, wash plant, spawn, parking and the heavy-equipment yard (256 m square, 0.5 m cells).
        static readonly Vector3 FieldOrigin = new Vector3(-40f, -8f, -196f);
        const float FieldSize = 256f, FieldHeight = 48f;
        static Rect FieldRect => new Rect(FieldOrigin.x, FieldOrigin.z, FieldSize, FieldSize);

        static Material Flat(string name, Color c, float smooth = 0.08f, float metal = 0f) => ColorMat(name, c, metal, smooth);

        // ------------------------------------------------------------------ materials

        static Mats BuildStylizedMaterials() => new Mats
        {
            ground = Flat("Ground", new Color(0.33f, 0.53f, 0.2f)),
            rock = Flat("Rock", new Color(0.52f, 0.52f, 0.54f)),
            wood = Flat("Wood", new Color(0.62f, 0.42f, 0.24f)),
            stone = Flat("Concrete", new Color(0.7f, 0.68f, 0.64f)),
            metal = Flat("SheetMetal", new Color(0.52f, 0.56f, 0.6f), 0.3f, 0.35f),
        };

        static Material[] FieldMaterials() => new[]
        {
            Flat("Field_GrassA", new Color(0.33f, 0.53f, 0.2f)),
            Flat("Field_GrassB", new Color(0.39f, 0.58f, 0.22f)),
            Flat("Field_LooseA", new Color(0.56f, 0.35f, 0.19f)),
            Flat("Field_LooseB", new Color(0.46f, 0.28f, 0.15f)),
            Flat("Field_Topsoil", new Color(0.5f, 0.31f, 0.17f)),
            Flat("Field_Paydirt", new Color(0.68f, 0.5f, 0.29f)),
            Flat("Field_Deep", new Color(0.58f, 0.54f, 0.46f)),
            Flat("Field_Bedrock", new Color(0.4f, 0.41f, 0.45f), 0.2f),
            Flat("Field_Sand", new Color(0.83f, 0.71f, 0.47f)),
            Flat("Field_Clay", new Color(0.66f, 0.38f, 0.26f), 0.18f),
            Flat("Field_Gravel", new Color(0.55f, 0.52f, 0.47f), 0.12f),
        };

        static Material GrassTuftMaterial()
        {
            var mat = Flat("GrassTuft", new Color(0.4f, 0.6f, 0.23f));
            mat.SetFloat("_DoubleSidedEnable", 1f);
            mat.SetFloat("_DoubleSidedNormalMode", 2f); // same normal on both sides: blades light like the ground
            mat.enableInstancing = true;
            HDShaderUtils.ResetMaterialKeywords(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ------------------------------------------------------------------ meshes

        class MeshBuilder
        {
            public readonly List<Vector3> v = new();
            public readonly List<int>[] t;
            public MeshBuilder(int subs) { t = new List<int>[subs]; for (int i = 0; i < subs; i++) t[i] = new List<int>(); }

            public void Tri(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) (b, c) = (c, b);
                int i = v.Count; v.Add(a); v.Add(b); v.Add(c);
                t[sub].Add(i); t[sub].Add(i + 1); t[sub].Add(i + 2);
            }

            public Mesh Save(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(v);
                m.subMeshCount = t.Length;
                for (int i = 0; i < t.Length; i++) m.SetTriangles(t[i], i);
                m.RecalculateNormals(); m.RecalculateBounds();
                EnsureFolder(Gen + "/Meshes");
                AssetDatabase.CreateAsset(m, $"{Gen}/Meshes/{name}.asset");
                return m;
            }
        }

        /// <summary>A faceted ring frustum (or cone when r1 = 0).</summary>
        static void Frustum(MeshBuilder mb, int sub, Vector3 c, float y0, float y1, float r0, float r1, int sides, System.Random rng, float jitter, bool capBottom)
        {
            float off = (float)rng.NextDouble() * Mathf.PI * 2f;
            var lo = new Vector3[sides]; var hi = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = off + i * Mathf.PI * 2f / sides;
                float j = 1f + ((float)rng.NextDouble() - 0.5f) * jitter;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                lo[i] = c + d * r0 * j + Vector3.up * (y0 + ((float)rng.NextDouble() - 0.5f) * jitter * 0.3f);
                hi[i] = c + d * r1 * j + Vector3.up * y1;
            }
            Vector3 apex = c + Vector3.up * y1;
            for (int i = 0; i < sides; i++)
            {
                int k = (i + 1) % sides;
                Vector3 mid = (lo[i] + lo[k]) * 0.5f; Vector3 outw = mid - new Vector3(c.x, mid.y, c.z) + Vector3.up * 0.2f;
                if (r1 <= 0.001f) mb.Tri(sub, lo[i], lo[k], apex, outw);
                else { mb.Tri(sub, lo[i], lo[k], hi[k], outw); mb.Tri(sub, lo[i], hi[k], hi[i], outw); }
                if (capBottom) mb.Tri(sub, lo[i], lo[k], c + Vector3.up * y0, Vector3.down);
            }
        }

        /// <summary>A lumpy faceted blob (subdivided icosahedron with jittered corners).</summary>
        static void Blob(MeshBuilder mb, int sub, Vector3 c, Vector3 scale, System.Random rng, float jitter)
        {
            float p = (1f + Mathf.Sqrt(5f)) / 2f;
            var pts = new List<Vector3>
            {
                new(-1, p, 0), new(1, p, 0), new(-1, -p, 0), new(1, -p, 0), new(0, -1, p), new(0, 1, p),
                new(0, -1, -p), new(0, 1, -p), new(p, 0, -1), new(p, 0, 1), new(-p, 0, -1), new(-p, 0, 1),
            };
            for (int i = 0; i < pts.Count; i++) pts[i] = pts[i].normalized;
            var faces = new List<int[]>
            {
                new[]{0,11,5}, new[]{0,5,1}, new[]{0,1,7}, new[]{0,7,10}, new[]{0,10,11}, new[]{1,5,9}, new[]{5,11,4}, new[]{11,10,2},
                new[]{10,7,6}, new[]{7,1,8}, new[]{3,9,4}, new[]{3,4,2}, new[]{3,2,6}, new[]{3,6,8}, new[]{3,8,9}, new[]{4,9,5},
                new[]{2,4,11}, new[]{6,2,10}, new[]{8,6,7}, new[]{9,8,1},
            };
            // one subdivision
            var mid = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (mid.TryGetValue(key, out int m)) return m;
                pts.Add(((pts[a] + pts[b]) * 0.5f).normalized);
                mid[key] = pts.Count - 1;
                return pts.Count - 1;
            }
            var sub2 = new List<int[]>();
            foreach (var f in faces)
            {
                int ab = Mid(f[0], f[1]), bc = Mid(f[1], f[2]), ca = Mid(f[2], f[0]);
                sub2.Add(new[] { f[0], ab, ca }); sub2.Add(new[] { f[1], bc, ab }); sub2.Add(new[] { f[2], ca, bc }); sub2.Add(new[] { ab, bc, ca });
            }
            for (int i = 0; i < pts.Count; i++)
                pts[i] = pts[i] * (1f + ((float)rng.NextDouble() - 0.5f) * jitter);
            foreach (var f in sub2)
            {
                Vector3 a = c + Vector3.Scale(pts[f[0]], scale), b = c + Vector3.Scale(pts[f[1]], scale), d = c + Vector3.Scale(pts[f[2]], scale);
                mb.Tri(sub, a, b, d, (a + b + d) / 3f - c);
            }
        }

        static Mesh PineMesh(int seed)
        {
            var rng = new System.Random(seed); var mb = new MeshBuilder(2);
            Frustum(mb, 0, Vector3.zero, 0f, 2.2f, 0.2f, 0.11f, 6, rng, 0.1f, false);
            for (int k = 0; k < 3; k++)
            {
                float y0 = 1.0f + k * 1.15f, y1 = y0 + 2.3f - k * 0.2f, r = 1.55f - k * 0.38f;
                Frustum(mb, 1, Vector3.zero, y0, y1, r, 0f, 7, rng, 0.25f, true);
            }
            return mb.Save("Pine_" + seed);
        }

        static Mesh BroadleafMesh(int seed)
        {
            var rng = new System.Random(seed); var mb = new MeshBuilder(2);
            Frustum(mb, 0, Vector3.zero, 0f, 2.6f, 0.24f, 0.15f, 6, rng, 0.1f, false);
            Blob(mb, 1, new Vector3(0f, 3.4f, 0f), new Vector3(1.7f, 1.35f, 1.7f), rng, 0.28f);
            Blob(mb, 1, new Vector3(0.75f, 2.9f, 0.35f), new Vector3(1.0f, 0.85f, 1.0f), rng, 0.3f);
            return mb.Save("Broadleaf_" + seed);
        }

        static Mesh RockMesh(int seed)
        {
            var rng = new System.Random(seed); var mb = new MeshBuilder(1);
            Blob(mb, 0, Vector3.zero, new Vector3(1.3f, 0.75f, 1.05f), rng, 0.35f);
            return mb.Save("Rock_" + seed);
        }

        static Mesh BushMesh(int seed)
        {
            var rng = new System.Random(seed); var mb = new MeshBuilder(1);
            Blob(mb, 0, new Vector3(0f, 0.35f, 0f), new Vector3(0.75f, 0.55f, 0.75f), rng, 0.3f);
            return mb.Save("Bush_" + seed);
        }

        static Mesh GrassTuftMesh()
        {
            var rng = new System.Random(77);
            var verts = new List<Vector3>(); var idx = new List<int>();
            for (int b = 0; b < 7; b++)
            {
                float a = b * Mathf.PI * 2f / 7f + (float)rng.NextDouble() * 0.6f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var perp = new Vector3(-dir.z, 0f, dir.x);
                Vector3 basePt = dir * (0.03f + (float)rng.NextDouble() * 0.05f);
                float h = 0.26f + (float)rng.NextDouble() * 0.2f;
                int i = verts.Count;
                verts.Add(basePt - perp * 0.035f); verts.Add(basePt + perp * 0.035f);
                verts.Add(basePt + dir * (0.1f + (float)rng.NextDouble() * 0.08f) + Vector3.up * h);
                idx.Add(i); idx.Add(i + 1); idx.Add(i + 2);
            }
            var m = new Mesh { name = "GrassTuft" };
            m.SetVertices(verts);
            m.SetTriangles(idx, 0);
            var n = new Vector3[verts.Count]; for (int i = 0; i < n.Length; i++) n[i] = Vector3.up;
            m.normals = n;
            m.RecalculateBounds();
            EnsureFolder(Gen + "/Meshes");
            AssetDatabase.CreateAsset(m, $"{Gen}/Meshes/GrassTuft.asset");
            return m;
        }

        // ------------------------------------------------------------------ terrain + nature

        static void SetupLowPolyTerrain(Terrain terrain, Mesh tuft, Material tuftMat)
        {
            var lp = terrain.gameObject.AddComponent<LowPolyTerrain>();
            lp.materials = new[]
            {
                Flat("Terrain_GrassA", new Color(0.33f, 0.53f, 0.2f)),
                Flat("Terrain_GrassB", new Color(0.39f, 0.58f, 0.22f)),
                Flat("Terrain_GrassDark", new Color(0.26f, 0.45f, 0.17f)),
                Flat("Terrain_Rock", new Color(0.5f, 0.5f, 0.52f)),
            };
            lp.excludeRect = FieldRect;
            lp.tuftMesh = tuft; lp.tuftMaterial = tuftMat;
            lp.noGrassCenter = new Vector2(60f, 88f); lp.noGrassRadius = 27f;
            terrain.drawHeightmap = false;
        }

        static void ScatterNature(Terrain terrain)
        {
            var root = new GameObject("Nature").transform;
            var rng = new System.Random(1234);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            var trunk = Flat("Bark", new Color(0.4f, 0.25f, 0.14f));
            var pineA = Flat("PineNeedles", new Color(0.13f, 0.36f, 0.17f));
            var pineB = Flat("PineNeedlesLight", new Color(0.18f, 0.44f, 0.19f));
            var leafA = Flat("Leaves", new Color(0.36f, 0.62f, 0.2f));
            var leafB = Flat("LeavesYellow", new Color(0.55f, 0.66f, 0.2f));
            var rockMat = Flat("Boulder", new Color(0.5f, 0.5f, 0.53f));
            var bushMat = Flat("Bush", new Color(0.28f, 0.52f, 0.18f));

            var pines = new[] { PineMesh(1), PineMesh(2), PineMesh(3) };
            var broad = new[] { BroadleafMesh(11), BroadleafMesh(12) };
            var rocks = new[] { RockMesh(21), RockMesh(22), RockMesh(23) };
            var bushes = new[] { BushMesh(31), BushMesh(32) };

            bool Blocked(Vector3 p, float pad)
            {
                var fr = FieldRect;
                if (p.x > fr.xMin - pad && p.x < fr.xMax + pad && p.z > fr.yMin - pad && p.z < fr.yMax + pad) return true;
                if (Vector3.Distance(p, new Vector3(60f, 0f, 88f)) < 32f + pad) return true;       // outpost
                if (p.x > -62f && p.x < 34f && p.z > -14f && p.z < 74f) return true;                // movement course
                if (Mathf.Abs(p.x) > 292f || Mathf.Abs(p.z) > 292f) return true;
                if (RoadDist(p.x, p.z) < 9f + pad) return true;                                     // the road to town
                return false;
            }

            GameObject Spawn(string name, Mesh mesh, Material[] mats, Vector3 p, float scale)
            {
                p.y = terrain.SampleHeight(p) + terrain.transform.position.y - 0.08f * scale;
                var go = new GameObject(name);
                go.transform.SetParent(root, false);
                go.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, R(0f, 360f), 0f));
                go.transform.localScale = Vector3.one * scale;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = mats;
                // (not static-batched: NatureBatcher merges them per tile at load, which draws far fewer)
                return go;
            }

            int trees = 0;
            for (int i = 0; i < 9000 && trees < 1500; i++)
            {
                var p = new Vector3(R(-295f, 295f), 0f, R(-295f, 295f));
                if (Blocked(p, 5f)) continue;
                float forest = Mathf.PerlinNoise(p.x * 0.012f + 40f, p.z * 0.012f + 17f);
                float distPlay = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(70f, 10f));
                bool keep = forest > 0.5f && distPlay > 95f || rng.NextDouble() < 0.035;
                if (!keep) continue;
                bool pine = rng.NextDouble() < 0.7;
                float s = R(0.85f, 1.6f);
                GameObject go = pine
                    ? Spawn("Pine", pines[rng.Next(pines.Length)], new[] { trunk, rng.NextDouble() < 0.5 ? pineA : pineB }, p, s)
                    : Spawn("Tree", broad[rng.Next(broad.Length)], new[] { trunk, rng.NextDouble() < 0.7 ? leafA : leafB }, p, s);
                var cc = go.AddComponent<CapsuleCollider>();
                cc.radius = 0.3f; cc.height = 3f; cc.center = new Vector3(0f, 1.5f, 0f);
                trees++;
            }
            for (int i = 0; i < 260; i++)
            {
                var p = new Vector3(R(-290f, 290f), 0f, R(-290f, 290f));
                if (Blocked(p, 3f)) continue;
                var go = Spawn("Boulder", rocks[rng.Next(rocks.Length)], new[] { rockMat }, p, R(0.4f, 2.2f));
                var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh; mc.convex = true;
            }
            for (int i = 0; i < 500; i++)
            {
                var p = new Vector3(R(-290f, 290f), 0f, R(-290f, 290f));
                if (Blocked(p, 2f)) continue;
                Spawn("Bush", bushes[rng.Next(bushes.Length)], new[] { bushMat }, p, R(0.7f, 1.5f));
            }
            // the hills round the town, east of the main terrain
            ScatterEast(root, (n, mesh, mats, p, s) =>
            {
                var go = Spawn(n, mesh, mats, p, s);
                var q = go.transform.position; q.y = LandY(terrain, p) - 0.08f * s; go.transform.position = q;
                return go;
            }, pines, broad, rocks, bushes, trunk, pineA, pineB, leafA, leafB, rockMat, bushMat);
        }

        // ------------------------------------------------------------------ dirt field (work area)

        static DigSite BuildDirtField(Terrain main, Material[] surf, Mesh tuft, Material tuftMat)
        {
            var data = new TerrainData { heightmapResolution = 513, size = new Vector3(FieldSize, FieldHeight, FieldSize) };
            int res = data.heightmapResolution;
            float cell = FieldSize / (res - 1);
            var hs = new float[res, res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    var w = FieldOrigin + new Vector3(x * cell, 0f, z * cell);
                    float y = main.SampleHeight(w) + main.transform.position.y;
                    bool edge = x == 0 || z == 0 || x == res - 1 || z == res - 1;
                    if (!edge) y += (Mathf.PerlinNoise(w.x * 0.4f + 5f, w.z * 0.4f + 2f) - 0.5f) * 0.07f; // gentle facets
                    hs[z, x] = (y - FieldOrigin.y) / FieldHeight;
                }
            data.SetHeights(0, 0, hs);
            AssetDatabase.CreateAsset(data, $"{Gen}/DirtFieldData.asset");

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Dirt Field (diggable work area)";
            go.transform.position = FieldOrigin;
            var t = go.GetComponent<Terrain>();
            t.drawHeightmap = false;
            var site = go.AddComponent<DigSite>();
            site.surfaceMaterials = surf;
            site.pileMaterial = surf[2];
            site.rockMaterial = Flat("Boulder_Rock", new Color(0.5f, 0.48f, 0.45f), 0.15f);
            site.grassTuftMesh = tuft; site.grassTuftMaterial = tuftMat;
            site.claimRect = new Rect(ClaimOrigin.x, ClaimOrigin.z, ClaimSize.x, ClaimSize.z);

            // Cut a hole in the big terrain where the field sits (only cells fully inside it).
            var md = main.terrainData;
            int hr = md.holesResolution;
            Vector3 mp = main.transform.position;
            float hc = md.size.x / hr;
            int i0 = Mathf.CeilToInt((FieldOrigin.x - mp.x) / hc), i1 = Mathf.FloorToInt((FieldOrigin.x + FieldSize - mp.x) / hc) - 1;
            int j0 = Mathf.CeilToInt((FieldOrigin.z - mp.z) / hc), j1 = Mathf.FloorToInt((FieldOrigin.z + FieldSize - mp.z) / hc) - 1;
            md.SetHoles(i0, j0, new bool[j1 - j0 + 1, i1 - i0 + 1]);
            EditorUtility.SetDirty(md);
            return site;
        }

        /// <summary>Orange-flagged survey stakes around the gold claim so you can see where the gold is.</summary>
        static void BuildClaimStakes(Material wood)
        {
            var root = new GameObject("Claim Stakes").transform;
            var flag = Flat("SurveyFlag", new Color(1f, 0.45f, 0.05f));
            var sign = Flat("SignBoard", new Color(0.92f, 0.88f, 0.78f));
            for (float s = 0f; s < ClaimSize.x * 4f; s += 10f)
            {
                Vector3 p;
                float side = ClaimSize.x;
                if (s < side) p = new Vector3(ClaimOrigin.x + s, 0f, ClaimOrigin.z);
                else if (s < 2 * side) p = new Vector3(ClaimOrigin.x + side, 0f, ClaimOrigin.z + s - side);
                else if (s < 3 * side) p = new Vector3(ClaimOrigin.x + 3 * side - s, 0f, ClaimOrigin.z + side);
                else p = new Vector3(ClaimOrigin.x, 0f, ClaimOrigin.z + 4 * side - s);
                var stake = new GameObject("Stake").transform;
                stake.SetParent(root, false);
                stake.position = p;
                Part(stake, "Post", new Vector3(0f, 0.6f, 0f), new Vector3(0.08f, 1.2f, 0.08f), wood);
                Part(stake, "Flag", new Vector3(0.16f, 1.08f, 0f), new Vector3(0.3f, 0.18f, 0.02f), flag);
            }
            var board = new GameObject("Claim Sign").transform;
            board.SetParent(root, false);
            board.position = new Vector3(ClaimOrigin.x + 4f, 0f, ClaimOrigin.z + ClaimSize.z + 0.5f);
            Part(board, "Leg L", new Vector3(-0.6f, 0.8f, 0f), new Vector3(0.1f, 1.6f, 0.1f), wood);
            Part(board, "Leg R", new Vector3(0.6f, 0.8f, 0f), new Vector3(0.1f, 1.6f, 0.1f), wood);
            Part(board, "Board", new Vector3(0f, 1.45f, 0f), new Vector3(1.6f, 0.7f, 0.06f), sign);
            Part(board, "Stripe", new Vector3(0f, 1.45f, -0.035f), new Vector3(1.4f, 0.12f, 0.01f), flag);
        }

        // ------------------------------------------------------------------ lighting

        static void BuildStylizedLighting()
        {
            var sunGo = new GameObject("Sun");
            sunGo.transform.rotation = Quaternion.Euler(42f, -40f, 0f);
            var sunData = sunGo.AddHDLight(LightType.Directional);
            var sun = sunGo.GetComponent<Light>();
            sun.lightUnit = LightUnit.Lux;
            sun.intensity = 100000f;
            sun.useColorTemperature = true;
            sun.colorTemperature = 5900f;
            sun.shadows = LightShadows.Soft;
            sunData.SetShadowResolutionOverride(true);
            sunData.SetShadowResolution(2048);

            var volGo = new GameObject("Global Volume (Sky, Fog, Post)");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, $"{Gen}/VP_Outdoor.asset");

            var env = profile.Add<VisualEnvironment>(true);
            env.skyType.value = (int)SkyType.PhysicallyBased;
            env.skyAmbientMode.value = SkyAmbientMode.Dynamic;
            profile.Add<PhysicallyBasedSky>(true);

            var clouds = profile.Add<VolumetricClouds>(true);
            clouds.enable.value = true;
            clouds.cloudPreset = VolumetricClouds.CloudPresets.Sparse;

            var fog = profile.Add<Fog>(true);
            fog.enabled.value = true;
            fog.meanFreePath.value = 1400f;
            fog.maximumHeight.value = 150f;
            fog.enableVolumetricFog.value = false;

            var exposure = profile.Add<Exposure>(true);
            exposure.mode.value = ExposureMode.AutomaticHistogram;
            exposure.limitMin.value = 9f;
            exposure.limitMax.value = 15f;

            profile.Add<Tonemapping>(true).mode.value = TonemappingMode.Neutral;
            var color = profile.Add<ColorAdjustments>(true);
            color.saturation.value = 6f;
            color.contrast.value = 8f;
            profile.Add<Bloom>(true).intensity.value = 0.1f;
            profile.Add<ScreenSpaceAmbientOcclusion>(true).intensity.value = 1.1f;
            profile.Add<HDShadowSettings>(true).maxShadowDistance.value = 220f;
            profile.Add<ContactShadows>(true).enable.value = true;

            foreach (var c in profile.components)
            {
                c.name = c.GetType().Name;
                c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(c, profile);
            }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            vol.sharedProfile = profile;
        }
    }
}
