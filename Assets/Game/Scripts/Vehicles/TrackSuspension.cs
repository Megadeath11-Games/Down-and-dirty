using System.Collections.Generic;
using FPSBase.Mining;
using UnityEngine;

namespace FPSBase.Vehicles
{
    /// <summary>
    /// Sprung ground contacts for heavy machines. Instead of a flat box sliding on the ground (which snags on every
    /// ledge, cut edge and heap), the body collider is lifted by `clearance` and the machine rides on stiff springs
    /// along each track. The end contacts sit a little higher, like the raised idlers on a real track, so the machine
    /// ramps up and over steps and mounds instead of hitting them. Also adds extra gravity so the machines feel heavy.
    /// </summary>
    [System.Serializable]
    public class TrackSuspension
    {
        [Tooltip("Fraction of the body box height the collider bottom is raised (capped by maxClearance).")]
        public float clearanceFraction = 0.32f;
        public float maxClearance = 0.6f;
        [Tooltip("How far the springs sag under the machine's weight (m). Small = stiff, heavy feel.")]
        public float sag = 0.035f;
        public float dampingRatio = 0.8f;
        [Tooltip("Extra gravity on heavy machines so they settle hard and don't bounce or float.")]
        public float gravityScale = 1.7f;
        [Tooltip("Track / brake grip against sliding downhill (friction coefficient: 0.9 holds on slopes up to ~42 degrees).")]
        public float holdFriction = 0.9f;
        /// <summary>0 (light, 1-5 t: twitchy and tippy) .. 1 (50 t and up: soft, heavily damped, smooth over a rough cut). Set from the mass.</summary>
        public float Heft { get; private set; }
        /// <summary>Colliders the contacts don't stand on (a semi's own trailer and its load).</summary>
        [System.NonSerialized] public System.Func<Collider, bool> ignore;

        public int Touching { get; private set; }
        public int Contacts => pts?.Length ?? 0;
        public Vector3 GroundNormal { get; private set; } = Vector3.up;
        public float Clearance { get; private set; }

        Vector3[] pts;
        float[] lift, reachOf, smoothDist;
        Rigidbody rb;
        Transform t;
        bool ignorePiles;
        static readonly RaycastHit[] hits = new RaycastHit[16];

        public void Setup(Rigidbody body, BoxCollider box, bool ignoreDirtPiles)
        {
            rb = body; t = body.transform; ignorePiles = ignoreDirtPiles;
            Heft = Mathf.InverseLerp(5000f, 50000f, body.mass);
            // heavy machines: their inertia irons out the ground, so they don't buck over every facet of a cut
            body.angularDamping = Mathf.Max(body.angularDamping, Mathf.Lerp(0.05f, 9f, Heft));
            if (box == null) return;
            Vector3 c = box.center, s = box.size;
            float bottom = c.y - s.y * 0.5f, top = c.y + s.y * 0.5f;
            Clearance = Mathf.Min(maxClearance, s.y * clearanceFraction);

            var p = new List<Vector3>(); var l = new List<float>();
            float hx = s.x * 0.4f, hz = s.z * 0.5f;
            float[] zs = { -1.12f, -0.62f, -0.22f, 0.22f, 0.62f, 1.12f };
            foreach (float side in new[] { -1f, 1f })
                foreach (float z in zs)
                {
                    p.Add(new Vector3(c.x + side * hx, bottom, c.z + z * hz));
                    // the end ones are the idlers: raised, so they only bear when the ground ahead is higher
                    l.Add(Mathf.Abs(z) > 1f ? Clearance * 0.45f : 0f);
                }
            pts = p.ToArray(); lift = l.ToArray();
            smoothDist = new float[pts.Length];
            for (int i = 0; i < smoothDist.Length; i++) smoothDist[i] = -1f;
            // idlers look much higher, so a steep pile or bank face ahead is climbed instead of rammed
            float idlerReach = Mathf.Max(Clearance + 0.3f, (top - bottom) * 0.75f);
            reachOf = new float[pts.Length];
            for (int i = 0; i < pts.Length; i++) reachOf[i] = lift[i] > 0f ? idlerReach : Clearance + 0.3f;

            // lift the body collider off the ground, and cut back its lower front and rear corners (a second,
            // shorter box) so the idlers reach a bank or pile face before the hull does and ramp the machine up it
            float nb = Mathf.Min(bottom + Clearance, top - 0.2f);
            // (stepped like a ramp: the lower the slice, the shorter it is)
            float h = top - nb, m1 = nb + h * 0.3f, m2 = nb + h * 0.6f;
            box.center = new Vector3(c.x, (top + m2) * 0.5f, c.z);
            box.size = new Vector3(s.x, top - m2, s.z);
            Slice(box, c, s, m1, m2, 0.88f);
            Slice(box, c, s, nb, m1, 0.72f);
        }

