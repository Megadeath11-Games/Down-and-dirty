using FPSBase.Core;
using FPSBase.Mining;
using FPSBase.Vehicles;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace FPSBase.EditorTools
{
    /// <summary>Builds the gold claim, the wash plant, and places the starter vehicles and the outpost.</summary>
    public static partial class ProjectBootstrap
    {
        // Claim footprint (world)
        static readonly Vector3 ClaimOrigin = new Vector3(30f, -5f, -90f);
        static readonly Vector3 ClaimSize = new Vector3(100f, 10f, 100f);

        static void BuildMiningArea(Mats m, Terrain mainTerrain, GameObject player)
        {
            var game = new GameObject("Game");
            game.AddComponent<Economy>();
            game.AddComponent<GameUI>().player = player.GetComponent<PlayerInteractor>();

            var paintYellow = ColorMat("Paint_ExcavatorYellow", new Color(0.85f, 0.62f, 0.08f), 0f, 0.45f);
            var paintTruck = ColorMat("Paint_TruckRed", new Color(0.45f, 0.06f, 0.05f), 0f, 0.5f);
            var rubber = ColorMat("Rubber", new Color(0.04f, 0.04f, 0.04f), 0f, 0.25f);
            var steel = ColorMat("Steel", new Color(0.55f, 0.55f, 0.56f), 0.6f, 0.35f);
            var display = ColorMat("Glass_Dark", new Color(0.08f, 0.1f, 0.12f), 0f, 0.95f);
            var glass = CabGlass("Glass_Cab", new Color(0.72f, 0.82f, 0.86f), 0.22f);
            var gold = ColorMat("Gold", new Color(1f, 0.77f, 0.34f), 1f, 0.85f);
            var fieldMats = FieldMaterials();
            BuildDirtField(mainTerrain, fieldMats, tuftMesh, tuftMat);
            BuildClaimStakes(m.wood);
            var dirt = fieldMats[2]; // loose dirt colour, also used for truck beds, buckets and the hopper
            var vm = new VehicleMats { paint = paintYellow, truckPaint = paintTruck, rubber = rubber, steel = steel, glass = glass, display = display, dirt = dirt };
            BuildWashPlant(m, steel, paintYellow, gold, dirt);
            var prefabs = BuildVehiclePrefabs(vm);
            // starter machines: the smallest of the new detailed fleet (5 t excavator, medium dozer, track loader)
            PlaceVehicle(prefabs.excavators[0], new Vector3(62f, 0.1f, -10f), 200f);
            PlaceVehicle(prefabs.pickup, new Vector3(60f, 0.6f, 16f), 160f);
            PlaceVehicle(prefabs.dozers[0], new Vector3(79f, 0.2f, 14f), 180f);
            PlaceVehicle(prefabs.skids[2], new Vector3(89f, 0.2f, 14f), 180f);
            BuildOutpost(m, vm, prefabs);
            var claimMark = new GameObject("Gold Claim (marker)").transform;
            claimMark.position = new Vector3(ClaimOrigin.x + ClaimSize.x * 0.5f, 0f, ClaimOrigin.z + ClaimSize.z * 0.5f);
            MapMark(claimMark, "Gold claim", new Color(1f, 0.82f, 0.3f), Vector3.zero);
            BuildAggregateYard(m, new Vector3(168f, 0f, 30f));
            BuildCity(m, mainTerrain);
        }

        static void MapMark(Transform t, string label, Color c, Vector3 arrive)
        {
            var mk = t.gameObject.AddComponent<MapMarker>();
            mk.label = label; mk.color = c; mk.arriveOffset = arrive;
        }

        static float FieldGroundY(Vector3 p)
        {
            DigSite site = null;
            foreach (var s in Object.FindObjectsByType<DigSite>(FindObjectsSortMode.None)) if (s.primary) site = s;
            if (site == null) return p.y;
            var t = site.GetComponent<Terrain>();
            return t.SampleHeight(p) + t.transform.position.y;
        }

        // ------------------------------------------------------------------ sand & gravel yard

        static void BuildAggregateYard(Mats m, Vector3 at)
        {
            at.y = FieldGroundY(at);
            var root = new GameObject("Sand & Gravel Yard");
            root.transform.position = at;
            var yard = root.AddComponent<AggregateYard>();
            var t = root.transform;
            const float w = 9f, d = 8f, wallH = 1.3f;
            Block(t, "Bay Pad", at + new Vector3(0f, 0.08f, 0f), new Vector3(w, 0.16f, d), m.stone, 3f);
            Block(t, "Bay Wall Back", at + new Vector3(0f, wallH / 2f, d / 2f), new Vector3(w, wallH, 0.5f), m.stone, 2f);
            Block(t, "Bay Wall L", at + new Vector3(-w / 2f, wallH / 2f, 0f), new Vector3(0.5f, wallH, d), m.stone, 2f);
            Block(t, "Bay Wall R", at + new Vector3(w / 2f, wallH / 2f, 0f), new Vector3(0.5f, wallH, d), m.stone, 2f);
            Block(t, "Sign Post L", at + new Vector3(-2.5f, 2f, d / 2f + 0.6f), new Vector3(0.2f, 4f, 0.2f), m.wood, 1f);
            Block(t, "Sign Post R", at + new Vector3(2.5f, 2f, d / 2f + 0.6f), new Vector3(0.2f, 4f, 0.2f), m.wood, 1f);
            Block(t, "Sign", at + new Vector3(0f, 3.4f, d / 2f + 0.55f), new Vector3(5.6f, 1.1f, 0.12f), m.wood, 1f);
            // display heaps of what they buy
            var sand = Flat("Yard_Sand", new Color(0.83f, 0.71f, 0.47f));
            var grav = Flat("Yard_Gravel", new Color(0.55f, 0.52f, 0.47f), 0.12f);
            var clay = Flat("Yard_Clay", new Color(0.66f, 0.38f, 0.26f), 0.18f);
            Material[] heaps = { sand, grav, clay };
            for (int i = 0; i < 3; i++)
            {
                var h = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                h.name = "Display Heap " + i;
                Object.DestroyImmediate(h.GetComponent<Collider>());
                h.transform.SetParent(t, true);
                Vector3 hp = at + new Vector3(w / 2f + 3f, 0f, -2.5f + i * 2.6f);
                hp.y = FieldGroundY(hp);
                h.transform.position = hp;
                h.transform.localScale = new Vector3(2.2f, 1.3f, 2.2f);
                h.GetComponent<MeshRenderer>().sharedMaterial = heaps[i];
            }
            var trig = new GameObject("BayVolume");
            trig.transform.SetParent(t, false);
            var bv = trig.AddComponent<BoxCollider>();
            bv.isTrigger = true; bv.center = new Vector3(0f, 1.2f, 0f); bv.size = new Vector3(w - 0.6f, 2f, d - 0.6f);
            yard.bayVolume = bv;
            MapMark(t, "Sand & gravel yard", new Color(0.8f, 0.75f, 0.65f), new Vector3(0f, 0f, -9f));
        }

        // ------------------------------------------------------------------ wash plant

        static void BuildWashPlant(Mats m, Material steel, Material paint, Material gold, Material dirt)
        {
            const float z = 28f, deckH = 1.8f;
            var concrete = Triplanar(m.stone, 3f);

            // Loading ramp + deck (separate root so it doesn't show the plant prompt)
            var rampRoot = new GameObject("WashPlant Loading Ramp").transform;
            float rampLen = 8f;
            float ang = Mathf.Atan2(deckH, rampLen) * Mathf.Rad2Deg;
            float slant = Mathf.Sqrt(rampLen * rampLen + deckH * deckH);
            var ramp = Block(rampRoot, "Ramp", Vector3.zero, new Vector3(slant, 0.4f, 4f), m.stone, 3f);
            ramp.transform.rotation = Quaternion.Euler(0f, 0f, ang);
            ramp.transform.position = new Vector3(66f - rampLen * 0.5f, deckH * 0.5f, z) + ramp.transform.up * -0.2f;
            Block(rampRoot, "Deck", new Vector3(68f, deckH * 0.5f, z), new Vector3(4f, deckH, 4f), m.stone, 3f);
            Block(rampRoot, "WheelStop", new Vector3(69.75f, deckH + 0.1f, z), new Vector3(0.3f, 0.2f, 3.6f), m.wood, 1f);

            var root = new GameObject("WashPlant");
            root.transform.position = new Vector3(71.3f, 0f, z);
            var plant = root.AddComponent<WashPlant>();
            var t = root.transform;
            // hopper box (open top), top at 1.6 m
            Part(t, "Hopper_Wall_N", new Vector3(0f, 1.1f, 1.2f), new Vector3(2.5f, 1.0f, 0.08f), steel, true);
            Part(t, "Hopper_Wall_S", new Vector3(0f, 1.1f, -1.2f), new Vector3(2.5f, 1.0f, 0.08f), steel, true);
            Part(t, "Hopper_Wall_W", new Vector3(-1.21f, 1.1f, 0f), new Vector3(0.08f, 1.0f, 2.4f), steel, true);
            Part(t, "Hopper_Wall_E", new Vector3(1.21f, 1.1f, 0f), new Vector3(0.08f, 1.0f, 2.4f), steel, true);
            Part(t, "Hopper_Floor", new Vector3(0f, 0.6f, 0f), new Vector3(2.5f, 0.08f, 2.5f), steel, true);
            for (int i = 0; i < 4; i++)
                Part(t, "Leg_" + i, new Vector3(i < 2 ? -1.1f : 1.1f, 0.3f, i % 2 == 0 ? -1.1f : 1.1f), new Vector3(0.12f, 0.6f, 0.12f), paint, true);
            var trig = new GameObject("HopperVolume");
            trig.transform.SetParent(t, false);
            var hv = trig.AddComponent<BoxCollider>();
            hv.isTrigger = true; hv.center = new Vector3(0f, 1.3f, 0f); hv.size = new Vector3(2.4f, 0.8f, 2.4f);
            var contentsRoot = new GameObject("HopperContents").transform;
            contentsRoot.SetParent(t, false); contentsRoot.localPosition = new Vector3(0f, 0.64f, 0f);
            var c = Part(contentsRoot, "Dirt", new Vector3(0f, 0.5f, 0f), new Vector3(2.3f, 1f, 2.3f), dirt);
            contentsRoot.gameObject.SetActive(false);

            // sluice run sloping away from the hopper
            float sx0 = 1.25f, sy0 = 0.75f, sx1 = 6.5f, sy1 = 0.35f;
            float sl = Mathf.Sqrt((sx1 - sx0) * (sx1 - sx0) + (sy1 - sy0) * (sy1 - sy0));
            var sluice = Part(t, "Sluice", new Vector3((sx0 + sx1) * 0.5f, (sy0 + sy1) * 0.5f, 0f), new Vector3(sl, 0.06f, 0.8f), steel, true);
            sluice.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(sy1 - sy0, sx1 - sx0) * Mathf.Rad2Deg);
            Part(sluice.transform, "Rail_N", new Vector3(0f, 2.5f, 0.48f), new Vector3(1f, 4f, 0.05f), steel);
            Part(sluice.transform, "Rail_S", new Vector3(0f, 2.5f, -0.48f), new Vector3(1f, 4f, 0.05f), steel);
            var goldVis = Part(t, "SluiceGold", new Vector3(2.4f, 0.72f, 0f), new Vector3(0.5f, 0.03f, 0.5f), gold);
            goldVis.SetActive(false);
            Part(t, "Generator", new Vector3(0f, 0.45f, -2.4f), new Vector3(1.4f, 0.9f, 0.8f), paint, true);
            Part(t, "WaterPump", new Vector3(1.4f, 0.3f, -2.2f), new Vector3(0.6f, 0.6f, 0.5f), steel, true);

            plant.hopperVolume = hv;
            plant.hopperContents = contentsRoot;
            plant.sluiceGoldVisual = goldVis.transform;
            plant.tailingsPoint = Marker(t, "Tailings", new Vector3(7.6f, 0.05f, 0f));
            plant.rejectPoint = Marker(t, "Grizzly Rejects", new Vector3(0f, 0.2f, 2.6f));
            // grizzly: steel bars over the hopper that throw out boulders (visual only, dirt pours through)
            for (int i = 0; i < 6; i++)
                Part(t, "Grizzly Bar " + i, new Vector3(0f, 1.66f, -1.0f + i * 0.4f), new Vector3(2.5f, 0.06f, 0.06f), steel);
            Part(t, "Grizzly Chute", new Vector3(0f, 1.0f, 1.75f), new Vector3(1.6f, 0.06f, 1.1f), steel).transform.localRotation = Quaternion.Euler(-38f, 0f, 0f);
            MapMark(t, "Wash plant", new Color(1f, 0.6f, 0.15f), new Vector3(2f, 0f, -7f));
        }

        // ------------------------------------------------------------------ helpers

        static GameObject Part(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat, bool collider = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        static Transform Marker(Transform parent, string name, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }

        static VehicleCamera VehicleCam(Transform root, Transform target, Transform seat, float distance, float height)
        {
            var go = new GameObject("VehicleCamera");
            go.transform.SetParent(root, false);
            var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 2000f; cam.fieldOfView = 65f;
            go.AddComponent<HDAdditionalCameraData>();
            go.AddComponent<AudioListener>();
            var vc = go.AddComponent<VehicleCamera>();
            vc.target = target; vc.seat = seat; vc.distance = distance; vc.height = height;
            go.SetActive(false);
            return vc;
        }

        static Material ColorMat(string name, Color color, float metallic, float smoothness)
        {
            string path = $"{Gen}/Materials/M_{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("HDRP/Lit")); AssetDatabase.CreateAsset(mat, path); }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);
            HDShaderUtils.ResetMaterialKeywords(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
