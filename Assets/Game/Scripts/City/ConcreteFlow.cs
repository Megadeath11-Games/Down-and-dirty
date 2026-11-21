using System.Collections.Generic;
using FPSBase.Mining;
using UnityEngine;

namespace FPSBase.City
{
    /// <summary>Something wet concrete can be poured into (a house site's forms and trenches).</summary>
    public interface IConcreteReceiver
    {
        /// <summary>Take `m3` of concrete landing at `at` (the ground under the chute). Return how much it took.</summary>
        float TakeConcrete(Vector3 at, float m3, Material mat);
    }

    /// <summary>
    /// Where poured concrete goes: down from the chute to whatever's below. A receiver (a house site's footings)
    /// takes what it can; the rest lands on the ground as a spreading puddle that sets.
    /// </summary>
    public static class ConcreteFlow
    {
        public static readonly List<IConcreteReceiver> Receivers = new List<IConcreteReceiver>();
        /// <summary>Concrete that went on the ground instead of into a site (m³).</summary>
        public static float Wasted;

        /// <summary>The ground (anything solid that isn't a machine or a person) below a point.</summary>
        public static bool GroundBelow(Vector3 from, Transform ignore, out Vector3 hit)
        {
            hit = from + Vector3.down * 2f;
            var hits = Physics.RaycastAll(from + Vector3.up * 0.3f, Vector3.down, 40f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (ignore != null && h.collider.transform.IsChildOf(ignore)) continue;
                if (h.collider is CharacterController) continue;
                if (h.collider.attachedRigidbody != null && !h.collider.attachedRigidbody.isKinematic) continue; // machines, loose blocks
                hit = h.point;
                return true;
            }
            var site = DigSite.At(from);
            if (site != null) { hit = new Vector3(from.x, site.SurfaceY(from), from.z); return true; }
            return false;
        }

        /// <summary>Pour `m3` of concrete from `from` (a chute's end).</summary>
        public static void Pour(Vector3 from, float m3, Material mat, Transform ignore)
        {
            if (m3 <= 0f) return;
            if (!GroundBelow(from, ignore, out Vector3 at)) at = from + Vector3.down * 2f;
            foreach (var r in Receivers)
            {
                if (r == null) continue;
                m3 -= Mathf.Max(0f, r.TakeConcrete(at, m3, mat));
                if (m3 <= 1e-6f) return;
            }
            Wasted += m3;
            ConcretePuddle.Add(at, m3, mat);
        }
    }

    /// <summary>
    /// Concrete poured onto the ground: a low puddle that spreads as more lands on it and goes hard over a few
    /// minutes (darker and matt once it's set). Puddles aren't saved.
    /// </summary>
    public class ConcretePuddle : MonoBehaviour
    {
        public static readonly List<ConcretePuddle> All = new List<ConcretePuddle>();
        public const float Thickness = 0.12f, MaxRadius = 6f, CureSeconds = 180f;

        public float Volume { get; private set; }
        public float Radius => Mathf.Min(MaxRadius, Mathf.Sqrt(Volume / (Mathf.PI * Thickness * 0.75f)));
        public float Age { get; private set; }
        public bool Cured => Age >= CureSeconds;

        Mesh mesh;
        Material mat;
        Color wet, dry;
        bool dirty;
        float rebuildIn;

        public static ConcretePuddle Add(Vector3 at, float m3, Material source)
        {
            ConcretePuddle best = null; float bd = float.MaxValue;
            foreach (var p in All)
            {
                if (p == null || p.Cured) continue;
                Vector3 d = at - p.transform.position;
                float h = new Vector2(d.x, d.z).magnitude;
                if (Mathf.Abs(d.y) < 1.2f && h < p.Radius + 0.6f && h < bd) { best = p; bd = h; }
            }
            if (best == null)
            {
                var go = new GameObject("Concrete puddle");
                go.transform.position = at;
                best = go.AddComponent<ConcretePuddle>();
                best.Init(source);
            }
            best.Volume += m3;
            best.Age = Mathf.Min(best.Age, 5f); // fresh concrete on it keeps it wet
            best.dirty = true;
            return best;
        }

