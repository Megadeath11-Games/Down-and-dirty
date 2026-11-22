using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using FPSBase.Core;
using FPSBase.Mining;
using FPSBase.Vehicles;
using Steamworks;
using UnityEngine;

namespace FPSBase.Net
{
    /// <summary>
    /// Steam multiplayer (Steam test app 480 "Spacewar"). One player hosts a friends-only Steam lobby and invites
    /// friends from the in-game list (or the Steam overlay); a friend who is running the game joins from the same
    /// list, or by accepting the Steam invite. Messages go peer to peer through Steam's relay network.
    ///
    /// What is shared:
    ///  * players: where everyone is and which machine they sit in (shown as a mannequin with a name tag);
    ///  * machines: whoever drives a machine runs it on their game and sends its position, arm / blade / box pose,
    ///    load and cab controls; it stays theirs until someone else gets in. Machines bought at the dealer appear
    ///    for everyone;
    ///  * the ground: every dug / filled / pushed cell is sent, and a player joining gets the whole worked site;
    ///  * dirt poured into a truck another player runs is added on their game.
    /// Each player keeps their own money.
    /// </summary>
    public partial class NetSession : MonoBehaviour
    {
        public const uint AppId = 480;
        const string LobbyTag = "fpsbase_goldrush";
        const int MaxPlayers = 4;

        public static NetSession Instance { get; private set; }
        public static bool InSession => Instance != null && (Instance.lan != null ? Instance.lanEntered && Instance.lan.Connected : Instance.lobby.IsValid() && Instance.lobby.m_SteamID != 0);

        // the local test link ("-nettest host|join <port>"): two copies of the game on one PC, no Steam
        LanLink lan;
        bool lanEntered;
        static string lanRole; static int lanPort = 27960;
        /// <summary>Playing over the local test link instead of Steam.</summary>
        public static bool LanMode => lanParsed ? lanRole != null : ParseLan();
        public static string LanRole => LanMode ? lanRole : null;
        static bool lanParsed;
        static bool ParseLan()
        {
            lanParsed = true;
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-nettest");
            if (i < 0 || i + 1 >= args.Length) return false;
            lanRole = args[i + 1];
            if (i + 2 < args.Length && int.TryParse(args[i + 2], out int port)) lanPort = port;
            return true;
        }
        // the host is whoever ran the game when we came in. (Steam hands the lobby to someone else if the host
        // leaves, but their copy of the world isn't the real one: the game is over then, for everyone.)
        CSteamID HostSteam => lan != null ? new CSteamID(LanLink.HostId) : hostAtStart.IsValid() ? hostAtStart : SteamMatchmaking.GetLobbyOwner(lobby);
        CSteamID hostAtStart = CSteamID.Nil;
        /// <summary>Everyone in the game, you included.</summary>
        List<CSteamID> MemberList()
        {
            var l = new List<CSteamID>();
            if (lan != null) { foreach (var id in lan.Members) l.Add(new CSteamID(id)); if (!l.Contains(me)) l.Add(me); return l; }
            int n = SteamMatchmaking.GetNumLobbyMembers(lobby);
            for (int i = 0; i < n; i++) l.Add(SteamMatchmaking.GetLobbyMemberByIndex(lobby, i));
            return l;
        }
        string NameOf(CSteamID p) => LanMode ? $"Player {p.m_SteamID}" : SteamFriends.GetFriendPersonaName(p);

        public bool SteamOk { get; private set; }
        public string Status { get; private set; } = "Steam not started";
        CSteamID lobby = CSteamID.Nil;
        CSteamID me;
        public bool IsHost => InSession && HostSteam == me;
        readonly List<CSteamID> peers = new();
        readonly List<CSteamID> pendingInvites = new();
        bool creating;

        // ------------------------------------------------------------------ start up

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            bool lanTest = ParseLan();
            if (Instance != null || (Application.isBatchMode && !lanTest)) return; // the automated tests run without Steam
            if (FindFirstObjectByType<DigSite>() == null) return;
            new GameObject("Net Session").AddComponent<NetSession>();
        }

        Callback<GameLobbyJoinRequested_t> cbJoinReq;
        Callback<GameRichPresenceJoinRequested_t> cbRichJoin;
        Callback<LobbyCreated_t> cbCreated;
        Callback<LobbyEnter_t> cbEnter;
        Callback<LobbyChatUpdate_t> cbChat;
        Callback<SteamNetworkingMessagesSessionRequest_t> cbSession;

