using System.Collections.Generic;
using System.IO;
using FPSBase.Core;
using FPSBase.Mining;
using FPSBase.UI;
using UnityEngine;

namespace FPSBase.City
{
    public enum JobKind { Driveway, Ditch }
    public enum JobState { Offered, Accepted, Paid, Declined }

    public class Job
    {
        public int id, house;
        public JobKind kind;
        public JobState state;
        public float pay;
        /// <summary>Ditch: how deep (m). Driveway: how thick the gravel has to be (m).</summary>
        public float depth;
        /// <summary>Multiplayer: shared with the crew (anyone can work it and finish it, the pay is split), or one player's own.</summary>
        public bool shared;
        public ulong owner;      // who took it (0 = you, in single player)
        public string ownerName = "";
        public float progress;   // 0..1, last check
        public string status = "";
        public bool ready;       // good enough to get paid
        public bool tidy;        // neat work: a tip
        /// <summary>Ditch / trench: where it runs (ground points), how wide (m) and where on the lot (see Places).</summary>
        public Vector3 a, b;
        public float width = 0.6f;
        public int place;
        /// <summary>Share of the bill the customer keeps for what got broken, and what it was (part ids).</summary>
        public float damage;
        public List<string> hits = new();

        public House House => house >= 0 && house < City.House.All.Count ? City.House.All[house] : null;
        public static readonly string[] Places = { "drainage ditch", "ditch along the front yard", "trench across the back yard", "drain pipe trench", "trench along the house" };
        public string Title => kind == JobKind.Driveway ? "gravel driveway" : Places[Mathf.Clamp(place, 0, Places.Length - 1)];
        public float Length => Vector3.Distance(new Vector3(a.x, 0, a.z), new Vector3(b.x, 0, b.z));
        public bool Open => state == JobState.Offered || state == JobState.Accepted;
    }

    /// <summary>A text message: from the customer (`from` empty) or from one of the players (their name).</summary>
    public struct Text { public int house; public string from; public string body; public bool read; }

    /// <summary>
    /// The landscaping side of the business: people in town text you when they need work done (a gravel
    /// driveway, a drainage ditch), with what they'll pay. Accept on your phone (P), do the work, and get paid
    /// once it passes. In multiplayer the host's game keeps the one list everyone sees: take a job yourself, or
    /// share it with the crew and split the pay.
    /// </summary>
    public class Jobs : MonoBehaviour
    {
        public static Jobs Instance { get; private set; }

        [Tooltip("Seconds until the first customer texts you, then between new requests.")]
        public float firstText = 25f;
        public Vector2 textEvery = new Vector2(150f, 300f);
        public int maxOpenOffers = 3;
        [Header("Pay")]
        public float drivewayPerM2 = 90f, drivewayBase = 1500f;
        public float ditchPerM = 60f, ditchPerM3 = 320f, ditchBase = 700f;
        [Header("Markers")]
        [Tooltip("See-through light blue: the trench to dig, the gravel to lay.")]
        public Material trenchMaterial;
        [Tooltip("See-through orange: the beacon over a job's house.")]
        public Material beaconMaterial;
        [Header("Gravel driveway")]
        public float gravelThick = 0.06f;
        public float maxBump = 0.3f;
        public float needCoverage = 0.9f;
        [Header("Drainage ditch")]
        public float ditchDepth = 0.6f;
        public float needDitch = 0.9f;

        public readonly List<Job> jobs = new();
        public readonly List<Text> texts = new();
        float nextText;
        int nextId = 1;
        System.Random rng = new System.Random();

        public const int ActAccept = 0, ActDecline = 1, ActCancel = 2, ActFinish = 3, ActDamage = 4;

        void Awake() { Instance = this; nextText = firstText; }

        // ------------------------------------------------------------------ multiplayer

        static FPSBase.Net.NetSession Net => FPSBase.Net.NetSession.Instance;
        static bool InSession => FPSBase.Net.NetSession.InSession && Net != null;
        /// <summary>This game keeps the job list (single player, or the host).</summary>
        static bool Keeper => !InSession || Net.IsHost;
        public static ulong MyId => InSession ? Net.MyId : 0UL;
        public static string MyName => InSession ? Net.MyName : "You";
        public static int CrewSize => InSession ? Mathf.Max(1, Net.CrewSize) : 1;

        /// <summary>Is this player allowed to work / finish / cancel this job?</summary>
        public static bool IsMine(Job j) => j.shared || j.owner == MyId || !InSession;
        bool Mine(Text t) => !string.IsNullOrEmpty(t.from) && (t.from == MyName || !InSession);

        public Job OpenJobAt(House h)
        {
            if (h == null) return null;
            foreach (var j in jobs) if (j.house == h.index && j.Open) return j;
            return null;
        }

        public Job ById(int id) { foreach (var j in jobs) if (j.id == id) return j; return null; }

        public int Unread { get { int n = 0; foreach (var t in texts) if (string.IsNullOrEmpty(t.from) && !t.read) n++; return n; } }

        // ------------------------------------------------------------------ the day

        void Update()
        {
            var ui = GameUI.Instance;
            bool phoneUp = ui != null && ui.PanelTitle == PhoneTitle;
            if (Controls.Pressed(GameAction.Phone) && ui != null && !ui.Paused && PhoneKeyFree())
            {
                if (phoneUp) ui.ClosePanel(); else if (ui.PanelTitle == null) OpenPhone(null);
            }
            if (House.All.Count == 0) return;
            // the requests come in on the game that keeps the list (a friend's phone gets them from the host)
            bool clockRuns = InSession || !(ui != null && ui.BlocksGameplay && !phoneUp); // menus and pause stop the clock (not in multiplayer)
            if (Keeper && clockRuns && (nextText -= Time.deltaTime) <= 0f)
            {
                nextText = Mathf.Lerp(textEvery.x, textEvery.y, (float)rng.NextDouble());
                int offers = 0; foreach (var j in jobs) if (j.state == JobState.Offered) offers++;
                if (offers < maxOpenOffers) NewRequest();
            }
            // keep an eye on the jobs (the ground is the same on every game, so each checks for itself)
            if ((checkTimer -= Time.deltaTime) <= 0f)
            {
                checkTimer = 0.5f;
                foreach (var j in jobs) if (j.state == JobState.Accepted) Check(j);
            }
        }
        float checkTimer;

        /// <summary>The D-pad's left button is a machine control in the excavators and grader: the phone stays on P there.</summary>
        static bool PhoneKeyFree()
        {
            var v = GameUI.Instance != null ? GameUI.Instance.ActiveVehicle : null;
            if (v is FPSBase.Vehicles.MiniExcavator || v is FPSBase.Vehicles.MotorGrader)
                return UnityEngine.InputSystem.Keyboard.current != null && Controls.KeyPressed(GameAction.Phone);
            return true;
        }

