using System.Collections.Generic;
using FPSBase.Core;
using FPSBase.Mining;
using UnityEngine;

namespace FPSBase.Player
{
    /// <summary>
    /// Handheld metal detector + hand shovel. Take it out (G), sweep the coil over the dirt field and listen:
    /// gold rings high, coins and relics in the middle, junk iron grunts low (X turns on iron discrimination).
    /// Stand over the strongest spot and dig with the shovel (R) until the find comes up.
    /// </summary>
    public class MetalDetector : MonoBehaviour
    {
        public Camera viewCamera;
        public float coilAhead = 1.05f;
        public float searchRadius = 1.7f;

        public bool Out { get; private set; }
        public bool Discriminate { get; private set; }
        public float Signal { get; private set; }
        public BuriedFind Target { get; private set; }
        public float TargetDepth { get; private set; }
        public Vector3 CoilPoint { get; private set; }
        public int GoldFound, CoinsFound, JunkFound;
        public float GoldGramsFound, ValueFound;

        Transform model, coil, shaft;
        AudioSource tone;
        readonly List<BuriedFind> near = new(), dug = new();
        float digCooldown, sway;
        GUIStyle meterStyle, small;
        static AudioClip toneClip;

        void Awake()
        {
            if (viewCamera == null) viewCamera = GetComponentInChildren<Camera>(true);
        }

        void OnDisable() { if (tone != null) tone.volume = 0f; }

        public void SetOut(bool on)
        {
            Out = on;
            if (on && model == null) BuildModel();
            if (model != null) model.gameObject.SetActive(on);
            if (!on) { Signal = 0f; Target = null; if (tone != null) tone.volume = 0f; }
        }

        void Update()
        {
            bool blocked = GameUI.Instance != null && GameUI.Instance.BlocksGameplay;
            if (!blocked)
            {
                if (Controls.Pressed(GameAction.DetectorToggle))
                {
                    SetOut(!Out);
                    GameUI.Toast(Out ? $"Metal detector out — sweep the dirt field. [{Controls.N(GameAction.HandDig)}] digs with the shovel." : "Metal detector away");
                }
                if (Out && Controls.Pressed(GameAction.DetectorDiscriminate))
                {
                    Discriminate = !Discriminate;
                    GameUI.Toast(Discriminate ? "Iron discrimination ON — junk iron is ignored" : "Iron discrimination OFF — everything beeps");
                }
            }
            if (viewCamera == null) return;

            // the coil sits on the ground a step ahead of you, swaying a little side to side
            Vector3 fwd = Vector3.ProjectOnPlane(viewCamera.transform.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = transform.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            sway += Time.deltaTime * 2.2f;
            Vector3 p = transform.position + fwd * coilAhead + right * Mathf.Sin(sway) * (Out ? 0.18f : 0f);
            p.y = GroundY(p) + 0.07f;
            CoilPoint = p;

            if (Out) Sense();
            if (Out) PlaceModel(fwd, right);

            digCooldown -= Time.deltaTime;
            if (!blocked && digCooldown <= 0f && Controls.Pressed(GameAction.HandDig)) { digCooldown = 0.3f; Shovel(fwd, right); }
        }

        float GroundY(Vector3 p)
        {
            var site = DigSite.At(p);
            if (site != null && site.Contains(p)) return site.SurfaceY(p);
            return Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out var h, 6f, ~0, QueryTriggerInteraction.Ignore) ? h.point.y : transform.position.y;
        }

        /// <summary>Signal 0..1 a target gives at a coil position.</summary>
        public static float SignalOf(BuriedFind f, Vector3 coilPoint, out float depth)
        {
            var site = DigSite.At(f.pos);
            depth = site != null ? Mathf.Max(0f, site.SurfaceY(f.pos) - f.pos.y) : 0f;
            float dx = f.pos.x - coilPoint.x, dz = f.pos.z - coilPoint.z;
            float d = Mathf.Sqrt(dx * dx + dz * dz + depth * depth);
            float range = 0.35f + 0.8f * f.Size;
            float s = Mathf.Clamp01(1f - d / range);
            return s * s * (3f - 2f * s);
        }

