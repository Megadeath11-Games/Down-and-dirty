using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSBase.Mining
{
    /// <summary>
    /// Loose dirt in flight. Everything poured out of a bucket, a truck bed or knocked off a collapsing wall becomes
    /// a stream of clods that really fall: they bounce off machines, rattle into truck beds and hoppers, spill over
    /// the sides when a bed is full, roll down steep slopes and only join the ground (the dig site's loose layer)
    /// where they finally stop. Each clod carries its share of the load (volume, gold, sand/gravel/clay), so nothing
    /// is lost on the way. Material matters: sand pours fine and runs far, clay comes out in sticky lumps that stop
    /// where they land (and break apart when they hit hard), gravel bounces and rattles.
    /// Simulated on the CPU against the dig-site heightfield (ground) plus physics raycasts (everything else);
    /// drawn with GPU instancing.
    /// </summary>
    public class DirtGrains : MonoBehaviour
    {
        public const int MaxGrains = 4000;
        const int MaxPerPour = 40;
        const float MaxStepDt = 1f / 30f;

        struct Grain
        {
            public Vector3 p, v, spinAxis;
            public Quaternion rot;
            public float r, spin, rest, age, bodyRest;
            public float mu, bounce, settleTime;
            public MaterialLoad load;
            public Transform ignore;
            public byte cat;
            public bool visual, clod;
            public bool pushed;   // cut by a blade: part of the heap rolling in front of it
            public float shoved;  // seconds since a blade last shoved it
            public bool onClod;   // sitting on other clods (last step)
        }

        // ------------------------------------------------------------------ blades pushing clods

        /// <summary>A blade face this frame: clods behind it are shoved along in front of it.</summary>
        struct Pusher { public Vector3 edge, fwd, right, vel; public float halfW, fullW, height, life, t; }
        readonly List<Pusher> pushers = new();
        readonly List<Pusher> recent = new(); // blades that stopped a moment ago (clods left on the machine still go off its front)

        /// <summary>
        /// A blade (dozer, grader, loader, excavator dozer blade) is working here: its face runs `width` across at
        /// `edge` (its cutting edge), facing `forward`, `height` tall. Called every step it works; its speed is
        /// worked out from where it was last time. Clods behind the face get shoved along at the blade's speed.
        /// </summary>
        public static void Blade(Vector3 edge, Vector3 forward, float width, float height, float fullWidth = 0f)
        {
            var me = Instance;
            forward.y = 0f; if (forward.sqrMagnitude < 1e-6f) return; forward.Normalize();
            float now = (Time.inFixedTimeStep ? Time.fixedTime : Time.time) + testClock;
            int best = -1; float bd = 0.5f;
            for (int k = 0; k < me.pushers.Count; k++)
            {
                if (me.pushers[k].t >= now) continue; // (already moved on this step: another part of the same blade)
                float d = Vector3.Distance(me.pushers[k].edge, edge);
                if (d < bd && Vector3.Dot(me.pushers[k].fwd, forward) > 0.7f) { bd = d; best = k; }
            }
            var p = new Pusher { edge = edge, fwd = forward, right = new Vector3(forward.z, 0f, -forward.x), halfW = width * 0.5f, fullW = fullWidth > 0f ? fullWidth : width, height = height, life = 0.15f, t = now };
            if (best >= 0)
            {
                var o = me.pushers[best];
                float dt = now - o.t;
                p.vel = dt > 1e-4f ? Vector3.Lerp(o.vel, (edge - o.edge) / dt, 0.5f) : o.vel;
                me.pushers[best] = p;
            }
            else me.pushers.Add(p);
        }

        /// <summary>
        /// Dirt a blade just cut (`load`, from a column of ground `cellSize` across centred at `at`, between
        /// `bottom` and `top`): it becomes clods in place that the blade then pushes. False if there's no room for
        /// more clods (the caller drops it the old way).
        /// </summary>
        public static bool Cut(Vector3 at, float cellSize, float bottom, float top, MaterialLoad load, Transform ignore, bool visual = false, float minClod = 0.006f)
        {
            if (load.IsEmpty) return true;
            var me = Instance;
            Traits(load, out float gv, out float mu, out float bounce, out _, out _, out bool clod);
            gv = Mathf.Max(gv, minClod); // (a big blade's clods are bigger: fewer of them for the same heap)
            int n = Mathf.Clamp(Mathf.CeilToInt(load.volume / gv), 1, 24);
            if (me.count + n > MaxGrains - 300) return false;
            var each = load.Scaled(1f / n);
            float r = RadiusOf(each.volume);
            for (int i = 0; i < n; i++)
            {
                me.g[me.count++] = new Grain
                {
                    p = new Vector3(at.x + Random.Range(-0.5f, 0.5f) * cellSize, Mathf.Lerp(bottom + r, Mathf.Max(bottom + r, top), Random.value), at.z + Random.Range(-0.5f, 0.5f) * cellSize),
                    v = Vector3.zero,
                    rot = Random.rotation, spinAxis = Random.onUnitSphere, spin = 0f,
                    r = r * Random.Range(0.85f, 1.15f), mu = mu, bounce = bounce * 0.5f, settleTime = 0.45f,
                    load = visual ? default : each, ignore = ignore, cat = PickCategory(load), clod = false, visual = visual, pushed = true,
                };
            }
            return true;
        }

        /// <summary>The spot just in front of the nearest blade face (for a clod that landed on the machine behind it).</summary>
        bool FrontOfBlade(Vector3 p, out Vector3 front)
        {
            front = p; float best = 36f; bool found = false;
            for (int k = 0; k < pushers.Count + recent.Count; k++)
            {
                var b = k < pushers.Count ? pushers[k] : recent[k - pushers.Count];
                Vector3 d = p - b.edge; d.y = 0f;
                if (d.sqrMagnitude > best) continue;
                best = d.sqrMagnitude; found = true;
                float lat = Mathf.Clamp(Vector3.Dot(d, b.right), -b.halfW, b.halfW);
                front = b.edge + b.right * lat + b.fwd * Random.Range(0.3f, 0.9f) + Vector3.up * (b.height * 0.6f);
            }
            return found;
        }

        /// <summary>Is a blade working within `radius` of `p` (this step)?</summary>
        public static bool NearBlade(Vector3 p, float radius)
        {
            if (inst == null) return false;
            foreach (var b in inst.pushers) { Vector3 d = p - b.edge; d.y = 0f; if (d.sqrMagnitude < (radius + b.halfW) * (radius + b.halfW)) return true; }
            return false;
        }

        /// <summary>The clod size the nearest working blade makes (as on its driver's game).</summary>
        public static float NearBladeClod(Vector3 p)
        {
            float best = float.MaxValue, w = 2.5f;
            if (inst != null) foreach (var b in inst.pushers) { float d = (p - b.edge).sqrMagnitude; if (d < best) { best = d; w = b.fullW; } }
            return Mathf.Clamp(w * 0.004f, 0.006f, 0.022f);
        }

        /// <summary>m^3 (and gold) of real clods within `radius` of `center` across the ground (a heap in front of a blade).</summary>
        public static float VolumeNear(Vector3 center, float radius, out float gold)
        {
            gold = 0f;
            if (inst == null) return 0f;
            float v = 0f, r2 = radius * radius;
            for (int i = 0; i < inst.count; i++)
            {
                ref Grain q = ref inst.g[i];
                if (q.visual) continue;
                float dx = q.p.x - center.x, dz = q.p.z - center.z;
                if (dx * dx + dz * dz > r2 || Mathf.Abs(q.p.y - center.y) > 3f) continue;
                v += q.load.volume; gold += q.load.goldGrams;
            }
            return v;
        }

        /// <summary>Shove every clod that's behind a blade face out in front of it, at the blade's speed.</summary>
        void ApplyPushers(float dt)
        {
            for (int k = recent.Count - 1; k >= 0; k--) { var o = recent[k]; o.life -= dt; if (o.life <= 0f) recent.RemoveAt(k); else recent[k] = o; }
            for (int k = pushers.Count - 1; k >= 0; k--)
            {
                var b = pushers[k];
                b.life -= dt;
                if (b.life <= 0f) { b.life = 4f; recent.Add(b); pushers.RemoveAt(k); continue; }
                pushers[k] = b;
                float speed = Vector3.Dot(b.vel, b.fwd);
                for (int i = 0; i < count; i++)
                {
                    ref Grain q = ref g[i];
                    Vector3 rel = q.p - b.edge;
                    float up = rel.y;
                    if (up < -0.3f || up > b.height + 1.6f) continue; // (higher than the blade: still kept off the machine)
                    float lat = Vector3.Dot(rel, b.right);
                    if (Mathf.Abs(lat) > b.halfW + q.r * 0.5f) continue;
                    float lon = Vector3.Dot(rel, b.fwd);
                    if (lon > q.r || lon < -0.9f) continue;
                    // behind (or in) the face: out in front of it, moving with it
                    q.p += b.fwd * (q.r - lon);
                    float vf = Vector3.Dot(q.v, b.fwd);
                    float want = Mathf.Max(0f, speed) * Random.Range(1.0f, 1.12f);
                    if (vf < want) q.v += b.fwd * (want - vf);
                    // the heap rolls: low clods ride up the face, some tumble off the ends
                    if (up < b.height * 0.8f) q.v += Vector3.up * (Mathf.Max(0f, speed) * 0.25f * Random.value);
                    q.v += b.right * (Random.Range(-0.5f, 0.5f) * Mathf.Max(0f, speed));
                    // the heap is taller than the blade: the top rolls forward and down its front
                    if (up > b.height * 0.85f) q.v += b.fwd * Mathf.Max(0.6f, speed) * 0.6f;
                    q.rest = 0f; q.shoved = 0f; q.pushed = true; q.age = Mathf.Min(q.age, 1f);
                }
            }
        }

        // ------------------------------------------------------------------ clods resting on clods

        const int TableSize = 8192; // (cells are hashed into this many slots; a shared slot only costs a few extra checks)
        readonly int[] slotHead = new int[TableSize];
        readonly int[] slotStamp = new int[TableSize];
        int stamp;
        readonly int[] nextInCell = new int[MaxGrains];
        readonly int[] cellX = new int[MaxGrains], cellY = new int[MaxGrains], cellZ = new int[MaxGrains];
        readonly Vector3[] cp = new Vector3[MaxGrains];
        readonly float[] cr = new float[MaxGrains];

        static int Slot(int x, int y, int z) => (int)(((uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791)) & (TableSize - 1));

        /// <summary>
        /// Clods in a heap push each other apart, so they pile up (instead of all sharing one spot on the ground) and
        /// roll off each other at the sides. A clod sitting on others counts as resting, so a heap left alone settles.
        /// The grid is sized to the clods about (a dense heap of small ones has small cells, so each looks at few);
        /// the odd big lump looks further out for what it touches.
        /// </summary>
        void CollideClods()
        {
            stamp++;
            int live = 0;
            float sumR = 0f, maxR = 0f;
            for (int i = 0; i < count; i++)
            {
                ref Grain q = ref g[i];
                q.onClod = false;
                if (!q.pushed && q.age < 0.25f) { nextInCell[i] = -2; continue; } // still falling out of a bucket
                nextInCell[i] = -1;
                live++; sumR += q.r;
                if (q.r > maxR) maxR = q.r;
            }
            if (live < 2) return;
            float small = Mathf.Clamp(sumR / live * 1.6f, 0.05f, 0.3f); // clods up to this size are "small" (nearly all)
            float H = 1.64f * small;                                     // two small ones touching are within one cell
            float inv = 1f / H;
            for (int i = 0; i < count; i++)
            {
                if (nextInCell[i] == -2) continue;
                ref Grain q = ref g[i];
                cp[i] = q.p; cr[i] = q.r;
                int x = Mathf.FloorToInt(q.p.x * inv), y = Mathf.FloorToInt(q.p.y * inv), z = Mathf.FloorToInt(q.p.z * inv);
                cellX[i] = x; cellY[i] = y; cellZ[i] = z;
                int sl = Slot(x, y, z);
                nextInCell[i] = slotStamp[sl] == stamp ? slotHead[sl] : -1;
                slotStamp[sl] = stamp; slotHead[sl] = i;
            }
            for (int i = 0; i < count; i++)
            {
                if (nextInCell[i] == -2) continue;
                ref Grain a = ref g[i];
                bool aBig = a.r > small;
                // small: the 27 cells round it, for other small ones (a big one finds it from its own side)
                // big: as far as it can reach, for everything (other big ones only once, from the lower number)
                int k = aBig ? Mathf.Min(4, Mathf.CeilToInt((a.r + maxR) * 0.82f * inv)) : 1;
                int cx = cellX[i], cy = cellY[i], cz = cellZ[i];
                for (int oz = -k; oz <= k; oz++)
                    for (int oy = -k; oy <= k; oy++)
                        for (int ox = -k; ox <= k; ox++)
                        {
                            int tx = cx + ox, ty = cy + oy, tz = cz + oz;
                            int sl = Slot(tx, ty, tz);
                            if (slotStamp[sl] != stamp) continue;
                            for (int j = slotHead[sl]; j >= 0; j = nextInCell[j])
                            {
                                if (j == i || cellX[j] != tx || cellY[j] != ty || cellZ[j] != tz) continue;
                                float rb = cr[j];
                                bool bBig = rb > small;
                                if (aBig ? (bBig && j < i) : (bBig || j < i)) continue;
                                // (a quick look from the packed copy first: most pairs aren't touching)
                                float rr = (a.r + rb) * 0.82f;
                                Vector3 dd = a.p - cp[j];
                                if (dd.x * dd.x + dd.y * dd.y + dd.z * dd.z >= rr * rr) continue;
                                Bump(ref a, ref g[j]);
                                cp[j] = g[j].p;
                            }
                        }
            }
        }


        static void Bump(ref Grain a, ref Grain b)
        {
            Vector3 d = a.p - b.p;
            float rr = (a.r + b.r) * 0.82f; // (lumpy: they nest into each other a little)
            float d2 = d.sqrMagnitude;
            if (d2 >= rr * rr || d2 < 1e-10f) return;
            float dist = Mathf.Sqrt(d2);
            Vector3 n = d / dist;
            float push = (rr - dist) * 0.5f;
            a.p += n * push; b.p -= n * push;
            Vector3 vr = a.v - b.v;
            float vn = Vector3.Dot(vr, n);
            if (vn < 0f) { a.v -= n * (vn * 0.5f); b.v += n * (vn * 0.5f); }
            Vector3 vt = vr - n * Vector3.Dot(vr, n);
            a.v -= vt * 0.12f; b.v += vt * 0.12f; // rough: they drag each other along
            if (n.y > 0.45f) a.onClod = true; else if (n.y < -0.45f) b.onClod = true;
        }

        static DirtGrains inst;
        readonly Grain[] g = new Grain[MaxGrains];
        int count;
        int spraysThisFrame;
        readonly RaycastHit[] hits = new RaycastHit[8];
        readonly Collider[] overlaps = new Collider[12];

        public static int Live => inst == null ? 0 : inst.count;
        /// <summary>Clods from a friend's blade, shown on this screen (their game has the real ones).</summary>
        public static int LivePushedVisual { get { if (inst == null) return 0; int n = 0; for (int i = 0; i < inst.count; i++) if (inst.g[i].visual && inst.g[i].pushed) n++; return n; } }
        /// <summary>Real clods a blade is pushing on this game.</summary>
        public static int LivePushed { get { if (inst == null) return 0; int n = 0; for (int i = 0; i < inst.count; i++) if (!inst.g[i].visual && inst.g[i].pushed) n++; return n; } }
        /// <summary>m^3 of dirt currently in the air (not yet landed anywhere).</summary>
        public static float InFlight
        {
            get
            {
                if (inst == null) return 0f;
                float v = 0f;
                for (int i = 0; i < inst.count; i++) v += inst.g[i].load.volume;
                return v;
            }
        }

        static DirtGrains Instance
        {
            get
            {
                if (inst != null) return inst;
                var go = new GameObject("DirtGrains");
                inst = go.AddComponent<DirtGrains>();
                return inst;
            }
        }

        void OnDestroy() { if (inst == this) inst = null; }

        // ------------------------------------------------------------------ spawning

        /// <summary>Kind-dependent behaviour: size of a clod, sliding friction, bounce, how long it takes to stop.</summary>
        static void Traits(in MaterialLoad l, out float grainVol, out float mu, out float bounce, out float settle, out byte cat, out bool clod)
        {
            float s = l.SandF, gr = l.GravelF, c = l.ClayF;
            grainVol = 0.008f + c * 0.02f - s * 0.005f + gr * 0.002f; // clay comes in lumps, sand in fine pours
            mu = 0.72f + c * 0.65f - s * 0.2f + gr * 0.06f;           // tan(slope) it can rest on
            bounce = Mathf.Clamp(0.12f + gr * 0.22f - c * 0.1f, 0.02f, 0.35f);
            settle = Mathf.Lerp(0.12f, 0.02f, c);
            clod = s < 0.5f;
            cat = c > 0.45f ? (byte)2 : s > 0.5f ? (byte)1 : gr > 0.45f ? (byte)3 : (byte)0;
        }

        static float RadiusOf(float vol) => Mathf.Clamp(Mathf.Pow(vol * 0.2387f, 1f / 3f), 0.035f, 0.4f);

        /// <summary>
        /// Pour a load from a point (a bucket lip, a tailgate). It falls as clods and lands wherever physics takes it.
        /// `ignore` = the machine doing the pouring (its own colliders don't catch the stream for the first moment).
        /// </summary>
        /// `grainR` = the size of the clods it pours (a bucket's own clumps: they come out as big as they were in it).
        public static void Pour(Vector3 from, MaterialLoad load, Transform ignore, Vector3 velocity = default, float grainR = 0f)
        {
            if (load.IsEmpty) return;
            WorkSounds.Pour(from, load.volume);
            Instance.Spawn(from, load, ignore, velocity, Mathf.Clamp(Mathf.Pow(load.volume, 1f / 3f) * 0.35f, 0.08f, 0.6f), false, grainR);
        }

        /// <summary>
        /// A pour that's only for show: another player's game poured the real load (and sends the ground it made),
        /// this screen just draws it falling. The clods vanish a moment after they land.
        /// </summary>
        public static void PourVisual(Vector3 from, MaterialLoad load, Transform ignore, Vector3 velocity = default)
        {
            if (load.IsEmpty) return;
            WorkSounds.Pour(from, load.volume);
            Instance.Spawn(from, load, ignore, velocity, Mathf.Clamp(Mathf.Pow(load.volume, 1f / 3f) * 0.35f, 0.08f, 0.6f), true);
        }

        /// <summary>Dirt falling off something (a wall that caved in): starts at `from` with `velocity`.</summary>
        public static void Fall(Vector3 from, MaterialLoad load, Vector3 velocity, float spread = 0.25f)
        {
            if (load.IsEmpty) return;
            Instance.Spawn(from, load, null, velocity, spread);
        }

        /// <summary>A purely visual bit of dirt (kicked up, sliding down a face). Carries no material.</summary>
        public static void Spray(Vector3 at, Vector3 velocity, int cat = 0, float size = 0.06f)
        {
            var me = Instance;
            if (me.count >= MaxGrains - 200 || me.spraysThisFrame > 90) return;
            me.spraysThisFrame++;
            me.g[me.count++] = new Grain
            {
                p = at, v = velocity, rot = Random.rotation, spinAxis = Random.onUnitSphere, spin = Random.Range(200f, 700f),
                r = size, mu = 0.8f, bounce = 0.15f, settleTime = 0.05f, visual = true, cat = (byte)cat,
            };
        }

        void Spawn(Vector3 from, MaterialLoad load, Transform ignore, Vector3 vel, float spread, bool visual = false, float grainR = 0f)
        {
            Traits(load, out float gv, out float mu, out float bounce, out float settle, out byte cat, out bool clod);
            if (grainR > 0f) gv = Mathf.Max(1e-5f, 4.18879f * grainR * grainR * grainR); // (a ball of that size)
            int n = Mathf.Clamp(Mathf.CeilToInt(load.volume / gv), 1, MaxPerPour);
            if (visual && count + n > MaxGrains - 200) return; // busy: skip the show
            if (count + n > MaxGrains)
            {
                // too much in the air already: this bit lands straight away (old behaviour)
                MaterialTransfer.PourInstant(from, load, ignore);
                return;
            }
            var each = load.Scaled(1f / n);
            float r = RadiusOf(each.volume);
            for (int i = 0; i < n; i++)
            {
                Vector2 d = Random.insideUnitCircle * spread;
                g[count++] = new Grain
                {
                    p = from + new Vector3(d.x, Random.Range(-0.08f, 0.08f), d.y),
                    v = vel + new Vector3(Random.Range(-0.35f, 0.35f), Random.Range(-0.6f, 0f), Random.Range(-0.35f, 0.35f)),
                    rot = Random.rotation, spinAxis = Random.onUnitSphere, spin = Random.Range(60f, 400f),
                    r = r * Random.Range(0.85f, 1.15f), mu = mu, bounce = bounce, settleTime = settle,
                    load = each, ignore = ignore, cat = PickCategory(load), clod = clod && each.volume > 0.006f && !visual, visual = visual,
                };
            }
        }

        // ------------------------------------------------------------------ simulation

        void Update()
        {
            spraysThisFrame = 0;
            float dt = Time.deltaTime;
            if (dt > 0f)
            {
                // two half steps when the frame is long: fast clods shouldn't skip through a thin truck side
                int sub = dt > MaxStepDt ? 2 : 1;
                for (int s = 0; s < sub; s++) Step(Mathf.Min(dt / sub, MaxStepDt));
            }
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            Draw();
            DrawMs += (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        }

        static float testClock;

        /// <summary>Tests: run the clods `dt` on (and move the clock the blades are timed by), without waiting for a frame.</summary>
        public static void Tick(float dt)
        {
            testClock += dt;
            if (inst == null) return;
            Physics.SyncTransforms();
            inst.Step(dt);
        }

        /// <summary>Run the simulation until everything has landed (used by tests and SettleNow).</summary>
        public static void FastForward(float maxSeconds = 30f)
        {
            if (inst == null || inst.count == 0) return;
            Physics.SyncTransforms();
            for (float t = 0f; t < maxSeconds && inst.count > 0; t += 1f / 60f) inst.Step(1f / 60f);
        }

        /// <summary>Time spent moving clods (for the frame-rate readout).</summary>
        public static double StepMs, PushersMs, CollideMs, DrawMs;

        void Step(float dt)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try { StepCore(dt); }
            finally { StepMs += (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency; }
        }

        int stepNo;

        void StepCore(float dt)
        {
            stepNo++;
            Vector3 grav = Physics.gravity * dt;
            for (int i = 0; i < count; i++)
            {
                ref Grain q = ref g[i];
                var site = DigSite.At(q.p);
                q.age += dt;
                q.shoved += dt;
                bool held = q.pushed && q.shoved < 0.3f; // being pushed along by a blade: don't settle it yet
                q.v += grav;
                q.v *= 1f - 0.05f * dt; // air drag
                Vector3 np = q.p + q.v * dt;
                bool gone = false, onSomething = false;

                // ---- machines, buildings, rocks, truck beds, hoppers...
                // (a slow clod looks for machines every third step: it moves far less than its own size meanwhile)
                if (!q.visual && (q.v.sqrMagnitude > 0.09f || ((i + stepNo) % 3) == 0))
                {
                    Vector3 d = np - q.p;
                    float len = d.magnitude;
                    if (len > 1e-5f && Cast(ref q, d / len, len + q.r, out RaycastHit h, out IMaterialReceiver rec))
                    {
                        if (rec != null && TryGive(rec, ref q)) gone = true;
                        else
                        {
                            onSomething = true;
                            Vector3 n = h.normal;
                            float vn = Vector3.Dot(q.v, n);
                            if (vn < 0f)
                            {
                                if (h.rigidbody != null && !h.rigidbody.isKinematic)
                                    h.rigidbody.AddForceAtPosition(n * (vn * (1f + q.bounce) * q.load.volume * 1600f), h.point, ForceMode.Impulse);
                                q.v -= (1f + q.bounce) * vn * n;
                            }
                            Friction(ref q, n, dt);
                            np = h.point + n * (q.r * 1.02f);
                            // a clod that has stopped on something that isn't a receiver: on a slope it slides off,
                            // flat ground outside the dig site makes a pile, anything else it eventually tips off
                            if (q.v.sqrMagnitude < 0.25f) q.bodyRest += dt; else q.bodyRest = 0f;
                            bool staticGround = h.rigidbody == null && n.y > 0.75f && (site == null || !site.Contains(h.point));
                            if (staticGround && q.bodyRest > 0.15f) { DigSite.DropAt(h.point, q.load); gone = true; }
                            else if (q.bodyRest > (q.pushed ? 0.3f : 1.2f))
                            {
                                if (q.pushed && h.rigidbody != null && FrontOfBlade(np, out Vector3 front)) { q.p = front; q.v = Vector3.zero; q.bodyRest = 0f; np = front; }
                                else { DropBelow(np, ref q); gone = true; }
                            }
                        }
                    }
                }
                if (!gone)
                {
                    q.p = np;
                    // ---- the dirt field
                    if (site != null && site.Contains(q.p))
                    {
                        float s = site.SurfaceY(q.p);
                        float sink = q.r * 0.55f; // clods settle into the dirt a little
                        if (q.p.y - sink < s)
                        {
                            Vector3 n = site.SurfaceNormal(q.p);
                            q.p.y = s + sink;
                            float vn = Vector3.Dot(q.v, n);
                            if (vn < 0f)
                            {
                                if (q.clod && !q.visual && -vn > 4.5f && count < MaxGrains - 4) Break(i, n);
                                else q.v -= (1f + q.bounce) * vn * n;
                                if (-vn > 2.5f && Random.value < 0.25f) Spray(q.p, Vector3.Reflect(q.v, n) * 0.4f + Random.insideUnitSphere * 0.8f, q.cat, q.r * 0.4f);
                            }
                            Friction(ref q, n, dt);
                            onSomething = true;
                            if (q.v.sqrMagnitude < 0.04f) q.rest += dt; else q.rest = 0f;
                            if (!held && (q.rest > q.settleTime || q.age > 10f))
                            {
                                if (!q.visual) Land(site, ref q);
                                gone = true;
                            }
                        }
                    }
                    else if (q.p.y < -60f) gone = true;
                    else if (q.visual && q.age > 3f && !held) gone = true;
                    // resting on other clods in a heap: it joins the ground where it sits (the heap settles)
                    if (!gone && q.onClod && !held && q.pushed && site != null && site.Contains(q.p))
                    {
                        if (q.v.sqrMagnitude < 0.06f) q.rest += dt; else q.rest = 0f;
                        if (q.rest > q.settleTime + 0.25f) { if (!q.visual) Land(site, ref q); gone = true; }
                    }
                    if (!gone && q.age > 14f && !held) { if (!q.visual) DropBelow(q.p, ref q); gone = true; }
                }

                if (gone)
                {
                    g[i] = g[--count];
                    i--;
                    continue;
                }
                // tumble while flying, roll while on the ground
                float sp = onSomething ? q.v.magnitude / Mathf.Max(0.03f, q.r) * Mathf.Rad2Deg : q.spin;
                if (onSomething && q.v.sqrMagnitude > 1e-4f) q.spinAxis = Vector3.Cross(Vector3.up, q.v).normalized;
                q.rot = Quaternion.AngleAxis(sp * dt, q.spinAxis) * q.rot;
            }
            long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            if (pushers.Count > 0 || recent.Count > 0) ApplyPushers(dt);
            long t2 = System.Diagnostics.Stopwatch.GetTimestamp();
            CollideClods();
            long t3 = System.Diagnostics.Stopwatch.GetTimestamp();
            double f = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            PushersMs += (t2 - t1) * f; CollideMs += (t3 - t2) * f;
        }

        /// <summary>Coulomb-style friction: gravity along a slope steeper than atan(mu) keeps it sliding, gentler slopes stop it.</summary>
        static void Friction(ref Grain q, Vector3 n, float dt)
        {
            Vector3 vn = Vector3.Dot(q.v, n) * n;
            Vector3 vt = q.v - vn;
            float sp = vt.magnitude;
            float dec = q.mu * 9.81f * Mathf.Max(0.3f, n.y) * dt * 1.15f;
            vt = sp <= dec ? Vector3.zero : vt * (1f - dec / sp);
            q.v = vt + (Vector3.Dot(vn, n) > 0f ? vn : Vector3.zero);
        }

        /// <summary>A hard landing breaks a lump into a few smaller ones that scatter.</summary>
        void Break(int i, Vector3 n)
        {
            var q = g[i];
            int parts = Random.Range(2, 4);
            var each = q.load.Scaled(1f / parts);
            float r = RadiusOf(each.volume);
            Vector3 bounceV = Vector3.Reflect(q.v, n) * 0.25f;
            for (int k = 0; k < parts; k++)
            {
                var c = q;
                c.load = each; c.r = r; c.clod = false;
                c.v = bounceV + Vector3.ProjectOnPlane(Random.insideUnitSphere, n) * 1.6f + n * Random.Range(0.3f, 1.2f);
                c.rot = Random.rotation; c.spinAxis = Random.onUnitSphere;
                if (k == 0) g[i] = c; else g[count++] = c;
            }
        }

        bool Cast(ref Grain q, Vector3 dir, float dist, out RaycastHit best, out IMaterialReceiver receiver)
        {
            best = default; receiver = null;
            int n = Physics.RaycastNonAlloc(q.p, dir, hits, dist, ~0, QueryTriggerInteraction.Collide);
            float bd = float.MaxValue; bool found = false;
            for (int k = 0; k < n; k++)
            {
                var h = hits[k];
                if (h.distance >= bd || h.distance <= 0f) continue;
                var c = h.collider;
                if (DigSite.IsSiteCollider(c)) continue;
                if (q.ignore != null && q.age < 1.5f && c.transform.IsChildOf(q.ignore)) continue;
                IMaterialReceiver rec = null;
                if (c.isTrigger)
                {
                    rec = c.GetComponentInParent<IMaterialReceiver>();
                    if (rec == null || !rec.AcceptsAt(h.point) || rec.FreeSpace <= 1e-4f) continue;
                }
                bd = h.distance; best = h; found = true; receiver = rec;
            }
            if (found && receiver == null) receiver = ReceiverAt(best.point);
            return found;
        }

        /// <summary>A receiver (truck bed, hopper, yard bay, pile) whose catch area contains `p`, with room left.</summary>
        IMaterialReceiver ReceiverAt(Vector3 p)
        {
            int n = Physics.OverlapSphereNonAlloc(p, 0.35f, overlaps, ~0, QueryTriggerInteraction.Collide);
            for (int k = 0; k < n; k++)
            {
                var rec = overlaps[k].GetComponentInParent<IMaterialReceiver>();
                if (rec != null && rec.AcceptsAt(p) && rec.FreeSpace > 1e-4f) return rec;
            }
            return null;
        }

        /// <summary>Give the clod to a receiver. False if it only took part (or none) of it.</summary>
        static bool TryGive(IMaterialReceiver rec, ref Grain q)
        {
            float room = rec.FreeSpace;
            if (room <= 1e-4f) return false;
            if (room >= q.load.volume) { rec.Receive(q.load); return true; }
            rec.Receive(q.load.Take(room));
            q.r = RadiusOf(q.load.volume);
            return q.load.IsEmpty;
        }

        void Land(DigSite site, ref Grain q)
        {
            var rec = ReceiverAt(q.p); // e.g. the sand & gravel yard's bay sits on the dirt field
            if (rec != null && TryGive(rec, ref q)) return;
            site.Land(q.p, q.load);
        }

        void DropBelow(Vector3 p, ref Grain q)
        {
            if (q.load.IsEmpty) return;
            MaterialTransfer.FindReceiverBelow(p, q.ignore, out Vector3 ground);
            var site = DigSite.At(p);
            if (site != null && site.Contains(p)) ground = new Vector3(p.x, site.SurfaceY(p), p.z);
            DigSite.DropAt(ground, q.load);
        }

        // ------------------------------------------------------------------ drawing

        static Mesh mesh;
        static Material[] mats;
        readonly List<Matrix4x4>[] lists = { new(), new(), new(), new() };
        static readonly Matrix4x4[] batch = new Matrix4x4[1023];

        void Draw()
        {
            if (count == 0) return;
            foreach (var l in lists) l.Clear();
            for (int i = 0; i < count; i++)
            {
                ref Grain q = ref g[i];
                Vector3 sc = new Vector3(q.r * 1.1f, q.r * 0.85f, q.r);
                lists[q.cat].Add(Matrix4x4.TRS(q.p, q.rot, sc));
            }
            var bounds = new Bounds(Vector3.zero, Vector3.one * 4000f);
            for (int c = 0; c < lists.Length; c++) DrawInstanced(c, lists[c], bounds, true);
        }

        /// <summary>Draw clods of one material category (0 dirt, 1 sand, 2 clay, 3 gravel). Shared with buckets, beds and heaps.</summary>
        public static void DrawInstanced(int cat, List<Matrix4x4> list, Bounds bounds, bool shadows)
        {
            if (list.Count == 0) return;
            if (mesh == null) mesh = BuildMesh();
            if (mats == null) BuildMaterials();
            if (mats[cat] == null) return;
            var rp = new RenderParams(mats[cat]) { shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off, receiveShadows = true, worldBounds = bounds };
            for (int s = 0; s < list.Count; s += batch.Length)
            {
                int n = Mathf.Min(batch.Length, list.Count - s);
                list.CopyTo(s, batch, 0, n);
                Graphics.RenderMeshInstanced(rp, mesh, 0, batch, n);
            }
        }

        /// <summary>A material category for one clod, picked at random by the load's make-up.</summary>
        public static byte PickCategory(in MaterialLoad l)
        {
            if (l.IsEmpty) return 0;
            float r = Random.value * l.volume;
            if ((r -= l.clay) < 0f) return 2;
            if ((r -= l.sand) < 0f) return 1;
            if ((r -= l.gravel) < 0f) return 3;
            return 0;
        }

        static void BuildMaterials()
        {
            mats = new Material[4];
            var site = DigSite.Instance;
            int[] src = { 2, 8, 9, 10 }; // looseA, sand, clay, gravel in the site's surface material list
            for (int c = 0; c < 4; c++)
            {
                Material m = null;
                if (site != null && site.surfaceMaterials != null && site.surfaceMaterials.Length > src[c]) m = site.surfaceMaterials[src[c]];
                if (m == null && site != null) m = site.pileMaterial;
                if (m == null)
                {
                    var sh = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
                    m = new Material(sh) { color = new Color(0.5f, 0.32f, 0.18f) };
                }
                mats[c] = new Material(m) { name = m.name + " (grains)", enableInstancing = true };
            }
        }

        /// <summary>A lumpy, flat-shaded clod: a jittered octahedron-ish blob, unit radius.</summary>
        static Mesh BuildMesh()
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            Vector3[] v =
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
            var rng = new System.Random(4242);
            for (int i = 0; i < v.Length; i++) v[i] = v[i].normalized * (0.75f + (float)rng.NextDouble() * 0.45f);
            var fv = new Vector3[f.Length];
            var fi = new int[f.Length];
            for (int i = 0; i < f.Length; i++) { fv[i] = v[f[i]]; fi[i] = i; }
            var m = new Mesh { name = "DirtClod", vertices = fv, triangles = fi };
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }
}
