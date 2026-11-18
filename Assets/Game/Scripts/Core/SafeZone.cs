using System.Collections.Generic;
using UnityEngine;

namespace FPSBase.Core
{
    /// <summary>
    /// A protected area (the outpost). Nobody can build inside it; the HUD shows a banner while you're in it.
    /// The game has no PvP at all, so this mainly keeps the trading area clear of bases.
    /// </summary>
    public class SafeZone : MonoBehaviour
    {
        static readonly List<SafeZone> zones = new();

        public string zoneName = "Outpost";
        public float radius = 30f;

        void OnEnable() => zones.Add(this);
        void OnDisable() => zones.Remove(this);

        public bool Contains(Vector3 p)
        {
            Vector3 d = p - transform.position; d.y = 0f;
            return d.magnitude <= radius;
        }

        public static SafeZone At(Vector3 p)
        {
            foreach (var z in zones) if (z.Contains(p)) return z;
            return null;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 1f, 0.3f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
