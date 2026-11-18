using FPSBase.Mining;
using UnityEngine;

namespace FPSBase.Vehicles
{
    /// <summary>Finds a clear patch of ground big enough for a machine (deliveries, tows, unsticking).</summary>
    public static class VehicleSpot
    {
        static readonly Collider[] hits = new Collider[64];

        /// <summary>Local-space bounds of everything that renders on `root` (its footprint and height).</summary>
        public static Bounds LocalBounds(Transform root)
        {
            bool any = false; var b = new Bounds(Vector3.zero, Vector3.zero);
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
                var wb = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = root.InverseTransformPoint(new Vector3((i & 1) == 0 ? wb.min.x : wb.max.x, (i & 2) == 0 ? wb.min.y : wb.max.y, (i & 4) == 0 ? wb.min.z : wb.max.z));
                    if (!any) { b = new Bounds(c, Vector3.zero); any = true; } else b.Encapsulate(c);
                }
            }
            if (!any) b = new Bounds(new Vector3(0f, 1f, 0f), new Vector3(2.5f, 2f, 4f));
            return b;
        }

        /// <summary>Would a machine with local bounds `lb` fit standing at ground point `at` facing `yaw`?</summary>
        public static bool IsClear(Vector3 at, float yaw, Bounds lb, Transform ignore, float margin = 0.4f)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 half = new Vector3(lb.extents.x + margin, Mathf.Max(0.3f, lb.extents.y * 0.5f), lb.extents.z + margin);
            // a box from a little above the ground up through the machine's height
            Vector3 c = at + rot * new Vector3(lb.center.x, 0f, lb.center.z) + Vector3.up * (0.4f + half.y);
            int n = Physics.OverlapBoxNonAlloc(c, half, hits, rot, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var h = hits[i];
                if (h == null || h is TerrainCollider) continue;
                if (ignore != null && h.transform.IsChildOf(ignore)) continue;
                if (h.GetComponentInParent<DirtPile>() != null) continue;
                if (h is CharacterController) continue;
                return false; // another machine, a rock, a building, a fence...
            }
            // not on a steep slope or into a pit: the corners shouldn't differ much in height
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                Vector3 k = at + rot * new Vector3(lb.center.x + ((i & 1) == 0 ? -1f : 1f) * lb.extents.x, 0f, lb.center.z + ((i & 2) == 0 ? -1f : 1f) * lb.extents.z);
                float y = VehicleDealer.GroundAt(k).y;
                lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
            }
            return hi - lo < Mathf.Max(0.8f, lb.size.z * 0.25f);
        }

        /// <summary>The clear spot nearest `near` (searching rings outward), on the ground. Falls back to `near`.</summary>
        public static Vector3 FindFree(Vector3 near, float yaw, Bounds lb, Transform ignore, float maxRadius = 40f)
        {
            Vector3 g = VehicleDealer.GroundAt(near);
            if (IsClear(g, yaw, lb, ignore)) return g;
            float step = Mathf.Max(1.5f, Mathf.Min(lb.size.x, lb.size.z) * 0.6f);
            for (float rad = step; rad <= maxRadius; rad += step)
            {
                int count = Mathf.Max(8, Mathf.CeilToInt(2f * Mathf.PI * rad / step));
                for (int i = 0; i < count; i++)
                {
                    float a = i / (float)count * Mathf.PI * 2f;
                    Vector3 p = VehicleDealer.GroundAt(near + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * rad);
                    if (IsClear(p, yaw, lb, ignore)) return p;
                }
            }
            return g;
        }
    }
}
