using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSBase.Vehicles
{
    /// <summary>
    /// Moving crawler tracks. Each track is a closed belt wrapped around its sprocket / idler wheels, and its shoes
    /// are drawn (instanced) at their place on the belt. The belt moves by how far that side of the machine actually
    /// travelled, so driving rolls both tracks, a spin turn runs them in opposite directions and a stopped machine's
    /// tracks stop: the bottom run stays put on the ground instead of the machine gliding over it.
    /// </summary>
    public class TrackAnimator : MonoBehaviour
    {
        [System.Serializable]
        public class Belt
        {
            public float lateral;           // local x of this track's centre line
            public Vector2[] loop;          // closed belt path in (local z = forward, local y = up), counter-clockwise
            public float[] cum;             // distance along the loop at each point
            public float perimeter;
            public int shoes;
            public float shoeWidth, shoeThickness;
            [System.NonSerialized] public float offset, travelled;
            [System.NonSerialized] public Vector3 lastWorld;
            [System.NonSerialized] public bool has;
        }

        public Belt[] belts = new Belt[0];
        public Material shoeMaterial;
        public float drawDistance = 260f;

        Material mat;
        static Mesh cube;
        readonly List<Matrix4x4> plates = new(), ribs = new();

        /// <summary>Total belt travel of each track (m, signed: + = forward). For the tests and the HUD.</summary>
        public float Travel(int belt) => belts != null && belt < belts.Length ? belts[belt].travelled : 0f;
        public float ShoeWidth => belts != null && belts.Length > 0 ? belts[0].shoeWidth : 0.4f;

        // ------------------------------------------------------------------ building (editor builders call this)

        /// <summary>
        /// Adds a track to `root`: a belt wrapped around `wheels` (each = local forward z, local up y, radius),
        /// running at local x `lateral`. Shoes are about `pitch` long.
        /// </summary>
        public static TrackAnimator AddBelt(GameObject root, Material shoe, float lateral, float shoeWidth, float shoeThickness, float pitch, params Vector3[] wheels)
        {
            var ta = root.GetComponent<TrackAnimator>();
            if (ta == null) ta = root.AddComponent<TrackAnimator>();
            ta.shoeMaterial = shoe;
            var pts = new List<Vector2>();
            foreach (var w in wheels)
                for (int i = 0; i < 28; i++)
                {
                    float a = i / 28f * Mathf.PI * 2f;
                    pts.Add(new Vector2(w.x + Mathf.Cos(a) * w.z, w.y + Mathf.Sin(a) * w.z));
                }
            var loop = Hull(pts);
            var cum = new float[loop.Length + 1];
            for (int i = 0; i < loop.Length; i++) cum[i + 1] = cum[i] + Vector2.Distance(loop[i], loop[(i + 1) % loop.Length]);
            var b = new Belt
            {
                lateral = lateral, loop = loop, cum = cum, perimeter = cum[loop.Length],
                shoeWidth = shoeWidth, shoeThickness = shoeThickness,
            };
            b.shoes = Mathf.Max(8, Mathf.RoundToInt(b.perimeter / Mathf.Max(0.04f, pitch)));
            var list = new List<Belt>(ta.belts ?? new Belt[0]) { b };
            ta.belts = list.ToArray();
            return ta;
        }

        /// <summary>Convex hull, counter-clockwise (monotone chain).</summary>
        static Vector2[] Hull(List<Vector2> p)
        {
            p.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var h = new Vector2[p.Count * 2];
            int k = 0;
            static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            for (int i = 0; i < p.Count; i++)
            {
                while (k >= 2 && Cross(h[k - 2], h[k - 1], p[i]) <= 1e-7f) k--;
                h[k++] = p[i];
            }
            for (int i = p.Count - 2, t = k + 1; i >= 0; i--)
            {
                while (k >= t && Cross(h[k - 2], h[k - 1], p[i]) <= 1e-7f) k--;
                h[k++] = p[i];
            }
            var r = new Vector2[k - 1];
            System.Array.Copy(h, r, k - 1);
            return r;
        }

        // ------------------------------------------------------------------ runtime

        void Awake()
        {
            if (cube == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube = go.GetComponent<MeshFilter>().sharedMesh;
                Destroy(go);
            }
            if (shoeMaterial != null)
                mat = shoeMaterial.enableInstancing ? shoeMaterial : new Material(shoeMaterial) { enableInstancing = true };
        }

        void LateUpdate()
        {
            if (belts == null) return;
            Vector3 fwd = transform.forward;
            foreach (var b in belts)
            {
                if (b.loop == null || b.loop.Length < 3 || b.perimeter <= 0f) continue;
                Vector3 w = transform.TransformPoint(new Vector3(b.lateral, 0f, 0f));
                if (b.has)
                {
                    float v = Vector3.Dot(w - b.lastWorld, fwd);
                    // not a teleport / recover (a long frame can legitimately move a few metres)
                    if (Mathf.Abs(v) < Mathf.Max(3f, Time.deltaTime * 40f))
                    {
                        b.travelled += v;
                        // counter-clockwise belt: the bottom run (on the ground) runs backward past the machine
                        b.offset = Mathf.Repeat(b.offset - v, b.perimeter);
                    }
                }
                b.lastWorld = w; b.has = true;
            }
            Draw();
        }

        void Draw()
        {
            if (mat == null || cube == null) return;
            var cam = FPSBase.Mining.TuftRenderer.ActiveCamera();
            if (cam != null && (cam.transform.position - transform.position).sqrMagnitude > drawDistance * drawDistance) return;
            plates.Clear(); ribs.Clear();
            Matrix4x4 l2w = transform.localToWorldMatrix;
            float maxZ = 0f;
            foreach (var b in belts)
            {
                if (b.loop == null || b.loop.Length < 3) continue;
                float pitch = b.perimeter / b.shoes;
                int seg = 0;
                for (int i = 0; i < b.shoes; i++)
                {
                    float d = Mathf.Repeat(i * pitch + b.offset, b.perimeter);
                    seg = Seg(b, d, seg);
                    Vector2 a = b.loop[seg], c = b.loop[(seg + 1) % b.loop.Length];
                    float len = b.cum[seg + 1] - b.cum[seg];
                    Vector2 p = Vector2.Lerp(a, c, len > 1e-6f ? (d - b.cum[seg]) / len : 0f);
                    Vector2 t = len > 1e-6f ? (c - a) / len : Vector2.right;
                    Vector2 n = new Vector2(t.y, -t.x); // outward for a counter-clockwise loop
                    var tangent = new Vector3(0f, t.y, t.x);
                    var normal = new Vector3(0f, n.y, n.x);
                    var rot = Quaternion.LookRotation(tangent, normal);
                    var pos = new Vector3(b.lateral, p.y, p.x);
                    maxZ = Mathf.Max(maxZ, Mathf.Abs(p.x) + Mathf.Abs(b.lateral));
                    plates.Add(l2w * Matrix4x4.TRS(pos, rot, new Vector3(b.shoeWidth, b.shoeThickness, pitch * 0.86f)));
                    // a grouser bar across each shoe, standing out from the belt: this is what you see move
                    var rib = pos + normal * (b.shoeThickness * 0.85f) + tangent * (pitch * 0.18f);
                    ribs.Add(l2w * Matrix4x4.TRS(rib, rot, new Vector3(b.shoeWidth * 0.98f, b.shoeThickness * 0.9f, pitch * 0.2f)));
                }
            }
            if (plates.Count == 0) return;
            var bounds = new Bounds(transform.position, Vector3.one * (maxZ * 2f + 4f));
            var rp = new RenderParams(mat)
            {
                worldBounds = bounds, shadowCastingMode = ShadowCastingMode.On, receiveShadows = true, layer = gameObject.layer,
            };
            Graphics.RenderMeshInstanced(rp, cube, 0, plates);
            Graphics.RenderMeshInstanced(rp, cube, 0, ribs);
        }

        static int Seg(Belt b, float d, int start)
        {
            int n = b.loop.Length;
            for (int k = 0; k < n; k++)
            {
                int s = (start + k) % n;
                if (d >= b.cum[s] && d < b.cum[s + 1]) return s;
            }
            return 0;
        }

        /// <summary>Points under the bottom run of every track, where it bears on the ground (world space).</summary>
        public IEnumerable<Vector3> GroundPoints(int perTrack = 5)
        {
            if (belts == null) yield break;
            foreach (var b in belts)
            {
                if (b.loop == null || b.loop.Length < 3) continue;
                float lo = float.MaxValue, minZ = float.MaxValue, maxZ = float.MinValue;
                foreach (var p in b.loop) lo = Mathf.Min(lo, p.y);
                foreach (var p in b.loop) if (p.y < lo + 0.03f) { minZ = Mathf.Min(minZ, p.x); maxZ = Mathf.Max(maxZ, p.x); }
                for (int i = 0; i < perTrack; i++)
                {
                    float z = Mathf.Lerp(minZ, maxZ, perTrack == 1 ? 0.5f : i / (float)(perTrack - 1));
                    yield return transform.TransformPoint(new Vector3(b.lateral, lo - b.shoeThickness * 0.5f, z));
                }
            }
        }
    }
}
