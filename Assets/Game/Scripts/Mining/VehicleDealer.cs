using FPSBase.Core;
using UnityEngine;

namespace FPSBase.Mining
{
    /// <summary>Equipment dealer at the outpost: buy bigger excavators and trucks. Bought machines appear on the sales lot.</summary>
    public class VehicleDealer : Interactable
    {
        [System.Serializable]
        public class Listing
        {
            public string name;
            [TextArea] public string description;
            public float price;
            public GameObject prefab;
            [Tooltip("Big machines are delivered to the heavy-equipment yard instead of the outpost lot.")]
            public bool heavy;
            public string category = "Machines";
        }

        public Listing[] listings;
        public Transform[] lotSpots;
        public Transform[] heavyLotSpots;
        Vector2 gridScroll, infoScroll;
        int tab;              // 0 = everything, else category tab-1
        int selected = -1;    // clicked card (the details stay on it); hover shows another
        int hovered = -1;

        public override string Prompt => "Equipment dealer  (excavators & trucks)";

        public override void Interact(PlayerInteractor player)
        {
            hovered = -1;
            GameUI.Instance.OpenShop("Equipment Dealer", Draw);
        }

        /// <summary>The categories, in the order the listings have them.</summary>
        System.Collections.Generic.List<string> Categories()
        {
            var cats = new System.Collections.Generic.List<string>();
            foreach (var l in listings) if (!cats.Contains(l.category)) cats.Add(l.category);
            return cats;
        }

        /// <summary>Dev screenshots: pick a tab and a machine as if clicked.</summary>
        public void DevShow(int tabIndex, int listing) { tab = tabIndex; selected = Mathf.Clamp(listing, 0, listings.Length - 1); }

        // a rounded box drawn as a 9-slice (the corners stay crisp however big the box is)
        static readonly System.Collections.Generic.Dictionary<Texture2D, GUIStyle> sliced = new();
        static void Card(Rect r, Texture2D t)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (!sliced.TryGetValue(t, out var st)) sliced[t] = st = new GUIStyle { border = new RectOffset(7, 7, 7, 7), normal = { background = t } };
            st.Draw(r, false, false, false, false);
        }

