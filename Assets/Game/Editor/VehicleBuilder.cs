using FPSBase.Vehicles;
using UnityEditor;
using UnityEngine;

namespace FPSBase.EditorTools
{
    /// <summary>Builds the placeholder excavators and trucks (from simple shapes) and saves them as prefabs.</summary>
    public static partial class ProjectBootstrap
    {
        class VehicleMats { public Material paint, truckPaint, rubber, steel, glass, display, dirt; }

        static CabMats CabMatsFrom(VehicleMats vm, Material accent) => new CabMats
        {
            seat = vm.rubber, dark = vm.steel, metal = vm.steel, accent = accent, screen = vm.glass, amber = accent,
        };

        /// <summary>
        /// Tread lugs round a tyre and spokes on both rim faces, so you can see the wheel turn (a plain cylinder
        /// looks the same at any angle). `tyre` is a primitive cylinder lying on its side: radius 0.5, height 2 in its
        /// own units, scaled; the parts are children so they spin with it.
        /// </summary>
        static void WheelDetail(Transform tyre, Material tread, Material rim, int lugs)
        {
            Vector3 s = tyre.localScale;
            float r = s.x * 0.5f; // world radius
            GameObject Cube(string name, Vector3 lp, Quaternion lr, Vector3 worldSize, Material m)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.name = name;
                go.transform.SetParent(tyre, false);
                go.transform.localPosition = lp;
                go.transform.localRotation = lr;
                // sizes are given in metres along (radial, across, tangential); undo the tyre's scale
                go.transform.localScale = new Vector3(worldSize.x / s.x, worldSize.y / s.y, worldSize.z / s.z);
                go.GetComponent<MeshRenderer>().sharedMaterial = m;
                return go;
            }
            float lugT = Mathf.Max(0.025f, r * 0.09f);
            for (int i = 0; i < lugs; i++)
            {
                float a = i / (float)lugs * 360f;
                float rad = a * Mathf.Deg2Rad;
                // staggered lugs, left and right half of the tread in turn (a simple chevron tread)
                float y = i % 2 == 0 ? 0.45f : -0.45f;
                var lp = new Vector3(Mathf.Cos(rad) * (0.5f + lugT * 0.35f / s.x), y, Mathf.Sin(rad) * (0.5f + lugT * 0.35f / s.x));
                Cube("Lug", lp, Quaternion.Euler(0f, -a, 0f), new Vector3(lugT, s.y * 1.05f, r * 0.32f), tread);
            }
            foreach (float face in new[] { -1f, 1f })
                for (int k = 0; k < 3; k++)
                {
                    float a = k * 60f + (face > 0f ? 0f : 30f);
                    Cube("Spoke", new Vector3(0f, face * 1.005f, 0f), Quaternion.Euler(0f, -a, 0f), new Vector3(r * 1.25f, 0.02f, r * 0.14f), rim);
                }
        }

        class VehiclePrefabs { public GameObject mini, pickup, dumpTruck, hauler, miningHauler, mixer, dozer, loader, grader, bowser, tilt7, goose10, dump5, dump10, loader15, loader30; public GameObject[] excavators, dozers, skids, semis; }

        class ExcavatorSpec
        {
            public string name; public float scale, bucket, digRate, drive, fuel, workBurn, mass;
        }

        class TruckSpec
        {
            public string name, id; public int paintHex = 0xd4d4d0, accentHex = 0x1e293b; public bool mining, mixer;
            public float length, width, wheelR, hood, cab, cabH, bedL, bedW, bedH, capacity, mass, fuel, workBurn, topSpeed;
            public float[] axles;
        }

