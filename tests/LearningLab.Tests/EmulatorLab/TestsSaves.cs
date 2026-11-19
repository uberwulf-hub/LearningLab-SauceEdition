// Emulator Lab - checks that the person's LabOS disk (disk.img) is never lost: a LabFS disk as LabOS has always written
// it opens with its files, it is backed up when it changed, and a damaged disk is copied aside before LabOS could
// format it, with the newest good copy put back. Part of the group "emulator".
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace EmulatorLab
{
    public static partial class Tests
    {
        /// <summary>A LabFS disk built by hand from the layout in os\disk.s (version 1, the only one so far): superblock
        /// "LBFS", version 1, 64 slots of 128 sectors from sector 8, a 4-sector directory; one file, "notes".</summary>
        static byte[] LabFsV1Disk(string name, string text)
        {
            var d = new byte[Computer.DiskSectors * 512];
            Action<int, uint> put = (o, v) => { d[o] = (byte)v; d[o + 1] = (byte)(v >> 8); d[o + 2] = (byte)(v >> 16); d[o + 3] = (byte)(v >> 24); };
            put(0, LabFs.Magic); put(4, 1); put(8, 64); put(12, 128); put(16, 8); put(20, 4);
            var nm = Encoding.ASCII.GetBytes(name); Array.Copy(nm, 0, d, 512, nm.Length);
            var body = Encoding.ASCII.GetBytes(text);
            put(512 + 24, (uint)body.Length); d[512 + 28] = 1;
            Array.Copy(body, 0, d, 8 * 512, body.Length);
            return d;
        }

        static string ReadOnDisk(string appRoot, string disk, string name)
        {
            using (var c = new Computer(Path.Combine(appRoot, "os")))
            {
                c.AttachDisk(disk);
                var b = c.Fs.Read(name);
                return b == null ? null : Encoding.ASCII.GetString(b);
            }
        }

        static void DiskSaves(string appRoot, string tempDir)
        {
            log.Add("Saves (the LabOS disk):");
            string dir = Path.Combine(tempDir, "disk-saves");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            string disk = Path.Combine(dir, "disk.img");

            Check(Computer.PrepareDisk(disk) == "" && !File.Exists(disk), "no disk yet: nothing to do (LabOS formats a new one)");
            File.WriteAllBytes(disk, new byte[Computer.DiskSectors * 512]);
            Check(Computer.PrepareDisk(disk) == "" && Directory.GetFiles(dir).Length == 1, "an empty, never formatted disk is left for LabOS to format");

            File.WriteAllBytes(disk, LabFsV1Disk("notes", "Buy milk"));
            Check(ReadOnDisk(appRoot, disk, "notes") == "Buy milk", "a LabFS version 1 disk opens with its file");
            Check(Computer.PrepareDisk(disk) == "" && File.Exists(disk + ".bak1") && File.ReadAllBytes(disk + ".bak1").SequenceEqual(File.ReadAllBytes(disk)), "a LabFS disk is backed up as disk.img.bak1 before LabOS starts");
            Computer.PrepareDisk(disk);
            Check(!File.Exists(disk + ".bak2"), "...only when it changed since the last backup");
            using (var c = new Computer(Path.Combine(appRoot, "os"))) { c.AttachDisk(disk); c.Fs.Write("poem", Encoding.ASCII.GetBytes("Roses are red")); }
            Computer.PrepareDisk(disk);
            Check(File.Exists(disk + ".bak2") && ReadOnDisk(appRoot, disk, "poem") == "Roses are red", "...and after a change the earlier copy moves to .bak2");

            // damaged: the superblock is gone (LabOS would format the disk); copied aside, the newest LabFS backup put back
            byte[] bad = File.ReadAllBytes(disk);
            for (int i = 0; i < 512; i++) bad[i] = 0x5A;
            File.WriteAllBytes(disk, bad);
            string m = Computer.PrepareDisk(disk);
            var q = Directory.GetFiles(dir, "disk.img.unreadable-*");
            Check(m.Contains("was put back") && m.Contains(".unreadable-"), "a damaged disk: the person is told the copy was put back", m);
            Check(q.Length == 1 && File.ReadAllBytes(q[0]).SequenceEqual(bad), "...the damaged disk is copied aside byte for byte");
            Check(ReadOnDisk(appRoot, disk, "poem") == "Roses are red" && ReadOnDisk(appRoot, disk, "notes") == "Buy milk", "...and the files are back");

            // damaged and no backups: kept aside before LabOS formats a new one
            string dir2 = Path.Combine(dir, "nobackup"); Directory.CreateDirectory(dir2);
            string disk2 = Path.Combine(dir2, "disk.img");
            File.WriteAllBytes(disk2, bad);
            string m2 = Computer.PrepareDisk(disk2);
            var q2 = Directory.GetFiles(dir2, "disk.img.unreadable-*");
            Check(m2.Contains("new, empty disk") && q2.Length == 1 && File.ReadAllBytes(q2[0]).SequenceEqual(bad), "a damaged disk without backups is kept aside before LabOS formats a new one", m2);
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }
    }
}
