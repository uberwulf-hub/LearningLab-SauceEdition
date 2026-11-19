// Cert Lab - the disks the AZ-800 machines start with: every machine has its system disk (C:); FS1 also has D: (the
// shares), E: (an archive volume full of copies, a good place for Data Deduplication) and four empty 100 GB disks for
// Storage Spaces; HV1 has D: for virtual machines.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CertLab
{
    public partial class WsLab
    {
        partial void SeedFilesMore()
        {
            const long G = 1L << 30;
            var t = new DateTime(2026, 1, 20, 9, 0, 0);
            foreach (var s in Servers.Values)
            {
                var sys = new WsDisk { Number = 0, FriendlyName = s.IsClient ? "Lumen NVMe 256GB" : "Virtual Disk", Bus = s.IsClient ? "NVMe" : "SAS", Media = s.IsClient ? "SSD" : "HDD", Size = s.IsClient ? 256 * G : 127 * G, Style = "GPT", Serial = "SYS" + s.Name };
                sys.Parts.Add(new WsPart { Number = 1, Size = 100L << 20, FileSystem = "FAT32" });
                sys.Parts.Add(new WsPart { Number = 2, Size = 16L << 20 });
                sys.Parts.Add(new WsPart { Number = 3, Letter = "C", Size = sys.Size - (700L << 20), FileSystem = "NTFS" });
                s.Disks.Add(sys);
                s.VolumeFs["C"] = "NTFS";
            }
            var fs = Server("FS1");
            Action<WsServer, int, long, string, string, string> data = (srv, n, size, letter, fsys, label) =>
            {
                var d = new WsDisk { Number = n, FriendlyName = "Virtual Disk", Size = size, Style = "GPT", Serial = "DATA" + n };
                d.Parts.Add(new WsPart { Number = 2, Letter = letter, Size = size - (128L << 20), FileSystem = fsys, Label = label });
                srv.Disks.Add(d); srv.VolumeFs[letter] = fsys;
            };
            data(fs, 1, 200 * G, "D", "NTFS", "Shares");
            data(fs, 2, 500 * G, "E", "NTFS", "Archive");
            for (int i = 3; i <= 6; i++) fs.Disks.Add(new WsDisk { Number = i, FriendlyName = "Lumen SAS HDD 100GB", Size = 100 * G, Serial = "LMN0" + (4180 + i) });
            // the archive: last year's reports, many of them copies of each other
            var m = fs.M;
            m.MakeDir("E:\\Archive", t);
            for (int y = 0; y < 3; y++)
                foreach (var dept in new[] { "Sales", "Finance", "HR" })
                    for (int q = 1; q <= 4; q++)
                    {
                        var f = m.File("E:\\Archive\\" + dept + "\\" + (2023 + y) + "\\Report Q" + q + ".pdf", q % 2 == 0 ? "tpl:report-template" : "rep:" + dept + y + q, t.AddDays(-400 + y * 120 + q));
                        f.FakeSize = (q % 2 == 0 ? 48L : 12L + q) << 20;
                        f.Created = f.Modified = f.Accessed = t.AddDays(-400 + y * 120 + q);
                    }
            foreach (var n in new[] { "Install media\\Server2022.iso", "Install media\\Server2022 (copy).iso", "Install media\\Old\\Server2022.iso" })
            { var f = m.File("E:\\Archive\\" + n, "iso:server2022", t.AddDays(-200)); f.FakeSize = 5L << 30; f.Created = f.Modified = f.Accessed = t.AddDays(-200); }
            var hv = Server("HV1");
            var root = new WinFile { Name = "D:", IsDir = true, Attr = 22, Created = t, Modified = t, Accessed = t };
            hv.M.ExtraDrives["D"] = root; DefaultRootAcl(hv, root);
            data(hv, 1, 1024 * G, "D", "ReFS", "VMs");
            foreach (var x in new[] { "D:\\Hyper-V\\Virtual Hard Disks", "D:\\Hyper-V\\Virtual Machines", "D:\\ISO" }) hv.M.MakeDir(x, t);
            var iso = hv.M.File("D:\\ISO\\WindowsServer2022.iso", "", t); iso.FakeSize = 5L << 30;
        }
    }
}
