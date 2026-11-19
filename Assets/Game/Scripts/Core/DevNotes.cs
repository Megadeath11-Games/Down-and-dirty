using UnityEngine;

namespace FPSBase.Core
{
    /// <summary>
    /// The dev blog (main menu) and the "what to test" checklist shown on the left of the screen. Update both with
    /// every build: bump <see cref="TestListVersion"/> when the checklist changes so the ticks start fresh.
    /// </summary>
    public static class DevNotes
    {
        public struct Post { public string date, title, body; }

        /// <summary>Newest first.</summary>
        public static readonly Post[] Blog =
        {
            new Post { date = "Update 7  ·  Nov 19, 2026", title = "Multiplayer: trailers, wheels, wash plant",
                body =
                "<b>Multiplayer fixes</b>\n" +
                "• Trailers stay in sync: whoever drives the truck runs its trailer, and everyone else sees it follow on the hitch, wheels rolling, deck tilting, ramps and gooseneck moving.\n" +
                "• A parked trailer belongs to whoever used it last; hitch it or use its menu and it's yours.\n" +
                "• Trailers bought on their own (bowser, tilt deck, gooseneck) now show up for everyone, and after loading a save.\n" +
                "• Friends' pickups and dump trucks roll their wheels on your screen.\n" +
                "• The wash plant is shared: the host's plant does the washing, and everyone sees what's in the hopper and the sluice. Dump into it on any game and it goes in. Cleaning up the sluice pays whoever cleans it.\n" +
                "• A friend's gooseneck ball shows in their pickup's bed.\n" +
                "<b>Also fixed</b>\n" +
                "• Haul Truck 25t: the back axle was stuck under the truck and didn't drive. Both rear axles show and pull now.\n" +
                "• Fuel bowser (and every trailer): \"Set it back upright\" shows whenever it's leaning over, hitched or not. Hitched, it stands back up with its ring on the hook.\n" +
                "• Repositioning or towing a truck that's rolled on its side keeps the way it was pointing, and its trailer comes back upright behind it.\n" },
            new Post { date = "Update 6  ·  Nov 19, 2026", title = "Trailer fixes, $75 gold, camera zoom",
                body =
                "<b>Changed / fixed</b>\n" +
                "• Tilt-deck trailer: you can tilt the deck with a machine chained on; it tilts with the deck.\n" +
                "• Gooseneck trailer: both ramps are there now (the second one was missing).\n" +
                "• Dump box tailgates swing out the back as the box tips, instead of into the box.\n" +
                "• Pickup: letting off the gas coasts instead of braking hard; fixed the wheels locking up around 18 km/h with a heavy trailer on.\n" +
                "• Trailer brakes only come on when you brake (or stop): no more dragging over bumps and round corners.\n" +
                "• A chained machine's wheels no longer drag the trailer along.\n" +
                "• The pickup pulls the 5 t excavator on the tilt trailer in 2WD on flat ground (about 30 km/h in 8 s).\n" +
                "• A tipped trailer stays put instead of gliding along on its side, and its menu can set it back upright.\n" +
                "• Repositioning or towing a truck brings its trailer back upright, straight behind it.\n" +
                "• Gold sells for $7,500 a gram ($75 per 0.01 g).\n" +
                "• Pickup cab: your feet, the pedals and the seat no longer stick out under the truck.\n" +
                "• Driving: switching to first person (V) looks straight ahead; the scroll wheel zooms the third-person camera.\n" +
                "• Tracked machines no longer creep sideways down slopes and heap flanks, parked or driving.\n" +
                "• An angled blade carries more of its load, so less rolls off the side.\n" +
                "• Loader buckets fill at any angle short of dumping (the HUD's \"flat\"), and fill when you shove into a pile even if it stops you.\n" },
            new Post { date = "Update 5  ·  Nov 18, 2026", title = "Equipment trailers, heavy dirt and proper towing",
                body =
                "<b>New</b>\n" +
                "• 7 t tilt-deck equipment trailer (bumper-pull, $9,500 at the dealer). Hitch it to the pickup's hook, tilt the deck down from its menu, drive a skid steer on over the back, then level the deck and the machine is chained down.\n" +
                "• 10 t gooseneck equipment trailer ($14,500) with folding ramps at the back: lower them from its menu, drive on, park, fold them up.\n" +
                "• Gooseneck ball for the GMT400: fit it from the pickup's menu (dump box empty and down). It locks the dump box while it's in. Back the truck in under the gooseneck's coupler to hitch.\n" +
                "• Both trailers are built for the three skid steers and the 5 t compact excavator. Anything else isn't chained down.\n\n" +
                "<b>Changed / fixed</b>\n" +
                "• Dirt in buckets and truck beds feels heavy now: it sits still over bumps and gentle starts, shifts with a thud on a hard jolt, and doesn't bounce about like packing peanuts.\n" +
                "• Skid steers keep their load: a full bucket carried low and curled back loses only a few percent through a hard reverse.\n" +
                "• A tipped bucket still pours freely.\n" +
                "• Trailers have their own brakes: the truck's brake controller brakes the trailer to match, so a full fuel bowser no longer shoves the pickup into a spin or a jackknife when you slow down. Stopped, they hold it; pull away and they let go.\n" +
                "• Pickup anti-lock brakes: the wheels don't lock and slide out under hard braking.\n" +
                "• Hitched trailers collide with the truck again (only the tongue at the hitch passes through), so you can't drive into your own trailer.\n" +
                "• The bowser's tandem axles have softer, longer-travel springs and the hitch pitches further, so the rig flexes over humps." },
            new Post { date = "Update 4  ·  Nov 18, 2026", title = "Blade tilt, blade angle, new skid steers",
                body =
                "<b>New</b>\n" +
                "• Dozer blade tilt: R drops the left end, T drops the right end. Good for cutting a ditch or crowning a road.\n" +
                "• Dozer blade angle (swivel): G swings it left, H swings it right. An angled blade rolls the dirt off its trailing end into a windrow beside you.\n" +
                "• All three dozers have it (the mining dozer swivels a bit less).\n" +
                "• GMT400 pickup: X switches 2WD / 4-HIGH / 4-LOW, Z locks the rear diff. 4-low pulls a full fuel bowser up a steep hill.\n" +
                "• Heavy machines pack loose dirt down under their tracks: cross a berm with the 66 t dozer and its ruts sink in. Light machines hardly pack it.\n\n" +
                "<b>Changed</b>\n" +
                "• Skid steers rebuilt: the stand-on, the wheeled skid steer and the compact track loader all have new bodies, cabs, lift towers, longer two-piece arms and a deeper low-back bucket.\n" +
                "• Track loaders get proper undercarriages: drive sprocket with teeth, idlers and bottom rollers.\n" +
                "• Machines no longer creep down hills when parked or with you in the seat (tracks grip up to about 42°).\n" +
                "• Heavy machines ride smoother and heavier over rough cuts; the 1 t loader and mini excavator stay twitchy and tippy.\n" +
                "• The pickup has a bit more power and its tyres keep their grip when they spin; W while rolling back drives instead of braking.\n" +
                "• Fixed: after unhitching the fuel bowser, the truck could drive straight through it.\n" +
                "• The bowser now collides with everything again once you pull away." },
            new Post { date = "Update 3  ·  Nov 18, 2026", title = "Saved worlds, a main menu, and heavy haul",
                body =
                "<b>New</b>\n" +
                "• A main menu: Single Player (start a new world, or load / delete a saved one) and Multiplayer (host a new or saved world over Steam, or join a friend).\n" +
                "• Save your world from the pause menu (Esc). Single player saves and multiplayer saves (made by the host) are kept apart. A save keeps the dug ground, every machine and trailer (where they are, fuel, the pickup's tidy tank and dirt in the box, a lowboy's gooseneck on or off, a bowser's fuel and what it's hitched to), dirt piles, your money and gold, and where you stand.\n" +
                "• Main Menu button in the pause menu (it asks first: unsaved progress is lost).\n" +
                "• Semi trucks: 25 t, 55 t and 95 t, each with a removable-gooseneck (RGN) lowboy. Detach the neck, the deck drops, drive a machine on, park it and it's chained down; hook the neck back on and haul it.\n" +
                "• The starter truck is now a GMT400 3/4-ton 4x4 pickup with a removable 420 L tidy tank in the box.\n" +
                "• A 3,500 L field fuel bowser trailer: hitch it to the pickup, unhitch it on site, refill it at the pump from the scroll menu.\n" +
                "• This dev blog, and a test checklist on the left of the screen.\n\n" +
                "<b>Changed / fixed</b>\n" +
                "• The scroll menu on machines: the selected option is readable now (same size, bold) and the box fits long options.\n" +
                "• Machines chained on a trailer no longer spin their tyres or roll their tracks as the trailer moves.\n" +
                "• Removed the \"scroll to choose\" hint under the machine menu." },
            new Post { date = "Update 2", title = "In the cab",
                body =
                "<b>New</b>\n" +
                "• First-person cab view shows your own arms and legs working the joysticks, levers and pedals." },
            new Post { date = "Update 1", title = "Machine menu, towing and fuel",
                body =
                "<b>New</b>\n" +
                "• Look at a machine and scroll for its menu: refuel from a tidy tank, reposition it, or pay for a tow anywhere on the map.\n\n" +
                "<b>Fixed</b>\n" +
                "• Dealer deliveries land on clear ground.\n" +
                "• Dozer berms build up properly in front of the blade." },
        };

