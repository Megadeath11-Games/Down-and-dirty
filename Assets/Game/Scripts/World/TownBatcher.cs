using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSBase.World
{
    /// <summary>
    /// The town is over a thousand little objects (house walls, roofs, windows, doors, steps, mailboxes, kerbs...),
    /// each its own draw, and again for its shadow: about 4,600 draws a frame, the biggest share of the render's CPU
    /// time. At load, the parts that never move are merged into one mesh per material per 64 m tile; their colliders
    /// stay as they were. Anything that can move or change keeps its own renderer: what can be knocked over
    /// (fences, sheds, yard trees), and anything with its own behaviour (signs, bins, the dig yards, building sites).
    /// </summary>
    public class TownBatcher : MonoBehaviour
    {
        public const float Tile = 64f;
        public static int MergedRenderers, Tiles;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AtLoad()
        {
            var root = GameObject.Find("Town");
            if (root == null || root.GetComponent<TownBatcher>() != null) return;
            root.AddComponent<TownBatcher>().Merge();
        }

        class Group { public readonly List<CombineInstance> parts = new(); public int verts; public bool shadows; }

        /// <summary>A part that never moves or changes: nothing with behaviour of its own on it or above it in the town.</summary>
        bool Fixed(Transform t)
        {
            for (var p = t; p != null && p != transform; p = p.parent)
            {
                if (p.GetComponent<Rigidbody>() != null) return false;
                foreach (var mb in p.GetComponents<MonoBehaviour>())
                    if (mb != null && !(mb is City.House) && !(mb is City.Street) && !(mb is TownBatcher)) return false;
            }
            return true;
        }

        void Merge()
        {
            var groups = new Dictionary<(int, int, Material, bool, int), Group>();
            var keyOrder = new List<(int, int, Material, bool, int)>();
            int merged = 0, skipped = 0;
            foreach (var mr in GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!mr.enabled || mr.isPartOfStaticBatch) continue;
                var mf = mr.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null || !mesh.isReadable || !Fixed(mr.transform)) { skipped++; continue; }
                var mats = mr.sharedMaterials;
                if (mats.Length < mesh.subMeshCount) { skipped++; continue; }
                bool shadows = mr.shadowCastingMode != ShadowCastingMode.Off;
                Vector3 p = mr.bounds.center;
                int tx = Mathf.FloorToInt(p.x / Tile), tz = Mathf.FloorToInt(p.z / Tile);
                var m = mr.transform.localToWorldMatrix;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    if (mats[s] == null) continue;
                    int part = 0;
                    (int, int, Material, bool, int) k;
                    Group g;
                    while (true)
                    {
                        k = (tx, tz, mats[s], shadows, part);
                        if (!groups.TryGetValue(k, out g)) { g = new Group { shadows = shadows }; groups[k] = g; keyOrder.Add(k); }
                        if (g.verts + mesh.vertexCount <= 200000) break;
                        part++;
                    }
                    g.parts.Add(new CombineInstance { mesh = mesh, subMeshIndex = s, transform = m });
                    g.verts += mesh.vertexCount;
                }
                mr.enabled = false;
                merged++;
            }
            int tiles = 0;
            foreach (var k in keyOrder)
            {
                var g = groups[k];
                var mesh = new Mesh { name = $"Town {k.Item1},{k.Item2} {k.Item3.name}", indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(g.parts.ToArray(), true, true, false);
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                var go = new GameObject(mesh.name); // (in world space: not under the town, so nothing above moves it)
                go.layer = gameObject.layer;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = k.Item3;
                r.shadowCastingMode = g.shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                tiles++;
            }
            MergedRenderers = merged; Tiles = tiles;
            Debug.Log($"[Town] merged {merged} fixed town parts into {tiles} tile meshes ({skipped} kept as they were)");
        }
    }
}
