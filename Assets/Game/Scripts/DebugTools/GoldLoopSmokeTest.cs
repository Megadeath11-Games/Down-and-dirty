using System.Collections;
using System.IO;
using System.Text;
using FPSBase.Core;
using FPSBase.Mining;
using FPSBase.Vehicles;
using UnityEngine;

namespace FPSBase.DebugTools
{
    /// <summary>
    /// Automated check of the gold loop (no input needed): dig -> pour into truck -> pour into hopper -> wash -> clean up -> sell.
    /// Also checks the vehicles settle on the ground. Writes Logs/smoketest.txt and a screenshot.
    /// </summary>
    public class GoldLoopSmokeTest : MonoBehaviour
    {
        public string reportPath = "Logs/smoketest.txt";
        public string shotPath = "Screenshots/mining.png";
        public bool Done { get; private set; }

        readonly StringBuilder log = new();
        public string Report => log.ToString();
        int fails;

        void Check(bool ok, string what) { log.AppendLine((ok ? "PASS  " : "FAIL  ") + what); if (!ok) fails++; }

        IEnumerator Start()
        {
            // SMOKE_ONLY=dig,machines,... runs just those parts (fast iteration); unset runs everything
            string only = System.Environment.GetEnvironmentVariable("SMOKE_ONLY");
            if (!string.IsNullOrEmpty(only))
            {
                for (int i = 0; i < 100; i++) yield return new WaitForFixedUpdate();
                foreach (var part in only.Split(','))
                    switch (part.Trim())
                    {
                        case "dig": yield return DigCycleTests(); break;
                        case "bucket": yield return BucketTests(); break;
                        case "multi": yield return MultiControlTests(); break;
                        case "fleet2": yield return Fleet2Tests(); break;
                        case "net": yield return NetSyncTests(); yield return NetMirrorTests(); break;
                        case "update3": yield return Update3Tests(); break;
                        case "berm": yield return BermTests(); break;
                        case "services": yield return ServiceTests(); break;
                        case "cabfp": yield return CabFirstPersonTests(); break;
                        case "transport": yield return TransportTests(); break;
                        case "save": yield return SaveTests(); break;
                        case "dozerblade": yield return DozerBladeTests(); break;
                        case "fine": yield return FineGroundTests(); break;
                        case "dirtphys": yield return DirtPhysicsTests(DigSite.Instance, FindFirstObjectByType<DumpPickup>()); break;
                        case "hill": yield return PickupHillTests(); break;
                        case "heavy": yield return HeavyTests(); break;
                        case "skidcarry": yield return SkidCarryTests(); break;
                        case "equip": yield return EquipTrailerTests(); break;
                        case "update8": yield return Update8Tests(); break;
                        case "update9": yield return Update9Tests(); break;
                        case "town": yield return TownTests(); break;
                        case "hitch": yield return DumpHitchTests(); break;
                        case "slosh": yield return SloshTests(DigSite.Instance, FindFirstObjectByType<DumpPickup>()); break;
                        case "materials": yield return MaterialsTests(DigSite.Instance, Economy.Instance, FindFirstObjectByType<WashPlant>(), FindFirstObjectByType<DumpPickup>()); break;
                        default: log.AppendLine($"INFO  unknown quick test '{part}'"); break;
                    }
                Finish();
                yield break;
            }
            var ex = FindFirstObjectByType<MiniExcavator>();
            var truck = FindFirstObjectByType<DumpPickup>();
            var plant = FindFirstObjectByType<WashPlant>();
            var buyer = FindFirstObjectByType<GoldBuyer>();
            var eco = Economy.Instance;
            Check(ex && truck && plant && buyer && eco && DigSite.Instance, "scene has excavator, pickup, wash plant, buyer, economy, dig site");
            Vector3 ex0 = ex.transform.position, tr0 = truck.transform.position;
            foreach (var p in new[] { new Vector3(54f, 0f, 4f), new Vector3(54f, 0f, 14f), new Vector3(70f, 0f, -10f), new Vector3(60f, 0f, -35f), new Vector3(45f, 0f, -10f), new Vector3(95f, 0f, -10f) })
            {
                string s = $"INFO  ray at ({p.x},{p.z}): ";
                foreach (var h in Physics.RaycastAll(p + Vector3.up * 20f, Vector3.down, 40f))
                    s += $"{h.collider.name}@{h.point.y:0.00}  ";
                log.AppendLine(s);
            }
            foreach (var t in Terrain.activeTerrains)
                log.AppendLine($"INFO  terrain {t.name} pos {t.transform.position} size {t.terrainData.size} res {t.terrainData.heightmapResolution} h(center) {t.terrainData.GetHeight(t.terrainData.heightmapResolution / 2, t.terrainData.heightmapResolution / 2):0.00}");

            for (int i = 0; i < 150; i++) yield return new WaitForFixedUpdate();
            Check(Mathf.Abs(ex.transform.position.y - ex0.y) < 0.5f, $"excavator settled (y {ex0.y:0.00} -> {ex.transform.position.y:0.00})");
            Check(Mathf.Abs(truck.transform.position.y - tr0.y) < 0.8f && truck.transform.up.y > 0.9f, $"pickup settled upright (y {tr0.y:0.00} -> {truck.transform.position.y:0.00})");

            // dig: lots of bucketfuls down to the pay layer
            var site = DigSite.Instance;
            Vector3 hole = new Vector3(70f, 0f, -20f);
            // strip the overburden first (top ~1.2 m has almost no gold), dumping it aside
            var overburden = new MaterialLoad();
            for (int i = 0; i < 2000 && site.SurfaceY(hole) > -1.35f; i++)
                overburden.Add(DigSite.DigAnywhere(hole + Vector3.down * 1.4f, 0.9f, 0.05f));
            log.AppendLine($"INFO  stripped {overburden.volume:0.00} m³ overburden ({overburden.Grade:0.00} g/m³)");
            var dug = new MaterialLoad();
            for (int i = 0; i < 2000 && dug.volume < 1.4f; i++)
            {
                float depth = Mathf.Min(4f, 1.4f + i * 0.01f);
                dug.Add(DigSite.DigAnywhere(hole + Vector3.down * depth, 0.9f, 0.05f));
            }
            Check(dug.volume > 1.0f, $"dug {dug.volume:0.00} m³ from the claim");
            Check(dug.goldGrams > 0.5f, $"dug dirt contains {dug.goldGrams:0.00} g gold ({dug.Grade:0.0} g/m³)");
            Check(site.SurfaceY(hole) < -0.5f, $"ground lowered at dig spot (surface y {site.SurfaceY(hole):0.00})");

            // the bucket can be *inside* the bed volume when dumping: the receiver must still be found
            Vector3 inBed = truck.bedVolume.transform.TransformPoint(truck.bedVolume.center);
            Check(MaterialTransfer.FindReceiverBelow(inBed, null, out _) == (IMaterialReceiver)truck, "pour point inside the truck bed still finds the truck");

            // pour into the truck bed from above (tidy tank out: the whole box)
            bool hadTank = truck.TidyTankFitted;
            truck.SetTidyTank(false);
            Vector3 bedTop = truck.bedVolume.transform.TransformPoint(truck.bedVolume.center) + Vector3.up * 1.5f;
            MaterialTransfer.Pour(bedTop, dug, null);
            DirtGrains.FastForward();
            Check(truck.load.volume > 1.0f, $"truck bed received {truck.load.volume:0.00} m³");

            // pour truck load into hopper from above
            Vector3 hopTop = plant.hopperVolume.transform.TransformPoint(plant.hopperVolume.center) + Vector3.up * 1.5f;
            var load = truck.load.Take(truck.load.volume);
            MaterialTransfer.Pour(hopTop, load, null);
            DirtGrains.FastForward();
            Check(plant.hopper.volume > 1.0f, $"hopper received {plant.hopper.volume:0.00} m³");
            if (hadTank) truck.SetTidyTank(true);

            // a pour onto bare ground leaves loose dirt that keeps its gold
            Vector3 pp = new Vector3(60f, 3f, -25f);
            float lv0 = site.LooseVolume(pp, 4f, out float lg0);
            MaterialTransfer.Pour(pp, new MaterialLoad(0.3f, 1f), null);
            site.SettleNow();
            float lv1 = site.LooseVolume(pp, 4f, out float lg1);
            Check(lv1 - lv0 > 0.28f && lg1 - lg0 > 0.9f, $"pouring on the ground leaves {lv1 - lv0:0.00} m³ of loose dirt holding {lg1 - lg0:0.00} g gold");

            // a big dump slumps into a cone at the angle of repose and no dirt is lost
            Vector3 cone = new Vector3(66f, 0f, 0f);
            float g0 = site.SurfaceY(cone), tot0 = site.TotalLoose();
            site.Deposit(cone, new MaterialLoad(3f, 0f));
            site.SettleNow();
            float peakH = site.SurfaceY(cone) - g0;
            Check(Mathf.Abs(site.TotalLoose() - tot0 - 3f) < 0.01f, $"3 m³ dump: volume kept ({site.TotalLoose() - tot0:0.000} m³)");
            Check(peakH > 0.5f && peakH < 1.6f, $"3 m³ dump slumped into a heap {peakH:0.00} m tall");

            // wash for a while (speed up time)
            Time.timeScale = 10f;
            float t0 = Time.time;
            while (plant.hopper.volume > 0.01f && Time.time - t0 < 400f) yield return null;
            Time.timeScale = 1f;
            Check(plant.hopper.volume < 0.05f, $"hopper processed (left {plant.hopper.volume:0.00} m³)");
            Check(plant.sluiceGold > 0.3f, $"sluice caught {plant.sluiceGold:0.00} g gold");

            // clean up + sell
            float money0 = eco.Money;
            eco.AddGold(plant.sluiceGold); plant.sluiceGold = 0f;
            float g = eco.TakeAllGold(); eco.Earn(g * buyer.pricePerGram);
            Check(eco.Money > money0, $"sold {g:0.00} g for ${eco.Money - money0:N0}");

            // outpost + dealer: every machine for sale spawns and settles upright
            Check(SafeZone.At(new Vector3(60f, 0f, 88f)) != null, "outpost safe zone exists");
            Check(FindObjectsByType<FuelPump>(FindObjectsSortMode.None).Length >= 2, "outpost has fuel pumps");
            var dealer = FindFirstObjectByType<VehicleDealer>();
            Check(dealer != null && dealer.listings.Length >= 4, "equipment dealer has listings");
            yield return FleetTests(dealer, site);

            // ---- bulldozer: cut + push dirt, then raise the blade to leave a pile
            var dozer = FindFirstObjectByType<Bulldozer>();
            var loader = FindFirstObjectByType<FrontLoader>();
            Check(dozer != null && loader != null, "scene has a bulldozer and a wheel loader");
            Check(dozer.transform.up.y > 0.9f && loader.transform.up.y > 0.9f, "bulldozer and loader sit upright");
            Check(eco.Unlimited && eco.TrySpend(1e9f), "unlimited money test mode is on (a $1B purchase goes through)");

            var drb = dozer.GetComponent<Rigidbody>(); drb.isKinematic = true;
            Quaternion south = Quaternion.Euler(0f, 180f, 0f);
            Vector3 dpos = new Vector3(82f, 0f, 4f); dpos.y = site.SurfaceY(dpos);
            dozer.transform.SetPositionAndRotation(dpos, south);
            Vector3 fwd = dozer.transform.forward, right = dozer.transform.right;
            dozer.SetBlade(-0.15f);
            const float step = 0.02f;
            for (int i = 0; i < 300; i++) // 6 m at 1 m/s
            {
                Vector3 np = dozer.transform.position + fwd * step;
                np.y = site.SurfaceY(np);
                dozer.transform.position = np;
                dozer.Work(1f, step);
                site.Relax(4000);
            }
            site.SettleNow();
            Vector3 front = dozer.bladeEdge.position + fwd * 1.0f;
            float heap = site.LooseVolume(front, 2.6f, out float heapGold);
            Check(heap > 1.0f, $"dozer cut and pushed a {heap:0.00} m³ heap ({heapGold:0.0} g gold) over 6 m");
            Check(site.SurfaceY(dpos + fwd * 3f) < dpos.y - 0.08f, $"the blade cut the ground down ({site.SurfaceY(dpos + fwd * 3f) - dpos.y:0.00} m)");
            float berm = site.LooseVolume(dpos + fwd * 3f + right * 1.7f, 0.7f, out _) + site.LooseVolume(dpos + fwd * 3f - right * 1.7f, 0.7f, out _);
            log.AppendLine($"INFO  berms beside the cut: {berm:0.00} m³");
            for (int i = 0; i < 150; i++) // back off 3 m
            {
                dozer.transform.position -= fwd * step;
                dozer.Work(-1f, step);
            }
            site.SettleNow();
            float heapAfter = site.LooseVolume(front, 2.6f, out _);
            float pileTop = site.SurfaceY(front) - dpos.y;
            Check(heapAfter > heap * 0.98f && pileTop > 0.3f, $"backing off leaves it as a pile ({heapAfter:0.00} m³, {pileTop:0.00} m high)");

            // ---- wheel loader: drive the bucket into the dozer's pile, then dump it into the truck
            var lrb = loader.GetComponent<Rigidbody>(); lrb.isKinematic = true;
            loader.SetPose(-36f, 0f);
            loader.transform.rotation = south;
            float reach = Vector3.Dot(loader.bucketTip.position - loader.transform.position, loader.transform.forward);
            Vector3 lpos = front - fwd * (reach + 2.2f);
            lpos.y = site.SurfaceY(lpos);
            loader.transform.position = lpos;
            for (int i = 0; i < 300 && loader.bucketLoad.volume < loader.bucketCapacity * 0.95f; i++)
            {
                loader.transform.position += fwd * step;
                loader.Work(1f, 0f, step);
                site.Relax(4000);
            }
            float scooped = loader.bucketLoad.volume;
            Check(scooped > 0.5f, $"loader scooped {scooped:0.00} m³ ({loader.bucketLoad.goldGrams:0.0} g gold) from the pile");
            loader.SetPose(45f, 55f);
            truck.load = default;
            Vector3 bedC = truck.bedVolume.transform.TransformPoint(truck.bedVolume.center);
            Vector3 tipNow = loader.bucketTip.position + loader.transform.forward * 0.15f - loader.transform.position;
            loader.transform.position = bedC + Vector3.up * 1.2f - tipNow;
            Physics.SyncTransforms();
            for (int i = 0; i < 300 && !loader.bucketLoad.IsEmpty; i++) loader.Work(0f, 1f, step);
            DirtGrains.FastForward();
            Check(truck.load.volume > scooped * 0.9f, $"loader dumped into the truck bed ({truck.load.volume:0.00} m³)");

            // trampling: driving over grass leaves dirt ruts (visual only)
            site.Trample(new Vector3(70f, 0f, 5f), 0.5f);

            // stage the screenshot: dozer back at its heap, loader beside it with a full bucket
            site.SettleNow();
            float bladeReach = Vector3.Dot(dozer.bladeEdge.position - dozer.transform.position, fwd);
            Vector3 dz = front - fwd * (bladeReach + 1.4f); dz.y = site.SurfaceY(dz);
            dozer.transform.SetPositionAndRotation(dz, south);
            dozer.SetBlade(0f);
            Vector3 lz = front + right * 4.5f - fwd * 2f; lz.y = site.SurfaceY(lz);
            loader.transform.SetPositionAndRotation(lz, Quaternion.Euler(0f, 200f, 0f));
            loader.SetPose(15f, -25f);
            loader.bucketLoad = new MaterialLoad(loader.bucketCapacity, 0f);
            Physics.SyncTransforms();
            yield return ShotAt(front - right * 7f + fwd * 6f + Vector3.up * 4f, front + Vector3.up * 0.3f, "Screenshots/dozer_loader.png");
            yield return ShotAt(new Vector3(40f, 22f, -48f), new Vector3(78f, 0f, 2f), "Screenshots/world.png");
            // looking straight down used to cull the grass on the dirt field
            yield return ShotAt(new Vector3(58f, 2.5f, 8f), new Vector3(58f, 0f, 8.3f), "Screenshots/grass_down.png");

            Check(Controls.Get(GameAction.Interact) == UnityEngine.InputSystem.Key.F, $"default interact / enter vehicle key is F ({Controls.N(GameAction.Interact)})");
            Check(Controls.Get(GameAction.BladeDown) == UnityEngine.InputSystem.Key.Q && Controls.Get(GameAction.BladeUp) == UnityEngine.InputSystem.Key.E,
                $"dozer blade down = Q, up = E ({Controls.N(GameAction.BladeDown)}/{Controls.N(GameAction.BladeUp)})");
            foreach (var bind in Controls.Defaults)
            {
                string clash = Controls.Conflicts(bind.action);
                if (clash != null) Check(false, $"default key clash: {bind.category} / {bind.label} {clash}");
            }

            yield return Shot(new Vector3(63f, 0.3f, -13f), 150f, 30f, "Screenshots/dug_hole.png");
            ex.bucketLoad = new MaterialLoad(ex.bucketCapacity, 0f);
            truck.Receive(new MaterialLoad(truck.capacity * 0.6f, 0f));
            Vector3 b = ex.bucketTip.position;
            for (int i = 0; i < 5; i++) yield return null;
            var exClumps = ex.GetComponent<ClumpContainer>();
            Check(exClumps != null && exClumps.Count > 30, $"a full bucket holds live clumps ({(exClumps != null ? exClumps.Count : 0)} clods of {(exClumps != null ? exClumps.ClumpRadius * 100f : 0f):0} cm)");
            yield return ShotAt(b + ex.transform.right * 1.6f + Vector3.up * 1.1f + ex.transform.forward * 0.6f, b, "Screenshots/bucket.png");
            Vector3 bc = truck.bedVolume.transform.TransformPoint(truck.bedVolume.center);
            var bedClumps = truck.GetComponent<ClumpContainer>();
            Check(bedClumps != null && bedClumps.Count > 30, $"the truck bed load is live clumps ({(bedClumps != null ? bedClumps.Count : 0)} clods)");
            yield return ShotAt(bc + truck.transform.right * 3.2f + Vector3.up * 2.6f - truck.transform.forward * 2f, bc, "Screenshots/truck_bed.png");
            Check(site.ClumpDecorations > 40, $"loose dirt heaps show some clods ({site.ClumpDecorations})");
            ex.bucketLoad = default;
            for (int i = 0; i < 3; i++) yield return null;
            Check(exClumps != null && exClumps.Count == 0, "emptying the bucket removes its clumps");
            yield return Shot(new Vector3(60f, 0.3f, 58f), 0f, 6f, "Screenshots/outpost.png");

            yield return MaterialsTests(site, eco, plant, truck);
            yield return DirtPhysicsTests(site, truck);
            yield return SloshTests(site, truck);
            yield return PlayerTests(site, truck);

            yield return DigCycleTests();
            yield return BucketTests();
            yield return MultiControlTests();
            yield return SaveTests();
            yield return DozerBladeTests();
            yield return FineGroundTests();
            yield return PickupHillTests();
            yield return HeavyTests();
            yield return SkidCarryTests();
            yield return EquipTrailerTests();
            Finish();
        }

        /// <summary>Save a world, change everything, load it back: the ground, machines, bought gear and money return.</summary>
        IEnumerator SaveTests()
        {
            var site = DigSite.Instance;
            var eco = Economy.Instance;
            var dealer = FindFirstObjectByType<VehicleDealer>();
            var deliver = typeof(VehicleDealer).GetMethod("Deliver", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            VehicleDealer.Listing L(string n) { foreach (var l in dealer.listings) if (l.name.StartsWith(n)) return l; return null; }
            T Nearest<T>(Vector3 at, System.Collections.Generic.HashSet<BoughtFrom> skip) where T : Component
            {
                T best = null; float bd = float.MaxValue;
                foreach (var b in FindObjectsByType<BoughtFrom>(FindObjectsSortMode.None))
                    if ((skip == null || !skip.Contains(b)) && b.GetComponent<T>() is T c && Vector3.Distance(c.transform.position, at) < bd) { bd = Vector3.Distance(c.transform.position, at); best = c; }
                return best;
            }
            var had = new System.Collections.Generic.HashSet<BoughtFrom>(FindObjectsByType<BoughtFrom>(FindObjectsSortMode.None));
            // dig a hole
            Vector3 p = new Vector3(70f, 0f, -12f);
            Check(site.Contains(p), "save test spot is on the dig site");
            p.y = site.SurfaceY(p);
            site.Dig(p + Vector3.down * 0.6f, 1.2f, 3f);
            site.Flush(9999);
            float h1 = site.SurfaceY(p);
            // buy a pickup and a bowser, set them up
            deliver.Invoke(dealer, new object[] { L("GMT400") });
            deliver.Invoke(dealer, new object[] { L("Field Fuel Bowser") });
            for (int i = 0; i < 20; i++) yield return new WaitForFixedUpdate();
            var pk = Nearest<DumpPickup>(dealer.transform.position, had);
            var bw = Nearest<Trailer>(dealer.transform.position, had);
            Check(pk != null && bw != null, "bought a pickup and a bowser for the save test");
            if (pk == null || bw == null) yield break;
            pk.fuel = 13f;
            pk.SetTidyTank(false);
            bw.Tank.liters = 777f;
            MiniExcavator ex = null;
            foreach (var m in FindObjectsByType<MiniExcavator>(FindObjectsSortMode.None)) if (m.GetComponent<BoughtFrom>() == null) ex = m;
            ex.fuel = 7f;
            for (int i = 0; i < 50; i++) yield return new WaitForFixedUpdate();
            pk.Receive(new MaterialLoad(0.3f, 0f)); // after it has settled on the lot (landing shakes some out)
            for (int i = 0; i < 5; i++) yield return null;
            Vector3 exAt = ex.transform.position, pkAt = pk.transform.position, bwAt = bw.transform.position;
            float money = eco.Money, bedAt = pk.load.volume;
            int boughtBefore = FindObjectsByType<BoughtFrom>(FindObjectsSortMode.None).Length;

            var plantS = FindFirstObjectByType<WashPlant>();
            plantS.throughput.level = 2; plantS.recovery.level = 1; plantS.scrubber.level = 3; plantS.hopperSize.level = 1;
            string err = SaveGame.Save("smoke test world", SaveGame.Mode.SinglePlayer);
            Check(err == null, $"saved the world ({err ?? "ok"})");
            var listed = SaveGame.List(SaveGame.Mode.SinglePlayer).Find(s => s.name == "smoke test world");
            Check(listed != null && SaveGame.List(SaveGame.Mode.Multiplayer).Find(s => s.name == "smoke test world") == null, "the save is listed under single player (not multiplayer)");
            if (listed == null) yield break;
            log.AppendLine($"INFO  save file {new FileInfo(listed.path).Length / 1024f:0.0} KB");

            // change everything
            site.Dig(p + Vector3.down * 1.4f, 1.2f, 3f);
            site.Flush(9999);
            float h2 = site.SurfaceY(p);
            eco.Earn(5000f);
            ex.fuel = 40f;
            plantS.throughput.level = 0; plantS.recovery.level = 0; plantS.scrubber.level = 0; plantS.hopperSize.level = 0;
            ex.transform.position += Vector3.right * 6f;
            var exRb = ex.GetComponent<Rigidbody>(); if (exRb != null) exRb.position = ex.transform.position;
            Destroy(pk.gameObject); Destroy(bw.gameObject);
            for (int i = 0; i < 5; i++) yield return null;

            string loadErr = "never finished";
            yield return SaveGame.Load(listed.path, e => loadErr = e);
            Check(loadErr == null, $"loaded the world back ({loadErr ?? "ok"})");
            for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();
            site.Flush(9999);
            float h3 = site.SurfaceY(p);
            Check(h2 < h1 - 0.2f && Mathf.Abs(h3 - h1) < 0.05f, $"the dug ground comes back as saved (saved {h1:0.00}, dug deeper {h2:0.00}, loaded {h3:0.00})");
            Check(Mathf.Abs(eco.Money - money) < 1f, $"money comes back (${eco.Money:N0}, saved ${money:N0})");
            Check(plantS.throughput.level == 2 && plantS.recovery.level == 1 && plantS.scrubber.level == 3 && plantS.hopperSize.level == 1,
                $"the wash plant's upgrades come back (throughput {plantS.throughput.level}, recovery {plantS.recovery.level}, scrubber {plantS.scrubber.level}, hopper {plantS.hopperSize.level}; saved 2 / 1 / 3 / 1)");
            Check(Vector3.Distance(ex.transform.position, exAt) < 0.6f && Mathf.Abs(ex.fuel - 7f) < 0.5f, $"the claim's excavator is back where it was with its fuel ({Vector3.Distance(ex.transform.position, exAt):0.00} m off, {ex.fuel:0.0} L)");
            int boughtAfter = FindObjectsByType<BoughtFrom>(FindObjectsSortMode.None).Length;
            Check(boughtAfter == boughtBefore, $"every bought machine comes back once ({boughtAfter}, saved {boughtBefore})");
            var pk2 = Nearest<DumpPickup>(pkAt, null);
            var bw2 = Nearest<Trailer>(bwAt, null);
            Check(pk2 != null && Vector3.Distance(pk2.transform.position, pkAt) < 0.8f && Mathf.Abs(pk2.fuel - 13f) < 0.5f && !pk2.TidyTankFitted && bedAt > 0.01f && Mathf.Abs(pk2.load.volume - bedAt) < 0.03f,
                  $"the bought pickup comes back: place, fuel, tidy tank off, dirt in the box ({(pk2 != null ? $"{Vector3.Distance(pk2.transform.position, pkAt):0.00} m off, {pk2.fuel:0.0} L, tank {pk2.TidyTankFitted}, {pk2.load.volume:0.00} m³ of {bedAt:0.00}" : "missing")})");
            Check(bw2 != null && Vector3.Distance(bw2.transform.position, bwAt) < 0.8f && bw2.Tank != null && Mathf.Abs(bw2.Tank.liters - 777f) < 1f,
                  $"the bowser comes back with its fuel ({(bw2 != null && bw2.Tank != null ? $"{Vector3.Distance(bw2.transform.position, bwAt):0.00} m off, {bw2.Tank.liters:0} L" : "missing")})");
            SaveGame.Delete(listed);
            Check(!File.Exists(listed.path), "deleting a save removes the file");
        }

        /// <summary>The finer ground: ditches with straight walls, and a save from the old 0.5 m squares loads onto it.</summary>
        IEnumerator FineGroundTests()
        {
            var site = DigSite.Instance;
            Check(site.Cell < 0.14f && site.Detail >= 4, $"the ground is in {site.Cell * 100f:0} cm squares ({site.Detail} to each of the map's)");
            // a ditch in topsoil, dug with a bucket: 1 m deep, its sides straight down, and they stay up
            Vector3 d0 = new Vector3(40f, 0f, -110f);
            for (int pass = 0; pass < 8; pass++)
                for (float s = 0f; s <= 5f; s += 0.15f)
                {
                    Vector3 tip = d0 + new Vector3(0f, 0f, s);
                    tip.y = site.OriginalY(tip) - 1f;
                    site.Dig(tip, 0.3f, 0.08f);
                }
            Vector3 mid = d0 + new Vector3(0f, 0f, 2.5f);
            for (int i = 0; i < 120; i++) yield return null;
            site.SettleNow();
            float inDitch = site.CutDepth(mid), wall = site.CutDepth(mid + Vector3.right * 0.6f);
            Check(inDitch > 0.9f && wall < 0.05f, $"a bucket digs a ditch with straight walls: {inDitch:0.00} m deep in the middle, {wall:0.00} m just 0.6 m to the side, still standing after settling");
            yield return ShotAt(mid + new Vector3(2.2f, 1.6f, -2.2f), mid + Vector3.down * 0.6f, "Screenshots/fine_ditch.png");
            // what you drive on follows the digging: the physics ground in the ditch is down at the dug bottom
            {
                var tc = site.GetComponent<TerrainCollider>();
                bool hit = tc.Raycast(new Ray(mid + Vector3.up * 20f, Vector3.down), out var rh, 60f);
                float gap = hit ? rh.point.y - site.SurfaceY(mid) : 99f;
                Check(hit && Mathf.Abs(gap) < 0.05f, $"the ground you drive on is dug out too ({gap:+0.00;-0.00} m from the drawn bottom of the ditch)");
            }

            // a cell from a save on the old squares: it lands on the same spot, as deep, with its loose dirt kept
            int coarseRes = (site.Res - 1) / site.Detail + 1;
            Vector3 p = new Vector3(30f, 0f, -120f);
            float c0 = site.Cell * site.Detail;
            int ox = Mathf.RoundToInt((p.x - site.transform.position.x) / c0), oz = Mathf.RoundToInt((p.z - site.transform.position.z) / c0);
            p = site.transform.position + new Vector3(ox * c0, 0f, oz * c0);
            float looseBefore = site.TotalLoose();
            var ms = new System.IO.MemoryStream();
            var w = new System.IO.BinaryWriter(ms);
            w.Write(oz * coarseRes + ox);
            w.Write(site.OriginalY(p) - 0.5f); w.Write(0.2f); w.Write(0f); w.Write(0f); w.Write(0f); w.Write(0f); w.Write((byte)1);
            w.Flush(); ms.Position = 0;
            site.ReadCells(new System.IO.BinaryReader(ms), 1, coarseRes);
            float added = site.TotalLoose() - looseBefore, want = 0.2f * c0 * c0;
            float there = site.CutDepth(p) + 0.2f, half = site.CutDepth(p + Vector3.right * c0 * 0.5f) + 0.1f;
            Check(Mathf.Abs(there - 0.5f) < 0.03f && Mathf.Abs(half - 0.25f) < 0.05f && Mathf.Abs(added - want) < want * 0.15f,
                $"an old save's dug square loads onto the finer ground ({there:0.00} m deep where it was, {half:0.00} m halfway to the next, {added:0.000} of {want:0.000} m³ loose dirt)");
            yield return null;
        }

        /// <summary>Dozer blades tilt (one end down) and swivel (angle): an angled blade rolls its dirt off to that side.</summary>
        IEnumerator DozerBladeTests()
        {
            var site = DigSite.Instance;
            var dealer = FindFirstObjectByType<VehicleDealer>();
            int k = 0;
            foreach (var l in dealer.listings)
            {
                if (l.prefab == null || l.prefab.GetComponent<Bulldozer>() == null) continue;
                var d = Instantiate(l.prefab, new Vector3(0f, -50f, 0f), Quaternion.identity).GetComponent<Bulldozer>();
                d.GetComponent<Rigidbody>().isKinematic = true;
                Vector3 st = new Vector3(60f + k * 32f, 0f, -70f); st.y = site.SurfaceY(st); k++;
                d.transform.SetPositionAndRotation(st, Quaternion.identity);
                Physics.SyncTransforms();
                yield return null;
                var sw = d.bladeSwivel != null ? d.bladeSwivel : d.blade;
                // tilt: the left end of the cutting edge drops, the right end rises
                d.SetBlade(0.3f); d.SetBladeAngle(0f); d.SetBladeTilt(d.maxTilt);
                Physics.SyncTransforms();
                Vector3 e = d.bladeEdge.position, r = sw.right * d.bladeWidth * 0.5f;
                float drop = (e + r).y - (e - r).y;
                Check(drop > d.bladeWidth * Mathf.Sin(d.maxTilt * Mathf.Deg2Rad) * 0.8f, $"{d.displayName}: tilting drops the left end ({drop:0.00} m across {d.bladeWidth:0.0} m, {d.BladeTilt:0}°)");
                // angle: the blade swivels about the vertical
                d.SetBladeTilt(0f); d.SetBladeAngle(d.maxAngle);
                Physics.SyncTransforms();
                float yaw = Vector3.SignedAngle(d.transform.right, Vector3.ProjectOnPlane(sw.right, Vector3.up), Vector3.up);
                Check(Mathf.Abs(yaw - d.maxAngle) < 2f, $"{d.displayName}: the blade swivels right {yaw:0}° (max {d.maxAngle:0}°)");
                yield return ShotAt(st + d.transform.forward * (d.bladeWidth * 1.4f + 3f) + Vector3.up * 3f + d.transform.right * 2f, d.bladeEdge.position + Vector3.up * 0.5f, $"Screenshots/dozer_blade_angled_{k}.png");
                // push with it angled right: dirt rolls off the right end, not the left
                d.SetBlade(-0.15f);
                Vector3 fw = d.transform.forward, rt = d.transform.right;
                float before = site.TotalLoose();
                for (int i = 0; i < 300; i++) { Vector3 np = d.transform.position + fw * 0.02f; np.y = site.SurfaceY(np); d.transform.position = np; d.Work(1f, 0.02f); DirtGrains.Tick(0.02f); }
                DirtGrains.FastForward(); site.SettleNow();
                // where the pushed dirt went, sideways from the line the machine drove along
                float sum = 0f, moment = 0f;
                float gs = site.Cell; // (every square once, however fine the ground)
                for (float a = 0f; a <= 12f; a += gs)
                    for (float b = -d.bladeWidth * 1.5f; b <= d.bladeWidth * 1.5f; b += gs)
                    {
                        float v = site.LooseVolume(st + fw * a + rt * b, gs * 0.5f, out _);
                        sum += v; moment += v * b;
                    }
                float side = sum > 1e-4f ? moment / sum : 0f;
                Check(site.TotalLoose() - before > 0.2f && side > 0.25f, $"{d.displayName}: an angled blade pushes its dirt off to the right (heap centre {side:+0.00;-0.00} m from the cut line)");
                yield return ShotAt(st + fw * 8f + rt * 6f + Vector3.up * 4f, st + fw * 3f, $"Screenshots/dozer_windrow_{k}.png");
                d.SetBladeAngle(0f); d.SetBladeTilt(0f);
                Destroy(d.gameObject);
            }
            Check(k >= 3, $"all the dozers were tried ({k})");
        }

        /// <summary>The GMT400 pulls a full bowser up an 18-degree ramp in 4-low, and the drive modes and diff lock behave.</summary>
        IEnumerator PickupHillTests()
        {
            var dealer = FindFirstObjectByType<VehicleDealer>();
            GameObject Prefab(string nm) { foreach (var l in dealer.listings) if (l.name.Contains(nm)) return l.prefab; return null; }
            var pkPrefab = Prefab("GMT400"); var bwPrefab = Prefab("Fuel Bowser");
            if (pkPrefab == null || bwPrefab == null) { Check(false, "GMT400 and bowser for the hill test"); yield break; }
            const float slope = 18f;
            Vector3 basePt = new Vector3(-150f, 400f, 150f);
            Quaternion up = Quaternion.Euler(-slope, 0f, 0f); // facing +z, nose up
            var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ramp.name = "Test ramp";
            ramp.transform.SetPositionAndRotation(basePt + up * new Vector3(0f, -0.5f, 30f), up);
            ramp.transform.localScale = new Vector3(8f, 1f, 80f);
            Physics.SyncTransforms();
            float[] climbed = new float[3];
            for (int run = 0; run < 3; run++)
            {
                // 0: 4-low with a full bowser, 1: 4-high with a full bowser, 2: 2WD alone, rear diff locked vs (logged) open
                var pk = Instantiate(pkPrefab, basePt + up * new Vector3(0f, 1.2f, 12f), up).GetComponent<DumpPickup>();
                var prb = pk.GetComponent<Rigidbody>();
                Trailer bw = null;
                yield return null;
                pk.SetDrive(run == 0 ? DumpPickup.DriveMode.FourLow : run == 1 ? DumpPickup.DriveMode.FourHigh : DumpPickup.DriveMode.TwoHigh, run == 2);
                if (run < 2)
                {
                    var pintle = pk.GetComponentInChildren<Hitch>();
                    bw = Instantiate(bwPrefab, pk.transform.position - pk.transform.forward * 6f, up).GetComponent<Trailer>();
                    yield return null;
                    bw.Tank.liters = bw.Tank.capacity;
                    bw.transform.position += pintle.transform.position - bw.couplerPoint.position;
                    bw.GetComponent<Rigidbody>().position = bw.transform.position;
                    Physics.SyncTransforms();
                    bw.Couple(pintle, true);
                }
                for (int i = 0; i < 40; i++) yield return new WaitForFixedUpdate();
                Vector3 p0 = pk.transform.position;
                pk.TestDrive(1f);
                for (int i = 0; i < 50 * 8; i++) yield return new WaitForFixedUpdate();
                float d = Vector3.Dot(pk.transform.position - p0, up * Vector3.forward);
                climbed[run] = d;
                pk.TestDrive(0f);
                log.AppendLine($"INFO  hill {slope:0}° run {run} ({pk.driveMode}, rear {(pk.rearLocked ? "locked" : "open")}, {(bw != null ? $"bowser {bw.Body.mass:0} kg" : "no trailer")}): climbed {d:0.0} m in 8 s, speed {prb.linearVelocity.magnitude * 3.6f:0} km/h, torque FL {pk.wheels[0].motorTorque:0} RL {pk.wheels[2].motorTorque:0}");
                if (run == 0) yield return ShotAt(pk.transform.position + pk.transform.right * 9f + Vector3.up * 2f - pk.transform.forward * 4f, pk.transform.position - pk.transform.forward * 3f, "Screenshots/pickup_hill_4lo.png");
                if (bw != null) Destroy(bw.gameObject);
                Destroy(pk.gameObject);
                yield return null;
            }
            Check(climbed[0] > 12f, $"in 4-low the GMT400 pulls a full 3,500 L bowser up an {slope:0}° ramp ({climbed[0]:0.0} m in 8 s)");
            Check(climbed[0] > climbed[1] + 1f || climbed[1] < 10f, $"4-low pulls harder than 4-high up the ramp ({climbed[0]:0.0} vs {climbed[1]:0.0} m)");
            Check(climbed[2] > 5f, $"on its own in 2WD with the rear locked it still climbs the ramp ({climbed[2]:0.0} m)");
            Destroy(ramp);

            // --- braking hard with a full bowser on: the trailer brakes itself, the rig stops straight (no push, no jackknife)
            {
                Vector3 padAt = new Vector3(-150f, 400f, -150f);
                var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pad.name = "Test pad"; pad.transform.position = padAt + Vector3.down * 0.5f; pad.transform.localScale = new Vector3(40f, 1f, 400f);
                Physics.SyncTransforms();
                var pk = Instantiate(pkPrefab, padAt + new Vector3(0f, 1.2f, -150f), Quaternion.identity).GetComponent<DumpPickup>();
                yield return null;
                pk.SetDrive(DumpPickup.DriveMode.FourHigh, false);
                var pintle = pk.GetComponentInChildren<Hitch>();
                var bw = Instantiate(bwPrefab, pk.transform.position - Vector3.forward * 6f, Quaternion.identity).GetComponent<Trailer>();
                yield return null;
                bw.Tank.liters = bw.Tank.capacity;
                bw.transform.position += pintle.transform.position - bw.couplerPoint.position;
                bw.Body.position = bw.transform.position;
                Physics.SyncTransforms();
                bw.Couple(pintle, true);
                var prb = pk.GetComponent<Rigidbody>();
                {
                    // hitched: the tongue passes through the truck's back end, but the tank and cabinet still hit the truck
                    Collider tank = null, tongue = null, chassis = pk.GetComponent<BoxCollider>(), cab = null;
                    foreach (var c in bw.GetComponentsInChildren<Collider>()) { if (c.name == "Tank") tank = c; if (c.name == "Tongue") tongue = c; }
                    foreach (var c in pk.GetComponentsInChildren<Collider>()) if (c.name == "Cab Collider") cab = c;
                    bool tankHits = tank != null && chassis != null && !Physics.GetIgnoreCollision(tank, chassis) && cab != null && !Physics.GetIgnoreCollision(tank, cab);
                    bool tongueFree = tongue != null && chassis != null && Physics.GetIgnoreCollision(tongue, chassis);
                    Check(tankHits && tongueFree, $"hitched, the bowser's tank still collides with the truck (only the tongue at the hitch passes through: {tongueFree})");
                }
                for (int i = 0; i < 40; i++) yield return new WaitForFixedUpdate();
                float deckY = bw.transform.position.y - padAt.y;
                pk.TestDrive(1f);
                for (int i = 0; i < 50 * 14 && Vector3.Dot(prb.linearVelocity, pk.transform.forward) < 14f; i++) yield return new WaitForFixedUpdate();
                float v0 = Vector3.Dot(prb.linearVelocity, pk.transform.forward);
                float yaw0 = pk.transform.eulerAngles.y;
                // a little sideways nudge on the trailer as the brakes go on (a bump, a crosswind, a bit of steering)
                bw.Body.AddTorque(Vector3.up * 0.35f, ForceMode.VelocityChange);
                pk.TestDrive(-1f);
                float maxHitch = 0f, maxBrake = 0f; int t = 0;
                for (; t < 50 * 8 && prb.linearVelocity.magnitude > 0.3f; t++)
                {
                    yield return new WaitForFixedUpdate();
                    maxHitch = Mathf.Max(maxHitch, Mathf.Abs(Mathf.DeltaAngle(pk.transform.eulerAngles.y, bw.transform.eulerAngles.y)));
                    maxBrake = Mathf.Max(maxBrake, bw.BrakeLevel);
                }
                pk.TestDrive(0f);
                float yawDrift = Mathf.Abs(Mathf.DeltaAngle(yaw0, pk.transform.eulerAngles.y));
                log.AppendLine($"INFO  hard stop with the bowser: from {v0 * 3.6f:0} km/h in {t / 50f:0.0} s, hitch angle max {maxHitch:0}°, truck yawed {yawDrift:0}°, trailer brakes up to {maxBrake * 100f:0}%, bowser deck {deckY:0.00} m up");
                Check(v0 > 10f && maxHitch < 12f && yawDrift < 8f, $"braking hard from {v0 * 3.6f:0} km/h with a full bowser: no jackknife (hitch {maxHitch:0}°, truck yawed {yawDrift:0}°)");
                Check(maxBrake > 0.3f, $"the bowser's own brakes come on when the truck slows ({maxBrake * 100f:0}%)");
                for (int i = 0; i < 50; i++) yield return new WaitForFixedUpdate();
                Check(bw.BrakeLevel > 0.9f, $"stopped, the bowser's brakes hold it with the truck ({bw.BrakeLevel * 100f:0}%)");
                pk.TestDrive(0.6f);
                Vector3 bp = bw.transform.position;
                for (int i = 0; i < 50 * 2; i++) yield return new WaitForFixedUpdate();
                pk.TestDrive(0f);
                Check(Vector3.Distance(bw.transform.position, bp) > 0.6f && bw.BrakeLevel < 0.2f, $"pulling away, they let go ({Vector3.Distance(bw.transform.position, bp):0.0} m, brakes {bw.BrakeLevel * 100f:0}%)");
                Destroy(bw.gameObject); Destroy(pk.gameObject); Destroy(pad);
                yield return null;
            }
        }

        /// <summary>Parked machines hold on a 30-degree slope; heavy machines pack the loose dirt they cross, light ones hardly.</summary>
        IEnumerator HeavyTests()
        {
            var site = DigSite.Instance;
            var dealer = FindFirstObjectByType<VehicleDealer>();
            // --- parked on a 30 degree ramp, nobody in it, and with the engine idling (occupied machines run the same code)
            const float slope = 30f;
            Vector3 basePt = new Vector3(150f, 420f, -150f);
            Quaternion up = Quaternion.Euler(-slope, 0f, 0f);
            var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ramp.name = "Test ramp 30";
            ramp.transform.SetPositionAndRotation(basePt + up * new Vector3(0f, -0.5f, 0f), up);
            ramp.transform.localScale = new Vector3(14f, 1f, 30f);
            Physics.SyncTransforms();
            string[] names = { "Mining Dozer", "Medium Dozer", "Bulldozer (small", "Compact Track Loader", "Mini Excavator", "Excavator 20" };
            foreach (var nm in names)
            {
                GameObject pf = null; foreach (var l in dealer.listings) if (l.name.Contains(nm)) { pf = l.prefab; break; }
                if (pf == null) { log.AppendLine($"INFO  no {nm} for the slope test"); continue; }
                foreach (float yaw in new[] { 0f, 90f }) // facing up the slope, and across it
                {
                    Quaternion rot = up * Quaternion.Euler(0f, yaw, 0f);
                    var v = Instantiate(pf, basePt + up * new Vector3(0f, 1.0f, 0f), rot).GetComponent<VehicleBase>();
                    var vrb = v.GetComponent<Rigidbody>(); vrb.isKinematic = false;
                    for (int i = 0; i < 50 * 2; i++) yield return new WaitForFixedUpdate(); // settle
                    Vector3 p0 = v.transform.position;
                    for (int i = 0; i < 50 * 5; i++) yield return new WaitForFixedUpdate();
                    float slid = Vector3.Distance(v.transform.position, p0);
                    Check(slid < 0.15f, $"{v.displayName} parked on a {slope:0}° slope ({(yaw == 0f ? "facing up it" : "side-on")}) holds still ({slid:0.00} m in 5 s)");
                    Destroy(v.gameObject);
                    yield return null;
                }
            }
            Destroy(ramp);

            // --- parked side-on on the flank of a loose dirt heap (uneven, faceted ground): no creep sideways
            {
                Vector3 hc = new Vector3(20f, 0f, -150f); hc.y = site.SurfaceY(hc);
                for (int k = 0; k < 14; k++)
                {
                    for (int bx = -6; bx <= 6; bx++)
                        for (int bz = -6; bz <= 6; bz++)
                        {
                            float d = Mathf.Sqrt(bx * bx + bz * bz);
                            if (d > 6.5f - k * 0.4f) continue;
                            Vector3 at = hc + new Vector3(bx * 0.7f, 0f, bz * 0.7f);
                            site.Deposit(new Vector3(at.x, site.SurfaceY(at), at.z), MaterialLoad.Mix(0.12f, 0f, 0f, 0f, 0f));
                        }
                    site.Relax(20000);
                }
                for (int i = 0; i < 30; i++) { site.Relax(20000); yield return null; }
                foreach (var nm in new[] { "Medium Dozer", "Mining Dozer" })
                {
                    GameObject pf = null; foreach (var l in dealer.listings) if (l.name.Contains(nm)) { pf = l.prefab; break; }
                    if (pf == null) continue;
                    Vector3 at = hc + Vector3.right * (nm.StartsWith("Mining") ? 4.5f : 3.6f); at.y = site.SurfaceY(at);
                    Vector3 dn = new Vector3(site.SurfaceY(at + Vector3.right * 0.5f) - site.SurfaceY(at - Vector3.right * 0.5f), 0f, 0f);
                    var v = Instantiate(pf, at + Vector3.up * 0.8f, Quaternion.LookRotation(Vector3.forward)).GetComponent<VehicleBase>();
                    var vrb = v.GetComponent<Rigidbody>(); vrb.isKinematic = false;
                    for (int i = 0; i < 50 * 3; i++) yield return new WaitForFixedUpdate();
                    Vector3 p0 = v.transform.position;
                    float side = Vector3.Angle(v.transform.right, Vector3.ProjectOnPlane(v.transform.right, Vector3.up));
                    for (int i = 0; i < 50 * 15; i++) yield return new WaitForFixedUpdate();
                    float slid = Vector3.ProjectOnPlane(v.transform.position - p0, Vector3.up).magnitude;
                    Check(slid < 0.15f, $"{v.displayName} parked side-on on a heap's flank ({side:0}° side slope) doesn't creep ({slid:0.00} m in 15 s)");
                    Destroy(v.gameObject);
                    yield return null;
                }
            }

            // --- packing a loose berm: the 66 t dozer packs its ruts down, the 1 t stand-on hardly
            foreach (var (nm, heavy) in new[] { ("Mining Dozer", true), ("Stand-On", false) })
            {
                GameObject pf = null; foreach (var l in dealer.listings) if (l.name.Contains(nm)) { pf = l.prefab; break; }
                if (pf == null) { Check(false, $"{nm} for the packing test"); continue; }
                Vector3 st = new Vector3(heavy ? 70f : 100f, 0f, -110f); st.y = site.SurfaceY(st);
                // a low loose berm across the path, 6 m ahead
                Vector3 berm = st + Vector3.forward * 6f;
                for (int bx = -4; bx <= 4; bx++)
                    for (int bz = -1; bz <= 1; bz++)
                    {
                        Vector3 at = berm + new Vector3(bx * 0.8f, 0f, bz * 0.8f);
                        site.Deposit(new Vector3(at.x, site.SurfaceY(at), at.z), MaterialLoad.Mix(0.35f, 0f, 0f, 0f, 0f));
                    }
                for (int i = 0; i < 30; i++) yield return null;
                float c0 = site.CompactedVolume;
                var v = Instantiate(pf, st + Vector3.up * 0.4f, Quaternion.identity).GetComponent<EarthmoverBase>();
                var vrb = v.GetComponent<Rigidbody>(); vrb.isKinematic = false;
                if (v is Bulldozer bd) bd.SetBlade(1f); // blade up: drive over it, don't push it
                if (v is FrontLoader fl) fl.SetPose(fl.armRange.y * 0.6f, 30f);
                for (int i = 0; i < 25; i++) yield return new WaitForFixedUpdate();
                v.TestDrive(0.6f);
                float maxPacked = 0f;
                for (int i = 0; i < 50 * 10 && Vector3.Dot(v.transform.position - berm, Vector3.forward) < 6f; i++)
                {
                    yield return new WaitForFixedUpdate();
                    if (i % 10 == 0) foreach (float lx in new[] { -1.2f, -0.8f, 0.8f, 1.2f }) maxPacked = Mathf.Max(maxPacked, site.PackedAt(new Vector3(v.transform.position.x + lx * (heavy ? 1.6f : 0.6f), 0f, berm.z)));
                }
                v.TestDrive(0f);
                float squeezed = site.CompactedVolume - c0;
                if (heavy)
                {
                    Check(maxPacked > 0.5f && squeezed > 0.05f, $"{v.displayName} packs the loose berm down in its track ruts (packed {maxPacked * 100f:0}%, {squeezed:0.00} m³ squeezed out)");
                    yield return ShotAt(berm + new Vector3(6f, 4f, -3f), berm, "Screenshots/dozer_packed_ruts.png");
                }
                else Check(maxPacked < 0.2f, $"the light {v.displayName} hardly packs it (packed {maxPacked * 100f:0}%, {squeezed:0.00} m³)");
                Destroy(v.gameObject);
                yield return null;
            }
        }

        /// <summary>Skid steers carry a full bucket (curled back, low) through starts, a hard reverse and stops without losing much of it.</summary>
        static string WheelInfo(WheelCollider[] ws)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var w in ws) { if (w == null) continue; bool g = w.GetGroundHit(out WheelHit h); sb.Append($"[{w.rpm:0} {(g ? $"s{h.forwardSlip:0.00} {h.collider.name}" : "air")} mt{w.motorTorque:0.####} en{(w.enabled ? 1 : 0)}]"); }
            return sb.ToString();
        }

