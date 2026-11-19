using UnityEngine;

namespace FPSBase.Mining
{
    /// <summary>Checks on finished ground for the customers in town: is the driveway gravelled, is the ditch dug.</summary>
    public partial class DigSite
    {
        /// <summary>
        /// Gravel on an area (world x/z rect): `coverage` = share of the area with at least `minThick` m of loose
        /// gravel on top, `avgThick` = average gravel thickness, `bump` = how far the highest spot stands above the
        /// average surface (a pile that wasn't spread).
        /// </summary>
        public void SurveyGravel(Rect r, float minThick, out float coverage, out float avgThick, out float bump)
        {
            coverage = avgThick = bump = 0f;
            if (data == null) return;
            int x0 = Mathf.Clamp(Mathf.CeilToInt((r.xMin - origin.x) / cell), 1, res - 2), x1 = Mathf.Clamp(Mathf.FloorToInt((r.xMax - origin.x) / cell), 1, res - 2);
            int z0 = Mathf.Clamp(Mathf.CeilToInt((r.yMin - origin.z) / cell), 1, res - 2), z1 = Mathf.Clamp(Mathf.FloorToInt((r.yMax - origin.z) / cell), 1, res - 2);
            int n = 0, covered = 0;
            float sumT = 0f, sumS = 0f, maxS = float.MinValue;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float t = looseGravel[z, x] / cellArea;
                    n++; sumT += t;
                    if (t >= minThick) covered++;
                    float s = Surf(x, z);
                    sumS += s; maxS = Mathf.Max(maxS, s);
                }
            if (n == 0) return;
            coverage = covered / (float)n;
            avgThick = sumT / n;
            bump = maxS - sumS / n;
        }

        /// <summary>How far the surface is below the original ground at a point (a hole / trench is positive).</summary>
        public float CutDepth(Vector3 p)
        {
            if (data == null) return 0f;
            int x = Mathf.Clamp(Mathf.RoundToInt((p.x - origin.x) / cell), 0, res - 1);
            int z = Mathf.Clamp(Mathf.RoundToInt((p.z - origin.z) / cell), 0, res - 1);
            return orig[z, x] - Surf(x, z);
        }

        /// <summary>Original ground height (before anyone dug) at a point.</summary>
        public float OriginalY(Vector3 p)
        {
            if (data == null) return p.y;
            int x = Mathf.Clamp(Mathf.RoundToInt((p.x - origin.x) / cell), 0, res - 1);
            int z = Mathf.Clamp(Mathf.RoundToInt((p.z - origin.z) / cell), 0, res - 1);
            return orig[z, x];
        }

        /// <summary>Scuff the grass off a rectangle (a worn dirt driveway).</summary>
        public void TrampleRect(Rect r)
        {
            for (float x = r.xMin + 0.25f; x < r.xMax; x += cell)
                for (float z = r.yMin + 0.25f; z < r.yMax; z += cell)
                    Trample(new Vector3(x, 0f, z), cell * 0.6f);
        }
    }
}
