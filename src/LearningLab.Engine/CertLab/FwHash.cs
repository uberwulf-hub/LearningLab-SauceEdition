// Cert Lab - string hash codes as .NET Framework (64-bit) made them. App Hub's simulators derive pretend details from
// string.GetHashCode() (a seeded lab's random choices, device ids, serial numbers, hardware hashes), and the course
// text shows them. .NET 10 randomizes string hash codes in every process, so the same lab would differ from run to
// run and from the lessons: FwHash.Of gives the old, fixed values (the algorithm of .NET Framework 4.x on x64).
using System;

namespace CertLab
{
    public static class FwHash
    {
        public static int Of(string s)
        {
            if (s == null) throw new NullReferenceException();
            int hash1 = 5381, hash2 = hash1;
            int i = 0, n = s.Length;
            unchecked
            {
                while (i < n)
                {
                    int c = s[i];
                    if (c == 0) break;   // .NET Framework read the string up to a nul character
                    hash1 = ((hash1 << 5) + hash1) ^ c;
                    if (i + 1 >= n) break;
                    c = s[i + 1];
                    if (c == 0) break;
                    hash2 = ((hash2 << 5) + hash2) ^ c;
                    i += 2;
                }
                return hash1 + hash2 * 1566083941;
            }
        }

        /// <summary>A string's fixed hash; any other value's own hash code.</summary>
        public static int Of(object o)
        {
            var s = o as string;
            return s != null ? Of(s) : o.GetHashCode();
        }
    }
}
