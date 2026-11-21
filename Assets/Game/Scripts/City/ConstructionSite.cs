using System.Collections.Generic;
using FPSBase.Core;
using FPSBase.Mining;
using UnityEngine;

namespace FPSBase.City
{
    public enum SiteState { BlueprintPlaced, Excavating, ReadyForConcrete, PouringConcrete, ReadyForHouse, Completed }

    /// <summary>
    /// A house being built: a plan set out on the ground. The blue outline shows the pad (levelled to one height)
    /// and the footing trenches round it and under its inside walls (dug to a depth below the pad). The ground under
    /// it is checked twice a second: each patch of pad and each stretch of trench turns green as it's done (a patch
    /// standing proud shows orange on top of the dirt), and once the pad and the footings pass, it's ready for
    /// concrete. Stages: BlueprintPlaced → Excavating → ReadyForConcrete → PouringConcrete → ReadyForHouse → Completed.
    /// </summary>
    public partial class ConstructionSite : MonoBehaviour
    {
        public HousePlan Plan { get; private set; }
        public DigSite Site { get; private set; }
        public SiteState State { get; private set; }
        /// <summary>The finished pad's height (world m); the footings go `Plan.footingDepth` below it.</summary>
        public float PadY { get; private set; }
        public float Yaw { get; private set; }
        public int Id;
        /// <summary>A plan being moved about before it's set down: drawn, not checked.</summary>
        public bool Preview { get; private set; }
        public bool PreviewValid = true;

        /// <summary>Share of the pad that's level (0..1), of the footings dug to depth, and how many squares of pad are too high / too low.</summary>
        public float PadDone { get; private set; }
        public float TrenchDone { get; private set; }
        public int PadHigh { get; private set; }
        public int PadLow { get; private set; }
        /// <summary>Which way from the middle most of the unfinished pad is (N, NE...), for the hint.</summary>
        public string PadWorstSide { get; private set; } = "";
        public bool DigDone => Plan != null && PadDone >= Plan.needPad && TrenchDone >= Plan.needTrench;
        public string Status { get; private set; } = "";

        static readonly List<ConstructionSite> all = new();
        public static IReadOnlyList<ConstructionSite> All => all;

        // the squares under it, packed (z * res + x)
        readonly List<int> padCells = new(), trenchCells = new();
        readonly List<int> padTileOf = new(), trenchPieceOf = new();
        // the pad in 1 m patches and the footings in ~1 m stretches (plan metres), for the colours
        readonly List<Vector2> tiles = new();
        readonly List<(Vector2 a, Vector2 b)> pieces = new();
        int[] tileOk, tileHigh, tileLow, tileN, pieceOk, pieceN;
        float[] tileTop;
        MeshFilter[] layers;
        float checkTimer;
        MapMarker marker;

        void OnEnable() { if (!all.Contains(this)) all.Add(this); OnEnableConcrete(); }
        void OnDisable() { all.Remove(this); OnDisableConcrete(); }

        // ------------------------------------------------------------------ setting out

        /// <summary>Set a plan out on the ground: its middle at `center` (x/z), turned `yaw`, the pad at `padY`.</summary>
        public void Setup(HousePlan plan, DigSite site, Vector3 center, float yaw, float padY, bool preview)
        {
            Plan = plan; Site = site; Yaw = yaw; PadY = padY; Preview = preview;
            transform.SetPositionAndRotation(new Vector3(center.x, padY, center.z), Quaternion.Euler(0f, yaw, 0f));
            if (layers == null) BuildLayers();
            if (!preview) Survey();
            Redraw();
            if (!preview && marker == null)
            {
                var mk = new GameObject("Map marker");
                mk.transform.SetParent(transform, false);
                marker = mk.AddComponent<MapMarker>();
                marker.color = new Color(0.4f, 0.8f, 1f); marker.arriveOffset = new Vector3(0f, 0f, -(plan.Bounds.height * 0.5f + 5f));
            }
            if (marker != null) marker.label = $"Building: {plan.title}";
        }

        /// <summary>Move a preview (re-drawn where it is now).</summary>
        public void MovePreview(Vector3 center, float yaw, float padY, DigSite site)
        {
            Site = site; Yaw = yaw; PadY = padY;
            transform.SetPositionAndRotation(new Vector3(center.x, padY, center.z), Quaternion.Euler(0f, yaw, 0f));
            Redraw();
        }

