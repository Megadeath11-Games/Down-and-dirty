using System.Collections.Generic;
using System.IO;
using Mathf = UnityEngine.Mathf;

namespace FPSBase.Mining
{
    /// <summary>
    /// Multiplayer: every ground cell that changes (dug, filled, pushed, rutted, scuffed) is remembered, so the
    /// changes can be sent to the other players, and a player who joins later can be sent the whole worked site.
    /// Cells received from another player are written straight in and are not sent back out again.
    /// </summary>
    public partial class DigSite
    {
        readonly HashSet<int> netChanged = new();   // changed since the last send
        readonly HashSet<int> everChanged = new();  // changed since the game started (sent to a player who joins)
        bool applyingNet;
        struct NetCutBlock { public int bx, bz; public float bottom, top, vol; }
        readonly Dictionary<int, NetCutBlock> netCut = new();

        void NetMark(int x, int z)
        {
            if (applyingNet || orig == null) return;
            int i = z * res + x;
            everChanged.Add(i);
            netChanged.Add(i);
        }

        /// <summary>Cells changed since the last call (cleared). `all` = every cell changed since the start.</summary>
        public List<int> TakeChangedCells(bool all)
        {
            var list = new List<int>(all ? everChanged : netChanged);
            if (!all) netChanged.Clear();
            return list;
        }

        public void ForgetChanges() => netChanged.Clear();

        public const int NetCellBytes = 4 + 6 * 4 + 1;

        public void WriteCell(BinaryWriter w, int i)
        {
            int x = i % res, z = i / res;
            w.Write(i);
            w.Write(native[z, x]); w.Write(loose[z, x]); w.Write(looseGold[z, x]);
            w.Write(looseSand[z, x]); w.Write(looseGravel[z, x]); w.Write(looseClay[z, x]);
            w.Write((byte)((trampled[z, x] ? 1 : 0) | (UnityEngine.Mathf.RoundToInt(UnityEngine.Mathf.Clamp01(packed[z, x]) * 127f) << 1)));
        }

        /// <summary>
        /// A save from before the town ground was fixed: its dug cells hold heights on the old, wrong ground (the
        /// hills squeezed toward one corner, the east and north strips 8 m down). Work out that old ground and move
        /// each saved cell onto the new one, keeping how deep it was dug or how high it was filled.
        /// </summary>
        public void RebaseOldTownGround()
        {
            if (primary || orig == null || (res - 1) % 512 != 0) return;
            float k = 512f / (res - 1); // (this ground's squares in the old map's)
            UnityEngine.Terrain hills = null;
            foreach (var t in UnityEngine.Terrain.activeTerrains)
                if (t != null && t.GetComponent<DigSite>() == null && t.terrainData.size.x > 500f &&
                    t.transform.position.x > 250f && t.transform.position.x < 350f) hills = t;
            if (hills == null) return;
            const int oldRes = 449; const float oldCell = 0.5f;
            int moved = 0;
            foreach (int i in new List<int>(everChanged)) // (a copy: MarkHeights adds to the set)
            {
                int x = i % res, z = i / res;
                float ox = x * k, oz = z * k;
                float old;
                if (ox > oldRes - 1 || oz > oldRes - 1) old = origin.y; // never filled in: the bottom of the ground
                else
                {
                    var w = origin + new UnityEngine.Vector3(ox * oldCell, 0f, oz * oldCell);
                    old = hills.SampleHeight(w) + hills.transform.position.y;
                    bool edge = ox < 0.5f || oz < 0.5f || ox > oldRes - 1.5f || oz > oldRes - 1.5f;
                    if (!edge) old += (UnityEngine.Mathf.PerlinNoise(w.x * 0.4f + 5f, w.z * 0.4f + 2f) - 0.5f) * 0.05f;
                }
                float shift = orig[z, x] - old;
                if (UnityEngine.Mathf.Abs(shift) < 0.005f) continue;
                native[z, x] += shift;
                MarkHeights(x, z);
                moved++;
            }
            UnityEngine.Debug.Log($"[Save] moved {moved} of {everChanged.Count} dug town cells onto the fixed town ground");
        }

