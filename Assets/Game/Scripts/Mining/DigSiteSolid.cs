using System.Collections.Generic;
using UnityEngine;

namespace FPSBase.Mining
{
    /// <summary>
    /// Solid things standing on the ground (concrete wall blocks): the squares under them hold up to their top, so
    /// loose dirt can't slump into them and a cut bank behind them doesn't cave in past them. A retaining wall.
    /// </summary>
    public partial class DigSite
    {
        readonly Dictionary<int, float> solid = new();
        readonly Dictionary<object, (List<int> cells, float top)> solidOwners = new();

        /// <summary>How high the ground stands at x,z for dirt next to it: the surface, or the top of a block on it.</summary>
        float Held(int x, int z)
        {
            float s = native[z, x] + loose[z, x];
            if (solid.Count == 0) return s;
            return solid.TryGetValue(z * res + x, out float t) && t > s ? t : s;
        }

        /// <summary>The squares inside `b` (across the ground) that `inside` says are covered.</summary>
        public void CellsUnder(Bounds b, System.Func<Vector3, bool> inside, List<int> into)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt((b.min.x - origin.x) / cell), 1, res - 2), x1 = Mathf.Clamp(Mathf.CeilToInt((b.max.x - origin.x) / cell), 1, res - 2);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((b.min.z - origin.z) / cell), 1, res - 2), z1 = Mathf.Clamp(Mathf.CeilToInt((b.max.z - origin.z) / cell), 1, res - 2);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    if (inside(new Vector3(origin.x + x * cell, b.center.y, origin.z + z * cell))) into.Add(z * res + x);
        }

        public void AddSolid(object owner, List<int> cells, float topWorldY)
        {
            RemoveSolid(owner);
            float top = topWorldY; // (the heights are world heights)
            solidOwners[owner] = (new List<int>(cells), top);
            foreach (int i in cells) { solid[i] = solid.TryGetValue(i, out float t) ? Mathf.Max(t, top) : top; Around(i); }
        }

        public void RemoveSolid(object owner)
        {
            if (!solidOwners.TryGetValue(owner, out var e)) return;
            solidOwners.Remove(owner);
            foreach (int i in e.cells) solid.Remove(i);
            // (another block on the same squares still holds them)
            foreach (var kv in solidOwners) foreach (int i in kv.Value.cells) if (e.cells.Contains(i)) solid[i] = solid.TryGetValue(i, out float t) ? Mathf.Max(t, kv.Value.top) : kv.Value.top;
            foreach (int i in e.cells) Around(i);
        }

        /// <summary>
        /// Dirt landing on a square a block stands on (poured on it, a clod rolled onto it) doesn't go into the ground
        /// under the block: it slides off onto the nearest square beside it. Returns false if x,z is open ground.
        /// </summary>
        bool OffBlock(ref int x, ref int z)
        {
            if (solid.Count == 0 || !solid.ContainsKey(z * res + x)) return false;
            for (int r = 1; r <= 24; r++)
            {
                int bx = -1, bz = -1; float bd = float.MaxValue;
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                        int nx = x + dx, nz = z + dz;
                        if (nx < 1 || nz < 1 || nx > res - 2 || nz > res - 2 || solid.ContainsKey(nz * res + nx)) continue;
                        float d = dx * dx + dz * dz + Random.value * 0.5f; // (either side of a wall, about evenly)
                        if (d < bd) { bd = d; bx = nx; bz = nz; }
                    }
                if (bx >= 0) { x = bx; z = bz; return true; }
            }
            return false;
        }

        /// <summary>
        /// A block set down on loose dirt presses into it with its weight: the dirt under it packs (a heavy barrier
        /// block much more than a half block) and some squeezes out round its edges, so it settles in instead of
        /// sitting on the fluffy top like it weighs nothing. Returns how far the ground under it went down (m, average).
        /// </summary>
        public float PressUnder(List<int> cells, float kg)
        {
            if (cells == null || cells.Count == 0) return 0f;
            float area = cells.Count * cellArea;
            float want = Mathf.Clamp01(kg / Mathf.Max(0.05f, area) / 3000f); // (kg per m² of ground it stands on)
            float before = 0f, after = 0f;
            foreach (int i in cells) before += loose[i / res, i % res];
            foreach (int i in cells)
            {
                int x = i % res, z = i / res;
                float l = loose[z, x];
                // squeezed out from under it: what's deeper than a thin skin, by how heavy it is
                float out_ = Mathf.Max(0f, l - 0.04f) * 0.35f * want;
                if (out_ > 0.002f)
                {
                    float k = out_ / l;
                    var m = new MaterialLoad(out_ * cellArea, looseGold[z, x] * k);
                    m.sand = looseSand[z, x] * k; m.gravel = looseGravel[z, x] * k; m.clay = looseClay[z, x] * k;
                    loose[z, x] -= out_; looseGold[z, x] -= m.goldGrams;
                    looseSand[z, x] -= m.sand; looseGravel[z, x] -= m.gravel; looseClay[z, x] -= m.clay;
                    Touch(x, z);
                    AddLoose(x, z, m); // (off the block: onto the squares round it)
                }
                for (int k = 0; k < 8; k++) PackCell(x, z, want);
            }
            foreach (int i in cells) after += loose[i / res, i % res];
            return (before - after) / cells.Count;
        }

        /// <summary>The mean height of the ground over these squares (world m).</summary>
        public float MeanHeight(List<int> cells)
        {
            if (cells == null || cells.Count == 0) return 0f;
            float sum = 0f;
            foreach (int i in cells) sum += Surf(i % res, i / res);
            return sum / cells.Count;
        }

        /// <summary>
        /// A flat bed for a block set down level: the ground over these squares is levelled to `y` (the high spots
        /// cut down and the dirt put in the low ones, so with `y` their mean height nothing's gained or lost).
        /// </summary>
        public void BedUnder(List<int> cells, float y, bool topUp = false)
        {
            if (cells == null || cells.Count == 0) return;
            var cut = new MaterialLoad();
            foreach (int i in cells)
            {
                int x = i % res, z = i / res;
                float s = Surf(x, z);
                if (s > y + 0.002f) { cut.Add(Remove(x, z, s - y, float.MinValue)); Touch(x, z); }
            }
            foreach (int i in cells)
            {
                int x = i % res, z = i / res;
                float s = Surf(x, z);
                if (s >= y - 0.002f) continue;
                // (levelled to the block beside it rather than to its own mean: any hollow left is packed up with fresh dirt)
                if (cut.volume < (y - s) * cellArea - 1e-5f && topUp) cut.Add(new MaterialLoad((y - s) * cellArea - cut.volume, 0f));
                if (cut.volume <= 1e-5f) continue;
                var part = cut.Take(Mathf.Min(cut.volume, (y - s) * cellArea));
                part.rock = 0f;
                float v = part.volume;
                if (v <= 0f) continue;
                float was = loose[z, x];
                loose[z, x] += v / cellArea;
                packed[z, x] *= was / loose[z, x];
                looseGold[z, x] += part.goldGrams;
                looseSand[z, x] += part.sand; looseGravel[z, x] += part.gravel; looseClay[z, x] += part.clay;
                Touch(x, z);
            }
            // (any left over, rounding: round the edge)
            if (cut.volume > 1e-4f && cells.Count > 0) { int i = cells[0]; AddLoose(i % res - 1, i / res - 1, cut); }
        }

        // the dirt round a block that came or went looks again at whether it should slide
        void Around(int i)
        {
            int x = i % res, z = i / res;
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) Queue(x + dx, z + dz);
        }

        /// <summary>For the tests: is square x,z (world point) held by a block, and to what height?</summary>
        public bool HeldByBlock(Vector3 p, out float topWorldY)
        {
            int x = Mathf.RoundToInt((p.x - origin.x) / cell), z = Mathf.RoundToInt((p.z - origin.z) / cell);
            topWorldY = 0f;
            if (x < 0 || z < 0 || x >= res || z >= res || !solid.TryGetValue(z * res + x, out float t)) return false;
            topWorldY = t;
            return true;
        }
    }
}
