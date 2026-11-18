using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FPSBase.EditorTools
{
    /// <summary>Runs GoldLoopSmokeTest in Play mode. Menu: Tools > FPS Base > Run Gold Loop Test. Batch: -executeMethod FPSBase.EditorTools.SmokeTestRunner.RunBatch</summary>
    [InitializeOnLoad]
    public static class SmokeTestRunner
    {
        const string Key = "FPSBase.SmokeTest.Pending", BatchKey = "FPSBase.SmokeTest.Batch";

        static SmokeTestRunner() => EditorApplication.playModeStateChanged += OnPlay;

        [MenuItem("Tools/FPS Base/Run Gold Loop Test")]
        public static void Run() => Begin(false);
        public static void RunBatch() => Begin(true);

        static void Begin(bool batch)
        {
            if (File.Exists("Logs/smoketest.txt")) File.Delete("Logs/smoketest.txt");
            EditorSceneManager.OpenScene("Assets/Game/Scenes/MovementPlayground.unity");
            SessionState.SetBool(Key, true);
            SessionState.SetBool(BatchKey, batch);
            EditorApplication.EnterPlaymode();
        }

        static void OnPlay(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
            SessionState.SetBool(Key, false);
            var test = new GameObject("GoldLoopSmokeTest").AddComponent<FPSBase.DebugTools.GoldLoopSmokeTest>();
            double start = EditorApplication.timeSinceStartup;
            void Poll()
            {
                if (test != null && !test.Done && EditorApplication.timeSinceStartup - start < 1200) return;
                if (test != null && !test.Done) File.WriteAllText("Logs/smoketest.txt", "TIMED OUT\n" + test.Report);
                EditorApplication.update -= Poll;
                bool ok = File.Exists("Logs/smoketest.txt") && File.ReadAllText("Logs/smoketest.txt").Contains("ALL PASSED");
                Debug.Log("[SmokeTest] " + (File.Exists("Logs/smoketest.txt") ? File.ReadAllText("Logs/smoketest.txt") : "no report (timed out)"));
                EditorApplication.ExitPlaymode();
                if (SessionState.GetBool(BatchKey, false)) EditorApplication.delayCall += () => EditorApplication.Exit(ok ? 0 : 1);
            }
            EditorApplication.update += Poll;
        }
    }
}
