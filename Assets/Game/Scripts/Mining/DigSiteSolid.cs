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
