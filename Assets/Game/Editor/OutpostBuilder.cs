using FPSBase.Core;
using FPSBase.Mining;
using UnityEngine;

namespace FPSBase.EditorTools
{
    /// <summary>The outpost: a fenced safe zone with a fuel station, assay office (gold buyer) and equipment dealer with a sales lot.</summary>
    public static partial class ProjectBootstrap
    {
        static readonly Vector3 OutpostCenter = new Vector3(60f, 0f, 88f);
        const float OutpostRadius = 26f;
        static readonly Vector3 HeavyYard = new Vector3(104f, 0f, 24f);

        static void BuildOutpost(Mats m, VehicleMats vm, VehiclePrefabs prefabs)
        {
            var root = new GameObject("Outpost (Safe Zone)");
            root.transform.position = OutpostCenter;
            var zone = root.AddComponent<SafeZone>();
            zone.zoneName = "Outpost"; zone.radius = OutpostRadius;
            var rt = root.transform;
            Vector3 c = OutpostCenter;

            // gravel/concrete yard
            Block(rt, "Yard", c + new Vector3(0f, 0.04f, 0f), new Vector3(OutpostRadius * 2f, 0.08f, OutpostRadius * 2f), m.stone, 4f);

            // wooden fence posts + rails around the edge, with a gate toward the claim (south)
            int posts = 40;
            Vector3 prev = Vector3.zero; bool havePrev = false;
            for (int i = 0; i <= posts; i++)
            {
                float a = i / (float)posts * Mathf.PI * 2f;
                Vector3 p = c + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * OutpostRadius;
                bool gate = Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, 180f)) < 14f; // south opening
                if (!gate && i < posts) Block(rt, "Fence Post", p + Vector3.up * 0.7f, new Vector3(0.2f, 1.4f, 0.2f), m.wood, 1f);
                if (havePrev && !gate)
                {
                    Vector3 mid = (prev + p) * 0.5f;
                    float len = Vector3.Distance(prev, p);
                    var rail = Block(rt, "Fence Rail", mid + Vector3.up * 1.0f, new Vector3(0.08f, 0.15f, len), m.wood, 1f);
                    rail.transform.rotation = Quaternion.LookRotation(p - prev);
                    var rail2 = Block(rt, "Fence Rail", mid + Vector3.up * 0.5f, new Vector3(0.08f, 0.15f, len), m.wood, 1f);
                    rail2.transform.rotation = Quaternion.LookRotation(p - prev);
                }
                prev = p; havePrev = !gate;
            }

