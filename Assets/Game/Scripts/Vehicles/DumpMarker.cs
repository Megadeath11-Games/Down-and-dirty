using FPSBase.Mining;
using UnityEngine;

namespace FPSBase.Vehicles
{
    /// <summary>
    /// Shows the driver where the bucket's load will land if it's dumped now: a drop line from the bucket down to
    /// a ring on whatever is below. Green over a truck bed or the hopper (it'll go in), amber over the ground.
    /// Only drawn while the bucket is lifted clear of the ground and someone on this game is driving.
    /// </summary>
    public class DumpMarker : MonoBehaviour
    {
        public Transform source;     // the bucket's pour point (cutting edge)
        public System.Func<Vector3> sourceAt; // or: where the load will leave from (overrides source)
        public Transform ignore;     // the machine itself
        public float ringRadius = 0.6f;
        public bool Visible => drop != null && drop.enabled;
        public bool OverReceiver { get; private set; }
        /// <summary>
        /// Set while something else wants the marker instead (an excavator's lifting chain let down): a ring of that
        /// radius round that point, green when `good` (a block's loop is in reach of the hook), with a drop line to
        /// the ground under it. Null: the usual dump marker.
        /// </summary>
        public System.Func<(Vector3 center, float radius, bool good)?> hookRing;
        /// <summary>The hook ring is showing (for the test).</summary>
        public bool HookMode { get; private set; }

        LineRenderer drop, ring;
        static Material matGood, matPlain;
        const int RingPoints = 28;
        static readonly Color Good = new(0.35f, 1f, 0.45f, 0.9f), Plain = new(1f, 0.75f, 0.25f, 0.85f);
        readonly RaycastHit[] hits = new RaycastHit[24];

        public static DumpMarker Attach(GameObject owner, Transform pourPoint, float radius)
        {
            var m = owner.AddComponent<DumpMarker>();
            m.source = pourPoint; m.ignore = owner.transform; m.ringRadius = radius;
            return m;
        }

        void Awake()
        {
            if (matGood == null) { matGood = Make(Good); matPlain = Make(Plain); }
            drop = Line("Dump drop line", 2, false, 0.035f);
            ring = Line("Dump ring", RingPoints, true, 0.06f);
            Show(false);
        }

        static Material Make(Color c)
        {
            var sh = Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            var m = new Material(sh) { name = "DumpMarker" };
            m.SetColor("_UnlitColor", c);
            m.color = c;
            return m;
        }

        LineRenderer Line(string n, int count, bool loop, float width)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = matPlain;
            lr.positionCount = count; lr.loop = loop; lr.useWorldSpace = true;
            lr.widthMultiplier = width;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        void Show(bool on)
        {
            if (drop.enabled != on) { drop.enabled = on; ring.enabled = on; }
        }

        void LateUpdate()
        {
            var v = GetComponent<VehicleBase>();
            if ((source == null && sourceAt == null) || v == null || !v.Occupied || v.NetRemote) { Show(false); return; }
            var hr = hookRing != null ? hookRing() : null;
            HookMode = hr.HasValue;
            if (hr.HasValue)
            {
                var (c, rad, good) = hr.Value;
                Show(true);
                OverReceiver = good;
                var hm = good ? matGood : matPlain;
                if (drop.sharedMaterial != hm) { drop.sharedMaterial = hm; ring.sharedMaterial = hm; }
                float gy = c.y - 30f, hb = float.MaxValue;
                int hn = Physics.RaycastNonAlloc(c + Vector3.up * 0.2f, Vector3.down, hits, 30f, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < hn; i++)
                {
                    var h = hits[i];
                    if (h.distance >= hb || (ignore != null && h.collider.transform.IsChildOf(ignore))) continue;
                    hb = h.distance; gy = h.point.y;
                }
                drop.SetPosition(0, c); drop.SetPosition(1, new Vector3(c.x, gy + 0.05f, c.z));
                for (int i = 0; i < RingPoints; i++)
                {
                    float a = i / (float)RingPoints * Mathf.PI * 2f;
                    ring.SetPosition(i, c + new Vector3(Mathf.Cos(a) * rad, 0f, Mathf.Sin(a) * rad));
                }
                return;
            }
            Vector3 from = sourceAt != null ? sourceAt() : source.position;
            // find what's under the bucket (the first solid thing, or a truck bed / hopper that takes the load)
            int n = Physics.RaycastNonAlloc(from + Vector3.up * 0.5f, Vector3.down, hits, 60f, ~0, QueryTriggerInteraction.Collide);
            float best = float.MaxValue; Vector3 point = from + Vector3.down * 60f; bool receiver = false, any = false;
            for (int i = 0; i < n; i++)
            {
                var h = hits[i];
                if (h.distance >= best || (ignore != null && h.collider.transform.IsChildOf(ignore))) continue;
                var rec = h.collider.GetComponentInParent<IMaterialReceiver>();
                bool takes = rec != null && !(rec is DirtPile) && rec.AcceptsAt(h.point);
                if (h.collider.isTrigger && !takes) continue;
                best = h.distance; point = h.point; receiver = takes; any = true;
            }
            // only while the bucket is up in the air (not while digging)
            if (!any || from.y - point.y < 0.15f) { Show(false); return; }
            Show(true);
            OverReceiver = receiver;
            var m = receiver ? matGood : matPlain;
            if (drop.sharedMaterial != m) { drop.sharedMaterial = m; ring.sharedMaterial = m; }
            drop.SetPosition(0, from); drop.SetPosition(1, point + Vector3.up * 0.05f);
            for (int i = 0; i < RingPoints; i++)
            {
                float a = i / (float)RingPoints * Mathf.PI * 2f;
                ring.SetPosition(i, point + new Vector3(Mathf.Cos(a) * ringRadius, 0.06f, Mathf.Sin(a) * ringRadius));
            }
        }
    }
}
