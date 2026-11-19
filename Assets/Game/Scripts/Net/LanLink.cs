using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace FPSBase.Net
{
    /// <summary>
    /// A stand-in for Steam's lobby and relay, for testing two (or more) copies of the game on one PC: the host
    /// listens on 127.0.0.1, friends connect, and the host passes messages between friends. Players are numbered
    /// (the host is 1). Every message is a frame: length, from, to, then the same bytes Steam would carry.
    /// Start a copy with "-nettest host 27960" or "-nettest join 27960".
    /// </summary>
    public sealed class LanLink : IDisposable
    {
        public const ulong HostId = 1;
        const ulong Roster = 0; // a frame "to 0" from the host: the list of players

        public ulong Me { get; private set; }
        public bool IsHost => Me == HostId;
        /// <summary>Everyone in the game (you included), as the host last said.</summary>
        public List<ulong> Members { get; } = new();
        public bool Connected { get; private set; }
        public bool Lost { get; private set; }

        readonly ConcurrentQueue<(ulong from, byte[] data, double at, bool reliable)> inbox = new();
        readonly List<(ulong from, byte[] data, double at, bool reliable)> held = new();
        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        double lastReliableAt;
        readonly Random rnd = new Random();

        // a bad connection, for testing: "-netsim <lag ms> <jitter ms> <lost %>" (lost: only the unreliable ones, like Steam)
        static float simLag, simJitter, simLoss;
        static bool simRead;
        static void ReadSim()
        {
            if (simRead) return; simRead = true;
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, "-netsim");
            if (i < 0 || i + 3 >= a.Length) return;
            float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out simLag);
            float.TryParse(a[i + 2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out simJitter);
            float.TryParse(a[i + 3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out simLoss);
        }
        public static string SimInfo { get { ReadSim(); return simLag + simJitter + simLoss > 0f ? $"lag {simLag} ms ± {simJitter}, {simLoss}% lost" : "perfect"; } }

        /// <summary>A frame arrived: when to hand it over (later, on a bad connection), or drop it.</summary>
        void Arrived(ulong from, byte[] data, bool reliable)
        {
            ReadSim();
            double now = clock.Elapsed.TotalMilliseconds, at = now;
            if (simLag + simJitter > 0f)
            {
                double r; lock (rnd) r = rnd.NextDouble();
                at = now + simLag + r * simJitter;
            }
            if (reliable) { lock (rnd) { if (at < lastReliableAt) at = lastReliableAt; lastReliableAt = at; } }
            else if (simLoss > 0f) { double r; lock (rnd) r = rnd.NextDouble(); if (r * 100.0 < simLoss) return; }
            inbox.Enqueue((from, data, at, reliable));
        }
        readonly ConcurrentQueue<List<ulong>> rosters = new();
        TcpListener listener;
        readonly Dictionary<ulong, Conn> conns = new(); // host: each friend; friend: the host (key 1)
        readonly object gate = new();
        ulong nextId = 2;
        volatile bool closing;

        sealed class Conn
        {
            public TcpClient tcp; public NetworkStream s; public ulong id;
            public readonly object sendGate = new();
        }

        public static LanLink Host(int port)
        {
            var l = new LanLink { Me = HostId };
            l.listener = new TcpListener(IPAddress.Loopback, port);
            l.listener.Start();
            l.Connected = true;
            l.Members.Add(HostId);
            new Thread(l.AcceptLoop) { IsBackground = true, Name = "LanLink accept" }.Start();
            return l;
        }

        public static LanLink Join(int port)
        {
            var l = new LanLink();
            var tcp = new TcpClient { NoDelay = true };
            tcp.Connect(IPAddress.Loopback, port);
            var c = new Conn { tcp = tcp, s = tcp.GetStream(), id = HostId };
            // the host tells us our number first
            var r = new BinaryReader(c.s);
            l.Me = r.ReadUInt64();
            lock (l.gate) l.conns[HostId] = c;
            l.Connected = true;
            new Thread(() => l.ReadLoop(c)) { IsBackground = true, Name = "LanLink read" }.Start();
            return l;
        }

        void AcceptLoop()
        {
            while (!closing)
            {
                TcpClient tcp;
                try { tcp = listener.AcceptTcpClient(); } catch { return; }
                tcp.NoDelay = true;
                Conn c;
                lock (gate)
                {
                    c = new Conn { tcp = tcp, s = tcp.GetStream(), id = nextId++ };
                    conns[c.id] = c;
                }
                try { lock (c.sendGate) new BinaryWriter(c.s).Write(c.id); } catch { continue; }
                SendRoster();
                new Thread(() => ReadLoop(c)) { IsBackground = true, Name = "LanLink read " + c.id }.Start();
            }
        }

        void ReadLoop(Conn c)
        {
            var r = new BinaryReader(c.s);
            try
            {
                while (!closing)
                {
                    int len = r.ReadInt32();
                    ulong from = r.ReadUInt64(), to = r.ReadUInt64();
                    bool reliable = r.ReadBoolean();
                    byte[] data = r.ReadBytes(len);
                    if (data.Length != len) throw new EndOfStreamException();
                    if (IsHost)
                    {
                        from = c.id; // (a friend can't pretend to be someone else)
                        if (to == Me) Arrived(from, data, reliable);
                        else Forward(from, to, data, reliable);
                    }
                    else if (to == Roster && from == HostId)
                    {
                        var list = new List<ulong>();
                        var br = new BinaryReader(new MemoryStream(data));
                        int n = br.ReadInt32();
                        for (int i = 0; i < n; i++) list.Add(br.ReadUInt64());
                        rosters.Enqueue(list);
                    }
                    else Arrived(from, data, reliable);
                }
            }
            catch { }
            if (closing) return;
            lock (gate) conns.Remove(c.id);
            try { c.tcp.Close(); } catch { }
            if (IsHost) SendRoster();
            else { Lost = true; rosters.Enqueue(new List<ulong> { Me }); }
        }

        void Forward(ulong from, ulong to, byte[] data, bool reliable)
        {
            Conn c;
            lock (gate) conns.TryGetValue(to, out c);
            if (c != null) Write(c, from, to, data, data.Length, reliable);
        }

        void SendRoster()
        {
            List<ulong> list;
            List<Conn> all;
            lock (gate) { list = new List<ulong> { HostId }; list.AddRange(conns.Keys); all = new List<Conn>(conns.Values); }
            list.Sort();
            rosters.Enqueue(list);
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write(list.Count); foreach (var id in list) w.Write(id);
            var b = ms.ToArray();
            foreach (var c in all) Write(c, HostId, Roster, b, b.Length, true);
        }

        static bool Write(Conn c, ulong from, ulong to, byte[] data, int len, bool reliable)
        {
            try
            {
                lock (c.sendGate)
                {
                    var w = new BinaryWriter(c.s);
                    w.Write(len); w.Write(from); w.Write(to); w.Write(reliable); w.Write(data, 0, len);
                    w.Flush();
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>Send to one player (the host passes it on when it's for a friend).</summary>
        public bool Send(ulong to, byte[] data, int len, bool reliable = true)
        {
            if (!Connected || to == Me) return false;
            Conn c;
            lock (gate) conns.TryGetValue(IsHost ? to : HostId, out c);
            return c != null && Write(c, Me, to, data, len, reliable);
        }

        public bool TryReceive(out ulong from, out byte[] data)
        {
            while (inbox.TryDequeue(out var m)) held.Add(m);
            double now = clock.Elapsed.TotalMilliseconds;
            for (int i = 0; i < held.Count; i++)
                if (held[i].at <= now)
                {
                    from = held[i].from; data = held[i].data; held.RemoveAt(i);
                    return true;
                }
            from = 0; data = null; return false;
        }

        /// <summary>The newest list of players, if it changed.</summary>
        public bool TakeRoster()
        {
            bool any = false;
            while (rosters.TryDequeue(out var l)) { Members.Clear(); Members.AddRange(l); any = true; }
            return any;
        }

        public void Dispose()
        {
            closing = true;
            Connected = false;
            try { listener?.Stop(); } catch { }
            lock (gate) { foreach (var c in conns.Values) try { c.tcp.Close(); } catch { } conns.Clear(); }
        }
    }
}