        public Vector3 ToWorld(Vector2 plan) => transform.TransformPoint(new Vector3(plan.x, 0f, plan.y));
        public Vector2 ToPlan(Vector3 world) { var l = transform.InverseTransformPoint(world); return new Vector2(l.x, l.z); }
        public Vector3 Center => transform.position;

        /// <summary>Which squares are pad and which are footing (and which patch / stretch each colours).</summary>
        void Survey()
        {
            padCells.Clear(); trenchCells.Clear(); padTileOf.Clear(); trenchPieceOf.Clear();
            ClearConcreteCells();
            MakeTilesAndPieces();
            var b = Plan.Bounds;
            float w = Plan.footingWidth * 0.5f;
            // a footing is checked across most of its width (its very edges crumble); the pad stops short of the trench's lip
            float checkW = Mathf.Max(0.13f, w - 0.08f), padKeep = w + 0.15f;
            Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
            foreach (var c in new[] { new Vector2(b.xMin, b.yMin), new Vector2(b.xMax, b.yMin), new Vector2(b.xMin, b.yMax), new Vector2(b.xMax, b.yMax) })
            { var p = ToWorld(c); lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
            float cell = Site.Cell;
            Site.CellOf(lo - new Vector3(cell, 0f, cell), out int x0, out int z0);
            Site.CellOf(hi + new Vector3(cell, 0f, cell), out int x1, out int z1);
            int last = Site.Res - 2;
            x0 = Mathf.Clamp(x0, 1, last); x1 = Mathf.Clamp(x1, 1, last); z0 = Mathf.Clamp(z0, 1, last); z1 = Mathf.Clamp(z1, 1, last);
            for (int cz = z0; cz <= z1; cz++)
                for (int cx = x0; cx <= x1; cx++)
                {
                    Vector3 cp = Site.CellPos(cx, cz);
                    int idx = Site.CellIndex(cx, cz);
                    Vector2 pl = ToPlan(cp);
                    float d = Plan.FootingDistance(pl, out _);
                    SurveyConcreteCell(idx, pl, d);
                    if (d <= checkW) { trenchCells.Add(idx); trenchPieceOf.Add(NearestPiece(pl)); }
                    else if (d > padKeep && Plan.Inside(pl)) { padCells.Add(idx); padTileOf.Add(NearestTile(pl)); }
                }
            InitConcrete();
        }

        void MakeTilesAndPieces()
        {
            tiles.Clear(); pieces.Clear();
            var b = Plan.Bounds;
            for (float z = Mathf.Floor(b.yMin) + 0.5f; z < b.yMax; z += 1f)
                for (float x = Mathf.Floor(b.xMin) + 0.5f; x < b.xMax; x += 1f)
                    if (Plan.Inside(new Vector2(x, z))) tiles.Add(new Vector2(x, z));
            foreach (var (a, e) in Plan.Footings())
            {
                float len = Vector2.Distance(a, e);
                int n = Mathf.Max(1, Mathf.RoundToInt(len));
                for (int k = 0; k < n; k++) pieces.Add((Vector2.Lerp(a, e, k / (float)n), Vector2.Lerp(a, e, (k + 1f) / n)));
            }
            tileOk = new int[tiles.Count]; tileHigh = new int[tiles.Count]; tileLow = new int[tiles.Count]; tileN = new int[tiles.Count]; tileTop = new float[tiles.Count];
            pieceOk = new int[pieces.Count]; pieceN = new int[pieces.Count];
        }

        int NearestTile(Vector2 p)
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < tiles.Count; i++) { float d = (tiles[i] - p).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
            return best;
        }

