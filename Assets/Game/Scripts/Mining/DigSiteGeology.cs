using System.Collections.Generic;
using UnityEngine;

namespace FPSBase.Mining
{
    public enum SoilKind { Topsoil, Clay, Sand, PayGravel, Gravel }

    /// <summary>One layer of native ground: what it's made of, how much gold it carries and how easy it digs.</summary>
    public struct SoilLayer
    {
        public SoilKind kind;
        public string name;
        public float sand, gravel, clay; // fractions 0..1 (the rest is plain dirt)
        public float gradeMul;           // x the claim's gold grade
        public float digEase;            // x the machine's dig rate

        public SoilLayer(SoilKind k, string n, float sa, float gr, float cl, float gm, float ease)
        { kind = k; name = n; sand = sa; gravel = gr; clay = cl; gradeMul = gm; digEase = ease; }
    }

    /// <summary>What a metal detector can pick up.</summary>
    public enum FindKind { Gold, Iron, Coin, Relic }

    public class BuriedFind
    {
        public Vector3 pos;
        public FindKind kind;
        public float grams;   // gold nuggets
        public float value;   // $ for coins and relics
        public string name;
        /// <summary>How strong it rings on a detector (bigger targets show up deeper).</summary>
        public float Size => kind switch { FindKind.Gold => Mathf.Clamp(0.35f + grams * 0.06f, 0.35f, 1.6f), FindKind.Iron => 0.8f, FindKind.Coin => 0.55f, _ => 0.9f };
    }

    /// <summary>
    /// Geology of the dirt field: soil layers (topsoil → clay → pay gravel / sand lenses → bedrock), boulders buried
    /// in them that come out as real physics rocks when the ground around them is dug away, and small things a
    /// metal detector can find (gold nuggets, junk iron, old coins, relics).
    /// </summary>
    public partial class DigSite
    {
        [Header("Soil layers & finds")]
        public float clayMaxThickness = 1.4f;

        /// <summary>World X/Z rectangle covered by the dirt field.</summary>
        public Rect FieldRect => new Rect(origin.x, origin.z, data.size.x, data.size.z);
        [Tooltip("Buried boulders per 1000 m² of field.")]
        public float boulderDensity = 4.5f;
        public int goldNuggets = 160, junkIron = 220, oldCoins = 30, relics = 8;

        static readonly SoilLayer LTopsoil = new(SoilKind.Topsoil, "Topsoil", 0.2f, 0.05f, 0.1f, 1f, 1f);
        static readonly SoilLayer LClay = new(SoilKind.Clay, "Clay", 0.1f, 0.03f, 0.75f, 0.3f, 0.6f);
        static readonly SoilLayer LSand = new(SoilKind.Sand, "Sand", 0.72f, 0.12f, 0.04f, 0.6f, 1.3f);
        static readonly SoilLayer LPay = new(SoilKind.PayGravel, "Pay gravel", 0.25f, 0.58f, 0.05f, 1f, 0.85f);
        static readonly SoilLayer LGravel = new(SoilKind.Gravel, "Gravel", 0.3f, 0.5f, 0.08f, 1f, 0.85f);

        float ClayThickness(float wx, float wz)
        {
            float n = Mathf.PerlinNoise(wx * 0.035f + seed * 0.21f, wz * 0.035f + 40f);
            return Mathf.Clamp01((n - 0.3f) / 0.5f) * clayMaxThickness;
        }

        /// <summary>The native layer at a world x/z and depth below the original surface.</summary>
        public SoilLayer LayerAt(float wx, float wz, float depth)
        {
            if (depth < overburdenDepth) return LTopsoil;
            if (depth < overburdenDepth + ClayThickness(wx, wz)) return LClay;
            float sandN = Mathf.PerlinNoise(wx * 0.045f + 11f, wz * 0.045f - 3f + seed * 0.05f);
            if (sandN > 0.6f && depth < payDepth - 0.8f) return LSand;
            return InClaim(wx, wz) ? LPay : LGravel;
        }

