// Network Lab - IPv4 and MAC address helpers. Addresses are kept as uint (192.168.1.1 = 0xC0A80101).
using System;
using System.Globalization;

namespace NetworkLab
{
    public static class Ip
    {
        public const uint Broadcast = 0xFFFFFFFF;

        public static bool TryParse(string s, out uint ip)
        {
            ip = 0;
            if (string.IsNullOrEmpty(s)) return false;
            string[] p = s.Trim().Split('.');
            if (p.Length != 4) return false;
            for (int i = 0; i < 4; i++)
            {
                int v;
                if (p[i].Length == 0 || p[i].Length > 3 || !int.TryParse(p[i], NumberStyles.None, CultureInfo.InvariantCulture, out v) || v > 255) return false;
                ip = (ip << 8) | (uint)v;
            }
            return true;
        }
        public static uint Parse(string s) { uint v; if (!TryParse(s, out v)) throw new FormatException("Not an IPv4 address: " + s); return v; }
        public static string Str(uint ip) { return (ip >> 24) + "." + ((ip >> 16) & 255) + "." + ((ip >> 8) & 255) + "." + (ip & 255); }

        public static uint MaskOf(int prefix) { if (prefix <= 0) return 0; if (prefix >= 32) return 0xFFFFFFFF; return 0xFFFFFFFF << (32 - prefix); }
        public static int PrefixOf(uint mask) { int n = 0; while (n < 32 && (mask & (0x80000000u >> n)) != 0) n++; return n; }
        public static bool IsValidMask(uint mask) { return MaskOf(PrefixOf(mask)) == mask; }
        public static uint Network(uint ip, uint mask) { return ip & mask; }
        public static uint BroadcastOf(uint ip, uint mask) { return (ip & mask) | ~mask; }
        public static bool SameNet(uint a, uint b, uint mask) { return (a & mask) == (b & mask); }

        /// <summary>A mask written as 255.255.255.0, /24 or 24.</summary>
        public static bool TryParseMask(string s, out uint mask)
        {
            mask = 0;
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim();
            if (s.StartsWith("/")) s = s.Substring(1);
            int p;
            if (s.IndexOf('.') < 0 && int.TryParse(s, out p) && p >= 0 && p <= 32) { mask = MaskOf(p); return true; }
            return TryParse(s, out mask) && IsValidMask(mask);
        }

        public static int ClassfulPrefix(uint ip)
        {
            uint a = ip >> 24;
            if (a < 128) return 8;
            if (a < 192) return 16;
            return 24;
        }
        public static bool IsPrivate(uint ip)
        {
            return (ip >> 24) == 10 || (ip >> 20) == 0xAC1 || (ip >> 16) == 0xC0A8;
        }
        public static bool IsMulticast(uint ip) { return (ip >> 28) == 0xE; }
        public static string Cidr(uint net, uint mask) { return Str(net) + "/" + PrefixOf(mask); }
        public static uint Wildcard(uint mask) { return ~mask; }
    }

    public static class Mac
    {
        public const string Broadcast = "FFFF.FFFF.FFFF";
        static int counter;
        /// <summary>A made-up MAC for a new port: 0060.xxxx.xxxx, unique within the run (seeded per project).</summary>
        public static string Next(Random r)
        {
            counter++;
            return string.Format("00{0:X2}.{1:X4}.{2:X4}", 0x10 + r.Next(0, 0xE0), r.Next(0, 0x10000), r.Next(0, 0x10000));
        }
        public static bool IsBroadcast(string m) { return m == Broadcast; }
        public static bool IsMulticast(string m) { return m.StartsWith("0100.5E", StringComparison.OrdinalIgnoreCase) || m == Broadcast; }
        /// <summary>Windows style: 00-10-AB-CD-EF-01.</summary>
        public static string Dashed(string m)
        {
            string h = m.Replace(".", "");
            if (h.Length != 12) return m;
            return h.Substring(0, 2) + "-" + h.Substring(2, 2) + "-" + h.Substring(4, 2) + "-" + h.Substring(6, 2) + "-" + h.Substring(8, 2) + "-" + h.Substring(10, 2);
        }
        /// <summary>Linux / macOS style: 00:10:ab:cd:ef:01.</summary>
        public static string Colon(string m) { return Dashed(m).Replace('-', ':').ToLowerInvariant(); }
        public static string ForIp(uint group) { return "0100.5E" + ((group >> 16) & 0x7F).ToString("X2") + "." + (group & 0xFFFF).ToString("X4"); }
    }
}
