using System.IO;
using System.Linq;
using FPSBase.Player;
using FPSBase.UI;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace FPSBase.EditorTools
{
    /// <summary>
    /// One-click (or command line) project setup:
    ///   Tools > FPS Base > Setup Project And Build Test Map
    ///   Unity.exe -batchmode -executeMethod FPSBase.EditorTools.ProjectBootstrap.RunBatch
    /// Sets up HDRP, imports Poly Haven textures as HDRP materials, and builds the movement test map.
    /// Safe to re-run: it regenerates the generated folder and scene.
    /// </summary>
    public static partial class ProjectBootstrap
    {
        const string PH = "Assets/ThirdParty/PolyHaven";
        const string Gen = "Assets/Game/Generated";
        const string ScenePath = "Assets/Game/Scenes/MovementPlayground.unity";

        [MenuItem("Tools/FPS Base/Setup Project And Build Test Map")]
        public static void Run()
        {
            EnsureFolder(Gen); EnsureFolder(Gen + "/Materials"); EnsureFolder(Gen + "/Masks"); EnsureFolder("Assets/Game/Scenes");
            SetupHdrp();
            var mats = BuildStylizedMaterials();
            BuildScene(mats);
            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectBootstrap] Done. Open " + ScenePath + " and press Play.");
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        // ------------------------------------------------------------------ HDRP

        static void SetupHdrp()
        {
            var asset = GraphicsSettings.defaultRenderPipeline as HDRenderPipelineAsset;
            if (asset == null)
            {
                string path = "Assets/Game/Settings/HDRP_HighQuality.asset";
                EnsureFolder("Assets/Game/Settings");
                asset = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(path);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<HDRenderPipelineAsset>();
                    AssetDatabase.CreateAsset(asset, path);
                }
                GraphicsSettings.defaultRenderPipeline = asset;
            }
            // The claim sits in a hole cut in the big terrain; without this HDRP still draws the big terrain over it.
            var rps = asset.currentPlatformRenderPipelineSettings;
            rps.supportTerrainHole = true;
            rps.supportVolumetricClouds = true;
            asset.currentPlatformRenderPipelineSettings = rps;

            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }
            QualitySettings.SetQualityLevel(QualitySettings.names.Length - 1, false);
            PlayerSettings.colorSpace = ColorSpace.Linear;
            EditorUtility.SetDirty(asset);
        }

        // ------------------------------------------------------------------ materials

        class Mats
        {
            public Material ground, rock, wood, stone, metal;
            public System.Collections.Generic.Dictionary<string, Material> models = new();
        }

        // ------------------------------------------------------------------ scene

        static Mesh tuftMesh;
        static Material tuftMat;

        static void BuildScene(Mats m)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildStylizedLighting();
            var terrain = BuildTerrain(m);
            tuftMesh = GrassTuftMesh();
            tuftMat = GrassTuftMaterial();
            SetupLowPolyTerrain(terrain, tuftMesh, tuftMat);
            BuildEastTerrain();

            var course = new GameObject("MovementCourse").transform;
            course.position = new Vector3(0f, 0f, 0f);

            // Spawn pad
            Block(course, "SpawnPad", new Vector3(0, 0.25f, 0), new Vector3(12, 0.5f, 12), m.stone, 3f);

            // Mantle boxes: 0.8 / 1.2 / 1.6 / 2.0 m
            float[] heights = { 0.8f, 1.2f, 1.6f, 2.0f };
            for (int i = 0; i < heights.Length; i++)
                Block(course, $"Mantle_{heights[i]:0.0}m", new Vector3(-8 + i * 4, 0.5f + heights[i] * 0.5f, 14), new Vector3(2.5f, heights[i], 2.5f), m.stone, 2f);

            // Wall-run corridor: two tall walls with a gap you must wall-run / wall-jump across
            Block(course, "WallRun_L", new Vector3(-5, 3f, 45), new Vector3(1, 6, 30), m.stone, 3f);
            Block(course, "WallRun_R", new Vector3(5, 3f, 45), new Vector3(1, 6, 30), m.stone, 3f);
            Block(course, "WallRun_StartLedge", new Vector3(0, 0.75f, 27), new Vector3(9, 1.5f, 4), m.wood, 2f);
            Block(course, "WallRun_EndLedge", new Vector3(0, 1.5f, 63), new Vector3(9, 3f, 4), m.wood, 2f);

            // Bhop strip: small platforms with gaps
            for (int i = 0; i < 10; i++)
                Block(course, $"Bhop_{i}", new Vector3(25, 0.5f + i * 0.15f, 10 + i * 5.5f), new Vector3(3, 1f, 2.2f), m.wood, 1.5f);

            // Slide ramp (shallow) and surf ramps (steep, > 45 degrees = surfable)
            var slide = Block(course, "SlideRamp", new Vector3(-28, 2.5f, 20), new Vector3(8, 0.5f, 30), m.stone, 3f);
            slide.transform.rotation = Quaternion.Euler(-12f, 0f, 0f);
            Block(course, "SlideRamp_Top", new Vector3(-28, 5.3f, 38), new Vector3(8, 0.5f, 8), m.stone, 3f);
            Block(course, "SlideRamp_Stairs", new Vector3(-28, 2.7f, 44), new Vector3(8, 5.4f, 4), m.stone, 3f);

            var surfL = Block(course, "Surf_L", new Vector3(-48, 8f, 60), new Vector3(10, 0.6f, 80), m.metal, 4f);
            surfL.transform.rotation = Quaternion.Euler(0f, 0f, 55f);
            var surfR = Block(course, "Surf_R", new Vector3(-36, 8f, 60), new Vector3(10, 0.6f, 80), m.metal, 4f);
            surfR.transform.rotation = Quaternion.Euler(0f, 0f, -55f);
            Block(course, "Surf_StartTower", new Vector3(-42, 8f, 14), new Vector3(6, 16, 6), m.stone, 3f);

            BuildDemoBase(m);
            ScatterNature(terrain);
            var player = BuildPlayer(new Vector3(56f, 0.3f, 8f));
            player.transform.rotation = Quaternion.Euler(0f, 150f, 0f);
            BuildMiningArea(m, terrain, player);

            var hud = new GameObject("MovementHUD").AddComponent<MovementHud>();
            hud.movement = player.GetComponent<SourceMovement>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        static Terrain BuildTerrain(Mats m)
        {
            const int res = 513;
            var data = new TerrainData { heightmapResolution = res, size = new Vector3(600f, 60f, 600f) };
            var hs = new float[res, res];
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    float wx = x / (float)(res - 1) * 600f - 300f, wz = y / (float)(res - 1) * 600f - 300f;
                    hs[y, x] = WorldHeight01(wx, wz); // flat play area in the middle (claim, outpost), hills, the pass to town
                }
            data.SetHeights(0, 0, hs);

            AssetDatabase.CreateAsset(data, $"{Gen}/TerrainData.asset");

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain";
            go.transform.position = new Vector3(-300f, -2f, -300f);
            var t = go.GetComponent<Terrain>();
            t.heightmapPixelError = 3f;
            t.basemapDistance = 400f;
            t.drawInstanced = true;
            return t;
        }

        /// <summary>A small Rust-style base (3 m foundation grid) showing the three building tiers.</summary>
        static void BuildDemoBase(Mats m)
        {
            var root = new GameObject("DemoBase").transform;
            root.position = new Vector3(30f, 0f, -25f);
            const float s = 3f, wallH = 3f, t = 0.2f, foundH = 0.8f;
            Material[] tiers = { m.wood, m.stone, m.metal };
            for (int x = 0; x < 2; x++)
                for (int z = 0; z < 2; z++)
                    Block(root, $"Foundation_{x}{z}", root.position + new Vector3(x * s, foundH * 0.5f, z * s), new Vector3(s, foundH, s), m.stone, 1.5f);

            float y = foundH + wallH * 0.5f;
            // North / south walls (one per tier) with a doorway gap on the south side
            Block(root, "Wall_Wood_N0", root.position + new Vector3(0, y, s * 1.5f), new Vector3(s, wallH, t), tiers[0], 1f);
            Block(root, "Wall_Stone_N1", root.position + new Vector3(s, y, s * 1.5f), new Vector3(s, wallH, t), tiers[1], 1f);
            Block(root, "Wall_Metal_S0", root.position + new Vector3(0, y, -s * 0.5f), new Vector3(s, wallH, t), tiers[2], 1f);
            Block(root, "Doorway_S1_L", root.position + new Vector3(s - 1.1f, y, -s * 0.5f), new Vector3(0.8f, wallH, t), tiers[1], 1f);
            Block(root, "Doorway_S1_R", root.position + new Vector3(s + 1.1f, y, -s * 0.5f), new Vector3(0.8f, wallH, t), tiers[1], 1f);
            Block(root, "Doorway_S1_Top", root.position + new Vector3(s, foundH + wallH - 0.4f, -s * 0.5f), new Vector3(1.4f, 0.8f, t), tiers[1], 1f);
            Block(root, "Wall_Wood_W", root.position + new Vector3(-s * 0.5f, y, s * 0.5f), new Vector3(t, wallH, s * 2), tiers[0], 1f);
            Block(root, "Wall_Stone_E", root.position + new Vector3(s * 1.5f, y, s * 0.5f), new Vector3(t, wallH, s * 2), tiers[1], 1f);
            Block(root, "Roof", root.position + new Vector3(s * 0.5f, foundH + wallH + t * 0.5f, s * 0.5f), new Vector3(s * 2 + t, t, s * 2 + t), tiers[0], 1f);
        }

        static GameObject BuildPlayer(Vector3 pos)
        {
            var go = new GameObject("Player");
            go.transform.position = pos;
            var cc = go.AddComponent<CharacterController>();
            cc.radius = 0.4f; cc.skinWidth = 0.03f;
            go.AddComponent<PlayerInput>();
            go.AddComponent<SourceMovement>();

            var camRoot = new GameObject("CameraRoot").transform;
            camRoot.SetParent(go.transform, false);
            camRoot.localPosition = new Vector3(0, 1.63f, 0);
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(camRoot, false);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 2000f;
            cam.fieldOfView = 74f;
            camGo.AddComponent<HDAdditionalCameraData>();
            camGo.AddComponent<AudioListener>();

            var look = go.AddComponent<PlayerLook>();
            look.cameraRoot = camRoot;
            look.viewCamera = cam;
            go.AddComponent<FPSBase.Core.PlayerInteractor>().viewCamera = cam;
            go.AddComponent<MetalDetector>().viewCamera = cam;
            AddPlayerBody(go);
            return go;
        }

        // ------------------------------------------------------------------ helpers

        static GameObject Block(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, float uvScale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, true);
            go.transform.position = pos;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = Triplanar(mat, uvScale);
            return go;
        }

        static readonly System.Collections.Generic.Dictionary<string, Material> triplanar = new();

        /// <summary>World-space triplanar copy of a material: textures tile at real-world scale on any block size.</summary>
        static Material Triplanar(Material baseMat, float metersPerTile)
        {
            string key = $"{baseMat.name}_Tri{metersPerTile:0.#}";
            if (triplanar.TryGetValue(key, out var cached) && cached != null) return cached;
            string path = $"{Gen}/Materials/{key}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(baseMat);
                AssetDatabase.CreateAsset(mat, path);
            }
            else mat.CopyPropertiesFromMaterial(baseMat);
            mat.SetFloat("_UVBase", 5f);               // Triplanar
            mat.SetFloat("_ObjectSpaceUVMapping", 0f); // World space
            mat.SetFloat("_TexWorldScale", 1f / metersPerTile);
            HDShaderUtils.ResetMaterialKeywords(mat);
            EditorUtility.SetDirty(mat);
            triplanar[key] = mat;
            return mat;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
