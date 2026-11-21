using System.Collections.Generic;
using FPSBase.Core;
using FPSBase.Mining;
using UnityEngine;

namespace FPSBase.City
{
    /// <summary>
    /// Pouring the foundation. Concrete that lands on the site runs to the lowest squares near where it lands that
    /// still need it (the footing trenches first, then the slab) and levels off, up to the top of the footings or the
    /// slab. Each square it fills becomes solid ground at the concrete's top (machines drive on it, buckets stop at
    /// it). Once the pour's done it cures for a few minutes, and then the site's ready for the house.
    /// </summary>
    public partial class ConstructionSite : IConcreteReceiver
    {
        // the squares that take concrete (packed index), where they are on the plan, how high they fill
        readonly List<int> concCells = new();
        readonly List<Vector2> concPlan = new();
        readonly Dictionary<int, int> concOf = new();
        float[] concTop, concBottom, concTarget;

        public const float NeedConcrete = 0.97f;
        public const float CureSeconds = 180f;
        /// <summary>m³ of concrete in the forms, and how much the whole foundation takes (as the ground's been dug).</summary>
        public float ConcretePoured { get; private set; }
        public float ConcreteNeeded { get; private set; }
        public float ConcreteDone => ConcreteNeeded > 0.01f ? Mathf.Clamp01(ConcretePoured / ConcreteNeeded) : 0f;
        /// <summary>Seconds of curing left (below 0: not curing).</summary>
        public float CureLeft { get; private set; } = -1f;
        public bool Curing => CureLeft > 0f && State == SiteState.PouringConcrete;
        public string PourSide { get; private set; } = "";

        MeshFilter concMesh;
        Material concMat;
        Color wet, dry;
        bool concDirty;
        float concRebuild;

        void OnEnableConcrete() { if (!ConcreteFlow.Receivers.Contains(this)) ConcreteFlow.Receivers.Add(this); }
        void OnDisableConcrete() => ConcreteFlow.Receivers.Remove(this);

        void ClearConcreteCells() { concCells.Clear(); concPlan.Clear(); concOf.Clear(); }

        /// <summary>Survey: does this square take concrete? (Under a footing, or inside the outline for a slab.)</summary>
        void SurveyConcreteCell(int idx, Vector2 pl, float footingDist)
        {
            if (footingDist > Plan.footingWidth * 0.5f && !(Plan.slab && Plan.Inside(pl))) return;
            concOf[idx] = concCells.Count;
            concCells.Add(idx); concPlan.Add(pl);
        }

        void InitConcrete()
        {
            int n = concCells.Count;
            concTop = new float[n]; concBottom = new float[n]; concTarget = new float[n];
            float top = PadY + (Plan.slab ? Plan.slabThickness : 0f);
            for (int i = 0; i < n; i++) { concTop[i] = float.NaN; concBottom[i] = float.NaN; concTarget[i] = top; }
        }

        /// <summary>The top of what's in a square: its concrete, or the ground if it has none yet.</summary>
        float ConcBase(int i)
        {
            if (!float.IsNaN(concTop[i])) return concTop[i];
            Site.CellXZ(concCells[i], out int x, out int z);
            return Site.CellSurface(x, z);
        }

        public float TakeConcrete(Vector3 at, float m3, Material mat)
        {
            if (Preview || Plan == null || Site == null || concTop == null || m3 <= 0f) return 0f;
            if (State != SiteState.ReadyForConcrete && State != SiteState.PouringConcrete) return 0f;
            if (CureLeft >= 0f) return 0f; // poured and setting: more just runs off
            Vector2 p = ToPlan(at);
            var b = Plan.Bounds;
            float m = Plan.footingWidth + 0.4f;
            if (p.x < b.xMin - m || p.x > b.xMax + m || p.y < b.yMin - m || p.y > b.yMax + m) return 0f;
            if (at.y < PadY - Plan.footingDepth - 1.5f || at.y > PadY + 1.5f) return 0f;
            if (concMat == null) MakeConcreteMaterial(mat);
            float left = m3;
            // it runs to the low spots near where it lands; if they're full, a bit further
            foreach (float r in new[] { 2.5f, 5f, 9f })
            {
                left -= FillNear(p, r, left);
                if (left <= 1e-6f) break;
            }
            float took = m3 - left;
            if (took > 0f)
            {
                concDirty = true;
                if (State == SiteState.ReadyForConcrete)
                {
                    State = SiteState.PouringConcrete;
                    GameUI.Toast($"{Plan.title}: the first concrete's in. Pour the footings{(Plan.slab ? " and the slab" : "")} all round: it runs to the low spots near the chute");
                    Redraw();
                }
            }
            return took;
        }

        static readonly List<int> cand = new();
        static readonly List<float> candBase = new();

