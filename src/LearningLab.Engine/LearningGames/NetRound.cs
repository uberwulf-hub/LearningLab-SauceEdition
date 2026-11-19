// Learning Games - the end of a game on the home network (2026-10-24, Andrew: "when a person wins, the other person's
// game should end so everyone's not waiting. Move on to next stage or back to menu"). Math Race and Boss Battle already
// end on every computer at once (the host decides: 'won', 'boss'); this is what comes after: the host sends everyone
// the same results ('over': who won, the places, a countdown), and when the countdown ends ('next') everyone goes back
// to the lobby together, where the host can start the next game. A computer that never hears 'next' goes back by itself
// a few seconds later; a player who leaves during the results holds nobody up.
//
//   {"t":"over","n":1,"game":"race","title":"Ana won the race!","winner":"2","secs":8,"rows":[{"id":"2","name":"Ana",
//    "color":"#EAB308","value":"10 of 10","place":1}, ...]}
//   {"t":"next","n":1}
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LearningGames
{
    public sealed class RoundRow { public string Id = "", Name = "", Color = "#3B82F6", Value = ""; public int Place; }

    public sealed class NetRound
    {
        /// <summary>How long the results show before everyone goes back to the lobby (seconds).</summary>
        public const double ResultsSeconds = 8;
        /// <summary>A computer that never hears 'next' goes back by itself this long after its countdown.</summary>
        public const double MissedGoOn = 4;

        public int Number;
        public string Game = "", Title = "", Winner = "";
        public double Left = ResultsSeconds;
        public readonly List<RoundRow> Rows = new List<RoundRow>();

        static string Clip(string s, int max)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "") if (!char.IsControl(c)) sb.Append(c);
            string r = sb.ToString().Trim();
            return r.Length > max ? r.Substring(0, max) : r;
        }
        static string Color(string c) { c = c ?? ""; if (c.Length == 7 && c[0] == '#' && c.Skip(1).All(Uri.IsHexDigit)) return c; return "#3B82F6"; }

        static void Rank(List<RoundRow> rows, Func<RoundRow, double> score)
        {
            int place = 0, n = 0; double last = double.NaN;
            foreach (RoundRow r in rows) { n++; double v = score(r); if (v != last) { place = n; last = v; } r.Place = place; }
        }

        /// <summary>Math Race: the winner first, then by how far each car got (cars level share a place).</summary>
        public static NetRound Race(int number, IList<string> ids, IList<string> names, IList<string> colors, IList<double> pos, int length, string winner)
        {
            var r = new NetRound { Number = number, Game = "race", Winner = winner ?? "" };
            var rows = new List<KeyValuePair<double, RoundRow>>();
            for (int i = 0; i < ids.Count; i++)
            {
                double p = Math.Max(0, Math.Min(length, i < pos.Count ? pos[i] : 0));
                if (ids[i] == r.Winner) p = length + 1;   // the one the host saw cross the line first
                rows.Add(new KeyValuePair<double, RoundRow>(p, new RoundRow { Id = ids[i], Name = Clip(i < names.Count ? names[i] : "", 24), Color = Color(i < colors.Count ? colors[i] : ""), Value = (int)Math.Min(length, p) + " of " + length }));
            }
            foreach (var kv in rows.OrderByDescending(kv => kv.Key)) r.Rows.Add(kv.Value);
            var score = rows.ToDictionary(kv => kv.Value, kv => kv.Key);
            Rank(r.Rows, x => -score[x]);
            RoundRow w = r.Rows.FirstOrDefault(x => x.Id == r.Winner);
            r.Title = w != null ? w.Name + " won the race!" : "The race is over";
            return r;
        }

        /// <summary>Boss Battle: the whole team won or lost together; each player's hits, most first.</summary>
        public static NetRound Boss(int number, bool won, IList<string> ids, IList<string> names, IList<string> colors, IList<int> hits)
        {
            var r = new NetRound { Number = number, Game = "boss", Title = won ? "The team beat the monster!" : "The monster got away!" };
            var rows = new List<RoundRow>();
            var count = new Dictionary<RoundRow, int>();
            for (int i = 0; i < ids.Count; i++)
            {
                int h = Math.Max(0, i < hits.Count ? hits[i] : 0);
                var row = new RoundRow { Id = ids[i], Name = Clip(i < names.Count ? names[i] : "", 24), Color = Color(i < colors.Count ? colors[i] : ""), Value = h + (h == 1 ? " hit" : " hits") };
                rows.Add(row); count[row] = h;
            }
            r.Rows.AddRange(rows.OrderByDescending(x => count[x]));
            Rank(r.Rows, x => -count[x]);
            return r;
        }

        // ---------------------------------------------------------------- the messages
        static string Q(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 32 || c > 126) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        public string ToLine()
        {
            var sb = new StringBuilder("{\"t\":\"over\",\"n\":").Append(Number).Append(",\"game\":").Append(Q(Game)).Append(",\"title\":").Append(Q(Title))
                .Append(",\"winner\":").Append(Q(Winner)).Append(",\"secs\":").Append(Math.Max(0, Left).ToString("0.###", CultureInfo.InvariantCulture)).Append(",\"rows\":[");
            for (int i = 0; i < Rows.Count && i < 12; i++)
            {
                RoundRow r = Rows[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"id\":").Append(Q(r.Id)).Append(",\"name\":").Append(Q(r.Name)).Append(",\"color\":").Append(Q(r.Color)).Append(",\"value\":").Append(Q(r.Value)).Append(",\"place\":").Append(r.Place).Append('}');
            }
            return sb.Append("]}").ToString();
        }

        public static string NextLine(int number) { return "{\"t\":\"next\",\"n\":" + number + "}"; }

        /// <summary>An 'over' line from the host (null if it doesn't make sense). Everything in it is cleaned.</summary>
        public static NetRound FromLine(string json)
        {
            object o;
            try { int i = 0; o = Json.Value(json ?? "", ref i); } catch (Exception) { return null; }
            var d = o as Dictionary<string, object>;
            if (d == null || Json.Str(d, "t") != "over") return null;
            var r = new NetRound { Number = (int)Json.Num(d, "n"), Game = Clip(Json.Str(d, "game"), 10), Title = Clip(Json.Str(d, "title"), 80), Winner = Clip(Json.Str(d, "winner"), 12), Left = Math.Max(0, Math.Min(60, Json.Num(d, "secs"))) };
            if (r.Number <= 0) return null;
            object rows;
            if (d.TryGetValue("rows", out rows) && rows is List<object>)
                foreach (object x in ((List<object>)rows).Take(12))
                {
                    var rd = x as Dictionary<string, object>;
                    if (rd == null) continue;
                    r.Rows.Add(new RoundRow { Id = Clip(Json.Str(rd, "id"), 12), Name = Clip(Json.Str(rd, "name"), 24), Color = Color(Json.Str(rd, "color")), Value = Clip(Json.Str(rd, "value"), 24), Place = Math.Max(0, Math.Min(99, (int)Json.Num(rd, "place"))) });
                }
            return r;
        }

        // ---------------------------------------------------------------- the countdown
        /// <summary>Counts down; true once it is time to go (the host then sends 'next').</summary>
        public bool Tick(double seconds) { Left -= Math.Max(0, seconds); return Left <= 0; }
        /// <summary>A joined computer that never heard 'next' goes anyway.</summary>
        public bool Missed { get { return Left <= -MissedGoOn; } }
        public string CountdownText { get { return Left > 0 ? "Back to the lobby in " + (int)Math.Ceiling(Left - 1e-9) + " s" : "Back to the lobby..."; } }

        /// <summary>The places as lines of text ("1.  Ana (you)   10 of 10").</summary>
        public string PlacesText(string myId)
        {
            var sb = new StringBuilder();
            foreach (RoundRow r in Rows)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(r.Place > 0 ? r.Place + ".  " : "").Append(r.Name).Append(r.Id == myId ? " (you)" : "").Append("   ").Append(r.Value);
            }
            return sb.ToString();
        }
    }

    /// <summary>Just enough JSON reading for the messages above (objects, arrays, strings, numbers, true / false / null).</summary>
    static class Json
    {
        public static string Str(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : ""; }
        public static double Num(Dictionary<string, object> d, string k)
        {
            object v; double n;
            if (!d.TryGetValue(k, out v) || v == null) return 0;
            if (v is double) return (double)v;
            return double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out n) ? n : 0;
        }

        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        public static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("end");
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>();
                i++; Ws(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i);
                    string k = (string)Value(s, ref i);
                    Ws(s, ref i); if (s[i] != ':') throw new FormatException(":"); i++;
                    d[k] = Value(s, ref i);
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw new FormatException("}");
                }
            }
            if (c == '[')
            {
                var l = new List<object>();
                i++; Ws(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw new FormatException("]");
                }
            }
            if (c == '"')
            {
                var sb = new StringBuilder();
                i++;
                while (s[i] != '"')
                {
                    if (s[i] == '\\')
                    {
                        i++;
                        char e = s[i];
                        if (e == 'u') { sb.Append((char)int.Parse(s.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); i += 4; }
                        else sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e == 'r' ? '\r' : e == 'b' ? '\b' : e == 'f' ? '\f' : e);
                    }
                    else sb.Append(s[i]);
                    i++;
                    if (sb.Length > 4000) throw new FormatException("long");
                }
                i++;
                return sb.ToString();
            }
            if (s.Substring(i).StartsWith("true", StringComparison.Ordinal)) { i += 4; return true; }
            if (s.Substring(i).StartsWith("false", StringComparison.Ordinal)) { i += 5; return false; }
            if (s.Substring(i).StartsWith("null", StringComparison.Ordinal)) { i += 4; return null; }
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (i == start) throw new FormatException("value");
            return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}
