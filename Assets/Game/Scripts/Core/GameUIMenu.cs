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
        enum MenuScreen { Main, Single, Multi, Blog, Options }
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

        GUIStyle menuTitle, menuSub, menuButton, menuButton2, menuRich;

        static string pendingMenuMessage;

        /// <summary>Back to the main menu with a fresh world (left a friend's game, or it closed). Unsaved progress is lost.</summary>
        public void ReturnToMainMenu(string message)
        {
            pendingMenuMessage = message;
            SaveGame.CurrentName = null;
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        void MenuAwake()
        {
            FPSBase.UI.MenuScene.Allow();
            menuOpen = !Application.isBatchMode || FPSBase.Net.NetSession.LanMode; // the automated tests start straight in the world
            screen = MenuScreen.Main;
            if (pendingMenuMessage != null) { menuMessage = pendingMenuMessage; pendingMenuMessage = null; screen = MenuScreen.Multi; }
            // dev: "-menushot <folder>" saves screenshots of the menus and the game, then quits
            var args = System.Environment.GetCommandLineArgs();
            int a = System.Array.IndexOf(args, "-menushot");
            if (a >= 0 && a + 1 < args.Length) StartCoroutine(MenuShots(args[a + 1]));
            // dev: "-vehshot <folder> <name part>,<name part>,..." photographs those dealer machines close up, then quits
            int vs = System.Array.IndexOf(args, "-vehshot");
            if (vs >= 0 && vs + 2 < args.Length) { menuOpen = false; StartCoroutine(VehicleShots(args[vs + 1], args[vs + 2].Split(','))); }
        }

        System.Collections.IEnumerator VehicleShots(string folder, string[] names)
        {
            System.IO.Directory.CreateDirectory(folder);
            for (int i = 0; i < 120; i++) yield return null;
            var dealer = FindFirstObjectByType<FPSBase.Mining.VehicleDealer>();
            if (dealer == null) { Application.Quit(); yield break; }
            var camGo = new GameObject("Shot Camera");
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
            cam.depth = 100f; cam.fieldOfView = 45f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 2000f;
            Vector3 at = dealer.heavyLotSpots != null && dealer.heavyLotSpots.Length > 0 ? dealer.heavyLotSpots[0].position : Vector3.zero;
            var site = FPSBase.Mining.DigSite.At(at);
            foreach (var part in names)
            {
                int li = System.Array.FindIndex(dealer.listings, l => l.name.Contains(part));
                if (li < 0 || dealer.listings[li].prefab == null) { Debug.Log($"[VehShot] no listing {part}"); continue; }
                float y = site != null && site.Contains(at) ? site.SurfaceY(at) : at.y;
                var go = Instantiate(dealer.listings[li].prefab, new Vector3(at.x, y + 0.05f, at.z), Quaternion.identity);
                var rb = go.GetComponent<Rigidbody>(); if (rb != null) rb.isKinematic = true;
                for (int i = 0; i < 10; i++) yield return null;
                var b = new Bounds(go.transform.position, Vector3.one);
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                    if (r.bounds.size.magnitude < 40f && (r.bounds.center - go.transform.position).magnitude < 25f) b.Encapsulate(r.bounds);
                    else Debug.Log($"[VehShot] {part}: skipped {r.name} {r.bounds}");
                float d = b.extents.magnitude * 2.4f;
                string safe = part.Replace(' ', '_');
                Vector3[] views = { new Vector3(-0.7f, 0.35f, 0.75f), new Vector3(0.75f, 0.3f, -0.7f), new Vector3(-0.25f, 0.12f, 0.35f) };
                for (int k = 0; k < views.Length; k++)
                {
                    Vector3 dir = views[k].normalized;
                    float dist = k == 2 ? d * 0.45f : d;
                    cam.transform.position = b.center + dir * dist;
                    cam.transform.LookAt(b.center + (k == 2 ? Vector3.up * b.extents.y * 0.1f : Vector3.zero));
                    for (int i = 0; i < 40; i++) yield return null;
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(folder, $"veh_{safe}_{k}.png"));
                    for (int i = 0; i < 10; i++) yield return null;
                }
                Destroy(go);
                yield return null;
            }
            Application.Quit();
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
            // the machines at work behind the menu, without the menu, a few seconds apart
            menuBare = true;
            for (int k = 0; k < 6; k++) { yield return Shot($"menu_scene{k}.png"); float until = Time.unscaledTime + 7f; while (Time.unscaledTime < until) yield return null; }
            // close by the excavator's trench, a few seconds apart (the dirt it digs and leaves)
            FPSBase.UI.MenuScene.CloseUp = true;
            for (int k = 0; k < 6; k++) { yield return Shot($"menu_dig{k}.png"); float until = Time.unscaledTime + 4f; while (Time.unscaledTime < until) yield return null; }
            // the excavator's lifting chain let down, hook and all (drawn by the real player's renderer, not the editor's)
            int chains = 0;
            foreach (var ch in FPSBase.Vehicles.BucketChain.All) if (ch != null && !ch.Out) { ch.Toggle(); chains++; }
            for (int k = 0; k < 2; k++) { yield return Shot($"menu_chain{k}.png"); float until = Time.unscaledTime + 2f; while (Time.unscaledTime < until) yield return null; }
            float paid = 0f; foreach (var ch in FPSBase.Vehicles.BucketChain.All) if (ch != null) paid = Mathf.Max(paid, ch.PaidOut);
            Debug.Log($"[MenuShot] chain let down on {chains} excavator(s), {paid:0.00} m paid out, drawn with its hook");
            foreach (var ch in FPSBase.Vehicles.BucketChain.All) if (ch != null && ch.Out && ch.Held == null) ch.Toggle();
            FPSBase.UI.MenuScene.CloseUp = false;
            menuBare = false;
            yield return Shot("menu_main.png");
            Debug.Log($"[Music] now playing: {MusicPlayer.NowPlaying ?? "(nothing)"}");
            screen = MenuScreen.Options; pauseTab = 1; yield return Shot("menu_options_audio.png");
            pauseTab = 2; yield return Shot("menu_options_graphics.png");
            pauseTab = 3; controlsOpen = true; yield return Shot("menu_options_keys.png");
            controlsOpen = false; pauseTab = 0;
            screen = MenuScreen.Blog; yield return Shot("menu_blog.png");
            screen = MenuScreen.Single; RefreshSaves(SaveGame.Mode.SinglePlayer); yield return Shot("menu_single.png");
            screen = MenuScreen.Multi; RefreshSaves(SaveGame.Mode.Multiplayer); yield return Shot("menu_multi.png");
            CloseMenu(); MapView.Instance?.BakeAll(); yield return Shot("game_hud.png");
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
                // a dozer pushing dirt: the cut rolls in front of the blade as clods
                int medL = System.Array.FindIndex(dealer.listings, l => l.name.Contains("Medium Dozer"));
                if (medL >= 0 && player != null)
                {
                    dealer.DevDeliver(medL);
                    FPSBase.Vehicles.Bulldozer dz = null;
                    foreach (var v in FPSBase.Vehicles.VehicleBase.All) if (v is FPSBase.Vehicles.Bulldozer b && b.name.Contains("Medium Dozer")) dz = b;
                    if (dz != null)
                    {
                        var site = FPSBase.Mining.DigSite.Instance;
                        Vector3 st = new Vector3(60f, 0f, -80f); st.y = site.SurfaceY(st) + 0.6f;
                        var rb = dz.GetComponent<Rigidbody>();
                        rb.isKinematic = true; dz.transform.SetPositionAndRotation(st, Quaternion.identity); rb.position = st; rb.rotation = Quaternion.identity; rb.isKinematic = false;
                        Physics.SyncTransforms();
                        for (int i = 0; i < 60; i++) yield return null;
                        dz.Interact(player);
                        for (int i = 0; i < 10; i++) yield return null;
                        dz.SetBlade(-0.18f);
                        Controls.TestHeld[GameAction.DriveForward] = 0.7f;
                        float ground0 = site.OriginalY(st);
                        for (int s = 0; s < 6; s++)
                        {
                            FPSBase.Mining.DigSite.RebuildMs = FPSBase.Mining.DigSite.RelaxMs = FPSBase.Mining.DigSite.PushMs = FPSBase.Mining.DigSite.UploadMs = FPSBase.Mining.DigSite.DecorMs = FPSBase.Mining.DigSite.ColliderMs = 0; FPSBase.Mining.DigSite.ColliderSamples = FPSBase.Mining.DigSite.ColliderCalls = 0; FPSBase.Mining.DigSite.Rebuilds = 0;
                            double g0 = FPSBase.Mining.DirtGrains.StepMs, gp0 = FPSBase.Mining.DirtGrains.PushersMs, gc0 = FPSBase.Mining.DirtGrains.CollideMs, gd0 = FPSBase.Mining.DirtGrains.DrawMs;
                            int fr = 0; float u = Time.unscaledTime + 1f; while (Time.unscaledTime < u) { yield return null; fr++; }
                            var ft = new FrameTiming[1]; FrameTimingManager.CaptureFrameTimings(); uint nft = FrameTimingManager.GetLatestTimings(1, ft);
                            if (nft > 0) Debug.Log($"[Perf] last frame: main thread {ft[0].cpuMainThreadFrameTime:0.0} ms, render thread {ft[0].cpuRenderThreadFrameTime:0.0} ms, GPU {ft[0].gpuFrameTime:0.0} ms");
                            Debug.Log($"[Perf] pushing: {fr} fps; per frame: ground pieces {FPSBase.Mining.DigSite.RebuildMs / fr:0.0} ms (upload {FPSBase.Mining.DigSite.UploadMs / fr:0.0}, grass+clods {FPSBase.Mining.DigSite.DecorMs / fr:0.0}, {FPSBase.Mining.DigSite.Rebuilds / (float)fr:0.0} pieces), slumping {FPSBase.Mining.DigSite.RelaxMs / fr:0.0} ms, collider {FPSBase.Mining.DigSite.ColliderMs / fr:0.0} ms ({FPSBase.Mining.DigSite.ColliderCalls} pushes of {FPSBase.Mining.DigSite.ColliderSamples / System.Math.Max(1, FPSBase.Mining.DigSite.ColliderCalls)} points), physics {Time.fixedDeltaTime * 1000f:0} ms steps, blade {FPSBase.Mining.DigSite.PushMs / fr:0.0} ms, clods {(FPSBase.Mining.DirtGrains.StepMs - g0) / fr:0.0} ms (blades {(FPSBase.Mining.DirtGrains.PushersMs - gp0) / fr:0.0}, bumping {(FPSBase.Mining.DirtGrains.CollideMs - gc0) / fr:0.0}, drawing {(FPSBase.Mining.DirtGrains.DrawMs - gd0) / fr:0.0})");
                            Debug.Log($"[Clods] {s + 1} s: pitch {dz.transform.eulerAngles.x:0.0}°, blade edge {dz.bladeEdge.position.y - site.OriginalY(dz.bladeEdge.position):+0.00;-0.00} m vs the ground, heap {dz.HeapVolume:0.0} m³, {FPSBase.Mining.DirtGrains.LivePushed} clods, {dz.transform.position.z - st.z:0.0} m along");
                            if (s == 2) yield return Shot("dozer_push1.png");
                        }
                        float until = Time.unscaledTime;
                        yield return Shot("dozer_push1.png");
                        until = Time.unscaledTime + 3f; while (Time.unscaledTime < until) yield return null;
                        yield return Shot("dozer_push2.png");
                        Controls.TestHeld.Clear();
                        Debug.Log($"[Clods] pushing: {FPSBase.Mining.DirtGrains.LivePushed} clods in front of the blade");
                        dz.SetBlade(0.5f);
                        until = Time.unscaledTime + 4f; while (Time.unscaledTime < until) yield return null;
                        dz.Exit();
                        for (int i = 0; i < 10; i++) yield return null;
                        player.PlaceAt(st + new Vector3(-7f, 0.5f, 10f), 120f);
                        for (int i = 0; i < 30; i++) yield return null;
                        yield return Shot("dozer_push_left.png");
                        // how fast the game runs here, then a ditch dug with a bucket: its walls stand straight up
                        {
                            int frames = 0; float t0 = Time.realtimeSinceStartup, worst = 0f, last = t0;
                            while (Time.realtimeSinceStartup - t0 < 3f) { yield return null; frames++; float now = Time.realtimeSinceStartup; worst = Mathf.Max(worst, now - last); last = now; }
                            Debug.Log($"[Perf] by the dozer: {frames / (Time.realtimeSinceStartup - t0):0} fps, slowest frame {worst * 1000f:0} ms, ground squares {site.Cell * 100f:0} cm");
                            Vector3 d0 = st + new Vector3(-10f, 0f, 2f);
                            for (int pass = 0; pass < 8; pass++)
                                for (float s = 0f; s <= 6f; s += 0.15f)
                                {
                                    Vector3 tip = d0 + new Vector3(0f, 0f, s);
                                    tip.y = site.OriginalY(tip) - 1.2f;
                                    site.Dig(tip, 0.35f, 0.08f);
                                }
                            site.Flush(9999);
                            Vector3 mid = d0 + new Vector3(0f, 0f, 3f);
                            Debug.Log($"[Ditch] depth across: 0 m {site.CutDepth(mid):0.00}, 0.25 m {site.CutDepth(mid + Vector3.right * 0.25f):0.00}, 0.5 m {site.CutDepth(mid + Vector3.right * 0.5f):0.00}, 0.75 m {site.CutDepth(mid + Vector3.right * 0.75f):0.00}");
                            player.PlaceAt(d0 + new Vector3(2.5f, 0.3f, -2.5f), -30f);
                            for (int i = 0; i < 30; i++) yield return null;
                            yield return Shot("ditch.png");
                            float until2 = Time.unscaledTime + 6f; while (Time.unscaledTime < until2) yield return null;
                            Debug.Log($"[Ditch] after 6 s: 0 m {site.CutDepth(mid):0.00}, 0.5 m {site.CutDepth(mid + Vector3.right * 0.5f):0.00}, 0.75 m {site.CutDepth(mid + Vector3.right * 0.75f):0.00}");
                            yield return Shot("ditch_later.png");
                            // a pit dug bucket by bucket in the gold claim, seen from above like a player at the controls
                            Vector3 pc = new Vector3(70f, 0f, -12f);
                            for (int b = 0; b < 160; b++)
                            {
                                Vector3 tip = pc + new Vector3(Random.Range(-1.6f, 1.6f), 0f, Random.Range(-1.6f, 1.6f));
                                tip.y = site.OriginalY(tip) - Random.Range(0.6f, 1.6f);
                                site.Dig(tip, 0.45f, 0.25f);
                                if (b % 8 == 7) site.Deposit(pc + new Vector3(4.5f, 0f, Random.Range(-2f, 2f)), new FPSBase.Mining.MaterialLoad(0.8f, 0f));
                            }
                            player.PlaceAt(pc + new Vector3(-3f, 0.3f, -6f), 20f);
                            for (int i = 0; i < 20; i++) yield return null;
                            float until3 = Time.unscaledTime + 3f; while (Time.unscaledTime < until3) yield return null;
                            var pcam = Camera.main;
                            var looks = new System.Collections.Generic.List<Behaviour>();
                            if (pcam != null) foreach (var lk in pcam.GetComponentsInParent<FPSBase.Player.PlayerLook>()) { if (lk.enabled) { lk.enabled = false; looks.Add(lk); } }
                            Vector3 camWas = pcam != null ? pcam.transform.position : Vector3.zero; Quaternion camRot = pcam != null ? pcam.transform.rotation : Quaternion.identity;
                            for (int i = 0; i < 20; i++) { if (pcam != null) { pcam.transform.position = pc + new Vector3(-2f, 6f, -4.5f); pcam.transform.LookAt(pc + Vector3.down * 0.8f); } yield return null; }
                            Debug.Log($"[Pit] drawn every {site.DrawStepAt(pc)} square(s) of {site.Cell * 100f:0} cm; camera {(pcam != null ? pcam.name : "none")}");
                            yield return Shot("pit_above.png");
                            if (pcam != null) pcam.transform.SetPositionAndRotation(camWas, camRot);
                            foreach (var lk in looks) lk.enabled = true;
                            // in the excavator, digging with the real controls: what the ground looks like from its camera
                            var exv = FindFirstObjectByType<FPSBase.Vehicles.MiniExcavator>();
                            if (exv != null)
                            {
                                var erb = exv.GetComponent<Rigidbody>();
                                Vector3 ep = new Vector3(56f, 0f, -98f); ep.y = site.SurfaceY(ep) + 0.4f;
                                erb.isKinematic = true; exv.transform.SetPositionAndRotation(ep, Quaternion.identity); erb.position = ep; erb.rotation = Quaternion.identity; erb.isKinematic = false;
                                Physics.SyncTransforms();
                                for (int i = 0; i < 30; i++) yield return null;
                                exv.Interact(player);
                                for (int i = 0; i < 20; i++) yield return null;
                                System.Collections.IEnumerator Hold(float secs, params GameAction[] acts)
                                {
                                    Controls.TestHeld.Clear();
                                    foreach (var a in acts) Controls.TestHeld[a] = 1f;
                                    float u4 = Time.unscaledTime + secs; while (Time.unscaledTime < u4) yield return null;
                                    Controls.TestHeld.Clear();
                                }
                                for (int c = 0; c < 4; c++)
                                {
                                    yield return Hold(1.6f, GameAction.BoomDown, GameAction.StickOut);
                                    yield return Hold(2.2f, GameAction.StickIn, GameAction.ExBucketCurl, GameAction.BoomDown);
                                    yield return Hold(1.8f, GameAction.BoomUp, GameAction.ExBucketCurl);
                                    yield return Hold(1.2f, GameAction.SwingLeft);
                                    yield return Hold(1.5f, GameAction.ExBucketDump);
                                    yield return Hold(1.2f, GameAction.SwingRight);
                                }
                                float u5 = Time.unscaledTime + 2f; while (Time.unscaledTime < u5) yield return null;
                                Vector3 tipAt = exv.bucketTip != null ? exv.bucketTip.position : exv.transform.position;
                                Debug.Log($"[Excavator] ground at the bucket drawn every {site.DrawStepAt(tipAt)} square(s); {FPSBase.Mining.DigSite.EyeInfo}; dug {site.CutDepth(tipAt):0.00} m there");
                                yield return Shot("excavator_dig.png");
                                exv.Exit();
                                for (int i = 0; i < 10; i++) yield return null;
                            }
                        }
                    }
                }
            }
            var plant = FindFirstObjectByType<FPSBase.Mining.WashPlant>();
            if (plant != null) { plant.Interact(null); yield return Shot("panel_plant.png"); ClosePanel(); }
            // the town: a street, the phone, the map
            var jobsC = FPSBase.City.Jobs.Instance;
            if (jobsC != null && FPSBase.City.House.All.Count > 12 && player != null)
            {
                var h = FPSBase.City.House.All[3];
                var hj = jobsC.NewRequest(h, FPSBase.City.JobKind.Driveway);
                jobsC.Accept(hj);
                var tj = jobsC.NewRequest(FPSBase.City.House.All[12], FPSBase.City.JobKind.Ditch);
                player.PlaceAt(h.transform.position - h.transform.forward * 9f + h.transform.right * 3f + Vector3.up * 0.3f, h.transform.eulerAngles.y - 12f);
                for (int i = 0; i < 120; i++) yield return null;
                yield return Shot("town_street.png");
                // a trench job: the light blue box shows how wide and deep to dig, the beacon marks the house
                if (tj != null)
                {
                    jobsC.Accept(tj);
                    Vector3 mid = (tj.a + tj.b) / 2f, along = tj.b - tj.a; along.y = 0f;
                    Vector3 side = Vector3.Cross(Vector3.up, along.normalized);
                    Vector3 at = mid + side * 5f - along.normalized * 3f;
                    var th = FPSBase.City.House.All[12];
                    if (th.Footprint.Contains(new Vector2(at.x, at.z))) at = mid - side * 5f - along.normalized * 3f;
                    player.PlaceAt(at + Vector3.up * 0.3f, Quaternion.LookRotation(new Vector3(mid.x - at.x, 0f, mid.z - at.z)).eulerAngles.y);
                    for (int i = 0; i < 60; i++) yield return null;
                    yield return Shot("town_trench.png");
                }
                // the north-east corner of town (Cedar Ave / East Ave): the ground there, and a height survey in the log
                {
                    var tsite = FPSBase.Mining.DigSite.At(new Vector3(556f, 0f, 40f));
                    var sb = new System.Text.StringBuilder("[Ground] Cedar/East corner, original / now (x across, z down):\n");
                    for (float z = 80f; z >= 20f; z -= 6f)
                    {
                        sb.Append($"z{z,4:0}:");
                        for (float x = 530f; x <= 600f; x += 7f)
                        {
                            var p = new Vector3(x, 0f, z);
                            bool rc = Physics.Raycast(p + Vector3.up * 60f, Vector3.down, out var hit, 120f, ~0, QueryTriggerInteraction.Ignore);
                            string o = tsite != null && tsite.Contains(p) ? $"{tsite.OriginalY(p):0.0}" : "  -";
                            sb.Append($" {o,4}/{(rc ? hit.point.y : -99f),5:0.0}");
                        }
                        sb.Append('\n');
                    }
                    Debug.Log(sb.ToString());
                    player.PlaceAt(new Vector3(552f, 0.3f, 30f), 20f);
                    for (int i = 0; i < 90; i++) yield return null;
                    yield return Shot("town_ne_corner.png");
                    player.PlaceAt(new Vector3(540f, 0.3f, 80f), 120f);
                    for (int i = 0; i < 90; i++) yield return null;
                    yield return Shot("town_ne_corner2.png");
                }
                // the road into town: the welcome board with the turns for your jobs, and a street of different houses
                player.PlaceAt(new Vector3(350f, 0.3f, 6f), 80f);
                for (int i = 0; i < 90; i++) yield return null;
                yield return Shot("town_welcome.png");
                player.PlaceAt(new Vector3(382f, 0.3f, 60f), 90f);
                for (int i = 0; i < 90; i++) yield return null;
                yield return Shot("town_houses.png");
                jobsC.OpenPhone(h);
                for (int i = 0; i < 5; i++) yield return null;
                yield return Shot("phone.png");
                jobsC.ShowTab(2);
                for (int i = 0; i < 5; i++) yield return null;
                yield return Shot("phone_build.png");
                jobsC.ShowTab(0);
                ClosePanel();
                MapView.Instance?.BakeAll();
                yield return Shot("town_hud.png");
                OpenMap();
                yield return Shot("map.png");
                mapView = new Rect(h.transform.position.x - 70f, h.transform.position.z - 70f, 140f, 140f);
                yield return Shot("map_town.png");
                var yard = FPSBase.Vehicles.VehicleBase.All.Count > 0 ? FPSBase.Vehicles.VehicleBase.All[0].transform.position : Vector3.zero;
                mapView = new Rect(yard.x - 50f, yard.z - 50f, 100f, 100f);
                yield return Shot("map_machines.png");
                ClosePanel();
                var bin = FindFirstObjectByType<FPSBase.City.GravelBin>();
                if (bin != null)
                {
                    player.PlaceAt(bin.transform.position + new Vector3(-14f, 0.3f, -10f), 50f);
                    for (int i = 0; i < 60; i++) yield return null;
                    yield return Shot("town_supply.png");
                    Vector3 bp = bin.transform.position, at = bp + new Vector3(-3.5f, 0f, -3.5f);
                    player.PlaceAt(at + Vector3.up * 0.3f, Quaternion.LookRotation(new Vector3(bp.x - at.x, 0f, bp.z - at.z)).eulerAngles.y);
                    for (int i = 0; i < 30; i++) yield return null;
                    yield return Shot("town_bin_prompt.png");
                }
            }
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

        bool menuBare; // (a screenshot of just the scene behind)
        bool MenuBackdropShowing => FPSBase.UI.MenuScene.Showing;

        void CloseMenu()
        {
            FPSBase.UI.MenuScene.Want(false); // (straight away: the game starts on untouched ground)
            menuOpen = false;
            menuMessage = null;
            paused = false;
        }

        void MenuStyles()
        {
            if (menuTitle != null) return;
            menuTitle = new GUIStyle(GUI.skin.label) { fontSize = 54, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            menuTitle.normal.textColor = FPSBase.UI.ShopTheme.Gold;
            menuSub = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            menuButton = new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold };
            menuButton2 = new GUIStyle(FPSBase.UI.ShopTheme.PlainButton) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
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
                GUILayout.Label($"<color=#f5c84c><b><size=18>{p.title}</size></b></color>   <color=#b9a685>{p.date}</color>", menuRich);
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
            GUI.Box(new Rect(x, y, w, h), GUIContent.none, menuOpen ? menuPanel : box);
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
            if (screen == MenuScreen.Options && !loading) { if (controlsOpen) DrawControlsMenu(); else DrawOptionsMenu(); return; }
            // behind: the machines at work (MenuScene), darkened at the edges; or plain dark earth if there's none
            bool scene = MenuBackdropShowing;
            FPSBase.UI.ShopTheme.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.06f, 0.042f, 0.026f, scene ? 0.18f : 0.94f));
            float w = Mathf.Min(600f, Screen.width - 40f), h = Mathf.Min(760f, Screen.height - 40f);
            // over to the right when the machines are working behind (they're in the middle), else in the middle
            float x = scene && Screen.width > 1100f ? Screen.width - w - Mathf.Min(80f, Screen.width * 0.04f) : (Screen.width - w) / 2f;
            var area = new Rect(x, (Screen.height - h) / 2f, w, h);
            GUILayout.BeginArea(area);
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical(menuPanel);
            GUILayout.Label("DOWN AND DIRTY", menuTitle, GUILayout.Height(80));
            GUILayout.Label("dig it, wash it, get rich", menuSub);
            GUILayout.Space(24);
            if (loading) { GUILayout.Label("Loading the world…", menuSub); GUILayout.EndVertical(); GUILayout.FlexibleSpace(); GUILayout.EndArea(); return; }
            switch (screen)
            {
                case MenuScreen.Main: MainScreen(); break;
                case MenuScreen.Single: SingleScreen(); break;
                case MenuScreen.Blog: BlogScreen(); break;
                default: MultiScreen(); break;
            }
            if (!string.IsNullOrEmpty(menuMessage)) { GUILayout.Space(10); GUILayout.Label($"<color=#f99>{menuMessage}</color>", menuRich); }
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.EndArea();
        }

        void MainScreen()
        {
            if (GUILayout.Button("Single Player", menuButton, GUILayout.Height(58))) { screen = MenuScreen.Single; RefreshSaves(SaveGame.Mode.SinglePlayer); menuMessage = null; }
            GUILayout.Space(8);
            if (GUILayout.Button("Multiplayer", menuButton, GUILayout.Height(58))) { screen = MenuScreen.Multi; RefreshSaves(SaveGame.Mode.Multiplayer); menuMessage = null; }
            GUILayout.Space(8);
            if (GUILayout.Button("Options", menuButton2, GUILayout.Height(48))) { screen = MenuScreen.Options; pauseScroll = Vector2.zero; menuMessage = null; }
            GUILayout.Space(8);
            if (GUILayout.Button("Dev Blog: what's new", menuButton2, GUILayout.Height(48))) { screen = MenuScreen.Blog; menuScroll = Vector2.zero; menuMessage = null; }
            GUILayout.Space(8);
            if (GUILayout.Button("Quit", menuButton2, GUILayout.Height(48))) QuitGame();
            GUILayout.Space(20);
            GUILayout.Label("Esc in the game opens the pause menu: save your world there.", menuSub);
        }

        /// <summary>The settings before you load in: the same tabs as the pause menu (game, audio, graphics, key bindings).</summary>
        void DrawOptionsMenu()
        {
            FPSBase.UI.ShopTheme.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.06f, 0.042f, 0.026f, MenuBackdropShowing ? 0.3f : 0.94f));
            float w = Mathf.Min(620f, Screen.width - 40f), h = Mathf.Min(720f, Screen.height - 40f);
            var r = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            GUI.Box(r, GUIContent.none, menuPanel);
            GUILayout.BeginArea(new Rect(r.x + 20, r.y + 14, w - 40, h - 28));
            GUILayout.Label("Options", header);
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
            if (GUILayout.Button("Back  (Esc)", GUILayout.Height(36))) { screen = MenuScreen.Main; capturing = null; }
            GUILayout.EndArea();
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
            FPSBase.UI.MenuScene.Want(false); // (the machines behind the menu packed up and their ground put back first)
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
                if (FPSBase.Net.NetSession.InSession) FPSBase.Net.NetSession.Instance.Leave(false);
                SaveGame.CurrentName = null;
                Time.timeScale = 1f;
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
        }
    }
}
