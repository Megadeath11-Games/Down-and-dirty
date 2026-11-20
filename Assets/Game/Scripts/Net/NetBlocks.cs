using System.Collections.Generic;
using System.IO;
using FPSBase.City;
using FPSBase.Vehicles;
using Steamworks;
using UnityEngine;

namespace FPSBase.Net
{
    /// <summary>
    /// Concrete blocks in multiplayer. Whoever last moved a block (bought it, hooked it, chained or unchained it)
    /// runs it and sends where it is while it moves (and straight away, reliably, when it's hooked, let go, chained
    /// or comes to rest); everyone else's copy follows. `NetSeq` goes up each time someone takes a block over, so an
    /// old update from its previous owner is ignored. The host also sends every block to a friend who joins.
    /// </summary>
    public partial class NetSession
    {
        const byte MsgBlocks = 19;
        float blockTimer;
        readonly List<ConcreteBlock> blockSend = new();
        readonly Dictionary<ConcreteBlock, bool> blockWasMoving = new();

        static ConcreteBlock FindBlock(int id)
        {
            foreach (var b in ConcreteBlock.All) if (b != null && b.NetId == id) return b;
            return null;
        }

        void BlocksTick(float dt)
        {
            if ((blockTimer -= dt) > 0f) return;
            blockTimer = 0.1f;
            blockSend.Clear();
            bool anyDirty = false;
            foreach (var b in ConcreteBlock.All)
            {
                if (b == null || !b.NetMine) continue;
                bool moving = b.ChainedTo == null && !b.Body.isKinematic && !b.Body.IsSleeping() && b.Body.linearVelocity.sqrMagnitude > 1e-4f;
                bool was = blockWasMoving.TryGetValue(b, out bool w) && w;
                blockWasMoving[b] = moving;
                if (b.NetDirty || moving || was) { blockSend.Add(b); if (b.NetDirty || (was && !moving)) anyDirty = true; }
            }
            if (blockSend.Count == 0) return;
            SendBlockList(blockSend, anyDirty, null);
            foreach (var b in blockSend) b.NetDirty = false;
        }

        void SendBlockList(List<ConcreteBlock> list, bool reliable, CSteamID? to)
        {
            const int per = 40;
            for (int s = 0; s < list.Count; s += per)
            {
                int n = Mathf.Min(per, list.Count - s);
                var w = Begin(MsgBlocks);
                w.Write(n);
                for (int i = 0; i < n; i++) WriteBlock(w, list[s + i]);
                if (to.HasValue) Send(to.Value, w, true); else Send(w, reliable, null);
            }
        }

        static void WriteBlock(BinaryWriter w, ConcreteBlock b)
        {
            w.Write(b.NetId); w.Write((byte)b.kind); w.Write(b.NetSeq);
            byte flags = 0;
            int carrier = 0; byte carrierKind = 0;
            if (b.ChainedTo != null)
            {
                var tr = b.ChainedTo.GetComponentInParent<Trailer>();
                var v = b.ChainedTo.GetComponentInParent<VehicleBase>();
                if (tr != null && tr.NetKey != 0) { carrierKind = 1; carrier = tr.NetKey; }
                else if (v != null && v.NetId != 0) { carrierKind = 2; carrier = v.NetId; }
                if (carrierKind != 0) flags |= 1;
            }
            int hookV = 0;
            if (b.HookedBy != null) { var ev = b.HookedBy.GetComponent<VehicleBase>(); if (ev != null && ev.NetId != 0) { hookV = ev.NetId; flags |= 2; } }
            w.Write(flags);
            WriteV3(w, b.transform.position); WriteQ(w, b.transform.rotation);
            if ((flags & 1) != 0) { w.Write(carrierKind); w.Write(carrier); WriteV3(w, b.transform.localPosition); WriteQ(w, b.transform.localRotation); }
            if ((flags & 2) != 0) w.Write(hookV);
        }

        void ReadBlocks(CSteamID from, BinaryReader r)
        {
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                int id = r.ReadInt32(); int kind = r.ReadByte(); int seq = r.ReadInt32();
                byte flags = r.ReadByte();
                Vector3 p = ReadV3(r); Quaternion q = ReadQ(r);
                byte ck = 0; int carrier = 0; Vector3 lp = default; Quaternion lq = Quaternion.identity;
                if ((flags & 1) != 0) { ck = r.ReadByte(); carrier = r.ReadInt32(); lp = ReadV3(r); lq = ReadQ(r); }
                int hookV = (flags & 2) != 0 ? r.ReadInt32() : 0;

                var b = FindBlock(id);
                if (b == null)
                {
                    b = BlockYard.Spawn(kind, p, q, id);
                    if (b == null) continue;
                    b.NetSetRemote(seq);
                }
                else
                {
                    if (seq < b.NetSeq) continue;              // an old update from whoever ran it before
                    if (b.NetMine && seq == b.NetSeq) continue; // (we took it at the same moment: ours stands)
                    if (b.NetMine || seq > b.NetSeq) b.NetSetRemote(seq);
                }
                // chained down to a load: it rides there
                Transform deck = null;
                if (ck == 1) { var t = FindTrailer(carrier); if (t != null) deck = t.tiltDeck != null ? t.tiltDeck : t.transform; }
                else if (ck == 2) { var v = Find(carrier); if (v != null) deck = ConcreteBlock.DeckOf(v); }
                if (deck != null)
                {
                    if (b.ChainedTo != deck) { if (b.ChainedTo != null) b.Unchain(true); b.ChainTo(deck, true); }
                    b.transform.SetLocalPositionAndRotation(lp, lq);
                }
                else
                {
                    if (b.ChainedTo != null) b.Unchain(true);
                    b.NetTarget(p, q);
                }
                // on a friend's chain
                var chain = hookV != 0 ? Find(hookV)?.GetComponent<BucketChain>() : null;
                if (chain != null) { if (chain.Held != b) { if (chain.Held != null) chain.Release(true); chain.Hook(b, true); } }
                else if (b.HookedBy != null) b.HookedBy.Release(true);
            }
        }

        /// <summary>A friend joined: the blocks we run (the host: every block, so none go missing).</summary>
        void SendBlocksTo(CSteamID to)
        {
            blockSend.Clear();
            foreach (var b in ConcreteBlock.All) if (b != null && (b.NetMine || IsHost)) blockSend.Add(b);
            if (blockSend.Count > 0) SendBlockList(blockSend, true, to);
        }
    }
}