        void Draw(Rect area)
        {
            var eco = Economy.Instance;
            GameUI.Instance.PanelCorner = eco.Unlimited ? "Money: unlimited (test mode)" : $"${eco.Money:N0}";
            var cats = Categories();
            var e = Event.current;

            // --- left: category tabs
            const float tabW = 230f, infoW = 400f, gap = 16f;
            float y = area.y;
            GUI.Label(new Rect(area.x, y, tabW, 22), "CATEGORIES", FPSBase.UI.ShopTheme.DimStyle);
            y += 24;
            for (int c = -1; c < cats.Count; c++)
            {
                int n = 0; foreach (var l in listings) if (c < 0 || l.category == cats[c]) n++;
                bool on = tab == c + 1;
                string label = (c < 0 ? "Everything" : cats[c]) + $"   <size=12>({n})</size>";
                if (GUI.Button(new Rect(area.x, y, tabW, 40), label, on ? FPSBase.UI.ShopTheme.TabOn : FPSBase.UI.ShopTheme.Tab)) { tab = c + 1; gridScroll = Vector2.zero; }
                y += 46;
            }
            GUI.Label(new Rect(area.x, area.yMax - 64, tabW, 64),
                "Hover over a machine for its specs, click to keep it selected. Big machines are delivered to the heavy-equipment yard.", FPSBase.UI.ShopTheme.DimStyle);

            // --- middle: the cards
            var grid = new Rect(area.x + tabW + gap, area.y, area.width - tabW - infoW - gap * 2, area.height);
            const float minCardW = 220f, pad = 12f;
            int cols = Mathf.Max(1, Mathf.FloorToInt((grid.width - 18f + pad) / (minCardW + pad)));
            float cardW = (grid.width - 18f - pad * (cols - 1)) / cols;   // the cards fill the row
            float cardH = (cardW - 12f) / 1.6f + 96f;
            var shown = new System.Collections.Generic.List<int>();
            for (int i = 0; i < listings.Length; i++) if (tab == 0 || (tab - 1 < cats.Count && listings[i].category == cats[tab - 1])) shown.Add(i);
            int rows = Mathf.CeilToInt(shown.Count / (float)cols);
            var view = new Rect(0, 0, grid.width - 18f, Mathf.Max(grid.height, rows * (cardH + pad)));
            bool overGrid = grid.Contains(e.mousePosition);
            if (e.type == EventType.Repaint) hovered = -1;
            gridScroll = GUI.BeginScrollView(grid, gridScroll, view);
            for (int k = 0; k < shown.Count; k++)
            {
                int i = shown[k];
                var l = listings[i];
                var cr = new Rect((k % cols) * (cardW + pad), (k / cols) * (cardH + pad), cardW, cardH);
                bool over = overGrid && cr.Contains(e.mousePosition);
                if (over && e.type == EventType.Repaint) hovered = i;
                Card(cr, i == selected ? FPSBase.UI.ShopTheme.TexCardSel : over ? FPSBase.UI.ShopTheme.TexCardHover : FPSBase.UI.ShopTheme.TexCard);
                var img = new Rect(cr.x + 6, cr.y + 6, cardW - 12, (cardW - 12) / 1.6f);
                Picture(img, l);
                GUI.Label(new Rect(cr.x + 10, img.yMax + 6, cardW - 20, 40), l.name, FPSBase.UI.ShopTheme.CardName);
                bool afford = eco.CanAfford(l.price);
                GUI.Label(new Rect(cr.x + 10, cr.yMax - 28, cardW - 20, 24), eco.Unlimited ? "FREE (test mode)" : $"${l.price:N0}", afford ? FPSBase.UI.ShopTheme.Price : FPSBase.UI.ShopTheme.PriceBad);
                if (e.type == EventType.MouseDown && e.button == 0 && cr.Contains(e.mousePosition)) { selected = i; infoScroll = Vector2.zero; e.Use(); }
            }
            GUI.EndScrollView();

            // --- right: the details of the hovered (or selected) machine
            var info = new Rect(area.xMax - infoW, area.y, infoW, area.height);
            Card(info, FPSBase.UI.ShopTheme.TexCard);
            int show = hovered >= 0 ? hovered : selected;
            if (show < 0 || show >= listings.Length)
            {
                GUI.Label(new Rect(info.x + 20, info.y + 20, infoW - 40, 80), "Point at a machine to see what it is, what it does and what it weighs.", FPSBase.UI.ShopTheme.Body);
                return;
            }
            var L = listings[show];
            float iy = info.y + 10;
            var big = new Rect(info.x + 10, iy, infoW - 20, (infoW - 20) / 1.6f);
            Picture(big, L);
            iy = big.yMax + 8;
            float nameH = FPSBase.UI.ShopTheme.Heading.CalcHeight(new GUIContent(L.name), infoW - 32);
            GUI.Label(new Rect(info.x + 16, iy, infoW - 32, nameH), L.name, FPSBase.UI.ShopTheme.Heading);
            iy += nameH + 2;
            bool canBuy = eco.CanAfford(L.price) && L.prefab != null;
            GUI.Label(new Rect(info.x + 16, iy, infoW - 32, 22), L.category, FPSBase.UI.ShopTheme.DimStyle);
            priceRight ??= new GUIStyle(FPSBase.UI.ShopTheme.Price) { alignment = TextAnchor.UpperRight };
            priceRightBad ??= new GUIStyle(FPSBase.UI.ShopTheme.PriceBad) { alignment = TextAnchor.UpperRight };
            GUI.Label(new Rect(info.x + 16, iy - 2, infoW - 32, 24), eco.Unlimited ? "FREE" : $"${L.price:N0}", canBuy ? priceRight : priceRightBad);
            iy += 26;

            // scrolling text: the description, then the spec sheet
            var specs = FPSBase.UI.VehicleSpecs.Of(L.prefab);
            float tw = infoW - 32 - 18;
            const float labW = 130f;
            float descH = FPSBase.UI.ShopTheme.Body.CalcHeight(new GUIContent(L.description), tw);
            float specsH = 0f;
            foreach (var (lab, val) in specs) specsH += Mathf.Max(FPSBase.UI.ShopTheme.SpecValue.CalcHeight(new GUIContent(val), tw - labW), 18f) + 4f;
            var textArea = new Rect(info.x + 16, iy, infoW - 32, info.yMax - 76 - iy);
            var tv = new Rect(0, 0, tw, descH + 14 + 24 + specsH);
            infoScroll = GUI.BeginScrollView(textArea, infoScroll, tv);
            GUI.Label(new Rect(0, 0, tw, descH), L.description, FPSBase.UI.ShopTheme.Body);
            float sy = descH + 10;
            FPSBase.UI.ShopTheme.Fill(new Rect(0, sy, tw, 1), FPSBase.UI.ShopTheme.GoldDim);
            GUI.Label(new Rect(0, sy + 4, tw, 20), "SPECS", FPSBase.UI.ShopTheme.DimStyle);
            sy += 26;
            for (int k = 0; k < specs.Count; k++)
            {
                var (lab, val) = specs[k];
                float rh = Mathf.Max(FPSBase.UI.ShopTheme.SpecValue.CalcHeight(new GUIContent(val), tw - labW), 18f);
                if (k % 2 == 0) FPSBase.UI.ShopTheme.Fill(new Rect(0, sy - 1, tw, rh + 3), new Color(0f, 0f, 0f, 0.16f));
                GUI.Label(new Rect(4, sy, labW - 6, rh), lab, FPSBase.UI.ShopTheme.SpecLabel);
                GUI.Label(new Rect(labW, sy, tw - labW, rh), val, FPSBase.UI.ShopTheme.SpecValue);
                sy += rh + 4;
            }
            GUI.EndScrollView();

            // buy
            var buy = new Rect(info.x + 12, info.yMax - 64, infoW - 24, 52);
            GUI.enabled = canBuy;
            string bl = L.prefab == null ? "Not available" : eco.Unlimited ? "Get it  (free in test mode)" : canBuy ? $"Buy  ${L.price:N0}" : $"Can't afford  (${L.price:N0})";
            if (GUI.Button(buy, bl, FPSBase.UI.ShopTheme.BuyButton) && eco.TrySpend(L.price)) Deliver(L);
            GUI.enabled = true;
        }
        GUIStyle priceRight, priceRightBad;

