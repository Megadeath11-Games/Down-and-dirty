using System.IO;
using FPSBase.Player;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace FPSBase.EditorTools
{
    /// <summary>The player's mannequin body (Assets/Game/Models/Mannequin) and see-through cab glass.</summary>
    public static partial class ProjectBootstrap
    {
        const string MannequinDir = "Assets/Game/Models/Mannequin";
        const float MannequinHeight = 1.8f;

        /// <summary>A prefab of the mannequin with an HDRP material, scaled to 1.8 m. Null if the model isn't in the project.</summary>
        static GameObject MannequinPrefab()
        {
            string fbx = $"{MannequinDir}/Ch36.fbx";
            if (!File.Exists(fbx)) { Debug.LogWarning("Mannequin model missing: " + fbx); return null; }

            // import as a plain skinned mesh: no avatar, no animation clips (it's posed procedurally), no materials
            var imp = (ModelImporter)AssetImporter.GetAtPath(fbx);
            bool dirty = false;
            if (imp.animationType != ModelImporterAnimationType.Generic) { imp.animationType = ModelImporterAnimationType.Generic; dirty = true; }
            if (imp.importAnimation) { imp.importAnimation = false; dirty = true; }
            if (imp.materialImportMode != ModelImporterMaterialImportMode.None) { imp.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
            if (imp.importCameras || imp.importLights) { imp.importCameras = false; imp.importLights = false; dirty = true; }
            if (dirty) imp.SaveAndReimport();

            string tex = $"{MannequinDir}/Textures";
            Texture2D Tex(string name, bool normal)
            {
                string p = $"{tex}/{name}";
                if (!File.Exists(p)) return null;
                var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                bool d = false;
                if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; d = true; }
                if (ti.maxTextureSize != 2048) { ti.maxTextureSize = 2048; d = true; }
                if (d) ti.SaveAndReimport();
                return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            }
            var diffuse = Tex("Ch36_1001_Diffuse.png", false);
            var normalMap = Tex("Ch36_1001_Normal.png", true);

            string matPath = $"{MannequinDir}/Mannequin.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { mat = new Material(Shader.Find("HDRP/Lit")); AssetDatabase.CreateAsset(mat, matPath); }
            mat.SetColor("_BaseColor", Color.white);
            if (diffuse != null) mat.SetTexture("_BaseColorMap", diffuse);
            if (normalMap != null) { mat.SetTexture("_NormalMap", normalMap); mat.SetFloat("_NormalScale", 1f); }
            mat.SetFloat("_Smoothness", 0.35f);
            mat.SetFloat("_Metallic", 0f);
            HDShaderUtils.ResetMaterialKeywords(mat);
            EditorUtility.SetDirty(mat);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = "Mannequin";
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mats = new Material[Mathf.Max(1, smr.sharedMaterials.Length)];
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                smr.sharedMaterials = mats;
                smr.updateWhenOffscreen = true; // the procedural pose can move limbs outside the bind-pose bounds
            }
            foreach (var anim in go.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(anim);

            // scale to a 1.8 m person standing on the origin
            var b = new Bounds(go.transform.position, Vector3.zero); bool any = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            if (any && b.size.y > 0.1f) go.transform.localScale *= MannequinHeight / b.size.y;
            Debug.Log($"Mannequin bounds height {b.size.y:0.00} -> scaled x{go.transform.localScale.x:0.000}");
            go.AddComponent<MannequinRig>();

            string prefabPath = $"{Gen}/Mannequin.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        /// <summary>Gives the player a body (mannequin + footsteps).</summary>
        static void AddPlayerBody(GameObject player)
        {
            var prefab = MannequinPrefab();
            var body = player.AddComponent<PlayerBody>();
            if (prefab == null) return;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, player.transform);
            inst.name = "Body";
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            body.rig = inst.GetComponent<MannequinRig>();
        }

        /// <summary>Clear, slightly tinted cab glass you can see the driver through (both sides drawn, so it shows from inside too).</summary>
        static Material CabGlass(string name, Color tint, float alpha)
        {
            string path = $"{Gen}/Materials/M_{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("HDRP/Lit")); AssetDatabase.CreateAsset(mat, path); }
            tint.a = alpha;
            mat.SetColor("_BaseColor", tint);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 0.96f);
            mat.SetFloat("_SurfaceType", 1f);            // transparent
            mat.SetFloat("_BlendMode", 0f);              // alpha
            mat.SetFloat("_EnableBlendModePreserveSpecularLighting", 1f);
            mat.SetFloat("_DoubleSidedEnable", 1f);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_TransparentZWrite", 0f);
            mat.SetFloat("_TransparentBackfaceEnable", 0f);
            HDShaderUtils.ResetMaterialKeywords(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
