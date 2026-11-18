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

        public int Touching { get; private set; }
        public int Contacts => pts?.Length ?? 0;
        public Vector3 GroundNormal { get; private set; } = Vector3.up;
        public float Clearance { get; private set; }

        Vector3[] pts;
        float[] lift, reachOf;
        Rigidbody rb;
        Transform t;
        bool ignorePiles;
        static readonly RaycastHit[] hits = new RaycastHit[16];

        public void Setup(Rigidbody body, BoxCollider box, bool ignoreDirtPiles)
        {
            rb = body; t = body.transform; ignorePiles = ignoreDirtPiles;
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

        /// <summary>How much of the drive the tracks get (0..1): full with a third of the contacts down, some even when high-centred.</summary>
        public float Grip => Contacts == 0 ? 1f : Touching == 0 ? 0f : Mathf.Clamp(Touching / (Contacts * 0.34f), 0.45f, 1f);

        /// <summary>Run every FixedUpdate on a non-kinematic body.</summary>
        public void Step()
        {
            if (rb == null || pts == null || rb.isKinematic) { Touching = 0; return; }
            rb.AddForce(Physics.gravity * (gravityScale - 1f), ForceMode.Acceleration);
            float m = rb.mass, g = -Physics.gravity.y * gravityScale;
            int bearing = pts.Length - 4; // the four idler contacts normally carry nothing
            float k = m * g / (bearing * sag);
            float cDamp = 2f * dampingRatio * Mathf.Sqrt(k * m / bearing);
            Vector3 up = t.up;
            Touching = 0;
            Vector3 nsum = Vector3.zero;
            for (int i = 0; i < pts.Length; i++)
            {
                Vector3 p = t.TransformPoint(pts[i]);
                float reach = reachOf[i];
                if (!Cast(p + up * reach, -up, reach + 0.6f, out RaycastHit hit)) continue;
                float x = reach + sag - lift[i] - hit.distance;
                if (x <= 0f) continue;
                float v = Vector3.Dot(rb.GetPointVelocity(p), up);
                float f = Mathf.Clamp(k * x - cDamp * v, 0f, m * g * 1.5f);
                rb.AddForceAtPosition(up * f, p, ForceMode.Force);
                Touching++;
                nsum += hit.normal;
            }
            GroundNormal = Touching > 0 ? nsum.normalized : Vector3.up;
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
