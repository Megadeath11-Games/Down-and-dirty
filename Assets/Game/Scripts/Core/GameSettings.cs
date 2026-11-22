using UnityEngine;

namespace FPSBase.Core
{
    public enum ExcavatorControlMode { Realistic, Simplified }

    /// <summary>Player options, saved between sessions (PlayerPrefs). Values are cached so they're cheap to read every frame.</summary>
    public static class GameSettings
    {
        static int GetI(string k, int d) => PlayerPrefs.GetInt(k, d);
        static float GetF(string k, float d) => PlayerPrefs.GetFloat(k, d);
        static void SetI(string k, int v) { PlayerPrefs.SetInt(k, v); PlayerPrefs.Save(); }
        static void SetF(string k, float v) { PlayerPrefs.SetFloat(k, v); PlayerPrefs.Save(); }

        // ------------------------------------------------------------------ gameplay

        public static bool AutoBhop
        {
            get => GetI("set.autoBhop", 0) == 1;
            set => SetI("set.autoBhop", value ? 1 : 0);
        }

        /// <summary>CS-style "jump on mouse wheel" bind. Off by default.</summary>
        public static bool WheelJump
        {
            get => wheelJump ??= GetI("set.wheelJump", 0) == 1;
            set { wheelJump = value; SetI("set.wheelJump", value ? 1 : 0); }
        }
        static bool? wheelJump;

        public static ExcavatorControlMode ExcavatorControls
        {
            get => (ExcavatorControlMode)GetI("set.excavatorControls", 0);
            set => SetI("set.excavatorControls", (int)value);
        }

        /// <summary>Testing: everything is free (on by default while the game is in development).</summary>
        public static bool UnlimitedMoney
        {
            get => GetI("set.unlimitedMoney", 1) == 1;
            set => SetI("set.unlimitedMoney", value ? 1 : 0);
        }

        /// <summary>CS:GO-style sensitivity (degrees = counts * sens * 0.022).</summary>
        public static float MouseSensitivity
        {
            get => sens ??= GetF("set.sensitivity", 2.0f);
            set { sens = Mathf.Clamp(value, 0.1f, 10f); SetF("set.sensitivity", sens.Value); }
        }
        static float? sens;

        /// <summary>Excavator: how fast each function moves for a full push (1 = as built; 0.25 slow and fine .. 2 quick).</summary>
        public static float ExSwingSens { get => exSens[0] ??= GetF("set.exSwing", 1f); set { exSens[0] = Mathf.Clamp(value, 0.25f, 2f); SetF("set.exSwing", exSens[0].Value); } }
        public static float ExBoomSens { get => exSens[1] ??= GetF("set.exBoom", 1f); set { exSens[1] = Mathf.Clamp(value, 0.25f, 2f); SetF("set.exBoom", exSens[1].Value); } }
        public static float ExStickSens { get => exSens[2] ??= GetF("set.exStick", 1f); set { exSens[2] = Mathf.Clamp(value, 0.25f, 2f); SetF("set.exStick", exSens[2].Value); } }
        public static float ExBucketSens { get => exSens[3] ??= GetF("set.exBucket", 1f); set { exSens[3] = Mathf.Clamp(value, 0.25f, 2f); SetF("set.exBucket", exSens[3].Value); } }
        static readonly float?[] exSens = new float?[4];

        /// <summary>How solid the labels over things are (name tags, the house site's label): 0.15 faint .. 1 solid.</summary>
        public static float LabelOpacity
        {
            get => labelOpacity ??= GetF("set.labelOpacity", 1f);
            set { labelOpacity = Mathf.Clamp(value, 0.15f, 1f); SetF("set.labelOpacity", labelOpacity.Value); }
        }
        static float? labelOpacity;

        /// <summary>How big a label over something `d` m away is drawn: full size close up, smaller further off.</summary>
        public static float LabelScale(float d) => Mathf.Clamp(14f / Mathf.Max(1f, d), 0.45f, 1f);

        public static bool InvertY
        {
            get => invertY ??= GetI("set.invertY", 0) == 1;
            set { invertY = value; SetI("set.invertY", value ? 1 : 0); }
        }
        static bool? invertY;

