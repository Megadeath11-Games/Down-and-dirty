using FPSBase.Vehicles;
using UnityEngine;

namespace FPSBase.EditorTools
{
    /// <summary>
    /// Operator stations, built around a machine's eye point (its CabView marker) at human scale, from the fleet
    /// visualizer's cab interiors: suspension seat, armrest pods with pilot joysticks, travel levers, pedals,
    /// lap bar, or a steering wheel. The moving parts are pivots driven by <see cref="CabControls"/>; the grips and
    /// pedals are where the seated driver's hands and feet go.
    /// </summary>
    public static partial class ProjectBootstrap
    {
        enum CabStyle { Excavator, Dozer, Loader, SkidSteer, Grader, Truck, StandOn }

        class CabMats { public Material seat, dark, metal, accent, screen, amber; }

        static CabMats CabMatsFrom(FleetMats fm, Material accent) => new CabMats
        {
            seat = fm.shoe, dark = fm.dark, metal = fm.pin, accent = accent, screen = fm.glass, amber = fm.amber,
        };

        /// <summary>Builds the station next to `eye` (same parent) and returns its controls.</summary>
        static CabControls CabInterior(Transform eye, CabStyle style, CabMats m, string id, float eyeAboveFloor = 1.24f, bool ownSeat = false)
        {
            float fy = 1.24f - eyeAboveFloor; // the station below the seat is laid out for a 1.24 m eye height: lift it to this cab's floor
            var node = Node(eye.parent, "Cab Interior", eye.localPosition);
            var cc = node.gameObject.AddComponent<CabControls>();
            var g = new Geo();
            Vector3 P(float x, float y, float z) => new Vector3(x, y, z);

            if (style != CabStyle.StandOn && !ownSeat)
            {
                // suspension seat on its pedestal
                g.Box(m.dark, P(0f, -1.0f, -0.1f), P(0.3f, 0.3f, 0.3f));
                g.Box(m.seat, P(0f, -0.8f, -0.08f), P(0.5f, 0.1f, 0.48f));
                g.Box(m.seat, P(0f, -0.47f, -0.33f), P(0.48f, 0.62f, 0.09f), Quaternion.Euler(-10f, 0f, 0f));
                g.Box(m.seat, P(0f, -0.08f, -0.39f), P(0.28f, 0.16f, 0.08f), Quaternion.Euler(-10f, 0f, 0f));
                g.Box(m.dark, P(0f, -1.24f, 0.2f), P(0.7f, 0.04f, 0.8f)); // floor plate
            }

            bool sticks = style != CabStyle.Truck;
            if (sticks)
            {
                float sx = style == CabStyle.StandOn ? 0.14f : 0.3f;
                float sy = style == CabStyle.StandOn ? -0.7f : -0.56f;
                float sz = style == CabStyle.StandOn ? 0.3f : 0.16f;
                foreach (int s in new[] { -1, 1 })
                {
                    if (style != CabStyle.StandOn)
                    {
                        // armrest pod with its screen / switch panel
                        g.Box(m.dark, P(s * sx, sy - 0.07f, 0.0f), P(0.13f, 0.1f, 0.42f));
                        g.Box(m.seat, P(s * sx, sy - 0.0f, -0.12f), P(0.11f, 0.05f, 0.22f));
                        g.Box(m.accent, P(s * sx, sy - 0.015f, 0.12f), P(0.1f, 0.03f, 0.1f));
                    }
                    else g.Box(m.accent, P(s * sx, sy - 0.03f, sz), P(0.07f, 0.06f, 0.07f));
                    var pivot = Node(node, s < 0 ? "Left Joystick" : "Right Joystick", P(s * sx, sy, sz));
                    var gs = new Geo();
                    gs.Cyl(m.seat, P(0f, 0.015f, 0f), Quaternion.identity, 0.03f, 0.022f, 0.03f, 8); // rubber boot
                    gs.Cyl(m.metal, P(0f, 0.08f, 0f), Quaternion.identity, 0.01f, 0.01f, 0.13f, 6);
                    gs.Box(m.seat, P(0f, 0.17f, 0f), P(0.04f, 0.1f, 0.045f), Quaternion.Euler(12f, 0f, 0f));
                    gs.Box(m.accent, P(0f, 0.225f, 0.008f), P(0.03f, 0.012f, 0.03f));
                    gs.Attach(pivot, id + (s < 0 ? "_Cab_StickL" : "_Cab_StickR"));
                    var grip = Node(pivot, "Grip", P(0f, 0.17f, 0f));
                    if (s < 0) { cc.leftStick = pivot; cc.leftGrip = grip; } else { cc.rightStick = pivot; cc.rightGrip = grip; }
                }
            }

            // feet: resting spots, or pedals that swing
            Transform Pedal(string name, float x, string asset)
            {
                var pv = Node(node, name, P(x, -1.2f + fy, 0.36f));
                var gp = new Geo();
                gp.Box(m.dark, P(0f, 0.09f, 0.04f), P(0.1f, 0.2f, 0.025f), Quaternion.Euler(-35f, 0f, 0f));
                gp.Box(m.metal, P(0f, 0.09f, 0.056f), P(0.09f, 0.17f, 0.006f), Quaternion.Euler(-35f, 0f, 0f));
                gp.Attach(pv, id + asset);
                return pv;
            }
            switch (style)
            {
                case CabStyle.Excavator:
                {
                    // twin travel levers with their foot pedals, and a short blade lever on the right console
                    foreach (int s in new[] { -1, 1 })
                    {
                        var lv = Node(node, s < 0 ? "Left Travel Lever" : "Right Travel Lever", P(s * 0.1f, -1.06f, 0.48f));
                        var gl = new Geo();
                        gl.Box(m.dark, P(0f, 0.02f, 0f), P(0.07f, 0.05f, 0.06f));
                        gl.Cyl(m.metal, P(0f, 0.25f, -0.06f), Quaternion.Euler(-14f, 0f, 0f), 0.012f, 0.012f, 0.46f, 6);
                        gl.Box(m.seat, P(0f, 0.48f, -0.12f), P(0.05f, 0.09f, 0.04f));
                        gl.Box(m.dark, P(0f, 0.04f, 0.1f), P(0.1f, 0.025f, 0.2f), Quaternion.Euler(-18f, 0f, 0f));
                        gl.Attach(lv, id + (s < 0 ? "_Cab_TravelL" : "_Cab_TravelR"));
                        if (s < 0) cc.leftLever = lv; else cc.rightLever = lv;
                        var foot = Node(lv, "Foot", P(0f, 0.06f, 0.08f));
                        if (s < 0) cc.leftFoot = foot; else cc.rightFoot = foot;
                    }
                    var aux = Node(node, "Blade Lever", P(0.42f, -0.62f, 0.2f));
                    var ga = new Geo();
                    ga.Cyl(m.metal, P(0f, 0.08f, 0f), Quaternion.identity, 0.009f, 0.009f, 0.16f, 6);
                    ga.Cyl(m.amber, P(0f, 0.17f, 0f), Quaternion.identity, 0.022f, 0.022f, 0.04f, 8);
                    ga.Attach(aux, id + "_Cab_BladeLever");
                    cc.auxLever = aux;
                    g.Box(m.screen, P(0.36f, -0.3f, 0.42f), P(0.2f, 0.14f, 0.02f), Quaternion.Euler(-15f, -25f, 0f)); // monitor
                    break;
                }
                case CabStyle.Dozer:
                case CabStyle.Grader:
                    cc.leftPedal = Pedal("Decel Pedal", -0.16f, "_Cab_PedalL");
                    cc.rightPedal = Pedal("Throttle Pedal", 0.16f, "_Cab_PedalR");
                    cc.leftFoot = Node(cc.leftPedal, "Foot", P(0f, 0.1f, 0.06f));
                    cc.rightFoot = Node(cc.rightPedal, "Foot", P(0f, 0.1f, 0.06f));
                    g.Box(m.dark, P(0f, -0.62f, 0.55f), P(0.7f, 0.3f, 0.14f)); // front dash
                    g.Box(m.screen, P(0f, -0.44f, 0.55f), P(0.22f, 0.12f, 0.02f), Quaternion.Euler(-20f, 0f, 0f));
                    break;
                case CabStyle.Loader:
                case CabStyle.SkidSteer:
                    cc.leftFoot = Node(node, "Left Foot Rest", P(-0.14f, -1.2f, 0.4f));
                    cc.rightFoot = Node(node, "Right Foot Rest", P(0.14f, -1.2f, 0.4f));
                    if (style == CabStyle.SkidSteer)
                    {
                        // seat bar across the lap
                        foreach (int s in new[] { -1, 1 })
                            g.Box(m.dark, P(s * 0.36f, -0.6f, -0.02f), P(0.03f, 0.03f, 0.34f), Quaternion.Euler(-30f, 0f, 0f));
                        g.Box(m.seat, P(0f, -0.52f, 0.16f), P(0.72f, 0.04f, 0.04f));
                    }
                    break;
                case CabStyle.Truck:
                {
                    cc.leftPedal = Pedal("Brake Pedal", -0.06f, "_Cab_PedalL");
                    cc.rightPedal = Pedal("Throttle Pedal", 0.16f, "_Cab_PedalR");
                    cc.leftFoot = Node(node, "Left Foot Rest", P(-0.2f, -1.2f + fy, 0.4f));
                    cc.rightFoot = Node(cc.rightPedal, "Foot", P(0f, 0.1f, 0.06f));
                    // steering column and wheel; the wheel turns about its own z (the column)
                    var col = Quaternion.Euler(-62f, 0f, 0f);
                    g.Cyl(m.dark, P(0f, -0.62f, 0.62f), col, 0.03f, 0.03f, 0.4f, 8);
                    g.Box(m.dark, P(0f, -0.56f, 0.6f), P(0.8f, 0.22f, 0.16f)); // dash
                    var wheel = Node(node, "Steering Wheel", P(0f, -0.46f, 0.36f));
                    wheel.localRotation = Quaternion.LookRotation(col * Vector3.down, col * Vector3.forward); // z down the column toward the driver
                    var gw = new Geo();
                    const int n = 14; const float wr = 0.19f;
                    for (int i = 0; i < n; i++)
                    {
                        float a0 = i * Mathf.PI * 2f / n, a1 = (i + 1) * Mathf.PI * 2f / n;
                        Vector3 p0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f) * wr, p1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f) * wr;
                        gw.Box(m.seat, (p0 + p1) * 0.5f, new Vector3(Vector3.Distance(p0, p1) * 1.05f, 0.025f, 0.025f),
                            Quaternion.Euler(0f, 0f, (a0 + a1) * 0.5f * Mathf.Rad2Deg + 90f));
                    }
                    foreach (float a in new[] { 90f, 210f, 330f })
                        gw.Box(m.dark, Quaternion.Euler(0f, 0f, a) * new Vector3(wr * 0.5f, 0f, 0f), new Vector3(wr, 0.02f, 0.012f), Quaternion.Euler(0f, 0f, a));
                    gw.Cyl(m.dark, Vector3.zero, Quaternion.Euler(90f, 0f, 0f), 0.045f, 0.045f, 0.04f, 8);
                    gw.Attach(wheel, id + "_Cab_Wheel");
                    cc.wheel = wheel;
                    // hands at ten to two
                    cc.leftGrip = Node(wheel, "Grip L", new Vector3(-wr * 0.82f, wr * 0.55f, 0f));
                    cc.rightGrip = Node(wheel, "Grip R", new Vector3(wr * 0.82f, wr * 0.55f, 0f));
                    cc.wheelAngle = 300f;
                    break;
                }
                case CabStyle.StandOn:
                    cc.standing = true;
                    break;
            }
            g.Attach(node, id + "_CabInterior");
            return cc;
        }
    }
}
