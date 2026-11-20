using System.Collections.Generic;
using UnityEngine;

namespace FPSBase.Mining
{
    /// <summary>
    /// Loose dirt (heaps, berms, windrows, spoil) is drawn covered in clods, the same ones that fall out of buckets,
    /// so a pushed or dumped heap keeps looking like a pile of clumps instead of a smooth surface. Placed per cell
    /// from a hash (stable, no flicker), rebuilt with the chunk whenever the dirt there moves. Third part of DigSite.
    /// </summary>
    public partial class DigSite
    {
        [Header("Clods on loose dirt")]
        public float clumpDrawDistance = 90f;
        List<Matrix4x4>[,][] chunkClumps;

        void RebuildClumps(int ci, int cj, int xa, int xb, int za, int zb)
        {
            chunkClumps ??= new List<Matrix4x4>[chunksZ, chunksX][];
            var lists = chunkClumps[cj, ci];
            if (lists == null) chunkClumps[cj, ci] = lists = new[] { new List<Matrix4x4>(), new List<Matrix4x4>(), new List<Matrix4x4>(), new List<Matrix4x4>() };
            foreach (var l in lists) l.Clear();
            int st = detail;
            float scale = cell * st / 0.5f;
            float span = cell * st;
            for (int z = za; z < zb; z += st)
                for (int x = xa; x < xb; x += st)
                {
                    float l = loose[z, x];
                    if (l < 0.04f) continue;
                    uint h = (uint)((x / st) * 73856093) ^ (uint)((z / st) * 19349663) ^ 0x9e3779b9u;
                    int n = l > 0.25f ? 3 : l > 0.1f ? 2 : 1;
                    float lv = l * cellArea;
                    for (int k = 0; k < n; k++)
                    {
                        h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                        float ox = (h & 255) / 255f, oz = ((h >> 8) & 255) / 255f;
                        float sz = (0.06f + ((h >> 16) & 15) / 15f * 0.09f) * scale;
                        Vector3 p = origin + new Vector3(x * cell + (ox - 0.5f) * span, 0f, z * cell + (oz - 0.5f) * span);
                        p.y = SurfaceY(p) + sz * 0.15f;
                        // which material this clod is, by the cell's make-up
                        float pick = ((h >> 20) & 1023) / 1023f * lv;
                        int cat = (pick -= looseClay[z, x]) < 0f ? 2 : (pick -= looseSand[z, x]) < 0f ? 1 : (pick -= looseGravel[z, x]) < 0f ? 3 : 0;
                        var rot = Quaternion.Euler(((h >> 3) & 63) * 5.6f, ((h >> 9) & 255) * 1.41f, ((h >> 17) & 63) * 5.6f);
                        lists[cat].Add(Matrix4x4.TRS(p, rot, new Vector3(sz * 1.15f, sz * 0.8f, sz)));
                    }
                }
        }

        void DrawClumps()
        {
            if (chunkClumps == null) return;
            var cam = TuftRenderer.ActiveCamera();
            if (cam == null) return;
            var planes = TuftRenderer.Planes(cam);
            float d2 = clumpDrawDistance * clumpDrawDistance;
            for (int j = 0; j < chunksZ; j++)
                for (int i = 0; i < chunksX; i++)
                {
                    var lists = chunkClumps[j, i];
                    if (lists == null) continue;
                    var b = chunkBounds[j, i];
                    if (b.SqrDistance(cam.transform.position) > d2) continue;
                    if (!GeometryUtility.TestPlanesAABB(planes, b)) continue;
                    for (int c = 0; c < 4; c++) if (lists[c].Count > 0) DirtGrains.DrawInstanced(c, lists[c], b, false);
                }
        }

        /// <summary>Clods drawn on loose dirt right now (for the test).</summary>
        public int ClumpDecorations
        {
            get
            {
                int n = 0;
                if (chunkClumps == null) return 0;
                foreach (var lists in chunkClumps) if (lists != null) foreach (var l in lists) n += l.Count;
                return n;
            }
        }
    }
}
