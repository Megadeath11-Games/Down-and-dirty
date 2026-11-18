using System.Collections.Generic;
using System.IO;

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
                    native[z, x] = nat; loose[z, x] = lo; looseGold[z, x] = go;
                    looseSand[z, x] = sa; looseGravel[z, x] = gr; looseClay[z, x] = cl;
                    trampled[z, x] = tr; packed[z, x] = pk;
                    everChanged.Add(i);
                    MarkHeights(x, z);
                }
            }
            finally { applyingNet = false; }
        }
    }
}