        /// <summary>
        /// Read `count` cells written on a ground of `fromRes` squares a side (a save from before the ground got
        /// finer): each one's dig, fill and dirt is spread over the finer squares around it.
        /// </summary>
        public void ReadCells(BinaryReader r, int count, int fromRes)
        {
            if (fromRes <= 1 || fromRes == res) { ReadCells(r, count); return; }
            int f = (res - 1) / (fromRes - 1);
            if (f < 2 || f * (fromRes - 1) != res - 1) { r.BaseStream.Position += (long)count * NetCellBytes; return; }
            var got = new Dictionary<int, float[]>(count);
            for (int n = 0; n < count; n++)
            {
                int i = r.ReadInt32();
                var v = new float[8];
                for (int k = 0; k < 6; k++) v[k] = r.ReadSingle();
                byte tb = r.ReadByte();
                v[6] = (tb & 1) != 0 ? 1f : 0f; v[7] = (tb >> 1) / 127f;
                int ox = i % fromRes, oz = i / fromRes;
                if (ox < 0 || oz < 0 || ox >= fromRes || oz >= fromRes) continue;
                v[0] -= orig[oz * f, ox * f];           // how far it was dug (-) or built up (+)
                float oldArea = cellArea * f * f;
                for (int k = 2; k <= 5; k++) v[k] /= oldArea; // gold and dirt kinds: per square metre
                got[i] = v;
            }
            float[] Old(int ox, int oz) => ox >= 0 && oz >= 0 && ox < fromRes && oz < fromRes && got.TryGetValue(oz * fromRes + ox, out var v) ? v : null;
            var fineCells = new HashSet<int>();
            foreach (int i in got.Keys)
            {
                int ox = i % fromRes, oz = i / fromRes;
                for (int z = Mathf.Max(0, (oz - 1) * f + 1); z <= Mathf.Min(res - 1, (oz + 1) * f - 1); z++)
                    for (int x = Mathf.Max(0, (ox - 1) * f + 1); x <= Mathf.Min(res - 1, (ox + 1) * f - 1); x++)
                        fineCells.Add(z * res + x);
            }
            applyingNet = true;
            try
            {
                var val = new float[8];
                foreach (int i in fineCells)
                {
                    int x = i % res, z = i / res;
                    int ox = x / f, oz = z / f; float tx = (x - ox * f) / (float)f, tz = (z - oz * f) / (float)f;
                    System.Array.Clear(val, 0, 8);
                    bool any = false;
                    for (int c = 0; c < 4; c++)
                    {
                        int cx = ox + (c & 1), cz = oz + (c >> 1);
                        float w = ((c & 1) != 0 ? tx : 1f - tx) * ((c >> 1) != 0 ? tz : 1f - tz);
                        if (w <= 0f) continue;
                        var v = Old(cx, cz);
                        if (v == null) continue;
                        any = true;
                        for (int k = 0; k < 8; k++) val[k] += v[k] * w;
                    }
                    if (!any) continue;
                    native[z, x] = orig[z, x] + val[0];
                    loose[z, x] = Mathf.Max(0f, val[1]);
                    looseGold[z, x] = val[2] * cellArea; looseSand[z, x] = val[3] * cellArea;
                    looseGravel[z, x] = val[4] * cellArea; looseClay[z, x] = val[5] * cellArea;
                    trampled[z, x] = val[6] >= 0.5f; packed[z, x] = Mathf.Clamp01(val[7]);
                    everChanged.Add(i);
                    MarkHeights(x, z);
                }
            }
            finally { applyingNet = false; }
        }

        /// <summary>Read `count` cells written by WriteCell on another player's game and put them in this ground.</summary>
        public void ReadCells(BinaryReader r, int count)
        {
            applyingNet = true;
            try
            {
                for (int n = 0; n < count; n++)
                {
                    int i = r.ReadInt32();
                    float nat = r.ReadSingle(), lo = r.ReadSingle(), go = r.ReadSingle(), sa = r.ReadSingle(), gr = r.ReadSingle(), cl = r.ReadSingle();
                    byte tb = r.ReadByte();
                    bool tr = (tb & 1) != 0;
                    float pk = (tb >> 1) / 127f;
                    int x = i % res, z = i / res;
                    if (x < 0 || z < 0 || x >= res || z >= res) continue;
                    float was = Surf(x, z);
                    native[z, x] = nat; loose[z, x] = lo; looseGold[z, x] = go;
                    looseSand[z, x] = sa; looseGravel[z, x] = gr; looseClay[z, x] = cl;
                    trampled[z, x] = tr; packed[z, x] = pk;
                    // a friend's blade cut this away: show it as clods rolling in front of their blade (only for show:
                    // the dirt itself arrives as ground from their game when their clods come to rest)
                    // (gathered into blocks of the map's squares first, like your own blade does: on fine ground one clod
                    // per small square was 16 times as many clods as your own pushing makes)
                    float now = Surf(x, z);
                    if (was - now > 0.02f && PushClods)
                    {
                        int key = (z / detail) * 65536 + (x / detail);
                        if (netCut.TryGetValue(key, out var b)) { b.bottom = Mathf.Min(b.bottom, now); b.top = Mathf.Max(b.top, was); b.vol += (was - now) * cellArea; netCut[key] = b; }
                        else netCut[key] = new NetCutBlock { bx = x / detail, bz = z / detail, bottom = now, top = was, vol = (was - now) * cellArea };
                    }
                    everChanged.Add(i);
                    MarkHeights(x, z);
                }
                foreach (var b in netCut.Values)
                {
                    float half = (detail - 1) * 0.5f;
                    var cp = new UnityEngine.Vector3(origin.x + (b.bx * detail + half) * cell, 0f, origin.z + (b.bz * detail + half) * cell);
                    if (DirtGrains.NearBlade(cp, 1.2f + cell * detail * 0.5f))
                        DirtGrains.Cut(cp, cell * detail, b.bottom, b.top, new MaterialLoad(b.vol, 0f), null, true, DirtGrains.NearBladeClod(cp));
                }
            }
            finally { applyingNet = false; netCut.Clear(); }
        }
    }
}
