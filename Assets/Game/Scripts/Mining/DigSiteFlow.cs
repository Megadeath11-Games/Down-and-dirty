using System.Collections.Generic;
using UnityEngine;

namespace FPSBase.Mining
{
    /// <summary>
    /// How the dirt behaves once it's moved (second part of DigSite):
    ///  - landing clods from DirtGrains join the loose layer,
    ///  - loose dirt stands at a slope that depends on what it is (sand runs flat, clay stands steep),
    ///  - dug walls that are too steep for their soil cave in after a while and slide into the hole
    ///    (sand quickly, clay hardly at all, bedrock never),
    ///  - heavy wheels and tracks press ruts into loose dirt and squeeze it up beside them.
    /// </summary>
    public partial class DigSite
    {
        [Header("Dirt behaviour")]
        [Tooltip("Steep dug walls cave in after a while (how steep they can stand depends on the soil).")]
        public bool wallCollapse = true;
        [Tooltip("How deep wheels and tracks rut loose dirt (0 = no ruts).")]
        public float rutScale = 1f;

        struct Crumble { public int cell; public float due; }
        readonly List<Crumble> crumbles = new();
        readonly HashSet<int> crumbleSet = new();

        /// <summary>Walls waiting to cave in.</summary>
        public int PendingCollapses => crumbles.Count;
        /// <summary>m^3 of wall that has caved in so far.</summary>
        public float CollapsedVolume { get; private set; }

        Vector3 CellPos(int x, int z) => new Vector3(origin.x + x * cell, Surf(x, z), origin.z + z * cell);

        /// <summary>Up-facing normal of the dirt surface at a world X/Z.</summary>
        public Vector3 SurfaceNormal(Vector3 p)
        {
            float e = cell;
            float hx = SurfaceY(p + new Vector3(e, 0f, 0f)) - SurfaceY(p - new Vector3(e, 0f, 0f));
            float hz = SurfaceY(p + new Vector3(0f, 0f, e)) - SurfaceY(p - new Vector3(0f, 0f, e));
            return new Vector3(-hx, 2f * e, -hz).normalized;
        }

        /// <summary>A clod came to rest here: it becomes part of the loose dirt.</summary>
        public void Land(Vector3 p, MaterialLoad l)
        {
            if (l.IsEmpty) return;
            var rocks = l.TakeRocks();
            if (!rocks.IsEmpty) Boulder.SpawnFromLoad(p, rocks);
            AddLoose(Mathf.RoundToInt((p.x - origin.x) / cell), Mathf.RoundToInt((p.z - origin.z) / cell), l);
        }

        /// <summary>Steepest step (m per cell) the loose dirt in this cell can hold: dry sand ~30°, dirt ~34°, clay ~42°.</summary>
        float MaxStepAt(int x, int z)
        {
            float l = loose[z, x];
            if (l < 0.03f) return maxStep;
            float lv = l * cellArea;
            float a = reposeAngle + (looseGravel[z, x] / lv) * 4f - (looseSand[z, x] / lv) * 4.5f + (looseClay[z, x] / lv) * 8f;
            return cell * Mathf.Tan(a * Mathf.Deg2Rad);
        }

        // ------------------------------------------------------------------ cave-ins

        /// <summary>Steepest step a dug wall of this cell's soil can hold, and how long it takes to give way.</summary>
        float WallStep(int x, int z, out float delayMin, out float delayMax)
        {
            delayMin = 1f; delayMax = 4f;
            if (native[z, x] <= Floor(x, z) + 0.05f) return float.MaxValue; // bedrock / dig floor never caves
            float wx = origin.x + x * cell, wz = origin.z + z * cell;
            float depth = orig[z, x] - native[z, x] + 0.2f;
            float ang;
            switch (LayerAt(wx, wz, depth).kind)
            {
                case SoilKind.Clay: ang = 80f; delayMin = 3f; delayMax = 10f; break;
                case SoilKind.Sand: ang = 50f; delayMin = 0.2f; delayMax = 1.2f; break;
                case SoilKind.Gravel: ang = 60f; delayMin = 0.6f; delayMax = 2.5f; break;
                case SoilKind.PayGravel: ang = 62f; delayMin = 0.8f; delayMax = 3f; break;
                default: ang = 72f; delayMin = 1.5f; delayMax = 5f; break; // topsoil: roots hold it together
            }
            return cell * Mathf.Tan(ang * Mathf.Deg2Rad);
        }

        float LowestNeighbour(int x, int z, out int lk)
        {
            float lowest = float.MaxValue; lk = 0;
            for (int k = 0; k < 4; k++)
            {
                float s = Surf(x + nx[k], z + nz[k]);
                if (s < lowest) { lowest = s; lk = k; }
            }
            return lowest;
        }