        static VehiclePrefabs BuildVehiclePrefabs(VehicleMats vm)
        {
            EnsureFolder(Gen + "/Prefabs");
            // (the trucks and the loader are built in the heavy fleet's style, so its materials come first)
            var fm = BuildFleetMats(vm.dirt);
            var tm = BuildTransportMats();
            var p = new VehiclePrefabs
            {
                mini = SavePrefab(BuildExcavator(new ExcavatorSpec { name = "Mini Excavator 1.5t", scale = 1f, bucket = 0.08f, digRate = 0.12f, drive = 1.6f, fuel = 22f, workBurn = 3.2f, mass = 1500f }, vm)),
                dumpTruck = SavePrefab(BuildFleetTruck(new TruckSpec
                {
                    name = "Single-Axle Dump Truck", id = "Dump6", paintHex = 0xe5e5e0, accentHex = 0x1f2937,
                    length = 7.4f, width = 2.45f, wheelR = 0.55f, hood = 1.0f, cab = 1.8f, cabH = 1.6f,
                    bedL = 3.9f, bedW = 2.4f, bedH = 0.95f, capacity = 6f, mass = 9500f, fuel = 250f, workBurn = 30f, topSpeed = 85f, axles = new[] { 2.55f, -2.05f }
                }, fm, tm, vm.dirt)),
                // the haul trucks and wheel loaders from the heavy-haul fleet visualizer (FleetModelBuilder)
                dump5 = SavePrefab(BuildModelHauler(HaulFleet[0], fm, tm, vm.dirt)),
                dump10 = SavePrefab(BuildModelHauler(HaulFleet[1], fm, tm, vm.dirt)),
                hauler = SavePrefab(BuildModelHauler(HaulFleet[2], fm, tm, vm.dirt)),
                miningHauler = SavePrefab(BuildModelHauler(HaulFleet[3], fm, tm, vm.dirt)),
                mixer = SavePrefab(BuildFleetTruck(new TruckSpec
                {
                    name = "Concrete Mixer Truck", id = "Mixer8", paintHex = 0xf1f1ec, accentHex = 0xd9480f, mixer = true,
                    length = 9.2f, width = 2.5f, wheelR = 0.55f, hood = 1.05f, cab = 1.9f, cabH = 1.6f,
                    capacity = 8f, mass = 12500f, fuel = 300f, workBurn = 34f, topSpeed = 80f, axles = new[] { 3.0f, -1.55f, -2.9f }
                }, fm, tm, vm.dirt)),
                dozer = SavePrefab(BuildDozer(vm)),
                loader = SavePrefab(BuildModelLoader(LoaderFleet[0], fm, tm, vm.dirt)),
                loader15 = SavePrefab(BuildModelLoader(LoaderFleet[1], fm, tm, vm.dirt)),
                loader30 = SavePrefab(BuildModelLoader(LoaderFleet[2], fm, tm, vm.dirt)),
            };
            // the heavy fleet (5 t - 150 t excavators, 22 t and 66.5 t dozers)
            p.excavators = new GameObject[ExcavatorFleet.Length];
            for (int i = 0; i < ExcavatorFleet.Length; i++) p.excavators[i] = SavePrefab(BuildFleetExcavator(ExcavatorFleet[i], fm));
            p.dozers = new GameObject[DozerFleet.Length];
            for (int i = 0; i < DozerFleet.Length; i++) p.dozers[i] = SavePrefab(BuildFleetDozer(DozerFleet[i], fm));
            // skid steers (stand-on 1 t, wheeled 3.6 t, tracked 5.2 t) and the motor grader
            p.skids = new GameObject[SkidFleet.Length];
            for (int i = 0; i < SkidFleet.Length; i++) p.skids[i] = SavePrefab(BuildSkidSteer(SkidFleet[i], fm));
            p.grader = SavePrefab(BuildGrader(fm));
            // transport + support: the GMT400 starter pickup (dump box + tidy tank), the fuel bowser, the semis with RGN lowboys
            p.pickup = SavePrefab(BuildGMT400(fm, tm, vm.dirt));
            p.bowser = SavePrefab(BuildBowser(fm, tm));
            p.tilt7 = SavePrefab(BuildEquipTrailer(false, fm, tm));
            p.goose10 = SavePrefab(BuildEquipTrailer(true, fm, tm));
            p.semis = new GameObject[SemiFleet.Length];
            for (int i = 0; i < SemiFleet.Length; i++) p.semis[i] = SavePrefab(BuildSemi(SemiFleet[i], fm, tm));
            return p;
        }