        /// <summary>The machine's picture on the shop's dark backdrop (a placeholder while the picture is being taken).</summary>
        static void Picture(Rect r, Listing l)
        {
            FPSBase.UI.ShopTheme.Fill(r, FPSBase.UI.VehiclePreviews.Background);
            var tex = FPSBase.UI.VehiclePreviews.Get(l.prefab, l.name);
            if (tex != null) GUI.DrawTexture(r, tex, ScaleMode.ScaleAndCrop);
            else
            {
                int dots = 1 + (int)(Time.unscaledTime * 3f) % 3;
                waiting ??= new GUIStyle(FPSBase.UI.ShopTheme.Small) { alignment = TextAnchor.MiddleCenter };
                GUI.Label(r, "<color=#b8a584>taking the picture" + new string('.', dots) + "</color>", waiting);
            }
        }
        static GUIStyle waiting;

        /// <summary>Tests: deliver a listing as if it was bought.</summary>
        public void DevDeliver(int listing) { if (listing >= 0 && listing < listings.Length) Deliver(listings[listing]); }

        void Deliver(Listing l)
        {
            bool heavy = l.heavy && heavyLotSpots != null && heavyLotSpots.Length > 0;
            var spots = heavy ? heavyLotSpots : lotSpots;
            float clear = heavy ? 5f : 2.5f;
            // size it up, then pick the first lot spot it fits on (or the nearest clear ground around the lot)
            var go = Instantiate(l.prefab, spots[0].position + Vector3.down * 500f, spots[0].rotation);
            go.name = l.name;
            var lb = FPSBase.Vehicles.VehicleSpot.LocalBounds(go.transform);
            Vector3 at = Vector3.zero; float yaw = spots[0].eulerAngles.y; bool found = false;
            foreach (var s in spots)
            {
                Vector3 g = GroundAt(s.position);
                if (FPSBase.Vehicles.VehicleSpot.IsClear(g, s.eulerAngles.y, lb, go.transform)) { at = g; yaw = s.eulerAngles.y; found = true; break; }
            }
            if (!found) at = FPSBase.Vehicles.VehicleSpot.FindFree(spots[0].position, yaw, lb, go.transform, 60f);
            go.transform.SetPositionAndRotation(at + Vector3.up * 0.5f, Quaternion.Euler(0f, yaw, 0f));
            var grb = go.GetComponent<Rigidbody>();
            if (grb != null) { grb.position = go.transform.position; grb.rotation = go.transform.rotation; }
            Physics.SyncTransforms();
            FPSBase.Net.NetSession.NotifySpawn(go, System.Array.IndexOf(listings, l));
            GameUI.Toast(heavy ? $"{l.name} delivered to the heavy-equipment yard (east of the wash plant)!" : $"{l.name} delivered to the sales lot!");
            GameUI.Instance.ClosePanel();
        }

        /// <summary>Ground height under a lot spot (the dirt field may have been dug or piled up since).</summary>
        public static Vector3 GroundAt(Vector3 p)
        {
            var site = DigSite.At(p);
            if (site != null && site.Contains(p)) { p.y = site.SurfaceY(p); return p; }
            // highest static surface (skip parked machines and anything else with a rigidbody)
            float best = float.NegativeInfinity;
            foreach (var hit in Physics.RaycastAll(p + Vector3.up * 60f, Vector3.down, 120f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.rigidbody == null && hit.point.y > best) best = hit.point.y;
            if (best > float.NegativeInfinity) p.y = best;
            return p;
        }
    }
}
