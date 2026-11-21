using FPSBase.Core;
using UnityEngine;

namespace FPSBase.City
{
    /// <summary>
    /// The house on the finished foundation. Order it on your phone once the concrete has cured: the crew puts it up
    /// (the walls rise, then the roof goes on) and it's yours, with an address on the nearest street. Sell it and a
    /// family moves in.
    /// </summary>
    public partial class ConstructionSite
    {
        /// <summary>How far the house is up (below 0: not ordered; 1: built).</summary>
        public float HouseProgress { get; private set; } = -1f;
        public bool HouseStarted => HouseProgress >= 0f;
        public bool Sold { get; private set; }
        public string Buyer { get; private set; } = "";
        public House HouseHere { get; private set; }
        public GameObject HouseObject => houseRoot;
        /// <summary>The house's address (worked out from the nearest street).</summary>
        public string Address { get; private set; } = "";

        GameObject houseRoot;
        Transform houseWalls, houseRoof;

        public float FloorY => PadY + (Plan.slab ? Plan.slabThickness : 0f);
        public string HouseStage => HouseProgress < 0.7f ? "the walls" : HouseProgress < 1f ? "the roof" : "done";

        /// <summary>Pay the crew and start the house. False (and why) if it can't.</summary>
        public bool OrderHouse(out string why)
        {
            why = null;
            if (State != SiteState.ReadyForHouse) { why = "The foundation isn't finished yet"; return false; }
            if (HouseStarted) { why = "The house is already going up"; return false; }
            var eco = Economy.Instance;
            if (eco != null && !eco.TrySpend(Plan.BuildCost)) { why = $"Building it costs ${Plan.BuildCost:N0}: not enough money"; return false; }
            HouseProgress = 0f;
            SpawnHouse();
            Redraw();
            GameUI.Toast($"The crew's putting up your {Plan.title} (${Plan.BuildCost:N0}). About {Plan.BuildSeconds / 60f:0.#} minutes");
            Construction.Instance?.MarkNetDirty();
            return true;
        }

        void SpawnHouse()
        {
            if (houseRoot != null) return;
            houseRoot = Plan.housePrefab != null ? Instantiate(Plan.housePrefab) : HouseShell.Build(Plan);
            houseRoot.name = $"House: {Plan.title}";
            houseRoot.transform.SetParent(transform, false);
            houseRoot.transform.SetPositionAndRotation(new Vector3(Center.x, FloorY, Center.z), transform.rotation);
            houseWalls = houseRoot.transform.Find("Walls");
            houseRoof = houseRoot.transform.Find("Roof");
            if (Address.Length == 0) Address = MakeAddress();
            ShowProgress();
        }

        /// <summary>Walls up first, then the roof on (a house prefab without those parts just rises as a whole).</summary>
        void ShowProgress()
        {
            if (houseRoot == null) return;
            float p = Mathf.Clamp01(HouseProgress);
            if (houseWalls == null || houseRoof == null)
            {
                houseRoot.transform.localScale = new Vector3(1f, Mathf.Max(0.02f, p), 1f);
                return;
            }
            float w = Mathf.Clamp01(p / 0.7f), r = Mathf.Clamp01((p - 0.7f) / 0.3f);
            houseWalls.localScale = new Vector3(1f, Mathf.Max(0.02f, w), 1f);
            houseRoof.gameObject.SetActive(r > 0f);
            houseRoof.localScale = new Vector3(1f, Mathf.Max(0.02f, r), 1f);
            houseRoof.localPosition = new Vector3(0f, Mathf.Max(1, Plan.floors) * HouseShell.Storey * w, 0f);
        }

        void HouseUpdate(float dt)
        {
            if (!HouseStarted || HouseProgress >= 1f) return;
            HouseProgress = Mathf.Min(1f, HouseProgress + dt / Mathf.Max(1f, Plan.BuildSeconds));
            ShowProgress();
            if (HouseProgress >= 1f) FinishHouse(true);
        }

        /// <summary>Tests: the crew finishes now.</summary>
        public void TestFinishHouse() { if (HouseStarted && HouseProgress < 1f) { HouseProgress = 1f; ShowProgress(); FinishHouse(true); } }

        void FinishHouse(bool announce)
        {
            State = SiteState.Completed;
            if (houseRoot == null) SpawnHouse();
            ShowProgress();
            MakeHouseComponent();
            Status = Sold ? $"Sold to {Buyer}." : $"Built: {Address}. It's yours: sell it on your phone for ${Plan.SalePrice:N0}.";
            if (marker != null) marker.label = Sold ? $"{Address} ({Buyer})" : $"Your house: {Address}";
            if (announce) GameUI.Toast($"Your {Plan.title} is built: {Address}. Sell it on your phone (Build tab) for ${Plan.SalePrice:N0}");
            Redraw();
            Construction.Instance?.MarkNetDirty();
        }