        void Init(Material source)
        {
            mesh = new Mesh { name = "Concrete puddle" };
            mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = gameObject.AddComponent<MeshRenderer>();
            if (source != null)
            {
                mat = new Material(source);
                wet = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : new Color(0.45f, 0.45f, 0.44f);
                dry = new Color(Mathf.Min(1f, wet.r * 1.45f), Mathf.Min(1f, wet.g * 1.45f), Mathf.Min(1f, wet.b * 1.42f), 1f);
                mr.sharedMaterial = mat;
            }
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);
        void OnDestroy() { if (mesh != null) Destroy(mesh); if (mat != null) Destroy(mat); }

        void Update()
        {
            float was = Age;
            Age += Time.deltaTime;
            if (mat != null && (was < CureSeconds || dirty))
            {
                float k = Mathf.Clamp01(Age / CureSeconds);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.Lerp(wet, dry, k));
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", Mathf.Lerp(0.75f, 0.2f, k));
            }
            if (dirty && (rebuildIn -= Time.deltaTime) <= 0f) { rebuildIn = 0.15f; dirty = false; Rebuild(); }
        }

        const int Rings = 7, Segs = 28;
        static readonly List<Vector3> verts = new List<Vector3>();
        static readonly List<int> tris = new List<int>();

        /// <summary>A low dome laid over the ground under it, thickest in the middle.</summary>
        float trampled;

        void Rebuild()
        {
            float r = Radius;
            // flatten the grass under it as it spreads, so no tufts poke up through the concrete
            if (r > trampled + 0.25f)
            {
                var site = DigSite.At(transform.position);
                if (site != null)
                    for (float x = -r; x <= r; x += 0.5f)
                        for (float z = -r; z <= r; z += 0.5f)
                        {
                            float d = Mathf.Sqrt(x * x + z * z);
                            if (d <= r + 0.2f && d >= trampled - 0.5f) site.Trample(transform.position + new Vector3(x, 0f, z), 0.4f);
                        }
                trampled = r;
            }
            // past the widest it spreads, it heaps up thicker instead
            float t = Thickness * Mathf.Max(1f, Volume / (Mathf.PI * MaxRadius * MaxRadius * Thickness * 0.75f));
            verts.Clear(); tris.Clear();
            Vector3 c = transform.position;
            verts.Add(new Vector3(0f, Ground(c) - c.y + t + 0.015f, 0f));
            for (int i = 1; i <= Rings; i++)
            {
                float k = i / (float)Rings;
                float h = t * (1f - k * k * k * k) + 0.015f;
                for (int s = 0; s < Segs; s++)
                {
                    float a = s / (float)Segs * Mathf.PI * 2f;
                    // a slightly ragged edge, as wet concrete runs out
                    float rr = r * k * (1f + (i == Rings ? (Mathf.PerlinNoise(s * 0.7f, c.x * 0.1f) - 0.5f) * 0.18f : 0f));
                    var p = new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr);
                    p.y = Ground(c + p) - c.y + h;
                    verts.Add(p);
                }
            }
            for (int s = 0; s < Segs; s++) { int a = 1 + s, b = 1 + (s + 1) % Segs; tris.Add(0); tris.Add(b); tris.Add(a); }
            for (int i = 1; i < Rings; i++)
            {
                int r0 = 1 + (i - 1) * Segs, r1 = 1 + i * Segs;
                for (int s = 0; s < Segs; s++)
                {
                    int s1 = (s + 1) % Segs;
                    tris.Add(r0 + s); tris.Add(r0 + s1); tris.Add(r1 + s);
                    tris.Add(r0 + s1); tris.Add(r1 + s1); tris.Add(r1 + s);
                }
            }
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        static float Ground(Vector3 p)
        {
            return ConcreteFlow.GroundBelow(p + Vector3.up * 2f, null, out Vector3 hit) ? hit.y : p.y;
        }
    }
}