        void Sense()
        {
            var site = DigSite.At(CoilPoint);
            Signal = 0f; Target = null;
            if (site == null || !site.Contains(CoilPoint)) { UpdateTone(); return; }
            site.FindsNear(CoilPoint, searchRadius, near);
            foreach (var f in near)
            {
                if (Discriminate && f.kind == FindKind.Iron) continue;
                float s = SignalOf(f, CoilPoint, out float depth);
                if (s > Signal) { Signal = s; Target = f; TargetDepth = depth; }
            }
            UpdateTone();
        }

        void UpdateTone()
        {
            if (tone == null)
            {
                tone = gameObject.AddComponent<AudioSource>();
                tone.clip = ToneClip(); tone.loop = true; tone.playOnAwake = false; tone.spatialBlend = 0f; tone.volume = 0f;
                tone.Play();
            }
            if (!tone.isPlaying) tone.Play();
            float pitch = Target == null ? 1f : Target.kind switch { FindKind.Gold => 2.1f, FindKind.Relic => 1.75f, FindKind.Coin => 1.45f, _ => 0.55f };
            tone.pitch = pitch;
            float v = Signal > 0.03f ? Mathf.Lerp(0.05f, 0.5f, Signal) : 0f;
            if (Target != null && Target.kind == FindKind.Iron) v *= 0.6f + 0.4f * Mathf.Sin(Time.time * 40f); // grunty
            tone.volume = Out ? v * FPSBase.Core.GameSettings.DetectorVolume : 0f;
        }

        static AudioClip ToneClip()
        {
            if (toneClip != null) return toneClip;
            const int rate = 44100;
            var data = new float[rate];
            for (int i = 0; i < rate; i++)
            {
                float t = i / (float)rate;
                data[i] = Mathf.Sin(2f * Mathf.PI * 440f * t) * 0.6f + Mathf.Sin(2f * Mathf.PI * 880f * t) * 0.15f;
            }
            toneClip = AudioClip.Create("DetectorTone", rate, 1, rate, false);
            toneClip.SetData(data, 0);
            return toneClip;
        }

        void Shovel(Vector3 fwd, Vector3 right) => DigAt(CoilPoint, right * 0.8f - fwd * 0.2f);

        /// <summary>One shovelful at a spot (spoil thrown by `spoilOffset`). Returns how many things turned up.</summary>
        public int DigAt(Vector3 at, Vector3 spoilOffset)
        {
            var site = DigSite.At(at);
            if (site == null || !site.Contains(at)) { GameUI.Toast("Too hard to dig here — use the shovel on the dirt field"); return 0; }
            site.HandDig(at, at + spoilOffset, dug);
            PourFx.Dig(at);
            foreach (var f in dug) Reward(f);
            return dug.Count;
        }

        void Reward(BuriedFind f)
        {
            var eco = Economy.Instance;
            switch (f.kind)
            {
                case FindKind.Gold:
                    eco?.AddGold(f.grams);
                    GoldFound++; GoldGramsFound += f.grams;
                    GameUI.Toast($"Found a {f.grams:0.0} g {f.name}!  (worth about ${f.grams * 95f:N0} at the assay office)");
                    break;
                case FindKind.Coin:
                case FindKind.Relic:
                    eco?.Earn(f.value);
                    CoinsFound++; ValueFound += f.value;
                    GameUI.Toast($"Found an {f.name} — a collector pays ${f.value:N0}");
                    break;
                default:
                    JunkFound++;
                    GameUI.Toast($"Dug up a {f.name}. Junk.  (Tip: [{Controls.N(GameAction.DetectorDiscriminate)}] ignores iron)");
                    break;
            }
        }

