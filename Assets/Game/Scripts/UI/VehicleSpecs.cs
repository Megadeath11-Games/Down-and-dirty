using System.Collections.Generic;
using FPSBase.Vehicles;
using UnityEngine;

namespace FPSBase.UI
{
    /// <summary>The spec sheet for a machine in the shop, read off its prefab (so it's always what you get).</summary>
    public static class VehicleSpecs
    {
        static readonly Dictionary<GameObject, List<(string, string)>> cache = new();

        public static List<(string label, string value)> Of(GameObject prefab)
        {
            if (prefab == null) return new List<(string, string)>();
            if (cache.TryGetValue(prefab, out var got)) return got;
            var s = new List<(string, string)>();
            var rb = prefab.GetComponent<Rigidbody>();
            var v = prefab.GetComponent<VehicleBase>();
            var tr = prefab.GetComponent<Trailer>();

            // what it does
            if (prefab.GetComponent<DumpPickup>() is DumpPickup pk)
            {
                s.Add(("Dump box", $"{pk.capacity:0.#} m³"));
                s.Add(("Top speed", $"{pk.topSpeedKmh:0} km/h"));
                s.Add(("Drive", pk.transferCase ? "4x4: 2WD / 4-high / 4-low" : pk.wheels != null && pk.wheels.Length > 4 ? "Tandem rear, diffs locked" : "Rear-wheel drive"));
                if (pk.lowGearBoost > 1.01f) s.Add(("Low gears", $"{pk.lowGearBoost:0.#}x pull at a crawl"));
                if (pk.tidyTank != null) s.Add(("Tidy tank", $"{pk.tidyTank.capacity:0} L diesel"));
            }
            else if (prefab.GetComponent<MiniExcavator>() is MiniExcavator ex)
            {
                s.Add(("Bucket", $"{ex.bucketCapacity:0.##} m³"));
                s.Add(("Dig rate", $"{ex.digRate * 60f:0.#} m³/min"));
                s.Add(("Track speed", $"{ex.driveSpeed * 3.6f:0.#} km/h"));
                s.Add(("Swing", $"{ex.swingSpeed:0}°/s"));
                if (HasChild(prefab, "Blade")) s.Add(("Front blade", "yes (backfill)"));
            }
            else if (prefab.GetComponent<Bulldozer>() is Bulldozer dz)
            {
                s.Add(("Blade", $"{dz.bladeWidth:0.##} m wide, {dz.heapCapacity:0.#} m³ load"));
                s.Add(("Blade moves", $"tilt {dz.maxTilt:0}°, swivel {dz.maxAngle:0}°"));
                if (HasChild(prefab, "Ripper")) s.Add(("Ripper", "yes"));
                s.Add(("Speed", $"{dz.driveSpeed * 3.6f:0.#} km/h"));
            }
            else if (prefab.GetComponent<FrontLoader>() is FrontLoader ld)
            {
                s.Add(("Bucket", $"{ld.bucketCapacity:0.##} m³"));
                s.Add(("Steering", ld.skidSteer ? "skid steer (turns on the spot)" : "articulated"));
                s.Add(("Speed", $"{ld.driveSpeed * 3.6f:0.#} km/h"));
            }
            else if (prefab.GetComponent<MotorGrader>() is MotorGrader gr)
            {
                s.Add(("Moldboard", $"circle ±{gr.maxCircle:0}°, side-shift ±{gr.maxShift:0.##} m"));
                s.Add(("Articulation", $"±{gr.maxArtic:0}°, wheel lean ±{gr.maxLean:0}°"));
                s.Add(("Speed", $"{gr.driveSpeed * 3.6f:0.#} km/h"));
            }
            else if (prefab.GetComponent<SemiTruck>() is SemiTruck semi)
            {
                var low = prefab.GetComponentInChildren<Trailer>(true);
                if (low != null)
                {
                    s.Add(("Lowboy payload", $"{low.payloadTons:0} t"));
                    s.Add(("Deck", $"{low.deckSize.y:0.#} m long, {low.deckSize.x:0.##} m wide"));
                    s.Add(("Gooseneck", "removable: the deck drops to the ground to drive on"));
                }
                int axles = CountAxles(semi.wheelSpins, semi.transform, low);
                if (axles > 0) s.Add(("Drive axles", $"{axles}"));
                s.Add(("Top speed", $"{semi.driveSpeed * 3.6f:0} km/h"));
            }
            if (tr != null)
            {
                if (tr.payloadTons > 0f) s.Add(("Payload", $"{tr.payloadTons:0.#} t"));
                if (tr.deckSize.sqrMagnitude > 0.01f && tr.payloadTons > 0f) s.Add(("Deck", $"{tr.deckSize.y:0.#} m long, {tr.deckSize.x:0.##} m wide"));
                s.Add(("Hitch", tr.coupler switch { HitchType.Pintle => "pintle hook / bumper pull", HitchType.Gooseneck => "gooseneck ball (pickup bed)", _ => tr.coupler.ToString() }));
                var ft = prefab.GetComponentInChildren<FuelTank>(true);
                if (ft != null) s.Add(("Fuel tank", $"{ft.capacity:N0} L diesel, with a pump and hose"));
            }

            // the basics
            if (rb != null) s.Add(("Weight", rb.mass >= 1000f ? $"{rb.mass / 1000f:0.#} t" : $"{rb.mass:0} kg"));
            var lb = VehicleSpot.LocalBounds(prefab.transform);
            s.Add(("Size (L x W x H)", $"{lb.size.z:0.#} x {lb.size.x:0.#} x {lb.size.y:0.#} m"));
            if (v != null)
            {
                s.Add(("Fuel tank", $"{v.fuelCapacity:N0} L"));
                s.Add(("Fuel use", $"{v.workBurn:0.#} L/h working"));
                var hitches = new List<string>();
                foreach (var h in prefab.GetComponentsInChildren<Hitch>(true))
                {
                    string n = h.type switch { HitchType.Pintle => "pintle hook", HitchType.Gooseneck => "gooseneck ball", HitchType.FifthWheel => "fifth wheel", _ => h.type.ToString() };
                    if (!hitches.Contains(n)) hitches.Add(n);
                }
                if (hitches.Count > 0) s.Add(("Tows with", string.Join(", ", hitches)));
            }
            cache[prefab] = s;
            return s;
        }

        static bool HasChild(GameObject go, string contains)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t.name.Contains(contains)) return true;
            return false;
        }

        static int CountAxles(Transform[] wheels, Transform truck, Trailer low)
        {
            var zs = new List<float>();
            if (wheels == null) return 0;
            foreach (var w in wheels)
            {
                if (w == null || (low != null && w.IsChildOf(low.transform))) continue;
                float z = Mathf.Round(truck.InverseTransformPoint(w.position).z * 2f) / 2f;
                if (!zs.Contains(z)) zs.Add(z);
            }
            return Mathf.Max(0, zs.Count - 1); // the steer axle doesn't drive
        }
    }
}