            BuildFuelStation(rt, c + new Vector3(-12f, 0f, -8f), m, vm);
            BuildAssayOffice(rt, c + new Vector3(12f, 0f, -8f), m);
            BuildDealer(rt, c + new Vector3(8f, 0f, 14f), m, prefabs);
            MapMark(rt.Find("Fuel Station"), "Outpost fuel", new Color(0.95f, 0.3f, 0.25f), new Vector3(0f, 0f, -7f));
            MapMark(rt.Find("Assay Office (Gold Buyer)"), "Assay office", new Color(1f, 0.85f, 0.2f), new Vector3(0f, 0f, -5f));
            MapMark(rt.Find("Equipment Dealer"), "Dealer", new Color(0.4f, 0.65f, 1f), new Vector3(0f, 0f, -7f));
            var heavy = new GameObject("Heavy Equipment Yard (marker)").transform;
            heavy.position = HeavyYard;
            MapMark(heavy, "Heavy yard", new Color(1f, 0.75f, 0.1f), new Vector3(-10f, 0f, 0f));
        }

        static void BuildFuelStation(Transform parent, Vector3 at, Mats m, VehicleMats vm)
        {
            var root = new GameObject("Fuel Station").transform;
            root.SetParent(parent, true);
            root.position = at;
            // canopy
            for (int i = 0; i < 4; i++)
                Block(root, "Canopy Post", at + new Vector3(i < 2 ? -4f : 4f, 2.6f, i % 2 == 0 ? -3f : 3f), new Vector3(0.3f, 5.2f, 0.3f), m.metal, 2f);
            Block(root, "Canopy Roof", at + new Vector3(0f, 5.3f, 0f), new Vector3(10f, 0.3f, 8f), m.metal, 3f);
            // two diesel pumps on an island
            Block(root, "Pump Island", at + new Vector3(0f, 0.1f, 0f), new Vector3(1.4f, 0.2f, 5f), m.stone, 2f);
            for (int i = 0; i < 2; i++)
            {
                var pump = new GameObject("Diesel Pump " + (i + 1));
                pump.transform.SetParent(root, true);
                pump.transform.position = at + new Vector3(0f, 0f, i == 0 ? -1.5f : 1.5f);
                pump.AddComponent<FuelPump>().range = 10f;
                Part(pump.transform, "Pump Body", new Vector3(0f, 0.95f, 0f), new Vector3(0.7f, 1.6f, 0.5f), vm.truckPaint, true);
                Part(pump.transform, "Pump Display", new Vector3(0f, 1.45f, 0.26f), new Vector3(0.5f, 0.3f, 0.02f), vm.display);
                Part(pump.transform, "Pump Display B", new Vector3(0f, 1.45f, -0.26f), new Vector3(0.5f, 0.3f, 0.02f), vm.display);
            }
            // bulk tank
            var tank = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tank.name = "Bulk Diesel Tank";
            tank.transform.SetParent(root, true);
            tank.transform.position = at + new Vector3(-7f, 1.4f, 0f);
            tank.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            tank.transform.localScale = new Vector3(2.4f, 3f, 2.4f);
            tank.GetComponent<MeshRenderer>().sharedMaterial = vm.truckPaint;
        }

        static void BuildAssayOffice(Transform parent, Vector3 at, Mats m)
        {
            var root = new GameObject("Assay Office (Gold Buyer)");
            root.transform.SetParent(parent, true);
            root.transform.position = at;
            root.AddComponent<GoldBuyer>();
            var t = root.transform;
            const float w = 7f, d = 5f, h = 3.2f;
            Block(t, "Floor", at + new Vector3(0f, 0.15f, 0f), new Vector3(w, 0.3f, d), m.wood, 1.5f);
            Block(t, "Wall Back", at + new Vector3(0f, h / 2f + 0.3f, d / 2f), new Vector3(w, h, 0.15f), m.wood, 1.5f);
            Block(t, "Wall L", at + new Vector3(-w / 2f, h / 2f + 0.3f, 0f), new Vector3(0.15f, h, d), m.wood, 1.5f);
            Block(t, "Wall R", at + new Vector3(w / 2f, h / 2f + 0.3f, 0f), new Vector3(0.15f, h, d), m.wood, 1.5f);
            // front wall with a door opening in the middle
            Block(t, "Wall Front L", at + new Vector3(-w / 2f + 1.5f, h / 2f + 0.3f, -d / 2f), new Vector3(3f, h, 0.15f), m.wood, 1.5f);
            Block(t, "Wall Front R", at + new Vector3(w / 2f - 1.5f, h / 2f + 0.3f, -d / 2f), new Vector3(3f, h, 0.15f), m.wood, 1.5f);
            Block(t, "Wall Front Top", at + new Vector3(0f, h - 0.25f + 0.3f, -d / 2f), new Vector3(1f, 0.5f, 0.15f), m.wood, 1.5f);
            Block(t, "Roof", at + new Vector3(0f, h + 0.4f, 0f), new Vector3(w + 0.8f, 0.15f, d + 0.8f), m.metal, 2f);
            Block(t, "Counter", at + new Vector3(0f, 0.85f, 1.2f), new Vector3(4f, 1.1f, 0.6f), m.wood, 1.5f);
            Block(t, "Porch", at + new Vector3(0f, 0.1f, -d / 2f - 1f), new Vector3(w, 0.2f, 2f), m.wood, 1.5f);
        }

        static void BuildDealer(Transform parent, Vector3 at, Mats m, VehiclePrefabs prefabs)
        {
            var root = new GameObject("Equipment Dealer");
            root.transform.SetParent(parent, true);
            root.transform.position = at;
            var dealer = root.AddComponent<VehicleDealer>();
            var t = root.transform;
            const float w = 10f, d = 7f, h = 4.5f;
            Block(t, "Floor", at + new Vector3(0f, 0.1f, 0f), new Vector3(w, 0.2f, d), m.stone, 3f);
            Block(t, "Wall Back", at + new Vector3(0f, h / 2f, d / 2f), new Vector3(w, h, 0.2f), m.metal, 2f);
            Block(t, "Wall L", at + new Vector3(-w / 2f, h / 2f, 0f), new Vector3(0.2f, h, d), m.metal, 2f);
            Block(t, "Wall R", at + new Vector3(w / 2f, h / 2f, 0f), new Vector3(0.2f, h, d), m.metal, 2f);
            Block(t, "Wall Front L", at + new Vector3(-w / 2f + 2f, h / 2f, -d / 2f), new Vector3(4f, h, 0.2f), m.metal, 2f);
            Block(t, "Wall Front R", at + new Vector3(w / 2f - 2f, h / 2f, -d / 2f), new Vector3(4f, h, 0.2f), m.metal, 2f);
            Block(t, "Wall Front Top", at + new Vector3(0f, h - 0.6f, -d / 2f), new Vector3(2f, 1.2f, 0.2f), m.metal, 2f);
            Block(t, "Roof", at + new Vector3(0f, h + 0.1f, 0f), new Vector3(w + 0.6f, 0.2f, d + 0.6f), m.metal, 2f);
            Block(t, "Sales Desk", at + new Vector3(0f, 0.6f, 1.5f), new Vector3(3f, 1f, 0.8f), m.wood, 1.5f);

            // sales lot west of the dealer building
            var spots = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                var s = new GameObject("Lot Spot " + (i + 1)).transform;
                s.SetParent(parent, true);
                s.position = OutpostCenter + new Vector3(-16f + (i % 2) * 8f, 0.1f, 8f + (i / 2) * 9f);
                s.rotation = Quaternion.Euler(0f, 180f, 0f);
                spots[i] = s;
            }
            dealer.lotSpots = spots;

            // heavy-equipment yard on the dirt field, east of the wash plant: room for 90 t machines
            var yard = new GameObject("Heavy Equipment Yard").transform;
            yard.SetParent(parent, true);
            var heavy = new Transform[6];
            for (int i = 0; i < 6; i++)
            {
                var s = new GameObject("Heavy Spot " + (i + 1)).transform;
                s.SetParent(yard, true);
                s.position = HeavyYard + new Vector3((i % 3) * 18f, 0f, (i / 3) * 20f);
                s.rotation = Quaternion.Euler(0f, 180f, 0f);
                heavy[i] = s;
            }
            dealer.heavyLotSpots = heavy;
            var flag = Flat("SurveyFlag", new Color(1f, 0.45f, 0.05f));
            foreach (var corner in new[] { new Vector3(-8f, 0f, -10f), new Vector3(44f, 0f, -10f), new Vector3(-8f, 0f, 30f), new Vector3(44f, 0f, 30f) })
            {
                var post = new GameObject("Yard Post").transform;
                post.SetParent(yard, true);
                post.position = HeavyYard + corner;
                Part(post, "Post", new Vector3(0f, 1f, 0f), new Vector3(0.15f, 2f, 0.15f), m.wood);
                Part(post, "Flag", new Vector3(0.3f, 1.75f, 0f), new Vector3(0.55f, 0.32f, 0.03f), flag);
            }

            string ex = "Machines of 20 t and up are delivered to the heavy-equipment yard east of the wash plant.";
            VehicleDealer.Listing L(string cat, string name, float price, GameObject prefab, bool big, string desc) =>
                new VehicleDealer.Listing { category = cat, name = name, price = price, prefab = prefab, heavy = big, description = desc };
            var e = prefabs.excavators; var dz = prefabs.dozers;
            dealer.listings = new[]
            {
                L("Starter machines", "Mini Excavator 1.5t", 18000f, prefabs.mini, false, "Another 1.5 t mini excavator: 80 L bucket and a backfill blade."),
                L("Starter machines", "GMT400 3/4-Ton 4x4 Pickup", 11500f, prefabs.pickup, false, "Another '90s starter pickup: dump box (1.5 m³, or 1.0 m³ with the tank in), 420 L tidy tank to refuel your machines in the field (take it out for a bigger box), pintle hook for the fuel bowser and the tilt trailer, and a gooseneck ball you can fit in the bed for the gooseneck trailer. 2WD / 4-high / 4-low. 130 L tank."),
                L("Excavators", "Compact Excavator 5 t", 48000f, e[0], false, "180 L bucket and a front backfill blade (blade up/down keys): push dirt forward, or drag it back in reverse. 75 L tank."),
                L("Excavators", "Excavator 20 t", 165000f, e[1], true, "1.35 m³ bucket, twin boom rams and a dozer blade for backfilling. The workhorse: reaches bedrock easily. 340 L tank. " + ex),
                L("Excavators", "Heavy Excavator 50 t", 420000f, e[2], true, "3.6 m³ bucket, catwalks and handrails. Fills a haul truck in four passes. 700 L tank."),
                L("Excavators", "Mass Excavator 70 t", 680000f, e[3], true, "5.4 m³ mining bucket with wear shrouds. Late-game production machine. 950 L tank."),
                L("Excavators", "Mining Excavator 90 t", 950000f, e[4], true, "7.8 m³ bucket - the biggest digger on the claim. Slow, hungry, unstoppable. 1,300 L tank."),
                L("Dozers & loaders", "Bulldozer (small crawler)", 85000f, prefabs.dozer, false, "2.5 m blade. Cuts and pushes dirt, strips overburden fast, builds piles. 200 L tank."),
                L("Dozers & loaders", "Medium Dozer 22 t", 260000f, dz[0], true, "3.45 m semi-U blade on push arms (5.6 m³) and a 3-shank ripper: rip hard ground, then push it. 400 L tank."),
                L("Dozers & loaders", "Mining Dozer 66.5 t", 820000f, dz[1], true, "4.95 m U blade (18.5 m³) and a single giant ripper shank. Moves a hillside. 1,100 L tank."),
                L("Dozers & loaders", "Wheel Loader (compact)", 65000f, prefabs.loader, false, "0.9 m³ bucket. Scoops piles, carries dirt and loads trucks or the hopper. 150 L tank."),
                L("Skid steers & grader", "Stand-On Mini Track Loader 1 t", 32000f, prefabs.skids[0], false, "Ride-on-the-back mini track loader with a 160 L bucket. Tiny, nimble, fits anywhere. 30 L tank."),
                L("Skid steers & grader", "Wheeled Skid Steer 3.6 t", 62000f, prefabs.skids[1], false, "0.48 m³ bucket, skid steering: spins on the spot. Quick on hard ground. 95 L tank."),
                L("Skid steers & grader", "Compact Track Loader 5.2 t", 89000f, prefabs.skids[2], false, "0.78 m³ bucket on rubber tracks: pushes and carries in soft dirt. 105 L tank."),
                L("Skid steers & grader", "Motor Grader 19.5 t", 340000f, prefabs.grader, true, "4.27 m moldboard on a drawbar and circle: lift each end, turn, side-shift and pitch it, plus articulation, wheel lean and a rear ripper. Cuts and spreads dirt and gravel into flat roads. 416 L tank. " + ex),
                L("Trucks", "Single-Axle Dump Truck", 58000f, prefabs.dumpTruck, false, "6 m³ dump box (4x the pickup). 250 L tank."),
                L("Trucks", "Haul Truck 25t", 220000f, prefabs.hauler, false, "14 m³ haul truck for big operations. Slow but moves a mountain. 400 L tank."),
                L("Transport & fuel", "Field Fuel Bowser 3,500 L", 18000f, prefabs.bowser, false, "Tandem-axle diesel trailer with a pump, meter and hose reel. Tow it with a pickup's pintle hook, fill it at the fuel pump (look at it, scroll to Refuel), park it by your machines to fuel them. Comes with 1,200 L."),
                L("Transport & fuel", "Tilt-Deck Equipment Trailer 7 t", 9500f, prefabs.tilt7, false, "Bumper-pull tandem trailer (5.4 m oak deck, 4.8 t payload) for the pickup's hitch. Hydraulic tilt deck: tilt it down from its menu, drive on over the back, level it and the machine is chained down. Carries the skid steers and the 5 t excavator."),
                L("Transport & fuel", "Gooseneck Equipment Trailer 10 t", 14500f, prefabs.goose10, false, "Tandem-dual gooseneck trailer (6.7 m deck, 6.8 t payload). Fit the gooseneck ball in the pickup's bed and back in under the coupler. Folding ramps at the back: lower them, drive on, park and it's chained down. Carries the skid steers and the 5 t excavator."),
                L("Transport & fuel", "Semi Truck 25 t + RGN Lowboy", 145000f, prefabs.semis[0], true, "Tandem-drive tractor with a condo sleeper and a 25 t removable-gooseneck lowboy (6.4 m deck). Detach the neck to drop the deck, drive a machine on over the front, hook the neck back on and haul it. 600 L tank."),
                L("Transport & fuel", "Semi Truck 55 t + RGN Lowboy", 230000f, prefabs.semis[1], true, "Tri-drive heavy hauler with a 55 t RGN lowboy (7.4 m deck, 3 axles). Carries the 20 t and 50 t excavators and the 22 t dozer. 900 L tank."),
                L("Transport & fuel", "Semi Truck 95 t + RGN Lowboy", 360000f, prefabs.semis[2], true, "Quad-drive mining hauler, 95 t RGN lowboy with a 5.2 m wide deck and a two-axle booster. Moves the 70-90 t excavators and the mining dozer. 1,200 L tank."),
                // (new machines go on the end: a save keeps a machine by its place in this list)
                L("Excavators", "Mining Shovel 150 t", 1650000f, e[5], true, "12.5 m³ bucket on a 9.4 m boom: just massive. Two-storey house with catwalks, fills the 60 t mining haul truck in three passes. Too big for any lowboy: it's delivered to the heavy yard and walks from there. 2,200 L tank."),
                L("Trucks", "Mining Haul Truck 60 t", 780000f, prefabs.miningHauler, true, "30 m³ off-highway rock body, dual rear tyres taller than you, the cab up on the deck with a ladder and handrails. The 50 t excavator fills it in eight passes, the 150 t shovel in three. Off-road only. 800 L tank. " + ex),
            };
        }
    }
}
