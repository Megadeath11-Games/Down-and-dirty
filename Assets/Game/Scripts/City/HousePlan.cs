using System.Collections.Generic;
using UnityEngine;

namespace FPSBase.City
{
    /// <summary>
    /// A house you can build: its footprint (the pad to level), the footings under its outside walls and its
    /// load-bearing inside walls (the trenches to dig), how deep they go, how level the pad has to be and how much
    /// concrete it takes. One asset per plan in Resources/HousePlans (made by the editor's house plan builder; edit
    /// them there). Coordinates are metres on the plan: x across the front, z from the front to the back, the middle
    /// of the house at 0, 0.
    /// </summary>
    [CreateAssetMenu(menuName = "Down and Dirty/House plan", fileName = "HousePlan")]
    public class HousePlan : ScriptableObject
    {
        [Tooltip("Where it shows in the list.")]
        public int order;
        public string title = "House";
        [TextArea] public string description = "";
        [Tooltip("The footprint's shape, for the list (rectangle, L-shaped...).")]
        public string shape = "Rectangle";
        public int floors = 1;
        [Tooltip("The plans and the building permit ($).")]
        public float price = 500f;

        [Header("Footprint and footings")]
        [Tooltip("The corners of the footprint, in order round the outside (m). The footings run under every edge.")]
        public Vector2[] outline = { new(-4f, -5f), new(4f, -5f), new(4f, 5f), new(-4f, 5f) };
        [System.Serializable]
        public struct Footing { public Vector2 a, b; }
        [Tooltip("Footings under the load-bearing inside walls (m).")]
        public Footing[] innerFootings = new Footing[0];
        [Tooltip("How wide the footing trenches are dug (m).")]
        public float footingWidth = 0.5f;
        [Tooltip("How deep the footing trenches go below the finished pad (m).")]
        public float footingDepth = 0.6f;
        [Tooltip("A concrete slab over the whole pad (else just the footings, with a crawl space).")]
        public bool slab = true;
        public float slabThickness = 0.1f;

        [Header("How exact the digging has to be")]
        [Tooltip("The pad counts as level where the ground is within this much of the pad height (m, up or down).")]
        public float padTolerance = 0.075f;
        [Tooltip("A footing counts as dug where it's within this much of its depth (m; deeper is fine).")]
        public float trenchTolerance = 0.08f;
        [Range(0.5f, 1f)] public float needPad = 0.92f;
        [Range(0.5f, 1f)] public float needTrench = 0.9f;

        [Header("The house")]
        [Tooltip("The house that's set on the finished foundation (stage 4).")]
        public GameObject housePrefab;

        // ------------------------------------------------------------------ worked out from the outline

        /// <summary>Every footing: the outside walls (each edge of the outline), then the inside ones.</summary>
        public List<(Vector2 a, Vector2 b)> Footings()
        {
            var l = new List<(Vector2, Vector2)>();
            if (outline != null) for (int i = 0; i < outline.Length; i++) l.Add((outline[i], outline[(i + 1) % outline.Length]));
            if (innerFootings != null) foreach (var f in innerFootings) l.Add((f.a, f.b));
            return l;
        }

        public float Area
        {
            get
            {
                float a = 0f;
                for (int i = 0; i < outline.Length; i++) { var p = outline[i]; var q = outline[(i + 1) % outline.Length]; a += p.x * q.y - q.x * p.y; }
                return Mathf.Abs(a) * 0.5f;
            }
        }

        public float FootingLength { get { float s = 0f; foreach (var (a, b) in Footings()) s += Vector2.Distance(a, b); return s; } }

        /// <summary>The concrete it takes (m³): the footings, and the slab if it has one.</summary>
        public float ConcreteVolume => FootingLength * footingWidth * footingDepth + (slab ? Area * slabThickness : 0f);

        /// <summary>The footprint's box on the plan (m), footings included.</summary>
        public Rect Bounds
        {
            get
            {
                float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
                foreach (var p in outline) { x0 = Mathf.Min(x0, p.x); z0 = Mathf.Min(z0, p.y); x1 = Mathf.Max(x1, p.x); z1 = Mathf.Max(z1, p.y); }
                float w = footingWidth * 0.5f;
                return Rect.MinMaxRect(x0 - w, z0 - w, x1 + w, z1 + w);
            }
        }

        /// <summary>What the crew charges to put the house up on the finished foundation.</summary>
        public int BuildCost => Mathf.RoundToInt(Area * Mathf.Max(1, floors) * 220f / 100f) * 100;
        /// <summary>What the finished house sells for: well over what it cost you (permit, concrete and the build).</summary>
        public int SalePrice => Mathf.RoundToInt((price + ConcreteVolume * 145f + BuildCost) * 1.7f / 100f) * 100;
        /// <summary>How long the crew takes (seconds).</summary>
        public float BuildSeconds => 60f + 30f * (Mathf.Max(1, floors) - 1);

        public string SizeText { get { var b = Bounds; return $"{b.width - footingWidth:0.#} × {b.height - footingWidth:0.#} m"; } }

