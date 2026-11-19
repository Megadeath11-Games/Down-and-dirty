using System.Collections.Generic;
using FPSBase.Core;
using UnityEngine;

namespace FPSBase.Mining
{
    /// <summary>
    /// A rock dug out of the ground. While it's still half buried it's stuck fast (kinematic): dig around it, rip it,
    /// or shove it hard with a blade or bucket to break it free. Free boulders are real physics bodies: push them with
    /// a dozer, or pick small ones up in a bucket (they ride along as "rock" in the load and come back out as boulders
    /// when dumped). The wash plant's grizzly rejects them.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Boulder : MonoBehaviour
    {
        static readonly List<Boulder> all = new();
        public static IReadOnlyList<Boulder> All => all;
        public const float Density = 2600f; // kg/m^3
        const int MaxBoulders = 260;

        public float volume, radius;
        public bool embedded;
        /// <summary>Riding in a bucket or truck bed (moved by the container, not by physics).</summary>
        public bool Held { get; private set; }
        float sellCheck;

        /// <summary>A container picked this rock up: it moves with the container until it tips it out.</summary>
        public void Hold()
        {
            embedded = false;
            Held = true;
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.None;
            // carried: it mustn't shove the machine carrying it about
            foreach (var c in GetComponents<Collider>()) c.enabled = false;
        }

        /// <summary>Tipped out of a container: a physics rock again, moving with `velocity`.</summary>
        public void Drop(Vector3 velocity)
        {
            Held = false;
            foreach (var c in GetComponents<Collider>()) c.enabled = true;
            rb.isKinematic = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.linearVelocity = Vector3.ClampMagnitude(velocity, 12f);
            rb.WakeUp();
        }

        /// <summary>The nearest free rock (not stuck in the ground, not already carried) within `reach` of `p` that fits.</summary>
        public static Boulder Nearest(Vector3 p, float reach, float maxVolume, float maxRadius)
        {
            Boulder best = null; float bd = float.MaxValue;
            foreach (var b in all)
            {
                if (b == null || b.embedded || b.Held || b.volume > maxVolume || b.radius > maxRadius) continue;
                float d = Vector3.Distance(b.transform.position, p) - b.radius;
                if (d < reach && d < bd) { bd = d; best = b; }
            }
            return best;
        }
        Rigidbody rb;
        float shoved;
        static Mesh[] meshes;
        static Material fallbackMat;

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        public static float RadiusFor(float volume) => Mathf.Pow(volume / (0.8f * 4.18879f), 1f / 3f);

        public static Boulder Spawn(Vector3 pos, float radius, bool embedded)
        {
            if (all.Count >= MaxBoulders) Destroy(all[0].gameObject);
            var go = new GameObject("Boulder");
            go.transform.SetPositionAndRotation(pos, Random.rotation);
            var squash = new Vector3(Random.Range(0.85f, 1.15f), Random.Range(0.7f, 0.95f), Random.Range(0.85f, 1.2f));
            go.transform.localScale = squash * radius;
            var mesh = RockMesh(Random.Range(0, 4));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var site = DigSite.Instance;
            mr.sharedMaterial = site != null && site.rockMaterial != null ? site.rockMaterial : Fallback();
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh; mc.convex = true;
            var b = go.AddComponent<Boulder>();
            b.radius = radius;
            b.volume = 0.8f * 4.18879f * radius * radius * radius * squash.x * squash.y * squash.z;
            b.rb = go.GetComponent<Rigidbody>();
            b.rb.mass = Mathf.Max(30f, b.volume * Density);
            b.rb.angularDamping = 0.8f;
            b.rb.linearDamping = 0.1f;
            b.rb.interpolation = RigidbodyInterpolation.Interpolate;
            b.rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            b.embedded = embedded;
            b.rb.isKinematic = embedded;
            return b;
        }

        /// <summary>Dump the rock part of a load as boulders (split into pieces no bigger than 1.5 m^3).</summary>
        public static void SpawnFromLoad(Vector3 ground, MaterialLoad rocks)
        {
            float v = rocks.rock > 0f ? rocks.rock : rocks.volume;
            int pieces = Mathf.Clamp(Mathf.CeilToInt(v / 1.5f), 1, 6);
            for (int i = 0; i < pieces; i++)
            {
                float r = RadiusFor(v / pieces) / 0.9f;
                Vector2 o = Random.insideUnitCircle * (0.3f + r) * (pieces > 1 ? 1f : 0.2f);
                Spawn(ground + new Vector3(o.x, r + 0.25f + i * 0.2f, o.y), r, false);
            }
        }

        /// <summary>
        /// A bucket digging at `tip`: grabs the nearest free boulder within reach that fits in `freeSpace`.
        /// Returns it as a rock-only load (or empty).
        /// </summary>
        public static MaterialLoad TryScoop(Vector3 tip, float reach, float freeSpace)
        {
            Boulder best = null; float bd = float.MaxValue;
            foreach (var b in all)
            {
                if (b == null || b.embedded || b.volume > freeSpace) continue;
                float d = Vector3.Distance(b.transform.position, tip) - b.radius;
                if (d < reach && d < bd) { bd = d; best = b; }
            }
            if (best == null) return default;
            var load = MaterialLoad.Rocks(best.volume);
            Destroy(best.gameObject);
            all.Remove(best);
            return load;
        }

        /// <summary>A ripper tooth going past breaks stuck boulders loose.</summary>
        public static void Loosen(Vector3 p, float r)
        {
            foreach (var b in all)
                if (b != null && b.embedded && Vector3.Distance(b.transform.position, p) < r + b.radius) b.Free();
        }

        public void Free()
        {
            if (!embedded) return;
            embedded = false;
            var site = DigSite.At(transform.position);
            if (site != null && site.Contains(transform.position))
            {
                site.Flush(0); // the physics ground must already have the dug-out hole in it
                // lift it so its bottom is only a little in the ground: no explosive pop out of the terrain
                Vector3 p = transform.position;
                p.y = Mathf.Max(p.y, site.SurfaceY(p) + radius * 0.45f);
                transform.position = p;
            }
            rb.isKinematic = false;
            rb.WakeUp();
        }

        void Awake() { if (rb == null) rb = GetComponent<Rigidbody>(); }

        void FixedUpdate()
        {
            if (Held) return;
            var site = DigSite.At(transform.position);
            // a rock tipped into the sand & gravel yard's bay is sold there
            if (!embedded && (sellCheck -= Time.fixedDeltaTime) <= 0f)
            {
                sellCheck = 0.5f;
                if (rb.linearVelocity.sqrMagnitude < 1f && MaterialTransfer.FindReceiverBelow(transform.position + Vector3.up * radius, null, out _) is AggregateYard yard)
                {
                    yard.Receive(MaterialLoad.Rocks(volume));
                    Destroy(gameObject);
                    return;
                }
            }
            if (embedded)
            {
                if (site == null || !site.Contains(transform.position) || site.SurfaceY(transform.position) < transform.position.y - radius * 0.3f) Free();
                shoved = Mathf.Max(0f, shoved - Time.fixedDeltaTime * 0.5f);
            }
            else if (transform.position.y < -60f) Destroy(gameObject);
            else if (rb.linearVelocity.sqrMagnitude > 64f) rb.linearVelocity = rb.linearVelocity.normalized * 8f; // a bucket clipping it doesn't fire it across the map
            else if (site != null && site.Contains(transform.position) && transform.position.y < site.SurfaceY(transform.position) - radius * 1.2f)
            {
                // slipped through the ground (dirt moved under it): put it back on top
                site.Flush(0);
                transform.position = new Vector3(transform.position.x, site.SurfaceY(transform.position) + radius, transform.position.z);
                rb.linearVelocity = Vector3.zero;
            }
        }

        void OnCollisionStay(Collision c)
        {
            if (!embedded || c.rigidbody == null || c.rigidbody.isKinematic) return;
            // a machine leaning on it: heavy enough and long enough, it pops out
            if (c.rigidbody.mass > rb.mass * 0.8f && c.impulse.magnitude > rb.mass * 0.02f)
            {
                shoved += Time.fixedDeltaTime * 1.5f;
                if (shoved > 0.8f)
                {
                    Free();
                    GameUI.Toast($"Broke a {volume:0.0} m³ boulder loose");
                }
            }
        }

        static Material Fallback()
        {
            if (fallbackMat == null)
            {
                var sh = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
                fallbackMat = new Material(sh) { color = new Color(0.45f, 0.44f, 0.42f) };
            }
            return fallbackMat;
        }

        /// <summary>Low-poly rock: a jittered icosahedron, one subdivision, flat shaded. Unit radius.</summary>
        static Mesh RockMesh(int variant)
        {
            meshes ??= new Mesh[4];
            if (meshes[variant] != null) return meshes[variant];
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3>
            {
                new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
                new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
                new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
            };
            int[] f =
            {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1,
            };
            var mid = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long k = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (mid.TryGetValue(k, out int i)) return i;
                v.Add((v[a] + v[b]) * 0.5f);
                return mid[k] = v.Count - 1;
            }
            var tris = new List<int>();
            for (int i = 0; i < f.Length; i += 3)
            {
                int a = f[i], b = f[i + 1], c = f[i + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                tris.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            var rng = new System.Random(variant * 977 + 13);
            for (int i = 0; i < v.Count; i++)
                v[i] = v[i].normalized * (0.78f + (float)rng.NextDouble() * 0.36f);
            // flatten one side a little: rocks tend to sit on a face
            for (int i = 0; i < v.Count; i++) if (v[i].y < -0.55f) v[i] = new Vector3(v[i].x, -0.55f - (v[i].y + 0.55f) * 0.3f, v[i].z);
            var fv = new Vector3[tris.Count];
            var fi = new int[tris.Count];
            for (int i = 0; i < tris.Count; i++) { fv[i] = v[tris[i]]; fi[i] = i; }
            var m = new Mesh { name = "Rock" + variant, vertices = fv, triangles = fi };
            m.RecalculateNormals(); m.RecalculateBounds();
            return meshes[variant] = m;
        }
    }
}
