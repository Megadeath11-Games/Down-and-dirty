using System.Collections.Generic;
using FPSBase.Mining;
using FPSBase.Vehicles;
using UnityEngine;

namespace FPSBase.Core
{
    /// <summary>
    /// The map picture: a top-down view of the whole world baked from downward rays (terrain shaded by height
    /// and slope with contour lines, the dig grounds coloured by how deep they're dug, buildings, roads and
    /// trees in their own colours), kept fresh a few thousand rays a frame. Draws it for the big map (M) and the
    /// minimap, with machines as little top-down icons and players as arrows pointing where they look.
    /// </summary>
    public class MapView : MonoBehaviour
    {
        public static MapView Instance { get; private set; }

        public const int Res = 1024;
        public Rect World { get; private set; }
        public bool Ready => tex != null;
        /// <summary>The first full picture is done.</summary>
        public bool Baked { get; private set; }

        Texture2D tex;
        Color32[] px;
        float[] hgt;
        int cursor, frames;
        float applyTimer;
        bool dirty;
        readonly RaycastHit[] hits = new RaycastHit[10];
        readonly Dictionary<Collider, Color> colliderColor = new();
        readonly Dictionary<Collider, bool> skip = new();

        void Awake() => Instance = this;

        void Update()
        {
            if (tex == null)
            {
                if (++frames < 3 || DigSite.Instance == null) return;
                Init();
            }
            int n = Baked ? 2500 : 24000;
            for (int k = 0; k < n; k++)
            {
                Sample(cursor % Res, cursor / Res);
                if (++cursor >= Res * Res) { cursor = 0; Baked = true; }
            }
            dirty = true;
            if ((applyTimer -= Time.unscaledDeltaTime) <= 0f && dirty)
            {
                applyTimer = Baked ? 1f : 0.3f;
                tex.SetPixels32(px); tex.Apply(false);
                dirty = false;
            }
        }

        /// <summary>Bake the whole picture now (tests and screenshots).</summary>
        public void BakeAll()
        {
            if (tex == null) Init();
            for (int i = 0; i < Res * Res; i++) Sample(i % Res, i / Res);
            cursor = 0; Baked = true;
            tex.SetPixels32(px); tex.Apply(false);
        }

