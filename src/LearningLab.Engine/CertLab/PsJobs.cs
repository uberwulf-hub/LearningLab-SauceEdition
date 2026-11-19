// Cert Lab - background jobs in the simulated PowerShell (Start-Job, Invoke-Command -AsJob, Get-Job, Receive-Job,
// Wait-Job, Stop-Job, Remove-Job). A job's script runs at once in a separate session on the machine, but keeps its
// own clock: Start-Sleep inside it does not move the machines' time; the job counts as Running until the machines'
// clock reaches its finishing time, and its output becomes available in step with that clock.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class PsJob
    {
        public int Id; public string Name = "", Type = "BackgroundJob", Location = "localhost", Command = "";
        public DateTime Start, End;
        public bool Failed, Stopped, Removed;
        public readonly List<KeyValuePair<DateTime, object>> Output = new List<KeyValuePair<DateTime, object>>();
        public readonly List<KeyValuePair<DateTime, PsErrorRecord>> Errors = new List<KeyValuePair<DateTime, PsErrorRecord>>();
        public int Received;
        public readonly List<PsJob> Children = new List<PsJob>();
        public PsJob Parent;
        public PsObj Obj;
        public PsMachine M;
        public string StateAt(DateTime now)
        {
            if (Children.Count > 0)
            {
                var cs = Children.Select(c => c.StateAt(now)).ToList();
                if (cs.Any(s => s == "Running")) return "Running";
                if (cs.Any(s => s == "Failed")) return "Failed";
                if (cs.All(s => s == "Stopped")) return "Stopped";
                return "Completed";
            }
            if (Stopped) return "Stopped";
            if (now < End) return "Running";
            return Failed ? "Failed" : "Completed";
        }
        public IEnumerable<object> Available(DateTime now) { return Output.Where(kv => kv.Key <= now).Select(kv => kv.Value); }
        public bool HasMore(DateTime now) { if (Children.Count > 0) return Children.Any(c => c.HasMore(now)); return Received < Output.Count || Errors.Count > 0 && Received <= Output.Count && StateAt(now) != "Running" && !errorsShown; }
        public bool errorsShown;
    }

    public static class PsJobs
    {

        public static List<PsJob> All(PsRunspace r) { if (r.Jobs == null) r.Jobs = new List<PsJob>(); return r.Jobs; }

        static PsObj MakeObj(PsJob j)
        {
            var o = new PsObj(j.Type == "RemoteJob" ? "System.Management.Automation.PSRemotingJob" : "System.Management.Automation.PSRemotingJob", "System.Management.Automation.Job", "System.Object");
            o.Val("Id", "int", j.Id); o.Val("Name", "string", j.Name);
            o.Val("PSJobTypeName", "string", j.Type);
            o.Prop("State", "System.Management.Automation.JobState", () => PsEnums.V("System.Management.Automation.JobState", j.StateAt(j.M.Now)));
            o.Prop("HasMoreData", "bool", () => j.HasMore(j.M.Now));
            o.Val("Location", "string", j.Location);
            o.Val("Command", "string", j.Command);
            o.Prop("PSBeginTime", "datetime", () => j.Start);
            o.Prop("PSEndTime", "datetime", () => j.StateAt(j.M.Now) == "Running" ? (object)null : j.End);
            o.Prop("ChildJobs", "System.Collections.Generic.List[System.Management.Automation.Job]", () => j.Children.Select(c => (object)c.Obj).ToArray());
            o.Val("InstanceId", "guid", j.M.NewGuid());
            o.DefaultDisplay = new[] { "Id", "Name", "PSJobTypeName", "State", "HasMoreData", "Location", "Command" };
            o.Base = j;
            j.Obj = o;
            return o;
        }

        /// <summary>Runs the job's script now in its own session and records when each output object appears.</summary>
        static void Execute(PsJob j, PsRunspace r, PsScriptBlock sb, object[] args, Dictionary<string, object> usingVals, string computer)
        {
            double t = 1.2 + j.Id * 0.05;
            r.SleepHook = secs => { t += secs; };
            r.Using = usingVals;
            var st = new Streams
            {
                Out = o => j.Output.Add(new KeyValuePair<DateTime, object>(j.Start.AddSeconds(t), computer != null ? PsRunspace.Deserialize(o, computer) : PsRunspace.Deserialize(o, null))),
                Err = e => { if (computer != null) e.Computer = computer; j.Errors.Add(new KeyValuePair<DateTime, PsErrorRecord>(j.Start.AddSeconds(t), e)); },
                Info = o => j.Output.Add(new KeyValuePair<DateTime, object>(j.Start.AddSeconds(t), new PsHostText { Text = PsConv.ToStr(o) })),
            };
            r.CurrentStreams = st;
            var oldCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture = PsConv.Us;
            try { r.InvokeBlockTo(new PsScriptBlock { Ast = sb.Ast }, r.Global, null, args, false, null, st); }
            catch (PsRuntimeException e) { j.Failed = true; if (computer != null) e.Rec.Computer = computer; j.Errors.Add(new KeyValuePair<DateTime, PsErrorRecord>(j.Start.AddSeconds(t), e.Rec)); }
            catch (PsExitEx) { }
            catch (PsFlowEx) { }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = oldCulture; r.M.Sessions.Remove(r); }
            j.End = j.Start.AddSeconds(t + 0.3);
        }

        public static PsObj Start(CmdCtx c, PsScriptBlock sb, object[] args, string name)
        {
            var jobs = All(c.R);
            
            var j = new PsJob { Id = ++c.R.NextJobId, M = c.M, Start = c.M.Now, Command = sb.Text.Trim(), Type = "BackgroundJob" };
            j.Name = name ?? "Job" + j.Id;
            var child = new PsJob { Id = ++c.R.NextJobId, M = c.M, Start = c.M.Now, Command = j.Command, Parent = j, Name = "Job" + (j.Id + 1) };
            var r = new PsRunspace(c.M, new NullHost(), c.R.User);
            r.SetLocation(c.R.Home + "\\Documents");
            Execute(child, r, sb, args, c.R.UsingValues(sb.Ast, c.Scope), null);
            j.Children.Add(child);
            MakeObj(child);
            jobs.Add(j);
            return MakeObj(j);
        }

        public static PsObj StartRemote(CmdCtx c, PsScriptBlock sb, object[] args, Dictionary<string, object> usingVals)
        {
            var jobs = All(c.R);
            
            var names = (c.Strs("ComputerName") ?? new string[0]).ToList();
            var sessions = c.Objs("Session").Select(x => (PsConv.Base(x) as PsObj)).Where(x => x != null && x.Base is PsRemoteSession).Select(x => (PsRemoteSession)x.Base).ToList();
            var j = new PsJob { Id = ++c.R.NextJobId, M = c.M, Start = c.M.Now, Command = sb.Text.Trim(), Type = "RemoteJob" };
            j.Name = c.Str("JobName") ?? "Job" + j.Id;
            j.Location = string.Join(",", names.Concat(sessions.Select(s => s.Target.Name)).ToArray());
            foreach (var n in names)
            {
                var child = new PsJob { Id = ++c.R.NextJobId, M = c.M, Start = c.M.Now, Command = j.Command, Parent = j, Location = n, Name = "Job" + c.R.NextJobId };
                PsRunspace remote;
                try { remote = c.R.Connect(n, c, null); }
                catch (PsRuntimeException e) { child.Failed = true; child.End = child.Start.AddSeconds(2); child.Errors.Add(new KeyValuePair<DateTime, PsErrorRecord>(child.End, e.Rec)); j.Children.Add(child); MakeObj(child); continue; }
                Execute(child, remote, sb, args, usingVals, remote.M.Name);
                j.Children.Add(child); MakeObj(child);
            }
            foreach (var s in sessions)
            {
                var child = new PsJob { Id = ++c.R.NextJobId, M = c.M, Start = c.M.Now, Command = j.Command, Parent = j, Location = s.Target.Name, Name = "Job" + c.R.NextJobId };
                var hook = s.Runspace.SleepHook;
                Execute(child, s.Runspace, sb, args, usingVals, s.Target.Name);
                s.Runspace.SleepHook = hook;
                if (!s.Target.Sessions.Contains(s.Runspace)) s.Target.Sessions.Add(s.Runspace);
                j.Children.Add(child); MakeObj(child);
            }
            jobs.Add(j);
            return MakeObj(j);
        }

        static List<PsJob> Pick(CmdCtx c)
        {
            var jobs = All(c.R).Where(j => !j.Removed).ToList();
            var l = new List<PsJob>();
            foreach (var x in c.Objs("Job")) { var po = PsConv.Base(x) as PsObj; if (po != null && po.Base is PsJob) l.Add((PsJob)po.Base); }
            if (c.Has("Id")) l.AddRange(jobs.Where(j => c.Objs("Id").Any(i => PsConv.ToInt(i) == j.Id)).Concat(jobs.SelectMany(j => j.Children).Where(ch => c.Objs("Id").Any(i => PsConv.ToInt(i) == ch.Id))));
            if (c.Has("Name")) l.AddRange(jobs.Where(j => c.Strs("Name").Any(n => PsConv.Like(j.Name, n, false))));
            if (c.Has("State")) l.AddRange(jobs.Where(j => c.Strs("State").Any(s => j.StateAt(c.M.Now).Equals(s, StringComparison.OrdinalIgnoreCase))));
            if (!c.Has("Job") && !c.Has("Id") && !c.Has("Name") && !c.Has("State")) l.AddRange(jobs);
            if (c.Has("Id") && l.Count == 0) foreach (var i in c.Objs("Id")) c.Error("The command cannot find a job with the job ID " + PsConv.ToInt(i) + ". Verify the value of the Id parameter and then try the command again.", "JobWithSpecifiedIdNotFound", "ObjectNotFound", i, "System.ArgumentException");
            if (c.Has("Name") && l.Count == 0) foreach (var n in c.Strs("Name")) if (!PsConv.HasWildcard(n)) c.Error("The command cannot find the job because the job name " + n + " was not found. Verify the value of the Name parameter, and then try the command again.", "JobWithSpecifiedNameNotFound", "ObjectNotFound", n, "System.ArgumentException");
            return l.Distinct().ToList();
        }

        static IEnumerable<PsJob> Leaves(PsJob j) { if (j.Children.Count == 0) return new[] { j }; return j.Children; }

        public static void Register()
        {
            PsFormat.Table("System.Management.Automation.PSRemotingJob", PsFormat.P("Id", 6), PsFormat.P("Name", 15), PsFormat.P("PSJobTypeName", 15), PsFormat.P("State", 13), PsFormat.P("HasMoreData", 15), PsFormat.P("Location", 20), PsFormat.P("Command", 0));
            PsCmdlets.AddType("Job", "System.Management.Automation.Job");
            string jobSel = "Id:int[]@0 ps=Id; Name:string[]@0 ps=Name; Job:Job[]@0 pipe ps=Job; State:string ps=State";
            PsCmdlets.Add("Start-Job", PsCmdlets.Core, "ScriptBlock:scriptblock@0 alias=Command; FilePath:string; Name:string; ArgumentList:object[] alias=Args; InitializationScript:scriptblock; Credential:pscredential; RunAs32:switch; InputObject:psobject pipe", (c, o) =>
            {
                var sb = c.Block("ScriptBlock");
                if (c.Has("FilePath"))
                {
                    string p = c.R.FullFsPath(c.Str("FilePath"), c.R.CurrentFsDir);
                    var text = c.R.ReadFileText(p);
                    if (text == null) throw c.Fail("Cannot find path '" + p + "' because it does not exist.", "PathNotFound", "ObjectNotFound", p, "System.Management.Automation.ItemNotFoundException");
                    sb = new PsScriptBlock { Ast = PsParser.Parse(text, p) };
                }
                if (sb == null) throw c.Fail("Cannot process command because of one or more missing mandatory parameters: ScriptBlock.", "MissingMandatoryParameter", "InvalidArgument", null, "System.Management.Automation.ParameterBindingException");
                c.Out(Start(c, sb, c.Objs("ArgumentList").ToArray(), c.Str("Name")));
            });
            PsCmdlets.Add("Get-Job", PsCmdlets.Core, jobSel + "; IncludeChildJob:switch; ChildJobState:string; HasMoreData:bool; Before:datetime; After:datetime; Newest:int", (c, o) =>
            {
                foreach (var j in Pick(c)) { c.Out(j.Obj); if (c.Sw("IncludeChildJob")) foreach (var ch in j.Children) c.Out(ch.Obj); }
            });
            PsCmdlets.Add("Receive-Job", PsCmdlets.Core, jobSel + "; Keep:switch; Wait:switch; AutoRemoveJob:switch; WriteEvents:switch; WriteJobInResults:switch; ComputerName:string[]", (c, o) =>
            {
                var l = Pick(c);
                if (c.Sw("Wait")) WaitFor(c, l, -1);
                foreach (var j in l)
                {
                    foreach (var leaf in Leaves(j))
                    {
                        var avail = leaf.Available(c.M.Now).ToList();
                        for (int i = leaf.Received; i < avail.Count; i++)
                        {
                            var x = avail[i];
                            if (x is PsHostText) c.Host(((PsHostText)x).Text); else c.Out(x);
                        }
                        if (!c.Sw("Keep")) leaf.Received = avail.Count;
                        if (leaf.StateAt(c.M.Now) != "Running" && (!leaf.errorsShown || c.Sw("Keep")))
                        {
                            foreach (var e in leaf.Errors.Where(e => e.Key <= c.M.Now || true)) { var r = e.Value; c.R.AddError(r); c.St.Err(r); }
                            if (!c.Sw("Keep")) leaf.errorsShown = true;
                        }
                    }
                    if (c.Sw("AutoRemoveJob") && c.Sw("Wait")) j.Removed = true;
                }
            });
            PsCmdlets.Add("Wait-Job", PsCmdlets.Core, jobSel + "; Timeout:int; Any:switch; Force:switch", (c, o) =>
            {
                if (c.HasInput) { c.Items.Add(o); return; }
                var l = Pick(c);
                if (WaitFor(c, l, c.Int("Timeout", -1))) foreach (var j in l) c.Out(j.Obj);
            }).End = c =>
            {
                if (c.Items.Count == 0) return;
                var l = c.Items.Select(x => PsConv.Base(x) as PsObj).Where(x => x != null && x.Base is PsJob).Select(x => (PsJob)x.Base).ToList();
                if (WaitFor(c, l, c.Int("Timeout", -1))) foreach (var j in l) c.Out(j.Obj);
            };
            PsCmdlets.Add("Stop-Job", PsCmdlets.Core, jobSel + "; PassThru:switch", (c, o) =>
            {
                foreach (var j in Pick(c))
                {
                    foreach (var leaf in Leaves(j)) if (leaf.StateAt(c.M.Now) == "Running") { leaf.Stopped = true; leaf.Output.RemoveAll(kv => kv.Key > c.M.Now); leaf.End = c.M.Now; }
                    if (c.Sw("PassThru")) c.Out(j.Obj);
                }
            });
            // Suspend / Resume work only for workflow jobs, which the practice console does not have; Debug-Job needs the debugger.
            foreach (var verb in new[] { "Suspend", "Resume" })
            {
                string v = verb;
                PsCmdlets.Add(v + "-Job", PsCmdlets.Core, jobSel + "; Force:switch; Wait:switch", (c, o) =>
                {
                    foreach (var j in Pick(c))
                        c.Error("The job " + j.Id + " (" + j.Name + ") cannot be " + (v == "Suspend" ? "suspended" : "resumed") + ": only workflow jobs can be suspended and resumed, and Start-Job, -AsJob and remote jobs are not workflow jobs. (Cert Lab: workflows are not simulated.)", "CertLab" + v + "NotSupported", "InvalidOperation", j.Obj, "System.InvalidOperationException");
                }).ShouldProcess = true;
            }
            PsCmdlets.Add("Debug-Job", PsCmdlets.Core, jobSel + "; BreakAll:switch", (c, o) =>
            {
                foreach (var j in Pick(c))
                    c.Error("Debug-Job is real Windows PowerShell 5.1, but Cert Lab's practice console does not simulate the debugger.", "CertLabNotSimulated", "NotImplemented", j.Obj, "System.NotImplementedException");
            });
            PsCmdlets.Add("Remove-Job", PsCmdlets.Core, jobSel + "; Force:switch", (c, o) =>
            {
                foreach (var j in Pick(c))
                {
                    if (j.StateAt(c.M.Now) == "Running" && !c.Sw("Force")) { c.Error("The command cannot remove the job with the job ID " + j.Id + " because the job is not finished. To remove the job, first stop the job, or use the Force parameter.", "JobObjectNotFinishedCannotBeRemoved", "InvalidOperation", j.Obj, "System.ArgumentException"); continue; }
                    j.Removed = true;
                    All(c.R).Remove(j);
                }
            });
        }

        /// <summary>Wait-Job: the clock moves on to when the jobs finish (or the timeout). True if they all finished.</summary>
        static bool WaitFor(CmdCtx c, List<PsJob> l, int timeout)
        {
            if (l.Count == 0) return true;
            var end = l.SelectMany(Leaves).Max(j => j.End);
            if (end > c.M.Now)
            {
                if (timeout >= 0 && c.M.Now.AddSeconds(timeout) < end) { c.M.Net.Advance(timeout); return false; }
                c.M.Net.Advance((long)Math.Ceiling((end - c.M.Now).TotalSeconds));
            }
            return true;
        }
    }

    public partial class PsRunspace { public List<PsJob> Jobs; public int NextJobId; }

    public partial class PsCmdlets
    {
        static partial void RegisterJobs() { PsJobs.Register(); }
    }
}