        public const string TestListVersion = "2026-11-19b";

        /// <summary>What's new in this build to try out.</summary>
        public static readonly string[] TestList =
        {
            "Multiplayer: a friend tows a trailer, it stays on their hitch on your screen",
            "Multiplayer: a friend tilts a deck / lowers ramps: you see it",
            "Multiplayer: hitch a trailer your friend parked, tow it: they see it follow",
            "Multiplayer: a friend buys a trailer: it appears for you",
            "Multiplayer: a friend's pickup drives by: its wheels roll",
            "Multiplayer: a friend dumps into the wash plant: you see it in the hopper",
            "Multiplayer (not host): clean up the sluice, you get the gold",
            "Haul Truck 25t: all three axles show and it drives",
            "Tip the fuel bowser over: its menu sets it back upright (hitched or not)",
            "Roll the pickup with the bowser on, reposition: both come back upright",
            "Tilt deck: tilt it down with a machine chained on, it rides down with the deck",
            "Gooseneck: both ramps are there and fold down",
            "Dump box: the tailgate swings out the back as it tips",
            "Pickup: let off the gas, it coasts (no hard braking)",
            "Pull the tilt trailer with the 5 t excavator on flat ground in 2WD",
            "Tow over bumps and round corners: the trailer doesn't drag its brakes",
            "Tip the bowser over: it stays put (no gliding)",
            "Reposition the pickup with a trailer on: the trailer comes back upright",
            "Sell gold: 0.01 g pays $75",
            "Pickup cab: no feet, pedals or seat showing under the truck",
            "Driving in 3rd person, press V: first person looks straight ahead",
            "Driving in 3rd person, scroll to zoom in and out",
            "Dozer parked sideways on a slope: it doesn't slide",
            "Tilted blade: clears a windrow without most of it rolling off",
            "Skid steer: push into a pile with the bucket flat, it fills",
            "Turn sharply with the bowser on: it hits the truck, not through it",
            "Park a dozer on a steep pile: it stays put",
            "Multiplayer: host a new world, a friend joins",
            "Multiplayer: save as host, then host the saved world again",
            "Drive a machine onto the lowboy and park: it gets chained down",
            "Hook the gooseneck back on and haul: the load's tyres / tracks stay still",
            "Get in the carried machine: the chains come off",
            "Save with the neck off / bowser hitched, load: they come back that way",
            "Read the Dev Blog on the main menu",
        };

        static string Key(int i) => $"test.{TestListVersion}.{i}";
        public static bool Done(int i) => PlayerPrefs.GetInt(Key(i), 0) == 1;
        public static void SetDone(int i, bool done) { PlayerPrefs.SetInt(Key(i), done ? 1 : 0); PlayerPrefs.Save(); }
        public static int DoneCount() { int n = 0; for (int i = 0; i < TestList.Length; i++) if (Done(i)) n++; return n; }
        public static void ClearTicks() { for (int i = 0; i < TestList.Length; i++) PlayerPrefs.DeleteKey(Key(i)); PlayerPrefs.Save(); }
    }
}
