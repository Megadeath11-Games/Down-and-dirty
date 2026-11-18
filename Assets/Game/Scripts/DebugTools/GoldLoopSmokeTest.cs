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
                        case "fleet2": yield return Fleet2Tests(); break;
                        case "net": yield return NetSyncTests(); break;
                        case "update3": yield return Update3Tests(); break;
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

            // pour into the truck bed from above
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
            Check(site.ClumpDecorations > 200, $"loose dirt heaps are covered in clods ({site.ClumpDecorations})");
            ex.bucketLoad = default;
            for (int i = 0; i < 3; i++) yield return null;
            Check(exClumps != null && exClumps.Count == 0, "emptying the bucket removes its clumps");
            yield return Shot(new Vector3(60f, 0.3f, 58f), 0f, 6f, "Screenshots/outpost.png");

            yield return MaterialsTests(site, eco, plant, truck);
            yield return DirtPhysicsTests(site, truck);
            yield return SloshTests(site, truck);
            yield return PlayerTests(site, truck);

            yield return DigCycleTests();
            Finish();
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
            Vector3 at = new Vector3(40f, 0f, -132f); at.y = site.SurfaceY(at);
            ex.transform.SetPositionAndRotation(at, Quaternion.identity);
            ex.SetPose(-5f, -70f, -40f, 0f);
            Physics.SyncTransforms();
            for (int i = 0; i < 3; i++) yield return null;
            var cc = ex.GetComponent<ClumpContainer>();
            Vector3 tip = ex.bucketTip.position;
            Vector3 rp = tip + ex.transform.forward * 1.2f; rp.y = site.SurfaceY(rp) + 0.12f;
            var rock = Boulder.Spawn(rp, 0.16f, false);
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
                log.AppendLine($"INFO  dig cycle {e.displayName}: peak {peak:0.000} m3 of {e.bucketCapacity:0.00}, kept {e.bucketLoad.volume:0.000}, spilled {(cc != null ? cc.Spilled - sp0 : 0f):0.000}, clumps {(cc != null ? cc.Count : 0)}, mouth {mw}");
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
                Vector3 rim = sandSpot + Vector3.right * 1.9f;
                float rimBefore = site.SurfaceY(rim);
                Check(site.PendingCollapses > 0, $"the sand walls start to give way ({site.PendingCollapses} cells)");
                for (int i = 0; i < 90; i++) yield return null;
                yield return ShotAt(sandSpot + new Vector3(3.5f, 3.2f, -3.5f), sandSpot + Vector3.down * 1.2f, "Screenshots/cave_in.png");
                site.SettleNow();
                float caved = site.CollapsedVolume - c0;
                Check(caved > 0.5f && site.SurfaceY(rim) < rimBefore - 0.08f,
                    $"sand hole caved in: {caved:0.00} m³ slid in, the rim 1.9 m out dropped {rimBefore - site.SurfaceY(rim):0.00} m");
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
            int lugs = 0;
            foreach (var w in truck.wheelVisuals) if (w != null) foreach (Transform c in w) if (c.name == "Lug") lugs++;
            Check(lugs >= 4 * 8, $"truck tyres have tread lugs that spin with the wheels ({lugs})");

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
            Check(lost > 0.005f && Mathf.Abs(lost - spilled) < 0.002f,
                $"slamming the brakes sloshes the load over the front of the bed ({lost * 1000f:0} L, {cc.SpillCount - sp0} clods)");
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
                for (int i = 0; i < 90; i++) yield return null;
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
            foreach (var r in truck.GetComponentsInChildren<MeshRenderer>(true)) if (r.name == "Windshield") glass = r.sharedMaterial;
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
