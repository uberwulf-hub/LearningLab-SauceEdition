// Cert Lab - the Storage module on the AZ-800 machines: physical disks, storage pools (with the primordial pool),
// virtual disks with their resiliency, disks, partitions and volumes (Initialize-Disk, New-Partition, Format-Volume,
// New-Volume). The pipeline Get-VirtualDisk | Get-Disk | Initialize-Disk -PassThru | New-Partition | Format-Volume works.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class PsCmdlets
    {
        const string StorMod = "Storage";
        static CmdletDef StCmd(string name, string spec, Action<CmdCtx, object> run) { var d = Cdxml(Add(name, StorMod, spec + (spec.Length > 0 ? "; " : "") + "CimSession:object; PassThru:switch", run)); d.Available = HasWs; return d; }
        static void StOk(CmdCtx c, string err, object t) { if (err != null) throw c.Fail(err, "StorageWMI", "InvalidOperation", t, "Microsoft.Management.Infrastructure.CimException"); }
        static PsObj StCim(string cls) { return new PsObj("Microsoft.Management.Infrastructure.CimInstance#ROOT/Microsoft/Windows/Storage/" + cls, "Microsoft.Management.Infrastructure.CimInstance#" + cls, "Microsoft.Management.Infrastructure.CimInstance"); }
        static PsObj PhysObj(WsServer s, WsDisk d)
        {
            var p = StCim("MSFT_PhysicalDisk"); p.Base = d;
            p.Val("Number", "System.Nullable[uint32]", (uint)d.Number); p.Val("DeviceId", "string", d.Number.ToString(CultureInfo.InvariantCulture)); p.Val("FriendlyName", "string", d.FriendlyName); p.Val("SerialNumber", "string", d.Serial); p.Val("MediaType", "string", d.Media);
            p.Val("CanPool", "bool", d.CanPool); p.Val("OperationalStatus", "string", "OK"); p.Val("HealthStatus", "string", d.Health); p.Val("Usage", "string", d.Pool.Length > 0 ? "Auto-Select" : "Auto-Select"); p.Val("Size", "uint64", (ulong)d.Size);
            p.Val("CannotPoolReason", "string", d.CanPool ? "" : d.Pool.Length > 0 ? "In a Pool" : d.Number == 0 ? "Insufficient Capacity" : "Not Healthy"); p.Val("BusType", "string", d.Bus);
            p.DefaultDisplay = new[] { "Number", "FriendlyName", "SerialNumber", "MediaType", "CanPool", "OperationalStatus", "HealthStatus", "Usage", "Size" };
            return p;
        }
        static PsObj PoolObj(WsServer s, WsPool pool, bool primordial)
        {
            var p = StCim("MSFT_StoragePool"); p.Base = pool;
            long size = primordial ? s.Disks.Where(d => d.CanPool || d.Pool.Length > 0).Sum(d => d.Size) : s.PoolSize(pool);
            p.Val("FriendlyName", "string", primordial ? "Primordial" : pool.Name); p.Val("OperationalStatus", "string", "OK"); p.Val("HealthStatus", "string", "Healthy"); p.Val("IsPrimordial", "bool", primordial); p.Val("IsReadOnly", "bool", false);
            p.Val("Size", "uint64", (ulong)size); p.Val("AllocatedSize", "uint64", (ulong)(primordial ? s.Disks.Where(d => d.Pool.Length > 0).Sum(d => d.Size) : s.PoolUsed(pool)));
            p.DefaultDisplay = new[] { "FriendlyName", "OperationalStatus", "HealthStatus", "IsPrimordial", "IsReadOnly", "Size", "AllocatedSize" };
            return p;
        }
        static PsObj VDiskObj(WsServer s, WsVDisk v)
        {
            var p = StCim("MSFT_VirtualDisk"); p.Base = v;
            p.Val("FriendlyName", "string", v.Name); p.Val("ResiliencySettingName", "string", v.Resiliency); p.Val("FaultDomainRedundancy", "uint16", (ushort)(v.Resiliency == "Mirror" ? v.Copies - 1 : v.Resiliency == "Parity" ? 1 : 0));
            p.Val("OperationalStatus", "string", "OK"); p.Val("HealthStatus", "string", "Healthy"); p.Val("Size", "uint64", (ulong)v.Size); p.Val("FootprintOnPool", "uint64", (ulong)v.Footprint);
            p.Val("StorageEfficiency", "string", (100.0 / WsServer.Factor(v)).ToString("0.00", CultureInfo.InvariantCulture) + "%"); p.Val("ProvisioningType", "string", v.Provisioning); p.Val("NumberOfDataCopies", "uint16", (ushort)v.Copies); p.Val("NumberOfColumns", "uint16", (ushort)v.Columns);
            p.DefaultDisplay = new[] { "FriendlyName", "ResiliencySettingName", "FaultDomainRedundancy", "OperationalStatus", "HealthStatus", "Size", "FootprintOnPool", "StorageEfficiency" };
            return p;
        }
        static PsObj DiskObj(WsServer s, WsDisk d)
        {
            var p = StCim("MSFT_Disk"); p.Base = d;
            p.Val("Number", "uint32", (uint)d.Number); p.Val("FriendlyName", "string", d.FriendlyName); p.Val("SerialNumber", "string", d.Serial); p.Val("HealthStatus", "string", "Healthy"); p.Val("OperationalStatus", "string", d.Offline ? "Offline" : "Online");
            p.Val("TotalSize", "uint64", (ulong)d.Size); p.Val("PartitionStyle", "string", d.Style); p.Val("IsBoot", "bool", d.Number == 0); p.Val("IsSystem", "bool", d.Number == 0);
            p.DefaultDisplay = new[] { "Number", "FriendlyName", "SerialNumber", "HealthStatus", "OperationalStatus", "TotalSize", "PartitionStyle" };
            return p;
        }
        static PsObj PartObj(WsDisk d, WsPart pt)
        {
            var p = StCim("MSFT_Partition"); p.Base = pt;
            p.Val("DiskNumber", "uint32", (uint)d.Number); p.Val("PartitionNumber", "uint32", (uint)pt.Number); p.Val("DriveLetter", "char", pt.Letter.Length > 0 ? (object)pt.Letter[0] : (char)0);
            p.Val("Offset", "uint64", 16777216UL); p.Val("Size", "uint64", (ulong)pt.Size); p.Val("Type", "string", pt.Letter.Length > 0 || pt.FileSystem == "" ? "Basic" : "System");
            p.DefaultDisplay = new[] { "PartitionNumber", "DriveLetter", "Offset", "Size", "Type" };
            return p;
        }
        static PsObj VolumeObj(WsServer s, WsPart pt)
        {
            var p = StCim("MSFT_Volume"); p.Base = pt;
            long used = 0; WinFile root;
            if (pt.Letter == "C") used = s.M.DiskSizeC - s.M.DiskFreeC;
            else if (s.M.ExtraDrives.TryGetValue(pt.Letter, out root)) { Action<WinFile> walk = null; walk = f => { foreach (var k in f.Kids.Values) { if (k.IsDir) walk(k); else used += Math.Max(0, k.FakeSize); } }; walk(root); }
            WsDedup dd; if (s.Dedup.TryGetValue(pt.Letter, out dd)) used = Math.Max(0, used - dd.Saved);
            p.Val("DriveLetter", "char", pt.Letter.Length > 0 ? (object)pt.Letter[0] : null); p.Val("FriendlyName", "string", pt.Label); p.Val("FileSystemType", "string", pt.FileSystem.Length == 0 ? "Unknown" : pt.FileSystem);
            p.Val("DriveType", "string", "Fixed"); p.Val("HealthStatus", "string", "Healthy"); p.Val("OperationalStatus", "string", "OK"); p.Val("SizeRemaining", "uint64", (ulong)Math.Max(0, pt.Size - used)); p.Val("Size", "uint64", (ulong)pt.Size);
            p.DefaultDisplay = new[] { "DriveLetter", "FriendlyName", "FileSystemType", "DriveType", "HealthStatus", "OperationalStatus", "SizeRemaining", "Size" };
            return p;
        }
        static WsDisk DiskFrom(CmdCtx c, WsServer s, object input)
        {
            var po = PsConv.Base(input) as PsObj;
            if (po != null && po.Base is WsDisk) return (WsDisk)po.Base;
            if (po != null && po.Base is WsVDisk) return s.Disk(((WsVDisk)po.Base).DiskNumber);
            int n = c.Has("Number") ? c.Int("Number", -1) : c.Has("DiskNumber") ? c.Int("DiskNumber", -1) : -1;
            var d = s.Disk(n);
            if (d == null) throw c.Fail("No MSFT_Disk objects found with property 'Number' equal to '" + n + "'.", "CmdletizationQuery_NotFound_Number", "ObjectNotFound", n, "Microsoft.Management.Infrastructure.CimException");
            return d;
        }

        static partial void RegWsStorage()
        {
            foreach (var t in new[] { "MSFT_PhysicalDisk|Number:6,FriendlyName:22,SerialNumber:12,MediaType:9,CanPool:7,OperationalStatus:17,HealthStatus:12,Usage:11,Size:9", "MSFT_StoragePool|FriendlyName:12,OperationalStatus:17,HealthStatus:12,IsPrimordial:12,IsReadOnly:10,Size:9,AllocatedSize:13", "MSFT_VirtualDisk|FriendlyName:12,ResiliencySettingName:21,FaultDomainRedundancy:21,OperationalStatus:17,HealthStatus:12,Size:9,FootprintOnPool:15,StorageEfficiency:17", "MSFT_Disk|Number:6,FriendlyName:22,SerialNumber:12,HealthStatus:12,OperationalStatus:17,TotalSize:10,PartitionStyle:14", "MSFT_Volume|DriveLetter:11,FriendlyName:12,FileSystemType:14,DriveType:9,HealthStatus:12,OperationalStatus:17,SizeRemaining:13,Size:9" })
            {
                var p = t.Split('|');
                PsFormat.Table("Microsoft.Management.Infrastructure.CimInstance#" + p[0], p[1].Split(',').Select(x => { var q = x.Split(':'); bool size = q[0].Contains("Size") || q[0] == "SizeRemaining" || q[0] == "AllocatedSize" || q[0] == "FootprintOnPool"; var col = new FmtCol { Header = q[0], Prop = q[0], Width = int.Parse(q[1], CultureInfo.InvariantCulture), Right = size || q[0] == "Number" }; if (size) { string prop = q[0]; col.Get = o => { var po = PsConv.Base(o) as PsObj; var v = po == null ? null : po.Get(prop); return v == null ? "" : SizeText(Convert.ToDouble(v, CultureInfo.InvariantCulture)); }; } return col; }).ToArray());
            }
            StCmd("Get-PhysicalDisk", "FriendlyName:string[]@0; CanPool:bool; SerialNumber:string[]; StoragePool:object pipe", (c, o) =>
            {
                var s = c.M.WsSrv; var pool = PsConv.Base(c.Get("StoragePool")) as PsObj;
                foreach (var d in s.Disks.Where(x => x.VirtualDisk.Length == 0 && (!c.Has("CanPool") || x.CanPool == PsConv.ToBool(c.Get("CanPool"))) && (!c.Has("FriendlyName") || c.Strs("FriendlyName").Any(n => WsText.Like(x.FriendlyName, n))) && (pool == null || pool.Base is WsPool && WsText.Eq(x.Pool, ((WsPool)pool.Base).Name))))
                    c.Out(PhysObj(s, d));
            });
            StCmd("Get-StorageSubSystem", "FriendlyName:string[]@0", (c, o) => { var p = StCim("MSFT_StorageSubSystem"); p.Val("FriendlyName", "string", "Windows Storage on " + c.M.Name); p.Val("HealthStatus", "string", "Healthy"); p.Val("OperationalStatus", "string", "OK"); c.Out(p); });
            StCmd("Get-StoragePool", "FriendlyName:string[]@0; IsPrimordial:bool", (c, o) =>
            {
                var s = c.M.WsSrv;
                if ((!c.Has("IsPrimordial") || PsConv.ToBool(c.Get("IsPrimordial"))) && (!c.Has("FriendlyName") || c.Strs("FriendlyName").Any(n => WsText.Like("Primordial", n)))) c.Out(PoolObj(s, null, true));
                if (c.Has("IsPrimordial") && PsConv.ToBool(c.Get("IsPrimordial"))) return;
                foreach (var p in s.Pools.Where(x => !c.Has("FriendlyName") || c.Strs("FriendlyName").Any(n => WsText.Like(x.Name, n)))) c.Out(PoolObj(s, p, false));
            });
            StCmd("New-StoragePool", "FriendlyName:string !; StorageSubSystemFriendlyName:string[] !; PhysicalDisks:object[] !; ResiliencySettingNameDefault:string; ProvisioningTypeDefault:string", (c, o) =>
            {
                var s = c.M.WsSrv;
                var nums = c.Objs("PhysicalDisks").Select(x => { var po = PsConv.Base(x) as PsObj; return po != null && po.Base is WsDisk ? ((WsDisk)po.Base).Number : -1; }).ToList();
                StOk(c, s.NewPool(c.Str("FriendlyName"), nums), c.Str("FriendlyName"));
                c.Out(PoolObj(s, s.Pool(c.Str("FriendlyName")), false));
            });
            StCmd("Add-PhysicalDisk", "StoragePoolFriendlyName:string !; PhysicalDisks:object[] !", (c, o) =>
            {
                var s = c.M.WsSrv; var pool = s.Pool(c.Str("StoragePoolFriendlyName"));
                if (pool == null) StOk(c, "No storage pool named " + c.Str("StoragePoolFriendlyName") + ".", null);
                foreach (var x in c.Objs("PhysicalDisks")) { var po = PsConv.Base(x) as PsObj; var d = po == null ? null : po.Base as WsDisk; if (d == null || !d.CanPool) StOk(c, "The physical disk can't be added to the pool.", null); d.Pool = pool.Name; pool.Disks.Add(d.Number); }
            });
            var rp = StCmd("Remove-StoragePool", "FriendlyName:string[]@0 !", (c, o) =>
            {
                var s = c.M.WsSrv;
                foreach (var n in c.Strs("FriendlyName")) { var p = s.Pool(n); if (p == null) StOk(c, "No storage pool named " + n + ".", n); if (s.VDisks.Any(v => WsText.Eq(v.Pool, p.Name))) StOk(c, "The storage pool still has virtual disks: remove them first.", n); if (!c.ShouldProcess(n, "Remove storage pool")) continue; foreach (var d in p.Disks) s.Disk(d).Pool = ""; s.Pools.Remove(p); }
            });
            rp.ShouldProcess = true; rp.ConfirmImpact = "High";
            RegWsStorage2();
        }
        static string SizeText(double b) { string[] u = { "B", "KB", "MB", "GB", "TB" }; int i = 0; while (b >= 1024 && i < 4) { b /= 1024; i++; } return (i == 0 ? b.ToString("0", CultureInfo.InvariantCulture) : b.ToString("0.##", CultureInfo.InvariantCulture)) + " " + u[i]; }
    }
}