        /// <summary>Level-fill the squares within `r` of `p` that still need concrete with up to `m3`. Returns m³ used.</summary>
        float FillNear(Vector2 p, float r, float m3)
        {
            cand.Clear(); candBase.Clear();
            float lo = float.MaxValue, hi = float.MinValue, r2 = r * r;
            for (int i = 0; i < concCells.Count; i++)
            {
                if ((concPlan[i] - p).sqrMagnitude > r2) continue;
                float cur = ConcBase(i);
                if (cur >= concTarget[i] - 0.002f) continue;
                cand.Add(i); candBase.Add(cur);
                lo = Mathf.Min(lo, cur); hi = Mathf.Max(hi, concTarget[i]);
            }
            if (cand.Count == 0) return 0f;
            float area = Site.Cell * Site.Cell;
            float Vol(float level)
            {
                float v = 0f;
                for (int k = 0; k < cand.Count; k++) v += Mathf.Clamp(level - candBase[k], 0f, concTarget[cand[k]] - candBase[k]);
                return v * area;
            }
            float L = hi;
            if (Vol(hi) > m3)
            {
                float a = lo, c = hi;
                for (int it = 0; it < 26; it++) { float mid = (a + c) * 0.5f; if (Vol(mid) > m3) c = mid; else a = mid; }
                L = a;
            }
            float used = 0f;
            for (int k = 0; k < cand.Count; k++)
            {
                int i = cand[k];
                float cur = candBase[k], nt = Mathf.Min(concTarget[i], Mathf.Max(cur, L));
                if (nt <= cur + 1e-5f) continue;
                if (float.IsNaN(concTop[i])) concBottom[i] = cur;
                concTop[i] = nt;
                used += (nt - cur) * area;
                Site.CellXZ(concCells[i], out int x, out int z);
                Site.SetSolid(x, z, nt);
            }
            return Mathf.Min(m3, used);
        }

        /// <summary>How much is in and how much it takes; start curing once it's poured; curing to ready for the house.</summary>
        void ConcreteCheck()
        {
            if (concTop == null) return;
            float area = Site.Cell * Site.Cell, poured = 0f, need = 0f;
            Vector2 offSum = Vector2.zero; int short_ = 0;
            for (int i = 0; i < concCells.Count; i++)
            {
                float bottom = float.IsNaN(concBottom[i]) ? ConcBase(i) : concBottom[i];
                float want = Mathf.Max(0f, concTarget[i] - bottom);
                need += want;
                float have = float.IsNaN(concTop[i]) ? 0f : Mathf.Max(0f, concTop[i] - concBottom[i]);
                poured += Mathf.Min(have, want);
                if (want - have > 0.03f) { short_++; offSum += concPlan[i]; }
            }
            ConcretePoured = poured * area; ConcreteNeeded = need * area;
            Vector3 dir = transform.TransformDirection(new Vector3(offSum.x, 0f, offSum.y));
            PourSide = short_ > 0 ? Compass(new Vector2(dir.x, dir.z)) : "";
            if (State == SiteState.PouringConcrete && CureLeft < 0f && ConcreteDone >= NeedConcrete)
            {
                Screed();
                CureLeft = CureSeconds;
                GameUI.Toast($"{Plan.title}: the foundation's poured ({ConcretePoured:0.#} m³). Let it cure for {CureSeconds / 60f:0} minutes");
            }
            Status = State switch
            {
                SiteState.ReadyForHouse when HouseStarted => $"The crew's putting the house up: {HouseProgress * 100f:0}% ({HouseStage}).",
                SiteState.ReadyForHouse => $"Foundation poured and cured ({ConcretePoured:0.#} m³): ready for the house. Have the crew build it for ${Plan.BuildCost:N0}; it sells for ${Plan.SalePrice:N0}.",
                SiteState.Completed => Sold ? $"{Address}: sold to {Buyer} for ${Plan.SalePrice:N0}." : $"Built: {Address}. It's yours: sell it for ${Plan.SalePrice:N0}.",
                _ when Curing => $"Poured ({ConcretePoured:0.#} m³). Curing: {CureText()} left.",
                _ => $"Pouring concrete: {ConcretePoured:0.0} of {ConcreteNeeded:0.0} m³ in ({ConcreteDone * 100f:0}%, needs {NeedConcrete * 100f:0}%)." +
                     (PourSide.Length > 0 ? $"  Still to fill: mostly the {PourSide} side." : ""),
            };
        }

