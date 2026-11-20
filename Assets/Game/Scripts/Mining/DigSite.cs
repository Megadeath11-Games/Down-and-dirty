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
        /// <summary>The main ground: the dirt field with the gold claim, wash plant and yards.</summary>
        public static DigSite Instance { get; private set; }
        /// <summary>Every diggable ground in the world (the main dirt field, the city's yards...).</summary>
        public static readonly List<DigSite> All = new();
        static readonly HashSet<Collider> siteColliders = new();

        /// <summary>The ground that covers this point (falls back to the main one, so callers can still check Contains).</summary>
        public static DigSite At(Vector3 world)
        {
            for (int i = 0; i < All.Count; i++) { var s = All[i]; if (s != null && s.data != null && s.Contains(world)) return s; }
            return Instance;
        }

        /// <summary>The grounds in the same order on every game (by where they lie): 0 is the main one.</summary>
        public static List<DigSite> Ordered()
        {
            var l = new List<DigSite>();
            foreach (var s in All) if (s != null && s != Instance) l.Add(s);
            l.Sort((a, b) => { int c = a.transform.position.x.CompareTo(b.transform.position.x); return c != 0 ? c : a.transform.position.z.CompareTo(b.transform.position.z); });
            if (Instance != null) l.Insert(0, Instance);
            return l;
        }

        /// <summary>Is this the physics collider of one of the grounds?</summary>
        public static bool IsSiteCollider(Collider c) => c != null && siteColliders.Contains(c);

        [Tooltip("The main ground (the one with the gold claim). A second ground (the city) leaves this off.")]
        public bool primary = true;
        [Tooltip("Areas that can't be dug (under houses and streets).")]
        public List<Rect> hardRects = new();
        [Tooltip("Gold nuggets buried for the metal detector (none in town).")]
        public int nuggetCount = -1;
        bool[,] hard;

        [Header("Geology (depths below original surface, m)")]
        public float overburdenDepth = 1.2f;
        public float bedrockDepth = 12f;
        /// <summary>Depth of the main pay streak. The gravel gets richer down to here, then a little richer still toward bedrock.</summary>
        public float payDepth = 4.5f;
        [Tooltip("Gold grade in the pay layer, grams per cubic meter (min..max across the claim).")]
        public float minGrade = 2f, maxGrade = 14f;
        public float overburdenGrade = 0.15f;
        public int seed = 1337;
        [Tooltip("World X/Z rectangle of the gold claim. Outside it the dirt has (almost) no gold and you can't dig as deep.")]
        public Rect claimRect = new Rect(50f, -30f, 40f, 40f);
        public float offClaimDigDepth = 6f;

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

        /// <summary>
        /// How much finer the dirt is than the ground's map: 2 = 25 cm squares on a 0.5 m map, so ditches and holes
        /// can have near-straight walls. The same on every game (the squares are sent to friends by number).
        /// </summary>
        public static int GroundDetail = ArgInt("-grounddetail", 4);
        static int ArgInt(string name, int def)
        {
            var a = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length && int.TryParse(a[i + 1], out int v) ? v : def;
        }
        int detail = 1;
        /// <summary>Squares a side (heightmap points).</summary>
        public int Res => res;
        /// <summary>Size of one square (m).</summary>
        public float Cell => cell;
        /// <summary>How many of this ground's squares make one of the map's (1 = none finer).</summary>
        public int Detail => detail;

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
            All.Add(this);
            if (primary || Instance == null) Instance = this;
            terrain = GetComponent<Terrain>();
            data = Instantiate(terrain.terrainData); // never modify the saved asset
            terrain.terrainData = data;
            var col = GetComponent<TerrainCollider>();
            if (col != null) { col.terrainData = data; siteColliders.Add(col); }
            terrain.drawHeightmap = false;            // we draw the faceted mesh ourselves
            terrain.drawTreesAndFoliage = false;

            // a finer grid than the map: fill in the points between the map's ones (straight lines, so the ground
            // looks just the same until it's dug)
            int srcRes = data.heightmapResolution;
            detail = Mathf.Clamp(GroundDetail, 1, 4);
            while (detail > 1 && (srcRes - 1) * detail + 1 > 4097) detail--;
            if (!Mathf.IsPowerOfTwo(srcRes - 1)) detail = 1;
            if (detail > 1)
            {
                var src = data.GetHeights(0, 0, srcRes, srcRes);
                int nr = (srcRes - 1) * detail + 1;
                var fine = new float[nr, nr];
                for (int z = 0; z < nr; z++)
                {
                    int z0 = z / detail; float tz = (z - z0 * detail) / (float)detail; int z1 = Mathf.Min(z0 + 1, srcRes - 1);
                    for (int x = 0; x < nr; x++)
                    {
                        int x0 = x / detail; float tx = (x - x0 * detail) / (float)detail; int x1 = Mathf.Min(x0 + 1, srcRes - 1);
                        float a = Mathf.Lerp(src[z0, x0], src[z0, x1], tx), b = Mathf.Lerp(src[z1, x0], src[z1, x1], tx);
                        fine[z, x] = Mathf.Lerp(a, b, tz);
                    }
                }
                var size = data.size;
                data.heightmapResolution = nr;
                data.size = size;
                if (data.heightmapResolution == nr) data.SetHeights(0, 0, fine);
                else { detail = 1; data.heightmapResolution = srcRes; data.size = size; data.SetHeights(0, 0, src); }
            }
            chunkCells = ArgInt("-chunkcells", chunkCells * detail); // (the same size pieces in metres: more, smaller ones cost more to draw)
            slumpBudget *= detail * detail; // (as many squares as before per square metre: the same slumping per frame)

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
            hard = new bool[res, res];
            foreach (var hr in hardRects)
            {
                int ax = Mathf.Clamp(Mathf.FloorToInt((hr.xMin - origin.x) / cell), 0, res - 1), bx = Mathf.Clamp(Mathf.CeilToInt((hr.xMax - origin.x) / cell), 0, res - 1);
                int az = Mathf.Clamp(Mathf.FloorToInt((hr.yMin - origin.z) / cell), 0, res - 1), bz = Mathf.Clamp(Mathf.CeilToInt((hr.yMax - origin.z) / cell), 0, res - 1);
                for (int z = az; z <= bz; z++) for (int x = ax; x <= bx; x++) hard[z, x] = true;
            }

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
                    mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; // (a close-up piece of fine ground has more than 65k corners)
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = mats;
                    mr.shadowCastingMode = ShadowCastingMode.On;
                    chunkMesh[j, i] = mesh;
                    chunkGo ??= new GameObject[chunksZ, chunksX];
                    chunkGo[j, i] = go;
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

        /// <summary>Height of the unbroken ground (under any loose dirt) at a world X/Z.</summary>
        public float NativeY(Vector3 world)
        {
            float fx = Mathf.Clamp((world.x - origin.x) / cell, 0f, res - 1.001f);
            float fz = Mathf.Clamp((world.z - origin.z) / cell, 0f, res - 1.001f);
            int x = (int)fx, z = (int)fz;
            float tx = fx - x, tz = fz - z;
            float a = Mathf.Lerp(native[z, x], native[z, x + 1], tx);
            float b = Mathf.Lerp(native[z + 1, x], native[z + 1, x + 1], tx);
            return Mathf.Lerp(a, b, tz);
        }

        public bool IsUnderground(Vector3 tip) => Contains(tip) && tip.y < SurfaceY(tip) - 0.02f;

        bool InClaim(float wx, float wz) => claimRect.Contains(new Vector2(wx, wz));

        float Floor(int x, int z) => hard[z, x] ? orig[z, x] : orig[z, x] - (InClaim(origin.x + x * cell, origin.z + z * cell) ? bedrockDepth : offClaimDigDepth);

        void OnDestroy()
        {
            All.Remove(this);
            var col = GetComponent<TerrainCollider>();
            if (col != null) siteColliders.Remove(col);
            if (Instance == this) Instance = All.Count > 0 ? All[0] : null;
        }

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
            float mul = depth <= payDepth
                ? Mathf.Lerp(0.6f, 1.4f, Mathf.InverseLerp(overburdenDepth, payDepth, depth))
                : Mathf.Lerp(1.4f, 1.9f, Mathf.InverseLerp(payDepth, bedrockDepth, depth));
            return grade * mul * LayerAt(worldX, worldZ, depth).gradeMul;
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
            var site = At(tip);
            if (site != null && site.Contains(tip)) return site.Dig(tip, radius, maxVolume);
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
            var site = At(ground);
            if (site != null && site.Contains(ground)) site.Deposit(ground, load);
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
                    float w = DigWeight(x, z, tip, radius);
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
                    float w = DigWeight(x, z, tip, radius);
                    if (w <= 0f) continue;
                    float s = Surf(x, z);
                    float target = Mathf.Max(tip.y, Floor(x, z));
                    if (s <= target) continue;
                    removed.Add(Remove(x, z, (s - target) * w * scale, tip.y));
                    Touch(x, z);
                }
            return removed;
        }

        /// <summary>A bucket takes all the dirt across its width and almost none past its sides (straight walls).</summary>
        float DigWeight(int x, int z, Vector3 c, float radius)
        {
            float ddx = origin.x + x * cell - c.x, ddz = origin.z + z * cell - c.z;
            return Mathf.Clamp01((1.1f - Mathf.Sqrt(ddx * ddx + ddz * ddz) / radius) * 4f);
        }

        float Weight(int x, int z, Vector3 c, float radius)
        {
            float ddx = origin.x + x * cell - c.x, ddz = origin.z + z * cell - c.z;
            return Mathf.Clamp01(1.25f - Mathf.Sqrt(ddx * ddx + ddz * ddz) / radius);
        }

        /// <summary>Drop loose dirt at a point. It spreads a little, then slumps into a natural heap.</summary>
        public void Deposit(Vector3 at, MaterialLoad load) => Deposit(at, load, 0f);

        /// <summary>As above, spread over `radius` m (0 = by how much there is).</summary>
        public void Deposit(Vector3 at, MaterialLoad load, float radius)
        {
            if (load.IsEmpty) return;
            var rocks = load.TakeRocks();
            if (!rocks.IsEmpty) Boulder.SpawnFromLoad(at, rocks);
            if (load.IsEmpty) return;
            float r = radius > 0f ? Mathf.Max(radius, cell * 0.6f) : Mathf.Clamp(Mathf.Sqrt(load.volume) * 0.9f, Mathf.Max(cell * 0.6f, 0.3f), 1.6f); // (at least 30 cm across however fine the squares)
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

        struct CutBlock { public int bx, bz; public MaterialLoad m; public float top, lat, band; public int n; }
        readonly List<CutBlock> cutBlocks = new();

        void AddCutBlock(int bx, int bz, MaterialLoad m, float top, float lat, float band)
        {
            for (int k = 0; k < cutBlocks.Count; k++)
            {
                var c = cutBlocks[k];
                if (c.bx != bx || c.bz != bz) continue;
                c.m.Add(m); c.top = Mathf.Max(c.top, top); c.lat += lat; c.band += band; c.n++;
                cutBlocks[k] = c;
                return;
            }
            cutBlocks.Add(new CutBlock { bx = bx, bz = bz, m = m, top = top, lat = lat, band = band, n = 1 });
        }

        /// <summary>Blades cut dirt into real clods that they push along (off: it moves straight to the cells ahead).</summary>
        public static bool PushClods = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-noclods") < 0;
        /// <summary>The whole blade's width while a machine pushes in pieces (an angled blade: 4 pieces), for the clod size.</summary>
        public static float BladeWidthHint;
        public void BladeWidthHintSet(float w) => BladeWidthHint = w;

        /// <summary>As above; `ahead` = how far in front of the edge the dirt lands, `fromNative` = how much was cut from unbroken ground.</summary>
        public float BladePush(Vector3 edge, Vector3 forward, float width, float bottomY, float ahead, out float fromNative)
            => BladePush(edge, forward, width, bottomY, ahead, float.MaxValue, out fromNative);

        /// <summary>
        /// Spread dirt (an excavator bucket dragged or swung through a pile): like BladePush, but it takes the
        /// loose layer plus at most `nativeCut` metres of the unbroken ground under it, so a dragged bucket can
        /// shave high ground and grade it, like a real one. Returns m^3 moved.
        /// </summary>
        public float Spread(Vector3 edge, Vector3 dir, float width, float bottomY, float ahead, float nativeCut = 0f)
            => BladePush(edge, dir, width, bottomY, ahead, Mathf.Max(0f, nativeCut), out _);

        float BladePush(Vector3 edge, Vector3 forward, float width, float bottomY, float ahead, float nativeLimit, out float fromNative)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try { return BladePushCore(edge, forward, width, bottomY, ahead, nativeLimit, out fromNative); }
            finally { PushMs += (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency; }
        }

        float BladePushCore(Vector3 edge, Vector3 forward, float width, float bottomY, float ahead, float nativeLimit, out float fromNative)
        {
            fromNative = 0f;
            forward.y = 0f; forward.Normalize();
            Vector3 right = new Vector3(forward.z, 0f, -forward.x);
            const float back = 0.35f, front = 0.3f;
            float reach = width * 0.5f + 1f;
            bool clods = PushClods && Application.isPlaying;
            if (clods) DirtGrains.Blade(edge, forward, width, Mathf.Clamp(width * 0.32f, 0.5f, 2.2f), BladeWidthHint);
            float bladeWidthNow = BladeWidthHint > 0f ? BladeWidthHint : width;
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
                    float cutDepth = Mathf.Min(s - bottomY, loose[z, x] + nativeLimit);
                    if (cutDepth <= 0.002f) continue;
                    var m = Remove(x, z, cutDepth, bottomY);
                    if (m.IsEmpty) continue;
                    fromNative += Mathf.Max(0f, m.volume - looseHere);
                    Touch(x, z);
                    // each cut row lands in its own row of the heap in front (not all in one row: that made a spike)
                    float band = Mathf.InverseLerp(-back, front, lon);
                    // the cut dirt becomes clods right where it was, and the blade face shoves them along (a real
                    // rolling heap); only if there are too many clods about already does it drop ahead the old way
                    // (on fine ground the cut is gathered into blocks of the map's squares first: clods as big and as many
                    // as before, not four times as many little ones)
                    if (clods && detail > 1) AddCutBlock(x / detail, z / detail, m, s, lat, band);
                    else if (!(clods && DirtGrains.Cut(new Vector3(origin.x + x * cell, 0f, origin.z + z * cell), cell, bottomY, s, m, null, false, Mathf.Clamp(bladeWidthNow * 0.004f, 0.006f, 0.022f))))
                    {
                        Vector3 to = edge + right * lat + forward * (ahead + band * Mathf.Max(cell * 1.5f, ahead * 0.6f));
                        AddLoose(Mathf.RoundToInt((to.x - origin.x) / cell), Mathf.RoundToInt((to.z - origin.z) / cell), m);
                    }
                    // a few bits fly off the cut
                    if (Random.value < (clods ? 0.05f : 0.18f) / (detail * detail))
                        DirtGrains.Spray(new Vector3(origin.x + x * cell, s - 0.05f, origin.z + z * cell),
                            forward * Random.Range(1.2f, 2.6f) + Vector3.up * Random.Range(0.8f, 2.2f) + right * Random.Range(-0.7f, 0.7f),
                            DirtGrains.PickCategory(m), Random.Range(0.06f, 0.13f));
                    moved += m.volume;
                }
            for (int k = 0; k < cutBlocks.Count; k++)
            {
                var cb = cutBlocks[k];
                float half = (detail - 1) * 0.5f;
                var at = new Vector3(origin.x + (cb.bx * detail + half) * cell, 0f, origin.z + (cb.bz * detail + half) * cell);
                if (!DirtGrains.Cut(at, cell * detail, bottomY, cb.top, cb.m, null, false, Mathf.Clamp(bladeWidthNow * 0.004f, 0.006f, 0.022f)))
                {
                    Vector3 to = edge + right * (cb.lat / cb.n) + forward * (ahead + cb.band / cb.n * Mathf.Max(cell * 1.5f, ahead * 0.6f));
                    AddLoose(Mathf.RoundToInt((to.x - origin.x) / cell), Mathf.RoundToInt((to.z - origin.z) / cell), cb.m);
                }
            }
            cutBlocks.Clear();
            if (moved > 0f)
            {
                AfterGroundMoved(edge, reach + 0.5f, edge + forward * ahead);
                Relax(3000 * detail * detail); // the heap slumps now, before this frame's ground is drawn
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
        readonly float[] excess = new float[8];
        // loose dirt slides to all 8 squares round it (only the 4 beside it made square pyramids with long flat faces)
        static readonly int[] nx8 = { 1, -1, 0, 0, 1, 1, -1, -1 }, nz8 = { 0, 0, 1, -1, 1, -1, 1, -1 };
        const float Diag = 1.41421356f;

        /// <summary>Let loose dirt slide off anything steeper than its angle of repose.</summary>
        public void Relax(int budget)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try { RelaxCore(budget); }
            finally { RelaxMs += (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency; }
        }

        void RelaxCore(int budget)
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
                    for (int k = 0; k < 8; k++)
                    {
                        int ax = x + nx8[k], az = z + nz8[k];
                        float d = s - Surf(ax, az) - (k < 4 ? step : step * Diag);
                        excess[k] = d > 0f ? (k < 4 ? d : d * 0.5f) : 0f; // (a diagonal is further: it takes less)
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
                    if (move > 0.03f && Random.value < 0.04f / (detail * detail))
                    {
                        int k0 = excess[0] >= excess[1] && excess[0] >= excess[2] && excess[0] >= excess[3] ? 0 : excess[1] >= excess[2] && excess[1] >= excess[3] ? 1 : excess[2] >= excess[3] ? 2 : 3;
                        DirtGrains.Spray(CellPos(x, z) + Vector3.up * 0.05f, new Vector3(nx[k0], -0.3f, nz[k0]) * 1.2f, 0, Random.Range(0.04f, 0.09f));
                    }
                    for (int k = 0; k < 8; k++)
                    {
                        if (excess[k] <= 0f) continue;
                        float f = excess[k] / total;
                        int ax = x + nx8[k], az = z + nz8[k];
                        float was = loose[az, ax];
                        loose[az, ax] += move * f; looseGold[az, ax] += g * f;
                        packed[az, ax] *= was / loose[az, ax];
                        looseSand[az, ax] += sa * f; looseGravel[az, ax] += gr * f; looseClay[az, ax] += cl * f;
                        Queue(ax, az);
                        MarkHeights(ax, az);
                    }
                    Queue(x, z); Queue(x - 1, z); Queue(x + 1, z); Queue(x, z - 1); Queue(x, z + 1);
                    Queue(x - 1, z - 1); Queue(x + 1, z - 1); Queue(x - 1, z + 1); Queue(x + 1, z + 1);
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
            if (quarterDirty == null) return;
            int hc = chunkCells / 2;
            for (int dz = -1; dz <= 0; dz++)
                for (int dx = -1; dx <= 0; dx++)
                {
                    int qx = Mathf.Clamp(x + dx, 0, res - 2), qz = Mathf.Clamp(z + dz, 0, res - 2);
                    int i = Mathf.Min(qx / chunkCells, chunksX - 1), j = Mathf.Min(qz / chunkCells, chunksZ - 1);
                    int q = ((qx - i * chunkCells) >= hc ? 1 : 0) | ((qz - j * chunkCells) >= hc ? 2 : 0);
                    quarterDirty[j, i] |= (byte)(1 << q);
                }
        }

        void Update()
        {
            Relax(slumpBudget);
            ProcessCrumbles(false);
            DrawGrass();
            DrawClumps();
        }

        // redraw what changed: as many pieces as fit in a few milliseconds (a busy heap stays smooth), nearest first
        void LateUpdate() { UpdateLods(); Flush(64, 3.5f); CatchUpDecor(); }

        /// <summary>Push height changes to the physics collider and rebuild up to `maxChunks` changed meshes.</summary>
        public void Flush(int maxChunks) => Flush(maxChunks, float.MaxValue);

        static readonly System.Diagnostics.Stopwatch flushWatch = new();

        public void Flush(int maxChunks, float maxMs)
        {
            if (dx1 >= dx0)
            {
                int w = dx1 - dx0 + 1, h = dz1 - dz0 + 1;
                var sub = new float[h, w];
                for (int z = 0; z < h; z++)
                    for (int x = 0; x < w; x++)
                        sub[z, x] = Mathf.Clamp01((Surf(dx0 + x, dz0 + z) - baseY) / sizeY);
                long c0 = System.Diagnostics.Stopwatch.GetTimestamp();
                // (without redoing the hidden terrain's own drawing levels, which cost ~12 ms a time on the fine ground
                // however little changed: the physics collider is still updated straight away)
                data.SetHeightsDelayLOD(dx0, dz0, sub);
                ColliderMs += (System.Diagnostics.Stopwatch.GetTimestamp() - c0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                ColliderSamples += w * h; ColliderCalls++;
                dx0 = dz0 = int.MaxValue; dx1 = dz1 = -1;
            }
            int built = 0;
            if (maxMs >= float.MaxValue)
            {
                for (int j = 0; j < chunksZ && built < maxChunks; j++)
                    for (int i = 0; i < chunksX && built < maxChunks; i++)
                        if (chunkDirty[j, i]) { RebuildChunk(i, j); built++; }
                if (chunkDecorStale != null)
                    for (int j = 0; j < chunksZ; j++) for (int i = 0; i < chunksX; i++) if (chunkDecorStale[j, i]) RebuildDecor(i, j);
                return;
            }
            // nearest the camera first, and stop when the time's up (at least 4 a frame, so nothing waits forever)
            // (the camera you're looking through: in a machine that's its own camera, not the player's)
            Vector3 eye = Eye();
            flushWatch.Restart();
            while (built < maxChunks)
            {
                int bi = -1, bj = -1; float bd = float.MaxValue;
                for (int j = 0; j < chunksZ; j++)
                    for (int i = 0; i < chunksX; i++)
                    {
                        if (!chunkDirty[j, i]) continue;
                        float cx = origin.x + (i + 0.5f) * chunkCells * cell - eye.x, cz = origin.z + (j + 0.5f) * chunkCells * cell - eye.z;
                        float dd = cx * cx + cz * cz;
                        if (dd < bd) { bd = dd; bi = i; bj = j; }
                    }
                if (bi < 0) break;
                RebuildChunk(bi, bj, eye); built++;
                if (built >= 4 && flushWatch.Elapsed.TotalMilliseconds > maxMs) break;
            }
            // then one piece the camera got nearer to (or further from), nearest first (two if the frame is quiet)
            if (chunkLodStale == null) return;
            int lodBuilt = 0, lodMax = built == 0 ? 2 : 1;
            while (lodBuilt < lodMax)
            {
                int bi = -1, bj = -1; float bd = float.MaxValue;
                for (int j = 0; j < chunksZ; j++)
                    for (int i = 0; i < chunksX; i++)
                    {
                        if (!chunkLodStale[j, i]) continue;
                        float cx = origin.x + (i + 0.5f) * chunkCells * cell - eye.x, cz = origin.z + (j + 0.5f) * chunkCells * cell - eye.z;
                        float dd = cx * cx + cz * cz;
                        if (dd < bd) { bd = dd; bi = i; bj = j; }
                    }
                if (bi < 0) break;
                RebuildChunk(bi, bj, eye); lodBuilt++;
            }
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
                return (((x / detail) * 7 + (z / detail) * 13) & 3) == 0 ? LooseB : LooseA;
            }
            float cut = orig[z, x] - native[z, x];
            if (cut > 0.03f) // (a few cm shaved off, by a grader: the grass is gone, it's dirt)
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
            int gx = x / detail, gz = z / detail;
            float n = Mathf.PerlinNoise(gx * 0.09f + 3.1f, gz * 0.09f + 7.7f) + (((gx * 31 + gz * 17) % 5) - 2) * 0.06f;
            return n > 0.55f ? GrassB : GrassA;
        }

        readonly List<Vector3> verts = new(), norms = new();
        readonly List<int>[] tris = { new(), new(), new(), new(), new(), new(), new(), new(), new(), new(), new() };
        int[] catBuf = new int[0], scoreBuf = new int[0];
        Vector3[] vBuf = new Vector3[0];
        int[,] chunkLod;
        bool[,] chunkLodStale; // only wants drawing finer or coarser (nothing changed): done with spare time
        float lodTimer;

        /// <summary>Where the camera is (far off when there's none yet: everything starts coarse and fills in).</summary>
        Vector3 Eye()
        {
            var cam = TuftRenderer.ActiveCamera();
            return cam != null ? cam.transform.position : origin + new Vector3(-1e5f, 0f, -1e5f);
        }

        /// <summary>
        /// How fine a piece of ground is drawn: every square close up, every 2nd further off, every 4th far away
        /// (the map's own squares are kept to the middle distance, so the hills look just as they did).
        /// </summary>
        int WantedStep(int ci, int cj, Vector3 eye)
        {
            if (detail <= 1) return 1;
            float half = chunkCells * cell * 0.5f;
            float cx = origin.x + ci * chunkCells * cell + half - eye.x, cz = origin.z + cj * chunkCells * cell + half - eye.z;
            float dist = Mathf.Max(0f, Mathf.Sqrt(cx * cx + cz * cz) - half * 1.41f);
            // (12.5 cm squares: every one close up, then every 2nd (25 cm), the map's own 50 cm, then 1 m far off)
            int st = detail >= 4
                ? (dist < 10f ? 1 : dist < 35f ? detail / 2 : dist < 90f ? detail : detail * 2)
                : (dist < 45f ? 1 : dist < 120f ? detail : detail * 2);
            return Mathf.Min(st, chunkCells);
        }

        /// <summary>Pieces that should be drawn finer or coarser now that the camera moved get redrawn.</summary>
        void UpdateLods()
        {
            if (detail <= 1 || chunkLod == null) return;
            if ((lodTimer -= Time.unscaledDeltaTime) > 0f) return;
            lodTimer = 0.2f;
            Vector3 eye = Eye();
            for (int j = 0; j < chunksZ; j++)
                for (int i = 0; i < chunksX; i++)
                    chunkLodStale[j, i] = WantedStep(i, j, eye) != chunkLod[j, i];
        }

        void RebuildChunk(int ci, int cj) => RebuildChunk(ci, cj, Eye());

        /// <summary>Time spent redrawing ground pieces (for the frame-rate readout).</summary>
        public static double RebuildMs, RelaxMs, PushMs; public static int Rebuilds;
        static readonly System.Diagnostics.Stopwatch statWatch = new();

        void RebuildChunk(int ci, int cj, Vector3 eye)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try { RebuildChunkCore(ci, cj, eye); }
            finally { RebuildMs += (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency; Rebuilds++; }
        }

        // close up, a piece of fine ground is drawn as four quarters, so digging in one redraws only that quarter
        GameObject[,] chunkGo;
        Mesh[,][] quarterMesh;
        byte[,] quarterDirty;
        bool UseQuarters => detail >= 4 && chunkCells >= 32;

        Mesh Quarter(int ci, int cj, int q)
        {
            quarterMesh ??= new Mesh[chunksZ, chunksX][];
            var qs = quarterMesh[cj, ci] ??= new Mesh[4];
            if (qs[q] != null) return qs[q];
            var go = new GameObject($"Quarter {q}");
            go.transform.SetParent(chunkGo[cj, ci].transform, false);
            go.layer = gameObject.layer;
            var mesh = new Mesh { name = $"{chunkGo[cj, ci].name} q{q}" };
            mesh.MarkDynamic();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = surfaceMaterials;
            mr.shadowCastingMode = ShadowCastingMode.On;
            return qs[q] = mesh;
        }

        void RebuildChunkCore(int ci, int cj, Vector3 eye)
        {
            bool changed = chunkDirty[cj, ci] || chunkLod == null || chunkLod[cj, ci] == 0; // (not just drawn finer/coarser)
            chunkDirty[cj, ci] = false;
            chunkLod ??= new int[chunksZ, chunksX];
            chunkLodStale ??= new bool[chunksZ, chunksX];
            chunkLodStale[cj, ci] = false;
            if (UseQuarters) quarterDirty ??= new byte[chunksZ, chunksX];
            int was = chunkLod[cj, ci];
            int st = WantedStep(ci, cj, eye);
            chunkLod[cj, ci] = st;
            int xa = ci * chunkCells, xb = Mathf.Min(xa + chunkCells, res - 1);
            int za = cj * chunkCells, zb = Mathf.Min(za + chunkCells, res - 1);
            float minY, maxY;
            long tm;
            if (UseQuarters && st == 1 && Application.isPlaying)
            {
                // only the quarters whose ground changed (all four when it just came close)
                int mask = was == 1 ? quarterDirty[cj, ci] : 0xF;
                if (mask == 0) mask = 0xF;
                quarterDirty[cj, ci] = 0;
                int hx = xa + chunkCells / 2, hz = za + chunkCells / 2;
                minY = chunkBounds[cj, ci].size.y > 0f && was == 1 ? chunkBounds[cj, ci].min.y - origin.y + 1.5f : float.MaxValue;
                maxY = chunkBounds[cj, ci].size.y > 0f && was == 1 ? chunkBounds[cj, ci].max.y - origin.y - 1.5f : float.MinValue;
                tm = System.Diagnostics.Stopwatch.GetTimestamp();
                for (int q = 0; q < 4; q++)
                {
                    if ((mask & (1 << q)) == 0) continue;
                    int qa = (q & 1) == 0 ? xa : Mathf.Min(hx, xb), qb = (q & 1) == 0 ? Mathf.Min(hx, xb) : xb;
                    int ra = (q & 2) == 0 ? za : Mathf.Min(hz, zb), rb = (q & 2) == 0 ? Mathf.Min(hz, zb) : zb;
                    var qm = Quarter(ci, cj, q);
                    if (qb <= qa || rb <= ra) { qm.Clear(); continue; }
                    BuildPiece(qm, qa, qb, ra, rb, 1, out float lo, out float hi);
                    minY = Mathf.Min(minY, lo); maxY = Mathf.Max(maxY, hi);
                }
                if (was != 1) chunkMesh[cj, ci].Clear();
            }
            else
            {
                BuildPiece(chunkMesh[cj, ci], xa, xb, za, zb, st, out minY, out maxY);
                tm = System.Diagnostics.Stopwatch.GetTimestamp();
                if (was == 1 && quarterMesh != null && quarterMesh[cj, ci] != null)
                    foreach (var qm in quarterMesh[cj, ci]) if (qm != null) qm.Clear();
                if (quarterDirty != null) quarterDirty[cj, ci] = 0;
            }
            chunkBounds[cj, ci] = new Bounds(
                origin + new Vector3((xa + xb) * 0.5f * cell, (minY + maxY) * 0.5f, (za + zb) * 0.5f * cell),
                new Vector3((xb - xa) * cell + 1f, maxY - minY + 3f, (zb - za) * cell + 1f));
            long tt = System.Diagnostics.Stopwatch.GetTimestamp();
            // grass and the clods on loose dirt: at most every 0.15 s while a piece keeps changing (a blade at work)
            chunkDecorAt ??= new float[chunksZ, chunksX];
            chunkDecorStale ??= new bool[chunksZ, chunksX];
            float now = Time.realtimeSinceStartup;
            if (!changed) { }
            else if (!Application.isPlaying || now - chunkDecorAt[cj, ci] > 0.15f) RebuildDecor(ci, cj);
            else chunkDecorStale[cj, ci] = true;
            long te = System.Diagnostics.Stopwatch.GetTimestamp();
            double fq = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            DecorMs += (te - tt) * fq;
        }

        /// <summary>Draw the ground from square `xa`..`xb`, `za`..`zb`, every `st` squares, into `mesh`.</summary>
        void BuildPiece(Mesh mesh, int xa, int xb, int za, int zb, int st, out float minY, out float maxY)
        {
            verts.Clear(); norms.Clear();
            foreach (var t in tris) t.Clear();
            // the points this piece uses, each looked at once (not once for every facet it's a corner of)
            int nxp = (xb - xa + st - 1) / st + 1, nzp = (zb - za + st - 1) / st + 1;
            int np = nxp * nzp;
            if (vBuf.Length < np) { vBuf = new Vector3[np]; catBuf = new int[np]; scoreBuf = new int[np]; }
            minY = float.MaxValue; maxY = float.MinValue;
            int half = st / 2;
            for (int pz = 0; pz < nzp; pz++)
                for (int px = 0; px < nxp; px++)
                {
                    int x = Mathf.Min(xa + px * st, xb), z = Mathf.Min(za + pz * st, zb);
                    int k = pz * nxp + px;
                    var v = V(x, z);
                    catBuf[k] = Category(x, z, out scoreBuf[k]);
                    // dug and dumped dirt is lumpy, not glassy-smooth: a few cm up or down at each point (only how it's
                    // drawn: what you drive on and dig is unchanged), so every small facet catches the light its own way
                    // (ground that is actually smooth, a graded road or a trimmed floor, keeps its finish)
                    if (scoreBuf[k] > 0) v.y += Rough(x, z) * (scoreBuf[k] == 3 ? 0.05f : scoreBuf[k] == 2 ? 0.035f : 0.015f) * Lumpy(x, z);
                    vBuf[k] = v;
                    if (v.y < minY) minY = v.y;
                    if (v.y > maxY) maxY = v.y;
                    // a coarse piece still shows dug or dumped ground in the squares it skips
                    if (half > 0 && scoreBuf[k] == 0)
                        for (int dz = -half; dz <= half; dz += half)
                            for (int dx = -half; dx <= half; dx += half)
                            {
                                if (dx == 0 && dz == 0) continue;
                                int qx = Mathf.Clamp(x + dx, 0, res - 1), qz = Mathf.Clamp(z + dz, 0, res - 1);
                                int c2 = Category(qx, qz, out int s2);
                                if (s2 > scoreBuf[k]) { scoreBuf[k] = s2; catBuf[k] = c2; }
                            }
                }
            for (int pz = 0; pz < nzp - 1; pz++)
                for (int px = 0; px < nxp - 1; px++)
                {
                    int ka = pz * nxp + px, kb = ka + 1, kc = ka + nxp, kd = kc + 1;
                    // split each square along the diagonal whose corners are closest in height: a wall or the edge of a
                    // cut then runs along the facets instead of zig-zagging across them (no saw teeth)
                    float dAD = Mathf.Abs(vBuf[ka].y - vBuf[kd].y), dBC = Mathf.Abs(vBuf[kb].y - vBuf[kc].y);
                    bool alongAD = Mathf.Abs(dAD - dBC) > 0.02f ? dAD < dBC : ((px + pz + (xa + za) / st) & 1) == 0;
                    if (WallQuad(ka, kb, kc, kd)) continue;
                    if (alongAD) { TriK(ka, kc, kd); TriK(ka, kd, kb); }
                    else { TriK(ka, kc, kb); TriK(kb, kc, kd); }
                }
            // a coarse piece gets a short skirt round its edge, so no gap shows against a finer neighbour
            if (st > 1)
            {
                for (int px = 0; px < nxp - 1; px++) { Skirt(px, px + 1); Skirt((nzp - 1) * nxp + px + 1, (nzp - 1) * nxp + px); }
                for (int pz = 0; pz < nzp - 1; pz++) { Skirt((pz + 1) * nxp, pz * nxp); Skirt(pz * nxp + nxp - 1, (pz + 1) * nxp + nxp - 1); }
                minY -= 2f;
            }
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.subMeshCount = tris.Length;
            for (int k = 0; k < tris.Length; k++) mesh.SetTriangles(tris[k], k, false);
            var lb = new Bounds();
            lb.SetMinMax(new Vector3(xa * cell, minY, za * cell), new Vector3(xb * cell, maxY, zb * cell));
            mesh.bounds = lb;
            UploadMs += (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        }

        public static double UploadMs, DecorMs, ColliderMs; public static long ColliderSamples, ColliderCalls;
        float[,] chunkDecorAt;
        bool[,] chunkDecorStale;

        void RebuildDecor(int ci, int cj)
        {
            int xa = ci * chunkCells, xb = Mathf.Min(xa + chunkCells, res - 1);
            int za = cj * chunkCells, zb = Mathf.Min(za + chunkCells, res - 1);
            RebuildTufts(ci, cj, xa, xb, za, zb);
            RebuildClumps(ci, cj, xa, xb, za, zb);
            chunkDecorAt[cj, ci] = Time.realtimeSinceStartup;
            chunkDecorStale[cj, ci] = false;
        }

        /// <summary>Grass and clods held back while their piece was busy: put them right once it's had a moment.</summary>
        void CatchUpDecor()
        {
            if (chunkDecorStale == null) return;
            float now = Time.realtimeSinceStartup;
            int done = 0;
            for (int j = 0; j < chunksZ && done < 3; j++)
                for (int i = 0; i < chunksX && done < 3; i++)
                    if (chunkDecorStale[j, i] && now - chunkDecorAt[j, i] > 0.15f) { RebuildDecor(i, j); done++; }
        }

        void TriK(int a, int b, int c)
        {
            int sa = scoreBuf[a], sb = scoreBuf[b], sc = scoreBuf[c];
            int cat = sa >= sb && sa >= sc ? catBuf[a] : sb >= sc ? catBuf[b] : catBuf[c];
            Tri(vBuf[a], vBuf[b], vBuf[c], cat);
        }

        /// <summary>
        /// A ground square that's a tall wall (the side of a cut, 45 cm or more top to bottom): drawn as flat bands
        /// about 35 cm high instead of two long slivers, and, dug into the ground, each band coloured by the layer at
        /// its own depth, so a pit wall shows the topsoil, clay and sand in bands instead of streaks.
        /// Corners: a (x,z), b (x+1,z), c (x,z+1), d (x+1,z+1). False if it's not a wall (drawn the usual way).
        /// </summary>
        bool WallQuad(int ka, int kb, int kc, int kd)
        {
            Vector3 A = vBuf[ka], B = vBuf[kb], C = vBuf[kc], D = vBuf[kd];
            float lo = Mathf.Min(Mathf.Min(A.y, B.y), Mathf.Min(C.y, D.y)), hi = Mathf.Max(Mathf.Max(A.y, B.y), Mathf.Max(C.y, D.y));
            int best = Mathf.Max(Mathf.Max(scoreBuf[ka], scoreBuf[kb]), Mathf.Max(scoreBuf[kc], scoreBuf[kd]));
            if (hi - lo < 0.45f || best == 0) return false;
            int k = scoreBuf[ka] == best ? ka : scoreBuf[kb] == best ? kb : scoreBuf[kc] == best ? kc : kd;
            int cat = catBuf[k];
            int n = Mathf.Clamp(Mathf.CeilToInt((hi - lo) / 0.35f), 2, 16);
            // the wall falls across x (a-b, c-d differ) or across z (a-c, b-d differ): the bands run the other way
            bool acrossZ = Mathf.Abs(A.y - C.y) + Mathf.Abs(B.y - D.y) >= Mathf.Abs(A.y - B.y) + Mathf.Abs(C.y - D.y);
            for (int i = 0; i < n; i++)
            {
                float t0 = i / (float)n, t1 = (i + 1) / (float)n;
                Vector3 p0, q0, p1, q1; // p = the a side, q = the other: the band from t0 to t1
                if (acrossZ) { p0 = Vector3.Lerp(A, C, t0); p1 = Vector3.Lerp(A, C, t1); q0 = Vector3.Lerp(B, D, t0); q1 = Vector3.Lerp(B, D, t1); }
                else { p0 = Vector3.Lerp(A, B, t0); p1 = Vector3.Lerp(A, B, t1); q0 = Vector3.Lerp(C, D, t0); q1 = Vector3.Lerp(C, D, t1); }
                int bc = best >= 2 ? WallCat((p0 + p1 + q0 + q1) * 0.25f, cat) : cat;
                // (same winding as the plain square: a-c-d and a-d-b)
                if (acrossZ) { Tri(p0, p1, q1, bc); Tri(p0, q1, q0, bc); }
                else { Tri(p0, q0, q1, bc); Tri(p0, q1, p1, bc); }
            }
            return true;
        }

        /// <summary>The colour of the ground dug into at a point of a wall (local to the field): the layer at its depth.</summary>
        int WallCat(Vector3 local, int fallback)
        {
            int x = Mathf.Clamp(Mathf.RoundToInt(local.x / cell), 0, res - 1), z = Mathf.Clamp(Mathf.RoundToInt(local.z / cell), 0, res - 1);
            float y = local.y + baseY, cut = orig[z, x] - y;
            if (cut < 0.03f) return fallback;
            if (y <= Floor(x, z) + 0.05f && InClaim(origin.x + x * cell, origin.z + z * cell)) return Bedrock;
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

        void Skirt(int a, int b)
        {
            // (deep enough to cover the seam at a tall cut wall, and drawn as dirt: it's the side of the ground)
            Vector3 pa = vBuf[a], pb = vBuf[b], da = pa + Vector3.down * 2f, db = pb + Vector3.down * 2f;
            int cat = scoreBuf[a] >= scoreBuf[b] ? catBuf[a] : catBuf[b];
            if (Mathf.Max(scoreBuf[a], scoreBuf[b]) == 0) cat = Topsoil;
            Tri(pa, db, da, cat); Tri(pa, pb, db, cat);
        }

        Vector3 V(int x, int z) => new Vector3(x * cell, Surf(x, z) - baseY, z * cell);

        /// <summary>Which camera the ground's detail follows, and where it is (for the test log).</summary>
        public static string EyeInfo { get { var c = TuftRenderer.ActiveCamera(); return c == null ? "no camera" : $"camera '{c.name}' at {c.transform.position}"; } }

        /// <summary>How finely the piece of ground under a point is drawn right now (squares per facet), for the test.</summary>
        public int DrawStepAt(Vector3 p)
        {
            if (chunkLod == null) return 0;
            int i = Mathf.Clamp(Mathf.FloorToInt((p.x - origin.x) / cell / chunkCells), 0, chunksX - 1);
            int j = Mathf.Clamp(Mathf.FloorToInt((p.z - origin.z) / cell / chunkCells), 0, chunksZ - 1);
            return chunkLod[j, i];
        }

        /// <summary>A steady -1..1 for each point (the same every time and on every game).</summary>
        /// <summary>0 where the ground is smooth (a graded road), 1 where it is broken up: how lumpy to draw it.</summary>
        float Lumpy(int x, int z)
        {
            if (x < 1 || z < 1 || x >= res - 1 || z >= res - 1) return 1f;
            float c = Surf(x, z) * 2f;
            float d = Mathf.Abs(Surf(x - 1, z) + Surf(x + 1, z) - c) + Mathf.Abs(Surf(x, z - 1) + Surf(x, z + 1) - c);
            return Mathf.Clamp01((d - 0.01f) / 0.04f);
        }

        static float Rough(int x, int z)
        {
            uint h = (uint)(x * 73856093) ^ (uint)(z * 19349663) ^ 0x2545F491u;
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return (h & 1023) / 511.5f - 1f;
        }

        void Tri(Vector3 a, Vector3 b, Vector3 c, int cat)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            var n = Vector3.Cross(b - a, c - a).normalized; // every facet its own flat normal
            norms.Add(n); norms.Add(n); norms.Add(n);
            var t = tris[cat]; t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }

        // ------------------------------------------------------------------ grass tufts

        void RebuildTufts(int ci, int cj, int xa, int xb, int za, int zb)
        {
            var list = chunkTufts[cj, ci];
            list.Clear();
            if (grassTuftMesh == null || grassTuftMaterial == null) return;
            int st = detail;
            for (int z = za; z + st <= zb && list.Count < 1023; z += st)
                for (int x = xa; x + st <= xb && list.Count < 1023; x += st)
                {
                    int gx = x / st, gz = z / st;
                    uint hsh = (uint)(gx * 73856093) ^ (uint)(gz * 19349663) ^ (uint)seed;
                    hsh ^= hsh >> 13; hsh *= 0x5bd1e995; hsh ^= hsh >> 15;
                    if ((hsh & 7) == 0) continue; // a few bare spots
                    if (!IsGrass(x, z) || !IsGrass(x + st, z) || !IsGrass(x, z + st) || !IsGrass(x + st, z + st) || st > 1 && !IsGrass(x + st / 2, z + st / 2)) continue;
                    float ox = ((hsh >> 4) & 255) / 255f, oz = ((hsh >> 12) & 255) / 255f;
                    float rot = ((hsh >> 20) & 255) / 255f * 360f;
                    float sc = 0.7f + ((hsh >> 8) & 15) / 15f * 0.7f;
                    Vector3 p = origin + new Vector3((x + ox * st) * cell, 0f, (z + oz * st) * cell);
                    p.y = SurfaceY(p);
                    list.Add(Matrix4x4.TRS(p, Quaternion.Euler(0f, rot, 0f), Vector3.one * sc));
                }
        }

        bool IsGrass(int x, int z) => !trampled[z, x] && !hard[z, x] && loose[z, x] <= 0.005f && orig[z, x] - native[z, x] <= 0.03f;

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
