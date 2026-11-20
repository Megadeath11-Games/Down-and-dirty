using System.Collections.Generic;

namespace FPSBase.Mining
{
    /// <summary>
    /// Scratch digging (the machines at work behind the main menu): the real ground, the real dirt, but when it's
    /// over every square they touched goes back to how it was, so the game you then start is untouched. While it's
    /// on, digging uncovers no gold or boulders (they stay buried for the game).
    /// </summary>
    public partial class DigSite
    {
        struct ScratchCell { public float nat, lo, go, sa, gr, cl, pk; public bool tr; }
        Dictionary<int, ScratchCell> scratchWas;
        HashSet<int> scratchEver, scratchTouched;

        /// <summary>How big the ground is (m): from its corner at transform.position.</summary>
        public UnityEngine.Vector3 Size => data != null ? data.size : UnityEngine.Vector3.zero;

        /// <summary>True between <see cref="BeginScratch"/> and <see cref="EndScratch"/>.</summary>
        public bool Scratching => scratchWas != null;

        UnityEngine.Vector3 scratchC; float scratchR;
        /// <summary>In the scratch work area (only there is the ground put back and nothing uncovered).</summary>
        public bool InScratch(UnityEngine.Vector3 p) { if (scratchWas == null) return false; float dx = p.x - scratchC.x, dz = p.z - scratchC.z; return dx * dx + dz * dz < scratchR * scratchR; }

        /// <summary>Scratch work within `radius` m of `center` from now on.</summary>
        public void BeginScratch(UnityEngine.Vector3 center, float radius)
        {
            scratchC = center; scratchR = radius;
            if (scratchWas != null || orig == null) return;
            scratchWas = new Dictionary<int, ScratchCell>();
            scratchEver = new HashSet<int>(everChanged);
            scratchTouched = new HashSet<int>();
            // the squares already changed before (the scratch work may change them again): what they were
            foreach (int i in everChanged)
            {
                int x = i % res, z = i / res;
                scratchWas[i] = new ScratchCell
                {
                    nat = native[z, x], lo = loose[z, x], go = looseGold[z, x], sa = looseSand[z, x], gr = looseGravel[z, x],
                    cl = looseClay[z, x], pk = packed[z, x], tr = trampled[z, x]
                };
            }
        }

        /// <summary>How many squares within `r` m of `c` aren't the untouched ground (dug, filled or with loose dirt on).</summary>
        public int CountWorkedNear(UnityEngine.Vector3 c, float r)
        {
            int n = 0;
            int x0 = UnityEngine.Mathf.Clamp((int)((c.x - r - origin.x) / cell), 0, res - 1), x1 = UnityEngine.Mathf.Clamp((int)((c.x + r - origin.x) / cell), 0, res - 1);
            int z0 = UnityEngine.Mathf.Clamp((int)((c.z - r - origin.z) / cell), 0, res - 1), z1 = UnityEngine.Mathf.Clamp((int)((c.z + r - origin.z) / cell), 0, res - 1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    if (UnityEngine.Mathf.Abs(native[z, x] - orig[z, x]) > 1e-4f || loose[z, x] > 1e-4f) n++;
            return n;
        }

        /// <summary>Every square changed since <see cref="BeginScratch"/> back as it was. Returns how many.</summary>
        public int EndScratch()
        {
            if (scratchWas == null) return 0;
            var changed = new List<int>();
            // (only what this game's machines changed: ground a friend's game sent meanwhile stays)
            // (and only in the work area: anything else going on meanwhile stays as it is)
            foreach (int i in scratchTouched) if (InScratch(new UnityEngine.Vector3(origin.x + (i % res) * cell, 0f, origin.z + (i / res) * cell))) changed.Add(i);
            var undo = new HashSet<int>();
            applyingNet = true; // (not sent to anyone, not remembered as changed)
            try
            {
                foreach (int i in changed)
                {
                    int x = i % res, z = i / res;
                    if (scratchWas.TryGetValue(i, out var c))
                    {
                        if (native[z, x] == c.nat && loose[z, x] == c.lo && packed[z, x] == c.pk) continue;
                        native[z, x] = c.nat; loose[z, x] = c.lo; looseGold[z, x] = c.go; looseSand[z, x] = c.sa;
                        looseGravel[z, x] = c.gr; looseClay[z, x] = c.cl; packed[z, x] = c.pk; trampled[z, x] = c.tr;
                    }
                    else
                    {
                        native[z, x] = orig[z, x]; loose[z, x] = 0f; looseGold[z, x] = 0f; looseSand[z, x] = 0f;
                        looseGravel[z, x] = 0f; looseClay[z, x] = 0f; packed[z, x] = 0f; trampled[z, x] = false;
                    }
                    undo.Add(i);
                    MarkHeights(x, z);
                }
            }
            finally { applyingNet = false; }
            foreach (int i in undo) if (!scratchEver.Contains(i)) { everChanged.Remove(i); netChanged.Remove(i); }
            crumbles.RemoveAll(c => undo.Contains(c.cell));
            scratchWas = null; scratchEver = null; scratchTouched = null;
            return undo.Count;
        }
    }
}
