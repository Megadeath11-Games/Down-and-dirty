using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FPSBase.EditorTools
{
    /// <summary>
    /// Builds a standalone Windows game to Build/Windows/Down and Dirty.exe
    ///   Menu: Tools > FPS Base > Build Windows EXE
    ///   Command line: Unity.exe -batchmode -quit -projectPath ... -executeMethod FPSBase.EditorTools.BuildScript.BuildWindows
    /// </summary>
    public static class BuildScript
    {
        public const string OutPath = "Build/Windows/Down and Dirty.exe";
        /// <summary>BUILD_OUT overrides where it goes (a preview build while the game is running).</summary>
        static string Out => string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("BUILD_OUT")) ? OutPath : System.Environment.GetEnvironmentVariable("BUILD_OUT");

        [MenuItem("Tools/FPS Base/Build Windows EXE")]
        public static void BuildWindows()
        {
            PlayerSettings.companyName = "FPSBase";
            PlayerSettings.productName = "Down and Dirty";
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.runInBackground = true; // multiplayer keeps talking while alt-tabbed
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);

            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0) scenes = new[] { "Assets/Game/Scenes/MovementPlayground.unity" };

            // the game's old name: clear its exe out of the folder so only the new one ships
            string dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Out));
            try
            {
                if (System.IO.File.Exists(System.IO.Path.Combine(dir, "FPS-BaseBuilder.exe"))) System.IO.File.Delete(System.IO.Path.Combine(dir, "FPS-BaseBuilder.exe"));
                if (System.IO.Directory.Exists(System.IO.Path.Combine(dir, "FPS-BaseBuilder_Data"))) System.IO.Directory.Delete(System.IO.Path.Combine(dir, "FPS-BaseBuilder_Data"), true);
            }
            catch (System.Exception e) { Debug.LogWarning("[BuildScript] couldn't remove the old exe: " + e.Message); }

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Out,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });

            var s = report.summary;
            // Steam multiplayer runs as Steam's test app 480 ("Spacewar"): the exe finds its app id here
            if (s.result == BuildResult.Succeeded)
                System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Out), "steam_appid.txt"), "480");
            Debug.Log($"[BuildScript] {s.result}: {s.outputPath}  size {s.totalSize / (1024f * 1024f):0} MB  errors {s.totalErrors}  time {s.totalTime}");
            if (Application.isBatchMode && s.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
