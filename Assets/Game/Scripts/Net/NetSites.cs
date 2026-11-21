using System.IO;
using FPSBase.City;
using Steamworks;
using UnityEngine;

namespace FPSBase.Net
{
    /// <summary>
    /// House building in multiplayer. Every game sends its construction sites (where each one is, its stage, the
    /// concrete square by square, the house's progress, sold or not) when something changes, at most once a second;
    /// a friend's game merges them by the site's id: sites it hasn't got appear, given-up ones go, and each is as
    /// far on as either game has it. A friend who joins gets them all.
    /// </summary>
    public partial class NetSession
    {
        const byte MsgSites = 20;
        float sitesTimer;
        int sitesSig;

        void SitesTick(float dt)
        {
            var con = Construction.Instance;
            if (con == null || (sitesTimer -= dt) > 0f) return;
            sitesTimer = 1f;
            int sig = con.NetSignature();
            if (!con.NetDirty && sig == sitesSig) return;
            con.NetDirty = false; sitesSig = sig;
            var w = Begin(MsgSites);
            con.WriteNet(w);
            Send(w, true, null);
        }

        void SendSitesTo(CSteamID to)
        {
            var con = Construction.Instance;
            if (con == null) return;
            var w = Begin(MsgSites);
            con.WriteNet(w);
            Send(to, w, true);
        }

        void ReadSites(BinaryReader r)
        {
            var con = Construction.Instance;
            if (con == null) return;
            try { con.ReadNet(r); }
            catch (System.Exception e) { Debug.LogWarning($"[Net] couldn't read a friend's building sites: {e.Message}"); }
            // (what we merged in changes our fingerprint: no need to send it straight back)
            sitesSig = con.NetSignature();
        }
    }
}