        // ------------------------------------------------------------------ model + HUD

        void BuildModel()
        {
            model = new GameObject("Metal Detector").transform;
            model.SetParent(transform, false);
            var dark = new Color(0.12f, 0.12f, 0.13f);
            shaft = Prim(PrimitiveType.Cylinder, model, new Color(0.6f, 0.6f, 0.62f));
            coil = Prim(PrimitiveType.Cylinder, model, dark);
            coil.localScale = new Vector3(0.32f, 0.015f, 0.26f);
            var box = Prim(PrimitiveType.Cube, shaft, new Color(0.85f, 0.55f, 0.1f));
            box.localPosition = new Vector3(0f, 0.55f, 0.6f); box.localScale = new Vector3(3f, 0.12f, 4f);
        }

        static Transform Prim(PrimitiveType t, Transform parent, Color c)
        {
            var go = GameObject.CreatePrimitive(t);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<MeshRenderer>();
            var sh = Shader.Find("HDRP/Lit");
            if (sh != null) { var m = new Material(sh); m.SetColor("_BaseColor", c); r.sharedMaterial = m; }
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        void PlaceModel(Vector3 fwd, Vector3 right)
        {
            // held from the player's head position (not the camera: in third person that's behind you)
            Vector3 eye = viewCamera.transform.parent != null ? viewCamera.transform.parent.position : viewCamera.transform.position;
            Vector3 hand = eye + right * 0.28f - Vector3.up * 0.55f + fwd * 0.25f;
            Vector3 c = CoilPoint + Vector3.up * 0.02f;
            coil.position = c;
            coil.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            Vector3 d = hand - c;
            shaft.position = (hand + c) * 0.5f;
            shaft.rotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
            shaft.localScale = new Vector3(0.025f, d.magnitude * 0.5f, 0.025f);
        }

        void OnGUI()
        {
            GUI.skin = FPSBase.UI.ShopTheme.Skin;
            if (!Out || (GameUI.Instance != null && GameUI.Instance.BlocksGameplay)) return;
            meterStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            small ??= new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            var r = new Rect(Screen.width / 2f - 200, Screen.height - 128, 400, 112);
            GUI.color = new Color(0f, 0f, 0f, 0.6f); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white;
            string id = Target == null || Signal < 0.03f ? "— no signal —" : Target.kind switch
            {
                FindKind.Gold => "GOLD?  (high tone)",
                FindKind.Coin => "Coin / brass  (mid tone)",
                FindKind.Relic => "Big non-ferrous  (mid-high tone)",
                _ => "Iron  (low grunt)",
            };
            GUI.Label(new Rect(r.x, r.y + 4, r.width, 24), "METAL DETECTOR" + (Discriminate ? "  ·  iron disc. ON" : ""), meterStyle);
            var bar = new Rect(r.x + 20, r.y + 32, r.width - 40, 20);
            GUI.color = new Color(1f, 1f, 1f, 0.15f); GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = Target != null && Target.kind == FindKind.Iron ? new Color(0.75f, 0.35f, 0.25f) : new Color(1f, 0.8f, 0.25f);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Signal, bar.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            string depth = Target != null && Signal > 0.25f ? $"   ·   depth ~{Mathf.Round(TargetDepth * 20f) * 5f:0} cm" : "";
            GUI.Label(new Rect(r.x, r.y + 54, r.width, 22), id + depth, meterStyle);
            GUI.Label(new Rect(r.x + 10, r.y + 78, r.width - 20, 32),
                $"[{Controls.N(GameAction.HandDig)}] dig with shovel   [{Controls.N(GameAction.DetectorDiscriminate)}] iron discrimination   [{Controls.N(GameAction.DetectorToggle)}] put away   ·   found {GoldFound} nuggets ({GoldGramsFound:0.0} g), {CoinsFound} coins/relics", small);
        }
    }
}