        /// <summary>
        /// Finishing the pour: ground standing a little above the slab inside the forms (the lip left beside a
        /// trench) is cut off flat and finished with the rest, so no dirt sticks up through the slab.
        /// </summary>
        void Screed()
        {
            bool any = false;
            for (int i = 0; i < concCells.Count; i++)
            {
                if (!float.IsNaN(concTop[i])) continue;
                Site.CellXZ(concCells[i], out int x, out int z);
                float g = Site.CellSurface(x, z);
                if (g < concTarget[i] - 0.002f || g > concTarget[i] + 0.5f) continue;
                concBottom[i] = concTop[i] = concTarget[i];
                Site.SetSolid(x, z, concTarget[i], true);
                any = true;
            }
            if (any) concDirty = true;
        }

        public string CureText() { float t = Mathf.Max(0f, CureLeft); return $"{Mathf.FloorToInt(t / 60f)}:{Mathf.FloorToInt(t % 60f):00}"; }

        /// <summary>Every frame: curing, and the concrete's mesh when it changed.</summary>
        void ConcreteUpdate(float dt)
        {
            if (Curing)
            {
                CureLeft -= dt;
                if (CureLeft <= 0f)
                {
                    CureLeft = 0f; State = SiteState.ReadyForHouse;
                    GameUI.Toast($"{Plan.title}: the foundation's cured. Ready for the house!");
                    ConcreteCheck(); Redraw();
                }
                TintConcrete();
            }
            if (concDirty && (concRebuild -= dt) <= 0f) { concRebuild = 0.2f; concDirty = false; RebuildConcrete(); }
        }

        /// <summary>Tests: finish curing now.</summary>
        public void TestCure() { if (Curing) CureLeft = 0.001f; }

        void MakeConcreteMaterial(Material source)
        {
            if (source == null && ConcretePlant.Instance != null) source = ConcretePlant.Instance.concreteMaterial;
            if (source != null) concMat = new Material(source);
            else
            {
                concMat = new Material(Shader.Find("HDRP/Lit"));
                concMat.SetColor("_BaseColor", new Color(0.4f, 0.4f, 0.39f));
            }
            wet = concMat.HasProperty("_BaseColor") ? concMat.GetColor("_BaseColor") : new Color(0.4f, 0.4f, 0.39f);
            dry = new Color(Mathf.Min(1f, wet.r * 1.55f), Mathf.Min(1f, wet.g * 1.55f), Mathf.Min(1f, wet.b * 1.5f), 1f);
            if (concMesh != null) concMesh.GetComponent<MeshRenderer>().sharedMaterial = concMat;
            TintConcrete();
        }

        void TintConcrete()
        {
            if (concMat == null) return;
            float k = State >= SiteState.ReadyForHouse ? 1f : Curing ? 1f - CureLeft / CureSeconds : 0f;
            if (concMat.HasProperty("_BaseColor")) concMat.SetColor("_BaseColor", Color.Lerp(wet, dry, k));
            if (concMat.HasProperty("_Smoothness")) concMat.SetFloat("_Smoothness", Mathf.Lerp(0.72f, 0.18f, k));
        }

        static readonly List<Vector3> cv = new();
        static readonly List<int> ct = new();

        /// <summary>One mesh for all the poured squares: a top on each, and a face down the side wherever the next square is lower.</summary>
        void RebuildConcrete()
        {
            if (concTop == null) return;
            if (concMesh == null)
            {
                var go = new GameObject("Concrete");
                go.transform.SetParent(transform, false);
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); // (world space: the dig squares run along x and z)
                concMesh = go.AddComponent<MeshFilter>();
                concMesh.sharedMesh = new Mesh { name = "Site concrete", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                var mr = go.AddComponent<MeshRenderer>();
                if (concMat == null) MakeConcreteMaterial(null);
                mr.sharedMaterial = concMat;
            }
            cv.Clear(); ct.Clear();
            float h = Site.Cell * 0.5f;
            const float lift = 0.012f;
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int o = cv.Count;
                cv.Add(a); cv.Add(b); cv.Add(c); cv.Add(d);
                ct.Add(o); ct.Add(o + 1); ct.Add(o + 2); ct.Add(o); ct.Add(o + 2); ct.Add(o + 3);
            }
            int[] ox = { 1, -1, 0, 0 }, oz = { 0, 0, 1, -1 };
            for (int i = 0; i < concCells.Count; i++)
            {
                if (float.IsNaN(concTop[i])) continue;
                Site.CellXZ(concCells[i], out int x, out int z);
                Vector3 c = Site.CellPos(x, z);
                float y = concTop[i] + lift;
                Quad(new Vector3(c.x - h, y, c.z - h), new Vector3(c.x - h, y, c.z + h), new Vector3(c.x + h, y, c.z + h), new Vector3(c.x + h, y, c.z - h));
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + ox[k], nz = z + oz[k];
                    float ny = concOf.TryGetValue(Site.CellIndex(nx, nz), out int j) && !float.IsNaN(concTop[j]) ? concTop[j] + lift : Site.CellSurface(nx, nz);
                    if (ny >= y - 0.02f) continue;
                    ny = Mathf.Max(ny, y - 3f);
                    // the face on that side, facing out towards the lower square
                    Vector3 e0, e1;
                    if (k == 0) { e0 = new Vector3(c.x + h, 0f, c.z - h); e1 = new Vector3(c.x + h, 0f, c.z + h); }
                    else if (k == 1) { e0 = new Vector3(c.x - h, 0f, c.z + h); e1 = new Vector3(c.x - h, 0f, c.z - h); }
                    else if (k == 2) { e0 = new Vector3(c.x + h, 0f, c.z + h); e1 = new Vector3(c.x - h, 0f, c.z + h); }
                    else { e0 = new Vector3(c.x - h, 0f, c.z - h); e1 = new Vector3(c.x + h, 0f, c.z - h); }
                    Quad(new Vector3(e0.x, ny, e0.z), new Vector3(e0.x, y, e0.z), new Vector3(e1.x, y, e1.z), new Vector3(e1.x, ny, e1.z));
                }
            }
            var m = concMesh.sharedMesh;
            m.Clear();
            m.SetVertices(cv); m.SetTriangles(ct, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
        }

