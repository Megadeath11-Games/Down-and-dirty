using System.IO;
using UnityEngine;

namespace FPSBase.Core
{
    /// <summary>
    /// The game used to be called FPS-BaseBuilder, and Windows keeps each game's files under its name. The first
    /// time Down and Dirty starts, it brings the saved worlds over from the old folder (the old ones stay put).
    /// </summary>
    static class RenameMigration
    {
        const string OldName = "FPS-BaseBuilder";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Run()
        {
            if (Application.isEditor) return;
            try
            {
                string now = Application.persistentDataPath;
                string parent = Path.GetDirectoryName(now);
                if (string.IsNullOrEmpty(parent) || Path.GetFileName(now) == OldName) return;
                string oldSaves = Path.Combine(parent, OldName, "Saves"), newSaves = Path.Combine(now, "Saves");
                if (!Directory.Exists(oldSaves) || Directory.Exists(newSaves)) return;
                Directory.CreateDirectory(newSaves);
                int n = 0;
                foreach (var f in Directory.GetFiles(oldSaves))
                {
                    File.Copy(f, Path.Combine(newSaves, Path.GetFileName(f)), false);
                    n++;
                }
                Debug.Log($"[Rename] brought {n} saved worlds over from {OldName}");
            }
            catch (System.Exception e) { Debug.LogWarning("[Rename] couldn't bring the old saves over: " + e.Message); }
        }
    }
}