        static void Slice(BoxCollider box, Vector3 c, Vector3 s, float y0, float y1, float lengthFraction)
        {
            var b = box.gameObject.AddComponent<BoxCollider>();
            b.sharedMaterial = box.sharedMaterial;
            b.center = new Vector3(c.x, (y0 + y1) * 0.5f, c.z);
            b.size = new Vector3(s.x, y1 - y0, s.z * lengthFraction);
        }

        Vector3 lastSet; bool hasLast;
        /// <summary>
        /// Tracks grip: the velocity along the ground becomes `along` (blended by Grip), the part across the ground
        /// is kept. The tracks also take back the little the springs and gravity added since the last step (it
        /// added up to a slow sideways creep down every slope); a real shove from a collision is left alone.
        /// </summary>
        public Vector3 TrackVelocity(Vector3 v, Vector3 along, Vector3 n, float grip)
        {
            Vector3 vPlane = Vector3.ProjectOnPlane(v, n);
            Vector3 drift = hasLast ? Vector3.ProjectOnPlane(v - lastSet, n) : Vector3.zero;
            if (drift.magnitude > 0.15f) drift = Vector3.zero;
            Vector3 set = Vector3.Lerp(vPlane, along - drift, grip);
            lastSet = set; hasLast = true;
            return set + n * Vector3.Dot(v, n);
        }
        /// <summary>Off the ground (or not driving the body): forget the last step.</summary>
        public void ResetTrack() => hasLast = false;

        /// <summary>How much of the drive the tracks get (0..1): full with a third of the contacts down, some even when high-centred.</summary>
        public float Grip => Contacts == 0 ? 1f : Touching == 0 ? 0f : Mathf.Clamp(Touching / (Contacts * 0.34f), 0.45f, 1f);

        /// <summary>Run every FixedUpdate on a non-kinematic body.</summary>
        public void Step()
        {
            if (rb == null || pts == null || rb.isKinematic) { Touching = 0; return; }
            rb.AddForce(Physics.gravity * (gravityScale - 1f), ForceMode.Acceleration);
            float m = rb.mass, g = -Physics.gravity.y * gravityScale;
            int bearing = pts.Length - 4; // the four idler contacts normally carry nothing
            // heavier = a longer, slower, better damped ride (a 66 t dozer wallows, a 1 t loader chatters)
            float sagEff = sag * (1f + Heft * 1.4f);
            float k = m * g / (bearing * sagEff);
            float cDamp = 2f * Mathf.Min(1.2f, dampingRatio + Heft * 0.35f) * Mathf.Sqrt(k * m / bearing);
            // and the ground under each contact is felt through a long track frame: smooth it over a few frames
            float follow = Heft > 0.01f ? 1f - Mathf.Exp(-Time.fixedDeltaTime / (Heft * 0.07f)) : 1f;
            Vector3 up = t.up;
            Touching = 0;
            Vector3 nsum = Vector3.zero;
            for (int i = 0; i < pts.Length; i++)
            {
                Vector3 p = t.TransformPoint(pts[i]);
                float reach = reachOf[i];
                if (!Cast(p + up * reach, -up, reach + 0.6f, out RaycastHit hit)) { smoothDist[i] = -1f; continue; }
                float dist = hit.distance;
                // smooth only small changes (facets, cut chatter); a real step is felt straight away
                if (smoothDist[i] >= 0f && Mathf.Abs(dist - smoothDist[i]) < 0.12f) dist = smoothDist[i] = Mathf.Lerp(smoothDist[i], dist, follow);
                else smoothDist[i] = dist;
                float x = reach + sagEff - lift[i] - dist;
                if (x <= 0f) continue;
                float v = Vector3.Dot(rb.GetPointVelocity(p), up);
                float f = Mathf.Clamp(k * x - cDamp * v, 0f, m * g * 1.5f);
                rb.AddForceAtPosition(up * f, p, ForceMode.Force);
                Touching++;
                nsum += hit.normal;
            }
            Vector3 nNew = Touching > 0 ? nsum.normalized : Vector3.up;
            GroundNormal = Touching > 0 && Heft > 0.01f ? Vector3.Slerp(GroundNormal, nNew, 1f - Mathf.Exp(-Time.fixedDeltaTime / (Heft * 0.12f))) : nNew;
            Hold();
        }