        void Init()
        {
            Rect w = DigSite.Instance != null ? DigSite.Instance.FieldRect : new Rect(-50, -200, 300, 320);
            void Add(Vector3 p, float pad) => w = Rect.MinMaxRect(Mathf.Min(w.xMin, p.x - pad), Mathf.Min(w.yMin, p.z - pad), Mathf.Max(w.xMax, p.x + pad), Mathf.Max(w.yMax, p.z + pad));
            foreach (var g in DigSite.All) if (g != null) { var f = g.FieldRect; Add(new Vector3(f.xMin, 0, f.yMin), 0); Add(new Vector3(f.xMax, 0, f.yMax), 0); }
            foreach (var m in MapMarker.All) Add(m.transform.position, 40f);
            foreach (var v in VehicleBase.All) Add(v.transform.position, 30f);
            w = Rect.MinMaxRect(w.xMin - 40f, w.yMin - 40f, w.xMax + 40f, w.yMax + 40f);
            float s = Mathf.Max(w.width, w.height);
            World = new Rect(w.center.x - s / 2f, w.center.y - s / 2f, s, s);
            tex = new Texture2D(Res, Res, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Map" };
            px = new Color32[Res * Res];
            hgt = new float[Res * Res];
            var bg = (Color32)new Color(0.14f, 0.2f, 0.12f);
            for (int i = 0; i < px.Length; i++) px[i] = bg;
            tex.SetPixels32(px); tex.Apply(false);
        }

        static readonly Vector3 Sun = new Vector3(-0.55f, 1f, 0.45f).normalized;

        void Sample(int x, int y)
        {
            float wx = World.x + (x + 0.5f) / Res * World.width, wz = World.y + (y + 0.5f) / Res * World.height;
            int n = Physics.RaycastNonAlloc(new Vector3(wx, 400f, wz), Vector3.down, hits, 800f, ~0, QueryTriggerInteraction.Ignore);
            int best = -1;
            for (int i = 0; i < n; i++)
            {
                if (Skip(hits[i].collider)) continue;
                if (best < 0 || hits[i].distance < hits[best].distance) best = i;
            }
            int idx = y * Res + x;
            if (best < 0) { px[idx] = new Color32(78, 112, 54, 255); hgt[idx] = 0f; return; }
            var h = hits[best];
            Color c;
            bool ground = false;
            float slope = 1f - h.normal.y;
            if (h.collider is TerrainCollider)
            {
                ground = true;
                c = Color.Lerp(new Color(0.34f, 0.5f, 0.22f), new Color(0.44f, 0.5f, 0.28f), Mathf.Clamp01((h.point.y - 4f) / 30f));
                if (slope > 0.25f) c = Color.Lerp(c, new Color(0.52f, 0.47f, 0.4f), Mathf.Clamp01((slope - 0.25f) / 0.2f));
            }
            else if (DigSite.IsSiteCollider(h.collider))
            {
                ground = true;
                var site = DigSite.At(h.point);
                float d = site != null ? site.OriginalY(h.point) - h.point.y : 0f;
                if (d > 0.06f) c = Color.Lerp(new Color(0.6f, 0.44f, 0.26f), new Color(0.3f, 0.2f, 0.12f), Mathf.Clamp01(d / 7f));
                else if (d < -0.06f) c = Color.Lerp(new Color(0.66f, 0.54f, 0.36f), new Color(0.76f, 0.66f, 0.48f), Mathf.Clamp01(-d / 2f));
                else c = new Color(0.4f, 0.5f, 0.24f);
            }
            else c = ColorOf(h.collider);
            // ground: soften the little bumps so hills read and the grass doesn't speckle
            Vector3 nrm = ground ? Vector3.Lerp(Vector3.up, h.normal, 0.22f).normalized : h.normal;
            float shade = 0.7f + 0.42f * Mathf.Clamp01(Vector3.Dot(nrm, Sun));
            c *= shade;
            hgt[idx] = h.point.y;
            if (ground)
            {
                int band = Mathf.FloorToInt(h.point.y / 3f);
                if ((x > 0 && Mathf.FloorToInt(hgt[idx - 1] / 3f) != band) || (y > 0 && Mathf.FloorToInt(hgt[idx - Res] / 3f) != band)) c *= 0.86f;
            }
            c.a = 1f;
            px[idx] = c;
        }

        bool Skip(Collider col)
        {
            if (col == null) return true;
            if (skip.TryGetValue(col, out bool s)) return s;
            var rb = col.attachedRigidbody;
            s = (rb != null && !rb.isKinematic) || col.GetComponentInParent<VehicleBase>() != null || col.GetComponentInParent<Trailer>() != null
                || col.GetComponentInParent<PlayerInteractor>() != null || col is CharacterController;
            skip[col] = s;
            return s;
        }

        Color ColorOf(Collider col)
        {
            if (colliderColor.TryGetValue(col, out var c)) return c;
            var r = col.GetComponent<Renderer>() ?? col.GetComponentInParent<Renderer>() ?? col.GetComponentInChildren<Renderer>();
            c = new Color(0.5f, 0.5f, 0.5f);
            var m = r != null ? r.sharedMaterial : null;
            if (m != null) c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.HasProperty("_Color") ? m.color : c;
            c.a = 1f;
            colliderColor[col] = c;
            return c;
        }

        // ------------------------------------------------------------------ drawing

        Rect scr, view;
        float ppm;
        Vector2 M(Vector3 p) => new Vector2(scr.x + (p.x - view.x) * ppm, scr.yMax - (p.z - view.y) * ppm);
        public Vector3 ToWorld(Rect screen, Rect v, Vector2 q) => new Vector3(v.x + (q.x - screen.x) / screen.width * v.width, 0f, v.y + (screen.yMax - q.y) / screen.height * v.height);

        static GUIStyle label, small, street;

        /// <summary>Draw the map of world area `v` into `screen`. `big`: the full map with names; otherwise the minimap.</summary>
        public void Draw(Rect screen, Rect v, bool big, Vector3 me, float myYaw)
        {
            scr = screen; view = v; ppm = screen.width / v.width;
            placed.Clear();
            label ??= new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, normal = { textColor = Color.white }, wordWrap = false, clipping = TextClipping.Overflow };
            small ??= new GUIStyle(label) { fontSize = 11, fontStyle = FontStyle.Normal, alignment = TextAnchor.MiddleCenter };
            street ??= new GUIStyle(label) { fontSize = 11, fontStyle = FontStyle.Italic, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.95f, 0.92f, 0.8f) } };

