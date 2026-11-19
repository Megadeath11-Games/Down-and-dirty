using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using FPSBase.Core;
using FPSBase.Mining;
using FPSBase.Net;
using FPSBase.Vehicles;
using UnityEngine;

namespace FPSBase.DebugTools
{
    /// <summary>
    /// A scripted player for testing multiplayer with two copies of the game on one PC (over the local test link):
    /// "-nettest host 27960 -netbot <folder>" on one, "-nettest join 27960 -netbot <folder>" on the other. Both run
    /// the same script; one player does something, the other checks that it shows up the same on their game.
    /// They take turns through little files in the folder, and each writes its results to host.txt / join.txt.
    /// </summary>
    public class NetBot : MonoBehaviour
    {
        string dir, role, other;
        bool host;
        int crew = 2;
        readonly StringBuilder log = new();
        int fails, passes;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-netbot");
            if (i < 0 || i + 1 >= args.Length || !NetSession.LanMode || FindFirstObjectByType<NetBot>() != null) return;
            var go = new GameObject("Net Bot");
            DontDestroyOnLoad(go);
            var b = go.AddComponent<NetBot>();
            b.dir = args[i + 1];
            b.role = NetSession.LanRole;
            if (i + 2 < args.Length && !args[i + 2].StartsWith("-")) b.role = args[i + 2];
            if (i + 3 < args.Length && int.TryParse(args[i + 3], out int c)) b.crew = c;
            b.host = b.role == "host";
            b.other = b.host ? "join" : "host";
        }

        void Start()
        {
            Application.runInBackground = true;
            AudioListener.volume = 0f;
            Directory.CreateDirectory(dir);
            StartCoroutine(Run());
        }

        // ------------------------------------------------------------------ taking turns, sharing numbers

        void Mark(string step) => File.WriteAllText(Path.Combine(dir, $"{role}.{step}"), DateTime.Now.ToString("O"));
        IEnumerator Wait(string step, float timeout = 90f)
        {
            string f = Path.Combine(dir, $"{other}.{step}");
            float t0 = Time.realtimeSinceStartup;
            while (!File.Exists(f))
            {
                if (Time.realtimeSinceStartup - t0 > timeout) { Fail($"waited too long for the other player ({step})"); yield break; }
                yield return null;
            }
        }
        /// <summary>Both players reach the same point.</summary>
        IEnumerator Sync(string step) { Mark(step); yield return Wait(step); }

        void Share(string key, string value) => File.WriteAllText(Path.Combine(dir, $"{role}.val.{key}"), value);
        string got;
        IEnumerator Get(string key, float timeout = 60f)
        {
            got = null;
            string f = Path.Combine(dir, $"{other}.val.{key}");
            float t0 = Time.realtimeSinceStartup;
            while (!File.Exists(f)) { if (Time.realtimeSinceStartup - t0 > timeout) { Fail($"never got {key}"); yield break; } yield return null; }
            for (int k = 0; k < 10 && string.IsNullOrEmpty(got); k++) { try { got = File.ReadAllText(f); } catch { } if (string.IsNullOrEmpty(got)) yield return null; }
        }
        static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
        static float P(string s) => float.Parse(s, CultureInfo.InvariantCulture);
        static string V(Vector3 v) => $"{F(v.x)},{F(v.y)},{F(v.z)}";
        static Vector3 PV(string s) { var a = s.Split(','); return new Vector3(P(a[0]), P(a[1]), P(a[2])); }

        void Pass(string what) { passes++; log.AppendLine("PASS  " + what); Flush(); }
        void Fail(string what) { fails++; log.AppendLine("FAIL  " + what); Flush(); }
        void Info(string what) { log.AppendLine("INFO  " + what); Flush(); }
        void Flush() { try { File.WriteAllText(Path.Combine(dir, role + ".txt"), log.ToString()); } catch { } }
        void Check(bool ok, string what) { if (ok) Pass(what); else Fail(what); }

        /// <summary>Wait for something to come true (it has to cross the network first).</summary>
        IEnumerator Expect(Func<bool> cond, float seconds, string what, Func<string> detail = null)
        {
            float t0 = Time.realtimeSinceStartup;
            bool ok = false;
            while (Time.realtimeSinceStartup - t0 < seconds)
            {
                bool c = false;
                try { c = cond(); } catch { }
                if (c) { ok = true; break; }
                yield return null;
            }
            string d = "";
            if (detail != null) { try { d = "  [" + detail() + "]"; } catch (Exception e) { d = "  [" + e.Message + "]"; } }
            Check(ok, $"{what}{d}  ({Time.realtimeSinceStartup - t0:0.0} s)");
        }

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        static IEnumerator Seconds(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }

        static VehicleBase ById(int id) { foreach (var v in VehicleBase.All) if (v != null && v.NetId == id) return v; return null; }
        static Trailer TrailerById(int id) { foreach (var t in Trailer.All) if (t != null && t.NetId == id) return t; return null; }
        static int Listing(VehicleDealer d, string name) { for (int i = 0; i < d.listings.Length; i++) if (d.listings[i].name.Contains(name)) return i; return -1; }
        VehicleBase Newest(System.Type type)
        {
            VehicleBase best = null;
            foreach (var v in VehicleBase.All) if (v != null && type.IsInstanceOfType(v) && v.NetId >= NetSession.SpawnIdBase && !v.NetRemote) best = v;
            return best;
        }

