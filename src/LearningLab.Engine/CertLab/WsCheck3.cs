// Cert Lab - the roots of the AZ-800 checks for networking, files, storage, Hyper-V and the Azure side: dns, resolve,
// dhcp, lease, vm, vswitch, vhd, container, images, share, ntfs, access, pool, vdisk, disk, volume, dedup, feature,
// service, namespace, dfsresolve, dfsr, file, arc, azure, cloud, signin, connect, filesync, wac, updates, hotfix.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public static partial class WsCheck
    {
        static WsVal Yn(bool b) { return WsVal.T(WsText.Yn(b)); }
        static WsVal N(long n) { return WsVal.T(n.ToString(CultureInfo.InvariantCulture)); }

        static partial void RootNetPart(WsLab lab, string dc, string root, Dictionary<string, string> a, string ix, ref WsVal v)
        {
            string s0 = A(a, "server", 0);
            var srv = s0 == null ? null : lab.Server(s0);
            switch (root)
            {
                case "dns":
                    {
                        var ds = srv == null ? null : srv.Dns; if (ds == null) return;
                        v = WsVal.O(srv.Name, (m, i) =>
                        {
                            switch (m)
                            {
                                case "zone": { var z = ds.Zone(i); return z == null ? null : Zone(z); }
                                case "zones": return WsVal.L(ds.Zones.Select(z => z.Name));
                                case "forwarders": return WsVal.L(ds.Forwarders);
                                case "recursion": return Yn(ds.Recursion);
                                case "roothints": return Yn(ds.UseRootHints);
                                case "conditional": { var z = ds.Zone(i); return z == null || z.Type != "Forwarder" ? null : WsVal.L(z.Masters); }
                                case "subnets": return WsVal.L(ds.ClientSubnets.Keys);
                                case "subnet": { List<string> l; return ds.ClientSubnets.TryGetValue(i ?? "", out l) ? WsVal.L(l) : null; }
                                case "policy": { var p = ds.Policies.FirstOrDefault(x => WsText.Eq(x.Name, i)); return p == null ? null : WsVal.O(p.Name, (mm, ii) => mm == "action" ? WsVal.T(p.Action) : mm == "zonescope" ? WsVal.T(p.ZoneScope) : mm == "clientsubnet" ? WsVal.T(p.ClientSubnet) : mm == "zone" ? WsVal.T(p.Zone) : null); }
                                case "running": return Yn(ds.Up);
                            }
                            return null;
                        });
                        return;
                    }
                case "resolve":
                    {
                        string name = A(a, "name", 1) ?? A(a, "name", 0), type = A(a, "type", 2) ?? "A", client = A(a, "client", 9), server = A(a, "server", 9);
                        var cm = lab.M(client ?? "CL1");
                        string err, fq;
                        var ans = lab.ClientResolve(cm, name, type, server, out err, out fq);
                        if (err != null) { v = WsVal.T(err == "NXDOMAIN" ? "NXDOMAIN" : "FAILED"); return; }
                        v = WsVal.L(ans.Where(x => x.Section == "Answer" && WsText.Eq(x.Type, type.ToUpperInvariant() == "A_AAAA" ? "A" : type)).Select(x => x.Data));
                        if (v.List.Count == 1) v = WsVal.T(v.List[0]);
                        return;
                    }
                case "dhcp":
                    {
                        var d = srv == null ? null : srv.Dhcp; if (d == null) return;
                        v = WsVal.O(srv.Name, (m, i) =>
                        {
                            switch (m)
                            {
                                case "authorized": return Yn(d.Authorized);
                                case "running": return Yn(d.Up);
                                case "scopes": return WsVal.L(d.Scopes.Select(x => x.ScopeId));
                                case "scope": { var sc = d.Scope(i); return sc == null ? null : Scope(d, sc); }
                                case "option": { int id; int.TryParse(i, out id); return WsVal.T(d.Option(null, id)); }
                                case "failover": { var f = d.Failovers.FirstOrDefault(x => i == null || WsText.Eq(x.Name, i)); return f == null ? null : WsVal.O(f.Name, (mm, ii) => mm == "mode" ? WsVal.T(f.Mode) : mm == "partner" ? WsVal.T(f.Partner(srv.Name)) : mm == "scopes" ? WsVal.L(f.ScopeIds) : mm == "percent" ? N(f.LoadBalancePercent) : mm == "reserve" ? N(f.ReservePercent) : mm == "mclt" ? N((long)f.Mclt.TotalMinutes) : mm == "secret" ? Yn(f.SharedSecret.Length > 0) : null); }
                            }
                            return null;
                        });
                        return;
                    }
                case "lease":
                    {
                        var c = lab.DhcpClient(ix ?? A(a, "client", 0)); if (c == null) return;
                        v = WsVal.O(c.Ip, (m, i) => m == "ip" ? WsVal.T(c.Ip) : m == "server" ? WsVal.T(c.Server) : m == "apipa" ? Yn(c.Note == "APIPA") : m == "dns" ? WsVal.L(lab.M(c.Name) == null ? new List<string>() : lab.M(c.Name).DnsFor(lab.M(c.Name).Eth.IfIndex)) : m == "gateway" ? WsVal.T(lab.M(c.Name) == null ? "" : lab.M(c.Name).DefaultGateway(lab.M(c.Name).Eth.IfIndex) ?? "") : null);
                        return;
                    }
                case "vm": case "vswitch": case "vhd": case "container": case "images": case "vmhost":
                    {
                        var h = (srv ?? lab.Server("HV1")).Hv; if (h == null) return;
                        if (root == "images") { v = WsVal.L(h.Images); return; }
                        if (root == "vmhost") { v = WsVal.O(h.S.Name, (m, i) => m == "enhancedsession" ? Yn(h.EnhancedSession) : m == "vmpath" ? WsVal.T(h.VmPath) : m == "vhdpath" ? WsVal.T(h.VhdPath) : null); return; }
                        if (root == "vswitch") { var sw = h.Switch(ix); if (sw != null) v = WsVal.O(sw.Name, (m, i) => m == "type" ? WsVal.T(sw.Type) : m == "adapter" ? WsVal.T(sw.Adapter) : m == "management" ? Yn(sw.ManagementOs) : null); return; }
                        if (root == "vhd") { var x = h.Vhd(ix); if (x != null) v = WsVal.O(x.Path, (m, i) => m == "type" ? WsVal.T(x.Type) : m == "format" ? WsVal.T(x.Format) : m == "sizegb" ? N(x.Size >> 30) : m == "parent" ? WsVal.T(x.Parent) : null); return; }
                        if (root == "container") { var ct = h.Container(ix); if (ct != null) v = WsVal.O(ct.Name, (m, i) => m == "status" ? WsVal.T(ct.Status.StartsWith("Up", StringComparison.Ordinal) ? "Running" : "Exited") : m == "isolation" ? WsVal.T(ct.Isolation) : m == "image" ? WsVal.T(ct.Image) : m == "ports" ? WsVal.T(ct.Ports) : null); return; }
                        var vm = h.Vm(ix); if (vm == null) return;
                        v = WsVal.O(vm.Name, (m, i) =>
                        {
                            switch (m)
                            {
                                case "state": return WsVal.T(vm.State);
                                case "generation": return N(vm.Generation);
                                case "memorygb": return WsVal.T((vm.Memory / (double)(1L << 30)).ToString("0.##", CultureInfo.InvariantCulture));
                                case "dynamic": return Yn(vm.Dynamic);
                                case "nested": return Yn(vm.Nested);
                                case "cpus": return N(vm.Cpus);
                                case "switch": return WsVal.T(vm.Nics.Count == 0 ? "" : vm.Nics[0].Switch);
                                case "spoofing": return WsVal.T(vm.Nics.Count == 0 ? "Off" : vm.Nics[0].Spoofing);
                                case "checkpoints": return WsVal.L(vm.Checkpoints.Select(c => c.Name));
                                case "checkpointtype": return WsVal.T(vm.CheckpointType);
                                case "current": return WsVal.T(vm.Current);
                                case "disks": return WsVal.L(vm.Disks);
                                case "secureboot": return Yn(vm.SecureBoot);
                                case "startaction": return WsVal.T(vm.StartAction);
                                case "stopaction": return WsVal.T(vm.StopAction);
                                case "integration": { bool on; return vm.Integration.TryGetValue(i ?? "", out on) ? Yn(on) : null; }
                            }
                            return null;
                        });
                        return;
                    }
            }
            RootFiles(lab, srv, root, a, ix, ref v);
        }

        static WsVal Zone(DnsZone z)
        {
            return WsVal.O(z.Name, (m, i) =>
            {
                switch (m)
                {
                    case "type": return WsVal.T(z.Type);
                    case "adintegrated": return Yn(z.AdIntegrated);
                    case "scope": case "replication": return WsVal.T(z.ReplicationScope);
                    case "dynamic": return WsVal.T(z.DynamicUpdate);
                    case "transfers": return WsVal.T(z.Transfers);
                    case "secondaries": return WsVal.L(z.SecondaryServers);
                    case "masters": return WsVal.L(z.Masters);
                    case "signed": return Yn(z.Signed);
                    case "aging": return Yn(z.Aging);
                    case "scopes": return WsVal.L(z.Scopes);
                    case "loaded": return Yn(!z.Expired && (z.Type != "Secondary" || z.Records.Count > 0));
                    case "record":
                        {
                            var p = (i ?? "").Split('/'); string n = p[0], t = p.Length > 1 ? p[1] : "A", sc = p.Length > 2 ? p[2] : "";
                            var l = z.Records.Where(r => WsText.Eq(r.Name, n) && WsText.Eq(r.Type, t) && WsText.Eq(r.Scope, sc)).Select(r => r.Data).ToList();
                            return l.Count == 0 ? null : l.Count == 1 ? WsVal.T(l[0]) : WsVal.L(l);
                        }
                    case "records": return WsVal.L(z.Records.Select(r => r.Name + "/" + r.Type));
                }
                return null;
            });
        }
        static WsVal Scope(WsDhcpServer d, DhcpScope sc)
        {
            return WsVal.O(sc.ScopeId, (m, i) =>
            {
                switch (m)
                {
                    case "state": return WsVal.T(sc.State);
                    case "name": return WsVal.T(sc.Name);
                    case "start": return WsVal.T(sc.Start);
                    case "end": return WsVal.T(sc.End);
                    case "leasedays": return WsVal.T(sc.Lease.TotalDays.ToString(CultureInfo.InvariantCulture));
                    case "option": { int id; int.TryParse(i, out id); return WsVal.T(d.Option(sc, id)); }
                    case "exclusions": return WsVal.L(sc.Exclusions.Select(x => x[0] + "-" + x[1]));
                    case "reservations": return WsVal.L(sc.Reservations.Select(x => x.Ip));
                    case "reservation": { var r = sc.Reservations.FirstOrDefault(x => x.Ip == i || WsText.Eq(x.Name, i)); return r == null ? null : WsVal.O(r.Ip, (mm, ii) => mm == "mac" ? WsVal.T(r.Mac) : mm == "name" ? WsVal.T(r.Name) : null); }
                    case "leases": return WsVal.L(sc.Leases.Select(x => x.Ip));
                    case "failover": return WsVal.T(sc.Failover);
                }
                return null;
            });
        }
        static partial void RootFilesPart(WsLab lab, WsServer srv, string root, Dictionary<string, string> a, string ix, ref WsVal v);
        static void RootFiles(WsLab lab, WsServer srv, string root, Dictionary<string, string> a, string ix, ref WsVal v) { RootFilesPart(lab, srv, root, a, ix, ref v); }
    }
}
