using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSBase.Mining
{
    /// <summary>
    /// The work area: a deformable dirt field (0.5 m grid) drawn as faceted low-poly ground.
    /// Every cell has its native ground height plus a layer of loose dirt on top. Loose dirt keeps track of its gold
    /// and slumps down to its angle of repose, so dumped and pushed dirt forms real heaps, berms and windrows.
    /// Native ground is layered: overburden (almost no gold) -> pay dirt (gold inside the claim) -> bedrock.
    /// A hidden Terrain + TerrainCollider gives the physics surface; it's refreshed whenever the dirt moves.
    /// </summary>
    [RequireComponent(typeof(Terrain))]
    public partial class DigSite : MonoBehaviour
    {
        public static DigSite Instance { get; private set; }

        [Header("Geology (depths below original surface, m)")]
        public float overburdenDepth = 1.2f;
        public float bedrockDepth = 4.5f;
        [Tooltip("Gold grade in the pay layer, grams per cubic meter (min..max across the claim).")]
        public float minGrade = 2f, maxGrade = 14f;
        public float overburdenGrade = 0.15f;
        public int seed = 1337;
        [Tooltip("World X/Z rectangle of the gold claim. Outside it the dirt has (almost) no gold and you can't dig as deep.")]
        public Rect claimRect = new Rect(50f, -30f, 40f, 40f);
        public float offClaimDigDepth = 2.5f;

        [Header("Loose dirt")]
        [Tooltip("Steepest slope loose dirt can hold (degrees).")]
        public float reposeAngle = 34f;
        [Tooltip("Max cells relaxed per frame.")]
        public int slumpBudget = 15000;

        [Header("Look")]
        public Material pileMaterial;                // falling clumps
        public Material[] surfaceMaterials = new Material[11]; // grassA, grassB, looseA, looseB, topsoil, paydirt, deep, bedrock, sand, clay, gravel
        public Material rockMaterial;                // boulders
        public Mesh grassTuftMesh;
        public Material grassTuftMaterial;
        public float grassDrawDistance = 120f;
        public float edgeMargin = 1.5f;
        public int chunkCells = 32;

        const int GrassA = 0, GrassB = 1, LooseA = 2, LooseB = 3, Topsoil = 4, Paydirt = 5, Deep = 6, Bedrock = 7, SandCat = 8, ClayCat = 9, GravelCat = 10;

        Terrain terrain;
        TerrainData data;
        int res;
        float cell, cellArea, baseY, sizeY, maxStep;
        Vector3 origin;
        float[,] orig, native, loose, looseGold, looseSand, looseGravel, looseClay;
        bool[,] trampled;
        /// <summary>How hard the loose dirt in a cell has been packed by machines (0 = fluffy, 1 = fully: <see cref="CompactShrink"/> less volume).</summary>
        float[,] packed;
        public const float CompactShrink = 0.25f;

        // change tracking
        int dx0 = int.MaxValue, dz0 = int.MaxValue, dx1 = -1, dz1 = -1;
        int chunksX, chunksZ;
        bool[,] chunkDirty;
        Mesh[,] chunkMesh;
        List<Matrix4x4>[,] chunkTufts;
        Bounds[,] chunkBounds;

        // slumping work queue
        readonly List<int> active = new(), next = new();
        readonly HashSet<int> queued = new();

        // ------------------------------------------------------------------ setup

        void Awake()
        {
            Instance = this;
            terrain = GetComponent<Terrain>();
            data = Instantiate(terrain.terrainData); // never modify the saved asset
            terrain.terrainData = data;
            var col = GetComponent<TerrainCollider>();
            if (col != null) col.terrainData = data;
            terrain.drawHeightmap = false;            // we draw the faceted mesh ourselves
            terrain.drawTreesAndFoliage = false;

            res = data.heightmapResolution;
            cell = data.size.x / (res - 1);
            cellArea = cell * cell;
            origin = transform.position;
            baseY = origin.y;
            sizeY = data.size.y;
            maxStep = cell * Mathf.Tan(reposeAngle * Mathf.Deg2Rad);

            var h = data.GetHeights(0, 0, res, res);
            orig = new float[res, res]; native = new float[res, res];
            loose = new float[res, res]; looseGold = new float[res, res];
            looseSand = new float[res, res]; looseGravel = new float[res, res]; looseClay = new float[res, res];
            trampled = new bool[res, res];
            packed = new float[res, res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    orig[z, x] = native[z, x] = h[z, x] * sizeY + baseY;

            GenerateGeology();
            BuildChunks();
        }

        void BuildChunks()
        {
            int cells = res - 1;
            chunksX = chunksZ = Mathf.CeilToInt(cells / (float)chunkCells);
            chunkDirty = new bool[chunksZ, chunksX];
            chunkMesh = new Mesh[chunksZ, chunksX];
            chunkTufts = new List<Matrix4x4>[chunksZ, chunksX];
            chunkBounds = new Bounds[chunksZ, chunksX];
            var mats = surfaceMaterials;
            for (int j = 0; j < chunksZ; j++)
                for (int i = 0; i < chunksX; i++)
                {
                    var go = new GameObject($"DirtChunk {i},{j}");
                    go.transform.SetParent(transform, false);
                    go.layer = gameObject.layer;
                    var mesh = new Mesh { name = go.name };
                    mesh.MarkDynamic();
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = mats;
                    mr.shadowCastingMode = ShadowCastingMode.On;
                    chunkMesh[j, i] = mesh;
                    chunkTufts[j, i] = new List<Matrix4x4>(1023);
                    RebuildChunk(i, j);
                }
        }

        // ------------------------------------------------------------------ queries

        public bool Contains(Vector3 world)
        {
            Vector3 p = world - origin;
            return p.x > edgeMargin && p.z > edgeMargin && p.x < data.size.x - edgeMargin && p.z < data.size.z - edgeMargin;
        }

        float Surf(int x, int z) => native[z, x] + loose[z, x];

        /// <summary>Height of the dirt surface (including loose dirt) at a world X/Z.</summary>
        public float SurfaceY(Vector3 world)
        {
            float fx = Mathf.Clamp((world.x - origin.x) / cell, 0f, res - 1.001f);
            float fz = Mathf.Clamp((world.z - origin.z) / cell, 0f, res - 1.001f);
            int x = (int)fx, z = (int)fz;
            float tx = fx - x, tz = fz - z;
            float a = Mathf.Lerp(Surf(x, z), Surf(x + 1, z), tx);
            float b = Mathf.Lerp(Surf(x, z + 1), Surf(x + 1, z + 1), tx);
            return Mathf.Lerp(a, b, tz);
        }

        public bool IsUnderground(Vector3 tip) => Contains(tip) && tip.y < SurfaceY(tip) - 0.02f;

        bool InClaim(float wx, float wz) => claimRect.Contains(new Vector2(wx, wz));

        float Floor(int x, int z) => orig[z, x] - (InClaim(origin.x + x * cell, origin.z + z * cell) ? bedrockDepth : offClaimDigDepth);

        /// <summary>Gold grade (g/m^3) of the native ground at a world x/z and depth below the original surface.</summary>
        public float GradeAt(float worldX, float worldZ, float depth)
        {
            if (!InClaim(worldX, worldZ)) return 0.02f;
            if (depth < overburdenDepth) return overburdenGrade;
            float n = Mathf.PerlinNoise(worldX * 0.06f + seed * 0.13f, worldZ * 0.06f - seed * 0.07f);
            float streak = Mathf.PerlinNoise(worldX * 0.15f + 50f, worldZ * 0.02f + seed); // narrow rich channels
            float grade = Mathf.Lerp(minGrade, maxGrade, n * n);
            if (streak > 0.68f) grade *= 2f;
            // richest just above bedrock, like a real placer deposit
            float t = Mathf.InverseLerp(overburdenDepth, bedrockDepth, depth);
            return grade * Mathf.Lerp(0.6f, 1.4f, t) * LayerAt(worldX, worldZ, depth).gradeMul;
        }

        /// <summary>Loose dirt (m^3) and its gold within `radius` of a point.</summary>
        public float LooseVolume(Vector3 center, float radius, out float gold)
        {
            gold = 0f; float v = 0f;
            Range(center, radius, out int x0, out int z0, out int x1, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float ddx = origin.x + x * cell - center.x, ddz = origin.z + z * cell - center.z;
                    if (ddx * ddx + ddz * ddz > radius * radius) continue;
                    v += loose[z, x] * cellArea; gold += looseGold[z, x];
                }
            return v;
        }

        /// <summary>Total loose dirt on the whole field (m^3).</summary>
        public float TotalLoose()
        {
            float v = 0f;
            for (int z = 0; z < res; z++) for (int x = 0; x < res; x++) v += loose[z, x];
            return v * cellArea;
        }

        void Range(Vector3 c, float r, out int x0, out int z0, out int x1, out int z1)
        {
            x0 = Mathf.Clamp(Mathf.FloorToInt((c.x - r - origin.x) / cell), 1, res - 2);
            x1 = Mathf.Clamp(Mathf.CeilToInt((c.x + r - origin.x) / cell), 1, res - 2);
            z0 = Mathf.Clamp(Mathf.FloorToInt((c.z - r - origin.z) / cell), 1, res - 2);
            z1 = Mathf.Clamp(Mathf.CeilToInt((c.z + r - origin.z) / cell), 1, res - 2);
        }

        // ------------------------------------------------------------------ digging / dumping

        /// <summary>Dig with a bucket tip anywhere: inside the dirt field, otherwise from a loose pile.</summary>
        public static MaterialLoad DigAnywhere(Vector3 tip, float radius, float maxVolume)
        {
            if (Instance != null && Instance.Contains(tip)) return Instance.Dig(tip, radius, maxVolume);
            var pile = DirtPile.FindContaining(tip);
            if (pile != null) return pile.Dig(maxVolume);
            return default;
        }

        /// <summary>Drop dirt on the ground at a point (onto the dirt field if it's there, otherwise a pile).</summary>
        public static void DropAt(Vector3 ground, MaterialLoad load)
        {
            if (load.IsEmpty) return;
            var rocks = load.TakeRocks();
            if (!rocks.IsEmpty) Boulder.SpawnFromLoad(ground, rocks);
            if (load.IsEmpty) return;
            if (Instance != null && Instance.Contains(ground)) Instance.Deposit(ground, load);
            else DirtPile.DropAt(ground, load);
        }

        /// <summary>Remove up to `want` m thickness from a cell: loose dirt first, then native ground (not below `floor`).</summary>
        MaterialLoad Remove(int x, int z, float thickness, float minY)
        {
            var got = new MaterialLoad();
            if (thickness <= 0f) return got;
            float fromLoose = Mathf.Min(thickness, loose[z, x]);
            if (fromLoose > 0f)
            {
                float f = loose[z, x] > 1e-6f ? fromLoose / loose[z, x] : 0f;
                float g = looseGold[z, x] * f, sa = looseSand[z, x] * f, gr = looseGravel[z, x] * f, cl = looseClay[z, x] * f;
                loose[z, x] -= fromLoose; looseGold[z, x] -= g;
                looseSand[z, x] -= sa; looseGravel[z, x] -= gr; looseClay[z, x] -= cl;
                if (loose[z, x] < 1e-5f) ClearLoose(x, z);
                got.volume += fromLoose * cellArea; got.goldGrams += g;
                got.sand += sa; got.gravel += gr; got.clay += cl;
                thickness -= fromLoose;
            }
            if (thickness > 0f)
            {
                float lowest = Mathf.Max(minY, Floor(x, z));
                float cut = Mathf.Min(thickness, native[z, x] - lowest);
                if (cut > 0f)
                {
                    float midDepth = orig[z, x] - (native[z, x] - cut * 0.5f);
                    float vol = cut * cellArea;
                    float wx = origin.x + x * cell, wz = origin.z + z * cell;
                    var layer = LayerAt(wx, wz, midDepth);
                    got.Add(MaterialLoad.Mix(vol, vol * GradeAt(wx, wz, midDepth), layer.sand, layer.gravel, layer.clay));
                    native[z, x] -= cut;
                }
            }
            return got;
        }

        void AddLoose(int x, int z, MaterialLoad l)
        {
            x = Mathf.Clamp(x, 1, res - 2); z = Mathf.Clamp(z, 1, res - 2);
            float v = l.volume - l.rock; // boulders never go into the loose dirt
            if (v <= 0f) return;
            float was = loose[z, x];
            loose[z, x] += v / cellArea;
            packed[z, x] *= was / loose[z, x]; // fresh fluffy dirt on top
            looseGold[z, x] += l.goldGrams;
            looseSand[z, x] += l.sand; looseGravel[z, x] += l.gravel; looseClay[z, x] += l.clay;
            Touch(x, z);
        }

        /// <summary>
        /// Dig with a bucket tip at `tip`: lowers the dirt around the tip toward the tip's height,
        /// removing at most `maxVolume` m^3 (loose dirt first), and returns what was removed.
        /// </summary>
        MaterialLoad DigCore(Vector3 tip, float radius, float maxVolume)
        {
            var removed = new MaterialLoad();
            if (maxVolume <= 0f) return removed;
            radius = Mathf.Max(radius, cell * 0.75f);
            Range(tip, radius, out int x0, out int z0, out int x1, out int z1);

            float potential = 0f;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float w = Weight(x, z, tip, radius);
                    if (w <= 0f) continue;
                    float target = Mathf.Max(tip.y, Floor(x, z));
                    float s = Surf(x, z);
                    if (s > target) potential += (s - target) * w * cellArea;
                }
            if (potential <= 1e-6f) return removed;
            float scale = Mathf.Min(1f, maxVolume / potential);

            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float w = Weight(x, z, tip, radius);
                    if (w <= 0f) continue;
                    float s = Surf(x, z);
                    float target = Mathf.Max(tip.y, Floor(x, z));
                    if (s <= target) continue;
                    removed.Add(Remove(x, z, (s - target) * w * scale, tip.y));
                    Touch(x, z);
                }
            return removed;
        }

        float Weight(int x, int z, Vector3 c, float radius)
        {
            float ddx = origin.x + x * cell - c.x, ddz = origin.z + z * cell - c.z;
            return Mathf.Clamp01(1.25f - Mathf.Sqrt(ddx * ddx + ddz * ddz) / radius);
        }

        /// <summary>Drop loose dirt at a point. It spreads a little, then slumps into a natural heap.</summary>
        public void Deposit(Vector3 at, MaterialLoad load)
        {
            if (load.IsEmpty) return;
            var rocks = load.TakeRocks();
            if (!rocks.IsEmpty) Boulder.SpawnFromLoad(at, rocks);
            if (load.IsEmpty) return;
            float r = Mathf.Clamp(Mathf.Sqrt(load.volume) * 0.9f, cell * 0.6f, 1.6f);
            Range(at, r, out int x0, out int z0, out int x1, out int z1);
            float sum = 0f;
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) sum += Weight(x, z, at, r);
            if (sum <= 0f)
            {
                AddLoose(Mathf.RoundToInt((at.x - origin.x) / cell), Mathf.RoundToInt((at.z - origin.z) / cell), load);
                return;
            }
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float w = Weight(x, z, at, r) / sum;
                    if (w <= 0f) continue;
                    AddLoose(x, z, load.Scaled(w));
                }
        }

        /// <summary>
        /// A dozer blade (or a full loader bucket) moving forward: everything above the blade's bottom edge in the
        /// strip under the blade is shoved into the cells just ahead of it. The heap that builds up spills to the
        /// sides as berms. Returns the volume moved this call (m^3).
        /// </summary>
        public float BladePush(Vector3 edge, Vector3 forward, float width, float bottomY) => BladePush(edge, forward, width, bottomY, 0.8f, out _);

        /// <summary>As above; `ahead` = how far in front of the edge the dirt lands, `fromNative` = how much was cut from unbroken ground.</summary>
        public float BladePush(Vector3 edge, Vector3 forward, float width, float bottomY, float ahead, out float fromNative)
            => BladePush(edge, forward, width, bottomY, ahead, false, out fromNative);

        /// <summary>
        /// Spread loose dirt (an excavator bucket dragged or swung through a pile): like BladePush, but only the
        /// loose layer moves; unbroken ground under it stays put. Returns m^3 moved.
        /// </summary>
        public float Spread(Vector3 edge, Vector3 dir, float width, float bottomY, float ahead)
            => BladePush(edge, dir, width, bottomY, ahead, true, out _);

        float BladePush(Vector3 edge, Vector3 forward, float width, float bottomY, float ahead, bool looseOnly, out float fromNative)
        {
            fromNative = 0f;
            forward.y = 0f; forward.Normalize();
            Vector3 right = new Vector3(forward.z, 0f, -forward.x);
            const float back = 0.35f, front = 0.3f;
            float reach = width * 0.5f + 1f;
            Range(edge, reach, out int x0, out int z0, out int x1, out int z1);
            float moved = 0f;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float rx = origin.x + x * cell - edge.x, rz = origin.z + z * cell - edge.z;
                    float lat = rx * right.x + rz * right.z;
                    float lon = rx * forward.x + rz * forward.z;
                    if (Mathf.Abs(lat) > width * 0.5f || lon < -back || lon > front) continue;
                    float s = Surf(x, z);
                    if (s <= bottomY + 0.01f) continue;
                    float looseHere = loose[z, x] * cellArea;
                    float cutDepth = looseOnly ? Mathf.Min(s - bottomY, loose[z, x]) : s - bottomY;
                    if (cutDepth <= 0.002f) continue;
                    var m = Remove(x, z, cutDepth, bottomY);
                    if (m.IsEmpty) continue;
                    fromNative += Mathf.Max(0f, m.volume - looseHere);
                    Touch(x, z);
                    // each cut row lands in its own row of the heap in front (not all in one row: that made a spike)
                    float band = Mathf.InverseLerp(-back, front, lon);
                    Vector3 to = edge + right * lat + forward * (ahead + band * Mathf.Max(cell * 1.5f, ahead * 0.6f));
                    AddLoose(Mathf.RoundToInt((to.x - origin.x) / cell), Mathf.RoundToInt((to.z - origin.z) / cell), m);
                    // clods tumble off the cut and roll onto the heap in front of the blade
                    if (Random.value < 0.18f)
                        DirtGrains.Spray(new Vector3(origin.x + x * cell, s - 0.05f, origin.z + z * cell),
                            forward * Random.Range(1.2f, 2.6f) + Vector3.up * Random.Range(0.8f, 2.2f) + right * Random.Range(-0.7f, 0.7f),
                            DirtGrains.PickCategory(m), Random.Range(0.06f, 0.13f));
                    moved += m.volume;
                }
            if (moved > 0f)
            {
                AfterGroundMoved(edge, reach + 0.5f, edge + forward * ahead);
                Relax(3000); // the heap slumps now, before this frame's ground is drawn
            }
            return moved;
        }

        /// <summary>
        /// A ripper shank dragged through the ground: breaks the native ground around the tip down to the tip's
        /// depth into loose dirt, in place. Loose dirt is quicker to dig and easier to push. Returns m^3 broken.
        /// </summary>
        public float Rip(Vector3 tip, float radius)
        {
            if (!Contains(tip)) return 0f;
            Range(tip, radius, out int x0, out int z0, out int x1, out int z1);
            float broken = 0f;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float ddx = origin.x + x * cell - tip.x, ddz = origin.z + z * cell - tip.z;
                    if (ddx * ddx + ddz * ddz > radius * radius) continue;
                    float target = Mathf.Max(tip.y, Floor(x, z));
                    float t = native[z, x] - target;
                    if (t <= 0.01f) continue;
                    var m = Remove(x, z, loose[z, x] + t, target); // all loose on top + the broken native layer
                    broken += t * cellArea;
                    AddLoose(x, z, m);                            // ...goes straight back as loose dirt
                }
            Boulder.Loosen(tip, radius + 0.7f);
            if (broken > 0f) AfterGroundMoved(tip, radius + 0.5f, tip);
            return broken;
        }

        /// <summary>Wheels and tracks scuff the grass off, leaving dirt ruts.</summary>
        public void Trample(Vector3 at, float radius)
        {
            if (!Contains(at)) return;
            Range(at, radius, out int x0, out int z0, out int x1, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    if (trampled[z, x]) continue;
                    float ddx = origin.x + x * cell - at.x, ddz = origin.z + z * cell - at.z;
                    if (ddx * ddx + ddz * ddz > radius * radius) continue;
                    trampled[z, x] = true;
                    MarkChunks(x, z);
                }
        }

        // ------------------------------------------------------------------ slumping

        void Touch(int x, int z)
        {
            Queue(x, z);
            Queue(x - 1, z); Queue(x + 1, z); Queue(x, z - 1); Queue(x, z + 1);
            MarkHeights(x, z);
        }

        void Queue(int x, int z)
        {
            if (x < 1 || z < 1 || x > res - 2 || z > res - 2) return;
            int i = z * res + x;
            if (queued.Add(i)) next.Add(i);
        }

        static readonly int[] nx = { 1, -1, 0, 0 }, nz = { 0, 0, 1, -1 };
        readonly float[] excess = new float[4];

        /// <summary>Let loose dirt slide off anything steeper than its angle of repose.</summary>
        public void Relax(int budget)
        {
            while (budget > 0 && next.Count > 0)
            {
                active.Clear(); active.AddRange(next);
                next.Clear(); queued.Clear();
                int processed = 0;
                for (; processed < active.Count && budget > 0; processed++, budget--)
                {
                    int i = active[processed];
                    int x = i % res, z = i / res;
                    CheckWall(x, z);
                    float l = loose[z, x];
                    if (l <= 1e-5f) continue;
                    float s = native[z, x] + l;
                    float total = 0f;
                    float step = MaxStepAt(x, z);
                    for (int k = 0; k < 4; k++)
                    {
                        int ax = x + nx[k], az = z + nz[k];
                        float d = s - Surf(ax, az) - step;
                        excess[k] = d > 0f ? d : 0f;
                        total += excess[k];
                    }
                    if (total <= 1e-4f) continue;
                    // move a share of the excess (damped so piles settle smoothly instead of oscillating)
                    float move = Mathf.Min(l, total * 0.4f);
                    float fr = move / l;
                    float g = looseGold[z, x] * fr, sa = looseSand[z, x] * fr, gr = looseGravel[z, x] * fr, cl = looseClay[z, x] * fr;
                    loose[z, x] -= move; looseGold[z, x] -= g;
                    looseSand[z, x] -= sa; looseGravel[z, x] -= gr; looseClay[z, x] -= cl;
                    // a bit of dirt visibly tumbling down the face of a slide
                    if (move > 0.03f && Random.value < 0.04f)
                    {
                        int k0 = excess[0] >= excess[1] && excess[0] >= excess[2] && excess[0] >= excess[3] ? 0 : excess[1] >= excess[2] && excess[1] >= excess[3] ? 1 : excess[2] >= excess[3] ? 2 : 3;
                        DirtGrains.Spray(CellPos(x, z) + Vector3.up * 0.05f, new Vector3(nx[k0], -0.3f, nz[k0]) * 1.2f, 0, Random.Range(0.04f, 0.09f));
                    }
                    for (int k = 0; k < 4; k++)
                    {
                        if (excess[k] <= 0f) continue;
                        float f = excess[k] / total;
                        int ax = x + nx[k], az = z + nz[k];
                        float was = loose[az, ax];
                        loose[az, ax] += move * f; looseGold[az, ax] += g * f;
                        packed[az, ax] *= was / loose[az, ax];
                        looseSand[az, ax] += sa * f; looseGravel[az, ax] += gr * f; looseClay[az, ax] += cl * f;
                        Queue(ax, az);
                        MarkHeights(ax, az);
                    }
                    Queue(x, z); Queue(x - 1, z); Queue(x + 1, z); Queue(x, z - 1); Queue(x, z + 1);
                    MarkHeights(x, z);
                }
                // anything we ran out of budget for goes back on the queue
                for (; processed < active.Count; processed++)
                {
                    int i = active[processed];
                    if (queued.Add(i)) next.Add(i);
                }
            }
        }

        /// <summary>True while dirt is still sliding somewhere.</summary>
        public bool Settling => next.Count > 0;

        // ------------------------------------------------------------------ change tracking + rendering

        void MarkHeights(int x, int z)
        {
            if (x < dx0) dx0 = x; if (x > dx1) dx1 = x;
            if (z < dz0) dz0 = z; if (z > dz1) dz1 = z;
            MarkChunks(x, z);
        }

        void MarkChunks(int x, int z)
        {
            NetMark(x, z);
            int i0 = Mathf.Clamp((x - 1) / chunkCells, 0, chunksX - 1), i1 = Mathf.Clamp(x / chunkCells, 0, chunksX - 1);
            int j0 = Mathf.Clamp((z - 1) / chunkCells, 0, chunksZ - 1), j1 = Mathf.Clamp(z / chunkCells, 0, chunksZ - 1);
            for (int j = j0; j <= j1; j++) for (int i = i0; i <= i1; i++) chunkDirty[j, i] = true;
        }

        void Update()
        {
            Relax(slumpBudget);
            ProcessCrumbles(false);
            DrawGrass();
            DrawClumps();
        }

        void LateUpdate() => Flush(8);

        /// <summary>Push height changes to the physics collider and rebuild up to `maxChunks` changed meshes.</summary>
        public void Flush(int maxChunks)
        {
            if (dx1 >= dx0)
            {
                int w = dx1 - dx0 + 1, h = dz1 - dz0 + 1;
                var sub = new float[h, w];
                for (int z = 0; z < h; z++)
                    for (int x = 0; x < w; x++)
                        sub[z, x] = Mathf.Clamp01((Surf(dx0 + x, dz0 + z) - baseY) / sizeY);
                data.SetHeights(dx0, dz0, sub);
                dx0 = dz0 = int.MaxValue; dx1 = dz1 = -1;
            }
            int built = 0;
            for (int j = 0; j < chunksZ && built < maxChunks; j++)
                for (int i = 0; i < chunksX && built < maxChunks; i++)
                    if (chunkDirty[j, i]) { RebuildChunk(i, j); built++; }
        }

        /// <summary>Let all the dirt settle and redraw everything right now (used by the automated test).</summary>
        public void SettleNow()
        {
            for (int n = 0; n < 400; n++)
            {
                DirtGrains.FastForward();
                Relax(slumpBudget);
                ProcessCrumbles(true);
                if (!Settling && crumbles.Count == 0 && DirtGrains.Live == 0) break;
            }
            Flush(int.MaxValue);
        }

        int Category(int x, int z, out int score)
        {
            if (loose[z, x] > 0.04f)
            {
                score = 3;
                float lv = loose[z, x] * cellArea;
                if (looseClay[z, x] > lv * 0.45f) return ClayCat;
                if (looseSand[z, x] > lv * 0.5f) return SandCat;
                if (looseGravel[z, x] > lv * 0.45f) return GravelCat;
                return ((x * 7 + z * 13) & 3) == 0 ? LooseB : LooseA;
            }
            float cut = orig[z, x] - native[z, x];
            if (cut > 0.08f)
            {
                score = 2;
                if (native[z, x] <= Floor(x, z) + 0.05f && InClaim(origin.x + x * cell, origin.z + z * cell)) return Bedrock;
                if (cut < overburdenDepth) return Topsoil;
                switch (LayerAt(origin.x + x * cell, origin.z + z * cell, cut - 0.05f).kind)
                {
                    case SoilKind.Clay: return ClayCat;
                    case SoilKind.Sand: return SandCat;
                    case SoilKind.PayGravel: return Paydirt;
                    case SoilKind.Gravel: return GravelCat;
                    default: return Topsoil;
                }
            }
            if (trampled[z, x] || loose[z, x] > 0.005f) { score = 1; return LooseB; }
            score = 0;
            float n = Mathf.PerlinNoise(x * 0.09f + 3.1f, z * 0.09f + 7.7f) + (((x * 31 + z * 17) % 5) - 2) * 0.06f;
            return n > 0.55f ? GrassB : GrassA;
        }

        int TriCategory(int ax, int az, int bx, int bz, int cx, int cz)
        {
            int ca = Category(ax, az, out int sa), cb = Category(bx, bz, out int sb), cc = Category(cx, cz, out int sc);
            if (sa >= sb && sa >= sc) return ca;
            return sb >= sc ? cb : cc;
        }

        readonly List<Vector3> verts = new();
        readonly List<int>[] tris = { new(), new(), new(), new(), new(), new(), new(), new(), new(), new(), new() };

        void RebuildChunk(int ci, int cj)
        {
            chunkDirty[cj, ci] = false;
            verts.Clear();
            foreach (var t in tris) t.Clear();
            int xa = ci * chunkCells, xb = Mathf.Min(xa + chunkCells, res - 1);
            int za = cj * chunkCells, zb = Mathf.Min(za + chunkCells, res - 1);
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int z = za; z < zb; z++)
                for (int x = xa; x < xb; x++)
                {
                    Vector3 a = V(x, z), b = V(x + 1, z), c = V(x, z + 1), d = V(x + 1, z + 1);
                    minY = Mathf.Min(minY, Mathf.Min(Mathf.Min(a.y, b.y), Mathf.Min(c.y, d.y)));
                    maxY = Mathf.Max(maxY, Mathf.Max(Mathf.Max(a.y, b.y), Mathf.Max(c.y, d.y)));
                    if (((x + z) & 1) == 0)
                    {
                        Tri(a, c, d, TriCategory(x, z, x, z + 1, x + 1, z + 1));
                        Tri(a, d, b, TriCategory(x, z, x + 1, z + 1, x + 1, z));
                    }
                    else
                    {
                        Tri(a, c, b, TriCategory(x, z, x, z + 1, x + 1, z));
                        Tri(b, c, d, TriCategory(x + 1, z, x, z + 1, x + 1, z + 1));
                    }
                }
            var mesh = chunkMesh[cj, ci];
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.subMeshCount = tris.Length;
            for (int k = 0; k < tris.Length; k++) mesh.SetTriangles(tris[k], k, false);
            mesh.RecalculateNormals();   // vertices aren't shared, so every facet gets its own flat normal
            mesh.RecalculateBounds();
            chunkBounds[cj, ci] = new Bounds(
                origin + new Vector3((xa + xb) * 0.5f * cell, (minY + maxY) * 0.5f, (za + zb) * 0.5f * cell),
                new Vector3((xb - xa) * cell + 1f, maxY - minY + 3f, (zb - za) * cell + 1f));
            RebuildTufts(ci, cj, xa, xb, za, zb);
            RebuildClumps(ci, cj, xa, xb, za, zb);
        }

        Vector3 V(int x, int z) => new Vector3(x * cell, Surf(x, z) - baseY, z * cell);

        void Tri(Vector3 a, Vector3 b, Vector3 c, int cat)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            var t = tris[cat]; t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }

        // ------------------------------------------------------------------ grass tufts

        void RebuildTufts(int ci, int cj, int xa, int xb, int za, int zb)
        {
            var list = chunkTufts[cj, ci];
            list.Clear();
            if (grassTuftMesh == null || grassTuftMaterial == null) return;
            for (int z = za; z < zb && list.Count < 1023; z++)
                for (int x = xa; x < xb && list.Count < 1023; x++)
                {
                    uint hsh = (uint)(x * 73856093) ^ (uint)(z * 19349663) ^ (uint)seed;
                    hsh ^= hsh >> 13; hsh *= 0x5bd1e995; hsh ^= hsh >> 15;
                    if ((hsh & 7) == 0) continue; // a few bare spots
                    if (!IsGrass(x, z) || !IsGrass(x + 1, z) || !IsGrass(x, z + 1) || !IsGrass(x + 1, z + 1)) continue;
                    float ox = ((hsh >> 4) & 255) / 255f, oz = ((hsh >> 12) & 255) / 255f;
                    float rot = ((hsh >> 20) & 255) / 255f * 360f;
                    float sc = 0.7f + ((hsh >> 8) & 15) / 15f * 0.7f;
                    Vector3 p = origin + new Vector3((x + ox) * cell, 0f, (z + oz) * cell);
                    p.y = SurfaceY(p);
                    list.Add(Matrix4x4.TRS(p, Quaternion.Euler(0f, rot, 0f), Vector3.one * sc));
                }
        }

        bool IsGrass(int x, int z) => !trampled[z, x] && loose[z, x] <= 0.005f && orig[z, x] - native[z, x] <= 0.08f;

        void DrawGrass()
        {
            if (grassTuftMesh == null || grassTuftMaterial == null) return;
            var cam = TuftRenderer.ActiveCamera();
            if (cam == null) return;
            var planes = TuftRenderer.Planes(cam);
            for (int j = 0; j < chunksZ; j++)
                for (int i = 0; i < chunksX; i++)
                {
                    var list = chunkTufts[j, i];
                    if (list.Count == 0) continue;
                    var b = chunkBounds[j, i];
                    if (b.SqrDistance(cam.transform.position) > grassDrawDistance * grassDrawDistance) continue;
                    if (!GeometryUtility.TestPlanesAABB(planes, b)) continue;
                    TuftRenderer.Draw(grassTuftMesh, grassTuftMaterial, list, b);
                }
        }
    }

    /// <summary>Draws lots of grass tufts with GPU instancing (no GameObjects).</summary>
    public static class TuftRenderer
    {
        static readonly Plane[] planes = new Plane[6];
        static Camera cached;

        public static Camera ActiveCamera()
        {
            if (cached != null && cached.isActiveAndEnabled) return cached;
            cached = Camera.main;
            if (cached != null && cached.isActiveAndEnabled) return cached;
            foreach (var c in Camera.allCameras) if (c.isActiveAndEnabled) { cached = c; return c; }
            return null;
        }

        public static Plane[] Planes(Camera cam)
        {
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            return planes;
        }

        public static void Draw(Mesh mesh, Material mat, List<Matrix4x4> instances, Bounds bounds)
        {
            var rp = new RenderParams(mat)
            {
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = true,
                worldBounds = bounds,
            };
            Graphics.RenderMeshInstanced(rp, mesh, 0, instances);
        }
    }
}