        /// <summary>Called for every cell the slump solver looks at: is its native wall too steep to stand?</summary>
        void CheckWall(int x, int z)
        {
            if (!wallCollapse) return;
            int i = z * res + x;
            if (crumbleSet.Contains(i)) return;
            float d = native[z, x] - LowestNeighbour(x, z, out _);
            if (d <= cell * 1.15f) return; // under ~49°: everything stands
            float step = WallStep(x, z, out float a, out float b);
            if (d <= step) return;
            crumbleSet.Add(i);
            crumbles.Add(new Crumble { cell = i, due = Time.time + Random.Range(a, b) });
        }

        /// <summary>Cave in the walls whose time has come (or all of them, right now).</summary>
        void ProcessCrumbles(bool all)
        {
            if (crumbles.Count == 0) return;
            float now = Time.time;
            int done = 0;
            for (int k = crumbles.Count - 1; k >= 0 && done < 64; k--)
            {
                var c = crumbles[k];
                if (!all && c.due > now) continue;
                crumbles.RemoveAt(k);
                crumbleSet.Remove(c.cell);
                Collapse(c.cell % res, c.cell / res, !all);
                done++;
            }
        }

        void Collapse(int x, int z, bool asGrains)
        {
            float lowest = LowestNeighbour(x, z, out int lk);
            float d = native[z, x] - lowest;
            float step = WallStep(x, z, out _, out _);
            if (d <= step) return;
            float t = Mathf.Min((d - step) * 0.6f + 0.12f, native[z, x] - Floor(x, z));
            if (t <= 0.01f) return;
            var m = Remove(x, z, loose[z, x] + t, native[z, x] - t);
            if (m.IsEmpty) return;
            CollapsedVolume += t * cellArea;
            Touch(x, z);
            int ax = x + nx[lk], az = z + nz[lk];
            Vector3 dir = new Vector3(nx[lk], 0f, nz[lk]);
            Vector3 top = CellPos(x, z) + Vector3.up * 0.1f + dir * (cell * 0.45f);
            if (asGrains) DirtGrains.Fall(top, m, dir * Random.Range(0.4f, 1.1f), cell * 0.4f);
            else AddLoose(ax, az, m);
            AfterGroundMoved(top, 1f, top);
        }

        /// <summary>What a footstep at this point sounds like.</summary>
        public FPSBase.Player.PlayerBody.StepSurface StepSurfaceAt(Vector3 p)
        {
            int x = Mathf.Clamp(Mathf.RoundToInt((p.x - origin.x) / cell), 0, res - 1);
            int z = Mathf.Clamp(Mathf.RoundToInt((p.z - origin.z) / cell), 0, res - 1);
            switch (Category(x, z, out _))
            {
                case GrassA: case GrassB: return FPSBase.Player.PlayerBody.StepSurface.Grass;
                case SandCat: return FPSBase.Player.PlayerBody.StepSurface.Sand;
                case GravelCat: case Deep: case Paydirt: return FPSBase.Player.PlayerBody.StepSurface.Gravel;
                case Bedrock: return FPSBase.Player.PlayerBody.StepSurface.Hard;
                default: return FPSBase.Player.PlayerBody.StepSurface.Dirt;
            }
        }

        // ------------------------------------------------------------------ ruts

        /// <summary>
        /// A wheel or track pad rolling over loose dirt at `at` with `massOnContact` kg on it: presses a rut and
        /// squeezes the dirt up into little berms either side. Only loose dirt ruts; firm ground just loses its grass.
        /// </summary>
        public void Rut(Vector3 at, float radius, float massOnContact, Vector3 velocity)
        {
            if (rutScale <= 0f || !Contains(at)) return;
            velocity.y = 0f;
            if (velocity.sqrMagnitude < 0.2f) return;
            Vector3 f = velocity.normalized, right = new Vector3(f.z, 0f, -f.x);
            float depth = Mathf.Clamp(0.025f + massOnContact / 20000f * 0.1f, 0.02f, 0.2f) * rutScale;
            // the dirt beside the track is the reference: the rut goes `depth` below it
            float side = radius + 0.6f;
            float target = Mathf.Min(SurfaceY(at + right * side), SurfaceY(at - right * side)) - depth;
            Range(at, radius, out int x0, out int z0, out int x1, out int z1);
            bool moved = false;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float rx = origin.x + x * cell - at.x, rz = origin.z + z * cell - at.z;
                    if (rx * rx + rz * rz > radius * radius) continue;
                    if (loose[z, x] < 0.02f) continue;
                    float s = Surf(x, z);
                    if (s <= target) continue;
                    float take = Mathf.Min(Mathf.Min(0.015f, s - target), loose[z, x]);
                    var m = Remove(x, z, take, native[z, x]);
                    if (m.IsEmpty) continue;
                    float lat = rx * right.x + rz * right.z;
                    float sgn = lat >= 0f ? 1f : -1f;
                    Vector3 to = new Vector3(origin.x + x * cell, 0f, origin.z + z * cell) + right * (sgn * (radius - Mathf.Abs(lat) + cell));
                    AddLoose(Mathf.RoundToInt((to.x - origin.x) / cell), Mathf.RoundToInt((to.z - origin.z) / cell), m);
                    Touch(x, z);
                    moved = true;
                }
            if (moved && Random.value < 0.3f)
                DirtGrains.Spray(at + Vector3.up * 0.1f - f * radius, -f * 0.8f + Vector3.up * 1.2f + Random.insideUnitSphere * 0.4f);
        }

