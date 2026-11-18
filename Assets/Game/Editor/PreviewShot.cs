using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FPSBase.EditorTools
{
    /// <summary>
    /// Captures a preview image of the test map from inside Play mode (so sky, exposure and temporal effects
    /// run exactly as in-game).  Menu: Tools > FPS Base > Preview Screenshot (Play Mode)
    /// Command line: Unity.exe -batchmode -projectPath ... -executeMethod FPSBase.EditorTools.PreviewShot.ShootBatch
    /// Output: Screenshots/preview.png
    /// </summary>
    [InitializeOnLoad]
    public static class PreviewShot
    {
        const string Key = "FPSBase.PreviewShot.Pending";
        const string BatchKey = "FPSBase.PreviewShot.Batch";
        const string Out = "Screenshots/preview.png";

        static PreviewShot()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        [MenuItem("Tools/FPS Base/Preview Screenshot (Play Mode)")]
        public static void Shoot() => Begin(false);

        public static void ShootBatch() => Begin(true);

        static void Begin(bool batch)
        {
            if (File.Exists(Out)) File.Delete(Out);
            EditorSceneManager.OpenScene("Assets/Game/Scenes/MovementPlayground.unity");
            SessionState.SetBool(Key, true);
            SessionState.SetBool(BatchKey, batch);
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
            SessionState.SetBool(Key, false);
            var go = new GameObject("PreviewCapturer");
            go.AddComponent<FPSBase.DebugTools.PreviewCapturer>().outputPath = Out;
            double start = EditorApplication.timeSinceStartup;
            void Poll()
            {
                bool done = File.Exists(Out);
                if (!done && EditorApplication.timeSinceStartup - start < 180) return;
                EditorApplication.update -= Poll;
                Debug.Log(done ? "[PreviewShot] saved " + Out : "[PreviewShot] timed out");
                EditorApplication.ExitPlaymode();
                if (SessionState.GetBool(BatchKey, false)) EditorApplication.delayCall += () => EditorApplication.Exit(done ? 0 : 1);
            }
            EditorApplication.update += Poll;
        }
    }
}