        /// <summary>What a bucket tip at this point is in: the loose dirt on top, or the native layer.</summary>
        public string GroundNameAt(Vector3 p, out float digEase)
        {
            digEase = 1f;
            if (!Contains(p)) return "—";
            int x = Mathf.Clamp(Mathf.RoundToInt((p.x - origin.x) / cell), 1, res - 2);
            int z = Mathf.Clamp(Mathf.RoundToInt((p.z - origin.z) / cell), 1, res - 2);
            if (p.y > Surf(x, z) + 0.05f) return "air";
            if (loose[z, x] > 0.05f && p.y > native[z, x] - 0.05f)
            {
                var l = LooseAt(x, z);
                return "loose " + l.Kind.ToLowerInvariant();
            }
            if (native[z, x] <= Floor(x, z) + 0.05f) { digEase = 0f; return InClaim(p.x, p.z) ? "Bedrock (can't dig)" : "Hardpan (can't dig deeper here)"; }
            var layer = LayerAt(p.x, p.z, orig[z, x] - p.y);
            digEase = layer.digEase;
            return layer.name;
        }

        MaterialLoad LooseAt(int x, int z)
        {
            float v = loose[z, x] * cellArea;
            return new MaterialLoad(v, looseGold[z, x]) { sand = looseSand[z, x], gravel = looseGravel[z, x], clay = looseClay[z, x] };
        }

        void ClearLoose(int x, int z)
        {
            loose[z, x] = 0f; looseGold[z, x] = 0f;
            looseSand[z, x] = 0f; looseGravel[z, x] = 0f; looseClay[z, x] = 0f;
            packed[z, x] = 0f;
        }

        /// <summary>All the loose dirt within `radius` of a point, with its make-up.</summary>
        public MaterialLoad LooseLoad(Vector3 center, float radius)
        {
            var sum = new MaterialLoad();
            Range(center, radius, out int x0, out int z0, out int x1, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float ddx = origin.x + x * cell - center.x, ddz = origin.z + z * cell - center.z;
                    if (ddx * ddx + ddz * ddz > radius * radius || loose[z, x] <= 0f) continue;
                    sum.Add(LooseAt(x, z));
                }
            return sum;
        }

        /// <summary>
        /// Dig with a bucket tip at `tip`: lowers the dirt around the tip toward the tip's height, removing at most
        /// `maxVolume` m^3 (loose dirt first; clay digs slower, sand faster), and returns what was removed.
        /// Gold nuggets in the dirt go into the bucket with it.
        /// </summary>
        public MaterialLoad Dig(Vector3 tip, float radius, float maxVolume)
        {
            GroundNameAt(tip, out float ease);
            if (ease <= 0f) ease = 1f; // the floor clamp already stops it
            var got = DigCore(tip, radius, maxVolume * ease);
            if (got.IsEmpty) return got;
            finds.Clear();
            TakeExposed(tip, Mathf.Max(radius, cell) + 0.3f, finds);
            foreach (var f in finds) if (f.kind == FindKind.Gold) got.goldGrams += f.grams;
            CheckBoulders(tip, radius + 1f);
            return got;
        }

        readonly List<BuriedFind> finds = new();

        /// <summary>After a blade or ripper moved dirt: free boulders, and nuggets end up in the loose dirt.</summary>
        void AfterGroundMoved(Vector3 center, float radius, Vector3 goldTo)
        {
            CheckBoulders(center, radius + 1f);
            finds.Clear();
            TakeExposed(center, radius, finds);
            int gx = Mathf.Clamp(Mathf.RoundToInt((goldTo.x - origin.x) / cell), 1, res - 2);
            int gz = Mathf.Clamp(Mathf.RoundToInt((goldTo.z - origin.z) / cell), 1, res - 2);
            foreach (var f in finds) if (f.kind == FindKind.Gold) looseGold[gz, gx] += f.grams;
        }

        // ------------------------------------------------------------------ boulders

        struct Buried { public Vector3 pos; public float radius; public bool exposed; }
        readonly List<Buried> buried = new();
        readonly Dictionary<long, List<int>> boulderGrid = new();
        readonly Dictionary<long, List<BuriedFind>> findGrid = new();
        const float GridSize = 8f;