        int NearestPiece(Vector2 p)
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < pieces.Count; i++)
            {
                var (a, b) = pieces[i]; Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                float d = (p - (a + ab * t)).sqrMagnitude;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        // ------------------------------------------------------------------ checking the digging

        void Update()
        {
            if (Preview || Plan == null || Site == null) return;
            ConcreteUpdate(Time.deltaTime);
            HouseUpdate(Time.deltaTime);
            if ((checkTimer -= Time.deltaTime) > 0f) return;
            checkTimer = 0.5f;
            Check();
        }

        /// <summary>Look at every square under the plan, colour the patches and stretches, and move the stage on.</summary>
        public void Check()
        {
            if (Preview || Plan == null || Site == null) return;
            // the concrete's going in: the digging's done with (the trenches are filling up)
            if (State >= SiteState.PouringConcrete) { ConcreteCheck(); return; }
            System.Array.Clear(tileOk, 0, tileOk.Length); System.Array.Clear(tileHigh, 0, tileHigh.Length);
            System.Array.Clear(tileLow, 0, tileLow.Length); System.Array.Clear(tileN, 0, tileN.Length);
            System.Array.Clear(pieceOk, 0, pieceOk.Length); System.Array.Clear(pieceN, 0, pieceN.Length);
            for (int i = 0; i < tileTop.Length; i++) tileTop[i] = float.MinValue;
            float tol = Plan.padTolerance;
            int ok = 0, high = 0, low = 0;
            Vector2 offSum = Vector2.zero;
            for (int i = 0; i < padCells.Count; i++)
            {
                Site.CellXZ(padCells[i], out int x, out int z);
                float s = Site.CellSurface(x, z);
                int t = padTileOf[i];
                tileN[t]++; tileTop[t] = Mathf.Max(tileTop[t], s);
                if (s > PadY + tol) { high++; tileHigh[t]++; }
                else if (s < PadY - tol) { low++; tileLow[t]++; }
                else { ok++; tileOk[t]++; continue; }
                var cp = Site.CellPos(x, z); offSum += new Vector2(cp.x - Center.x, cp.z - Center.z);
            }
            PadDone = padCells.Count > 0 ? ok / (float)padCells.Count : 1f;
            PadHigh = high; PadLow = low;
            PadWorstSide = high + low > 0 ? Compass(offSum) : "";
            float floor = PadY - Plan.footingDepth + Plan.trenchTolerance;
            int tok = 0;
            for (int i = 0; i < trenchCells.Count; i++)
            {
                Site.CellXZ(trenchCells[i], out int x, out int z);
                int p = trenchPieceOf[i];
                pieceN[p]++;
                if (Site.CellSurface(x, z) <= floor) { tok++; pieceOk[p]++; }
            }
            TrenchDone = trenchCells.Count > 0 ? tok / (float)trenchCells.Count : 1f;

            // the stages (digging can go backwards until the concrete goes in: dirt slumps back into a trench)
            if (State == SiteState.BlueprintPlaced && (PadDone > 0.02f + startPad || TrenchDone > 0.02f)) State = SiteState.Excavating;
            if ((State == SiteState.BlueprintPlaced || State == SiteState.Excavating) && DigDone) { State = SiteState.ReadyForConcrete; GameUI.Toast($"{Plan.title}: the pad is level and the footings are dug. Ready for concrete!"); }
            else if (State == SiteState.ReadyForConcrete && !DigDone) State = SiteState.Excavating;

            Status = State switch
            {
                SiteState.ReadyForConcrete => $"Ready for concrete: about {Plan.ConcreteVolume:0.#} m³ ({(Plan.slab ? "footings and slab" : "footings")}).",
                _ => $"Pad {PadDone * 100f:0}% level (needs {Plan.needPad * 100f:0}%), footings {TrenchDone * 100f:0}% dug to {Plan.footingDepth:0.0#} m (needs {Plan.needTrench * 100f:0}%)." +
                     (high + low > 0 ? $"  {PadHint()}" : ""),
            };
            Redraw();
        }
        float startPad = -1f;

        /// <summary>Where the pad still needs work, in words.</summary>
        public string PadHint()
        {
            string side = string.IsNullOrEmpty(PadWorstSide) ? "" : $", mostly on the {PadWorstSide} side";
            if (PadHigh > 0 && PadLow > 0) return $"{PadHigh * Site.Cell * Site.Cell:0.#} m² of pad still too high (orange) and {PadLow * Site.Cell * Site.Cell:0.#} m² too low{side}.";
            if (PadHigh > 0) return $"{PadHigh * Site.Cell * Site.Cell:0.#} m² of pad still too high (orange){side}: cut it down.";
            return $"{PadLow * Site.Cell * Site.Cell:0.#} m² of pad too low{side}: fill it and level it off.";
        }

        static string Compass(Vector2 v)
        {
            if (v.sqrMagnitude < 1e-4f) return "";
            float a = Mathf.Atan2(v.x, v.y) * Mathf.Rad2Deg; // 0 = north (+z), 90 = east (+x)
            string[] n = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
            return n[Mathf.RoundToInt(Mathf.Repeat(a, 360f) / 45f) % 8];
        }

        /// <summary>Where it starts (for telling "begun" from "the ground was already level there").</summary>
        public void MarkStart() { startPad = 2f; Check(); startPad = PadDone; }

        // ------------------------------------------------------------------ drawing

        enum Layer { Blue, Green, Orange, Line, Bad }
        static Material[] mats;

        void BuildLayers()
        {
            mats ??= new[]
            {
                SeeThrough(new Color(0.35f, 0.75f, 1f, 0.32f)),  // to do
                SeeThrough(new Color(0.3f, 1f, 0.45f, 0.38f)),   // done
                SeeThrough(new Color(1f, 0.55f, 0.15f, 0.45f)),  // too high
                SeeThrough(new Color(0.55f, 0.88f, 1f, 0.85f)),  // the outline
                SeeThrough(new Color(1f, 0.25f, 0.2f, 0.45f)),   // can't go here
            };
            layers = new MeshFilter[mats.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                var go = new GameObject(((Layer)i).ToString());
                go.transform.SetParent(transform, false);
                layers[i] = go.AddComponent<MeshFilter>();
                layers[i].sharedMesh = new Mesh { name = "Site " + (Layer)i };
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mats[i];
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
        }

        readonly List<Vector3>[] verts = { new(), new(), new(), new(), new() };
        readonly List<int>[] tris = { new(), new(), new(), new(), new() };

        void Redraw()
        {
            if (layers == null || Plan == null) return;
            for (int i = 0; i < verts.Length; i++) { verts[i].Clear(); tris[i].Clear(); }
            float w = Plan.footingWidth, depth = Plan.footingDepth;
            const float above = 0.3f; // the trench boxes stand up out of the grass so you see them
            bool bad = Preview && !PreviewValid;
            bool poured = State >= SiteState.PouringConcrete; // (the concrete shows where it's gone: just the outline)
            // the footings
            if (!Preview && !poured)
            for (int i = 0; i < pieces.Count; i++)
            {
                var (a, b) = pieces[i];
                bool done = pieceN[i] > 0 && pieceOk[i] >= pieceN[i] * 0.85f;
                float top = Proud((a + b) * 0.5f) + above;
                AddBox(done ? Layer.Green : Layer.Blue, (a + b) * 0.5f, b - a, (top - depth) * 0.5f, Vector2.Distance(a, b) + 0.02f, w, depth + top);
            }
            if (Preview)
                foreach (var (a, b) in Plan.Footings())
                {
                    float top = Proud((a + b) * 0.5f) + above;
                    AddBox(bad ? Layer.Bad : Layer.Blue, (a + b) * 0.5f, b - a, (top - depth) * 0.5f, Vector2.Distance(a, b) + w, w, depth + top);
                }
            // the pad, patch by patch: level (green, at the pad), still to fill (blue, at the pad), standing proud (orange, on top of the dirt)
            if (!Preview && !poured)
            for (int i = 0; i < tiles.Count; i++)
            {
                var t = tiles[i];
                Layer l; float y = 0f;
                if (tileN[i] == 0) continue;
                if (tileOk[i] >= tileN[i] * 0.85f) l = Layer.Green;
                else if (tileHigh[i] >= tileLow[i]) { l = Layer.Orange; y = Mathf.Max(0f, tileTop[i] - PadY); }
                else l = Layer.Blue;
                AddBox(l, t, Vector2.right, y + 0.03f, 0.96f, 0.96f, 0.05f);
            }
            if (Preview)
            {
                // the whole pad in one colour while it's being moved about
                foreach (var t in PreviewTiles()) AddBox(bad ? Layer.Bad : Layer.Blue, t, Vector2.right, 0.03f, 0.96f, 0.96f, 0.05f);
            }
            // the outline round the pad, and corner posts so you can find it in tall grass (gone once the house goes up)
            var o = Plan.outline;
            for (int i = 0; i < o.Length && !HouseStarted; i++)
            {
                Vector2 a = o[i], b = o[(i + 1) % o.Length];
                AddBox(bad ? Layer.Bad : Layer.Line, (a + b) * 0.5f, b - a, 0.12f, Vector2.Distance(a, b) + 0.08f, 0.08f, 0.08f);
                float post = Proud(a) + 1.2f; // (stands up out of the ground even where the pad is below it)
                AddBox(bad ? Layer.Bad : Layer.Line, a, Vector2.right, post * 0.5f, 0.1f, 0.1f, post);
            }
            for (int i = 0; i < layers.Length; i++)
            {
                var m = layers[i].sharedMesh;
                m.Clear();
                if (verts[i].Count == 0) continue;
                m.indexFormat = verts[i].Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
                m.SetVertices(verts[i]); m.SetTriangles(tris[i], 0);
                m.RecalculateNormals(); m.RecalculateBounds();
            }
        }

        /// <summary>How far the ground stands above the pad at a point on the plan (0 where it's at or below it).</summary>
        float Proud(Vector2 plan)
        {
            if (Site == null) return 0f;
            var w = ToWorld(plan);
            return Site.Contains(w) ? Mathf.Max(0f, Site.SurfaceY(w) - PadY) : 0f;
        }

        List<Vector2> previewTiles; HousePlan previewTilesFor;
        List<Vector2> PreviewTiles()
        {
            if (previewTilesFor == Plan && previewTiles != null) return previewTiles;
            previewTilesFor = Plan; previewTiles = new List<Vector2>();
            var b = Plan.Bounds;
            for (float z = Mathf.Floor(b.yMin) + 0.5f; z < b.yMax; z += 1f)
                for (float x = Mathf.Floor(b.xMin) + 0.5f; x < b.xMax; x += 1f)
                    if (Plan.Inside(new Vector2(x, z))) previewTiles.Add(new Vector2(x, z));
            return previewTiles;
        }

        /// <summary>A box on the plan (local to the site: y up from the pad), centred at `c`, its length along `along`.</summary>
        void AddBox(Layer layer, Vector2 c, Vector2 along, float cy, float length, float width, float height)
        {
            var v = verts[(int)layer]; var t = tris[(int)layer];
            Vector2 f = along.sqrMagnitude > 1e-8f ? along.normalized : Vector2.up;
            Vector3 fw = new Vector3(f.x, 0f, f.y) * (length * 0.5f), rt = new Vector3(f.y, 0f, -f.x) * (width * 0.5f), up = Vector3.up * (height * 0.5f);
            Vector3 cc = new Vector3(c.x, cy, c.y);
            // six faces, four corners each (flat faces)
            void Face(Vector3 n, Vector3 u, Vector3 w2)
            {
                int o = v.Count;
                Vector3 m = cc + n;
                v.Add(m - u - w2); v.Add(m + u - w2); v.Add(m + u + w2); v.Add(m - u + w2);
                t.Add(o); t.Add(o + 1); t.Add(o + 2); t.Add(o); t.Add(o + 2); t.Add(o + 3);
            }
            Face(up, rt, fw); Face(-up, fw, rt);
            Face(rt, fw, up); Face(-rt, up, fw);
            Face(fw, up, rt); Face(-fw, rt, up);
        }

        /// <summary>A see-through material made here (the same as the town's job markers).</summary>
        static Material SeeThrough(Color c)
        {
            var m = new Material(Shader.Find("HDRP/Lit"));
            m.SetColor("_BaseColor", c);
            m.SetFloat("_SurfaceType", 1f); m.SetFloat("_BlendMode", 0f); m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_DoubleSidedEnable", 1f);
            try { UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(m); } catch { }
            return m;
        }

        // ------------------------------------------------------------------ saving

        public void Write(System.IO.BinaryWriter w)
        {
            w.Write(Id); w.Write(Plan.title); w.Write((byte)State);
            w.Write(Center.x); w.Write(Center.z); w.Write(Yaw); w.Write(PadY); w.Write(startPad);
            WriteConcrete(w);
            WriteHouse(w);
        }

        public void RestoreState(SiteState s, float start) { State = s; startPad = start; Check(); }

        /// <summary>A friend's copy is further on: take their stage (stages only go forward).</summary>
        public void MergeState(SiteState s)
        {
            if (s <= State) return;
            // (the house itself finishes the site: a friend's Completed comes with their house's progress)
            State = s > SiteState.ReadyForHouse ? SiteState.ReadyForHouse : s;
            Check(); Redraw();
        }

        /// <summary>Skip a site's saved concrete and house (its plan or ground isn't in this game).</summary>
        public static void SkipData(System.IO.BinaryReader r, int version)
        {
            if (version >= 2) { r.ReadSingle(); int k = r.ReadInt32(); for (int j = 0; j < k; j++) { r.ReadInt32(); r.ReadSingle(); r.ReadSingle(); } }
            if (version >= 3) { r.ReadSingle(); r.ReadBoolean(); r.ReadString(); r.ReadString(); }
        }
    }
}
