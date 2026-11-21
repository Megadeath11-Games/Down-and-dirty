using System.Collections.Generic;
using FPSBase.Vehicles;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FPSBase.EditorTools
{
    /// <summary>
    /// The wheel loader and the dump / haul trucks, built in the heavy fleet's style: one flat-shaded mesh per rigid
    /// part with a submesh per colour, real tyres with lugs and rims, hydraulic rams that follow the joints, frame
    /// rails, fenders, grilles, lights, ladders and handrails. Same pivots, markers and colliders as the old
    /// block-built ones (so the machines handle exactly as before), and Unity coordinates (x right, y up, z forward).
    /// </summary>
    public static partial class ProjectBootstrap
    {
        static void Rail(Geo g, Material m, Vector3 a, Vector3 b, float r) => Beam(g, m, a, b, r);

        // ------------------------------------------------------------------ wheel loader

        static GameObject BuildFleetLoader(FleetMats fm, TransportMats tm, Material dirt)
        {
            const string id = "Loader";
            var paint = fm.Paint(Hex(0xf59e0b));
            var accent = fm.Paint(Hex(0x1e293b));
            var root = new GameObject("Wheel Loader (compact)");
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 6000f; rb.angularDamping = 3f; rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.6f, -0.3f); col.size = new Vector3(1.9f, 1.2f, 3.4f);
            var t = root.transform;
            const float wr = 0.55f, tw = 0.42f;
            float[] axles = { 1.0f, -1.3f };
            var rams = new List<Ram>();

            // --- frames (front loader tower, articulation, rear engine frame), axles, fenders
            var g = new Geo();
            // front frame: two side plates with the tower, a nose plate and the axle housing
            foreach (int s in new[] { -1, 1 })
            {
                var plate = new List<Vector2> { new(-0.1f, 0.45f), new(1.55f, 0.45f), new(1.55f, 0.9f), new(0.65f, 1.75f), new(0.2f, 1.75f), new(-0.1f, 1.05f) };
                g.Prism(paint, plate, (p, d) => new Vector3(d, p.y, p.x), s * 0.38f - 0.05f, s * 0.38f + 0.05f);
                Pin(g, fm, new Vector3(s * 0.38f, 1.6f, 0.3f), 0.07f, 0.16f); // the lift arm pins
            }
            g.Box(paint, new Vector3(0f, 0.62f, 1.5f), new Vector3(0.86f, 0.34f, 0.12f));
            g.Box(accent, new Vector3(0f, 0.95f, 1.1f), new Vector3(0.7f, 0.12f, 0.9f));
            g.Cyl(fm.dark, new Vector3(0f, wr, axles[0]), AxisLat, 0.16f, 0.16f, 1.5f, 10);
            g.Cyl(fm.dark, new Vector3(0f, wr, axles[0]), AxisLat, 0.26f, 0.26f, 0.5f, 12); // diff housing
            // articulation hitch: upper and lower pins
            g.Box(fm.dark, new Vector3(0f, 0.75f, -0.15f), new Vector3(0.5f, 0.42f, 0.4f));
            foreach (float y in new[] { 0.55f, 0.98f }) g.Cyl(fm.pin, new Vector3(0f, y, -0.15f), Quaternion.identity, 0.07f, 0.07f, 0.16f, 10);
            foreach (int s in new[] { -1, 1 }) // steering rams across the hitch
                Rail(g, fm.chrome, new Vector3(s * 0.32f, 0.72f, 0.2f), new Vector3(s * 0.32f, 0.72f, -0.5f), 0.07f);
            // rear frame: engine bay with a sloped hood, louvred side doors, rear grille, counterweight
            var hood = new List<Vector2> { new(-0.4f, 0.55f), new(-2.25f, 0.55f), new(-2.25f, 1.45f), new(-2.05f, 1.68f), new(-0.85f, 1.72f), new(-0.4f, 1.6f) };
            g.Prism(paint, hood, (p, d) => new Vector3(d, p.y, p.x), -0.74f, 0.74f);
            foreach (int s in new[] { -1, 1 })
            {
                for (int i = 0; i < 5; i++) g.Box(accent, new Vector3(s * 0.745f, 1.12f + i * 0.09f, -1.45f), new Vector3(0.02f, 0.035f, 0.75f));
                g.Box(fm.dark, new Vector3(s * 0.745f, 0.95f, -0.85f), new Vector3(0.02f, 0.5f, 0.02f)); // door seam
                g.Box(fm.chrome, new Vector3(s * 0.75f, 0.9f, -1.0f), new Vector3(0.02f, 0.04f, 0.12f));   // latch
            }
            g.Box(accent, new Vector3(0f, 1.15f, -2.26f), new Vector3(1.2f, 0.55f, 0.03f)); // radiator grille
            for (int i = 0; i < 7; i++) g.Box(fm.dark, new Vector3(0f, 0.92f + i * 0.075f, -2.28f), new Vector3(1.18f, 0.02f, 0.02f));
            g.Box(paint, new Vector3(0f, 0.9f, -2.2f), new Vector3(1.72f, 0.65f, 0.3f)); // cast counterweight
            g.Box(accent, new Vector3(0f, 0.6f, -2.36f), new Vector3(1.6f, 0.08f, 0.04f));
            foreach (int s in new[] { -1, 1 })
            {
                g.Box(tm.tail, new Vector3(s * 0.7f, 1.08f, -2.36f), new Vector3(0.14f, 0.1f, 0.03f));
                g.Box(tm.head, new Vector3(s * 0.58f, 1.08f, -2.36f), new Vector3(0.1f, 0.1f, 0.03f)); // reversing light
            }
            g.Cyl(fm.dark, new Vector3(0f, wr, axles[1]), AxisLat, 0.16f, 0.16f, 1.5f, 10);
            g.Cyl(fm.dark, new Vector3(0f, wr, axles[1]), AxisLat, 0.26f, 0.26f, 0.5f, 12);
            // exhaust stack, air precleaner bowl
            g.Cyl(fm.dark, new Vector3(0.42f, 1.95f, -1.25f), Quaternion.identity, 0.05f, 0.05f, 0.55f, 8);
            g.Cyl(fm.dark, new Vector3(0.42f, 2.23f, -1.25f), Quaternion.identity, 0.06f, 0.05f, 0.04f, 8);
            g.Cyl(fm.dark, new Vector3(-0.42f, 1.85f, -1.45f), Quaternion.identity, 0.045f, 0.045f, 0.32f, 8);
            g.Cyl(fm.glass, new Vector3(-0.42f, 2.06f, -1.45f), Quaternion.identity, 0.09f, 0.08f, 0.12f, 10);
            // fenders: curved over the front tyres (on the front frame), flat on the rear
            foreach (int s in new[] { -1, 1 })
            {
                g.Prism(paint, ArcPoly(wr + 0.1f, wr + 0.04f, 0.15f, Mathf.PI - 0.15f, 8), (p, d) => new Vector3(d, wr + p.y, axles[0] + p.x), s * 0.82f - tw * 0.55f, s * 0.82f + tw * 0.55f);
                g.Box(paint, new Vector3(s * 0.82f, wr * 2f + 0.12f, axles[1] + 0.05f), new Vector3(tw * 1.15f, 0.05f, 1.05f));
                g.Box(accent, new Vector3(s * 0.82f, wr * 2f + 0.06f, axles[1] - 0.5f), new Vector3(tw * 1.1f, 0.18f, 0.04f));
            }
            // steps and grab rails up the left side to the cab door
            for (int i = 0; i < 3; i++) g.Box(fm.wear, new Vector3(-0.98f, 0.35f + i * 0.33f, -0.55f), new Vector3(0.26f, 0.04f, 0.3f));
            Rail(g, fm.dark, new Vector3(-1.1f, 0.35f, -0.4f), new Vector3(-1.1f, 1.25f, -0.4f), 0.03f);
            Rail(g, fm.rail, new Vector3(-0.68f, 1.3f, -0.05f), new Vector3(-0.68f, 2.3f, -0.05f), 0.035f);

            // --- cab (ROPS): posts, glass all round, roof with beacon and work lights, mirrors
            Vector3 cabC = new Vector3(0f, 2.05f, -0.4f);
            g.Box(accent, cabC + new Vector3(0f, -0.48f, 0f), new Vector3(1.25f, 0.2f, 1.15f));
            g.Box(fm.glass, cabC + new Vector3(0f, 0.08f, 0f), new Vector3(1.2f, 0.95f, 1.1f));
            foreach (float x in new[] { -0.6f, 0.6f })
                foreach (float z in new[] { -0.55f, 0.55f })
                    g.Box(accent, cabC + new Vector3(x, 0.05f, z), new Vector3(0.07f, 1.08f, 0.07f));
            g.Box(paint, cabC + new Vector3(0f, 0.62f, 0f), new Vector3(1.45f, 0.1f, 1.35f));
            g.Box(accent, cabC + new Vector3(0f, 0.69f, 0f), new Vector3(1.3f, 0.04f, 1.2f));
            g.Cyl(fm.amber, cabC + new Vector3(0.45f, 0.78f, -0.4f), Quaternion.identity, 0.06f, 0.05f, 0.12f, 8);
            foreach (int s in new[] { -1, 1 })
            {
                g.Box(tm.head, cabC + new Vector3(s * 0.55f, 0.6f, 0.69f), new Vector3(0.14f, 0.09f, 0.04f));
                g.Box(tm.head, cabC + new Vector3(s * 0.55f, 0.6f, -0.69f), new Vector3(0.14f, 0.09f, 0.04f));
                Rail(g, fm.dark, cabC + new Vector3(s * 0.64f, 0.35f, 0.5f), cabC + new Vector3(s * 0.85f, 0.35f, 0.6f), 0.025f);
                g.Box(tm.trim, cabC + new Vector3(s * 0.88f, 0.25f, 0.62f), new Vector3(0.03f, 0.22f, 0.14f));
            }
            g.Box(fm.dark, cabC + new Vector3(-0.61f, -0.05f, 0.0f), new Vector3(0.02f, 0.02f, 0.9f)); // door hinge line
            g.Attach(t, id + "_Body");
            var cabCol = ColBox(t, "Cab", cabC, new Vector3(1.25f, 1.15f, 1.15f));
            var seat = Marker(t, "CabView", new Vector3(0f, 2.3f, -0.55f));
            CabInterior(seat, CabStyle.Loader, CabMatsFrom(fm, accent), id);

            // --- wheels (spin about their own x)
            var wheels = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                var w = Node(t, "Wheel " + i, new Vector3(side * 0.82f, wr, axles[i / 2]));
                var gw = new Geo();
                GeoWheel(gw, Vector3.zero, wr, tw, fm.shoe, paint, fm.dark, 14);
                // deep loader tread: chevron lugs on both halves
                for (int k = 0; k < 14; k++)
                {
                    float a = (k + 0.5f) / 14f * 360f, rad = a * Mathf.Deg2Rad;
                    Vector3 radial = new Vector3(0f, Mathf.Sin(rad), Mathf.Cos(rad));
                    foreach (int h in new[] { -1, 1 })
                        gw.Box(fm.shoe, radial * (wr + 0.015f) + Vector3.right * h * tw * 0.25f, new Vector3(tw * 0.42f, wr * 0.1f, wr * 0.12f), Quaternion.Euler(90f - a, h * 20f, 0f));
                }
                for (int k = 0; k < 8; k++)
                {
                    float a = k / 8f * Mathf.PI * 2f;
                    gw.Cyl(fm.pin, new Vector3(side * tw * 0.56f, Mathf.Sin(a) * wr * 0.18f, Mathf.Cos(a) * wr * 0.18f), AxisLat, 0.018f, 0.018f, 0.03f, 6);
                }
                gw.Attach(w, id + "_Wheel" + i); // (one mesh asset each: a second one saved under the same name took the first one's mesh away)
                wheels[i] = w;
            }

            // --- lift arms, cross tube, tilt linkage
            var armPivot = Marker(t, "ArmPivot", new Vector3(0f, 1.6f, 0.3f));
            var ga = new Geo();
            foreach (int s in new[] { -1, 1 })
            {
                var arm = new List<Vector2> { new(-0.12f, -0.12f), new(-0.12f, 0.12f), new(1.05f, 0.26f), new(2.48f, 0.1f), new(2.48f, -0.12f), new(1.1f, 0.0f) };
                ga.Prism(paint, arm, (p, d) => new Vector3(d, p.y, p.x), s * 0.55f - 0.07f, s * 0.55f + 0.07f);
                ga.Box(paint, new Vector3(s * 0.55f, 0.02f, 1.05f), new Vector3(0.16f, 0.22f, 0.24f)); // ram lug
            }
            ga.Cyl(paint, new Vector3(0f, 0.08f, 1.5f), AxisLat, 0.09f, 0.09f, 1.1f, 10);
            ga.Box(paint, new Vector3(0f, 0.3f, 1.5f), new Vector3(0.24f, 0.4f, 0.22f)); // tilt lever tower
            Pin(ga, fm, new Vector3(0f, 0f, 2.4f), 0.055f, 1.24f);
            ga.Attach(armPivot, id + "_Arms");
            ToolBox(armPivot, "Arms", new Vector3(0f, 0.05f, 1.2f), new Vector3(1.24f, 0.3f, 2.45f), Quaternion.identity);
            var bucketPivot = Marker(armPivot, "BucketPivot", new Vector3(0f, 0f, 2.4f));
            const float bw = 1.9f;
            var gk = new Geo();
            Vector3 BM(Vector2 p, float d) => new Vector3(d, p.y, p.x);
            var shellPoly = new List<Vector2> { new(0.3f, 0.42f), new(-0.02f, 0.42f), new(-0.08f, 0.2f), new(-0.08f, -0.12f), new(0.02f, -0.32f), new(0.18f, -0.385f), new(1.0f, -0.385f),
                new(1.0f, -0.325f), new(0.2f, -0.325f), new(0.06f, -0.26f), new(-0.02f, -0.1f), new(-0.02f, 0.2f), new(0.03f, 0.36f), new(0.3f, 0.36f) };
            gk.Prism(fm.dark, shellPoly, BM, -bw * 0.5f, bw * 0.5f);
            var sidePoly = new List<Vector2> { new(-0.08f, 0.42f), new(0.3f, 0.42f), new(1.0f, -0.3f), new(1.0f, -0.385f), new(0.1f, -0.385f), new(-0.08f, -0.12f) };
            foreach (int s in new[] { -1, 1 }) gk.Prism(fm.dark, sidePoly, BM, s * bw * 0.5f - (s > 0 ? 0.05f : 0f), s * bw * 0.5f + (s < 0 ? 0.05f : 0f));
            gk.Box(fm.wear, new Vector3(0f, -0.355f, 0.98f), new Vector3(bw + 0.05f, 0.06f, 0.12f)); // bolt-on cutting edge
            for (int k = 0; k < 7; k++) gk.Box(fm.wear, new Vector3(-bw * 0.42f + k * bw * 0.14f, -0.35f, 1.06f), new Vector3(0.08f, 0.05f, 0.14f));
            foreach (int s in new[] { -1, 1 }) gk.Box(fm.wear, new Vector3(s * bw * 0.5f, 0.0f, 0.55f), new Vector3(0.07f, 0.5f, 0.08f), Quaternion.Euler(-45f, 0f, 0f));
            foreach (float x in new[] { -0.3f, 0.3f }) gk.Box(fm.dark, new Vector3(x, 0.32f, -0.12f), new Vector3(0.08f, 0.3f, 0.2f)); // linkage ears
            gk.Box(fm.dark, new Vector3(0f, 0.5f, -0.06f), new Vector3(0.24f, 0.16f, 0.14f));
            gk.Attach(bucketPivot, id + "_Bucket");
            ToolBox(bucketPivot, "Bucket", new Vector3(0f, 0f, 0.45f), new Vector3(bw, 0.75f, 0.95f), Quaternion.identity);
            var tip = Marker(bucketPivot, "BucketTip", new Vector3(0f, -0.33f, 0.95f));
            var mound = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(mound.GetComponent<Collider>());
            mound.name = "Bucket Load (dirt)";
            mound.transform.SetParent(bucketPivot, false);
            mound.transform.localPosition = new Vector3(0f, -0.12f, 0.45f);
            mound.transform.localScale = new Vector3(1.75f, 0.65f, 0.9f);
            mound.GetComponent<MeshRenderer>().sharedMaterial = dirt;
            mound.SetActive(false);

            var ld = root.AddComponent<FrontLoader>();
            ld.displayName = "Wheel Loader (compact)";
            ld.fuelCapacity = 150f; ld.fuel = 120f; ld.idleBurn = 2.5f; ld.workBurn = 12f;
            ld.driveSpeed = 5f; ld.reverseSpeed = 3.5f; ld.turnSpeed = 45f; ld.turnInPlace = false;
            ld.centerOfMass = new Vector3(0f, 0.5f, -0.6f);
            ld.armPivot = armPivot; ld.bucketPivot = bucketPivot; ld.bucketTip = tip; ld.bucketLoadVisual = mound.transform;
            ld.wheelVisuals = wheels; ld.wheelRadius = wr; ld.bucketCapacity = 0.9f;
            // (the bucket opens forward and up, between the top lip and the cutting edge: left unset, it was guessed
            // from the round dirt mound and came out wrong, so a curled, raised bucket tipped its load out)
            ld.bucketMouth = new Vector3(0f, 0.7f, 0.8f).normalized;

            // rams: two lift rams (frame to arms) and the tilt ram (arm tower to the bucket)
            foreach (int s in new[] { -1, 1 })
            {
                var a = Node(t, "LiftRamBase" + s, new Vector3(s * 0.45f, 0.82f, 0.95f));
                var b = Node(armPivot, "LiftRamRod" + s, new Vector3(s * 0.55f, -0.06f, 1.05f));
                rams.Add(new Ram { name = "Lift Ram " + (s < 0 ? "L" : "R"), parent = t, a = a, b = b, barrelR = 0.075f, rodR = 0.045f });
            }
            var ta = Node(armPivot, "TiltRamBase", new Vector3(0f, 0.45f, 1.45f));
            var tb = Node(bucketPivot, "TiltRamRod", new Vector3(0f, 0.5f, -0.06f));
            rams.Add(new Ram { name = "Tilt Ram", parent = armPivot, a = ta, b = tb, barrelR = 0.07f, rodR = 0.042f });
            for (int i = 0; i <= 12; i++)
                for (int j = 0; j <= 6; j++)
                {
                    float arm = Mathf.Lerp(ld.armRange.x, ld.armRange.y, i / 12f), tilt = Mathf.Lerp(ld.tiltRange.x, ld.tiltRange.y, j / 6f);
                    armPivot.localRotation = Quaternion.Euler(-arm, 0f, 0f);
                    bucketPivot.localRotation = Quaternion.Euler(arm + tilt, 0f, 0f);
                    foreach (var r in rams) r.Measure();
                }
            armPivot.localRotation = Quaternion.identity; bucketPivot.localRotation = Quaternion.identity;
            foreach (var r in rams) BuildRam(r, fm, paint, id);

            ld.exitPoint = Marker(t, "ExitPoint", new Vector3(-1.6f, 0f, -0.4f));
            ld.vehicleCamera = VehicleCam(t, t, seat, 9f, 3.2f);
            return root;
        }

        // ------------------------------------------------------------------ dump and haul trucks

        static GameObject BuildFleetTruck(TruckSpec s, FleetMats fm, TransportMats tm, Material dirt)
        {
            wheelProto.Clear();
            string id = "Truck_" + s.id;
            var paint = fm.Paint(Hex(s.paintHex));
            var accent = fm.Paint(Hex(s.accentHex));
            var root = new GameObject(s.name);
            var t = root.transform;
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = s.mass; rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.linearDamping = 0.05f; rb.angularDamping = 0.5f;

            float L = s.length, W = s.width, wr = s.wheelR, tw = wr * (s.mining ? 0.72f : 0.4f);
            float chassisY = wr + 0.25f;
            float bedFloor = Mathf.Max(chassisY + 0.2f, wr * 2f + 0.12f);
            float front = L / 2f;
            float cabFront = front - s.hood, cabBack = cabFront - s.cab;
            float railX = Mathf.Min(W * 0.24f, 0.55f) * (s.mining ? 1.6f : 1f);

            var chassis = root.AddComponent<BoxCollider>();
            chassis.center = new Vector3(0f, chassisY + 0.1f, 0f); chassis.size = new Vector3(W, 0.5f, L);

            var g = new Geo();
            // --- frame rails, cross members, rear axles housings, fuel and air tanks, battery box
            foreach (int sd in new[] { -1, 1 })
                g.Box(fm.dark, new Vector3(sd * railX, chassisY, -0.1f), new Vector3(0.14f, 0.3f, L * 0.94f));
            for (int i = 0; i < 6; i++) g.Box(fm.dark, new Vector3(0f, chassisY, -L * 0.45f + i * L * 0.18f), new Vector3(railX * 2f, 0.18f, 0.1f));
            foreach (float az in s.axles)
            {
                g.Cyl(fm.dark, new Vector3(0f, wr, az), AxisLat, wr * 0.17f, wr * 0.17f, W - tw * 2f, 10);
                g.Cyl(fm.dark, new Vector3(0f, wr, az), AxisLat, wr * 0.3f, wr * 0.3f, wr * 0.55f, 12); // diff / steering knuckles
                foreach (int sd in new[] { -1, 1 }) // leaf / strut springs
                    g.Box(fm.under, new Vector3(sd * railX, (wr + chassisY) * 0.5f, az), new Vector3(0.12f, chassisY - wr + 0.1f, wr * 1.6f));
            }
            if (!s.mining)
            {
                g.Cyl(fm.chrome, new Vector3(-W * 0.5f + 0.3f, chassisY - 0.05f, cabBack + s.cab * 0.4f), AxisFwd, 0.28f, 0.28f, s.cab * 0.75f, 14); // fuel tank
                g.Box(fm.dark, new Vector3(-W * 0.5f + 0.3f, chassisY - 0.05f, cabBack + s.cab * 0.4f), new Vector3(0.58f, 0.06f, 0.06f));
                g.Box(fm.dark, new Vector3(W * 0.5f - 0.3f, chassisY - 0.05f, cabBack + s.cab * 0.45f), new Vector3(0.5f, 0.5f, 0.6f)); // battery / tool box
                g.Box(fm.wear, new Vector3(W * 0.5f - 0.3f, chassisY + 0.21f, cabBack + s.cab * 0.45f), new Vector3(0.52f, 0.03f, 0.62f));
                foreach (int sd in new[] { -1, 1 }) for (int i = 0; i < 2; i++) // steps
                    g.Box(fm.wear, new Vector3(sd * (W * 0.5f - 0.1f), chassisY - 0.25f + i * 0.35f, cabBack + s.cab * 0.8f), new Vector3(0.2f, 0.04f, 0.32f));
            }

            Transform seat; Vector3 exitAt;
            if (!s.mining)
            {
                // --- conventional hood: sloped nose, chrome grille with bars, headlights, turn signals, fenders
                float hy = chassisY + 0.15f, hTop = s.hood > 0.1f ? Mathf.Min(1.15f, s.cabH * 0.62f) : 0f;
                if (s.hood > 0.1f)
                {
                    var hp = new List<Vector2> { new(cabFront, hy), new(front - 0.04f, hy), new(front, hy + 0.18f), new(front, hy + hTop * 0.85f), new(front - 0.18f, hy + hTop), new(cabFront, hy + hTop + 0.05f) };
                    g.Prism(paint, hp, (p, d) => new Vector3(d, p.y, p.x), -W * 0.42f, W * 0.42f);
                    g.Box(fm.chrome, new Vector3(0f, hy + hTop * 0.5f, front + 0.01f), new Vector3(W * 0.6f, hTop * 0.66f, 0.04f));
                    for (int i = 0; i < 7; i++) g.Box(tm.trim, new Vector3(0f, hy + hTop * 0.22f + i * hTop * 0.085f, front + 0.035f), new Vector3(W * 0.56f, hTop * 0.035f, 0.02f));
                    g.Box(accent, new Vector3(0f, hy + hTop + 0.02f, front - s.hood * 0.5f), new Vector3(0.06f, 0.03f, s.hood * 0.9f)); // hood crease
                    foreach (int sd in new[] { -1, 1 })
                    {
                        g.Box(tm.head, new Vector3(sd * W * 0.36f, hy + hTop * 0.55f, front - 0.02f), new Vector3(0.22f, 0.16f, 0.06f));
                        g.Box(fm.amber, new Vector3(sd * W * 0.36f, hy + hTop * 0.32f, front - 0.02f), new Vector3(0.22f, 0.08f, 0.06f));
                        // air cleaner canister beside the hood, front fender over the steer tyre
                        g.Cyl(fm.dark, new Vector3(sd * (W * 0.5f - 0.12f), hy + hTop * 0.75f, cabFront + 0.25f), Quaternion.identity, 0.16f, 0.16f, hTop * 0.9f, 12);
                        g.Prism(paint, ArcPoly(wr + 0.14f, wr + 0.06f, 0.08f, Mathf.PI - 0.08f, 9), (p, d) => new Vector3(d, wr + 0.1f + p.y, s.axles[0] + p.x),
                            sd * (W * 0.5f - wr * 0.35f) - tw * 0.6f, sd * (W * 0.5f - wr * 0.35f) + tw * 0.6f);
                    }
                }
                g.Box(fm.chrome, new Vector3(0f, chassisY, front + 0.08f), new Vector3(W + 0.1f, 0.26f, 0.16f)); // bumper
                foreach (int sd in new[] { -1, 1 }) g.Box(fm.amber, new Vector3(sd * W * 0.25f, chassisY + 0.02f, front + 0.17f), new Vector3(0.12f, 0.12f, 0.02f)); // tow hooks
                // --- cab: lower body, doors, pillars, glass all round, roof with marker lights and visor, mirrors, stacks
                float y0 = chassisY + 0.15f, mid = (cabFront + cabBack) / 2f, upH = s.cabH * 0.5f, upY = y0 + s.cabH * 0.75f;
                ColBox(t, "Cab Collider", new Vector3(0f, y0 + s.cabH / 2f, mid), new Vector3(W, s.cabH, s.cab));
                g.Box(paint, new Vector3(0f, y0 + s.cabH * 0.25f, mid), new Vector3(W, s.cabH * 0.5f, s.cab));
                g.Box(paint, new Vector3(0f, y0 + s.cabH - 0.04f, mid), new Vector3(W, 0.08f, s.cab));
                g.Box(accent, new Vector3(0f, y0 + s.cabH + 0.02f, cabFront + 0.12f), new Vector3(W * 0.95f, 0.05f, 0.3f)); // sun visor
                for (int i = 0; i < 5; i++) g.Box(fm.amber, new Vector3(-W * 0.3f + i * W * 0.15f, y0 + s.cabH + 0.06f, cabFront + 0.2f), new Vector3(0.1f, 0.05f, 0.06f));
                foreach (float px in new[] { -1f, 1f })
                {
                    foreach (float pz in new[] { cabFront - 0.04f, cabBack + 0.04f })
                        g.Box(paint, new Vector3(px * (W / 2f - 0.04f), upY, pz), new Vector3(0.08f, upH, 0.08f));
                    g.Box(fm.dark, new Vector3(px * (W / 2f + 0.005f), y0 + s.cabH * 0.25f, mid), new Vector3(0.01f, s.cabH * 0.46f, 0.02f)); // door seam
                    g.Box(fm.chrome, new Vector3(px * (W / 2f + 0.01f), y0 + s.cabH * 0.42f, mid - s.cab * 0.3f), new Vector3(0.02f, 0.04f, 0.14f)); // handle
                    Rail(g, fm.chrome, new Vector3(px * (W / 2f), upY + 0.1f, cabFront - 0.1f), new Vector3(px * (W / 2f + 0.28f), upY + 0.1f, cabFront - 0.05f), 0.025f);
                    g.Box(tm.trim, new Vector3(px * (W / 2f + 0.3f), upY, cabFront - 0.05f), new Vector3(0.04f, 0.4f, 0.18f)); // west-coast mirror
                    g.Cyl(fm.chrome, new Vector3(px * (W / 2f - 0.15f), y0 + s.cabH * 0.75f, cabBack - 0.12f), Quaternion.identity, 0.065f, 0.065f, s.cabH * 0.9f, 10);
                    g.Cyl(tm.trim, new Vector3(px * (W / 2f - 0.15f), y0 + s.cabH * 1.22f, cabBack - 0.12f), Quaternion.identity, 0.07f, 0.075f, 0.06f, 10); // exhaust stacks
                }
                g.Box(fm.glass, new Vector3(0f, upY, cabFront - 0.03f), new Vector3(W - 0.12f, upH - 0.04f, 0.02f));
                g.Box(fm.glass, new Vector3(0f, upY, cabBack + 0.03f), new Vector3(W - 0.12f, upH - 0.04f, 0.02f));
                g.Box(fm.glass, new Vector3(-W / 2f + 0.02f, upY, mid), new Vector3(0.02f, upH - 0.04f, s.cab - 0.12f));
                g.Box(fm.glass, new Vector3(W / 2f - 0.02f, upY, mid), new Vector3(0.02f, upH - 0.04f, s.cab - 0.12f));
                g.Box(tm.fabric, new Vector3(W * 0.22f, y0 + 0.12f, cabBack + 0.34f), new Vector3(W * 0.42f, 0.12f, 0.46f));
                g.Box(tm.fabric, new Vector3(W * 0.22f, y0 + 0.45f, cabBack + 0.12f), new Vector3(W * 0.42f, 0.6f, 0.09f));
                seat = Marker(t, "CabView", new Vector3(-W * 0.2f, chassisY + 0.15f + Mathf.Min(s.cabH * 0.78f, 0.98f), cabFront - Mathf.Min(0.62f, s.cab * 0.56f)));
                exitAt = new Vector3(-W / 2f - 0.9f, 0f, mid);
            }
            else
            {
                // --- off-highway hauler: radiator and bumper across the front, an upper deck with the cab on the left,
                // a ladder up the front, handrails, big air cleaners
                float deckY = wr * 2f + 0.35f;
                g.Box(paint, new Vector3(0f, chassisY + 0.55f, front - 0.6f), new Vector3(W * 0.5f, 1.4f, 1.2f)); // radiator housing
                g.Box(accent, new Vector3(0f, chassisY + 0.6f, front + 0.01f), new Vector3(W * 0.44f, 1.15f, 0.04f));
                for (int i = 0; i < 9; i++) g.Box(fm.dark, new Vector3(0f, chassisY + 0.1f + i * 0.12f, front + 0.04f), new Vector3(W * 0.42f, 0.03f, 0.02f));
                g.Box(fm.dark, new Vector3(0f, chassisY - 0.1f, front + 0.1f), new Vector3(W * 0.9f, 0.45f, 0.3f)); // bumper
                g.Box(fm.wear, new Vector3(0f, deckY, front - 1.2f), new Vector3(W, 0.08f, 2.6f)); // the deck
                foreach (int sd in new[] { -1, 1 })
                {
                    g.Box(paint, new Vector3(sd * W * 0.38f, deckY - 0.5f, front - 1.2f), new Vector3(W * 0.22f, 0.9f, 2.4f)); // deck supports / fenders
                    g.Box(tm.head, new Vector3(sd * W * 0.4f, deckY - 0.25f, front + 0.02f), new Vector3(0.3f, 0.18f, 0.04f));
                    g.Box(fm.amber, new Vector3(sd * W * 0.47f, deckY - 0.25f, front + 0.02f), new Vector3(0.1f, 0.18f, 0.04f));
                    g.Cyl(fm.dark, new Vector3(sd * W * 0.12f, deckY + 0.45f, front - 0.35f), Quaternion.identity, 0.3f, 0.3f, 0.8f, 14); // air cleaners
                    g.Prism(paint, ArcPoly(wr + 0.16f, wr + 0.08f, 0.1f, Mathf.PI * 0.55f, 7), (p, d) => new Vector3(d, wr + 0.1f + p.y, s.axles[0] + p.x),
                        sd * (W * 0.5f - tw * 0.6f) - tw * 0.6f, sd * (W * 0.5f - tw * 0.6f) + tw * 0.6f);
                }
                // handrails round the deck front and right side
                float hrY = deckY + 1.0f;
                Rail(g, fm.rail, new Vector3(-W * 0.5f, hrY, front + 0.05f), new Vector3(W * 0.5f, hrY, front + 0.05f), 0.045f);
                Rail(g, fm.rail, new Vector3(W * 0.5f, hrY, front + 0.05f), new Vector3(W * 0.5f, hrY, front - 2.4f), 0.045f);
                for (int i = 0; i < 6; i++) Rail(g, fm.rail, new Vector3(-W * 0.5f + i * W * 0.2f, deckY, front + 0.05f), new Vector3(-W * 0.5f + i * W * 0.2f, hrY, front + 0.05f), 0.035f);
                // diagonal ladder up the front left
                Vector3 l0 = new Vector3(-W * 0.32f, 0.35f, front + 0.35f), l1 = new Vector3(-W * 0.12f, deckY, front + 0.12f);
                foreach (float o in new[] { -0.28f, 0.28f }) Rail(g, fm.rail, l0 + Vector3.forward * o * 0.2f + Vector3.right * o, l1 + Vector3.right * o, 0.04f);
                for (int i = 1; i < 7; i++) g.Box(fm.wear, Vector3.Lerp(l0, l1, i / 7f), new Vector3(0.56f, 0.04f, 0.16f));
                // cab on the left of the deck
                float cw = W * 0.34f, cl = s.cab, ch = s.cabH;
                Vector3 cc = new Vector3(-W * 0.5f + cw * 0.5f + 0.1f, deckY + ch * 0.5f + 0.04f, front - 1.35f);
                ColBox(t, "Cab Collider", cc, new Vector3(cw, ch, cl));
                g.Box(paint, cc + new Vector3(0f, -ch * 0.3f, 0f), new Vector3(cw, ch * 0.4f, cl));
                g.Box(fm.glass, cc + new Vector3(0f, ch * 0.2f, 0f), new Vector3(cw * 0.96f, ch * 0.6f, cl * 0.96f));
                g.Box(paint, cc + new Vector3(0f, ch * 0.52f, 0f), new Vector3(cw * 1.06f, 0.08f, cl * 1.06f));
                foreach (float px in new[] { -0.48f, 0.48f }) foreach (float pz in new[] { -0.48f, 0.48f })
                    g.Box(accent, cc + new Vector3(px * cw, ch * 0.2f, pz * cl), new Vector3(0.08f, ch * 0.62f, 0.08f));
                g.Cyl(fm.amber, cc + new Vector3(0f, ch * 0.6f, -cl * 0.3f), Quaternion.identity, 0.08f, 0.07f, 0.14f, 8);
                g.Box(tm.trim, cc + new Vector3(-cw * 0.6f, ch * 0.15f, cl * 0.45f), new Vector3(0.05f, 0.45f, 0.25f)); // mirror
                g.Box(tm.fabric, cc + new Vector3(cw * 0.2f, -ch * 0.15f, -cl * 0.1f), new Vector3(cw * 0.3f, 0.1f, 0.4f)); // trainer seat
                seat = Marker(t, "CabView", cc + new Vector3(-cw * 0.05f, ch * 0.12f, cl * 0.05f));
                exitAt = new Vector3(-W / 2f - 1.2f, 0f, front - 1.35f);
                // engine bay behind the radiator, under the deck
                g.Box(fm.dark, new Vector3(0f, chassisY + 0.5f, front - 2.2f), new Vector3(W * 0.34f, 1.1f, 1.6f));
            }
            // rear: bumper, mud flaps, tail lights
            g.Box(fm.dark, new Vector3(0f, chassisY, -front + 0.05f), new Vector3(W * 0.8f, 0.18f, 0.12f));
            foreach (int sd in new[] { -1, 1 })
            {
                g.Box(tm.tail, new Vector3(sd * W * 0.36f, chassisY + 0.02f, -front - 0.02f), new Vector3(0.24f, 0.12f, 0.04f));
                g.Box(tm.trim, new Vector3(sd * (W * 0.5f - wr * 0.35f), wr * 0.55f, s.axles[s.axles.Length - 1] - wr - 0.12f), new Vector3(tw * 1.6f, wr * 0.9f, 0.02f));
            }
            g.Attach(t, id + "_Body");
            if (!s.mining)
            {
                // a pintle hook on a drop plate under the rear bumper (tows the equipment trailers)
                const float hookY = 0.56f;
                var gh = new Geo();
                gh.Box(fm.dark, new Vector3(0f, (chassisY + hookY) / 2f, -front - 0.02f), new Vector3(0.16f, chassisY - hookY + 0.16f, 0.1f));
                gh.Box(fm.dark, new Vector3(0f, hookY, -front - 0.13f), new Vector3(0.14f, 0.1f, 0.22f));
                gh.Cyl(fm.pin, new Vector3(0f, hookY, -front - 0.22f), Quaternion.identity, 0.04f, 0.04f, 0.14f, 8);
                gh.Attach(t, id + "_Hitch");
                var hook = Marker(t, "Pintle Hook", new Vector3(0f, hookY, -front - 0.22f));
                hook.gameObject.AddComponent<Hitch>().type = HitchType.Pintle;
            }

            // --- dump box hinged at the back: ribbed sides, top rails, a cab protector, hoist ram, tailgate
            var bedPivot = Marker(t, "BedPivot (hinge)", new Vector3(0f, bedFloor, -front + 0.15f));
            var bed = Marker(bedPivot, "DumpBox", new Vector3(0f, 0f, s.bedL / 2f));
            float bw = s.bedW, bl = s.bedL, bh = s.bedH;
            var gb = new Geo();
            gb.Box(paint, Vector3.zero, new Vector3(bw, 0.08f, bl));
            gb.Box(fm.dark, new Vector3(0f, -0.1f, 0f), new Vector3(railX * 2.2f, 0.14f, bl * 0.96f)); // long sills
            for (int i = 0; i < 6; i++) gb.Box(fm.dark, new Vector3(0f, -0.09f, -bl * 0.42f + i * bl * 0.168f), new Vector3(bw * 0.96f, 0.1f, 0.08f));
            foreach (int sd in new[] { -1, 1 })
            {
                gb.Box(paint, new Vector3(sd * (bw / 2f - 0.03f), bh / 2f, 0f), new Vector3(0.06f, bh, bl));
                gb.Box(accent, new Vector3(sd * (bw / 2f), bh + 0.03f, 0f), new Vector3(0.12f, 0.08f, bl + 0.04f)); // top rail
                int ribs = Mathf.Max(3, Mathf.RoundToInt(bl / 0.9f));
                for (int i = 0; i < ribs; i++) gb.Box(paint, new Vector3(sd * (bw / 2f + 0.03f), bh * 0.5f, -bl * 0.45f + i * bl * 0.9f / (ribs - 1)), new Vector3(0.06f, bh * 0.95f, 0.08f));
                gb.Box(tm.tail, new Vector3(sd * (bw / 2f + 0.04f), bh * 0.85f, -bl * 0.48f), new Vector3(0.02f, 0.06f, 0.08f)); // clearance lights
            }
            gb.Box(paint, new Vector3(0f, bh * 0.7f, bl / 2f - 0.03f), new Vector3(bw, bh * 1.4f, 0.06f));
            float canopy = s.mining ? 2.6f : Mathf.Min(0.9f, s.cab * 0.45f);
            gb.Box(paint, new Vector3(0f, bh * 1.4f, bl / 2f + canopy * 0.5f), new Vector3(bw, 0.06f, canopy)); // cab protector
            for (int i = 0; i < 4; i++) gb.Box(accent, new Vector3(-bw * 0.36f + i * bw * 0.24f, bh * 1.4f - 0.06f, bl / 2f + canopy * 0.5f), new Vector3(0.06f, 0.1f, canopy));
            gb.Cyl(fm.chrome, new Vector3(0f, -0.35f, bl * 0.3f), Quaternion.Euler(60f, 0f, 0f), 0.09f, 0.09f, 0.9f, 10); // hoist ram
            gb.Cyl(fm.dark, new Vector3(0f, -0.25f, bl * 0.42f), AxisFwd, 0.13f, 0.13f, 0.4f, 10);
            gb.Attach(bed, id + "_DumpBox");
            ColBox(bed, "Floor", Vector3.zero, new Vector3(bw, 0.08f, bl));
            foreach (int sd in new[] { -1, 1 }) ColBox(bed, sd < 0 ? "Side L" : "Side R", new Vector3(sd * (bw / 2f - 0.03f), bh / 2f, 0f), new Vector3(0.06f, bh, bl));
            ColBox(bed, "Headboard", new Vector3(0f, bh * 0.7f, bl / 2f - 0.03f), new Vector3(bw, bh * 1.4f, 0.06f));
            var gateHinge = Marker(bed, "Tailgate Hinge", new Vector3(0f, bh * 0.9f, -bl / 2f + 0.03f));
            var gg = new Geo();
            gg.Box(fm.dark, new Vector3(0f, -bh * 0.45f, 0f), new Vector3(bw, bh * 0.9f, 0.05f));
            for (int i = 0; i < 3; i++) gg.Box(accent, new Vector3(0f, -bh * 0.15f - i * bh * 0.3f, -0.04f), new Vector3(bw * 0.96f, 0.06f, 0.04f));
            foreach (int sd in new[] { -1, 1 }) gg.Cyl(fm.pin, new Vector3(sd * bw * 0.45f, 0f, 0f), AxisLat, 0.05f, 0.05f, 0.12f, 8);
            gg.Attach(gateHinge, id + "_Tailgate");
            var volGo = new GameObject("BedVolume");
            volGo.transform.SetParent(bed, false);
            var vol = volGo.AddComponent<BoxCollider>();
            vol.isTrigger = true; vol.center = new Vector3(0f, bh * 0.65f, 0f); vol.size = new Vector3(bw * 0.95f, bh * 1.3f, bl * 0.95f);
            var contents = Part(bed, "Contents (dirt)", new Vector3(0f, 0.05f, 0f), new Vector3(bw * 0.94f, 0.04f, bl * 0.95f), dirt);
            contents.SetActive(false);
            var mound = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(mound.GetComponent<Collider>());
            mound.name = "Contents mound";
            mound.transform.SetParent(bed, false);
            mound.GetComponent<MeshRenderer>().sharedMaterial = dirt;
            mound.SetActive(false);

            var pk = root.AddComponent<DumpPickup>();
            pk.displayName = s.name;
            pk.fuelCapacity = s.fuel; pk.fuel = s.fuel * 0.6f; pk.idleBurn = s.workBurn * 0.12f; pk.workBurn = s.workBurn;
            pk.capacity = s.capacity; pk.topSpeedKmh = s.topSpeed;
            pk.motorTorque = s.mass * 0.35f; pk.brakeTorque = s.mass * 1.35f;
            pk.transferCase = false;
            pk.rearLocked = s.axles.Length > 2 || s.mining;
            pk.lowGearBoost = 2.8f;
            pk.bedPivot = bedPivot; pk.bedContents = contents.transform; pk.bedMound = mound.transform; pk.bedVolume = vol;
            pk.bedInnerSize = new Vector3(bw * 0.94f, bh, bl * 0.95f);
            pk.tailgatePour = Marker(bedPivot, "PourPoint", new Vector3(0f, 0.25f, -0.25f));
            pk.tailgate = gateHinge;
            pk.exitPoint = Marker(t, "ExitPoint", exitAt);

            int n = s.axles.Length * 2;
            pk.wheels = new WheelCollider[n];
            pk.wheelVisuals = new Transform[n];
            float perWheel = s.mass / n;
            for (int i = 0; i < n; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                bool dual = i >= 2 && (s.mining || s.axles.Length > 2 || s.mass > 9000f);
                float wx = s.mining ? W / 2f - tw * (dual ? 1.15f : 0.62f) : W / 2f - wr * 0.35f;
                var wgo = new GameObject("Wheel " + i);
                wgo.transform.SetParent(t, false);
                wgo.transform.localPosition = new Vector3(side * wx, wr + 0.1f, s.axles[i / 2]);
                var wc = wgo.AddComponent<WheelCollider>();
                wc.radius = wr; wc.mass = Mathf.Max(20f, s.mass / 60f); wc.suspensionDistance = 0.3f; wc.forceAppPointDistance = wr * 0.5f;
                float spring = perWheel * 9.81f * 8f;
                wc.suspensionSpring = new JointSpring { spring = spring, damper = spring * 0.1f, targetPosition = 0.5f };
                var ff = wc.forwardFriction; ff.stiffness = 1.6f; wc.forwardFriction = ff;
                var sf = wc.sidewaysFriction; sf.stiffness = 1.8f; wc.sidewaysFriction = sf;
                pk.wheels[i] = wc;
                // (the truck turns its wheel visuals by (pose x 90 deg about z): undo that for a mesh whose axle is x)
                var v = Node(t, "Wheel Visual " + i, new Vector3(side * wx, wr, s.axles[i / 2]));
                var axle = Node(v, "Axle", Vector3.zero);
                axle.localRotation = Quaternion.Euler(0f, 0f, -90f);
                float off = dual ? tw * 0.55f : 0f;
                TruckWheel(axle, id + (dual ? "_DualWheel" : "_Wheel") + (side < 0 ? "L" : "R"), wr, dual ? tw * 0.95f : tw, dual ? new[] { -off, off } : new[] { side * 0.001f }, fm, s.mining ? paint : fm.wear);
                pk.wheelVisuals[i] = v;
            }
            CabInterior(seat, CabStyle.Truck, CabMatsFrom(fm, paint), id);
            pk.vehicleCamera = VehicleCam(t, t, seat, L * 1.5f, s.cabH + 1.2f + (s.mining ? 2.5f : 0f));
            return root;
        }
    }
}