        static GameObject SavePrefab(GameObject go)
        {
            string path = $"{Gen}/Prefabs/{go.name.Replace('/', '-')}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        static void PlaceVehicle(GameObject prefab, Vector3 pos, float yaw)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        }

        // ------------------------------------------------------------------ excavator

        static GameObject BuildExcavator(ExcavatorSpec s, VehicleMats vm)
        {
            var root = new GameObject(s.name);
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = s.mass; rb.angularDamping = 3f; rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.24f, 0f); col.size = new Vector3(1.0f, 0.46f, 1.55f);

            // undercarriage + tracks
            foreach (float x in new[] { -0.38f, 0.38f })
            {
                Part(root.transform, x < 0f ? "Track Frame_L" : "Track Frame_R", new Vector3(x, 0.2f, 0f), new Vector3(0.2f, 0.26f, 1.2f), vm.steel);
                // rubber track running round the sprocket and idler as the machine moves
                TrackAnimator.AddBelt(root, vm.rubber, x, 0.24f, 0.04f, 0.1f, new Vector3(-0.58f, 0.2f, 0.18f), new Vector3(0.58f, 0.2f, 0.18f));
            }
            for (int i = 0; i < 4; i++)
            {
                float z = -0.55f + i * 0.37f;
                Part(root.transform, "Roller_L" + i, new Vector3(-0.38f, 0.1f, z), new Vector3(0.26f, 0.1f, 0.1f), vm.steel);
                Part(root.transform, "Roller_R" + i, new Vector3(0.38f, 0.1f, z), new Vector3(0.26f, 0.1f, 0.1f), vm.steel);
            }
            Part(root.transform, "Frame", new Vector3(0f, 0.28f, 0f), new Vector3(0.55f, 0.18f, 1.2f), vm.steel);
            // backfill blade on two push arms, pivoting on the track frame (blade up / down keys)
            var bladePivot = Marker(root.transform, "BladePivot", new Vector3(0f, 0.3f, 0.55f));
            foreach (float x in new[] { -0.32f, 0.32f })
                Part(bladePivot, "Blade Arm", new Vector3(x, -0.04f, 0.18f), new Vector3(0.07f, 0.07f, 0.4f), vm.steel);
            Part(bladePivot, "Blade", new Vector3(0f, -0.12f, 0.4f), new Vector3(1.04f, 0.3f, 0.07f), vm.paint);
            Part(bladePivot, "Blade Edge", new Vector3(0f, -0.26f, 0.43f), new Vector3(1.06f, 0.05f, 0.08f), vm.steel);
            var bladeRam = Part(bladePivot, "Blade Ram", new Vector3(0f, 0.08f, 0.12f), new Vector3(0.06f, 0.06f, 0.42f), vm.steel);
            bladeRam.transform.localRotation = Quaternion.Euler(-25f, 0f, 0f);
            ToolBox(bladePivot, "Blade", new Vector3(0f, -0.12f, 0.4f), new Vector3(1.04f, 0.3f, 0.1f), Quaternion.identity);
            var bladeEdge = Marker(bladePivot, "BladeEdge", new Vector3(0f, -0.28f, 0.46f));
            Part(root.transform, "Slew Ring", new Vector3(0f, 0.42f, 0f), new Vector3(0.6f, 0.06f, 0.6f), vm.steel);

            var upper = new GameObject("Upper (swing)").transform;
            upper.SetParent(root.transform, false);
            upper.localPosition = new Vector3(0f, 0.45f, 0f);
            Part(upper, "House", new Vector3(0.05f, 0.3f, -0.12f), new Vector3(1.0f, 0.55f, 0.95f), vm.paint, true);
            Part(upper, "Counterweight", new Vector3(0.05f, 0.25f, -0.62f), new Vector3(1.0f, 0.45f, 0.18f), vm.paint);
            Part(upper, "Engine Hood", new Vector3(0.3f, 0.6f, -0.3f), new Vector3(0.45f, 0.06f, 0.55f), vm.steel);
            Part(upper, "Exhaust", new Vector3(0.45f, 0.75f, -0.45f), new Vector3(0.05f, 0.3f, 0.05f), vm.rubber);
            Part(upper, "Post_FL", new Vector3(-0.55f, 1.1f, 0.42f), new Vector3(0.05f, 1.1f, 0.05f), vm.steel);
            Part(upper, "Post_FR", new Vector3(0.11f, 1.1f, 0.42f), new Vector3(0.05f, 1.1f, 0.05f), vm.steel);
            Part(upper, "Post_RL", new Vector3(-0.55f, 1.1f, -0.38f), new Vector3(0.05f, 1.1f, 0.05f), vm.steel);
            Part(upper, "Post_RR", new Vector3(0.11f, 1.1f, -0.38f), new Vector3(0.05f, 1.1f, 0.05f), vm.steel);
            Part(upper, "Roof", new Vector3(-0.22f, 1.67f, 0.02f), new Vector3(0.76f, 0.05f, 0.92f), vm.paint);
            var seat = Marker(upper, "CabView", new Vector3(-0.22f, 1.47f, 0.0f));
            CabInterior(seat, CabStyle.Excavator, CabMatsFrom(vm, vm.paint), "Mini");

            // boom (with a fake hydraulic cylinder), stick, bucket
            var boomPivot = Marker(upper, "BoomPivot", new Vector3(0.28f, 0.45f, 0.42f));
            Part(boomPivot, "Boom", new Vector3(0f, 0f, 0.8f), new Vector3(0.14f, 0.2f, 1.65f), vm.paint);
            Part(boomPivot, "Boom Cylinder", new Vector3(0f, -0.15f, 0.5f), new Vector3(0.08f, 0.08f, 0.9f), vm.steel);
            ToolBox(boomPivot, "Boom", new Vector3(0f, 0f, 0.8f), new Vector3(0.14f, 0.2f, 1.65f), Quaternion.identity);
            var stickPivot = Marker(boomPivot, "StickPivot", new Vector3(0f, 0f, 1.6f));
            Part(stickPivot, "Stick", new Vector3(0f, 0f, 0.5f), new Vector3(0.12f, 0.15f, 1.05f), vm.paint);
            Part(stickPivot, "Bucket Cylinder", new Vector3(0f, 0.12f, 0.45f), new Vector3(0.06f, 0.06f, 0.7f), vm.steel);
            ToolBox(stickPivot, "Stick", new Vector3(0f, 0f, 0.5f), new Vector3(0.12f, 0.15f, 1.05f), Quaternion.identity);
            var bucketPivot = Marker(stickPivot, "BucketPivot", new Vector3(0f, 0f, 1.0f));
            ToolBox(bucketPivot, "Bucket", new Vector3(0f, 0.16f, 0.2f), new Vector3(0.42f, 0.32f, 0.38f), Quaternion.identity);
            var tip = BuildBucket(bucketPivot, vm, out GameObject mound);

            var ex = root.AddComponent<MiniExcavator>();
            ex.displayName = s.name;
            ex.sizeScale = s.scale;
            ex.fuelCapacity = s.fuel; ex.fuel = s.fuel * 0.8f; ex.idleBurn = s.workBurn * 0.25f; ex.workBurn = s.workBurn;
            ex.bucketCapacity = s.bucket; ex.digRate = s.digRate; ex.digRadius = 0.35f * s.scale; ex.dumpRate = s.bucket * 3f;
            ex.driveSpeed = s.drive;
            ex.upper = upper; ex.boomPivot = boomPivot; ex.stickPivot = stickPivot; ex.bucketPivot = bucketPivot;
            ex.bucketTip = tip; ex.bucketLoadVisual = mound.transform;
            ex.blade = bladePivot; ex.bladeEdge = bladeEdge; ex.bladeWidth = 1.04f;
            ex.exitPoint = Marker(root.transform, "ExitPoint", new Vector3(-1.4f, 0f, 0f));
            ex.vehicleCamera = VehicleCam(root.transform, upper, seat, 6f * s.scale, 1.8f * s.scale);
            root.transform.localScale = Vector3.one * s.scale;
            return root;
        }