        long Key(float wx, float wz) => ((long)Mathf.FloorToInt(wx / GridSize) << 32) ^ (uint)Mathf.FloorToInt(wz / GridSize);

        void GenerateGeology()
        {
            var rng = new System.Random(seed * 31 + 7);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float size = data.size.x, m = edgeMargin + 2f;

            int nb = Mathf.RoundToInt(size * size / 1000f * boulderDensity);
            for (int i = 0; i < nb; i++)
            {
                float wx = origin.x + R(m, size - m), wz = origin.z + R(m, size - m);
                float r = Mathf.Lerp(0.22f, 0.85f, Mathf.Pow(R(0f, 1f), 1.6f));
                bool inClaim = InClaim(wx, wz);
                float floorDepth = Mathf.Min(inClaim ? bedrockDepth : offClaimDigDepth, payDepth);
                // most rocks sit in the gravel, some in the topsoil
                float depth = R(0f, 1f) < 0.65f ? R(overburdenDepth, floorDepth) : R(0.35f, overburdenDepth);
                depth = Mathf.Clamp(depth, r + 0.15f, floorDepth - r * 0.4f);
                float gy = OrigAt(wx, wz);
                buried.Add(new Buried { pos = new Vector3(wx, gy - depth, wz), radius = r });
                Add(boulderGrid, Key(wx, wz), buried.Count - 1);
            }

            void Place(FindKind kind, float wx, float wz, float depth, float grams, float value, string name)
            {
                var f = new BuriedFind { pos = new Vector3(wx, OrigAt(wx, wz) - depth, wz), kind = kind, grams = grams, value = value, name = name };
                Add(findGrid, Key(wx, wz), f);
            }
            Rect c = claimRect;
            int nuggets = nuggetCount >= 0 ? nuggetCount : goldNuggets;
            for (int i = 0; i < nuggets; i++)
            {
                bool claim = i < nuggets * 0.85f && claimRect.width > 1f;
                float wx = claim ? R(c.xMin, c.xMax) : origin.x + R(m, size - m);
                float wz = claim ? R(c.yMin, c.yMax) : origin.z + R(m, size - m);
                float g = 0.3f * Mathf.Exp(R(0f, 2.8f));
                if (R(0f, 1f) < 0.04f) g *= 5f; // the odd big one
                if (!claim) g *= 0.5f;
                Place(FindKind.Gold, wx, wz, R(0.08f, 0.7f), g, 0f, g >= 10f ? "big gold nugget" : g >= 2f ? "gold nugget" : "small gold picker");
            }
            string[] junk = { "rusty nail", "bottle cap", "horseshoe", "old bolt", "tin can", "bit of barbed wire", "pull tab" };
            for (int i = 0; i < junkIron; i++)
                Place(FindKind.Iron, origin.x + R(m, size - m), origin.z + R(m, size - m), R(0.04f, 0.5f), 0f, 0f, junk[rng.Next(junk.Length)]);
            string[] coins = { "old silver coin", "1890s penny", "trade token", "silver half dollar", "brass button" };
            for (int i = 0; i < oldCoins; i++)
                Place(FindKind.Coin, origin.x + R(m, size - m), origin.z + R(m, size - m), R(0.06f, 0.4f), 0f, Mathf.Round(R(30f, 420f)), coins[rng.Next(coins.Length)]);
            string[] rel = { "miner's pocket watch", "gold ring", "old pistol", "brass lantern", "assay office token" };
            for (int i = 0; i < relics; i++)
                Place(FindKind.Relic, origin.x + R(m, size - m), origin.z + R(m, size - m), R(0.15f, 0.6f), 0f, Mathf.Round(R(700f, 3200f)), rel[rng.Next(rel.Length)]);
        }

        static void Add<T>(Dictionary<long, List<T>> grid, long k, T v)
        {
            if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<T>();
            l.Add(v);
        }

        float OrigAt(float wx, float wz)
        {
            int x = Mathf.Clamp(Mathf.RoundToInt((wx - origin.x) / cell), 0, res - 1);
            int z = Mathf.Clamp(Mathf.RoundToInt((wz - origin.z) / cell), 0, res - 1);
            return orig[z, x];
        }

