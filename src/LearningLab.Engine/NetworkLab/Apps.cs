// Network Lab - the things people do from a device: look up a name, ping, trace the route, fetch a web page, open
// a shared folder, print, discover printers, log in with SSH. Each runs inside the simulation and reports back
// through callbacks, so a command prompt (or a lab check) can word the results its own way.
using System;
using System.Collections.Generic;
using System.Linq;

namespace NetworkLab
{
    public static class Resolver
    {
        /// <summary>Name to address: an IP as is, this device's own name, the cache, DNS servers, then (for one-word
        /// names on Windows-like and Linux-like devices) asking the local network (LLMNR). cb(ip, error, server).</summary>
        public static void Resolve(Device d, string name, Action<uint, string, uint> cb)
        {
            uint ip;
            if (Ip.TryParse(name, out ip)) { cb(ip, "", 0); return; }
            name = name.Trim().TrimEnd('.');
            if (name.Length == 0) { cb(0, "empty", 0); return; }
            var s = d.Ip;
            var sim = d.P.Sim;
            if (string.Equals(name, d.HostName, StringComparison.OrdinalIgnoreCase) || string.Equals(name, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                var me = d.Host != null ? d.Host.Main : s.Ifs.FirstOrDefault(f => f.Ip != 0);
                cb(name.ToLowerInvariant() == "localhost" ? 0x7F000001 : (me != null ? me.Ip : 0x7F000001), "", 0);
                return;
            }
            if (d.Host != null && d.Host.DnsCache.TryGetValue(name, out ip)) { cb(ip, "", 0); return; }
            if (d.Ios != null) { uint h; if (d.Ios.HostTable.TryGetValue(name, out h)) { cb(h, "", 0); return; } }
            var servers = d.Host != null ? d.Host.DnsServers : (d.Ios != null ? d.Ios.NameServers : new List<uint>());
            Ask(d, name, servers, 0, cb);
        }

        static void Ask(Device d, string name, List<uint> servers, int i, Action<uint, string, uint> cb)
        {
            var s = d.Ip;
            var sim = d.P.Sim;
            if (i >= servers.Count)
            {
                bool llmnr = d.Host != null && name.IndexOf('.') < 0 && Personalities.Get(d.Os).Llmnr;
                if (!llmnr) { cb(0, servers.Count == 0 ? "no-dns" : (i == 0 ? "no-dns" : "timeout"), 0); return; }
                AskLocal(d, name, cb, servers.Count == 0 ? "no-dns" : "nxdomain");
                return;
            }
            int port = s.Ephemeral();
            int id = d.P.Rng.Next(1, 65535);
            SimTimer t = null;
            bool done = false;
            s.Udp[port] = (ifc, p) =>
            {
                var r = p.Seg.App as DnsMsg;
                if (r == null || r.Id != id || done) return;
                done = true; s.Udp.Remove(port); if (t != null) t.Cancel();
                if (r.Answer != 0)
                {
                    if (d.Host != null) d.Host.DnsCache[name] = r.Answer;
                    cb(r.Answer, "", servers[i]);
                }
                else
                {
                    bool llmnr = d.Host != null && name.IndexOf('.') < 0 && Personalities.Get(d.Os).Llmnr;
                    if (llmnr) AskLocal(d, name, cb, r.Error == "REFUSED" ? "refused" : "nxdomain");
                    else cb(0, r.Error == "REFUSED" ? "refused" : "nxdomain", servers[i]);
                }
            };
            var q = new IpPkt { Dst = servers[i], Proto = "UDP", Seg = new Seg { SrcPort = port, DstPort = 53, App = new DnsMsg { Id = id, QName = name } } };
            sim.NoteOrigin(d, new Frame { Ip = q }, d.Name + " asks the DNS server " + Ip.Str(servers[i]) + " for the address of " + name + ".");
            string err = s.Send(q, null);
            if (err.Length > 0) { s.Udp.Remove(port); done = true; Ask(d, name, servers, i + 1, cb); return; }
            t = sim.After(2000, () => { if (done) return; done = true; s.Udp.Remove(port); Ask(d, name, servers, i + 1, cb); });
        }

        /// <summary>LLMNR: asks everyone on the local network "who is NAME?" (how Windows finds PCs without DNS).</summary>
        static void AskLocal(Device d, string name, Action<uint, string, uint> cb, string failure)
        {
            var s = d.Ip;
            var sim = d.P.Sim;
            var f = d.Host.Main;
            if (f == null || f.Ip == 0 || !f.Up) { cb(0, failure, 0); return; }
            int port = s.Ephemeral();
            bool done = false;
            SimTimer t = null;
            s.Udp[port] = (ifc, p) =>
            {
                var r = p.Seg.App as DnsMsg;
                if (r == null || done || r.Answer == 0) return;
                done = true; s.Udp.Remove(port); if (t != null) t.Cancel();
                d.Host.DnsCache[name] = r.Answer;
                cb(r.Answer, "", 0);
            };
            var q = new IpPkt { Src = f.Ip, Dst = 0xE00000FCu, Ttl = 1, Proto = "UDP", Seg = new Seg { SrcPort = port, DstPort = 5355, App = new DnsMsg { Id = 1, QName = name } } };
            sim.NoteOrigin(d, new Frame { Ip = q }, d.Name + " asks everyone on its own network: who is " + name + "?");
            s.Send(q, f);
            t = sim.After(1000, () => { if (done) return; done = true; s.Udp.Remove(port); cb(0, failure, 0); });
        }
    }

