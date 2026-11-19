// Cert Lab - RIPEMD-160 for the pretend Get-FileHash -Algorithm RIPEMD160 (Windows PowerShell 5.1 offers it; .NET 10
// has no RIPEMD160Managed, so this is a small plain implementation of the published algorithm).
using System;

namespace CertLab
{
    public static class Ripemd160
    {
        static readonly int[] RL = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 7, 4, 13, 1, 10, 6, 15, 3, 12, 0, 9, 5, 2, 14, 11, 8, 3, 10, 14, 4, 9, 15, 8, 1, 2, 7, 0, 6, 13, 11, 5, 12, 1, 9, 11, 10, 0, 8, 12, 4, 13, 3, 7, 15, 14, 5, 6, 2, 4, 0, 5, 9, 7, 12, 2, 10, 14, 1, 3, 8, 11, 6, 15, 13 };
        static readonly int[] RR = { 5, 14, 7, 0, 9, 2, 11, 4, 13, 6, 15, 8, 1, 10, 3, 12, 6, 11, 3, 7, 0, 13, 5, 10, 14, 15, 8, 12, 4, 9, 1, 2, 15, 5, 1, 3, 7, 14, 6, 9, 11, 8, 12, 2, 10, 0, 4, 13, 8, 6, 4, 1, 3, 11, 15, 0, 5, 12, 2, 13, 9, 7, 10, 14, 12, 15, 10, 4, 1, 5, 8, 7, 6, 2, 13, 14, 0, 3, 9, 11 };
        static readonly int[] SL = { 11, 14, 15, 12, 5, 8, 7, 9, 11, 13, 14, 15, 6, 7, 9, 8, 7, 6, 8, 13, 11, 9, 7, 15, 7, 12, 15, 9, 11, 7, 13, 12, 11, 13, 6, 7, 14, 9, 13, 15, 14, 8, 13, 6, 5, 12, 7, 5, 11, 12, 14, 15, 14, 15, 9, 8, 9, 14, 5, 6, 8, 6, 5, 12, 9, 15, 5, 11, 6, 8, 13, 12, 5, 12, 13, 14, 11, 8, 5, 6 };
        static readonly int[] SR = { 8, 9, 9, 11, 13, 15, 15, 5, 7, 7, 8, 11, 14, 14, 12, 6, 9, 13, 15, 7, 12, 8, 9, 11, 7, 7, 12, 7, 6, 15, 13, 11, 9, 7, 15, 11, 8, 6, 6, 14, 12, 13, 5, 14, 13, 13, 7, 5, 15, 5, 8, 11, 14, 14, 6, 14, 6, 9, 12, 9, 12, 5, 15, 8, 8, 5, 12, 9, 12, 5, 14, 6, 8, 13, 6, 5, 15, 13, 11, 11 };
        static readonly uint[] KL = { 0x00000000, 0x5A827999, 0x6ED9EBA1, 0x8F1BBCDC, 0xA953FD4E };
        static readonly uint[] KR = { 0x50A28BE6, 0x5C4DD124, 0x6D703EF3, 0x7A6D76E9, 0x00000000 };

        static uint Rol(uint x, int n) { return (x << n) | (x >> (32 - n)); }
        static uint F(int j, uint x, uint y, uint z)
        {
            if (j < 16) return x ^ y ^ z;
            if (j < 32) return (x & y) | (~x & z);
            if (j < 48) return (x | ~y) ^ z;
            if (j < 64) return (x & z) | (y & ~z);
            return x ^ (y | ~z);
        }

        public static byte[] Hash(byte[] data)
        {
            data = data ?? new byte[0];
            long bits = (long)data.Length * 8;
            int padded = ((data.Length + 8) / 64 + 1) * 64;
            var m = new byte[padded];
            Array.Copy(data, m, data.Length);
            m[data.Length] = 0x80;
            for (int i = 0; i < 8; i++) m[padded - 8 + i] = (byte)(bits >> (8 * i));
            uint h0 = 0x67452301, h1 = 0xEFCDAB89, h2 = 0x98BADCFE, h3 = 0x10325476, h4 = 0xC3D2E1F0;
            var x = new uint[16];
            for (int off = 0; off < padded; off += 64)
            {
                for (int i = 0; i < 16; i++) x[i] = BitConverter.ToUInt32(new[] { m[off + 4 * i], m[off + 4 * i + 1], m[off + 4 * i + 2], m[off + 4 * i + 3] }, 0);
                uint al = h0, bl = h1, cl = h2, dl = h3, el = h4, ar = h0, br = h1, cr = h2, dr = h3, er = h4;
                for (int j = 0; j < 80; j++)
                {
                    uint t = Rol(al + F(j, bl, cl, dl) + x[RL[j]] + KL[j / 16], SL[j]) + el;
                    al = el; el = dl; dl = Rol(cl, 10); cl = bl; bl = t;
                    t = Rol(ar + F(79 - j, br, cr, dr) + x[RR[j]] + KR[j / 16], SR[j]) + er;
                    ar = er; er = dr; dr = Rol(cr, 10); cr = br; br = t;
                }
                uint tt = h1 + cl + dr; h1 = h2 + dl + er; h2 = h3 + el + ar; h3 = h4 + al + br; h4 = h0 + bl + cr; h0 = tt;
            }
            var r = new byte[20];
            uint[] hs = { h0, h1, h2, h3, h4 };
            for (int i = 0; i < 5; i++) for (int k = 0; k < 4; k++) r[4 * i + k] = (byte)(hs[i] >> (8 * k));
            return r;
        }
    }
}