        [Tooltip("How much a machine's weight presses its tracks / tyres into undisturbed ground (0 = never).")]
        public float trackPressScale = 1f;

        /// <summary>Deepest a machine of `tonnes` presses into native ground just driving over it (m).</summary>
        public static float PressDepth(float tonnes) => Mathf.Clamp(0.008f + tonnes * 0.0011f, 0.008f, 0.1f);

        /// <summary>
        /// A track or tyre of a machine weighing `tonnes` bears on native ground at `at`, rolling along `along` with
        /// the ground contact moving at `contactVel`. Rolling straight compacts a shallow mark (deeper the heavier
        /// the machine, deepening a little with every pass up to a limit). Turning scrubs the track sideways: that
        /// tears up the top of the ground and shoves it out into a berm on the outside of the turn.
        /// </summary>
        public void TrackPress(Vector3 at, float radius, float tonnes, Vector3 along, Vector3 contactVel)
        {
            if (trackPressScale <= 0f || !Contains(at)) return;
            contactVel.y = 0f; along.y = 0f;
            if (along.sqrMagnitude < 1e-4f || contactVel.sqrMagnitude < 0.03f) return;
            along.Normalize();
            Vector3 right = new Vector3(along.z, 0f, -along.x);
            float slipV = Vector3.Dot(contactVel, right);
            float scrub = Mathf.Clamp01((Mathf.Abs(slipV) - 0.15f) / 0.6f);
            float sink = PressDepth(tonnes) * trackPressScale * (1f + 1.5f * scrub);
            // the ground beside the track is the reference (the higher side, so a bank edge isn't carved away)
            float side = radius + 0.6f;
            float target = Mathf.Max(SurfaceY(at + right * side), SurfaceY(at - right * side)) - sink;
            float perPass = 0.002f + tonnes * 0.00006f;
            Vector3 push = right * (slipV >= 0f ? 1f : -1f);
            Range(at, radius, out int x0, out int z0, out int x1, out int z1);
            bool scuffed = false;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float rx = origin.x + x * cell - at.x, rz = origin.z + z * cell - at.z;
                    if (rx * rx + rz * rz > radius * radius) continue;
                    if (loose[z, x] > 0.02f) continue; // loose dirt ruts instead (Rut)
                    float n = native[z, x], low = Mathf.Max(target, Floor(x, z) + 0.02f);
                    if (n <= low) continue;
                    float d = Mathf.Min(perPass * (1f + scrub), n - low);
                    if (scrub > 0.1f)
                    {
                        // scrubbed out sideways: real dirt, shoved to the outside of the turn
                        var m = Remove(x, z, d, low);
                        if (m.IsEmpty) continue;
                        float lat = rx * push.x + rz * push.z;
                        Vector3 to = new Vector3(origin.x + x * cell, 0f, origin.z + z * cell) + push * (radius - lat + cell);
                        AddLoose(Mathf.RoundToInt((to.x - origin.x) / cell), Mathf.RoundToInt((to.z - origin.z) / cell), m);
                        scuffed = true;
                    }
                    else native[z, x] -= d * 0.7f; // compacted: the soil is pressed denser, nothing moves
                    trampled[z, x] = true;
                    Touch(x, z);
                    pressedVolume += d * cellArea;
                }
            if (scuffed && Random.value < 0.35f)
                DirtGrains.Spray(at + Vector3.up * 0.08f, push * 1.1f + Vector3.up * 0.9f + Random.insideUnitSphere * 0.3f);
        }

        float pressedVolume;
        /// <summary>Ground pressed or scrubbed by machine tracks/tyres so far (m3, for the test).</summary>
        public float PressedVolume => pressedVolume;
    }
}
