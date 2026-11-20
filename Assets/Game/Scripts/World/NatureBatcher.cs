using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSBase.World
{
    /// <summary>
    /// The trees, bushes and rocks round the map are thousands of little objects, each its own draw (twice, with the
    /// trunk and the leaves, and again for every shadow cascade): that was a third of everything drawn and most of the
    /// render's CPU time. At load they're merged into one mesh per material per 96 m tile; each tree keeps its own
    /// collider, only its renderer goes. Distant tiles stop casting shadows (they're too far to see one anyway).
    /// </summary>
    public class NatureBatcher : MonoBehaviour
    {
        public const float Tile = 96f;
        public static int MergedRenderers, Tiles;
        static readonly string[] Names = { "Pine", "Tree", "Bush", "Boulder" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AtLoad()
        {
            var root = GameObject.Find("Nature");
            if (root == null || root.GetComponent<NatureBatcher>() != null) return;
            root.AddComponent<NatureBatcher>().Merge();
        }

        struct Key { public int tx, tz; public Material mat; }
        class Group { public readonly List<CombineInstance> parts = new(); public int verts; public bool shadows; }

        readonly List<(MeshRenderer r, Vector3 c)> tiles = new();

        void Merge()
        {
            var groups = new Dictionary<(int, int, Material, int), Group>();
            var keyOrder = new List<(int, int, Material, int)>();
            int merged = 0;
            foreach (var mr in GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!mr.enabled || !IsNature(mr.name)) continue;
                var mf = mr.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null || !mesh.isReadable) continue;
                var mats = mr.sharedMaterials;
                Vector3 p = mr.transform.position;
                int tx = Mathf.FloorToInt(p.x / Tile), tz = Mathf.FloorToInt(p.z / Tile);
                var m = mr.transform.localToWorldMatrix;
                for (int s = 0; s < mesh.subMeshCount && s < mats.Length; s++)
                {
                    // (a tile's mesh can't pass 4 billion verts, but keep them a sensible size: start a new part past 200k)
                    int part = 0;
                    (int, int, Material, int) k;
                    Group g;
                    while (true)
                    {
                        k = (tx, tz, mats[s], part);
                        if (!groups.TryGetValue(k, out g)) { g = new Group { shadows = mr.shadowCastingMode != ShadowCastingMode.Off }; groups[k] = g; keyOrder.Add(k); }
                        if (g.verts + mesh.vertexCount <= 200000) break;
                        part++;
                    }
                    g.parts.Add(new CombineInstance { mesh = mesh, subMeshIndex = s, transform = m });
                    g.verts += mesh.vertexCount;
                }
                mr.enabled = false;
                merged++;
            }
            foreach (var k in keyOrder)
            {
                var g = groups[k];
                var mesh = new Mesh { name = $"Nature {k.Item1},{k.Item2} {k.Item3.name}", indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(g.parts.ToArray(), true, true, false);
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                var go = new GameObject(mesh.name); // (at the scene root: the parts are in world space)
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = k.Item3;
                r.shadowCastingMode = g.shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                tiles.Add((r, mesh.bounds.center));
            }
            MergedRenderers = merged; Tiles = tiles.Count;
            Debug.Log($"[Nature] merged {merged} trees, bushes and rocks into {tiles.Count} tile meshes");
        }

        static bool IsNature(string n)
        {
            foreach (var s in Names) if (n == s) return true;
            return false;
        }

        // far tiles: no shadow (the shadow distance cuts them off anyway, this just saves the culling work)
        float timer;
        void Update()
        {
            if ((timer -= Time.unscaledDeltaTime) > 0f) return;
            timer = 0.5f;
            var cam = Camera.main != null ? Camera.main : FindActiveCamera();
            if (cam == null) return;
            Vector3 c = cam.transform.position;
            foreach (var (r, centre) in tiles)
            {
                if (r == null) continue;
                float d = Vector2.Distance(new Vector2(c.x, c.z), new Vector2(centre.x, centre.z));
                var want = d < 260f ? ShadowCastingMode.On : ShadowCastingMode.Off;
                if (r.shadowCastingMode != want) r.shadowCastingMode = want;
            }
        }

        static Camera FindActiveCamera()
        {
            foreach (var c in Camera.allCameras) if (c.isActiveAndEnabled) return c;
            return null;
        }
    }
}