        /// <summary>Someone in town texts about a job (or a particular house, for tests).</summary>
        public Job NewRequest(House at = null, JobKind? kind = null)
        {
            var free = new List<House>();
            foreach (var h in House.All) if (OpenJobAt(h) == null) free.Add(h);
            if (at == null)
            {
                if (free.Count == 0) return null;
                at = free[rng.Next(free.Count)];
            }
            var k = kind ?? (rng.NextDouble() < 0.6 ? JobKind.Driveway : JobKind.Ditch);
            var j = new Job { id = nextId++, house = at.index, kind = k, state = JobState.Offered };
            string msg;
            if (k == JobKind.Driveway)
            {
                float area = at.driveway.width * at.driveway.height;
                float[] thick = { 0.05f, 0.06f, 0.06f, 0.08f, 0.1f, 0.12f };
                j.depth = thick[rng.Next(thick.Length)];
                j.pay = Mathf.Round((area * drivewayPerM2 * (0.5f + j.depth / 0.12f) + drivewayBase) / 50f) * 50f;
                float m3 = area * (j.depth + 0.04f);
                string[] t =
                {
                    $"Hi, this is {at.first} at {at.Address}. Our driveway is nothing but ruts and mud. Could you put down fresh gravel, at least {j.depth * 100f:0} cm of it? It's about {at.driveway.width:0} x {at.driveway.height:0} m. I can pay ${j.pay:N0}.",
                    $"Hey! {at.first} from {at.Address} here. The neighbours said you do gravel. Our drive needs resurfacing, roughly {area:0} m², {j.depth * 100f:0} cm deep. Would ${j.pay:N0} work?",
                    $"Hello, {at.first} {at.last} ({at.Address}). We'd like our dirt driveway gravelled before winter, a good {j.depth * 100f:0} cm. ${j.pay:N0} for the job?",
                };
                msg = t[rng.Next(t.Length)] + $"  (Tip: about {m3:0.#} m³ of gravel covers it. Dump it on the drive and spread it out evenly to the top of the blue marker.)";
                string near = Hazards(at, new Vector3(at.driveway.xMin, 0, at.driveway.yMin), new Vector3(at.driveway.xMax, 0, at.driveway.yMax), Mathf.Max(at.driveway.width, at.driveway.height), true);
                if (near != "") msg += $"  Please mind the {near}: anything that gets damaged comes off the bill.";
            }
            else
            {
                PlanTrench(at, j);
                float len = j.Length, vol = len * j.depth * j.width;
                j.pay = Mathf.Round((ditchBase + vol * ditchPerM3 + len * ditchPerM + (j.place >= 3 ? 300f : 0f)) / 50f) * 50f;
                string size = $"{len:0} m long, {j.depth:0.0#} m deep and {j.width * 100f:0} cm wide";
                string ask = j.place switch
                {
                    0 => $"Water pools down the side of our lot every time it rains. Could you dig a drainage ditch along there, {size}?",
                    1 => $"Our front yard floods by the sidewalk. We need a ditch along the front of the yard, {size}.",
                    2 => $"The back yard turns into a pond after rain. Could you dig a trench across it, {size}?",
                    3 => $"The downspout dumps water right against the house. We need a trench from the corner of the house out toward the street for a drain pipe, {size}.",
                    _ => $"Water's getting into the basement along the side of the house. Could you dig a trench along that wall, {size}? Carefully!",
                };
                msg = $"Hi, {at.first} {at.last} here, {at.Address}. {ask} I'll pay ${j.pay:N0}.";
                string near = Hazards(at, j.a, j.b, j.width, false);
                if (near != "") msg += $"  It runs close to the {near}, so please be careful: anything that gets damaged comes off the bill.";
            }
            jobs.Add(j);
            Say(at, "", msg);
            ToastNew(at, j);
            Shared();
            return j;
        }

        /// <summary>
        /// Where a customer wants their trench: down the side of the lot, along the front of the yard, across the
        /// back yard, from the downspout at the corner of the house out toward the street, or along the side of the
        /// house. Never through the house, the driveway, a tree, the shed or a fence; often close to them.
        /// </summary>
        void PlanTrench(House h, Job j)
        {
            var site = DigSite.At(h.Front);
            float W = h.lotHalfWidth, D = h.lotDepth; int ys = h.yardSide;
            var hl = h.houseLocal; var dl = h.drivewayLocal;
            float R() => (float)rng.NextDouble();
            float Pick(params float[] v) => v[rng.Next(v.Length)];
            for (int attempt = 0; attempt < 40; attempt++)
            {
                int place = rng.Next(5);
                Vector2 la, lb;
                switch (place)
                {
                    case 0: { float x = ys * (W - 1.3f - R() * 0.8f); la = new Vector2(x, 1.0f + R() * 3f); lb = new Vector2(x, D - 1.0f - R() * 4f); break; }
                    case 1: { float z = 2.0f + R() * 1.6f; float xs = ys > 0 ? dl.xMax + 1.0f : dl.xMin - 1.0f; la = new Vector2(xs, z); lb = new Vector2(ys * (W - 1.0f - R() * 2f), z); break; }
                    case 2:
                    {
                        if (D - hl.yMax < 2.2f) continue;
                        float z = Mathf.Lerp(hl.yMax, D, 0.5f) + (R() - 0.5f) * 0.6f;
                        la = new Vector2(-W + 1.2f + R() * 4f, z); lb = new Vector2(W - 1.2f - R() * 4f, z); break;
                    }
                    case 3: { float cx = ys > 0 ? hl.xMax - 0.5f : hl.xMin + 0.5f; la = new Vector2(cx, hl.yMin - 0.6f); lb = new Vector2(cx, 1.2f + R() * 2f); break; }
                    default: { float x = ys > 0 ? hl.xMax + 1.1f + R() * 0.4f : hl.xMin - 1.1f - R() * 0.4f; la = new Vector2(x, hl.yMin - 0.5f); lb = new Vector2(x, hl.yMax + 0.5f); break; }
                }
                float depth = place switch { 0 => Pick(0.5f, 0.6f, 0.8f, 1.0f, 1.2f), 1 => Pick(0.4f, 0.5f, 0.6f), 2 => Pick(0.5f, 0.6f, 0.8f, 1.0f), 3 => Pick(0.4f, 0.5f), _ => Pick(0.5f, 0.6f, 0.7f) };
                float width = place == 3 ? Pick(0.4f, 0.5f) : Pick(0.5f, 0.6f, 0.8f, 1.0f);
                Vector3 a = h.Local(la.x, la.y), b = h.Local(lb.x, lb.y);
                if (Vector3.Distance(a, b) < 3f || !TrenchFits(h, site, a, b, width)) continue;
                j.place = place; j.a = a; j.b = b; j.depth = depth; j.width = width;
                return;
            }
            j.place = 0; j.a = h.ditchA; j.b = h.ditchB; j.depth = ditchDepth; j.width = 0.6f;
        }