    /// <summary>One ping answer (or not).</summary>
    public class PingResult
    {
        public int Seq;
        public string Kind = "reply";   // reply, timeout, unreachable, ttl, prohibited, port, general
        public uint From;
        public long Ms;
        public int Ttl;
        public int Bytes = 32;
    }

    public class PingJob
    {
        public Device D;
        public uint Target;
        public string Name = "";
        public int Count = 4, Ttl = 0, Size = 32;
        public long TimeoutMs = 1000, IntervalMs = 1000;
        public bool Forever;
        public int Sent, Received;
        public long MinMs = long.MaxValue, MaxMs, SumMs;
        public bool Stopped, Finished;
        public Action<PingResult> OnResult;
        public Action<PingJob> OnDone;
        public Action<string> OnResolveFail;
        int ident;
        int seq;

        public static PingJob Start(Device d, string target, int count, Action<PingJob> onReady)
        {
            var j = new PingJob { D = d, Name = target, Count = count };
            if (onReady != null) onReady(j);
            Resolver.Resolve(d, target, (ip, err, srv) =>
            {
                if (ip == 0) { j.Finished = true; if (j.OnResolveFail != null) j.OnResolveFail(err); if (j.OnDone != null) j.OnDone(j); return; }
                j.Target = ip;
                j.ident = d.Ip.NextIdent++;
                j.Next();
            });
            return j;
        }

        public void Stop() { Stopped = true; }

        void Next()
        {
            var sim = D.P.Sim;
            if (Stopped || (!Forever && seq >= Count)) { Finished = true; if (OnDone != null) OnDone(this); return; }
            int mySeq = ++seq;
            Sent++;
            long sent = sim.Now;
            bool answered = false;
            IcmpWaiter w = null;
            SimTimer to = null;
            Action<PingResult> finish = r =>
            {
                if (answered) return;
                answered = true;
                D.Ip.Waiters.Remove(w);
                if (to != null) to.Cancel();
                if (OnResult != null) OnResult(r);
                long wait = Math.Max(0, IntervalMs - (sim.Now - sent));
                sim.After(r.Kind == "timeout" ? 0 : wait, Next);
            };
            w = new IcmpWaiter
            {
                Ident = ident,
                OnReply = p =>
                {
                    var ic = p.Icmp;
                    if (ic.Type == 0 && ic.Seq != mySeq) return;
                    if (ic.Type != 0 && (ic.Original == null || ic.Original.Icmp == null || ic.Original.Icmp.Seq != mySeq)) return;
                    var r = new PingResult { Seq = mySeq, From = p.Src, Ms = sim.Now - sent, Ttl = p.Ttl, Bytes = Size };
                    if (ic.Type == 0) { Received++; MinMs = Math.Min(MinMs, r.Ms); MaxMs = Math.Max(MaxMs, r.Ms); SumMs += r.Ms; }
                    else if (ic.Type == 11) r.Kind = "ttl";
                    else if (ic.Type == 3) r.Kind = ic.Code == 13 ? "prohibited" : ic.Code == 3 ? "port" : "unreachable";
                    finish(r);
                }
            };
            D.Ip.Waiters.Add(w);
            var pk = new IpPkt { Dst = Target, Proto = "ICMP", Ttl = Ttl > 0 ? Ttl : 0, Icmp = new IcmpPkt { Type = 8, Ident = ident, Seq = mySeq } };
            sim.NoteOrigin(D, new Frame { Ip = pk }, D.Name + " sends a ping (echo request) to " + Ip.Str(Target) + ".");
            string err = D.Ip.Send(pk, null);
            if (err.Length > 0)
            {
                // no address or no way out: like Windows' "General failure" / "Destination host unreachable"
                sim.After(Math.Min(TimeoutMs, 200), () => finish(new PingResult { Seq = mySeq, Kind = err == "no route" ? "noroute" : "general" }));
                return;
            }
            to = sim.After(TimeoutMs, () => finish(new PingResult { Seq = mySeq, Kind = "timeout" }));
        }
    }

