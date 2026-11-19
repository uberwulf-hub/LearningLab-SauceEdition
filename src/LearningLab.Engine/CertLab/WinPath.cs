// Cert Lab - file names in the pretend Windows machines' paths (C:\Users\Public\Riverbend-Bulk.ppkg). App Hub used
// System.IO.Path, which on Windows splits at \ and /; on Android and Linux it splits only at /, so the simulators use
// these, which follow Windows' rules on every platform.
using System;

namespace CertLab
{
    public static class WinPath
    {
        static int NameStart(string path)
        {
            int i = path.Length;
            while (--i >= 0) { char c = path[i]; if (c == '\\' || c == '/' || c == ':') break; }
            return i + 1;
        }

        public static string GetFileName(string path)
        {
            if (path == null) return null;
            return path.Substring(NameStart(path));
        }

        public static string GetFileNameWithoutExtension(string path)
        {
            if (path == null) return null;
            string name = GetFileName(path);
            int dot = name.LastIndexOf('.');
            return dot < 0 ? name : name.Substring(0, dot);
        }

        public static string GetExtension(string path)
        {
            if (path == null) return null;
            string name = GetFileName(path);
            int dot = name.LastIndexOf('.');
            return dot < 0 || dot == name.Length - 1 ? "" : name.Substring(dot);
        }
    }
}