        void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // survives "quit to main menu" (a scene reload): Steam stays up
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneReloaded;
            AssignSceneIds();
            VehicleBase.NetEntered = v => { if (InSession) SendClaim(v, true); };
            VehicleBase.NetExited = v => { if (InSession) SendClaim(v, false); };
            VehicleBase.NetForwardLoad = ForwardLoad;
            DirtPile.NetChanged = p => { if (InSession && p != null) dirtyPiles[p.NetId] = (p.transform.position, p.load); };
            WashPlant.NetForwardLoad = (p, l) =>
            {
                if (!InSession) return;
                var w = Begin(MsgPlantLoad); w.Write(PlantIndex(p)); NetIO.Write(w, l);
                Send(HostSteam, w, true);
            };
            FPSBase.City.ConcreteBlock.NetRemoved = SendBlocksGone;
            Trailer.NetDeckChanged = t =>
            {
                if (!InSession || t == null || t.NetKey == 0) return;
                var w = Begin(MsgTrailerDeck); w.Write(t.NetKey); w.Write(t.TiltTarget); w.Write(t.RampTarget);
                Send(w, true, null); // to everyone: their decks start moving now, and whoever runs it keeps it
            };
            WashPlant.NetAction = (p, a) =>
            {
                if (!InSession) return;
                var w = Begin(MsgPlantAction); w.Write(PlantIndex(p)); w.Write(a);
                Send(HostSteam, w, true);
            };
            if (LanMode)
            {
                SteamOk = true;
                Status = $"Local test link ({lanRole}, port {lanPort})";
                return;
            }
            try
            {
                if (!Packsize.Test()) { Status = "Steamworks.NET was built for the wrong platform"; return; }
                SteamOk = SteamAPI.Init();
            }
            catch (Exception e) { SteamOk = false; Debug.LogWarning("Steam init failed: " + e.Message); }
            if (!SteamOk) { Status = "Steam isn't running (start Steam and log in to play together)"; return; }
            me = SteamUser.GetSteamID();
            Status = "Not in a game. Host one, or join a friend";
            SteamNetworkingUtils.InitRelayNetworkAccess();
            SetGlobal(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendBufferSize, 8 * 1024 * 1024);
            SetGlobal(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMin, 256 * 1024);
            SetGlobal(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMax, 4 * 1024 * 1024);
            cbJoinReq = Callback<GameLobbyJoinRequested_t>.Create(e => Join(e.m_steamIDLobby));
            cbRichJoin = Callback<GameRichPresenceJoinRequested_t>.Create(e => TryJoinFromConnect(e.m_rgchConnect));
            cbCreated = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
            cbEnter = Callback<LobbyEnter_t>.Create(OnLobbyEnter);
            cbChat = Callback<LobbyChatUpdate_t>.Create(e => RefreshPeers(true));
            cbSession = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(OnSessionRequest);
            // started by accepting an invite while the game was closed: "+connect_lobby <id>"
            TryJoinFromConnect(string.Join(" ", Environment.GetCommandLineArgs()));
        }

        static void SetGlobal(ESteamNetworkingConfigValue v, int value)
        {
            var h = GCHandle.Alloc(value, GCHandleType.Pinned);
            try { SteamNetworkingUtils.SetConfigValue(v, ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global, IntPtr.Zero, ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32, h.AddrOfPinnedObject()); }
            finally { h.Free(); }
        }

        void TryJoinFromConnect(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            int i = s.IndexOf("+connect_lobby", StringComparison.Ordinal);
            if (i < 0) return;
            var parts = s.Substring(i).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1 && ulong.TryParse(parts[1], out ulong id)) Join(new CSteamID(id));
        }

        void OnSceneReloaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
        {
            // a fresh copy of the world: forget the old one's machines and players
            owner.Clear(); spawnedListing.Clear(); vTarget.Clear(); vMotion.Clear(); snaps.Clear(); clockOffset.Clear();
            foreach (var a in avatars.Values) if (a.root != null) Destroy(a.root);
            avatars.Clear();
            localPlayer = null;
            AssignSceneIds();
            if (pendingJoin.IsValid()) { var j = pendingJoin; pendingJoin = CSteamID.Nil; Join(j); }
            if (lanJoinAfterReload) { lanJoinAfterReload = false; LanStart(false); }
        }

        void OnDestroy()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneReloaded;
            if (Instance == this) Instance = null;
            if (LanMode) { Leave(false); return; }
            if (!SteamOk) return;
            Leave(false);
            SteamAPI.Shutdown();
        }

        void OnApplicationQuit() { quitting = true; if (SteamOk && InSession) Leave(false); }
        bool quitting;

        /// <summary>Local test link: start hosting, or connect to the host on this PC.</summary>
        void LanStart(bool host)
        {
            try
            {
                lan = host ? LanLink.Host(lanPort) : LanLink.Join(lanPort);
                me = new CSteamID(lan.Me);
                if (host) { lanEntered = true; Entered(); }
                else Status = "Joining…"; // in when the host's player list comes
            }
            catch (Exception e) { lan = null; Status = "Couldn't " + (host ? "host" : "join") + ": " + e.Message; }
        }

        /// <summary>The local link's news: who's in the game.</summary>
        void LanPump()
        {
            if (lan == null || !lan.TakeRoster()) return;
            if (!lanEntered && !lan.Lost) { lanEntered = true; Entered(); return; }
            if (lan.Lost && !lan.IsHost) { HostGone(); return; }
            RefreshPeers(true);
        }

        /// <summary>Join the local test game (the host on this PC).</summary>
        public void JoinLan()
        {
            FPSBase.UI.MenuScene.Stop(); // (the machines behind the menu packed up and their ground put back before any is sent)
            if (!LanMode || InSession || lan != null) return;
            if (GameUI.Instance != null && !GameUI.Instance.InMainMenu) { lanJoinAfterReload = true; GameUI.Instance.ReturnToMainMenu("Joining…"); return; }
            LanStart(false);
        }
        bool lanJoinAfterReload;

        // ------------------------------------------------------------------ lobby

        public void Host()
        {
            FPSBase.UI.MenuScene.Stop(); // (the machines behind the menu packed up and their ground put back before any is sent)
            if (LanMode) { if (!InSession && lan == null) LanStart(true); return; }
            if (!SteamOk || InSession || creating) return;
            creating = true;
            Status = "Creating a game…";
            SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, MaxPlayers);
        }

        void OnLobbyCreated(LobbyCreated_t e)
        {
            creating = false;
            if (e.m_eResult != EResult.k_EResultOK) { Status = $"Couldn't create the game ({e.m_eResult})"; return; }
            var id = new CSteamID(e.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(id, "game", LobbyTag);
            SteamMatchmaking.SetLobbyData(id, "host", SteamFriends.GetPersonaName());
        }

        CSteamID pendingJoin = CSteamID.Nil;

        public void Join(CSteamID id)
        {
            FPSBase.UI.MenuScene.Stop(); // (the machines behind the menu packed up and their ground put back before any is sent)
            if (!SteamOk || !id.IsValid()) return;
            if (InSession && id == lobby) return;
            // in a world (yours, or another friend's): close it first, so you get the host's world as it is
            if (GameUI.Instance != null && !GameUI.Instance.InMainMenu)
            {
                if (InSession) Leave(false);
                pendingJoin = id;
                GameUI.Instance.ReturnToMainMenu("Joining your friend's game…");
                return;
            }
            if (InSession) Leave(false);
            Status = "Joining…";
            SteamMatchmaking.JoinLobby(id);
        }

        void OnLobbyEnter(LobbyEnter_t e)
        {
            if (e.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            { Status = "Couldn't join (the game is full or closed)"; return; }
            lobby = new CSteamID(e.m_ulSteamIDLobby);
            SteamFriends.SetRichPresence("connect", "+connect_lobby " + lobby.m_SteamID);
            SteamFriends.SetRichPresence("status", "Mining for gold");
            Entered();
        }

        /// <summary>In a game (hosting it, or joined a friend's).</summary>
        void Entered()
        {
            hostAtStart = CSteamID.Nil;
            hostAtStart = HostSteam;
            RefreshPeers(false);
            if (IsHost)
            {
                Status = "Hosting. Invite friends below";
                GameUI.Toast("Multiplayer game started. Invite a friend (F2)");
                // loaded in: straight away ask who to invite (not when the host already picked a friend to invite)
                if (lan == null && pendingInvites.Count == 0 && GameUI.Instance != null) GameUI.Instance.OfferInvites();
                if (lan == null) foreach (var f in pendingInvites) SteamMatchmaking.InviteUserToLobby(lobby, f);
                pendingInvites.Clear();
            }
            else
            {
                Status = $"Playing in {NameOf(HostSteam)}'s game";
                GameUI.Toast(Status);
                // the host's game runs every machine that was there at the start; ours are just copies now
                var host = HostSteam;
                foreach (var v in VehicleBase.All)
                    if (v.NetId > 0 && v.NetId < SpawnIdBase && !v.Occupied) { v.SetNetRemote(true); owner[v.NetId] = host; }
                var w = Begin(MsgHello);
                Send(host, w, true);
                // machines we bought before joining, and the one we're sitting in
                foreach (var kv in spawnedListing) SendSpawn(kv.Key, kv.Value, null);
                foreach (var v in VehicleBase.All) if (v.Occupied) SendClaim(v, true);
            }
        }

        /// <summary>
        /// Leave the game (the host: close it). A friend goes back to the main menu: what they had was a copy of
        /// the host's world, and joining again starts from a fresh one. `toMenu` false: quitting.
        /// </summary>
        public void Leave() => Leave(true);
        public void Leave(bool toMenu)
        {
            if (!InSession) return;
            bool wasClient = !IsHost;
            Send(Begin(MsgBye), true, null);
            if (lan != null) { lan.Dispose(); lan = null; lanEntered = false; }
            else
            {
                foreach (var p in peers) CloseSession(p);
                SteamMatchmaking.LeaveLobby(lobby);
                SteamFriends.ClearRichPresence();
            }
            lobby = CSteamID.Nil;
            hostAtStart = CSteamID.Nil;
            backlog.Clear();
            peers.Clear();
            foreach (var a in avatars.Values) if (a.root != null) Destroy(a.root);
            avatars.Clear();
            // every machine runs on this game again
            foreach (var v in VehicleBase.All) v.SetNetRemote(false);
            owner.Clear();
            Status = "Left the game";
            if (wasClient && toMenu && GameUI.Instance != null) GameUI.Instance.ReturnToMainMenu("You left your friend's game.");
        }

        void RefreshPeers(bool announce)
        {
            if (!InSession) return;
            var now = new List<CSteamID>();
            foreach (var m in MemberList()) if (m != me) now.Add(m);
            foreach (var p in now)
                if (!peers.Contains(p) && announce) GameUI.Toast($"{NameOf(p)} joined the game");
            foreach (var p in peers)
                if (!now.Contains(p)) PeerLeft(p);
            peers.Clear(); peers.AddRange(now);
            // the host left: the game is over for us
            // (the local test link's player numbers aren't Steam ids, so only Steam's are checked for being real)
            if (!IsHost && ((lan == null && !HostSteam.IsValid()) || !peers.Contains(HostSteam))) HostGone();
        }

        /// <summary>The host closed the game (or the connection to them broke): back to the main menu.</summary>
        void HostGone()
        {
            Debug.Log($"[Net] host gone: me {me.m_SteamID}, host {HostSteam.m_SteamID}, peers [{string.Join(",", peers.ConvertAll(x => x.m_SteamID.ToString()))}], members [{string.Join(",", MemberList().ConvertAll(x => x.m_SteamID.ToString()))}], lost {lan?.Lost}");
            Leave(false);
            Status = "The host closed the game";
            if (!quitting && GameUI.Instance != null) GameUI.Instance.ReturnToMainMenu("The host closed the game.");
        }

        void PeerLeft(CSteamID p)
        {
            GameUI.Toast($"{NameOf(p)} left the game");
            if (avatars.TryGetValue(p.m_SteamID, out var a)) { if (a.root != null) Destroy(a.root); avatars.Remove(p.m_SteamID); }
            // their machines: the host's game runs them now (everyone else follows the host's)
            foreach (var v in VehicleBase.All)
                if (owner.TryGetValue(v.NetId, out var o) && o == p)
                {
                    if (IsHost) { v.SetNetRemote(false); owner.Remove(v.NetId); }
                    else { owner[v.NetId] = HostSteam; v.NetRemoteDriven = false; }
                }
            // and the trailers they had parked
            foreach (var t in Trailer.All)
                if (t != null && t.NetHeldBy == p.m_SteamID)
                {
                    if (IsHost) { t.NetHeldBy = 0; t.NetMine = true; }
                    else t.NetHeldBy = HostSteam.m_SteamID;
                }
            CloseSession(p);
        }

        public void Invite(CSteamID friend)
        {
            if (!SteamOk) return;
            if (!InSession) { pendingInvites.Add(friend); Host(); GameUI.Toast($"Starting a game and inviting {NameOf(friend)}…"); return; }
            SteamMatchmaking.InviteUserToLobby(lobby, friend);
            GameUI.Toast($"Invite sent to {NameOf(friend)}");
        }

        public void OverlayInvite()
        {
            if (!SteamOk) return;
            if (!InSession) { Host(); return; }
            SteamFriends.ActivateGameOverlayInviteDialog(lobby);
        }

        void OnSessionRequest(SteamNetworkingMessagesSessionRequest_t e)
        {
            var who = e.m_identityRemote.GetSteamID();
            if (InSession && IsMember(who)) SteamNetworkingMessages.AcceptSessionWithUser(ref e.m_identityRemote);
        }

        bool IsMember(CSteamID who) => MemberList().Contains(who);

        static void CloseSession(CSteamID p)
        {
            if (LanMode) return;
            var id = new SteamNetworkingIdentity(); id.SetSteamID(p);
            SteamNetworkingMessages.CloseSessionWithUser(ref id);
        }

        // ------------------------------------------------------------------ messages

        const byte MsgHello = 1, MsgPlayer = 2, MsgVehicles = 3, MsgClaim = 4, MsgSpawn = 5, MsgTerrain = 6, MsgLoad = 7, MsgBye = 8,
            MsgPlant = 9, MsgPlantLoad = 10, MsgPlantAction = 11, MsgPlantGold = 12, MsgTrailers = 13,
            MsgJobs = 14, MsgJobAction = 15, MsgJobPay = 16, MsgPiles = 17, MsgTrailerDeck = 18;
        const int ChReliable = 0, ChFast = 1;

        static BinaryWriter Begin(byte type)
        {
            var w = new BinaryWriter(new MemoryStream(256));
            w.Write(type);
            return w;
        }

        // big reliable messages wait here when Steam's send buffer is full
        readonly Dictionary<ulong, Queue<byte[]>> backlog = new();

        void Send(CSteamID to, BinaryWriter w, bool reliable)
        {
            var ms = (MemoryStream)w.BaseStream;
            SendBytes(to, ms.GetBuffer(), (int)ms.Length, reliable);
        }

        /// <summary>Send to everyone in the game (except `skip`).</summary>
        void Send(BinaryWriter w, bool reliable, CSteamID? skip)
        {
            var ms = (MemoryStream)w.BaseStream;
            foreach (var p in peers) if (skip == null || p != skip.Value) SendBytes(p, ms.GetBuffer(), (int)ms.Length, reliable);
        }

        void SendBytes(CSteamID to, byte[] data, int len, bool reliable)
        {
            if (reliable && backlog.TryGetValue(to.m_SteamID, out var q) && q.Count > 0)
            {
                var copy = new byte[len]; Buffer.BlockCopy(data, 0, copy, 0, len); q.Enqueue(copy); return;
            }
            var r = RawSend(to, data, len, reliable);
            if (reliable && (r == EResult.k_EResultLimitExceeded || r == EResult.k_EResultRateLimitExceeded))
            {
                if (!backlog.TryGetValue(to.m_SteamID, out q)) backlog[to.m_SteamID] = q = new Queue<byte[]>();
                var copy = new byte[len]; Buffer.BlockCopy(data, 0, copy, 0, len); q.Enqueue(copy);
            }
        }

        EResult RawSend(CSteamID to, byte[] data, int len, bool reliable)
        {
            if (lan != null) return lan.Send(to.m_SteamID, data, len, reliable) ? EResult.k_EResultOK : EResult.k_EResultFail;
            var id = new SteamNetworkingIdentity(); id.SetSteamID(to);
            var h = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                int flags = (reliable ? Constants.k_nSteamNetworkingSend_Reliable : Constants.k_nSteamNetworkingSend_Unreliable)
                            | Constants.k_nSteamNetworkingSend_AutoRestartBrokenSession;
                return SteamNetworkingMessages.SendMessageToUser(ref id, h.AddrOfPinnedObject(), (uint)len, flags, reliable ? ChReliable : ChFast);
            }
            finally { h.Free(); }
        }

        void FlushBacklog()
        {
            foreach (var kv in backlog)
            {
                var q = kv.Value;
                while (q.Count > 0)
                {
                    var b = q.Peek();
                    var r = RawSend(new CSteamID(kv.Key), b, b.Length, true);
                    if (r == EResult.k_EResultLimitExceeded || r == EResult.k_EResultRateLimitExceeded) break;
                    q.Dequeue();
                }
            }
        }

        readonly IntPtr[] inbox = new IntPtr[64];

        void Receive()
        {
            if (lan != null)
            {
                int guard = 0;
                while (guard++ < 4000 && lan != null && lan.TryReceive(out ulong fromId, out byte[] bytes))
                {
                    var from = new CSteamID(fromId);
                    if (!InSession || !IsMember(from)) continue;
                    try { Handle(from, new BinaryReader(new MemoryStream(bytes))); }
                    catch (Exception e) { Debug.LogWarning($"Bad network message from {from}: {e}"); }
                }
                return;
            }
            for (int ch = 0; ch < 2; ch++)
            {
                int n, guard = 0;
                while ((n = SteamNetworkingMessages.ReceiveMessagesOnChannel(ch, inbox, inbox.Length)) > 0 && guard++ < 50)
                    for (int i = 0; i < n; i++)
                    {
                        var m = SteamNetworkingMessage_t.FromIntPtr(inbox[i]);
                        var data = new byte[m.m_cbSize];
                        Marshal.Copy(m.m_pData, data, 0, m.m_cbSize);
                        var from = m.m_identityPeer.GetSteamID();
                        SteamNetworkingMessage_t.Release(inbox[i]);
                        if (!InSession || !IsMember(from)) continue;
                        try { Handle(from, new BinaryReader(new MemoryStream(data))); }
                        catch (Exception e) { Debug.LogWarning($"Bad network message from {from}: {e.Message}"); }
                    }
            }
        }

        void Handle(CSteamID from, BinaryReader r)
        {
            byte type = r.ReadByte();
            switch (type)
            {
                case MsgHello: SendWorldTo(from); break;
                case MsgPlayer: ReadPlayer(from, r); break;
                case MsgVehicles: ReadVehicles(from, r); break;
                case MsgClaim: ReadClaim(from, r); break;
                case MsgSpawn: ReadSpawn(from, r); break;
                case MsgTerrain: ReadTerrain(r); break;
                case MsgLoad: ReadLoad(r); break;
                case MsgPlant: { var p = Plant(r.ReadInt32()); if (p != null && !IsHost) p.NetRead(r); break; }
                case MsgPlantLoad: { var p = Plant(r.ReadInt32()); var l = NetIO.ReadLoad(r); if (p != null && IsHost) { p.Receive(l); plantTimer = 0f; } break; }
                case MsgPlantAction:
                {
                    var p = Plant(r.ReadInt32()); int a = r.ReadInt32();
                    if (p == null || !IsHost) break;
                    float g = p.ApplyNetAction(a);
                    if (a == 0) { var w = Begin(MsgPlantGold); w.Write(g); Send(from, w, true); }
                    plantTimer = 0f;
                    break;
                }
                case MsgPlantGold:
                {
                    float g = r.ReadSingle();
                    Economy.Instance?.AddGold(g);
                    GameUI.Toast($"Cleaned up {g:0.00} g of gold");
                    break;
                }
                case MsgTrailers: ReadTrailers(from, r); break;
                case MsgTrailerDeck: { var t = FindTrailer(r.ReadInt32()); float tt = r.ReadSingle(), rt = r.ReadSingle(); t?.NetApplyDeck(tt, rt); break; }
                case MsgJobs: if (!IsHost && FPSBase.City.Jobs.Instance != null) FPSBase.City.Jobs.Instance.Read(r, true); break;
                case MsgJobAction:
                {
                    int id = r.ReadInt32(), a = r.ReadInt32(); bool shared = r.ReadBoolean(); string arg = r.ReadString();
                    if (IsHost && FPSBase.City.Jobs.Instance != null)
                        FPSBase.City.Jobs.Instance.ApplyAction(id, a, shared, from.m_SteamID, NameOf(from), arg == "" ? null : arg);
                    break;
                }
                case MsgJobPay:
                {
                    float amt = r.ReadSingle(); string why = r.ReadString();
                    Economy.Instance?.Earn(amt);
                    GameUI.Toast($"You got ${amt:N0} for {why}");
                    break;
                }
                case MsgPiles:
                {
                    int n = r.ReadInt32();
                    for (int i = 0; i < n; i++) { int id = r.ReadInt32(); Vector3 p = ReadV3(r); var l = NetIO.ReadLoad(r); DirtPile.NetApply(id, p, l); }
                    break;
                }
                case MsgBlocks: ReadBlocks(from, r); break;
                case MsgSites: ReadSites(r); break;
                case MsgChat: ReadChat(from, r); break;
                case MsgBye: break;
            }
        }

        // ------------------------------------------------------------------ the loop

        float playerTimer, vehicleTimer, slowTimer, terrainTimer, plantTimer;

        void Update()
        {
            if (!SteamOk) return;
            bool mirror = InSession && !IsHost; // (back to running its own plant once you leave)
            foreach (var p in WashPlant.All) if (p != null) p.NetMirror = mirror;
            if (!InSession) foreach (var t in Trailer.All) if (t != null && t.NetRemote) { t.NetHeldBy = 0; t.NetMine = true; t.NetSetRemote(false); }
            if (lan == null && !LanMode) SteamAPI.RunCallbacks(); else LanPump();
            if (!InSession) return;
            if (IsHost && (plantTimer -= Time.unscaledDeltaTime) <= 0f) { plantTimer = 0.25f; SendPlants(null); }
            Receive();
            FlushBacklog();
            float dt = Time.unscaledDeltaTime;
            if ((playerTimer -= dt) <= 0f) { playerTimer = 1f / 15f; SendPlayer(); }
            bool slow = (slowTimer -= dt) <= 0f;
            if (slow) slowTimer = 0.5f;
            TrailerOwnership();
            if ((vehicleTimer -= dt) <= 0f) { vehicleTimer = 1f / 15f; SendVehicles(slow); SendTrailers(slow, null); }
            if ((terrainTimer -= dt) <= 0f) { terrainTimer = 0.12f; SendTerrain(); SendPiles(null, false); }
            BlocksTick(dt);
            SitesTick(dt);
            ShowRemotes(Time.deltaTime);
        }

        // ------------------------------------------------------------------ machines

        public const int SpawnIdBase = 1_000_000;
        readonly Dictionary<int, CSteamID> owner = new();
        readonly Dictionary<VehicleBase, int> spawnedListing = new();
        readonly Dictionary<int, (Vector3 p, Quaternion q)> vTarget = new();
        // how fast each friend's machine is going (from its last two updates), to keep it moving between updates
        readonly Dictionary<int, (float t, Vector3 vel)> vMotion = new();

        // ---- friends' machines are drawn a moment behind, between the updates they sent (by the time on their
        // game), so a late or lost update doesn't make them stop and start
        const double InterpDelay = 0.13, MaxExtrapolate = 0.35;
        readonly Dictionary<int, List<(double t, Vector3 p, Quaternion q)>> snaps = new();
        // each friend's clock: our time minus theirs (the smallest seen: the quickest an update got here)
        readonly Dictionary<ulong, double> clockOffset = new();

        void TheirClock(CSteamID from, double sent)
        {
            double off = Time.unscaledTimeAsDouble - sent;
            if (!clockOffset.TryGetValue(from.m_SteamID, out double o) || off < o || off > o + 2.0) o = off;
            else o += (off - o) * 0.01; // (drifts up slowly if the connection gets slower)
            clockOffset[from.m_SteamID] = o;
        }

        void AddSnap(int id, double sent, Vector3 p, Quaternion q)
        {
            if (!snaps.TryGetValue(id, out var l)) snaps[id] = l = new List<(double, Vector3, Quaternion)>();
            if (l.Count > 0 && sent <= l[l.Count - 1].t) return; // (an older update, arrived late)
            l.Add((sent, p, q));
            while (l.Count > 40 || (l.Count > 2 && sent - l[0].t > 2.0)) l.RemoveAt(0);
        }

        /// <summary>Where a friend's machine is to be drawn now, from its updates. False: no updates to go by.</summary>
        bool Sample(int id, out Vector3 p, out Quaternion q)
        {
            p = default; q = Quaternion.identity;
            if (!snaps.TryGetValue(id, out var l) || l.Count == 0 || !owner.TryGetValue(id, out var o) || !clockOffset.TryGetValue(o.m_SteamID, out double off)) return false;
            double t = Time.unscaledTimeAsDouble - off - InterpDelay;
            if (t <= l[0].t || l.Count == 1) { p = l[0].p; q = l[0].q; return true; }
            for (int i = l.Count - 1; i > 0; i--)
            {
                if (t < l[i - 1].t) continue;
                var a = l[i - 1]; var b = l[i];
                if (t <= b.t)
                {
                    float k = (float)((t - a.t) / Math.Max(1e-4, b.t - a.t));
                    p = Vector3.Lerp(a.p, b.p, k); q = Quaternion.Slerp(a.q, b.q, k);
                    return true;
                }
                // past the newest update: carry on the way it was going, for a little while
                double dt = Math.Max(1e-3, b.t - a.t);
                float ahead = (float)Math.Min(t - b.t, MaxExtrapolate);
                Vector3 vel = (b.p - a.p) / (float)dt;
                if (vel.sqrMagnitude > 60f * 60f || b.t - a.t > 1.0) vel = Vector3.zero; // a jump, or it had stopped
                p = b.p + vel * ahead;
                q = b.q;
                return true;
            }
            p = l[0].p; q = l[0].q;
            return true;
        }

        void SetTarget(int id, Vector3 p, Quaternion q, bool track)
        {
            if (!track) snaps.Remove(id); // (claimed or spawned: start again from here)
            float now = Time.unscaledTime;
            Vector3 vel = Vector3.zero;
            if (track && vTarget.TryGetValue(id, out var old) && vMotion.TryGetValue(id, out var m))
            {
                float dt = now - m.t;
                if (dt > 0.02f && dt < 0.6f) vel = Vector3.Lerp(m.vel, (p - old.p) / dt, 0.6f);
                else if (dt <= 0.02f) vel = m.vel;
                if (vel.sqrMagnitude > 60f * 60f) vel = Vector3.zero; // a jump, not driving
            }
            vTarget[id] = (p, q);
            vMotion[id] = (now, vel);
        }

        /// <summary>The machines in the scene get the same ids on every player's game (sorted by name and where they start).</summary>
        public static void AssignSceneIds()
        {
            var list = new List<VehicleBase>(FindObjectsByType<VehicleBase>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            list.RemoveAll(v => v.NetId >= SpawnIdBase || v.GetComponent<BoughtFrom>() != null); // only the ones the scene starts with
            list.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(a.name, b.name);
                if (c != 0) return c;
                Vector3 pa = a.transform.position, pb = b.transform.position;
                c = Mathf.RoundToInt(pa.x * 10f).CompareTo(Mathf.RoundToInt(pb.x * 10f));
                return c != 0 ? c : Mathf.RoundToInt(pa.z * 10f).CompareTo(Mathf.RoundToInt(pb.z * 10f));
            });
            for (int i = 0; i < list.Count; i++) if (list[i].NetId == 0) list[i].NetId = i + 1;
            // the scene's own trailers (not a semi's lowboy, which goes by its semi; not bought ones)
            var tl = new List<Trailer>(FindObjectsByType<Trailer>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            tl.RemoveAll(t => t.NetId != 0 || t.transform.parent != null || t.GetComponent<BoughtFrom>() != null);
            tl.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(a.name, b.name);
                if (c != 0) return c;
                Vector3 pa = a.transform.position, pb = b.transform.position;
                c = Mathf.RoundToInt(pa.x * 10f).CompareTo(Mathf.RoundToInt(pb.x * 10f));
                return c != 0 ? c : Mathf.RoundToInt(pa.z * 10f).CompareTo(Mathf.RoundToInt(pb.z * 10f));
            });
            for (int i = 0; i < tl.Count; i++) tl[i].NetId = i + 1;
        }

        static Trailer FindTrailer(int key)
        {
            if (key == 0) return null;
            foreach (var t in Trailer.All) if (t != null && t.NetKey == key) return t;
            return null;
        }

        readonly Dictionary<Trailer, int> spawnedTrailers = new();

        /// <summary>
        /// Who runs each trailer: hitched, whoever runs the truck; parked, whoever used it last. A copy (someone
        /// else's) follows their updates.
        /// </summary>
        void TrailerOwnership()
        {
            foreach (var t in Trailer.All)
            {
                if (t == null || t.NetKey == 0) continue;
                var tr = t.Tractor;
                if (tr != null && !tr.NetRemote) { t.NetMine = true; t.NetHeldBy = 0; }
                bool remote = tr != null ? tr.NetRemote : t.NetHeldBy != 0;
                t.NetSetRemote(remote);
            }
        }

        void SendTrailers(bool slowTick, CSteamID? to)
        {
            var w = Begin(MsgTrailers);
            long countPos = w.BaseStream.Position;
            w.Write(0);
            int n = 0;
            foreach (var t in Trailer.All)
            {
                if (t == null || t.NetKey == 0 || t.NetRemote) continue;
                var tr = t.Tractor;
                bool ours = tr != null ? !tr.NetRemote : t.NetMine;
                if (!ours) continue;
                var rb = t.Body;
                bool moving = rb != null && !rb.isKinematic && rb.linearVelocity.sqrMagnitude > 0.01f;
                if (to == null && !moving && !slowTick) continue;
                w.Write(t.NetKey); w.Write(t.NetSeq);
                WriteV3(w, t.transform.position); WriteQ(w, t.transform.rotation);
                t.NetWriteState(w);
                n++;
            }
            if (n == 0) return;
            long end = w.BaseStream.Position;
            w.BaseStream.Position = countPos; w.Write(n); w.BaseStream.Position = end;
            if (to != null) Send(to.Value, w, true); else Send(w, false, null);
        }

        void ReadTrailers(CSteamID from, BinaryReader r)
        {
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                int key = r.ReadInt32(), seq = r.ReadInt32();
                Vector3 p = ReadV3(r); Quaternion q = ReadQ(r);
                var t = FindTrailer(key);
                if (t == null)
                {
                    // (unknown here: skip its state)
                    r.ReadInt32(); r.ReadSByte(); r.ReadBoolean(); r.ReadSingle(); r.ReadSingle(); r.ReadSingle();
                    continue;
                }
                var tr = t.Tractor;
                long statePos = r.BaseStream.Position;
                // what it's hitched to, by their account
                int vid = r.ReadInt32(); r.BaseStream.Position = statePos;
                var theirTruck = vid != 0 ? Find(vid) : null;
                bool onTheirTruck = theirTruck != null && theirTruck.NetRemote;
                bool accept;
                if (tr != null && !tr.NetRemote) accept = false;             // it's on our truck: ours
                else if (onTheirTruck || tr != null) accept = true;           // on their truck (or one they run)
                else accept = seq > t.NetSeq || (seq == t.NetSeq && !t.NetMine) || t.NetHeldBy == from.m_SteamID;
                if (!accept) { r.ReadInt32(); r.ReadSByte(); r.ReadBoolean(); r.ReadSingle(); r.ReadSingle(); r.ReadSingle(); continue; }
                t.NetSeq = Mathf.Max(t.NetSeq, seq);
                t.NetHeldBy = from.m_SteamID; t.NetMine = false; // their game runs it
                t.NetSetRemote(true);
                t.NetTarget(p, q);
                t.NetReadState(r, Find);
            }
        }

        static VehicleBase Find(int id)
        {
            foreach (var v in VehicleBase.All) if (v.NetId == id) return v;
            return null;
        }

        /// <summary>A machine was bought at the dealer on this game.</summary>
        public static void NotifySpawn(GameObject go, int listing)
        {
            BoughtFrom.Mark(go, listing);
            var v = go.GetComponent<VehicleBase>();
            if (v == null)
            {
                var tl = go.GetComponent<Trailer>();
                if (tl == null) return;
                tl.NetId = SpawnIdBase + UnityEngine.Random.Range(1, int.MaxValue - SpawnIdBase - 1);
                tl.NetMine = true;
                if (Instance == null) return;
                Instance.spawnedTrailers[tl] = listing;
                if (InSession) Instance.SendSpawnTrailer(tl, listing, null);
                return;
            }
            v.NetId = SpawnIdBase + UnityEngine.Random.Range(1, int.MaxValue - SpawnIdBase - 1);
            if (Instance == null) return;
            Instance.spawnedListing[v] = listing;
            if (InSession) Instance.SendSpawn(v, listing, null);
        }

        void SendSpawn(VehicleBase v, int listing, CSteamID? to)
        {
            if (v == null) return;
            var w = Begin(MsgSpawn);
            w.Write(v.NetId); w.Write(listing); w.Write(v.name);
            WriteV3(w, v.transform.position); WriteQ(w, v.transform.rotation);
            w.Write(owner.TryGetValue(v.NetId, out var o) ? o.m_SteamID : me.m_SteamID);
            if (to.HasValue) Send(to.Value, w, true); else Send(w, true, null);
        }

        void SendSpawnTrailer(Trailer t, int listing, CSteamID? to)
        {
            if (t == null) return;
            var w = Begin(MsgSpawn);
            w.Write(t.NetId); w.Write(listing); w.Write(t.name);
            WriteV3(w, t.transform.position); WriteQ(w, t.transform.rotation);
            w.Write(t.NetRemote && t.NetHeldBy != 0 ? t.NetHeldBy : me.m_SteamID);
            if (to.HasValue) Send(to.Value, w, true); else Send(w, true, null);
        }

        void ReadSpawn(CSteamID from, BinaryReader r)
        {
            int id = r.ReadInt32(), listing = r.ReadInt32();
            string name = r.ReadString();
            Vector3 p = ReadV3(r); Quaternion q = ReadQ(r);
            var own = new CSteamID(r.ReadUInt64());
            if (Find(id) != null || FindTrailer(id) != null) return;
            var dealer = FindFirstObjectByType<VehicleDealer>();
            if (dealer == null || listing < 0 || listing >= dealer.listings.Length || dealer.listings[listing].prefab == null) return;
            var go = Instantiate(dealer.listings[listing].prefab, p, q);
            go.name = name;
            BoughtFrom.Mark(go, listing);
            if (go.GetComponent<VehicleBase>() == null && go.GetComponent<Trailer>() is Trailer tl)
            {
                tl.NetId = id;
                spawnedTrailers[tl] = listing;
                if (own == me) { tl.NetMine = true; return; }
                tl.NetHeldBy = own.m_SteamID; tl.NetMine = false;
                tl.NetSetRemote(true);
                tl.NetTarget(p, q);
                GameUI.Toast($"{NameOf(from)} bought a {name}");
                return;
            }
            var v = go.GetComponent<VehicleBase>();
            v.NetId = id;
            spawnedListing[v] = listing;
            if (own == me) return; // it was ours (we bought it, left and came back)
            owner[id] = own;
            v.SetNetRemote(true);
            SetTarget(id, p, q, false);
            GameUI.Toast($"{NameOf(from)} bought a {name}");
        }

        void SendClaim(VehicleBase v, bool driving)
        {
            owner.Remove(v.NetId);
            var w = Begin(MsgClaim);
            w.Write(v.NetId); w.Write(driving);
            WriteV3(w, v.transform.position); WriteQ(w, v.transform.rotation);
            v.NetWrite(w);
            Send(w, true, null);
        }

        void ReadClaim(CSteamID from, BinaryReader r)
        {
            int id = r.ReadInt32(); bool driving = r.ReadBoolean();
            Vector3 p = ReadV3(r); Quaternion q = ReadQ(r);
            var v = Find(id);
            if (v == null) return;
            // both got in at the same moment: the player with the lower Steam id keeps it
            if (v.Occupied && driving && me.m_SteamID < from.m_SteamID) { SendClaim(v, true); return; }
            // an old "it's mine" from before we got in: we're in it, so it stays ours
            if (v.Occupied && !driving) { SendClaim(v, true); return; }
            if (v.Occupied) GameUI.Toast($"{NameOf(from)} took over the {v.displayName}");
            owner[id] = from;
            v.SetNetRemote(true);
            v.NetRemoteDriven = driving;
            v.NetDriverName = NameOf(from);
            v.NetRead(r);
            SetTarget(id, p, q, false);
        }

        void SendVehicles(bool slowTick)
        {
            var w = Begin(MsgVehicles);
            w.Write(Time.unscaledTimeAsDouble);
            long countPos = w.BaseStream.Position;
            w.Write(0);
            int n = 0;
            foreach (var v in VehicleBase.All)
            {
                if (v.NetRemote || v.NetId == 0) continue;
                var rb = v.GetComponent<Rigidbody>();
                bool moving = v.Occupied || v.RidingTrailer || (rb != null && !rb.isKinematic && rb.linearVelocity.sqrMagnitude > 0.01f);
                if (!moving && !slowTick) continue;
                w.Write(v.NetId);
                w.Write((byte)((v.Occupied ? 1 : 0) | (v.RidingTrailer ? 2 : 0)));
                WriteV3(w, v.transform.position); WriteQ(w, v.transform.rotation);
                w.Write(rb != null && !rb.isKinematic ? Vector3.Dot(rb.linearVelocity, v.transform.forward) : 0f);
                var ms = new MemoryStream(); var bw = new BinaryWriter(ms);
                v.NetWrite(bw);
                w.Write((ushort)ms.Length); w.Write(ms.GetBuffer(), 0, (int)ms.Length);
                n++;
            }
            if (n == 0) return;
            long end = w.BaseStream.Position;
            w.BaseStream.Position = countPos; w.Write(n); w.BaseStream.Position = end;
            Send(w, false, null);
        }

        void ReadVehicles(CSteamID from, BinaryReader r)
        {
            double sent = r.ReadDouble();
            TheirClock(from, sent);
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                int id = r.ReadInt32(); byte flags = r.ReadByte(); bool driven = (flags & 1) != 0;
                Vector3 p = ReadV3(r); Quaternion q = ReadQ(r); float spd = r.ReadSingle();
                int len = r.ReadUInt16();
                byte[] blob = r.ReadBytes(len);
                var v = Find(id);
                // only the machine's owner says where it is (a late packet from a previous owner is ignored)
                if (v == null || !v.NetRemote || !owner.TryGetValue(id, out var o) || o != from) continue;
                v.NetRemoteDriven = driven;
                v.RidingTrailer = (flags & 2) != 0; // chained on their trailer: wheels and tracks hold still
                v.NetSpeed = spd;
                v.NetRead(new BinaryReader(new MemoryStream(blob)));
                SetTarget(id, p, q, true);
                AddSnap(id, sent, p, q);
            }
        }

        void ForwardLoad(VehicleBase v, MaterialLoad l)
        {
            if (!owner.TryGetValue(v.NetId, out var o)) return;
            var w = Begin(MsgLoad);
            w.Write(v.NetId); NetIO.Write(w, l);
            Send(o, w, true);
        }

        void ReadLoad(BinaryReader r)
        {
            int id = r.ReadInt32(); var l = NetIO.ReadLoad(r);
            if (Find(id) is IMaterialReceiver rec && !Find(id).NetRemote) rec.Receive(l);
        }

        // ------------------------------------------------------------------ players

        class Avatar
        {
            public GameObject root;
            public FPSBase.Player.MannequinRig rig;
            public Vector3 pos, shown; public float yaw, shownYaw, speed, crouch; public bool grounded = true;
            public int vehicle; public string name;
        }
        readonly Dictionary<ulong, Avatar> avatars = new();
        PlayerInteractor localPlayer;

        /// <summary>The other players: name, where they are (their machine when driving) and which way they face.</summary>
        public IEnumerable<(string name, Vector3 pos, float yaw)> OtherPlayers()
        {
            foreach (var a in avatars.Values)
            {
                if (a.root == null) continue;
                Vector3 pos = a.shown; float yaw = a.shownYaw;
                if (a.vehicle != 0) foreach (var v in VehicleBase.All) if (v != null && v.NetId == a.vehicle) { pos = v.transform.position; yaw = v.transform.eulerAngles.y; break; }
                yield return (a.name, pos, yaw);
            }
        }

        void SendPlayer()
        {
            if (localPlayer == null) localPlayer = FindFirstObjectByType<PlayerInteractor>(FindObjectsInactive.Include);
            if (localPlayer == null) return;
            var w = Begin(MsgPlayer);
            int inVehicle = 0;
            foreach (var v in VehicleBase.All) if (v.Occupied) inVehicle = v.NetId;
            var t = localPlayer.transform;
            WriteV3(w, t.position); w.Write(t.eulerAngles.y);
            var body = localPlayer.GetComponent<FPSBase.Player.PlayerBody>();
            w.Write(body != null && body.rig != null ? body.rig.speed : 0f);
            w.Write(body != null && body.rig != null ? body.rig.crouch : 0f);
            w.Write(body == null || body.rig == null || body.rig.grounded);
            w.Write(inVehicle);
            Send(w, false, null);
        }

        void ReadPlayer(CSteamID from, BinaryReader r)
        {
            if (!avatars.TryGetValue(from.m_SteamID, out var a))
            {
                a = MakeAvatar(NameOf(from));
                if (a == null) return;
                avatars[from.m_SteamID] = a;
            }
            a.pos = ReadV3(r); a.yaw = r.ReadSingle(); a.speed = r.ReadSingle(); a.crouch = r.ReadSingle(); a.grounded = r.ReadBoolean();
            a.vehicle = r.ReadInt32();
        }

        Avatar MakeAvatar(string who)
        {
            if (localPlayer == null) localPlayer = FindFirstObjectByType<PlayerInteractor>(FindObjectsInactive.Include);
            var body = localPlayer != null ? localPlayer.GetComponent<FPSBase.Player.PlayerBody>() : null;
            if (body == null || body.rig == null) return null;
            var root = new GameObject($"Player {who}");
            var copy = Instantiate(body.rig.gameObject, root.transform);
            copy.transform.localPosition = Vector3.zero; copy.transform.localRotation = Quaternion.identity;
            var rig = copy.GetComponent<FPSBase.Player.MannequinRig>();
            rig.CloneFrom(body.rig);
            rig.pose = FPSBase.Player.MannequinRig.Pose.Stand; rig.seat = null; rig.controls = null;
            foreach (var c in copy.GetComponentsInChildren<Collider>()) Destroy(c);
            return new Avatar { root = root, rig = rig, name = who };
        }

        void ShowRemotes(float dt)
        {
            float k = 1f - Mathf.Exp(-12f * dt), kFast = 1f - Mathf.Exp(-40f * dt);
            foreach (var kv in vTarget)
            {
                var v = Find(kv.Key);
                if (v == null || !v.NetRemote) continue;
                var (p, q) = kv.Value;
                if (Sample(kv.Key, out var sp, out var sq))
                {
                    // between its updates (already smooth): follow closely
                    if ((v.transform.position - sp).sqrMagnitude > 100f) v.transform.SetPositionAndRotation(sp, sq);
                    else v.transform.SetPositionAndRotation(Vector3.Lerp(v.transform.position, sp, kFast), Quaternion.Slerp(v.transform.rotation, sq, kFast));
                    continue;
                }
                if ((v.transform.position - p).sqrMagnitude > 100f) v.transform.SetPositionAndRotation(p, q);
                else v.transform.SetPositionAndRotation(Vector3.Lerp(v.transform.position, p, k), Quaternion.Slerp(v.transform.rotation, q, k));
            }
            foreach (var a in avatars.Values)
            {
                if (a.root == null) continue;
                var v = a.vehicle != 0 ? Find(a.vehicle) : null;
                var rig = a.rig;
                if (v != null)
                {
                    var eye = v.CabEye;
                    if (rig.pose != FPSBase.Player.MannequinRig.Pose.Sit || rig.seat != eye)
                    {
                        rig.transform.SetParent(eye != null && eye.parent != null ? eye.parent : v.transform, true);
                        rig.pose = FPSBase.Player.MannequinRig.Pose.Sit; rig.seat = eye; rig.controls = v.Controls3D;
                        rig.speed = 0f; rig.crouch = 0f; rig.grounded = true;
                    }
                    a.shown = a.pos;
                    continue;
                }
                if (rig.pose == FPSBase.Player.MannequinRig.Pose.Sit)
                {
                    rig.transform.SetParent(a.root.transform, false);
                    rig.transform.localPosition = Vector3.zero; rig.transform.localRotation = Quaternion.identity;
                    rig.pose = FPSBase.Player.MannequinRig.Pose.Stand; rig.seat = null; rig.controls = null;
                    a.shown = a.pos;
                }
                if ((a.shown - a.pos).sqrMagnitude > 64f) a.shown = a.pos;
                a.shown = Vector3.Lerp(a.shown, a.pos, k);
                a.shownYaw = Mathf.LerpAngle(a.shownYaw, a.yaw, k);
                a.root.transform.SetPositionAndRotation(a.shown, Quaternion.Euler(0f, a.shownYaw, 0f));
                rig.speed = Mathf.Lerp(rig.speed, a.speed, k); rig.crouch = a.crouch; rig.grounded = a.grounded;
            }
        }

        // ------------------------------------------------------------------ ground

        /// <summary>For the tests: time spent sending / reading the ground, and how much went.</summary>
        public static double TerrainSendMs, TerrainReadMs; public static long TerrainSentBytes, TerrainSentCells, TerrainReadCells;

        void SendTerrain()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var sites = DigSite.Ordered();
            for (int k = 0; k < sites.Count; k++)
            {
                var cells = sites[k].TakeChangedCells(false);
                if (cells.Count > 0) SendCells(k, cells, null);
            }
            TerrainSendMs += sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>Changed ground cells of ground number `k` (<see cref="DigSite.Ordered"/>).</summary>
        void SendCells(int k, List<int> cells, CSteamID? to)
        {
            var site = DigSite.Ordered()[k];
            const int per = 6000;
            for (int s = 0; s < cells.Count; s += per)
            {
                int n = Mathf.Min(per, cells.Count - s);
                var raw = new MemoryStream(n * DigSite.NetCellBytes);
                var rw = new BinaryWriter(raw);
                for (int i = 0; i < n; i++) site.WriteCell(rw, cells[s + i]);
                var packed = new MemoryStream();
                using (var z = new DeflateStream(packed, System.IO.Compression.CompressionLevel.Fastest, true)) z.Write(raw.GetBuffer(), 0, (int)raw.Length);
                TerrainSentBytes += packed.Length; TerrainSentCells += n;
                var w = Begin(MsgTerrain);
                w.Write(k); w.Write(n); w.Write((int)packed.Length); w.Write(packed.GetBuffer(), 0, (int)packed.Length);
                if (to.HasValue) Send(to.Value, w, true); else Send(w, true, null);
            }
        }

        // dumped dirt off the dig grounds (on the yard, by the road): piles, by their number
        readonly Dictionary<int, (Vector3 p, MaterialLoad l)> dirtyPiles = new();

        void SendPiles(CSteamID? to, bool all)
        {
            var list = new List<(int id, Vector3 p, MaterialLoad l)>();
            if (all) { foreach (var p in DirtPile.All) if (p != null) list.Add((p.NetId, p.transform.position, p.load)); }
            else { foreach (var kv in dirtyPiles) list.Add((kv.Key, kv.Value.p, kv.Value.l)); dirtyPiles.Clear(); }
            if (list.Count == 0) return;
            var w = Begin(MsgPiles);
            w.Write(list.Count);
            foreach (var e in list) { w.Write(e.id); WriteV3(w, e.p); NetIO.Write(w, e.l); }
            if (to.HasValue) Send(to.Value, w, true); else Send(w, true, null);
        }

        void ReadTerrain(BinaryReader r)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { ReadTerrainCore(r); } finally { TerrainReadMs += sw.Elapsed.TotalMilliseconds; }
        }

        void ReadTerrainCore(BinaryReader r)
        {
            int k = r.ReadInt32();
            int n = r.ReadInt32(), len = r.ReadInt32();
            var packed = new MemoryStream(r.ReadBytes(len));
            var raw = new MemoryStream();
            using (var z = new DeflateStream(packed, CompressionMode.Decompress)) z.CopyTo(raw);
            raw.Position = 0;
            var sites = DigSite.Ordered();
            TerrainReadCells += n;
            if (k >= 0 && k < sites.Count) sites[k].ReadCells(new BinaryReader(raw), n);
        }

        // ------------------------------------------------------------------ wash plant (the host's is the real one)

        /// <summary>The plants in the same order on every game (by where they stand).</summary>
        public static List<WashPlant> Plants()
        {
            var list = new List<WashPlant>(WashPlant.All);
            list.RemoveAll(p => p == null);
            list.Sort((a, b) => { var pa = a.transform.position; var pb = b.transform.position; int c = pa.x.CompareTo(pb.x); return c != 0 ? c : pa.z.CompareTo(pb.z); });
            return list;
        }
        static int PlantIndex(WashPlant p) => Plants().IndexOf(p);
        static WashPlant Plant(int i) { var l = Plants(); return i >= 0 && i < l.Count ? l[i] : null; }

        void SendPlants(CSteamID? to)
        {
            var list = Plants();
            for (int i = 0; i < list.Count; i++)
            {
                var w = Begin(MsgPlant); w.Write(i); list[i].NetWrite(w);
                if (to != null) Send(to.Value, w, true); else Send(w, false, null);
            }
        }

        // ------------------------------------------------------------------ the town's jobs (the host keeps the list)

        public ulong MyId => me.m_SteamID;
        public string MyName => LanMode ? $"Player {me.m_SteamID}" : SteamFriends.GetPersonaName();
        /// <summary>How many players are in the game (you included).</summary>
        public int CrewSize => InSession ? MemberList().Count : 1;

        /// <summary>Host: everyone's phone gets the job list.</summary>
        public void SendJobs(CSteamID? to = null)
        {
            if (!IsHost || FPSBase.City.Jobs.Instance == null) return;
            var w = Begin(MsgJobs);
            FPSBase.City.Jobs.Instance.Write(w);
            if (to != null) Send(to.Value, w, true); else Send(w, true, null);
        }

        /// <summary>A friend's answer to a customer: the host does it.</summary>
        public void SendJobAction(int id, int action, bool shared, string arg = null)
        {
            var w = Begin(MsgJobAction); w.Write(id); w.Write(action); w.Write(shared); w.Write(arg ?? "");
            Send(HostSteam, w, true);
        }

        /// <summary>Host: pay one player for a job (yourself, or a friend over the network).</summary>
        public void PayPlayer(ulong id, float amount, string why)
        {
            if (id == MyId || id == 0) { Economy.Instance?.Earn(amount); GameUI.Toast($"You got ${amount:N0} for {why}"); return; }
            var w = Begin(MsgJobPay); w.Write(amount); w.Write(why ?? "");
            Send(new CSteamID(id), w, true);
        }

        /// <summary>Host: everyone in the game gets an equal share.</summary>
        public void PayCrew(float each, string why)
        {
            var all = MemberList();
            foreach (var m in all) PayPlayer(m.m_SteamID, each, why + " (crew share)");
            if (all.Count == 0) PayPlayer(MyId, each, why);
        }

        /// <summary>A player joined: send them the worked ground, the machines bought so far and where everything is.</summary>
        void SendWorldTo(CSteamID to)
        {
            var sites = DigSite.Ordered();
            for (int k = 0; k < sites.Count; k++) SendCells(k, sites[k].TakeChangedCells(true), to);
            SendPiles(to, true);
            if (IsHost) { SendPlants(to); SendJobs(to); }
            foreach (var kv in spawnedListing) if (kv.Key != null) SendSpawn(kv.Key, kv.Value, to);
            foreach (var kv in spawnedTrailers) if (kv.Key != null) SendSpawnTrailer(kv.Key, kv.Value, to);
            // the machines this game runs: their owner (us), where they are and what's in them
            foreach (var v in VehicleBase.All)
            {
                if (v.NetRemote || v.NetId == 0) continue;
                var w = Begin(MsgClaim);
                w.Write(v.NetId); w.Write(v.Occupied);
                WriteV3(w, v.transform.position); WriteQ(w, v.transform.rotation);
                v.NetWrite(w);
                Send(to, w, true);
            }
            SendTrailers(true, to); // and the trailers this game runs
            SendBlocksTo(to); // and the concrete blocks (after the trailers they may be chained to)
            SendSitesTo(to); // and the houses being built
        }

        // ------------------------------------------------------------------ helpers

        static void WriteV3(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        static Vector3 ReadV3(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        static void WriteQ(BinaryWriter w, Quaternion q) { w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(q.w); }
        static Quaternion ReadQ(BinaryReader r) => new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        // ------------------------------------------------------------------ UI

        GUIStyle nameTag;

        void OnGUI()
        {
            if (!InSession || Event.current.type != EventType.Repaint) return;
            var cam = Camera.main;
            if (cam == null) return;
            nameTag ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 15, fontStyle = FontStyle.Bold };
            foreach (var a in avatars.Values)
            {
                if (a.rig == null) continue;
                Vector3 head = (a.rig.Head != null ? a.rig.Head.position : a.root.transform.position + Vector3.up * 1.8f) + Vector3.up * 0.35f;
                Vector3 s = cam.WorldToScreenPoint(head);
                if (s.z <= 0f || s.z > 250f) continue;
                var r = new Rect(s.x - 100f, Screen.height - s.y - 12f, 200f, 24f);
                // (smaller further off, and as see-through as the settings say)
                var m0 = GUI.matrix;
                float sc = GameSettings.LabelScale(s.z), op = GameSettings.LabelOpacity;
                GUIUtility.ScaleAroundPivot(new Vector2(sc, sc), r.center);
                GUI.color = new Color(0f, 0f, 0f, 0.6f * op); GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), a.name, nameTag);
                GUI.color = new Color(1f, 1f, 1f, op); GUI.Label(r, a.name, nameTag);
                GUI.matrix = m0;
            }
            GUI.color = Color.white;
        }

        Vector2 friendScroll;

        readonly List<(int rank, string name, CSteamID id)> friendOrder = new();
        float friendsSortedAt = -10f;

        /// <summary>The F2 / pause-menu multiplayer panel.</summary>
        public void DrawPanel()
        {
            var rich = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
            GUILayout.Label($"<b>Status:</b> {Status}", rich);
            if (!SteamOk)
            {
                GUILayout.Label("Multiplayer goes through Steam. Start Steam, log in, then restart the game.", rich);
                return;
            }
            if (LanMode)
            {
                GUILayout.Label($"Local test link: you are <b>{(InSession ? MyName : lanRole)}</b>.", rich);
                if (!InSession && GUILayout.Button(lanRole == "host" ? "Host on this PC" : "Join the game on this PC", GUILayout.Height(32))) { if (lanRole == "host") Host(); else JoinLan(); }
                if (InSession)
                {
                    string all = MyName + (IsHost ? " (host)" : "");
                    foreach (var p in peers) all += ", " + NameOf(p) + (HostSteam == p ? " (host)" : "");
                    GUILayout.Label($"<b>Players:</b> {all}", rich);
                    if (GUILayout.Button(IsHost ? "Close game" : "Leave game", GUILayout.Height(32), GUILayout.Width(140))) Leave();
                }
                return;
            }
            GUILayout.Label($"You are <b>{SteamFriends.GetPersonaName()}</b> (playing as Steam test app 480 \"Spacewar\").", rich);
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (!InSession)
            {
                GUI.enabled = !creating;
                if (GUILayout.Button("Host a game", GUILayout.Height(32))) Host();
                GUI.enabled = true;
            }
            else
            {
                if (GUILayout.Button("Invite with the Steam overlay", GUILayout.Height(32))) OverlayInvite();
                if (GUILayout.Button(IsHost ? "Close game" : "Leave game", GUILayout.Height(32), GUILayout.Width(140))) Leave();
            }
            GUILayout.EndHorizontal();
            if (InSession)
            {
                GUILayout.Space(4);
                string who = MyName + (IsHost ? " (host)" : "");
                foreach (var p in peers) who += ", " + NameOf(p) + (HostSteam == p ? " (host)" : "");
                GUILayout.Label($"<b>Players:</b> {who}", rich);
            }
            GUILayout.Space(8);
            GUILayout.Label("<b>Friends online</b>  (they need the game open; \"Join\" shows when they are hosting)", rich);
            friendScroll = GUILayout.BeginScrollView(friendScroll, GUILayout.Height(Mathf.Min(360f, Screen.height - 380f)));
            // friends with the game open go at the top (hosting first), then everyone else online, by name
            if (Time.unscaledTime >= friendsSortedAt + 2f || friendOrder.Count == 0)
            {
                friendsSortedAt = Time.unscaledTime;
                friendOrder.Clear();
                int n = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
                for (int i = 0; i < n; i++)
                {
                    var f = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                    if (SteamFriends.GetFriendPersonaState(f) == EPersonaState.k_EPersonaStateOffline) continue;
                    bool inGame = SteamFriends.GetFriendGamePlayed(f, out FriendGameInfo_t gi) && gi.m_gameID.AppID().m_AppId == AppId;
                    int rank = !inGame ? 2 : gi.m_steamIDLobby.IsValid() ? 0 : 1;
                    friendOrder.Add((rank, NameOf(f), f));
                }
                friendOrder.Sort((a, b) => a.rank != b.rank ? a.rank.CompareTo(b.rank) : string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase));
            }
            int shown = 0;
            foreach (var (_, _, f) in friendOrder)
            {
                shown++;
                bool playing = SteamFriends.GetFriendGamePlayed(f, out FriendGameInfo_t g) && g.m_gameID.AppID().m_AppId == AppId;
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label(NameOf(f) + (playing ? "  <color=#9f9>(in game)</color>" : ""), rich);
                if (playing && g.m_steamIDLobby.IsValid() && g.m_steamIDLobby != lobby && GUILayout.Button("Join", GUILayout.Width(80), GUILayout.Height(26))) Join(g.m_steamIDLobby);
                if (!peers.Contains(f) && GUILayout.Button("Invite", GUILayout.Width(80), GUILayout.Height(26))) Invite(f);
                GUILayout.EndHorizontal();
            }
            if (shown == 0) GUILayout.Label("No friends online right now.");
            GUILayout.EndScrollView();
            GUILayout.Space(4);
            GUILayout.Label("Your friend accepts the invite in Steam chat while this game is running (or picks \"Join\" here). " +
                            "Each player has their own money; the ground, the machines and what's in them are shared.", rich);
        }
    }
}
