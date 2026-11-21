using System.IO;
using FPSBase.City;
using UnityEditor;
using UnityEngine;

namespace FPSBase.EditorTools
{
    /// <summary>
    /// Saves the ten house plans as assets in Resources/HousePlans, where the game loads them from. Edit a plan's
    /// asset to change it (footprint, footings, depths, tolerances, price); this only writes plans that aren't there
    /// yet, so edits are kept. Delete an asset and run it again to get the original back.
    /// </summary>
    public static class HousePlanBuilder
    {
        const string Folder = "Assets/Game/Resources/HousePlans";

        [MenuItem("FPSBase/Build house plans")]
        public static void Build()
        {
            Directory.CreateDirectory(Folder);
            int made = 0;
            foreach (var p in HousePlan.Defaults())
            {
                string path = $"{Folder}/{p.order:00} {p.title}.asset";
                if (AssetDatabase.LoadAssetAtPath<HousePlan>(path) != null) continue;
                AssetDatabase.CreateAsset(p, path);
                made++;
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[HousePlans] {made} new plan(s) saved in {Folder}");
        }

        /// <summary>For -executeMethod.</summary>
        public static void RunBatch()
        {
            Build();
            EditorApplication.Exit(0);
        }
    }
}
