using System.Collections.Generic;
using UnityEngine;

namespace FPSBase.City
{
    /// <summary>A street in town (for the map).</summary>
    public class Street : MonoBehaviour
    {
        static readonly List<Street> all = new();
        public static IReadOnlyList<Street> All => all;
        public Rect rect;
        public string streetName;
        public Rect Rect => rect;
        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);
    }
}
