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

        public const string TestListVersion = "2026-11-18f";

        /// <summary>What's new in this build to try out.</summary>
        public static readonly string[] TestList =
        {
            "Buy the 7 t tilt trailer, hitch it to the pickup",
            "Tilt the deck down, drive a skid steer on, level it",
            "Fit the gooseneck ball (pickup menu), hitch the 10 t gooseneck",
            "Gooseneck: lower the ramps, drive the track loader or 5 t excavator on",
            "Haul a machine on each trailer: it stays chained",
            "Carry a full skid steer bucket around: it stays in",
            "Drive a loaded truck over bumps: the dirt feels heavy",
            "Tow the full bowser and brake hard: no spin, no jackknife",
            "Turn sharply with the bowser on: it hits the truck, not through it",
            "Tow the bowser over humps: the trailer flexes",
            "Dozer: R / T tilt the blade (each end drops)",
            "Dozer: G / H swivel the blade left / right",
            "Push with the blade angled: dirt rolls off to the side",
            "Try tilt and swivel on all three dozers",
            "Look at the new skid steers (stand-on, wheeled, tracked)",
            "Skid steer: dig a bucket full and dump it",
            "Pickup: X for 2WD / 4-high / 4-low, Z rear diff lock",
            "Pull the full bowser up a hill in 4-low",
            "Unhitch the bowser, drive into it: it's solid",
            "Park a dozer on a steep pile: it stays put",
            "Cross a loose berm with the 66 t dozer: the ruts pack down",
            "Big dozer feels smooth cutting; small machines still twitchy",
            "Main menu: Single Player > start a new world",
            "Esc > Save, then Main Menu > Single Player > Load it: everything is where you left it",
            "Delete a save (asks \"Really delete\" first)",
            "Multiplayer: host a new world, a friend joins",
            "Multiplayer: save as host, then host the saved world again",
            "Look at a machine and scroll: the selected option is readable",
            "Starter GMT400: take the tidy tank out / put it back (box empty)",
            "Refuel a machine from the pickup's tidy tank",
            "Bowser: hitch to the pickup, tow it, unhitch it",
            "Bowser: refill it at the gas station (scroll menu)",
            "Semi: detach the gooseneck, the deck drops to the ground",
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
