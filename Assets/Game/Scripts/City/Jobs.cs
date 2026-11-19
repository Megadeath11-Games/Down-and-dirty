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
        public float progress;   // 0..1, last check
        public string status = "";
        public bool ready;       // good enough to get paid
        public bool tidy;        // neat work: a tip

        public House House => house >= 0 && house < City.House.All.Count ? City.House.All[house] : null;
        public string Title => kind == JobKind.Driveway ? "gravel driveway" : "drainage ditch";
        public bool Open => state == JobState.Offered || state == JobState.Accepted;
    }

    public struct Text { public int house; public bool mine; public string body; public bool read; }

    /// <summary>
    /// The landscaping side of the business: people in town text you when they need work done (a gravel
    /// driveway, a drainage ditch), with what they'll pay. Accept on your phone (P), do the work, and get paid
    /// once it passes. Your phone keeps every customer's messages and your jobs.
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
        public float ditchPerM = 140f, ditchBase = 1000f;
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

        void Awake() { Instance = this; nextText = firstText; }

        public Job OpenJobAt(House h)
        {
            if (h == null) return null;
            foreach (var j in jobs) if (j.house == h.index && j.Open) return j;
            return null;
        }

        public int Unread { get { int n = 0; foreach (var t in texts) if (!t.mine && !t.read) n++; return n; } }

        // ------------------------------------------------------------------ the day

        void Update()
        {
            var ui = GameUI.Instance;
            bool phoneUp = ui != null && ui.PanelTitle == PhoneTitle;
            if (Controls.Pressed(GameAction.Phone) && ui != null && !ui.Paused && PhoneKeyFree())
            {
                if (phoneUp) ui.ClosePanel(); else if (ui.PanelTitle == null) OpenPhone(null);
            }
            if (ui != null && ui.BlocksGameplay && !phoneUp) return; // menus and pause stop the clock
            if (House.All.Count == 0) return;

            if ((nextText -= Time.deltaTime) <= 0f)
            {
                nextText = Mathf.Lerp(textEvery.x, textEvery.y, (float)rng.NextDouble());
                int offers = 0; foreach (var j in jobs) if (j.state == JobState.Offered) offers++;
                if (offers < maxOpenOffers) NewRequest();
            }
            // keep an eye on the jobs you took
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

        /// <summary>Someone in town texts you about a job (or a particular house, for tests).</summary>
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
                j.depth = gravelThick;
                j.pay = Mathf.Round((area * drivewayPerM2 + drivewayBase) / 50f) * 50f;
                float m3 = area * 0.1f;
                string[] t =
                {
                    $"Hi, this is {at.first} at {at.Address}. Our driveway is nothing but ruts and mud. Could you put down fresh gravel? It's about {at.driveway.width:0} x {at.driveway.height:0} m. I can pay ${j.pay:N0}.",
                    $"Hey! {at.first} from {at.Address} here. The neighbours said you do gravel. Our drive needs resurfacing, roughly {area:0} m². Would ${j.pay:N0} work?",
                    $"Hello, {at.first} {at.last} ({at.Address}). We'd like our dirt driveway gravelled before winter. ${j.pay:N0} for the job?",
                };
                msg = t[rng.Next(t.Length)] + $"  (Tip: about {m3:0.#} m³ of gravel covers it 10 cm deep. Dump it on the drive and spread it out evenly.)";
            }
            else
            {
                float len = Vector3.Distance(at.ditchA, at.ditchB);
                j.depth = ditchDepth;
                j.pay = Mathf.Round((len * ditchPerM + ditchBase) / 50f) * 50f;
                string[] t =
                {
                    $"Hello, {at.first} at {at.Address}. Water pools down the side of our lot every time it rains. Could you dig a drainage ditch along there, about {len:0} m long and {ditchDepth:0.#} m deep? I'll pay ${j.pay:N0}.",
                    $"Hi! {at.first} {at.last} here, {at.Address}. Our yard floods. We need a ditch down the side of the property, {len:0} m, {ditchDepth:0.#} m deep. ${j.pay:N0} sound fair?",
                };
                msg = t[rng.Next(t.Length)];
            }
            jobs.Add(j);
            Say(at, false, msg);
            GameUI.Toast($"New text from {at.first} ({at.Address}): {j.Title}, ${j.pay:N0}  —  [{Controls.N(GameAction.Phone)}] phone");
            return j;
        }

        void Say(House h, bool mine, string body) => texts.Add(new Text { house = h.index, mine = mine, body = body, read = mine || (GameUI.Instance != null && GameUI.Instance.PanelTitle == PhoneTitle && chatWith == h.index) });

        public void Accept(Job j)
        {
            if (j == null || j.state != JobState.Offered) return;
            j.state = JobState.Accepted;
            var h = j.House;
            Say(h, true, "Sounds good, I'll be over soon.");
            Say(h, false, j.kind == JobKind.Driveway ? "Great, thanks! I've put stakes at the corners of the drive." : "Great, thank you! I've staked out where it goes, down the side of the lot.");
            Stake(j);
            Check(j);
        }

        public void Decline(Job j)
        {
            if (j == null || j.state != JobState.Offered) return;
            j.state = JobState.Declined;
            Say(j.House, true, "Sorry, I'm booked up right now.");
            Say(j.House, false, "No worries, maybe next time!");
        }

        public void Cancel(Job j)
        {
            if (j == null || j.state != JobState.Accepted) return;
            j.state = JobState.Declined;
            Say(j.House, true, "Sorry, I can't get to it after all.");
            Say(j.House, false, "Oh. Okay then.");
            Unstake(j);
        }

        /// <summary>Get paid for a job that passes.</summary>
        public bool Finish(Job j)
        {
            if (j == null || j.state != JobState.Accepted) return false;
            Check(j);
            if (!j.ready) return false;
            float tip = j.tidy ? Mathf.Round(j.pay * 0.1f / 10f) * 10f : 0f;
            Economy.Instance?.Earn(j.pay + tip);
            j.state = JobState.Paid;
            var h = j.House;
            Say(h, true, "All done! Have a look.");
            Say(h, false, (j.kind == JobKind.Driveway ? "Wow, it looks brand new! " : "Perfect, that should keep the yard dry. ") +
                $"Sent you ${j.pay:N0}" + (tip > 0f ? $", plus ${tip:N0} for doing such a tidy job." : ". Thanks!"));
            GameUI.Toast($"{h.first} paid you ${j.pay + tip:N0} for the {j.Title} at {h.Address}");
            Unstake(j);
            return true;
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
                site.SurveyGravel(shrunk, j.depth, out float cov, out float avg, out float bump);
                j.progress = Mathf.Clamp01(cov / needCoverage);
                j.ready = cov >= needCoverage && bump <= maxBump;
                j.tidy = cov >= 0.98f && bump <= maxBump * 0.5f;
                j.status = $"Gravel covers {cov * 100f:0}% of the drive (needs {needCoverage * 100f:0}%), {avg * 100f:0} cm thick on average." +
                           (bump > maxBump ? $"  There's a pile {bump:0.0} m high: spread it out (no more than {maxBump * 100f:0} cm)." : "");
            }
            else
            {
                var site = DigSite.At(h.ditchA);
                Vector3 a = h.ditchA, b = h.ditchB;
                Vector3 across = Vector3.Cross(Vector3.up, (b - a).normalized) * 0.25f;
                int n = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(a, b) / 0.5f)), ok = 0;
                float deepest = 0f, sum = 0f;
                for (int i = 0; i <= n; i++)
                {
                    Vector3 p = Vector3.Lerp(a, b, i / (float)n);
                    float d = Mathf.Max(site.CutDepth(p), site.CutDepth(p + across), site.CutDepth(p - across));
                    sum += d; deepest = Mathf.Max(deepest, d);
                    if (d >= j.depth - 0.08f) ok++;
                }
                float f = ok / (float)(n + 1);
                j.progress = Mathf.Clamp01(f / needDitch);
                j.ready = f >= needDitch;
                j.tidy = f >= 0.98f;
                j.status = $"{f * 100f:0}% of the ditch is {j.depth:0.#} m deep (needs {needDitch * 100f:0}%), {sum / (n + 1):0.00} m deep on average.";
            }
        }

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
            float G(Vector3 p) => site != null && site.Contains(p) ? site.OriginalY(p) : p.y;
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
            }
            else
            {
                int n = Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(h.ditchA, h.ditchB) / 3f));
                for (int i = 0; i <= n; i++) Post(Vector3.Lerp(h.ditchA, h.ditchB, i / (float)n) + Vector3.Cross(Vector3.up, (h.ditchB - h.ditchA).normalized) * 0.7f);
            }
            var mk = new GameObject($"Job: {h.Address}");
            mk.transform.SetParent(root.transform, false);
            mk.transform.position = h.Front;
            var m = mk.AddComponent<MapMarker>();
            m.label = $"Job: {h.Address}"; m.color = new Color(1f, 0.5f, 0.1f); m.arriveOffset = new Vector3(0f, 0f, 0f);
            stakes[j.id] = root;
        }

        void Unstake(Job j)
        {
            if (stakes.TryGetValue(j.id, out var go) && go != null) Destroy(go);
            stakes.Remove(j.id);
        }

        // ------------------------------------------------------------------ save

        public void Write(BinaryWriter w)
        {
            w.Write(1); // version
            w.Write(nextId); w.Write(nextText);
            w.Write(jobs.Count);
            foreach (var j in jobs) { w.Write(j.id); w.Write(j.house); w.Write((int)j.kind); w.Write((int)j.state); w.Write(j.pay); w.Write(j.depth); }
            w.Write(texts.Count);
            foreach (var t in texts) { w.Write(t.house); w.Write(t.mine); w.Write(t.body ?? ""); w.Write(t.read); }
        }

        public void Read(BinaryReader r)
        {
            if (r.ReadInt32() != 1) return;
            foreach (var j in jobs) Unstake(j);
            jobs.Clear(); texts.Clear();
            nextId = r.ReadInt32(); nextText = r.ReadSingle();
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
                jobs.Add(new Job { id = r.ReadInt32(), house = r.ReadInt32(), kind = (JobKind)r.ReadInt32(), state = (JobState)r.ReadInt32(), pay = r.ReadSingle(), depth = r.ReadSingle() });
            n = r.ReadInt32();
            for (int i = 0; i < n; i++) texts.Add(new Text { house = r.ReadInt32(), mine = r.ReadBoolean(), body = r.ReadString(), read = r.ReadBoolean() });
            foreach (var j in jobs) if (j.state == JobState.Accepted) { Stake(j); Check(j); }
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
                    foreach (var t in texts) if (t.house == hi) { last = (t.mine ? "You: " : "") + t.body; if (!t.mine && !t.read) dot = true; }
                    rows.Add((hi, $"{h.Owner}", $"{h.Address}  ·  {Snip(last, 60)}", dot));
                }
            else
                foreach (var j in jobs)
                {
                    if (j.state != JobState.Accepted && j.state != JobState.Offered) continue;
                    var h = j.House; if (h == null) continue;
                    rows.Add((j.house, $"{(j.state == JobState.Offered ? "OFFER: " : "")}{j.Title}  ${j.pay:N0}", $"{h.Address}  ·  {(j.state == JobState.Accepted ? $"{j.progress * 100f:0}% done{(j.ready ? "  ·  READY" : "")}" : "waiting for your answer")}", j.ready));
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
                GUI.Label(new Rect(chat.x + 20, chat.y + 20, chat.width - 40, 120),
                    "Your phone. Customers in town text you when they need a gravel driveway or a drainage ditch, with what they'll pay.\n\n" +
                    "Accept a job and the house gets marked on your map (" + Controls.N(GameAction.Map) + ") and staked out. Buy gravel at the landscape supply on the south side of town, haul it over, dump it on the drive and spread it with a skid steer or loader. When it passes, tap Finish to get paid.", ShopTheme.Body);
                return;
            }
            var house = House.All[chatWith];
            for (int i = 0; i < texts.Count; i++) if (texts[i].house == chatWith && !texts[i].read) { var t = texts[i]; t.read = true; texts[i] = t; }
            GUI.Label(new Rect(chat.x + 16, chat.y + 10, chat.width - 32, 28), house.Owner, ShopTheme.Heading);
            GUI.Label(new Rect(chat.x + 16, chat.y + 38, chat.width - 32, 20), house.Address, ShopTheme.DimStyle);
            Job job = OpenJobAt(house);
            float actH = job == null ? 0f : job.state == JobState.Offered ? 64f : 132f;
            var thread = new Rect(chat.x + 10, chat.y + 64, chat.width - 20, chat.height - 74 - actH);

            // bubbles
            bubbleIn ??= new GUIStyle(ShopTheme.Body) { padding = new RectOffset(12, 12, 8, 8) };
            float bw = thread.width * 0.72f, y = 0f;
            var heights = new List<float>();
            foreach (var t in texts) if (t.house == chatWith) { float bh = bubbleIn.CalcHeight(new GUIContent(t.body), bw); heights.Add(bh); y += bh + 10f; }
            var tv = new Rect(0, 0, thread.width - 20, Mathf.Max(thread.height, y));
            if (scrollToEnd && e.type == EventType.Repaint) { chatScroll.y = tv.height; scrollToEnd = false; }
            chatScroll = GUI.BeginScrollView(thread, chatScroll, tv);
            y = 0f; int bi = 0;
            foreach (var t in texts)
            {
                if (t.house != chatWith) continue;
                float bh = heights[bi++];
                var br = new Rect(t.mine ? tv.width - bw : 0f, y, bw, bh);
                Card(br, t.mine ? ShopTheme.TexGreen : ShopTheme.TexTab);
                GUI.Label(br, t.body, bubbleIn);
                y += bh + 10f;
            }
            GUI.EndScrollView();

            // what you can do about it
            if (job == null) return;
            var act = new Rect(chat.x + 12, chat.yMax - actH - 4, chat.width - 24, actH);
            if (job.state == JobState.Offered)
            {
                if (GUI.Button(new Rect(act.x, act.y + 8, act.width * 0.62f, 48), $"Accept  ${job.pay:N0}", ShopTheme.BuyButton)) Accept(job);
                if (GUI.Button(new Rect(act.x + act.width * 0.65f, act.y + 8, act.width * 0.35f, 48), "Decline", ShopTheme.PlainButton)) Decline(job);
            }
            else
            {
                GUI.Label(new Rect(act.x, act.y, act.width, 42), job.status, ShopTheme.Small);
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
        GUIStyle badge;
        void OnGUI()
        {
            var ui = GameUI.Instance;
            if (ui == null || ui.BlocksGameplay) return;
            int n = Unread;
            int ready = 0; foreach (var j in jobs) if (j.state == JobState.Accepted && j.ready) ready++;
            if (n == 0 && ready == 0) return;
            badge ??= new GUIStyle(GUI.skin.box) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 0.85f, 0.35f) } };
            string s = n > 0 ? $"[{Controls.N(GameAction.Phone)}] Phone: {n} new message{(n == 1 ? "" : "s")}" : $"[{Controls.N(GameAction.Phone)}] Phone: a job is ready to finish";
            GUI.Box(new Rect(Screen.width - 330, Screen.height - 120, 310, 36), s, badge);
        }
    }
}