        static string Touching(GameObject go, GameObject except)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in go.GetComponentsInChildren<Collider>())
            {
                if (c is WheelCollider || c.isTrigger) continue;
                var b = c.bounds; b.Expand(0.04f);
                foreach (var h in Physics.OverlapBox(b.center, b.extents))
                    if (!h.transform.IsChildOf(go.transform) && !Physics.GetIgnoreCollision(c, h) && Physics.ComputePenetration(c, c.transform.position, c.transform.rotation, h, h.transform.position, h.transform.rotation, out _, out float d) && d > 0.001f)
                        sb.Append($"{c.name}x{h.name}({d:0.00}) ");
            }
            return sb.Length == 0 ? "nothing" : sb.ToString();
        }

        /// <summary>
        /// Multiplayer bits that don't need Steam: a friend's pickup rolls its wheels on your screen as it moves; on a
        /// friend's game the wash plant sends dumped dirt to the host and shows the host's plant.
        /// </summary>
        IEnumerator NetMirrorTests()
        {
            var site = DigSite.Instance;
            var dealer = FindFirstObjectByType<VehicleDealer>();
            var pf = Prefab0(dealer, "GMT400");
            Vector3 at = new Vector3(30f, 0f, -175f); at.y = site.SurfaceY(at);
            var pk = Instantiate(pf, at + Vector3.up * 0.6f, Quaternion.identity).GetComponent<DumpPickup>();
            yield return null;
            pk.SetNetRemote(true);
            var ms0 = new System.IO.MemoryStream(); pk.NetWrite(new System.IO.BinaryWriter(ms0)); ms0.Position = 0; pk.NetRead(new System.IO.BinaryReader(ms0));
            Quaternion r0 = pk.wheelVisuals[0].rotation;
            float s0 = pk.NetWheelSpin(0);
            for (int i = 0; i < 60; i++) { pk.transform.position += pk.transform.forward * 0.08f; yield return null; }
            float rolled = Mathf.DeltaAngle(s0, pk.NetWheelSpin(0));
            float expect = 60 * 0.08f / pk.wheels[0].radius * Mathf.Rad2Deg;
            Check(Mathf.Abs(Mathf.Repeat(expect, 360f) - Mathf.Repeat(pk.NetWheelSpin(0) - s0, 360f)) < 25f && Quaternion.Angle(r0, pk.wheelVisuals[0].rotation) > 5f,
                $"a friend's pickup driving on your screen rolls its wheels ({Mathf.Repeat(pk.NetWheelSpin(0) - s0, 360f):0}° for {Mathf.Repeat(expect, 360f):0}° expected)");
            Destroy(pk.gameObject);

            // a friend tows a trailer: your copy rides on your copy of their truck, follows it and matches its deck
            {
                Vector3 padAt = new Vector3(-150f, 400f, 150f);
                var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pad.name = "Test pad"; pad.transform.position = padAt + Vector3.down * 0.5f; pad.transform.localScale = new Vector3(60f, 1f, 200f);
                Physics.SyncTransforms();
                var trP = Prefab0(dealer, "Tilt-Deck Equipment Trailer");
                Vector3 off = Vector3.right * 20f;
                // their game: the truck and its trailer, running for real
                var pa = Instantiate(pf, padAt + new Vector3(0f, 1.2f, -40f), Quaternion.identity).GetComponent<DumpPickup>();
                // your game: copies of both
                var pb = Instantiate(pf, padAt + off + new Vector3(0f, 1.2f, -40f), Quaternion.identity).GetComponent<DumpPickup>();
                pa.NetId = pb.NetId = 777001;
                yield return null;
                pb.SetNetRemote(true);
                Hitch ha = null, hb = null;
                foreach (var h in pa.GetComponentsInChildren<Hitch>()) if (h.type == HitchType.Pintle) ha = h;
                foreach (var h in pb.GetComponentsInChildren<Hitch>()) if (h.type == HitchType.Pintle) hb = h;
                var ta = Instantiate(trP, pa.transform.position - Vector3.forward * 7f, Quaternion.identity).GetComponent<Trailer>();
                var tb = Instantiate(trP, pb.transform.position - Vector3.forward * 7f, Quaternion.identity).GetComponent<Trailer>();
                ta.NetId = tb.NetId = 777002;
                yield return null;
                ta.transform.position += ha.transform.position - ta.couplerPoint.position; ta.Body.position = ta.transform.position;
                Physics.SyncTransforms();
                ta.Couple(ha, true);
                tb.NetSetRemote(true);
                for (int i = 0; i < 50; i++) yield return new WaitForFixedUpdate();
                System.Func<int, VehicleBase> findB = id => id == pa.NetId ? pb : null;
                void Relay()
                {
                    // what their game sends, applied on yours (the pickup through its own update, the trailer through its)
                    pb.transform.SetPositionAndRotation(pa.transform.position + off, pa.transform.rotation);
                    var m1 = new System.IO.MemoryStream(); pa.NetWrite(new System.IO.BinaryWriter(m1)); m1.Position = 0; pb.NetRead(new System.IO.BinaryReader(m1));
                    var m2 = new System.IO.MemoryStream(); ta.NetWriteState(new System.IO.BinaryWriter(m2)); m2.Position = 0;
                    tb.NetTarget(ta.transform.position + off, ta.transform.rotation);
                    tb.NetReadState(new System.IO.BinaryReader(m2), findB);
                }
                Relay();
                yield return null;
                Check(tb.Hitched == hb && tb.Body.isKinematic && tb.GetComponent<ConfigurableJoint>() == null,
                    $"your copy of a friend's trailer is hitched to your copy of their truck, with no physics of its own (hitched {(tb.Hitched == hb)}, kinematic {tb.Body.isKinematic})");
                pa.TestDrive(0.6f);
                float gapW = 0f, angW = 0f, tS0 = tb.NetWheelSpin(0);
                for (float t = 0f, i = 0; t < 6f; t += Time.deltaTime, i++)
                {
                    Relay();
                    yield return null; // (what you see: measured after the frame's update, as drawn)
                    if (t > 1f) { gapW = Mathf.Max(gapW, Vector3.Distance(tb.couplerPoint.position, hb.transform.position)); angW = Mathf.Max(angW, Quaternion.Angle(tb.transform.rotation, ta.transform.rotation)); }
                }
                pa.TestDrive(-1f);
                for (int i = 0; i < 50 * 3; i++) { yield return new WaitForFixedUpdate(); Relay(); }
                pa.TestDrive(0f);
                Check(gapW < 0.05f && angW < 6f && Vector3.Distance(ta.transform.position, padAt) > 5f, $"it follows their trailer as they tow it, its coupler staying on the hitch ({gapW:0.00} m off, {angW:0.0}° behind)");
                Check(Mathf.Abs(tb.NetWheelSpin(0) - tS0) > 1f, $"and its wheels roll ({tb.NetWheelSpin(0) - tS0:0}°)");
                ta.SetDeckTilt(true);
                for (int i = 0; i < 50 * 5; i++) { yield return new WaitForFixedUpdate(); Relay(); }
                Check(Mathf.Abs(tb.DeckTilt - ta.DeckTilt) < 0.5f && ta.DeckTilt > 5f, $"they tilt the deck: yours tilts too ({tb.DeckTilt:0.0} vs {ta.DeckTilt:0.0}°)");
                ta.SetDeckTilt(false);
                for (int i = 0; i < 50 * 5; i++) { yield return new WaitForFixedUpdate(); Relay(); }
                ta.Uncouple();
                Relay();
                yield return null;
                Check(tb.Hitched == null && hb.Attached == null, "they unhitch it: your copy unhitches too");
                // you take the parked trailer: it runs on your game again
                tb.NetTake();
                Check(!tb.NetRemote && !tb.Body.isKinematic && tb.NetMine, "you use the parked trailer: it runs on your game (they'd follow yours)");
                Destroy(ta.gameObject); Destroy(tb.gameObject); Destroy(pa.gameObject); Destroy(pb.gameObject); Destroy(pad);
                yield return null;
            }

            var plant = FindFirstObjectByType<WashPlant>();
            if (plant == null) { Check(false, "a wash plant for the multiplayer test"); yield break; }
            var keepFwd = WashPlant.NetForwardLoad;
            var saved = new System.IO.MemoryStream(); plant.NetWrite(new System.IO.BinaryWriter(saved));
            float sentVol = 0f;
            WashPlant.NetForwardLoad = (p, l) => sentVol += l.volume;
            plant.hopper = default;
            plant.NetMirror = true;
            plant.Receive(MaterialLoad.Mix(0.8f, 4f, 0f, 0f, 0f));
            Check(Mathf.Abs(sentVol - 0.8f) < 0.01f, $"on a friend's game, dirt dumped in the wash plant goes to the host's plant ({sentVol:0.00} of 0.80 m³ sent)");
            float before = plant.hopper.volume;
            for (int i = 0; i < 30; i++) yield return null;
            Check(Mathf.Abs(plant.hopper.volume - before) < 1e-4f, "a friend's copy of the plant doesn't wash it itself (the host does)");
            // the host's plant, with a friend's load in it, as everyone else's game shows it
            var host = new System.IO.MemoryStream(); var hw = new System.IO.BinaryWriter(host);
            var hostState = MaterialLoad.Mix(2.4f, 9f, 0f, 0f, 0f);
            plant.hopper = hostState; plant.sluiceGold = 3.5f;
            plant.NetWrite(hw);
            plant.hopper = default; plant.sluiceGold = 0f;
            host.Position = 0; plant.NetRead(new System.IO.BinaryReader(host));
            Check(Mathf.Abs(plant.hopper.volume - 2.4f) < 0.01f && Mathf.Abs(plant.sluiceGold - 3.5f) < 0.01f && host.Position == host.Length,
                $"the host's plant shows on everyone's game: hopper {plant.hopper.volume:0.0} m³, sluice {plant.sluiceGold:0.0} g");
            Check(plant.hopperContents == null || plant.hopperContents.gameObject.activeSelf, "and its hopper shows the dirt");
            float g = plant.ApplyNetAction(0);
            Check(Mathf.Abs(g - 3.5f) < 0.01f && plant.sluiceGold == 0f, $"a friend cleaning up the host's sluice gets its gold ({g:0.0} g)");
            plant.NetMirror = false;
            WashPlant.NetForwardLoad = keepFwd;
            saved.Position = 0; plant.NetRead(new System.IO.BinaryReader(saved));
        }

        /// <summary>
        /// Update 8: a controller's sticks and triggers work the controls; the 25 t haul truck climbs a 13 deg ramp
        /// forwards and backing up; the excavator's load lands where the dump marker shows (not under the teeth);
        /// a friend's dump shows dirt falling on your screen; a machine gets chained on a trailer a friend runs.
        /// </summary>
        /// <summary>Update 10: the town over the hill, its customers, the phone and the landscape supply.</summary>
        IEnumerator TownTests()
        {
            var main = DigSite.Instance;
            var jobs = FPSBase.City.Jobs.Instance;
            var eco = Economy.Instance;
            var houses = FPSBase.City.House.All;
            DigSite town = null;
            foreach (var g in DigSite.All) if (g != main) town = g;
            Check(town != null && jobs != null && houses.Count >= 40 && FPSBase.City.Street.All.Count >= 5,
                $"the town is there: its own ground, {houses.Count} houses, {FPSBase.City.Street.All.Count} streets, the customer jobs");
            if (town == null || jobs == null || houses.Count == 0) yield break;
            var h0 = houses[0];
            Check(DigSite.At(new Vector3(h0.driveway.center.x, 0f, h0.driveway.center.y)) == town && DigSite.At(new Vector3(70f, 0f, -12f)) == main, "each place uses the ground it's on (town yards / the mine)");
            Check(DigSite.Ordered()[0] == main && DigSite.Ordered().Count == 2, "the grounds are numbered the same on every game");

            // the town's ground lies on the hills round it everywhere (no squeezed hills in the yards, no 8 m pit by East Ave)
            {
                Terrain hills = null;
                foreach (var t in Terrain.activeTerrains) if (t.GetComponent<DigSite>() == null && t.transform.position.x > 250f && t.transform.position.x < 350f) hills = t;
                float worst = 0f; Vector3 worstAt = Vector3.zero;
                if (hills != null)
                    for (float x = 360f; x <= 580f; x += 4f)
                        for (float z = -100f; z <= 120f; z += 4f)
                        {
                            var p = new Vector3(x, 0f, z);
                            if (!town.Contains(p)) continue;
                            float d = Mathf.Abs(town.OriginalY(p) - (hills.SampleHeight(p) + hills.transform.position.y));
                            if (d > worst) { worst = d; worstAt = p; }
                        }
                Check(hills != null && worst < 0.3f, $"the town's ground meets the hills everywhere (worst {worst:0.00} m off at {worstAt.x:0}, {worstAt.z:0})");
            }
            // an old save's dug town cells move onto the fixed ground, as deep as they were dug
            {
                var nat = (float[,])typeof(DigSite).GetField("native", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(town);
                var ever = (System.Collections.Generic.HashSet<int>)typeof(DigSite).GetField("everChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(town);
                var o = town.transform.position; int res = nat.GetLength(0); float c = 224f / (res - 1);
                Vector3 pit = new Vector3(575f, 0f, 30f), mid = new Vector3(470f, 0f, 30f); // (the old pit by East Ave; the middle of town, flat both times)
                int Idx(Vector3 p) => Mathf.RoundToInt((p.z - o.z) / c) * res + Mathf.RoundToInt((p.x - o.x) / c);
                int ip = Idx(pit), im = Idx(mid);
                float keepP = nat[ip / res, ip % res], keepM = nat[im / res, im % res];
                nat[ip / res, ip % res] = o.y - 0.3f;                    // dug 0.3 m into the old pit's floor
                nat[im / res, im % res] = town.OriginalY(mid) - 0.4f;      // dug 0.4 m
                ever.Add(ip); ever.Add(im);
                town.RebaseOldTownGround();
                Check(Mathf.Abs(town.CutDepth(pit) - 0.3f) < 0.06f && Mathf.Abs(town.CutDepth(mid) - 0.4f) < 0.06f,
                    $"an old save's dug town ground moves onto the fixed ground ({town.CutDepth(pit):0.00} m and {town.CutDepth(mid):0.00} m deep, were 0.30 and 0.40)");
                nat[ip / res, ip % res] = keepP; nat[im / res, im % res] = keepM;
                town.Flush(9999);
            }

            // a driveway job
            int texts0 = jobs.texts.Count;
            var j = jobs.NewRequest(h0, FPSBase.City.JobKind.Driveway);
            Check(j != null && j.pay > 1000f && jobs.texts.Count > texts0 && jobs.Unread > 0, $"{h0.first} at {h0.Address} texts about a gravel driveway for ${j?.pay:N0}");
            GameUI.Instance.ClosePanel();
            jobs.OpenPhone(h0);
            Check(GameUI.Instance.PanelTitle == FPSBase.City.Jobs.PhoneTitle && GameUI.Instance.BlocksGameplay, "the phone opens on their messages");
            yield return null;
            GameUI.Instance.ClosePanel();
            jobs.Accept(j);
            Check(j.state == FPSBase.City.JobState.Accepted && !j.ready && GameObject.Find($"Job stakes: {h0.Address}") != null, "accepting stakes it out, and it isn't done yet");
            var r = h0.driveway;
            // one big pile in the middle: not good enough (it has to be spread)
            DigSite.DropAt(new Vector3(r.center.x, 0f, r.center.y), FPSBase.City.GravelBin.Make(0, r.width * r.height * 0.11f));
            town.Flush(9999);
            jobs.Check(j);
            log.AppendLine($"INFO  driveway with one dumped pile: {j.status}");
            Check(!j.ready, "a dumped pile of gravel on the drive doesn't pass");
            // spread it out evenly (as a skid steer would): take the drive's loose dirt back up and lay it flat
            for (float x = r.xMin + 0.25f; x < r.xMax; x += 0.5f)
                for (float z = r.yMin + 0.25f; z < r.yMax; z += 0.5f)
                    for (int k = 0; k < 4; k++) town.Dig(new Vector3(x, town.OriginalY(new Vector3(x, 0f, z)) + 0.01f, z), 0.4f, 5f);
            for (float x = r.xMin + 0.125f; x < r.xMax; x += 0.25f)
                for (float z = r.yMin + 0.125f; z < r.yMax; z += 0.25f)
                    DigSite.DropAt(new Vector3(x, 0f, z), FPSBase.City.GravelBin.Make(0, 0.0625f * (j.depth + 0.03f)));
            town.Flush(9999);
            jobs.Check(j);
            log.AppendLine($"INFO  driveway spread out: {j.status}");
            float m0 = eco.Money;
            bool paid = jobs.Finish(j);
            Check(paid && j.state == FPSBase.City.JobState.Paid && eco.Money >= m0 + j.pay - 1f, $"spread evenly it passes and pays (${eco.Money - m0:N0})");

            // a ditch job
            var h1 = houses[5];
            var dj = jobs.NewRequest(h1, FPSBase.City.JobKind.Ditch);
            jobs.Accept(dj);
            Check(!dj.ready, "the ditch isn't dug yet");
            Vector3 A = dj.a, B = dj.b;
            log.AppendLine($"INFO  trench at {h1.Address}: {FPSBase.City.Job.Places[dj.place]}, {dj.Length:0.0} m long, {dj.depth:0.0#} m deep, {dj.width * 100f:0} cm wide, ${dj.pay:N0}");
            Check(GameObject.Find($"Job stakes: {h1.Address}")?.transform.Find("Trench Box") != null, "the trench is marked with a see-through box its size");
            {
                // the box follows the ground: every piece's bottom is the trench's depth below the ground under it
                float worst = 0f; int pieces = 0;
                foreach (Transform t in GameObject.Find($"Job stakes: {h1.Address}").transform)
                {
                    if (t.name != "Trench Box") continue;
                    pieces++;
                    float bottom = t.position.y - t.up.y * t.localScale.y / 2f;
                    worst = Mathf.Max(worst, Mathf.Abs(town.OriginalY(t.position) - dj.depth - bottom));
                }
                Check(pieces >= Mathf.CeilToInt(dj.Length) && worst < 0.08f, $"the blue box follows the ground ({pieces} pieces, bottom at most {worst * 100f:0} cm off the depth)");
            }
            {
                // and on a slope too: a trench up the hill at the town's south-east corner
                var slopeJob = jobs.NewRequest(houses[6], FPSBase.City.JobKind.Ditch);
                slopeJob.a = new Vector3(552f, 0f, -96f); slopeJob.b = new Vector3(566f, 0f, -96f); slopeJob.depth = 0.6f; slopeJob.width = 0.8f;
                jobs.Accept(slopeJob);
                var root = GameObject.Find($"Job stakes: {houses[6].Address}");
                float worst = 0f, rise = town.OriginalY(slopeJob.b) - town.OriginalY(slopeJob.a);
                if (root != null)
                    foreach (Transform t in root.transform)
                        if (t.name == "Trench Box")
                            worst = Mathf.Max(worst, Mathf.Abs(town.OriginalY(t.position) - slopeJob.depth - (t.position.y - t.up.y * t.localScale.y / 2f)));
                Check(root != null && rise > 1f && worst < 0.1f, $"on a slope ({rise:0.0} m rise) the box still follows the ground ({worst * 100f:0} cm off)");
                jobs.Cancel(slopeJob);
            }
            DigTrench(town, dj);
            jobs.Check(dj);
            log.AppendLine($"INFO  ditch dug: {dj.status}");
            float dm0 = eco.Money;
            Check(dj.ready && jobs.Finish(dj), "dug to its depth and width, the trench passes and pays");
            Check(dj.damage <= 0f ? eco.Money >= dm0 + dj.pay - 1f : true, $"dug carefully, nothing's damaged and it pays in full (damage {dj.damage:P0}, hits {string.Join(",", dj.hits)})");
            Check(town.CutDepth(Vector3.Lerp(A, B, 0.5f)) > 0.3f && main.SurfaceY(new Vector3(70f, 0f, -12f)) > -1f, "the ditch is in the town's ground (the mine's isn't touched)");

            // trenches vary: where on the lot, how deep, how wide, how long; and they never go through the house or the drive
            {
                var places = new System.Collections.Generic.HashSet<int>(); var depths = new System.Collections.Generic.HashSet<float>(); var widths = new System.Collections.Generic.HashSet<float>();
                float minL = 999f, maxL = 0f; bool allFit = true;
                for (int k = 0; k < houses.Count; k++)
                {
                    var tj = jobs.NewRequest(houses[k], FPSBase.City.JobKind.Ditch);
                    if (tj == null) continue;
                    places.Add(tj.place); depths.Add(tj.depth); widths.Add(tj.width); minL = Mathf.Min(minL, tj.Length); maxL = Mathf.Max(maxL, tj.Length);
                    var f = houses[k].Footprint;
                    for (float t = 0f; t <= 1f; t += 0.05f) { var q = Vector3.Lerp(tj.a, tj.b, t); if (f.Contains(new Vector2(q.x, q.z)) || houses[k].driveway.Contains(new Vector2(q.x, q.z))) allFit = false; }
                    jobs.Decline(tj);
                }
                log.AppendLine($"INFO  trench variety: places {string.Join(",", places)}, depths {string.Join(",", depths)}, widths {string.Join(",", widths)}, lengths {minL:0.0}..{maxL:0.0} m");
                Check(places.Count >= 4 && depths.Count >= 4 && widths.Count >= 3 && maxL - minL > 5f, "trenches come in different places, depths, widths and lengths");
                Check(allFit, "no trench goes under a house or across a driveway");
            }

            // the town isn't copy-paste: different sizes, roofs, colours
            {
                var sizes = new System.Collections.Generic.HashSet<string>(); var colours = new System.Collections.Generic.HashSet<Material>(); var roofs = new System.Collections.Generic.HashSet<string>();
                foreach (var hh in houses)
                {
                    sizes.Add($"{hh.houseLocal.width:0.0}x{hh.houseLocal.height:0.0}");
                    var wl = hh.transform.Find("Walls"); if (wl != null) colours.Add(wl.GetComponent<Renderer>().sharedMaterial);
                    var rf = hh.transform.Find("Roof"); if (rf != null) { var bs = rf.GetComponent<Renderer>().bounds.size; roofs.Add($"{bs.x:0.0}/{bs.y:0.0}/{bs.z:0.0}"); }
                }
                log.AppendLine($"INFO  houses: {sizes.Count} sizes, {colours.Count} wall colours, {roofs.Count} roof shapes");
                Check(sizes.Count > 30 && colours.Count >= 8 && roofs.Count > 10, "the houses are all different (sizes, colours, roofs)");
                Check(FindObjectsByType<FPSBase.City.StreetSign>(FindObjectsSortMode.None).Length >= 7, "street signs at the corners and a welcome board");
                Check(houses[20].Directions.Contains(houses[20].street) && houses[20].Directions.Contains("house on your"), $"directions to the house: {houses[20].Directions}");
            }

            // hitting the mailbox on a job takes a share off the bill
            {
                var hd = houses[22];
                var mj = jobs.NewRequest(hd, FPSBase.City.JobKind.Ditch);
                jobs.Accept(mj);
                var box = hd.Part("mailbox");
                Check(box != null, "the mailbox can be damaged");
                if (box != null)
                {
                    Quaternion r0 = hd.mailbox.rotation;
                    jobs.Damage(hd, box, "Skid Steer");
                    jobs.Damage(hd, box, "Skid Steer"); // the same thing twice only counts once
                    Check(Mathf.Abs(mj.damage - box.penalty) < 0.001f && mj.hits.Count == 1 && Quaternion.Angle(r0, hd.mailbox.rotation) > 30f,
                        $"knocking the mailbox over takes {mj.damage:P0} off the bill and it falls over");
                    Check(jobs.texts.Exists(t => t.house == hd.index && t.body.Contains("mailbox")), "the customer texts about it");
                    // the save keeps the damage
                    var ms = new MemoryStream(); jobs.Write(new BinaryWriter(ms)); ms.Position = 0; jobs.Read(new BinaryReader(ms), true);
                    var mb = jobs.ById(mj.id);
                    Check(mb != null && mb.hits.Contains("mailbox") && Mathf.Abs(mb.damage - box.penalty) < 0.001f, "the damage is in the save (and sent to friends)");
                    // they hold back that share when it's paid
                    DigTrench(town, mb);
                    float pm = eco.Money;
                    bool ok = jobs.Finish(mb);
                    log.AppendLine($"INFO  damaged job: {mb.status}; paid ${eco.Money - pm:N0} of ${mb.pay:N0}");
                    Check(ok && Mathf.Abs((eco.Money - pm) - mb.pay * (1f - mb.damage)) < 2f, $"the bill is paid less the damage (${eco.Money - pm:N0} of ${mb.pay:N0})");
                }
                // with no job there, they bill you
                var hn = houses[30]; var post = hn.Part("mailbox");
                float bm = eco.Money;
                if (post != null) jobs.Damage(hn, post, "Pickup");
                Check(post != null && (eco.Unlimited || Mathf.Abs(bm - eco.Money - post.fine) < 1f), "wreck a mailbox with no job there and they bill you");
            }

            // the house's lot is hard under the house
            Vector3 under = new Vector3(h1.Footprint.center.x, 0f, h1.Footprint.center.y);
            float before = town.SurfaceY(under);
            town.Dig(under + Vector3.down * 0.8f, 0.5f, 2f);
            Check(Mathf.Abs(town.SurfaceY(under) - before) < 0.02f, "you can't dig under a house");

            // the landscape supply loads a truck
            var bin = FindFirstObjectByType<FPSBase.City.GravelBin>();
            var dealer = FindFirstObjectByType<VehicleDealer>();
            var pf = Prefab0(dealer, "Single-Axle Dump");
            if (bin != null && pf != null)
            {
                Vector3 at = bin.chute.position; at.y = 0.3f;
                var tk = Instantiate(pf, at + Vector3.up * 0.4f, Quaternion.Euler(0f, 90f, 0f)).GetComponent<DumpPickup>();
                // the bed under the chute
                Vector3 bed = tk.transform.InverseTransformPoint(tk.tailgatePour != null ? tk.tailgatePour.position : tk.transform.position);
                tk.transform.position -= tk.transform.TransformVector(new Vector3(0f, 0f, bed.z * 0.5f));
                var trb = tk.GetComponent<Rigidbody>(); if (trb != null) trb.position = tk.transform.position;
                for (int i = 0; i < 60; i++) yield return new WaitForFixedUpdate();
                eco.Earn(5000f);
                float before2 = eco.Money;
                Check(bin.Buy(0, 3f), "bought 3 m³ of gravel at the landscape supply");
                for (int i = 0; i < 50 * 6; i++) yield return new WaitForFixedUpdate();
                log.AppendLine($"INFO  truck under the bin: {tk.load.volume:0.00} m³ in the bed ({tk.load.gravel:0.00} gravel), paid ${before2 - eco.Money:N0}");
                Check(tk.load.gravel > 2.4f && (eco.Unlimited || Mathf.Abs(before2 - eco.Money - 3f * bin.gravelPrice) < 1f), $"the gravel pours into the truck's bed ({tk.load.gravel:0.0} m³) and costs ${3f * bin.gravelPrice:N0}");
                Destroy(tk.gameObject);
                for (int i = 0; i < 5; i++) yield return null;
            }
            else Check(false, "found the landscape supply and the dump truck");

            // drive the road over the hill to town
            {
                var hp = Prefab0(dealer, "GMT400");
                Vector3 a = new Vector3(222f, 0f, 21.6f);
                Vector3 b = new Vector3(366f, 0f, 10f);
                Vector3 dir = (b - a).normalized;
                var pk = Instantiate(hp, VehicleDealer.GroundAt(a) + Vector3.up * 0.8f, Quaternion.LookRotation(dir)).GetComponent<DumpPickup>();
                for (int i = 0; i < 50; i++) yield return new WaitForFixedUpdate();
                pk.TestDrive(0.7f);
                float lowest = float.MaxValue, highest = float.MinValue;
                for (int i = 0; i < 50 * 18; i++)
                {
                    yield return new WaitForFixedUpdate();
                    lowest = Mathf.Min(lowest, pk.transform.position.y); highest = Mathf.Max(highest, pk.transform.position.y);
                    if (Vector3.Dot(pk.transform.position - a, dir) > 140f) break;
                }
                pk.TestDrive(0f);
                float went = Vector3.Dot(pk.transform.position - a, dir);
                log.AppendLine($"INFO  pickup on the road to town: {went:0} m along, {pk.transform.position}, height {lowest:0.0}..{highest:0.0}, up {pk.transform.up.y:0.00}");
                Check(went > 100f && pk.transform.up.y > 0.9f, $"the road through the pass drives to town ({went:0} m)");
                Destroy(pk.gameObject);
                for (int i = 0; i < 5; i++) yield return null;
            }

            // the map picture, the machine icons (Update 11)
            {
                var mv = MapView.Instance;
                Check(mv != null, "the map is there");
                if (mv != null)
                {
                    mv.BakeAll();
                    var hp = houses[0].transform.position;
                    Check(mv.Baked && mv.World.Contains(new Vector2(hp.x, hp.z)) && mv.World.Contains(new Vector2(main.FieldRect.center.x, main.FieldRect.center.y)),
                        $"the map covers the mine and the town ({mv.World})");
                }
                var kinds = new System.Collections.Generic.HashSet<string>();
                foreach (var v in FPSBase.Vehicles.VehicleBase.All) { kinds.Add(MapView.KindOf(v)); Check(MapView.IconFor(v) != null, $"{v.displayName} has a map icon ({MapView.KindOf(v)})"); }
                log.AppendLine($"INFO  machine icons: {string.Join(", ", kinds)}");
                Check(MapView.Arrow != null && GameUI.MinimapRect.width > 100f, "players are arrows, and the minimap sits bottom right");
            }

            // multiplayer jobs: a friend takes one just for themself (Update 11)
            {
                var J = FPSBase.City.Jobs.ActAccept;
                var h3 = houses[14];
                var fj = jobs.NewRequest(h3, FPSBase.City.JobKind.Ditch);
                jobs.ApplyAction(fj.id, FPSBase.City.Jobs.ActAccept, false, 777UL, "Friend");
                Check(fj.state == FPSBase.City.JobState.Accepted && fj.owner == 777UL && fj.ownerName == "Friend" && !fj.shared,
                    "a friend can take a job just for themself (it's theirs, with their name)");
                bool friendText = jobs.texts.Exists(t => t.house == h3.index && t.from == "Friend");
                Check(friendText, "their answer shows in the conversation with their name");

                jobs.ApplyAction(fj.id, FPSBase.City.Jobs.ActFinish, false, 555UL, "Someone else");
                jobs.ApplyAction(fj.id, FPSBase.City.Jobs.ActCancel, false, 555UL, "Someone else");
                Check(fj.state == FPSBase.City.JobState.Accepted, "someone else can't finish or cancel a friend's own job");
                jobs.ApplyAction(fj.id, FPSBase.City.Jobs.ActFinish, false, 777UL, "Friend");
                Check(fj.state == FPSBase.City.JobState.Paid, "the friend who took it can finish it");
                // the list as the host sends it: owners and names come through, and this phone keeps its read marks
                var gj = jobs.NewRequest(houses[15], FPSBase.City.JobKind.Driveway);
                jobs.ApplyAction(gj.id, J, true, 888UL, "Pal");
                var ms = new MemoryStream(); jobs.Write(new BinaryWriter(ms));
                for (int i = 0; i < jobs.texts.Count; i++) { var t = jobs.texts[i]; t.read = true; jobs.texts[i] = t; }
                int unread0 = jobs.Unread, nj = jobs.jobs.Count;
                ms.Position = 0; jobs.Read(new BinaryReader(ms), true);
                var back2 = jobs.ById(gj.id);
                Check(back2 != null && back2.owner == 888UL && back2.ownerName == "Pal" && jobs.jobs.Count == nj && jobs.Unread == unread0,
                    $"the host's job list reads back on a friend's phone (owner {back2?.ownerName}, {jobs.Unread} unread)");
                Check(GameObject.Find($"Job stakes: {houses[15].Address}") != null, "a friend's phone stakes out the jobs too");
                jobs.ApplyAction(gj.id, FPSBase.City.Jobs.ActCancel, false, 888UL, "Pal"); yield return null;
                Check(GameObject.Find($"Job stakes: {houses[15].Address}") == null, "and pulls the stakes when it's cancelled");
            }

            // saved and loaded: the jobs and the town's ground
            var h2 = houses[9];
            var sj = jobs.NewRequest(h2, FPSBase.City.JobKind.Ditch);
            jobs.Accept(sj);
            Vector3 dp = Vector3.Lerp(sj.a, sj.b, 0.5f);
            town.Dig(new Vector3(dp.x, town.OriginalY(dp) - 0.7f, dp.z), 0.6f, 3f);
            town.Flush(9999);
            float cut = town.CutDepth(dp);
            int nJobs = jobs.jobs.Count, nTexts = jobs.texts.Count;
            string err = SaveGame.Save("smoke test town", SaveGame.Mode.SinglePlayer);
            var listed = SaveGame.List(SaveGame.Mode.SinglePlayer).Find(x => x.name == "smoke test town");
            Check(err == null && listed != null, $"saved with the town ({err ?? "ok"})");
            if (listed == null) yield break;
            jobs.Cancel(sj);
            town.Dig(new Vector3(dp.x, town.OriginalY(dp) - 1.5f, dp.z), 0.6f, 3f);
            town.Flush(9999);
            string loadErr = "never finished";
            yield return SaveGame.Load(listed.path, e => loadErr = e);
            for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();
            town.Flush(9999);
            var back = jobs.OpenJobAt(h2);
            Check(back != null && (back.a - sj.a).sqrMagnitude < 0.01f && (back.b - sj.b).sqrMagnitude < 0.01f && Mathf.Abs(back.width - sj.width) < 0.001f && back.place == sj.place,
                "the trench's place, length and width come back from the save");
            Check(loadErr == null && back != null && back.state == FPSBase.City.JobState.Accepted && jobs.jobs.Count == nJobs && jobs.texts.Count == nTexts,
                $"jobs and texts come back from the save ({jobs.jobs.Count} jobs, {jobs.texts.Count} texts; saved {nJobs} / {nTexts})");
            Check(Mathf.Abs(town.CutDepth(dp) - cut) < 0.05f, $"the town's dug ground comes back ({town.CutDepth(dp):0.00} m deep, saved {cut:0.00})");
            Check(GameObject.Find($"Job stakes: {h2.Address}") != null, "the accepted job's stakes come back");
            File.Delete(listed.path);
        }

        /// <summary>Dig a job's trench out to its depth across its width (as an excavator would).</summary>
        static void DigTrench(DigSite town, FPSBase.City.Job j)
        {
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
            town.Flush(9999);
        }

        /// <summary>Update 11: both dump trucks tow the equipment trailers (gravel in the box, a skid steer behind).</summary>
        IEnumerator DumpHitchTests()
        {
            var dealer = FindFirstObjectByType<VehicleDealer>();
            GameObject Prefab(string nm) { foreach (var l in dealer.listings) if (l.name.Contains(nm)) return l.prefab; return null; }
            var tiltPrefab = Prefab("Tilt-Deck Equipment Trailer");
            foreach (var truckName in new[] { "Single-Axle Dump Truck", "Haul Truck 25t" })
            {
                var tp = Prefab(truckName);
                if (tp == null || tiltPrefab == null) { Check(false, $"{truckName} and the tilt trailer are on sale"); continue; }
                Vector3 padAt = new Vector3(150f, 400f, truckName.StartsWith("Haul") ? 250f : -250f);
                var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pad.name = "Test pad"; pad.transform.position = padAt + Vector3.down * 0.5f; pad.transform.localScale = new Vector3(40f, 1f, 260f);
                Physics.SyncTransforms();
                var tk = Instantiate(tp, padAt + new Vector3(0f, 1.5f, -60f), Quaternion.identity).GetComponent<DumpPickup>();
                yield return null;
                for (int i = 0; i < 60; i++) yield return new WaitForFixedUpdate();
                Hitch hitch = null;
                foreach (var h in tk.GetComponentsInChildren<Hitch>()) if (h.type == HitchType.Pintle) hitch = h;
                Check(hitch != null, $"the {truckName} has a pintle hook");
                if (hitch == null) { Destroy(tk.gameObject); Destroy(pad); continue; }
                var trl = Instantiate(tiltPrefab, tk.transform.position - Vector3.forward * 9f, Quaternion.identity).GetComponent<Trailer>();
                yield return null;
                trl.transform.position += hitch.transform.position - trl.couplerPoint.position;
                trl.Body.position = trl.transform.position;
                Physics.SyncTransforms();
                trl.Couple(hitch, true);
                for (int i = 0; i < 100; i++) yield return new WaitForFixedUpdate();
                float pitch = Mathf.Abs(Mathf.DeltaAngle(0f, trl.transform.eulerAngles.x));
                Check(trl.Hitched == hitch && pitch < 4f, $"the tilt trailer hitches to the {truckName} and sits level ({pitch:0.0} deg)");
                // a load of gravel in the box, then drive off with the trailer behind
                tk.Receive(FPSBase.City.GravelBin.Make(0, tk.capacity * 0.8f));
                Vector3 t0 = trl.transform.position;
                tk.TestDrive(1f);
                for (int i = 0; i < 50 * 8; i++) yield return new WaitForFixedUpdate();
                tk.TestDrive(0f);
                for (int i = 0; i < 50 * 3; i++) yield return new WaitForFixedUpdate();
                float moved = Vector3.Distance(trl.transform.position, t0);
                float gap = Vector3.Distance(trl.couplerPoint.position, hitch.transform.position);
                log.AppendLine($"INFO  {truckName} towing: trailer moved {moved:0.0} m, coupler gap {gap:0.00} m, trailer up {trl.transform.up.y:0.00}");
                Check(trl.Hitched == hitch && moved > 10f && gap < 0.4f && trl.transform.up.y > 0.95f, $"the {truckName} pulls the loaded trailer ({moved:0} m)");
                trl.Uncouple();
                Check(trl.Hitched == null && hitch.Attached == null, "and unhitches it at the customer's");
                Destroy(trl.gameObject); Destroy(tk.gameObject); Destroy(pad);
                for (int i = 0; i < 5; i++) yield return null;
            }
        }

        IEnumerator Update9Tests()
        {
            var dealer = FindFirstObjectByType<VehicleDealer>();
            var site = DigSite.Instance;
            // --- controller: with a menu up the sticks drive the pointer, not the player
            {
                var ins = UnityEngine.InputSystem.InputSystem.settings;
                var bg0 = ins.backgroundBehavior; var ed0 = ins.editorInputBehaviorInPlayMode;
                ins.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
                ins.editorInputBehaviorInPlayMode = UnityEngine.InputSystem.InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                var gp = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();
                gp.MakeCurrent();
                yield return null;
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(gp, new UnityEngine.InputSystem.LowLevel.GamepadState { leftStick = new Vector2(0f, 1f), rightTrigger = 1f });
                UnityEngine.InputSystem.InputSystem.Update();
                float free = Controls.Axis(GameAction.MoveForward, GameAction.MoveBack);
                GameUI.Instance.OpenPanel("test", () => GUILayout.Label("test"));
                float onMenu = Controls.Axis(GameAction.MoveForward, GameAction.MoveBack);
                float gasOnMenu = Controls.Axis(GameAction.DriveForward, GameAction.DriveBack);
                GameUI.Instance.ClosePanel();
                Check(free > 0.9f && Mathf.Abs(onMenu) < 0.01f && Mathf.Abs(gasOnMenu) < 0.01f,
                    $"controller: the left stick walks ({free:0.00}) but not with a menu open ({onMenu:0.00}, gas {gasOnMenu:0.00})");
                UnityEngine.InputSystem.InputSystem.RemoveDevice(gp);
                ins.backgroundBehavior = bg0; ins.editorInputBehaviorInPlayMode = ed0;
            }

            // --- how deep the 90 t can reach, and the ground lets it
            {
                var pf = Prefab0(dealer, "Mining Excavator");
                Check(pf != null, "found the 90 t mining excavator");
                if (pf != null)
                {
                    var ex = Instantiate(pf, new Vector3(-300f, 600f, -300f), Quaternion.identity).GetComponent<MiniExcavator>();
                    var rb = ex.GetComponent<Rigidbody>(); if (rb != null) rb.isKinematic = true;
                    yield return null;
                    var T = typeof(MiniExcavator);
                    var fb = T.GetField("boom", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var fs = T.GetField("stick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var fk = T.GetField("bucket", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var ap = T.GetMethod("ApplyPose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    float baseY = float.MaxValue;
                    foreach (var c in ex.GetComponentsInChildren<Collider>()) if (!c.isTrigger) baseY = Mathf.Min(baseY, c.bounds.min.y);
                    float low = float.MaxValue;
                    for (float b = ex.boomRange.x; b <= ex.boomRange.y; b += 2.5f)
                        for (float st = ex.stickRange.x; st <= ex.stickRange.y; st += 5f)
                        {
                            fb.SetValue(ex, b); fs.SetValue(ex, st); fk.SetValue(ex, -60f); ap.Invoke(ex, null);
                            low = Mathf.Min(low, ex.bucketTip.position.y);
                        }
                    float reach = baseY - low;
                    log.AppendLine($"INFO  90 t excavator: deepest the bucket teeth reach is {reach:0.0} m below its tracks; ground goes {site.bedrockDepth:0.#} m to bedrock on the claim, {site.offClaimDigDepth:0.#} m off it");
                    Check(reach > 5f && site.bedrockDepth >= reach, $"the 90 t can dig as deep as its arm reaches ({reach:0.0} m, bedrock at {site.bedrockDepth:0.#} m)");
                    Destroy(ex.gameObject);
                }
                // dig a deep hole on the claim
                Vector2 c2 = site.claimRect.center + new Vector2(-30f, 25f);
                Vector3 at = new Vector3(c2.x, 0f, c2.y); at.y = site.SurfaceY(at);
                float top = at.y;
                for (int i = 0; i < 400; i++)
                {
                    float sy = site.SurfaceY(at);
                    site.BladePush(at + Vector3.up * 0.2f, Vector3.forward, 2.5f, sy - 0.6f, 6f, out _);
                    if (sy < top - 11.5f) break;
                }
                float deep = top - site.SurfaceY(at);
                Check(deep > 9f, $"the claim can be dug 9+ m deep ({deep:0.0} m)");
                float g45 = 0f, g10 = 0f;
                for (int i = 0; i < 20; i++) { g45 += site.GradeAt(c2.x + i, c2.y, 4.4f); g10 += site.GradeAt(c2.x + i, c2.y, 10f); }
                Check(g10 > g45, $"the gravel gets richer toward bedrock ({g45 / 20f:0.0} g/m3 at 4.4 m, {g10 / 20f:0.0} at 10 m)");
            }

            // --- a fully unlatched dump truck empties in a few seconds
            foreach (var nm in new[] { "Single-Axle Dump", "Haul Truck" })
            {
                var pf = Prefab0(dealer, nm);
                if (pf == null) { Check(false, $"found the {nm}"); continue; }
                var t = Instantiate(pf, new Vector3(-260f, 600f, -260f), Quaternion.identity).GetComponent<DumpPickup>();
                var rb = t.GetComponent<Rigidbody>(); if (rb != null) rb.isKinematic = true;
                t.load = new MaterialLoad(t.capacity, 0f);
                t.gateSetting = 0;
                yield return null;
                float tStart = Time.time; float tiltNow = 0f;
                while (!t.load.IsEmpty && Time.time - tStart < 30f)
                {
                    tiltNow = Mathf.Min(t.maxTilt, tiltNow + t.tiltSpeed * Time.deltaTime);
                    t.SetTilt(tiltNow);
                    yield return null;
                }
                // from when it starts to pour (the box reaching the pour angle)
                float secs = Time.time - tStart - t.pourStartTilt / t.tiltSpeed;
                Check(t.load.IsEmpty && secs < 6f, $"{nm} ({t.capacity:0.#} m3), gate fully open: the load is out {secs:0.0} s after it starts to pour");
                Destroy(t.gameObject);
                for (int i = 0; i < 5; i++) yield return null;
            }

            // --- a dragged bucket reshapes unbroken ground
            {
                Vector2 c2 = site.claimRect.center + new Vector2(25f, -30f);
                Vector3 at = new Vector3(c2.x, 0f, c2.y);
                float y0 = site.SurfaceY(at);
                float oldWay = site.Spread(at + Vector3.up * 0.3f, Vector3.right, 2f, y0 - 0.3f, 0.6f, 0f);
                float moved = 0f;
                for (int i = 0; i < 40; i++) moved += site.Spread(at + Vector3.right * (i * 0.05f), Vector3.right, 2f, y0 - 0.3f, 0.6f, 1.2f / 50f);
                float y1 = site.SurfaceY(at);
                Check(oldWay < 0.001f && moved > 0.2f && y1 < y0 - 0.12f,
                    $"dragging the bucket over undug ground shaves it (before: {oldWay:0.000} m3; now {moved:0.00} m3, ground down {y0 - y1:0.00} m)");
            }
            yield break;
        }

        IEnumerator Update8Tests()
        {
            var dealer = FindFirstObjectByType<VehicleDealer>();
            // --- controller
            {
                var ins = UnityEngine.InputSystem.InputSystem.settings;
                var bg0 = ins.backgroundBehavior; var ed0 = ins.editorInputBehaviorInPlayMode;
                ins.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
                ins.editorInputBehaviorInPlayMode = UnityEngine.InputSystem.InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                var gp = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();
                gp.MakeCurrent();
                yield return null;
                void PadState(UnityEngine.InputSystem.LowLevel.GamepadState st) { UnityEngine.InputSystem.InputSystem.QueueStateEvent(gp, st); UnityEngine.InputSystem.InputSystem.Update(); }
                PadState(new UnityEngine.InputSystem.LowLevel.GamepadState { rightTrigger = 1f, leftStick = new Vector2(-0.6f, 0f) });
                log.AppendLine($"INFO  virtual pad: current {(UnityEngine.InputSystem.Gamepad.current == gp)}, enabled {gp.enabled}, RT {gp.rightTrigger.ReadValue():0.00}, L-stick {gp.leftStick.ReadValue()}");
                float thr = Controls.Axis(GameAction.DriveForward, GameAction.DriveBack), st = Controls.Axis(GameAction.SteerRight, GameAction.SteerLeft);
                Check(thr > 0.95f && st < -0.3f && st > -0.9f, $"controller: RT is the gas ({thr:0.00}), the left stick steers in proportion ({st:0.00} for 60% left)");
                PadState(new UnityEngine.InputSystem.LowLevel.GamepadState().WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.Y));
                Check(Controls.Pressed(GameAction.Tailgate), "controller: Y works the tailgate");
                PadState(new UnityEngine.InputSystem.LowLevel.GamepadState { rightStick = new Vector2(0f, -1f) });
                float boom = Controls.Axis(GameAction.BoomUp, GameAction.BoomDown);
                Controls.PadLooking();
                float boomLooking = Controls.Axis(GameAction.BoomUp, GameAction.BoomDown);
                Check(boom > 0.9f && Mathf.Abs(boomLooking) < 0.01f, $"controller: right stick back raises the boom ({boom:0.00}), not while L3 looks around with it ({boomLooking:0.00})");
                PadState(new UnityEngine.InputSystem.LowLevel.GamepadState());
                yield return null; yield return null;
                Check(Mathf.Abs(Controls.Axis(GameAction.DriveForward, GameAction.DriveBack)) < 0.01f, "controller: let go, nothing pushed");
                UnityEngine.InputSystem.InputSystem.RemoveDevice(gp);
                ins.backgroundBehavior = bg0; ins.editorInputBehaviorInPlayMode = ed0;
            }

            // --- the 25 t haul truck: back it up the wash plant's loading ramp (12.7 deg, 8 m), then steeper test ramps
            {
                var hp = Prefab0(dealer, "Haul Truck");
                Transform rampT = null;
                foreach (var tr in FindObjectsByType<Transform>(FindObjectsSortMode.None)) if (tr.name == "Ramp" && tr.parent != null && tr.parent.name == "WashPlant Loading Ramp") rampT = tr;
                Check(rampT != null, "found the wash plant's loading ramp");
                IEnumerator Run(Vector3 start, Quaternion rot, float throttle, bool old, float seconds, System.Action<float> got)
                {
                    var t = Instantiate(hp, start + Vector3.up * 1.2f, rot).GetComponent<DumpPickup>();
                    if (old) { t.lowGearBoost = 1f; t.rearLocked = false; }
                    t.load = new MaterialLoad(t.capacity, 0f);
                    for (int i = 0; i < 75; i++) yield return new WaitForFixedUpdate();
                    float y0 = t.wheels[t.wheels.Length - 1].transform.position.y;
                    t.TestDrive(throttle);
                    float best = 0f;
                    for (int i = 0; i < 50 * seconds; i++)
                    {
                        yield return new WaitForFixedUpdate();
                        // how high the last axle got (backing up: it leads the way up)
                        best = Mathf.Max(best, (throttle < 0f ? t.wheels[t.wheels.Length - 1] : t.wheels[0]).transform.position.y - y0);
                    }
                    t.TestDrive(0f);
                    got(best);
                    Destroy(t.gameObject);
                    for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();
                }
                if (rampT != null)
                {
                    // up the slope = the ramp's +x; back up it: nose pointing down the slope, the rear axles going up first
                    Vector3 up = Vector3.ProjectOnPlane(rampT.right, Vector3.up).normalized;
                    float slant = rampT.localScale.x;
                    Vector3 bottom = rampT.position - rampT.right * slant * 0.5f;
                    Vector3 start = VehicleDealer.GroundAt(bottom - up * 7.5f);
                    Quaternion back = Quaternion.LookRotation(-up);
                    float hOld = 0f, hNew = 0f;
                    yield return Run(start, back, -1f, true, 14, h => hOld = h);
                    yield return Run(start, back, -1f, false, 14, h => hNew = h);
                    log.AppendLine($"INFO  backing the full haul truck up the wash plant ramp: its back axle got {hOld:0.00} m up before the fix, {hNew:0.00} m now (the deck is 1.8 m up)");
                    Check(hNew > 1.5f, $"the 25 t haul truck, full, backs up the wash plant ramp ({hNew:0.00} m up)");
                }
                foreach (float ang in new[] { 18f, 22f })
                {
                    Vector3 padAt = new Vector3(300f, 400f, ang * 20f);
                    var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    pad.transform.position = padAt + Vector3.down * 0.5f; pad.transform.localScale = new Vector3(80f, 1f, 120f);
                    const float len = 18f;
                    float rise = len * Mathf.Sin(ang * Mathf.Deg2Rad), runL = len * Mathf.Cos(ang * Mathf.Deg2Rad);
                    var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    ramp.transform.rotation = Quaternion.Euler(-ang, 0f, 0f);
                    ramp.transform.localScale = new Vector3(6f, 0.5f, len);
                    ramp.transform.position = padAt + new Vector3(0f, rise * 0.5f, runL * 0.5f) - ramp.transform.up * 0.25f;
                    var top = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    top.transform.localScale = new Vector3(6f, rise, 16f);
                    top.transform.position = padAt + new Vector3(0f, rise * 0.5f, runL + 8f);
                    Physics.SyncTransforms();
                    float hOld = 0f, hF = 0f, hB = 0f;
                    yield return Run(padAt + new Vector3(0f, 0f, -8f), Quaternion.identity, 1f, true, 18, h => hOld = h);
                    yield return Run(padAt + new Vector3(0f, 0f, -8f), Quaternion.identity, 1f, false, 18, h => hF = h);
                    yield return Run(padAt + new Vector3(0f, 0f, -8f), Quaternion.Euler(0f, 180f, 0f), -1f, false, 18, h => hB = h);
                    log.AppendLine($"INFO  full haul truck on a {ang:0} deg slope ({rise:0.0} m up): before the fix {hOld:0.0} m; now forwards {hF:0.0} m, backing {hB:0.0} m");
                    if (ang < 20f)
                    {
                        Check(hF > rise - 0.6f, $"the full haul truck drives up an {ang:0} deg hill ({hF:0.0} of {rise:0.0} m)");
                        Check(hB > rise - 0.6f, $"and backs up it ({hB:0.0} of {rise:0.0} m)");
                    }
                    Destroy(pad); Destroy(ramp); Destroy(top);
                    yield return null;
                }
            }

            // --- the excavator's load lands where the marker points (where the open bucket's mouth will be)
            {
                Vector3 padAt = new Vector3(420f, 400f, 0f);
                var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pad.transform.position = padAt + Vector3.down * 0.5f; pad.transform.localScale = new Vector3(60f, 1f, 60f);
                Physics.SyncTransforms();
                var ex = Instantiate(Prefab0(dealer, "Excavator 20 t"), padAt + Vector3.up * 0.4f, Quaternion.identity).GetComponent<MiniExcavator>();
                for (int i = 0; i < 50; i++) yield return new WaitForFixedUpdate();
                ex.SetPose(35f, -80f, -130f, 0f); // carrying: boom up, bucket curled in
                yield return null;
                float aim = Mathf.Lerp(ex.dumpAngle, ex.bucketRange.y, 0.5f);
                Vector3 P = ex.DumpPoint(aim), teeth = ex.bucketTip.position;
                Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);
                log.AppendLine($"INFO  bucket curled: the teeth are {Vector2.Distance(Flat(teeth), Flat(P)):0.00} m (across the ground) from where the load will come out");
                var tk = Instantiate(Prefab0(dealer, "Single-Axle Dump Truck"), padAt + new Vector3(0f, 1f, 9f), Quaternion.Euler(0f, 90f, 0f)).GetComponent<DumpPickup>();
                yield return null;
                Transform bedV = null; foreach (var tr in tk.GetComponentsInChildren<Transform>()) if (tr.name == "BedVolume") bedV = tr;
                Vector3 bc = bedV != null ? bedV.position : tk.transform.position;
                tk.transform.position += new Vector3(P.x - bc.x, 0f, P.z - bc.z); tk.GetComponent<Rigidbody>().position = tk.transform.position;
                Physics.SyncTransforms();
                for (int i = 0; i < 50 * 2; i++) yield return new WaitForFixedUpdate();
                float load = ex.bucketCapacity;
                ex.bucketLoad = new MaterialLoad(load, 0f);
                yield return null;
                ex.SetPose(35f, -80f, aim, 0f); // open it to dump
                for (int i = 0; i < 50 * 6; i++) yield return new WaitForFixedUpdate();
                DirtGrains.FastForward();
                yield return null;
                Check(tk.load.volume > load * 0.8f, $"lined up on the marker, the excavator's dump lands in the truck ({tk.load.volume:0.00} of {load:0.00} m³ in the bed)");
                Destroy(ex.gameObject); Destroy(tk.gameObject); Destroy(pad);
                yield return null;
            }

            // --- a friend tips their dump truck: you see the dirt falling (just for show: no dirt lands twice)
            {
                Vector3 at = new Vector3(470f, 400f, 0f);
                var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pad.transform.position = at + Vector3.down * 0.5f; pad.transform.localScale = new Vector3(30f, 1f, 30f);
                Physics.SyncTransforms();
                var b = Instantiate(Prefab0(dealer, "Single-Axle Dump Truck"), at + Vector3.up * 0.8f, Quaternion.identity).GetComponent<DumpPickup>();
                yield return null;
                b.load = new MaterialLoad(b.capacity, 0f); b.SetTilt(50f);
                var m1 = new System.IO.MemoryStream(); b.NetWrite(new System.IO.BinaryWriter(m1));
                b.load = new MaterialLoad(b.capacity * 0.8f, 0f);
                var m2 = new System.IO.MemoryStream(); b.NetWrite(new System.IO.BinaryWriter(m2));
                b.SetNetRemote(true);
                m1.Position = 0; b.NetRead(new System.IO.BinaryReader(m1));
                int g0 = DirtGrains.Live; float poured0 = b.Poured;
                m2.Position = 0; b.NetRead(new System.IO.BinaryReader(m2));
                int most = 0;
                for (int i = 0; i < 40; i++) { yield return null; most = Mathf.Max(most, DirtGrains.Live - g0); }
                Check(most > 10 && b.Poured == poured0, $"a friend tipping their truck: dirt falls out of the tailgate on your screen ({most} clods; this copy poured {b.Poured - poured0:0.00} m³ itself)");
                Destroy(b.gameObject); Destroy(pad);
                yield return null;
            }

            // --- a friend sits in the truck: the trailer is theirs, but your machine still gets chained on it
            {
                Vector3 at = new Vector3(520f, 400f, 0f);
                var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pad.transform.position = at + Vector3.down * 0.5f; pad.transform.localScale = new Vector3(40f, 1f, 40f);
                Physics.SyncTransforms();
                var t = Instantiate(Prefab0(dealer, "Tilt-Deck Equipment Trailer"), at + Vector3.up * 1f, Quaternion.identity).GetComponent<Trailer>();
                for (int i = 0; i < 50 * 2; i++) yield return new WaitForFixedUpdate();
                t.NetSetRemote(true);
                var m = Instantiate(Prefab0(dealer, "Compact Track Loader"), t.deckArea.position + Vector3.up * 0.8f, t.transform.rotation).GetComponent<VehicleBase>();
                for (int i = 0; i < 50 * 4 && !t.Carries(m); i++) yield return new WaitForFixedUpdate();
                Check(t.NetRemote && t.Carries(m), $"on a trailer a friend's game runs (they're sitting in the truck), your parked machine is chained down (remote {t.NetRemote}, chained {t.Carries(m)})");
                Trailer.ReleaseCargo(m);
                Destroy(m.gameObject); Destroy(t.gameObject); Destroy(pad);
                yield return null;
            }
        }

        static GameObject Prefab0(VehicleDealer dealer, string n) { foreach (var l in dealer.listings) if (l.name.Contains(n)) return l.prefab; return null; }

        IEnumerator SkidCarryTests()
        {
            var site = DigSite.Instance;
            var dealer = FindFirstObjectByType<VehicleDealer>();
            int k = 0;
            foreach (var l in dealer.listings)
            {
                if (l.prefab == null) continue;
                var fl0 = l.prefab.GetComponent<FrontLoader>();
                if (fl0 == null || !fl0.skidSteer) continue;
                Vector3 st = new Vector3(40f + k * 16f, 0f, -130f); st.y = site.SurfaceY(st); k++;
                var fl = Instantiate(l.prefab, st + Vector3.up * 0.4f, Quaternion.identity).GetComponent<FrontLoader>();
                var frb = fl.GetComponent<Rigidbody>(); frb.isKinematic = false;
                fl.SetPose(Mathf.Lerp(fl.armRange.x, fl.armRange.y, 0.2f), fl.tiltRange.x); // carry: low and curled right back
                fl.bucketLoad = MaterialLoad.Mix(fl.bucketCapacity, 0f, 0f, 0f, 0f);
                var ccb = fl.GetComponent<ClumpContainer>();
                for (int i = 0; i < 60; i++) yield return new WaitForFixedUpdate();
                float full = fl.bucketLoad.volume;
                log.AppendLine($"INFO  {fl.displayName} carry: capacity {fl.bucketCapacity:0.00}, after settling {full:0.00} (spilled {(ccb != null ? ccb.Spilled : 0f):0.00}), mouth up {(ccb != null ? ccb.MouthUp : 0f):0.00}, box half {(ccb != null ? ccb.Half : Vector3.zero)}, clumps {(ccb != null ? ccb.Count : 0)}, arm {fl.Arm:0}");
                float[] th = { 0.8f, -0.8f, 1f, 0f };
                float[] secs = { 3f, 1.5f, 2f, 1.5f };
                for (int s = 0; s < th.Length; s++)
                {
                    fl.TestDrive(th[s]);
                    for (int i = 0; i < 50 * secs[s]; i++) yield return new WaitForFixedUpdate();
                }
                fl.TestDrive(0f);
                float lost = (full - fl.bucketLoad.volume) / Mathf.Max(1e-4f, full);
                Check(full > fl.bucketCapacity * 0.85f && lost < 0.12f, $"{fl.displayName} carries a full bucket ({full:0.00} m³) through a start, a hard reverse and a stop, losing {lost * 100f:0}%");
                Destroy(fl.gameObject);
                yield return null;
            }
            Check(k >= 3, $"all the skid steers were tried ({k})");

            // a bucket at 16 deg (the HUD calls it flat) pushed into a heap, the machine stalled against it: it fills
            {
                var pf = Prefab0(dealer, "Wheeled Skid Steer");
                Vector3 hc = new Vector3(40f, 0f, -170f); hc.y = site.SurfaceY(hc);
                for (int k2 = 0; k2 < 10; k2++)
                {
                    for (int bx = -4; bx <= 4; bx++)
                        for (int bz = -4; bz <= 4; bz++)
                        {
                            if (Mathf.Sqrt(bx * bx + bz * bz) > 4.5f - k2 * 0.4f) continue;
                            Vector3 at = hc + new Vector3(bx * 0.6f, 0f, bz * 0.6f);
                            site.Deposit(new Vector3(at.x, site.SurfaceY(at), at.z), MaterialLoad.Mix(0.1f, 0f, 0f, 0f, 0f));
                        }
                    site.Relax(20000);
                }
                var fl = Instantiate(pf, Vector3.down * 300f, Quaternion.identity).GetComponent<FrontLoader>();
                var frb = fl.GetComponent<Rigidbody>(); frb.isKinematic = true;
                fl.SetPose(fl.armRange.x, 16f);
                float reach = Vector3.Dot(fl.bucketTip.position - fl.transform.position, fl.transform.forward);
                // bucket lip just into the toe of the heap, and stopped there (speed 0, throttle on)
                Vector3 pos = hc - Vector3.forward * (reach + 2.2f);
                pos.y = site.SurfaceY(pos); fl.transform.position = pos;
                Physics.SyncTransforms();
                fl.TestDrive(1f);
                float dt = 0.02f;
                for (int i = 0; i < 100 && !site.IsUnderground(fl.bucketTip.position + Vector3.up * 0.12f); i++) { fl.transform.position += Vector3.forward * 0.03f; Physics.SyncTransforms(); }
                for (int i = 0; i < 150; i++) fl.Work(0.6f, 0f, dt); // what the drive passes in when the throttle's on and the pile has stopped it
                Check(fl.bucketLoad.volume > fl.bucketCapacity * 0.5f, $"{fl.displayName}: bucket at 16° shoved into a pile fills ({fl.bucketLoad.volume:0.00} of {fl.bucketCapacity:0.00} m³)");
                Destroy(fl.gameObject);
                yield return null;
            }
        }

        /// <summary>
        /// The two equipment trailers behind the GMT400: the 7 t bumper-pull tilts its deck down to the ground, a skid
        /// steer drives on, the deck levels and it's chained; the 10 t gooseneck goes on the ball fitted in the bed,
        /// lowers its ramps, a track loader drives up them and is chained. Both haul their load away. Only the skid
        /// steers and the 5 t excavator are carried.
        /// </summary>
        IEnumerator EquipTrailerTests()
        {
            var dealer = FindFirstObjectByType<VehicleDealer>();
            var pi = FindFirstObjectByType<PlayerInteractor>();
            GameObject Prefab(string nm) { foreach (var l in dealer.listings) if (l.name.Contains(nm)) return l.prefab; return null; }
            var pkPrefab = Prefab("GMT400");
            var tiltPrefab = Prefab("Tilt-Deck Equipment Trailer"); var goosePrefab = Prefab("Gooseneck Equipment Trailer");
            if (pkPrefab == null || tiltPrefab == null || goosePrefab == null) { Check(false, "the pickup and both equipment trailers are on sale"); yield break; }

            // who rides: the 3 skid steers and the 5 t excavator; not the mini excavator or the big machines
            {
                var tr = tiltPrefab.GetComponent<Trailer>();
                string ok = "", bad = "";
                foreach (var n in new[] { "Stand-On Mini Track Loader", "Wheeled Skid Steer", "Compact Track Loader", "Compact Excavator 5 t" })
                { var p = Prefab(n); if (p == null || !tr.CanCarry(p.GetComponent<VehicleBase>())) bad += n + "; "; else ok += n + "; "; }
                foreach (var n in new[] { "Mini Excavator", "Excavator 20 t", "Medium Dozer" })
                { var p = Prefab(n); if (p != null && tr.CanCarry(p.GetComponent<VehicleBase>())) bad += "(should refuse) " + n + "; "; }
                Check(bad == "", $"the equipment trailers take the skid steers and the 5 t excavator, and nothing else ({ok}{bad})");
            }

            for (int which = 0; which < 2; which++)
            {
                bool goose = which == 1;
                Vector3 padAt = new Vector3(150f, 400f, goose ? 250f : -250f);
                var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pad.name = "Test pad"; pad.transform.position = padAt + Vector3.down * 0.5f; pad.transform.localScale = new Vector3(40f, 1f, 200f);
                Physics.SyncTransforms();
                var pk = Instantiate(pkPrefab, padAt + new Vector3(0f, 1.2f, 30f), Quaternion.identity).GetComponent<DumpPickup>();
                var prb = pk.GetComponent<Rigidbody>();
                yield return null;
                for (int i = 0; i < 50; i++) yield return new WaitForFixedUpdate();
                pk.SetDrive(DumpPickup.DriveMode.FourHigh, false);
                Hitch hitch = null;
                if (goose)
                {
                    var fit = FindOption(pk, "Fit the gooseneck ball");
                    Check(fit != null && fit.enabled, "the pickup's menu offers to fit the gooseneck ball (box empty and down)");
                    fit?.run(pi);
                    Check(pk.GooseneckFitted && !pk.AcceptsAt(pk.bedVolume != null ? pk.bedVolume.transform.TransformPoint(pk.bedVolume.center) : pk.transform.position), "ball fitted: the dump box is locked and takes no dirt");
                    foreach (var h in pk.GetComponentsInChildren<Hitch>()) if (h.type == HitchType.Gooseneck) hitch = h;
                }
                else foreach (var h in pk.GetComponentsInChildren<Hitch>()) if (h.type == HitchType.Pintle) hitch = h;
                if (hitch == null) { Check(false, $"the pickup has a {(goose ? "gooseneck ball" : "pintle hook")}"); Destroy(pk.gameObject); Destroy(pad); continue; }
                var trl = Instantiate(goose ? goosePrefab : tiltPrefab, pk.transform.position - Vector3.forward * 8f, Quaternion.identity).GetComponent<Trailer>();
                yield return null;
                trl.transform.position += hitch.transform.position - trl.couplerPoint.position;
                trl.Body.position = trl.transform.position;
                Physics.SyncTransforms();
                trl.Couple(hitch, true);
                Check(trl.Hitched == hitch, $"the {trl.displayName} hitches to the pickup's {(goose ? "gooseneck ball" : "pintle hook")}");
                for (int i = 0; i < 50 * 2; i++) yield return new WaitForFixedUpdate();
                float pitch = Mathf.Abs(Mathf.DeltaAngle(0f, trl.transform.eulerAngles.x));
                log.AppendLine($"INFO  {trl.displayName}: hitched pitch {pitch:0.0} deg, deck {trl.deckArea.position.y - padAt.y:0.00} m up, pickup pitch {Mathf.DeltaAngle(0f, pk.transform.eulerAngles.x):0.0}");
                Check(pitch < 3f, $"hitched and parked, the {trl.displayName} sits level ({pitch:0.0} deg)");
                if (goose) Check(!pk.SetGooseneck(false), "the ball can't come out with the trailer on it");

                // get the deck ready to load
                Vector3 rearEnd() => trl.deckArea.position - trl.deckArea.forward * trl.deckSize.y * 0.5f;
                if (goose)
                {
                    var o = FindOption(trl, "Lower the loading ramps");
                    Check(o != null && o.enabled, "the gooseneck's menu offers to lower the ramps");
                    o?.run(pi);
                    for (int i = 0; i < 50 * 4; i++) yield return new WaitForFixedUpdate();
                    float tipMax = 0f;
                    foreach (var r in trl.ramps) tipMax = Mathf.Max(tipMax, r.TransformPoint(Vector3.up * 2.4f).y - padAt.y);
                    Check(trl.RampsDown > 0.99f && tipMax < 0.2f, $"ramps down: their ends rest on the ground ({tipMax:0.00} m up)");
                }
                else
                {
                    var o = FindOption(trl, "Tilt the deck down");
                    Check(o != null && o.enabled, "the tilt trailer's menu offers to tilt the deck");
                    o?.run(pi);
                    for (int i = 0; i < 50 * 5; i++) yield return new WaitForFixedUpdate();
                    float rearY = rearEnd().y - padAt.y;
                    Check(trl.DeckTilt > trl.tiltMax - 0.1f && rearY < 0.25f, $"deck tilted {trl.DeckTilt:0.0} deg: its back edge is down at the ground ({rearY:0.00} m up)");
                }
                yield return ShotAt(trl.transform.position + trl.transform.right * 7f + Vector3.up * 2.5f - trl.transform.forward * 5f, rearEnd(),
                    goose ? "Screenshots/equip_goose_ramps.png" : "Screenshots/equip_tilt_down.png");

                // drive a machine on over the back
                var mPrefab = Prefab(goose ? "Compact Track Loader" : "Wheeled Skid Steer");
                var m = Instantiate(mPrefab, Vector3.down * 400f, Quaternion.identity).GetComponent<EarthmoverBase>();
                Vector3 start = rearEnd() - trl.transform.forward * 3.2f; start.y = padAt.y + 0.3f;
                m.transform.SetPositionAndRotation(start, Quaternion.LookRotation(trl.transform.forward));
                if (m is FrontLoader carry)
                {
                    float baseY = start.y - 0.3f, a = carry.armRange.x;
                    for (; a < carry.armRange.y; a += 1f) { carry.SetPose(a, 30f); if (carry.bucketTip != null && carry.bucketTip.position.y > baseY + 0.6f) break; }
                }
                var mrb = m.GetComponent<Rigidbody>(); mrb.isKinematic = false; mrb.position = m.transform.position; mrb.rotation = m.transform.rotation;
                Physics.SyncTransforms();
                for (int i = 0; i < 50; i++) yield return new WaitForFixedUpdate();
                m.TestDrive(0.6f);
                for (int i = 0; i < 50 * 14; i++)
                {
                    yield return new WaitForFixedUpdate();
                    Vector3 l = trl.deckArea.InverseTransformPoint(m.transform.position);
                    if (i % 50 == 0) log.AppendLine($"INFO  {m.displayName} loading t {i / 50}: deck-local {l} vel {mrb.linearVelocity.magnitude:0.00}");
                    if (l.z > -0.2f) break;
                }
                m.TestDrive(0f);
                for (int i = 0; i < 50 * 2; i++) yield return new WaitForFixedUpdate();
                if (goose) { FindOption(trl, "Fold the ramps up")?.run(pi); }
                else
                {
                    Check(FindOption(trl, "Level the deck") is InteractOption lv && lv.enabled, "loaded: the menu offers to level the deck");
                    FindOption(trl, "Level the deck")?.run(pi);
                }
                for (int i = 0; i < 50 * 5; i++) yield return new WaitForFixedUpdate();
                Vector3 onDeck = trl.deckArea.InverseTransformPoint(m.transform.position);
                Check(trl.Carries(m) && Mathf.Abs(onDeck.y) < 0.45f && trl.DeckTilt < 0.1f,
                    $"the {m.displayName} drives on and is chained down (deck-local {onDeck.x:0.0}, {onDeck.y:0.00}, {onDeck.z:0.0}; tilt {trl.DeckTilt:0.0}, ramps {trl.RampsDown:0.0})");
                if (!goose)
                {
                    var td = FindOption(trl, "Tilt the deck down");
                    Check(td != null && td.enabled, "with a machine chained on, the menu still offers to tilt the deck");
                    td?.run(pi);
                    for (int i = 0; i < 50 * 5; i++) yield return new WaitForFixedUpdate();
                    float mPitch = Mathf.Abs(Mathf.DeltaAngle(0f, m.transform.eulerAngles.x));
                    Vector3 onTilted = trl.deckArea.InverseTransformPoint(m.transform.position);
                    Check(trl.DeckTilt > trl.tiltMax - 0.5f && trl.Carries(m) && Mathf.Abs(mPitch - trl.DeckTilt) < 3f && Vector3.Distance(onTilted, onDeck) < 0.1f,
                        $"tilted with the machine chained on: it tilts with the deck ({mPitch:0.0} vs {trl.DeckTilt:0.0} deg) and stays put on it");
                    FindOption(trl, "Level the deck")?.run(pi);
                    for (int i = 0; i < 50 * 5; i++) yield return new WaitForFixedUpdate();
                    Check(trl.DeckTilt < 0.1f && trl.Carries(m), "levelled again, still chained");
                }

                // haul it
                Vector3 c0 = m.transform.position;
                pk.TestDrive(0.6f);
                for (int i = 0; i < 50 * 7; i++) yield return new WaitForFixedUpdate();
                {
                    // let off the gas: it coasts, the trailer brakes stay off
                    float vC0 = Vector3.Dot(prb.linearVelocity, pk.transform.forward), trBrake = 0f;
                    pk.TestCoast();
                    for (int i = 0; i < 50 * 2; i++)
                    {
                        yield return new WaitForFixedUpdate(); trBrake = Mathf.Max(trBrake, trl.BrakeLevel);
                        if (i % 10 == 0) log.AppendLine($"INFO  coast t {i / 50f:0.0}: {Vector3.Dot(prb.linearVelocity, pk.transform.forward) * 3.6f:0.0} km/h, trailer {Vector3.Dot(trl.Body.linearVelocity, trl.transform.forward) * 3.6f:0.0} km/h brakes {trl.BrakeLevel * 100f:0}%, pedal {pk.BrakePedal}, truck wheel brake {pk.wheels[0].brakeTorque:0}/{pk.wheels[2].brakeTorque:0}, trailer wheel brake {trl.wheels[0].brakeTorque:0}, pk at {pk.transform.position - padAt}, tilt {trl.DeckTilt:0.0}, trl euler {trl.transform.eulerAngles}, m kin {m.GetComponent<Rigidbody>().isKinematic} parent {(m.transform.parent != null ? m.transform.parent.name : "-")}, touching {Touching(trl.gameObject, pad)} / truck {Touching(pk.gameObject, pad)}, rpm truck {WheelInfo(pk.wheels)} trailer {WheelInfo(trl.wheels)}");
                    }
                    float vC1 = Vector3.Dot(prb.linearVelocity, pk.transform.forward);
                    float decel = (vC0 - vC1) / 2f;
                    Check(vC0 > 3f && decel < 1.3f && trBrake < 0.05f, $"letting off the gas it coasts ({vC0 * 3.6f:0} to {vC1 * 3.6f:0} km/h in 2 s, {decel:0.00} m/s²), trailer brakes off ({trBrake * 100f:0}%)");
                    pk.TestDrive(0.6f);
                    for (int i = 0; i < 50; i++) yield return new WaitForFixedUpdate();
                }
                pk.TestDrive(-1f);
                for (int i = 0; i < 50 * 4 && prb.linearVelocity.magnitude > 0.3f; i++) yield return new WaitForFixedUpdate();
                pk.TestDrive(0f);
                for (int i = 0; i < 50 * 2; i++) yield return new WaitForFixedUpdate();
                Vector3 still = trl.deckArea.InverseTransformPoint(m.transform.position);
                float hitchAng = Mathf.Abs(Mathf.DeltaAngle(pk.transform.eulerAngles.y, trl.transform.eulerAngles.y));
                Check(Vector3.Distance(m.transform.position, c0) > 8f && trl.Carries(m) && Mathf.Abs(still.z - onDeck.z) < 0.2f && hitchAng < 10f,
                    $"the pickup hauls the {m.displayName} away and stops ({Vector3.Distance(m.transform.position, c0):0.0} m, slid {Mathf.Abs(still.z - onDeck.z):0.00} m, hitch {hitchAng:0} deg, load {trl.CargoTons:0.0} t of {trl.payloadTons:0.0})");
                yield return ShotAt(trl.transform.position + trl.transform.right * 8f + Vector3.up * 3.5f + trl.transform.forward * 3f, trl.deckArea.position,
                    goose ? "Screenshots/equip_goose_loaded.png" : "Screenshots/equip_tilt_loaded.png");
                Trailer.ReleaseCargo(m);
                Destroy(m.gameObject);

                if (!goose)
                {
                    // the 5 t excavator chained on: pulls away on the flat in 2WD (no 4-low needed)
                    var exP = Prefab("Compact Excavator 5 t");
                    var ex = Instantiate(exP, trl.deckArea.position + trl.deckArea.up * 0.3f, trl.transform.rotation).GetComponent<VehicleBase>();
                    var exrb = ex.GetComponent<Rigidbody>(); exrb.isKinematic = false;
                    for (int i = 0; i < 50 * 3 && !trl.Carries(ex); i++) yield return new WaitForFixedUpdate();
                    Check(trl.Carries(ex), $"the {ex.displayName} is chained on the tilt trailer");
                    pk.SetDrive(DumpPickup.DriveMode.TwoHigh, false);
                    for (int i = 0; i < 50; i++) yield return new WaitForFixedUpdate();
                    Vector3 s0 = pk.transform.position;
                    pk.TestDrive(1f);
                    float v5 = 0f;
                    for (int i = 0; i < 50 * 8; i++) { yield return new WaitForFixedUpdate(); if (i == 50 * 5) v5 = Vector3.Dot(prb.linearVelocity, pk.transform.forward); }
                    float v8 = Vector3.Dot(prb.linearVelocity, pk.transform.forward);
                    pk.TestDrive(-1f);
                    for (int i = 0; i < 50 * 6 && prb.linearVelocity.magnitude > 0.3f; i++) yield return new WaitForFixedUpdate();
                    pk.TestDrive(0f);
                    log.AppendLine($"INFO  2WD with the 5 t excavator on: {v5 * 3.6f:0} km/h after 5 s, {v8 * 3.6f:0} after 8 s, {Vector3.Distance(pk.transform.position, s0):0} m; trailer {trl.CargoTons:0.0} t on, rig {(prb.mass + trl.Body.mass) / 1000f:0.0} t");
                    Check(v5 > 4.2f && v8 > 6f, $"2WD on the flat pulls the 5 t excavator on the tilt trailer briskly ({v5 * 3.6f:0} km/h after 5 s, {v8 * 3.6f:0} after 8 s)");
                    Trailer.ReleaseCargo(ex);
                    Destroy(ex.gameObject);

                    // tip the trailer over hitched, reposition the truck: the trailer comes back upright behind it
                    trl.transform.rotation = Quaternion.AngleAxis(70f, trl.transform.forward) * trl.transform.rotation;
                    trl.Body.rotation = trl.transform.rotation;
                    Physics.SyncTransforms();
                    for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();
                    pk.Reposition();
                    for (int i = 0; i < 50 * 2; i++) yield return new WaitForFixedUpdate();
                    float roll = Vector3.Angle(trl.transform.up, pk.transform.up); // (the reposition spot may be on a slope: upright with the truck)
                    log.AppendLine($"INFO  after reposition: trailer euler {trl.transform.eulerAngles}, truck euler {pk.transform.eulerAngles}, trailer y {trl.transform.position.y - padAt.y:0.00}, truck y {pk.transform.position.y - padAt.y:0.00}");
                    float gapH = Vector3.Distance(trl.couplerPoint.position, hitch.transform.position);
                    Check(roll < 8f && trl.Hitched == hitch && gapH < 0.4f, $"reposition the truck: its trailer comes back upright behind it ({roll:0} deg off the truck's upright, coupler {gapH:0.00} m from the hitch)");
                }
                Destroy(trl.gameObject); Destroy(pk.gameObject); Destroy(pad);
                yield return null;
            }

            // a bowser tipped on its side stays put (it used to glide along on its side)
            {
                Vector3 padAt = new Vector3(150f, 400f, 0f);
                var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pad.name = "Test pad"; pad.transform.position = padAt + Vector3.down * 0.5f; pad.transform.localScale = new Vector3(40f, 1f, 40f);
                Physics.SyncTransforms();
                var bw = Instantiate(Prefab("Fuel Bowser"), padAt + Vector3.up * 1.6f, Quaternion.Euler(0f, 0f, 90f)).GetComponent<Trailer>();
                for (int i = 0; i < 50 * 2; i++) yield return new WaitForFixedUpdate();
                Vector3 b0 = bw.transform.position;
                for (int i = 0; i < 50 * 5; i++) yield return new WaitForFixedUpdate();
                float glide = Vector3.ProjectOnPlane(bw.transform.position - b0, Vector3.up).magnitude;
                Check(bw.Tipped && glide < 0.2f, $"a bowser tipped on its side stays put ({glide:0.00} m in 5 s)");
                var up = FindOption(bw, "Set it back upright");
                Check(up != null, "a tipped trailer's menu offers to set it back upright");
                up?.run(pi);
                for (int i = 0; i < 50 * 2; i++) yield return new WaitForFixedUpdate();
                Check(!bw.Tipped && Vector3.Angle(bw.transform.up, Vector3.up) < 8f, $"set back upright ({Vector3.Angle(bw.transform.up, Vector3.up):0} deg)");
                Destroy(bw.gameObject); Destroy(pad);
                yield return null;
            }

            var gb = FindFirstObjectByType<GoldBuyer>();
            Check(gb != null && Mathf.Abs(gb.pricePerGram * 0.01f - 75f) < 0.01f, $"gold sells at $75 per 0.01 g (${(gb != null ? gb.pricePerGram : 0f):0}/g)");
        }

        void Finish()
        {
            log.AppendLine(fails == 0 ? "ALL PASSED" : $"{fails} FAILED");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            File.WriteAllText(reportPath, log.ToString());
            Done = true;
        }

        /// <summary>
        /// A real dig cycle through the controls, like a player: boom down into the ground, pull the stick in and
        /// curl, lift out, swing. The dirt must stay in the bucket (it used to fall straight out of it).
        /// </summary>
        static InteractOption FindOption(Interactable it, string contains)
        {
            var list = new System.Collections.Generic.List<InteractOption>();
            it.GetOptions(list);
            foreach (var o in list) if (o.label != null && o.label.Contains(contains)) return o;
            return null;
        }

        /// <summary>
        /// Transport fleet: the GMT400's tidy tank comes out and goes back in, the fuel bowser hitches to its pintle
        /// hook, follows it, is towed with it, fills at the pump from its menu and fuels a machine; a semi's RGN lowboy
        /// drops its deck when the gooseneck comes off, a machine drives on and is chained down, the neck goes back on
        /// and the load rides along.
        /// </summary>
        IEnumerator TransportTests()
        {
            var dealer = FindFirstObjectByType<VehicleDealer>();
            var pi = FindFirstObjectByType<PlayerInteractor>();
            GameObject Prefab(string n) { foreach (var l in dealer.listings) if (l.name.Contains(n)) return l.prefab; return null; }
            Vector3 yard = dealer.heavyLotSpots[0].position;
            Vector3 G(Vector3 p) => VehicleDealer.GroundAt(p);

            // --- GMT400 + tidy tank
            var pkPrefab = Prefab("GMT400"); var bwPrefab = Prefab("Fuel Bowser");
            Check(pkPrefab != null && bwPrefab != null, "dealer sells the GMT400 pickup and the fuel bowser");
            if (pkPrefab == null || bwPrefab == null) yield break;
            var starter = FindFirstObjectByType<DumpPickup>();
            Check(starter != null && starter.displayName.Contains("GMT400") && starter.TidyTankFitted, $"the starter truck is the GMT400 with its tidy tank ({(starter != null ? starter.displayName : "none")})");
            Quaternion yawX = Quaternion.Euler(0f, 90f, 0f);
            var pk = Instantiate(pkPrefab, yard + Vector3.down * 300f, yawX).GetComponent<DumpPickup>();
            {
                Vector3 at = VehicleSpot.FindFree(yard + new Vector3(2f, 0f, 8f), 90f, VehicleSpot.LocalBounds(pk.transform), pk.transform, 40f);
                pk.transform.SetPositionAndRotation(at + Vector3.up * 0.5f, yawX);
                var r = pk.GetComponent<Rigidbody>(); r.position = pk.transform.position; r.rotation = yawX;
                Physics.SyncTransforms();
            }
            for (int i = 0; i < 60; i++) yield return new WaitForFixedUpdate();
            float capWith = pk.capacity;
            Check(pk.TidyTankFitted && pk.tidyTank.capacity >= 420f && ContainsTank(pk.tidyTank), $"GMT400 carries a {pk.tidyTank.capacity:0} L tidy tank (box {capWith:0.00} m³ with it in)");
            var opt = FindOption(pk, "Take the tidy tank out");
            Check(opt != null && opt.enabled, "the pickup's menu offers to take the tidy tank out");
            opt?.run(pi);
            Check(!pk.TidyTankFitted && !pk.tidyTank.gameObject.activeSelf && pk.capacity > capWith + 0.3f && !ContainsTank(pk.tidyTank),
                $"tank out: the whole bed holds {pk.capacity:0.00} m³");
            opt = FindOption(pk, "Fit the tidy tank");
            opt?.run(pi);
            Check(pk.TidyTankFitted && Mathf.Abs(pk.capacity - capWith) < 0.01f, "and fitted again");

            {
                var r0 = pk.GetComponent<Rigidbody>(); Vector3 p0 = pk.transform.position;
                pk.TestDrive(1f);
                for (int i = 0; i < 100; i++) yield return new WaitForFixedUpdate();
                pk.TestDrive(0f);
                log.AppendLine($"INFO  pickup alone, 2 s full throttle: {Vector3.Distance(pk.transform.position, p0):0.0} m, vel {r0.linearVelocity.magnitude:0.0}, rpm {pk.wheels[0].rpm:0}, y {pk.transform.position.y:0.00}");
                var touch = new System.Text.StringBuilder();
                foreach (var c in Physics.OverlapBox(pk.transform.TransformPoint(new Vector3(0f, 1.0f, 0f)), new Vector3(1.2f, 1.1f, 3.0f), pk.transform.rotation, ~0, QueryTriggerInteraction.Ignore))
                    if (!c.transform.IsChildOf(pk.transform)) touch.Append(c.name).Append(c.attachedRigidbody != null ? "(rb) " : " ");
                var own = new System.Text.StringBuilder();
                foreach (var c in pk.GetComponentsInChildren<Collider>()) if (!(c is WheelCollider) && !c.isTrigger) own.Append(c.name).Append(' ').Append(c.bounds.min.y.ToString("0.00")).Append("; ");
                log.AppendLine($"INFO  pickup overlaps: {touch}  | ground {G(pk.transform.position).y:0.00} | own colliders (bottom y): {own}");
                for (int i = 0; i < 100; i++) yield return new WaitForFixedUpdate();
            }
            // --- bowser on the pintle hook
            Hitch pintle = pk.GetComponentInChildren<Hitch>();
            var bw = Instantiate(bwPrefab, pk.transform.position - pk.transform.forward * 6f, pk.transform.rotation).GetComponent<Trailer>();
            yield return null;
            bw.transform.position += (pintle.transform.position - pk.transform.forward * 0.8f) - bw.couplerPoint.position;
            var bwrb = bw.GetComponent<Rigidbody>(); bwrb.position = bw.transform.position;
            Physics.SyncTransforms();
            for (int i = 0; i < 80; i++) yield return new WaitForFixedUpdate();
            opt = FindOption(bw, "Hitch to");
            Check(opt != null && opt.enabled, $"backing up to the bowser: its menu offers to hitch it ({Vector3.Distance(pintle.transform.position, bw.couplerPoint.position):0.00} m)");
            opt?.run(pi);
            Check(bw.Hitched == pintle, "bowser hitched to the pickup's pintle hook");
            Vector3 b0 = bw.transform.position;
            pk.TestDrive(0.6f);
            var pkrb = pk.GetComponent<Rigidbody>();
            for (int i = 0; i < 50 * 6; i++)
            {
                yield return new WaitForFixedUpdate();
                if (i % 50 == 0) log.AppendLine($"INFO  pickup t {i / 50}: pos {pk.transform.position} vel {pkrb.linearVelocity.magnitude:0.00} kin {pkrb.isKinematic} sleep {pkrb.IsSleeping()} w0 grounded {pk.wheels[0].isGrounded} rpm {pk.wheels[0].rpm:0} torque {pk.wheels[0].motorTorque:0} brake {pk.wheels[0].brakeTorque:0} bowser vel {bwrb.linearVelocity.magnitude:0.00} brake {bw.wheels[0].brakeTorque:0} jack {bw.jack.localPosition.y:0.00}");
            }
            pk.TestDrive(0f);
            for (int i = 0; i < 50 * 2; i++) yield return new WaitForFixedUpdate();
            float gap = Vector3.Distance(pintle.transform.position, bw.couplerPoint.position);
            Check(Vector3.Distance(bw.transform.position, b0) > 6f && gap < 0.25f && bw.transform.up.y > 0.9f,
                $"the pickup pulls the bowser ({Vector3.Distance(bw.transform.position, b0):0.0} m, ring {gap:0.00} m from the hook, upright {bw.transform.up.y:0.00})");
            {
                // tip the bowser over hitched (and the truck with it), reposition the truck: the bowser comes back upright on the hook
                Vector3 rigP = pk.transform.position; Quaternion rigR = pk.transform.rotation;
                bw.transform.rotation = Quaternion.AngleAxis(85f, bw.transform.forward) * bw.transform.rotation;
                bwrb.rotation = bw.transform.rotation;
                Physics.SyncTransforms();
                for (int i = 0; i < 50 * 2; i++) yield return new WaitForFixedUpdate();
                log.AppendLine($"INFO  bowser tipped: up {bw.transform.up.y:0.00}, hitched {bw.Hitched != null}, truck up {pk.transform.up.y:0.00}");
                pk.Reposition();
                for (int i = 0; i < 50 * 3; i++) yield return new WaitForFixedUpdate();
                float bRoll = Vector3.Angle(bw.transform.up, pk.transform.up);
                float bGap = Vector3.Distance(pintle.transform.position, bw.couplerPoint.position);
                Check(bRoll < 8f && bw.Hitched == pintle && bGap < 0.4f, $"reposition the truck: its tipped bowser comes back upright on the hook ({bRoll:0} deg off the truck's upright, ring {bGap:0.00} m from the hook, up {bw.transform.up.y:0.00})");
                // leaning on the hook: its own menu sets it back upright, still hitched
                bw.transform.rotation = Quaternion.AngleAxis(60f, bw.transform.forward) * bw.transform.rotation;
                bwrb.rotation = bw.transform.rotation;
                Physics.SyncTransforms();
                var up = FindOption(bw, "Set it back upright");
                Check(up != null, $"a hitched bowser leaning on its side offers \"Set it back upright\" (up {bw.transform.up.y:0.00})");
                up?.run(pi);
                for (int i = 0; i < 50 * 2; i++) yield return new WaitForFixedUpdate();
                Check(bw.transform.up.y > 0.9f && bw.Hitched == pintle && Vector3.Distance(pintle.transform.position, bw.couplerPoint.position) < 0.4f,
                    $"and it stands back up on the hook (up {bw.transform.up.y:0.00}, ring {Vector3.Distance(pintle.transform.position, bw.couplerPoint.position):0.00} m)");
                // unhitched and on its side: the same
                bw.Uncouple();
                bw.transform.rotation = Quaternion.AngleAxis(88f, bw.transform.forward) * bw.transform.rotation;
                bwrb.rotation = bw.transform.rotation;
                Physics.SyncTransforms();
                up = FindOption(bw, "Set it back upright");
                up?.run(pi);
                for (int i = 0; i < 50 * 2; i++) yield return new WaitForFixedUpdate();
                Check(up != null && bw.transform.up.y > 0.9f, $"an unhitched bowser on its side sets back upright from its menu (up {bw.transform.up.y:0.00})");
                // put the rig back where it was for the pump tests
                pk.transform.SetPositionAndRotation(rigP + Vector3.up * 0.3f, rigR);
                pkrb.position = pk.transform.position; pkrb.rotation = rigR; pkrb.linearVelocity = Vector3.zero; pkrb.angularVelocity = Vector3.zero;
                Physics.SyncTransforms();
                for (int i = 0; i < 30; i++) yield return new WaitForFixedUpdate();
                bw.transform.SetPositionAndRotation(pk.transform.position - pk.transform.forward * 5f, pk.transform.rotation);
                bwrb.position = bw.transform.position; bwrb.rotation = bw.transform.rotation;
                Physics.SyncTransforms();
                for (int i = 0; i < 30; i++) yield return new WaitForFixedUpdate();
                bw.transform.position += pintle.transform.position - bw.couplerPoint.position;
                bwrb.position = bw.transform.position;
                Physics.SyncTransforms();
                FindOption(bw, "Hitch to")?.run(pi);
            }
            {
                // the 25 t haul truck: all three axles' wheels sit on their wheels and all four rear wheels drive
                var hp = Prefab("Haul Truck");
                Check(hp != null, "dealer sells the 25 t haul truck");
                if (hp != null)
                {
                    Vector3 hAt = VehicleSpot.FindFree(yard + new Vector3(-14f, 0f, -10f), 90f, VehicleSpot.LocalBounds(hp.transform), null, 60f);
                    var ht = Instantiate(hp, hAt + Vector3.up * 0.8f, yawX).GetComponent<DumpPickup>();
                    for (int i = 0; i < 50 * 2; i++) yield return new WaitForFixedUpdate();
                    ht.TestDrive(1f);
                    for (int i = 0; i < 50 * 2; i++) yield return new WaitForFixedUpdate();
                    float worst = 0f; int driven = 0;
                    for (int i = 0; i < ht.wheels.Length; i++)
                    {
                        ht.wheels[i].GetWorldPose(out Vector3 wp, out _);
                        worst = Mathf.Max(worst, Vector3.Distance(wp, ht.wheelVisuals[i].position));
                        if (i >= 2 && ht.wheels[i].motorTorque > 1f) driven++;
                    }
                    ht.TestDrive(0f);
                    Check(ht.wheels.Length == 6 && worst < 0.05f, $"haul truck: all {ht.wheels.Length} wheels show on their axles (worst {worst:0.00} m off)");
                    Check(driven == 4, $"haul truck: both rear axles drive ({driven} of 4 rear wheels pulling)");
                    Destroy(ht.gameObject);
                }
            }
            yield return ShotAt(pk.transform.position + pk.transform.right * 7f + Vector3.up * 3f - pk.transform.forward * 2f, pk.transform.position - pk.transform.forward * 3f + Vector3.up * 1f, "Screenshots/transport_bowser.png");

            // tow the rig to the fuel pump: the bowser comes along; fill it from its menu
            var pump = FindFirstObjectByType<FuelPump>();
            float tank0 = bw.Tank.liters;
            bool towed = pk.TowTo(pump.transform.position + pump.transform.right * 6f, null);
            for (int i = 0; i < 30; i++) yield return new WaitForFixedUpdate();
            gap = Vector3.Distance(pintle.transform.position, bw.couplerPoint.position);
            Check(towed && gap < 0.4f && bw.Hitched == pintle, $"towing the pickup brings its hitched bowser along (ring {gap:0.00} m from the hook)");
            opt = FindOption(bw, "Refuel the fuel bowser at the pump");
            Check(opt != null && opt.enabled, $"at the pump the bowser's menu offers a refuel ({Vector3.Distance(bw.Tank.transform.position, pump.transform.position):0.0} m from the pump)");
            opt?.run(pi);
            Check(bw.Tank.liters >= bw.Tank.capacity - 0.5f && bw.Tank.liters > tank0, $"bowser filled: {tank0:0} -> {bw.Tank.liters:0} L");
            opt = FindOption(pk, "Refuel the tidy tank at the pump");
            Check(opt != null, "the pickup's menu offers to refuel its tidy tank at the pump");
            opt = FindOption(bw, "Unhitch");
            Check(opt != null && opt.enabled, "the bowser's menu offers to unhitch it");
            opt?.run(pi);
            Check(bw.Hitched == null, "bowser unhitched");
            // a machine parked by the bowser is fuelled from it
            var mini = Instantiate(Prefab("Mini Excavator"), bw.transform.position + bw.transform.right * 4f + Vector3.up * 0.5f, bw.transform.rotation).GetComponent<VehicleBase>();
            for (int i = 0; i < 40; i++) yield return new WaitForFixedUpdate();
            mini.fuel = 1f;
            opt = FindOption(mini, "Refuel from the fuel bowser");
            Check(opt != null && opt.enabled, "a machine parked by the bowser can refuel from it");
            float bl0 = bw.Tank.liters;
            opt?.run(pi);
            Check(mini.fuel > mini.fuelCapacity - 0.5f && bw.Tank.liters < bl0, $"machine fuelled from the bowser ({bl0:0} -> {bw.Tank.liters:0} L)");
            for (int i = 0; i < 50 * 2; i++) yield return new WaitForFixedUpdate();
            float ringUp = -1f;
            foreach (var h in Physics.RaycastAll(bw.couplerPoint.position + Vector3.up * 1f, Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore))
                if (h.collider.GetComponentInParent<Trailer>() != bw) ringUp = Mathf.Max(ringUp, 1f - h.distance);
            ringUp = ringUp < 0f ? -1f : bw.couplerPoint.position.y - (bw.couplerPoint.position.y + 1f - (1f - ringUp));
            float ringH = -1f;
            {
                float best = float.MaxValue;
                foreach (var h in Physics.RaycastAll(bw.couplerPoint.position + Vector3.up * 1f, Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore))
                    if (h.collider.GetComponentInParent<Trailer>() != bw && h.distance < best) best = h.distance;
                if (best < float.MaxValue) ringH = best - 1f;
            }
            Check(bw.transform.up.y > 0.95f && ringH > 0.35f, $"unhitched, the bowser stands on its jack (ring {ringH:0.00} m up, upright {bw.transform.up.y:0.00})");
            pk.TestDrive(0.5f);
            for (int i = 0; i < 50 * 3; i++) yield return new WaitForFixedUpdate();
            pk.TestDrive(0f);
            for (int i = 0; i < 50; i++) yield return new WaitForFixedUpdate();
            Check(!bw.IgnoresCollisionWith(pk.transform), $"pulled clear of the unhitched bowser, the pickup collides with it again ({Vector3.Distance(pk.transform.position, bw.transform.position):0.0} m apart)");
            {
                var cc = pi.GetComponent<CharacterController>();
                if (cc == null) cc = pi.GetComponentInParent<CharacterController>();
                Check(cc != null, "player has a character controller");
                if (cc != null)
                {
                    Vector3 keep = cc.transform.position;
                    Vector3 side = bw.transform.right;
                    Vector3 c = bw.Tank.transform.position;
                    Vector3 start = c + side * 3f; start.y = bw.transform.position.y + 0.1f + cc.height * 0.5f - cc.center.y; // standing on the ground beside it (not on the pump canopy above)
                    cc.enabled = false; cc.transform.position = start; cc.enabled = true;
                    Physics.SyncTransforms();
                    for (int i = 0; i < 20; i++) cc.Move(-side * 0.2f);
                    float reached = Vector3.Dot(cc.transform.position - c, side);
                    {
                        var sb = new System.Text.StringBuilder();
                        foreach (var h in Physics.RaycastAll(start, -side, 6f, ~0, QueryTriggerInteraction.Ignore)) sb.Append(h.collider.name).Append(" @").Append(h.distance.ToString("0.0")).Append(" L").Append(h.collider.gameObject.layer).Append(Physics.GetIgnoreCollision(h.collider, cc) ? " IGN" : "").Append("; ");
                        log.AppendLine($"INFO  player sweep: cc on {cc.gameObject.name} layer {cc.gameObject.layer} active {cc.gameObject.activeInHierarchy} enabled {cc.enabled} start {start} tank {c} end {cc.transform.position} hits: {sb}");
                    }
                    Check(reached > 0.6f, $"walking into the bowser's side, the player is stopped by it ({reached:0.00} m from its middle)");
                    cc.enabled = false; cc.transform.position = keep; cc.enabled = true;
                }
            }
            Destroy(mini.gameObject); Destroy(bw.gameObject); Destroy(pk.gameObject);
            yield return null;

            // --- semis with RGN lowboys
            var semis = new System.Collections.Generic.List<SemiTruck>();
            string[] names = { "Semi Truck 25", "Semi Truck 55", "Semi Truck 95" };
            for (int k = 0; k < 3; k++)
            {
                var pf = Prefab(names[k]);
                Check(pf != null, $"dealer sells the {names[k]} t");
                if (pf == null) continue;
                var s = Instantiate(pf, G(yard + new Vector3(10f, 0f, -2f + k * 14f)) + Vector3.up * 0.4f, yawX).GetComponent<SemiTruck>();
                semis.Add(s);
            }
            for (int i = 0; i < 50 * 3; i++) yield return new WaitForFixedUpdate();
            foreach (var s in semis)
            {
                var tw = s.Towing;
                Check(tw != null && tw.Hitched == s.fifthWheel && tw.NeckOn && s.transform.up.y > 0.97f && tw.transform.up.y > 0.97f,
                    $"{s.displayName}: lowboy on the fifth wheel, both standing level (tractor {s.transform.up.y:0.000}, trailer {(tw != null ? tw.transform.up.y : 0f):0.000}, y {s.transform.position.y - G(s.transform.position).y:0.00})");
            }
            if (semis.Count == 3)
                yield return ShotAt(semis[2].transform.position + new Vector3(-14f, 9f, 18f), semis[1].transform.position + Vector3.left * 6f, "Screenshots/transport_semis.png");
            var semi = semis.Count > 0 ? semis[0] : null;
            if (semi == null) yield break;
            for (int k = 1; k < semis.Count; k++) { if (semis[k].Towing != null) semis[k].Towing.gameObject.SetActive(false); semis[k].gameObject.SetActive(false); }
            var low = semi.Towing;
            if (low == null) yield break;
            Vector3 s0 = semi.transform.position;
            semi.TestDrive(0.6f);
            for (int i = 0; i < 50 * 5; i++) yield return new WaitForFixedUpdate();
            semi.TestDrive(0f);
            var srb = semi.GetComponent<Rigidbody>();
            for (int i = 0; i < 50 * 10 && (i < 50 || srb.linearVelocity.magnitude > 0.2f); i++) yield return new WaitForFixedUpdate();
            float kp = Vector3.Distance(low.couplerPoint.position, semi.fifthWheel.transform.position);
            Check(Vector3.Distance(semi.transform.position, s0) > 10f && kp < 0.3f && low.transform.up.y > 0.95f,
                $"{semi.displayName} pulls its lowboy ({Vector3.Distance(semi.transform.position, s0):0.0} m, kingpin {kp:0.00} m from the fifth wheel)");

            opt = FindOption(low, "Detach the gooseneck");
            Check(opt != null && opt.enabled, "the lowboy's menu offers to detach the gooseneck");
            opt?.run(pi);
            for (int i = 0; i < 50 * 2; i++) yield return new WaitForFixedUpdate();
            float lip = low.deckSocket.position.y - G(low.deckSocket.position).y;
            Check(!low.NeckOn && low.Hitched == null && low.NeckCarrier == semi.fifthWheel && lip < 0.3f,
                $"gooseneck off: it stays on the tractor and the deck front drops to the ground ({lip:0.00} m up)");
            semi.TestDrive(0.3f);
            for (int i = 0; i < 50 * 3; i++) yield return new WaitForFixedUpdate();
            semi.TestDrive(0f);
            for (int i = 0; i < 50 * 6 && (i < 50 || srb.linearVelocity.magnitude > 0.2f); i++) yield return new WaitForFixedUpdate();
            float pulled = Vector3.Distance(low.neckSocket.position, low.deckSocket.position);
            Check(pulled > 3f, $"the tractor drives off with the neck, leaving the deck ({pulled:0.0} m)");
            yield return ShotAt(low.deckSocket.position + low.transform.right * 8f + Vector3.up * 3f + low.transform.forward * 4f, low.deckSocket.position - low.transform.forward * 2f, "Screenshots/transport_deck_down.png");

            // drive a compact track loader on over the front of the deck
            var ctl = Instantiate(Prefab("Compact Track Loader"), Vector3.down * 400f, Quaternion.identity).GetComponent<EarthmoverBase>();
            Vector3 front = low.deckSocket.position + low.transform.forward * 3.2f;
            ctl.transform.SetPositionAndRotation(G(front) + Vector3.up * 0.3f, Quaternion.LookRotation(-low.transform.forward));
            if (ctl is FrontLoader carry)
            {
                // bucket up to carry height (its edge ~0.6 m above the track bottoms), curled back, not catching the deck
                float baseY = ctl.transform.position.y - 0.3f;
                float a = carry.armRange.x;
                for (; a < carry.armRange.y; a += 1f) { carry.SetPose(a, 30f); if (carry.bucketTip != null && carry.bucketTip.position.y > baseY + 0.6f) break; }
                log.AppendLine($"INFO  track loader carry pose: arm {a:0} deg, edge {carry.bucketTip.position.y - baseY:0.00} m up");
            }
            var crb = ctl.GetComponent<Rigidbody>(); crb.isKinematic = false; crb.position = ctl.transform.position; crb.rotation = ctl.transform.rotation;
            Physics.SyncTransforms();
            for (int i = 0; i < 50; i++) yield return new WaitForFixedUpdate();
            ctl.TestDrive(0.7f);
            for (int i = 0; i < 50 * 14; i++)
            {
                yield return new WaitForFixedUpdate();
                if (i % 50 == 0) log.AppendLine($"INFO  loader t {i / 50}: pos {ctl.transform.position} vel {crb.linearVelocity.magnitude:0.00} kin {crb.isKinematic} touching {ctl.Suspension.Touching}/{ctl.Suspension.Contacts} parent {(ctl.transform.parent != null ? ctl.transform.parent.name : "-")} occupied {ctl.Occupied}");
                Vector3 l = low.deckArea.InverseTransformPoint(ctl.transform.position);
                if (Mathf.Abs(l.z) < 0.6f) break;
            }
            ctl.TestDrive(0f);
            for (int i = 0; i < 50 * 3; i++) yield return new WaitForFixedUpdate();
            Vector3 onDeck = low.deckArea.InverseTransformPoint(ctl.transform.position);
            Check(low.Carries(ctl) && Mathf.Abs(onDeck.y) < 0.35f,
                $"{ctl.displayName} drives up onto the deck and is chained down (deck-local {onDeck.x:0.0}, {onDeck.y:0.00}, {onDeck.z:0.0})");

            // back the tractor up to the deck and pin the neck back on
            semi.TestDrive(-0.6f);
            float dist = 99f;
            for (int i = 0; i < 50 * 20; i++)
            {
                yield return new WaitForFixedUpdate();
                low.NeckLinedUp(out dist, out _);
                if (dist < 1.4f) break;
            }
            semi.TestDrive(0f);
            for (int i = 0; i < 50 * 6 && (i < 50 || srb.linearVelocity.magnitude > 0.2f); i++) yield return new WaitForFixedUpdate();
            low.NeckLinedUp(out dist, out float yaw);
            opt = FindOption(low, "Attach the gooseneck");
            Check(opt != null && opt.enabled, $"backed up to the deck: the menu offers to attach the gooseneck ({dist:0.00} m, {yaw:0.0} deg off)");
            opt?.run(pi);
            for (int i = 0; i < 50 * 3; i++) yield return new WaitForFixedUpdate();
            Check(low.NeckOn && low.Hitched == semi.fifthWheel && low.Carries(ctl), "gooseneck back on: hitched, the machine still chained on the deck");
            Vector3 c0 = ctl.transform.position;
            var cta = ctl.GetComponentInChildren<TrackAnimator>();
            float belt0 = cta != null ? cta.Travel(0) : 0f, belt1 = cta != null ? cta.Travel(1) : 0f;
            semi.TestDrive(0.6f);
            for (int i = 0; i < 50 * 6; i++) yield return new WaitForFixedUpdate();
            semi.TestDrive(0f);
            for (int i = 0; i < 50 * 3; i++) yield return new WaitForFixedUpdate();
            Vector3 still = low.deckArea.InverseTransformPoint(ctl.transform.position);
            Check(Vector3.Distance(ctl.transform.position, c0) > 8f && Mathf.Abs(still.z - onDeck.z) < 0.2f && semi.LoadFraction > 0.1f,
                $"the load rides along ({Vector3.Distance(ctl.transform.position, c0):0.0} m, slid {Mathf.Abs(still.z - onDeck.z):0.00} m, load {semi.LoadFraction * 100f:0}% of {low.payloadTons:0} t)");
            float beltRun = cta != null ? Mathf.Max(Mathf.Abs(cta.Travel(0) - belt0), Mathf.Abs(cta.Travel(1) - belt1)) : 0f;
            Check(cta != null && ctl.RidingTrailer && beltRun < 0.05f, $"the chained machine's tracks hold still while the lowboy drives ({beltRun:0.00} m of belt)");
            yield return ShotAt(low.transform.position + low.transform.right * 9f + Vector3.up * 4f + low.transform.forward * 2f, low.deckArea.position, "Screenshots/transport_loaded.png");
            Trailer.ReleaseCargo(ctl);
            Check(!low.Carries(ctl) && !crb.isKinematic && ctl.transform.parent == null && !ctl.RidingTrailer, "getting in (or towing) the machine takes the chains off");

            Destroy(ctl.gameObject);
            // the big excavators fit their lowboys: drive the 50 t onto the 55 t, the 90 t onto the 95 t
            if (semis.Count == 3)
            {
                semi.gameObject.SetActive(false); low.gameObject.SetActive(false);
                yield return WideLoad(semis[1], "Heavy Excavator 50", pi);
                semis[1].gameObject.SetActive(false); if (semis[1].Towing != null) semis[1].Towing.gameObject.SetActive(false);
                yield return WideLoad(semis[2], "Mining Excavator 90", pi);
            }
            foreach (var s in semis) { if (s.Towing != null) Destroy(s.Towing.gameObject); Destroy(s.gameObject); }
            yield return null;
        }

        /// <summary>Drop the lowboy's neck, drive the machine on over the front: both tracks stay on the deck and it's chained down.</summary>
        IEnumerator WideLoad(SemiTruck s, string machine, PlayerInteractor pi)
        {
            var dealer = FindFirstObjectByType<VehicleDealer>();
            GameObject pf = null; foreach (var l in dealer.listings) if (l.name.Contains(machine)) pf = l.prefab;
            Check(pf != null, $"dealer sells the {machine} t");
            if (pf == null) yield break;
            s.gameObject.SetActive(true);
            var low = s.Towing;
            if (low == null) { Check(false, $"{s.displayName} has its lowboy"); yield break; }
            low.gameObject.SetActive(true);
            for (int i = 0; i < 50; i++) yield return new WaitForFixedUpdate();
            FindOption(low, "Detach the gooseneck")?.run(pi);
            for (int i = 0; i < 50 * 2; i++) yield return new WaitForFixedUpdate();
            var srb = s.GetComponent<Rigidbody>();
            s.TestDrive(0.4f);
            for (int i = 0; i < 50 * 7; i++) yield return new WaitForFixedUpdate();
            s.TestDrive(0f);
            for (int i = 0; i < 50 * 6 && (i < 50 || srb.linearVelocity.magnitude > 0.2f); i++) yield return new WaitForFixedUpdate();
            Check(!low.NeckOn && Vector3.Distance(low.neckSocket.position, low.deckSocket.position) > 8f, $"{low.displayName}: gooseneck off, deck down, the tractor pulled clear ({Vector3.Distance(low.neckSocket.position, low.deckSocket.position):0.0} m)");

            var m = Instantiate(pf, Vector3.down * 400f, Quaternion.identity).GetComponent<VehicleBase>();
            var mex = m as MiniExcavator; var mem = m as EarthmoverBase;
            void Go(float t) { if (mex != null) mex.TestControl(t == 0f ? (MiniExcavator.Inputs?)null : new MiniExcavator.Inputs { tracks = t }); else mem?.TestDrive(t); }
            var tb = m.GetComponent<BoxCollider>(); // the track frame: gauge plus a shoe across, track length long
            Vector3 front = low.deckSocket.position + low.transform.forward * ((tb != null ? tb.size.z * 0.5f : 3f) + 0.8f);
            m.transform.SetPositionAndRotation(VehicleDealer.GroundAt(front) + Vector3.up * 0.3f, Quaternion.LookRotation(-low.transform.forward));
            var mrb = m.GetComponent<Rigidbody>(); mrb.isKinematic = false; mrb.position = m.transform.position; mrb.rotation = m.transform.rotation;
            Physics.SyncTransforms();
            if (mex != null)
            {
                // boom up a bit so the bucket rides clear of the deck
                mex.TestControl(new MiniExcavator.Inputs { boom = 1f });
                for (int i = 0; i < 50; i++) yield return new WaitForFixedUpdate();
            }
            for (int i = 0; i < 50; i++) yield return new WaitForFixedUpdate();
            Go(0.7f);
            float worstX = 0f; Vector3 deck0 = low.transform.position;
            for (int i = 0; i < 50 * 25; i++)
            {
                yield return new WaitForFixedUpdate();
                Vector3 l = low.deckArea.InverseTransformPoint(m.transform.position);
                if (i % 50 == 0) log.AppendLine($"INFO    t {i / 50}: deck-local ({l.x:0.00}, {l.y:0.00}, {l.z:0.00}) yaw {Vector3.SignedAngle(low.deckArea.forward, m.transform.forward, low.deckArea.up):0} pitch {Vector3.Angle(m.transform.up, low.deckArea.up):0.0} deck up {low.transform.up.y:0.000} deck moved {Vector3.Distance(low.transform.position, deck0):0.00} touching {(m is EarthmoverBase eb ? eb.Suspension.Touching : (m is MiniExcavator mx ? mx.suspension.Touching : -1))}");
                if (l.z < low.deckSize.y * 0.5f) worstX = Mathf.Max(worstX, Mathf.Abs(l.x));
                if (Mathf.Abs(l.z) < 0.6f) break;
            }
            Go(0f);
            for (int i = 0; i < 50 * 3; i++) yield return new WaitForFixedUpdate();
            Vector3 on = low.deckArea.InverseTransformPoint(m.transform.position);
            // the outside of the tracks, across the machine (its bounds without the boom: the track frame is the widest low part)
            float trackHalf = tb != null ? tb.size.x * 0.5f : 0f;
            float edge = Mathf.Abs(on.x) + trackHalf;
            log.AppendLine($"INFO  carried {low.Carries(m)}, deck-local z {on.z:0.00} y {on.y:0.00}");
            log.AppendLine($"INFO  {m.displayName} on the {low.displayName}: deck {low.deckSize.x:0.00} m wide, tracks {trackHalf * 2f:0.00} m across, off-centre {on.x:0.00} m (worst {worstX:0.00} m), tilt {Vector3.Angle(m.transform.up, low.transform.up):0.0} deg");
            Check(low.Carries(m) && Mathf.Abs(on.y) < 0.4f && edge < low.deckSize.x * 0.5f + 0.02f && Vector3.Angle(m.transform.up, low.transform.up) < 4f,
                $"the {m.displayName} drives onto the {low.displayName} and is chained, both tracks on the deck (outer edge {edge:0.00} m of {low.deckSize.x * 0.5f:0.00} m half-width)");
            Trailer.ReleaseCargo(m);
            Destroy(m.gameObject);
        }

        static bool ContainsTank(FuelTank t) { foreach (var x in FuelTank.All) if (x == t) return true; return false; }

        /// <summary>First person in the cab: your arms and legs are drawn on the controls, the head isn't in the way.</summary>
        IEnumerator CabFirstPersonTests()
        {
            var pi = FindFirstObjectByType<PlayerInteractor>();
            var body = pi != null ? pi.GetComponent<FPSBase.Player.PlayerBody>() : null;
            if (body == null || body.rig == null) { Check(false, "player body found"); yield break; }
            var machines = new System.Collections.Generic.List<VehicleBase>();
            foreach (var e in FindObjectsByType<MiniExcavator>(FindObjectsSortMode.None)) if (e.displayName.Contains("5 t")) { machines.Add(e); break; }
            var dz = FindFirstObjectByType<Bulldozer>(); if (dz != null) machines.Add(dz);
            var tr = FindFirstObjectByType<DumpPickup>(); if (tr != null) machines.Add(tr);
            var fl = FindFirstObjectByType<FrontLoader>(); if (fl != null) machines.Add(fl);
            var smr = body.rig.GetComponentInChildren<SkinnedMeshRenderer>();
            int k = 0;
            foreach (var v in machines)
            {
                var vrb = v.GetComponent<Rigidbody>(); vrb.isKinematic = true;
                pi.PlaceAt(v.transform.position + v.transform.right * -3f, 0f);
                yield return null;
                v.Interact(pi);
                var vcam = v.vehicleCamera;
                for (int i = 0; i < 20; i++) { if (vcam != null) vcam.TestCabView(true, 38f, -10f); yield return null; }
                Transform eye = null;
                foreach (var t in v.GetComponentsInChildren<Transform>(true)) if (t.name == "CabView") eye = t;
                var cc = v.Controls3D;
                float hand = cc != null && cc.rightGrip != null ? Vector3.Distance(body.rig.RightHand.position, cc.rightGrip.position) : -1f;
                Check(body.rig.LimbsInView && smr.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.On && smr.enabled,
                    $"{v.displayName}: in cab view your arms and legs are drawn (limbs {body.rig.LimbsInView}, hand {hand:0.00} m from the grip, near clip {(vcam != null ? vcam.GetComponent<Camera>().nearClipPlane : -1f):0.00})");
                float headGap = eye != null && body.rig.Head != null ? Vector3.Distance(body.rig.Head.position, eye.position) : 0f;
                Check(headGap < 0.45f, $"{v.displayName}: the body still sits in the seat under the eye point ({headGap:0.00} m)");
                if (vcam != null)
                {
                    var cam = vcam.GetComponent<Camera>();
                    var rt = new RenderTexture(1600, 900, 24);
                    var prev = cam.targetTexture;
                    cam.targetTexture = rt;
                    for (int i = 0; i < 30; i++) { vcam.TestCabView(true, 38f, -10f); yield return null; }
                    yield return RenderLate(cam, rt, $"Screenshots/cab_fp_{k}.png");
                    cam.targetTexture = prev; rt.Release();
                }
                if (vcam != null) vcam.TestCabView(false);
                v.Exit();
                for (int i = 0; i < 5; i++) yield return null;
                Check(!body.rig.LimbsInView, $"{v.displayName}: getting out puts things back (limbs view off)");
                k++;
            }
        }

        /// <summary>Dealer deliveries never overlap; tidy tank refuelling; reposition; paid towing.</summary>
        IEnumerator ServiceTests()
        {
            var site = DigSite.Instance;
            var dealer = FindFirstObjectByType<VehicleDealer>();
            var deliver = typeof(VehicleDealer).GetMethod("Deliver", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            // ---- buy six machines in a row (small and heavy): each lands on clear ground
            var bought = new System.Collections.Generic.List<VehicleBase>();
            foreach (string want in new[] { "Excavator 20 t", "Excavator 20 t", "Excavator 20 t", "Compact Track Loader", "Compact Track Loader", "Compact Track Loader", "Mining Dozer", "Mining Dozer" })
            {
                VehicleDealer.Listing l = null;
                foreach (var x in dealer.listings) if (x.name.StartsWith(want)) { l = x; break; }
                if (l == null) { log.AppendLine($"INFO  no listing {want}"); continue; }
                var before = new System.Collections.Generic.HashSet<VehicleBase>(VehicleBase.All);
                deliver.Invoke(dealer, new object[] { l });
                GameUI.Instance.ClosePanel();
                foreach (var v in VehicleBase.All) if (!before.Contains(v)) bought.Add(v);
                yield return null;
            }
            int overlaps = 0; string ov = "";
            for (int i = 0; i < bought.Count; i++)
                for (int j = i + 1; j < bought.Count; j++)
                {
                    var a = bought[i]; var b = bought[j];
                    float ra = Mathf.Min(a.LocalBounds.extents.x, a.LocalBounds.extents.z), rb2 = Mathf.Min(b.LocalBounds.extents.x, b.LocalBounds.extents.z);
                    Vector3 d = a.transform.TransformPoint(a.LocalBounds.center) - b.transform.TransformPoint(b.LocalBounds.center); d.y = 0f;
                    if (d.magnitude < ra + rb2) { overlaps++; ov += $" {a.displayName}/{b.displayName} {d.magnitude:0.0}m"; }
                }
            Check(bought.Count >= 6 && overlaps == 0, $"{bought.Count} machines bought in a row all land on their own clear spot ({overlaps} overlapping{ov})");
            for (int i = 0; i < 90; i++) yield return new WaitForFixedUpdate();
            int upright = 0; foreach (var v in bought) if (v.transform.up.y > 0.9f) upright++;
            Check(upright == bought.Count, $"delivered machines settle upright ({upright}/{bought.Count})");
            yield return ShotAt(bought[0].transform.position + new Vector3(14f, 12f, -14f), bought[0].transform.position, "Screenshots/dealer_lot.png");

            // ---- the pickup has a tidy tank; a machine parked next to it refuels from it
            var truck = FindFirstObjectByType<DumpPickup>();
            var tank = truck != null ? truck.GetComponentInChildren<FPSBase.Vehicles.FuelTank>() : null;
            Check(tank != null && tank.capacity > 150f, $"the pickup carries a tidy tank ({(tank != null ? tank.capacity : 0f):0} L)");
            var ex = bought.Count > 0 ? bought[0] : null;
            if (tank != null && ex != null)
            {
                var trb = truck.GetComponent<Rigidbody>(); trb.isKinematic = true;
                trb.interpolation = RigidbodyInterpolation.None;
                Vector3 near = ex.transform.position + ex.transform.right * 7f;
                truck.transform.position = VehicleDealer.GroundAt(near) + Vector3.up * 0.6f;
                Physics.SyncTransforms();
                ex.fuel = 5f;
                var opts = new System.Collections.Generic.List<InteractOption>();
                ex.GetOptions(opts);
                var refuel = opts.Find(x => x.label.StartsWith("Refuel"));
                log.AppendLine("INFO  menu: " + string.Join(" | ", opts.ConvertAll(x => x.label + (x.enabled ? "" : " (off)"))));
                float t0 = tank.liters;
                if (refuel != null && refuel.enabled) refuel.run(null);
                Check(refuel != null && ex.fuel > 5f + 50f && tank.liters < t0 - 50f, $"looking at a machine near the truck offers Refuel from the tidy tank ({ex.fuel:0} L in it now, {tank.liters:0} L left in the tank)");
                yield return ShotAt(truck.transform.position + new Vector3(-3f, 3f, -4f), truck.bedVolume.transform.position, "Screenshots/tidy_tank.png");
                // fill the tank back up at the fuel pump
                var pump = FindFirstObjectByType<FuelPump>();
                if (pump != null)
                {
                    truck.transform.position = VehicleDealer.GroundAt(pump.transform.position + pump.transform.forward * 3f + pump.transform.right * 2f) + Vector3.up * 0.6f;
                    Physics.SyncTransforms();
                    float l1 = tank.liters;
                    pump.Interact(null);
                    Check(tank.liters > l1 + 50f, $"the fuel pump fills the tidy tank ({l1:0} → {tank.liters:0} L)");
                }
                trb.isKinematic = false;
            }

            // ---- reposition: a machine on its side gets set upright on clear ground
            var dz = bought.Find(v => v is Bulldozer);
            if (dz != null)
            {
                var drb = dz.GetComponent<Rigidbody>();
                drb.interpolation = RigidbodyInterpolation.None;
                dz.transform.rotation = Quaternion.Euler(0f, 30f, 95f);
                Physics.SyncTransforms();
                for (int i = 0; i < 30; i++) yield return new WaitForFixedUpdate();
                dz.Reposition();
                for (int i = 0; i < 90; i++) yield return new WaitForFixedUpdate();
                Check(dz.transform.up.y > 0.9f, $"Reposition sets a tipped machine back on its tracks (up {dz.transform.up.y:0.00})");
            }

            // ---- tow: costs money, puts it (and you) at the spot picked on the map
            if (ex != null)
            {
                var eco = Economy.Instance;
                bool unl = GameSettings.UnlimitedMoney;
                GameSettings.UnlimitedMoney = false;
                eco.Earn(100000f);
                float m0 = eco.Money;
                Vector3 dest = new Vector3(60f, 0f, -60f);
                float cost = ex.TowCost(dest);
                var pi = FindFirstObjectByType<PlayerInteractor>();
                bool ok = ex.TowTo(dest, pi);
                for (int i = 0; i < 10; i++) yield return null;
                float off = Vector3.Distance(new Vector3(ex.transform.position.x, 0f, ex.transform.position.z), new Vector3(dest.x, 0f, dest.z));
                float pd = pi != null ? Vector3.Distance(pi.transform.position, ex.transform.position) : 0f;
                Check(ok && Mathf.Abs(m0 - eco.Money - cost) < 1f && cost > 100f && off < 15f && pd < 12f,
                    $"towing costs ${cost:N0} and drops the machine {off:0.0} m from the picked spot, you {pd:0.0} m from it");
                GameSettings.UnlimitedMoney = unl;
            }
        }

        /// <summary>Every dozer drives over a spoil berm (with a log of what holds it up).</summary>
        IEnumerator BermTests()
        {
            var site = DigSite.Instance;
            var dealer = FindFirstObjectByType<VehicleDealer>();
            var dozers = new System.Collections.Generic.List<Bulldozer>();
            foreach (var l in dealer.listings)
                if (l.prefab != null && l.prefab.GetComponent<Bulldozer>() != null)
                    dozers.Add(Instantiate(l.prefab, new Vector3(0f, -50f, 0f), Quaternion.identity).GetComponent<Bulldozer>());
            // same order and state as the full run: the ripper dozers, after ripping and pushing a strip
            dozers.RemoveAll(d => !d.HasRipper);
            for (int k = 0; k < dozers.Count; k++)
            {
                var d = dozers[k];
                d.GetComponent<Rigidbody>().isKinematic = true;
                Vector3 st = new Vector3(75f + k * 30f, 0f, -30f); st.y = site.SurfaceY(st);
                d.transform.SetPositionAndRotation(st, Quaternion.Euler(0f, 180f, 0f));
                Vector3 fw = d.transform.forward;
                d.SetBlade(d.bladeRange.y); d.SetRipper(d.ripperRange.y);
                Physics.SyncTransforms();
                for (int i = 0; i < 300; i++) { Vector3 np = d.transform.position + fw * 0.02f; np.y = site.SurfaceY(np); d.transform.position = np; d.Work(1f, 0.02f); }
                d.SetRipper(d.ripperRange.x);
                Vector3 bk = st - fw * 3f; bk.y = site.SurfaceY(bk); d.transform.position = bk;
                d.SetBlade(-0.2f);
                for (int i = 0; i < 450; i++) { Vector3 np = d.transform.position + fw * 0.02f; np.y = site.SurfaceY(np); d.transform.position = np; d.Work(1f, 0.02f); site.Relax(4000); }
                site.SettleNow();
            }
            Collider terrain = FindFirstObjectByType<TerrainCollider>();
            for (int k = 0; k < dozers.Count; k++)
            {
                var d = dozers[k];
                Vector3 start = new Vector3(70f + k * 30f, 0f, 0f); start.y = site.SurfaceY(start) + 0.3f;
                d.transform.SetPositionAndRotation(start, Quaternion.Euler(0f, 180f, 0f));
                Vector3 fwd = d.transform.forward;
                Vector3 mound = start + fwd * 9f; mound.y = site.SurfaceY(mound);
                for (float off = -4f; off <= 4.01f; off += 1f)
                    site.Deposit(mound + d.transform.right * off, new MaterialLoad(2.2f, 0f));
                site.SettleNow();
                float moundH = site.SurfaceY(mound) - mound.y;
                d.SetBlade(d.bladeRange.y); d.SetRipper(d.ripperRange.x);
                var rb = d.GetComponent<Rigidbody>();
                rb.isKinematic = false;
                Physics.SyncTransforms();
                for (int i = 0; i < 60; i++) yield return new WaitForFixedUpdate();
                d.TestDrive(1f);
                Vector3 p0 = d.transform.position; float maxY = p0.y;
                var cols = d.GetComponentsInChildren<Collider>();
                for (int i = 0; i < 50 * 9; i++)
                {
                    yield return new WaitForFixedUpdate(); maxY = Mathf.Max(maxY, d.transform.position.y);
                    if (i % 25 == 0)
                    {
                        var sb = new System.Text.StringBuilder();
                        foreach (var c in cols)
                        {
                            if (c == null || c.isTrigger || !c.enabled || terrain == null) continue;
                            if (Physics.GetIgnoreCollision(c, terrain)) continue;
                            if (Physics.ComputePenetration(c, c.transform.position, c.transform.rotation, terrain, terrain.transform.position, terrain.transform.rotation, out _, out float dist))
                                sb.Append($" {c.name}({c.GetType().Name[0]}) {dist:0.00}");
                        }
                        log.AppendLine($"INFO  {d.displayName} t={i / 50f:0.0} kin {rb.isKinematic} buried {d.Buried} {d.BuryDepth:0.00} heap {d.HeapVolume:0.0} rip {d.RipperDepth:0.00} along {Vector3.Dot(d.transform.position - p0, fwd):0.00} up {d.transform.position.y - p0.y:0.00} pitch {Vector3.SignedAngle(Vector3.ProjectOnPlane(d.transform.forward, Vector3.up), d.transform.forward, d.transform.right):0} v {d.ForwardSpeed:0.00} touch {d.Suspension.Touching}/{d.Suspension.Contacts} hits:{sb}");
                    }
                }
                d.TestDrive(0f);
                float went = Vector3.Dot(d.transform.position - p0, fwd);
                Check(went > 11f && maxY > p0.y + moundH * 0.4f && d.transform.up.y > 0.85f,
                    $"{d.displayName}: drives over a {moundH:0.00} m berm ({went:0.0} m in 9 s, climbed {maxY - p0.y:0.00} m, upright {d.transform.up.y:0.00})");
                rb.isKinematic = true;
                d.transform.position += Vector3.down * 80f;
            }
        }

        /// <summary>Rocks as real objects in buckets and beds, spreading with the bucket, burying a machine.</summary>
        IEnumerator Update3Tests()
        {
            var site = DigSite.Instance;
            MiniExcavator ex = null;
            foreach (var e in FindObjectsByType<MiniExcavator>(FindObjectsSortMode.None)) if (e.displayName.Contains("5 t")) ex = e;
            var truck = FindFirstObjectByType<DumpPickup>();
            Check(ex != null && FindFirstObjectByType<Bulldozer>() is Bulldozer dz0 && dz0.HasRipper && FindFirstObjectByType<FrontLoader>() is FrontLoader fl0 && fl0.skidSteer,
                  "the starting machines are the new models (5 t excavator, ripper dozer, track loader)");
            if (ex == null || truck == null) yield break;

            // ---- a rock rolls into the bucket, rides along, and tips out into the truck bed
            var erb = ex.GetComponent<Rigidbody>(); erb.isKinematic = true;
            erb.interpolation = RigidbodyInterpolation.None; // teleported: no smoothing from where it was
            Vector3 at = new Vector3(40f, 0f, -132f); at.y = site.SurfaceY(at);
            ex.transform.SetPositionAndRotation(at, Quaternion.identity);
            ex.SetPose(-5f, -70f, -40f, 0f);
            Physics.SyncTransforms();
            for (int i = 0; i < 3; i++) yield return null;
            var cc = ex.GetComponent<ClumpContainer>();
            Vector3 tip = ex.bucketTip.position;
            Vector3 rp = tip + ex.transform.forward * 1.2f; rp.y = site.SurfaceY(rp) + 0.12f;
            var rock = Boulder.Spawn(rp, 0.16f, false);
            log.AppendLine($"INFO  ex {ex.displayName} at {ex.transform.position} tip {tip} rock spawned {rock.transform.position} buried {ex.Buried}");
            for (int i = 0; i < 5; i++) yield return new WaitForFixedUpdate();
            log.AppendLine($"INFO  rock r {rock.radius:0.00} v {rock.volume:0.000} embedded {rock.embedded} dist {Vector3.Distance(rock.transform.position, ex.bucketTip.position) - rock.radius:0.00} bucket load {ex.bucketLoad.volume:0.00}/{ex.bucketCapacity:0.00} cc {cc != null} rocks {Boulder.All.Count}");
            var got = cc.Scoop(ex.bucketTip.position, 2f, ex.bucketCapacity);
            Check(got == rock && rock.Held && cc.HeldRocks == 1, $"a {rock.volume:0.00} m³ rock rolls into the bucket ({cc.HeldRocks} held)");
            // lift it up and swing over the truck: it rides along in the bucket
            log.AppendLine($"INFO  bucket mouth {cc.Mouth} half {cc.Half} up {cc.MouthUp:0.00} held {rock.Held}");
            ex.SetPose(40f, -60f, -110f, 0f);
            for (int i = 0; i < 40; i++) yield return null;
            log.AppendLine($"INFO  raised: up {cc.MouthUp:0.00} held {rock.Held}");
            float inBucket = Vector3.Distance(rock.transform.position, ex.bucketTip.position);
            Check(rock.Held && inBucket < 1.2f, $"the rock rides in the raised bucket ({inBucket:0.00} m from the teeth)");
            var trb = truck.GetComponent<Rigidbody>(); trb.isKinematic = true;
            truck.SetTilt(0f); truck.load = default;
            Vector3 bucketPos = ex.bucketTip.position;
            truck.transform.SetPositionAndRotation(new Vector3(bucketPos.x, at.y + 0.1f, bucketPos.z) - truck.transform.forward * 0f, Quaternion.Euler(0f, 90f, 0f));
            Vector3 bedC = truck.bedVolume.transform.TransformPoint(truck.bedVolume.center);
            truck.transform.position += new Vector3(bucketPos.x - bedC.x, 0f, bucketPos.z - bedC.z);
            Physics.SyncTransforms();
            var tcc = truck.GetComponent<ClumpContainer>();
            {
                // the driver sees where the load will land: a green ring in the truck bed
                var pi = FindFirstObjectByType<PlayerInteractor>();
                ex.Interact(pi);
                erb.isKinematic = true;
                for (int i = 0; i < 10; i++) yield return null;
                var mk = ex.GetComponent<DumpMarker>();
                log.AppendLine($"INFO  marker {mk != null} occupied {ex.Occupied} tip {ex.bucketTip.position} ground {site.SurfaceY(ex.bucketTip.position):0.00}");
                Check(mk != null && mk.Visible && mk.OverReceiver, $"dump marker shows the load will land in the truck (visible {mk != null && mk.Visible}, over truck {mk != null && mk.OverReceiver})");
                yield return ShotAt(ex.transform.position + new Vector3(4f, 3.5f, -4f), bucketPos, "Screenshots/rock_in_bucket.png");
                ex.Exit();
                erb.isKinematic = true;
                ex.transform.SetPositionAndRotation(at, Quaternion.identity);
                Physics.SyncTransforms();
                yield return null;
            }
            // open the bucket: the rock slides out and drops into the bed
            for (float t = 0f; t < 1.5f; t += Time.deltaTime) { ex.SetPose(40f, -60f, Mathf.Lerp(-110f, 25f, t / 1.0f), 0f); yield return null; }
            log.AppendLine($"INFO  after dump: held {rock.Held} rock at {rock.transform.position} bed {bedC} bed half {(tcc != null ? tcc.Half : Vector3.zero)} bed up {(tcc != null ? tcc.MouthUp : 0f):0.00}");
            for (int i = 0; i < 120 && !(tcc != null && tcc.HeldRocks > 0); i++) yield return new WaitForFixedUpdate();
            log.AppendLine($"INFO  later: rock at {rock.transform.position}");
            Check(!rock.Held || (tcc != null && tcc.HeldRocks == 1), "dumping tips the rock out of the bucket");
            Check(tcc != null && tcc.HeldRocks == 1, $"the rock lands in the truck bed and rides there ({(tcc != null ? tcc.HeldRocks : 0)} in the bed)");
            yield return ShotAt(truck.transform.position + new Vector3(3.5f, 3.5f, -3.5f), bedC, "Screenshots/rock_in_bed.png");
            // tip the box: out it comes
            for (float t = 0f; t < 2f; t += Time.deltaTime) { truck.SetTilt(Mathf.Lerp(0f, truck.maxTilt, t / 1.2f)); yield return null; }
            for (int i = 0; i < 60; i++) yield return new WaitForFixedUpdate();
            Check(rock != null && !rock.Held && tcc.HeldRocks == 0, $"tipping the box dumps the rock out ({(rock != null ? rock.transform.position.y - site.SurfaceY(rock.transform.position) : -1f):0.00} m above the ground)");
            truck.SetTilt(0f);
            truck.transform.position += Vector3.right * 15f;
            ex.SetPose(10f, -70f, 0f, 0f);

            // ---- spreading: swing the bucket through a heap of loose dirt
            Vector3 heap = ex.transform.position + ex.transform.forward * 3.2f; heap.y = site.SurfaceY(heap);
            MaterialTransfer.PourInstant(heap + Vector3.up * 2f, MaterialLoad.Mix(2.5f, 0f, 0f, 0f, 0f), null);
            site.SettleNow();
            float peak0 = site.SurfaceY(heap);
            // bucket down into the heap, curled flat, then swing back and forth
            float reachBoom = -10f;
            ex.SetPose(reachBoom, -80f, -60f, -35f);
            for (int i = 0; i < 5; i++) yield return null;
            float spread = 0f;
            for (int pass = 0; pass < 4; pass++)
                for (float t = 0f; t < 1.2f; t += Time.deltaTime)
                {
                    ex.TestControl(new MiniExcavator.Inputs { swing = pass % 2 == 0 ? 1f : -1f, boom = -0.15f });
                    spread += ex.LastSpread;
                    yield return null;
                }
            ex.TestControl(null);
            site.SettleNow();
            float peak1 = site.SurfaceY(heap);
            Check(spread > 0.2f && peak1 < peak0 - 0.1f, $"swinging the bucket through a heap spreads it ({spread:0.00} m³ moved, heap {peak0 - at.y:0.00} → {peak1 - at.y:0.00} m high)");
            yield return ShotAt(heap + new Vector3(5f, 4f, -5f), heap, "Screenshots/bucket_spread.png");

            // ---- bury the dozer: backfill dirt all round it, it stays put instead of floating up
            var dz = FindFirstObjectByType<Bulldozer>();
            var drb = dz.GetComponent<Rigidbody>();
            Vector3 pit = new Vector3(78f, 0f, -140f); pit.y = site.SurfaceY(pit);
            log.AppendLine($"INFO  burial spot in the dig site: {site.Contains(pit)}");
            drb.isKinematic = false;
            dz.transform.SetPositionAndRotation(pit + Vector3.up * 0.5f, Quaternion.identity);
            drb.linearVelocity = Vector3.zero;
            Physics.SyncTransforms();
            for (int i = 0; i < 150; i++) yield return new WaitForFixedUpdate();
            float y0 = dz.transform.position.y;
            var bounds = new Bounds(dz.transform.position, Vector3.zero);
            foreach (var r in dz.GetComponentsInChildren<MeshRenderer>()) bounds.Encapsulate(r.bounds);
            // dirt poured in rings round it, rising past the tracks
            for (int layer = 0; layer < 6; layer++)
            {
                for (float a = 0f; a < 360f; a += 12f)
                {
                    Vector3 d = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                    Vector3 q = bounds.center + new Vector3(d.x * (bounds.extents.x + 0.6f), 0f, d.z * (bounds.extents.z + 0.6f));
                    q.y = site.SurfaceY(q) + 0.3f;
                    MaterialTransfer.PourInstant(q, MaterialLoad.Mix(0.35f, 0f, 0f, 0f, 0f), null);
                }
                site.SettleNow();
                for (int i = 0; i < 30; i++) yield return new WaitForFixedUpdate();
            }
            for (int i = 0; i < 60; i++) yield return null;
            float rise = dz.transform.position.y - y0;
            log.AppendLine($"INFO  dozer {dz.BuryDebug} at {dz.transform.position} contains {site.Contains(dz.transform.position)} ground beside {site.SurfaceY(bounds.center + new Vector3(bounds.extents.x + 0.6f, 0f, 0f)) - y0:0.00} bounds {bounds}");
            Check(dz.Buried && rise < 0.25f, $"backfilled dozer is buried and stays put (buried {dz.Buried}, {dz.BuryDepth:0.00} m of dirt round it, moved up {rise:0.00} m)");
            yield return ShotAt(dz.transform.position + new Vector3(9f, 6f, -9f), dz.transform.position, "Screenshots/buried_dozer.png");
            // dig it out (remove the dirt round it): it's free again
            for (int pass = 0; pass < 4; pass++)
            {
                for (float x = -bounds.extents.x - 3.5f; x <= bounds.extents.x + 3.5f; x += 0.5f)
                    for (float z = -bounds.extents.z - 3.5f; z <= bounds.extents.z + 3.5f; z += 0.5f)
                    {
                        if (Mathf.Abs(x) < bounds.extents.x && Mathf.Abs(z) < bounds.extents.z) continue;
                        Vector3 q = new Vector3(bounds.center.x + x, y0 - 0.3f, bounds.center.z + z);
                        DigSite.DigAnywhere(q, 0.5f, 5f);
                    }
                site.SettleNow();
            }
            for (float t = 0f; t < 0.8f; t += Time.deltaTime) yield return null;
            Check(!dz.Buried, $"dug out, the dozer is free again ({dz.BuryDepth:0.00} m of dirt left round it)");
        }

        /// <summary>Multiplayer state without Steam: each machine's state copied onto a second copy that only shows it.</summary>
        IEnumerator NetSyncTests()
        {
            var site = DigSite.Instance;
            var dealer = FindFirstObjectByType<VehicleDealer>();
            var types = new System.Collections.Generic.Dictionary<System.Type, VehicleDealer.Listing>();
            foreach (var l in dealer.listings)
                if (l.prefab != null && l.prefab.GetComponent<VehicleBase>() is VehicleBase vb0 && !types.ContainsKey(vb0.GetType())) types[vb0.GetType()] = l;
            int k = 0;
            foreach (var kv in types)
            {
                Vector3 at = new Vector3(30f + k * 14f, 0f, -150f); at.y = site.SurfaceY(at); k++;
                var a = Instantiate(kv.Value.prefab, at + Vector3.up * 0.4f, Quaternion.identity).GetComponent<VehicleBase>();
                var b = Instantiate(kv.Value.prefab, at + Vector3.right * 6f + Vector3.up * 0.4f, Quaternion.identity).GetComponent<VehicleBase>();
                b.SetNetRemote(true);
                yield return null;
                // pose the owner's copy
                if (a is MiniExcavator ex) { ex.SetPose(25f, -60f, -30f, 40f); ex.SetBlade(-10f); ex.bucketLoad = MaterialLoad.Mix(ex.bucketCapacity * 0.7f, 2f, 0f, 0f, 0f); }
                if (a is FrontLoader fl) { fl.SetPose(20f, -20f); fl.bucketLoad = MaterialLoad.Mix(fl.bucketCapacity * 0.6f, 1f, 0f, 0f, 0f); }
                if (a is MotorGrader gr) gr.SetBlade(5f, 3f, -20f, 0.4f, 10f);
                if (a is Bulldozer dz) dz.SetBlade(0.6f);
                if (a is DumpPickup tr) { tr.SetTilt(20f); tr.load = MaterialLoad.Mix(tr.capacity * 0.5f, 3f, 0f, 0f, 0f); }
                bool threw = false;
                for (float t = 0f; t < 1.5f; t += Time.deltaTime)
                {
                    var ms = new System.IO.MemoryStream();
                    a.NetWrite(new System.IO.BinaryWriter(ms));
                    ms.Position = 0;
                    try { b.NetRead(new System.IO.BinaryReader(ms)); } catch (System.Exception e) { threw = true; log.AppendLine("INFO  " + e); }
                    if (ms.Position != ms.Length) { threw = true; log.AppendLine($"INFO  {a.GetType().Name} read {ms.Position} of {ms.Length} bytes"); }
                    yield return null;
                }
                // compare every moving part's local rotation / position
                var ta = a.GetComponentsInChildren<Transform>(); var tb = b.GetComponentsInChildren<Transform>();
                float worst = 0f; string worstName = "";
                for (int i = 0; i < ta.Length && i < tb.Length; i++)
                {
                    float d = Quaternion.Angle(ta[i].localRotation, tb[i].localRotation);
                    string nm = ta[i].name;
                    bool joint = nm.Contains("Pivot") || nm.Contains("Upper") || nm.Contains("Hinge") || nm.Contains("Drawbar") || nm.Contains("Circle") || nm.Contains("Frame");
                    if (!joint) continue; // wheels, camera mount: they move on their own
                    if (d > 1f) log.AppendLine($"INFO    {nm} differs {d:0.0}°");
                    if (d > worst) { worst = d; worstName = nm; }
                }
                log.AppendLine($"INFO  worst: {worstName}");
                float la = a is MiniExcavator e1 ? e1.bucketLoad.volume : a is FrontLoader f1 ? f1.bucketLoad.volume : a is DumpPickup d1 ? d1.load.volume : 0f;
                float lb = b is MiniExcavator e2 ? e2.bucketLoad.volume : b is FrontLoader f2 ? f2.bucketLoad.volume : b is DumpPickup d2 ? d2.load.volume : 0f;
                var rb = b.GetComponent<Rigidbody>();
                Check(!threw && worst < 2f && Mathf.Abs(la - lb) < 0.03f * Mathf.Max(la, 0.01f) && rb.isKinematic,
                      $"{a.GetType().Name} ({kv.Value.name}) state copies over the network (worst joint {worst:0.0}°, load {la:0.00}/{lb:0.00} m³)");
                // a friend driving it: you can't get in
                b.NetRemoteDriven = true;
                Check(b.Prompt.Contains("driving"), $"{a.GetType().Name} copy says your friend is driving it");
                b.SetNetRemote(false);
                Check(!b.GetComponent<Rigidbody>().isKinematic && !b.NetRemoteDriven, $"{a.GetType().Name} runs locally again when the friend leaves");
                Destroy(a.gameObject); Destroy(b.gameObject);
            }
            // ground: changed cells are recorded, written and read back
            site.TakeChangedCells(false);
            Vector3 p = new Vector3(40f, 0f, -170f); p.y = site.SurfaceY(p);
            float before = site.SurfaceY(p);
            DigSite.DigAnywhere(p + Vector3.down * 0.3f, 0.8f, 0.5f);
            var cells = site.TakeChangedCells(false);
            float after = site.SurfaceY(p);
            var ms2 = new System.IO.MemoryStream(); var w2 = new System.IO.BinaryWriter(ms2);
            foreach (int c in cells) site.WriteCell(w2, c);
            site.SettleNow();
            site.TakeChangedCells(false);
            ms2.Position = 0;
            site.ReadCells(new System.IO.BinaryReader(ms2), cells.Count);
            var echo = site.TakeChangedCells(false);
            Check(cells.Count > 0 && ms2.Length == cells.Count * DigSite.NetCellBytes && Mathf.Abs(site.SurfaceY(p) - after) < 0.05f && after < before - 0.01f,
                  $"dug ground is recorded for the network ({cells.Count} cells, {ms2.Length / 1024f:0.0} KB) and reads back ({before:0.00} → {after:0.00} → {site.SurfaceY(p):0.00} m)");
            Check(echo.Count == 0, $"ground received from a friend isn't sent back out ({echo.Count} echoed)");
            yield return null;
        }

        /// <summary>Skid steers, motor grader, excavator dozer blades, and the moving cab controls.</summary>
        IEnumerator Fleet2Tests()
        {
            var site = DigSite.Instance;
            var dealer = FindFirstObjectByType<VehicleDealer>();
            Check(site != null && dealer != null, "dig site and dealer present");
            if (site == null || dealer == null) yield break;
            var skids = new System.Collections.Generic.List<FrontLoader>();
            MotorGrader grader = null;
            var exs = new System.Collections.Generic.List<MiniExcavator>();
            int k = 0;
            foreach (var l in dealer.listings)
            {
                if (l.prefab == null) continue;
                bool skid = l.prefab.GetComponent<FrontLoader>() is FrontLoader fl0 && fl0.skidSteer;
                bool gr = l.prefab.GetComponent<MotorGrader>() != null;
                bool ex = l.prefab.GetComponent<ExcavatorLinkage>() != null && (l.name.Contains("20") || l.name.Contains("5 t"));
                if (!skid && !gr && !ex) continue;
                Vector3 at = new Vector3(30f + k * 22f, 0f, -120f); at.y = site.SurfaceY(at); k++;
                var go = Instantiate(l.prefab, at + Vector3.up * 0.4f, Quaternion.identity);
                if (skid) skids.Add(go.GetComponent<FrontLoader>());
                if (gr) grader = go.GetComponent<MotorGrader>();
                if (ex) exs.Add(go.GetComponent<MiniExcavator>());
            }
            MiniExcavator mini = null;
            foreach (var m in FindObjectsByType<MiniExcavator>(FindObjectsSortMode.None)) if (m.GetComponent<ExcavatorLinkage>() == null) mini = m;
            Check(skids.Count == 3 && grader != null, $"dealer sells 3 skid steers ({skids.Count}) and a grader ({grader != null})");
            for (int i = 0; i < 150; i++) yield return new WaitForFixedUpdate();

            // skid steers: bucket down, drive into the dirt, the bucket fills; raise the arms and the load stays in
            foreach (var s in skids)
            {
                var rb = s.GetComponent<Rigidbody>();
                Check(s.transform.up.y > 0.9f, $"{s.displayName} sits upright");
                s.SetPose(-90f, 0f);
                s.bucketLoad = default;
                for (int i = 0; i < 10; i++) yield return null;
                s.TestDrive(0.6f);
                for (float t = 0f; t < 5f && s.bucketLoad.volume < s.bucketCapacity * 0.95f; t += Time.deltaTime)
                {
                    s.Work(Mathf.Max(0.3f, s.ForwardSpeed), -0.3f, Time.deltaTime);
                    yield return null;
                }
                s.TestDrive(0f);
                float filled = s.bucketLoad.volume;
                log.AppendLine($"INFO  {s.displayName}: arm {s.Arm:0} (min {s.armRange.x:0}), edge {s.bucketTip.position.y - site.SurfaceY(s.bucketTip.position):0.00} m vs ground, speed {s.ForwardSpeed:0.00}");
                for (float t = 0f; t < 2.5f; t += Time.deltaTime) { s.SetPose(s.Arm + 20f * Time.deltaTime, Mathf.Max(s.tiltRange.x, s.Tilt - 30f * Time.deltaTime)); yield return null; }
                Check(filled > s.bucketCapacity * 0.5f && s.bucketLoad.volume > filled * 0.8f,
                      $"{s.displayName} fills its bucket ({filled:0.00} / {s.bucketCapacity:0.00} m³) and keeps it raised ({s.bucketLoad.volume:0.00})");
                yield return ShotAt(s.transform.position + s.transform.right * 4f + s.transform.forward * 4f + Vector3.up * 2.2f, s.transform.position + Vector3.up * 0.8f,
                    $"Screenshots/skid_{s.displayName.Split(' ')[0]}_{skids.IndexOf(s)}.png");
            }

            // grader: blade angled and dropped, drive forward, it cuts and windrows dirt
            if (grader != null)
            {
                Check(grader.transform.up.y > 0.9f, $"{grader.displayName} sits upright");
                grader.SetBlade(-9f, 0f, 30f, 0f, 5f);
                grader.TestDrive(0.7f);
                float cut = 0f;
                for (float t = 0f; t < 4f; t += Time.deltaTime)
                {
                    grader.Operate(0f, -0.3f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, Time.deltaTime);
                    cut += grader.CutRate * Time.deltaTime;
                    yield return null;
                }
                grader.TestDrive(0f);
                Check(cut > 0.05f, $"grader blade cuts and moves dirt ({cut:0.00} m³)");
                var g = grader.transform;
                yield return ShotAt(g.position + g.right * 9f + g.forward * 6f + Vector3.up * 4f, g.position + Vector3.up * 1f, "Screenshots/grader.png");
                yield return ShotAt(g.position - g.right * 7f - g.forward * 3f + Vector3.up * 3f, g.position + g.forward * 2f, "Screenshots/grader_blade.png");
            }

            // excavator dozer blades: blade down, track forward pushes dirt, reverse drags it back
            if (mini != null) exs.Insert(0, mini);
            foreach (var e in exs)
            {
                if (e.blade == null) { Check(false, $"{e.displayName} has a dozer blade"); continue; }
                var rb = e.GetComponent<Rigidbody>();
                Vector3 at = e.transform.position; at.x += 0f; at.z += 30f; at.y = site.SurfaceY(at);
                rb.isKinematic = true; e.transform.SetPositionAndRotation(at + Vector3.up * 0.3f, Quaternion.identity); Physics.SyncTransforms();
                rb.isKinematic = false;
                e.SetPose(30f, -40f, 0f, 0f);
                e.SetBlade(-30f);
                for (int i = 0; i < 60; i++) yield return new WaitForFixedUpdate();
                float fwd = 0f, back = 0f;
                for (float t = 0f; t < 3f; t += Time.deltaTime) { e.TestControl(new MiniExcavator.Inputs { tracks = 1f, blade = -0.2f }); fwd += e.LastBladeMoved; yield return null; }
                for (float t = 0f; t < 2f; t += Time.deltaTime) { e.TestControl(new MiniExcavator.Inputs { tracks = -1f, blade = -0.2f }); back += e.LastBladeMoved; yield return null; }
                e.TestControl(new MiniExcavator.Inputs { swing = 1f });
                var cc = e.Controls3D;
                Quaternion r0 = cc != null && cc.leftStick != null ? cc.leftStick.localRotation : Quaternion.identity;
                for (int i = 0; i < 20; i++) yield return null;
                float stickMove = cc != null && cc.leftStick != null ? Quaternion.Angle(r0, cc.leftStick.localRotation) : 0f;
                e.TestControl(null);
                Check(fwd > 0.01f, $"{e.displayName} blade pushes dirt driving forward ({fwd:0.00} m³), reverse drags {back:0.00} m³ (blade {e.BladeAngle:0}°)");
                Check(cc != null && stickMove > 3f, $"{e.displayName} cab joystick moves with the swing control ({stickMove:0}°)");
                yield return ShotAt(e.transform.position + e.transform.right * 4f * e.sizeScale + e.transform.forward * 5f * e.sizeScale + Vector3.up * 2f * e.sizeScale,
                    e.transform.position + e.transform.forward * 1.5f * e.sizeScale, $"Screenshots/ex_blade_{exs.IndexOf(e)}.png");
            }

            // seated driver reaches the pilot controls
            var pi = FindFirstObjectByType<PlayerInteractor>();
            var body = pi != null ? pi.GetComponent<FPSBase.Player.PlayerBody>() : null;
            var seatTests = new System.Collections.Generic.List<VehicleBase>();
            foreach (var e in exs) if (e.displayName.Contains("20")) seatTests.Add(e);
            if (skids.Count > 0) seatTests.Add(skids[skids.Count - 1]);
            if (grader != null) seatTests.Add(grader);
            if (body != null && body.rig != null)
                foreach (var v in seatTests)
                {
                    var vrb = v.GetComponent<Rigidbody>(); vrb.isKinematic = true;
                    v.Interact(pi);
                    for (int i = 0; i < 30; i++) yield return null;
                    var cc = v.Controls3D;
                    float d = cc != null && cc.rightGrip != null && body.rig.RightHand != null ? Vector3.Distance(body.rig.RightHand.position, cc.rightGrip.position) : 99f;
                    Check(d < 0.2f, $"driver's right hand is on the {v.displayName} control ({d:0.00} m)");
                    Transform eye = null;
                    foreach (var t in v.GetComponentsInChildren<Transform>(true)) if (t.name == "CabView") eye = t;
                    if (eye != null)
                        yield return ShotAt(eye.position + v.transform.right * -1.6f + v.transform.forward * 1.3f + Vector3.up * 0.2f, eye.position - Vector3.up * 0.4f + v.transform.forward * 0.2f,
                            $"Screenshots/cab_{seatTests.IndexOf(v)}.png");
                    v.Exit();
                    for (int i = 0; i < 5; i++) yield return null;
                }
        }

        /// <summary>Holding stick in, swing, curl and boom up together (through the real key reading): all four move.</summary>
        IEnumerator MultiControlTests()
        {
            MiniExcavator ex = null;
            foreach (var v in VehicleBase.All) if (v is MiniExcavator m && (ex == null || m.displayName.Contains("20"))) ex = m;
            var pi = FindFirstObjectByType<PlayerInteractor>();
            if (ex == null || pi == null) { Check(false, "an excavator to test the controls on"); yield break; }
            ex.SetPose(15f, -90f, -40f, 0f);
            Quaternion u0 = ex.upper.localRotation, b0 = ex.boomPivot.localRotation, s0 = ex.stickPivot.localRotation, k0 = ex.bucketPivot.localRotation;
            ex.Interact(pi);
            for (int i = 0; i < 5; i++) yield return null;
            Controls.TestHeld[GameAction.StickIn] = 1f; Controls.TestHeld[GameAction.SwingLeft] = 1f;
            Controls.TestHeld[GameAction.ExBucketCurl] = 1f; Controls.TestHeld[GameAction.BoomUp] = 1f;
            for (float t = 0f; t < 0.6f; t += Time.deltaTime) yield return null;
            var inp = ex.LastInputs;
            Controls.TestHeld.Clear();
            float du = Quaternion.Angle(u0, ex.upper.localRotation), db = Quaternion.Angle(b0, ex.boomPivot.localRotation);
            float ds = Quaternion.Angle(s0, ex.stickPivot.localRotation), dk = Quaternion.Angle(k0, ex.bucketPivot.localRotation);
            log.AppendLine($"INFO  four at once: inputs boom {inp.boom:0.0} stick {inp.stick:0.0} bucket {inp.bucket:0.0} swing {inp.swing:0.0}; moved swing {du:0.0} boom {db:0.0} stick {ds:0.0} bucket {dk:0.0} deg");
            Check(inp.boom > 0.5f && inp.stick < -0.5f && inp.bucket < -0.5f && inp.swing < -0.5f, "excavator: four controls held at once all register");
            Check(du > 2f && db > 2f && ds > 2f && dk > 2f, "excavator: swing, boom, stick and bucket all move at the same time");
            ex.Exit();
            for (int i = 0; i < 5; i++) yield return null;
        }

        /// <summary>
        /// The bucket cuts the ground in its own shape (no steel buried in drawn dirt), scoops only through its
        /// mouth, shoves dirt with its back; the ground pushes back on a bucket, arm or blade forced into it.
        /// </summary>
        IEnumerator BucketTests()
        {
            var site = DigSite.Instance;
            var dealer = FindFirstObjectByType<VehicleDealer>();
            MiniExcavator e = null;
            foreach (var l in dealer.listings)
                if (l.prefab != null && l.prefab.GetComponent<MiniExcavator>() != null && l.name.Contains("5 t"))
                { e = Instantiate(l.prefab, new Vector3(0f, -50f, 0f), Quaternion.identity).GetComponent<MiniExcavator>(); break; }
            if (e == null) { Check(false, "a 5 t excavator for sale"); yield break; }
            Check(e.ShellPoints > 150 && e.bucketPivot.Find("Tool Teeth") != null, $"the bucket cuts with its whole shell ({e.ShellPoints} points) and the teeth have a hitbox of their own");
            var rb = e.GetComponent<Rigidbody>();
            Vector3 at = new Vector3(40f, 0f, -150f); at.y = site.SurfaceY(at);
            IEnumerator Place(float boom, float stick, float bucket)
            {
                e.TestControl(null);
                rb.isKinematic = true;
                e.transform.SetPositionAndRotation(at + Vector3.up * 0.2f, Quaternion.identity);
                e.SetPose(boom, stick, bucket, 0f);
                e.bucketLoad = default; e.fuel = 50f;
                Physics.SyncTransforms();
                yield return null;
                rb.isKinematic = false; rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
                for (int i = 0; i < 80; i++) yield return new WaitForFixedUpdate();
            }
            // ---- a dig cycle with the machine free on its tracks
            yield return Place(10f, -70f, 0f);
            float deepest = 0f, sumDeep = 0f, peak = 0f; int frames = 0, phase = 0, deepAt = 0, deepPhase = 0; string deepWhy = "";
            void Watch() { float d = e.DeepestInDirt(); if (d > deepest) { deepest = d; deepAt = frames; deepPhase = phase; deepWhy = $"load {e.bucketLoad.volume:0.000}, scooped {e.LastScooped * 1000f:0.0} L, shoved {e.LastSpread * 1000f:0.0} L, scooping {e.LastScooping}, past the firm ground {e.LastPenetration:0.000} m"; } sumDeep += d; frames++; peak = Mathf.Max(peak, e.bucketLoad.volume); }
            for (float t = 0f; t < 6f && !site.IsUnderground(e.bucketTip.position + Vector3.down * 0.25f * e.sizeScale); t += Time.deltaTime)
            { e.TestControl(new MiniExcavator.Inputs { boom = -1f }); yield return null; Watch(); }
            phase = 1;
            for (float t = 0f; t < 5f && e.bucketLoad.volume < e.bucketCapacity * 0.98f; t += Time.deltaTime)
            { e.TestControl(new MiniExcavator.Inputs { stick = -1f, bucket = -0.6f, boom = -0.15f }); yield return null; Watch(); }
            phase = 2;
            for (float t = 0f; t < 3f; t += Time.deltaTime)
            { e.TestControl(new MiniExcavator.Inputs { bucket = -1f, boom = 1f }); yield return null; Watch(); }
            e.TestControl(new MiniExcavator.Inputs());
            float avg = sumDeep / Mathf.Max(1, frames);
            log.AppendLine($"INFO  bucket dig: peak {peak:0.000} of {e.bucketCapacity:0.00} m3, steel under the dirt: deepest {deepest:0.000} m (frame {deepAt}, {(deepPhase == 0 ? "lowering" : deepPhase == 1 ? "digging" : "curling up")}: {deepWhy}), average {avg:0.000} m over {frames} frames");
            Check(peak > e.bucketCapacity * 0.6f, $"the bucket scoops a load ({peak / e.bucketCapacity * 100f:0}%)");
            Check(avg < 0.05f && deepest < 0.25f, $"the bucket stays out of the drawn dirt while digging (average {avg * 100f:0.0} cm, worst {deepest * 100f:0} cm)");
            yield return ShotAt(at + new Vector3(3f, 3f, 5f), e.bucketTip.position, "Screenshots/bucket_cut.png");

            // ---- swung sideways through a heap: it shoves the dirt, nothing jumps into the bucket
            yield return Place(10f, -70f, -60f);
            Vector3 heap = e.transform.position + e.transform.forward * 3.2f; heap.y = site.SurfaceY(heap);
            MaterialTransfer.PourInstant(heap + Vector3.up * 2f, MaterialLoad.Mix(1.5f, 0f, 0f, 0f, 0f), null);
            site.SettleNow();
            float peak0 = site.SurfaceY(heap);
            e.SetPose(-10f, -80f, -60f, -35f);
            for (int i = 0; i < 5; i++) yield return null;
            float spread = 0f, took = 0f;
            for (int pass = 0; pass < 2; pass++)
                for (float t = 0f; t < 1.2f; t += Time.deltaTime)
                {
                    e.TestControl(new MiniExcavator.Inputs { swing = pass % 2 == 0 ? 1f : -1f, boom = -0.15f });
                    yield return null;
                    spread += e.LastSpread; took += e.LastScooped;
                }
            e.TestControl(new MiniExcavator.Inputs());
            site.SettleNow();
            float peak1 = site.SurfaceY(heap);
            log.AppendLine($"INFO  bucket swung through a heap: {spread:0.00} m3 shoved, {took:0.00} m3 into the bucket, heap {peak0 - at.y:0.00} to {peak1 - at.y:0.00} m");
            Check(spread > 0.1f && peak1 < peak0 - 0.05f && took < spread * 0.5f,
                $"swung sideways the bucket shoves the heap along ({spread:0.00} m3 shoved, {took:0.00} m3 went in the bucket)");

            // ---- shoved straight down as hard as it goes (teeth first, then flat): it doesn't fill, barely digs
            Vector3 atWas = at;
            foreach (float bk in new[] { -40f, -100f })
            {
                at = atWas + Vector3.right * (bk < -50f ? 14f : 7f); at.y = site.SurfaceY(at); // (fresh ground each time)
                yield return Place(10f, -70f, bk);
                Vector3 under = e.bucketTip.position; float g0 = site.SurfaceY(under);
                float load = 0f;
                for (float t = 0f; t < 3f; t += Time.deltaTime)
                { e.TestControl(new MiniExcavator.Inputs { boom = -1f }); yield return null; load = Mathf.Max(load, e.bucketLoad.volume); }
                e.TestControl(new MiniExcavator.Inputs());
                float sunk = g0 - site.SurfaceY(under);
                log.AppendLine($"INFO  bucket at {bk:0} deg shoved straight down: {load * 1000f:0} L got in, ground there down {sunk:0.00} m, machine tilted {Vector3.Angle(e.transform.up, Vector3.up):0.0} deg");
                Check(load < e.bucketCapacity * 0.1f && sunk < 0.25f, $"shoving the bucket straight down (at {bk:0} deg) doesn't fill it or dig a hole ({load * 1000f:0} L in, {sunk * 100f:0} cm down)");
            }
            at = atWas;

            // ---- boom forced down with the bucket's back to the ground: the ground lifts the machine
            yield return Place(10f, -60f, 20f);
            float pitch0 = Vector3.Angle(e.transform.up, Vector3.up), over = 0f, pen = 0f;
            for (float t = 0f; t < 3f; t += Time.deltaTime)
            { e.TestControl(new MiniExcavator.Inputs { boom = -1f }); yield return null; over = Mathf.Max(over, e.Overload); pen = Mathf.Max(pen, e.LastPenetration); }
            float pitch1 = Vector3.Angle(e.transform.up, Vector3.up);
            e.TestControl(new MiniExcavator.Inputs());
            log.AppendLine($"INFO  boom forced down: push-back {over * 100f:0}% of the weight, steel {pen:0.00} m past where it may go, tilt {pitch0:0.0} to {pitch1:0.0} deg");
            Check(over > 0.15f && pen < 0.3f, $"forcing the bucket down into hard ground pushes the machine up ({over * 100f:0}% of its weight, {pen * 100f:0} cm in)");

            // ---- the backfill blade pushed down: lifts the front, like on pavement
            if (e.HasBlade)
            {
                yield return Place(25f, -60f, -30f);
                float o = 0f;
                for (float t = 0f; t < 2f; t += Time.deltaTime)
                { e.TestControl(new MiniExcavator.Inputs { blade = -1f }); yield return null; o = Mathf.Max(o, e.Overload); }
                e.TestControl(new MiniExcavator.Inputs());
                Check(o > 0.1f, $"the 5 t blade pushed into the ground lifts the machine ({o * 100f:0}% of its weight)");
            }
            Destroy(e.gameObject);

            // ---- a dozer blade lowered into the ground while standing still
            foreach (var l in dealer.listings)
            {
                if (l.prefab == null || l.prefab.GetComponent<Bulldozer>() == null) continue;
                var d = Instantiate(l.prefab, new Vector3(0f, -50f, 0f), Quaternion.identity).GetComponent<Bulldozer>();
                var drb = d.GetComponent<Rigidbody>();
                drb.isKinematic = true;
                d.transform.SetPositionAndRotation(at + Vector3.up * 0.2f + Vector3.right * 12f, Quaternion.identity);
                d.SetBlade(0.2f);
                Physics.SyncTransforms();
                yield return null;
                drb.isKinematic = false;
                for (int i = 0; i < 80; i++) yield return new WaitForFixedUpdate();
                float p0 = Vector3.SignedAngle(Vector3.up, d.transform.up, d.transform.right), push = 0f;
                for (float t = 0f; t < 1.5f; t += Time.fixedDeltaTime) { d.SetBlade(d.BladeHeight - 0.6f * Time.fixedDeltaTime); yield return new WaitForFixedUpdate(); push = Mathf.Max(push, d.BladePushBack); }
                float p1 = Vector3.SignedAngle(Vector3.up, d.transform.up, d.transform.right);
                Check(push > 0.1f, $"{d.displayName}: the blade lowered into the ground lifts the front ({push * 100f:0}% of its weight, pitch {p0:0.0} to {p1:0.0} deg)");
                Destroy(d.gameObject);
                yield return null;
            }
        }

        IEnumerator DigCycleTests()
        {
            var site = DigSite.Instance;
            var dealer = FindFirstObjectByType<VehicleDealer>();
            var machines = new System.Collections.Generic.List<MiniExcavator>();
            var made = new System.Collections.Generic.List<GameObject>();
            var mini = FindFirstObjectByType<MiniExcavator>();
            if (mini != null) machines.Add(mini);
            if (dealer != null)
                foreach (var l in dealer.listings)
                    if (l.prefab != null && l.prefab.GetComponent<ExcavatorLinkage>() != null && (l.name.Contains("20") || l.name.Contains("90") || l.name.Contains("5 t")))
                    {
                        var go = Instantiate(l.prefab, new Vector3(0f, -50f, 0f), Quaternion.identity);
                        made.Add(go); machines.Add(go.GetComponent<MiniExcavator>());
                    }
            for (int k = 0; k < machines.Count; k++)
            {
                var e = machines[k];
                var rb = e.GetComponent<Rigidbody>();
                Vector3 at = new Vector3(30f + k * 22f, 0f, -120f); at.y = site.SurfaceY(at);
                rb.isKinematic = true;
                e.transform.SetPositionAndRotation(at, Quaternion.identity);
                e.SetPose(10f, -70f, 0f, 0f);
                e.bucketLoad = default;
                Physics.SyncTransforms();
                for (int i = 0; i < 5; i++) yield return null;
                var cc = e.GetComponent<ClumpContainer>();
                float sp0 = cc != null ? cc.Spilled : 0f;
                // 1. boom down until the teeth are in the ground
                for (float t = 0f; t < 6f && !site.IsUnderground(e.bucketTip.position + Vector3.down * 0.25f * e.sizeScale); t += Time.deltaTime)
                {
                    e.TestControl(new MiniExcavator.Inputs { boom = -1f });
                    yield return null;
                }
                // 2. crowd: stick in and curl, boom easing down a little
                float peak = 0f;
                for (float t = 0f; t < 5f && e.bucketLoad.volume < e.bucketCapacity * 0.98f; t += Time.deltaTime)
                {
                    e.TestControl(new MiniExcavator.Inputs { stick = -1f, bucket = -0.6f, boom = -0.15f });
                    peak = Mathf.Max(peak, e.bucketLoad.volume);
                    yield return null;
                }
                // 3. curl fully and lift out
                for (float t = 0f; t < 4f; t += Time.deltaTime)
                {
                    e.TestControl(new MiniExcavator.Inputs { bucket = -1f, boom = 1f });
                    peak = Mathf.Max(peak, e.bucketLoad.volume);
                    yield return null;
                }
                // 4. swing over and hold
                for (float t = 0f; t < 2.5f; t += Time.deltaTime)
                {
                    e.TestControl(new MiniExcavator.Inputs { swing = 1f });
                    yield return null;
                }
                e.TestControl(new MiniExcavator.Inputs());
                Vector3 mw = cc != null ? e.bucketLoadVisual.parent.rotation * cc.mouth : Vector3.zero;
                float kept = peak > 0f ? e.bucketLoad.volume / peak : 0f;
                log.AppendLine($"INFO  dig cycle {e.displayName}: bucket shell {e.ShellPoints} points ({e.TeethPoints} at the teeth), {e.Stalls} stalls against the ground; peak {peak:0.000} m3 of {e.bucketCapacity:0.00}, kept {e.bucketLoad.volume:0.000}, spilled {(cc != null ? cc.Spilled - sp0 : 0f):0.000}, clumps {(cc != null ? cc.Count : 0)}, mouth {mw}");
                Check(peak > e.bucketCapacity * 0.6f, $"{e.displayName}: a dig cycle fills the bucket ({peak / e.bucketCapacity * 100f:0}%)");
                Check(kept > 0.85f, $"{e.displayName}: the dirt stays in the bucket while lifting and swinging ({kept * 100f:0}% kept)");
                yield return ShotAt(e.bucketTip.position + e.transform.right * 3f * e.sizeScale + Vector3.up * 1.5f * e.sizeScale - e.transform.forward * 1.5f * e.sizeScale,
                    e.bucketTip.position, $"Screenshots/dig_cycle_{k}.png");
                e.TestControl(null);
                e.bucketLoad = default;
            }
            foreach (var go in made) Destroy(go);
        }

        /// <summary>
        /// Every machine for sale spawns on its lot (big ones in the heavy-equipment yard) and settles upright.
        /// Then the new fleet: bucket linkage closes, every excavator gets its bucket underground and fills it,
        /// the 5 t blade pushes, the big dozers rip hard ground and push it. Ends with lineup screenshots.
        /// </summary>
        IEnumerator FleetTests(VehicleDealer dealer, DigSite site)
        {
            var spawned = new System.Collections.Generic.List<(GameObject go, Transform spot, string name)>();
            int light = 0, heavy = 0;
            foreach (var l in dealer.listings)
            {
                Transform spot = l.heavy ? (heavy < dealer.heavyLotSpots.Length ? dealer.heavyLotSpots[heavy++] : null)
                                         : (light < dealer.lotSpots.Length ? dealer.lotSpots[light++] : null);
                if (spot == null || l.prefab == null) continue;
                spawned.Add((Instantiate(l.prefab, VehicleDealer.GroundAt(spot.position) + Vector3.up * 0.5f, spot.rotation), spot, l.name));
            }
            Check(dealer.listings.Length >= 13 && heavy == 6, $"dealer sells {dealer.listings.Length} machines, {heavy} of them delivered to the heavy-equipment yard");
            for (int i = 0; i < 200; i++) yield return new WaitForFixedUpdate();
            foreach (var (go, spot, name) in spawned)
            {
                float dy = go.transform.position.y - VehicleDealer.GroundAt(spot.position).y;
                Check(go.transform.up.y > 0.9f && dy > -1f && dy < 2f, $"{name} spawns and sits upright (y offset {dy:0.00})");
            }

            var fleetEx = new System.Collections.Generic.List<MiniExcavator>();
            var fleetDz = new System.Collections.Generic.List<Bulldozer>();
            foreach (var (go, _, _) in spawned)
            {
                if (go.GetComponent<ExcavatorLinkage>() != null) fleetEx.Add(go.GetComponent<MiniExcavator>());
                else if (go.GetComponent<Bulldozer>() is Bulldozer d && d.HasRipper) fleetDz.Add(d);
                else Destroy(go);
            }
            Check(fleetEx.Count == 5 && fleetDz.Count == 2, $"heavy fleet: {fleetEx.Count} excavators, {fleetDz.Count} ripper dozers");
            foreach (var e in fleetEx) e.GetComponent<Rigidbody>().isKinematic = true;
            foreach (var d in fleetDz) d.GetComponent<Rigidbody>().isKinematic = true;

            // tracks: drive the big dozer over untouched ground. Its tracks roll (not glide) and its weight
            // presses track marks into the ground
            {
                var dzT = fleetDz[fleetDz.Count - 1];
                var ta = dzT.GetComponent<TrackAnimator>();
                Check(ta != null && ta.belts.Length == 2, $"{dzT.displayName} has two live tracks ({(ta != null ? ta.belts.Length : 0)})");
                if (ta != null && ta.belts.Length == 2)
                {
                    Vector3 s0 = new Vector3(40f, 0f, -98f); s0.y = site.SurfaceY(s0);
                    var drb = dzT.GetComponent<Rigidbody>();
                    dzT.SetBlade(10f);
                    if (dzT.HasRipper) dzT.SetRipper(-90f);
                    dzT.transform.SetPositionAndRotation(s0 + Vector3.up * 0.3f, Quaternion.Euler(0f, 90f, 0f));
                    Physics.SyncTransforms();
                    Vector3 mark = dzT.transform.TransformPoint(new Vector3(ta.belts[0].lateral, 0f, 5f)); mark.y = 0f;
                    float g0 = site.SurfaceY(mark), pv0 = site.PressedVolume;
                    float t0 = ta.Travel(0), t1 = ta.Travel(1);
                    drb.isKinematic = false; drb.linearVelocity = Vector3.zero; drb.angularVelocity = Vector3.zero;
                    for (int i = 0; i < 40; i++) yield return new WaitForFixedUpdate();
                    Vector3 p0 = dzT.transform.position;
                    dzT.TestDrive(1f);
                    for (int i = 0; i < 260; i++) yield return new WaitForFixedUpdate();
                    dzT.TestDrive(0f);
                    for (int i = 0; i < 40; i++) yield return new WaitForFixedUpdate();
                    float moved = Vector3.Dot(dzT.transform.position - p0, Vector3.right);
                    float r0 = ta.Travel(0) - t0, r1 = ta.Travel(1) - t1;
                    Check(moved > 3f && r0 > moved * 0.8f && r1 > moved * 0.8f && r0 < moved * 1.3f + 0.5f,
                        $"driving {moved:0.0} m rolls both tracks with it ({r0:0.0} m / {r1:0.0} m of track), not gliding");
                    float sunk = g0 - site.SurfaceY(mark);
                    float tonnes = drb.mass / 1000f;
                    Check(site.PressedVolume > pv0 && sunk > 0.008f && sunk < DigSite.PressDepth(tonnes) * 1.6f + 0.02f,
                        $"the {tonnes:0} t dozer presses {sunk * 100f:0.0} cm track marks into firm ground");
                    yield return ShotAt(dzT.transform.position - Vector3.right * 9f + Vector3.up * 4f + Vector3.forward * 5f,
                        dzT.transform.position - Vector3.right * 3f, "Screenshots/track_marks.png");
                    drb.isKinematic = true;
                }

                // turning on the spot scrubs the tracks sideways: the ground is torn up and shoved into a berm
                Vector3 q0 = new Vector3(60f, 0f, -104f); q0.y = site.SurfaceY(q0);
                float l0 = site.TotalLoose(), gq = site.SurfaceY(q0), gb = site.SurfaceY(q0 + Vector3.right * 1.1f);
                for (int i = 0; i < 40; i++)
                {
                    Vector3 c = q0; c.y = site.SurfaceY(c);
                    site.TrackPress(c, 0.5f, 40f, Vector3.forward, Vector3.right * 1.2f);
                }
                float scrubbed = gq - site.SurfaceY(q0), berm = site.SurfaceY(q0 + Vector3.right * 1.1f) - gb;
                Check(site.TotalLoose() - l0 > 0.01f && scrubbed > 0.03f && berm > 0.005f,
                    $"a 40 t machine turning on the spot scrubs {scrubbed * 100f:0} cm out and builds a {berm * 100f:0} cm berm");
            }

            // excavators: on the claim, sweep the boom down until the bucket is in the ground, then fill it
            for (int k = 0; k < fleetEx.Count; k++)
            {
                var e = fleetEx[k];
                Vector3 at = new Vector3(40f + k * 18f, 0f, -75f); at.y = site.SurfaceY(at);
                e.transform.SetPositionAndRotation(at, Quaternion.identity);
                var link = e.GetComponent<ExcavatorLinkage>();
                e.SetPose(15f, -95f, -60f, 0f);
                link.Apply();
                Vector3 dogEnd = link.dogbone.TransformPoint(new Vector3(0f, 0f, link.dogboneLength));
                float gap = Vector3.Distance(dogEnd, link.bucketEar.position);
                Check(gap < link.dogboneLength * 0.05f, $"{e.displayName}: bucket linkage closes (gap {gap * 100f:0} cm)");
                float lowest = float.MaxValue, reach = 0f;
                for (float boom = 60f; boom >= -35f; boom -= 5f)
                    for (float stick = -155f; stick <= -35f; stick += 10f)
                    {
                        e.SetPose(boom, stick, -60f, 0f);
                        Vector3 tp = e.bucketTip.position;
                        lowest = Mathf.Min(lowest, tp.y - at.y);
                        reach = Mathf.Max(reach, Vector3.Dot(tp - at, e.transform.forward));
                    }
                log.AppendLine($"INFO  {e.displayName}: dig depth {-lowest:0.0} m, reach {reach:0.0} m, bucket {e.bucketCapacity:0.00} m³");
                float b = -10f;
                e.SetPose(b, -70f, -60f, 0f);
                while (!site.IsUnderground(e.bucketTip.position) && b > -35f) { b -= 1f; e.SetPose(b, -70f, -60f, 0f); }
                Vector3 tip = e.bucketTip.position;
                Check(site.IsUnderground(tip), $"{e.displayName}: bucket gets into the ground (boom {b:0}°)");
                var got = new MaterialLoad();
                for (int i = 0; i < 400 && got.volume < e.bucketCapacity * 0.95f; i++)
                    got.Add(DigSite.DigAnywhere(tip + Vector3.down * Mathf.Min(1.5f, i * 0.01f), e.digRadius, e.digRate * 0.05f));
                Check(got.volume > e.bucketCapacity * 0.8f, $"{e.displayName}: fills its {e.bucketCapacity:0.00} m³ bucket ({got.volume:0.00} m³)");
                e.SetPose(15f, -95f, -60f, 0f);
            }

            // 5 t backfill blade pushes dirt when lowered
            var ex5 = fleetEx[0];
            Check(ex5.HasBlade, "5 t excavator has a front blade");
            if (ex5.HasBlade)
            {
                Vector3 p = new Vector3(120f, 0f, -20f); p.y = site.SurfaceY(p);
                ex5.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, 180f, 0f));
                ex5.SetBlade(-12f);
                var erb = ex5.GetComponent<Rigidbody>();
                erb.isKinematic = false;
                float moved = 0f;
                for (int i = 0; i < 150; i++)
                {
                    Vector3 np = ex5.transform.position + ex5.transform.forward * 0.02f; np.y = site.SurfaceY(np);
                    ex5.transform.position = np;
                    erb.linearVelocity = ex5.transform.forward; // BladeWork reads the track speed
                    moved += ex5.BladeWork();
                }
                erb.isKinematic = true;
                Check(moved > 0.2f, $"5 t front blade pushed {moved:0.00} m³ of dirt over 3 m");
            }

            // big dozers: rip a strip, back up, push it
            for (int k = 0; k < fleetDz.Count; k++)
            {
                var d = fleetDz[k];
                Vector3 start = new Vector3(75f + k * 30f, 0f, -30f); start.y = site.SurfaceY(start);
                d.transform.SetPositionAndRotation(start, Quaternion.Euler(0f, 180f, 0f));
                Vector3 fwd = d.transform.forward;
                d.SetBlade(d.bladeRange.y);
                d.SetRipper(d.ripperRange.y);
                Physics.SyncTransforms();
                float depth = d.RipperDepth, loose0 = site.TotalLoose();
                const float step = 0.02f;
                for (int i = 0; i < 300; i++)
                {
                    Vector3 np = d.transform.position + fwd * step; np.y = site.SurfaceY(np);
                    d.transform.position = np;
                    d.Work(1f, step);
                }
                float ripped = site.TotalLoose() - loose0;
                Check(depth > 0.2f && ripped > 0.3f, $"{d.displayName}: ripper goes {depth * 100f:0} cm deep and broke up {ripped:0.00} m³ over 6 m");
                d.SetRipper(d.ripperRange.x);
                Vector3 back = start - fwd * 3f; back.y = site.SurfaceY(back);
                d.transform.position = back;
                d.SetBlade(-0.2f);
                for (int i = 0; i < 450; i++)
                {
                    Vector3 np = d.transform.position + fwd * step; np.y = site.SurfaceY(np);
                    d.transform.position = np;
                    d.Work(1f, step);
                    site.Relax(4000);
                }
                site.SettleNow();
                float heap = site.LooseVolume(d.bladeEdge.position + fwd * (d.pushAhead + 0.3f), d.bladeWidth * 0.7f, out _);
                Check(heap > d.heapCapacity * 0.3f, $"{d.displayName}: pushed a {heap:0.0} m³ heap (blade rated {d.heapCapacity:0.#} m³)");
            }

            yield return PhysicsTests(dealer, site, fleetEx, fleetDz);

            // lineup in the heavy-equipment yard for the screenshots
            var all = new System.Collections.Generic.List<Transform>();
            foreach (var e in fleetEx) all.Add(e.transform);
            foreach (var d in fleetDz) all.Add(d.transform);
            float x = 100f;
            Vector3 mid = Vector3.zero;
            for (int i = 0; i < all.Count; i++)
            {
                float w = all[i].GetComponent<BoxCollider>().size.x;
                x += w * 0.5f;
                Vector3 p = new Vector3(x, 0f, 30f); p.y = site.SurfaceY(p);
                all[i].SetPositionAndRotation(p, Quaternion.Euler(0f, 200f, 0f));
                if (all[i].GetComponent<Bulldozer>() is Bulldozer d) { d.SetBlade(0.05f); d.SetRipper(d.ripperRange.x); }
                if (all[i].GetComponent<MiniExcavator>() is MiniExcavator e) e.SetPose(25f, -80f, -95f, 0f);
                x += w * 0.5f + 4.5f;
                mid += p;
            }
            mid /= all.Count;
            Physics.SyncTransforms();
            yield return ShotAt(mid + new Vector3(8f, 14f, -38f), mid + Vector3.up * 2.5f, "Screenshots/fleet.png");
            var big = fleetEx[fleetEx.Count - 1].transform;
            yield return ShotAt(big.position + big.right * 13f + big.forward * 9f + Vector3.up * 6f, big.position + big.forward * 6f + Vector3.up * 4f, "Screenshots/fleet_excavator.png");
            var bd = fleetDz[fleetDz.Count - 1].transform;
            yield return ShotAt(bd.position + bd.right * 9f + bd.forward * 8f + Vector3.up * 4f, bd.position + Vector3.up * 1.5f, "Screenshots/fleet_dozer.png");
            var small = fleetEx[0].transform;
            yield return ShotAt(small.position + small.right * 4f + small.forward * 4.5f + Vector3.up * 2.2f, small.position + small.forward * 1.5f + Vector3.up * 1f, "Screenshots/fleet_5t.png");

            foreach (var t in all) Destroy(t.gameObject);
            yield return null;
        }

        /// <summary>Real physics: dozers drive over a mound, the arm shoves a truck, over-digging lifts the machine.</summary>
        IEnumerator PhysicsTests(VehicleDealer dealer, DigSite site,
            System.Collections.Generic.List<MiniExcavator> fleetEx, System.Collections.Generic.List<Bulldozer> fleetDz)
        {
            // dozers: blade up, drive straight at a dirt mound and over it
            for (int k = 0; k < fleetDz.Count; k++)
            {
                var d = fleetDz[k];
                Vector3 start = new Vector3(70f + k * 30f, 0f, 0f); start.y = site.SurfaceY(start) + 0.3f;
                d.transform.SetPositionAndRotation(start, Quaternion.Euler(0f, 180f, 0f));
                Vector3 fwd = d.transform.forward;
                Vector3 mound = start + fwd * 9f; mound.y = site.SurfaceY(mound);
                // a spoil berm across the track, wider than the machine
                for (float off = -4f; off <= 4.01f; off += 1f)
                    site.Deposit(mound + d.transform.right * off, new MaterialLoad(2.2f, 0f));
                site.SettleNow();
                float moundH = site.SurfaceY(mound) - mound.y;
                d.SetBlade(d.bladeRange.y); d.SetRipper(d.ripperRange.x);
                var rb = d.GetComponent<Rigidbody>();
                rb.isKinematic = false;
                Physics.SyncTransforms();
                for (int i = 0; i < 60; i++) yield return new WaitForFixedUpdate();
                d.TestDrive(1f);
                Vector3 p0 = d.transform.position;
                float maxY = p0.y;
                for (int i = 0; i < 50 * 9; i++)
                {
                    yield return new WaitForFixedUpdate(); maxY = Mathf.Max(maxY, d.transform.position.y);
                }
                d.TestDrive(0f);
                float went = Vector3.Dot(d.transform.position - p0, fwd);
                Check(went > 11f && maxY > p0.y + moundH * 0.4f && d.transform.up.y > 0.85f,
                    $"{d.displayName}: drives over a {moundH:0.00} m berm ({went:0.0} m in 9 s, climbed {maxY - p0.y:0.00} m, upright {d.transform.up.y:0.00}, buried {d.Buried})");
                rb.isKinematic = true;
            }

            // excavator arm: swing the boom into a pickup and shove it
            var ex = fleetEx[1];
            Vector3 at = new Vector3(150f, 0f, 60f); at.y = VehicleDealer.GroundAt(at).y + 0.3f;
            ex.transform.SetPositionAndRotation(at, Quaternion.identity);
            ex.SetPose(0f, -60f, -40f, 0f);
            var erb = ex.GetComponent<Rigidbody>();
            erb.isKinematic = false;
            GameObject truckPrefab = null;
            foreach (var l in dealer.listings) if (l.name.Contains("Pickup")) truckPrefab = l.prefab;
            if (truckPrefab != null)
            {
                Vector3 tp = at + Vector3.right * 6.5f + Vector3.forward * 4f; tp = VehicleDealer.GroundAt(tp) + Vector3.up * 0.4f;
                var truck = Instantiate(truckPrefab, tp, Quaternion.identity);
                var trb = truck.GetComponent<Rigidbody>();
                if (trb != null) trb.isKinematic = false;
                Physics.SyncTransforms();
                for (int i = 0; i < 80; i++) yield return new WaitForFixedUpdate();
                Vector3 t0 = truck.transform.position;
                for (float sw = 0f; sw <= 110f; sw += 0.6f) { ex.SetPose(0f, -60f, -40f, sw); yield return new WaitForFixedUpdate(); }
                for (int i = 0; i < 40; i++) yield return new WaitForFixedUpdate();
                float shoved = Vector3.Distance(truck.transform.position, t0);
                Check(shoved > 0.5f, $"{ex.displayName}: swinging the arm shoves a pickup {shoved:0.00} m");
                Destroy(truck);
            }
            else Check(false, "dealer sells a pickup (for the arm push test)");

            // over-digging: drive the bucket into hard ground and the machine's side comes up
            Vector3 dig = new Vector3(60f, 0f, -60f); dig.y = site.SurfaceY(dig) + 0.3f;
            ex.transform.SetPositionAndRotation(dig, Quaternion.identity);
            ex.SetPose(20f, -90f, -60f, 0f);
            Physics.SyncTransforms();
            for (int i = 0; i < 80; i++) yield return new WaitForFixedUpdate();
            float tilt0 = Vector3.Angle(ex.transform.up, Vector3.up), peakOver = 0f, peakTilt = 0f;
            for (float b = 20f; b >= -35f; b -= 0.5f)
            {
                ex.SetPose(b, -90f, -60f, 0f);
                yield return new WaitForFixedUpdate();
                peakOver = Mathf.Max(peakOver, ex.Overload);
                peakTilt = Mathf.Max(peakTilt, Vector3.Angle(ex.transform.up, Vector3.up));
            }
            for (int i = 0; i < 40; i++)
            {
                yield return new WaitForFixedUpdate();
                peakOver = Mathf.Max(peakOver, ex.Overload);
                peakTilt = Mathf.Max(peakTilt, Vector3.Angle(ex.transform.up, Vector3.up));
            }
            Check(peakOver > 0.3f && peakTilt > tilt0 + 1.5f,
                $"{ex.displayName}: forcing the bucket into the ground lifts the machine ({peakOver * 100f:0}% of its weight, tilts {peakTilt - tilt0:0.0} deg)");
            yield return ShotAt(ex.transform.position + ex.transform.right * 14f + ex.transform.forward * 5f + Vector3.up * 3f, ex.transform.position + ex.transform.forward * 4f + Vector3.up * 1.5f, "Screenshots/overdig.png");
            ex.SetPose(20f, -90f, -60f, 0f);
            erb.isKinematic = true;
        }

        /// <summary>Soil layers, boulders, the grizzly, the sand &amp; gravel yard, the metal detector and the QoL tools.</summary>
        IEnumerator MaterialsTests(DigSite site, Economy eco, WashPlant plant, DumpPickup truck)
        {
            // layers: every kind shows up somewhere
            var kinds = new System.Collections.Generic.HashSet<SoilKind>();
            Rect f = site.FieldRect;
            for (float x = f.xMin + 5f; x < f.xMax; x += 6f)
                for (float z = f.yMin + 5f; z < f.yMax; z += 6f)
                    for (float d = 0.3f; d < 4.4f; d += 0.4f) kinds.Add(site.LayerAt(x, z, d).kind);
            Check(kinds.Count == 5, $"soil layers present: {string.Join(", ", kinds)}");

            // dig a terraced pit in the claim: the load has real make-up, and the cut shows the layers
            Vector3 pit = new Vector3(100f, 0f, -55f); pit.y = site.SurfaceY(pit);
            var dugAll = new MaterialLoad();
            float[] rings = { 9f, 7f, 5f, 3f }, depths = { 0.7f, 1.6f, 2.6f, 3.8f };
            for (int k = 0; k < rings.Length; k++)
                for (float a = 0f; a < 360f; a += 8f)
                    for (float rr = 0f; rr <= rings[k]; rr += 1.2f)
                    {
                        Vector3 tp = pit + Quaternion.Euler(0f, a, 0f) * Vector3.forward * rr;
                        tp.y = pit.y - depths[k];
                        for (int i = 0; i < 6; i++) dugAll.Add(site.Dig(tp, 0.9f, 0.4f));
                    }
            site.SettleNow();
            log.AppendLine($"INFO  terraced pit: {dugAll.volume:0.0} m³ = {dugAll.Describe()}, {dugAll.Grade:0.0} g/m³");
            Check(dugAll.gravel > dugAll.volume * 0.1f && dugAll.clay + dugAll.sand > dugAll.volume * 0.05f, "dug dirt carries gravel, sand and clay");
            yield return ShotAt(pit + new Vector3(-13f, 9f, -13f), pit + Vector3.down * 2f, "Screenshots/layers.png");

            // boulders: dig the dirt off a buried one and it comes out
            int b0 = Boulder.All.Count;
            Check(site.NearestBuriedBoulder(new Vector3(60f, 0f, -40f), out Vector3 bp, out float br), "boulders are buried in the field");
            for (int i = 0; i < 400 && Boulder.All.Count == b0; i++)
            {
                Vector3 tp = bp; tp.y = bp.y - br * 0.4f;
                site.Dig(tp + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)) * br, 1.2f, 0.3f);
            }
            Check(Boulder.All.Count > b0, $"digging around a buried boulder brings it out (r {br:0.00} m)");
            Boulder rock = Boulder.All.Count > b0 ? Boulder.All[Boulder.All.Count - 1] : null;
            if (rock != null)
            {
                Check(rock.embedded, "a freshly dug-out boulder is stuck in the ground until freed");
                Boulder.Loosen(rock.transform.position, 1f);
                var rrb = rock.GetComponent<Rigidbody>();
                for (int i = 0; i < 300 && (i < 120 || rrb.linearVelocity.magnitude > 0.3f); i++) yield return new WaitForFixedUpdate();
                float above = rock.transform.position.y - site.SurfaceY(rock.transform.position);
                Check(!rock.embedded && !rrb.isKinematic && rrb.linearVelocity.magnitude < 1f && above > -rock.radius,
                    $"ripped loose, it settles as a physics rock ({rock.volume:0.00} m³, {rrb.mass / 1000f:0.0} t, speed {rrb.linearVelocity.magnitude:0.00}, {above:0.00} m above ground, kinematic {rrb.isKinematic})");
                yield return ShotAt(rock.transform.position + new Vector3(3f, 5f, -3f), rock.transform.position, "Screenshots/boulder.png");
                var scoop = Boulder.TryScoop(rock.transform.position + Vector3.up * rock.radius, 1f, 10f);
                yield return null;
                Check(scoop.rock > 0.01f && rock == null, $"a bucket scoops up a boulder that fits ({scoop.rock:0.00} m³ of rock)");
            }

            // dumping a load with rock in it: dirt goes on the ground, rock comes out as boulders
            int b1 = Boulder.All.Count;
            var mixed = MaterialLoad.Mix(1f, 0f, 0.2f, 0.5f, 0.1f); mixed.Add(MaterialLoad.Rocks(0.6f));
            Vector3 dumpAt = new Vector3(120f, 0f, -10f); dumpAt.y = site.SurfaceY(dumpAt);
            float loose0 = site.TotalLoose();
            DigSite.DropAt(dumpAt, mixed);
            Check(Boulder.All.Count > b1 && Mathf.Abs(site.TotalLoose() - loose0 - 1f) < 0.02f, $"dumping 1 m³ dirt + 0.6 m³ rock: {site.TotalLoose() - loose0:0.00} m³ loose dirt, {Boulder.All.Count - b1} boulder(s)");

            // grizzly on the wash plant
            int b2 = Boulder.All.Count;
            var feed = MaterialLoad.Mix(0.5f, 2f, 0.2f, 0.5f, 0.1f); feed.Add(MaterialLoad.Rocks(0.4f));
            float hop0 = plant.hopper.volume;
            plant.Receive(feed);
            Check(plant.hopper.rock < 0.001f && Boulder.All.Count > b2 && Mathf.Abs(plant.hopper.volume - hop0 - 0.5f) < 0.01f, "wash plant grizzly rejects rocks, takes the dirt");
            Check(plant.RecoveryFor(0.6f) < plant.RecoveryFor(0f) * 0.8f, $"clay cuts gold recovery ({plant.RecoveryFor(0f) * 100f:0}% clean feed vs {plant.RecoveryFor(0.6f) * 100f:0}% clay)");

            // sand & gravel yard pays for a load poured into its bay
            var yard = FindFirstObjectByType<AggregateYard>();
            Check(yard != null && yard.bayVolume != null, "sand & gravel yard exists");
            if (yard != null)
            {
                float m0 = eco.Money;
                var sale = MaterialLoad.Mix(2f, 0f, 0.3f, 0.5f, 0.1f);
                float expect = yard.PriceOf(sale);
                Vector3 bay = yard.bayVolume.transform.TransformPoint(yard.bayVolume.center);
                MaterialTransfer.Pour(bay + Vector3.up * 2.5f, sale, null);
                DirtGrains.FastForward();
                Check(eco.Money - m0 > expect * 0.99f, $"pouring 2 m³ into the yard bay sells it for ${eco.Money - m0:N0} (expected ${expect:N0})");
                yield return ShotAt(bay + new Vector3(8f, 6f, -12f), bay, "Screenshots/aggregate_yard.png");
            }

            // metal detector: finds ring, hand digging brings them up
            var det = FindFirstObjectByType<FPSBase.Player.MetalDetector>(FindObjectsInactive.Include);
            Check(det != null, "player has a metal detector");
            // the nearest nugget the detector can actually hear
            BuriedFind nug = null;
            var cands = new System.Collections.Generic.List<BuriedFind>();
            site.FindsNear(new Vector3(80f, 0f, -40f), 40f, cands);
            float bestD = float.MaxValue;
            foreach (var c in cands)
            {
                if (c.kind != FindKind.Gold) continue;
                Vector3 o = c.pos; o.y = site.SurfaceY(o) + 0.07f;
                float dd = (c.pos - new Vector3(80f, c.pos.y, -40f)).sqrMagnitude;
                if (FPSBase.Player.MetalDetector.SignalOf(c, o, out _) > 0.3f && dd < bestD) { bestD = dd; nug = c; }
            }
            int audible = 0, golds = 0;
            foreach (var c in cands) if (c.kind == FindKind.Gold) { golds++; Vector3 o = c.pos; o.y = site.SurfaceY(o) + 0.07f; if (FPSBase.Player.MetalDetector.SignalOf(c, o, out _) > 0.05f) audible++; }
            log.AppendLine($"INFO  detector can hear {audible} of {golds} gold nuggets near the claim centre");
            Check(nug != null && site.FindsLeft(FindKind.Gold) > 50 && site.FindsLeft(FindKind.Iron) > 50, $"gold nuggets and junk are buried ({site.FindsLeft(FindKind.Gold)} gold, {site.FindsLeft(FindKind.Iron)} iron, {site.FindsLeft(FindKind.Coin)} coins)");
            if (det != null && nug != null)
            {
                Vector3 over = nug.pos; over.y = site.SurfaceY(over) + 0.07f;
                float sig = FPSBase.Player.MetalDetector.SignalOf(nug, over, out float nd);
                float sigFar = FPSBase.Player.MetalDetector.SignalOf(nug, over + Vector3.right * 2f, out _);
                Check(sig > 0.25f && sigFar < 0.01f, $"detector rings over a {nug.grams:0.0} g nugget {nd * 100f:0} cm down (signal {sig:0.00}, 2 m off {sigFar:0.00})");
                float g0 = eco.GoldGrams;
                int got = 0;
                for (int i = 0; i < 20 && got == 0; i++) got = det.DigAt(over, Vector3.right * 0.9f);
                Check(eco.GoldGrams - g0 > nug.grams * 0.99f, $"hand digging brings it up: +{eco.GoldGrams - g0:0.00} g gold");
                yield return ShotAt(over + new Vector3(1.4f, 1.3f, -1.4f), over, "Screenshots/hand_dig.png");
            }

            // QoL: map markers, flip upright, recall, engine sound
            Check(MapMarker.All.Count >= 7, $"{MapMarker.All.Count} places on the map/compass");
            var trb = truck.GetComponent<Rigidbody>();
            truck.transform.rotation = Quaternion.Euler(0f, 30f, 120f);
            truck.Recover();
            for (int i = 0; i < 60; i++) yield return new WaitForFixedUpdate();
            Check(truck.transform.up.y > 0.95f, $"flip upright puts a tipped truck back on its wheels (up {truck.transform.up.y:0.00})");
            Vector3 spot = new Vector3(40f, 0f, 30f);
            Check(truck.RecallTo(spot, 90f) && Vector3.Distance(new Vector3(truck.transform.position.x, 0f, truck.transform.position.z), spot) < 0.5f, "recall brings a machine to you");
            Check(FindObjectsByType<EngineAudio>(FindObjectsSortMode.None).Length > 0, "machines have engine sound");
            yield return null;
        }

        static float PileVolume() { float v = 0f; foreach (var p in DirtPile.All) v += p.load.volume; return v; }

        /// <summary>Falling clods, material-dependent heaps, spilling off a full truck, cave-ins and ruts.</summary>
        IEnumerator DirtPhysicsTests(DigSite site, DumpPickup truck)
        {
            // a pour really falls: nothing on the ground at first, then all of it lands
            site.SettleNow(); // earlier pits may still be caving in
            Vector3 a = new Vector3(135f, 0f, -35f); a.y = site.SurfaceY(a);
            float l0 = site.TotalLoose(), p0 = PileVolume();
            MaterialTransfer.Pour(a + Vector3.up * 3f, MaterialLoad.Mix(1f, 2f, 0.2f, 0.3f, 0.1f), null);
            Check(DirtGrains.Live > 10 && site.TotalLoose() - l0 < 0.01f, $"poured dirt falls as {DirtGrains.Live} clods ({DirtGrains.InFlight:0.00} m³ in the air)");
            DirtGrains.FastForward();
            float landed = site.TotalLoose() - l0 + PileVolume() - p0;
            Check(DirtGrains.Live == 0 && Mathf.Abs(landed - 1f) < 0.01f, $"all of it lands and joins the ground ({landed:0.000} m³)");

            // sand runs out flat, clay stands up in a steep heap
            float Heap(Vector3 at, MaterialLoad l)
            {
                at.y = site.SurfaceY(at);
                float g = at.y;
                for (int i = 0; i < 30; i++) { MaterialTransfer.Pour(at + Vector3.up * 2.5f, l.Scaled(1f / 30f), null); DirtGrains.FastForward(); }
                site.SettleNow();
                return site.SurfaceY(at) - g;
            }
            float sandH = Heap(new Vector3(145f, 0f, -45f), MaterialLoad.Mix(2f, 0f, 1f, 0f, 0f));
            float clayH = Heap(new Vector3(145f, 0f, -30f), MaterialLoad.Mix(2f, 0f, 0f, 0f, 1f));
            Check(clayH > sandH * 1.15f && sandH > 0.15f, $"2 m³ heaps: clay stands {clayH:0.00} m tall, sand runs out to {sandH:0.00} m");

            // a full truck bed: the rest spills over the sides onto the ground, nothing is lost
            var trb = truck.GetComponent<Rigidbody>();
            trb.isKinematic = true;
            Vector3 tp = new Vector3(130f, 0f, -15f); tp.y = site.SurfaceY(tp) + 0.05f;
            truck.transform.SetPositionAndRotation(tp, Quaternion.Euler(0f, 90f, 0f));
            Physics.SyncTransforms();
            truck.load = new MaterialLoad(truck.capacity - 0.3f, 0f);
            float tl0 = truck.load.volume; l0 = site.TotalLoose(); p0 = PileVolume();
            Vector3 bed = truck.bedVolume.transform.TransformPoint(truck.bedVolume.center);
            for (int i = 0; i < 20; i++) MaterialTransfer.Pour(bed + Vector3.up * 1.6f, new MaterialLoad(0.05f, 0f), null);
            DirtGrains.FastForward();
            float inBed = truck.load.volume - tl0, spilled = site.TotalLoose() - l0 + PileVolume() - p0;
            Check(truck.FreeSpace < 0.01f && spilled > 0.5f && Mathf.Abs(inBed + spilled - 1f) < 0.02f,
                $"overfilling the truck: {inBed:0.00} m³ in the bed, {spilled:0.00} m³ spilled over the sides");
            truck.load = default;
            trb.isKinematic = false;

            // cave-ins: a steep hole in sand caves in, the same hole in plain topsoil stands
            Vector3 sandSpot = Vector3.zero; bool found = false;
            Rect f = site.FieldRect;
            for (float x = f.xMin + 20f; x < f.xMax - 20f && !found; x += 3f)
                for (float z = f.yMin + 20f; z < f.yMax - 20f && !found; z += 3f)
                {
                    if (site.claimRect.Contains(new Vector2(x, z))) continue;
                    if (site.LayerAt(x, z, 1.3f).kind == SoilKind.Sand && site.LayerAt(x, z, 2.1f).kind == SoilKind.Sand && site.LayerAt(x + 2f, z, 1.4f).kind == SoilKind.Sand)
                    { sandSpot = new Vector3(x, 0f, z); found = true; }
                }
            Check(found, $"found a sand lens to dig in at ({sandSpot.x:0}, {sandSpot.z:0})");
            if (found)
            {
                sandSpot.y = site.SurfaceY(sandSpot);
                float c0 = site.CollapsedVolume;
                for (int i = 0; i < 300; i++) site.Dig(sandSpot + Vector3.down * 2.2f + new Vector3(Random.Range(-0.6f, 0.6f), 0f, Random.Range(-0.6f, 0.6f)), 0.7f, 0.3f);
                site.Relax(site.slumpBudget);
                Vector3 rim = sandSpot + Vector3.right * 1.55f; // (just past where the bucket reached)
                float rimBefore = site.SurfaceY(rim);
                Check(site.PendingCollapses > 0, $"the sand walls start to give way ({site.PendingCollapses} cells)");
                for (int i = 0; i < 90; i++) yield return null;
                yield return ShotAt(sandSpot + new Vector3(3.5f, 3.2f, -3.5f), sandSpot + Vector3.down * 1.2f, "Screenshots/cave_in.png");
                site.SettleNow();
                float caved = site.CollapsedVolume - c0;
                Check(caved > 0.5f && site.SurfaceY(rim) < rimBefore - 0.08f,
                    $"sand hole caved in: {caved:0.00} m³ slid in, the rim 1.55 m out dropped {rimBefore - site.SurfaceY(rim):0.00} m");
            }
            Vector3 top = new Vector3(150f, 0f, 10f); top.y = site.SurfaceY(top);
            log.AppendLine($"INFO  topsoil test spot layer at 0.6 m: {site.LayerAt(top.x, top.z, 0.6f).kind}");
            if (site.LayerAt(top.x, top.z, 0.6f).kind == SoilKind.Topsoil)
            {
                float c1 = site.CollapsedVolume;
                for (int i = 0; i < 200; i++) site.Dig(top + Vector3.down * 0.9f + new Vector3(Random.Range(-0.6f, 0.6f), 0f, Random.Range(-0.6f, 0.6f)), 0.7f, 0.3f);
                site.SettleNow();
                Check(site.CollapsedVolume - c1 < 0.05f, $"a 0.9 m deep hole in firm topsoil keeps its walls ({site.CollapsedVolume - c1:0.00} m³ fell in)");
            }

            // tyres: tread lugs and spokes turn with the wheel (a bare cylinder looks still when it spins)
            int lugs = 0, treadWheels = 0;
            foreach (var w in truck.wheelVisuals)
            {
                if (w == null) continue;
                foreach (var c in w.GetComponentsInChildren<Transform>(true)) if (c.name == "Lug") lugs++;
                foreach (var mf in w.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh != null && mf.sharedMesh.vertexCount > 400) { treadWheels++; break; } // baked tyre with tread blocks
            }
            Check(lugs >= 4 * 8 || treadWheels >= 4, $"truck tyres have tread that spins with the wheels ({lugs} lugs, {treadWheels} baked treaded tyres)");

            // ruts: heavy contacts rolling over loose dirt press a groove and push berms up beside it
            Vector3 r0 = new Vector3(150f, 0f, -10f);
            for (int i = -3; i <= 3; i++) site.Deposit(r0 + Vector3.right * i * 1.2f, new MaterialLoad(1f, 0f));
            site.SettleNow();
            float before = site.SurfaceY(r0), lr0 = site.TotalLoose(), side0 = site.SurfaceY(r0 + Vector3.forward * 0.8f);
            for (int pass = 0; pass < 4; pass++)
                for (float x = -3f; x <= 3f; x += 0.25f)
                {
                    Vector3 c = r0 + Vector3.right * x; c.y = site.SurfaceY(c);
                    site.Rut(c, 0.4f, 12000f, Vector3.right * 2f);
                }
            site.SettleNow();
            float rut = before - site.SurfaceY(r0);
            float bermH = site.SurfaceY(r0 + Vector3.forward * 0.8f) - side0;
            Check(rut > 0.03f && Mathf.Abs(site.TotalLoose() - lr0) < 0.01f, $"a 12 t contact rolling over loose dirt leaves a {rut * 100f:0} cm rut (berm {bermH * 100f:0} cm), no dirt lost");

            // screenshot: a stream of clods pouring out onto a heap
            Vector3 sp = new Vector3(140f, 0f, 0f); sp.y = site.SurfaceY(sp);
            StartCoroutine(PourStream(sp + Vector3.up * 3.2f, MaterialLoad.Mix(0.04f, 0f, 0.2f, 0.3f, 0.25f), 140));
            yield return ShotAt(sp + new Vector3(4.5f, 2.2f, -4.5f), sp + Vector3.up * 1.2f, "Screenshots/pour_stream.png");
            DirtGrains.FastForward();
        }

        /// <summary>A heaped truck load holds when driven gently, sloshes and spills over the rim on a hard stop.</summary>
        IEnumerator SloshTests(DigSite site, DumpPickup truck)
        {
            var cc = truck.GetComponent<ClumpContainer>();
            if (cc == null) { Check(false, "truck has a clump container"); yield break; }
            var rb = truck.GetComponent<Rigidbody>();
            bool wasKin = rb.isKinematic;
            Vector3 home = truck.transform.position; Quaternion homeRot = truck.transform.rotation;
            var savedLoad = truck.load;
            rb.isKinematic = true;
            Vector3 start = new Vector3(60f, 0f, -40f); start.y = site.SurfaceY(start) + 0.6f;
            var rot = Quaternion.Euler(0f, 90f, 0f);
            truck.transform.SetPositionAndRotation(start, rot);
            Physics.SyncTransforms();
            truck.load = MaterialLoad.Mix(truck.capacity * 0.97f, 0f, 0.25f, 0.2f, 0.2f);
            for (int i = 0; i < 90; i++) yield return null; // clumps drop in and settle
            float full = truck.load.volume;
            int sp0 = cc.SpillCount;

            // gentle: speed up slowly to 4 m/s and coast
            Vector3 dir = rot * Vector3.forward, pos = start;
            float v = 0f;
            for (float t = 0f; t < 3f; t += Time.deltaTime)
            {
                float dt = Mathf.Max(Time.deltaTime, 1e-3f);
                v = Mathf.Min(4f, v + 1.5f * dt);
                pos += dir * v * dt; truck.transform.position = pos;
                yield return null;
            }
            float gentle = full - truck.load.volume;
            Check(cc.SpillCount == sp0 && gentle < 0.01f, $"a full bed driven gently keeps its load ({gentle * 1000f:0} L spilled)");

            // hard: up to 12 m/s (43 km/h), then slam the brakes (about 1.4 g)
            while (v < 12f)
            {
                float dt = Mathf.Max(Time.deltaTime, 1e-3f);
                v = Mathf.Min(12f, v + 6f * dt);
                pos += dir * v * dt; truck.transform.position = pos;
                yield return null;
            }
            float before = truck.load.volume, spilled0 = cc.Spilled;
            while (v > 0f)
            {
                float dt = Mathf.Max(Time.deltaTime, 1e-3f);
                v = Mathf.Max(0f, v - 14f * dt);
                pos += dir * v * dt; truck.transform.position = pos;
                yield return null;
            }
            for (float t = 0f; t < 1.5f; t += Time.deltaTime) yield return null;
            float lost = before - truck.load.volume, spilled = cc.Spilled - spilled0;
            // (a pickup with a tidy tank at the front of the bed: the tank takes the surge, so little or nothing goes over)
            bool tidy = truck.GetComponentInChildren<FuelTank>() != null;
            Check((tidy || lost > 0.005f) && Mathf.Abs(lost - spilled) < 0.002f,
                $"slamming the brakes: whatever sloshes over the front falls out as clods ({lost * 1000f:0} L, {cc.SpillCount - sp0} clods{(tidy ? ", tidy tank in front" : "")})");
            yield return ShotAt(pos + dir * 4f + Vector3.up * 2.2f + rot * Vector3.right * 3.5f, pos + dir * 1f, "Screenshots/slosh_spill.png");
            DirtGrains.FastForward();

            // a bucket tipped part way: the clumps fall out of its mouth under gravity (before the dump angle)
            var ex = FindFirstObjectByType<MiniExcavator>();
            var exc = ex != null ? ex.GetComponent<ClumpContainer>() : null;
            if (exc != null)
            {
                var erb = ex.GetComponent<Rigidbody>();
                bool eKin = erb.isKinematic;
                Vector3 eHome = ex.transform.position; Quaternion eRot = ex.transform.rotation;
                erb.isKinematic = true;
                Vector3 air = new Vector3(70f, 0f, -40f); air.y = site.SurfaceY(air) + 4f;
                ex.transform.SetPositionAndRotation(air, Quaternion.identity);
                ex.SetPose(20f, -60f, -120f, 0f);
                Physics.SyncTransforms();
                ex.bucketLoad = MaterialLoad.Mix(ex.bucketCapacity, 0f, 0.2f, 0.2f, 0.2f);
                for (int i = 0; i < 90; i++) yield return null;
                float held = ex.bucketLoad.volume / ex.bucketCapacity;
                Vector3 mouthW = ex.bucketLoadVisual.parent.rotation * exc.mouth;
                log.AppendLine($"INFO  curled bucket mouth points {mouthW} (up = {mouthW.y:0.00})");
                Check(held > 0.9f, $"a curled bucket carries its load ({held * 100f:0}% still in)");
                // roll the whole machine over so the bucket mouth turns downward
                for (int i = 0; i < 60; i++)
                {
                    ex.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, 150f, (i + 1) / 60f));
                    yield return null;
                }
                for (float t = 0f; t < 4f && ex.bucketLoad.volume / ex.bucketCapacity > 0.3f; t += Time.deltaTime) yield return null; // heavy dirt takes a moment to let go (how long depends on the frame rate)
                float left = ex.bucketLoad.volume / ex.bucketCapacity;
                Check(left < 0.5f && exc.Spilled > 0.2f * ex.bucketCapacity,
                    $"tipping the bucket over pours the clumps out by gravity ({left * 100f:0}% left, {exc.SpillCount} clods fell)");
                DirtGrains.FastForward();
                ex.bucketLoad = default;
                ex.transform.SetPositionAndRotation(eHome, eRot);
                Physics.SyncTransforms();
                erb.isKinematic = eKin;
            }

            // nobody in the cab: a raised box keeps dumping, and the tailgate swings open
            {
                Vector3 at = new Vector3(52f, 0f, -58f); at.y = site.SurfaceY(at) + 0.6f;
                truck.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, 90f, 0f));
                Physics.SyncTransforms();
                truck.gateSetting = 0;
                truck.load = MaterialLoad.Mix(truck.capacity * 0.8f, 0f, 0.2f, 0.4f, 0.1f);
                for (int i = 0; i < 30; i++) yield return null;
                float l0 = truck.load.volume;
                truck.SetTilt(truck.maxTilt);
                for (float t = 0f; t < 2f; t += Time.deltaTime) yield return null;
                float swing = truck.tailgate != null ? Mathf.DeltaAngle(0f, truck.tailgate.localEulerAngles.x) : 0f;
                Check(!truck.Occupied && truck.load.volume < l0 - 0.05f, $"a raised box keeps dumping with nobody in the cab ({(l0 - truck.load.volume) * 1000f:0} L out)");
                Check(Mathf.Abs(swing) > 30f, $"the tailgate swings open when dumping ({Mathf.Abs(swing):0}°)");
                yield return ShotAt(truck.transform.position - truck.transform.forward * 5.5f + truck.transform.right * 3.5f + Vector3.up * 2.5f,
                    truck.transform.position - truck.transform.forward * 2.5f + Vector3.up * 0.8f, "Screenshots/tailgate_dump.png");
                truck.SetTilt(0f);
                for (int i = 0; i < 40; i++) yield return null;
                DirtGrains.FastForward();
            }

            // spreading: the tailgate cracked open while the truck drives lays an even strip of gravel
            {
                Vector3 a = new Vector3(48f, 0f, -66f); a.y = site.SurfaceY(a);
                var rotS = Quaternion.Euler(0f, 90f, 0f);
                Vector3 dirS = rotS * Vector3.forward;
                var probe = new System.Collections.Generic.List<Vector3>();
                var h0 = new System.Collections.Generic.List<float>();
                for (int i = 0; i < 9; i++)
                    for (int k = -1; k <= 1; k++)
                    {
                        Vector3 q = a + dirS * (4f + i * 1.2f) + Vector3.forward * (k * 0.5f);
                        probe.Add(q); h0.Add(site.SurfaceY(q));
                    }
                truck.gateSetting = System.Array.IndexOf(DumpPickup.GateSettings, 0.25f);
                truck.load = MaterialLoad.Mix(truck.capacity, 0f, 0.15f, 0.75f, 0.02f);
                // the gate end of the truck starts behind the strip: drive forward... the pour comes out the back,
                // so drive in reverse direction of travel: the truck faces back along the strip
                var face = Quaternion.LookRotation(-dirS);
                Vector3 tp = a - face * Vector3.forward * 1f; tp.y = a.y + 0.6f;
                // start with the tailgate over the strip start
                Vector3 gateOff = truck.tailgatePour.position - truck.transform.position;
                truck.transform.SetPositionAndRotation(tp, face);
                Physics.SyncTransforms();
                gateOff = truck.tailgatePour.position - truck.transform.position; gateOff.y = 0f;
                tp = a + dirS * 2f - gateOff; tp.y = a.y + 0.6f;
                truck.transform.position = tp;
                truck.SetTilt(truck.maxTilt * 0.9f);
                float poured0 = truck.Poured;
                // creep forward (the truck faces -dirS, so it backs along the strip: tailgate leading, like a spreader)
                for (float t = 0f; t < 7.5f; t += Time.deltaTime)
                {
                    tp += dirS * 2.2f * Time.deltaTime;
                    truck.transform.position = tp;
                    yield return null;
                }
                truck.SetTilt(0f);
                for (int i = 0; i < 30; i++) yield return null;
                DirtGrains.FastForward();
                site.SettleNow();
                int covered = 0; float lo = float.MaxValue, hi = 0f;
                for (int i = 0; i < probe.Count; i++)
                {
                    float d = site.SurfaceY(probe[i]) - h0[i];
                    if (d > 0.004f) covered++;
                    if (i % 3 == 1) { lo = Mathf.Min(lo, d); hi = Mathf.Max(hi, d); }
                }
                float spread = truck.Poured - poured0;
                Check(spread > 0.05f && covered >= probe.Count * 0.7f && hi < 0.25f,
                    $"a quarter-open tailgate spreads an even strip of gravel ({spread * 1000f:0} L over 11 m, {covered}/{probe.Count} spots covered, {lo * 100f:0.0}-{hi * 100f:0.0} cm deep)");
                yield return ShotAt(a + dirS * 6f + Vector3.forward * 5f + Vector3.up * 3.5f, a + dirS * 7f, "Screenshots/gravel_spread.png");
                truck.gateSetting = 0;
            }

            truck.load = savedLoad;
            truck.transform.SetPositionAndRotation(home, homeRot);
            Physics.SyncTransforms();
            rb.isKinematic = wasKin;
            for (int i = 0; i < 5; i++) yield return null;
        }

        /// <summary>Mannequin body, third-person camera, footsteps, sitting in a cab behind glass, settings.</summary>
        IEnumerator PlayerTests(DigSite site, DumpPickup truck)
        {
            Check(!GameSettings.WheelJump, "mouse wheel no longer jumps (option off by default)");
            var pi = FindFirstObjectByType<PlayerInteractor>();
            var body = pi != null ? pi.GetComponent<FPSBase.Player.PlayerBody>() : null;
            Check(body != null && body.rig != null && body.rig.Ready, "player has a mannequin body with a skeleton");
            if (body == null || body.rig == null) yield break;
            var mover = pi.GetComponent<FPSBase.Player.SourceMovement>();
            var cam = pi.GetComponentInChildren<Camera>();
            var look = pi.GetComponent<FPSBase.Player.PlayerLook>();
            look.enabled = true; // earlier screenshots froze the player's camera
            mover.enabled = true;

            // height
            var bb = new Bounds(); bool any = false;
            foreach (var r in body.rig.GetComponentsInChildren<SkinnedMeshRenderer>()) { if (!any) { bb = r.bounds; any = true; } else bb.Encapsulate(r.bounds); }
            Check(any && bb.size.y > 1.4f && bb.size.y < 2.2f, $"mannequin is person-sized ({bb.size.y:0.00} m tall)");

            // walk across the grass in third person: footsteps play and the camera sits behind the body
            bool tp0 = GameSettings.ThirdPerson;
            GameSettings.ThirdPerson = true;
            Vector3 start = new Vector3(40f, 0f, 20f); start.y = site.Contains(start) ? site.SurfaceY(start) + 0.05f : 0.3f;
            pi.PlaceAt(start, 90f);
            for (int i = 0; i < 30; i++) yield return null;
            int steps0 = body.StepsPlayed;
            StartCoroutine(WalkPlayer(mover, Vector3.right, 3.5f, 4.5f));
            for (int i = 0; i < 40; i++) yield return null;
            float camBack = -cam.transform.localPosition.z;
            var smr0 = body.rig.GetComponentInChildren<SkinnedMeshRenderer>();
            log.AppendLine($"INFO  walking: player {mover.transform.position}, grounded {mover.Grounded}, rig speed {body.rig.speed:0.0}, rig root {body.rig.transform.position}, head {(body.rig.Head ? body.rig.Head.position : Vector3.zero)}, mesh bounds {smr0.bounds.center} size {smr0.bounds.size}, enabled {smr0.enabled} active {smr0.gameObject.activeInHierarchy}, cam {cam.transform.position}");
            Check(camBack > 1.2f, $"third person: camera {camBack:0.0} m behind the player");
            Check(body.rig.GetComponentInChildren<SkinnedMeshRenderer>().shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.On, "third person: the body is drawn");
            yield return TempCamShot(mover.transform, new Vector3(2.2f, 0.6f, -2.8f), Vector3.up * 0.9f, "Screenshots/player_walk.png");
            look.enabled = true; // the screenshot froze it again
            yield return new WaitForSeconds(0.6f);
            Check(body.StepsPlayed - steps0 >= 3, $"footsteps while walking ({body.StepsPlayed - steps0} steps on {body.LastSurface})");
            GameSettings.ThirdPerson = false;
            yield return new WaitForSeconds(0.6f);
            Check(body.rig.GetComponentInChildren<SkinnedMeshRenderer>().shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly && cam.transform.localPosition.magnitude < 0.05f,
                "first person: camera back in the head, body only casts a shadow");
            GameSettings.ThirdPerson = tp0;

            // get in the pickup: the body sits in the seat, visible through the glass
            var trb = truck.GetComponent<Rigidbody>();
            Vector3 tp = new Vector3(120f, 0f, 22f); tp.y = site.SurfaceY(tp) + 0.05f;
            trb.isKinematic = true;
            truck.transform.SetPositionAndRotation(tp, Quaternion.Euler(0f, 60f, 0f));
            Physics.SyncTransforms();
            pi.PlaceAt(tp + truck.transform.right * -2.5f, 0f);
            yield return null;
            truck.Interact(pi);
            for (int i = 0; i < 10; i++) yield return null;
            Transform eye = null;
            foreach (var t in truck.GetComponentsInChildren<Transform>(true)) if (t.name == "CabView") eye = t;
            var head = body.rig.Head;
            float headErr = eye != null && head != null ? Vector3.Distance(head.position, eye.position) : 99f;
            Check(body.rig.pose == FPSBase.Player.MannequinRig.Pose.Sit && body.rig.transform.IsChildOf(truck.transform) && headErr < 0.2f,
                $"getting in: the driver sits in the cab (head {headErr:0.00} m from the eye point)");
            Material glass = null;
            foreach (var r in truck.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.name == "Windshield") glass = r.sharedMaterial;
                else foreach (var m in r.sharedMaterials)
                    if (m != null && m.name.Contains("Glass") && m.HasProperty("_BaseColor") && (glass == null || (glass.name != "Windshield" && m.GetColor("_BaseColor").a < glass.GetColor("_BaseColor").a))) glass = m; // the clearest pane (windshield), not a tinted rear one
            }
            Check(glass != null && glass.GetFloat("_SurfaceType") > 0.5f && glass.GetColor("_BaseColor").a < 0.5f, "cab windows are see-through glass");
            Check(body.rig.GetComponentInChildren<SkinnedMeshRenderer>().shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.On, "the seated driver is drawn (outside cab view)");
            Vector3 cabC = eye != null ? eye.position : tp + Vector3.up * 1.5f;
            yield return ShotAt(cabC + truck.transform.right * -2.6f + truck.transform.forward * 1.6f + Vector3.up * 0.3f, cabC - Vector3.up * 0.25f, "Screenshots/driver_in_cab.png");
            var fleetEx = FindObjectsByType<MiniExcavator>(FindObjectsSortMode.None);
            truck.Exit();
            yield return null;
            Check(body.rig.pose == FPSBase.Player.MannequinRig.Pose.Stand && body.rig.transform.parent == pi.transform, "getting out: the body comes back with you");
            trb.isKinematic = false;

            // a big excavator: driver visible through the glass cab
            MiniExcavator big = null;
            foreach (var e in fleetEx) if (e.displayName.Contains("20")) big = e;
            log.AppendLine($"INFO  excavators: {fleetEx.Length}, 20 t found {big != null}");
            if (big != null)
            {
                var erb = big.GetComponent<Rigidbody>();
                bool k0 = erb.isKinematic; erb.isKinematic = true;
                big.Interact(pi);
                for (int i = 0; i < 10; i++) yield return null;
                Transform bEye = null;
                foreach (var t in big.GetComponentsInChildren<Transform>(true)) if (t.name == "CabView") bEye = t;
                log.AppendLine($"INFO  excavator eye {(bEye ? bEye.position.ToString() : "none")}, occupied {big.Occupied}, active cam {(TuftRenderer.ActiveCamera() ? TuftRenderer.ActiveCamera().name : "none")}");
                if (bEye != null)
                    yield return ShotAt(bEye.position + bEye.right * -3.2f + bEye.forward * 2.2f + Vector3.up * 0.4f, bEye.position - Vector3.up * 0.3f, "Screenshots/excavator_driver.png");
                big.Exit();
                erb.isKinematic = k0;
                yield return null;
            }

            // settings apply without errors
            GraphicsApply.All();
            Check(Camera.main != null && Mathf.Abs(Camera.main.fieldOfView - GameSettings.Fov) < 20f, $"graphics settings applied (fov {GameSettings.Fov:0}, AA {GraphicsApply.AaNames[GameSettings.AntiAliasing]}, shadows {GameSettings.ShadowDistance:0} m)");
        }

        /// <summary>Screenshot from a temporary camera that follows `target` (doesn't touch the player's own camera).</summary>
        IEnumerator TempCamShot(Transform target, Vector3 offset, Vector3 lookOffset, string path)
        {
            var go = new GameObject("Shot Camera");
            var cam = go.AddComponent<Camera>();
            go.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
            cam.fieldOfView = 55f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 1500f; cam.depth = 50;
            var rt = new RenderTexture(1600, 900, 24);
            cam.targetTexture = rt;
            for (int i = 0; i < 45; i++)
            {
                Vector3 c = target.position + offset, l = target.position + lookOffset;
                go.transform.SetPositionAndRotation(c, Quaternion.LookRotation(l - c));
                yield return null;
            }
            yield return RenderLate(cam, rt, path);
            Destroy(go); rt.Release();
        }

        IEnumerator WalkPlayer(FPSBase.Player.SourceMovement mover, Vector3 dir, float speed, float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                mover.GetComponent<CharacterController>().Move(dir * speed * Time.deltaTime);
                yield return null;
            }
        }

        IEnumerator PourStream(Vector3 from, MaterialLoad each, int frames)
        {
            for (int i = 0; i < frames; i++) { MaterialTransfer.Pour(from, each, null); yield return null; }
        }

        /// <summary>Freeze the player and put the camera at an exact spot looking at a point.</summary>
        IEnumerator ShotAt(Vector3 camPos, Vector3 lookAt, string path, Transform follow = null)
        {
            var cam = Camera.main != null ? Camera.main : TuftRenderer.ActiveCamera();
            if (cam == null) yield break;
            var player = cam.GetComponentInParent<FPSBase.Player.SourceMovement>();
            if (player != null && follow == null) { player.enabled = false; player.GetComponent<FPSBase.Player.PlayerLook>().enabled = false; }
            var vcam = cam.GetComponent<VehicleCamera>();
            if (vcam != null) vcam.enabled = false;
            // with `follow`, the positions are offsets from that object (it may be moving)
            Vector3 f0 = follow != null ? follow.position : Vector3.zero;
            var rt = new RenderTexture(1600, 900, 24);
            cam.targetTexture = rt;
            for (int i = 0; i < 90; i++)
            {
                Vector3 d = follow != null ? follow.position - f0 : Vector3.zero;
                cam.transform.SetPositionAndRotation(camPos + d, Quaternion.LookRotation(lookAt - camPos));
                yield return null;
            }
            if (vcam != null) vcam.enabled = true;
            yield return RenderLate(cam, rt, path);
            cam.targetTexture = null;
        }

        /// <summary>
        /// Render and save at the very end of LateUpdate, so things drawn in LateUpdate (clumps in buckets and
        /// beds, track shoes) are in the shot, like they are in the game's own frames.
        /// </summary>
        IEnumerator RenderLate(Camera cam, RenderTexture rt, string path)
        {
            bool done = false;
            var late = GetComponent<LateShot>();
            if (late == null) late = gameObject.AddComponent<LateShot>();
            late.act = () =>
            {
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply();
                RenderTexture.active = null;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, tex.EncodeToPNG());
                done = true;
            };
            for (int i = 0; i < 10 && !done; i++) yield return null;
        }

        IEnumerator Shot(Vector3 pos, float yaw, float pitch, string path)
        {
            var cam = Camera.main;
            if (cam == null) yield break;
            var player = cam.GetComponentInParent<FPSBase.Player.SourceMovement>();
            if (player == null) yield break;
            player.GetComponent<FPSBase.Player.PlayerLook>().enabled = false;
            player.Teleport(pos);
            player.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            cam.transform.parent.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            var rt = new RenderTexture(1600, 900, 24);
            cam.targetTexture = rt;
            for (int i = 0; i < 90; i++) yield return null;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply();
            RenderTexture.active = null; cam.targetTexture = null;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
        }
    }

    /// <summary>Runs one action after every other LateUpdate (for test screenshots).</summary>
    [DefaultExecutionOrder(32000)]
    public class LateShot : MonoBehaviour
    {
        public System.Action act;
        void LateUpdate() { var a = act; act = null; a?.Invoke(); }
    }
}