    public class TraceHop
    {
        public int Ttl;
        public long[] Ms = { -1, -1, -1 };
        public uint From;
        public bool Reached, Unreachable;
    }

    public class TraceJob
    {
        public Device D;
        public uint Target;
        public string Name = "";
        public int MaxHops = 30;
        public bool Udp;            // routers and Linux send UDP probes, Windows sends pings
        public long TimeoutMs = 1000;
        public bool Stopped, Finished;
        public Action<TraceHop> OnHop;
        public Action<TraceJob> OnDone;
        public Action<string> OnResolveFail;
        int ttl;
        int ident;

        public static TraceJob Start(Device d, string target, bool udp, Action<TraceJob> onReady)
        {
            var j = new TraceJob { D = d, Name = target, Udp = udp };
            if (onReady != null) onReady(j);
            Resolver.Resolve(d, target, (ip, err, srv) =>
            {
                if (ip == 0) { j.Finished = true; if (j.OnResolveFail != null) j.OnResolveFail(err); if (j.OnDone != null) j.OnDone(j); return; }
                j.Target = ip;
                j.ident = d.Ip.NextIdent++;
                j.Hop();
            });
            return j;
        }
        public void Stop() { Stopped = true; }

        void Hop()
        {
            var sim = D.P.Sim;
            if (Stopped || ttl >= MaxHops) { Finished = true; if (OnDone != null) OnDone(this); return; }
            ttl++;
            var hop = new TraceHop { Ttl = ttl };
            Probe(hop, 0);
        }

        void Probe(TraceHop hop, int n)
        {
            var sim = D.P.Sim;
            if (n >= 3)
            {
                if (OnHop != null) OnHop(hop);
                if (hop.Reached || hop.Unreachable) { Finished = true; if (OnDone != null) OnDone(this); return; }
                Hop();
                return;
            }
            long sent = sim.Now;
            bool done = false;
            IcmpWaiter w = null;
            SimTimer to = null;
            int srcPort = Udp ? D.Ip.Ephemeral() : 0;
            int seq = hop.Ttl * 3 + n;
            Action<IpPkt> got = p =>
            {
                if (done) return;
                var ic = p.Icmp;
                if (!Udp)
                {
                    if (ic.Type == 0 && ic.Seq != seq) return;
                    if (ic.Type != 0 && (ic.Original == null || ic.Original.Icmp == null || ic.Original.Icmp.Seq != seq)) return;
                }
                done = true;
                D.Ip.Waiters.Remove(w);
                if (to != null) to.Cancel();
                hop.Ms[n] = sim.Now - sent;
                hop.From = p.Src;
                if (ic.Type == 0 || (ic.Type == 3 && ic.Code == 3)) hop.Reached = true;
                else if (ic.Type == 3) hop.Unreachable = true;
                sim.After(0, () => Probe(hop, n + 1));
            };
            w = new IcmpWaiter { Ident = Udp ? -srcPort : ident, OnReply = got };
            D.Ip.Waiters.Add(w);
            IpPkt pk = Udp
                ? new IpPkt { Dst = Target, Ttl = hop.Ttl, Proto = "UDP", Seg = new Seg { SrcPort = srcPort, DstPort = 33434 + seq } }
                : new IpPkt { Dst = Target, Ttl = hop.Ttl, Proto = "ICMP", Icmp = new IcmpPkt { Type = 8, Ident = ident, Seq = seq } };
            sim.NoteOrigin(D, new Frame { Ip = pk }, D.Name + " sends a probe with TTL " + hop.Ttl + " towards " + Ip.Str(Target) + ": the router where the TTL runs out will answer.");
            string err = D.Ip.Send(pk, null);
            if (err.Length > 0) { D.Ip.Waiters.Remove(w); hop.Unreachable = true; sim.After(0, () => Probe(hop, 3)); return; }
            to = sim.After(TimeoutMs, () => { if (done) return; done = true; D.Ip.Waiters.Remove(w); Probe(hop, n + 1); });
        }
    }

