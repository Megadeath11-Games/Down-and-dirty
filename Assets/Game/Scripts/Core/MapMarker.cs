using System.Collections.Generic;
using UnityEngine;

namespace FPSBase.Core
{
    /// <summary>A named place shown on the compass and the map (M), and a fast-travel spot.</summary>
    public class MapMarker : MonoBehaviour
    {
        static readonly List<MapMarker> all = new();
        public static IReadOnlyList<MapMarker> All => all;

        public string label = "Place";
        public Color color = Color.white;
        [Tooltip("Can travel here from the map when on foot.")]
        public bool travel = true;
        [Tooltip("Where the player lands, relative to the marker.")]
        public Vector3 arriveOffset = new Vector3(0f, 0f, -6f);

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);
    }
}