        /// <summary>A proper scoop: curved shell, side plates, teeth, and a dirt mound inside. Returns the cutting tip.</summary>
        static Transform BuildBucket(Transform pivot, VehicleMats vm, out GameObject mound)
        {
            // profile in the bucket's local Y/Z plane, from the pivot round the back and bottom to the cutting edge
            // Backhoe bucket: the opening and teeth face the cab, so curling scoops toward the machine.
            Vector2[] prof = { new(-0.02f, 0.0f), new(0.12f, -0.05f), new(0.27f, 0.02f), new(0.33f, 0.16f), new(0.3f, 0.3f), new(0.2f, 0.42f) };
            const float width = 0.46f;
            for (int i = 0; i < prof.Length - 1; i++)
            {
                Vector3 a = new Vector3(0f, prof[i].x, prof[i].y), b = new Vector3(0f, prof[i + 1].x, prof[i + 1].y);
                Vector3 dir = b - a;
                Vector3 normal = new Vector3(0f, dir.z, -dir.y).normalized;
                var plate = Part(pivot, "Bucket Shell " + i, (a + b) * 0.5f, new Vector3(width, 0.025f, dir.magnitude + 0.02f), vm.steel);
                plate.transform.localRotation = Quaternion.LookRotation(dir, normal);
            }
            Part(pivot, "Bucket Side L", new Vector3(-width / 2f, 0.16f, 0.2f), new Vector3(0.025f, 0.34f, 0.42f), vm.steel);
            Part(pivot, "Bucket Side R", new Vector3(width / 2f, 0.16f, 0.2f), new Vector3(0.025f, 0.34f, 0.42f), vm.steel);
            Part(pivot, "Bucket Ear", new Vector3(0f, -0.05f, 0.0f), new Vector3(0.14f, 0.08f, 0.1f), vm.steel);

            Vector3 edge = new Vector3(0f, prof[^1].x, prof[^1].y);
            Vector3 edgeDir = (edge - new Vector3(0f, prof[^2].x, prof[^2].y)).normalized;
            for (int t = 0; t < 4; t++)
            {
                var tooth = Part(pivot, "Tooth " + t, edge + edgeDir * 0.05f + Vector3.right * (-0.165f + t * 0.11f), new Vector3(0.045f, 0.03f, 0.1f), vm.rubber);
                tooth.transform.localRotation = Quaternion.LookRotation(edgeDir, Vector3.up);
            }

            mound = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(mound.GetComponent<Collider>());
            mound.name = "Bucket Load (dirt)";
            mound.transform.SetParent(pivot, false);
            mound.transform.localPosition = new Vector3(0f, 0.12f, 0.24f);
            mound.transform.localScale = new Vector3(0.44f, 0.42f, 0.46f); // heaped above the rim when full
            mound.GetComponent<MeshRenderer>().sharedMaterial = vm.dirt;
            mound.SetActive(false);

            return Marker(pivot, "BucketTip", edge + edgeDir * 0.06f);
        }

        // ------------------------------------------------------------------ bulldozer

        static GameObject BuildDozer(VehicleMats vm)
        {
            var root = new GameObject("Bulldozer (small crawler)");
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 8000f; rb.angularDamping = 3f; rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.55f, 0f); col.size = new Vector3(2.1f, 1.1f, 2.4f);
            var t = root.transform;

            // tracks
            foreach (float x in new[] { -0.8f, 0.8f })
            {
                Part(t, "Track Frame", new Vector3(x, 0.3f, 0f), new Vector3(0.36f, 0.36f, 1.95f), vm.steel);
                Part(t, "Fender", new Vector3(x, 0.64f, 0f), new Vector3(0.5f, 0.05f, 2.5f), vm.paint);
                // steel shoes with grousers, running round the idlers as the dozer moves
                TrackAnimator.AddBelt(root, vm.rubber, x, 0.45f, 0.06f, 0.2f, new Vector3(-0.97f, 0.3f, 0.26f), new Vector3(0.97f, 0.3f, 0.26f));
            }
            Part(t, "Main Frame", new Vector3(0f, 0.65f, 0f), new Vector3(1.15f, 0.35f, 2.2f), vm.steel);
            Part(t, "Engine", new Vector3(0f, 1.05f, 0.5f), new Vector3(1.1f, 0.8f, 1.35f), vm.paint);
            Part(t, "Grille", new Vector3(0f, 1.0f, 1.18f), new Vector3(0.95f, 0.6f, 0.04f), vm.steel);
            Part(t, "Exhaust", new Vector3(0.35f, 1.65f, 0.85f), new Vector3(0.08f, 0.5f, 0.08f), vm.rubber);
            Part(t, "Cab Base", new Vector3(0f, 1.0f, -0.55f), new Vector3(1.3f, 0.35f, 1.1f), vm.paint);
            Part(t, "Cab", new Vector3(0f, 1.7f, -0.55f), new Vector3(1.2f, 1.05f, 1.0f), vm.glass, true);
            foreach (float x in new[] { -0.62f, 0.62f })
                foreach (float z in new[] { -1.07f, -0.03f })
                    Part(t, "ROPS Post", new Vector3(x, 1.75f, z), new Vector3(0.08f, 1.15f, 0.08f), vm.steel);
            Part(t, "ROPS Roof", new Vector3(0f, 2.35f, -0.55f), new Vector3(1.4f, 0.08f, 1.2f), vm.paint);
            Part(t, "Ripper Beam", new Vector3(0f, 0.55f, -1.4f), new Vector3(1.2f, 0.15f, 0.15f), vm.steel);
            Part(t, "Ripper Shank", new Vector3(0f, 0.35f, -1.45f), new Vector3(0.12f, 0.6f, 0.12f), vm.steel);
            var seat = Marker(t, "CabView", new Vector3(0f, 2.05f, -0.62f));
            CabInterior(seat, CabStyle.Dozer, CabMatsFrom(vm, vm.paint), "SmallDozer");

