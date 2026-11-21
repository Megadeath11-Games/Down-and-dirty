using System.Collections.Generic;
using FPSBase.Mining;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSBase.World
{
    /// <summary>
    /// Draws the big landscape terrain as faceted low-poly ground (flat-shaded triangles in a few greens, rock on
    /// steep slopes) plus instanced grass tufts around the play area. The Terrain keeps its collider for physics.
    /// </summary>
    [RequireComponent(typeof(Terrain))]
    public class LowPolyTerrain : MonoBehaviour
    {
        [Tooltip("grassA, grassB, darkGrass (hills), rock")]
        public Material[] materials = new Material[4];
        [Tooltip("Heightmap samples per facet edge.")]
        public int step = 2;
        public int chunkCells = 32;
        [Tooltip("World X/Z area not drawn here (the dirt field draws itself).")]
        public Rect excludeRect;

        [Header("Grass tufts")]
        public Mesh tuftMesh;
        public Material tuftMaterial;
        public Rect grassArea = new Rect(-60f, -110f, 240f, 240f);
        public float grassPerSquareMeter = 0.45f;
        public float grassDrawDistance = 110f;
        public Vector2 noGrassCenter;
        public float noGrassRadius;

        readonly List<(Bounds b, List<Matrix4x4> m)> grass = new();

        void Awake()
        {
            var terrain = GetComponent<Terrain>();
            terrain.drawHeightmap = false;
            terrain.drawTreesAndFoliage = false;
            BuildMesh(terrain);
            BuildGrass(terrain);
        }

        void BuildMesh(Terrain terrain)
        {
            var data = terrain.terrainData;
            int res = data.heightmapResolution;
            int cells = (res - 1) / step;
            float cs = data.size.x / cells;
            Vector3 pos = terrain.transform.position;
            var h = data.GetHeights(0, 0, res, res);
            float H(int cx, int cz) => h[cz * step, cx * step] * data.size.y;

            int chunks = Mathf.CeilToInt(cells / (float)chunkCells);
            var verts = new List<Vector3>();
            var tris = new List<int>[] { new(), new(), new(), new() };
            for (int j = 0; j < chunks; j++)
                for (int i = 0; i < chunks; i++)
                {
                    verts.Clear(); foreach (var t in tris) t.Clear();
                    int xa = i * chunkCells, xb = Mathf.Min(xa + chunkCells, cells);
                    int za = j * chunkCells, zb = Mathf.Min(za + chunkCells, cells);
                    for (int z = za; z < zb; z++)
                        for (int x = xa; x < xb; x++)
                        {
                            float wx0 = pos.x + x * cs, wz0 = pos.z + z * cs;
                            bool inside = wx0 >= excludeRect.xMin && wx0 + cs <= excludeRect.xMax && wz0 >= excludeRect.yMin && wz0 + cs <= excludeRect.yMax;
                            if (inside) continue;
                            Vector3 a = P(x, z), b = P(x + 1, z), c = P(x, z + 1), d = P(x + 1, z + 1);
                            if (((x + z) & 1) == 0) { Tri(a, c, d, x, z, 0); Tri(a, d, b, x, z, 1); }
                            else { Tri(a, c, b, x, z, 0); Tri(b, c, d, x, z, 1); }
                        }
                    if (verts.Count == 0) continue;
                    var mesh = new Mesh { name = $"LowPolyTerrain {i},{j}" };
                    if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
                    mesh.SetVertices(verts);
                    mesh.subMeshCount = tris.Length;
                    for (int k = 0; k < tris.Length; k++) mesh.SetTriangles(tris[k], k, false);
                    mesh.RecalculateNormals();
                    mesh.RecalculateBounds();
                    var go = new GameObject(mesh.name);
                    go.transform.SetParent(transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = materials;
                }

            Vector3 P(int cx, int cz)
            {
                var v = new Vector3(cx * cs, H(cx, cz), cz * cs);
                // tuck the ground under the dirt field's edge so the two never fight
                float wx = pos.x + v.x, wz = pos.z + v.z;
                if (wx > excludeRect.xMin && wx < excludeRect.xMax && wz > excludeRect.yMin && wz < excludeRect.yMax) v.y -= 0.1f;
                return v;
            }

            void Tri(Vector3 a, Vector3 b, Vector3 c, int x, int z, int half)
            {
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                int cat;
                if (n.y < 0.78f) cat = 3; // rock on steep slopes
                else
                {
                    float noise = Mathf.PerlinNoise(x * 0.07f + 11.3f, z * 0.07f + 4.1f) + ((x * 13 + z * 7 + half) % 5 - 2) * 0.05f;
                    float height = (a.y + b.y + c.y) / 3f;
                    cat = height > 9f && noise > 0.45f ? 2 : noise > 0.55f ? 1 : 0;
                }
                int idx = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c);
                tris[cat].Add(idx); tris[cat].Add(idx + 1); tris[cat].Add(idx + 2);
            }
        }

        void BuildGrass(Terrain terrain)
        {
            if (tuftMesh == null || tuftMaterial == null) return;
            var rng = new System.Random(4242);
            const float chunk = 16f;
            Vector3 tp = terrain.transform.position;
            for (float cz = grassArea.yMin; cz < grassArea.yMax; cz += chunk)
                for (float cx = grassArea.xMin; cx < grassArea.xMax; cx += chunk)
                {
                    var list = new List<Matrix4x4>();
                    int n = Mathf.Min(1023, Mathf.RoundToInt(chunk * chunk * grassPerSquareMeter));
                    float minY = float.MaxValue, maxY = float.MinValue;
                    for (int k = 0; k < n; k++)
                    {
                        float x = cx + (float)rng.NextDouble() * chunk, z = cz + (float)rng.NextDouble() * chunk;
                        if (excludeRect.Contains(new Vector2(x, z))) continue;
                        if (noGrassRadius > 0f && Vector2.Distance(new Vector2(x, z), noGrassCenter) < noGrassRadius) continue;
                        // clumpy: thin out with noise so there are meadows and bare patches
                        if (Mathf.PerlinNoise(x * 0.05f + 3f, z * 0.05f + 9f) < 0.32f) continue;
                        var nrm = terrain.terrainData.GetInterpolatedNormal((x - tp.x) / terrain.terrainData.size.x, (z - tp.z) / terrain.terrainData.size.z);
                        if (nrm.y < 0.85f) continue;
                        float y = terrain.SampleHeight(new Vector3(x, 0f, z)) + tp.y;
                        minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                        float s = 0.7f + (float)rng.NextDouble() * 0.8f;
                        list.Add(Matrix4x4.TRS(new Vector3(x, y, z), Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), Vector3.one * s));
                    }
                    if (list.Count == 0) continue;
                    var b = new Bounds(new Vector3(cx + chunk / 2f, (minY + maxY) / 2f, cz + chunk / 2f), new Vector3(chunk, maxY - minY + 1.5f, chunk));
                    grass.Add((b, list));
                }
        }

        void Update()
        {
            if (grass.Count == 0) return;
            var cam = TuftRenderer.ActiveCamera();
            if (cam == null) return;
            var planes = TuftRenderer.Planes(cam);
            Vector3 cp = cam.transform.position;
            float d2 = grassDrawDistance * grassDrawDistance;
            foreach (var (b, m) in grass)
            {
                float bd2 = b.SqrDistance(cp);
                if (bd2 > d2 || !GeometryUtility.TestPlanesAABB(planes, b)) continue;
                TuftRenderer.Draw(tuftMesh, tuftMaterial, m, b, Mathf.Sqrt(bd2), grassDrawDistance);
            }
        }
    }
}
