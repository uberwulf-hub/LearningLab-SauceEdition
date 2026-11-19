// Learning Games - games on the home network: one computer hosts, the others find it and join. Messages are lines of text
// (JSON made by the PowerShell side). Only the host listens for connections, so only the host may need a firewall
// "Allow" (or the rule App Hub's all-users setup adds); players find hosts by asking (UDP broadcast) and the host
// answers them directly, which the firewall lets through as a reply.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace LearningGames
{
    public sealed class FoundHost { public string Address = "", Name = "", Session = ""; public int Port, Players; public DateTime Seen; }

    /// <summary>A message from the network: who sent it (client id, 0 = the host) and the line.</summary>
    public sealed class NetMessage { public int From; public string Line = ""; }

    public static class Lan
    {
        public const int DiscoveryPort = 47800, GamePort = 47801;
        const string Ask = "AMATH?", Reply = "AMATH!";

        /// <summary>Broadcasts "who is hosting?" on every network and collects the answers for a short while.</summary>
        public static FoundHost[] Discover(int waitMs)
        {
            var found = new Dictionary<string, FoundHost>();
            using (UdpClient u = new UdpClient(0))
            {
                u.EnableBroadcast = true;
                byte[] ask = Encoding.UTF8.GetBytes(Ask);
                foreach (IPAddress b in Broadcasts()) { try { u.Send(ask, ask.Length, new IPEndPoint(b, DiscoveryPort)); } catch (Exception) { } }
                try { u.Send(ask, ask.Length, new IPEndPoint(IPAddress.Loopback, DiscoveryPort)); } catch (Exception) { }   // a host on this same computer
                DateTime end = DateTime.Now.AddMilliseconds(waitMs);
                while (DateTime.Now < end)
                {
                    u.Client.ReceiveTimeout = Math.Max(50, (int)(end - DateTime.Now).TotalMilliseconds);
                    IPEndPoint from = null;
                    byte[] data;
                    try { data = u.Receive(ref from); }
                    catch (SocketException) { break; }
                    try
                    {
                        // one odd reply (anyone on the network can send one) must not stop the search
                        string s = Encoding.UTF8.GetString(data);
                        string[] p = s.Split('|');
                        int port, players;
                        if (p.Length >= 5 && p[0] == Reply && int.TryParse(p[2], out port) && port > 0 && port < 65536 && int.TryParse(p[4], out players))
                        {
                            FoundHost h = new FoundHost();
                            h.Address = from.Address.ToString(); h.Name = Clip(p[1], 32); h.Port = port; h.Session = Clip(p[3], 32); h.Players = Math.Max(0, Math.Min(99, players)); h.Seen = DateTime.Now;
                            if (IPAddress.IsLoopback(from.Address) && found.Values.Any(x => x.Name == h.Name && x.Port == h.Port)) continue;
                            found[h.Address + ":" + h.Port] = h;
                        }
                    }
                    catch (Exception) { }
                }
            }
            return found.Values.OrderBy(h => h.Name).ToArray();
        }

        static string Clip(string s, int max)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "") if (!char.IsControl(c)) sb.Append(c);
            string r = sb.ToString().Trim();
            return r.Length > max ? r.Substring(0, max) : r;
        }

        static List<IPAddress> Broadcasts()
        {
            var l = new List<IPAddress> { IPAddress.Broadcast };
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (UnicastIPAddressInformation u in ni.GetIPProperties().UnicastAddresses)
                {
                    if (u.Address.AddressFamily != AddressFamily.InterNetwork || u.IPv4Mask == null) continue;
                    byte[] a = u.Address.GetAddressBytes(), m = u.IPv4Mask.GetAddressBytes();
                    for (int i = 0; i < 4; i++) a[i] = (byte)(a[i] | ~m[i]);
                    l.Add(new IPAddress(a));
                }
            }
            return l;
        }

        public static string LocalAddresses()
        {
            var l = new List<string>();
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (!ni.GetIPProperties().GatewayAddresses.Any()) continue;
                foreach (UnicastIPAddressInformation u in ni.GetIPProperties().UnicastAddresses)
                    if (u.Address.AddressFamily == AddressFamily.InterNetwork) l.Add(u.Address.ToString());
            }
            return string.Join(", ", l.ToArray());
        }
    }

    /// <summary>Reads lines of text without letting a peer make one endless line: longer lines are skipped.</summary>
    public sealed class LineReader
    {
        public const int MaxLine = 65536;
        readonly StreamReader reader;
        readonly char[] buf = new char[4096];
        int pos, len;
        public LineReader(StreamReader r) { reader = r; }

        /// <summary>The next line of at most MaxLine characters (without the line end), or null at the end of the stream.</summary>
        public string ReadLine()
        {
            var sb = new StringBuilder();
            bool skipping = false;
            while (true)
            {
                if (pos >= len)
                {
                    len = reader.Read(buf, 0, buf.Length); pos = 0;
                    if (len <= 0) return null;   // a part line at the end is dropped: every message ends with a newline
                }
                int nl = Array.IndexOf(buf, '\n', pos, len - pos);
                int end = nl < 0 ? len : nl;
                if (!skipping)
                {
                    if (sb.Length + (end - pos) > MaxLine) { skipping = true; sb.Length = 0; }
                    else sb.Append(buf, pos, end - pos);
                }
                pos = nl < 0 ? len : nl + 1;
                if (nl >= 0)
                {
                    if (skipping) { skipping = false; continue; }
                    if (sb.Length > 0 && sb[sb.Length - 1] == '\r') sb.Length--;
                    return sb.ToString();
                }
            }
        }
    }

    /// <summary>Discovery without blocking the window: the window polls the task.</summary>
    public static class Discovery
    {
        public static System.Threading.Tasks.Task<FoundHost[]> Run(int waitMs) { return System.Threading.Tasks.Task.Run(() => Lan.Discover(waitMs)); }
    }

    /// <summary>The hosting computer: accepts players, relays lines, answers discovery.</summary>
    public sealed class GameHost : IDisposable
    {
        TcpListener listener;
        UdpClient discovery;
        readonly ConcurrentDictionary<int, Peer> peers = new ConcurrentDictionary<int, Peer>();
        readonly ConcurrentQueue<NetMessage> inbox = new ConcurrentQueue<NetMessage>();
        int nextId = 1;
        volatile bool running = true;
        public readonly string Name, Session;
        public int Port { get; private set; }
        public string LastError = "";
        /// <summary>Tests: listen on 127.0.0.1 only (no firewall prompt).</summary>
        public static bool LoopbackOnly;

        sealed class Peer { public int Id; public TcpClient Client; public StreamWriter Writer; public readonly object Lock = new object(); }

        public GameHost(string name, string session)
        {
            Name = name.Replace("|", "/"); Session = session.Replace("|", "/");
            for (int port = Lan.GamePort; port < Lan.GamePort + 10; port++)
            {
                try { listener = new TcpListener(LoopbackOnly ? IPAddress.Loopback : IPAddress.Any, port); listener.Start(); Port = port; break; }
                catch (SocketException) { listener = null; }
            }
            if (listener == null) throw new Exception("No free network port for hosting (47801-47810).");
            new Thread(AcceptLoop) { IsBackground = true, Name = "Learning Games accept" }.Start();
            try
            {
                discovery = new UdpClient();
                discovery.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                discovery.Client.Bind(new IPEndPoint(LoopbackOnly ? IPAddress.Loopback : IPAddress.Any, Lan.DiscoveryPort));
                new Thread(DiscoveryLoop) { IsBackground = true, Name = "Learning Games discovery" }.Start();
            }
            catch (Exception ex) { LastError = "Other computers may not find this game automatically (" + ex.Message + "); they can type this computer's address."; }
        }

        public int PlayerCount { get { return peers.Count; } }
        public int[] PlayerIds { get { return peers.Keys.OrderBy(k => k).ToArray(); } }

        void DiscoveryLoop()
        {
            while (running)
            {
                try
                {
                    IPEndPoint from = null;
                    byte[] d = discovery.Receive(ref from);
                    if (Encoding.UTF8.GetString(d) == "AMATH?")
                    {
                        byte[] r = Encoding.UTF8.GetBytes("AMATH!|" + Name + "|" + Port + "|" + Session + "|" + (peers.Count + 1));
                        discovery.Send(r, r.Length, from);
                    }
                }
                catch (Exception) { if (!running) return; Thread.Sleep(100); }
            }
        }

        void AcceptLoop()
        {
            while (running)
            {
                TcpClient c;
                try { c = listener.AcceptTcpClient(); } catch (Exception) { if (!running) return; continue; }
                c.NoDelay = true;
                Peer p = new Peer { Id = Interlocked.Increment(ref nextId) - 1, Client = c };
                c.SendTimeout = 2000;   // a stalled player is dropped instead of freezing the host window
                var stream = c.GetStream();
                stream.WriteTimeout = 2000;
                p.Writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                peers[p.Id] = p;
                inbox.Enqueue(new NetMessage { From = p.Id, Line = "{\"t\":\"_join\"}" });
                new Thread(() => ReadLoop(p, stream)) { IsBackground = true, Name = "Learning Games peer " + p.Id }.Start();
            }
        }

        void ReadLoop(Peer p, NetworkStream s)
        {
            try
            {
                using (var r = new StreamReader(s, Encoding.UTF8))
                {
                    string line;
                    var lr = new LineReader(r);
                    while (running && (line = lr.ReadLine()) != null) if (line.Length > 0) inbox.Enqueue(new NetMessage { From = p.Id, Line = line });
                }
            }
            catch (Exception) { }
            Drop(p.Id);
        }

        void Drop(int id)
        {
            Peer p;
            if (peers.TryRemove(id, out p)) { try { p.Client.Close(); } catch (Exception) { } inbox.Enqueue(new NetMessage { From = id, Line = "{\"t\":\"_leave\"}" }); }
        }

        public NetMessage[] Drain() { var l = new List<NetMessage>(); NetMessage m; while (inbox.TryDequeue(out m)) l.Add(m); return l.ToArray(); }

        public void Send(int id, string line)
        {
            Peer p;
            if (!peers.TryGetValue(id, out p)) return;
            try { lock (p.Lock) p.Writer.WriteLine(line); } catch (Exception) { Drop(id); }
        }
        public void Broadcast(string line) { foreach (int id in peers.Keys.ToArray()) Send(id, line); }
        public void Kick(int id) { Drop(id); }

        public void Dispose()
        {
            running = false;
            try { listener.Stop(); } catch (Exception) { }
            try { if (discovery != null) discovery.Close(); } catch (Exception) { }
            foreach (var p in peers.Values) try { p.Client.Close(); } catch (Exception) { }
            peers.Clear();
        }
    }

    /// <summary>A player's connection to a host.</summary>
    public sealed class GameClient : IDisposable
    {
        readonly TcpClient client;
        readonly StreamWriter writer;
        readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>();
        readonly object sendLock = new object();
        public volatile bool Connected;
        public readonly string Address;

        public GameClient(string address, int port)
        {
            Address = address;
            client = new TcpClient();
            if (!client.ConnectAsync(address, port).Wait(2500)) { client.Close(); throw new Exception("No answer from " + address + ". Is the game still hosted there?"); }
            client.NoDelay = true;
            client.SendTimeout = 2000;
            var s = client.GetStream();
            s.WriteTimeout = 2000;
            writer = new StreamWriter(s, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            Connected = true;
            new Thread(() =>
            {
                try
                {
                    using (var r = new StreamReader(s, Encoding.UTF8))
                    {
                        string line;
                        var lr = new LineReader(r);
                        while ((line = lr.ReadLine()) != null) if (line.Length > 0) inbox.Enqueue(line);
                    }
                }
                catch (Exception) { }
                Connected = false;
                inbox.Enqueue("{\"t\":\"_closed\"}");
            }) { IsBackground = true, Name = "Learning Games client" }.Start();
        }

        public string[] Drain() { var l = new List<string>(); string m; while (inbox.TryDequeue(out m)) l.Add(m); return l.ToArray(); }
        public void Send(string line) { if (!Connected) return; try { lock (sendLock) writer.WriteLine(line); } catch (Exception) { Dispose(); } }   // the reader then reports "_closed"
        public void Dispose() { Connected = false; try { client.Close(); } catch (Exception) { } }
    }
}
