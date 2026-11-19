// Cert Lab - DNS servers of the AZ-800 course. Each server that runs the DNS role has file-backed zones and settings
// (forwarders, recursion, client subnets and query resolution policies); Active Directory-integrated zones live in the
// directory (one object per zone, its records serialized) so they replicate with it and appear on every DC that runs
// DNS. Zone kinds: primary, secondary (copied from its masters if they allow zone transfers, refreshed every 15
// minutes), stub and conditional forwarder. Record data: A ip, CNAME target, MX "10|mail.x", SRV "0|100|389|host",
// NS / PTR a name, TXT text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class DnsRec
    {
        public string Name = "@", Type = "A", Data = "", Scope = "";
        public int Ttl = 3600;
        public DnsRec Clone() { return new DnsRec { Name = Name, Type = Type, Data = Data, Scope = Scope, Ttl = Ttl }; }
        public string Line { get { return Name + "|" + Type + "|" + Data + "|" + Ttl.ToString(CultureInfo.InvariantCulture) + "|" + Scope; } }
        public static DnsRec Parse(string l) { var p = l.Split('|'); var r = new DnsRec { Name = p[0], Type = p[1] }; int i = p.Length - 4; r.Data = string.Join("|", p.Skip(2).Take(i).ToArray()); r.Ttl = int.Parse(p[p.Length - 2], CultureInfo.InvariantCulture); r.Scope = p[p.Length - 1]; return r; }
    }

    public class DnsZone
    {
        public string Name = "", Type = "Primary", ReplicationScope = "", DynamicUpdate = "None", Transfers = "NoTransfer";
        public bool Signed, Aging;
        public readonly List<string> Masters = new List<string>(), SecondaryServers = new List<string>(), Scopes = new List<string>();
        public readonly List<DnsRec> Records = new List<DnsRec>();
        public DateTime Loaded; public bool Expired;
        public bool AdIntegrated { get { return ReplicationScope.Length > 0; } }
        public bool Reverse { get { return Name.EndsWith(".in-addr.arpa", StringComparison.OrdinalIgnoreCase); } }
        public bool Holds(string fqdn) { return WsText.Eq(fqdn, Name) || fqdn.EndsWith("." + Name, StringComparison.OrdinalIgnoreCase); }
        public string Rel(string fqdn) { return WsText.Eq(fqdn, Name) ? "@" : fqdn.Substring(0, fqdn.Length - Name.Length - 1); }
        public string Fqdn(string rel) { return rel == "@" ? Name : rel + "." + Name; }

        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.Append("type=" + Type + "\ndyn=" + DynamicUpdate + "\nxfr=" + Transfers + "\nsigned=" + Signed + "\naging=" + Aging + "\n");
            sb.Append("masters=" + string.Join(",", Masters.ToArray()) + "\nsecondaries=" + string.Join(",", SecondaryServers.ToArray()) + "\nscopes=" + string.Join(",", Scopes.ToArray()) + "\n");
            foreach (var r in Records) sb.Append("rr=" + r.Line + "\n");
            return sb.ToString();
        }
        public static DnsZone FromAd(WsAdObj o)
        {
            var z = new DnsZone { Name = o.Name, ReplicationScope = o.Get("dnsScope") };
            foreach (var line in o.Get("zoneData").Split('\n'))
            {
                int eq = line.IndexOf('='); if (eq < 0) continue;
                string k = line.Substring(0, eq), v = line.Substring(eq + 1);
                switch (k)
                {
                    case "type": z.Type = v; break; case "dyn": z.DynamicUpdate = v; break; case "xfr": z.Transfers = v; break;
                    case "signed": z.Signed = v == "True"; break; case "aging": z.Aging = v == "True"; break;
                    case "masters": if (v.Length > 0) z.Masters.AddRange(v.Split(',')); break;
                    case "secondaries": if (v.Length > 0) z.SecondaryServers.AddRange(v.Split(',')); break;
                    case "scopes": if (v.Length > 0) z.Scopes.AddRange(v.Split(',')); break;
                    case "rr": z.Records.Add(DnsRec.Parse(v)); break;
                }
            }
            return z;
        }
        public DnsZone Copy()
        {
            var z = new DnsZone { Name = Name, Type = Type, ReplicationScope = ReplicationScope, DynamicUpdate = DynamicUpdate, Transfers = Transfers, Signed = Signed, Aging = Aging, Loaded = Loaded, Expired = Expired };
            z.Masters.AddRange(Masters); z.SecondaryServers.AddRange(SecondaryServers); z.Scopes.AddRange(Scopes);
            foreach (var r in Records) z.Records.Add(r.Clone());
            return z;
        }
    }

    public class DnsPolicy
    {
        public string Name = "", Action = "ALLOW", Zone = "", ClientSubnet = "", Fqdn = "", TimeOfDay = "", ZoneScope = "";
        public int Order = 1;
        public bool Enabled = true;
    }

    public class WsDnsServer
    {
        public WsServer S;
        public readonly List<DnsZone> FileZones = new List<DnsZone>();
        public readonly List<string> Forwarders = new List<string>();
        public bool Recursion = true, UseRootHints = true;
        public readonly Dictionary<string, List<string>> ClientSubnets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        public readonly List<DnsPolicy> Policies = new List<DnsPolicy>();
        public bool Scavenging;
        public bool Up { get { return S.Running && S.Has("DNS") && S.SvcRunning("DNS"); } }

        /// <summary>Every zone this server loads: its own, plus the directory-integrated ones if it is a DC running DNS.</summary>
        public List<DnsZone> Zones
        {
            get
            {
                var l = new List<DnsZone>(FileZones);
                var d = S.Lab.Dir(S.Name);
                if (d != null) foreach (var o in S.Lab.AdZones(d)) l.Add(DnsZone.FromAd(o));
                return l;
            }
        }
        public DnsZone Zone(string name) { return Zones.FirstOrDefault(z => WsText.Eq(z.Name, (name ?? "").TrimEnd('.'))); }
        /// <summary>The most specific zone (of any kind) that holds a name.</summary>
        public DnsZone ZoneFor(string fqdn) { return Zones.Where(z => z.Holds(fqdn)).OrderByDescending(z => z.Name.Length).FirstOrDefault(); }
    }
}
