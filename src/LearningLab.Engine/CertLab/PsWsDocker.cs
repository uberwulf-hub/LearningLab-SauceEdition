// Cert Lab - a pretend docker command on HV1: version, info, pull, images, run, ps, stop, start, rm, rmi, inspect,
// exec (hostname only) and network ls. Images come from a pretend registry (nothing is downloaded).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public static partial class PsNatives
    {
        static int Docker(NativeCall c)
        {
            var s = c.M.WsSrv; var h = s == null ? null : s.Hv;
            if (h == null || !h.Docker) { c.Err("docker : The term 'docker' is not recognized as the name of a cmdlet, function, script file, or operable program."); return 1; }
            var a = c.Args;
            string verb = a.Count > 0 ? a[0].ToLowerInvariant() : "";
            Func<string, string> opt = n => { int i = a.FindIndex(x => x == n || x.StartsWith(n + "=", StringComparison.Ordinal)); if (i < 0) return null; return a[i].Contains("=") ? a[i].Substring(a[i].IndexOf('=') + 1) : i + 1 < a.Count ? a[i + 1] : null; };
            switch (verb)
            {
                case "version": c.L("Client: Docker Engine - Enterprise\n Version:           24.0.7\n OS/Arch:           windows/amd64\n\nServer: Docker Engine - Enterprise\n Engine:\n  Version:          24.0.7\n  OS/Arch:          windows/amd64\n  (Cert Lab's pretend engine)"); return 0;
                case "info": c.L("Containers: " + h.Containers.Count + "\n Running: " + h.Containers.Count(x => x.Status.StartsWith("Up", StringComparison.Ordinal)) + "\nImages: " + h.Images.Count + "\nServer Version: 24.0.7\nStorage Driver: windowsfilter\nDefault Isolation: process\nKernel Version: " + c.M.OsVersion + "\nOperating System: Microsoft Windows Server Version 21H2 (OS Build 20348)\nOSType: windows"); return 0;
                case "pull":
                    {
                        string img = a.Count > 1 ? a[1] : ""; if (!img.Contains(":")) img += ":latest";
                        if (!WsHvHost.Registry.Contains(img, StringComparer.OrdinalIgnoreCase)) { c.L("Error response from daemon: manifest for " + img + " not found: manifest unknown"); return 1; }
                        if (!h.Images.Contains(img, StringComparer.OrdinalIgnoreCase)) h.Images.Add(img);
                        c.L(img.Substring(img.IndexOf(':') + 1) + ": Pulling from " + img.Substring(0, img.IndexOf(':')).Replace("mcr.microsoft.com/", "") + "\nDigest: sha256:" + Math.Abs(FwHash.Of(img)).ToString("x8", CultureInfo.InvariantCulture) + "...\nStatus: Downloaded newer image for " + img + "\n" + img + "\n(Cert Lab: a pretend registry; nothing was downloaded.)");
                        return 0;
                    }
                case "images":
                    {
                        var sb = new StringBuilder("REPOSITORY".PadRight(58) + "TAG".PadRight(40) + "SIZE\n");
                        foreach (var i in h.Images) { var p = i.Split(':'); sb.Append(p[0].PadRight(58) + p[1].PadRight(40) + (i.Contains("nanoserver") ? "297MB" : i.Contains("iis") ? "4.92GB" : "4.79GB") + "\n"); }
                        c.L(sb.ToString().TrimEnd('\n')); return 0;
                    }
                case "run":
                    {
                        int k = 1; string name = null, iso = null, ports = null; bool detach = false;
                        while (k < a.Count && a[k].StartsWith("-", StringComparison.Ordinal))
                        {
                            string x = a[k];
                            if (x == "-d" || x == "--detach") detach = true;
                            else if (x == "--rm" || x == "-it" || x == "-i" || x == "-t") { }
                            else if (x.StartsWith("--name", StringComparison.Ordinal)) { name = x.Contains("=") ? x.Substring(7) : a[++k]; }
                            else if (x.StartsWith("--isolation", StringComparison.Ordinal)) { iso = x.Contains("=") ? x.Substring(12) : a[++k]; }
                            else if (x == "-p" || x.StartsWith("--publish", StringComparison.Ordinal)) { ports = x.Contains("=") ? x.Substring(x.IndexOf('=') + 1) : a[++k]; }
                            else if (x == "-e" || x == "-v" || x == "--memory" || x == "-m" || x == "--network") k++;
                            k++;
                        }
                        if (k >= a.Count) { c.L("\"docker run\" requires at least 1 argument."); return 1; }
                        string img = a[k], cmd = k + 1 < a.Count ? string.Join(" ", a.Skip(k + 1).ToArray()) : null;
                        WsContainer ct; var err = h.Run(img, name, iso, ports, cmd, detach, out ct);
                        if (err != null) { c.L(err); return 125; }
                        if (detach) c.L(ct.Id + "f3a9c1b2d4e5f60718293a4b5c6d7e8f9a0b1c2d3e4f5a6b7c8d9e0f1a2b3c4d".Substring(0, 52));
                        else if (cmd != null && cmd.Contains("ver")) c.L("\nMicrosoft Windows [Version " + WsHvHost.ImageOs(img) + ".1]");
                        else if (cmd != null && cmd.Contains("hostname")) c.L(ct.Id);
                        return 0;
                    }
                case "ps":
                    {
                        bool all = a.Contains("-a") || a.Contains("--all");
                        var sb = new StringBuilder("CONTAINER ID   IMAGE".PadRight(70) + "STATUS".PadRight(16) + "PORTS".PadRight(22) + "NAMES\n");
                        foreach (var x in h.Containers.Where(x => all || x.Status.StartsWith("Up", StringComparison.Ordinal))) sb.Append((x.Id + "   " + x.Image).PadRight(70) + x.Status.PadRight(16) + (x.Ports.Length > 0 ? "0.0.0.0:" + x.Ports.Replace(":", "->") + "/tcp" : "").PadRight(22) + x.Name + "\n");
                        c.L(sb.ToString().TrimEnd('\n')); return 0;
                    }
                case "stop": case "start": case "rm": case "restart":
                    {
                        foreach (var key in a.Skip(1).Where(x => !x.StartsWith("-", StringComparison.Ordinal)))
                        {
                            var ct = h.Container(key);
                            if (ct == null) { c.L("Error response from daemon: No such container: " + key); return 1; }
                            if (verb == "rm") { if (ct.Status.StartsWith("Up", StringComparison.Ordinal) && !a.Contains("-f") && !a.Contains("--force")) { c.L("Error response from daemon: You cannot remove a running container " + ct.Id + ". Stop the container before attempting removal or force remove"); return 1; } h.Containers.Remove(ct); }
                            else ct.Status = verb == "stop" ? "Exited (1067)" : "Up";
                            c.L(key);
                        }
                        return 0;
                    }
                case "rmi":
                    {
                        string img = a.Count > 1 ? a[1] : ""; if (!img.Contains(":")) img += ":latest";
                        if (h.Containers.Any(x => WsText.Eq(x.Image, img))) { c.L("Error response from daemon: conflict: unable to remove repository reference \"" + img + "\" - container is using its referenced image"); return 1; }
                        if (h.Images.RemoveAll(x => WsText.Eq(x, img)) == 0) { c.L("Error: No such image: " + img); return 1; }
                        c.L("Untagged: " + img); return 0;
                    }
                case "inspect":
                    {
                        var ct = h.Container(a.LastOrDefault(x => !x.StartsWith("-", StringComparison.Ordinal) && x != "inspect") ?? "");
                        if (ct == null) { c.L("Error: No such object"); return 1; }
                        string f = opt("-f") ?? opt("--format");
                        if (f != null && f.Contains("Isolation")) { c.L(ct.Isolation); return 0; }
                        c.L("[\n    {\n        \"Id\": \"" + ct.Id + "\",\n        \"Name\": \"/" + ct.Name + "\",\n        \"Image\": \"" + ct.Image + "\",\n        \"State\": { \"Status\": \"" + (ct.Status.StartsWith("Up", StringComparison.Ordinal) ? "running" : "exited") + "\" },\n        \"HostConfig\": { \"Isolation\": \"" + ct.Isolation + "\", \"NetworkMode\": \"nat\" }\n    }\n]");
                        return 0;
                    }
                case "exec": { var ct = h.Container(a.Count > 1 ? a.Skip(1).FirstOrDefault(x => !x.StartsWith("-", StringComparison.Ordinal)) : ""); if (ct == null || !ct.Status.StartsWith("Up", StringComparison.Ordinal)) { c.L("Error response from daemon: container is not running"); return 1; } c.L(ct.Id); return 0; }
                case "network": c.L("NETWORK ID     NAME      DRIVER    SCOPE\n5a1f0c7e2b94   nat       nat       local\n9d3e6b8a1c27   none      null      local"); return 0;
            }
            c.L("Cert Lab's pretend Docker engine knows: version, info, pull, images, run, ps, stop, start, restart, rm, rmi, inspect, exec, network ls.");
            return 1;
        }
    }
}