            GUI.color = new Color(0.1f, 0.14f, 0.09f);
            GUI.DrawTexture(screen, Texture2D.whiteTexture);
            if (tex != null)
            {
                // the part of the view the picture covers
                var o = Rect.MinMaxRect(Mathf.Max(v.xMin, World.xMin), Mathf.Max(v.yMin, World.yMin), Mathf.Min(v.xMax, World.xMax), Mathf.Min(v.yMax, World.yMax));
                if (o.width > 0f && o.height > 0f)
                {
                    Vector2 a = M(new Vector3(o.xMin, 0, o.yMax)), b = M(new Vector3(o.xMax, 0, o.yMin));
                    var uv = new Rect((o.x - World.x) / World.width, (o.y - World.y) / World.height, o.width / World.width, o.height / World.height);
                    GUI.color = Color.white;
                    GUI.DrawTextureWithTexCoords(Rect.MinMaxRect(a.x, a.y, b.x, b.y), tex, uv);
                }
            }

            // your claim's edge
            var site = DigSite.Instance;
            if (site != null && site.claimRect.width > 0f)
            {
                var c = site.claimRect; float e = Mathf.Max(1.5f / ppm, 0.6f);
                var gold = new Color(1f, 0.8f, 0.3f, 0.9f);
                FillW(new Rect(c.xMin, c.yMin, c.width, e), gold); FillW(new Rect(c.xMin, c.yMax - e, c.width, e), gold);
                FillW(new Rect(c.xMin, c.yMin, e, c.height), gold); FillW(new Rect(c.xMax - e, c.yMin, e, c.height), gold);
                if (big && ppm > 0.6f) Text(new Vector3(c.center.x, 0, c.yMax), "YOUR CLAIM", small, new Color(1f, 0.85f, 0.4f), 0f, -9f, true);
            }

            // the town: street names, house numbers, customers waiting
            var jobs = FPSBase.City.Jobs.Instance;
            if (big && ppm > 0.9f)
                foreach (var st in FPSBase.City.Street.All)
                {
                    var sr = st.Rect;
                    bool tall = sr.height > sr.width;
                    var q = M(new Vector3(sr.center.x, 0, sr.center.y));
                    if (!scr.Contains(q)) continue;
                    var keep = GUI.matrix;
                    if (tall) GUIUtility.RotateAroundPivot(-90f, q);
                    Shadowed(new Rect(q.x - 80, q.y - 9, 160, 18), st.streetName, street);
                    GUI.matrix = keep;
                }
            foreach (var hs in FPSBase.City.House.All)
            {
                var hj = jobs != null ? jobs.OpenJobAt(hs) : null;
                if (hj != null)
                {
                    var col = hj.state == FPSBase.City.JobState.Accepted ? new Color(1f, 0.5f, 0.1f) : new Color(1f, 0.88f, 0.3f);
                    Icon(Pin, hs.Front, big ? 18f : 14f, 0f, col);
                    if (big && ppm > 1.6f) Text(hs.Front, hj.Title, small, col, 0f, -16f);
                }
                else if (big && ppm > 2.6f) Text(hs.transform.position, hs.number.ToString(), small, new Color(1f, 1f, 1f, 0.8f), 0f, 0f);
            }

            // places
            foreach (var m in MapMarker.All)
            {
                if (m.label.StartsWith("Job")) continue; // jobs have their own pins
                Icon(Pin, m.transform.position, big ? 16f : 11f, 0f, m.color);
                if (!big) continue;
                var q = M(m.transform.position);
                var size = label.CalcSize(new GUIContent(m.label));
                var lr = new Rect(q.x + 10, q.y - 9, size.x, 18);
                bool Clash(Rect x) { foreach (var p in placed) if (p.Overlaps(x)) return true; return x.xMax > scr.xMax || x.yMax > scr.yMax; }
                if (Clash(lr)) { lr.y += 16; if (Clash(lr)) { lr.y -= 32; if (Clash(lr)) continue; } }
                if (!scr.Contains(q)) continue;
                placed.Add(lr);
                GUI.color = m.color; Shadowed(lr, m.label, label);
            }

