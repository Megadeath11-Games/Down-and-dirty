using System.Collections.Generic;
using System.IO;
using FPSBase.Core;
using FPSBase.Mining;
using FPSBase.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FPSBase.City
{
    /// <summary>
    /// Building houses: pick a plan on your phone (Build tab), set its outline down on open ground (turn it, raise or
    /// lower the pad), then level the pad and dig the footings to the blue outline. Keeps your construction sites,
    /// draws their progress over them, and saves them. Lives next to the jobs.
    /// </summary>
    public class Construction : MonoBehaviour
    {
        public static Construction Instance { get; private set; }

        [Tooltip("The most the ground may drop across a footprint for a plan to go there (m).")]
        public float maxSlope = 2.0f;
        [Tooltip("How far the pad can be set above or below the ground's average (m).")]
        public float maxPadShift = 1.5f;

        readonly List<ConstructionSite> sites = new();
        public IReadOnlyList<ConstructionSite> Sites => sites;
        int nextId = 1;

        void Awake() { Instance = this; }

        // ------------------------------------------------------------------ setting a plan down

        HousePlan placing;
        ConstructionSite preview;
        float yaw, padShift;
        Vector3 lastCenter = new(1e9f, 0f, 1e9f); float lastYaw = 999f, groundAvg;
        string why = "";
        bool valid;
        public bool Placing => placing != null;
        public string PlacingWhy => why;

        /// <summary>Start setting a plan out on the ground (on foot: look where it should go).</summary>
        public void BeginPlacing(HousePlan plan)
        {
            var ui = GameUI.Instance;
            if (ui != null && ui.ActiveVehicle != null) { GameUI.Toast("Get out of the machine to set out a house: walk to the spot and look at it"); return; }
            var eco = Economy.Instance;
            if (eco != null && !eco.CanAfford(plan.price)) { GameUI.Toast($"The plans and permit for the {plan.title} cost ${plan.price:N0}"); return; }
            CancelPlacing();
            placing = plan; padShift = 0f; lastCenter = new Vector3(1e9f, 0f, 1e9f);
            if (Camera.main != null) yaw = Mathf.Round(Camera.main.transform.eulerAngles.y / 15f) * 15f;
            var go = new GameObject($"Plan preview: {plan.title}");
            preview = go.AddComponent<ConstructionSite>();
            ui?.ClosePanel();
            GameUI.Toast($"Setting out the {plan.title}: look at open ground. {Controls.N(GameAction.PlanRotateLeft)}/{Controls.N(GameAction.PlanRotateRight)} or the wheel turns it, {Controls.N(GameAction.PlanPlace)} or click sets it down");
        }

        public void CancelPlacing()
        {
            placing = null;
            if (preview != null) Destroy(preview.gameObject);
            preview = null;
        }

        void Update()
        {
            if (placing == null) return;
            var ui = GameUI.Instance;
            if (ui != null && ui.ActiveVehicle != null) { CancelPlacing(); return; }
            if (ui != null && ui.BlocksGameplay) return;
            if (Controls.Pressed(GameAction.PlanCancel) || (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)) { CancelPlacing(); GameUI.Toast("Put the plans away"); return; }
            float wheel = Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;
            if (Controls.Pressed(GameAction.PlanRotateLeft) || wheel > 0.01f) yaw -= 15f;
            if (Controls.Pressed(GameAction.PlanRotateRight) || wheel < -0.01f) yaw += 15f;
            yaw = Mathf.Repeat(yaw, 360f);
            if (Controls.Pressed(GameAction.PlanRaise)) padShift = Mathf.Min(maxPadShift, padShift + 0.1f);
            if (Controls.Pressed(GameAction.PlanLower)) padShift = Mathf.Max(-maxPadShift, padShift - 0.1f);

            var cam = Camera.main;
            if (cam == null) return;
            DigSite site = null; Vector3 at = default;
            var hits = Physics.RaycastAll(cam.transform.position, cam.transform.forward, 120f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            foreach (var h in hits)
            {
                if (h.distance >= best || h.collider.GetComponentInParent<CharacterController>() != null) continue;
                if (ui != null && ui.player != null && h.collider.transform.IsChildOf(ui.player.transform)) continue;
                var s = DigSite.At(h.point);
                if (s == null || !s.Contains(h.point)) continue;
                best = h.distance; site = s; at = h.point;
            }
            if (site == null) { preview.gameObject.SetActive(false); why = "Look at open ground on a dig field (the town's or the claim's)"; valid = false; return; }
            preview.gameObject.SetActive(true);
            if ((at - lastCenter).sqrMagnitude > 0.04f || !Mathf.Approximately(yaw, lastYaw))
            {
                lastCenter = at; lastYaw = yaw;
                valid = Check(placing, site, at, yaw, out why, out groundAvg);
            }
            float padY = Mathf.Round((groundAvg + padShift) / 0.05f) * 0.05f;
            preview.PreviewValid = valid;
            if (preview.Plan != placing) preview.Setup(placing, site, at, yaw, padY, true);
            else preview.MovePreview(at, yaw, padY, site);

            bool place = Controls.Pressed(GameAction.PlanPlace) || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);
            if (!place) return;
            if (!valid) { GameUI.Toast($"Can't build here: {why}"); return; }
            var eco = Economy.Instance;
            if (eco != null && !eco.TrySpend(placing.price)) { GameUI.Toast($"The plans and permit cost ${placing.price:N0}: not enough money"); return; }
            var plan = placing;
            CancelPlacing();
            var made = Place(plan, site, at, yaw, padY);
            GameUI.Toast($"{plan.title} set out. Level the pad to the outline and dig the footings {plan.footingDepth:0.0#} m below it: patches and trenches turn green as they're done");
            Debug.Log($"[Construction] {plan.title} set out at {made.Center} (pad {padY:0.00}, turned {yaw:0}°)");
        }

        /// <summary>Set a plan out for real (paid for already).</summary>
        public ConstructionSite Place(HousePlan plan, DigSite site, Vector3 center, float yaw, float padY)
        {
            var go = new GameObject($"Construction site: {plan.title}");
            var s = go.AddComponent<ConstructionSite>();
            // (a random id: friends setting sites out at once don't get the same one)
            do s.Id = Random.Range(1000, 1 << 30); while (ById(s.Id) != null || removedIds.Contains(s.Id));
            nextId++;
            s.Setup(plan, site, center, yaw, padY, false);
            s.MarkStart();
            sites.Add(s);
            MarkNetDirty();
            return s;
        }

        public void Remove(ConstructionSite s)
        {
            if (s == null) return;
            sites.Remove(s);
            removedIds.Add(s.Id);
            s.ReleaseGround();
            Destroy(s.gameObject);
            MarkNetDirty();
        }

        public ConstructionSite ById(int id) { foreach (var s in sites) if (s != null && s.Id == id) return s; return null; }

        /// <summary>Open the phone on a site (looking at your house).</summary>
        public void OpenSite(ConstructionSite s)
        {
            var j = Jobs.Instance;
            if (j == null) return;
            j.OpenPhone(null);
            j.ShowTab(2);
            int i = s != null ? sites.IndexOf(s) : -1;
            if (i >= 0) { selPlan = -1 - i; confirmCancel = -1; }
        }

        // ------------------------------------------------------------------ multiplayer: every game's sites, merged by id

        readonly HashSet<int> removedIds = new();
        /// <summary>Something changed that friends should hear about now (a site set out or given up, the house ordered or sold).</summary>
        public bool NetDirty { get; set; }
        public void MarkNetDirty() => NetDirty = true;

        /// <summary>A cheap fingerprint of the sites: when it changes (more concrete in, a stage on), friends get the sites again.</summary>
        public int NetSignature()
        {
            unchecked
            {
                int h = sites.Count * 31 + removedIds.Count;
                foreach (var s in sites)
                {
                    if (s == null) continue;
                    h = h * 17 + s.Id; h = h * 17 + (int)s.State; h = h * 17 + s.ConcreteSquares; h = h * 17 + Mathf.RoundToInt(s.ConcretePoured * 10f);
                    h = h * 17 + (s.HouseStarted ? 1 : 0) + (s.Sold ? 2 : 0) + (s.Curing ? 4 : 0);
                }
                return h;
            }
        }

        public void WriteNet(BinaryWriter w)
        {
            w.Write(removedIds.Count);
            foreach (var id in removedIds) w.Write(id);
            Write(w);
        }

        /// <summary>A friend's sites: new ones appear, ones they gave up go, and each one is as far on as either game has it.</summary>
        public void ReadNet(BinaryReader r)
        {
            int nr = r.ReadInt32();
            for (int i = 0; i < nr; i++)
            {
                int id = r.ReadInt32();
                if (removedIds.Add(id)) { var gone = ById(id); if (gone != null) { sites.Remove(gone); gone.ReleaseGround(); Destroy(gone.gameObject); } }
            }
            ReadSites(r, true);
        }

        /// <summary>Can `plan` go here? `groundAvg` = the ground's average height under it (the pad's starting height).</summary>
        public bool Check(HousePlan plan, DigSite site, Vector3 center, float yaw, out string reason, out float groundAvg)
        {
            reason = ""; groundAvg = center.y;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var b = plan.Bounds;
            float lo = float.MaxValue, hi = float.MinValue, sum = 0f; int n = 0;
            for (float z = b.yMin; z <= b.yMax + 0.01f; z += 0.75f)
                for (float x = b.xMin; x <= b.xMax + 0.01f; x += 0.75f)
                {
                    var pl = new Vector2(x, z);
                    if (!plan.Inside(pl) && plan.FootingDistance(pl, out _) > plan.footingWidth * 0.5f + 0.05f) continue;
                    Vector3 p = center + rot * new Vector3(x, 0f, z);
                    if (!site.Contains(p)) { reason = "it runs off the edge of the dig ground"; return false; }
                    if (!site.CellOf(p, out int cx, out int cz) || site.CellHard(cx, cz)) { reason = "it's over a street, a house or a yard that can't be dug"; return false; }
                    float y = site.SurfaceY(p);
                    lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y); sum += y; n++;
                    foreach (var o in sites)
                        if (o != null && (o.Plan.Inside(o.ToPlan(p)) || o.Plan.FootingDistance(o.ToPlan(p), out _) < o.Plan.footingWidth + 1f))
                        { reason = $"it's on top of your {o.Plan.title}"; return false; }
                }
            if (n == 0) { reason = "nothing to set out"; return false; }
            groundAvg = sum / n;
            if (hi - lo > maxSlope) { reason = $"the ground drops {hi - lo:0.0} m across it (at most {maxSlope:0.#} m): find flatter ground"; return false; }
            // buildings, machines, rocks in the way
            Vector3 half = new Vector3(b.width * 0.5f, (hi - lo) * 0.5f + 2f, b.height * 0.5f);
            Vector3 mid = center + rot * new Vector3(b.center.x, 0f, b.center.y); mid.y = (hi + lo) * 0.5f + 2.3f;
            foreach (var c in Physics.OverlapBox(mid, half, rot, ~0, QueryTriggerInteraction.Ignore))
            {
                if (DigSite.IsSiteCollider(c) || c is TerrainCollider || c.GetComponentInParent<CharacterController>() != null) continue;
                var me = GameUI.Instance != null && GameUI.Instance.player != null ? GameUI.Instance.player.transform : null;
                if (me != null && c.transform.IsChildOf(me)) continue;
                if (preview != null && c.transform.IsChildOf(preview.transform)) continue;
                reason = $"something's in the way ({c.transform.root.name.Replace("(Clone)", "").Trim()})"; return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ the phone's Build tab

        int selPlan;
        Vector2 planScroll;
        int confirmCancel = -1;

        public void DrawPhone(Rect area)
        {
            var e = Event.current;
            var plans = HousePlan.All;
            const float listW = 340f, gap = 16f;
            var list = new Rect(area.x, area.y, listW, area.height);
            ShopTheme.Fill(list, ShopTheme.Card);
            float rowH = 58f, y = 0f;
            int rowsN = plans.Count + sites.Count + (sites.Count > 0 ? 2 : 1);
            var view = new Rect(0, 0, listW - 36, Mathf.Max(list.height - 12, rowsN * (rowH + 6) + 40)); // (narrower than the box by the scrollbar: no sideways bar)
            planScroll = GUI.BeginScrollView(new Rect(list.x + 6, list.y + 6, listW - 12, list.height - 12), planScroll, view);
            if (sites.Count > 0)
            {
                GUI.Label(new Rect(8, y, view.width, 24), "YOUR SITES", ShopTheme.DimStyle); y += 26;
                for (int i = 0; i < sites.Count; i++)
                {
                    var s = sites[i];
                    var rr = new Rect(0, y, view.width, rowH);
                    bool sel = selPlan == -1 - i;
                    ShopTheme.Fill(rr, sel ? ShopTheme.CardHover : rr.Contains(e.mousePosition) ? ShopTheme.CardHover : ShopTheme.Deep);
                    GUI.Label(new Rect(rr.x + 12, rr.y + 6, rr.width - 24, 22), s.Plan.title, ShopTheme.CardName);
                    GUI.Label(new Rect(rr.x + 12, rr.y + 28, rr.width - 24, 26), StageText(s), ShopTheme.DimStyle);
                    if (e.type == EventType.MouseDown && e.button == 0 && rr.Contains(e.mousePosition)) { selPlan = -1 - i; confirmCancel = -1; e.Use(); }
                    y += rowH + 6;
                }
                y += 8;
            }
            GUI.Label(new Rect(8, y, view.width, 24), "HOUSE PLANS", ShopTheme.DimStyle); y += 26;
            for (int i = 0; i < plans.Count; i++)
            {
                var p = plans[i];
                var rr = new Rect(0, y, view.width, rowH);
                ShopTheme.Fill(rr, selPlan == i ? ShopTheme.CardHover : rr.Contains(e.mousePosition) ? ShopTheme.CardHover : ShopTheme.Deep);
                GUI.Label(new Rect(rr.x + 12, rr.y + 6, rr.width - 110, 22), p.title, ShopTheme.CardName);
                GUI.Label(new Rect(rr.xMax - 100, rr.y + 6, 90, 22), $"${p.price:N0}", ShopTheme.Price);
                GUI.Label(new Rect(rr.x + 12, rr.y + 28, rr.width - 24, 26), $"{p.shape}  ·  {p.SizeText}  ·  {p.floors} floor{(p.floors > 1 ? "s" : "")}", ShopTheme.DimStyle);
                if (e.type == EventType.MouseDown && e.button == 0 && rr.Contains(e.mousePosition)) { selPlan = i; e.Use(); }
                y += rowH + 6;
            }
            GUI.EndScrollView();

            var card = new Rect(area.x + listW + gap, area.y, area.width - listW - gap, area.height);
            ShopTheme.Fill(card, ShopTheme.Card);
            var inner = new Rect(card.x + 18, card.y + 14, card.width - 36, card.height - 28);
            if (selPlan < 0 && -1 - selPlan < sites.Count) { DrawSite(inner, sites[-1 - selPlan]); return; }
            selPlan = Mathf.Clamp(selPlan, 0, plans.Count - 1);
            var plan = plans[selPlan];
            GUI.Label(new Rect(inner.x, inner.y, inner.width, 32), plan.title, ShopTheme.Heading);
            GUI.Label(new Rect(inner.x, inner.y + 34, inner.width, 44), plan.description, ShopTheme.Body);
            var drawing = new Rect(inner.x, inner.y + 86, inner.width * 0.5f - 10, 260);
            DrawFootprint(drawing, plan);
            var facts = new Rect(drawing.xMax + 20, drawing.y, inner.width * 0.5f - 10, 260);
            string fx =
                $"Footprint: {plan.shape.ToLower()}, {plan.SizeText}, {plan.Area:0} m² of pad\n" +
                $"Floors: {plan.floors}\n" +
                $"Footings: {plan.FootingLength:0} m of trench, {plan.footingWidth * 100f:0} cm wide, {plan.footingDepth:0.0#} m below the pad\n" +
                $"Pad: level to ±{plan.padTolerance * 100f:0.#} cm over {plan.needPad * 100f:0}% of it\n" +
                $"Footings: to within {plan.trenchTolerance * 100f:0} cm of depth over {plan.needTrench * 100f:0}% of them\n" +
                $"Concrete: about {plan.ConcreteVolume:0.#} m³ ({(plan.slab ? "footings and a slab" : "footings, crawl space")})\n\n" +
                "Set it out on open ground on a dig field: the blue outline shows the pad and the footing trenches. Level the pad, dig the trenches, and it's ready for concrete.";
            GUI.Label(facts, fx, ShopTheme.Small);
            var eco = Economy.Instance;
            bool afford = eco == null || eco.CanAfford(plan.price);
            GUI.enabled = afford && placing == null;
            if (GUI.Button(new Rect(inner.x, inner.yMax - 56, inner.width * 0.6f, 52), afford ? $"Set it out  (plans & permit ${plan.price:N0})" : $"Need ${plan.price:N0}", ShopTheme.BuyButton)) BeginPlacing(plan);
            GUI.enabled = true;
            GUI.Label(new Rect(inner.x + inner.width * 0.62f, inner.yMax - 56, inner.width * 0.38f, 52),
                $"{Controls.N(GameAction.PlanRotateLeft)}/{Controls.N(GameAction.PlanRotateRight)} or wheel: turn  ·  {Controls.N(GameAction.PlanRaise)}/{Controls.N(GameAction.PlanLower)}: pad up/down  ·  {Controls.N(GameAction.PlanPlace)} or click: set down  ·  {Controls.N(GameAction.PlanCancel)} or right-click: cancel", ShopTheme.DimStyle);
        }

        void DrawSite(Rect inner, ConstructionSite s)
        {
            GUI.Label(new Rect(inner.x, inner.y, inner.width, 32), s.Plan.title, ShopTheme.Heading);
            GUI.Label(new Rect(inner.x, inner.y + 34, inner.width, 24), StageText(s), ShopTheme.DimStyle);
            var drawing = new Rect(inner.x, inner.y + 66, inner.width * 0.5f - 10, 260);
            DrawFootprint(drawing, s.Plan);
            var right = new Rect(drawing.xMax + 20, drawing.y, inner.width * 0.5f - 10, 260);
            float yy = right.y;
            void Bar(string label, float f, float need)
            {
                GUI.Label(new Rect(right.x, yy, right.width, 22), $"{label}  {f * 100f:0}%  (needs {need * 100f:0}%)", ShopTheme.Body);
                var bar = new Rect(right.x, yy + 24, right.width, 14);
                ShopTheme.Fill(bar, new Color(0f, 0f, 0f, 0.35f));
                ShopTheme.Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(f), bar.height), f >= need ? ShopTheme.Green : ShopTheme.Gold);
                ShopTheme.Fill(new Rect(bar.x + bar.width * need - 1, bar.y - 3, 2, bar.height + 6), ShopTheme.Cream);
                yy += 50;
            }
            if (s.State >= SiteState.PouringConcrete)
            {
                Bar($"Concrete in ({s.ConcretePoured:0.0} of {s.ConcreteNeeded:0.0} m³)", s.ConcreteDone, ConstructionSite.NeedConcrete);
                if (s.Curing || s.State >= SiteState.ReadyForHouse)
                    Bar(s.Curing ? $"Curing ({s.CureText()} left)" : "Cured", s.Curing ? 1f - s.CureLeft / ConstructionSite.CureSeconds : 1f, 1f);
                if (s.HouseStarted) Bar(s.HouseProgress >= 1f ? "House built" : $"House going up ({s.HouseStage})", s.HouseProgress, 1f);
            }
            else
            {
                Bar("Pad level", s.PadDone, s.Plan.needPad);
                Bar("Footings dug", s.TrenchDone, s.Plan.needTrench);
            }
            GUI.Label(new Rect(right.x, yy, right.width, right.yMax - yy + 60), s.Status, ShopTheme.Small);
            if (GUI.Button(new Rect(inner.x, inner.yMax - 56, inner.width * 0.3f, 52), "Map", ShopTheme.PlainButton)) GameUI.Instance.OpenMap();
            var eco = Economy.Instance;
            var act = new Rect(inner.x + inner.width * 0.32f, inner.yMax - 56, inner.width * 0.4f, 52);
            if (s.State == SiteState.ReadyForHouse && !s.HouseStarted)
            {
                bool afford = eco == null || eco.CanAfford(s.Plan.BuildCost);
                GUI.enabled = afford;
                if (GUI.Button(act, afford ? $"Have the house built  (${s.Plan.BuildCost:N0})" : $"Building it: ${s.Plan.BuildCost:N0}", ShopTheme.BuyButton))
                    if (!s.OrderHouse(out string why)) GameUI.Toast(why);
                GUI.enabled = true;
                act.x += act.width + inner.width * 0.02f; act.width = inner.width * 0.26f;
            }
            else if (s.HouseStarted)
            {
                if (s.State == SiteState.Completed && !s.Sold)
                {
                    if (GUI.Button(act, $"Sell it  (${s.Plan.SalePrice:N0})", ShopTheme.BuyButton))
                        if (!s.SellHouse(out string why)) GameUI.Toast(why);
                }
                else GUI.Label(act, s.Sold ? $"Sold to {s.Buyer}" : $"Sells for ${s.Plan.SalePrice:N0} when it's built", ShopTheme.DimStyle);
                return; // (no giving up once the house is going up)
            }
            int idx = sites.IndexOf(s);
            bool sure = confirmCancel == idx;
            if (GUI.Button(act, sure ? "Sure? The permit isn't refunded" : "Give up this site", ShopTheme.PlainButton))
            {
                if (sure) { Remove(s); selPlan = 0; confirmCancel = -1; } else confirmCancel = idx;
            }
        }

        public static string StageText(ConstructionSite s) => s.State switch
        {
            SiteState.BlueprintPlaced => "Set out: level the pad and dig the footings",
            SiteState.Excavating => $"Digging: pad {s.PadDone * 100f:0}% level, footings {s.TrenchDone * 100f:0}% dug",
            SiteState.ReadyForConcrete => "Ready for concrete: bring a mixer truck",
            SiteState.PouringConcrete => s.Curing ? $"Curing: {s.CureText()} left" : $"Pouring concrete: {s.ConcreteDone * 100f:0}% in",
            SiteState.ReadyForHouse => s.HouseStarted ? $"House going up: {s.HouseProgress * 100f:0}%" : "Ready for the house",
            _ => s.Sold ? $"Sold: {s.Address}" : $"Built: {s.Address} (for sale)",
        };

        /// <summary>The plan drawn from above: the pad in sand, the footings in blue, the front at the bottom.</summary>
        static void DrawFootprint(Rect r, HousePlan p)
        {
            ShopTheme.Fill(r, ShopTheme.Deep);
            var b = p.Bounds;
            float s = Mathf.Min((r.width - 24) / b.width, (r.height - 40) / b.height);
            Vector2 c = new Vector2(r.center.x, r.center.y - 8);
            const float step = 0.25f;
            float px = Mathf.Max(1f, step * s);
            if (Event.current.type == EventType.Repaint)
                for (float z = b.yMin; z < b.yMax; z += step)
                    for (float x = b.xMin; x < b.xMax; x += step)
                    {
                        var pl = new Vector2(x + step * 0.5f, z + step * 0.5f);
                        bool foot = p.FootingDistance(pl, out _) <= p.footingWidth * 0.5f;
                        if (!foot && !p.Inside(pl)) continue;
                        var col = foot ? new Color(0.35f, 0.75f, 1f) : new Color(0.62f, 0.5f, 0.32f);
                        ShopTheme.Fill(new Rect(c.x + (x - b.center.x) * s, c.y - (z + step - b.center.y) * s, px + 0.5f, px + 0.5f), col);
                    }
            GUI.Label(new Rect(r.x, r.yMax - 26, r.width, 22), "front", new GUIStyle(ShopTheme.DimStyle) { alignment = TextAnchor.MiddleCenter });
        }

        // ------------------------------------------------------------------ over the sites, and while setting one out

        GUIStyle label, help;
        void OnGUI()
        {
            var ui = GameUI.Instance;
            if (ui == null || ui.BlocksGameplay) return;
            GUI.skin = ShopTheme.Skin;
            label ??= new GUIStyle(GUI.skin.box) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true, normal = { textColor = new Color(0.6f, 0.88f, 1f) } };
            help ??= new GUIStyle(label) { fontSize = 15, normal = { textColor = ShopTheme.Cream } };
            if (placing != null)
            {
                string keys = $"{Controls.N(GameAction.PlanRotateLeft)}/{Controls.N(GameAction.PlanRotateRight)} or wheel: turn   {Controls.N(GameAction.PlanRaise)}/{Controls.N(GameAction.PlanLower)}: pad {(padShift >= 0 ? "+" : "")}{padShift * 100f:0} cm   {Controls.N(GameAction.PlanPlace)} / click: set down (${placing.price:N0})   {Controls.N(GameAction.PlanCancel)} / right-click: cancel";
                string top = valid ? $"Setting out the {placing.title} ({placing.SizeText})" : $"Can't build here: {why}";
                var c1 = new GUIContent(top + "\n" + keys);
                float w = Mathf.Min(Screen.width - 40, 920);
                float h = help.CalcHeight(c1, w);
                var old = help.normal.textColor;
                help.normal.textColor = valid ? ShopTheme.Cream : ShopTheme.Red;
                GUI.Box(new Rect((Screen.width - w) / 2f, 80, w, h + 8), c1, help);
                help.normal.textColor = old;
            }
            var cam = Camera.main;
            if (cam == null) return;
            foreach (var s in sites)
            {
                if (s == null) continue;
                Vector3 top = s.Center + Vector3.up * 3.5f;
                float d = Vector3.Distance(cam.transform.position, top);
                if (d > 80f) continue;
                Vector3 sp = cam.WorldToScreenPoint(top);
                if (sp.z <= 0f) continue;
                if (s.State == SiteState.Completed && d > 30f) continue; // (a finished house: its label only close by)
                string text = s.State == SiteState.Completed ? (s.Sold ? $"{s.Address}  ·  {s.Buyer}" : $"Your {s.Plan.title}  ·  {s.Address}  ·  for sale (${s.Plan.SalePrice:N0}, on your phone)")
                    : s.State == SiteState.ReadyForConcrete ? $"{s.Plan.title}  ·  ready for concrete ({s.Plan.ConcreteVolume:0.#} m³)"
                    : s.State >= SiteState.PouringConcrete ? $"{s.Plan.title}  ·  {StageText(s)}" +
                        (s.State == SiteState.PouringConcrete && !s.Curing ? $" ({s.ConcretePoured:0.0} of {s.ConcreteNeeded:0.0} m³)" + (d < 35f && s.PourSide.Length > 0 ? $"\nStill to fill: mostly the {s.PourSide} side" : "") : "")
                    : $"{s.Plan.title}  ·  pad {s.PadDone * 100f:0}% level  ·  footings {s.TrenchDone * 100f:0}% dug ({s.Plan.footingDepth:0.0#} m)";
                if (d < 35f && s.State < SiteState.ReadyForConcrete && s.PadHigh + s.PadLow > 0 && s.PadDone < s.Plan.needPad) text += "\n" + s.PadHint();
                var gc = new GUIContent(text);
                float w = Mathf.Min(520f, label.CalcSize(gc).x + 16f);
                float h = label.CalcHeight(gc, w);
                GUI.Box(new Rect(sp.x - w / 2f, Screen.height - sp.y - h / 2f, w, h + 6), gc, label);
            }
        }

        // ------------------------------------------------------------------ saving

        public void Write(BinaryWriter w)
        {
            w.Write(-3); // version 3: each site has its concrete, then its house, after it
            w.Write(nextId);
            w.Write(sites.Count);
            foreach (var s in sites) s.Write(w);
        }

        public void Read(BinaryReader r)
        {
            foreach (var s in sites) if (s != null) Destroy(s.gameObject);
            sites.Clear();
            removedIds.Clear();
            ReadSites(r, false);
        }

        void ReadSites(BinaryReader r, bool merge)
        {
            int first = r.ReadInt32(), version = 1;
            if (first < 0) version = -first;
            int nid = first < 0 ? r.ReadInt32() : first;
            if (!merge) nextId = nid;
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                int id = r.ReadInt32(); string title = r.ReadString(); var state = (SiteState)r.ReadByte();
                float x = r.ReadSingle(), z = r.ReadSingle(), yw = r.ReadSingle(), pad = r.ReadSingle(), start = r.ReadSingle();
                var plan = HousePlan.Find(title);
                var at = new Vector3(x, pad, z);
                var site = DigSite.At(at);
                if (plan == null || site == null || (merge && removedIds.Contains(id))) { ConstructionSite.SkipData(r, version); continue; }
                var have = merge ? ById(id) : null;
                if (have != null)
                {
                    have.MergeState(state);
                    if (version >= 2) have.MergeConcrete(r);
                    if (version >= 3) have.ReadHouse(r);
                    continue;
                }
                var go = new GameObject($"Construction site: {plan.title}");
                var s = go.AddComponent<ConstructionSite>();
                s.Id = id;
                s.Setup(plan, site, at, yw, pad, false);
                s.RestoreState(state, start);
                if (version >= 2) s.ReadConcrete(r);
                if (version >= 3) s.ReadHouse(r);
                sites.Add(s);
            }
        }
    }
}
