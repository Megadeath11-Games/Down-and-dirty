using FPSBase.Vehicles;
using UnityEngine;

namespace FPSBase.City
{
    /// <summary>
    /// Something on a customer's lot a machine can wreck: the house, the mailbox, a fence, a hedge, the shed, a
    /// tree, the porch. Hit it with a machine (or a trailer) and the owner takes a share of the job's bill to fix it
    /// (or, with no job there, bills you for it). Mailboxes and fence sections get knocked over.
    /// </summary>
    public class Damageable : MonoBehaviour
    {
        public House house;
        [Tooltip("Unique on its lot (the same on every game).")]
        public string id = "mailbox";
        [Tooltip("What the owner calls it: \"mailbox\", \"fence\", \"house\".")]
        public string what = "mailbox";
        [Tooltip("Share of the job's bill it costs.")]
        public float penalty = 0.1f;
        [Tooltip("What they bill you for it when there's no job here.")]
        public float fine = 250f;
        [Tooltip("Gets knocked over when hit (this transform tips).")]
        public bool tipsOver;
        [Tooltip("What tips over (the whole mailbox, a fence section); this object if empty.")]
        public Transform tipRoot;
        [Tooltip("How fast a machine has to be going into it to do damage (m/s).")]
        public float minSpeed = 0.9f;

        public bool Broken { get; private set; }
        float cool;

        void OnCollisionEnter(Collision c) => Hit(c);
        void OnCollisionStay(Collision c) { if (Time.time > cool) Hit(c); }

        void Hit(Collision c)
        {
            if (Time.time < cool || house == null) return;
            var rb = c.rigidbody;
            if (rb == null || rb.isKinematic) return;
            var v = rb.GetComponentInParent<VehicleBase>();
            var t = v == null ? rb.GetComponentInParent<Trailer>() : null;
            if (v == null && t == null) return;
            if (v != null && v.NetRemote) return; // a friend's machine: their game reports it
            if (c.relativeVelocity.magnitude < minSpeed && c.impulse.magnitude / Time.fixedDeltaTime < rb.mass * 2.5f) return;
            cool = Time.time + 4f;
            Jobs.Instance?.Damage(house, this, v != null ? v.displayName : t.displayName);
        }

        /// <summary>Knocked over (shown on every game).</summary>
        public void ShowBroken()
        {
            if (Broken) return;
            Broken = true;
            if (!tipsOver) return;
            // lean it over away from the street
            var r = tipRoot != null ? tipRoot : transform;
            foreach (var d in r.GetComponentsInChildren<Damageable>()) d.Broken = true;
            r.RotateAround(r.position, r.right, 72f);
            foreach (var col in r.GetComponentsInChildren<Collider>()) col.enabled = false;
        }
    }
}