            // machines and trailers
            foreach (var t in Trailer.All)
                if (t != null && t.isActiveAndEnabled) Icon(IconFor(t.displayName), t.transform.position, Size(t, big), t.transform.eulerAngles.y, Color.white);
            foreach (var veh in VehicleBase.All)
            {
                if (veh == null) continue;
                bool lastOne = ReferenceEquals(veh, VehicleBase.LastDriven);
                Icon(IconFor(veh), veh.transform.position, Size(veh, big), veh.transform.eulerAngles.y, Color.white);
                if (big && (lastOne || ppm > 2.2f) && !veh.Occupied) Text(veh.transform.position, veh.displayName, small, new Color(0.7f, 0.9f, 1f), 0f, Size(veh, big) * 0.5f + 8f, true);
            }

            // players: friends in blue with their names, you in red
            var net = FPSBase.Net.NetSession.Instance;
            if (net != null && FPSBase.Net.NetSession.InSession)
                foreach (var (name, pos, yaw) in net.OtherPlayers())
                {
                    Icon(Arrow, pos, big ? 20f : 16f, yaw, new Color(0.35f, 0.7f, 1f));
                    Text(pos, name, small, new Color(0.6f, 0.85f, 1f), 0f, big ? 16f : 13f);
                }
            Icon(Arrow, me, big ? 24f : 20f, myYaw, new Color(1f, 0.3f, 0.22f));
            GUI.color = Color.white;
        }

        readonly List<Rect> placed = new();

        void FillW(Rect wr, Color c)
        {
            Vector2 a = M(new Vector3(wr.xMin, 0, wr.yMax)), b = M(new Vector3(wr.xMax, 0, wr.yMin));
            var r = Rect.MinMaxRect(Mathf.Max(a.x, scr.xMin), Mathf.Max(a.y, scr.yMin), Mathf.Min(b.x, scr.xMax), Mathf.Min(b.y, scr.yMax));
            if (r.width <= 0f || r.height <= 0f) return;
            GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture);
        }

        void Text(Vector3 at, string s, GUIStyle st, Color c, float dx, float dy, bool avoid = false)
        {
            var q = M(at) + new Vector2(dx, dy);
            if (!scr.Contains(q)) return;
            var size = st.CalcSize(new GUIContent(s));
            var lr = new Rect(q.x - size.x / 2f, q.y - size.y / 2f, size.x, size.y);
            if (avoid)
            {
                bool Hit(Rect x) { foreach (var p in placed) if (p.Overlaps(x)) return true; return false; }
                if (Hit(lr)) { lr.y += size.y; if (Hit(lr)) return; }
                placed.Add(lr);
            }
            GUI.color = c;
            Shadowed(lr, s, st);
        }

        static void Shadowed(Rect r, string s, GUIStyle st)
        {
            var keep = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.75f * keep.a);
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), s, st);
            GUI.color = keep;
            GUI.Label(r, s, st);
        }

        /// <summary>An icon centred on a world point, turned to `yaw` (kept inside the map's edge).</summary>
        void Icon(Texture2D t, Vector3 at, float size, float yaw, Color tint)
        {
            var q = M(at);
            float m = size * 0.5f;
            if (q.x < scr.xMin + m || q.x > scr.xMax - m || q.y < scr.yMin + m || q.y > scr.yMax - m) return;
            var keep = GUI.matrix;
            if (Mathf.Abs(yaw) > 0.01f) GUIUtility.RotateAroundPivot(yaw, q);
            GUI.color = tint;
            GUI.DrawTexture(new Rect(q.x - m, q.y - m, size, size), t);
            GUI.matrix = keep;
        }

        readonly Dictionary<Object, float> sizes = new();

        /// <summary>How big a machine is drawn: its real size at this zoom, never too small to see.</summary>
        float Size(Component c, bool big)
        {
            if (!sizes.TryGetValue(c, out float len))
            {
                var b = new Bounds(); bool any = false;
                foreach (var r in c.GetComponentsInChildren<MeshRenderer>())
                {
                    var lb = r.localBounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var corner = lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        var l = c.transform.InverseTransformPoint(r.transform.TransformPoint(corner));
                        if (!any) { b = new Bounds(l, Vector3.zero); any = true; } else b.Encapsulate(l);
                    }
                }
                len = any ? Mathf.Clamp(Mathf.Max(b.size.x, b.size.z), 2f, 22f) : 6f;
                sizes[c] = len;
            }
            return Mathf.Clamp(len * ppm * 1.15f, big ? 20f : 16f, big ? 90f : 40f);
        }

        // ------------------------------------------------------------------ icons (top-down, front at the top)

        static readonly Dictionary<string, Texture2D> icons = new();
        static Texture2D arrow, pin;
        public static Texture2D Arrow => arrow ??= Paint(p => p.Poly(Color.white, new Vector2(32, 61), new Vector2(55, 5), new Vector2(32, 19), new Vector2(9, 5)));
        public static Texture2D Pin => pin ??= Paint(p => { p.Circle(32, 32, 20, Color.white); p.Circle(32, 32, 8, new Color(0.15f, 0.15f, 0.15f)); });

        public static string KindOf(VehicleBase v)
        {
            string n = v.displayName ?? "";
            if (n.Contains("Skid") || n.Contains("Track Loader")) return "skid";
            if (v is MiniExcavator) return "exc";
            if (v is Bulldozer) return "dozer";
            if (v is MotorGrader) return "grader";
            if (v is SemiTruck) return "semi";
            if (v is FrontLoader) return "loader";
            if (v is DumpPickup) return n.Contains("Pickup") ? "pickup" : "dump";
            return "generic";
        }

        public static Texture2D IconFor(VehicleBase v) => IconFor(KindOf(v), true);
        public static Texture2D IconFor(string trailerName) => IconFor(trailerName != null && (trailerName.Contains("fuel") || trailerName.Contains("bowser")) ? "bowser" : "trailer", true);

        static Texture2D IconFor(string kind, bool _)
        {
            if (icons.TryGetValue(kind, out var t) && t != null) return t;
            Color Y = new(0.97f, 0.7f, 0.12f), DY = new(0.72f, 0.5f, 0.08f), TR = new(0.2f, 0.2f, 0.22f), GL = new(0.3f, 0.42f, 0.55f),
                  ST = new(0.45f, 0.45f, 0.47f), TI = new(0.1f, 0.1f, 0.1f), CB = new(0.16f, 0.16f, 0.18f);
            t = kind switch
            {
                "exc" => Paint(p =>
                {
                    p.R(7, 5, 19, 48, TR); p.R(45, 5, 57, 48, TR);
                    for (int y = 7; y < 47; y += 5) { p.R(7, y, 19, y + 1, CB); p.R(45, y, 57, y + 1, CB); }
                    p.R(14, 8, 50, 15, DY); p.R(14, 15, 50, 40, Y); p.R(15, 27, 27, 39, GL);
                    p.R(29, 36, 37, 55, Y); p.R(24, 53, 42, 61, ST);
                }),
                "dump" => Paint(p =>
                {
                    foreach (int y in new[] { 7, 19, 44 }) { p.R(8, y, 15, y + 9, TI); p.R(49, y, 56, y + 9, TI); }
                    p.R(13, 4, 51, 42, new Color(0.62f, 0.55f, 0.44f)); p.R(17, 8, 47, 38, new Color(0.45f, 0.39f, 0.31f));
                    p.R(15, 44, 49, 61, Y); p.R(18, 53, 46, 58, GL);
                }),
                "pickup" => Paint(p =>
                {
                    foreach (int y in new[] { 9, 43 }) { p.R(12, y, 18, y + 9, TI); p.R(46, y, 52, y + 9, TI); }
                    Color B = new(0.86f, 0.86f, 0.84f);
                    p.R(17, 3, 47, 61, B); p.R(20, 6, 44, 27, new Color(0.4f, 0.4f, 0.42f));
                    p.R(20, 31, 44, 43, new Color(0.72f, 0.72f, 0.7f)); p.R(20, 43, 44, 48, GL);
                }),
                "loader" => Paint(p =>
                {
                    foreach (int y in new[] { 7, 34 }) { p.R(7, y, 16, y + 13, TI); p.R(48, y, 57, y + 13, TI); }
                    p.R(17, 3, 47, 30, Y); p.R(21, 15, 43, 29, CB); p.R(19, 30, 45, 48, Y);
                    p.R(18, 46, 24, 56, DY); p.R(40, 46, 46, 56, DY); p.R(5, 53, 59, 62, ST);
                }),
                "skid" => Paint(p =>
                {
                    p.R(11, 10, 19, 46, TR); p.R(45, 10, 53, 46, TR);
                    p.R(18, 8, 46, 46, Y); p.R(22, 18, 42, 40, CB); p.R(9, 47, 55, 58, CB);
                }),
                "dozer" => Paint(p =>
                {
                    p.R(9, 5, 20, 45, TR); p.R(44, 5, 55, 45, TR);
                    for (int y = 7; y < 44; y += 5) { p.R(9, y, 20, y + 1, CB); p.R(44, y, 55, y + 1, CB); }
                    p.R(18, 7, 46, 43, Y); p.R(22, 9, 42, 25, CB); p.R(16, 43, 21, 50, DY); p.R(43, 43, 48, 50, DY); p.R(5, 48, 59, 57, DY);
                }),
                "grader" => Paint(p =>
                {
                    p.R(13, 53, 20, 61, TI); p.R(44, 53, 51, 61, TI);
                    foreach (int y in new[] { 5, 17 }) { p.R(11, y, 19, y + 10, TI); p.R(45, y, 53, y + 10, TI); }
                    p.R(19, 4, 45, 31, Y); p.R(28, 30, 36, 60, Y); p.R(22, 24, 42, 37, CB);
                    p.Poly(ST, new Vector2(10, 40), new Vector2(12, 35), new Vector2(54, 43), new Vector2(52, 48));
                }),
                "semi" => Paint(p =>
                {
                    foreach (int y in new[] { 5, 16, 43 }) { p.R(11, y, 18, y + 9, TI); p.R(46, y, 53, y + 9, TI); }
                    p.R(23, 3, 41, 40, ST); p.Circle(32, 18, 6, CB); p.R(15, 38, 49, 61, new Color(0.82f, 0.16f, 0.12f)); p.R(18, 53, 46, 58, GL);
                }),
                "trailer" => Paint(p =>
                {
                    foreach (int y in new[] { 5, 15 }) { p.R(9, y, 16, y + 8, TI); p.R(48, y, 55, y + 8, TI); }
                    Color D = new(0.38f, 0.33f, 0.27f);
                    p.R(15, 3, 49, 46, D); for (int y = 8; y < 46; y += 6) p.R(15, y, 49, y + 1, new Color(0.27f, 0.23f, 0.19f));
                    p.R(21, 46, 43, 61, new Color(0.3f, 0.27f, 0.23f));
                }),
                "bowser" => Paint(p =>
                {
                    p.R(12, 12, 18, 22, TI); p.R(46, 12, 52, 22, TI);
                    p.R(18, 6, 46, 50, new Color(0.85f, 0.22f, 0.15f)); p.R(22, 10, 42, 46, new Color(0.95f, 0.35f, 0.25f)); p.R(30, 50, 34, 61, ST);
                }),
                _ => Paint(p => { p.R(16, 6, 48, 58, ST); p.R(20, 44, 44, 54, GL); }),
            };
            icons[kind] = t;
            return t;
        }

        class Painter
        {
            public readonly Color[] c = new Color[64 * 64];
            public void R(int x0, int y0, int x1, int y1, Color col)
            {
                for (int y = Mathf.Max(0, y0); y < Mathf.Min(64, y1); y++)
                    for (int x = Mathf.Max(0, x0); x < Mathf.Min(64, x1); x++) c[y * 64 + x] = col;
            }
            public void Circle(float cx, float cy, float r, Color col)
            {
                for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                    if ((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy) <= r * r) c[y * 64 + x] = col;
            }
            public void Poly(Color col, params Vector2[] v)
            {
                for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f; bool inside = false;
                    for (int i = 0, j = v.Length - 1; i < v.Length; j = i++)
                        if ((v[i].y > py) != (v[j].y > py) && px < (v[j].x - v[i].x) * (py - v[i].y) / (v[j].y - v[i].y) + v[i].x) inside = !inside;
                    if (inside) c[y * 64 + x] = col;
                }
            }
        }

        /// <summary>Paint a 64×64 icon and give it a dark outline so it reads on any ground.</summary>
        static Texture2D Paint(System.Action<Painter> draw)
        {
            var p = new Painter();
            draw(p);
            var outline = new Color(0.06f, 0.05f, 0.04f, 1f);
            for (int pass = 0; pass < 3; pass++)
            {
                var src = (Color[])p.c.Clone();
                for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                {
                    if (src[y * 64 + x].a > 0f) continue;
                    bool edge = false;
                    for (int dy = -1; dy <= 1 && !edge; dy++) for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (xx >= 0 && yy >= 0 && xx < 64 && yy < 64 && src[yy * 64 + xx].a > 0f) { edge = true; break; }
                    }
                    if (edge) p.c[y * 64 + x] = outline;
                }
            }
            var t = new Texture2D(64, 64, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, name = "Map icon" };
            t.SetPixels(p.c); t.Apply(true);
            return t;
        }
    }
}
