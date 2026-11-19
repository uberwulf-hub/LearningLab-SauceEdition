// Cert Lab - PowerShell remoting between the pretend machines: PSSessions (a runspace on the other machine),
// Invoke-Command (results come back "deserialized": property bags with PSComputerName and no methods),
// Enter-PSSession / Exit-PSSession at the console, $using: variables, Enable-PSRemoting and Test-WSMan.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class PsRemoteSession
    {
        public int Id; public string Name = "", ConfigurationName = "Microsoft.PowerShell", State = "Opened";
        public PsMachine Target; public PsRunspace Runspace; public object InstanceId;
        public PsObj Obj;
    }

    public static class PsRemoteHook
    {
        /// <summary>Set by the administration / networking parts: WinRM service started, firewall rule enabled...</summary>
        public static Action<PsMachine> OnEnable, OnDisable;
        /// <summary>Is TCP port 5985 open from one machine to another (firewall)? null: yes.</summary>
        public static Func<PsMachine, PsMachine, bool> PortOpen;
        /// <summary>JEA endpoints: the commands an endpoint allows (null: every command).</summary>
        public static Func<PsMachine, string, string, IEnumerable<string>> EndpointCommands;
    }

    public partial class PsRunspace
    {
        public readonly List<PsRemoteSession> PSSessions = new List<PsRemoteSession>();
        int sessionCounter;
        public HashSet<string> AllowedCommands;     // a JEA session: only these commands may run

        /// <summary>Opens a runspace on another machine, or explains why it cannot (the messages of 5.1's WinRM errors).</summary>
        public PsRunspace Connect(string name, CmdCtx c, string configuration)
        {
            PsMachine target;
            string n = (name ?? "").Trim();
            if (n == "." || n.Equals("localhost", StringComparison.OrdinalIgnoreCase) || n == "127.0.0.1" || n.Equals(M.Name, StringComparison.OrdinalIgnoreCase) || n.Equals(M.Fqdn, StringComparison.OrdinalIgnoreCase)) target = M;
            else
            {
                var h = M.Net.FindHost(n);
                if (h == null)
                    throw RemoteFail(c, n, "Connecting to remote server " + n + " failed with the following error message : WinRM cannot process the request. The following error occurred while using Kerberos authentication: Cannot find the computer " + n + ". Verify that the computer exists on the network and that the name provided is spelled correctly. For more information, see the about_Remote_Troubleshooting Help topic.", "NetworkPathNotFound,PSSessionStateBroken");
                if (h.Machine == null)
                    throw RemoteFail(c, n, "Connecting to remote server " + n + " failed with the following error message : Access is denied. For more information, see the about_Remote_Troubleshooting Help topic. (Cert Lab: " + h.Name + " is part of the pretend network, but you have no account on it.)", "AccessDenied,PSSessionStateBroken");
                target = h.Machine;
            }
            bool reachable = target.WinRmListening && target.State == "running" && (PsRemoteHook.PortOpen == null || target == M || PsRemoteHook.PortOpen(M, target));
            if (!reachable)
                throw RemoteFail(c, n, "Connecting to remote server " + n + " failed with the following error message : WinRM cannot complete the operation. Verify that the specified computer name is valid, that the computer is accessible over the network, and that a firewall exception for the WinRM service is enabled and allows access from this computer. By default, the WinRM firewall exception for public profiles limits access to remote computers within the same local subnet. For more information, see the about_Remote_Troubleshooting Help topic.", "WinRMOperationTimeout,PSSessionStateBroken");
            var r = new PsRunspace(target, Host, User) { RemoteFrom = M.Name, Console = null };
            r.SetLocation(r.Home + "\\Documents");
            r.ReplaceHostObject();
            if (configuration != null && !configuration.Equals("Microsoft.PowerShell", StringComparison.OrdinalIgnoreCase))
            {
                var cmds = PsRemoteHook.EndpointCommands == null ? null : PsRemoteHook.EndpointCommands(target, configuration, User);
                if (cmds == null && target.Endpoint(configuration) != null)
                    throw RemoteFail(c, n, "Connecting to remote server " + n + " failed with the following error message : Access is denied. For more information, see the about_Remote_Troubleshooting Help topic.", "AccessDenied,PSSessionStateBroken");
                if (cmds == null) throw RemoteFail(c, n, "Connecting to remote server " + n + " failed with the following error message : The WS-Management service cannot process the request. Cannot find the " + configuration + " session configuration in the WSMan: drive on the " + n + " computer. For more information, see the about_Remote_Troubleshooting Help topic.", "InvalidResourceUri,PSSessionStateBroken");
                r.AllowedCommands = new HashSet<string>(cmds, StringComparer.OrdinalIgnoreCase);
                r.Aliases.Clear();
            }
            return r;
        }
        void ReplaceHostObject() { Global.Vars["Host"] = new PsVariable { Name = "Host", Value = HostObject() }; }

        PsRuntimeException RemoteFail(CmdCtx c, string name, string msg, string fqid)
        {
            var r = MakeError(c == null ? null : c.Ast, msg, fqid, "OpenError", "System.Management.Automation.Remoting.PSRemotingTransportException", name);
            r.ActivityOverride = ""; r.Reason = "PSRemotingTransportException"; r.FullCommandName = "";
            r.CommandName = "";
            r.Computer = null;
            return new PsRuntimeException(r, false);
        }

        public PsRemoteSession NewSession(PsRunspace remote, string name, string config)
        {
            int id = ++sessionCounter;
            var s = new PsRemoteSession { Id = id, Name = name ?? "WinRM" + id, Target = remote.M, Runspace = remote, InstanceId = M.NewGuid(), ConfigurationName = config ?? "Microsoft.PowerShell" };
            var o = new PsObj("System.Management.Automation.Runspaces.PSSession", "System.Object");
            o.Val("Id", "int", s.Id); o.Val("Name", "string", s.Name);
            o.Val("ComputerName", "string", remote.M.Name);
            o.Val("ComputerType", "System.Management.Automation.Runspaces.TargetMachineType", "RemoteMachine");
            o.Prop("State", "System.Management.Automation.Runspaces.RunspaceState", () => PsEnums.V("System.Management.Automation.Runspaces.RunspaceState", s.Runspace.Closed || s.State == "Closed" ? (s.State == "Closed" ? "Closed" : "Broken") : "Opened"));
            o.Val("ConfigurationName", "string", s.ConfigurationName);
            o.Prop("Availability", "System.Management.Automation.Runspaces.RunspaceAvailability", () => PsEnums.V("System.Management.Automation.Runspaces.RunspaceAvailability", s.Runspace.Closed || s.State == "Closed" ? "None" : "Available"));
            o.Val("InstanceId", "guid", s.InstanceId);
            o.Base = s;
            o.ToStr = x => "[PSSession]" + s.Name;
            s.Obj = o;
            PSSessions.Add(s);
            remote.M.RebootListeners.Add(() => { if (s.State != "Closed") s.State = "Broken"; });
            return s;
        }

        /// <summary>Collects $using: values from a script block before it runs elsewhere.</summary>
        public Dictionary<string, object> UsingValues(ScriptBlockAst ast, PsScope sc)
        {
            var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var text = ast.Text ?? "";
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, "\\$(?:using|USING|Using):([A-Za-z_][A-Za-z0-9_]*)"))
            {
                string n = m.Groups[1].Value;
                if (!d.ContainsKey(n)) d[n] = Deserialize(GetVar(n, sc), null);
            }
            return d;
        }

        /// <summary>What comes back from another machine: a copy with "Deserialized." type names and no methods.</summary>
        public static object Deserialize(object o, string computer) { return Deserialize(o, computer, 0); }
        static object Deserialize(object o, string computer, int depth)
        {
            o = PsConv.Base(o);
            if (o == null) return null;
            if (o is string)
            {
                if (computer == null) return o;
                var s = new string(((string)o).ToCharArray());
                var n = PsNotes.For(s); n.Add("PSComputerName", computer); n.Add("PSShowComputerName", true);
                return s;
            }
            if (PsConv.IsNumber(o) || o is bool || o is char || o is DateTime || o is TimeSpan || o is PsEnum || o is Guid || o is Version || o is PsSecureString) return o;
            if (o is PsScriptBlock) return ((PsScriptBlock)o).Text;
            if (o is IDictionary) { var h = PsConv.NewHashtable(); foreach (DictionaryEntry e in (IDictionary)o) h[e.Key] = Deserialize(e.Value, null, depth + 1); return h; }
            if (PsConv.IsCollection(o))
            {
                if (depth > 1) return PsConv.ToStr(o);
                return ((IEnumerable)o).Cast<object>().Select(x => Deserialize(x, null, depth + 1)).ToArray();
            }
            var po = o as PsObj;
            if (po == null) return PsConv.ToStr(o);
            if (depth > 1) return po.ToString();
            var r = new PsObj();
            foreach (var t in po.TypeNames) r.TypeNames.Add(t.StartsWith("Deserialized.", StringComparison.Ordinal) || t.StartsWith("Selected.", StringComparison.Ordinal) ? t : "Deserialized." + t);
            foreach (var p in po.Props) { object v; try { v = po.ValueOf(p); } catch (PsRuntimeException) { v = null; } r.Add(p.Name, Deserialize(v, null, depth + 1), p.TypeName); }
            if (po.Methods.ContainsKey("ToString")) { }
            r.DefaultDisplay = po.DefaultDisplay;
            string text = po.ToString();
            r.ToStr = x => text;
            if (computer != null)
            {
                bool custom = po.IsCustom;
                r.Add("PSComputerName", computer, "string");
                byte[] gb;
                using (var md5 = System.Security.Cryptography.MD5.Create()) gb = md5.ComputeHash(Encoding.UTF8.GetBytes("runspace:" + computer));
                gb[7] = (byte)((gb[7] & 0x0F) | 0x40); gb[8] = (byte)((gb[8] & 0x3F) | 0x80);
                r.Add("RunspaceId", new Guid(gb), "guid");
                r.Add("PSShowComputerName", true, "bool");
                r.Computer = computer;
                if (custom && po.DefaultDisplay == null) r.DefaultDisplay = po.Props.Select(p => p.Name).Concat(new[] { "PSComputerName", "RunspaceId" }).ToArray();
            }
            return r;
        }

        /// <summary>Runs a script block in a (remote) runspace's global scope, the results deserialized.</summary>
        public void RunRemote(PsRunspace remote, PsScriptBlock sb, object[] args, Dictionary<string, object> usingVals, Streams local, bool hideComputer, object input)
        {
            string comp = hideComputer ? null : remote.M.Name;
            var st = new Streams
            {
                Out = o => local.Out(Deserialize(o, comp)),
                Err = e => { e.Computer = remote.M.Name; local.Err(e); },
                Warn = w => local.Warn(w), Verbose = w => local.Verbose(w), Debug = w => local.Debug(w), Info = w => local.Info(w)
            };
            var oldUsing = remote.Using; remote.Using = usingVals;
            var oldStreams = remote.CurrentStreams; remote.CurrentStreams = st;
            var oldCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture = PsConv.Us;
            var oldHost = remote.Host; remote.Host = Host;
            try
            {
                var block = new PsScriptBlock { Ast = sb.Ast };
                remote.InvokeBlockTo(block, remote.Global, input, args, true, null, st);
            }
            catch (PsRuntimeException e) { e.Rec.Computer = remote.M.Name; local.Err(e.Rec); AddError(e.Rec); }
            catch (PsExitEx) { }
            finally { remote.Using = oldUsing; remote.CurrentStreams = oldStreams; remote.Host = oldHost; System.Threading.Thread.CurrentThread.CurrentCulture = oldCulture; }
        }
    }

    public partial class PsCmdlets
    {
        static List<PsRemoteSession> SessionsFrom(CmdCtx c)
        {
            var l = new List<PsRemoteSession>();
            foreach (var x in c.Objs("Session")) { var po = PsConv.Base(x) as PsObj; var s = po == null ? null : po.Base as PsRemoteSession; if (s != null) l.Add(s); }
            return l;
        }

        static partial void RegisterRemoting()
        {
            PsEnums.Define("System.Management.Automation.Runspaces.TargetMachineType", "RemoteMachine=0,VirtualMachine=1,Container=2");
            PsFormat.Table("System.Management.Automation.Runspaces.PSSession", PsFormat.PR("Id", 3), PsFormat.P("Name", 15), PsFormat.P("ComputerName", 15), PsFormat.P("ComputerType", 15), PsFormat.P("State", 13), PsFormat.P("ConfigurationName", 20), PsFormat.PR("Availability", 13));
            PsCmdlets.AddType("PSSession", "System.Management.Automation.Runspaces.PSSession");

            Add("Invoke-Command", Core, "ScriptBlock:scriptblock@0 alias=Command; ComputerName:string[] alias=Cn; Session:PSSession[]; ArgumentList:object[] alias=Args; FilePath:string alias=PSPath; Credential:pscredential; AsJob:switch; JobName:string; HideComputerName:switch alias=HCN; ThrottleLimit:int; ConfigurationName:string; Port:int; UseSSL:switch; Authentication:string; InputObject:psobject pipe; NoNewScope:switch; SessionName:string[]; EnableNetworkAccess:switch", (c, o) =>
            {
                if (c.HasInput) { c.Items.Add(o); return; }
            }).End = c =>
            {
                PsScriptBlock sb = c.Block("ScriptBlock");
                if (c.Has("FilePath"))
                {
                    var text = c.R.ReadFileText(c.R.FullFsPath(c.Str("FilePath"), c.R.CurrentFsDir));
                    if (text == null) throw c.Fail("Cannot find path '" + c.R.FullFsPath(c.Str("FilePath"), c.R.CurrentFsDir) + "' because it does not exist.", "PathNotFound", "ObjectNotFound", c.Str("FilePath"), "System.Management.Automation.ItemNotFoundException");
                    sb = new PsScriptBlock { Ast = PsParser.Parse(text, c.R.FullFsPath(c.Str("FilePath"), c.R.CurrentFsDir)) };
                }
                if (sb == null) throw c.Fail("Cannot process command because of one or more missing mandatory parameters: ScriptBlock.", "MissingMandatoryParameter", "InvalidArgument", null, "System.Management.Automation.ParameterBindingException");
                var args = c.Objs("ArgumentList").ToArray();
                object input = c.Items.Count == 0 ? null : (c.Items.Count == 1 ? c.Items[0] : (object)c.Items.ToArray());
                if (!c.Has("ComputerName") && !c.Has("Session"))
                {
                    // locally: like & { } with the arguments
                    c.R.InvokeBlockTo(sb, c.Scope, input, args, c.Sw("NoNewScope"), null, c.St);
                    return;
                }
                var usingVals = c.R.UsingValues(sb.Ast, c.Scope);
                if (c.Sw("AsJob")) { c.Out(PsJobs.StartRemote(c, sb, args, usingVals)); return; }
                foreach (var s in SessionsFrom(c))
                {
                    if (s.State != "Opened" || s.Runspace.Closed) { c.Error("Because the session state for session " + s.Name + ", " + s.InstanceId + ", " + s.Target.Name + " is not equal to Open, you cannot run a command in the session.  The session state is " + (s.State == "Closed" ? "Closed" : "Broken") + ".", "InvalidSessionState," + s.Target.Name, "InvalidOperation", s.Obj, "System.InvalidOperationException"); continue; }
                    c.R.RunRemote(s.Runspace, sb, args, usingVals, c.St, c.Sw("HideComputerName"), input);
                }
                foreach (var n in c.Strs("ComputerName") ?? new string[0])
                {
                    PsRunspace remote;
                    try { remote = c.R.Connect(n, c, c.Str("ConfigurationName")); }
                    catch (PsRuntimeException e) { e.Rec.Computer = null; e.Rec.TargetName = n; e.Rec.TargetType = "String"; c.WriteError(e.Rec); continue; }
                    c.R.RunRemote(remote, sb, args, usingVals, c.St, c.Sw("HideComputerName"), input);
                    remote.M.Sessions.Remove(remote);
                }
            };
            Add("New-PSSession", Core, "ComputerName:string[]@0 pipe alias=Cn; Name:string[]; Credential:pscredential; ConfigurationName:string; Port:int; UseSSL:switch; Authentication:string; ThrottleLimit:int; EnableNetworkAccess:switch", (c, o) =>
            {
                var names = c.Strs("ComputerName") ?? new[] { "localhost" };
                var given = c.Strs("Name");
                for (int i = 0; i < names.Length; i++)
                {
                    PsRunspace remote;
                    try { remote = c.R.Connect(names[i], c, c.Str("ConfigurationName")); }
                    catch (PsRuntimeException e) { c.WriteError(e.Rec); continue; }
                    var s = c.R.NewSession(remote, given != null && i < given.Length ? given[i] : null, c.Str("ConfigurationName"));
                    c.Out(s.Obj);
                }
            });
            Add("Get-PSSession", Core, "Name:string[] ps=Name; Id:int[]@0 ps=Id; ComputerName:string[] alias=Cn ps=Computer; State:string", (c, o) =>
            {
                foreach (var s in c.R.PSSessions.Where(x => x.State != "Closed").ToList())
                {
                    if (c.Has("Id") && !c.Objs("Id").Any(i => PsConv.ToInt(i) == s.Id)) continue;
                    if (c.Has("Name") && !c.Strs("Name").Any(n => PsConv.Like(s.Name, n, false))) continue;
                    if (c.Has("ComputerName") && !c.Strs("ComputerName").Any(n => PsConv.Like(s.Target.Name, n, false))) continue;
                    c.Out(s.Obj);
                }
            });
            Add("Remove-PSSession", Core, "Session:PSSession[]@0 pipe ps=Session; Id:int[] ps=Id; Name:string[] ps=Name; ComputerName:string[] alias=Cn ps=Computer", (c, o) =>
            {
                var l = SessionsFrom(c);
                if (c.Has("Id")) l.AddRange(c.R.PSSessions.Where(s => c.Objs("Id").Any(i => PsConv.ToInt(i) == s.Id)));
                if (c.Has("Name")) l.AddRange(c.R.PSSessions.Where(s => c.Strs("Name").Any(n => PsConv.Like(s.Name, n, false))));
                if (c.Has("ComputerName")) l.AddRange(c.R.PSSessions.Where(s => c.Strs("ComputerName").Any(n => PsConv.Like(s.Target.Name, n, false))));
                foreach (var s in l.Distinct())
                {
                    s.State = "Closed"; s.Runspace.Closed = true;
                    s.Target.Sessions.Remove(s.Runspace);
                    c.R.PSSessions.Remove(s);
                }
            });
            Add("Enter-PSSession", Core, "ComputerName:string@0 alias=Cn ps=Computer; Session:PSSession ps=Session pipe; Id:int ps=Id; Name:string ps=Name; Credential:pscredential; ConfigurationName:string; Port:int; UseSSL:switch; Authentication:string; EnableNetworkAccess:switch", (c, o) =>
            {
                var con = c.R.Console;
                if (c.R.RemoteFrom != null) throw c.Fail("You are currently in a Windows PowerShell PSSession and cannot use the Enter-PSSession cmdlet to enter another PSSession.", "RemoteHostDoesNotSupportPushRunspace", "InvalidArgument", null, "System.ArgumentException");
                if (con == null) throw c.Fail("Enter-PSSession works only when you type it at the console. In a script, use Invoke-Command.", "CertLabEnterPSSessionInScript", "InvalidOperation", null, "System.Management.Automation.PSInvalidOperationException");
                PsRunspace remote = null;
                var sess = SessionsFrom(c).FirstOrDefault();
                if (sess == null && c.Has("Id")) sess = c.R.PSSessions.FirstOrDefault(s => s.Id == c.Int("Id", 0));
                if (sess == null && c.Has("Name")) sess = c.R.PSSessions.FirstOrDefault(s => s.Name.Equals(c.Str("Name"), StringComparison.OrdinalIgnoreCase));
                if (sess != null)
                {
                    if (sess.State != "Opened" || sess.Runspace.Closed) throw c.Fail("The session " + sess.Name + " is not open. Its state is " + (sess.State == "Closed" ? "Closed" : "Broken") + ".", "InvalidSessionState", "InvalidOperation", sess.Obj, "System.InvalidOperationException");
                    remote = sess.Runspace;
                }
                else if (c.Has("ComputerName")) { remote = c.R.Connect(c.Str("ComputerName"), c, c.Str("ConfigurationName")); }
                else throw c.Fail("Cannot process command because of one or more missing mandatory parameters: ComputerName.", "MissingMandatoryParameter", "InvalidArgument", null, "System.Management.Automation.ParameterBindingException");
                remote.Host = con;
                remote.Console = null;
                con.RemoteStack.Push(remote);
            });
            Add("Exit-PSSession", Core, "", (c, o) => { if (c.R.RemoteFrom != null) c.R.ExitRequested = true; });
            Add("Enable-PSRemoting", Core, "Force:switch; SkipNetworkProfileCheck:switch", (c, o) =>
            {
                if (!c.Sw("Force") && c.R.AskChoice("WinRM Quick Configuration", "Running command \"Set-WSManQuickConfig\" to enable remote management of this computer by using the Windows Remote Management (WinRM) service.", "Y") == "N") return;
                bool was = c.M.WinRmListening;
                c.M.WinRmListening = true;
                if (PsRemoteHook.OnEnable != null) PsRemoteHook.OnEnable(c.M);
                if (was) c.Host("WinRM is already set up to receive requests on this computer.\nWinRM is already set up for remote management on this computer.");
                else c.Host("WinRM has been updated to receive requests.\nWinRM service type changed successfully.\nWinRM service started.\n\nWinRM has been updated for remote management.\nWinRM firewall exception enabled.");
            }).ShouldProcess = false;
            Add("Disable-PSRemoting", Core, "Force:switch", (c, o) =>
            {
                c.M.WinRmListening = false;
                if (PsRemoteHook.OnDisable != null) PsRemoteHook.OnDisable(c.M);
                c.Warn("Disabling the session configurations does not undo all the changes made by the Enable-PSRemoting or Enable-PSSessionConfiguration cmdlet. You might have to manually undo the changes. (Cert Lab: remote sessions to " + c.M.Name + " are now refused.)");
            });
            Add("Test-WSMan", "Microsoft.WSMan.Management", "ComputerName:string@0 alias=Cn; Credential:pscredential; Authentication:string; Port:int; UseSSL:switch", (c, o) =>
            {
                string n = c.Str("ComputerName") ?? "localhost";
                try { var r = c.R.Connect(n, c, null); r.M.Sessions.Remove(r); }
                catch (PsRuntimeException e) { throw c.Fail(n.Equals("localhost", StringComparison.OrdinalIgnoreCase) ? "<f:WSManFault xmlns:f=\"http://schemas.microsoft.com/wbem/wsman/1/wsmanfault\" Code=\"2150858770\" Machine=\"localhost\"><f:Message>The client cannot connect to the destination specified in the request.</f:Message></f:WSManFault>" : e.Rec.Exception.Message, "WsManError", "InvalidOperation", n, "System.InvalidOperationException"); }
                var o2 = new PsObj("System.Xml.XmlElement#http://schemas.dmtf.org/wbem/wsman/identity/1/wsmanidentity.xsd#IdentifyResponse", "System.Object");
                o2.Val("wsmid", "string", "http://schemas.dmtf.org/wbem/wsman/identity/1/wsmanidentity.xsd");
                o2.Val("ProtocolVersion", "string", "http://schemas.dmtf.org/wbem/wsman/1/wsman.xsd");
                o2.Val("ProductVendor", "string", "Microsoft Corporation");
                o2.Val("ProductVersion", "string", "OS: 0.0.0 SP: 0.0 Stack: 3.0");
                c.Out(o2);
            });
        }
    }
}
