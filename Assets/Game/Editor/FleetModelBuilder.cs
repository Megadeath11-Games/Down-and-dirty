using System.Collections.Generic;
using System.IO;
using FPSBase.Vehicles;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FPSBase.EditorTools
{
    /// <summary>
    /// The haul trucks (5 t cab-over and 10 t conventional dump trucks, 25 t and 50 t articulated haulers) and the
    /// wheel loaders (6 t, 15 t, 30 t) from the heavy-haul fleet visualizer. Its own three.js builders were run to
    /// export each machine's parts (FleetModels/*.json, in Unity axes: x right, y up, z forward): the body, the
    /// frames that articulate, the dump box and tailgate, the lift arms, Z-bar and bucket, and every wheel. Here they
    /// become meshes on the same working parts as the other machines: wheel colliders, a dump box with its load and
    /// pour point, hydraulic rams that follow the joints, the cab, colliders, and the loader's arms and bucket.
    /// </summary>
    public static partial class ProjectBootstrap
    {
        [System.Serializable] class FMNode { public string name, parent; public float[] pos; public bool wheel; }
        [System.Serializable] class FMWheel { public string name, parent; public float r, w; public int count; public bool steer; public float[] center, offsets; }
        [System.Serializable] class FMAnchor { public string node; public float[] pos; }
        [System.Serializable] class FMRam { public FMAnchor a, b; public float barrel, rod; }
        [System.Serializable] class FMZBar { public float tipX, tipY, zLower, pushrod; public float[] zPivot, ear; }
        [System.Serializable] class FMMat { public string name; public int color; }
        [System.Serializable] class FMMesh { public string node; public int mat; public float[] v; }
        [System.Serializable]
        class FMCfg
        {
            public string cabStyle; public int primaryColor, accentColor, bedColor;
            public float trackGauge, tireRadius, tireWidth, frontAxleX, rearAxleX, wheelbase, cabX, cabLength, cabWidth, cabHeight, hoodLength,
                bedPivotX, bedPivotY, bedLength, bedWidth, bedHeight, articPivotX, tandemCenterX, tandemSpan,
                armPivotX, armPivotY, armLength, armDepth, bucketWidth, bucketRadius, rearLength, frontLength;
        }
        [System.Serializable]
        class FMData
        {
            public string key, type, name; public FMCfg cfg; public FMNode[] nodes; public FMWheel[] wheels; public FMRam[] rams;
            public FMAnchor seat; public FMZBar zbar; public FMMat[] mats; public FMMesh[] meshes;
        }

        class HaulSpec { public string key, name, display; public float mass, capacity, fuel, workBurn, topSpeed; public bool hitch; }
        class LoaderSpec { public string key, name, display; public float mass, bucket, fuel, workBurn, drive, reverse, turn; }

        const string FleetModelDir = "Assets/Game/Editor/FleetModels";

        static FMData LoadModel(string key) => JsonUtility.FromJson<FMData>(File.ReadAllText($"{FleetModelDir}/{key}.json"));
        static Vector3 V3(float[] a) => a == null || a.Length < 3 ? Vector3.zero : new Vector3(a[0], a[1], a[2]);

        /// <summary>The visualizer's materials, on the fleet's own (so the new machines match the rest).</summary>
        static Material ModelMat(FMMat m, FleetMats fm, TransportMats tm) => m.name switch
        {
            "darkSteel" or "cylinderBarrel" => fm.dark,
            "undercarriage" => fm.under,
            "trackShoe" or "tireRubber" => fm.shoe,
            "chromeRod" => fm.chrome,
            "pinSteel" => fm.pin,
            "wearPlate" => fm.wear,
            "headlightLens" => tm.head,
            "cabGlass" => ModelGlass(),
            "cabSeatFabric" => tm.fabric,
            "cabTrim" => tm.trim,
            "screenGlow" => tm.screen,
            "beaconAmber" => fm.amber,
            "safetyHandrail" => fm.rail,
            _ => m.color == 0xe2e8f0 ? fm.chrome : fm.Paint(Hex(m.color)),
        };

        /// <summary>
        /// The fleet models' cab glass: plain glass, a faint grey-green you can just see (not the deep blue it was;
        /// fully clear, the mostly-glass cabs read as panels missing).
        /// </summary>
        static Material ModelGlass() => CabGlass("Fleet_ModelGlass", new Color(0.74f, 0.83f, 0.86f), 0.24f);

        /// <summary>Lays out the rig nodes and puts each part's mesh on its node. Wheels go where `wheelNode` says.</summary>
        static Dictionary<string, Transform> ModelRig(FMData d, Transform root, FleetMats fm, TransportMats tm, string id, System.Func<FMWheel, Transform> wheelNode,
            Dictionary<string, Bounds> bounds = null)
        {
            var nodes = new Dictionary<string, Transform> { ["Body"] = root };
            foreach (var n in d.nodes)
                if (n.name != "Body" && !n.wheel) nodes[n.name] = Node(nodes[n.parent], n.name, V3(n.pos));
            foreach (var w in d.wheels) nodes[w.name] = wheelNode(w);
            var geos = new Dictionary<string, Geo>();
            foreach (var m in d.meshes)
            {
                if (!geos.TryGetValue(m.node, out var g)) geos[m.node] = g = new Geo();
                // (the model files don't keep one winding: tyres, rims and some cab panels came out facing inward,
                // so you saw through them. Every face is put in both ways round: it shows from either side)
                var mat = ModelMat(d.mats[m.mat], fm, tm);
                g.Raw(mat, m.v);
                var back = new float[m.v.Length];
                for (int i = 0; i + 8 < m.v.Length; i += 9)
                {
                    System.Array.Copy(m.v, i, back, i, 3);
                    System.Array.Copy(m.v, i + 6, back, i + 3, 3);
                    System.Array.Copy(m.v, i + 3, back, i + 6, 3);
                }
                g.Raw(mat, back);
                if (bounds != null)
                    for (int i = 0; i + 2 < m.v.Length; i += 3)
                    {
                        var p = new Vector3(m.v[i], m.v[i + 1], m.v[i + 2]);
                        if (bounds.TryGetValue(m.node, out var b)) { b.Encapsulate(p); bounds[m.node] = b; } else bounds[m.node] = new Bounds(p, Vector3.zero);
                    }
            }
            foreach (var kv in geos) kv.Value.Attach(nodes[kv.Key], $"{id}_{kv.Key}");
            return nodes;
        }

        static Vector3 RootPos(FMData d, string node)
        {
            Vector3 p = Vector3.zero;
            for (string n = node; n != null && n != "Body";)
            {
                var e = System.Array.Find(d.nodes, x => x.name == n);
                if (e == null) break;
                p += V3(e.pos); n = e.parent;
            }
            return p;
        }

        static List<Ram> ModelRams(FMData d, Dictionary<string, Transform> nodes, string[] names)
        {
            var rams = new List<Ram>();
            for (int i = 0; i < d.rams.Length; i++)
            {
                var r = d.rams[i];
                string nm = i < names.Length ? names[i] : "Ram " + i;
                var a = Node(nodes[r.a.node], nm + " Base", V3(r.a.pos));
                var b = Node(nodes[r.b.node], nm + " Rod", V3(r.b.pos));
                rams.Add(new Ram { name = nm, parent = nodes[r.a.node], a = a, b = b, barrelR = r.barrel, rodR = r.rod });
            }
            return rams;
        }

        // ------------------------------------------------------------------ dump trucks and articulated haulers

        static readonly HaulSpec[] HaulFleet =
        {
            new HaulSpec { key = "haul5", name = "Dump Truck 5 t (cab-over)", display = "Cab-Over Dump Truck 5 t", mass = 5000f, capacity = 3.6f, fuel = 150f, workBurn = 18f, topSpeed = 90f, hitch = true },
            new HaulSpec { key = "haul10", name = "Dump Truck 10 t", display = "Conventional Dump Truck 10 t", mass = 10000f, capacity = 6.8f, fuel = 250f, workBurn = 28f, topSpeed = 88f, hitch = true },
            // (the 25 t keeps the old haul truck's name: the menu scene and saves know it by that)
            new HaulSpec { key = "haul25", name = "Haul Truck 25t", display = "Articulated Haul Truck 25 t", mass = 25000f, capacity = 15f, fuel = 400f, workBurn = 45f, topSpeed = 55f, hitch = true },
            new HaulSpec { key = "haul50", name = "Articulated Haul Truck 50 t", display = "Articulated Haul Truck 50 t", mass = 48000f, capacity = 29.5f, fuel = 800f, workBurn = 72f, topSpeed = 50f },
        };

        static GameObject BuildModelHauler(HaulSpec s, FleetMats fm, TransportMats tm, Material dirt)
        {
            var d = LoadModel(s.key);
            var c = d.cfg;
            bool adt = d.type == "haul_adt";
            string id = "Model_" + s.key;
            var root = new GameObject(s.name);
            var t = root.transform;
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = s.mass; rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.linearDamping = 0.05f; rb.angularDamping = 0.5f;
            float r = c.tireRadius;

            // wheels: the steered pair first (left, right), then the rear axles front to back, left before right
            var order = new List<FMWheel>(d.wheels);
            order.Sort((a, b) =>
            {
                if (a.steer != b.steer) return a.steer ? -1 : 1;
                float za = RootPos(d, a.parent).z + V3(a.center).z, zb = RootPos(d, b.parent).z + V3(b.center).z;
                if (Mathf.Abs(za - zb) > 0.05f) return zb.CompareTo(za);
                return V3(a.center).x.CompareTo(V3(b.center).x);
            });
            var visuals = new Transform[order.Count];
            var at = new Vector3[order.Count];
            for (int i = 0; i < order.Count; i++)
            {
                var w = order[i];
                at[i] = RootPos(d, w.parent) + V3(System.Array.Find(d.nodes, x => x.name == w.name).pos);
            }
            var bounds = new Dictionary<string, Bounds>();
            var nodes = ModelRig(d, t, fm, tm, id, w =>
            {
                int i = order.IndexOf(w);
                var v = Node(t, "Wheel Visual " + i, at[i]);
                var axle = Node(v, "Axle", Vector3.zero);
                axle.localRotation = Quaternion.Euler(0f, 0f, -90f);
                visuals[i] = v;
                return axle;
            }, bounds);
            var bedPivot = nodes["BedPivot"];

            // --- colliders: the chassis, the cab (and hood), the dump box
            float frameY, length;
            if (!adt)
            {
                frameY = r * 1.85f;
                float f0 = c.bedPivotX - 0.14f, f1 = c.frontAxleX + 0.92f;
                length = f1 - f0;
                ColBox(t, "Chassis", new Vector3(0f, frameY, (f0 + f1) * 0.5f), new Vector3(c.trackGauge * 0.8f, 0.4f, length));
                float cabY = frameY + 0.1f;
                ColBox(t, "Cab Collider", new Vector3(0f, cabY + c.cabHeight * 0.5f, c.cabX), new Vector3(c.cabWidth, c.cabHeight, c.cabLength));
                if (c.cabStyle == "conventional")
                    ColBox(t, "Hood Collider", new Vector3(0f, cabY + c.cabHeight * 0.3f, (c.cabX + c.cabLength * 0.5f + f1) * 0.5f), new Vector3(c.cabWidth * 0.8f, c.cabHeight * 0.6f, f1 - c.cabX - c.cabLength * 0.5f));
            }
            else
            {
                frameY = r * 1.15f;
                var tb = bounds["Tractor"]; Vector3 tp = nodes["Tractor"].localPosition;
                float y0 = Mathf.Max(tb.min.y, r * 0.95f);
                ColBox(t, "Tractor Collider", tp + new Vector3(tb.center.x, (y0 + tb.max.y) * 0.5f, tb.center.z), new Vector3(tb.size.x * 0.9f, tb.max.y - y0, tb.size.z));
                var rf = bounds["RearFrame"]; Vector3 rp = nodes["RearFrame"].localPosition;
                float top = c.bedPivotY - 0.08f;
                ColBox(t, "Chassis", rp + new Vector3(0f, (r * 0.95f + top) * 0.5f, rf.center.z), new Vector3(c.trackGauge * 0.7f, top - r * 0.95f, rf.size.z * 0.9f));
                length = tp.z + tb.max.z - (rp.z + rf.min.z);
            }

            float bL = c.bedLength, bW = c.bedWidth, bH = c.bedHeight;
            Vector3 boxAt, inner, volC, volS;
            if (!adt)
            {
                boxAt = new Vector3(0f, 0.12f, bL * 0.5f);
                ColBox(bedPivot, "Floor", new Vector3(0f, 0.08f, bL * 0.5f), new Vector3(bW, 0.08f, bL));
                foreach (int sd in new[] { -1, 1 })
                    ColBox(bedPivot, sd < 0 ? "Side L" : "Side R", new Vector3(sd * (bW * 0.5f - 0.03f), 0.12f + bH * 0.5f, bL * 0.5f), new Vector3(0.06f, bH, bL));
                ColBox(bedPivot, "Headboard", new Vector3(0f, 0.12f + bH * 0.8f, bL - 0.03f), new Vector3(bW, bH * 1.6f, 0.06f));
                inner = new Vector3(bW * 0.94f, bH, bL * 0.95f);
                volC = new Vector3(0f, bH * 0.65f, 0f); volS = new Vector3(bW * 0.95f, bH * 1.3f, bL * 0.95f);
            }
            else
            {
                // the scow body: a flat keel floor, walls flared out over the tyres, a sloped headboard and a raised tail lip (no tailgate)
                boxAt = new Vector3(0f, 0.15f, bL * 0.4f);
                ColBox(bedPivot, "Floor", new Vector3(0f, 0.1f, bL * 0.38f), new Vector3(bW * 0.8f, 0.1f, bL * 0.82f));
                const float flare = 0.22f;
                foreach (int sd in new[] { -1, 1 })
                    ColBox(bedPivot, sd < 0 ? "Side L" : "Side R", new Vector3(sd * (bW * 0.38f + Mathf.Sin(flare) * bH * 0.47f), bH * 0.47f, bL * 0.4f), new Vector3(0.08f, bH * 0.96f, bL * 0.98f),
                        Quaternion.Euler(0f, 0f, -sd * flare * Mathf.Rad2Deg));
                float headAng = Mathf.Atan2(bH * 0.9f, bL * 0.18f), headLen = Mathf.Sqrt(bH * 0.9f * bH * 0.9f + bL * 0.18f * bL * 0.18f);
                ColBox(bedPivot, "Headboard", new Vector3(0f, bH * 0.46f, bL * 0.85f), new Vector3(bW * 0.94f, headLen, 0.08f), Quaternion.Euler(90f - headAng * Mathf.Rad2Deg, 0f, 0f));
                float scowAng = Mathf.Atan2(bH * 0.5f, bL * 0.16f), scowLen = Mathf.Sqrt(bH * 0.5f * bH * 0.5f + bL * 0.16f * bL * 0.16f);
                ColBox(bedPivot, "Tail Lip", new Vector3(0f, bH * 0.37f, -bL * 0.07f), new Vector3(bW * 0.78f, 0.08f, scowLen), Quaternion.Euler(scowAng * Mathf.Rad2Deg, 0f, 0f));
                inner = new Vector3(bW * 0.78f, bH * 0.95f, bL * 0.8f);
                volC = new Vector3(0f, bH * 0.5f, 0f); volS = new Vector3(bW * 0.8f, bH * 1.1f, bL * 0.85f);
            }
            var box = Marker(bedPivot, "DumpBox", boxAt);
            var volGo = new GameObject("BedVolume");
            volGo.transform.SetParent(box, false);
            var vol = volGo.AddComponent<BoxCollider>();
            vol.isTrigger = true; vol.center = volC; vol.size = volS;
            var contents = Part(box, "Contents (dirt)", new Vector3(0f, 0.05f, 0f), new Vector3(inner.x, 0.04f, inner.z), dirt);
            contents.SetActive(false);
            var mound = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(mound.GetComponent<Collider>());
            mound.name = "Contents mound";
            mound.transform.SetParent(box, false);
            mound.GetComponent<MeshRenderer>().sharedMaterial = dirt;
            mound.SetActive(false);

            var pk = root.AddComponent<DumpPickup>();
            pk.bedPivot = bedPivot; pk.bedContents = contents.transform; pk.bedMound = mound.transform; pk.bedVolume = vol;
            pk.bedInnerSize = inner;
            pk.tailgatePour = Marker(bedPivot, "PourPoint", adt ? new Vector3(0f, bH * 0.5f, -bL * 0.18f) : new Vector3(0f, 0.3f, -0.3f));
            pk.tailgate = nodes.TryGetValue("Tailgate", out var gate) ? gate : null;
            pk.maxTilt = adt ? 58f : 52f;
            pk.displayName = s.display;
            pk.fuelCapacity = s.fuel; pk.fuel = s.fuel * 0.6f; pk.idleBurn = s.workBurn * 0.12f; pk.workBurn = s.workBurn;
            pk.capacity = s.capacity; pk.topSpeedKmh = s.topSpeed;
            pk.motorTorque = s.mass * 0.32f; pk.brakeTorque = s.mass * 1.3f;
            pk.transferCase = false;
            pk.rearLocked = adt;
            pk.lowGearBoost = 2.8f;
            pk.maxSteer = adt ? 36f : 34f;

            // wheel colliders (the visuals follow them; an articulated hauler's front pair turns with the tractor)
            int n = order.Count;
            pk.wheels = new WheelCollider[n];
            pk.wheelVisuals = visuals;
            float perWheel = s.mass / n;
            for (int i = 0; i < n; i++)
            {
                var wgo = new GameObject("Wheel " + i);
                wgo.transform.SetParent(t, false);
                wgo.transform.localPosition = at[i] + Vector3.up * 0.1f;
                var wc = wgo.AddComponent<WheelCollider>();
                wc.radius = r; wc.mass = Mathf.Max(20f, s.mass / 60f); wc.suspensionDistance = 0.3f; wc.forceAppPointDistance = r * 0.5f;
                float spring = perWheel * 9.81f * 8f;
                wc.suspensionSpring = new JointSpring { spring = spring, damper = spring * 0.1f, targetPosition = 0.5f };
                var ff = wc.forwardFriction; ff.stiffness = 1.6f; wc.forwardFriction = ff;
                var sf = wc.sidewaysFriction; sf.stiffness = 1.8f; wc.sidewaysFriction = sf;
                pk.wheels[i] = wc;
            }
            if (adt)
            {
                var ar = root.AddComponent<FrameArticulation>();
                ar.front = nodes["Tractor"]; ar.maxAngle = pk.maxSteer; ar.rate = 40f;
                ar.wheelbase = at[0].z - (at[n - 1].z + at[n - 2].z) * 0.5f;
                pk.articulation = ar;
            }

            // rams: the hoist (and an articulated hauler's steering rams across the hitch), sized over their travel
            var rams = ModelRams(d, nodes, adt ? new[] { "Steer Ram L", "Steer Ram R", "Hoist Ram L", "Hoist Ram R" } : new[] { "Hoist Ram" });
            var art = root.GetComponent<FrameArticulation>();
            for (float tilt = 0f; tilt <= pk.maxTilt + 0.1f; tilt += 4f)
                for (int k = -2; k <= 2; k++)
                {
                    bedPivot.localRotation = Quaternion.Euler(-tilt, 0f, 0f);
                    if (art != null) art.front.localRotation = Quaternion.Euler(0f, k * art.maxAngle * 0.5f, 0f);
                    foreach (var rm in rams) rm.Measure();
                }
            bedPivot.localRotation = Quaternion.identity;
            if (art != null) art.front.localRotation = Quaternion.identity;
            foreach (var rm in rams) BuildRam(rm, fm, fm.dark, id);

            // the pintle hook under the back (tows the equipment trailers)
            if (s.hitch)
            {
                const float hookY = 0.56f;
                float back = adt ? nodes["RearFrame"].localPosition.z + bounds["RearFrame"].min.z : c.bedPivotX - 0.2f;
                float hy0 = adt ? frameY : frameY - 0.16f;
                var gh = new Geo();
                gh.Box(fm.dark, new Vector3(0f, (hy0 + hookY) * 0.5f, back - 0.02f), new Vector3(0.16f, Mathf.Max(0.1f, hy0 - hookY + 0.16f), 0.1f));
                gh.Box(fm.dark, new Vector3(0f, hookY, back - 0.13f), new Vector3(0.14f, 0.1f, 0.22f));
                gh.Cyl(fm.pin, new Vector3(0f, hookY, back - 0.22f), Quaternion.identity, 0.04f, 0.04f, 0.14f, 8);
                gh.Attach(t, id + "_Hitch");
                var hook = Marker(t, "Pintle Hook", new Vector3(0f, hookY, back - 0.22f));
                hook.gameObject.AddComponent<Hitch>().type = HitchType.Pintle;
            }

            // the cab: the seat where the visualizer had the driver, the station under it, the way out and the camera
            var seatNode = nodes[d.seat.node];
            var eye = Marker(seatNode, "CabView", V3(d.seat.pos) + new Vector3(0f, 1.0f, -0.12f));
            CabInterior(eye, CabStyle.Truck, CabMatsFrom(fm, fm.Paint(Hex(c.primaryColor))), id, 1.0f);
            Vector3 eyeRoot = RootPos(d, d.seat.node) + eye.localPosition;
            float halfW = c.trackGauge * 0.5f + c.tireWidth;
            pk.exitPoint = Marker(t, "ExitPoint", new Vector3(-halfW - 0.9f, 0f, eyeRoot.z));
            pk.vehicleCamera = VehicleCam(t, t, eye, length * 1.4f, eyeRoot.y + 1.3f);
            return root;
        }

        // ------------------------------------------------------------------ wheel loaders

        static readonly LoaderSpec[] LoaderFleet =
        {
            // (the 6 t keeps the old compact loader's name: saves and the dealer's list know it by that)
            new LoaderSpec { key = "loader6", name = "Wheel Loader (compact)", display = "Wheel Loader 6 t", mass = 6200f, bucket = 1.05f, fuel = 150f, workBurn = 12f, drive = 5f, reverse = 3.5f, turn = 45f },
            new LoaderSpec { key = "loader15", name = "Wheel Loader 15 t", display = "Wheel Loader 15 t", mass = 15400f, bucket = 2.65f, fuel = 280f, workBurn = 24f, drive = 5.5f, reverse = 3.8f, turn = 38f },
            new LoaderSpec { key = "loader30", name = "Wheel Loader 30 t", display = "Quarry Wheel Loader 30 t", mass = 30800f, bucket = 4.8f, fuel = 520f, workBurn = 42f, drive = 5.5f, reverse = 3.8f, turn = 32f },
        };

        static GameObject BuildModelLoader(LoaderSpec s, FleetMats fm, TransportMats tm, Material dirt)
        {
            var d = LoadModel(s.key);
            var c = d.cfg;
            string id = "Model_" + s.key;
            var root = new GameObject(s.name);
            var t = root.transform;
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = s.mass; rb.angularDamping = 3f; rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            float r = c.tireRadius;

            var wheelList = new List<Transform>();
            var bounds = new Dictionary<string, Bounds>();
            var nodes = ModelRig(d, t, fm, tm, id, w =>
            {
                var wt = Node(t, "Wheel " + wheelList.Count, Vector3.zero);
                wheelList.Add(wt);
                return wt;
            }, bounds);
            // (the wheels were made before `nodes` existed: hang them off their frames now)
            foreach (var w in d.wheels)
            {
                var wt = nodes[w.name];
                var node = System.Array.Find(d.nodes, x => x.name == w.name);
                wt.SetParent(nodes[w.parent], false);
                wt.localPosition = V3(node.pos);
            }
            var front = nodes["FrontGroup"];
            var armPivot = nodes["ArmPivot"]; var bucketPivot = nodes["BucketPivot"];

            // body collider (both frames), the cab
            float zf = float.MinValue, zr = float.MaxValue;
            foreach (var w in d.wheels) { float wz = RootPos(d, w.parent).z + V3(System.Array.Find(d.nodes, x => x.name == w.name).pos).z; zf = Mathf.Max(zf, wz); zr = Mathf.Min(zr, wz); }
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, r * 1.09f, (zf + zr) * 0.5f);
            col.size = new Vector3((c.trackGauge + c.tireWidth) * 0.92f, r * 2.18f, zf - zr + r * 2f);
            Vector3 seatRoot = RootPos(d, d.seat.node) + V3(d.seat.pos);
            ColBox(t, "Cab", seatRoot + new Vector3(0f, c.cabHeight * 0.5f, 0f), new Vector3(c.cabWidth, c.cabHeight, c.cabLength));

            // the bucket's working points: the cutting edge, the load in it
            float R = c.bucketRadius, bw = c.bucketWidth;
            var tip = Marker(bucketPivot, "BucketTip", new Vector3(0f, -0.03f, R * 1.66f));
            var mound = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(mound.GetComponent<Collider>());
            mound.name = "Bucket Load (dirt)";
            mound.transform.SetParent(bucketPivot, false);
            mound.transform.localPosition = new Vector3(0f, R * 0.4f, R * 0.78f);
            mound.transform.localScale = new Vector3(bw * 0.9f, R * 0.75f, R * 1.2f);
            mound.GetComponent<MeshRenderer>().sharedMaterial = dirt;
            mound.SetActive(false);
            if (bounds.TryGetValue("ArmPivot", out var ab)) ToolBox(armPivot, "Arms", ab.center, ab.size * 0.95f, Quaternion.identity);
            if (bounds.TryGetValue("BucketPivot", out var bb)) ToolBox(bucketPivot, "Bucket", bb.center, bb.size * 0.95f, Quaternion.identity);

            // the Z-bar: bellcrank and push rod follow the bucket
            var z = d.zbar;
            var zl = root.AddComponent<ZBarLinkage>();
            zl.bucketPivot = bucketPivot; zl.bellcrank = nodes["ZBar"]; zl.pushrod = nodes["Pushrod"];
            zl.tip = new Vector2(z.tipX, z.tipY); zl.pivot = new Vector2(z.zPivot[0], z.zPivot[1]);
            zl.lowerArm = z.zLower; zl.rodLength = z.pushrod; zl.ear = new Vector2(z.ear[0], z.ear[1]);

            var ar = root.AddComponent<FrameArticulation>();
            ar.front = front; ar.maxAngle = 38f; ar.rate = 50f; ar.wheelbase = zf - zr;

            var ld = root.AddComponent<FrontLoader>();
            ld.displayName = s.display;
            ld.fuelCapacity = s.fuel; ld.fuel = s.fuel * 0.8f; ld.idleBurn = s.workBurn * 0.2f; ld.workBurn = s.workBurn;
            ld.driveSpeed = s.drive; ld.reverseSpeed = s.reverse; ld.turnSpeed = s.turn; ld.turnInPlace = false;
            ld.centerOfMass = new Vector3(0f, r * 0.9f, (zf + zr) * 0.5f - (zf - zr) * 0.2f);
            ld.armPivot = armPivot; ld.bucketPivot = bucketPivot; ld.bucketTip = tip; ld.bucketLoadVisual = mound.transform;
            ld.wheelVisuals = wheelList.ToArray(); ld.wheelRadius = r; ld.bucketCapacity = s.bucket;
            ld.bucketMouth = new Vector3(0f, 0.7f, 0.8f).normalized;
            ld.articulation = ar;
            ld.fillRate = 0.7f * s.bucket / 0.9f; ld.curlFillRate = 0.35f * s.bucket / 0.9f; ld.dumpRate = 0.9f * s.bucket / 0.9f;
            // arms down: as low as puts the level bucket's edge on the ground
            float lo = -50f, hi = 5f;
            for (int it = 0; it < 30; it++)
            {
                float mid = (lo + hi) * 0.5f;
                armPivot.localRotation = Quaternion.Euler(-mid, 0f, 0f);
                bucketPivot.localRotation = Quaternion.Euler(mid, 0f, 0f);
                if (t.InverseTransformPoint(tip.position).y > -0.09f) hi = mid; else lo = mid; // arms right down: the edge bites a little
            }
            ld.armRange = new Vector2(Mathf.Round(hi * 10f) / 10f, 46f);

            // rams: lift (frame to arms), tilt (frame to the bellcrank), steering (across the hitch); sized over their travel
            var rams = ModelRams(d, nodes, new[] { "Lift Ram L", "Lift Ram R", "Tilt Ram", "Steer Ram L", "Steer Ram R" });
            for (int i = 0; i <= 12; i++)
                for (int j = 0; j <= 6; j++)
                    for (int k = -1; k <= 1; k++)
                    {
                        float arm = Mathf.Lerp(ld.armRange.x, ld.armRange.y, i / 12f), tl = Mathf.Lerp(ld.tiltRange.x, ld.tiltRange.y, j / 6f);
                        armPivot.localRotation = Quaternion.Euler(-arm, 0f, 0f);
                        bucketPivot.localRotation = Quaternion.Euler(arm + tl, 0f, 0f);
                        front.localRotation = Quaternion.Euler(0f, k * ar.maxAngle, 0f);
                        zl.Solve();
                        foreach (var rm in rams) rm.Measure();
                    }
            armPivot.localRotation = Quaternion.identity; bucketPivot.localRotation = Quaternion.identity; front.localRotation = Quaternion.identity;
            zl.Solve();
            foreach (var rm in rams) BuildRam(rm, fm, fm.dark, id);

            var eye = Marker(nodes[d.seat.node], "CabView", V3(d.seat.pos) + new Vector3(0f, 1.05f, -0.1f));
            CabInterior(eye, CabStyle.Loader, CabMatsFrom(fm, fm.Paint(Hex(c.accentColor))), id, 1.05f);
            float halfW = c.trackGauge * 0.5f + c.tireWidth;
            ld.exitPoint = Marker(t, "ExitPoint", new Vector3(-halfW - 0.8f, 0f, seatRoot.z));
            float k2 = r / 0.55f;
            ld.vehicleCamera = VehicleCam(t, t, eye, 9f * k2, 3.2f * k2);
            return root;
        }
    }
}
