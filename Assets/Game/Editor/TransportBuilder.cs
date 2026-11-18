using System.Collections.Generic;
using FPSBase.Vehicles;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FPSBase.EditorTools
{
    /// <summary>
    /// Transport and support fleet, from the transport visualizer: 25 / 55 / 95 t heavy-haul semi tractors with
    /// removable-gooseneck (RGN) lowboys, the 1990s GMT400 3/4-ton 4x4 starter pickup (dump box plus a 420 L tidy
    /// tank you can take out) and the 3,500 L field fuel bowser trailer. Same coordinate kit as the heavy fleet:
    /// source X forward, Y up, Z right, mapped by F().
    /// </summary>
    public static partial class ProjectBootstrap
    {
        class SemiCfg
        {
            public string name, id;
            public int paint, accent, driveAxles, trailerAxles;
            public bool booster;
            public float cabL, cabW, cabH, deckL, deckW, mass, payload, fuel, burn, speed, accel;
        }

        static readonly SemiCfg[] SemiFleet =
        {
            new SemiCfg { name = "Semi Truck 25 t + RGN Lowboy", id = "Semi25", paint = 0xf8fafc, accent = 0x0f172a, driveAxles = 2, trailerAxles = 2,
                cabL = 3.15f, cabW = 2.50f, cabH = 2.25f, deckL = 6.4f, deckW = 2.60f, mass = 16400f, payload = 25f, fuel = 600f, burn = 42f, speed = 22f, accel = 1.8f },
            new SemiCfg { name = "Semi Truck 55 t + RGN Lowboy", id = "Semi55", paint = 0xdc2626, accent = 0x111827, driveAxles = 3, trailerAxles = 3,
                cabL = 3.35f, cabW = 2.54f, cabH = 2.35f, deckL = 7.4f, deckW = 2.75f, mass = 23800f, payload = 55f, fuel = 900f, burn = 58f, speed = 20f, accel = 1.4f },
            new SemiCfg { name = "Semi Truck 95 t + RGN Lowboy", id = "Semi95", paint = 0xf59e0b, accent = 0x0f172a, driveAxles = 4, trailerAxles = 3, booster = true,
                cabL = 3.45f, cabW = 2.58f, cabH = 2.40f, deckL = 8.2f, deckW = 3.10f, mass = 34500f, payload = 95f, fuel = 1200f, burn = 80f, speed = 18f, accel = 1.1f },
        };

        class TransportMats { public Material wood, head, tail, screen, tint, trim, fabric; }

        static TransportMats BuildTransportMats() => new TransportMats
        {
            wood = Flat("Transport_OakDeck", Hex(0x78350f), 0.1f),
            head = Flat("Transport_Headlight", new Color(1f, 0.97f, 0.86f), 0.9f),
            tail = Flat("Transport_TailLight", Hex(0xdc2626), 0.7f),
            screen = Flat("Transport_Screen", new Color(0.12f, 0.5f, 0.62f), 0.9f),
            tint = CabGlass("Transport_TintGlass", new Color(0.12f, 0.16f, 0.2f), 0.6f),
            trim = Flat("Transport_BlackTrim", new Color(0.05f, 0.05f, 0.06f), 0.3f),
            fabric = Flat("Transport_SeatFabric", new Color(0.18f, 0.17f, 0.16f), 0.1f),
        };

        static Quaternion RY(float rad) => Quaternion.Euler(0f, -rad * Mathf.Rad2Deg, 0f);

        /// <summary>A half ring (wheel-arch flare) in the X-Y plane, angles in radians from +X.</summary>
        static List<Vector2> ArcPoly(float rOut, float rIn, float a0, float a1, int n = 10)
        {
            var l = new List<Vector2>();
            for (int i = 0; i <= n; i++) { float a = Mathf.Lerp(a0, a1, i / (float)n); l.Add(new Vector2(Mathf.Cos(a) * rOut, Mathf.Sin(a) * rOut)); }
            for (int i = n; i >= 0; i--) { float a = Mathf.Lerp(a0, a1, i / (float)n); l.Add(new Vector2(Mathf.Cos(a) * rIn, Mathf.Sin(a) * rIn)); }
            return l;
        }

        static BoxCollider ColBox(Transform parent, string name, Vector3 center, Vector3 size, Quaternion rot)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center; go.transform.localRotation = rot;
            var b = go.AddComponent<BoxCollider>();
            b.size = size;
            return b;
        }
        static BoxCollider ColBox(Transform parent, string name, Vector3 center, Vector3 size) => ColBox(parent, name, center, size, Quaternion.identity);

        // one baked mesh per wheel type per vehicle, copied onto every wheel of that type
        static readonly Dictionary<string, GameObject> wheelProto = new();
        static void TruckWheel(Transform node, string asset, float r, float w, float[] zs, FleetMats fm, Material rim)
        {
            if (wheelProto.TryGetValue(asset, out var proto) && proto != null)
            {
                var m = Object.Instantiate(proto, node, false); m.name = "Mesh";
                return;
            }
            var g = new Geo();
            foreach (float z in zs)
            {
                GeoWheel(g, F(0f, 0f, z), r, w, fm.shoe, rim, fm.dark, 16);
                // Alcoa-style polished disc and lug nuts on the outer face
                float side = z >= 0f ? 1f : -1f;
                g.Cyl(fm.chrome, F(0f, 0f, z + side * w * 0.52f), AxisLat, r * 0.5f, r * 0.5f, 0.012f, 16);
                for (int k = 0; k < 8; k++)
                {
                    float a = k / 8f * Mathf.PI * 2f;
                    g.Cyl(fm.dark, F(Mathf.Cos(a) * r * 0.18f, Mathf.Sin(a) * r * 0.18f, z + side * w * 0.56f), AxisLat, 0.016f, 0.016f, 0.03f, 6);
                }
            }
            g.Attach(node, asset);
            wheelProto[asset] = node.Find("Mesh").gameObject;
        }

        // ------------------------------------------------------------------ semi tractor + RGN lowboy

        static GameObject BuildSemi(SemiCfg c, FleetMats fm, TransportMats tm)
        {
            wheelProto.Clear();
            var paint = fm.Paint(Hex(c.paint));
            var accent = fm.Paint(Hex(c.accent));
            const float rT = 0.54f;
            float cabL = c.cabL, cabW = c.cabW, cabH = c.cabH;
            float fifthX = -1.35f - (c.driveAxles - 2) * 0.65f;
            float lastDriveX = -0.75f - (c.driveAxles - 1) * 1.35f;
            float driveMid = -0.75f - (c.driveAxles - 1) * 0.675f;
            float ox = -driveMid; // the tractor pivots about the middle of its drive axles
            Vector3 P(float x, float y, float z) => F(x + ox, y, z);

            var root = new GameObject(c.name);
            var t = root.transform;
            var g = new Geo();

            // --- chassis rails, fifth wheel, fenders
            float railFront = 1.35f + (4.9f + c.driveAxles * 1.35f) * 0.15f, railRear = lastDriveX - rT - 0.35f;
            foreach (float z in new[] { -0.42f, 0.42f })
                g.Box(accent, P((railFront + railRear) * 0.5f, rT + 0.32f, z), FS(railFront - railRear, 0.28f, 0.10f));
            for (int i = 0; i < c.driveAxles + 2; i++)
                g.Box(fm.dark, P(Mathf.Lerp(railRear + 0.2f, railFront - 0.3f, i / (float)(c.driveAxles + 1)), rT + 0.3f, 0f), FS(0.12f, 0.2f, 0.84f));
            g.Box(fm.dark, P(fifthX, rT + 0.4f, 0f), FS(1.0f, 0.12f, 1.0f));
            g.Cyl(fm.dark, P(fifthX, rT + 0.48f, 0f), Quaternion.identity, 0.48f, 0.48f, 0.08f, 22);
            g.Box(tm.trim, P(fifthX - 0.3f, rT + 0.53f, 0f), FS(0.5f, 0.02f, 0.12f)); // the kingpin slot
            foreach (int s in new[] { -1, 1 })
            {
                g.Prism(paint, ArcPoly(rT + 0.22f, rT + 0.08f, 0.08f, Mathf.PI - 0.08f), (p, z) => P(2.55f + p.x, rT + p.y, z), s * 1.015f - 0.22f, s * 1.015f + 0.22f);
                g.Box(fm.amber, P(2.55f - rT - 0.24f, rT + 0.16f, s * 1.13f), FS(0.12f, 0.05f, 0.03f));
                g.Prism(fm.chrome, ArcPoly(rT + 0.14f, rT + 0.1f, 0.45f, Mathf.PI - 0.45f), (p, z) => P(lastDriveX + p.x, rT + 0.02f + p.y, z), s * 1.05f - 0.32f, s * 1.05f + 0.32f);
                g.Box(tm.trim, P(lastDriveX - rT - 0.12f, rT - 0.06f, s * 1.05f), FS(0.03f, 0.62f, 0.62f));
                // round fuel tank under the cab step
                g.Cyl(fm.chrome, P(1.72f - cabL * 0.5f, rT + 0.12f, s * 0.88f), AxisFwd, 0.3f, 0.3f, cabL * 0.62f, 16);
            }

            // --- hood, grille, lights, bumper
            const float hoodX = 1.72f, hoodLen = 1.78f;
            var hood = new List<Vector2> { new(0f, 0f), new(hoodLen, 0f), new(hoodLen, 0.78f) };
            Quadratic(hood, new Vector2(hoodLen * 0.55f, 1.06f), new Vector2(0f, 1.24f), 6);
            g.Prism(paint, hood, (p, z) => P(hoodX + p.x, rT + 0.44f + p.y, z), -cabW * 0.38f, cabW * 0.38f);
            g.Box(fm.chrome, P(hoodX + hoodLen + 0.04f, rT + 0.86f, 0f), FS(0.10f, 0.94f, cabW * 0.54f));
            g.Box(tm.trim, P(hoodX + hoodLen + 0.06f, rT + 0.86f, 0f), FS(0.10f, 0.84f, cabW * 0.48f));
            for (int i = 0; i < 7; i++)
                g.Box(fm.chrome, P(hoodX + hoodLen + 0.115f, rT + 0.48f + i * 0.125f, 0f), FS(0.02f, 0.03f, cabW * 0.48f));
            foreach (int s in new[] { -1, 1 })
            {
                g.Box(tm.head, P(hoodX + hoodLen - 0.04f, rT + 0.62f, s * cabW * 0.36f), FS(0.24f, 0.16f, 0.34f), RY(s > 0 ? -0.24f : 0.24f));
                g.Cyl(tm.trim, P(hoodX + hoodLen - 0.18f, rT + 1.24f, s * cabW * 0.41f), Quaternion.identity, 0.015f, 0.015f, 0.28f, 6);
                g.Cyl(fm.chrome, P(hoodX + hoodLen - 0.18f, rT + 1.34f, s * cabW * 0.47f), Quaternion.identity, 0.08f, 0.08f, 0.1f, 10);
            }
            g.Box(paint, P(hoodX + hoodLen + 0.05f, rT + 0.16f, 0f), FS(0.26f, 0.36f, cabW * 0.96f));
            g.Box(tm.trim, P(hoodX + hoodLen + 0.04f, rT - 0.06f, 0f), FS(0.24f, 0.12f, cabW * 0.94f));
            foreach (int s in new[] { -1, 1 })
            {
                g.Box(paint, P(hoodX - cabL * 0.5f, rT + 0.24f, s * cabW * 0.49f), FS(cabL * 1.02f, 0.56f, 0.06f));
                foreach (float ys in new[] { 0.08f, 0.32f })
                    g.Box(fm.chrome, P(hoodX - 0.62f, rT + ys, s * cabW * 0.505f), FS(0.85f, 0.04f, 0.12f));
            }

            // --- cab + condo sleeper: an open shell, so you can sit inside it and see out
            float cgx = hoodX - cabL * 0.5f, cgy = rT + 0.46f;
            Vector3 C(float x, float y, float z) => P(cgx + x, cgy + y, z);
            g.Box(accent, C(0f, 0f, 0f), FS(cabL, 0.08f, cabW));
            g.Box(paint, C(cabL * 0.48f, 0.52f, 0f), FS(0.14f, 1.04f, cabW * 0.98f));
            foreach (int s in new[] { -1, 1 })
            {
                g.Box(paint, C(cabL * 0.27f, 0.52f, s * cabW * 0.49f), FS(cabL * 0.48f, 1.04f, 0.07f));
                g.Box(tm.trim, C(cabL * 0.42f, 1.32f, s * cabW * 0.53f), FS(0.22f, 0.04f, 0.24f));
                g.Box(fm.chrome, C(cabL * 0.44f, 1.24f, s * cabW * 0.59f), FS(0.10f, 0.48f, 0.18f));
                g.Box(fm.glass, C(cabL * 0.25f, 1.41f, s * cabW * 0.485f), FS(cabL * 0.38f, 0.72f, 0.02f));
                g.Box(paint, C(cabL * 0.44f, 1.33f, s * cabW * 0.47f), FS(0.08f, 0.94f, 0.06f), RZ(0.16f));
                g.Box(paint, C(cabL * 0.04f, 1.33f, s * cabW * 0.47f), FS(0.10f, 0.94f, 0.07f));
                // the sleeper's side walls, tinted bunk windows, the cab extender wings and the chrome stacks
                g.Box(paint, C(-cabL * 0.22f, cabH * 0.48f, s * cabW * 0.49f), FS(cabL * 0.56f, cabH * 0.96f, 0.05f));
                g.Box(tm.tint, C(-cabL * 0.22f, 1.22f, s * cabW * 0.504f), FS(0.56f, 0.52f, 0.02f));
                g.Box(tm.tint, C(cabL * 0.10f, 2.22f, s * 0.56f), FS(0.14f, 0.24f, 0.82f), RZ(-0.62f));
                g.Box(accent, C(-cabL * 0.54f, cabH * 0.64f, s * cabW * 0.49f), FS(0.36f, cabH * 1.26f, 0.05f));
                g.Cyl(fm.chrome, C(-cabL * 0.46f, cabH * 0.66f, s * cabW * 0.505f), Quaternion.identity, 0.065f, 0.065f, cabH * 1.24f, 14);
                g.Cyl(tm.trim, C(-cabL * 0.46f, cabH * 1.28f + 0.02f, s * cabW * 0.505f), Quaternion.identity, 0.07f, 0.07f, 0.05f, 14);
            }
            g.Box(fm.glass, C(cabL * 0.47f, 1.42f, 0f), FS(0.02f, 0.78f, cabW * 0.88f), RZ(0.16f));
            g.Box(paint, C(cabL * 0.23f, 1.80f, 0f), FS(cabL * 0.48f, 0.08f, cabW * 0.98f));
            g.Box(paint, C(cabL * 0.46f, 1.78f, 0f), FS(0.24f, 0.14f, cabW * 0.98f), RZ(-0.25f));
            foreach (float zl in new[] { -0.75f, -0.38f, 0f, 0.38f, 0.75f })
                g.Box(fm.amber, C(cabL * 0.5f, 1.84f, zl), FS(0.05f, 0.04f, 0.08f));
            g.Box(paint, C(-cabL * 0.49f, cabH * 0.48f, 0f), FS(0.05f, cabH * 0.96f, cabW)); // sleeper back wall
            var condo = new List<Vector2> { new(-cabL * 0.5f, 0f), new(cabL * 0.36f, 0f) };
            Quadratic(condo, new Vector2(cabL * 0.16f, 0.74f), new Vector2(-cabL * 0.5f, 0.86f), 6);
            g.Prism(paint, condo, (p, z) => C(p.x, 1.82f + p.y, z), -cabW * 0.48f, cabW * 0.48f);

            // --- interior: wrap-around dash, gauges, passenger air-ride seat, console + shifter, the bunks
            float iL = cabL * 0.92f, iW = cabW * 0.92f;
            Vector3 I(float x, float y, float z) => C(x, 0.05f + y, z);
            g.Box(fm.dark, I(iL * 0.34f, 0.54f, 0f), FS(0.58f, 0.54f, iW * 0.94f));
            g.Box(tm.screen, I(iL * 0.34f - 0.295f, 0.68f, -iW * 0.24f), FS(0.01f, 0.2f, 0.46f));
            g.Box(tm.screen, I(iL * 0.34f - 0.295f, 0.68f, 0.02f), FS(0.01f, 0.2f, 0.26f), RY(0.18f));
            {
                float zs = iW * 0.24f;
                g.Box(fm.dark, I(-0.04f, 0.18f, zs), FS(0.44f, 0.36f, 0.46f));
                g.Box(tm.fabric, I(0f, 0.40f, zs), FS(0.54f, 0.13f, 0.54f));
                g.Box(tm.fabric, I(-0.23f, 0.82f, zs), FS(0.15f, 0.82f, 0.52f), RZ(0.10f));
                g.Box(tm.fabric, I(-0.28f, 1.30f, zs), FS(0.12f, 0.22f, 0.30f));
            }
            g.Box(fm.dark, I(0.05f, 0.14f, 0f), FS(0.68f, 0.28f, 0.26f));
            g.Cyl(fm.chrome, I(0.10f, 0.36f, -0.05f), RZ(-0.15f), 0.022f, 0.015f, 0.62f, 8);
            g.Cyl(fm.dark, I(0.15f, 0.66f, -0.05f), Quaternion.identity, 0.044f, 0.044f, 0.08f, 10);
            g.Box(fm.dark, I(-iL * 0.30f, 0.22f, 0f), FS(1.02f, 0.42f, iW * 0.92f));
            g.Box(tm.fabric, I(-iL * 0.30f, 0.50f, 0f), FS(0.96f, 0.16f, iW * 0.88f));
            g.Box(fm.dark, I(-iL * 0.32f, 1.38f, 0f), FS(0.88f, 0.10f, iW * 0.90f));
            g.Box(tm.fabric, I(-iL * 0.32f, 1.48f, 0f), FS(0.84f, 0.12f, iW * 0.86f));
            g.Attach(t, c.id + "_Tractor");

            var seat = Marker(t, "CabView", I(0.02f, 1.2f, -iW * 0.24f));
            CabInterior(seat, CabStyle.Truck, CabMatsFrom(fm, accent), c.id);

            // --- wheels: steering front axle, dual drive axles
            var steers = new List<Transform>(); var spins = new List<Transform>();
            foreach (int s in new[] { -1, 1 })
            {
                var st = Node(t, s < 0 ? "Steer L" : "Steer R", P(2.55f, rT, s * 1.08f));
                var w = Node(st, "Wheel", Vector3.zero);
                TruckWheel(w, c.id + "_SteerWheel", rT, 0.32f, new[] { s * 0.0f }, fm, fm.chrome);
                steers.Add(st); spins.Add(w);
            }
            for (int ax = 0; ax < c.driveAxles; ax++)
            {
                float x = -0.75f - ax * 1.35f;
                var gx = new Geo();
                gx.Cyl(fm.dark, P(x, rT, 0f), AxisLat, 0.11f, 0.11f, 1.9f, 10);
                gx.Box(fm.dark, P(x, rT + 0.08f, 0f), FS(0.34f, 0.3f, 0.5f));
                gx.Attach(t, $"{c.id}_DriveAxle{ax}");
                foreach (int s in new[] { -1, 1 })
                {
                    var w = Node(t, $"Drive {ax} {(s < 0 ? "L" : "R")}", P(x, rT, s * 1.02f));
                    TruckWheel(w, c.id + "_DualWheel", rT, 0.29f, new[] { -0.18f, 0.18f }, fm, fm.chrome);
                    spins.Add(w);
                }
            }

            var fifth = Node(t, "Fifth Wheel", P(fifthX, rT + 0.48f, 0f));
            var hitch = fifth.gameObject.AddComponent<Hitch>();
            hitch.type = HitchType.FifthWheel;

            // --- machine script, body, collider
            var semi = root.AddComponent<SemiTruck>();
            semi.displayName = c.name;
            semi.fuelCapacity = c.fuel; semi.fuel = c.fuel * 0.7f; semi.idleBurn = c.burn * 0.1f; semi.workBurn = c.burn;
            semi.driveSpeed = c.speed; semi.reverseSpeed = 4f; semi.acceleration = c.accel; semi.turnSpeed = 15f; semi.turnInPlace = false;
            semi.steerPivots = steers.ToArray(); semi.wheelSpins = spins.ToArray(); semi.wheelRadius = rT;
            semi.fifthWheel = hitch;
            semi.centerOfMass = new Vector3(0f, 1.0f, 1.4f);
            semi.timeScaleHours = 1f / 600f;
            var rb = root.GetComponent<Rigidbody>();
            rb.mass = c.mass; rb.angularDamping = 3f; rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var col = root.AddComponent<BoxCollider>();
            float rearEnd = lastDriveX - rT - 0.2f, frontEnd = hoodX + hoodLen + 0.2f, topY = cgy + 1.82f + 0.86f;
            col.center = new Vector3(0f, topY * 0.5f, (rearEnd + frontEnd) * 0.5f + ox);
            col.size = new Vector3(2.7f, topY, frontEnd - rearEnd);
            semi.exitPoint = Marker(t, "ExitPoint", P(1.72f - cabL * 0.3f, 0f, -(cabW * 0.5f + 1.1f)));
            semi.brakeDecel = 4f;
            semi.vehicleCamera = VehicleCam(t, t, seat, 24f, 8f);

            BuildLowboy(c, fm, tm, accent, t, P(fifthX, rT + 0.48f, 0f));
            return root;
        }

        /// <summary>The RGN lowboy, built riding on the tractor (it unhooks into its own body when the game starts).</summary>
        static void BuildLowboy(SemiCfg c, FleetMats fm, TransportMats tm, Material accent, Transform tractor, Vector3 hitchAt)
        {
            const float rT = 0.54f;
            float L = c.deckL, W = c.deckW;
            var tr = Node(tractor, "RGN Lowboy", hitchAt);
            var g = new Geo();

            // --- removable gooseneck (rides on the fifth wheel; hydraulics hold the deck)
            var neck = Node(tr, "Gooseneck", Vector3.zero);
            var gn = new Geo();
            gn.Box(accent, F(-1.15f, 0.22f, 0f), FS(2.65f, 0.26f, W * 0.48f));
            gn.Box(accent, F(-2.55f, -0.22f, 0f), FS(1.15f, 0.92f, W * 0.48f), RZ(0.42f));
            gn.Box(fm.dark, F(0f, 0.05f, 0f), FS(1.1f, 0.06f, W * 0.5f));
            gn.Cyl(fm.pin, F(0f, -0.02f, 0f), Quaternion.identity, 0.045f, 0.045f, 0.1f, 8);
            foreach (int s in new[] { -1, 1 })
            {
                // neck lift rams and the stub deck-lock arms
                gn.Cyl(fm.dark, F(-1.9f, 0.05f, s * W * 0.2f), RZ(0.9f), 0.07f, 0.07f, 0.8f, 10);
                gn.Cyl(fm.chrome, F(-2.2f, -0.2f, s * W * 0.2f), RZ(0.9f), 0.04f, 0.04f, 0.5f, 8);
                gn.Box(fm.dark, F(-2.98f, -0.6f, s * W * 0.18f), FS(0.18f, 0.16f, 0.14f));
            }
            gn.Box(fm.amber, F(-0.2f, 0.37f, 0f), FS(0.1f, 0.04f, 0.3f));
            gn.Attach(neck, c.id + "_Gooseneck");
            ColBox(neck, "Neck Top", F(-1.15f, 0.22f, 0f), FS(2.65f, 0.26f, W * 0.48f));
            ColBox(neck, "Neck Drop", F(-2.55f, -0.22f, 0f), FS(1.15f, 0.92f, W * 0.48f), RZ(0.42f));
            var neckSocket = Marker(neck, "NeckSocket", F(-2.95f, -0.62f, 0f));

            // --- well deck with oak planks and outrigger brackets
            const float deckY = -0.62f;
            float dCx = -2.95f - L * 0.5f;
            g.Box(accent, F(dCx, deckY, 0f), FS(L, 0.24f, W));
            g.Box(tm.wood, F(dCx, deckY + 0.13f, 0f), FS(L * 0.96f, 0.04f, W * 0.84f));
            int brackets = Mathf.RoundToInt(L / 0.8f);
            foreach (int s in new[] { -1, 1 })
                for (int i = 0; i <= brackets; i++)
                    g.Box(fm.dark, F(-2.95f - 0.15f - i * (L - 0.3f) / brackets, deckY - 0.02f, s * W * 0.51f), FS(0.08f, 0.18f, 0.06f));
            g.Box(fm.dark, F(-3.05f, deckY + 0.02f, 0f), FS(0.2f, 0.2f, W));
            ColBox(tr, "Deck", F(dCx, deckY, 0f), FS(L, 0.24f, W));
            var socket = Marker(tr, "DeckSocket", F(-2.95f, deckY, 0f));
            var deckArea = Marker(tr, "DeckArea", F(dCx, deckY + 0.15f, 0f));

            // --- kick-up and rear bogie
            float bogieStart = -2.95f - L - 0.55f, bogieLen = c.trailerAxles * 1.35f + 0.4f;
            g.Box(accent, F(-2.95f - L - 0.12f, -0.34f, 0f), FS(0.3f, 0.56f, W));
            g.Box(accent, F(bogieStart - bogieLen * 0.5f + 0.4f, -0.02f, 0f), FS(bogieLen, 0.26f, W));
            ColBox(tr, "Bogie", F(bogieStart - bogieLen * 0.5f + 0.4f, -0.02f, 0f), FS(bogieLen, 0.26f, W));
            ColBox(tr, "Kick-up", F(-2.95f - L - 0.12f, -0.34f, 0f), FS(0.3f, 0.56f, W));
            float rearTail = bogieStart - bogieLen + 0.35f;
            g.Box(accent, F(rearTail, -0.14f, 0f), FS(0.16f, 0.22f, W));
            foreach (int s in new[] { -1, 1 })
            {
                g.Box(tm.tail, F(rearTail - 0.08f, -0.14f, s * W * 0.38f), FS(0.06f, 0.10f, 0.32f));
                g.Box(tm.trim, F(rearTail + 0.12f, -0.5f, s * W * 0.44f), FS(0.03f, 0.5f, 0.55f));
            }

            var wheels = new List<WheelCollider>(); var vis = new List<Transform>();
            float gross = (5000f + c.payload * 300f) + c.payload * 1000f;
            int nWheels = c.trailerAxles * 2 + (c.booster ? 4 : 0);
            float perWheel = gross * 0.7f / nWheels;
            void Wheel(float x, float z, float w, float[] duals, string asset)
            {
                var wgo = new GameObject("Wheel " + wheels.Count);
                wgo.transform.SetParent(tr, false);
                wgo.transform.localPosition = F(x, -0.48f + 0.1f, z);
                var wc = wgo.AddComponent<WheelCollider>();
                wc.radius = rT; wc.mass = 120f; wc.suspensionDistance = 0.25f; wc.forceAppPointDistance = 0.2f;
                float spring = perWheel * 9.81f * 8f;
                wc.suspensionSpring = new JointSpring { spring = spring, damper = spring * 0.12f, targetPosition = 0.5f };
                var sf = wc.sidewaysFriction; sf.stiffness = 1.6f; wc.sidewaysFriction = sf;
                wheels.Add(wc);
                var v = Node(tr, "Wheel Visual " + vis.Count, F(x, -0.48f, z));
                TruckWheel(v, asset, rT, w, duals, fm, fm.chrome);
                vis.Add(v);
            }
            for (int a = 0; a < c.trailerAxles; a++)
            {
                float x = bogieStart - a * 1.35f;
                g.Cyl(fm.dark, F(x, -0.48f, 0f), AxisLat, 0.1f, 0.1f, W * 0.8f, 10);
                foreach (int s in new[] { -1, 1 }) Wheel(x, s * W * 0.44f, 0.28f, new[] { -0.16f, 0.16f }, c.id + "_TrailerDual");
            }
            if (c.booster)
            {
                float bs = bogieStart - bogieLen - 0.6f;
                g.Box(accent, F(bs - 1.2f, -0.02f, 0f), FS(3.2f, 0.24f, W * 0.85f));
                g.Box(accent, F(bs + 0.25f, -0.02f, 0f), FS(1.0f, 0.18f, 0.5f));
                g.Box(tm.tail, F(bs - 2.85f, -0.02f, 0f), FS(0.06f, 0.1f, W * 0.7f));
                ColBox(tr, "Booster", F(bs - 1.2f, -0.02f, 0f), FS(3.2f, 0.24f, W * 0.85f));
                foreach (float bx in new[] { bs - 1.1f, bs - 2.45f })
                {
                    g.Cyl(fm.dark, F(bx, -0.48f, 0f), AxisLat, 0.1f, 0.1f, W * 0.8f, 10);
                    foreach (int s in new[] { -1, 1 }) Wheel(bx, s * W * 0.44f, 0.32f, new[] { 0f }, c.id + "_BoosterWheel");
                }
            }
            g.Attach(tr, c.id + "_LowboyDeck");

            var rb = tr.gameObject.AddComponent<Rigidbody>();
            rb.mass = 5000f + c.payload * 300f; rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.angularDamping = 0.6f; rb.isKinematic = true; // until it unhooks from the tractor's hierarchy
            var trl = tr.gameObject.AddComponent<Trailer>();
            trl.displayName = $"RGN lowboy ({c.payload:0} t)";
            trl.coupler = HitchType.FifthWheel;
            trl.couplerPoint = Marker(neck, "Kingpin", Vector3.zero);
            trl.wheels = wheels.ToArray(); trl.wheelVisuals = vis.ToArray();
            trl.parkBrake = perWheel * 3f;
            trl.payloadTons = c.payload;
            trl.gooseneck = neck; trl.deckSocket = socket; trl.neckSocket = neckSocket;
            trl.deckArea = deckArea; trl.deckSize = new Vector2(W, L);
        }

        // ------------------------------------------------------------------ GMT400 3/4-ton 4x4 (starter truck)

        static GameObject BuildGMT400(FleetMats fm, TransportMats tm, Material dirt)
        {
            wheelProto.Clear();
            const string id = "GMT400";
            var red = fm.Paint(Hex(0xb91c1c));
            var silver = fm.Paint(Hex(0xd1d5db));
            const float rT = 0.40f;
            var root = new GameObject("GMT400 3/4-Ton 4x4 Pickup");
            var t = root.transform;
            var g = new Geo();

            g.Box(fm.dark, F(0f, rT + 0.18f, 0f), FS(5.15f, 0.16f, 0.86f));
            g.Box(red, F(1.62f, rT + 0.64f, 0f), FS(1.44f, 0.56f, 1.88f));
            g.Box(red, F(1.62f, rT + 0.925f, 0f), FS(1.42f, 0.025f, 0.12f));
            g.Box(silver, F(1.62f, rT + 0.28f, 0f), FS(1.45f, 0.24f, 1.90f));
            g.Box(fm.shoe, F(1.62f, rT + 0.39f, 0f), FS(1.46f, 0.04f, 1.92f));
            foreach (float xw in new[] { 1.68f, -1.68f })
                foreach (int s in new[] { -1, 1 })
                    g.Prism(red, ArcPoly(rT + 0.14f, rT + 0.06f, 0.05f, Mathf.PI - 0.05f), (p, z) => F(xw + p.x, rT + p.y, z), s * 0.95f - 0.04f, s * 0.95f + 0.04f);
            // chrome split-bar egg-crate grille, quad sealed beams, amber turn signals and corner markers
            g.Box(fm.chrome, F(2.34f, rT + 0.62f, 0f), FS(0.06f, 0.46f, 1.86f));
            g.Box(tm.trim, F(2.35f, rT + 0.62f, 0f), FS(0.07f, 0.42f, 1.12f));
            for (int i = -2; i <= 2; i++) g.Box(fm.chrome, F(2.39f, rT + 0.62f, i * 0.22f), FS(0.02f, 0.4f, 0.025f));
            g.Box(fm.chrome, F(2.39f, rT + 0.62f, 0f), FS(0.025f, 0.03f, 1.12f));
            foreach (int s in new[] { -1, 1 })
            {
                foreach (float zs in new[] { -0.08f, 0.08f })
                    g.Box(tm.head, F(2.345f, rT + 0.71f, s * 0.74f + zs), FS(0.075f, 0.14f, 0.14f));
                g.Box(fm.amber, F(2.345f, rT + 0.51f, s * 0.74f), FS(0.075f, 0.13f, 0.30f));
                g.Box(fm.amber, F(2.28f, rT + 0.62f, s * 0.945f), FS(0.14f, 0.34f, 0.04f));
            }
            g.Box(fm.chrome, F(2.43f, rT + 0.24f, 0f), FS(0.18f, 0.20f, 1.98f));
            g.Box(fm.shoe, F(2.435f, rT + 0.24f, 0f), FS(0.19f, 0.04f, 1.99f));
            g.Box(fm.shoe, F(2.40f, rT + 0.09f, 0f), FS(0.14f, 0.10f, 1.88f));
            foreach (float zg in new[] { -0.48f, 0.48f }) g.Box(fm.shoe, F(2.44f, rT + 0.24f, zg), FS(0.22f, 0.24f, 0.07f));

            // --- regular cab: door skins, two-tone trim, greenhouse, roof, rear wall + window, pillars, interior
            Vector3 Cb(float x, float y, float z) => F(0.28f + x, rT + 0.22f + y, z);
            g.Box(fm.dark, Cb(0f, 0f, 0f), FS(1.26f, 0.06f, 1.86f));
            foreach (int s in new[] { -1, 1 })
            {
                g.Box(red, Cb(0f, 0.28f, s * 0.91f), FS(1.26f, 0.52f, 0.06f));
                g.Box(silver, Cb(0f, 0.11f, s * 0.915f), FS(1.28f, 0.22f, 0.07f));
                g.Box(fm.shoe, Cb(0f, 0.22f, s * 0.918f), FS(1.28f, 0.04f, 0.075f));
                g.Box(red, Cb(0.54f, 0.81f, s * 0.89f), FS(0.06f, 0.58f, 0.06f), RZ(0.22f));
                g.Box(red, Cb(-0.58f, 0.81f, s * 0.89f), FS(0.10f, 0.58f, 0.07f));
                g.Box(fm.chrome, Cb(0.62f, 0.72f, s * 1.02f), FS(0.06f, 0.2f, 0.14f));
                g.Box(red, Cb(-0.59f, 0.81f, s * 0.81f), FS(0.08f, 0.56f, 0.23f)); // rear wall beside the window
            }
            g.Box(fm.glass, Cb(0f, 0.80f, 0f), FS(1.18f, 0.56f, 1.80f));
            g.Box(red, Cb(0f, 1.10f, 0f), FS(1.22f, 0.07f, 1.84f));
            g.Box(red, Cb(-0.59f, 0.27f, 0f), FS(0.08f, 0.54f, 1.86f));
            g.Box(silver, Cb(-0.592f, 0.11f, 0f), FS(0.085f, 0.22f, 1.87f));
            g.Box(fm.shoe, Cb(-0.594f, 0.22f, 0f), FS(0.09f, 0.04f, 1.88f));
            g.Box(red, Cb(-0.59f, 1.06f, 0f), FS(0.08f, 0.06f, 1.38f));
            g.Box(tm.tint, Cb(-0.59f, 0.82f, 0f), FS(0.03f, 0.42f, 1.38f));
            // dash, gauges, bench seat (the driver's half comes with the cab controls)
            g.Box(fm.dark, Cb(0.46f, 0.42f, 0f), FS(0.30f, 0.30f, 1.70f));
            g.Box(tm.screen, Cb(0.30f, 0.52f, -0.40f), FS(0.01f, 0.12f, 0.36f));
            g.Box(tm.fabric, Cb(-0.25f, 0.30f, 0.40f), FS(0.50f, 0.12f, 0.86f));
            g.Box(tm.fabric, Cb(-0.48f, 0.62f, 0.40f), FS(0.12f, 0.56f, 0.86f), RZ(0.12f));
            g.Attach(t, id + "_Body");

            // fixed rear quarters, steps, bumper, pintle hook
            var gq = new Geo();
            Vector3 Bq(float x, float y, float z) => F(-1.42f + x, rT + 0.22f + y, z);
            foreach (int s in new[] { -1, 1 })
            {
                gq.Box(red, Bq(-0.26f, 0.28f, s * 0.82f), FS(1.48f, 0.44f, 0.24f));
                gq.Box(silver, Bq(0f, 0.10f, s * 0.82f), FS(2.14f, 0.20f, 0.25f));
                gq.Box(fm.shoe, Bq(0f, 0.20f, s * 0.82f), FS(2.15f, 0.04f, 0.255f));
                gq.Box(fm.shoe, Bq(0.76f, 0.22f, s * 0.82f), FS(0.56f, 0.05f, 0.22f));
            }
            gq.Box(fm.chrome, Bq(-1.12f, 0.04f, 0f), FS(0.18f, 0.20f, 1.94f));
            gq.Box(fm.dark, F(-2.62f, 0.58f, 0f), FS(0.18f, 0.1f, 0.16f));
            gq.Cyl(fm.pin, F(-2.7f, 0.6f, 0f), Quaternion.identity, 0.04f, 0.04f, 0.14f, 8);
            gq.Attach(t, id + "_Quarters");
            var pintle = Node(t, "Pintle Hook", F(-2.7f, 0.56f, 0f));
            pintle.gameObject.AddComponent<Hitch>().type = HitchType.Pintle;
            // removable gooseneck ball: a plate on the bed floor just ahead of the rear axle (fitted from the menu)
            var goose = Node(t, "Gooseneck Ball", F(-1.56f, rT + 0.22f + 0.12f + 0.04f + 0.12f, 0f));
            var gg0 = new Geo();
            gg0.Box(fm.dark, F(0f, -0.11f, 0f), FS(0.42f, 0.02f, 0.42f));
            gg0.Cyl(fm.dark, F(0f, -0.07f, 0f), Quaternion.identity, 0.05f, 0.05f, 0.07f, 12);
            gg0.Cyl(fm.chrome, F(0f, -0.03f, 0f), Quaternion.identity, 0.03f, 0.044f, 0.03f, 12);
            gg0.Cyl(fm.chrome, F(0f, -0.01f, 0f), Quaternion.identity, 0.044f, 0.02f, 0.03f, 12);
            gg0.Attach(goose, id + "_GooseBall");
            goose.gameObject.AddComponent<Hitch>().type = HitchType.Gooseneck;

            // --- dump box (hinged at the back), tailgate, tidy tank
            float bedY = rT + 0.22f + 0.12f;
            const float bl = 2.12f, bw = 1.44f, bh = 0.52f;
            var bedPivot = Marker(t, "BedPivot (hinge)", F(-2.48f, bedY, 0f));
            var bed = Marker(bedPivot, "DumpBox", F(bl * 0.5f, 0f, 0f));
            var gb = new Geo();
            gb.Box(red, Vector3.zero, FS(bl, 0.08f, bw));
            foreach (int s in new[] { -1, 1 })
            {
                gb.Box(red, F(0f, bh * 0.5f + 0.04f, s * 0.72f), FS(bl, bh, 0.08f));
                gb.Box(fm.shoe, F(0f, bh + 0.05f, s * 0.72f), FS(bl, 0.03f, 0.1f)); // bed rail caps
            }
            gb.Box(red, F(bl * 0.5f - 0.03f, bh * 0.5f + 0.04f, 0f), FS(0.06f, bh, bw));
            gb.Box(fm.dark, F(-0.2f, -0.12f, 0f), FS(1.4f, 0.14f, 0.3f)); // the hoist under the box
            gb.Attach(bed, id + "_DumpBox");
            ColBox(bed, "Floor", Vector3.zero, FS(bl, 0.08f, bw));
            foreach (int s in new[] { -1, 1 }) ColBox(bed, s < 0 ? "Side L" : "Side R", F(0f, bh * 0.5f + 0.04f, s * 0.72f), FS(bl, bh, 0.08f));
            ColBox(bed, "Headboard", F(bl * 0.5f - 0.03f, bh * 0.5f + 0.04f, 0f), FS(0.06f, bh, bw));
            var gateHinge = Marker(bed, "Tailgate Hinge", F(-bl * 0.5f + 0.03f, bh * 0.92f, 0f));
            var gg = new Geo();
            gg.Box(red, F(0f, -bh * 0.46f, 0f), FS(0.06f, bh * 0.92f, bw - 0.02f));
            gg.Box(silver, F(-0.035f, -bh * 0.3f, 0f), FS(0.01f, 0.1f, 0.6f));
            gg.Attach(gateHinge, id + "_Tailgate");
            Vector3 inner = new Vector3(bw - 0.16f, bh, bl - 0.12f);
            var volGo = new GameObject("BedVolume");
            volGo.transform.SetParent(bed, false);
            var vol = volGo.AddComponent<BoxCollider>();
            vol.isTrigger = true; vol.center = new Vector3(0f, bh * 0.65f, 0f); vol.size = new Vector3(inner.x, bh * 1.3f, inner.z);
            var contents = Part(bed, "Contents (dirt)", new Vector3(0f, 0.05f, 0f), new Vector3(inner.x, 0.04f, inner.z), dirt);
            contents.SetActive(false);
            var mound = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(mound.GetComponent<Collider>());
            mound.name = "Contents mound";
            mound.transform.SetParent(bed, false);
            mound.GetComponent<MeshRenderer>().sharedMaterial = dirt;
            mound.SetActive(false);

            // 420 L tidy tank with a 12 V transfer pump, nozzle and hose reel, strapped in at the front of the box
            const float tankLen = 0.68f;
            var tankNode = Node(bed, "Tidy tank", F(bl * 0.5f - 0.06f - tankLen * 0.5f, 0.04f, 0f));
            var gt = new Geo();
            gt.Box(fm.chrome, F(0f, 0.27f, 0f), FS(tankLen, 0.54f, 1.32f));
            for (int i = -1; i <= 1; i++) gt.Box(fm.wear, F(0f, 0.27f, i * 0.42f), FS(tankLen + 0.01f, 0.55f, 0.02f)); // baffle seams
            gt.Cyl(red, F(0f, 0.64f, -0.35f), Quaternion.identity, 0.08f, 0.08f, 0.28f, 12);
            gt.Box(fm.dark, F(0.06f, 0.72f, -0.35f), FS(0.14f, 0.08f, 0.06f));
            gt.Cyl(tm.trim, F(0.05f, 0.62f, 0.3f), AxisLat, 0.13f, 0.13f, 0.16f, 14);
            gt.Cyl(fm.dark, F(-0.18f, 0.56f, 0.1f), Quaternion.identity, 0.06f, 0.06f, 0.05f, 10); // filler cap
            foreach (int s in new[] { -1, 1 }) gt.Box(fm.amber, F(0f, 0.3f, s * 0.67f), FS(tankLen * 0.9f, 0.04f, 0.02f)); // straps
            gt.Attach(tankNode, id + "_TidyTank");
            var tank = tankNode.gameObject.AddComponent<FuelTank>();
            tank.label = "Tidy tank"; tank.capacity = 420f; tank.liters = 420f; tank.hoseReach = 12f;

            var pk = root.AddComponent<DumpPickup>();
            pk.displayName = "GMT400 3/4-Ton 4x4 Pickup";
            pk.fuelCapacity = 130f; pk.fuel = 90f; pk.idleBurn = 2.2f; pk.workBurn = 18f;
            pk.capacity = 1.5f; pk.topSpeedKmh = 105f;
            const float mass = 2680f;
            pk.motorTorque = mass * 0.45f; pk.brakeTorque = mass * 1.35f; // per wheel in 4-high (a 2WD truck sends 2x that to each rear wheel)
            pk.transferCase = true; pk.lowRange = 2.6f; pk.lowRangeTopKmh = 38f; pk.driveMode = DumpPickup.DriveMode.FourHigh;
            pk.bedPivot = bedPivot; pk.bedContents = contents.transform; pk.bedMound = mound.transform; pk.bedVolume = vol;
            pk.bedInnerSize = inner;
            pk.tailgatePour = Marker(bedPivot, "PourPoint", new Vector3(0f, 0.25f, -0.25f));
            pk.tailgate = gateHinge;
            pk.tidyTank = tank; pk.tidyTankLength = tankLen; pk.tidyTankFitted = true;
            pk.gooseneckBall = goose.gameObject; pk.gooseneckFitted = false;
            pk.exitPoint = Marker(t, "ExitPoint", F(0.28f, 0f, -1.9f));

            var rb = root.GetComponent<Rigidbody>();
            rb.mass = mass; rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.linearDamping = 0.05f; rb.angularDamping = 0.5f;
            float chassisY = rT + 0.25f;
            var chassis = root.AddComponent<BoxCollider>();
            chassis.center = new Vector3(0f, chassisY + 0.1f, 0.05f); chassis.size = new Vector3(1.9f, 0.5f, 5.0f);
            ColBox(t, "Cab Collider", Cb(0f, 0.56f, 0f), FS(1.26f, 1.12f, 1.86f));
            ColBox(t, "Hood Collider", F(1.62f, rT + 0.55f, 0f), FS(1.5f, 0.7f, 1.9f));

            pk.wheels = new WheelCollider[4]; pk.wheelVisuals = new Transform[4];
            float perWheel = mass / 4f;
            for (int i = 0; i < 4; i++)
            {
                float x = i < 2 ? 1.68f : -1.68f, z = i % 2 == 0 ? -0.92f : 0.92f;
                var wgo = new GameObject("Wheel " + i);
                wgo.transform.SetParent(t, false);
                wgo.transform.localPosition = F(x, rT + 0.1f, z);
                var wc = wgo.AddComponent<WheelCollider>();
                wc.radius = rT; wc.mass = 45f; wc.suspensionDistance = 0.3f; wc.forceAppPointDistance = rT * 0.5f;
                float spring = perWheel * 9.81f * 8f;
                wc.suspensionSpring = new JointSpring { spring = spring, damper = spring * 0.1f, targetPosition = 0.5f };
                // a spinning tyre keeps most of its bite (the default curve drops to half, and a pickup with a trailer bogs down)
                var ff = wc.forwardFriction; ff.stiffness = 1.6f; ff.extremumSlip = 0.35f; ff.extremumValue = 1f; ff.asymptoteSlip = 1.2f; ff.asymptoteValue = 0.85f; wc.forwardFriction = ff;
                var sf = wc.sidewaysFriction; sf.stiffness = 1.8f; wc.sidewaysFriction = sf;
                pk.wheels[i] = wc;
                // the pickup turns its wheel visuals by (pose x 90 deg about z): undo that for a mesh whose axle is x
                var v = Node(t, "Wheel Visual " + i, F(x, rT, z));
                var inner2 = Node(v, "Axle", Vector3.zero);
                inner2.localRotation = Quaternion.Euler(0f, 0f, -90f);
                TruckWheel(inner2, id + "_Wheel", rT, 0.27f, new[] { (z < 0f ? -1f : 1f) * 0.0f }, fm, silver);
                pk.wheelVisuals[i] = v;
            }

            var seat = Marker(t, "CabView", Cb(-0.12f, 0.98f, -0.40f));
            CabInterior(seat, CabStyle.Truck, CabMatsFrom(fm, red), id);
            pk.vehicleCamera = VehicleCam(t, t, seat, 8f, 2.6f);
            return root;
        }

        // ------------------------------------------------------------------ equipment trailers (from the trailers visualizer)

        /// <summary>
        /// 7 t bumper-pull hydraulic tilt-deck trailer (pintle / receiver hitch) or 10 t gooseneck tandem-dual trailer
        /// with folding ramps (gooseneck ball in a pickup's bed). Built for the skid steers and the 5 t excavator.
        /// </summary>
        static GameObject BuildEquipTrailer(bool goose, FleetMats fm, TransportMats tm)
        {
            wheelProto.Clear();
            string id = goose ? "Goose10" : "Tilt7";
            float L = goose ? 6.7f : 5.4f, W = goose ? 2.54f : 2.12f;
            var frame = fm.Paint(Hex(goose ? 0x1e293b : 0x0f172a));
            var accent = fm.Paint(Hex(goose ? 0xdc2626 : 0xf59e0b));
            var wood = tm.wood;
            const float rT = 0.39f;
            float deckY = goose ? rT + 0.48f : rT + 0.24f;   // deck centre height
            float axleC = -L * 0.14f, spread = 0.92f;
            float front = L * 0.5f;
            var root = new GameObject(goose ? "Gooseneck Equipment Trailer 10 t" : "Tilt-Deck Equipment Trailer 7 t");
            var t = root.transform;
            var g = new Geo();

            // --- the deck (on its own pivot for the tilt trailer)
            Transform deck = t; float ox = 0f;
            float pivotX = axleC - 0.1f;
            if (!goose) { deck = Node(t, "Tilt Deck (pivot)", F(pivotX, deckY, 0f)); ox = -pivotX; }
            float dy = goose ? deckY : 0f; // deck parts' height in the deck frame
            var gd = goose ? g : new Geo();
            gd.Box(frame, F(ox, dy - 0.02f, 0f), FS(L, 0.06f, W));
            gd.Box(wood, F(ox, dy + 0.035f, 0f), FS(L * 0.98f, 0.05f, W * 0.92f));
            foreach (int s in new[] { -1, 1 })
            {
                float zs = s * W * 0.49f;
                gd.Box(frame, F(ox, dy, zs), FS(L, 0.12f, 0.07f));
                gd.Box(accent, F(ox, dy + 0.02f, zs + s * 0.065f), FS(L * 0.96f, 0.05f, 0.025f));
                for (int k = -3; k <= 3; k++) gd.Box(frame, F(ox + k * L * 0.13f, dy + 0.01f, zs + s * 0.042f), FS(0.09f, 0.09f, 0.045f));
                foreach (float xm in new[] { -L * 0.38f, 0f, L * 0.38f }) gd.Box(fm.amber, F(ox + xm, dy - 0.03f, zs + s * 0.038f), FS(0.08f, 0.03f, 0.02f));
            }
            float rearX = ox - L * 0.5f;
            gd.Box(frame, F(rearX, dy - 0.02f, 0f), FS(0.12f, 0.14f, W * 0.98f));
            foreach (int s in new[] { -1, 1 }) gd.Box(tm.tail, F(rearX - 0.065f, dy - 0.02f, s * W * 0.38f), FS(0.04f, 0.08f, 0.26f));
            if (!goose)
            {
                // beavertail lip and the tilt ram's top mount under the deck
                gd.Box(frame, F(rearX + 0.12f, dy - 0.07f, 0f), FS(0.3f, 0.05f, W * 0.96f), RZ(-0.2f));
                gd.Box(fm.dark, F(ox + 0.9f, dy - 0.1f, 0f), FS(0.16f, 0.1f, 0.2f));
                gd.Attach(deck, id + "_Deck");
            }
            ColBox(deck, "Deck", F(ox, dy - 0.005f, 0f), FS(L, 0.13f, W));
            var deckArea = Marker(deck, "DeckArea", F(ox, dy + 0.06f, 0f));

            // --- running gear: tandem axles, singles (tilt) or duals under the deck (gooseneck), fenders
            var wheels = new List<WheelCollider>(); var vis = new List<Transform>();
            float tare = goose ? 2280f : 1540f, payload = goose ? 6790f : 4810f;
            float perWheel = (tare + payload) * 0.85f / 4f;
            float wz = goose ? W * 0.44f : W * 0.5f + 0.14f;
            foreach (float dx in new[] { -spread * 0.5f, spread * 0.5f })
            {
                float x = axleC + dx;
                g.Cyl(fm.dark, F(x, rT, 0f), AxisLat, 0.045f, 0.045f, wz * 2f - 0.2f, 12);
                foreach (int s in new[] { -1, 1 })
                {
                    var wgo = new GameObject("Wheel " + wheels.Count);
                    wgo.transform.SetParent(t, false);
                    // leaf / torsion sprung: soft, long travel, rests mid-travel at its design height
                    wgo.transform.localPosition = F(x, rT + 0.15f, s * wz);
                    var wc = wgo.AddComponent<WheelCollider>();
                    wc.radius = rT; wc.mass = goose ? 60f : 35f; wc.suspensionDistance = 0.3f; wc.forceAppPointDistance = 0.15f;
                    float spring = perWheel * 9.81f * 6f;
                    wc.suspensionSpring = new JointSpring { spring = spring, damper = spring * 0.16f, targetPosition = 0.1f };
                    var sf = wc.sidewaysFriction; sf.stiffness = 1.5f; wc.sidewaysFriction = sf;
                    wheels.Add(wc);
                    var v = Node(t, "Wheel Visual " + vis.Count, F(x, rT, s * wz));
                    if (goose) TruckWheel(v, id + "_Dual", rT, 0.22f, new[] { -0.14f, 0.14f }, fm, fm.chrome);
                    else TruckWheel(v, id + "_Wheel", rT, 0.24f, new[] { 0f }, fm, fm.chrome);
                    vis.Add(v);
                }
            }
            if (!goose)
                foreach (int s in new[] { -1, 1 })
                {
                    float zf = s * (W * 0.5f + 0.18f);
                    g.Box(frame, F(axleC, rT * 2f + 0.12f, zf), FS(1.88f, 0.05f, 0.32f));
                    g.Box(frame, F(axleC + 1.04f, rT * 2f + 0.04f, zf), FS(0.28f, 0.05f, 0.32f), RZ(-0.58f));
                    g.Box(frame, F(axleC - 1.04f, rT * 2f + 0.04f, zf), FS(0.28f, 0.05f, 0.32f), RZ(0.58f));
                    ColBox(t, s < 0 ? "Fender L" : "Fender R", F(axleC, rT * 2f + 0.12f, zf), FS(1.88f, 0.05f, 0.32f));
                }

            // --- frame, hitch and jack
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = tare; rb.interpolation = RigidbodyInterpolation.Interpolate; rb.angularDamping = 0.5f;
            var trl = root.AddComponent<Trailer>();
            Transform jack;
            if (goose)
            {
                foreach (int s in new[] { -1, 1 }) g.Box(frame, F(0f, deckY - 0.14f, s * W * 0.34f), FS(L * 0.94f, 0.18f, 0.08f));
                foreach (int s in new[] { -1, 1 }) g.Box(frame, F(front + 0.04f, deckY + 0.44f, s * W * 0.34f), FS(0.24f, 0.96f, 0.12f), RZ(-0.14f));
                g.Box(frame, F(front - 0.04f, deckY + 0.21f, 0f), FS(0.08f, 0.42f, W * 0.96f));
                g.Box(fm.chrome, F(front + 0.12f, deckY + 0.24f, 0f), FS(0.42f, 0.34f, W * 0.62f));
                g.Box(frame, F(front + 0.98f, deckY + 0.88f, 0f), FS(2.15f, 0.22f, W * 0.72f));
                const float ballTop = 0.90f;
                float tubeTop = deckY + 0.77f, tubeLen = tubeTop - (ballTop + 0.06f);
                g.Cyl(fm.dark, F(front + 1.98f, ballTop + 0.06f + tubeLen * 0.5f, 0f), Quaternion.identity, 0.065f, 0.065f, tubeLen, 16);
                g.Cyl(accent, F(front + 1.98f, ballTop + 0.06f, 0f), Quaternion.identity, 0.11f, 0.08f, 0.12f, 16);
                g.Box(accent, F(front + 1.6f, deckY + 0.88f, 0f), FS(0.5f, 0.04f, W * 0.73f)); // accent stripe on the neck
                ColBox(t, "Neck", F(front + 0.98f, deckY + 0.88f, 0f), FS(2.15f, 0.22f, W * 0.72f));
                ColBox(t, "Neck Uprights", F(front + 0.06f, deckY + 0.44f, 0f), FS(0.3f, 0.96f, W * 0.8f));
                ColBox(t, "Coupler", F(front + 1.98f, ballTop + 0.06f + tubeLen * 0.5f, 0f), FS(0.16f, tubeLen, 0.16f));
                trl.couplerPoint = Marker(t, "Gooseneck Coupler", F(front + 1.98f, ballTop, 0f));
                trl.coupler = HitchType.Gooseneck;
                // drop-leg landing gear (both legs on one jack)
                jack = Node(t, "Landing Gear", F(front + 0.22f, deckY - 0.16f, 0f));
                var gj = new Geo();
                foreach (int s in new[] { -1, 1 })
                {
                    g.Box(frame, F(front + 0.22f, deckY + 0.14f, s * W * 0.34f), FS(0.11f, 0.72f, 0.11f));
                    gj.Box(fm.chrome, F(0f, 0f, s * W * 0.34f), FS(0.085f, 0.58f, 0.085f));
                    gj.Box(fm.dark, F(0f, -0.29f, s * W * 0.34f), FS(0.22f, 0.03f, 0.22f));
                    ColBox(jack, s < 0 ? "Leg Pad L" : "Leg Pad R", F(0f, -0.25f, s * W * 0.34f), FS(0.22f, 0.1f, 0.22f));
                }
                gj.Attach(jack, id + "_Legs");
                trl.jackDrop = deckY - 0.16f - 0.30f + 0.02f;
                // folding ramps at the back: stowed standing up, lowered onto the ground
                const float rampLen = 2.4f, rampW = 0.9f;
                float hingeY = deckY + 0.06f;
                float drop = Mathf.Asin(Mathf.Clamp01((hingeY - 0.03f) / rampLen)) * Mathf.Rad2Deg;
                var rampList = new List<Transform>();
                foreach (int s in new[] { -1, 1 })
                {
                    var rp = Node(t, s < 0 ? "Ramp L" : "Ramp R", F(-front, hingeY, s * W * 0.25f));
                    var gr = new Geo();
                    gr.Box(accent, F(0f, rampLen * 0.5f, 0f), FS(0.07f, rampLen, rampW));
                    foreach (int e in new[] { -1, 1 }) gr.Box(frame, F(0.02f, rampLen * 0.5f, e * rampW * 0.47f), FS(0.1f, rampLen, 0.05f));
                    for (int k = 0; k < 10; k++) gr.Box(frame, F(0.05f, 0.15f + k * (rampLen - 0.3f) / 9f, 0f), FS(0.04f, 0.04f, rampW * 0.9f)); // cleats
                    gr.Attach(rp, id + "_Ramp");
                    ColBox(rp, "Ramp", F(0f, rampLen * 0.5f, 0f), FS(0.08f, rampLen, rampW));
                    rampList.Add(rp);
                }
                trl.ramps = rampList.ToArray();
                trl.rampSwing = 90f + drop;
            }
            else
            {
                // front frame (pivot forward), A-frame tongue, toolbox, coupler, the tilt ram
                float fl = front - pivotX, fc = (front + pivotX) * 0.5f;
                foreach (int s in new[] { -1, 1 })
                {
                    g.Box(frame, F(fc, deckY - 0.17f, s * W * 0.34f), FS(fl, 0.16f, 0.08f));
                    float zb = s * W * 0.34f, ang = Mathf.Atan2(zb, 1.48f);
                    g.Box(frame, F(front + 0.74f, deckY - 0.12f, zb * 0.5f), FS(1.62f, 0.14f, 0.08f), RY(ang));
                }
                g.Box(frame, F(pivotX, deckY - 0.17f, 0f), FS(0.12f, 0.16f, W * 0.72f));
                g.Cyl(fm.pin, F(pivotX, deckY - 0.09f, 0f), AxisLat, 0.04f, 0.04f, W * 0.9f, 10); // tilt pivot pin
                g.Box(accent, F(front + 1.56f, deckY - 0.10f, 0f), FS(0.32f, 0.14f, 0.14f));
                g.Box(fm.chrome, F(front + 0.42f, deckY + 0.08f, 0f), FS(0.46f, 0.32f, 0.76f));
                g.Cyl(accent, F(front - 0.85f, deckY - 0.16f, 0f), RZ(-0.35f), 0.045f, 0.045f, 0.54f, 14);
                g.Cyl(fm.chrome, F(front - 0.62f, deckY - 0.1f, 0f), RZ(-0.35f), 0.028f, 0.028f, 0.4f, 12);
                g.Box(frame, F(front + 1.12f, deckY + 0.04f, 0f), FS(0.09f, 0.56f, 0.09f));
                ColBox(t, "Front Frame", F(fc, deckY - 0.17f, 0f), FS(fl, 0.16f, W * 0.72f));
                ColBox(t, "Tongue", F(front + 0.8f, deckY - 0.12f, 0f), FS(1.6f, 0.14f, 0.5f));
                ColBox(t, "Toolbox", F(front + 0.42f, deckY + 0.08f, 0f), FS(0.46f, 0.32f, 0.76f));
                trl.couplerPoint = Marker(t, "Coupler", F(front + 1.56f, deckY - 0.10f, 0f));
                trl.coupler = HitchType.Pintle;
                jack = Node(t, "Jack Foot", F(front + 1.12f, deckY - 0.2f, 0f));
                var gj = new Geo();
                gj.Box(fm.chrome, Vector3.zero, FS(0.075f, 0.44f, 0.075f));
                gj.Box(fm.dark, F(0f, -0.22f, 0f), FS(0.2f, 0.03f, 0.2f));
                gj.Attach(jack, id + "_Jack");
                ColBox(jack, "Jack Pad", F(0f, -0.17f, 0f), FS(0.2f, 0.12f, 0.2f));
                trl.jackDrop = deckY - 0.2f - 0.23f + 0.02f;
                trl.tiltDeck = deck;
                float rearDist = pivotX + L * 0.5f, under = deckY - 0.06f;
                // empty it rides ~0.1 m above its loaded design height: tilt far enough that the back edge still meets the ground
                trl.tiltMax = Mathf.Asin(Mathf.Clamp01((under + 0.1f) / rearDist)) * Mathf.Rad2Deg;
            }
            g.Attach(t, id + "_Body");

            rb.centerOfMass = new Vector3(0f, deckY, axleC + 0.2f);
            trl.displayName = goose ? "10 t gooseneck equipment trailer" : "7 t tilt-deck equipment trailer";
            trl.wheels = wheels.ToArray(); trl.wheelVisuals = vis.ToArray();
            trl.parkBrake = perWheel * 3f;
            trl.payloadTons = payload / 1000f;
            trl.jack = jack;
            trl.deckArea = deckArea; trl.deckSize = new Vector2(W, L);
            trl.carriesOnly = new[] { "Stand-On", "Wheeled Skid Steer", "Compact Track Loader", "Compact Excavator" };
            return root;
        }

        // ------------------------------------------------------------------ 3,500 L field fuel bowser trailer

        static GameObject BuildBowser(FleetMats fm, TransportMats tm)
        {
            wheelProto.Clear();
            const string id = "FuelBowser";
            var white = fm.Paint(Hex(0xf8fafc));
            var navy = fm.Paint(Hex(0x1e293b));
            const float rT = 0.38f;
            var root = new GameObject("Field Fuel Bowser 3,500 L");
            var t = root.transform;
            var g = new Geo();

            g.Box(navy, F(0f, rT + 0.18f, 0f), FS(3.45f, 0.16f, 1.72f));
            foreach (int s in new[] { -1, 1 })
            {
                g.Box(fm.dark, F(0f, rT * 2f + 0.08f, s * 0.95f), FS(1.5f, 0.04f, 0.34f)); // mudguards
                float zb = s * 0.78f, ang = Mathf.Atan2(zb, 1.48f);
                g.Box(navy, F(1.72f + 0.74f, rT + 0.18f, zb * 0.5f), FS(1.68f, 0.12f, 0.10f), RY(ang));
            }
            g.Box(fm.dark, F(3.08f, rT + 0.18f, 0f), FS(0.3f, 0.06f, 0.14f));
            g.Cyl(fm.pin, F(3.22f, rT + 0.18f, 0f), Quaternion.identity, 0.1f, 0.1f, 0.045f, 14);
            g.Cyl(fm.dark, F(3.22f, rT + 0.18f, 0f), Quaternion.identity, 0.06f, 0.06f, 0.05f, 10);
            g.Box(navy, F(2.55f, rT + 0.35f, 0f), FS(0.10f, 0.55f, 0.10f));
            g.Cyl(fm.dark, F(2.55f, rT + 0.66f, 0.08f), AxisLat, 0.02f, 0.02f, 0.16f, 6); // jack crank
            // the tank: baffled cylinder on saddles, end caps, walkway, hatch, handrail
            g.Cyl(white, F(0.28f, rT + 0.96f, 0f), AxisFwd, 0.72f, 0.72f, 2.55f, 28);
            foreach (float e in new[] { -1.29f, 1.29f }) g.Cyl(fm.wear, F(0.28f + e, rT + 0.96f, 0f), AxisFwd, 0.66f, 0.66f, 0.04f, 28);
            foreach (float sx in new[] { -0.55f, 0.95f }) g.Box(navy, F(sx, rT + 0.38f, 0f), FS(0.16f, 0.26f, 1.3f));
            g.Box(fm.dark, F(0.28f, rT + 1.69f, 0f), FS(2.1f, 0.04f, 0.42f));
            g.Cyl(navy, F(0.55f, rT + 1.75f, 0f), Quaternion.identity, 0.22f, 0.22f, 0.12f, 16);
            foreach (int s in new[] { -1, 1 })
            {
                g.Box(fm.rail, F(0.28f, rT + 2.0f, s * 0.23f), FS(1.9f, 0.03f, 0.03f));
                foreach (float px in new[] { -0.6f, 0.28f, 1.16f }) g.Box(fm.rail, F(px, rT + 1.85f, s * 0.23f), FS(0.03f, 0.3f, 0.03f));
            }
            g.Box(fm.amber, F(0.28f, rT + 1.1f, 0.73f), FS(0.5f, 0.12f, 0.01f)); // "DIESEL" placard
            g.Box(fm.amber, F(0.28f, rT + 1.1f, -0.73f), FS(0.5f, 0.12f, 0.01f));
            // pump cabinet with hose reel, digital meter, door; stabilizer legs; tail lights
            g.Box(navy, F(-1.35f, rT + 0.88f, 0f), FS(0.72f, 1.28f, 1.56f));
            g.Box(white, F(-1.73f, rT + 0.88f, 0f), FS(0.05f, 1.2f, 1.52f));
            g.Box(fm.chrome, F(-1.765f, rT + 0.88f, 0.6f), FS(0.02f, 0.2f, 0.04f));
            g.Cyl(tm.tail, F(-1.52f, rT + 0.85f, 0.95f), AxisLat, 0.24f, 0.24f, 0.32f, 18); // reel on the side, outside the cabinet
            g.Box(tm.screen, F(-1.55f, rT + 1.05f, -0.8f), FS(0.24f, 0.22f, 0.02f));
            g.Cyl(tm.trim, F(-1.2f, rT + 0.9f, 0.86f), Quaternion.identity, 0.03f, 0.03f, 0.4f, 6); // nozzle holster
            foreach (int s in new[] { -1, 1 })
            {
                g.Box(fm.chrome, F(-1.62f, rT + 0.12f, s * 0.78f), FS(0.10f, 0.52f, 0.10f));
                g.Box(tm.tail, F(-1.77f, rT + 0.22f, s * 0.7f), FS(0.04f, 0.08f, 0.2f));
            }
            g.Attach(t, id + "_Body");

            var jack = Node(t, "Jack Foot", F(2.55f, rT + 0.05f, 0f));
            var gj = new Geo();
            gj.Box(fm.chrome, Vector3.zero, FS(0.08f, 0.38f, 0.08f));
            gj.Box(fm.dark, F(0f, -0.18f, 0f), FS(0.2f, 0.03f, 0.2f));
            gj.Attach(jack, id + "_Jack");
            ColBox(jack, "Jack Pad", F(0f, -0.1f, 0f), FS(0.2f, 0.18f, 0.2f));

            ColBox(t, "Deck", F(0f, rT + 0.18f, 0f), FS(3.45f, 0.16f, 1.72f));
            ColBox(t, "Tank", F(0.28f, rT + 0.96f, 0f), FS(2.55f, 1.4f, 1.4f));
            ColBox(t, "Cabinet", F(-1.35f, rT + 0.88f, 0f), FS(0.72f, 1.28f, 1.56f));
            ColBox(t, "Tongue", F(2.4f, rT + 0.18f, 0f), FS(1.6f, 0.12f, 0.5f));

            var wheels = new List<WheelCollider>(); var vis = new List<Transform>();
            float perWheel = (1420f + 3500f * 0.84f) * 0.9f / 4f;
            foreach (float x in new[] { -0.45f, 0.45f })
                foreach (int s in new[] { -1, 1 })
                {
                    var wgo = new GameObject("Wheel " + wheels.Count);
                    wgo.transform.SetParent(t, false);
                    wgo.transform.localPosition = F(x, rT + 0.1f, s * 0.95f);
                    var wc = wgo.AddComponent<WheelCollider>();
                    // leaf-sprung tandem: soft and long-travel so each axle follows the ground (the deck flexes over humps).
                    // Loaded it sags ~0.17 m past a target near full droop: rests mid-travel with the wheel at its design height
                    wc.radius = rT; wc.mass = 35f; wc.suspensionDistance = 0.4f; wc.forceAppPointDistance = 0.15f;
                    wgo.transform.localPosition = F(x, rT + 0.19f, s * 0.95f);
                    float spring = perWheel * 9.81f * 6f;
                    wc.suspensionSpring = new JointSpring { spring = spring, damper = spring * 0.16f, targetPosition = 0.1f };
                    var sf = wc.sidewaysFriction; sf.stiffness = 1.5f; wc.sidewaysFriction = sf;
                    wheels.Add(wc);
                    var v = Node(t, "Wheel Visual " + vis.Count, F(x, rT, s * 0.95f));
                    TruckWheel(v, id + "_Wheel", rT, 0.24f, new[] { 0f }, fm, white);
                    vis.Add(v);
                }

            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 1420f; rb.interpolation = RigidbodyInterpolation.Interpolate; rb.angularDamping = 0.5f;
            rb.centerOfMass = new Vector3(0f, rT + 0.8f, 0.2f);
            var trl = root.AddComponent<Trailer>();
            trl.displayName = "Field fuel bowser (3,500 L)";
            trl.coupler = HitchType.Pintle;
            trl.couplerPoint = Marker(t, "Lunette Ring", F(3.22f, rT + 0.18f, 0f));
            trl.wheels = wheels.ToArray(); trl.wheelVisuals = vis.ToArray();
            trl.parkBrake = perWheel * 3f;
            trl.jack = jack; trl.jackDrop = 0.27f;
            var tankNode = Node(t, "Bowser tank", F(0.28f, rT + 0.96f, 0f));
            var tank = tankNode.gameObject.AddComponent<FuelTank>();
            tank.label = "Fuel bowser"; tank.capacity = 3500f; tank.liters = 1200f; tank.hoseReach = 15f;
            return root;
        }
    }
}
