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

        public const string TestListVersion = "2026-11-18b";

        /// <summary>What's new in this build to try out.</summary>
        public static readonly string[] TestList =
        {
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