        /// <summary>
        /// Tracks (and parked brakes) grip: cancel the part of gravity along the ground, as static friction would, up
        /// to `holdFriction`. Without it the machines creep down every slope, parked or with someone in the seat.
        /// </summary>
        void Hold()
        {
            if (Touching == 0 || holdFriction <= 0f) return;
            Vector3 g = Physics.gravity * gravityScale;
            Vector3 n = GroundNormal;
            Vector3 along = Vector3.ProjectOnPlane(g, n);
            float a = along.magnitude;
            if (a < 1e-3f) return;
            float hold = Mathf.Min(1f, holdFriction * Mathf.Abs(Vector3.Dot(g, n)) / a) * Grip;
            rb.AddForce(-along * hold, ForceMode.Acceleration);
        }

        bool Cast(Vector3 origin, Vector3 dir, float len, out RaycastHit best)
        {
            best = default;
            int n = Physics.RaycastNonAlloc(origin, dir, hits, len, ~0, QueryTriggerInteraction.Ignore);
            float bd = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var h = hits[i];
                if (h.collider.attachedRigidbody == rb) continue;
                if (ignorePiles && h.collider.GetComponent<DirtPile>() != null) continue;
                if (ignore != null && ignore(h.collider)) continue;
                if (h.distance < bd) { bd = h.distance; best = h; }
            }
            return bd < float.MaxValue;
        }

        /// <summary>Contact points in world space (for trampling grass).</summary>
        public IEnumerable<Vector3> WorldContacts()
        {
            if (pts == null) yield break;
            for (int i = 0; i < pts.Length; i++) if (lift[i] == 0f) yield return t.TransformPoint(pts[i]);
        }
    }

    /// <summary>
    /// Colliders on a machine's working tools (excavator boom/stick/bucket, dozer blade). Named "Tool …" by the
    /// builder. They shove other vehicles and hit buildings, but pass into the dirt (digging is handled by the dig
    /// code, and the dig resistance spring stands in for the ground pushing back).
    /// </summary>
    public static class ToolColliders
    {
        public static Collider[] Find(Transform root)
        {
            var list = new List<Collider>();
            foreach (var c in root.GetComponentsInChildren<Collider>(true))
                if (c.name.StartsWith("Tool")) list.Add(c);
            return list.ToArray();
        }

        public static void IgnoreGround(Collider[] tools)
        {
            foreach (var terr in Object.FindObjectsByType<TerrainCollider>(FindObjectsSortMode.None))
                foreach (var c in tools) if (c != null) Physics.IgnoreCollision(c, terr, true);
        }

        public static void IgnorePiles(Collider[] tools, HashSet<int> done)
        {
            foreach (var p in DirtPile.All)
            {
                if (p == null || !done.Add(p.GetInstanceID())) continue;
                var pc = p.GetComponent<Collider>();
                if (pc == null) continue;
                foreach (var c in tools) if (c != null) Physics.IgnoreCollision(c, pc, true);
            }
        }
    }
}
