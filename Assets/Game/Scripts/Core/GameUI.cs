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
    public class GameUI : MonoBehaviour
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

        GUIStyle hudBig, hudSmall, prompt, header, toastStyle, box;

        public bool BlocksGameplay => paused || panelDraw != null;

        void Awake() => Instance = this;

        void Start() => GraphicsApply.All();

        public static void Toast(string message)
        {
            if (Instance == null) { Debug.Log(message); return; }
            var t = Instance.toasts;
            t.RemoveAll(x => x.text == message);
            t.Add((message, Time.unscaledTime + 3.5f));
            if (t.Count > 4) t.RemoveAt(0);
        }

        public void OpenPanel(string title, Action draw)
        {
            panelTitle = title;
            panelDraw = draw;
            panelScroll = Vector2.zero;
        }

        public void ClosePanel() { panelDraw = null; towing = null; towDest = null; }

        void OpenMultiplayer()
        {
            OpenPanel("Multiplayer", () =>
            {
                var net = FPSBase.Net.NetSession.Instance;
                if (net != null) net.DrawPanel();
                else GUILayout.Label("Multiplayer isn't available in this session.");
            });
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (capturing.HasValue)
            {
                // waiting for the player to press the new key for an action
                if (kb != null)
                {
                    if (kb.escapeKey.wasPressedThisFrame) capturing = null;
                    else
                        foreach (var k in kb.allKeys)
                            if (k != null && k.wasPressedThisFrame && k.keyCode != Key.None)
                            {
                                Controls.Set(capturing.Value, k.keyCode);
                                capturing = null;
                                break;
                            }
                }
            }
            else if (!paused && Controls.Pressed(GameAction.Map))
            {
                if (panelTitle == "Map" && panelDraw != null) ClosePanel(); else OpenPanel("Map", DrawMap);
            }
            else if (!paused && Controls.Pressed(GameAction.Help))
            {
                if (panelTitle == "Help & Tips" && panelDraw != null) ClosePanel(); else OpenPanel("Help & Tips", DrawHelp);
            }
            else if (!paused && Controls.Pressed(GameAction.Invite))
            {
                if (panelTitle == "Multiplayer" && panelDraw != null) ClosePanel(); else OpenMultiplayer();
            }
            else if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                if (controlsOpen) controlsOpen = false;
                else if (panelDraw != null) ClosePanel();
                else paused = !paused;
            }
            // in a multiplayer game the world keeps going while the menu is open
            Time.timeScale = paused && !FPSBase.Net.NetSession.InSession ? 0f : 1f;
            if ((applyTimer -= Time.unscaledDeltaTime) <= 0f) { applyTimer = 0.5f; GraphicsApply.Cameras(); }
            fpsSmooth = Mathf.Lerp(fpsSmooth, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.05f);
            bool wantLocked = !BlocksGameplay && Application.isFocused;
            Cursor.lockState = wantLocked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !wantLocked;
        }

        void Styles()
        {
            if (hudBig != null) return;
            hudBig = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            hudSmall = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            prompt = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            header = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            toastStyle = new GUIStyle(GUI.skin.box) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
            box = new GUIStyle(GUI.skin.box);
            box.normal.background = MakeTex(new Color(0f, 0f, 0f, 0.6f));
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
            Styles();
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
                var r = new Rect(Screen.width / 2f - 230, Screen.height - 74, 460, 30);
                GUI.Box(r, GUIContent.none, box);
                var fill = new Rect(r.x + 3, r.y + 3, (r.width - 6) * f, r.height - 6);
                GUI.color = f >= 0.98f ? new Color(1f, 0.55f, 0.1f) : new Color(0.35f, 0.8f, 0.3f);
                GUI.DrawTexture(fill, Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(r, ActiveVehicle.HudBarLabel, prompt);
            }

            // Safe zone banner
            var who = ActiveVehicle as Component;
            Vector3 here = who != null ? who.transform.position : (player != null ? player.transform.position : Vector3.zero);
            var zone = SafeZone.At(here);
            if (!paused && panelDraw == null) DrawCompass(here);
            if (zone != null && !paused)
                GUI.Label(new Rect(Screen.width / 2f - 300, 40, 600, 30), zone.zoneName + "  —  SAFE ZONE", prompt);
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
                GUI.Box(new Rect(Screen.width / 2f - 320, 76 + i * 40, 640, 36), toasts[i].text, toastStyle);
            }
            GUI.color = Color.white;

            if (panelDraw != null) DrawPanel();
            if (paused) { if (controlsOpen) DrawControlsMenu(); else DrawPauseMenu(); }
        }

        // ------------------------------------------------------------------ compass

        void DrawCompass(Vector3 here)
        {
            var cam = Camera.main;
            if (cam == null) foreach (var c in Camera.allCameras) if (c.isActiveAndEnabled) { cam = c; break; }
            if (cam == null) return;
            float yaw = cam.transform.eulerAngles.y;
            const float w = 600f, half = 90f; // degrees shown either side
            var r = new Rect(Screen.width / 2f - w / 2f, 6, w, 30);
            GUI.Box(r, GUIContent.none, box);
            float X(float bearing) => r.center.x + Mathf.DeltaAngle(yaw, bearing) / half * (w / 2f);
            for (int d = 0; d < 360; d += 15)
            {
                float dd = Mathf.DeltaAngle(yaw, d);
                if (Mathf.Abs(dd) > half) continue;
                float x = X(d);
                string lab = d switch { 0 => "N", 90 => "E", 180 => "S", 270 => "W", _ => null };
                if (lab != null) GUI.Label(new Rect(x - 15, r.y + 3, 30, 24), lab, prompt);
                else GUI.DrawTexture(new Rect(x - 0.5f, r.y + (d % 45 == 0 ? 8 : 13), 1, d % 45 == 0 ? 14 : 8), Texture2D.whiteTexture);
            }
            void Mark(Vector3 at, string label, Color col)
            {
                Vector3 d = at - here; d.y = 0f;
                if (d.magnitude < 3f) return;
                float bearing = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                if (Mathf.Abs(Mathf.DeltaAngle(yaw, bearing)) > half) return;
                float x = X(bearing);
                GUI.color = col;
                GUI.DrawTexture(new Rect(x - 4, r.y + r.height - 9, 8, 8), Texture2D.whiteTexture);
                GUI.Label(new Rect(x - 80, r.yMax, 160, 20), $"{label} {d.magnitude:0} m", markerStyle);
                GUI.color = Color.white;
            }
            foreach (var m in MapMarker.All) Mark(m.transform.position, m.label, m.color);
            var last = FPSBase.Vehicles.VehicleBase.LastDriven;
            if (last != null && !ReferenceEquals(last, ActiveVehicle)) Mark(last.transform.position, last.displayName, new Color(0.55f, 0.85f, 1f));
        }

        GUIStyle markerStyle;

        // ------------------------------------------------------------------ look-at prompt / menu

        void DrawLookPrompt()
        {
            var opts = player.Options;
            string key = Controls.N(GameAction.Interact);
            float cx = Screen.width / 2f, y = Screen.height / 2f + 30;
            if (opts.Count == 0)
            {
                GUI.Label(new Rect(cx - 300, y, 600, 30), $"[{key}] " + player.Current.Prompt, prompt);
                return;
            }
            GUI.Label(new Rect(cx - 300, y, 600, 26), player.Current.Prompt, prompt);
            y += 28;
            float w = 420f, h = 26f;
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(new Rect(cx - w / 2f - 6, y - 3, w + 12, opts.Count * h + 6), Texture2D.whiteTexture);
            for (int i = 0; i < opts.Count; i++)
            {
                var o = opts[i];
                bool sel = i == player.Selected;
                if (sel) { GUI.color = new Color(1f, 0.8f, 0.3f, 0.35f); GUI.DrawTexture(new Rect(cx - w / 2f - 4, y + i * h, w + 8, h - 2), Texture2D.whiteTexture); }
                GUI.color = !o.enabled ? new Color(1f, 1f, 1f, 0.45f) : sel ? new Color(1f, 0.92f, 0.6f) : Color.white;
                string text = (sel ? $"[{key}]  " : "      ") + o.label + (!o.enabled && !string.IsNullOrEmpty(o.note) ? $"   ({o.note})" : "");
                GUI.Label(new Rect(cx - w / 2f, y + i * h, w, h), text, sel ? prompt : hudSmall);
            }
            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            if (opts.Count > 1) GUI.Label(new Rect(cx - 300, y + opts.Count * h + 2, 600, 22), "scroll to choose", hudSmall);
            GUI.color = Color.white;
        }

        // ------------------------------------------------------------------ towing (paid fast travel for a machine)

        FPSBase.Vehicles.VehicleBase towing;
        Vector3? towDest;

        /// <summary>Open the map to pick where a tow truck should take `v` (you ride along).</summary>
        public void OpenTow(FPSBase.Vehicles.VehicleBase v)
        {
            towing = v; towDest = null;
            OpenPanel("Map", DrawMap);
        }

        // ------------------------------------------------------------------ map

        static Vector3 Ground(Vector3 p) => FPSBase.Mining.VehicleDealer.GroundAt(p);

        void DrawMap()
        {
            var site = FPSBase.Mining.DigSite.Instance;
            var who = ActiveVehicle as Component;
            Vector3 me = who != null ? who.transform.position : (player != null ? player.transform.position : Vector3.zero);
            Rect world = site != null ? site.FieldRect : new Rect(-50, -200, 300, 320);
            foreach (var m in MapMarker.All)
            {
                Vector3 q = m.transform.position;
                world = Rect.MinMaxRect(Mathf.Min(world.xMin, q.x - 20), Mathf.Min(world.yMin, q.z - 20), Mathf.Max(world.xMax, q.x + 20), Mathf.Max(world.yMax, q.z + 30));
            }
            float size = Mathf.Max(world.width, world.height);
            world = new Rect(world.center.x - size / 2f, world.center.y - size / 2f, size, size);

            GUILayout.Label("Click Travel to walk there instantly (on foot only). A tow truck brings an empty machine to you, or takes it anywhere (it costs money).", hudSmall);
            Rect r = GUILayoutUtility.GetRect(500, 470, GUILayout.ExpandWidth(true));
            float px = Mathf.Min(r.width, r.height);
            r = new Rect(r.x + (r.width - px) / 2f, r.y, px, px);
            Vector2 M(Vector3 p) => new Vector2(r.x + (p.x - world.x) / world.width * r.width, r.yMax - (p.z - world.y) / world.height * r.height);
            void Fill(Rect wr, Color c)
            {
                Vector2 a = M(new Vector3(wr.xMin, 0, wr.yMax)), b = M(new Vector3(wr.xMax, 0, wr.yMin));
                GUI.color = c; GUI.DrawTexture(Rect.MinMaxRect(a.x, a.y, b.x, b.y), Texture2D.whiteTexture);
            }
            Fill(world, new Color(0.22f, 0.36f, 0.16f));
            if (site != null)
            {
                Fill(site.FieldRect, new Color(0.34f, 0.42f, 0.2f));
                var c = site.claimRect;
                Fill(c, new Color(0.55f, 0.4f, 0.2f));
                const float e = 1.2f;
                var gold = new Color(1f, 0.8f, 0.3f);
                Fill(new Rect(c.xMin, c.yMin, c.width, e * 2), gold); Fill(new Rect(c.xMin, c.yMax - e * 2, c.width, e * 2), gold);
                Fill(new Rect(c.xMin, c.yMin, e * 2, c.height), gold); Fill(new Rect(c.xMax - e * 2, c.yMin, e * 2, c.height), gold);
            }
            void Dot(Vector3 p, float s, Color c, string label)
            {
                Vector2 q = M(p);
                if (!r.Contains(q)) return;
                GUI.color = Color.black; GUI.DrawTexture(new Rect(q.x - s / 2 - 1, q.y - s / 2 - 1, s + 2, s + 2), Texture2D.whiteTexture);
                GUI.color = c; GUI.DrawTexture(new Rect(q.x - s / 2, q.y - s / 2, s, s), Texture2D.whiteTexture);
                if (label != null) { GUI.color = Color.white; GUI.Label(new Rect(q.x + s, q.y - 10, 180, 20), label, markerStyle); }
            }
            foreach (var v in FPSBase.Vehicles.VehicleBase.All) Dot(v.transform.position, 6, new Color(0.55f, 0.85f, 1f), ReferenceEquals(v, FPSBase.Vehicles.VehicleBase.LastDriven) ? v.displayName : null);
            foreach (var m in MapMarker.All) Dot(m.transform.position, 10, m.color, m.label);
            Dot(me, 12, new Color(1f, 0.25f, 0.2f), "YOU");
            GUI.color = Color.white;

            if (towing != null)
            {
                if (towing == null || towing.Occupied) { towing = null; return; }
                Dot(towing.transform.position, 12, new Color(1f, 0.6f, 0.1f), towing.displayName);
                var ev = Event.current;
                if (ev.type == EventType.MouseDown && ev.button == 0 && r.Contains(ev.mousePosition))
                {
                    Vector2 q = ev.mousePosition;
                    var w = new Vector3(world.x + (q.x - r.x) / r.width * world.width, 0f, world.y + (r.yMax - q.y) / r.height * world.height);
                    towDest = Ground(w);
                    ev.Use();
                }
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
            P($"Use {K(GameAction.Interact)} on buildings, pumps and machines. Esc pauses (settings, key bindings, unlimited-money test mode).");
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
            P($"{K(GameAction.Map)} opens the map: fast travel, see all your machines, have one towed (to you, or anywhere: you ride along) or flip it upright. Look at a machine on foot and scroll to refuel it from a tidy tank, reposition it or call a tow. In a machine, {K(GameAction.Recover)} flips it upright / unsticks it.");
            P("Machines burn diesel — refuel at the Outpost pumps. Buy bigger machines from the dealer (heavy ones are delivered to the heavy-equipment yard).");
            P("Excavator: push the bucket in too hard and the ground lifts the machine — the HUD warns you. Big dozers have rippers for hard ground and boulders.");
        }

        void DrawPanel()
        {
            float w = 560, h = Mathf.Min(620, Screen.height - 80);
            var r = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            GUI.Box(r, GUIContent.none, box);
            GUI.Box(r, GUIContent.none, box);
            GUILayout.BeginArea(new Rect(r.x + 16, r.y + 12, w - 32, h - 24));
            GUILayout.Label(panelTitle, header);
            GUILayout.Space(8);
            panelScroll = GUILayout.BeginScrollView(panelScroll);
            panelDraw?.Invoke();
            GUILayout.EndScrollView();
            GUILayout.Space(6);
            if (GUILayout.Button("Close (Esc)", GUILayout.Height(32))) ClosePanel();
            GUILayout.EndArea();
        }

        void DrawControlsMenu()
        {
            float w = 680, h = Mathf.Min(780, Screen.height - 40);
            var r = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            GUI.Box(r, GUIContent.none, box);
            GUI.Box(r, GUIContent.none, box);
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
            GUI.Box(r, GUIContent.none, box);
            GUI.Box(r, GUIContent.none, box);
            GUILayout.BeginArea(new Rect(r.x + 20, r.y + 14, w - 40, h - 28));
            GUILayout.Label("Paused", header);
            GUILayout.Space(6);
            int tab = GUILayout.Toolbar(pauseTab, PauseTabs, GUILayout.Height(34));
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
            if (GUILayout.Button("Quit Game", GUILayout.Height(36), GUILayout.Width(150)))
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
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
            int newMode = GUILayout.Toolbar(mode, new[] { "Realistic (ISO joysticks)", "Simplified (mouse)" }, GUILayout.Height(30));
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
                int nwm = GUILayout.Toolbar(Mathf.Max(0, wm), modes, GUILayout.Height(28));
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
            int nfi = GUILayout.Toolbar(fi, fpsNames, GUILayout.Height(26));
            if (nfi != fi) { GameSettings.FpsLimit = fps[nfi]; changed = true; }
            GUILayout.EndHorizontal();
            bool showFps = Toggle("Show FPS counter", GameSettings.ShowFps);
            if (showFps != GameSettings.ShowFps) GameSettings.ShowFps = showFps;

            GUILayout.Space(8);
            GUILayout.Label("Quality", hudBig);
            var names = QualitySettings.names;
            if (names.Length > 1)
            {
                int q = QualitySettings.GetQualityLevel();
                int nq = GUILayout.Toolbar(q, names, GUILayout.Height(28));
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
            int aa = GUILayout.Toolbar(GameSettings.AntiAliasing, GraphicsApply.AaNames, GUILayout.Height(26));
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
