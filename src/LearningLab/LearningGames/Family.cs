// Learning Games - playing with family on the home network: a grown-up's family room (watching the kids' answers live,
// sending hints, starting games for everyone) and Math Race / Boss Battle hosted by anyone. The wire format is App Hub's
// Learning Games' own (JSON lines over Engine\LearningGames\Net.cs: discovery on UDP 47800, games on TCP 47801), so a
// phone with Learning Lab and a PC with App Hub's Learning Games can play together.
//
// Only the host decides: a race car moves at most one step per message and one every 0.8 s, the monster takes at most
// one hit every 0.6 s from each player, and the end of a game (Engine\LearningGames\NetRound.cs) comes from the host,
// shown on every device at once with a countdown back to the lobby. Everything another device sends is cleaned before
// it is shown (names, colours, lengths).
using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using LearningGames;

namespace LearningLab.LearningGames;

public enum NetMode { None, Host, Client }

public sealed class NetPlayer
{
    public int Id;
    public string Name = "", Color = "#3B82F6", Current = "";
    public int Grade;
    public JsonObject Summary;
    public readonly List<FeedItem> Feed = new();
}

public sealed record FeedItem(string When, string Text, string Answer, string Given, bool Ok, string Skill);

/// <summary>A game page taking part in a network game (Math Race or Boss Battle).</summary>
public interface INetGame
{
    void RacePositions(Dictionary<string, double> pos);
    void RaceWon(string id);
    void BossState(int hp, int max, int lives, bool attack, int by);
    /// <summary>The host's own copy: a player's claimed race position or a hit.</summary>
    void HostPos(int from, double claim);
    void HostHit(int from);
    void Ended(string why);
    void Hint(string from, string text);
}

public static class Family
{
    public static NetMode Mode { get; private set; }
    public static GameHost Host { get; private set; }
    public static GameClient Client { get; private set; }
    public static readonly SortedDictionary<int, NetPlayer> Players = new();
    public static int SelfId { get; private set; } = -1;
    public static string HostName { get; private set; } = "";
    public static string Session { get; private set; } = "";
    public static string HostGame { get; set; } = "";
    public static FoundHost[] Hosts { get; private set; } = Array.Empty<FoundHost>();
    public static bool ParentPlays { get; set; }
    public static INetGame Game { get; set; }

    /// <summary>Something changed that a page shows (players, the invitation, the top bar).</summary>
    public static event Action Changed;
    /// <summary>A hint or cheer from the family room (shown on any page).</summary>
    public static event Action<string, string> HintArrived;
    /// <summary>The host started a game for everyone: (game, players).</summary>
    public static event Action<string, List<NetPlayer>> GameStarted;

    // ---- the end of a network game
    public static NetRound Round { get; private set; }
    static int roundNo, roundDone;
    static DateTime roundAt, roundSent;
    /// <summary>The countdown ended: everyone back to the lobby.</summary>
    public static event Action GoNext;

    static DispatcherTimer timer;
    static System.Threading.Tasks.Task<FoundHost[]> discovering;
    static DateTime nextDiscover;
    /// <summary>True while a page wants the family rooms around looked for every few seconds (a child's math page).</summary>
    public static bool Looking { get; set; }

    /// <summary>Starts the 10-a-second network pump (once).</summary>
    public static void StartPump()
    {
        if (timer != null) return;
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (s, e) => Pump();
        timer.Start();
    }

    public static void Pump()
    {
        try { Poll(); } catch (Exception) { }
        try { TickRound(); } catch (Exception) { }
        if (Looking && Mode == NetMode.None && DateTime.Now >= nextDiscover) { nextDiscover = DateTime.Now.AddSeconds(8); Discover(); }
    }

    public static void Discover() { if (discovering == null) discovering = Discovery.Run(900); }

    static void Raise() { try { Changed?.Invoke(); } catch (Exception) { } }

    // ---------------------------------------------------------------- cleaning what other devices send
    public static string CleanText(string v, int max = 200)
    {
        string t = System.Text.RegularExpressions.Regex.Replace(v ?? "", "[\\x00-\\x1F\\x7F]", " ").Trim();
        return t.Length > max ? t.Substring(0, max).Trim() : t;
    }
    public static string CleanName(string v) { string t = CleanText(v, 24); return t.Length > 0 ? t : "Player"; }
    public static string CleanColor(string v) => System.Text.RegularExpressions.Regex.IsMatch(v ?? "", "^#[0-9A-Fa-f]{6}$") ? v : "#3B82F6";
    public static int CleanGrade(JsonNode v) { int n = Player.Int(v, -1); return n >= 0 && n <= 12 ? n : 0; }

