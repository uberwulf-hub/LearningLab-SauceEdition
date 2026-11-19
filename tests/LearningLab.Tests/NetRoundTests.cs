// Learning Games - checks for the end of a network game (Engine\NetRound.cs), run by LearningGames.ps1 -Test: the
// results message, the places, the countdown, and a host with two players on 127.0.0.1 (one drops during the results).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace LearningGames
{
    public static partial class MathTests
    {
        /// <summary>Lines from a client until one is an 'over' / 'next' message (or the time runs out).</summary>
        static string WaitFor(GameClient c, string kind, int ms, List<string> keep)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                foreach (string l in c.Drain()) { keep.Add(l); if (l.Contains("\"t\":\"" + kind + "\"")) return l; }
                Thread.Sleep(5);
            }
            return null;
        }

        static void NetRounds_()
        {
            // places and the message
            NetRound race = NetRound.Race(1, new[] { "0", "2", "3" }, new[] { "Kim", "Ana \"Speedy\"", "Lee\u00e9" }, new[] { "#EAB308", "bad", "#22C55E" }, new[] { 7.0, 10.0, 7.0 }, 10, "2");
            Ok(race.Rows[0].Id == "2" && race.Rows[0].Place == 1 && race.Rows[1].Place == 2 && race.Rows[2].Place == 2 && race.Rows[0].Value == "10 of 10" && race.Title == "Ana \"Speedy\" won the race!", "race: the winner first, cars level share a place");
            Ok(race.Rows[0].Color == "#3B82F6", "race: a bad colour becomes the default");
            NetRound back = NetRound.FromLine(race.ToLine());
            Ok(back != null && back.Number == 1 && back.Game == "race" && back.Winner == "2" && back.Rows.Count == 3 && back.Rows[0].Name == "Ana \"Speedy\"" && back.Rows[2].Name == "Lee\u00e9" && Math.Abs(back.Left - NetRound.ResultsSeconds) < 0.01 && back.Rows[1].Place == 2, "race: the results message reads back the same");
            NetRound boss = NetRound.Boss(2, true, new[] { "0", "1" }, new[] { "Kim", "Sam" }, new[] { "#EAB308", "#3B82F6" }, new[] { 3, 5 });
            Ok(boss.Rows[0].Name == "Sam" && boss.Rows[0].Value == "5 hits" && boss.Title.Contains("beat"), "boss: the team's result, most hits first");
            Ok(NetRound.FromLine("{\"t\":\"over\",\"n\":0}") == null && NetRound.FromLine("not json") == null && NetRound.FromLine("{\"t\":\"hint\",\"n\":3}") == null, "a broken or other message is ignored");
            Ok(back != null && back.PlacesText("3").Contains("Lee\u00e9 (you)") && back.CountdownText == "Back to the lobby in 8 s", "the places and the countdown in words");
            // the countdown: the host goes on after 8 s; a computer that missed 'next' goes 4 s later
            var t = new NetRound();
            int ticks = 0; while (!t.Tick(0.1) && ticks < 200) ticks++;
            Ok(ticks >= 78 && ticks <= 80 && !t.Missed, "the countdown ends after 8 seconds");
            for (int i = 0; i < 41; i++) t.Tick(0.1);
            Ok(t.Missed, "a computer that never hears 'next' goes back by itself");

            // a host and two players on 127.0.0.1: the results reach both; one drops; the other still goes on with the host
            bool was = GameHost.LoopbackOnly;
            GameHost.LoopbackOnly = true;
            GameHost host = null; GameClient a = null, b = null;
            try
            {
                host = new GameHost("Kim", "Math Race");
                a = new GameClient("127.0.0.1", host.Port);
                b = new GameClient("127.0.0.1", host.Port);
                var sw = Stopwatch.StartNew();
                while (host.PlayerCount < 2 && sw.ElapsedMilliseconds < 3000) Thread.Sleep(5);
                Ok(host.PlayerCount == 2, "net: two players joined the host");
                host.Broadcast(race.ToLine());
                var seenA = new List<string>(); var seenB = new List<string>();
                sw.Restart();
                string la = WaitFor(a, "over", 2000, seenA), lb = WaitFor(b, "over", 2000, seenB);
                NetRound ra = NetRound.FromLine(la), rb = NetRound.FromLine(lb);
                Ok(ra != null && rb != null && ra.Number == rb.Number && ra.Winner == "2" && rb.Rows.Count == 3 && sw.ElapsedMilliseconds < 1000, "net: both players get the same results within a second (" + sw.ElapsedMilliseconds + " ms)");
                b.Dispose();   // Sam's computer drops during the results
                // the host's countdown is its own: it goes on regardless
                var hostRound = NetRound.FromLine(race.ToLine());
                double clock = 0; bool due = false;
                while (!due && clock < 20) { clock += 0.1; due = hostRound.Tick(0.1); }
                sw.Restart();
                host.Broadcast(NetRound.NextLine(hostRound.Number));
                string na = WaitFor(a, "next", 2000, seenA);
                Ok(due && clock > 7.9 && clock < 8.2 && na != null && na.Contains("\"n\":1") && sw.ElapsedMilliseconds < 1000, "net: a player dropping holds nobody up: the other hears 'go on' when the countdown ends");
                sw.Restart();
                while (host.PlayerCount > 1 && sw.ElapsedMilliseconds < 3000) { host.Drain(); Thread.Sleep(5); }
                Ok(host.PlayerCount == 1, "net: the host noticed the one who dropped");
            }
            catch (Exception ex) { Ok(false, "net: " + ex.GetType().Name + ": " + ex.Message); }
            finally
            {
                if (a != null) a.Dispose();
                if (b != null) b.Dispose();
                if (host != null) host.Dispose();
                GameHost.LoopbackOnly = was;
            }
        }
    }
}