        /// <summary>The site's given up: its concrete stays in the ground, but it can be dug out again.</summary>
        public void ReleaseGround()
        {
            if (concTop == null || Site == null) return;
            for (int i = 0; i < concCells.Count; i++)
                if (!float.IsNaN(concTop[i])) { Site.CellXZ(concCells[i], out int x, out int z); Site.ClearSolid(x, z); }
        }

        /// <summary>Tests: squares with concrete in them, and how many take it.</summary>
        public int ConcreteSquares { get { int n = 0; if (concTop != null) foreach (var t in concTop) if (!float.IsNaN(t)) n++; return n; } }
        public int ConcreteCells => concCells.Count;
        /// <summary>Tests: a square with concrete in it (its world point at the concrete's top), false if none.</summary>
        public bool ConcreteSample(int nth, out Vector3 world)
        {
            world = default;
            if (concTop == null) return false;
            for (int i = 0; i < concCells.Count; i++)
                if (!float.IsNaN(concTop[i]) && nth-- <= 0)
                {
                    Site.CellXZ(concCells[i], out int x, out int z);
                    world = Site.CellPos(x, z); world.y = concTop[i];
                    return true;
                }
            return false;
        }

        // ------------------------------------------------------------------ saving

        void WriteConcrete(System.IO.BinaryWriter w)
        {
            w.Write(CureLeft);
            int n = ConcreteSquares;
            w.Write(n);
            if (concTop == null) return;
            for (int i = 0; i < concCells.Count; i++)
                if (!float.IsNaN(concTop[i])) { w.Write(i); w.Write(concBottom[i]); w.Write(concTop[i]); }
        }

        /// <summary>A friend's concrete: every square as full as either copy has it.</summary>
        public void MergeConcrete(System.IO.BinaryReader r)
        {
            float cure = r.ReadSingle();
            int n = r.ReadInt32();
            bool any = false;
            for (int k = 0; k < n; k++)
            {
                int i = r.ReadInt32(); float bot = r.ReadSingle(), top = r.ReadSingle();
                if (concTop == null || i < 0 || i >= concCells.Count) continue;
                if (!float.IsNaN(concTop[i]) && concTop[i] >= top - 0.001f) continue;
                if (float.IsNaN(concBottom[i]) || bot < concBottom[i]) concBottom[i] = bot;
                concTop[i] = top;
                Site.CellXZ(concCells[i], out int x, out int z);
                Site.SetSolid(x, z, top);
                any = true;
            }
            // the curing clock: started by whoever finished the pour, and the furthest on wins
            if (cure >= 0f && (CureLeft < 0f || cure < CureLeft) && State == SiteState.PouringConcrete) CureLeft = cure;
            if (any) { concDirty = true; concRebuild = 0f; }
            ConcreteCheck();
            Redraw();
            TintConcrete();
        }

        public void ReadConcrete(System.IO.BinaryReader r)
        {
            CureLeft = r.ReadSingle();
            int n = r.ReadInt32();
            for (int k = 0; k < n; k++)
            {
                int i = r.ReadInt32(); float bot = r.ReadSingle(), top = r.ReadSingle();
                if (concTop == null || i < 0 || i >= concCells.Count) continue;
                concBottom[i] = bot; concTop[i] = top;
                Site.CellXZ(concCells[i], out int x, out int z);
                Site.SetSolid(x, z, top);
            }
            if (n > 0) { concDirty = true; concRebuild = 0f; }
            ConcreteCheck();
            Redraw();
            TintConcrete();
        }
    }
}