        /// <summary>Is a point (plan metres) inside the footprint?</summary>
        public bool Inside(Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = outline.Length - 1; i < outline.Length; j = i++)
            {
                Vector2 a = outline[i], b = outline[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        /// <summary>How far a point (plan metres) is from the nearest footing's centre line.</summary>
        public float FootingDistance(Vector2 p, out int which)
        {
            float best = float.MaxValue; which = -1; int k = 0;
            foreach (var (a, b) in Footings())
            {
                Vector2 ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                float d = Vector2.Distance(p, a + ab * t);
                if (d < best) { best = d; which = k; }
                k++;
            }
            return best;
        }

        // ------------------------------------------------------------------ the plans

        static List<HousePlan> catalog;

        /// <summary>The plans you can build, in order (the assets in Resources/HousePlans, or the built-in ten).</summary>
        public static List<HousePlan> All
        {
            get
            {
                if (catalog != null) return catalog;
                catalog = new List<HousePlan>(Resources.LoadAll<HousePlan>("HousePlans"));
                if (catalog.Count == 0) catalog = Defaults();
                catalog.Sort((a, b) => a.order != b.order ? a.order.CompareTo(b.order) : string.CompareOrdinal(a.title, b.title));
                return catalog;
            }
        }

        public static HousePlan Find(string title) { foreach (var p in All) if (p.title == title) return p; return null; }

        static Vector2[] Box(float w, float d) => new[] { new Vector2(-w / 2f, -d / 2f), new Vector2(w / 2f, -d / 2f), new Vector2(w / 2f, d / 2f), new Vector2(-w / 2f, d / 2f) };
        static Footing F(float ax, float az, float bx, float bz) => new Footing { a = new Vector2(ax, az), b = new Vector2(bx, bz) };

        static HousePlan Make(int order, string title, string shape, int floors, float price, Vector2[] outline, Footing[] inner,
            float width, float depth, bool slab, string description)
        {
            var p = CreateInstance<HousePlan>();
            p.name = title;
            p.order = order; p.title = title; p.shape = shape; p.floors = floors; p.price = price;
            p.outline = outline; p.innerFootings = inner;
            p.footingWidth = width; p.footingDepth = depth; p.slab = slab;
            p.description = description;
            return p;
        }

        /// <summary>The ten plans the game comes with (the editor saves these as the assets).</summary>
        public static List<HousePlan> Defaults() => new()
        {
            Make(1, "Starter Cabin", "Rectangle", 1, 600f, Box(7f, 9f), new Footing[0], 0.45f, 0.6f, true,
                "A one-room cabin on a slab. Small pad, short shallow footings: a good first foundation."),
            Make(2, "Tiny Home", "Rectangle", 1, 450f, Box(5f, 10f), new Footing[0], 0.4f, 0.5f, true,
                "Long and narrow. The smallest pad there is, but keep that long trench straight."),
            Make(3, "Bungalow", "Rectangle", 1, 1100f, Box(10f, 12f), new[] { F(0f, -6f, 0f, 6f) }, 0.5f, 0.7f, true,
                "A family bungalow on a slab, with a load-bearing wall down the middle that needs its own footing."),
            Make(4, "Saltbox", "Rectangle", 2, 1200f, Box(9f, 14f), new[] { F(-4.5f, 0f, 4.5f, 0f) }, 0.5f, 0.7f, false,
                "Two floors at the front, one at the back. Footings only, with a crawl space under the floor."),
            Make(5, "L-Shaped Ranch", "L-shaped", 1, 1800f,
                new[] { new Vector2(-7f, -6f), new Vector2(7f, -6f), new Vector2(7f, 0f), new Vector2(0f, 0f), new Vector2(0f, 6f), new Vector2(-7f, 6f) },
                new[] { F(0f, -6f, 0f, 0f) }, 0.55f, 0.75f, true,
                "A ranch house round two sides of a yard. Mind the inside corner when you level the pad."),
            Make(6, "T-Shaped Farmhouse", "T-shaped", 2, 2100f,
                new[] { new Vector2(-3f, -7f), new Vector2(3f, -7f), new Vector2(3f, 1f), new Vector2(8f, 1f), new Vector2(8f, 7f), new Vector2(-8f, 7f), new Vector2(-8f, 1f), new Vector2(-3f, 1f) },
                new[] { F(-3f, 1f, 3f, 1f) }, 0.55f, 0.8f, false,
                "A long farmhouse at the back with a wing out to the front. Deep footings, crawl space."),
            Make(7, "U-Shaped Ranch", "U-shaped", 1, 2600f,
                new[] { new Vector2(-9f, -7f), new Vector2(-3f, -7f), new Vector2(-3f, 0f), new Vector2(3f, 0f), new Vector2(3f, -7f), new Vector2(9f, -7f), new Vector2(9f, 7f), new Vector2(-9f, 7f) },
                new[] { F(-3f, 0f, -3f, 7f), F(3f, 0f, 3f, 7f) }, 0.55f, 0.75f, true,
                "Two wings round a front courtyard. The biggest pad, and the most trench to dig."),
            Make(8, "Two-Story Colonial", "Rectangle", 2, 2400f, Box(11f, 13f), new[] { F(-5.5f, 0f, 5.5f, 0f), F(0f, -6.5f, 0f, 6.5f) }, 0.65f, 1.0f, false,
                "Two full floors: heavy, so its footings go a metre down, with a cross of inside footings."),
            Make(9, "Split-Level", "Stepped", 2, 2200f,
                new[] { new Vector2(-6f, -8f), new Vector2(4f, -8f), new Vector2(4f, -2f), new Vector2(6f, -2f), new Vector2(6f, 8f), new Vector2(-6f, 8f) },
                new[] { F(-6f, 1f, 6f, 1f) }, 0.6f, 0.9f, true,
                "A house with a step in its front wall and a footing across the middle where the floors split."),
            Make(10, "Duplex", "Rectangle", 2, 2800f, Box(16f, 12f), new[] { F(0f, -6f, 0f, 6f) }, 0.65f, 0.9f, false,
                "Two homes side by side, two floors each, with a footing under the wall between them."),
        };
    }
}