            // blade: origin at its bottom edge, slides up and down
            var blade = Marker(t, "Blade", new Vector3(0f, 0f, 1.55f));
            const float bw = 2.5f;
            var swivel = Marker(blade, "BladeSwivel", Vector3.zero); // tilts and angles on the push-arm C-frame
            var plate = Part(swivel, "Blade Plate", new Vector3(0f, 0.5f, 0.02f), new Vector3(bw, 1.0f, 0.1f), vm.paint);
            plate.transform.localRotation = Quaternion.Euler(-6f, 0f, 0f);
            Part(swivel, "Cutting Edge", new Vector3(0f, 0.04f, 0.06f), new Vector3(bw + 0.05f, 0.08f, 0.14f), vm.steel);
            Part(swivel, "End Bit L", new Vector3(-bw / 2f, 0.45f, 0.12f), new Vector3(0.08f, 0.9f, 0.32f), vm.paint);
            Part(swivel, "End Bit R", new Vector3(bw / 2f, 0.45f, 0.12f), new Vector3(0.08f, 0.9f, 0.32f), vm.paint);
            Part(blade, "Swivel Pivot", new Vector3(0f, 0.35f, -0.08f), new Vector3(0.22f, 0.22f, 0.2f), vm.steel);
            Part(blade, "Push Arm L", new Vector3(-0.9f, 0.35f, -0.7f), new Vector3(0.15f, 0.18f, 1.4f), vm.steel);
            Part(blade, "Push Arm R", new Vector3(0.9f, 0.35f, -0.7f), new Vector3(0.15f, 0.18f, 1.4f), vm.steel);
            var cylL = Part(blade, "Lift Cylinder L", new Vector3(-0.45f, 0.85f, -0.4f), new Vector3(0.1f, 0.1f, 0.9f), vm.steel);
            var cylR = Part(blade, "Lift Cylinder R", new Vector3(0.45f, 0.85f, -0.4f), new Vector3(0.1f, 0.1f, 0.9f), vm.steel);
            cylL.transform.localRotation = cylR.transform.localRotation = Quaternion.Euler(-25f, 0f, 0f);
            var edge = Marker(swivel, "BladeEdge", new Vector3(0f, 0.02f, 0.12f));
            ToolBox(swivel, "Blade", new Vector3(0f, 0.5f, 0.06f), new Vector3(bw, 1.0f, 0.2f), Quaternion.identity);