        /// <summary>The trench is all diggable yard: not under a house, across a driveway or the street, or through anything standing there.</summary>
        static bool TrenchFits(House h, DigSite site, Vector3 a, Vector3 b, float width)
        {
            if (site == null || !site.Contains(a) || !site.Contains(b)) return false;
            Vector3 dir = (b - a); dir.y = 0f; float len = dir.magnitude; dir /= len;
            Vector3 across = Vector3.Cross(Vector3.up, dir) * (width / 2f + 0.15f);
            for (float t = 0f; t <= len; t += 0.5f)
                foreach (var off in new[] { Vector3.zero, across, -across })
                {
                    Vector3 p = a + dir * t + off;
                    if (!site.Contains(p)) return false;
                    foreach (var o in House.All)
                    {
                        var f = o.Footprint; if (Rect.MinMaxRect(f.xMin - 0.2f, f.yMin - 0.2f, f.xMax + 0.2f, f.yMax + 0.2f).Contains(new Vector2(p.x, p.z))) return false;
                        if (o.driveway.Contains(new Vector2(p.x, p.z))) return false;
                    }
                    foreach (var st in Street.All) if (st.Rect.Contains(new Vector2(p.x, p.z))) return false;
                }
            // nothing in the way (trees, sheds, fences, hedges, the porch, the mailbox)
            Vector3 mid = (a + b) / 2f; mid.y = site.OriginalY(mid) + 1.05f;
            var cols = Physics.OverlapBox(mid, new Vector3(width / 2f + 0.25f, 0.85f, len / 2f + 0.2f), Quaternion.LookRotation(dir), ~0, QueryTriggerInteraction.Ignore);
            foreach (var c in cols)
            {
                if (DigSite.IsSiteCollider(c) || c is TerrainCollider) continue;
                if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) continue; // a machine parked there moves
                return false;
            }
            return true;
        }