        /// <summary>Boulders whose top half has been dug clear come out as real rocks.</summary>
        void CheckBoulders(Vector3 c, float r)
        {
            for (float gx = c.x - r; gx <= c.x + r + GridSize; gx += GridSize)
                for (float gz = c.z - r; gz <= c.z + r + GridSize; gz += GridSize)
                {
                    if (!boulderGrid.TryGetValue(Key(gx, gz), out var list)) continue;
                    foreach (int i in list)
                    {
                        var b = buried[i];
                        if (b.exposed) continue;
                        if (SurfaceY(b.pos) > b.pos.y + b.radius * 0.35f) continue;
                        b.exposed = true; buried[i] = b;
                        Boulder.Spawn(b.pos, b.radius, true);
                    }
                }
        }

        /// <summary>Test helper: the nearest still-buried boulder to a point.</summary>
        public bool NearestBuriedBoulder(Vector3 p, out Vector3 pos, out float radius)
        {
            pos = default; radius = 0f; float best = float.MaxValue;
            foreach (var b in buried)
            {
                if (b.exposed) continue;
                float d = (b.pos - p).sqrMagnitude;
                if (d < best) { best = d; pos = b.pos; radius = b.radius; }
            }
            return best < float.MaxValue;
        }

        // ------------------------------------------------------------------ detector finds

        /// <summary>Still-buried finds within `radius` (horizontal) of a point.</summary>
        public void FindsNear(Vector3 p, float radius, List<BuriedFind> into)
        {
            into.Clear();
            for (float gx = p.x - radius; gx <= p.x + radius + GridSize; gx += GridSize)
                for (float gz = p.z - radius; gz <= p.z + radius + GridSize; gz += GridSize)
                {
                    if (!findGrid.TryGetValue(Key(gx, gz), out var list)) continue;
                    foreach (var f in list)
                    {
                        float dx = f.pos.x - p.x, dz = f.pos.z - p.z;
                        if (dx * dx + dz * dz <= radius * radius && !into.Contains(f)) into.Add(f);
                    }
                }
        }

        readonly List<BuriedFind> tmpFinds = new();

        /// <summary>Takes finds near a point that the dirt over them has been dug away from.</summary>
        public void TakeExposed(Vector3 c, float radius, List<BuriedFind> into)
        {
            FindsNear(c, radius, tmpFinds);
            foreach (var f in tmpFinds)
            {
                if (SurfaceY(f.pos) > f.pos.y + 0.03f) continue;
                if (findGrid.TryGetValue(Key(f.pos.x, f.pos.z), out var list)) list.Remove(f);
                into.Add(f);
            }
        }

        /// <summary>A shovelful by hand: digs a small hole (spoil goes beside it) and returns what turned up.</summary>
        public void HandDig(Vector3 at, Vector3 spoilTo, List<BuriedFind> found)
        {
            found.Clear();
            if (!Contains(at)) return;
            Vector3 tip = at; tip.y = SurfaceY(at) - 0.14f;
            var dirt = DigCore(tip, 0.38f, 0.05f);
            if (!dirt.IsEmpty) { spoilTo.y = SurfaceY(spoilTo); Deposit(spoilTo, dirt); }
            TakeExposed(at, 0.5f, found);
            CheckBoulders(at, 1.2f);
        }

        /// <summary>Total buried finds left (for the HUD / tests).</summary>
        public int FindsLeft(FindKind kind)
        {
            int n = 0;
            foreach (var l in findGrid.Values) foreach (var f in l) if (f.kind == kind) n++;
            return n;
        }

        /// <summary>Test helper: the nearest still-buried find of a kind.</summary>
        public BuriedFind NearestFind(Vector3 p, FindKind kind)
        {
            BuriedFind best = null; float bd = float.MaxValue;
            foreach (var l in findGrid.Values)
                foreach (var f in l)
                {
                    if (f.kind != kind) continue;
                    float d = (f.pos - p).sqrMagnitude;
                    if (d < bd) { bd = d; best = f; }
                }
            return best;
        }
    }
}