    static string S(JsonObject m, string k) => Player.Str(m[k]);

    // ---------------------------------------------------------------- summaries (what the parent sees)
    public static JsonObject Summary(Player p)
    {
        var sk = new JsonObject();
        if (p.Json["Skills"] is JsonObject skills)
            foreach (var kv in skills)
            {
                if (kv.Value is not JsonObject) continue;
                var s = p.Skill(kv.Key);
                sk[kv.Key] = new JsonObject { ["Level"] = s.Level, ["Attempts"] = s.Attempts, ["Correct"] = s.Correct, ["Acc"] = Math.Round(s.Accuracy, 2), ["Mastered"] = s.Mastered };
            }
        var mist = new JsonArray();
        foreach (var m in p.Mistakes.TakeLast(10)) mist.Add(new JsonObject { ["When"] = m.When, ["Skill"] = m.Skill, ["Text"] = m.Text, ["Answer"] = m.Answer, ["Given"] = m.Given });
        return new JsonObject { ["grade"] = p.Grade, ["stars"] = p.Stars, ["today"] = p.TodayCount.Done, ["streak"] = p.DayStreak, ["skills"] = sk, ["mistakes"] = mist };
    }

    // ---------------------------------------------------------------- hosting
    /// <summary>Hosts a family room or a game here; the error in words when it couldn't.</summary>
    public static string StartHosting(Player me, string session)
    {
        if (Mode != NetMode.None) Stop();
        try { Host = new GameHost(me.Name, session); }
        catch (Exception e) { return "Could not host a game. " + (e.InnerException?.Message ?? e.Message); }
        Mode = NetMode.Host; Session = session; SelfId = 0; HostName = me.Name;
        Players.Clear();
        if (me.Grade > 0 || ParentPlays) Players[0] = new NetPlayer { Id = 0, Name = me.Name, Color = me.Color, Grade = me.Grade };
        Raise();
        return Host.LastError ?? "";
    }

    public static void Stop()
    {
        Round = null;
        if (Mode == NetMode.Host) { try { Host.Broadcast(Line(new JsonObject { ["t"] = "closed" })); Host.Dispose(); } catch (Exception) { } }
        if (Mode == NetMode.Client) { try { Client.Dispose(); } catch (Exception) { } }
        Mode = NetMode.None; Host = null; Client = null; Players.Clear(); SelfId = -1;
        Raise();
    }

    public static string Line(JsonObject o) => o.ToJsonString();

    public static void Send(JsonObject o)
    {
        if (Mode == NetMode.Client) Client.Send(Line(o));
        else if (Mode == NetMode.Host) HandleHost(0, o);   // the host's own moves go through the same code
    }

    public static JsonArray PlayerList()
    {
        var a = new JsonArray();
        foreach (var p in Players.Values) a.Add(new JsonObject { ["id"] = p.Id, ["name"] = p.Name, ["color"] = p.Color, ["grade"] = p.Grade });
        return a;
    }

    static void BroadcastPlayers()
    {
        Host.Broadcast(Line(new JsonObject { ["t"] = "players", ["players"] = PlayerList(), ["session"] = Session }));
        Raise();
    }

    public static void SetParentPlays(Player me, bool on)
    {
        ParentPlays = on;
        if (Mode != NetMode.Host) return;
        if (on) Players[0] = new NetPlayer { Id = 0, Name = me.Name, Color = me.Color, Grade = 6 };
        else Players.Remove(0);
        BroadcastPlayers();
    }

    /// <summary>The host starts a race or battle for everyone in the room.</summary>
    public static bool StartGameForAll(string game)
    {
        if (Mode != NetMode.Host || Players.Count == 0) return false;
        var list = PlayerList();
        Host.Broadcast(Line(new JsonObject { ["t"] = "start", ["game"] = game, ["players"] = list }));
        Round = null;
        GameStarted?.Invoke(game, Players.Values.ToList());
        return true;
    }

