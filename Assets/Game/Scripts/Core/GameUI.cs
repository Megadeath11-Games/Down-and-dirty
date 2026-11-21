using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FPSBase.Core
{
    /// <summary>Anything that wants to show status text on the HUD while the player is using it (vehicles).</summary>
    public interface IHudProvider
    {
        string HudTitle { get; }
        string HudBody { get; }
        string HudControls { get; }
        /// <summary>0..1 fill bar shown at the bottom centre (bucket / dump box), or negative for none.</summary>
        float HudBar { get; }
        string HudBarLabel { get; }
    }

    /// <summary>
    /// Placeholder in-game UI (IMGUI): money/gold HUD, interaction prompts, toasts, the pause/settings menu,
    /// and pop-up panels for machines and shops. Also owns mouse-cursor locking.
    /// </summary>
    public partial class GameUI : MonoBehaviour
    {
        public static GameUI Instance { get; private set; }

        public PlayerInteractor player;
        public IHudProvider ActiveVehicle { get; set; }

        bool paused;
        bool controlsOpen;
        GameAction? capturing;
        Vector2 controlsScroll;
        string panelTitle;
        Action panelDraw;
        Vector2 panelScroll;
        readonly System.Collections.Generic.List<(string text, float until)> toasts = new();

        GUIStyle hudBig, hudSmall, prompt, header, toastStyle, box, menuPanel;

        public bool BlocksGameplay => paused || panelDraw != null || menuOpen;
        /// <summary>The open panel's title (null when none is open).</summary>
        public string PanelTitle => panelDraw != null ? panelTitle : null;
        public bool Paused => paused || menuOpen;
        public void OpenMap() { mapView = default; OpenPanel("Map", DrawMap, 1120f); }

        void Awake() { Instance = this; MenuAwake(); if (GetComponent<MapView>() == null) gameObject.AddComponent<MapView>(); }

        /// <summary>Where the minimap sits (bottom right); the phone's message badge goes just above it.</summary>
        public static Rect MinimapRect => new Rect(Screen.width - 266f, Screen.height - 266f, 250f, 250f);

        void Start() => GraphicsApply.All();

        public static void Toast(string message)
        {
            if (Instance == null) { Debug.Log(message); return; }
            var t = Instance.toasts;
            t.RemoveAll(x => x.text == message);
            t.Add((message, Time.unscaledTime + 3.5f));
            if (t.Count > 4) t.RemoveAt(0);
        }

        public void OpenPanel(string title, Action draw, float width = 600f)
        {
            panelWidth = width;
            panelTitle = title;
            panelDraw = draw;
            panelDrawRect = null;
            panelScroll = Vector2.zero;
        }

        /// <summary>A big shop window that lays itself out in the rect it's given (no outer scroll).</summary>
        public void OpenShop(string title, Action<Rect> draw, Vector2? size = null)
        {
            shopSize = size ?? new Vector2(1280f, 800f);
            panelTitle = title;
            panelDraw = () => { };
            panelDrawRect = draw;
            panelScroll = Vector2.zero;
        }

        Action<Rect> panelDrawRect;
        Vector2 shopSize = new Vector2(1280f, 800f);
        float panelWidth = 600f;
        GUIStyle panelStyle;
        /// <summary>Set by a shop: shown at the top right of its window (your money).</summary>
        public string PanelCorner { get; set; }

        public void ClosePanel() { askInvite = false; panelDraw = null; panelDrawRect = null; PanelCorner = null; towing = null; towDest = null; }

        /// <summary>Just started hosting: open the multiplayer panel asking who to invite (friends in the game at the top).</summary>
        public void OfferInvites() { OpenMultiplayer(); askInvite = true; }
        bool askInvite;

        void OpenMultiplayer()
        {
            OpenPanel("Multiplayer", () =>
            {
                if (askInvite)
                {
                    GUILayout.BeginHorizontal(GUI.skin.box);
                    GUILayout.Label("<b><size=16>Your game is up. Invite friends?</size></b>  Pick \"Invite\" next to a friend below (friends with the game open are at the top).",
                        new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true });
                    if (GUILayout.Button("Not now", GUILayout.Width(100), GUILayout.Height(30))) { askInvite = false; ClosePanel(); GUIUtility.ExitGUI(); }
                    GUILayout.EndHorizontal();
                    GUILayout.Space(6);
                }
                var net = FPSBase.Net.NetSession.Instance;
                if (net != null) net.DrawPanel();
                else GUILayout.Label("Multiplayer isn't available in this session.");
            });
        }

        void Update()
        {
            FPSBase.UI.MenuScene.Want(menuOpen && !loading && !FPSBase.Net.NetSession.InSession);
            var kb = Keyboard.current;
            if (menuOpen)
            {
                MenuUpdate();
                if (capturing.HasValue) CaptureKey(kb);
                else if (menuOpen && Controls.MenuBack(true))
                {
                    if (controlsOpen) controlsOpen = false;
                    else if (screen != MenuScreen.Main) { screen = MenuScreen.Main; menuMessage = null; }
                }
                // (the world waits while the menu is up, unless friends are in it or the real machines are working behind it)
                if (menuOpen) Time.timeScale = FPSBase.Net.NetSession.InSession || FPSBase.UI.MenuScene.Live ? 1f : 0f;
                if (menuOpen && AudioListener.pause != (Time.timeScale == 0f)) AudioListener.pause = Time.timeScale == 0f;
                ApplyCursor(Application.isFocused ? CursorLockMode.Confined : CursorLockMode.None, true);
                return;
            }
            AutoSaveTick();
            if (capturing.HasValue) CaptureKey(kb);
            else if (!paused && Controls.Pressed(GameAction.Map))
            {
                if (panelTitle == "Map" && panelDraw != null) ClosePanel(); else OpenPanel("Map", DrawMap, 1120f);
            }
            else if (!paused && Controls.Pressed(GameAction.Help))
            {
                if (panelTitle == "Help & Tips" && panelDraw != null) ClosePanel(); else OpenPanel("Help & Tips", DrawHelp);
            }
            else if (!paused && Controls.Pressed(GameAction.Invite))
            {
                if (panelTitle == "Multiplayer" && panelDraw != null) ClosePanel(); else OpenMultiplayer();
            }
            else if (Controls.MenuBack(Cursor.visible && Cursor.lockState != CursorLockMode.Locked))
            {
                if (controlsOpen) controlsOpen = false;
                else if (panelDraw != null) ClosePanel();
                else { paused = !paused; confirmQuitToMenu = false; saveName = null; }
            }
            // in a multiplayer game the world keeps going while the menu is open
            Time.timeScale = paused && !FPSBase.Net.NetSession.InSession ? 0f : 1f;
            // the world's sounds stop with it (the music carries on)
            bool hush = Time.timeScale == 0f;
            if (AudioListener.pause != hush) AudioListener.pause = hush;
            if ((applyTimer -= Time.unscaledDeltaTime) <= 0f) { applyTimer = 0.5f; GraphicsApply.Cameras(); }
            fpsSmooth = Mathf.Lerp(fpsSmooth, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.05f);
            // playing: locked in the middle. A menu, the map or the phone open: free to move but kept inside the
            // game's window (so it can't wander onto another monitor). Only let go when the game isn't in front.
            var want = !Application.isFocused ? CursorLockMode.None : !BlocksGameplay ? CursorLockMode.Locked : CursorLockMode.Confined;
            ApplyCursor(want, want != CursorLockMode.Locked);
        }

        bool cursorFocused;
        float cursorRefresh;
        /// <summary>
        /// Lock (or confine) the cursor. Windows lets go of the cursor's clip when the game loses focus, a window pops
        /// up over it, or the display changes, while Unity still thinks it's locked, so the mouse wandered onto the
        /// other monitor. Coming back into focus, and every couple of seconds while it's in front, the lock is set
        /// again from scratch.
        /// </summary>
        void ApplyCursor(CursorLockMode want, bool show)
        {
            bool focused = Application.isFocused;
            bool refresh = focused && (!cursorFocused || (cursorRefresh -= Time.unscaledDeltaTime) <= 0f);
            cursorFocused = focused;
            if (refresh && want != CursorLockMode.None)
            {
                cursorRefresh = 2f;
                Cursor.lockState = CursorLockMode.None;
                Cursor.lockState = want;
            }
            else if (Cursor.lockState != want) Cursor.lockState = want;
            if (Cursor.visible != show) Cursor.visible = show;
        }

        void Styles()
        {
            if (hudBig != null) return;
            hudBig = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            hudSmall = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            prompt = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            header = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            header.normal.textColor = FPSBase.UI.ShopTheme.Gold;
            hudBig.normal.textColor = FPSBase.UI.ShopTheme.Gold;
            toastStyle = FPSBase.UI.ShopTheme.Toast;
            box = new GUIStyle(GUI.skin.box) { border = new RectOffset(7, 7, 7, 7) };
            box.normal.background = FPSBase.UI.ShopTheme.TexHud;
            menuPanel = new GUIStyle(box) { padding = new RectOffset(20, 20, 14, 18) };
            menuPanel.normal.background = FPSBase.UI.ShopTheme.TexDeep;
            markerStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.UpperCenter };
            markerStyle.normal.textColor = Color.white;
        }

        static Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        void OnGUI()
        {
            GUI.skin = FPSBase.UI.ShopTheme.Skin; // (every menu and box in the dealer's colours)
            Styles();
            if (menuOpen) { if (!menuBare) { DrawMainMenu(); DrawTestList(true); } return; }
            var eco = Economy.Instance;

            // Wallet (top right)
            if (eco != null)
            {
                var r = new Rect(Screen.width - 270, 10, 260, 64);
                GUI.Box(r, GUIContent.none, box);
                GUI.Label(new Rect(r.x + 12, r.y + 4, 240, 30), eco.Unlimited ? "$ ∞  (test mode)" : $"${eco.Money:N0}", hudBig);
                GUI.Label(new Rect(r.x + 12, r.y + 34, 240, 24), $"Gold: {eco.GoldGrams:0.00} g" + (eco.Unlimited ? $"   ·   earned ${eco.Money:N0}" : ""), hudSmall);
            }

            if (GameSettings.ShowFps) GUI.Label(new Rect(10, Screen.height - 26, 120, 22), $"{fpsSmooth:0} FPS", hudSmall);

            // Vehicle HUD (bottom left)
            if (ActiveVehicle != null && !paused)
            {
                string body = ActiveVehicle.HudBody;
                string controls = ActiveVehicle.HudControls;
                var r = new Rect(10, Screen.height - 280, 470, 270);
                GUI.Box(r, GUIContent.none, box);
                GUI.Label(new Rect(r.x + 10, r.y + 6, 450, 28), ActiveVehicle.HudTitle, hudBig);
                GUI.Label(new Rect(r.x + 10, r.y + 36, 450, 112), body, hudSmall);
                GUI.Label(new Rect(r.x + 10, r.y + 150, 450, 116), controls, hudSmall);
            }

            // Big fill bar (bucket / dump box)
            if (ActiveVehicle != null && !paused && ActiveVehicle.HudBar >= 0f)
            {
                float f = Mathf.Clamp01(ActiveVehicle.HudBar);
                // as wide as the words need (between the machine panel on the left and the minimap), wrapping
                // onto more lines above if they still don't fit
                var label = new GUIContent(ActiveVehicle.HudBarLabel);
                barLabel ??= new GUIStyle(prompt) { fontSize = 16, wordWrap = true, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(10, 10, 4, 4) };
                float cx = Screen.width / 2f;
                float room = 2f * Mathf.Max(230f, Mathf.Min(cx - 495f, Screen.width - 286f - cx));
                float bw = Mathf.Clamp(barLabel.CalcSize(label).x + 8f, 460f, room);
                float bh = Mathf.Max(30f, barLabel.CalcHeight(label, bw));
                var r = new Rect(cx - bw / 2f, Screen.height - 44 - bh, bw, bh);
                GUI.Box(r, GUIContent.none, box);
                var fill = new Rect(r.x + 3, r.y + 3, (r.width - 6) * f, r.height - 6);
                GUI.color = f >= 0.98f ? new Color(1f, 0.55f, 0.1f, 0.85f) : new Color(0.35f, 0.8f, 0.3f, 0.85f);
                GUI.DrawTexture(fill, Texture2D.whiteTexture);
                GUI.color = new Color(0f, 0f, 0f, 0.8f);
                GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), label, barLabel);
                GUI.color = Color.white;
                GUI.Label(r, label, barLabel);
            }

            // Safe zone banner
            var who = ActiveVehicle as Component;
            Vector3 here = who != null ? who.transform.position : (player != null ? player.transform.position : Vector3.zero);
            var zone = SafeZone.At(here);
            if (!paused && panelDraw == null) { DrawCompass(here); DrawMinimap(here); }
            if (zone != null && !paused)
                GUI.Label(new Rect(Screen.width / 2f - 300, 76, 600, 30), zone.zoneName + "  —  SAFE ZONE", prompt);
            if (!paused && panelDraw == null)
                GUI.Label(new Rect(10, 10, 420, 22), $"[{Controls.N(GameAction.Map)}] map & machines   [{Controls.N(GameAction.Help)}] help" + (ActiveVehicle == null ? $"   [{Controls.N(GameAction.DetectorToggle)}] metal detector" : $"   [{Controls.N(GameAction.Recover)}] flip upright"), hudSmall);

            // Interaction prompt
            if (player != null && player.isActiveAndEnabled && player.Current != null && !BlocksGameplay)
                DrawLookPrompt();

            // Toast
            toasts.RemoveAll(x => Time.unscaledTime > x.until);
            for (int i = 0; i < toasts.Count; i++)
            {
                float a = Mathf.Clamp01((toasts[i].until - Time.unscaledTime) * 2f);
                GUI.color = new Color(1f, 1f, 1f, a);
                GUI.Box(new Rect(Screen.width / 2f - 320, 110 + i * 40, 640, 36), toasts[i].text, toastStyle);
            }
            GUI.color = Color.white;

            if (panelDraw == null) DrawTestList(paused);
            if (panelDraw != null) DrawPanel();
            if (paused) { if (controlsOpen) DrawControlsMenu(); else DrawPauseMenu(); }
        }

        // ------------------------------------------------------------------ compass

        /// <summary>Which way you're looking (the camera's heading, degrees from north).</summary>
        static float CamYaw()
        {
            var cam = Camera.main;
            if (cam == null) foreach (var c in Camera.allCameras) if (c.isActiveAndEnabled) { cam = c; break; }
            return cam != null ? cam.transform.eulerAngles.y : 0f;
        }

        GUIStyle compassBig, compassSmall;

        /// <summary>
        /// The compass strip: N/E/S/W and the in-betweens, a pin for each place, and its name and distance
        /// underneath in up to two rows. The places nearest where you're looking get their names first; ones that
        /// would overlap just show the pin.
        /// </summary>
        void DrawCompass(Vector3 here)
        {
            float yaw = CamYaw();
            const float w = 640f, half = 80f; // degrees shown either side
            var r = new Rect(Screen.width / 2f - w / 2f, 6, w, 30);
            GUI.Box(r, GUIContent.none, box);
            compassBig ??= new GUIStyle(prompt) { fontSize = 17, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            compassSmall ??= new GUIStyle(prompt) { fontSize = 11, fontStyle = FontStyle.Normal, alignment = TextAnchor.MiddleCenter };
            float X(float bearing) => r.center.x + Mathf.DeltaAngle(yaw, bearing) / half * (w / 2f);
            for (int d = 0; d < 360; d += 15)
            {
                float dd = Mathf.DeltaAngle(yaw, d);
                if (Mathf.Abs(dd) > half) continue;
                float x = X(d);
                GUI.color = new Color(1f, 1f, 1f, 1f - Mathf.Abs(dd) / half * 0.6f);
                string big = d switch { 0 => "N", 90 => "E", 180 => "S", 270 => "W", _ => null };
                string mid = d switch { 45 => "NE", 135 => "SE", 225 => "SW", 315 => "NW", _ => null };
                if (big != null) { if (d == 0) GUI.color = new Color(1f, 0.55f, 0.4f, GUI.color.a); GUI.Label(new Rect(x - 15, r.y + 1, 30, 26), big, compassBig); }
                else if (mid != null) GUI.Label(new Rect(x - 15, r.y + 3, 30, 22), mid, compassSmall);
                else GUI.DrawTexture(new Rect(x - 0.5f, r.y + 11, 1, 8), Texture2D.whiteTexture);
            }
            // where you're facing
            GUI.color = new Color(1f, 0.82f, 0.35f);
            GUI.DrawTexture(new Rect(r.center.x - 1, r.y, 2, 6), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.center.x - 1, r.yMax - 6, 2, 6), Texture2D.whiteTexture);

            var marks = new System.Collections.Generic.List<(float x, string text, Color col)>();
            void Mark(Vector3 at, string label, Color col)
            {
                Vector3 d = at - here; d.y = 0f;
                float dist = d.magnitude;
                if (dist < 3f) return;
                float bearing = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                if (Mathf.Abs(Mathf.DeltaAngle(yaw, bearing)) > half) return;
                marks.Add((X(bearing), $"{label}  {(dist >= 1000f ? $"{dist / 1000f:0.0} km" : $"{dist:0} m")}", col));
            }
            foreach (var m in MapMarker.All) Mark(m.transform.position, m.label, m.color);
            var last = FPSBase.Vehicles.VehicleBase.LastDriven;
            if (last != null && !ReferenceEquals(last, ActiveVehicle)) Mark(last.transform.position, last.displayName, new Color(0.55f, 0.85f, 1f));

            // pins on the strip
            foreach (var (x, _, col) in marks) { GUI.color = col; GUI.DrawTexture(new Rect(x - 5, r.yMax - 11, 10, 10), MapView.Pin); }
            // names under it, nearest the middle first, two rows, never on top of each other
            marks.Sort((p, q) => Mathf.Abs(p.x - r.center.x).CompareTo(Mathf.Abs(q.x - r.center.x)));
            var rows = new[] { new System.Collections.Generic.List<Rect>(), new System.Collections.Generic.List<Rect>() };
            foreach (var (x, text, col) in marks)
            {
                float tw = markerStyle.CalcSize(new GUIContent(text)).x + 12f;
                float lx = Mathf.Clamp(x - tw / 2f, r.xMin, r.xMax - tw);
                for (int row = 0; row < 2; row++)
                {
                    var lr = new Rect(lx, r.yMax + 2 + row * 19, tw, 18);
                    bool clash = false; foreach (var o in rows[row]) if (o.Overlaps(lr)) { clash = true; break; }
                    if (clash) continue;
                    rows[row].Add(lr);
                    GUI.color = new Color(0.05f, 0.04f, 0.03f, 0.72f); GUI.DrawTexture(lr, Texture2D.whiteTexture);
                    GUI.color = Color.Lerp(col, Color.white, 0.45f); GUI.Label(lr, text, markerStyle);
                    break;
                }
            }
            GUI.color = Color.white;
        }

        // ------------------------------------------------------------------ minimap

        float minimapRange = 170f;
        GUIStyle miniNote;

        void DrawMinimap(Vector3 here)
        {
            var mv = MapView.Instance;
            if (mv == null || !mv.Ready) return;
            var r = MinimapRect;
            GUI.Box(new Rect(r.x - 5, r.y - 5, r.width + 10, r.height + 10), GUIContent.none, box);
            mv.Draw(r, new Rect(here.x - minimapRange / 2f, here.z - minimapRange / 2f, minimapRange, minimapRange), false, here, CamYaw());
            miniNote ??= new GUIStyle(hudSmall) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            GUI.color = new Color(0f, 0f, 0f, 0.55f); GUI.DrawTexture(new Rect(r.center.x - 11, r.y + 2, 22, 18), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.6f, 0.45f); GUI.Label(new Rect(r.center.x - 11, r.y + 2, 22, 18), "N", miniNote);
            GUI.color = new Color(0f, 0f, 0f, 0.55f); GUI.DrawTexture(new Rect(r.xMax - 122, r.yMax - 20, 120, 18), Texture2D.whiteTexture);
            GUI.color = Color.white; GUI.Label(new Rect(r.xMax - 122, r.yMax - 20, 120, 18), $"{minimapRange:0} m  [{Controls.N(GameAction.Map)}] map", miniNote);
        }

        GUIStyle markerStyle;

        // ------------------------------------------------------------------ look-at prompt / menu

        GUIStyle optStyle, optSelStyle, promptWrap, barLabel;

        /// <summary>The look-at prompt: on a dark backing, as wide as it needs, wrapping onto more lines if it's long. Returns its height.</summary>
        float PromptLine(float cx, float y, string text)
        {
            promptWrap ??= new GUIStyle(prompt) { wordWrap = true, padding = new RectOffset(12, 12, 4, 4) };
            var c = new GUIContent(text);
            float w = Mathf.Min(promptWrap.CalcSize(c).x + 4f, Mathf.Min(900f, Screen.width - 40f));
            float h = Mathf.Max(30f, promptWrap.CalcHeight(c, w));
            var r = new Rect(cx - w / 2f, y, w, h);
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(r, c, promptWrap);
            return h;
        }

        void DrawLookPrompt()
        {
            var opts = player.Options;
            string key = Controls.N(GameAction.Interact);
            float cx = Screen.width / 2f, y = Screen.height / 2f + 30;
            if (opts.Count == 0)
            {
                PromptLine(cx, y, $"[{key}] " + player.Current.Prompt);
                return;
            }
            y += PromptLine(cx, y, player.Current.Prompt) + 4f;
            optStyle ??= new GUIStyle(hudSmall) { wordWrap = false, clipping = TextClipping.Clip, alignment = TextAnchor.MiddleLeft };
            optSelStyle ??= new GUIStyle(optStyle) { fontStyle = FontStyle.Bold };
            const float h = 24f;
            var texts = new string[opts.Count];
            float w = 360f;
            for (int i = 0; i < opts.Count; i++)
            {
                var o = opts[i];
                texts[i] = (i == player.Selected ? $"[{key}]  " : "      ") + o.label + (!o.enabled && !string.IsNullOrEmpty(o.note) ? $"   ({o.note})" : "");
                w = Mathf.Max(w, optSelStyle.CalcSize(new GUIContent("[" + key + "]  " + o.label + (!o.enabled && !string.IsNullOrEmpty(o.note) ? $"   ({o.note})" : ""))).x + 16f);
            }
            w = Mathf.Min(w, Screen.width - 40f);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(cx - w / 2f - 6, y - 3, w + 12, opts.Count * h + 6), Texture2D.whiteTexture);
            for (int i = 0; i < opts.Count; i++)
            {
                var o = opts[i];
                bool sel = i == player.Selected;
                if (sel) { GUI.color = new Color(1f, 0.8f, 0.3f, 0.35f); GUI.DrawTexture(new Rect(cx - w / 2f - 4, y + i * h, w + 8, h - 2), Texture2D.whiteTexture); }
                GUI.color = !o.enabled ? new Color(1f, 1f, 1f, 0.45f) : sel ? new Color(1f, 0.92f, 0.6f) : Color.white;
                GUI.Label(new Rect(cx - w / 2f + 4, y + i * h, w - 4, h), texts[i], sel ? optSelStyle : optStyle);
            }
            GUI.color = Color.white;
        }

        // ------------------------------------------------------------------ towing (paid fast travel for a machine)

        FPSBase.Vehicles.VehicleBase towing;
        Vector3? towDest;

        /// <summary>Open the map to pick where a tow truck should take `v` (you ride along).</summary>
        public void OpenTow(FPSBase.Vehicles.VehicleBase v)
        {
            towing = v; towDest = null; mapView = default;
            OpenPanel("Map", DrawMap, 1120f);
        }

        // ------------------------------------------------------------------ map

        static Vector3 Ground(Vector3 p) => FPSBase.Mining.VehicleDealer.GroundAt(p);

        Rect mapView;           // the part of the world the big map shows (zoom / drag)
        bool mapGrab, mapDragged;
        Vector2 mapDown;

        static Rect Zoomed(Rect v, float k, Rect world)
        {
            float ns = Mathf.Clamp(v.width * k, 40f, world.width * 1.25f);
            return new Rect(v.center.x - ns / 2f, v.center.y - ns / 2f, ns, ns);
        }

        void DrawMap()
        {
            var site = FPSBase.Mining.DigSite.Instance;
            var who = ActiveVehicle as Component;
            Vector3 me = who != null ? who.transform.position : (player != null ? player.transform.position : Vector3.zero);
            Rect world = site != null ? site.FieldRect : new Rect(-50, -200, 300, 320);
            foreach (var g in FPSBase.Mining.DigSite.All)
                if (g != null && g != site) { var f = g.FieldRect; world = Rect.MinMaxRect(Mathf.Min(world.xMin, f.xMin), Mathf.Min(world.yMin, f.yMin), Mathf.Max(world.xMax, f.xMax), Mathf.Max(world.yMax, f.yMax)); }
            foreach (var m in MapMarker.All)
            {
                Vector3 q = m.transform.position;
                world = Rect.MinMaxRect(Mathf.Min(world.xMin, q.x - 20), Mathf.Min(world.yMin, q.z - 20), Mathf.Max(world.xMax, q.x + 20), Mathf.Max(world.yMax, q.z + 30));
            }
            var mv = MapView.Instance;
            if (mv != null && mv.Ready) world = mv.World;
            else { float size = Mathf.Max(world.width, world.height); world = new Rect(world.center.x - size / 2f, world.center.y - size / 2f, size, size); }
            if (mapView.width <= 0f) mapView = world;

            GUILayout.Label("Scroll to zoom in, drag to move the map. Click Travel to walk there instantly (on foot only). A tow truck brings an empty machine to you, or takes it anywhere (it costs money).", hudSmall);
            Rect r = GUILayoutUtility.GetRect(500, Mathf.Clamp(Screen.height - 400f, 420f, 720f), GUILayout.ExpandWidth(true));
            float px = Mathf.Min(r.width, r.height);
            r = new Rect(r.x + (r.width - px) / 2f, r.y, px, px);

            // zoom (scroll, toward the mouse) and drag
            var ev = Event.current;
            Vector3 W(Vector2 q) => new Vector3(mapView.x + (q.x - r.x) / r.width * mapView.width, 0f, mapView.y + (r.yMax - q.y) / r.height * mapView.height);
            if (r.Contains(ev.mousePosition) && ev.type == EventType.ScrollWheel)
            {
                Vector3 at = W(ev.mousePosition);
                float k = ev.delta.y > 0f ? 1.25f : 0.8f;
                float ns = Mathf.Clamp(mapView.width * k, 40f, world.width * 1.25f);
                float fx = (at.x - mapView.x) / mapView.width, fz = (at.z - mapView.y) / mapView.height;
                mapView = new Rect(at.x - fx * ns, at.z - fz * ns, ns, ns);
                ev.Use();
            }
            else if (r.Contains(ev.mousePosition) && ev.type == EventType.MouseDown && ev.button == 0) { mapGrab = true; mapDragged = false; mapDown = ev.mousePosition; ev.Use(); }
            else if (mapGrab && ev.type == EventType.MouseDrag)
            {
                if ((ev.mousePosition - mapDown).sqrMagnitude > 16f) mapDragged = true;
                float ppm = r.width / mapView.width;
                mapView.x -= ev.delta.x / ppm; mapView.y += ev.delta.y / ppm;
                ev.Use();
            }
            else if (mapGrab && ev.type == EventType.MouseUp)
            {
                mapGrab = false;
                if (!mapDragged && towing != null && r.Contains(ev.mousePosition)) towDest = Ground(W(ev.mousePosition));
                ev.Use();
            }

            var shown = mapView;
            Vector2 M(Vector3 p) => new Vector2(r.x + (p.x - shown.x) / shown.width * r.width, r.yMax - (p.z - shown.y) / shown.height * r.height);
            if (mv != null) mv.Draw(r, shown, true, me, CamYaw());
            void Dot(Vector3 p, float s, Color c, string label)
            {
                Vector2 q = M(p);
                if (!r.Contains(q)) return;
                GUI.color = Color.black; GUI.DrawTexture(new Rect(q.x - s / 2 - 1, q.y - s / 2 - 1, s + 2, s + 2), Texture2D.whiteTexture);
                GUI.color = c; GUI.DrawTexture(new Rect(q.x - s / 2, q.y - s / 2, s, s), Texture2D.whiteTexture);
                if (label != null) { GUI.color = Color.white; GUI.Label(new Rect(q.x + s, q.y - 10, 180, 20), label, markerStyle); }
            }
            GUI.color = Color.white;

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Zoom in", GUILayout.Height(26), GUILayout.Width(90))) mapView = Zoomed(mapView, 0.6f, world);
            if (GUILayout.Button("Zoom out", GUILayout.Height(26), GUILayout.Width(90))) mapView = Zoomed(mapView, 1.6f, world);
            if (GUILayout.Button("Centre on me", GUILayout.Height(26), GUILayout.Width(120))) mapView = new Rect(me.x - mapView.width / 2f, me.z - mapView.height / 2f, mapView.width, mapView.height);
            if (GUILayout.Button("Whole map", GUILayout.Height(26), GUILayout.Width(100))) mapView = world;
            GUILayout.Label("  <color=#ff5a40>▲</color> you   <color=#5ab4ff>▲</color> friends   <color=#ffe050>●</color> customer waiting   <color=#ff8020>●</color> job taken   <color=#ffd060>■</color> your claim", hudSmall);
            GUILayout.EndHorizontal();

            if (towing != null)
            {
                if (towing == null || towing.Occupied) { towing = null; return; }
                Dot(towing.transform.position, 12, new Color(1f, 0.6f, 0.1f), towing.displayName);
                if (towDest.HasValue) { Dot(towDest.Value, 12, new Color(1f, 0.9f, 0.2f), "TOW HERE"); GUI.color = Color.white; }
                GUILayout.Space(6);
                GUILayout.Label($"Tow {towing.displayName}", hudBig);
                if (!towDest.HasValue) GUILayout.Label("Click on the map where the tow truck should drop it off. You ride along.", hudSmall);
                else
                {
                    float cost = towing.TowCost(towDest.Value);
                    float dist = Vector3.Distance(towing.transform.position, towDest.Value);
                    GUILayout.Label($"{dist:0} m away  —  tow costs ${cost:N0}" + (Economy.Instance != null && Economy.Instance.Unlimited ? " (free in test mode)" : ""), hudSmall);
                    GUILayout.BeginHorizontal();
                    GUI.enabled = Economy.Instance == null || Economy.Instance.CanAfford(cost);
                    if (GUILayout.Button($"Tow it there (${cost:N0})", GUILayout.Height(32), GUILayout.Width(220)))
                    {
                        var v = towing; var d = towDest.Value;
                        towing = null; towDest = null;
                        ClosePanel();
                        v.TowTo(d, player);
                    }
                    GUI.enabled = true;
                    if (GUILayout.Button("Cancel", GUILayout.Height(32), GUILayout.Width(100))) { towing = null; towDest = null; ClosePanel(); }
                    GUILayout.EndHorizontal();
                }
                return;
            }

            GUILayout.Space(6);
            GUILayout.Label("Travel", hudBig);
            bool onFoot = ActiveVehicle == null && player != null && player.isActiveAndEnabled;
            GUI.enabled = onFoot;
            int col = 0;
            GUILayout.BeginHorizontal();
            foreach (var m in MapMarker.All)
            {
                if (!m.travel) continue;
                if (GUILayout.Button(m.label, GUILayout.Height(28), GUILayout.Width(160)))
                {
                    Vector3 to = Ground(m.transform.position + m.arriveOffset) + Vector3.up * 0.1f;
                    player.PlaceAt(to, Quaternion.LookRotation(m.transform.position - to).eulerAngles.y);
                    ClosePanel();
                    Toast($"Travelled to {m.label}");
                }
                if (++col % 3 == 0) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
            }
            GUILayout.EndHorizontal();
            GUI.enabled = true;
            if (!onFoot) GUILayout.Label("(get out of the machine to travel)", hudSmall);

            GUILayout.Space(8);
            GUILayout.Label("Your machines", hudBig);
            var list = new System.Collections.Generic.List<FPSBase.Vehicles.VehicleBase>(FPSBase.Vehicles.VehicleBase.All);
            list.Sort((a, b) => Vector3.Distance(a.transform.position, me).CompareTo(Vector3.Distance(b.transform.position, me)));
            foreach (var v in list)
            {
                GUILayout.BeginHorizontal();
                bool tipped = v.transform.up.y < 0.75f;
                GUILayout.Label($"{v.displayName}  —  {Vector3.Distance(v.transform.position, me):0} m · fuel {v.FuelFraction * 100f:0}%" + (tipped ? " · TIPPED" : "") + (v.Occupied ? " · (you)" : ""), hudSmall, GUILayout.Width(330));
                GUI.enabled = onFoot && !v.Occupied && !v.Buried && !v.NetRemoteDriven;
                var box = v.GetComponent<BoxCollider>();
                float reach = 4f + (box != null ? box.size.z * 0.6f : 3f);
                Vector3 f = player != null ? Vector3.ProjectOnPlane(player.transform.forward, Vector3.up).normalized : Vector3.forward;
                Vector3 toMe = me + f * reach;
                if (GUILayout.Button($"Tow to me ${v.TowCost(toMe):N0}", GUILayout.Width(150))) v.TowTo(toMe, null);
                if (GUILayout.Button("Tow…", GUILayout.Width(60))) OpenTow(v);
                GUI.enabled = true;
                if (GUILayout.Button("Flip upright", GUILayout.Width(100))) v.Recover();
                GUILayout.EndHorizontal();
            }
        }

        // ------------------------------------------------------------------ help

        void DrawHelp()
        {
            string K(GameAction a) => Controls.N(a);
            void H(string t) { GUILayout.Space(8); GUILayout.Label(t, hudBig); }
            void P(string t) => GUILayout.Label(t, hudSmall);
            H("The gold loop");
            P("1. Strip the topsoil and clay off the claim (gold-coloured stakes) — it carries little gold.");
            P("2. Dig the PAY GRAVEL underneath (richest just above bedrock) and load it in a truck.");
            P("3. Tip it into the wash plant hopper. Clean up the sluice, then sell the gold at the assay office in the Outpost.");
            P($"Use {K(GameAction.Interact)} on buildings, pumps and machines. Esc pauses (save, settings, music and sound volumes, key bindings, unlimited-money test mode).");
            H("Controller");
            P("On foot: left stick walks, right stick looks, A jump, B crouch, L3 sprint, X use, LB / RB pick a menu option. D-pad up: metal detector, RT digs with the hand shovel.");
            P("Driving: RT gas, LT brake / reverse, left stick steers, A handbrake, X get out, R3 cab view, D-pad down: flip / unstick. Trucks: right stick looks around; RB / LB dump box up / down, Y tailgate, D-pad up 4x4, D-pad right diff lock.");
            P("Machines: right stick works the blade / arms (pull back to raise, sideways to tilt or curl / dump); hold L3 to look around with it instead. Dozer: LB / RB swivel the blade, Y / B ripper. Excavator: left stick swings and runs the stick, right stick boom and bucket. RT / LT drive the tracks, and while you hold one the left stick steers them; D-pad left / right spins on the spot. RB / LB front blade. Dozers and loaders steer with the left stick like a truck.");
            P("Menus and shops: left stick moves the pointer, A clicks, right stick scrolls, B goes back, Menu (Start) pauses. View opens the map.");
            H("Ground layers");
            P("Topsoil — top ~1.2 m. Easy digging, almost no gold.");
            P("Clay — sticky band under the topsoil (thicker in places). Digs SLOW, little gold, and clay in the wash plant rolls gold away in clay balls. Upgrade the Clay scrubber, or sell clay at the sand & gravel yard.");
            P("Sand lenses — loose, digs FAST, some gold.");
            P("Pay gravel (inside the claim) / gravel — the gold-bearing layer. Bedrock stops you at the bottom.");
            P("Boulders are buried in the gravel. Dig around one, rip it or shove it hard to break it loose. Small ones can be scooped into a bucket; the wash plant's grizzly throws rocks out.");
            P("The excavator HUD shows what the bucket tip is in. Truck and bucket HUDs show what the load is made of.");
            H("Selling dirt");
            P("The Sand & Gravel Yard pays for what a load is made of (gravel, sand, rock, clay, dirt) — handy for overburden, tailings and rocks. Gold in a load you sell there is lost!");
            H("Metal detector (on foot)");
            P($"{K(GameAction.DetectorToggle)} takes it out. Walk slowly over the dirt field: HIGH tone = gold, middle = coins/relics, low grunt = junk iron. {K(GameAction.DetectorDiscriminate)} ignores iron.");
            P($"Stand where it's loudest and press {K(GameAction.HandDig)} to dig with the shovel until the find pops out. Nuggets go to your gold, coins and relics pay cash. Bigger finds ring deeper.");
            H("Machines");
            P($"{K(GameAction.Phone)} opens your phone: people in the town over the hill (east, follow the road from the heavy yard) text you jobs, a gravel driveway or a trench (how long, deep and wide, and where on their lot), with what they'll pay. Accept one and it's marked out (a light blue box shows how wide and deep to dig), pinned on the map, an orange beacon stands over the house, and the text tells you which way to turn coming into town. Don't hit their house, mailbox, fences or trees: they take a share off the bill for damage. Buy gravel at the landscape supply on the south side of town (back a truck under the bin), dump it on the drive, spread it with a skid steer, then Finish on the phone to get paid.");
            P("Playing with friends, everyone gets the same customers. Accept a job Just me (all the pay is yours) or Share with crew (anyone can do it and finish it, and the pay is split between everyone).");
            P("The minimap in the bottom right follows you: machines are little icons that look like them, and players are arrows pointing where they look. The big map zooms with the scroll wheel and drags around.");
            P($"{K(GameAction.Map)} opens the map: fast travel, see all your machines, have one towed (to you, or anywhere: you ride along) or flip it upright. Look at a machine on foot and scroll to refuel it from a tidy tank, reposition it or call a tow. In a machine, {K(GameAction.Recover)} flips it upright / unsticks it.");
            P("Machines burn diesel — refuel at the Outpost pumps. Buy bigger machines from the dealer (heavy ones are delivered to the heavy-equipment yard).");
            P("Excavator: push the bucket in too hard and the ground lifts the machine — the HUD warns you. Big dozers have rippers for hard ground and boulders.");
        }

        void DrawPanel()
        {
            // the shop look (brown, gold and green) for every panel
            var oldSkin = GUI.skin;
            GUI.skin = FPSBase.UI.ShopTheme.Skin;
            panelStyle ??= new GUIStyle { normal = { background = FPSBase.UI.ShopTheme.TexDeep }, border = new RectOffset(7, 7, 7, 7) };
            bool shop = panelDrawRect != null;
            float w = shop ? Mathf.Min(shopSize.x, Screen.width - 40f) : Mathf.Min(panelWidth, Screen.width - 40f), h = shop ? Mathf.Min(shopSize.y, Screen.height - 40f) : Mathf.Min(660f, Screen.height - 80f);
            var r = new Rect(Mathf.Round((Screen.width - w) / 2f), Mathf.Round((Screen.height - h) / 2f), w, h);
            FPSBase.UI.ShopTheme.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.35f));
            GUI.Box(r, GUIContent.none, panelStyle);
            // title bar: gold title, a gold rule under it, the shop's corner note (money) on the right
            GUI.Label(new Rect(r.x + 20, r.y + 10, w - 40, 40), panelTitle, FPSBase.UI.ShopTheme.Title);
            if (!string.IsNullOrEmpty(PanelCorner)) GUI.Label(new Rect(r.x + 20, r.y + 14, w - 40, 32), PanelCorner, FPSBase.UI.ShopTheme.Money);
            FPSBase.UI.ShopTheme.Fill(new Rect(r.x + 16, r.y + 54, w - 32, 2), FPSBase.UI.ShopTheme.GoldDim);
            var body = new Rect(r.x + 18, r.y + 66, w - 36, h - 66 - 58);
            if (shop) panelDrawRect(body);
            else
            {
                GUILayout.BeginArea(body);
                panelScroll = GUILayout.BeginScrollView(panelScroll);
                // as wide as the window (a long row mustn't widen everything and push it off the side)
                GUILayout.BeginVertical(GUILayout.Width(body.width - 22f));
                panelDraw?.Invoke();
                GUILayout.EndVertical();
                GUILayout.EndScrollView();
                GUILayout.EndArea();
            }
            if (GUI.Button(new Rect(r.xMax - 200, r.yMax - 48, 182, 36), Controls.HasPad ? "Close  (Esc / B)" : "Close  (Esc)", FPSBase.UI.ShopTheme.PlainButton)) ClosePanel();
            GUI.skin = oldSkin;
        }

        /// <summary>Waiting for the player to press the new key for an action (Esc cancels).</summary>
        void CaptureKey(Keyboard kb)
        {
            if (kb == null || !capturing.HasValue) return;
            if (kb.escapeKey.wasPressedThisFrame) { capturing = null; return; }
            foreach (var k in kb.allKeys)
                if (k != null && k.wasPressedThisFrame && k.keyCode != Key.None)
                {
                    Controls.Set(capturing.Value, k.keyCode);
                    capturing = null;
                    return;
                }
        }

        void DrawControlsMenu()
        {
            float w = 680, h = Mathf.Min(780, Screen.height - 40);
            var r = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            GUI.Box(r, GUIContent.none, menuPanel);
            GUILayout.BeginArea(new Rect(r.x + 20, r.y + 14, w - 40, h - 28));
            GUILayout.Label("Controls", header);
            GUILayout.Label("Click a key, then press the new key (Esc cancels). Keys in different groups can share a key — e.g. W drives and walks. Orange = clash with another action used at the same time.", hudSmall);
            GUILayout.Space(6);
            controlsScroll = GUILayout.BeginScrollView(controlsScroll);
            string lastCat = null;
            var warn = new GUIStyle(hudSmall) { fontSize = 12 };
            warn.normal.textColor = new Color(1f, 0.6f, 0.15f);
            foreach (var b in Controls.Defaults)
            {
                if (b.category != lastCat)
                {
                    GUILayout.Space(lastCat == null ? 0 : 10);
                    GUILayout.Label(b.category, hudBig);
                    lastCat = b.category;
                }
                GUILayout.BeginHorizontal();
                GUILayout.Label(b.label, hudSmall, GUILayout.Width(300));
                bool waiting = capturing == b.action;
                string keyText = waiting ? "Press a key…" : Controls.N(b.action);
                if (GUILayout.Button(keyText, GUILayout.Width(150), GUILayout.Height(26))) capturing = b.action;
                string clash = Controls.Conflicts(b.action);
                GUILayout.Label(clash ?? (Controls.Get(b.action) != b.key ? $"(default {Controls.KeyName(b.key)})" : ""), clash != null ? warn : hudSmall);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset all to defaults", GUILayout.Height(32))) { Controls.ResetAll(); capturing = null; }
            if (GUILayout.Button("Back", GUILayout.Height(32))) { controlsOpen = false; capturing = null; }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        int pauseTab;
        Vector2 pauseScroll;
        static readonly string[] PauseTabs = { "Game", "Audio", "Graphics", "Controls" };
        float fpsSmooth, applyTimer;

        void DrawPauseMenu()
        {
            float w = 620, h = Mathf.Min(720, Screen.height - 40);
            var r = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            FPSBase.UI.ShopTheme.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.35f));
            GUI.Box(r, GUIContent.none, menuPanel);
            GUILayout.BeginArea(new Rect(r.x + 20, r.y + 14, w - 40, h - 28));
            GUILayout.Label("Paused", header);
            DrawSaveRow();
            GUILayout.Space(6);
            int tab = GUILayout.Toolbar(pauseTab, PauseTabs, FPSBase.UI.ShopTheme.Toolbar, GUILayout.Height(34));
            if (tab != pauseTab) { pauseTab = tab; pauseScroll = Vector2.zero; }
            GUILayout.Space(10);
            pauseScroll = GUILayout.BeginScrollView(pauseScroll);
            switch (pauseTab)
            {
                case 0: GameTab(); break;
                case 1: AudioTab(); break;
                case 2: GraphicsTab(); break;
                default: ControlsTab(); break;
            }
            GUILayout.EndScrollView();
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Resume  (Esc)", GUILayout.Height(36))) paused = false;
            if (GUILayout.Button($"Multiplayer ({Controls.N(GameAction.Invite)})", GUILayout.Height(36), GUILayout.Width(170))) { paused = false; OpenMultiplayer(); }
            QuitToMenuButton();
            if (GUILayout.Button("Quit Game", GUILayout.Height(36), GUILayout.Width(120))) QuitGame();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        float Slider(string label, float value, float min, float max, string format)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, hudSmall, GUILayout.Width(200));
            float v = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(250));
            GUILayout.Space(10);
            GUILayout.Label(string.Format(format, v), hudSmall, GUILayout.Width(90));
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
            return v;
        }

        bool Toggle(string label, bool value) => GUILayout.Toggle(value, "  " + label);

        void GameTab()
        {
            GUILayout.Label("Camera", hudBig);
            bool tp = Toggle($"Third-person camera on foot  (toggle any time with {Controls.N(GameAction.CabView)})", GameSettings.ThirdPerson);
            if (tp != GameSettings.ThirdPerson) GameSettings.ThirdPerson = tp;
            float d = Slider("Third-person distance", GameSettings.ThirdPersonDistance, 1.5f, 8f, "{0:0.0} m");
            if (Mathf.Abs(d - GameSettings.ThirdPersonDistance) > 0.01f) GameSettings.ThirdPersonDistance = d;

            GUILayout.Space(8);
            GUILayout.Label("Mouse", hudBig);
            float sens = Slider("Sensitivity", GameSettings.MouseSensitivity, 0.2f, 6f, "{0:0.00}");
            if (Mathf.Abs(sens - GameSettings.MouseSensitivity) > 0.001f) GameSettings.MouseSensitivity = Mathf.Round(sens * 20f) / 20f;
            bool inv = Toggle("Invert mouse Y", GameSettings.InvertY);
            if (inv != GameSettings.InvertY) GameSettings.InvertY = inv;
            bool wj = Toggle("Mouse wheel jumps (CS-style bunny hop bind)", GameSettings.WheelJump);
            if (wj != GameSettings.WheelJump) GameSettings.WheelJump = wj;
            bool auto = Toggle($"Auto bunny hop (hold {Controls.N(GameAction.Jump)} to keep hopping)", GameSettings.AutoBhop);
            if (auto != GameSettings.AutoBhop) GameSettings.AutoBhop = auto;

            GUILayout.Space(8);
            GUILayout.Label("Excavator controls", hudBig);
            int mode = (int)GameSettings.ExcavatorControls;
            int newMode = GUILayout.Toolbar(mode, new[] { "Realistic (ISO joysticks)", "Simplified (mouse)" }, FPSBase.UI.ShopTheme.Toolbar, GUILayout.Height(30));
            if (newMode != mode) GameSettings.ExcavatorControls = (ExcavatorControlMode)newMode;
            GUILayout.Label(newMode == 0
                ? "ISO joysticks on the keyboard: stick, swing, boom, bucket and tracks each have their own keys (see Controls)."
                : "Drive with the Driving keys.  Mouse X swing, mouse Y boom, scroll wheel stick.  Hold left click to curl/dig, right click to dump.", hudSmall);

            GUILayout.Space(8);
            GUILayout.Label("Testing", hudBig);
            bool unl = Toggle("Unlimited money (everything is free)", GameSettings.UnlimitedMoney);
            if (unl != GameSettings.UnlimitedMoney) GameSettings.UnlimitedMoney = unl;
        }

        void AudioTab()
        {
            GUILayout.Label("Volume", hudBig);
            float m = Slider("Master", GameSettings.MasterVolume, 0f, 1f, "{0:P0}");
            if (Mathf.Abs(m - GameSettings.MasterVolume) > 0.001f) GameSettings.MasterVolume = m;
            float e = Slider("Engines", GameSettings.EngineVolume, 0f, 1f, "{0:P0}");
            if (Mathf.Abs(e - GameSettings.EngineVolume) > 0.001f) GameSettings.EngineVolume = e;
            float s = Slider("Footsteps", GameSettings.FootstepVolume, 0f, 1f, "{0:P0}");
            if (Mathf.Abs(s - GameSettings.FootstepVolume) > 0.001f) GameSettings.FootstepVolume = s;
            float dt = Slider("Metal detector", GameSettings.DetectorVolume, 0f, 1f, "{0:P0}");
            if (Mathf.Abs(dt - GameSettings.DetectorVolume) > 0.001f) GameSettings.DetectorVolume = dt;
            float fx = Slider("Other effects", GameSettings.EffectsVolume, 0f, 1f, "{0:P0}");
            if (Mathf.Abs(fx - GameSettings.EffectsVolume) > 0.001f) GameSettings.EffectsVolume = fx;
            GUILayout.Space(8);
            GUILayout.Label("Music", hudBig);
            float mu = Slider("Music", GameSettings.MusicVolume, 0f, 1f, "{0:P0}");
            if (Mathf.Abs(mu - GameSettings.MusicVolume) > 0.001f) GameSettings.MusicVolume = mu;
            bool mig = Toggle("Music while playing (off: only in the menus)", GameSettings.MusicInGame);
            if (mig != GameSettings.MusicInGame) GameSettings.MusicInGame = mig;
            GUILayout.BeginHorizontal();
            GUILayout.Label(MusicPlayer.NowPlaying != null ? $"Now playing: {MusicPlayer.NowPlaying}" : "No music playing", hudSmall, GUILayout.Width(330));
            if (GUILayout.Button("Next song", FPSBase.UI.ShopTheme.PlainButton, GUILayout.Width(120), GUILayout.Height(26))) MusicPlayer.Next();
            GUILayout.EndHorizontal();
            GUILayout.Label("Original lofi tracks made for the game.", hudSmall);
            GUILayout.Space(6);
            GUILayout.Label("Volumes change straight away and are saved.", hudSmall);
        }

        void GraphicsTab()
        {
            bool changed = false;
            GUILayout.Label("Display", hudBig);
            if (!Application.isEditor)
            {
                var res = GraphicsApply.Resolutions();
                int cur = res.FindIndex(v => v.x == Screen.width && v.y == Screen.height);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Resolution", hudSmall, GUILayout.Width(200));
                if (GUILayout.Button("<", GUILayout.Width(36))) { cur = Mathf.Clamp(cur + 1, 0, res.Count - 1); GameSettings.ResolutionW = res[cur].x; GameSettings.ResolutionH = res[cur].y; changed = true; }
                GUILayout.Label($"{Screen.width} x {Screen.height}", prompt, GUILayout.Width(170));
                if (GUILayout.Button(">", GUILayout.Width(36))) { cur = Mathf.Clamp(cur - 1, 0, res.Count - 1); GameSettings.ResolutionW = res[cur].x; GameSettings.ResolutionH = res[cur].y; changed = true; }
                GUILayout.EndHorizontal();
                GUILayout.Space(4);
                string[] modes = { "Fullscreen", "Borderless", "Windowed" };
                FullScreenMode[] modeVals = { FullScreenMode.ExclusiveFullScreen, FullScreenMode.FullScreenWindow, FullScreenMode.Windowed };
                int wm = System.Array.IndexOf(modeVals, (FullScreenMode)GameSettings.WindowMode);
                int nwm = GUILayout.Toolbar(Mathf.Max(0, wm), modes, FPSBase.UI.ShopTheme.Toolbar, GUILayout.Height(28));
                if (nwm != wm) { GameSettings.WindowMode = (int)modeVals[nwm]; changed = true; }
            }
            else GUILayout.Label("(Resolution and window mode apply in the built game.)", hudSmall);
            GUILayout.Space(4);
            bool vs = Toggle("V-Sync", GameSettings.VSync);
            if (vs != GameSettings.VSync) { GameSettings.VSync = vs; changed = true; }
            GUILayout.BeginHorizontal();
            GUILayout.Label("Frame rate limit", hudSmall, GUILayout.Width(200));
            var fps = GraphicsApply.FpsChoices;
            int fi = Mathf.Max(0, System.Array.IndexOf(fps, GameSettings.FpsLimit));
            string[] fpsNames = System.Array.ConvertAll(fps, f => f == 0 ? "None" : f.ToString());
            int nfi = GUILayout.Toolbar(fi, fpsNames, FPSBase.UI.ShopTheme.Toolbar, GUILayout.Height(26));
            if (nfi != fi) { GameSettings.FpsLimit = fps[nfi]; changed = true; }
            GUILayout.EndHorizontal();
            bool showTests = Toggle("Show the \"what to test\" list (left of the screen)", GameSettings.ShowTestList);
            if (showTests != GameSettings.ShowTestList) GameSettings.ShowTestList = showTests;
            bool showFps = Toggle("Show FPS counter", GameSettings.ShowFps);
            if (showFps != GameSettings.ShowFps) GameSettings.ShowFps = showFps;

            GUILayout.Space(8);
            GUILayout.Label("Quality", hudBig);
            var names = QualitySettings.names;
            if (names.Length > 1)
            {
                int q = QualitySettings.GetQualityLevel();
                int nq = GUILayout.Toolbar(q, names, FPSBase.UI.ShopTheme.Toolbar, GUILayout.Height(28));
                if (nq != q) { GameSettings.Quality = nq; changed = true; }
            }
            float fov = Slider("Field of view", GameSettings.Fov, 55f, 100f, "{0:0}°");
            if (Mathf.Abs(fov - GameSettings.Fov) > 0.1f) { GameSettings.Fov = Mathf.Round(fov); changed = true; }
            float sd = Slider("Shadow distance", GameSettings.ShadowDistance, 40f, 400f, "{0:0} m");
            if (Mathf.Abs(sd - GameSettings.ShadowDistance) > 1f) { GameSettings.ShadowDistance = Mathf.Round(sd / 10f) * 10f; changed = true; }
            float gd = Slider("Grass distance", GameSettings.GrassDistance, 30f, 250f, "{0:0} m");
            if (Mathf.Abs(gd - GameSettings.GrassDistance) > 1f) { GameSettings.GrassDistance = Mathf.Round(gd / 5f) * 5f; changed = true; }
            GUILayout.BeginHorizontal();
            GUILayout.Label("Anti-aliasing", hudSmall, GUILayout.Width(200));
            int aa = GUILayout.Toolbar(GameSettings.AntiAliasing, GraphicsApply.AaNames, FPSBase.UI.ShopTheme.Toolbar, GUILayout.Height(26));
            if (aa != GameSettings.AntiAliasing) { GameSettings.AntiAliasing = aa; changed = true; }
            GUILayout.EndHorizontal();
            bool mb = Toggle("Motion blur", GameSettings.MotionBlur);
            if (mb != GameSettings.MotionBlur) { GameSettings.MotionBlur = mb; changed = true; }
            if (changed) GraphicsApply.All();
        }

        void ControlsTab()
        {
            GUILayout.Label("Key bindings", hudBig);
            GUILayout.Label("Every action can be rebound. Keys in different groups can share a key (W drives and walks).", hudSmall);
            if (GUILayout.Button("Change key bindings…", GUILayout.Height(34))) { controlsOpen = true; controlsScroll = Vector2.zero; }
            GUILayout.Space(8);
            GUILayout.Label($"Camera: {Controls.N(GameAction.CabView)} switches first / third person on foot, and cab view in machines.", hudSmall);
            GUILayout.Label($"Map {Controls.N(GameAction.Map)}  ·  Help {Controls.N(GameAction.Help)}  ·  Flip machine {Controls.N(GameAction.Recover)}  ·  Use / get in {Controls.N(GameAction.Interact)}", hudSmall);
        }
    }
}