            var dz = root.AddComponent<Bulldozer>();
            dz.displayName = "Bulldozer (small crawler)";
            dz.fuelCapacity = 200f; dz.fuel = 160f; dz.idleBurn = 3f; dz.workBurn = 14f;
            dz.driveSpeed = 2.6f; dz.reverseSpeed = 3f; dz.turnSpeed = 35f; dz.turnInPlace = true;
            dz.centerOfMass = new Vector3(0f, 0.45f, 0.1f);
            dz.blade = blade; dz.bladeSwivel = swivel; dz.bladeEdge = edge; dz.bladeWidth = bw;
            dz.exitPoint = Marker(t, "ExitPoint", new Vector3(-1.7f, 0f, -0.5f));
            dz.vehicleCamera = VehicleCam(t, t, seat, 9f, 3f);
            return root;
        }

        // ------------------------------------------------------------------ wheel loader

        static GameObject BuildLoader(VehicleMats vm)
        {
            var root = new GameObject("Wheel Loader (compact)");
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 6000f; rb.angularDamping = 3f; rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.6f, -0.3f); col.size = new Vector3(1.9f, 1.2f, 3.4f);
            var t = root.transform;
            const float wr = 0.55f;

            var wheels = new Transform[4];
            float[] axles = { 1.0f, -1.3f };
            for (int i = 0; i < 4; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                var w = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(w.GetComponent<Collider>());
                w.name = "Wheel " + i;
                w.transform.SetParent(t, false);
                w.transform.localPosition = new Vector3(side * 0.82f, wr, axles[i / 2]);
                w.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                w.transform.localScale = new Vector3(wr * 2f, 0.25f, wr * 2f);
                w.GetComponent<MeshRenderer>().sharedMaterial = vm.rubber;
                WheelDetail(w.transform, vm.rubber, vm.steel, 12);
                wheels[i] = w.transform;
                Part(t, "Hub", new Vector3(side * 1.08f, wr, axles[i / 2]), new Vector3(0.04f, 0.4f, 0.4f), vm.steel);
                Part(t, "Fender", new Vector3(side * 0.82f, wr * 2f + 0.08f, axles[i / 2]), new Vector3(0.6f, 0.05f, 1.1f), vm.paint);
            }
            Part(t, "Front Frame", new Vector3(0f, 0.8f, 0.75f), new Vector3(0.95f, 0.5f, 1.5f), vm.paint);
            Part(t, "Articulation Joint", new Vector3(0f, 0.75f, -0.15f), new Vector3(0.5f, 0.4f, 0.4f), vm.steel);
            Part(t, "Rear Body", new Vector3(0f, 1.05f, -1.2f), new Vector3(1.55f, 0.9f, 2.0f), vm.paint);
            Part(t, "Engine Hood", new Vector3(0f, 1.55f, -1.5f), new Vector3(1.35f, 0.25f, 1.3f), vm.paint);
            Part(t, "Counterweight", new Vector3(0f, 0.9f, -2.2f), new Vector3(1.7f, 0.65f, 0.3f), vm.paint);
            Part(t, "Exhaust", new Vector3(0.45f, 1.95f, -1.2f), new Vector3(0.08f, 0.5f, 0.08f), vm.rubber);
            Part(t, "Cab", new Vector3(0f, 2.05f, -0.4f), new Vector3(1.25f, 1.15f, 1.15f), vm.glass, true);
            foreach (float x in new[] { -0.64f, 0.64f })
                foreach (float z in new[] { -0.98f, 0.18f })
                    Part(t, "Cab Post", new Vector3(x, 2.05f, z), new Vector3(0.07f, 1.2f, 0.07f), vm.steel);
            Part(t, "Cab Roof", new Vector3(0f, 2.68f, -0.4f), new Vector3(1.45f, 0.08f, 1.35f), vm.paint);
            var seat = Marker(t, "CabView", new Vector3(0f, 2.3f, -0.55f));
            CabInterior(seat, CabStyle.Loader, CabMatsFrom(vm, vm.paint), "Loader");

            // lift arms + self-levelling bucket
            var armPivot = Marker(t, "ArmPivot", new Vector3(0f, 1.6f, 0.3f));
            Part(armPivot, "Lift Arm L", new Vector3(-0.55f, 0f, 1.2f), new Vector3(0.14f, 0.3f, 2.45f), vm.paint);
            Part(armPivot, "Lift Arm R", new Vector3(0.55f, 0f, 1.2f), new Vector3(0.14f, 0.3f, 2.45f), vm.paint);
            Part(armPivot, "Arm Cross Tube", new Vector3(0f, 0f, 1.5f), new Vector3(1.1f, 0.16f, 0.16f), vm.steel);
            Part(armPivot, "Tilt Cylinder", new Vector3(0f, 0.32f, 1.05f), new Vector3(0.11f, 0.11f, 1.7f), vm.steel);
            var bucketPivot = Marker(armPivot, "BucketPivot", new Vector3(0f, 0f, 2.4f));
            const float bw = 1.9f;
            Part(bucketPivot, "Bucket Back", new Vector3(0f, 0.025f, -0.02f), new Vector3(bw, 0.75f, 0.06f), vm.steel);
            Part(bucketPivot, "Bucket Floor", new Vector3(0f, -0.35f, 0.47f), new Vector3(bw, 0.06f, 0.95f), vm.steel);
            Part(bucketPivot, "Bucket Top Lip", new Vector3(0f, 0.4f, 0.13f), new Vector3(bw, 0.06f, 0.3f), vm.steel);
            Part(bucketPivot, "Bucket Side L", new Vector3(-bw / 2f, 0.025f, 0.47f), new Vector3(0.05f, 0.75f, 0.95f), vm.steel);
            Part(bucketPivot, "Bucket Side R", new Vector3(bw / 2f, 0.025f, 0.47f), new Vector3(0.05f, 0.75f, 0.95f), vm.steel);
            Part(bucketPivot, "Cutting Edge", new Vector3(0f, -0.35f, 0.98f), new Vector3(bw + 0.05f, 0.05f, 0.12f), vm.rubber);
            var tip = Marker(bucketPivot, "BucketTip", new Vector3(0f, -0.33f, 0.95f));
            var mound = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(mound.GetComponent<Collider>());
            mound.name = "Bucket Load (dirt)";
            mound.transform.SetParent(bucketPivot, false);
            mound.transform.localPosition = new Vector3(0f, -0.12f, 0.45f);
            mound.transform.localScale = new Vector3(1.75f, 0.65f, 0.9f);
            mound.GetComponent<MeshRenderer>().sharedMaterial = vm.dirt;
            mound.SetActive(false);

            var ld = root.AddComponent<FrontLoader>();
            ld.displayName = "Wheel Loader (compact)";
            ld.fuelCapacity = 150f; ld.fuel = 120f; ld.idleBurn = 2.5f; ld.workBurn = 12f;
            ld.driveSpeed = 5f; ld.reverseSpeed = 3.5f; ld.turnSpeed = 45f; ld.turnInPlace = false;
            ld.centerOfMass = new Vector3(0f, 0.5f, -0.6f);
            ld.armPivot = armPivot; ld.bucketPivot = bucketPivot; ld.bucketTip = tip; ld.bucketLoadVisual = mound.transform;
            ld.wheelVisuals = wheels; ld.wheelRadius = wr; ld.bucketCapacity = 0.9f;
            ld.exitPoint = Marker(t, "ExitPoint", new Vector3(-1.6f, 0f, -0.4f));
            ld.vehicleCamera = VehicleCam(t, t, seat, 9f, 3.2f);
            return root;
        }

        // ------------------------------------------------------------------ trucks

        static GameObject BuildTruck(TruckSpec s, VehicleMats vm, Material paint)
        {
            var root = new GameObject(s.name);
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = s.mass; rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.linearDamping = 0.05f; rb.angularDamping = 0.5f;

            float L = s.length, W = s.width, wr = s.wheelR;
            float chassisY = wr + 0.25f;
            float bedFloor = Mathf.Max(chassisY + 0.2f, wr * 2f + 0.12f);
            float front = L / 2f;
            float cabFront = front - s.hood, cabBack = cabFront - s.cab;

            var chassis = root.AddComponent<BoxCollider>();
            chassis.center = new Vector3(0f, chassisY + 0.1f, 0f); chassis.size = new Vector3(W, 0.5f, L);
            Part(root.transform, "Chassis", new Vector3(0f, chassisY, 0f), new Vector3(W * 0.5f, 0.25f, L * 0.95f), vm.steel);
            if (s.hood > 0.1f)
            {
                Part(root.transform, "Hood", new Vector3(0f, chassisY + 0.45f, front - s.hood / 2f), new Vector3(W, 0.6f, s.hood), paint);
                Part(root.transform, "Grille", new Vector3(0f, chassisY + 0.35f, front + 0.02f), new Vector3(W * 0.85f, 0.5f, 0.04f), vm.steel);
            }
            Part(root.transform, "Bumper F", new Vector3(0f, chassisY, front + 0.06f), new Vector3(W + 0.1f, 0.2f, 0.12f), vm.steel);
            // cab: painted lower body, pillars and roof, glass all round the top half, a seat and dash inside
            {
                float y0 = chassisY + 0.15f, mid = (cabFront + cabBack) / 2f, upH = s.cabH * 0.5f, upY = y0 + s.cabH * 0.75f;
                var cabCol = new GameObject("Cab Collider");
                cabCol.transform.SetParent(root.transform, false);
                var cbc = cabCol.AddComponent<BoxCollider>();
                cbc.center = new Vector3(0f, y0 + s.cabH / 2f, mid); cbc.size = new Vector3(W, s.cabH, s.cab);
                Part(root.transform, "Cab", new Vector3(0f, y0 + s.cabH * 0.25f, mid), new Vector3(W, s.cabH * 0.5f, s.cab), paint);
                Part(root.transform, "Cab Roof", new Vector3(0f, y0 + s.cabH - 0.04f, mid), new Vector3(W, 0.08f, s.cab), paint);
                foreach (float px in new[] { -1f, 1f })
                    foreach (float pz in new[] { cabFront - 0.04f, cabBack + 0.04f })
                        Part(root.transform, "Cab Pillar", new Vector3(px * (W / 2f - 0.04f), upY, pz), new Vector3(0.08f, upH, 0.08f), paint);
                Part(root.transform, "Windshield", new Vector3(0f, upY, cabFront - 0.03f), new Vector3(W - 0.12f, upH - 0.04f, 0.02f), vm.glass);
                Part(root.transform, "Rear Window", new Vector3(0f, upY, cabBack + 0.03f), new Vector3(W - 0.12f, upH - 0.04f, 0.02f), vm.glass);
                Part(root.transform, "Window L", new Vector3(-W / 2f + 0.02f, upY, mid), new Vector3(0.02f, upH - 0.04f, s.cab - 0.12f), vm.glass);
                Part(root.transform, "Window R", new Vector3(W / 2f - 0.02f, upY, mid), new Vector3(0.02f, upH - 0.04f, s.cab - 0.12f), vm.glass);
                // passenger seat (the driver gets the seat, wheel and pedals of the cab interior)
                Part(root.transform, "Passenger Seat", new Vector3(W * 0.22f, y0 + 0.12f, cabBack + 0.34f), new Vector3(W * 0.42f, 0.12f, 0.46f), vm.rubber);
                Part(root.transform, "Passenger Seat Back", new Vector3(W * 0.22f, y0 + 0.45f, cabBack + 0.12f), new Vector3(W * 0.42f, 0.6f, 0.09f), vm.rubber);
            }
            Part(root.transform, "Bumper R", new Vector3(0f, chassisY, -front + 0.05f), new Vector3(W + 0.1f, 0.15f, 0.12f), vm.steel);
            // a pintle hook on a drop plate under the rear bumper: tows the equipment trailers (a skid steer behind a load of gravel)
            const float hookY = 0.56f;
            Part(root.transform, "Hitch Drop Plate", new Vector3(0f, (chassisY + hookY) / 2f, -front - 0.02f), new Vector3(0.16f, chassisY - hookY + 0.16f, 0.1f), vm.steel);
            Part(root.transform, "Hitch Hook", new Vector3(0f, hookY, -front - 0.13f), new Vector3(0.14f, 0.1f, 0.22f), vm.steel);
            var hook = Marker(root.transform, "Pintle Hook", new Vector3(0f, hookY, -front - 0.22f));
            hook.gameObject.AddComponent<Hitch>().type = HitchType.Pintle;

            // dump box hinged at the back
            var bedPivot = Marker(root.transform, "BedPivot (hinge)", new Vector3(0f, bedFloor, -front + 0.15f));
            var bed = Marker(bedPivot, "DumpBox", new Vector3(0f, 0f, s.bedL / 2f));
            float bw = s.bedW, bl = s.bedL, bh = s.bedH;
            Part(bed, "Floor", Vector3.zero, new Vector3(bw, 0.08f, bl), vm.steel, true);
            Part(bed, "Side L", new Vector3(-bw / 2f + 0.03f, bh / 2f, 0f), new Vector3(0.06f, bh, bl), paint, true);
            Part(bed, "Side R", new Vector3(bw / 2f - 0.03f, bh / 2f, 0f), new Vector3(0.06f, bh, bl), paint, true);
            Part(bed, "Headboard", new Vector3(0f, bh * 0.7f, bl / 2f - 0.03f), new Vector3(bw, bh * 1.4f, 0.06f), paint, true);
            // hinged along its top edge so it swings out at the bottom when the load slides against it
            var gateHinge = Marker(bed, "Tailgate Hinge", new Vector3(0f, bh * 0.9f, -bl / 2f + 0.03f));
            Part(gateHinge, "Tailgate", new Vector3(0f, -bh * 0.45f, 0f), new Vector3(bw, bh * 0.9f, 0.05f), vm.steel);
            var volGo = new GameObject("BedVolume");
            volGo.transform.SetParent(bed, false);
            var vol = volGo.AddComponent<BoxCollider>();
            vol.isTrigger = true; vol.center = new Vector3(0f, bh * 0.65f, 0f); vol.size = new Vector3(bw * 0.95f, bh * 1.3f, bl * 0.95f);
            var contents = Part(bed, "Contents (dirt)", new Vector3(0f, 0.05f, 0f), new Vector3(bw * 0.94f, 0.04f, bl * 0.95f), vm.dirt);
            contents.SetActive(false);
            var mound = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(mound.GetComponent<Collider>());
            mound.name = "Contents mound";
            mound.transform.SetParent(bed, false);
            mound.GetComponent<MeshRenderer>().sharedMaterial = vm.dirt;
            mound.SetActive(false);

            var pk = root.AddComponent<DumpPickup>();
            pk.displayName = s.name;
            pk.fuelCapacity = s.fuel; pk.fuel = s.fuel * 0.6f; pk.idleBurn = s.workBurn * 0.12f; pk.workBurn = s.workBurn;
            pk.capacity = s.capacity; pk.topSpeedKmh = s.topSpeed;
            pk.motorTorque = s.mass * 0.35f; pk.brakeTorque = s.mass * 1.35f;
            pk.transferCase = false;
            // rear drive; the tandem hauler's diffs come locked (both rear axles pull together; the diff lock key still works)
            pk.rearLocked = s.axles.Length > 2;
            pk.lowGearBoost = 2.8f; // low gears: enough to climb the wash plant ramp (13 deg) loaded, forwards or backing up
            pk.bedPivot = bedPivot; pk.bedContents = contents.transform; pk.bedMound = mound.transform; pk.bedVolume = vol;
            pk.bedInnerSize = new Vector3(bw * 0.94f, bh, bl * 0.95f);
            pk.tailgatePour = Marker(bedPivot, "PourPoint", new Vector3(0f, 0.25f, -0.25f));
            pk.tailgate = gateHinge;
            pk.exitPoint = Marker(root.transform, "ExitPoint", new Vector3(-W / 2f - 0.9f, 0f, (cabFront + cabBack) / 2f));

            int n = s.axles.Length * 2;
            pk.wheels = new WheelCollider[n];
            pk.wheelVisuals = new Transform[n];
            float perWheel = s.mass / n;
            for (int i = 0; i < n; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                var wgo = new GameObject("Wheel " + i);
                wgo.transform.SetParent(root.transform, false);
                wgo.transform.localPosition = new Vector3(side * (W / 2f - wr * 0.35f), wr + 0.1f, s.axles[i / 2]);
                var wc = wgo.AddComponent<WheelCollider>();
                wc.radius = wr; wc.mass = Mathf.Max(20f, s.mass / 60f); wc.suspensionDistance = 0.3f; wc.forceAppPointDistance = wr * 0.5f;
                float spring = perWheel * 9.81f * 8f;
                wc.suspensionSpring = new JointSpring { spring = spring, damper = spring * 0.1f, targetPosition = 0.5f };
                var ff = wc.forwardFriction; ff.stiffness = 1.6f; wc.forwardFriction = ff;
                var sf = wc.sidewaysFriction; sf.stiffness = 1.8f; wc.sidewaysFriction = sf;
                pk.wheels[i] = wc;
                var vis = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(vis.GetComponent<Collider>());
                vis.name = "Wheel Visual " + i;
                vis.transform.SetParent(root.transform, false);
                vis.transform.localScale = new Vector3(wr * 2f, wr * 0.35f, wr * 2f);
                vis.GetComponent<MeshRenderer>().sharedMaterial = vm.rubber;
                WheelDetail(vis.transform, vm.rubber, vm.steel, 10);
                pk.wheelVisuals[i] = vis.transform;
            }
            var seat = Marker(root.transform, "CabView", new Vector3(-W * 0.2f, chassisY + 0.15f + Mathf.Min(s.cabH * 0.78f, 0.98f), cabFront - Mathf.Min(0.62f, s.cab * 0.56f)));
            CabInterior(seat, CabStyle.Truck, CabMatsFrom(vm, paint), s.name.Replace(' ', '_').Replace('/', '-'));
            pk.vehicleCamera = VehicleCam(root.transform, root.transform, seat, L * 1.5f, s.cabH + 1.2f);
            return root;
        }
    }
}