    public static void SendHint(int kid, string text, string from)
    {
        if (Mode != NetMode.Host || string.IsNullOrWhiteSpace(text)) return;
        Host.Send(kid, Line(new JsonObject { ["t"] = "hint", ["text"] = text, ["from"] = from }));
    }

    public static void HostBroadcast(JsonObject o) { if (Mode == NetMode.Host) Host.Broadcast(Line(o)); }

    static void HandleHost(int from, JsonObject m)
    {
        string t = S(m, "t");
        switch (t)
        {
            case "hello":
                if (from == 0) return;
                Players[from] = new NetPlayer { Id = from, Name = CleanName(S(m, "name")), Color = CleanColor(S(m, "color")), Grade = CleanGrade(m["grade"]), Summary = m["summary"] as JsonObject, Current = "Just joined" };
                Host.Send(from, Line(new JsonObject { ["t"] = "welcome", ["id"] = from, ["host"] = HostName, ["session"] = Session }));
                BroadcastPlayers();
                break;
            case "_leave":
                if (Players.Remove(from)) BroadcastPlayers();
                break;
            case "act":
                if (!Players.TryGetValue(from, out var p)) return;
                p.Current = CleanText(S(m, "text"));
                p.Feed.Add(new FeedItem(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture), CleanText(S(m, "text")), CleanText(S(m, "answer"), 40), CleanText(S(m, "given"), 40), m["ok"] is JsonValue ok && ok.TryGetValue(out bool b) && b, CleanText(S(m, "skill"), 20)));
                if (p.Feed.Count > 60) p.Feed.RemoveRange(0, p.Feed.Count - 60);
                if (m["summary"] is JsonObject sum) p.Summary = (JsonObject)sum.DeepClone();
                Raise();
                break;
            case "pos":
                if (double.TryParse(S(m, "pos"), NumberStyles.Float, CultureInfo.InvariantCulture, out double claim)) Game?.HostPos(from, claim);
                break;
            case "hit":
                if (from != 0 && !Players.ContainsKey(from)) return;
                Game?.HostHit(from);
                break;
        }
    }

    // ---------------------------------------------------------------- joining
    public static string Join(Player me, string address, int port)
    {
        if (Mode != NetMode.None) Stop();
        SelfId = -1;   // not a player until the host's 'welcome'
        try { Client = new GameClient(address, port); }
        catch (Exception e) { return "Could not join. " + (e.InnerException?.Message ?? e.Message); }
        Mode = NetMode.Client;
        Client.Send(Line(new JsonObject { ["t"] = "hello", ["name"] = me.Name, ["color"] = me.Color, ["grade"] = me.Grade, ["summary"] = Summary(me) }));
        Raise();
        return "";
    }

    /// <summary>Every answer goes to the family room's screen while joined.</summary>
    public static void SendActivity(Player me, string skill, string text, string answer, string given, bool ok)
    {
        if (Mode != NetMode.Client || me == null) return;
        Client.Send(Line(new JsonObject { ["t"] = "act", ["skill"] = skill, ["text"] = text, ["answer"] = answer, ["given"] = given, ["ok"] = ok, ["summary"] = Summary(me) }));
    }

    static void HandleClient(JsonObject m)
    {
        switch (S(m, "t"))
        {
            case "welcome":
                SelfId = Player.Int(m["id"], -1); HostName = CleanName(S(m, "host")); Session = CleanText(S(m, "session"), 40);
                Raise();
                break;
            case "players":
                Players.Clear();
                if (m["players"] is JsonArray a)
                    foreach (var x in a) if (x is JsonObject o) { int id = Player.Int(o["id"], -1); if (id >= 0) Players[id] = new NetPlayer { Id = id, Name = CleanName(Player.Str(o["name"])), Color = CleanColor(Player.Str(o["color"])), Grade = CleanGrade(o["grade"]) }; }
                Raise();
                break;
            case "start":
            {
                string game = S(m, "game");
                var list = new List<NetPlayer>();
                if (m["players"] is JsonArray pa)
                    foreach (var x in pa) if (x is JsonObject o) { int id = Player.Int(o["id"], -1); if (id >= 0) list.Add(new NetPlayer { Id = id, Name = CleanName(Player.Str(o["name"])), Color = CleanColor(Player.Str(o["color"])), Grade = CleanGrade(o["grade"]) }); }
                if ((game == "race" || game == "boss") && SelfId >= 0 && list.Any(p => p.Id == SelfId)) { Round = null; GameStarted?.Invoke(game, list); }
                break;
            }
            case "race":
                if (m["pos"] is JsonArray pos)
                {
                    var d = new Dictionary<string, double>();
                    foreach (var x in pos) if (x is JsonObject o) d[Player.Str(o["id"])] = Player.Dbl(o["pos"]);
                    Game?.RacePositions(d);
                }
                break;
            case "won": Game?.RaceWon(S(m, "id")); break;
            case "boss":
                Game?.BossState(Player.Int(m["hp"]), Math.Max(1, Player.Int(m["max"], 1)), m["lives"] == null ? -1 : Math.Max(0, Player.Int(m["lives"])), m["attack"] is JsonValue av && av.TryGetValue(out bool att) && att, Player.Int(m["by"], -1));
                break;
            case "over":
            {
                var r = NetRound.FromLine(m.ToJsonString());
                if (r != null && r.Number > roundDone)
                {
                    if (Round != null && Round.Number == r.Number) Round.Left = Math.Min(Round.Left, r.Left + 0.25);
                    else ShowRound(r);
                }
                break;
            }
            case "next": { int n = Player.Int(m["n"], 0); if (n > roundDone) Next(n); break; }
            case "end": Game?.Ended(S(m, "text").Length > 0 ? CleanText(S(m, "text"), 120) : "The host ended the game."); break;
            case "hint":
            {
                string from = CleanName(S(m, "from")), txt = CleanText(S(m, "text"), 300);
                global::LearningGames.Sound.Tick();
                Game?.Hint(from, txt);
                HintArrived?.Invoke(from, txt);
                break;
            }
            case "closed": { string h = HostName; Stop(); Game?.Ended(h + " closed the game."); break; }
            case "_closed": { string h = HostName; Stop(); Game?.Ended("Lost the connection to " + h + "."); break; }
        }
    }

    static void Poll()
    {
        if (Mode == NetMode.Host && Host != null)
            foreach (var msg in Host.Drain())
            {
                JsonObject m = null;
                if (msg.Line == "_join" || msg.Line.Contains("\"_join\"")) continue;
                if (msg.Line.Contains("\"_leave\"")) { HandleHost(msg.From, new JsonObject { ["t"] = "_leave" }); continue; }
                try { m = JsonNode.Parse(msg.Line) as JsonObject; } catch (Exception) { }
                if (m != null) try { HandleHost(msg.From, m); } catch (Exception) { }
            }
        else if (Mode == NetMode.Client && Client != null)
            foreach (var l in Client.Drain())
            {
                JsonObject m = null;
                try { m = JsonNode.Parse(l) as JsonObject; } catch (Exception) { }
                if (m != null) try { HandleClient(m); } catch (Exception) { }
            }
        var d = discovering;
        if (d != null && d.IsCompleted)
        {
            discovering = null;
            if (!d.IsFaulted) { Hosts = d.Result ?? Array.Empty<FoundHost>(); Raise(); }
        }
    }

    // ---------------------------------------------------------------- the end of a network game
    /// <summary>The host declares the outcome: everyone gets the same results and goes back together.</summary>
    public static void Declare(NetRound r)
    {
        if (Mode != NetMode.Host || Round != null) return;
        ShowRound(r);
        roundSent = DateTime.Now;
        Host.Broadcast(r.ToLine());
    }
    public static int NextRoundNumber() { roundNo = Math.Max(roundNo, roundDone) + 1; return roundNo; }

    static void ShowRound(NetRound r) { Round = r; roundAt = DateTime.Now; Raise(); }

    static void TickRound()
    {
        var r = Round;
        if (r == null) return;
        var now = DateTime.Now;
        bool due = r.Tick((now - roundAt).TotalSeconds);
        roundAt = now;
        if (Mode == NetMode.Host)
        {
            if (due) { Host.Broadcast(NetRound.NextLine(r.Number)); Next(r.Number); return; }
            if ((now - roundSent).TotalSeconds >= 1) { roundSent = now; Host.Broadcast(r.ToLine()); }
        }
        else if (Mode != NetMode.Client || r.Missed) { Next(r.Number); return; }   // never heard 'next': go anyway
        Raise();
    }

    static void Next(int n)
    {
        roundDone = Math.Max(roundDone, n);
        Round = null;
        Raise();
        if (Mode != NetMode.None) GoNext?.Invoke();
    }
}