        /// <summary>On foot: camera behind the player instead of in their eyes.</summary>
        public static bool ThirdPerson
        {
            get => thirdPerson ??= GetI("set.thirdPerson", 0) == 1;
            set { thirdPerson = value; SetI("set.thirdPerson", value ? 1 : 0); }
        }
        static bool? thirdPerson;

        public static float ThirdPersonDistance
        {
            get => tpDist ??= GetF("set.tpDistance", 3.2f);
            set { tpDist = Mathf.Clamp(value, 1.5f, 8f); SetF("set.tpDistance", tpDist.Value); }
        }
        static float? tpDist;

        // ------------------------------------------------------------------ audio (0..1)

        public static float MasterVolume { get => master ??= GetF("set.volMaster", 0.8f); set { master = Mathf.Clamp01(value); SetF("set.volMaster", master.Value); AudioListener.volume = master.Value; } }
        public static float EngineVolume { get => engine ??= GetF("set.volEngine", 0.8f); set { engine = Mathf.Clamp01(value); SetF("set.volEngine", engine.Value); } }
        public static float EffectsVolume { get => effects ??= GetF("set.volEffects", 0.9f); set { effects = Mathf.Clamp01(value); SetF("set.volEffects", effects.Value); } }
        public static float FootstepVolume { get => steps ??= GetF("set.volSteps", 0.8f); set { steps = Mathf.Clamp01(value); SetF("set.volSteps", steps.Value); } }
        public static float DetectorVolume { get => detector ??= GetF("set.volDetector", 0.8f); set { detector = Mathf.Clamp01(value); SetF("set.volDetector", detector.Value); } }
        public static float MusicVolume { get => music ??= GetF("set.volMusic", 0.45f); set { music = Mathf.Clamp01(value); SetF("set.volMusic", music.Value); } }
        /// <summary>Music while you play too (not just in the menus).</summary>
        public static bool MusicInGame { get => musicInGame ??= GetI("set.musicInGame", 1) == 1; set { musicInGame = value; SetI("set.musicInGame", value ? 1 : 0); } }
        static bool? musicInGame;
        static float? master, engine, effects, steps, detector, music;

        // ------------------------------------------------------------------ graphics

        public static int Quality { get => GetI("set.quality", -1); set => SetI("set.quality", value); }           // -1 = project default
        public static int WindowMode { get => GetI("set.windowMode", (int)FullScreenMode.FullScreenWindow); set => SetI("set.windowMode", value); }
        public static int ResolutionW { get => GetI("set.resW", 0); set => SetI("set.resW", value); }              // 0 = desktop
        public static int ResolutionH { get => GetI("set.resH", 0); set => SetI("set.resH", value); }
        public static bool VSync { get => GetI("set.vsync", 1) == 1; set => SetI("set.vsync", value ? 1 : 0); }
        public static int FpsLimit { get => GetI("set.fpsLimit", 0); set => SetI("set.fpsLimit", value); }           // 0 = unlimited
        public static float Fov { get => fov ??= GetF("set.fov", 74f); set { fov = Mathf.Clamp(value, 55f, 100f); SetF("set.fov", fov.Value); } }
        static float? fov;
        public static float ShadowDistance { get => GetF("set.shadowDist", 150f); set => SetF("set.shadowDist", value); }
        public static float GrassDistance { get => GetF("set.grassDist", 120f); set => SetF("set.grassDist", value); }
        public static int AntiAliasing { get => GetI("set.aa", 1); set => SetI("set.aa", value); }                 // 0 off, 1 FXAA, 2 SMAA, 3 TAA
        public static bool MotionBlur { get => GetI("set.motionBlur", 0) == 1; set => SetI("set.motionBlur", value ? 1 : 0); }
        public static bool ShowTestList { get => showTestList ??= GetI("set.showTestList", 1) == 1; set { showTestList = value; SetI("set.showTestList", value ? 1 : 0); } }
        static bool? showTestList;
        public static bool ShowFps { get => showFps ??= GetI("set.showFps", 0) == 1; set { showFps = value; SetI("set.showFps", value ? 1 : 0); } }
        static bool? showFps;
    }
}
