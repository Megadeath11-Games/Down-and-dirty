using System.Collections.Generic;
using UnityEngine;

namespace FPSBase.Vehicles
{
    public enum HitchType { Pintle, FifthWheel, Gooseneck }

    /// <summary>
    /// A tow point on a truck: a pintle hook on a pickup's rear bumper (takes a trailer's lunette ring) or a semi
    /// tractor's fifth wheel (takes a lowboy's gooseneck kingpin). The transform is the coupling point.
    /// </summary>
    public class Hitch : MonoBehaviour
    {
        public HitchType type = HitchType.Pintle;
        /// <summary>The trailer coupled here (null = free).</summary>
        public Trailer Attached { get; set; }

        static readonly List<Hitch> all = new();
        public static IReadOnlyList<Hitch> All => all;
        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        public VehicleBase Vehicle => GetComponentInParent<VehicleBase>();
        public Rigidbody Body => GetComponentInParent<Rigidbody>();
    }
}
