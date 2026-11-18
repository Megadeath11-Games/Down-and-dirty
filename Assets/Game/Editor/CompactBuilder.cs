using System.Collections.Generic;
using FPSBase.Vehicles;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FPSBase.EditorTools
{
    /// <summary>
    /// Skid steers (1 t stand-on mini track loader, 262D3-class wheeled skid steer, 299D3-class compact track
    /// loader) and the 140M-class articulated motor grader, from the fleet visualizer v2. Same coordinate kit as
    /// the heavy fleet: source X forward, Y up, Z right, mapped by F().
    /// </summary>
    public static partial class ProjectBootstrap
    {
        class SkidCfg
        {
            public string id, name; public bool tracked, standOn, blackBucket;
            public float wheelbase, gauge, tireR, tireW, trackLength, trackHeight, sprocketElev, shoe;
            public float chL, chW, chH, cabL, cabW, cabH, armL, armD, bW, bR;
            public float tonnes, capacity, fuel, burn, drive, turn;
        }

        static readonly SkidCfg[] SkidFleet =
        {
            new SkidCfg { id = "SkidStand", name = "Stand-On Mini Track Loader 1 t", tracked = true, standOn = true, blackBucket = true,
                trackLength = 1.18f, gauge = 0.78f, trackHeight = 0.5f, sprocketElev = 0.44f, shoe = 0.18f,
                chL = 1.48f, chW = 0.62f, chH = 0.76f, armL = 1.24f, armD = 0.11f, bW = 1.05f, bR = 0.24f,
                tonnes = 1.04f, capacity = 0.16f, fuel = 30f, burn = 3.5f, drive = 1.6f, turn = 60f },
            new SkidCfg { id = "SkidWheel", name = "Wheeled Skid Steer 3.6 t", blackBucket = true,
                wheelbase = 1.06f, gauge = 1.46f, tireR = 0.44f, tireW = 0.32f,
                chL = 2.26f, chW = 1.24f, chH = 1.04f, cabL = 1.28f, cabW = 1.06f, cabH = 1.24f, armL = 2.34f, armD = 0.2f, bW = 1.88f, bR = 0.38f,
                tonnes = 3.63f, capacity = 0.48f, fuel = 95f, burn = 9f, drive = 3.4f, turn = 75f },
            new SkidCfg { id = "SkidTrack", name = "Compact Track Loader 5.2 t", tracked = true, blackBucket = true,
                trackLength = 2.28f, gauge = 1.56f, trackHeight = 0.72f, sprocketElev = 0.62f, shoe = 0.45f,
                chL = 2.46f, chW = 1.3f, chH = 1.06f, cabL = 1.36f, cabW = 1.12f, cabH = 1.24f, armL = 2.54f, armD = 0.22f, bW = 2.14f, bR = 0.42f,
                tonnes = 5.2f, capacity = 0.78f, fuel = 105f, burn = 12f, drive = 2.9f, turn = 60f },
        };

        /// <summary>A tyre with rim, hub and tread lugs, axle across the machine, centred on c.</summary>
        static void GeoWheel(Geo g, Vector3 c, float r, float w, Material tire, Material rim, Material hub, int lugs)
        {
            g.Cyl(tire, c, AxisLat, r, r, w, 18);
            g.Cyl(rim, c, AxisLat, r * 0.54f, r * 0.54f, w * 1.03f, 12);
            g.Cyl(hub, c, AxisLat, r * 0.25f, r * 0.25f, w * 1.12f, 8);
            for (int i = 0; i < lugs; i++)
            {
                float a = i / (float)lugs * 360f;
                float rad = a * Mathf.Deg2Rad;
                Vector3 radial = new Vector3(0f, Mathf.Sin(rad), Mathf.Cos(rad));
                g.Box(tire, c + radial * (r + 0.01f), new Vector3(w * 0.96f, r * 0.08f, r * 0.13f), Quaternion.Euler(90f - a, 0f, 0f));
            }
        }

        /// <summary>A box beam from a to b (Unity coords) with a square section.</summary>
        static void Beam(Geo g, Material m, Vector3 a, Vector3 b, float t)
        {
            Vector3 d = b - a;
            if (d.sqrMagnitude < 1e-6f) return;
            g.Box(m, (a + b) * 0.5f, new Vector3(t, t, d.magnitude), Quaternion.LookRotation(d, Mathf.Abs(d.normalized.y) > 0.95f ? Vector3.forward : Vector3.up));
        }

        static List<Vector2> DropLast(List<Vector2> l) { l.RemoveAt(l.Count - 1); return l; }

        // ------------------------------------------------------------------ skid steers

        static GameObject BuildSkidSteer(SkidCfg c, FleetMats fm)
        {
            var paint = fm.Paint(Hex(0xf59e0b));
            var accent = fm.Paint(Hex(0x111827));
            string id = "Fleet_" + c.id;
            var root = new GameObject(c.name);
            var t = root.transform;
            var rams = new List<Ram>();
            var g = new Geo();
            float TH = c.trackHeight, H = c.chH, L = c.chL, W = c.chW, D = c.armD;
            float belly = c.standOn ? TH * 0.48f : c.tracked ? TH * 0.46f : c.tireR * 0.72f;
            var wheels = new List<Transform>();

            // --- A. tracks (triangular rubber, drive sprocket up near the middle) or four lugged tyres
            if (c.tracked)
            {
                float SW = c.shoe, rBot = TH * 0.25f, rTop = TH * 0.27f, half = c.trackLength * 0.5f - rBot;
                float spX = -c.trackLength * 0.06f, spY = c.sprocketElev, rIn = rBot - 0.042f;
                int rollers = c.standOn ? 3 : 4;
                const float lift = 0.03f; // belt thickness under the idlers and rollers
                foreach (int s in new[] { -1, 1 })
                {
                    float zs = s * c.gauge * 0.5f;
                    g.Box(fm.dark, F(0.05f, rBot + lift, zs), FS(half * 2f, rBot * 0.95f, SW * 0.56f));
                    foreach (float ix in new[] { -half, half })
                    {
                        g.Cyl(fm.dark, F(0.05f + ix, rBot + lift, zs), AxisLat, rIn * 0.94f, rIn * 0.94f, SW * 0.64f, 14);
                        g.Cyl(fm.pin, F(0.05f + ix, rBot + lift, zs), AxisLat, rIn * 0.34f, rIn * 0.34f, SW * 0.72f, 8);
                    }
                    var belt = new List<Vector3> { new(0.05f + half, rBot + lift, rBot), new(0.05f - half, rBot + lift, rBot), new(0.05f + spX, spY, rTop) };
                    float rr = rIn * 0.58f;
                    for (int r = 0; r < rollers; r++)
                    {
                        float rx = 0.05f - half * 0.62f + r / (float)(rollers - 1) * half * 1.24f;
                        g.Cyl(fm.pin, F(rx, rr + lift, zs), AxisLat, rr, rr, SW * 0.68f, 12);
                        belt.Add(new Vector3(rx, rr + lift, rr));
                    }
                    g.Cyl(fm.dark, F(0.05f + spX, spY, zs), AxisLat, rTop * 0.88f, rTop * 0.88f, SW * 0.66f, 16);
                    for (int k = 0; k < 10; k++)
                    {
                        float a = k / 10f * Mathf.PI * 2f;
                        g.Box(fm.dark, F(0.05f + spX + Mathf.Cos(a) * rTop * 0.9f, spY + Mathf.Sin(a) * rTop * 0.9f, zs), FS(rTop * 0.2f, rTop * 0.2f, SW * 0.5f), RZ(a));
                    }
                    g.Cyl(paint, F(0.05f + spX, spY, zs), AxisLat, rTop * 0.42f, rTop * 0.42f, SW * 0.76f, 10);
                    // rubber belt: lugs run round the idlers, rollers and sprocket as the machine moves
                    TrackAnimator.AddBelt(root, fm.shoe, zs, SW, Mathf.Max(0.032f, TH * 0.055f), Mathf.Max(0.05f, c.trackLength / 18f), belt.ToArray());
                    if (!c.standOn)
                        foreach (float tx in new[] { -0.45f, 0.45f })
                            g.Cyl(fm.dark, F(tx, belly * 0.85f, s * c.gauge * 0.22f), AxisLat, 0.09f, 0.09f, c.gauge * 0.42f, 12); // torsion axle tubes
                }
            }
            else
            {
                float hwb = c.wheelbase * 0.5f, ht = c.gauge * 0.5f;
                int k = 0;
                foreach (float wx in new[] { hwb, -hwb })
                    foreach (float wz in new[] { -ht, ht })
                    {
                        var wn = Node(t, "Wheel " + k, F(wx, c.tireR, wz));
                        var gw = new Geo();
                        GeoWheel(gw, Vector3.zero, c.tireR, c.tireW, fm.shoe, paint, accent, 12);
                        gw.Attach(wn, id + "_Wheel" + k++);
                        wheels.Add(wn);
                    }
            }

            // --- B. chassis, cab / operator station, loader towers
            float armSpanZ, towerX, towerTopY, elbowX, elbowY, tipX, tipY;
            Vector2 liftBase, liftRod;
            Transform seat;
            if (!c.standOn)
            {
                // belly pan, the sloped radiator bonnet behind the cab, rear door and bumper
                g.Box(paint, F(0f, belly + H * 0.21f, 0f), FS(L * 0.94f, H * 0.42f, W));
                var bon = new List<Vector2> { new(0f, 0f), new(L * 0.46f, 0f), new(L * 0.46f, H * 0.62f), new(L * 0.10f, H * 0.34f), new(0f, H * 0.34f) };
                g.Prism(paint, bon, (p, z) => F(-L * 0.56f + p.x, belly + H * 0.40f + p.y, z), -W * 0.49f, W * 0.49f);
                for (int i = 0; i < 4; i++) // radiator louvres on the bonnet slope
                    g.Box(accent, F(-L * (0.40f - i * 0.055f), belly + H * (0.79f + i * 0.045f), 0f), FS(0.035f, 0.012f, W * 0.7f), RZ(0.66f));
                g.Box(accent, F(-L * 0.14f, belly + H * 0.63f, 0f), FS(0.06f, H * 0.46f, c.cabW * 0.92f));
                g.Box(accent, F(-L * 0.57f, belly + H * 0.56f, 0f), FS(0.06f, H * 0.68f, W * 0.88f), RZ(-0.14f));
                g.Box(accent, F(-L * 0.50f, belly + 0.16f, 0f), FS(0.18f, 0.24f, W * 1.04f));
                foreach (int s in new[] { -1, 1 }) g.Box(fm.amber, F(-L * 0.585f, belly + H * 0.82f, s * W * 0.38f), FS(0.03f, 0.07f, 0.1f), RZ(-0.14f)); // tail lights
                g.Cyl(fm.dark, F(-L * 0.34f, belly + H * 1.0f, W * 0.3f), Quaternion.identity, 0.03f, 0.028f, H * 0.34f, 8);

                // FOPS cab: raked front posts and windshield, side sills, headers and wire-mesh screens
                float cL = c.cabL, cH = c.cabH, cW = c.cabW;
                Vector3 cab = F(L * 0.12f, belly + H * 0.42f, 0f);
                g.Box(accent, cab, FS(cL, 0.08f, cW));
                foreach (var (px, pz, tilt) in new[] { (-cL * 0.44f, -cW * 0.46f, -0.06f), (-cL * 0.44f, cW * 0.46f, -0.06f), (cL * 0.40f, -cW * 0.46f, 0.14f), (cL * 0.40f, cW * 0.46f, 0.14f) })
                    g.Box(accent, cab + F(px, cH * 0.5f, pz), FS(0.065f, cH, 0.065f), RZ(tilt));
                g.Box(accent, cab + F(-0.04f, cH, 0f), FS(cL * 0.94f, 0.08f, cW * 1.02f));
                g.Box(fm.glass, cab + F(cL * 0.40f, cH * 0.5f, 0f), FS(0.014f, cH * 0.92f, cW * 0.88f), RZ(0.14f));
                g.Box(fm.glass, cab + F(-cL * 0.45f, cH * 0.62f, 0f), FS(0.014f, cH * 0.6f, cW * 0.86f)); // rear window
                foreach (int s in new[] { -1, 1 })
                {
                    float sz = s * cW * 0.47f;
                    g.Box(accent, cab + F(-0.02f, cH * 0.15f, sz), FS(cL * 0.86f, cH * 0.30f, 0.055f));
                    g.Box(accent, cab + F(-0.03f, cH * 0.92f, sz), FS(cL * 0.88f, cH * 0.12f, 0.055f));
                    for (int b = 0; b < 4; b++) g.Box(accent, cab + F(-0.02f, cH * (0.38f + b * 0.12f), sz), FS(cL * 0.82f, 0.012f, 0.012f));
                    for (int b = 0; b < 7; b++) g.Box(accent, cab + F(-cL * 0.38f + b * cL * 0.126f, cH * 0.58f, sz), FS(0.012f, cH * 0.58f, 0.012f));
                    g.Box(accent, cab + F(cL * 0.42f, cH * 0.95f, s * cW * 0.36f), FS(0.08f, 0.06f, 0.11f));
                    g.Box(fm.amber, cab + F(cL * 0.46f, cH * 0.95f, s * cW * 0.36f), FS(0.02f, 0.04f, 0.08f));
                }
                g.Cyl(fm.amber, cab + F(-cL * 0.3f, cH + 0.08f, -cW * 0.32f), Quaternion.identity, 0.05f, 0.045f, 0.09f, 8);
                g.Box(fm.dark, cab + F(cL * 0.56f, 0.02f, 0f), FS(0.24f, 0.04f, cW * 0.72f));
                seat = Node(t, "CabView", cab + F(0.1f, Mathf.Min(cH * 0.86f, 1.08f), 0f));
                CabInterior(seat, CabStyle.SkidSteer, CabMatsFrom(fm, accent), id);

                // rear loader towers (vertical-lift path), arms reach well past the front wheels / tracks
                towerX = -L * 0.40f; towerTopY = belly + H * 1.18f; armSpanZ = W * 0.61f;
                foreach (int s in new[] { -1, 1 })
                {
                    g.Box(paint, F(towerX, towerTopY - H * 0.42f, s * armSpanZ), FS(0.34f, H * 0.88f, 0.14f));
                    g.Box(accent, F(towerX + 0.12f, towerTopY - H * 0.62f, s * armSpanZ), FS(0.1f, H * 0.42f, 0.15f));
                }
                elbowX = c.armL * 0.72f; elbowY = -H * (c.tracked ? 0.20f : 0.22f);
                tipX = c.armL; tipY = -H * (c.tracked ? 1.14f : 1.18f);
                liftBase = new Vector2(towerX + 0.22f, belly + H * 0.38f);
                liftRod = new Vector2(elbowX * 0.68f, elbowY * 0.68f - 0.06f);
            }
            else
            {
                // stand-on: compact tub, sloped black hood, rear control console, hip pad and the standing platform
                g.Box(paint, F(0.04f, belly + H * 0.22f, 0f), FS(L * 0.86f, H * 0.44f, W));
                var hood = new List<Vector2> { new(0f, 0f), new(L * 0.64f, 0f), new(L * 0.64f, H * 0.44f), new(L * 0.12f, H * 0.62f), new(0f, H * 0.62f) };
                g.Prism(accent, hood, (p, z) => F(-L * 0.14f + p.x, belly + H * 0.42f + p.y, z), -W * 0.47f, W * 0.47f);
                for (int i = 0; i < 4; i++)
                    g.Box(fm.pin, F(L * (0.06f + i * 0.08f), belly + H * (0.99f - i * 0.04f), 0f), FS(0.03f, 0.01f, W * 0.66f), RZ(-0.33f));
                float rearX = -L * 0.42f;
                g.Box(fm.dark, F(rearX - 0.18f, belly * 0.85f, 0f), FS(0.42f, 0.06f, W * 0.96f));
                g.Box(paint, F(rearX + 0.04f, belly + H * 0.56f, 0f), FS(0.32f, H * 0.72f, W * 0.96f));
                g.Box(accent, F(rearX - 0.14f, belly + H * 0.54f, 0f), FS(0.10f, 0.24f, W * 0.84f));
                foreach (int s in new[] { -1, 1 })
                    g.Cyl(accent, F(rearX - 0.36f, belly * 0.85f + H * 0.42f, s * W * 0.44f), Quaternion.identity, 0.015f, 0.015f, H * 0.85f, 6);
                g.Box(accent, F(rearX - 0.36f, belly * 0.85f + H * 0.85f, 0f), FS(0.03f, 0.03f, W * 0.9f));
                g.Box(fm.amber, F(rearX + 0.1f, belly + H * 0.92f + 0.03f, W * 0.3f), FS(0.06f, 0.055f, 0.1f));
                seat = Node(t, "CabView", F(rearX - 0.22f, belly * 0.85f + 1.55f, 0f));
                CabInterior(seat, CabStyle.StandOn, CabMatsFrom(fm, accent), id);

                towerX = -L * 0.22f; towerTopY = belly + H * 0.96f; armSpanZ = W * 0.62f;
                foreach (int s in new[] { -1, 1 })
                    g.Box(paint, F(towerX, towerTopY - H * 0.30f, s * armSpanZ), FS(0.24f, H * 0.64f, 0.08f));
                elbowX = c.armL * 0.72f; elbowY = -H * 0.20f;
                tipX = c.armL; tipY = -H * 0.92f;
                liftBase = new Vector2(towerX + 0.14f, belly + H * 0.34f);
                liftRod = new Vector2(elbowX * 0.72f, elbowY * 0.72f - 0.04f);
            }

            // --- C. lift arms (two-piece: up and over, then down to the coupler), cross tube, lift rams
            var armPivot = Node(t, "ArmPivot", F(towerX, towerTopY, 0f));
            var gA = new Geo();
            float upLen = Mathf.Sqrt(elbowX * elbowX + elbowY * elbowY), upAng = Mathf.Atan2(elbowY, elbowX);
            float loDx = tipX - elbowX, loDy = tipY - elbowY, loLen = Mathf.Sqrt(loDx * loDx + loDy * loDy), loAng = Mathf.Atan2(loDy, loDx);
            float armT = c.standOn ? 0.065f : 0.11f;
            foreach (int s in new[] { -1, 1 })
            {
                float z = s * armSpanZ;
                gA.Box(paint, F(elbowX * 0.5f, elbowY * 0.5f, z), FS(upLen + D * 0.4f, D, armT), RZ(upAng));
                gA.Box(paint, F(elbowX + loDx * 0.5f, elbowY + loDy * 0.5f, z), FS(loLen + D * 0.3f, D * 0.92f, armT), RZ(loAng));
                gA.Box(paint, F(elbowX, elbowY, z), FS(D * 1.3f, D * 1.2f, armT * 1.05f), RZ((upAng + loAng) * 0.5f)); // elbow knuckle
                if (!c.standOn) gA.Box(accent, F(elbowX * 0.55f, elbowY * 0.55f + D * 0.18f, z), FS(upLen * 0.5f, D * 0.3f, armT * 1.06f), RZ(upAng));
                Pin(gA, fm, F(0f, 0f, z), D * 0.24f, armT * 1.6f);
                Pin(gA, fm, F(tipX, tipY, z), D * 0.22f, armT * 1.5f);
                Pin(g, fm, F(liftBase.x, liftBase.y, z), D * 0.2f, armT * 1.4f);
                Pin(gA, fm, F(liftRod.x, liftRod.y, z), D * 0.2f, armT * 1.4f);
                float lr = c.standOn ? 0.036f : 0.052f;
                rams.Add(new Ram { name = "Lift Ram " + (s < 0 ? "L" : "R"), parent = t, barrelR = lr, rodR = lr * 0.62f,
                    a = Node(t, "LiftRamBase" + s, F(liftBase.x, liftBase.y, z)), b = Node(armPivot, "LiftRamRod" + s, F(liftRod.x, liftRod.y, z)) });
            }
            float tubeR = c.standOn ? 0.055f : 0.085f;
            gA.Cyl(paint, F(elbowX * 0.96f, elbowY * 0.96f, 0f), AxisLat, tubeR, tubeR, armSpanZ * 2.04f, 16);
            ToolBox(armPivot, "Arms", F(elbowX * 0.5f, elbowY * 0.5f, 0f), FS(upLen, D, armSpanZ * 2f + armT), RZ(upAng));

            // --- D. quick-attach bucket: pivots low on its back, deep floor, side cutters
            var bucketPivot = Node(armPivot, "BucketPivot", F(tipX, tipY, 0f));
            var gK = new Geo();
            float bR = c.bR, bW = c.bW;
            var body = c.blackBucket ? fm.wear : paint;
            // profile (in bucket radii, except the back-plate x offsets in metres)
            float bTop = c.standOn ? 1.15f : 1.25f, bOut = c.standOn ? 0.04f : 0.05f, bIn = c.standOn ? 0.14f : 0.18f;
            float floorOut = c.standOn ? -0.78f : -0.76f, floorIn = c.standOn ? -0.68f : -0.64f, len = c.standOn ? 2.15f : 2.25f;
            var shell = new List<Vector2> { new(bOut, bTop * bR), new(bIn, bTop * bR), new(bIn, (c.standOn ? -0.42f : -0.40f) * bR) };
            Quadratic(shell, new Vector2(bIn, floorIn * bR), new Vector2((c.standOn ? 0.48f : 0.52f) * bR, floorIn * bR));
            shell.Add(new Vector2(len * bR, floorIn * bR));
            shell.Add(new Vector2(len * bR, floorOut * bR));
            shell.Add(new Vector2((c.standOn ? 0.45f : 0.48f) * bR, floorOut * bR));
            Quadratic(shell, new Vector2(bOut + 0.02f, floorOut * bR), new Vector2(bOut, -0.45f * bR));
            gK.Prism(body, shell, (p, z) => F(p.x, p.y, z), -bW * 0.5f, bW * 0.5f);
            float cheekT = c.standOn ? 0.022f : 0.026f;
            var cheek = new List<Vector2> { new(bOut + 0.01f, bTop * bR), new(bIn, bTop * bR),
                new((c.standOn ? 0.36f : 0.42f) * bR, (c.standOn ? 0.85f : 0.92f) * bR), new((c.standOn ? 1.85f : 1.92f) * bR, (c.standOn ? -0.42f : -0.36f) * bR),
                new(len * bR, (floorOut + 0.02f) * bR), new((c.standOn ? 0.45f : 0.48f) * bR, (floorOut + 0.02f) * bR) };
            Quadratic(cheek, new Vector2(bOut + 0.04f, (floorOut + 0.02f) * bR), new Vector2(bOut + 0.01f, -0.45f * bR));
            foreach (float zs in new[] { -bW * 0.5f, bW * 0.5f - cheekT })
                gK.Prism(body, cheek, (p, z) => F(p.x, p.y, z), zs, zs + cheekT);
            gK.Box(fm.dark, F(c.standOn ? 0.03f : 0.04f, bR * (c.standOn ? 0.25f : 0.28f), 0f), FS(c.standOn ? 0.05f : 0.07f, bR * (c.standOn ? 1.4f : 1.45f), bW * (c.standOn ? 0.78f : 0.72f)));
            gK.Box(body, F(bR * 0.12f + 0.05f, bTop * bR, 0f), FS(bR * 0.26f, bR * 0.1f, bW * 0.98f)); // spill guard lip
            gK.Box(fm.wear, F(bR * (len + 0.08f), (floorOut + 0.04f) * bR, 0f), FS(c.standOn ? 0.12f : 0.14f, c.standOn ? 0.022f : 0.028f, bW + 0.03f));
            gK.Attach(bucketPivot, id + "_Bucket");
            ToolBox(bucketPivot, "Bucket", F(bR * len * 0.5f, (bTop + floorOut) * 0.5f * bR, 0f), FS(bR * len, (bTop - floorOut) * bR * 0.95f, bW * 0.95f), Quaternion.identity);
            var tip = Node(bucketPivot, "BucketTip", F(bR * (len + 0.12f), floorOut * bR + 0.01f, 0f));
            var mound = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(mound.GetComponent<Collider>());
            mound.name = "Bucket Load (dirt)";
            mound.transform.SetParent(bucketPivot, false);
            mound.transform.localPosition = F(bR * len * 0.5f, floorIn * bR + bR * 0.42f, 0f);
            mound.transform.localScale = FS(bR * len * 0.78f, bR * 0.85f, bW * 0.88f);
            mound.GetComponent<MeshRenderer>().sharedMaterial = fm.dirt;
            mound.SetActive(false);
            float floorDrop = -floorOut * bR;

            if (!c.standOn)
            {
                // twin tilt rams from the arm crossmember down to the coupler
                foreach (int s in new[] { -1, 1 })
                {
                    float z = s * armSpanZ * 0.78f;
                    Vector2 tb = new Vector2(elbowX * 0.94f, elbowY * 0.94f + 0.12f);
                    Pin(gA, fm, F(tb.x, tb.y, z), D * 0.16f, 0.12f);
                    gA.Box(paint, F(tb.x - 0.02f, tb.y - 0.06f, z), FS(0.14f, 0.16f, 0.1f));
                    rams.Add(new Ram { name = "Tilt Ram " + (s < 0 ? "L" : "R"), parent = t, barrelR = 0.044f, rodR = 0.026f,
                        a = Node(armPivot, "TiltRamBase" + s, F(tb.x, tb.y, z)), b = Node(bucketPivot, "TiltRamRod" + s, F(0.06f, bR * 1.05f, z)) });
                }
            }
            else
            {
                Vector2 tb = new Vector2(elbowX * 0.94f, elbowY * 0.94f + 0.08f);
                gA.Box(paint, F(tb.x, tb.y - 0.04f, 0f), FS(0.1f, 0.12f, 0.12f));
                Pin(gA, fm, F(tb.x, tb.y, 0f), D * 0.2f, 0.14f);
                rams.Add(new Ram { name = "Tilt Ram", parent = t, barrelR = 0.032f, rodR = 0.019f,
                    a = Node(armPivot, "TiltRamBase", F(tb.x, tb.y, 0f)), b = Node(bucketPivot, "TiltRamRod", F(0.04f, bR * 0.95f, 0f)) });
            }
            gA.Attach(armPivot, id + "_Arms");
            g.Attach(t, id + "_Body");

            // --- machine script (the loader logic, with skid steering)
            var ld = root.AddComponent<FrontLoader>();
            ld.displayName = c.name;
            ld.fuelCapacity = c.fuel; ld.fuel = c.fuel * 0.8f; ld.idleBurn = c.burn * 0.2f; ld.workBurn = c.burn;
            ld.driveSpeed = c.drive; ld.reverseSpeed = c.drive * 0.85f; ld.turnSpeed = c.turn; ld.turnInPlace = true; ld.skidSteer = true;
            ld.acceleration = 3f;
            ld.armPivot = armPivot; ld.bucketPivot = bucketPivot; ld.bucketTip = tip; ld.bucketLoadVisual = mound.transform;
            ld.bucketMouth = F(0.35f, 0.94f, 0f).normalized;
            ld.wheelVisuals = c.tracked ? null : wheels.ToArray(); ld.wheelRadius = Mathf.Max(0.1f, c.tireR);
            ld.bucketCapacity = c.capacity; ld.fillRate = c.capacity * 0.9f; ld.curlFillRate = c.capacity * 0.45f; ld.dumpRate = c.capacity * 1.3f;
            ld.armSpeed = 30f; ld.tiltSpeed = 60f; ld.dumpAngle = 20f; ld.tiltRange = new Vector2(-40f, 50f);
            // lowest arm angle: the cutting edge just below the ground
            float r0 = Mathf.Sqrt(tipX * tipX + tipY * tipY), phi0 = Mathf.Atan2(tipY, tipX);
            // the coupler pin rides a bucket-floor height above the ground: the cutting edge can bite ~10 cm below grade
            float low = (Mathf.Asin(Mathf.Clamp((floorDrop - 0.10f - towerTopY) / r0, -1f, 1f)) - phi0) * Mathf.Rad2Deg;
            ld.armRange = new Vector2(Mathf.Max(-65f, low), 50f);

            for (int i = 0; i <= 16; i++)
            {
                float u = i / 16f;
                float arm = Mathf.Lerp(ld.armRange.x, ld.armRange.y, u);
                foreach (float tilt in new[] { ld.tiltRange.x, 0f, ld.tiltRange.y })
                {
                    armPivot.localRotation = Quaternion.Euler(-arm, 0f, 0f);
                    bucketPivot.localRotation = Quaternion.Euler(arm + tilt, 0f, 0f);
                    foreach (var r in rams) r.Measure();
                }
            }
            armPivot.localRotation = Quaternion.Euler(-ld.armRange.x, 0f, 0f);
            bucketPivot.localRotation = Quaternion.Euler(ld.armRange.x, 0f, 0f);
            foreach (var r in rams) BuildRam(r, fm, paint, id);

            var rb = root.GetComponent<Rigidbody>(); if (rb == null) rb = root.AddComponent<Rigidbody>(); // the machine script already required one
            rb.mass = c.tonnes * 1000f; rb.angularDamping = 3f; rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var col = root.AddComponent<BoxCollider>();
            float width = c.tracked ? c.gauge + c.shoe : c.gauge + c.tireW;
            float top = c.standOn ? belly + c.chH * 1.2f : belly + c.chH * 0.42f + c.cabH + 0.08f;
            col.center = new Vector3(0f, top * 0.5f, -c.chL * 0.05f);
            col.size = new Vector3(width, top, c.tracked ? Mathf.Max(c.trackLength, c.chL * 0.9f) : c.chL * 0.95f);
            ld.centerOfMass = new Vector3(0f, belly + c.chH * 0.3f, -c.chL * 0.12f);

            ld.exitPoint = Marker(t, "ExitPoint", new Vector3(-(width * 0.5f + 0.9f), 0f, c.standOn ? -c.chL * 0.6f : 0f));
            ld.vehicleCamera = VehicleCam(t, t, seat, c.chL * 2.2f + 3f, c.chH + 1.6f);
            return root;
        }

        // ------------------------------------------------------------------ motor grader

        static GameObject BuildGrader(FleetMats fm)
        {
            var paint = fm.Paint(Hex(0xf59e0b));
            var accent = fm.Paint(Hex(0x111827));
            const string id = "Fleet_Grader140";
            const float gauge = 2.18f, tireR = 0.68f, tireW = 0.42f, tandemWB = 1.56f, RHL = 3.15f, RHW = 1.62f, RHH = 1.52f;
            const float FB = 4.35f, cL = 1.62f, cW = 1.38f, cH = 1.64f, bladeW = 4.27f, bH = 0.64f, rs = 0.85f;
            var root = new GameObject("Motor Grader 19.5 t");
            var t = root.transform;
            var rams = new List<Ram>();
            float eb = tireR * 1.15f;
            var spins = new List<Transform>();

            // --- A. rear frame: tandem drive, engine module, ripper
            var rear = Node(t, "RearFrame", F(-0.35f, 0f, 0f));
            var gR = new Geo();
            const float tcx = -1.65f;
            int wk = 0;
            foreach (int s in new[] { -1, 1 })
            {
                gR.Box(paint, F(tcx, tireR, s * (gauge * 0.5f - tireW * 0.62f)), FS(tandemWB + 0.45f, 0.34f, 0.28f));
                foreach (float wx in new[] { -tandemWB * 0.5f, tandemWB * 0.5f })
                {
                    var wn = Node(rear, "Wheel " + wk, F(tcx + wx, tireR, s * gauge * 0.5f));
                    var gw = new Geo();
                    GeoWheel(gw, Vector3.zero, tireR, tireW, fm.shoe, paint, accent, 18);
                    gw.Attach(wn, id + "_Wheel" + wk++);
                    spins.Add(wn);
                }
            }
            gR.Cyl(fm.dark, F(tcx, tireR, 0f), AxisLat, 0.18f, 0.18f, gauge * 0.85f, 10);
            gR.Box(paint, F(-RHL * 0.52f, eb + RHH * 0.5f, 0f), FS(RHL, RHH, RHW));
            gR.Box(accent, F(-RHL * 0.5f, eb + RHH * 1.1f, 0f), FS(RHL * 0.88f, RHH * 0.24f, RHW * 0.84f));
            foreach (int s in new[] { -1, 1 })
                gR.Box(accent, F(-RHL * 0.52f, eb + RHH * 0.55f, s * (RHW * 0.5f + 0.01f)), FS(RHL * 0.68f, RHH * 0.55f, 0.04f));
            gR.Cyl(fm.dark, F(-1.35f, eb + RHH + 0.45f, -0.36f), Quaternion.identity, 0.075f, 0.065f, 0.95f, 8);

            float rmX = -RHL * 1.02f, rmY = eb + 0.25f, rArm = 0.95f * rs;
            var ripPivot = Node(rear, "RipperPivot", F(rmX, rmY, 0f));
            var gRip = new Geo();
            foreach (float rz in new[] { -0.48f * rs, 0.48f * rs })
                gRip.Box(fm.dark, F(-rArm * 0.5f, 0f, rz), FS(rArm, 0.14f * rs, 0.12f * rs));
            gRip.Box(paint, F(-rArm, 0.02f * rs, 0f), FS(0.26f * rs, 0.28f * rs, 1.95f * rs));
            var ripTips = new List<Transform>();
            foreach (float sz in new[] { -0.76f, -0.38f, 0f, 0.38f, 0.76f })
            {
                gRip.Box(fm.wear, F(-rArm - 0.06f * rs, -0.28f * rs, sz * rs), FS(0.16f * rs, 0.95f * rs, 0.07f * rs), RZ(-0.2f));
                ripTips.Add(Node(ripPivot, "RipperTip", F(-rArm + 0.03f * rs, -0.74f * rs, sz * rs)));
            }
            gRip.Attach(ripPivot, id + "_Ripper");
            gR.Box(fm.dark, F(rmX + 0.05f, rmY + 0.3f * rs, 0f), FS(0.2f, 0.8f * rs, 1.0f));
            rams.Add(new Ram { name = "Ripper Ram", parent = t, barrelR = 0.08f * rs, rodR = 0.048f * rs,
                a = Node(rear, "RipRamBase", F(rmX, rmY + 0.65f * rs, 0f)), b = Node(ripPivot, "RipRamRod", F(-rArm * 0.85f, 0.14f * rs, 0f)) });
            gR.Attach(rear, id + "_RearFrame");

            // --- B. front frame: gooseneck, cab, front axle, drawbar / circle / moldboard
            var front = Node(t, "FrontFrame", F(-0.35f, 0f, 0f));
            var gF = new Geo();
            var spine = new List<Vector2>
            {
                new(0f, eb + 0.05f), new(1.25f, eb + 0.05f), new(2.45f, eb + 0.88f), new(FB + 0.22f, eb + 0.88f),
                new(FB + 0.22f, eb + 1.32f), new(2.15f, eb + 1.32f), new(1.05f, eb + 0.62f), new(0f, eb + 0.62f),
            };
            gF.Prism(paint, spine, (p, z) => F(p.x, p.y, z), -0.21f, 0.21f);
            gF.Cyl(fm.pin, F(0f, eb + 0.85f, 0f), Quaternion.identity, 0.08f, 0.08f, 0.45f, 8);
            gF.Cyl(fm.pin, F(0f, eb + 0.12f, 0f), Quaternion.identity, 0.08f, 0.08f, 0.45f, 8);
            foreach (int s in new[] { -1, 1 })
                rams.Add(new Ram { name = "Articulation Ram " + (s < 0 ? "L" : "R"), parent = t, barrelR = 0.075f, rodR = 0.045f,
                    a = Node(rear, "ArtRamBase" + s, F(-0.68f, eb + 0.45f, s * 0.58f)), b = Node(front, "ArtRamRod" + s, F(0.68f, eb + 0.45f, s * 0.42f)) });

            Vector3 cab = F(0.95f, eb + 0.62f, 0f);
            gF.Box(fm.dark, cab + F(0f, 0.06f, 0f), FS(cL, 0.12f, cW));
            gF.Box(fm.glass, cab + F(0.04f, cH * 0.5f, 0f), FS(cL * 0.94f, cH * 0.88f, cW * 0.92f));
            gF.Box(paint, cab + F(0.02f, cH * 0.98f, 0f), FS(cL * 1.08f, 0.12f, cW * 1.08f));
            foreach (float px in new[] { -cL * 0.44f, cL * 0.46f })
                foreach (float pz in new[] { -cW * 0.44f, cW * 0.44f })
                    gF.Cyl(accent, cab + F(px, cH * 0.5f, pz), Quaternion.identity, 0.032f, 0.032f, cH * 0.94f, 6);
            gF.Cyl(fm.amber, cab + F(-cL * 0.3f, cH * 1.08f, 0f), Quaternion.identity, 0.06f, 0.05f, 0.11f, 8);
            foreach (int s in new[] { -1, 1 })
                gF.Box(fm.dark, F(0.85f, eb - 0.15f, s * cW * 0.54f), FS(0.45f, 0.04f, 0.24f));
            var seat = Node(front, "CabView", cab + F(-0.12f, Mathf.Min(cH * 0.72f, 1.18f), 0f));
            CabInterior(seat, CabStyle.Grader, CabMatsFrom(fm, accent), id);

            // front axle: bolster, oscillating beam, lean bar, leaning + steering knuckles
            var axle = Node(front, "FrontAxle", F(FB, tireR, 0f));
            var gX = new Geo();
            gX.Box(paint, F(0f, 0.58f, 0f), FS(0.42f, 0.95f, 0.52f));
            gX.Box(paint, Vector3.zero, FS(0.28f, 0.24f, gauge * 0.84f));
            gX.Box(fm.dark, F(0f, 0.34f, 0f), FS(0.12f, 0.1f, gauge * 0.82f));
            gX.Attach(axle, id + "_FrontAxle");
            var steers = new List<Transform>(); var leans = new List<Transform>();
            foreach (int s in new[] { -1, 1 })
            {
                var lean = Node(axle, s < 0 ? "Lean L" : "Lean R", F(0f, 0f, s * gauge * 0.44f));
                var gk = new Geo();
                gk.Cyl(fm.dark, F(0f, 0.14f, 0f), Quaternion.identity, 0.09f, 0.09f, 0.44f, 8);
                gk.Attach(lean, id + (s < 0 ? "_KnuckleL" : "_KnuckleR"));
                var st = Node(lean, "Steer", Vector3.zero);
                var wn = Node(st, "Wheel", F(0f, 0f, s * gauge * 0.06f));
                var gw = new Geo();
                GeoWheel(gw, Vector3.zero, tireR, tireW, fm.shoe, paint, accent, 18);
                gw.Attach(wn, id + "_Wheel" + wk++);
                spins.Add(wn); steers.Add(st); leans.Add(lean);
            }

            // drawbar on its ball joint, the A-frame back to the circle
            var drawbar = Node(front, "Drawbar", F(FB - 0.38f, eb + 0.58f, 0f));
            var gD = new Geo();
            gD.Cyl(fm.dark, Vector3.zero, Quaternion.identity, 0.16f, 0.16f, 0.3f, 10);
            const float ccx = -1.95f, ccy = -0.52f;
            foreach (int s in new[] { -1, 1 })
                Beam(gD, paint, Vector3.zero, F(ccx, ccy, s * 0.72f), 0.16f);
            gD.Box(paint, F(ccx, ccy, 0f), FS(0.22f, 0.18f, 1.54f));
            gD.Attach(drawbar, id + "_Drawbar");
            gF.Box(paint, F(2.25f, eb + 1.15f, 0f), FS(0.36f, 0.32f, 1.48f)); // saddle
            foreach (int s in new[] { -1, 1 })
                rams.Add(new Ram { name = "Blade Lift Ram " + (s < 0 ? "L" : "R"), parent = t, barrelR = 0.075f, rodR = 0.045f,
                    a = Node(front, "LiftRamBase" + s, F(2.25f, eb + 1.15f, s * 0.68f)), b = Node(drawbar, "LiftRamRod" + s, F(ccx, ccy + 0.08f, s * 0.68f)) });

            var circle = Node(drawbar, "Circle", F(ccx, ccy - 0.14f, 0f));
            var gC = new Geo();
            gC.Cyl(fm.dark, Vector3.zero, Quaternion.identity, 0.76f, 0.76f, 0.16f, 24);
            foreach (int s in new[] { -1, 1 })
                gC.Box(paint, F(-0.1f, -0.16f, s * 0.6f), FS(0.18f, 0.3f, 0.16f));
            gC.Attach(circle, id + "_Circle");
            var shift = Node(circle, "Shift", F(0f, -0.26f, 0f));
            var pitch = Node(shift, "Pitch", Vector3.zero);
            var gM = new Geo();
            var mb = new List<Vector2> { new(0.14f, bH * 0.52f) };
            Quadratic(mb, new Vector2(-0.14f, bH * 0.14f), new Vector2(-0.06f, -bH * 0.34f));
            mb.Add(new Vector2(0.14f, -bH * 0.5f)); mb.Add(new Vector2(0.08f, -bH * 0.54f)); mb.Add(new Vector2(-0.15f, -bH * 0.35f));
            Quadratic(mb, new Vector2(-0.24f, bH * 0.16f), new Vector2(0.06f, bH * 0.54f));
            gM.Prism(paint, mb, (p, z) => F(p.x, p.y, z), -bladeW * 0.5f, bladeW * 0.5f);
            foreach (float ry in new[] { -bH * 0.24f, bH * 0.24f })
                gM.Box(fm.dark, F(-0.18f, ry, 0f), FS(0.1f, 0.08f, bladeW * 0.94f));
            gM.Box(fm.wear, F(0.11f, -bH * 0.48f, 0f), FS(0.05f, 0.14f, bladeW * 1.02f), RZ(0.68f));
            gM.Attach(pitch, id + "_Moldboard");
            ToolBox(pitch, "Moldboard", F(-0.02f, 0f, 0f), FS(0.3f, bH, bladeW), Quaternion.identity);
            var endL = Node(pitch, "BladeEnd L", F(0.13f, -bH * 0.53f, -bladeW * 0.5f));
            var endR = Node(pitch, "BladeEnd R", F(0.13f, -bH * 0.53f, bladeW * 0.5f));
            rams.Add(new Ram { name = "Sideshift Ram", parent = t, barrelR = 0.058f, rodR = 0.035f,
                a = Node(circle, "ShiftRamBase", F(-0.18f, -0.12f, -0.55f)), b = Node(shift, "ShiftRamRod", F(-0.14f, 0.12f, 0.65f)) });
            gF.Attach(front, id + "_FrontFrame");

            // --- machine script
            var gr = root.AddComponent<MotorGrader>();
            gr.displayName = "Motor Grader 19.5 t";
            gr.fuelCapacity = 416f; gr.fuel = 330f; gr.idleBurn = 5f; gr.workBurn = 26f;
            gr.driveSpeed = 6f; gr.reverseSpeed = 4f; gr.acceleration = 1.6f; gr.turnSpeed = 18f; gr.turnInPlace = false;
            gr.frontFrame = front; gr.rearFrame = rear;
            gr.steerPivots = steers.ToArray(); gr.leanPivots = leans.ToArray();
            gr.wheelSpins = spins.ToArray(); gr.wheelRadius = tireR;
            gr.drawbar = drawbar; gr.circle = circle; gr.shift = shift; gr.pitch = pitch; gr.bladeEndL = endL; gr.bladeEndR = endR;
            gr.ripperPivot = ripPivot; gr.ripperTips = ripTips.ToArray(); gr.ripperRange = new Vector2(-10f, 40f);
            gr.centerOfMass = new Vector3(0f, eb + 0.3f, -1.2f);

            // ram strokes over the full travel of every joint
            for (int i = 0; i <= 12; i++)
            {
                float u = i / 12f;
                drawbar.localRotation = Quaternion.Euler(Mathf.Lerp(gr.liftRange.x, gr.liftRange.y, u), 0f, Mathf.Lerp(-gr.maxRoll, gr.maxRoll, u));
                shift.localPosition = F(0f, -0.26f, 0f) + Vector3.right * Mathf.Lerp(-gr.maxShift, gr.maxShift, u);
                ripPivot.localRotation = Quaternion.Euler(-Mathf.Lerp(gr.ripperRange.x, gr.ripperRange.y, u), 0f, 0f);
                front.localRotation = Quaternion.Euler(0f, Mathf.Lerp(-gr.maxArtic, gr.maxArtic, u) * 0.5f, 0f);
                rear.localRotation = Quaternion.Euler(0f, -Mathf.Lerp(-gr.maxArtic, gr.maxArtic, u) * 0.5f, 0f);
                foreach (var r in rams) r.Measure();
            }
            drawbar.localRotation = Quaternion.identity; shift.localPosition = F(0f, -0.26f, 0f);
            ripPivot.localRotation = Quaternion.identity; front.localRotation = rear.localRotation = Quaternion.identity;
            foreach (var r in rams) BuildRam(r, fm, r.name.StartsWith("Ripper") || r.name.StartsWith("Sideshift") ? fm.dark : paint, id);

            var rb = root.GetComponent<Rigidbody>(); if (rb == null) rb = root.AddComponent<Rigidbody>(); // the machine script already required one
            rb.mass = 19500f; rb.angularDamping = 3f; rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var col = root.AddComponent<BoxCollider>();
            float rearEnd = -0.35f - RHL * 1.04f, frontEnd = -0.35f + FB + tireR + 0.1f, topY = eb + RHH * 1.22f;
            col.center = new Vector3(0f, topY * 0.5f, (rearEnd + frontEnd) * 0.5f);
            col.size = new Vector3(gauge + tireW, topY, frontEnd - rearEnd);

            gr.exitPoint = Marker(t, "ExitPoint", new Vector3(-(gauge * 0.5f + tireW + 1.1f), 0f, 0.6f));
            gr.vehicleCamera = VehicleCam(t, t, seat, 13f, 4.5f);
            return root;
        }
    }
}
