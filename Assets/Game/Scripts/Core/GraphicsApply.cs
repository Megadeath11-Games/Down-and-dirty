using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace FPSBase.Core
{
    /// <summary>Pushes the saved graphics settings into Unity / HDRP. Safe to call any time.</summary>
    public static class GraphicsApply
    {
        public static readonly int[] FpsChoices = { 0, 30, 60, 90, 120, 144, 165, 240 };
        public static readonly string[] AaNames = { "Off", "FXAA", "SMAA", "TAA" };
        static readonly List<VolumeProfile> profiles = new();

        /// <summary>Everything: window, quality, frame rate, post effects, cameras.</summary>
        public static void All()
        {
            if (GameSettings.Quality >= 0 && GameSettings.Quality < QualitySettings.names.Length && QualitySettings.GetQualityLevel() != GameSettings.Quality)
                QualitySettings.SetQualityLevel(GameSettings.Quality, true);
            Window();
            QualitySettings.vSyncCount = GameSettings.VSync ? 1 : 0;
            Application.targetFrameRate = GameSettings.FpsLimit > 0 ? GameSettings.FpsLimit : -1;
            AudioListener.volume = GameSettings.MasterVolume;
            PostFx();
            Cameras();
            foreach (var site in FPSBase.Mining.DigSite.All) if (site != null) site.grassDrawDistance = GameSettings.GrassDistance;
            foreach (var t in Object.FindObjectsByType<FPSBase.World.LowPolyTerrain>(FindObjectsSortMode.None)) t.grassDrawDistance = GameSettings.GrassDistance * 0.92f;
        }

        public static void Window()
        {
            if (Application.isEditor) return; // don't fight the editor's game view
            var mode = (FullScreenMode)GameSettings.WindowMode;
            int w = GameSettings.ResolutionW, h = GameSettings.ResolutionH;
            if (w <= 0 || h <= 0) { w = Display.main.systemWidth; h = Display.main.systemHeight; }
            if (Screen.width != w || Screen.height != h || Screen.fullScreenMode != mode) Screen.SetResolution(w, h, mode);
        }

        static Volume settingsVolume;

        /// <summary>
        /// Shadow distance and motion blur: one global volume of our own, above everything else, so it also
        /// overrides HDRP's default settings profile (which has motion blur on).
        /// </summary>
        public static void PostFx()
        {
            if (settingsVolume == null)
            {
                var go = new GameObject("Player Settings Volume");
                Object.DontDestroyOnLoad(go);
                settingsVolume = go.AddComponent<Volume>();
                settingsVolume.isGlobal = true;
                settingsVolume.priority = 1000f;
                settingsVolume.sharedProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            }
            var p = settingsVolume.sharedProfile;
            if (!p.TryGet(out MotionBlur mb)) mb = p.Add<MotionBlur>(true);
            mb.intensity.overrideState = true;
            mb.intensity.value = GameSettings.MotionBlur ? 0.4f : 0f;
            if (!p.TryGet(out HDShadowSettings sh)) sh = p.Add<HDShadowSettings>();
            sh.maxShadowDistance.overrideState = true;
            sh.maxShadowDistance.value = GameSettings.ShadowDistance;
        }

        /// <summary>Anti-aliasing on every camera, field of view on vehicle cameras (the player's is set by PlayerLook).</summary>
        public static void Cameras()
        {
            var aa = GameSettings.AntiAliasing switch
            {
                0 => HDAdditionalCameraData.AntialiasingMode.None,
                2 => HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing,
                3 => HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing,
                _ => HDAdditionalCameraData.AntialiasingMode.FastApproximateAntialiasing,
            };
            foreach (var cam in Camera.allCameras)
            {
                if (cam.TryGetComponent(out HDAdditionalCameraData hd) && hd.antialiasing != aa) hd.antialiasing = aa;
                if (cam.GetComponentInParent<FPSBase.Player.PlayerLook>() == null && cam.targetTexture == null) cam.fieldOfView = GameSettings.Fov;
            }
        }

        /// <summary>Resolutions the monitor offers (unique sizes, biggest first).</summary>
        public static List<Vector2Int> Resolutions()
        {
            var list = new List<Vector2Int>();
            foreach (var r in Screen.resolutions)
            {
                var v = new Vector2Int(r.width, r.height);
                if (!list.Contains(v)) list.Add(v);
            }
            if (list.Count == 0) list.Add(new Vector2Int(Screen.width, Screen.height));
            list.Sort((a, b) => (b.x * b.y).CompareTo(a.x * a.y));
            return list;
        }
    }
}