        /// <summary>The house joins the town: an address and an owner (it isn't one of the customers' houses that text you for work).</summary>
        void MakeHouseComponent()
        {
            if (houseRoot == null) return;
            if (HouseHere == null)
            {
                var go = new GameObject("House (address)");
                go.SetActive(false);
                go.transform.SetParent(houseRoot.transform, false);
                var h = go.AddComponent<House>();
                h.playerBuilt = true;
                h.site = this;
                var b = Plan.Bounds;
                h.houseLocal = b;
                h.index = -1;
                HouseHere = h;
                // look at it: the walls carry the collider
                go.SetActive(true);
            }
            int num = 0; string street = Address;
            int sp = Address.IndexOf(' ');
            if (sp > 0 && int.TryParse(Address.Substring(0, sp), out num)) street = Address.Substring(sp + 1);
            HouseHere.number = num; HouseHere.street = street;
            if (Sold) { var parts = Buyer.Split(' '); HouseHere.first = parts[0]; HouseHere.last = parts.Length > 1 ? parts[1] : ""; }
            else { HouseHere.first = "You"; HouseHere.last = ""; }
            // the walls' colliders point the look-at prompt to the house
            foreach (var c in houseRoot.GetComponentsInChildren<Collider>(true))
                if (c.GetComponent<HouseLink>() == null) c.gameObject.AddComponent<HouseLink>().house = HouseHere;
        }

        static readonly string[] firsts = { "Dana", "Chris", "Jordan", "Sam", "Alex", "Robin", "Casey", "Morgan", "Jamie", "Taylor", "Lee", "Riley" };
        static readonly string[] lasts = { "Walker", "Hughes", "Bennett", "Foster", "Reyes", "Novak", "Palmer", "Quinn", "Hayes", "Brooks", "Murphy", "Ortiz" };

        /// <summary>Sell the finished house: the money's yours and a family moves in.</summary>
        public bool SellHouse(out string why)
        {
            why = null;
            if (State != SiteState.Completed) { why = "The house isn't finished"; return false; }
            if (Sold) { why = "Already sold"; return false; }
            var rng = new System.Random(Id * 7919 + Plan.order);
            Buyer = $"{firsts[rng.Next(firsts.Length)]} {lasts[rng.Next(lasts.Length)]}";
            Sold = true;
            Economy.Instance?.Earn(Plan.SalePrice);
            FinishHouse(false);
            GameUI.Toast($"Sold! {Buyer} bought your {Plan.title} at {Address} for ${Plan.SalePrice:N0}");
            return true;
        }

        /// <summary>A number on the nearest street (even on one side, odd on the other).</summary>
        string MakeAddress()
        {
            Street best = null; float bd = float.MaxValue;
            Vector2 c = new Vector2(Center.x, Center.z);
            foreach (var s in Street.All)
            {
                if (s == null) continue;
                var r = s.rect;
                Vector2 q = new Vector2(Mathf.Clamp(c.x, r.xMin, r.xMax), Mathf.Clamp(c.y, r.yMin, r.yMax));
                float d = (q - c).sqrMagnitude;
                if (d < bd) { bd = d; best = s; }
            }
            if (best == null || bd > 300f * 300f) return $"{100 + (Id % 400) * 2} Claim Rd";
            var rr = best.rect;
            bool alongX = rr.width >= rr.height;
            float along = alongX ? c.x - rr.xMin : c.y - rr.yMin;
            bool side = alongX ? c.y > rr.center.y : c.x > rr.center.x;
            int n = 200 + Mathf.RoundToInt(along / 11f) * 2 + (side ? 1 : 0);
            return $"{n} {best.streetName}";
        }

        // ------------------------------------------------------------------ saving

        void WriteHouse(System.IO.BinaryWriter w)
        {
            w.Write(HouseProgress); w.Write(Sold); w.Write(Buyer ?? ""); w.Write(Address ?? "");
        }

        public void ReadHouse(System.IO.BinaryReader r)
        {
            float p = r.ReadSingle(); bool sold = r.ReadBoolean(); string buyer = r.ReadString(), addr = r.ReadString();
            ApplyHouse(p, sold, buyer, addr);
        }

        /// <summary>Saved or a friend's state: the house as far up as it got (it only goes forward).</summary>
        void ApplyHouse(float p, bool sold, string buyer, string addr)
        {
            if (addr.Length > 0) Address = addr;
            if (sold && !Sold) { Sold = true; Buyer = buyer; }
            if (p > HouseProgress)
            {
                HouseProgress = p;
                if (p >= 0f) SpawnHouse();
            }
            if (HouseProgress >= 1f) FinishHouse(false);
            else ShowProgress();
            Redraw();
        }
    }

    /// <summary>On a player-built house's colliders: what you're looking at is that house.</summary>
    public class HouseLink : Interactable
    {
        public House house;
        public override string Prompt => house != null ? house.Prompt : "";
        public override void Interact(PlayerInteractor player) { if (house != null) house.Interact(player); }
    }
}
