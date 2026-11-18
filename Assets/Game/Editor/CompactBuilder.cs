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
                chL = 2.26f, chW = 1.24f, chH = 1.04f, cabL = 1.28f, cabW = 1.06f, cabH = 1.24f, armL = 1.88f, armD = 0.2f, bW = 1.88f, bR = 0.38f,
                tonnes = 3.63f, capacity = 0.48f, fuel = 95f, burn = 9f, drive = 3.4f, turn = 75f },
            new SkidCfg { id = "SkidTrack", name = "Compact Track Loader 5.2 t", tracked = true, blackBucket = true,
                trackLength = 2.28f, gauge = 1.56f, trackHeight = 0.72f, sprocketElev = 0.62f, shoe = 0.45f,
                chL = 2.46f, chW = 1.3f, chH = 1.06f, cabL = 1.36f, cabW = 1.12f, cabH = 1.24f, armL = 2.04f, armD = 0.22f, bW = 2.14f, bR = 0.42f,
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
            float belly = c.standOn ? c.trackHeight * 0.34f : c.tracked ? c.trackHeight * 0.32f : c.tireR * 0.52f;
            var wheels = new List<Transform>();

            // --- A. tracks (triangular rubber, raised rear sprocket) or four lugged tyres
            if (c.tracked)
            {
                float TH = c.trackHeight, SW = c.shoe, idR = TH * 0.27f, spR = TH * 0.29f;
                float half = (c.trackLength - idR * 2f) * 0.5f, spX = -half * 0.58f, spY = c.sprocketElev;
                var frameMat = c.standOn ? accent : paint;
                foreach (int s in new[] { -1, 1 })
                {
                    float zs = s * c.gauge * 0.5f;
                    g.Box(frameMat, F(0f, idR + 0.03f, zs), FS(half * 2.05f, idR * 0.85f, SW * 0.54f));
                    foreach (float ix in new[] { -half, half })
                    {
                        g.Cyl(frameMat, F(ix, idR + 0.03f, zs), AxisLat, idR * 0.9f, idR * 0.9f, SW * 0.62f, 12);
                        g.Cyl(fm.dark, F(ix, idR + 0.03f, zs), AxisLat, idR * 0.36f, idR * 0.36f, SW * 0.72f, 8);
                    }
                    var belt = new List<Vector3> { new(half, idR + 0.03f, idR), new(-half, idR + 0.03f, idR), new(spX, spY, spR) };
                    for (int r = 0; r < 4; r++)
                    {
                        float rx = -half * 0.55f + r / 3f * half * 1.1f;
                        g.Cyl(frameMat, F(rx, idR * 0.45f + 0.02f, zs), AxisLat, idR * 0.42f, idR * 0.42f, SW * 0.65f, 10);
                        belt.Add(new Vector3(rx, idR * 0.45f + 0.02f, idR * 0.42f));
                    }
                    g.Cyl(frameMat, F(spX, spY, zs), AxisLat, spR, spR, SW * 0.64f, 14);
                    g.Cyl(fm.dark, F(spX, spY, zs), AxisLat, spR * 0.48f, spR * 0.48f, SW * 0.74f, 10);
                    // rubber belt: lugs run round the idlers, rollers and sprocket as the machine moves
                    TrackAnimator.AddBelt(root, fm.shoe, zs, SW, Mathf.Max(0.032f, TH * 0.055f), Mathf.Max(0.05f, c.trackLength / 18f), belt.ToArray());
                    if (!c.standOn)
                        foreach (float tx in new[] { -0.45f, 0.45f })
                            g.Cyl(fm.dark, F(tx, belly + 0.06f, s * c.gauge * 0.34f), AxisLat, 0.065f, 0.065f, c.gauge * 0.36f, 8);
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
                        GeoWheel(gw, Vector3.zero, c.tireR, c.tireW, fm.shoe, paint, accent, 16);
                        gw.Attach(wn, id + "_Wheel" + k++);
                        wheels.Add(wn);
                    }
            }

            // --- B. chassis
            float hl = c.chL * 0.48f, armSpanZ, towerX, towerTopY, elbowX, elbowY, tipX, tipY, armPivotX, liftBaseX, liftBaseY, liftRodK;
            Transform seat;
            if (!c.standOn)
            {
                float tubH = c.chH * 0.48f, deckY = belly + tubH, bonH = c.chH * 0.82f;
                var tub = new List<Vector2> { new(-hl * 0.92f, 0f), new(hl * 0.88f, 0f), new(hl, tubH * 0.45f), new(hl * 0.94f, tubH), new(-hl, tubH), new(-hl * 1.04f, tubH * 0.35f) };
                g.Prism(paint, tub, (p, z) => F(p.x, belly + p.y, z), -c.chW * 0.5f, c.chW * 0.5f);
                var bon = new List<Vector2> { new(-hl * 0.98f, 0f), new(-hl * 0.14f, 0f), new(-hl * 0.22f, bonH), new(-hl * 0.78f, bonH * 0.84f), new(-hl * 1.04f, bonH * 0.24f) };
                g.Prism(paint, bon, (p, z) => F(p.x, deckY + p.y, z), -c.chW * 0.47f, c.chW * 0.47f);
                g.Box(accent, F(-hl * 0.93f, deckY + bonH * 0.48f, 0f), FS(0.06f, bonH * 0.58f, c.chW * 0.78f), RZ(0.38f));
                g.Box(accent, F(-hl * 0.5f, deckY + bonH * 0.92f, 0f), FS(hl * 0.52f, 0.03f, c.chW * 0.72f), RZ(0.24f));
                g.Cyl(fm.dark, F(-hl * 0.28f, deckY + bonH + 0.1f, c.chW * 0.28f), Quaternion.identity, 0.028f, 0.026f, bonH * 0.45f, 8);

                towerX = -hl * 0.78f;
                towerTopY = belly + c.chH * (c.tracked ? 1.62f : 1.66f);
                armSpanZ = c.chW * 0.5f + c.armD * 0.5f;
                foreach (int s in new[] { -1, 1 })
                {
                    g.Box(paint, F(towerX, belly + c.chH * (c.tracked ? 1.03f : 1.05f), s * armSpanZ), FS(c.armD * 1.45f, c.chH * (c.tracked ? 1.2f : 1.24f), c.armD * 0.64f), RZ(-0.08f));
                    g.Box(fm.dark, F(-c.chL * 0.06f, deckY + 0.02f, s * armSpanZ), FS(c.armD * 1.25f, c.armD * 1.05f, c.armD * 0.58f));
                }

                // forward-tapered ROPS/FOPS cab with side guard bars
                float cL = c.cabL, cH = c.cabH, cW = c.cabW;
                Vector3 cab = F(c.chL * 0.11f, deckY, 0f);
                var prof = new List<Vector2> { new(-cL * 0.48f, 0f), new(cL * 0.52f, 0f), new(cL * 0.36f, cH * 0.94f), new(-cL * 0.44f, cH * 0.98f) };
                g.Prism(fm.glass, prof, (p, z) => cab + F(p.x, p.y, z), -cW * 0.47f, cW * 0.47f);
                g.Box(accent, cab + F(-cL * 0.03f, cH * 0.97f, 0f), FS(cL * 0.94f, cH * 0.09f, cW * 1.06f), RZ(-0.05f));
                foreach (int s in new[] { -1, 1 })
                {
                    float fz = s * cW * 0.46f;
                    g.Cyl(accent, cab + F(cL * 0.44f, cH * 0.47f, fz), RZ(0.16f), 0.026f, 0.026f, cH * 0.98f, 6);
                    g.Cyl(accent, cab + F(-cL * 0.46f, cH * 0.49f, fz), RZ(-0.04f), 0.026f, 0.026f, cH * 0.98f, 6);
                    for (int b = 0; b < 5; b++)
                        g.Box(accent, cab + F(-cL * 0.02f, cH * (0.28f + b * 0.14f), fz * 1.02f), FS(cL * 0.8f, 0.012f, 0.012f));
                    g.Box(accent, cab + F(cL * 0.44f, cH * 0.93f, s * cW * 0.38f), FS(0.08f, 0.06f, 0.11f));
                    g.Box(fm.amber, cab + F(cL * 0.48f, cH * 0.93f, s * cW * 0.38f), FS(0.02f, 0.04f, 0.08f));
                }
                g.Box(fm.dark, cab + F(cL * 0.56f, 0.02f, 0f), FS(0.24f, 0.04f, cW * 0.72f));
                seat = Node(t, "CabView", cab + F(0.12f, Mathf.Min(cH * 0.86f, 1.08f), 0f));
                CabInterior(seat, CabStyle.SkidSteer, CabMatsFrom(fm, accent), id);

                elbowX = c.armL * 0.66f; elbowY = -c.chH * (c.tracked ? 0.22f : 0.24f);
                tipX = c.armL; tipY = -c.chH * (c.tracked ? 1.24f : 1.28f);
                armPivotX = towerX;
                liftBaseX = -c.chL * 0.06f; liftBaseY = deckY + 0.04f; liftRodK = 0.85f;
            }
            else
            {
                // stand-on: compact tub, sloped black hood, rear control tower and the operator's platform
                g.Box(paint, F(0.02f, belly + c.chH * 0.27f, 0f), FS(c.chL * 0.86f, c.chH * 0.54f, c.chW));
                g.Box(accent, F(0.06f, belly + c.chH * 0.72f, 0f), FS(c.chL * 0.62f, c.chH * 0.44f, c.chW * 0.92f), RZ(-0.34f));
                g.Box(fm.pin, F(0.06f - 0.07f, belly + c.chH * 0.72f + c.chH * 0.21f, 0f), FS(c.chL * 0.52f, 0.02f, c.chW * 0.76f), RZ(-0.34f));
                float rearTowerX = -c.chL * 0.38f;
                g.Box(paint, F(rearTowerX, belly + c.chH * 0.84f, 0f), FS(c.chL * 0.26f, c.chH * 0.68f, c.chW * 0.94f));
                g.Box(accent, F(-c.chL * 0.52f, belly + c.chH * 0.74f, 0f), FS(0.07f, c.chH * 0.42f, c.chW * 0.86f));
                g.Box(fm.dark, F(-c.chL * 0.66f, belly * 0.82f, 0f), FS(0.36f, 0.04f, c.chW * 0.92f));
                foreach (int s in new[] { -1, 1 })
                    g.Cyl(accent, F(-c.chL * 0.62f, belly * 0.82f + c.chH * 0.42f, s * c.chW * 0.44f), Quaternion.identity, 0.015f, 0.015f, c.chH * 0.85f, 6);
                g.Box(fm.amber, F(rearTowerX + 0.1f, belly + c.chH * 1.18f + 0.04f, 0f), FS(0.06f, 0.055f, 0.1f));
                seat = Node(t, "CabView", F(rearTowerX - 0.3f, belly * 0.82f + 1.62f, 0f));
                CabInterior(seat, CabStyle.StandOn, CabMatsFrom(fm, accent), id);

                armSpanZ = c.chW * 0.5f + c.armD * 0.52f;
                towerX = rearTowerX;
                towerTopY = belly + c.chH * 1.02f;
                armPivotX = rearTowerX + 0.04f;
                elbowX = c.armL * 0.68f; elbowY = -c.chH * 0.24f;
                tipX = c.armL; tipY = -c.chH * 0.88f;
                liftBaseX = -c.chL * 0.36f; liftBaseY = belly + c.chH * 0.34f; liftRodK = 0.68f;
            }

            // --- C. lift arms
            var armPivot = Node(t, "ArmPivot", F(armPivotX, towerTopY, 0f));
            var gA = new Geo();
            float upLen = Mathf.Sqrt(elbowX * elbowX + elbowY * elbowY), upAng = Mathf.Atan2(elbowY, elbowX);
            float loDx = tipX - elbowX, loDy = tipY - elbowY, loLen = Mathf.Sqrt(loDx * loDx + loDy * loDy), loAng = Mathf.Atan2(loDy, loDx);
            float D = c.armD;
            foreach (int s in new[] { -1, 1 })
            {
                float z = s * armSpanZ;
                gA.Box(paint, F(elbowX * 0.5f, elbowY * 0.5f, z), FS(upLen * 1.06f, D * 1.04f, D * 0.54f), RZ(upAng));
                if (!c.standOn) gA.Box(accent, F(elbowX * 0.55f, elbowY * 0.55f + D * 0.16f, z), FS(upLen * 0.52f, D * 0.34f, D * 0.58f), RZ(upAng));
                gA.Box(paint, F(elbowX + loDx * 0.5f, elbowY + loDy * 0.5f, z), FS(loLen * 1.05f, D * 0.92f, D * 0.5f), RZ(loAng));
                Pin(gA, fm, F(0f, 0f, z), D * 0.22f, D * 0.85f);
                Pin(gA, fm, F(tipX, tipY, z), D * 0.2f, D * 0.78f);
                Pin(g, fm, F(liftBaseX, liftBaseY, z), D * 0.18f, D * 0.72f);
                Vector2 rod = new Vector2(elbowX * liftRodK, elbowY * liftRodK - D * 0.44f);
                Pin(gA, fm, F(rod.x, rod.y, z), D * 0.18f, D * 0.72f);
                rams.Add(new Ram { name = "Lift Ram " + (s < 0 ? "L" : "R"), parent = t, barrelR = D * 0.22f, rodR = D * 0.13f,
                    a = Node(t, "LiftRamBase" + s, F(liftBaseX, liftBaseY, z)), b = Node(armPivot, "LiftRamRod" + s, F(rod.x, rod.y, z)) });
            }
            float frac = c.standOn ? 0.25f : 0.45f;
            float brX = elbowX + loDx * frac, brY = elbowY + loDy * frac;
            if (!c.standOn)
            {
                gA.Cyl(paint, F(brX, brY, 0f), AxisLat, D * 0.42f, D * 0.42f, armSpanZ * 2f, 12);
                gA.Box(paint, F(brX + D * 0.24f, brY + D * 0.1f, 0f), FS(D * 0.92f, D * 1.38f, armSpanZ * 1.76f));
            }
            else
            {
                gA.Box(paint, F(brX, brY, 0f), FS(D * 1.15f, D * 1.25f, armSpanZ * 2.05f));
                gA.Box(paint, F(brX, brY + D * 0.85f, 0f), FS(D, D * 1.4f, D * 0.9f));
            }
            ToolBox(armPivot, "Arms", F(elbowX * 0.5f, elbowY * 0.5f, 0f), FS(upLen, D, armSpanZ * 2f + D), RZ(upAng));

            // --- D. quick-coupler bucket and tilt rams
            var bucketPivot = Node(armPivot, "BucketPivot", F(tipX, tipY, 0f));
            var gK = new Geo();
            float bR = c.bR, bW = c.bW, plate = c.standOn ? 0.016f : Mathf.Max(0.022f, bR * 0.06f), lipX = c.standOn ? 1.75f : 1.82f;
            var body = c.blackBucket ? fm.wear : paint;
            Vector2 V(float x, float y) => new Vector2(x * bR, y * bR);
            var shell = new List<Vector2> { V(0f, 1.15f), V(0.18f, 1.15f) };
            Quadratic(shell, V(0.24f, 0.2f), V(0.56f, 0.05f));
            shell.Add(V(lipX, 0f)); shell.Add(new Vector2(lipX * bR, -plate)); shell.Add(new Vector2(0.48f * bR, -plate));
            Quadratic(shell, V(0.08f, 0.12f), V(0f, 1.15f));
            gK.Prism(body, DropLast(shell), (p, z) => F(p.x, p.y, z), -bW * 0.5f, bW * 0.5f);
            var cheek = new List<Vector2> { V(0f, 1.15f), V(0.18f, 1.15f), V(lipX, 0f), new(lipX * bR, -plate), new(0f, -plate) };
            foreach (float zs in new[] { -bW * 0.5f, bW * 0.5f - plate })
                gK.Prism(body, cheek, (p, z) => F(p.x, p.y, z), zs, zs + plate);
            gK.Box(fm.dark, F(plate * 0.9f, bR * 0.42f, 0f), FS(plate * 1.8f, bR * 1.15f, armSpanZ * 1.82f));
            gK.Box(body, F(bR * 0.12f, bR * 1.18f, 0f), FS(bR * 0.22f, bR * 0.12f, bW * 0.98f));
            gK.Box(fm.wear, F(bR * (lipX - 0.1f), -plate * 0.45f, 0f), FS(bR * 0.36f, plate * 1.35f, bW * 1.02f));
            gK.Attach(bucketPivot, id + "_Bucket");
            ToolBox(bucketPivot, "Bucket", F(bR * 0.9f, bR * 0.45f, 0f), FS(bR * 1.8f, bR * 0.95f, bW * 0.95f), Quaternion.identity);
            var tip = Node(bucketPivot, "BucketTip", F(bR * lipX, 0.01f, 0f));
            var mound = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(mound.GetComponent<Collider>());
            mound.name = "Bucket Load (dirt)";
            mound.transform.SetParent(bucketPivot, false);
            mound.transform.localPosition = F(bR * 0.85f, bR * 0.38f, 0f);
            mound.transform.localScale = FS(bR * 1.5f, bR * 0.8f, bW * 0.88f);
            mound.GetComponent<MeshRenderer>().sharedMaterial = fm.dirt;
            mound.SetActive(false);

            if (!c.standOn)
            {
                Vector2 tb = new Vector2(elbowX + loDx * 0.22f - D * 0.38f, elbowY + loDy * 0.22f + D * 0.28f);
                foreach (int s in new[] { -1, 1 })
                {
                    float z = s * armSpanZ;
                    Pin(gA, fm, F(tb.x, tb.y, z), D * 0.16f, D * 0.68f);
                    rams.Add(new Ram { name = "Tilt Ram " + (s < 0 ? "L" : "R"), parent = t, barrelR = D * 0.18f, rodR = D * 0.11f,
                        a = Node(armPivot, "TiltRamBase" + s, F(tb.x, tb.y, z)), b = Node(bucketPivot, "TiltRamRod" + s, F(bR * 0.05f, bR * 0.98f, z)) });
                }
            }
            else
            {
                Pin(gA, fm, F(brX + D * 0.15f, brY + D * 1.35f, 0f), D * 0.2f, D * 1.15f);
                rams.Add(new Ram { name = "Tilt Ram", parent = t, barrelR = D * 0.28f, rodR = D * 0.17f,
                    a = Node(armPivot, "TiltRamBase", F(brX + D * 0.15f, brY + D * 1.35f, 0f)), b = Node(bucketPivot, "TiltRamRod", F(bR * 0.04f, bR * 1.05f, 0f)) });
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
            float low = (Mathf.Asin(Mathf.Clamp(-(towerTopY + 0.14f) / r0, -1f, 1f)) - phi0) * Mathf.Rad2Deg; // the edge can bite a little below grade
            ld.armRange = new Vector2(Mathf.Max(-65f, low), 46f);

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
            float top = c.standOn ? belly + c.chH * 1.2f : belly + c.chH * 0.48f + c.cabH;
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