    /// <summary>Opens a TCP connection to a name or address and sends one request; cb(answer or null, error).</summary>
    public static class Request
    {
        public static void Send(Device d, string host, int port, TextMsg req, Action<TextMsg, string, uint> cb)
        {
            Resolver.Resolve(d, host, (ip, err, srv) =>
            {
                if (ip == 0) { cb(null, "name:" + err, 0); return; }
                bool done = false;
                TcpConn.Open(d.Ip, ip, port,
                    c => c.Send(req),
                    (c, m) => { if (done) return; done = true; c.Close(); cb(m, "", ip); },
                    (c, why) => { if (done) return; done = true; cb(null, why, ip); });
            });
        }

        /// <summary>Splits http://host:port/path into its parts.</summary>
        public static bool ParseUrl(string url, out string host, out int port, out string path)
        {
            host = ""; port = 80; path = "/";
            if (string.IsNullOrEmpty(url)) return false;
            string u = url.Trim();
            int i = u.IndexOf("://");
            if (i >= 0)
            {
                string scheme = u.Substring(0, i).ToLowerInvariant();
                if (scheme == "https") port = 443;
                else if (scheme != "http") return false;
                u = u.Substring(i + 3);
            }
            int slash = u.IndexOf('/');
            if (slash >= 0) { path = u.Substring(slash); u = u.Substring(0, slash); }
            int colon = u.IndexOf(':');
            if (colon >= 0) { int pp; if (!int.TryParse(u.Substring(colon + 1), out pp)) return false; port = pp; u = u.Substring(0, colon); }
            host = u;
            return host.Length > 0;
        }

        public static void Http(Device d, string url, string method, Dictionary<string, string> form, Action<TextMsg, string> cb)
        {
            string host, path; int port;
            if (!ParseUrl(url, out host, out port, out path)) { d.P.Sim.After(0, () => cb(null, "badurl")); return; }
            if (port == 443) { d.P.Sim.After(0, () => cb(null, "https")); return; }
            var req = new TextMsg { Proto = "HTTP", Line = (method ?? "GET") + " " + path };
            req.Fields["Host"] = host;
            if (form != null) foreach (var kv in form) req.Fields[kv.Key] = kv.Value;
            Send(d, host, port, req, (m, err, ip) => cb(m, err));
        }

        /// <summary>Asks the local network which printers there are (mDNS-style). cb gets name, model, address each.</summary>
        public static void DiscoverPrinters(Device d, Action<List<string[]>> cb)
        {
            var s = d.Ip;
            var sim = d.P.Sim;
            var found = new List<string[]>();
            var f = d.Host != null ? d.Host.Main : null;
            if (f == null || f.Ip == 0 || !f.Up) { sim.After(0, () => cb(found)); return; }
            int port = s.Ephemeral();
            s.Udp[port] = (ifc, p) =>
            {
                var a = p.Seg.App as TextMsg;
                if (a == null || a.Line != "I PRINT") return;
                string n, m, ad;
                a.Fields.TryGetValue("Name", out n); a.Fields.TryGetValue("Model", out m); a.Fields.TryGetValue("Address", out ad);
                if (!found.Any(x => x[2] == ad)) found.Add(new[] { n ?? "", m ?? "", ad ?? "" });
            };
            var q = new IpPkt { Src = f.Ip, Dst = 0xE00000FBu, Ttl = 1, Proto = "UDP", Seg = new Seg { SrcPort = port, DstPort = 5353, App = new TextMsg { Proto = "MDNS", Line = "WHO PRINTS?" } } };
            sim.NoteOrigin(d, new Frame { Ip = q }, d.Name + " asks everyone on its own network: who is a printer?");
            s.Send(q, f);
            sim.After(1500, () => { s.Udp.Remove(port); cb(found); });
        }

        public static void Print(Device d, string printer, string text, Action<bool, string> cb)
        {
            var req = new TextMsg { Proto = "PRINT", Line = "PRINT", Body = text };
            req.Fields["From"] = d.Name;
            Send(d, printer, 9100, req, (m, err, ip) => cb(m != null && m.Line == "PRINTED", err));
        }
    }
}
