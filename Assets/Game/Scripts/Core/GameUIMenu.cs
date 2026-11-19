using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FPSBase.Core
{
    /// <summary>
    /// The main menu (shown when the game starts and after "Quit to main menu"): Single Player (a new world or a
    /// saved one) and Multiplayer (host a new or saved world over Steam, or join a friend). Also the pause menu's
    /// Save World row.
    /// </summary>
    public partial class GameUI
    {
        /// <summary>The main menu is up (the world waits behind it).</summary>
        public bool InMainMenu => menuOpen;
        bool menuOpen;
        enum MenuScreen { Main, Single, Multi, Blog }
        MenuScreen screen;
        string newWorldName = "My claim";
        string menuMessage;
        bool loading;
        string confirmDelete;
        Vector2 menuScroll;
        List<SaveGame.Info> saves;
        SaveGame.Mode savesMode;
        bool hostAfterLoad;

        // pause menu: saving
        string saveName;
        string saveMessage;
        float saveMessageUntil;
        bool confirmQuitToMenu;

        GUIStyle menuTitle, menuSub, menuButton, menuRich;

        void MenuAwake()
        {
            menuOpen = !Application.isBatchMode; // the automated tests start straight in the world
            screen = MenuScreen.Main;
            // dev: "-menushot <folder>" saves screenshots of the menus and the game, then quits
            var args = System.Environment.GetCommandLineArgs();
            int a = System.Array.IndexOf(args, "-menushot");
            if (a >= 0 && a + 1 < args.Length) StartCoroutine(MenuShots(args[a + 1]));
        }

        System.Collections.IEnumerator MenuShots(string folder)
        {
            System.IO.Directory.CreateDirectory(folder);
            System.Collections.IEnumerator Shot(string name)
            {
                for (int i = 0; i < 30; i++) yield return null;
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(folder, name));
                for (int i = 0; i < 10; i++) yield return null;
            }
            for (int i = 0; i < 90; i++) yield return null;
            yield return Shot("menu_main.png");
            screen = MenuScreen.Blog; yield return Shot("menu_blog.png");
            screen = MenuScreen.Single; RefreshSaves(SaveGame.Mode.SinglePlayer); yield return Shot("menu_single.png");
            screen = MenuScreen.Multi; RefreshSaves(SaveGame.Mode.Multiplayer); yield return Shot("menu_multi.png");
            CloseMenu(); yield return Shot("game_hud.png");
            paused = true; yield return Shot("game_paused.png");
            paused = false;
            var dealer = FindFirstObjectByType<FPSBase.Mining.VehicleDealer>();
            if (dealer != null)
            {
                dealer.Interact(null);
                dealer.DevShow(0, 2);
                for (int i = 0; i < 30 * 60 && (i < 10 || FPSBase.UI.VehiclePreviews.Pending > 0); i++) yield return null;
                yield return Shot("shop_dealer.png");
                dealer.DevShow(6, 30);
                yield return Shot("shop_dealer_transport.png");
                ClosePanel();
            }
            var plant = FindFirstObjectByType<FPSBase.Mining.WashPlant>();
            if (plant != null) { plant.Interact(null); yield return Shot("panel_plant.png"); ClosePanel(); }
            Application.Quit();
        }

        void MenuUpdate()
        {
            var net = FPSBase.Net.NetSession.Instance;
            // joined a friend's game (or hosted from the friends list): into the world
            if (menuOpen && !loading && FPSBase.Net.NetSession.InSession && net != null)
            {
                SaveGame.CurrentMode = SaveGame.Mode.Multiplayer;
                if (!net.IsHost) SaveGame.CurrentName = null; // a client can't save their friend's world
                CloseMenu();
            }
        }

        void CloseMenu()
        {
            menuOpen = false;
            menuMessage = null;
            paused = false;
        }

        void MenuStyles()
        {
            if (menuTitle != null) return;
            menuTitle = new GUIStyle(GUI.skin.label) { fontSize = 54, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            menuTitle.normal.textColor = new Color(0.96f, 0.78f, 0.3f);
            menuSub = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            menuButton = new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold };
            menuRich = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true, wordWrap = true };
        }

        void BlogScreen()
        {
            GUILayout.Label("<b><size=22>Dev Blog</size></b>", menuRich);
            GUILayout.Space(4);
            menuScroll = GUILayout.BeginScrollView(menuScroll, GUILayout.Height(Mathf.Max(200f, Mathf.Min(720f, Screen.height - 40f) - 250f)));
            foreach (var p in DevNotes.Blog)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label($"<color=#f5c84c><b><size=18>{p.title}</size></b></color>   <color=#aaa>{p.date}</color>", menuRich);
                GUILayout.Label(p.body, menuRich);
                GUILayout.EndVertical();
                GUILayout.Space(8);
            }
            GUILayout.EndScrollView();
            GUILayout.Space(8);
            if (GUILayout.Button("Back", GUILayout.Height(34))) { screen = MenuScreen.Main; menuScroll = Vector2.zero; }
        }

        GUIStyle testStyle, testHead;

        /// <summary>The "what to test" checklist down the left of the screen. Tick items off in a menu (the mouse is free there).</summary>
        void DrawTestList(bool clickable)
        {
            if (!GameSettings.ShowTestList) return;
            testStyle ??= new GUIStyle(hudSmall) { fontSize = 13, wordWrap = true, richText = true };
            testHead ??= new GUIStyle(hudSmall) { fontSize = 14, fontStyle = FontStyle.Bold, richText = true };
            var items = DevNotes.TestList;
            float w = Mathf.Clamp(Screen.width * 0.22f, 240f, 330f);
            float x = 10f, y = menuOpen ? 40f : 84f; // in the game: under the key hints
            float maxH = Screen.height - y - (ActiveVehicle != null && !menuOpen ? 230f : 20f);
            // measure
            float h = 46f;
            var heights = new float[items.Length];
            for (int i = 0; i < items.Length; i++) { heights[i] = Mathf.Max(18f, testStyle.CalcHeight(new GUIContent(items[i]), w - 36f)) + 2f; h += heights[i]; }
            if (clickable) h += 24f;
            h = Mathf.Min(h, maxH);
            GUI.color = Color.white;
            GUI.Box(new Rect(x, y, w, h), GUIContent.none, box);
            GUI.Box(new Rect(x, y, w, h), GUIContent.none, box);
            GUI.color = Color.white;
            GUI.BeginGroup(new Rect(x, y, w, h));
            GUI.Label(new Rect(8, 4, w - 16, 20), $"What to test  <color=#aaa>({DevNotes.DoneCount()}/{items.Length} done)</color>", testHead);
            GUI.Label(new Rect(8, 22, w - 16, 18), clickable ? "<color=#aaa>click to tick off</color>" : "<color=#aaa>tick off in the pause menu (Esc)</color>", testStyle);
            float cy = 44f;
            for (int i = 0; i < items.Length && cy < h - 4f; i++)
            {
                bool done = DevNotes.Done(i);
                string text = done ? $"<color=#8c8>{items[i]}</color>" : items[i];
                if (clickable)
                {
                    bool now = GUI.Toggle(new Rect(8, cy, 18, 18), done, GUIContent.none);
                    if (GUI.Button(new Rect(28, cy, w - 36, heights[i]), GUIContent.none, GUIStyle.none)) now = !done;
                    if (now != done) DevNotes.SetDone(i, now);
                }
                else GUI.Label(new Rect(8, cy, 18, 18), done ? "<color=#8c8>[x]</color>" : "•", testStyle);
                GUI.Label(new Rect(28, cy, w - 36, heights[i]), text, testStyle);
                cy += heights[i];
            }
            if (clickable && cy + 22f <= h && GUI.Button(new Rect(8, h - 24f, 110, 20), "Clear ticks")) DevNotes.ClearTicks();
            GUI.EndGroup();
        }

        void RefreshSaves(SaveGame.Mode mode) { savesMode = mode; saves = SaveGame.List(mode); confirmDelete = null; }

        void DrawMainMenu()
        {
            MenuStyles();
            GUI.color = new Color(1f, 1f, 1f, 0.92f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), box.normal.background);
            GUI.color = Color.white;
            float w = Mathf.Min(640f, Screen.width - 40f), h = Mathf.Min(720f, Screen.height - 40f);
            GUILayout.BeginArea(new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h));
            GUILayout.Label("DOWN AND DIRTY", menuTitle, GUILayout.Height(80));
            GUILayout.Label("dig it, wash it, get rich", menuSub);
            GUILayout.Space(24);
            if (loading) { GUILayout.Label("Loading the world…", menuSub); GUILayout.EndArea(); return; }
            switch (screen)
            {
                case MenuScreen.Main: MainScreen(); break;
                case MenuScreen.Single: SingleScreen(); break;
                case MenuScreen.Blog: BlogScreen(); break;
                default: MultiScreen(); break;
            }
            if (!string.IsNullOrEmpty(menuMessage)) { GUILayout.Space(10); GUILayout.Label($"<color=#f99>{menuMessage}</color>", menuRich); }
            GUILayout.EndArea();
        }

        void MainScreen()
        {
            if (GUILayout.Button("Single Player", menuButton, GUILayout.Height(58))) { screen = MenuScreen.Single; RefreshSaves(SaveGame.Mode.SinglePlayer); menuMessage = null; }
            GUILayout.Space(8);
            if (GUILayout.Button("Multiplayer", menuButton, GUILayout.Height(58))) { screen = MenuScreen.Multi; RefreshSaves(SaveGame.Mode.Multiplayer); menuMessage = null; }
            GUILayout.Space(8);
            if (GUILayout.Button("Dev Blog: what's new", menuButton, GUILayout.Height(48))) { screen = MenuScreen.Blog; menuScroll = Vector2.zero; menuMessage = null; }
            GUILayout.Space(8);
            if (GUILayout.Button("Quit", menuButton, GUILayout.Height(48))) QuitGame();
            GUILayout.Space(20);
            GUILayout.Label("Esc in the game opens the pause menu: save your world there.", menuSub);
        }

        void NewWorldRow(string button, System.Action start)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("World name", menuRich, GUILayout.Width(100));
            newWorldName = GUILayout.TextField(newWorldName ?? "", 40, GUILayout.Height(30));
            GUI.enabled = !string.IsNullOrWhiteSpace(newWorldName);
            if (GUILayout.Button(button, GUILayout.Height(30), GUILayout.Width(170))) start();
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        void SingleScreen()
        {
            GUILayout.Label("<b><size=22>Single Player</size></b>", menuRich);
            GUILayout.Space(6);
            NewWorldRow("Start new world", () =>
            {
                SaveGame.CurrentName = newWorldName.Trim(); SaveGame.CurrentMode = SaveGame.Mode.SinglePlayer;
                CloseMenu();
                Toast($"New world \"{SaveGame.CurrentName}\". Save it from the pause menu (Esc)");
            });
            GUILayout.Space(12);
            SaveList(info => LoadWorld(info, false));
            GUILayout.Space(10);
            if (GUILayout.Button("Back", GUILayout.Height(34))) { screen = MenuScreen.Main; menuMessage = null; }
        }

        void MultiScreen()
        {
            var net = FPSBase.Net.NetSession.Instance;
            GUILayout.Label("<b><size=22>Multiplayer</size></b>  (over Steam: up to 4 players)", menuRich);
            bool steam = net != null && net.SteamOk;
            if (!steam)
            {
                GUILayout.Label(net == null ? "Multiplayer isn't available in this build." : $"<color=#fc6>{net.Status}</color>", menuRich);
                GUILayout.Label("Start Steam, log in, then restart the game.", menuRich);
            }
            GUILayout.Space(6);
            GUI.enabled = steam;
            NewWorldRow("Host new world", () =>
            {
                SaveGame.CurrentName = newWorldName.Trim(); SaveGame.CurrentMode = SaveGame.Mode.Multiplayer;
                net.Host();
                CloseMenu();
                Toast("Starting the game: invite friends with F2");
            });
            GUILayout.Space(12);
            GUILayout.Label("<b>Host a saved multiplayer world</b>", menuRich);
            SaveList(info => LoadWorld(info, true));
            GUI.enabled = true;
            GUILayout.Space(10);
            if (steam)
            {
                GUILayout.Label("<b>Join a friend</b>  (they host from their main menu, then \"Join\" shows next to their name)", menuRich);
                GUILayout.BeginVertical(GUI.skin.box);
                net.DrawPanel();
                GUILayout.EndVertical();
            }
            GUILayout.Space(10);
            if (GUILayout.Button("Back", GUILayout.Height(34))) { screen = MenuScreen.Main; menuMessage = null; }
        }

        void SaveList(System.Action<SaveGame.Info> load)
        {
            if (saves == null) RefreshSaves(savesMode);
            if (saves.Count == 0) { GUILayout.Label("No saved worlds yet.", menuRich); return; }
            menuScroll = GUILayout.BeginScrollView(menuScroll, GUILayout.Height(Mathf.Min(220f, 44f * saves.Count + 8f)));
            foreach (var s in saves)
            {
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label($"<b>{s.name}</b>   <color=#bbb>{s.saved:g}   ${s.money:N0}</color>", menuRich, GUILayout.Height(30));
                if (GUILayout.Button(savesMode == SaveGame.Mode.Multiplayer ? "Host" : "Load", GUILayout.Width(80), GUILayout.Height(30))) load(s);
                if (confirmDelete == s.path)
                {
                    if (GUILayout.Button("Really delete", GUILayout.Width(110), GUILayout.Height(30))) { SaveGame.Delete(s); RefreshSaves(savesMode); GUIUtility.ExitGUI(); }
                }
                else if (GUILayout.Button("Delete", GUILayout.Width(110), GUILayout.Height(30))) confirmDelete = s.path;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        void LoadWorld(SaveGame.Info info, bool host)
        {
            loading = true; hostAfterLoad = host; menuMessage = null;
            StartCoroutine(SaveGame.Load(info.path, err =>
            {
                loading = false;
                if (err != null) { menuMessage = "Couldn't load: " + err; return; }
                CloseMenu();
                if (hostAfterLoad && FPSBase.Net.NetSession.Instance != null) FPSBase.Net.NetSession.Instance.Host();
                Toast(hostAfterLoad ? $"Hosting \"{info.name}\": invite friends with F2" : $"Loaded \"{info.name}\"");
            }));
            GUIUtility.ExitGUI();
        }

        static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ------------------------------------------------------------------ pause menu: save / main menu

        void DrawSaveRow()
        {
            var net = FPSBase.Net.NetSession.Instance;
            bool inSession = FPSBase.Net.NetSession.InSession;
            bool client = inSession && net != null && !net.IsHost;
            var mode = inSession ? SaveGame.Mode.Multiplayer : SaveGame.Mode.SinglePlayer;
            saveName ??= SaveGame.CurrentName ?? "My claim";
            GUILayout.BeginHorizontal();
            if (client) GUILayout.Label("Playing in your friend's world: only the host can save it.", hudSmall);
            else
            {
                GUILayout.Label("World", hudSmall, GUILayout.Width(50));
                saveName = GUILayout.TextField(saveName, 40, GUILayout.Height(30));
                GUI.enabled = !string.IsNullOrWhiteSpace(saveName);
                if (GUILayout.Button(mode == SaveGame.Mode.Multiplayer ? "Save (multiplayer)" : "Save (single player)", GUILayout.Height(30), GUILayout.Width(180)))
                {
                    string err = SaveGame.Save(saveName, mode);
                    saveMessage = err == null ? $"Saved \"{saveName.Trim()}\"" : "Couldn't save: " + err;
                    saveMessageUntil = Time.unscaledTime + 4f;
                }
                GUI.enabled = true;
            }
            GUILayout.EndHorizontal();
            if (saveMessage != null && Time.unscaledTime < saveMessageUntil) GUILayout.Label(saveMessage, hudSmall);
        }

        void QuitToMenuButton()
        {
            if (GUILayout.Button(confirmQuitToMenu ? "Unsaved progress is lost: sure?" : "Main Menu", GUILayout.Height(36), GUILayout.Width(confirmQuitToMenu ? 250 : 120)))
            {
                if (!confirmQuitToMenu) { confirmQuitToMenu = true; return; }
                confirmQuitToMenu = false;
                if (FPSBase.Net.NetSession.InSession) FPSBase.Net.NetSession.Instance.Leave();
                SaveGame.CurrentName = null;
                Time.timeScale = 1f;
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
        }
    }
}
