using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSBase.Mining
{
    /// <summary>
    /// The dirt inside a bucket or a truck bed, as live clumps (the same clods that fall when you dump).
    /// The load itself (MaterialLoad) stays the authority for volume, gold and make-up; this keeps a matching
    /// number of clumps in the container and simulates them in the container's own frame: they pile up, shake
    /// when the machine drives over bumps, slide to the lip when the bucket curls or the bed tips, and the ones
    /// nearest the lip are the ones that leave when dirt pours out.
    /// The top is open: a load can heap above the rim, and clumps that slosh or splash over the rim fall out as real
    /// dirt (taken out of the load through `takeOut`), so a hard stop, a sharp turn or a big load dumped in on top
    /// spills some over the sides.
    /// </summary>
    public class ClumpContainer : MonoBehaviour
    {
        struct Clump
        {
            public Vector3 p, v;      // box-local, metres
            public Quaternion rot;
            public float r;
            public byte cat;
        }

        Transform frame;
        Vector3 center, half;         // in frame-local units, scaled to metres
        float capacity;
        int maxN;
        float baseR;
        readonly List<Clump> clumps = new();
        Vector3 lastPos, lastVel;
        bool hasLast;
        float quiet;
        Renderer[] hidden;
        static readonly List<ClumpContainer> all = new();
        public static IReadOnlyList<ClumpContainer> All => all;

        public int Count => clumps.Count;
        public float ClumpRadius => baseR;
        /// <summary>Takes `volume` m3 out of the real load (for clumps that spill over the rim). Null = clumps can't leave.</summary>
        public System.Func<float, MaterialLoad> takeOut;
        [Tooltip("How strongly the container's own movement throws the clumps around (1 = physical).")]
        public float sloshGain = 1.5f;
        /// <summary>
        /// Which way the container opens, in its own frame (unit vector). Up for a truck bed; a bucket finds its own
        /// mouth from its mesh (AutoMouth). The box faces on that side are open: clumps leave through them whenever
        /// gravity (and shaking) carries them out, so a bucket tipped part way pours out part of its load.
        /// </summary>
        public Vector3 mouth = Vector3.up;
        /// <summary>True while the load is packed in and can't fall out (a bucket cutting into the ground).</summary>
        public System.Func<bool> packed;
        bool isPacked;
        readonly bool[] openLo = new bool[3], openHi = { false, true, false };

        public void SetMouth(Vector3 m) { mouth = m; UpdateOpen(); }

        void UpdateOpen()
        {
            for (int k = 0; k < 3; k++) { openHi[k] = mouth[k] > 0.35f; openLo[k] = mouth[k] < -0.35f; }
        }

        float Support => Mathf.Abs(mouth.x) * half.x + Mathf.Abs(mouth.y) * half.y + Mathf.Abs(mouth.z) * half.z;

        /// <summary>
        /// Finds the open side of a bucket from its own shape: the direction (from the load) where the bucket's
        /// mesh is thinnest. `exclude` = renderers that aren't the bucket (the old load mound).
        /// </summary>
        public void AutoMouth(params Renderer[] exclude)
        {
            if (frame == null) return;
            var ex = new HashSet<Renderer>(exclude);
            var dirs = new List<Vector3>();
            Vector3 o = Origin; Quaternion inv = Quaternion.Inverse(Rot);
            void Add(Vector3 world)
            {
                Vector3 d = inv * (world - o);
                if (d.sqrMagnitude > 1e-6f) dirs.Add(d.normalized);
            }
            foreach (var mf in frame.GetComponentsInChildren<MeshFilter>(true))
            {
                var r = mf.GetComponent<Renderer>();
                var m = mf.sharedMesh;
                if (r == null || m == null || ex.Contains(r)) continue;
                if (m.isReadable)
                {
                    var vs = m.vertices;
                    int step = Mathf.Max(1, vs.Length / 600);
                    for (int i = 0; i < vs.Length; i += step) Add(mf.transform.TransformPoint(vs[i]));
                }
                else
                {
                    var b = m.bounds;
                    for (int i = 0; i < 8; i++)
                        Add(mf.transform.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                }
            }
            if (dirs.Count < 8) return;
            Vector3 best = mouth; float bestScore = float.MaxValue;
            const int N = 500;
            for (int i = 0; i < N; i++)
            {
                // even spread of directions (Fibonacci sphere)
                float y = 1f - 2f * (i + 0.5f) / N, rr = Mathf.Sqrt(1f - y * y), a = i * 2.39996323f;
                var d = new Vector3(Mathf.Cos(a) * rr, y, Mathf.Sin(a) * rr);
                float score = 0f;
                foreach (var v in dirs) { float k = Vector3.Dot(v, d); if (k > 0f) score += k * k; }
                if (score < bestScore) { bestScore = score; best = d; }
            }
            mouth = best;
            UpdateOpen();
        }

        /// <summary>Volume spilled over the rim so far (m3).</summary>
        public float Spilled { get; private set; }
        FPSBase.Vehicles.VehicleBase vehicle;
        public int SpillCount { get; private set; }

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        /// <summary>
        /// Adds a clump container to `owner`. `frame` = the transform the container moves with; `boxCenter`/`boxSize`
        /// are in `frame`'s local units (the space the old solid "mound" visual lived in). Renderers in `hide` are
        /// switched off: the clumps replace them.
        /// </summary>
        public static ClumpContainer Attach(GameObject owner, Transform frame, Vector3 boxCenter, Vector3 boxSize, float capacity,
            System.Func<MaterialLoad> source, System.Func<Vector3?> spawnAt, System.Func<Vector3> drainAt, params Renderer[] hide)
        {
            var c = owner.AddComponent<ClumpContainer>();
            c.source = source; c.spawnAt = spawnAt; c.drainAt = drainAt;
            c.frame = frame;
            c.Reshape(boxCenter, boxSize, capacity);
            c.hidden = hide;
            foreach (var r in hide) if (r != null) r.enabled = false;
            return c;
        }

        /// <summary>New box / capacity (an empty pickup bed with its tidy tank fitted or taken out).</summary>
        public void Reshape(Vector3 boxCenter, Vector3 boxSize, float cap)
        {
            Vector3 ls = frame.lossyScale;
            center = boxCenter;
            half = new Vector3(Mathf.Abs(boxSize.x * ls.x), Mathf.Abs(boxSize.y * ls.y), Mathf.Abs(boxSize.z * ls.z)) * 0.5f;
            capacity = Mathf.Max(0.01f, cap);
            maxN = Mathf.Clamp(Mathf.RoundToInt(cap * 220f), 45, 150);
            // random-packed spheres take about 7 r^3 of space each
            baseR = Mathf.Clamp(Mathf.Pow(capacity / (maxN * 7f), 1f / 3f), 0.04f, 0.3f);
            baseR = Mathf.Min(baseR, Mathf.Min(half.x, Mathf.Min(half.y, half.z)) * 0.6f);
            // a small bucket whose box is tight for its rated (heaped) load: smaller clumps, so a full load packs in
            // instead of stacking up over the rim and falling out
            float boxVol = 8f * half.x * half.y * half.z;
            float rFit = Mathf.Pow(boxVol * 1.1f / (maxN * 7f), 1f / 3f);
            baseR = Mathf.Max(0.025f, Mathf.Min(baseR, rFit));
        }

        System.Func<MaterialLoad> source;
        System.Func<Vector3?> spawnAt;
        System.Func<Vector3> drainAt;

        Vector3 Origin => frame.TransformPoint(center);
        Quaternion Rot => frame.rotation;
        Vector3 ToLocal(Vector3 world) => Quaternion.Inverse(Rot) * (world - Origin);
        Vector3 ToWorld(Vector3 local) => Origin + Rot * local;

        /// <summary>Match the clumps to the load: new ones appear at `spawnWorld` (or dropped in from the top), the ones nearest `drainWorld` leave.</summary>
        public void Sync(in MaterialLoad load, Vector3? spawnWorld, Vector3 drainWorld)
        {
            if (frame == null) return;
            foreach (var r in hidden) if (r != null && r.enabled) r.enabled = false;
            int target = load.IsEmpty ? 0 : Mathf.Clamp(Mathf.CeilToInt(load.volume / capacity * maxN), 1, maxN);
            if (target == clumps.Count) return;
            quiet = 0f;
            int guard = 0;
            while (clumps.Count < target && guard++ < 40)
            {
                // dropped in from above (a bucket dumped into a bed): they come in falling, and splash the pile.
                // Dug in (a bucket): they go in at the back of the bucket, away from the mouth
                bool fromAbove = !spawnWorld.HasValue;
                Vector3 p = !fromAbove
                    ? Clamp(-mouth * (Support * 0.35f) + Vector3.Scale(Random.insideUnitSphere, half * 0.5f), baseR)
                    : new Vector3(Random.Range(-half.x, half.x) * 0.7f, half.y + baseR * Random.Range(1f, 4f), Random.Range(-half.z, half.z) * 0.7f);
                Vector3 v0 = fromAbove ? Quaternion.Inverse(Rot) * (Vector3.down * 2.6f) + Random.insideUnitSphere * 0.8f : Random.insideUnitSphere * 0.3f;
                clumps.Add(new Clump
                {
                    p = p, v = v0, rot = Random.rotation,
                    r = baseR * Random.Range(0.8f, 1.2f), cat = DirtGrains.PickCategory(load),
                });
            }
            if (clumps.Count > target)
            {
                Vector3 d = ToLocal(drainWorld);
                while (clumps.Count > target)
                {
                    int best = 0; float bd = float.MaxValue;
                    for (int i = 0; i < clumps.Count; i++)
                    {
                        float dd = (clumps[i].p - d).sqrMagnitude;
                        if (dd < bd) { bd = dd; best = i; }
                    }
                    clumps[best] = clumps[clumps.Count - 1];
                    clumps.RemoveAt(clumps.Count - 1);
                }
            }
        }

        Vector3 Clamp(Vector3 p, float r)
        {
            p.x = Mathf.Clamp(p.x, -half.x + r, half.x - r);
            p.y = Mathf.Clamp(p.y, -half.y + r, half.y - r);
            p.z = Mathf.Clamp(p.z, -half.z + r, half.z - r);
            return p;
        }

        // ------------------------------------------------------------------ whole rocks

        class HeldRock { public Boulder b; public Vector3 p; public Quaternion q; public bool leaving; }
        readonly List<HeldRock> rocks = new();
        readonly Dictionary<Boulder, float> justDropped = new();
        Vector3 rockLastPos; bool rockHasLast;
        /// <summary>Boulders riding in this container (m^3): they take up room.</summary>
        public float HeldRockVolume { get; private set; }
        public int HeldRocks => rocks.Count;
        /// <summary>Catch rocks that fall in (and hold them while the open side faces up at least this much).</summary>
        public bool catchRocks = true;
        public float holdCos = -0.1f; // a bucket keeps its rocks until it's tipped mouth-down

        /// <summary>How much the open side faces up (1 = straight up).</summary>
        public float MouthUp => frame != null ? Vector3.Dot(Rot * mouth, Vector3.up) : 0f;
        public Vector3 Mouth => mouth;
        public Vector3 Half => half;
        float MinHalf => Mathf.Min(half.x, Mathf.Min(half.y, half.z));
        float MaxRockRadius => Mathf.Max(0.12f, MinHalf * 1.25f);

        bool Remote()
        {
            if (vehicle == null) vehicle = GetComponent<FPSBase.Vehicles.VehicleBase>();
            return vehicle != null && vehicle.NetRemote;
        }

        /// <summary>
        /// A bucket digging at `tip` with room for `capacity` m^3 in all: the nearest loose rock that fits rolls in.
        /// Returns the rock (or null).
        /// </summary>
        public Boulder Scoop(Vector3 tip, float reach, float capacity)
        {
            if (frame == null || !catchRocks || Remote()) return null;
            float used = (source != null ? source().volume : 0f) + HeldRockVolume;
            var b = Boulder.Nearest(tip, reach, capacity - used, MaxRockRadius);
            if (b == null || justDropped.ContainsKey(b)) return null;
            Take(b);
            return b;
        }

        void Take(Boulder b)
        {
            b.Hold();
            rocks.Add(new HeldRock { b = b, p = ToLocal(b.transform.position), q = Quaternion.Inverse(Rot) * b.transform.rotation });
            HeldRockVolume += b.volume;
        }

        /// <summary>
        /// Rocks ride in the container as real objects: they roll to the bottom, move with it, and slide out of
        /// the open side when it's tipped (then fall as physics rocks, into a truck bed or onto the ground).
        /// </summary>
        void UpdateRocks(float dt)
        {
            if (dt <= 0f) return;
            Vector3 pos = Origin;
            Vector3 vel = rockHasLast ? (pos - rockLastPos) / dt : Vector3.zero;
            rockLastPos = pos; rockHasLast = true;
            float up = Vector3.Dot(Rot * mouth, Vector3.up);
            if (justDropped.Count > 0)
            {
                var gone = new List<Boulder>();
                foreach (var kv in justDropped) if (kv.Key == null || Time.time > kv.Value) gone.Add(kv.Key);
                foreach (var b in gone) justDropped.Remove(b);
            }

            // rocks that fall in (dumped from a bucket into a bed, or scooped through) are caught
            if (catchRocks && up > Mathf.Min(holdCos + 0.35f, 0.95f) && !Remote())
            {
                float rMax = MaxRockRadius;
                foreach (var b in Boulder.All)
                {
                    if (b == null || b.embedded || b.Held || b.radius > rMax || justDropped.ContainsKey(b)) continue;
                    Vector3 l = ToLocal(b.transform.position);
                    float m = b.radius * 0.3f;
                    if (Mathf.Abs(l.x) > half.x + m || Mathf.Abs(l.y) > half.y + m || Mathf.Abs(l.z) > half.z + m) continue;
                    Take(b);
                    break; // one a frame is plenty
                }
            }

            if (rocks.Count == 0) return;
            Vector3 gl = Quaternion.Inverse(Rot) * Vector3.down;
            for (int i = rocks.Count - 1; i >= 0; i--)
            {
                var h = rocks[i];
                if (h.b == null) { rocks.RemoveAt(i); continue; }
                float r = h.b.radius * 0.85f;
                if (up < holdCos) h.leaving = true;
                if (h.leaving)
                {
                    // tipped: it slides out over the open side, then it's a falling rock again
                    Vector3 dir = (mouth * 0.6f + gl).normalized;
                    h.p += dir * (3f * dt);
                    bool outside = Mathf.Abs(h.p.x) > half.x + r * 0.6f || Mathf.Abs(h.p.y) > half.y + r * 0.6f || Mathf.Abs(h.p.z) > half.z + r * 0.6f;
                    if (outside)
                    {
                        h.b.transform.SetPositionAndRotation(ToWorld(h.p), Rot * h.q);
                        h.b.Drop(vel + Rot * dir * 1.5f);
                        justDropped[h.b] = Time.time + 1.2f;
                        HeldRockVolume -= h.b.volume;
                        rocks.RemoveAt(i);
                        continue;
                    }
                }
                else
                {
                    // settles to the bottom of the container (away from the open side)
                    Vector3 rest = Clamp(h.p + (gl - mouth).normalized * 0.5f, r);
                    h.p = Vector3.MoveTowards(Clamp(h.p, r), rest, 2.5f * dt);
                }
                h.b.transform.SetPositionAndRotation(ToWorld(h.p), Rot * h.q);
            }
            if (rocks.Count == 0) HeldRockVolume = 0f;
        }

        void LateUpdate()
        {
            if (frame == null) return;
            UpdateRocks(Time.deltaTime);
            if (source != null) Sync(source(), spawnAt?.Invoke(), drainAt != null ? drainAt() : Origin);
            if (clumps.Count == 0) { hasLast = false; return; }
            bool wasPacked = isPacked;
            isPacked = packed != null && packed();
            if (wasPacked != isPacked) quiet = 0f;
            float dt = Time.deltaTime;
            if (dt <= 0f) { Draw(); return; }

            // how the container is being shaken around: its acceleration acts on the clumps like extra gravity
            Vector3 pos = Origin, vel = hasLast ? (pos - lastPos) / dt : Vector3.zero;
            Vector3 acc = hasLast ? (vel - lastVel) / dt : Vector3.zero;
            if (acc.sqrMagnitude > 1600f) acc = acc.normalized * 40f; // teleports
            lastPos = pos; lastVel = vel; hasLast = true;
            // another player's machine moves in network steps: don't let that jerkiness throw its load about
            Vector3 g = Quaternion.Inverse(Rot) * (Physics.gravity - acc * (Remote() ? 0.15f : sloshGain));

            // rest when nothing is happening (most of the time): no work at all
            Vector3 gl = g.normalized;
            bool still = acc.sqrMagnitude < 0.6f;
            // a load at rest holds its heap (dirt sticks); once shaken it moves freely and sloshes
            float damp = still ? 3.5f : 0.7f, stick = still ? 0.15f : 0.06f;
            if (still && quiet > 0.6f && Vector3.Dot(gl, lastG) > 0.999f) { Draw(); return; }
            lastG = gl;

            int sub = 3;
            float h = Mathf.Min(dt, 1f / 30f) / sub;
            float maxV2 = 0f;
            for (int s = 0; s < sub; s++)
            {
                for (int i = 0; i < clumps.Count; i++)
                {
                    var c = clumps[i];
                    c.v += g * h;
                    c.v *= 1f - damp * h;
                    c.p += c.v * h;
                    Walls(ref c, g, h);
                    clumps[i] = c;
                }
                for (int i = 0; i < clumps.Count; i++)
                {
                    var a = clumps[i];
                    for (int j = i + 1; j < clumps.Count; j++)
                    {
                        var b = clumps[j];
                        Vector3 d = b.p - a.p;
                        float rs = (a.r + b.r) * 0.92f;
                        float d2 = d.sqrMagnitude;
                        if (d2 >= rs * rs || d2 < 1e-10f) continue;
                        float dist = Mathf.Sqrt(d2);
                        Vector3 n = d / dist;
                        Vector3 corr = n * ((rs - dist) * 0.5f);
                        a.p -= corr; b.p += corr;
                        Vector3 rv = b.v - a.v;
                        float vn = Vector3.Dot(rv, n);
                        if (vn < 0f) { a.v += n * (vn * 0.5f); b.v -= n * (vn * 0.5f); }
                        Vector3 vt = rv - n * Vector3.Dot(rv, n);
                        a.v += vt * stick; b.v -= vt * stick; // dirt sticks to dirt
                        clumps[j] = b;
                    }
                    clumps[i] = a;
                }
            }
            for (int i = 0; i < clumps.Count; i++)
            {
                var c = clumps[i];
                Walls(ref c, g, 0f);
                float sp = c.v.magnitude;
                maxV2 = Mathf.Max(maxV2, sp * sp);
                if (sp > 0.02f) c.rot = Quaternion.AngleAxis(sp / c.r * Mathf.Rad2Deg * dt, Vector3.Cross(Vector3.up, c.v).normalized) * c.rot;
                clumps[i] = c;
            }
            quiet = still && maxV2 < 0.01f ? quiet + dt : 0f;
            SpillOver(vel, g);
            Draw();
        }

        /// <summary>Clumps that went over the rim and out past a side leave the container as falling dirt.</summary>
        void SpillOver(Vector3 containerVel, Vector3 localG)
        {
            if (takeOut == null || source == null) return;
            // a machine another player runs: their game spills the dirt (and sends the ground), not this copy
            if (vehicle == null) vehicle = GetComponent<FPSBase.Vehicles.VehicleBase>();
            if (vehicle != null && vehicle.NetRemote) return;
            for (int i = clumps.Count - 1; i >= 0; i--)
            {
                var c = clumps[i];
                if (isPacked) break;
                // how far past an open face, and whether it is off past a closed side (slid off over the rim)
                float past = float.MinValue; bool offSide = false;
                for (int k = 0; k < 3; k++)
                {
                    float hi = c.p[k] - half[k], lo = -half[k] - c.p[k];
                    if (openHi[k]) past = Mathf.Max(past, hi); else if (hi > c.r * 0.3f) offSide = true;
                    if (openLo[k]) past = Mathf.Max(past, lo); else if (lo > c.r * 0.3f) offSide = true;
                }
                if (past <= 0f) continue;
                // gone over the side, or falling away out of the mouth (gravity / the swing carries it out)
                bool falling = past > 2f * c.r && Vector3.Dot(localG, mouth) > 0f;
                if (!offSide && !falling && past < 10f * c.r) continue;
                var load = source();
                int n = clumps.Count;
                clumps[i] = clumps[n - 1];
                clumps.RemoveAt(n - 1);
                if (load.IsEmpty || n <= 0) continue;
                var part = takeOut(load.volume / n);
                if (part.IsEmpty) continue;
                Spilled += part.volume; SpillCount++;
                Vector3 wp = ToWorld(c.p), wv = containerVel + Rot * c.v;
                DirtGrains.Pour(wp, part, transform, wv);
            }
        }

        Vector3 lastG;

        [Tooltip("Dirt-on-steel friction: the load holds against gentle starts and stops and slides on hard ones.")]
        public float wallFriction = 0.55f;
        [Tooltip("How much of a hard hit on a wall surges up it (the load sloshing up and over the front on a hard stop).")]
        public float runUp = 0.75f;

        void Walls(ref Clump c, Vector3 g, float h)
        {
            // the open side (the mouth / the top of a bed) has no wall. Once a clump is out past it, the other walls
            // don't hold it either: it can slide off over the rim. Packed in (digging): nothing leaves
            bool open = !isPacked;
            if (open)
                for (int k = 0; k < 3; k++)
                    if ((openHi[k] && c.p[k] - c.r * 0.5f > half[k]) || (openLo[k] && c.p[k] + c.r * 0.5f < -half[k])) return;
            for (int k = 0; k < 3; k++)
            {
                float lo = open && openLo[k] ? float.MinValue : -half[k] + c.r * 0.7f;
                float hi = open && openHi[k] ? float.MaxValue : half[k] - c.r * 0.7f;
                if (c.p[k] < lo) { c.p[k] = lo; if (c.v[k] < 0f) Hit(ref c, k, -1f, g, h); }
                else if (c.p[k] > hi) { c.p[k] = hi; if (c.v[k] > 0f) Hit(ref c, k, 1f, g, h); }
            }
        }

        /// <summary>A clump moving into the wall on `axis` (side `sgn`): bounce a little, surge up it, rub along it.</summary>
        void Hit(ref Clump c, int axis, float sgn, Vector3 g, float h)
        {
            float impact = Mathf.Abs(c.v[axis]);
            c.v[axis] *= -0.2f;
            // a hard hit on a side wall (not the floor) throws the load up it, like a wave hitting a wall
            var n = Vector3.zero; n[axis] = sgn;
            if (Mathf.Abs(Vector3.Dot(n, mouth)) < 0.5f && impact > 0.9f)
                c.v += mouth * ((impact - 0.9f) * runUp);
            // Coulomb friction: slows the sliding by mu x how hard it is pressed on the wall
            float press = Mathf.Max(0f, Vector3.Dot(g, n)) * h + impact * 0.3f;
            Vector3 vt = c.v; vt[axis] = 0f;
            float sp = vt.magnitude;
            if (sp < 1e-5f) return;
            float slow = Mathf.Min(sp, wallFriction * press);
            for (int k = 0; k < 3; k++) if (k != axis) c.v[k] -= vt[k] / sp * slow;
        }

        readonly List<Matrix4x4>[] lists = { new(), new(), new(), new() };

        void Draw()
        {
            var cam = TuftRenderer.ActiveCamera();
            if (cam != null && (cam.transform.position - Origin).sqrMagnitude > 160f * 160f) return;
            foreach (var l in lists) l.Clear();
            Quaternion fr = Rot;
            Vector3 o = Origin;
            foreach (var c in clumps)
                lists[c.cat].Add(Matrix4x4.TRS(o + fr * c.p, fr * c.rot, new Vector3(c.r * 1.15f, c.r * 0.9f, c.r * 1.05f)));
            var b = new Bounds(o, Vector3.one * (half.magnitude * 2f + 2f));
            for (int k = 0; k < lists.Length; k++)
                if (lists[k].Count > 0) DirtGrains.DrawInstanced(k, lists[k], b, true);
        }
    }
}