        /// <summary>The joints' angles (boom, stick, bucket, swing, blade, box): the same on both games when synced.</summary>
        static List<float> Joints(VehicleBase v)
        {
            var l = new List<float>();
            foreach (var t in v.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (!(n.Contains("Pivot") || n.Contains("Upper") || n.Contains("Hinge") || n.Contains("Circle"))) continue;
                var e = t.localEulerAngles; l.Add(e.x); l.Add(e.y); l.Add(e.z);
            }
            return l;
        }
        static float JointDiff(List<float> a, List<float> b)
        {
            if (a.Count != b.Count) return 999f;
            float w = 0f; for (int i = 0; i < a.Count; i++) w = Mathf.Max(w, Mathf.Abs(Mathf.DeltaAngle(a[i], b[i]))); return w;
        }
        static string JS(List<float> l) { var sb = new StringBuilder(); foreach (var f in l) sb.Append(F(f)).Append(';'); return sb.ToString(); }
        static List<float> PJ(string s) { var l = new List<float>(); foreach (var p in s.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)) l.Add(P(p)); return l; }

        PlayerInteractor Me => FindFirstObjectByType<PlayerInteractor>(FindObjectsInactive.Include);

        // ------------------------------------------------------------------ the script

        IEnumerator Run()
        {
            Info($"{role} starting (frame {Time.frameCount}), {crew} players, connection: {LanLink.SimInfo}");
            if (role == "join2") { yield return Observer(); yield break; }
            yield return Frames(120);
            var net = NetSession.Instance;
            var site = DigSite.Instance;
            var dealer = FindFirstObjectByType<VehicleDealer>();
            var jobs = FPSBase.City.Jobs.Instance;
            var eco = Economy.Instance;
            if (net == null || site == null || dealer == null || jobs == null || eco == null) { Fail("the world is there (net, ground, dealer, jobs, money)"); yield return End(); yield break; }
            DigSite town = null; foreach (var g in DigSite.All) if (g != site) town = g;
            // (a friend leaving goes back to the main menu: a fresh world, so everything is found again)
            void Refresh()
            {
                site = DigSite.Instance; dealer = FindFirstObjectByType<VehicleDealer>(); jobs = FPSBase.City.Jobs.Instance; eco = Economy.Instance;
                town = null; foreach (var g in DigSite.All) if (g != site) town = g;
            }

            MakePad();
            // ---------------- before anyone joins: the host has been playing a while
            Vector3 p1 = new Vector3(40f, 0f, -170f);
            int pickupL = Listing(dealer, "GMT400"), exL = Listing(dealer, "20 t"), truckL = Listing(dealer, "Single-Axle Dump"), tiltL = Listing(dealer, "Tilt-Deck"), skidL = Listing(dealer, "Skid");
            if (exL < 0) exL = Listing(dealer, "Excavator");
            Info($"listings: pickup {pickupL}, excavator {exL}, dump truck {truckL}, tilt trailer {tiltL}, skid {skidL}");
            if (host)
            {
                // dig a lot (the friend joining gets all of it)
                for (int i = 0; i < 400; i++)
                {
                    Vector3 q = p1 + new Vector3((i % 20) * 0.7f, 0f, (i / 20) * 0.7f);
                    DigSite.DigAnywhere(new Vector3(q.x, site.SurfaceY(q) - 0.6f, q.z), 0.6f, 2f);
                }
                site.Flush(99999);
                Share("p1", F(site.SurfaceY(p1 + new Vector3(5f, 0f, 5f))));
                dealer.DevDeliver(pickupL); yield return Frames(5);
                var pk = Newest(typeof(DumpPickup)); Share("pickup", pk != null ? pk.NetId.ToString() : "0");
                dealer.DevDeliver(exL); yield return Frames(5);
                var ex = Newest(typeof(MiniExcavator)); Share("ex", ex != null ? ex.NetId.ToString() : "0");
                var j0 = jobs.NewRequest(FPSBase.City.House.All[3], FPSBase.City.JobKind.Driveway);
                Share("job0", j0 != null ? j0.id.ToString() : "0");
                net.Host();
                yield return Expect(() => NetSession.InSession && net.IsHost, 5f, "the host's game is up");
                Mark("hosted");
            }
            else
            {
                yield return Wait("hosted");
                net.JoinLan();
                yield return Expect(() => NetSession.InSession && !net.IsHost, 20f, "joined the host's game");
            }
            yield return Sync("in");
            if (crew > 2) yield return Wait2("in");
            yield return Expect(() => net.CrewSize == crew, 15f, $"all {crew} players are in the game", () => $"crew {net.CrewSize}");
            yield return Expect(() => new List<(string, Vector3, float)>(net.OtherPlayers()).Count == crew - 1, 10f, "you see the other players", () => $"{new List<(string, Vector3, float)>(net.OtherPlayers()).Count} others");
            yield return Expect(() => GameUI.Instance != null && !GameUI.Instance.InMainMenu, 10f, "the main menu closed into the world");

            // ---------------- what a friend gets when they join
            if (!host)
            {
                yield return Get("p1"); float hp1 = got != null ? P(got) : 0f;
                yield return Expect(() => Mathf.Abs(site.SurfaceY(p1 + new Vector3(5f, 0f, 5f)) - hp1) < 0.05f, 20f, "the ground the host dug before you joined is dug on your game",
                    () => $"{site.SurfaceY(p1 + new Vector3(5f, 0f, 5f)):0.00} vs host {hp1:0.00}");
                yield return Get("pickup"); int pid = int.Parse(got ?? "0");
                yield return Get("ex"); int eid = int.Parse(got ?? "0");
                yield return Expect(() => ById(pid) != null && ById(pid).NetRemote && ById(eid) != null && ById(eid).NetRemote, 10f, "the machines the host bought before you joined are there (run by the host)");
                yield return Get("job0"); int jid = int.Parse(got ?? "0");
                yield return Expect(() => jobs.ById(jid) != null && jobs.ById(jid).state == FPSBase.City.JobState.Offered, 10f, "the host's customers' texts are on your phone");
            }
            yield return Sync("joined-checked");

            // ---------------- walking about
            {
                Vector3 spot = host ? new Vector3(20f, 0f, -60f) : new Vector3(26f, 0f, -60f);
                spot = VehicleDealer.GroundAt(spot) + Vector3.up * 0.2f;
                Me.PlaceAt(spot, host ? 90f : 270f);
                yield return Frames(30);
                Share("me", V(Me.transform.position));
                yield return Get("me"); Vector3 theirs = got != null ? PV(got) : Vector3.zero;
                yield return Expect(() =>
                {
                    foreach (var o in net.OtherPlayers()) if (Vector3.Distance(o.pos, theirs) < 1.0f) return true;
                    return false;
                }, 6f, "the other player stands where they are on their own screen", () => { var s = ""; foreach (var o in net.OtherPlayers()) s += $"{o.name} {o.pos} vs {theirs}"; return s; });
            }
            yield return Sync("walked");

            // ---------------- the host drives the pickup; the friend sees it
            int pickupId = int.Parse(File.ReadAllText(Path.Combine(dir, "host.val.pickup")));
            yield return DriveAndWatch(pickupId, host, "host");
            // ---------------- and then the friend takes it
            yield return DriveAndWatch(pickupId, !host, "friend");

            // ---------------- the friend works the host's excavator
            {
                int exId = int.Parse(File.ReadAllText(Path.Combine(dir, "host.val.ex")));
                var ex = ById(exId) as MiniExcavator;
                if (!host && ex != null)
                {
                    ex.Interact(Me);
                    yield return Frames(20);
                    for (int i = 0; i < 60; i++) { ex.SetPose(28f, -55f, -35f, 50f); ex.bucketLoad = MaterialLoad.Mix(ex.bucketCapacity * 0.6f, 2f, 0f, 0f, 0f); yield return null; }
                    yield return Seconds(1f);
                    Share("exj", JS(Joints(ex)));
                    Share("exl", F(ex.bucketLoad.volume));
                }
                yield return Sync("ex-posed");
                if (host && ex != null)
                {
                    yield return Expect(() => ex.NetRemote && ex.NetRemoteDriven, 6f, "your friend is in your excavator (it's theirs to run now)");
                    yield return Get("exj"); var theirJ = PJ(got ?? "");
                    yield return Get("exl"); float theirL = P(got ?? "0");
                    yield return Expect(() => JointDiff(Joints(ex), theirJ) < 2f, 6f, "their arm, bucket and swing show the same on your game", () => $"worst {JointDiff(Joints(ex), theirJ):0.0}°");
                    yield return Expect(() => Mathf.Abs(ex.bucketLoad.volume - theirL) < 0.02f, 6f, "and the dirt in their bucket", () => $"{ex.bucketLoad.volume:0.00} vs {theirL:0.00}");
                }
                yield return Sync("ex-checked");
                if (!host && ex != null) { ex.Exit(); yield return Frames(20); }
                yield return Sync("ex-left");
                if (host && ex != null) yield return Expect(() => !ex.NetRemoteDriven, 6f, "they got out: nobody's driving it");
            }

            // ---------------- digging shows on both games (the mine and the town)
            {
                Vector3 pd = host ? new Vector3(60f, 0f, -160f) : new Vector3(70f, 0f, -160f);
                for (int i = 0; i < 12; i++) DigSite.DigAnywhere(new Vector3(pd.x + i * 0.4f, site.SurfaceY(pd) - 0.8f, pd.z), 0.6f, 3f);
                site.Flush(99999);
                Share("dig", F(site.SurfaceY(pd + Vector3.right * 2f)));
                Vector3 pt = Vector3.zero;
                if (town != null)
                {
                    var h = FPSBase.City.House.All[host ? 10 : 11];
                    pt = h.Local(host ? 8f : -8f, 3f);
                    for (int i = 0; i < 6; i++) town.Dig(new Vector3(pt.x, town.OriginalY(pt) - 0.6f, pt.z + i * 0.4f), 0.4f, 2f);
                    town.Flush(99999);
                    Share("tdig", F(town.SurfaceY(pt + Vector3.forward * 1f)));
                }
                yield return Get("dig"); float theirs = P(got ?? "0");
                Vector3 po = host ? new Vector3(70f, 0f, -160f) : new Vector3(60f, 0f, -160f);
                yield return Expect(() => Mathf.Abs(site.SurfaceY(po + Vector3.right * 2f) - theirs) < 0.05f, 8f, "the hole the other player dug in the mine is on your game", () => $"{site.SurfaceY(po + Vector3.right * 2f):0.00} vs {theirs:0.00}");
                if (town != null)
                {
                    yield return Get("tdig"); float tt = P(got ?? "0");
                    var h2 = FPSBase.City.House.All[host ? 11 : 10];
                    Vector3 pt2 = h2.Local(host ? -8f : 8f, 3f);
                    yield return Expect(() => Mathf.Abs(town.SurfaceY(pt2 + Vector3.forward * 1f) - tt) < 0.05f, 8f, "and the one in a town yard", () => $"{town.SurfaceY(pt2 + Vector3.forward * 1f):0.00} vs {tt:0.00}");
                }
            }
            yield return Sync("dug");

            // ---------------- the friend buys a machine; it shows up for the host
            if (!host)
            {
                dealer.DevDeliver(truckL); yield return Frames(5);
                var tk = Newest(typeof(DumpPickup));
                Share("truck", tk != null ? tk.NetId.ToString() : "0");
                Share("truckpos", tk != null ? V(tk.transform.position) : "0,0,0");
            }
            else
            {
                yield return Get("truck"); int tid = int.Parse(got ?? "0");
                yield return Get("truckpos"); Vector3 tp = PV(got ?? "0,0,0");
                yield return Expect(() => ById(tid) != null && ById(tid).NetRemote, 6f, "the dump truck your friend bought is on your game (they run it)");
                yield return Expect(() => ById(tid) != null && Vector3.Distance(ById(tid).transform.position, tp) < 1.5f, 6f, "where they got it", () => ById(tid) != null ? $"{Vector3.Distance(ById(tid).transform.position, tp):0.00} m off" : "none");
            }
            yield return Sync("bought");

            // ---------------- the friend buys the mining dozer (a heavy machine: it goes to the heavy-equipment yard)
            {
                int bigL = Listing(dealer, "Mining Dozer");
                if (!host)
                {
                    Info($"buying the mining dozer (listing {bigL})");
                    float t0 = Time.realtimeSinceStartup;
                    dealer.DevDeliver(bigL);
                    Info($"bought it in {(Time.realtimeSinceStartup - t0) * 1000f:0} ms");
                    yield return Frames(30);
                    var bd = Newest(typeof(Bulldozer));
                    Check(bd != null && bd.displayName.StartsWith("Mining Dozer"), "you buy the mining dozer and it's delivered");
                    Share("bigdozer", bd != null ? bd.NetId.ToString() : "0");
                    yield return Seconds(3f);
                    Check(bd != null && bd.transform.position.y > -5f, $"and it sits on the ground ({(bd != null ? bd.transform.position.y : 0f):0.0} m)");
                }
                else
                {
                    yield return Get("bigdozer"); int bid = int.Parse(got ?? "0");
                    yield return Expect(() => ById(bid) != null && ById(bid).NetRemote, 8f, "the mining dozer your friend bought is on your game");
                }
                yield return Sync("bigdozer-done");
            }

            // ---------------- dirt into a machine the other player runs: the host loads the friend's truck
            {
                int tid = int.Parse(File.ReadAllText(Path.Combine(dir, "join.val.truck")));
                var tk = ById(tid) as DumpPickup;
                if (!host && tk != null) { Share("load0", F(tk.load.volume)); }
                yield return Sync("load0");
                if (host && tk != null) { for (int i = 0; i < 4; i++) tk.Receive(MaterialLoad.Mix(0.5f, 2f, 0f, 0f, 0f)); }
                yield return Sync("loaded");
                if (!host && tk != null)
                {
                    float l0 = P(File.ReadAllText(Path.Combine(dir, "join.val.load0")));
                    yield return Expect(() => tk.load.volume > l0 + 1.9f, 6f, "the host's 2 m³ lands in your truck's box", () => $"{tk.load.volume:0.00} m³");
                    yield return Frames(20);
                    Share("load1", F(tk.load.volume));
                }
                yield return Sync("load1");
                if (host && tk != null)
                {
                    float l1 = P(File.ReadAllText(Path.Combine(dir, "join.val.load1")));
                    yield return Expect(() => Mathf.Abs(tk.load.volume - l1) < 0.05f, 6f, "and your copy shows it full too", () => $"{tk.load.volume:0.00} vs {l1:0.00}");
                }
            }
            yield return Sync("loads");

            // ---------------- the wash plant: a friend's load goes in the host's plant; everyone sees the hopper
            {
                var plants = NetSession.Plants();
                var plant = plants.Count > 0 ? plants[0] : null;
                if (plant == null) Fail("a wash plant");
                else
                {
                    if (host) Share("hop0", F(plant.hopper.volume));
                    yield return Sync("hop0");
                    if (!host) plant.Receive(MaterialLoad.Mix(1.0f, 5f, 0f, 0f, 0f));
                    yield return Sync("hop-dumped");
                    if (host)
                    {
                        float h0 = P(File.ReadAllText(Path.Combine(dir, "host.val.hop0")));
                        yield return Expect(() => plant.hopper.volume > h0 + 0.5f, 6f, "the dirt your friend dumps goes in your wash plant's hopper", () => $"{plant.hopper.volume:0.00} m³ (was {h0:0.00})");
                    }
                    else
                    {
                        yield return Expect(() => plant.hopper.volume > 0.3f, 6f, "the hopper shows it on your game too (the host's plant washes it)", () => $"{plant.hopper.volume:0.00}");
                    }
                }
            }
            yield return Sync("plant");

            // ---------------- the friend cleans up the host's sluice: the gold goes to the friend
            {
                var plants = NetSession.Plants();
                var plant = plants.Count > 0 ? plants[0] : null;
                if (plant != null)
                {
                    if (host) plant.sluiceGold = 3.25f;
                    yield return Sync("gold-in");
                    if (!host)
                    {
                        yield return Expect(() => Mathf.Abs(plant.sluiceGold - 3.25f) < 0.01f, 6f, "the host's sluice shows its gold on your game", () => $"{plant.sluiceGold:0.00} g");
                        float g0 = eco.GoldGrams;
                        WashPlant.NetAction?.Invoke(plant, 0); plant.sluiceGold = 0f; // (what the Clean up button does on a friend's game)
                        yield return Expect(() => eco.GoldGrams > g0 + 3.2f, 6f, "you clean it up and get the gold", () => $"{eco.GoldGrams - g0:0.00} g");
                    }
                    yield return Sync("gold-taken");
                    yield return Expect(() => plant.sluiceGold < 0.01f, 6f, "the sluice is empty on both games", () => $"{plant.sluiceGold:0.00} g");
                }
            }
            yield return Sync("gold");

            // ---------------- the friend dumps their truck by the dealer: the dirt is there on everyone's game
            {
                int tid = int.Parse(File.ReadAllText(Path.Combine(dir, "join.val.truck")));
                var tk = ById(tid) as DumpPickup;
                if (!host && tk != null)
                {
                    tk.gateSetting = 0;
                    for (float t = 0f; t < 8f; t += Time.deltaTime) { tk.SetTilt(Mathf.Min(tk.maxTilt, t * 15f)); yield return null; }
                    for (float t = 0f; t < 3f; t += Time.deltaTime) { tk.SetTilt(Mathf.Max(0f, tk.maxTilt - t * 25f)); yield return null; }
                    Info($"dumped: {tk.load.volume:0.00} m³ left in the box");
                    yield return Seconds(4f);
                }
                yield return Sync("dumped");
                yield return Seconds(1.5f);
                float pv = 0f; foreach (var pl in DirtPile.All) if (pl != null) pv += pl.load.volume;
                Share("piles", F(pv) + "|" + DirtPile.All.Count);
                Share("ground-dump", GroundNow());
                yield return Sync("piles");
                var mine = File.ReadAllText(Path.Combine(dir, $"{role}.val.piles")).Split('|');
                var theirs = File.ReadAllText(Path.Combine(dir, $"{other}.val.piles")).Split('|');
                Info($"dirt piles: here {mine[0]} m³ in {mine[1]}, there {theirs[0]} m³ in {theirs[1]}");
                Check(Mathf.Abs(P(mine[0]) - P(theirs[0])) < 0.05f && mine[1] == theirs[1], $"the dumped dirt piles match on both games ({mine[0]} vs {theirs[0]} m³)");
                string gd = GroundDiff(File.ReadAllText(Path.Combine(dir, $"{role}.val.ground-dump")), File.ReadAllText(Path.Combine(dir, $"{other}.val.ground-dump")));
                Check(gd == null, $"and the ground where it landed{(gd != null ? ": " + gd : "")}");
                // the host digs a pile back up
                if (host)
                {
                    DirtPile biggest = null; foreach (var pl in DirtPile.All) if (pl != null && (biggest == null || pl.load.volume > biggest.load.volume)) biggest = pl;
                    if (biggest != null) { biggest.Dig(biggest.load.volume * 0.5f); Share("dugpile", biggest.NetId + "|" + F(biggest.load.volume)); }
                    else Share("dugpile", "0|0");
                }
                yield return Sync("dugpile");
                if (!host)
                {
                    var dp = File.ReadAllText(Path.Combine(dir, "host.val.dugpile")).Split('|');
                    int pid = int.Parse(dp[0]); float vol = P(dp[1]);
                    if (pid != 0) yield return Expect(() => DirtPile.ById(pid) != null && Mathf.Abs(DirtPile.ById(pid).load.volume - vol) < 0.02f, 6f, "the host digs half a pile back up: it's smaller on your game too", () => DirtPile.ById(pid) != null ? $"{DirtPile.ById(pid).load.volume:0.00} vs {vol:0.00}" : "gone");
                }
            }
            yield return Sync("dump-done");

            // ---------------- a crew job: shared, a knocked-over mailbox, finished, the pay split
            {
                var houses = FPSBase.City.House.All;
                var hh = houses[6];
                if (host)
                {
                    var j = jobs.NewRequest(hh, FPSBase.City.JobKind.Ditch);
                    Share("job", j != null ? j.id.ToString() : "0");
                    Share("money0", F(eco.Money));
                }
                else Share("money0", F(eco.Money));
                yield return Sync("job-offered");
                int jid = int.Parse(File.ReadAllText(Path.Combine(dir, "host.val.job")));
                if (!host)
                {
                    yield return Expect(() => jobs.ById(jid) != null, 6f, "the new customer's text shows on your phone");
                    var j = jobs.ById(jid);
                    if (j != null) jobs.Accept(j, true);
                    yield return Expect(() => jobs.ById(jid) != null && jobs.ById(jid).state == FPSBase.City.JobState.Accepted && jobs.ById(jid).shared, 6f, "you take it for the crew (the host says it's taken)");
                }
                yield return Sync("job-accepted");
                var jj = jobs.ById(jid);
                if (host)
                {
                    yield return Expect(() => jj != null && jj.state == FPSBase.City.JobState.Accepted && jj.shared && jj.owner != net.MyId, 4f, "your friend took the job for the crew", () => jj != null ? $"{jj.state}, shared {jj.shared}, by {jj.ownerName}" : "gone");
                    yield return Expect(() => GameObject.Find($"Job stakes: {hh.Address}") != null, 4f, "it's marked out on your game too");
                }
                else
                {
                    yield return Expect(() => GameObject.Find($"Job stakes: {hh.Address}") != null, 4f, "it's marked out on your game");
                    // the friend bumps the mailbox
                    var box = hh.Part("mailbox");
                    if (box != null) jobs.Damage(hh, box, "Dump Truck");
                }
                yield return Sync("bumped");
                Quaternion boxRot = hh.mailbox != null ? hh.mailbox.localRotation : Quaternion.identity;
                yield return Expect(() => jobs.ById(jid) != null && jobs.ById(jid).hits.Contains("mailbox") && jobs.ById(jid).damage > 0.05f, 6f, "the knocked-over mailbox is on the job's bill (both games)", () => jobs.ById(jid) != null ? $"damage {jobs.ById(jid).damage:P0}" : "gone");
                yield return Expect(() => hh.mailbox != null && hh.Part("mailbox") != null && hh.Part("mailbox").Broken, 6f, "and the mailbox lies knocked over");
                // the friend digs the trench (the host's game checks it)
                if (!host && jj != null) DigTrench(town, jj);
                yield return Sync("trenched");
                if (host && jj != null)
                    yield return Expect(() => { jobs.Check(jj); return jj.ready; }, 10f, "the trench your friend dug passes on your game (the one that keeps the jobs)", () => jj.status);
                yield return Sync("ready");
                if (!host && jj != null)
                {
                    yield return Expect(() => jobs.ById(jid).ready, 6f, "your phone says it's ready to finish");
                    jobs.Finish(jobs.ById(jid));
                }
                yield return Sync("finished");
                float m0 = P(File.ReadAllText(Path.Combine(dir, $"{role}.val.money0")));
                var fj = jobs.ById(jid);
                float pay = fj != null ? fj.pay : 0f, dmg = fj != null ? fj.damage : 0f;
                float share = pay * (1f - dmg) / crew;
                yield return Expect(() => jobs.ById(jid) != null && jobs.ById(jid).state == FPSBase.City.JobState.Paid, 6f, "the job is paid");
                yield return Expect(() => Mathf.Abs(eco.Money - m0 - share) < 2f || eco.Unlimited, 6f, $"you get your share of the pay (1/{crew}), less the damage", () => $"got ${eco.Money - m0:N0}, expected ${share:N0} of ${pay:N0}");
            }
            yield return Sync("job-done");

            // ---------------- towing: the host hitches a trailer to their dump truck and drives off
            {
                if (host)
                {
                    dealer.DevDeliver(truckL); yield return Frames(5);
                    var tk = Newest(typeof(DumpPickup)) as DumpPickup;
                    dealer.DevDeliver(tiltL); yield return Frames(5);
                    Trailer tr = null; foreach (var t in Trailer.All) if (t != null && t.NetId >= NetSession.SpawnIdBase && t.NetMine) tr = t;
                    if (tk == null || tr == null) Fail("bought a dump truck and a tilt trailer to tow");
                    else
                    {
                        var trb = tk.GetComponent<Rigidbody>();
                        trb.isKinematic = true; tk.transform.SetPositionAndRotation(new Vector3(-150f, 401f, 120f), Quaternion.identity); trb.position = tk.transform.position; trb.rotation = Quaternion.identity; trb.isKinematic = false;
                        Hitch hitch = null; foreach (var hc in tk.GetComponentsInChildren<Hitch>()) if (hc.type == HitchType.Pintle) hitch = hc;
                        tr.Body.isKinematic = true;
                        tr.transform.SetPositionAndRotation(tk.transform.position - Vector3.forward * 7f, Quaternion.identity);
                        tr.transform.position += hitch.transform.position - tr.couplerPoint.position; tr.Body.position = tr.transform.position;
                        tr.Body.isKinematic = false;
                        Physics.SyncTransforms();
                        tr.Couple(hitch, true);
                        yield return Frames(60);
                        tk.Interact(Me);
                        yield return Frames(10);
                        Vector3 t0p = tr.transform.position;
                        Controls.TestHeld[GameAction.DriveForward] = 0.6f;
                        yield return Seconds(5f);
                        Controls.TestHeld.Clear();
                        yield return Seconds(4f);
                        Info($"towed the trailer {Vector3.Distance(t0p, tr.transform.position):0.0} m");
                        Check(Vector3.Distance(t0p, tr.transform.position) > 5f, "the host towed the trailer away");
                        Share("tow", tr.NetId.ToString());
                        Share("towpos", V(tr.transform.position));
                        Share("towtruck", tk.NetId.ToString());
                        Share("towtruckpos", V(tk.transform.position));
                    }
                }
                yield return Sync("towed");
                if (!host)
                {
                    int tid = int.Parse(File.ReadAllText(Path.Combine(dir, "host.val.tow")));
                    Vector3 tp = PV(File.ReadAllText(Path.Combine(dir, "host.val.towpos")));
                    int kid = int.Parse(File.ReadAllText(Path.Combine(dir, "host.val.towtruck")));
                    Vector3 kp = PV(File.ReadAllText(Path.Combine(dir, "host.val.towtruckpos")));
                    yield return Expect(() => ById(kid) != null && Vector3.Distance(ById(kid).transform.position, kp) < 0.2f, 6f, "the host's dump truck is where they drove it", () => ById(kid) != null ? $"{Vector3.Distance(ById(kid).transform.position, kp):0.00} m off" : "none");
                    yield return Expect(() => TrailerById(tid) != null && Vector3.Distance(TrailerById(tid).transform.position, tp) < 0.2f, 6f, "and the trailer they towed is behind it", () => TrailerById(tid) != null ? $"{Vector3.Distance(TrailerById(tid).transform.position, tp):0.00} m off" : "none");
                    yield return Expect(() => TrailerById(tid) != null && TrailerById(tid).Hitched != null, 4f, "hitched to it");
                }
                yield return Sync("tow-checked");

                // the deck: the host (who runs the trailer) tilts it, then the friend levels it from the ground
                {
                    int tid = int.Parse(File.ReadAllText(Path.Combine(dir, "host.val.tow")));
                    var dt = TrailerById(tid);
                    if (host && dt != null) dt.SetDeckTilt(true);
                    if (!host)
                    {
                        float t0 = Time.unscaledTime;
                        yield return Expect(() => dt != null && dt.DeckTilt > 1.5f, 1.5f, "the host tilts the deck: it starts tilting on your game straight away", () => dt != null ? $"{dt.DeckTilt:0.0}° after {Time.unscaledTime - t0:0.00} s" : "no trailer");
                        Info($"deck started moving here {Time.unscaledTime - t0:0.00} s after the host worked it");
                    }
                    yield return Seconds(4f);
                    yield return Sync("deck-down");
                    if (!host)
                    {
                        if (dt != null) dt.SetDeckTilt(false);
                        yield return Seconds(2f);
                        Check(dt != null && dt.TiltTarget < 0.5f && dt.DeckTilt < dt.tiltMax - 2f, $"you level the deck of a trailer on the host's truck: it keeps going on your game ({(dt != null ? dt.DeckTilt : -1f):0.0}°)");
                    }
                    else
                    {
                        float t0 = Time.unscaledTime;
                        yield return Expect(() => dt != null && dt.TiltTarget < 0.5f && dt.DeckTilt < dt.tiltMax - 1.5f, 2f, "the friend levels the deck: it moves on your game straight away", () => dt != null ? $"{dt.DeckTilt:0.0}°, target {dt.TiltTarget:0}" : "no trailer");
                        Info($"deck started levelling here {Time.unscaledTime - t0:0.00} s after the friend worked it");
                    }
                    yield return Seconds(4f);
                    yield return Sync("deck-up");
                    Check(dt != null && dt.DeckTilt < 0.5f, $"the deck ends level on your game ({(dt != null ? dt.DeckTilt : -1f):0.0}°)");
                }
                if (host) { foreach (var v in VehicleBase.All) if (v != null && v.Occupied) { v.Exit(); break; } yield return Frames(10); }
            }
            yield return Sync("tow-done");

            // ---------------- both jump in the same machine at once: one gets it, and both games agree who
            {
                int pid = int.Parse(File.ReadAllText(Path.Combine(dir, "host.val.pickup")));
                var pk = ById(pid);
                if (pk != null)
                {
                    var spot = pk.transform.position + pk.transform.right * 2.5f;
                    Me.PlaceAt(spot, 0f);
                }
                yield return Sync("race-ready");
                if (pk != null) pk.Interact(Me);
                yield return Seconds(3f);
                Share("race", pk != null && pk.Occupied ? "in" : "out");
                yield return Sync("race");
                string mine = File.ReadAllText(Path.Combine(dir, $"{role}.val.race")), theirs = File.ReadAllText(Path.Combine(dir, $"{other}.val.race"));
                Check(mine != theirs, $"one of you gets the seat (you: {mine}, them: {theirs})");
                if (pk != null) Check(pk.Occupied ? !pk.NetRemote : pk.NetRemote, $"and your game agrees who runs it (you're {(pk.Occupied ? "in" : "out")}, remote {pk.NetRemote})");
                if (pk != null && pk.Occupied) pk.Exit();
                yield return Seconds(1f);
            }
            yield return Sync("race-done");

            // ---------------- the friend leaves and comes back
            {
                int tid = int.Parse(File.ReadAllText(Path.Combine(dir, "join.val.truck")));
                if (!host)
                {
                    net.Leave();
                    yield return Expect(() => !NetSession.InSession, 4f, "you left the game");
                    yield return Expect(() => GameUI.Instance != null && GameUI.Instance.InMainMenu, 10f, "and you're back at the main menu (it was the host's world)");
                    yield return Frames(30);
                    Refresh();
                }
                yield return Sync("left");
                if (host)
                {
                    yield return Expect(() => net.CrewSize == crew - 1, 6f, "your friend left: one fewer in the game");
                    yield return Expect(() => new List<(string, Vector3, float)>(net.OtherPlayers()).Count == crew - 2, 4f, "their player is gone");
                    yield return Expect(() => ById(tid) != null && !ById(tid).NetRemote, 4f, "the truck they bought runs on your game now");
                    // the host keeps working while they're gone
                    Vector3 pz = new Vector3(80f, 0f, -150f);
                    for (int i = 0; i < 10; i++) DigSite.DigAnywhere(new Vector3(pz.x + i * 0.4f, site.SurfaceY(pz) - 0.8f, pz.z), 0.6f, 3f);
                    site.Flush(99999);
                    Share("dig2", F(site.SurfaceY(pz + Vector3.right * 2f)));
                    var pk = ById(int.Parse(File.ReadAllText(Path.Combine(dir, "host.val.pickup"))));
                    if (pk != null) { var rb = pk.GetComponent<Rigidbody>(); rb.isKinematic = true; pk.transform.position += Vector3.right * 4f; rb.position = pk.transform.position; rb.isKinematic = false; yield return Seconds(2f); Share("pk2", V(pk.transform.position)); }
                }
                yield return Sync("while-away");
                if (!host) { net.JoinLan(); yield return Expect(() => NetSession.InSession, 15f, "you joined again"); yield return Seconds(2f); }
                yield return Sync("rejoined");
                yield return Expect(() => net.CrewSize == crew, 8f, "everyone in the game again");
                if (!host)
                {
                    var pk = ById(int.Parse(File.ReadAllText(Path.Combine(dir, "host.val.pickup"))));
                    Vector3 p2 = PV(File.ReadAllText(Path.Combine(dir, "host.val.pk2")));
                    yield return Expect(() => pk != null && pk.NetRemote && Vector3.Distance(pk.transform.position, p2) < 0.8f, 8f, "the host's pickup is where they moved it, and runs on their game", () => pk != null ? $"remote {pk.NetRemote}, {Vector3.Distance(pk.transform.position, p2):0.00} m off" : "none");
                    var tk = ById(tid);
                    yield return Expect(() => tk != null && tk.NetRemote, 8f, "the truck you bought before is run by the host now (it was theirs while you were away)", () => tk != null ? $"remote {tk.NetRemote}" : "none");
                }
                else
                {
                    var tk = ById(tid);
                    yield return Seconds(3f);
                    Check(tk != null && !tk.NetRemote, $"the host still runs the truck after your friend comes back (remote {tk?.NetRemote})");
                }
            }
            yield return Sync("rejoin-checked");

            // ---------------- every machine and trailer: both games agree who runs it, and where it is
            {
                yield return Seconds(2f);
                var sb = new StringBuilder();
                foreach (var v in VehicleBase.All) if (v != null && v.NetId != 0) sb.Append($"{v.NetId}={(v.NetRemote ? "R" : "L")}@{V(v.transform.position)}|");
                foreach (var t in Trailer.All) if (t != null && t.NetKey != 0) sb.Append($"t{t.NetKey}={(t.NetRemote ? "R" : "L")}@{V(t.transform.position)}|");
                Share("ground", GroundNow());
                Share("census", sb.ToString());
                yield return Sync("census");
                string gd = GroundDiff(File.ReadAllText(Path.Combine(dir, $"{role}.val.ground")), File.ReadAllText(Path.Combine(dir, $"{other}.val.ground")));
                Check(gd == null, $"the ground is the same on both games (the mine and the town yards){(gd != null ? ": " + gd : "")}");
                var mine = Census(File.ReadAllText(Path.Combine(dir, $"{role}.val.census")));
                var theirs = Census(File.ReadAllText(Path.Combine(dir, $"{other}.val.census")));
                int both = 0, neither = 0, missing = 0, far = 0; string bad = "";
                foreach (var kv in mine)
                {
                    if (!theirs.TryGetValue(kv.Key, out var t)) { missing++; bad += $" {kv.Key} only here;"; continue; }
                    if (kv.Value.local && t.local) { both++; bad += $" {kv.Key} run by both;"; }
                    if (!kv.Value.local && !t.local) { neither++; bad += $" {kv.Key} run by nobody;"; }
                    if (Vector3.Distance(kv.Value.p, t.p) > 1.0f) { far++; bad += $" {kv.Key} {Vector3.Distance(kv.Value.p, t.p):0.0} m apart;"; }
                }
                foreach (var kv in theirs) if (!mine.ContainsKey(kv.Key)) { missing++; bad += $" {kv.Key} only there;"; }
                Info($"{mine.Count} machines and trailers here, {theirs.Count} there");
                Check(missing == 0, $"every machine and trailer is on both games{(missing > 0 ? ":" + bad : "")}");
                Check(both == 0 && neither == 0, $"each one is run by exactly one game{(both + neither > 0 ? ":" + bad : "")}");
                Check(far == 0, $"and they're in the same place on both{(far > 0 ? ":" + bad : "")}");
            }
            yield return Sync("census-done");

            // ---------------- the host saves, closes the game, hosts the saved world again; the friend joins it
            {
                if (host)
                {
                    string err = SaveGame.Save("net bot world", SaveGame.Mode.Multiplayer);
                    Check(err == null, $"the host saves the multiplayer world ({err ?? "ok"})");
                    var info = SaveGame.List(SaveGame.Mode.Multiplayer).Find(x => x.name == "net bot world");
                    Share("save", info != null ? info.path : "");
                    Share("saved-census", CensusNow(true));
                    yield return Seconds(1f);
                    net.Leave();
                }
                yield return Sync("closed");
                if (!host)
                {
                    yield return Expect(() => !NetSession.InSession, 10f, "the host closed the game: you're out");
                    yield return Expect(() => GameUI.Instance != null && GameUI.Instance.InMainMenu, 10f, "back at the main menu");
                    yield return Frames(30);
                    Refresh();
                }
                if (host)
                {
                    string path = File.ReadAllText(Path.Combine(dir, "host.val.save"));
                    string loadErr = "never finished";
                    yield return SaveGame.Load(path, e => loadErr = e);
                    yield return Frames(30);
                    Check(loadErr == null, $"the host loads the saved world ({loadErr ?? "ok"})");
                    net.Host();
                    yield return Expect(() => NetSession.InSession && net.IsHost, 5f, "and hosts it");
                    try { File.Delete(path); } catch { }
                }
                yield return Sync("rehosted");
                if (!host) { net.JoinLan(); yield return Expect(() => NetSession.InSession, 15f, "you join the host's saved world"); }
                yield return Expect(() => GameUI.Instance != null && !GameUI.Instance.InMainMenu, 10f, "in the world");
                yield return Sync("rejoined2");
                yield return Seconds(3f);
                Share("ground2", GroundNow());
                Share("census2", CensusNow(false));
                yield return Sync("census2");
                string gd2 = GroundDiff(File.ReadAllText(Path.Combine(dir, $"{role}.val.ground2")), File.ReadAllText(Path.Combine(dir, $"{other}.val.ground2")));
                Check(gd2 == null, $"the saved world's ground is the same on both games{(gd2 != null ? ": " + gd2 : "")}");
                if (!host)
                {
                    var mine = Census(File.ReadAllText(Path.Combine(dir, "join.val.census2")));
                    var theirs = Census(File.ReadAllText(Path.Combine(dir, "host.val.census2")));
                    int missing = 0, far = 0, owners = 0; string bad = "";
                    foreach (var kv in theirs)
                    {
                        if (!mine.TryGetValue(kv.Key, out var m)) { missing++; bad += $" {kv.Key} missing;"; continue; }
                        if (m.local == kv.Value.local) { owners++; bad += $" {kv.Key} {(m.local ? "run by both" : "run by nobody")};"; }
                        if (Vector3.Distance(m.p, kv.Value.p) > 1f) { far++; bad += $" {kv.Key} {Vector3.Distance(m.p, kv.Value.p):0.0} m apart;"; }
                    }
                    foreach (var kv in mine) if (!theirs.ContainsKey(kv.Key)) { missing++; bad += $" {kv.Key} extra here;"; }
                    Check(missing == 0 && owners == 0 && far == 0, $"the saved world's machines and trailers match on both games ({theirs.Count}){bad}");
                    float hp1 = P(File.ReadAllText(Path.Combine(dir, "host.val.p1")));
                    Check(Mathf.Abs(site.SurfaceY(p1 + new Vector3(5f, 0f, 5f)) - hp1) < 0.08f, $"the ground dug at the start is still dug ({site.SurfaceY(p1 + new Vector3(5f, 0f, 5f)):0.00} vs {hp1:0.00})");
                }
            }
            yield return Sync("saved-world-done");

            // ---------------- the host closes the game
            if (host) { yield return Seconds(1f); net.Leave(); }
            if (!host)
            {
                yield return Expect(() => !NetSession.InSession, 10f, "the host closed the game: you're out");
                yield return Expect(() => GameUI.Instance != null && GameUI.Instance.InMainMenu, 10f, "and back at the main menu, with a message why");
            }
            yield return End();
        }

        /// <summary>Every machine and trailer: who runs it, where it is. `keys`: by listing and place (ids change on load).</summary>
        static string CensusNow(bool keys)
        {
            var sb = new StringBuilder();
            foreach (var v in VehicleBase.All) if (v != null && v.NetId != 0) sb.Append($"{v.NetId}={(v.NetRemote ? "R" : "L")}@{V(v.transform.position)}|");
            foreach (var t in Trailer.All) if (t != null && t.NetKey != 0) sb.Append($"t{t.NetKey}={(t.NetRemote ? "R" : "L")}@{V(t.transform.position)}|");
            return sb.ToString();
        }

        void Mark2(string step) => File.WriteAllText(Path.Combine(dir, $"{role}.2.{step}"), "");
        IEnumerator Wait2(string step, float timeout = 90f)
        {
            string f = Path.Combine(dir, $"join2.{step}");
            float t0 = Time.realtimeSinceStartup;
            while (!File.Exists(f)) { if (Time.realtimeSinceStartup - t0 > timeout) { Fail($"waited too long for the third player ({step})"); yield break; } yield return null; }
        }

        /// <summary>The third player: joins, watches, and at the end checks every machine is where the host has it.</summary>
        IEnumerator Observer()
        {
            var net = NetSession.Instance;
            string H(string f) => Path.Combine(dir, "host." + f);
            float t0 = Time.realtimeSinceStartup;
            while (!File.Exists(H("hosted")) && Time.realtimeSinceStartup - t0 < 120f) yield return null;
            yield return Seconds(2f);
            net.JoinLan();
            yield return Expect(() => NetSession.InSession && !net.IsHost, 20f, "the third player joins the game");
            Mark("in");
            yield return Expect(() => net.CrewSize == crew, 15f, $"all {crew} players are in", () => $"crew {net.CrewSize}");
            yield return Expect(() => new List<(string, Vector3, float)>(net.OtherPlayers()).Count == crew - 1, 10f, "and you see both the others");
            // watch until the census
            t0 = Time.realtimeSinceStartup;
            while (!File.Exists(H("val.census")) && Time.realtimeSinceStartup - t0 < 600f) yield return null;
            yield return Seconds(1f);
            var mine = Census(CensusNow(false));
            var theirs = Census(File.ReadAllText(H("val.census")));
            int missing = 0, far = 0, owners = 0; string bad = "";
            foreach (var kv in theirs)
            {
                if (!mine.TryGetValue(kv.Key, out var m)) { missing++; bad += $" {kv.Key} missing;"; continue; }
                if (m.local) { owners++; bad += $" {kv.Key} runs here too;"; }
                if (Vector3.Distance(m.p, kv.Value.p) > 1.0f) { far++; bad += $" {kv.Key} {Vector3.Distance(m.p, kv.Value.p):0.0} m apart;"; }
            }
            Info($"third player sees {mine.Count} machines and trailers, the host {theirs.Count}");
            Check(missing == 0, $"the third player has every machine and trailer{(missing > 0 ? bad : "")}");
            Check(owners == 0, $"none of them run on the third player's game (they never got in one){(owners > 0 ? bad : "")}");
            Check(far == 0, $"and they're where the host has them{(far > 0 ? bad : "")}");
            string gd = null;
            for (int k = 0; k < 20 && !File.Exists(H("val.ground")); k++) yield return Seconds(0.2f);
            if (File.Exists(H("val.ground"))) gd = GroundDiff(GroundNow(), File.ReadAllText(H("val.ground")), 0.12f); // (read a second later: dug walls are still settling)
            Check(File.Exists(H("val.ground")) && gd == null, $"the third player's ground matches the host's{(gd != null ? ": " + gd : "")}");
            // the host closes the game (to load the save)
            yield return Expect(() => !NetSession.InSession, 600f, "the host closed the game: you're out");
            Mark("done");
            Flush();
            log.AppendLine(fails == 0 ? $"ALL PASSED ({passes})" : $"{fails} FAILED ({passes} passed)");
            Flush();
            yield return Seconds(1f);
            Application.Quit();
        }

        static void MakePad()
        {
            if (GameObject.Find("Bot pad") != null) return;
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "Bot pad"; pad.transform.position = new Vector3(-150f, 399.5f, 150f); pad.transform.localScale = new Vector3(80f, 1f, 300f);
            Physics.SyncTransforms();
        }

        /// <summary>The ground's height on a grid over where the bots dig (the mine and three town yards).</summary>
        static string GroundNow()
        {
            var sb = new StringBuilder();
            var site = DigSite.Instance;
            for (float x = 20f; x <= 95f; x += 1f) for (float z = -180f; z <= -140f; z += 1f) sb.Append(F(site.SurfaceY(new Vector3(x, 0f, z)))).Append(';');
            DigSite town = null; foreach (var g in DigSite.All) if (g != site) town = g;
            if (town != null)
                foreach (int hi in new[] { 6, 10, 11 })
                {
                    var h = FPSBase.City.House.All[hi];
                    for (float lx = -10.5f; lx <= 10.5f; lx += 0.75f) for (float lz = 0.5f; lz <= 20f; lz += 0.75f) sb.Append(F(town.SurfaceY(h.Local(lx, lz)))).Append(';');
                }
            return sb.ToString();
        }
        static string GroundDiff(string a, string b, float tol = 0.06f)
        {
            var la = PJ(a); var lb = PJ(b);
            if (la.Count != lb.Count) return $"different sizes {la.Count}/{lb.Count}";
            int off = 0; float worst = 0f;
            for (int i = 0; i < la.Count; i++) { float d = Mathf.Abs(la[i] - lb[i]); if (d > tol) off++; worst = Mathf.Max(worst, d); }
            return off == 0 ? null : $"{off} of {la.Count} points differ, worst {worst:0.00} m";
        }

        static Dictionary<string, (bool local, Vector3 p)> Census(string s)
        {
            var d = new Dictionary<string, (bool, Vector3)>();
            foreach (var e in s.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = e.IndexOf('='), at = e.IndexOf('@');
                d[e.Substring(0, eq)] = (e[eq + 1] == 'L', PV(e.Substring(at + 1)));
            }
            return d;
        }

        /// <summary>One player drives a pickup about; the other's copy has to follow it and stop in the same place.</summary>
        IEnumerator DriveAndWatch(int id, bool driver, string who)
        {
            var pk = ById(id) as DumpPickup;
            if (pk == null) { Fail($"the pickup {id} is on this game"); yield break; }
            if (driver)
            {
                var me = Me;
                // on a slow connection the other player getting out takes a moment to arrive: wait for the seat
                // to be free, as a player would
                for (float w = 0f; w < 6f && pk.NetRemoteDriven; w += Time.unscaledDeltaTime) yield return null;
                var prb = pk.GetComponent<Rigidbody>();
                prb.isKinematic = true;
                pk.transform.SetPositionAndRotation(new Vector3(who == "host" ? -175f : -125f, 401f, 20f), Quaternion.identity);
                prb.position = pk.transform.position; prb.rotation = Quaternion.identity;
                prb.isKinematic = false; prb.linearVelocity = Vector3.zero;
                Physics.SyncTransforms();
                yield return Seconds(1.5f);
                me.PlaceAt(pk.transform.position + pk.transform.right * 2.5f, 0f);
                yield return Frames(5);
                pk.Interact(me);
                yield return Frames(20);
                Check(pk.Occupied && !pk.NetRemote, $"{who}: in the pickup, running it");
                Controls.TestHeld[GameAction.DriveForward] = 0.5f;
                yield return Seconds(1.5f);
                Share($"moving-{who}", "1");
                yield return Seconds(3.5f);
                Share($"rev-{who}", "1");
                Info($"{who} drove the pickup at {pk.GetComponent<Rigidbody>().linearVelocity.magnitude:0.0} m/s");
                Controls.TestHeld.Clear(); Controls.TestHeld[GameAction.DriveBack] = 0.6f;
                yield return Seconds(1.5f);
                Controls.TestHeld.Clear();
                yield return Seconds(3f);
                Share($"stop-{who}", V(pk.transform.position) + "|" + F(pk.transform.eulerAngles.y));
                yield return Wait($"seen-{who}", 30f);
                pk.Exit();
                yield return Frames(20);
                Mark($"out-{who}");
            }
            else
            {
                yield return Get($"moving-{who}");
                yield return Expect(() => pk.NetRemote && pk.NetRemoteDriven, 4f, $"{who} drives the pickup: your copy is theirs and shows them driving", () => $"remote {pk.NetRemote}, driven {pk.NetRemoteDriven}");
                yield return Expect(() => pk.GetComponent<Rigidbody>().isKinematic, 2f, "your copy has no physics of its own (it follows theirs)");
                // how smoothly it moves on your screen while they drive straight on
                var speeds = new List<float>();
                Vector3 last = pk.transform.position;
                string revFile = Path.Combine(dir, $"{other}.val.rev-{who}");
                float tw = Time.realtimeSinceStartup;
                yield return null;
                while (!File.Exists(revFile) && Time.realtimeSinceStartup - tw < 10f)
                {
                    float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
                    speeds.Add(Vector3.Distance(pk.transform.position, last) / dt);
                    last = pk.transform.position;
                    yield return null;
                }
                if (speeds.Count > 20)
                {
                    speeds.RemoveRange(0, speeds.Count / 6); // (still catching up to speed)
                    var sorted = new List<float>(speeds); sorted.Sort();
                    float med = sorted[sorted.Count / 2];
                    int stalls = 0, lurches = 0;
                    foreach (var v in speeds) { if (v < med * 0.3f) stalls++; if (v > med * 2.2f) lurches++; }
                    float sf = stalls / (float)speeds.Count, lf = lurches / (float)speeds.Count;
                    Check(med > 0.5f && sf < 0.04f && lf < 0.04f, $"it moves smoothly on your screen while {who} drives (median {med:0.0} m/s, {sf:P0} of frames stalled, {lf:P0} lurched, {speeds.Count} frames)");
                }
                else Fail($"watched {who} drive ({speeds.Count} frames)");
                yield return Get($"stop-{who}");
                var parts = (got ?? "0,0,0|0").Split('|');
                Vector3 sp = PV(parts[0]); float yaw = P(parts[1]);
                yield return Expect(() => Vector3.Distance(pk.transform.position, sp) < 0.25f && Mathf.Abs(Mathf.DeltaAngle(pk.transform.eulerAngles.y, yaw)) < 3f, 6f,
                    $"it stops where {who} stopped it", () => $"{Vector3.Distance(pk.transform.position, sp):0.00} m, {Mathf.DeltaAngle(pk.transform.eulerAngles.y, yaw):0.0}° off");
                Mark($"seen-{who}");
                yield return Wait($"out-{who}", 30f);
                yield return Expect(() => !pk.NetRemoteDriven, 4f, $"{who} got out: nobody's driving it");
            }
            yield return Sync($"drive-{who}");
        }

        /// <summary>Dig a job's trench out to its depth across its width (as an excavator would).</summary>
        static void DigTrench(DigSite town, FPSBase.City.Job j)
        {
            if (town == null) return;
            Vector3 dir = j.b - j.a; dir.y = 0f; float len = dir.magnitude; dir /= Mathf.Max(len, 0.01f);
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            float half = Mathf.Max(0f, j.width / 2f - 0.12f);
            int lanes = Mathf.Max(1, Mathf.CeilToInt(2f * half / 0.15f));
            for (int pass = 0; pass < 3; pass++)
                for (int l = 0; l <= lanes; l++)
                    for (float t = 0f; t <= len; t += 0.15f)
                    {
                        Vector3 q = j.a + dir * t + side * (-half + 2f * half * l / lanes);
                        town.Dig(new Vector3(q.x, town.OriginalY(q) - j.depth - 0.08f, q.z), 0.18f, 2f);
                    }
            town.Flush(99999);
        }

        IEnumerator End()
        {
            log.AppendLine(fails == 0 ? $"ALL PASSED ({passes})" : $"{fails} FAILED ({passes} passed)");
            Flush();
            Mark("done");
            yield return Wait("done", 60f);
            yield return Seconds(1f);
            Application.Quit();
        }
    }
}