        /// <summary>The things on the lot close to the work ("mailbox, fence and house"), for the customer's text.</summary>
        static string Hazards(House h, Vector3 a, Vector3 b, float width, bool area)
        {
            var names = new List<string>();
            float reach = width / 2f + 2.2f;
            float Dist(Vector3 p)
            {
                if (area) { var r = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z), Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z)); float dx = Mathf.Max(r.xMin - p.x, 0f, p.x - r.xMax), dz = Mathf.Max(r.yMin - p.z, 0f, p.z - r.yMax); return Mathf.Sqrt(dx * dx + dz * dz); }
                Vector3 ab = b - a; ab.y = 0f; Vector3 ap = p - a; ap.y = 0f;
                float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
                return (ap - ab * t).magnitude;
            }
            foreach (var d in h.Parts)
            {
                if (d.what == "house" || names.Contains(d.what)) continue;
                var col = d.GetComponent<Collider>();
                Vector3 p = col != null ? col.ClosestPointOnBounds(Vector3.Lerp(a, b, 0.5f)) : d.transform.position;
                if (Dist(p) < reach) names.Add(d.what);
            }
            var f = h.Footprint;
            for (float t = 0f; t <= 1f; t += 0.05f)
            {
                Vector3 p = Vector3.Lerp(a, b, t);
                float dx = Mathf.Max(f.xMin - p.x, 0f, p.x - f.xMax), dz = Mathf.Max(f.yMin - p.z, 0f, p.z - f.yMax);
                if (Mathf.Sqrt(dx * dx + dz * dz) < width / 2f + 1.6f) { names.Add("house"); break; }
            }
            if (names.Count == 0) return "";
            if (names.Count == 1) return names[0];
            return string.Join(", ", names.GetRange(0, names.Count - 1)) + " and " + names[names.Count - 1];
        }

        static void ToastNew(House at, Job j) =>
            GameUI.Toast($"New text from {at.first} ({at.Address}): {j.Title}, ${j.pay:N0}  —  [{Controls.N(GameAction.Phone)}] phone");

        void Say(House h, string from, string body) => texts.Add(new Text { house = h.index, from = from, body = body, read = !string.IsNullOrEmpty(from) || (GameUI.Instance != null && GameUI.Instance.PanelTitle == PhoneTitle && chatWith == h.index) });

        /// <summary>The list changed on the game that keeps it: everyone else's phone gets the new list.</summary>
        void Shared() { if (InSession && Net.IsHost) Net.SendJobs(); }

        // ------------------------------------------------------------------ what you do about a job

        /// <summary>Take the job: just you (`shared` false) or with the crew (in multiplayer; the pay is split).</summary>
        public void Accept(Job j, bool shared = false) => Act(j, ActAccept, shared);
        public void Decline(Job j) => Act(j, ActDecline, false);
        public void Cancel(Job j) => Act(j, ActCancel, false);

        readonly HashSet<string> fined = new();

        /// <summary>A machine hit something on a lot (or the dig went under it): it comes off that job's bill, or they bill you.</summary>
        public void Damage(House h, Damageable part, string by)
        {
            if (h == null || part == null) return;
            var j = OpenJobAt(h);
            if (j != null && j.state == JobState.Accepted)
            {
                if (j.hits.Contains(part.id)) return;
                part.ShowBroken();
                GameUI.Toast($"Careful! You damaged {h.first}'s {part.what}");
                Act(j, ActDamage, false, part.id);
                return;
            }
            // no job here: the owner sends a bill
            string key = h.index + "/" + part.id;
            if (fined.Contains(key)) return;
            fined.Add(key);
            part.ShowBroken();
            Economy.Instance?.Earn(-part.fine);
            GameUI.Toast($"You damaged {h.Owner}'s {part.what} at {h.Address}: they billed you ${part.fine:N0}");
            if (Keeper) { Say(h, "", $"Hey! Somebody with a {by} just wrecked our {part.what}. That's ${part.fine:N0} to fix, and I've sent you the bill."); Shared(); }
        }

        /// <summary>The foundation, the mailbox post, a tree's roots: digging too close damages them (the game that keeps the list checks).</summary>
        void DigHazards(Job j)
        {
            var h = j.House; if (h == null) return;
            var site = DigSite.At(h.Front); if (site == null) return;
            var f = h.Footprint;
            if (!j.hits.Contains("foundation"))
            {
                for (float x = f.xMin; x <= f.xMax; x += 1f)
                    foreach (float z in new[] { f.yMin - 0.45f, f.yMax + 0.45f })
                        if (site.CutDepth(new Vector3(x, 0, z)) > 0.45f) { ApplyAction(j.id, ActDamage, false, j.owner, "", "foundation"); return; }
                for (float z = f.yMin; z <= f.yMax; z += 1f)
                    foreach (float x in new[] { f.xMin - 0.45f, f.xMax + 0.45f })
                        if (site.CutDepth(new Vector3(x, 0, z)) > 0.45f) { ApplyAction(j.id, ActDamage, false, j.owner, "", "foundation"); return; }
            }
            foreach (var d in h.Parts)
            {
                if (j.hits.Contains(d.id) || (d.what != "mailbox" && d.what != "tree")) continue;
                float r = d.what == "tree" ? 0.9f : 0.45f, deep = d.what == "tree" ? 0.5f : 0.35f;
                Vector3 c = d.transform.position;
                foreach (var o in new[] { Vector3.zero, Vector3.right * r, Vector3.left * r, Vector3.forward * r, Vector3.back * r })
                    if (site.CutDepth(c + o) > deep) { d.ShowBroken(); ApplyAction(j.id, ActDamage, false, j.owner, "", d.id); break; }
            }
        }

        static string Complaint(string what, float pen)
        {
            int pct = Mathf.RoundToInt(pen * 100f);
            return what switch
            {
                "mailbox" => $"Hey! Our mailbox just got flattened. I'm taking {pct}% off the bill for a new one.",
                "house" => $"Whoa! You just hit the house! That's coming off the bill: {pct}%.",
                "foundation" => $"You're digging right under our foundation! That's going to cost you {pct}% of the bill.",
                "tree" => $"Our tree! That's {pct}% off the bill.",
                _ => $"Hey, careful! You wrecked our {what}. I'm taking {pct}% off the bill to fix it.",
            };
        }
        /// <summary>Get paid for a job that passes (false if it isn't done, or isn't yours).</summary>
        public bool Finish(Job j)
        {
            if (j == null || j.state != JobState.Accepted || !IsMine(j)) return false;
            Check(j);
            if (!j.ready) return false;
            Act(j, ActFinish, j.shared);
            return Keeper ? j.state == JobState.Paid : true;
        }

        void Act(Job j, int action, bool shared, string arg = null)
        {
            if (j == null) return;
            if (Keeper) ApplyAction(j.id, action, shared, MyId, MyName, arg);
            else Net.SendJobAction(j.id, action, shared, arg); // the host does it and sends the list back
        }

        /// <summary>A player's answer, on the game that keeps the list (single player, the host, or a friend's sent over).</summary>
        public void ApplyAction(int id, int action, bool shared, ulong by, string byName, string arg = null)
        {
            var j = ById(id);
            if (j == null) return;
            var h = j.House;
            if (h == null) return;
            bool allowed = j.shared || j.owner == by;
            switch (action)
            {
                case ActAccept:
                    if (j.state != JobState.Offered) return;
                    j.state = JobState.Accepted;
                    j.shared = shared && InSession;
                    j.owner = by; j.ownerName = byName ?? "";
                    Say(h, byName, j.shared ? "Sounds good, the crew will be over soon." : "Sounds good, I'll be over soon.");
                    Say(h, "", (j.kind == JobKind.Driveway ? "Great, thanks! The drive is marked out: lay the gravel up to the top of the blue marker." : "Great, thank you! I've marked the trench out in blue: dig it out to the bottom of the marker.") +
                               $"  {h.Directions}  Look for the orange beacon over the house.");
                    Stake(j);
                    Check(j);
                    break;
                case ActDecline:
                    if (j.state != JobState.Offered) return;
                    j.state = JobState.Declined;
                    Say(h, byName, "Sorry, I'm booked up right now.");
                    Say(h, "", "No worries, maybe next time!");
                    break;
                case ActCancel:
                    if (j.state != JobState.Accepted || !allowed) return;
                    j.state = JobState.Declined;
                    Say(h, byName, "Sorry, I can't get to it after all.");
                    Say(h, "", "Oh. Okay then.");
                    Unstake(j);
                    break;
                case ActDamage:
                {
                    if (j.state != JobState.Accepted || string.IsNullOrEmpty(arg) || j.hits.Contains(arg)) return;
                    var part = h.Part(arg);
                    float pen = arg == "foundation" ? 0.2f : part != null ? part.penalty : 0.1f;
                    j.hits.Add(arg);
                    j.damage = Mathf.Min(0.9f, j.damage + pen);
                    h.ShowDamage(arg);
                    Say(h, "", Complaint(arg == "foundation" ? "foundation" : part != null ? part.what : arg, pen));
                    break;
                }
                case ActFinish:
                {
                    if (j.state != JobState.Accepted || !allowed) return;
                    Check(j);
                    float tip = j.tidy && j.damage <= 0f ? Mathf.Round(j.pay * 0.1f / 10f) * 10f : 0f;
                    float kept = Mathf.Round(j.pay * j.damage);
                    float total = j.pay - kept + tip;
                    j.state = JobState.Paid;
                    Say(h, byName, "All done! Have a look.");
                    string thanks = (j.kind == JobKind.Driveway ? "Wow, it looks brand new! " : "Perfect, that should keep the yard dry. ") +
                                    (kept > 0f ? $"Sent ${total:N0}: that's ${j.pay:N0} less ${kept:N0} for the damage." : $"Sent ${j.pay:N0}" + (tip > 0f ? $", plus ${tip:N0} for doing such a tidy job." : ". Thanks!"));
                    if (j.shared) thanks += $"  (Split {CrewSize} ways: ${total / CrewSize:N0} each.)";
                    Say(h, "", thanks);
                    Unstake(j);
                    string why = $"the {j.Title} at {h.Address}";
                    if (j.shared && InSession) Net.PayCrew(total / CrewSize, why);       // everyone in the game gets a share
                    else if (InSession && by != MyId) Net.PayPlayer(by, total, why);    // a friend's own job: it's theirs
                    else { Economy.Instance?.Earn(total); GameUI.Toast($"{h.first} paid you ${total:N0} for {why}"); }
                    break;
                }
            }
            Shared();
        }

        /// <summary>How far along a job is (updates progress / status / ready).</summary>
        public void Check(Job j)
        {
            var h = j.House;
            if (h == null) return;
            if (j.kind == JobKind.Driveway)
            {
                var r = h.driveway;
                var shrunk = new Rect(r.xMin + 0.3f, r.yMin + 0.3f, r.width - 0.6f, r.height - 0.6f);
                var site = DigSite.At(r.center3());
                site.SurveyGravel(shrunk, j.depth - 0.015f, out float cov, out float avg, out float bump);
                j.progress = Mathf.Clamp01(cov / needCoverage);
                j.ready = cov >= needCoverage && bump <= maxBump;
                j.tidy = cov >= 0.98f && bump <= maxBump * 0.5f;
                j.status = $"Gravel covers {cov * 100f:0}% of the drive (needs {needCoverage * 100f:0}%), {avg * 100f:0} cm thick on average." +
                           (bump > maxBump ? $"  There's a pile {bump:0.0} m high: spread it out (no more than {maxBump * 100f:0} cm)." : "");
            }
            else
            {
                Vector3 a = j.a, b = j.b;
                if ((a - b).sqrMagnitude < 0.01f) { a = h.ditchA; b = h.ditchB; }
                var site = DigSite.At(a);
                Vector3 dir = (b - a); dir.y = 0f; dir.Normalize();
                Vector3 side = Vector3.Cross(Vector3.up, dir);
                float half = Mathf.Max(0f, j.width / 2f - 0.2f);
                int n = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(a, b) / 0.5f)), ok = 0;
                float sum = 0f;
                for (int i = 0; i <= n; i++)
                {
                    Vector3 p = Vector3.Lerp(a, b, i / (float)n);
                    float d = Mathf.Max(site.CutDepth(p), site.CutDepth(p + side * 0.12f), site.CutDepth(p - side * 0.12f));
                    // a wide trench has to be dug out across its width too
                    bool wide = half < 0.15f || (site.CutDepth(p + side * half) >= j.depth - 0.2f && site.CutDepth(p - side * half) >= j.depth - 0.2f);
                    sum += d;
                    if (d >= j.depth - 0.08f && wide) ok++;
                }
                float f = ok / (float)(n + 1);
                j.progress = Mathf.Clamp01(f / needDitch);
                j.ready = f >= needDitch;
                j.tidy = f >= 0.98f;
                j.status = $"{f * 100f:0}% of the trench is {j.depth:0.0#} m deep and {j.width * 100f:0} cm wide (needs {needDitch * 100f:0}%), {sum / (n + 1):0.00} m deep on average.";
            }
            if (j.damage > 0f) j.status += $"  Damage so far: {j.damage * 100f:0}% off the bill.";
            if (Keeper && j.state == JobState.Accepted) DigHazards(j);
        }

        /// <summary>Who has the job, for the phone.</summary>
        public static string WhoHas(Job j) =>
            j.state != JobState.Accepted ? "" : j.shared ? "the crew" : (!InSession || j.owner == MyId) ? "you" : (string.IsNullOrEmpty(j.ownerName) ? "a friend" : j.ownerName);

        // ------------------------------------------------------------------ stakes and map pins

        readonly Dictionary<int, GameObject> stakes = new();
        static Material stakeMat;

        void Stake(Job j)
        {
            Unstake(j);
            var h = j.House;
            if (h == null) return;
            var root = new GameObject($"Job stakes: {h.Address}");
            var site = DigSite.At(h.Front);
            float G(Vector3 p) => site != null && site.Contains(p) ? site.OriginalY(p) : h.transform.position.y;
            if (stakeMat == null)
            {
                stakeMat = new Material(Shader.Find("HDRP/Lit"));
                stakeMat.SetColor("_BaseColor", new Color(1f, 0.45f, 0.05f));
                stakeMat.SetColor("_EmissiveColor", new Color(1f, 0.35f, 0.02f) * 0.6f);
                stakeMat.EnableKeyword("_EMISSIVE_COLOR_MAP");
            }
            void Post(Vector3 p)
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(s.GetComponent<Collider>());
                s.transform.SetParent(root.transform, false);
                s.transform.position = new Vector3(p.x, G(p) + 0.45f, p.z);
                s.transform.localScale = new Vector3(0.06f, 0.9f, 0.06f);
                s.GetComponent<MeshRenderer>().sharedMaterial = stakeMat;
                var flag = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(flag.GetComponent<Collider>());
                flag.transform.SetParent(s.transform, false);
                flag.transform.localPosition = new Vector3(1.6f, 0.42f, 0f);
                flag.transform.localScale = new Vector3(3f, 0.12f, 0.2f);
                flag.GetComponent<MeshRenderer>().sharedMaterial = stakeMat;
            }
            if (j.kind == JobKind.Driveway)
            {
                var r = h.driveway;
                Post(new Vector3(r.xMin, 0, r.yMin)); Post(new Vector3(r.xMax, 0, r.yMin));
                Post(new Vector3(r.xMin, 0, r.yMax)); Post(new Vector3(r.xMax, 0, r.yMax));
                // the gravel to lay: a see-through slab as thick as it has to be
                var c = r.center3(); float g = G(c);
                Box(root.transform, new Vector3(c.x, g + j.depth / 2f, c.z), Quaternion.identity, new Vector3(r.width, j.depth, r.height), TrenchMat);
            }
            else
            {
                Vector3 a = j.a, b = j.b;
                if ((a - b).sqrMagnitude < 0.01f) { a = h.ditchA; b = h.ditchB; }
                Vector3 dir = b - a; dir.y = 0f;
                float len = dir.magnitude;
                var rot = Quaternion.LookRotation(dir / len);
                // the trench: a see-through light blue box, as wide and as deep as it has to be dug. It follows the
                // ground (as the digging is measured: that deep below the ground at each point), in short pieces
                // that tilt with the slope, so on a hillside it isn't left floating over the low end.
                const float above = 0.35f; // stands up out of the grass so you see it before you dig
                Vector3 side = rot * Vector3.right * (j.width / 2f);
                float Ground(Vector3 p) => G(p);
                int pieces = Mathf.Max(1, Mathf.CeilToInt(len / 1.0f));
                Vector3 fwd = dir / len;
                for (int k = 0; k < pieces; k++)
                {
                    Vector3 p0 = a + fwd * (len * k / pieces), p1 = a + fwd * (len * (k + 1) / pieces);
                    p0.y = Ground(p0); p1.y = Ground(p1);
                    Vector3 seg = p1 - p0, c = (p0 + p1) / 2f;
                    var r = Quaternion.LookRotation(seg.normalized, Vector3.up);
                    float sl = seg.magnitude + (k < pieces - 1 ? 0.02f : 0f); // (a hair of overlap: no seams)
                    var bx = Box(root.transform, c + Vector3.up * ((above - j.depth) / 2f), r, new Vector3(j.width, j.depth + above, sl), TrenchMat);
                    bx.name = "Trench Box";
                    // its outline on the ground
                    foreach (float sgn in new[] { -1f, 1f })
                        Box(root.transform, c + Vector3.up * 0.04f + side * sgn, r, new Vector3(0.05f, 0.05f, sl), stakeMat);
                }
                // and across each end
                foreach (var e in new[] { a, b })
                    Box(root.transform, new Vector3(e.x, Ground(e) + 0.04f, e.z), rot, new Vector3(j.width + 0.05f, 0.05f, 0.05f), stakeMat);
                Post(a - rot * Vector3.forward * 0.3f + side * 1.6f); Post(b + rot * Vector3.forward * 0.3f + side * 1.6f);
            }
            // a beacon over the house you can see from the edge of town
            Box(root.transform, h.Front + Vector3.up * (G(h.Front) + 11f), Quaternion.identity, new Vector3(0.9f, 22f, 0.9f), BeaconMat);
            var mk = new GameObject($"Job: {h.Address}");
            mk.transform.SetParent(root.transform, false);
            mk.transform.position = h.Front;
            var m = mk.AddComponent<MapMarker>();
            string who = WhoHas(j);
            m.label = who == "you" ? $"Job: {h.Address}" : $"Job ({who}): {h.Address}";
            m.color = new Color(1f, 0.5f, 0.1f); m.arriveOffset = Vector3.zero;
            stakes[j.id] = root;
        }

        static GameObject Box(Transform parent, Vector3 at, Quaternion rot, Vector3 size, Material m)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(b.GetComponent<Collider>());
            b.transform.SetParent(parent, false);
            b.transform.SetPositionAndRotation(at, rot);
            b.transform.localScale = size;
            var mr = b.GetComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return b;
        }

        Material TrenchMat => trenchMaterial != null ? trenchMaterial : (trenchMaterial = SeeThrough(new Color(0.35f, 0.75f, 1f, 0.32f)));
        Material BeaconMat => beaconMaterial != null ? beaconMaterial : (beaconMaterial = SeeThrough(new Color(1f, 0.55f, 0.1f, 0.3f)));

        /// <summary>A see-through material made here (the town's builder normally supplies them).</summary>
        static Material SeeThrough(Color c)
        {
            var m = new Material(Shader.Find("HDRP/Lit"));
            m.SetColor("_BaseColor", c);
            m.SetFloat("_SurfaceType", 1f); m.SetFloat("_BlendMode", 0f); m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_DoubleSidedEnable", 1f);
            try { UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(m); } catch { }
            return m;
        }

        void Unstake(Job j)
        {
            if (stakes.TryGetValue(j.id, out var go) && go != null) Destroy(go);
            stakes.Remove(j.id);
        }

        /// <summary>Stakes on every accepted job, none on the rest.</summary>
        void Restake()
        {
            var keep = new HashSet<int>();
            foreach (var j in jobs) if (j.state == JobState.Accepted) { keep.Add(j.id); if (!stakes.ContainsKey(j.id)) Stake(j); Check(j); }
            foreach (var id in new List<int>(stakes.Keys)) if (!keep.Contains(id)) { if (stakes[id] != null) Destroy(stakes[id]); stakes.Remove(id); }
        }

        // ------------------------------------------------------------------ save / network

        const int Version = 3;

        public void Write(BinaryWriter w)
        {
            w.Write(Version);
            w.Write(nextId); w.Write(nextText);
            w.Write(jobs.Count);
            foreach (var j in jobs)
            {
                w.Write(j.id); w.Write(j.house); w.Write((int)j.kind); w.Write((int)j.state); w.Write(j.pay); w.Write(j.depth);
                w.Write(j.shared); w.Write(j.owner); w.Write(j.ownerName ?? "");
                w.Write(j.a.x); w.Write(j.a.y); w.Write(j.a.z); w.Write(j.b.x); w.Write(j.b.y); w.Write(j.b.z);
                w.Write(j.width); w.Write(j.place); w.Write(j.damage);
                w.Write(j.hits.Count); foreach (var hid in j.hits) w.Write(hid ?? "");
            }
            w.Write(texts.Count);
            foreach (var t in texts) { w.Write(t.house); w.Write(t.from ?? ""); w.Write(t.body ?? ""); w.Write(t.read); }
        }

        /// <summary>Read a saved list (or, `fromNet`, the host's: keep this phone's own read marks and pop up what's new).</summary>
        public void Read(BinaryReader r, bool fromNet = false)
        {
            int ver = r.ReadInt32();
            if (ver < 1 || ver > Version) return;
            int oldTexts = texts.Count;
            var wasRead = new List<bool>(); foreach (var t in texts) wasRead.Add(t.read);
            var oldIds = new HashSet<int>(); foreach (var j in jobs) oldIds.Add(j.id);
            jobs.Clear(); texts.Clear();
            nextId = r.ReadInt32(); float nt = r.ReadSingle();
            if (!fromNet) nextText = nt;
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                var j = new Job { id = r.ReadInt32(), house = r.ReadInt32(), kind = (JobKind)r.ReadInt32(), state = (JobState)r.ReadInt32(), pay = r.ReadSingle(), depth = r.ReadSingle() };
                if (ver >= 2) { j.shared = r.ReadBoolean(); j.owner = r.ReadUInt64(); j.ownerName = r.ReadString(); }
                if (ver >= 3)
                {
                    j.a = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); j.b = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    j.width = r.ReadSingle(); j.place = r.ReadInt32(); j.damage = r.ReadSingle();
                    int nh = r.ReadInt32(); for (int k = 0; k < nh; k++) j.hits.Add(r.ReadString());
                }
                else if (j.kind == JobKind.Ditch && j.House != null) { j.a = j.House.ditchA; j.b = j.House.ditchB; j.width = 0.6f; }
                jobs.Add(j);
            }
            n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                var t = new Text { house = r.ReadInt32() };
                if (ver >= 2) t.from = r.ReadString(); else t.from = r.ReadBoolean() ? MyName : "";
                t.body = r.ReadString(); t.read = r.ReadBoolean();
                if (fromNet) t.read = i < wasRead.Count ? wasRead[i] : !string.IsNullOrEmpty(t.from) && t.from == MyName;
                texts.Add(t);
            }
            if (fromNet)
                foreach (var j in jobs)
                    if (!oldIds.Contains(j.id) && j.state == JobState.Offered && j.House != null) ToastNew(j.House, j);
            Restake();
            // what got broken shows broken here too
            foreach (var j in jobs) if (j.House != null) foreach (var hid in j.hits) j.House.ShowDamage(hid);
        }

        // ------------------------------------------------------------------ the phone

        public const string PhoneTitle = "Phone";
        int chatWith = -1;   // house index of the open conversation
        int phoneTab;        // 0 = messages, 1 = jobs
        Vector2 listScroll, chatScroll;
        bool scrollToEnd;

        public void OpenPhone(House h)
        {
            chatWith = h != null ? h.index : chatWith;
            if (h != null) phoneTab = 0;
            scrollToEnd = true;
            GameUI.Instance.OpenShop(PhoneTitle, DrawPhone, new Vector2(1060f, 720f));
        }

        /// <summary>The customers you've heard from, newest conversation first.</summary>
        List<int> Conversations()
        {
            var order = new List<int>();
            for (int i = texts.Count - 1; i >= 0; i--) if (!order.Contains(texts[i].house)) order.Add(texts[i].house);
            return order;
        }

        void DrawPhone(Rect area)
        {
            var e = Event.current;
            GameUI.Instance.PanelCorner = $"${Economy.Instance?.Money ?? 0f:N0}";
            const float listW = 340f, gap = 16f;
            // tabs
            int unread = Unread, active = 0; foreach (var j in jobs) if (j.state == JobState.Accepted) active++;
            if (GUI.Button(new Rect(area.x, area.y, listW / 2 - 4, 36), $"Messages{(unread > 0 ? $"  ({unread} new)" : "")}", phoneTab == 0 ? ShopTheme.TabOn : ShopTheme.Tab)) phoneTab = 0;
            if (GUI.Button(new Rect(area.x + listW / 2 + 4, area.y, listW / 2 - 4, 36), $"Jobs  ({active})", phoneTab == 1 ? ShopTheme.TabOn : ShopTheme.Tab)) phoneTab = 1;

            // left: conversations or jobs
            var list = new Rect(area.x, area.y + 46, listW, area.height - 46);
            Card(list, ShopTheme.TexCard);
            var rows = new List<(int house, string title, string sub, bool dot)>();
            if (phoneTab == 0)
                foreach (int hi in Conversations())
                {
                    if (hi < 0 || hi >= House.All.Count) continue;
                    var h = House.All[hi];
                    string last = ""; bool dot = false;
                    foreach (var t in texts) if (t.house == hi) { last = (Mine(t) ? "You: " : !string.IsNullOrEmpty(t.from) ? t.from + ": " : "") + t.body; if (string.IsNullOrEmpty(t.from) && !t.read) dot = true; }
                    rows.Add((hi, $"{h.Owner}", $"{h.Address}  ·  {Snip(last, 60)}", dot));
                }
            else
                foreach (var j in jobs)
                {
                    if (j.state != JobState.Accepted && j.state != JobState.Offered) continue;
                    var h = j.House; if (h == null) continue;
                    string who = WhoHas(j);
                    string sub = j.state == JobState.Offered ? "waiting for an answer"
                        : $"{(who == "you" ? "" : $"{(who == "the crew" ? "shared with the crew" : $"{who}'s job")}  ·  ")}{j.progress * 100f:0}% done{(j.ready ? "  ·  READY" : "")}";
                    rows.Add((j.house, $"{(j.state == JobState.Offered ? "OFFER: " : "")}{j.Title}  ${j.pay:N0}", $"{h.Address}  ·  {sub}", j.ready && IsMine(j)));
                }
            float rowH = 64f;
            var view = new Rect(0, 0, listW - 20, Mathf.Max(list.height - 12, rows.Count * (rowH + 6)));
            listScroll = GUI.BeginScrollView(new Rect(list.x + 6, list.y + 6, listW - 12, list.height - 12), listScroll, view);
            for (int k = 0; k < rows.Count; k++)
            {
                var (hi, title, sub, dot) = rows[k];
                var rr = new Rect(0, k * (rowH + 6), view.width, rowH);
                bool sel = hi == chatWith;
                Card(rr, sel ? ShopTheme.TexCardSel : rr.Contains(e.mousePosition) ? ShopTheme.TexCardHover : ShopTheme.TexTab);
                GUI.Label(new Rect(rr.x + 12, rr.y + 6, rr.width - 40, 22), title, ShopTheme.CardName);
                GUI.Label(new Rect(rr.x + 12, rr.y + 28, rr.width - 24, 34), sub, ShopTheme.DimStyle);
                if (dot) ShopTheme.Fill(new Rect(rr.xMax - 20, rr.y + 12, 10, 10), ShopTheme.Gold);
                if (e.type == EventType.MouseDown && e.button == 0 && rr.Contains(e.mousePosition)) { chatWith = hi; scrollToEnd = true; e.Use(); }
            }
            if (rows.Count == 0) GUI.Label(new Rect(12, 12, view.width - 24, 80), phoneTab == 0 ? "No messages yet. People in town will text you when they need work done." : "No jobs yet. Accept one from your messages.", ShopTheme.Body);
            GUI.EndScrollView();

            // right: the conversation
            var chat = new Rect(area.x + listW + gap, area.y, area.width - listW - gap, area.height);
            Card(chat, ShopTheme.TexCard);
            if (chatWith < 0 || chatWith >= House.All.Count)
            {
                GUI.Label(new Rect(chat.x + 20, chat.y + 20, chat.width - 40, 200),
                    "Your phone. Customers in town text you when they need a gravel driveway or a drainage ditch, with what they'll pay.\n\n" +
                    "Accept a job and the house gets marked on your map (" + Controls.N(GameAction.Map) + ") and staked out. Buy gravel at the landscape supply on the south side of town, haul it over, dump it on the drive and spread it with a skid steer or loader. When it passes, tap Finish to get paid." +
                    (InSession ? "\n\nPlaying with friends: everyone sees the same customers. Take a job yourself (you get all the pay) or share it with the crew (anyone can work it and finish it, and the pay is split between you)." : ""), ShopTheme.Body);
                return;
            }
            var house = House.All[chatWith];
            for (int i = 0; i < texts.Count; i++) if (texts[i].house == chatWith && !texts[i].read) { var t = texts[i]; t.read = true; texts[i] = t; }
            GUI.Label(new Rect(chat.x + 16, chat.y + 10, chat.width - 32, 28), house.Owner, ShopTheme.Heading);
            GUI.Label(new Rect(chat.x + 16, chat.y + 38, chat.width - 32, 20), house.Address, ShopTheme.DimStyle);
            Job job = OpenJobAt(house);
            bool mine = job != null && IsMine(job);
            float actH = job == null ? 0f : job.state == JobState.Offered ? (InSession ? 84f : 64f) : mine ? 132f : 64f;
            var thread = new Rect(chat.x + 10, chat.y + 64, chat.width - 20, chat.height - 74 - actH);

            // bubbles: the customer on the left, you on the right in green, a friend on the right in brown with their name
            bubbleIn ??= new GUIStyle(ShopTheme.Body) { padding = new RectOffset(12, 12, 8, 8) };
            float bw = thread.width * 0.72f, y = 0f;
            var heights = new List<float>();
            string Shown(Text t) => !string.IsNullOrEmpty(t.from) && !Mine(t) ? $"<b>{t.from}:</b> {t.body}" : t.body;
            foreach (var t in texts) if (t.house == chatWith) { float bh = bubbleIn.CalcHeight(new GUIContent(Shown(t)), bw); heights.Add(bh); y += bh + 10f; }
            var tv = new Rect(0, 0, thread.width - 20, Mathf.Max(thread.height, y));
            if (scrollToEnd && e.type == EventType.Repaint) { chatScroll.y = tv.height; scrollToEnd = false; }
            chatScroll = GUI.BeginScrollView(thread, chatScroll, tv);
            y = 0f; int bi = 0;
            foreach (var t in texts)
            {
                if (t.house != chatWith) continue;
                float bh = heights[bi++];
                bool player = !string.IsNullOrEmpty(t.from);
                var br = new Rect(player ? tv.width - bw : 0f, y, bw, bh);
                Card(br, !player ? ShopTheme.TexTab : Mine(t) ? ShopTheme.TexGreen : ShopTheme.TexCardHover);
                GUI.Label(br, Shown(t), bubbleIn);
                y += bh + 10f;
            }
            GUI.EndScrollView();

            // what you can do about it
            if (job == null) return;
            var act = new Rect(chat.x + 12, chat.yMax - actH - 4, chat.width - 24, actH);
            if (job.state == JobState.Offered)
            {
                if (InSession)
                {
                    int crew = CrewSize;
                    GUI.Label(new Rect(act.x, act.y, act.width, 20), "Take it yourself (all the pay is yours), or share it with the crew (anyone can work it, the pay is split).", ShopTheme.DimStyle);
                    if (GUI.Button(new Rect(act.x, act.y + 26, act.width * 0.38f, 48), $"Just me  ${job.pay:N0}", ShopTheme.BuyButton)) Accept(job, false);
                    if (GUI.Button(new Rect(act.x + act.width * 0.40f, act.y + 26, act.width * 0.38f, 48), $"Share with crew  (${job.pay / crew:N0} each)", ShopTheme.BuyButton)) Accept(job, true);
                    if (GUI.Button(new Rect(act.x + act.width * 0.80f, act.y + 26, act.width * 0.20f, 48), "Decline", ShopTheme.PlainButton)) Decline(job);
                }
                else
                {
                    if (GUI.Button(new Rect(act.x, act.y + 8, act.width * 0.62f, 48), $"Accept  ${job.pay:N0}", ShopTheme.BuyButton)) Accept(job);
                    if (GUI.Button(new Rect(act.x + act.width * 0.65f, act.y + 8, act.width * 0.35f, 48), "Decline", ShopTheme.PlainButton)) Decline(job);
                }
            }
            else if (!mine)
            {
                GUI.Label(new Rect(act.x, act.y, act.width, 24), $"{WhoHas(job)} took this job (just them).", ShopTheme.Body);
                GUI.Label(new Rect(act.x, act.y + 26, act.width, 36), job.status, ShopTheme.Small);
            }
            else
            {
                string share = job.shared ? $"  Shared with the crew: ${job.pay / CrewSize:N0} each." : "";
                GUI.Label(new Rect(act.x, act.y, act.width, 42), job.status + share, ShopTheme.Small);
                var bar = new Rect(act.x, act.y + 44, act.width, 14);
                ShopTheme.Fill(bar, new Color(0f, 0f, 0f, 0.35f));
                ShopTheme.Fill(new Rect(bar.x, bar.y, bar.width * job.progress, bar.height), job.ready ? ShopTheme.Green : ShopTheme.Gold);
                GUI.enabled = job.ready;
                if (GUI.Button(new Rect(act.x, act.y + 68, act.width * 0.5f, 48), job.ready ? $"Finish & get paid  ${job.pay:N0}" : "Not done yet", ShopTheme.BuyButton)) Finish(job);
                GUI.enabled = true;
                if (GUI.Button(new Rect(act.x + act.width * 0.52f, act.y + 68, act.width * 0.24f, 48), "Map", ShopTheme.PlainButton)) GameUI.Instance.OpenMap();
                if (GUI.Button(new Rect(act.x + act.width * 0.78f, act.y + 68, act.width * 0.22f, 48), "Cancel job", ShopTheme.PlainButton)) Cancel(job);
            }
        }
        static GUIStyle bubbleIn;

        static string Snip(string s, int n) => s == null ? "" : s.Length <= n ? s : s.Substring(0, n - 1) + "…";

        static readonly Dictionary<Texture2D, GUIStyle> sliced = new();
        static void Card(Rect r, Texture2D t)
        {
            if (Event.current.type != EventType.Repaint || t == null) return;
            if (!sliced.TryGetValue(t, out var st)) sliced[t] = st = new GUIStyle { border = new RectOffset(7, 7, 7, 7), normal = { background = t } };
            st.Draw(r, false, false, false, false);
        }

        // a little badge on the HUD when someone has texted
        GUIStyle badge, beaconLabel, trenchLabel;
        void OnGUI()
        {
            GUI.skin = FPSBase.UI.ShopTheme.Skin;
            var ui = GameUI.Instance;
            if (ui == null || ui.BlocksGameplay) return;
            // the address over each job's beacon
            var cam = Camera.main;
            if (cam != null)
            {
                beaconLabel ??= new GUIStyle(GUI.skin.box) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 0.78f, 0.4f) } };
                trenchLabel ??= new GUIStyle(beaconLabel) { normal = { textColor = new Color(0.6f, 0.88f, 1f) } };
                var used = new List<Rect>();
                Rect Free(Rect r)
                {
                    // move it down until it doesn't cover another label
                    for (int k = 0; k < 8; k++)
                    {
                        bool clash = false;
                        foreach (var u in used) if (u.Overlaps(r)) { clash = true; r.y = u.yMax + 2f; }
                        if (!clash) break;
                    }
                    used.Add(r);
                    return r;
                }
                foreach (var j in jobs)
                {
                    if (j.state != JobState.Accepted || j.House == null || j.kind != JobKind.Ditch) continue;
                    // the trench's size right over it, when you're close
                    Vector3 mid = (j.a + j.b) / 2f + Vector3.up * 1.4f;
                    float d = Vector3.Distance(cam.transform.position, mid);
                    if (d > 45f) continue;
                    Vector3 tp = cam.WorldToScreenPoint(mid);
                    if (tp.z <= 0f) continue;
                    var tc = new GUIContent($"Dig to the blue box: {j.depth:0.0#} m deep  ·  {j.width * 100f:0} cm wide  ·  {j.Length:0.0} m long");
                    var ts = trenchLabel.CalcSize(tc);
                    GUI.Box(Free(new Rect(tp.x - ts.x / 2f - 6f, Screen.height - tp.y - 14f, ts.x + 12f, 28f)), tc, trenchLabel);
                }
                foreach (var j in jobs)
                {
                    if (j.state != JobState.Accepted || j.House == null) continue;
                    Vector3 top = j.House.Front + Vector3.up * 16f;
                    Vector3 sp = cam.WorldToScreenPoint(top);
                    float dist = Vector3.Distance(cam.transform.position, j.House.Front);
                    if (sp.z <= 0f || dist < 8f || dist > 900f) continue;
                    string who = WhoHas(j);
                    var c = new GUIContent($"{j.House.Address}  ·  {j.Title}{(who == "you" ? "" : $" ({who})")}  ·  {dist:0} m");
                    var size = beaconLabel.CalcSize(c);
                    GUI.Box(Free(new Rect(sp.x - size.x / 2f - 6f, Screen.height - sp.y - 14f, size.x + 12f, 28f)), c, beaconLabel);
                }
            }
            int n = Unread;
            int ready = 0; foreach (var j in jobs) if (j.state == JobState.Accepted && j.ready && IsMine(j)) ready++;
            if (n == 0 && ready == 0) return;
            badge ??= new GUIStyle(GUI.skin.box) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 0.85f, 0.35f) } };
            string s = n > 0 ? $"[{Controls.N(GameAction.Phone)}] Phone: {n} new message{(n == 1 ? "" : "s")}" : $"[{Controls.N(GameAction.Phone)}] Phone: job ready to finish";
            var mm = GameUI.MinimapRect;
            GUI.Box(new Rect(mm.x - 4, mm.y - 50, mm.width + 8, 36), s, badge);
        }
    }
}
