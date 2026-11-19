// Learning Lab - Spelling Bee: what is saved, through the safe-save helper (five backups; a file that can't be read is
// copied aside, a backup is used, and with no good backup nothing is written over it until the person chooses).
//   <data>\SpellingBee\players.json  - per player (by Learning Games player name):
//     { "format": 1, "players": { "Sam": { "tricky": ["cabin", "fertilizer|fertiliser"], "lists": ["p2"], "howMany": 25,
//       "shuffle": true, "autoSay": true, "history": [ { "when": "2026-11-11 17:20", "lists": "Starter Words",
//       "right": 20, "total": 25 } ] } } }
//   <data>\SpellingBee\lists\<id>.json - each imported list (all players share them):
//     { "format": 1, "id": "my-0123456789", "name": "Week 3", "emoji": "...", "note": "...", "words": ["cabin", ...] }
// Unknown fields are kept and written back.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LearningLab.Saves;

namespace LearningLab.SpellingBee
{
    static class J
    {
        public static readonly JsonSerializerOptions Indented = new JsonSerializerOptions { WriteIndented = true };
        public static JsonObject Parse(byte[] b)
        {
            var o = JsonNode.Parse(Encoding.UTF8.GetString(b).TrimStart('﻿')) as JsonObject;
            if (o == null) throw new FormatException("not a Spelling Bee file");
            return o;
        }
        public static string Str(JsonNode n) { try { return n != null && n.AsValue().TryGetValue(out string s) ? s : null; } catch (InvalidOperationException) { return null; } }
        public static int Int(JsonNode n, int dflt)
        {
            if (n == null) return dflt;
            try { var v = n.AsValue(); if (v.TryGetValue(out int i)) return i; if (v.TryGetValue(out double d)) return (int)d; if (v.TryGetValue(out string s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) return i; }
            catch (InvalidOperationException) { }
            return dflt;
        }
        public static bool Bool(JsonNode n, bool dflt) { try { return n != null && n.AsValue().TryGetValue(out bool b) ? b : dflt; } catch (InvalidOperationException) { return dflt; } }
        public static List<string> Strs(JsonNode n) => n is JsonArray a ? a.Select(Str).Where(x => !string.IsNullOrEmpty(x)).ToList() : new List<string>();
        public static JsonArray Arr(IEnumerable<string> l) { var a = new JsonArray(); foreach (var s in l) a.Add(s); return a; }
    }

    /// <summary>Everyone's Spelling Bee settings, tricky words and history.</summary>
    public sealed class BeePlayers
    {
        JsonObject root = new JsonObject { ["format"] = 1 };
        public string File { get; }
        public string Message { get; private set; }
        /// <summary>The file couldn't be read, nor any backup: nothing is saved until the person chooses Start fresh.</summary>
        public bool Blocked { get; private set; }
        public static Func<DateTime> Now { get; set; } = () => DateTime.Now;

        BeePlayers(string file) { File = file; }

        public static BeePlayers Load(string file)
        {
            var p = new BeePlayers(file);
            if (!System.IO.File.Exists(file) && SafeFile.Backups(file).Count == 0) return p;
            try
            {
                p.root = SafeFile.Load(file, J.Parse, out SaveLoad info);
                p.Message = string.IsNullOrEmpty(info?.Message) ? null : info.Message;
            }
            catch (Exception)
            {
                p.Blocked = true;
                p.Message = "The saved spelling practice (tricky words, scores and choices) couldn't be read, nor any of its backups. It is kept as it was, and nothing is saved over it.";
            }
            return p;
        }

        public void StartFresh() { root = new JsonObject { ["format"] = 1 }; Blocked = false; Message = null; }

        /// <summary>Saves; false (and nothing written) while blocked.</summary>
        public bool Save()
        {
            if (Blocked) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(File));
            SafeFile.WriteText(File, root.ToJsonString(J.Indented));
            return true;
        }

        public BeePlayer For(string name)
        {
            name = string.IsNullOrWhiteSpace(name) ? "Player" : name.Trim();
            var all = root["players"] as JsonObject;
            if (all == null) { all = new JsonObject(); root["players"] = all; }
            foreach (var kv in all)
                if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase) && kv.Value is JsonObject found) return new BeePlayer(found);
            var o = new JsonObject();
            all[name] = o;
            return new BeePlayer(o);
        }
    }

    public sealed record BeeHistory(string When, string Lists, int Right, int Total);

    public sealed class BeePlayer
    {
        readonly JsonObject o;
        public BeePlayer(JsonObject o) { this.o = o; }

        /// <summary>The words missed before, as "a|b" tokens (the main spelling first).</summary>
        public List<string> Tricky => J.Strs(o["tricky"]);
        public List<BeeWord> TrickyWords => Tricky.Select(t => BeeWord.Parse(t, "tricky")).Where(w => w != null).ToList();

        public void AddTricky(BeeWord w)
        {
            var l = Tricky;
            if (l.Any(t => BeeWord.Parse(t)?.Word == w.Word)) return;
            l.Add(w.Token);
            o["tricky"] = J.Arr(l);
        }

        public void RemoveTricky(BeeWord w)
        {
            var l = Tricky;
            if (l.RemoveAll(t => BeeWord.Parse(t)?.Word == w.Word) > 0) o["tricky"] = J.Arr(l);
        }

        /// <summary>The chosen lists' ids (null: never chosen).</summary>
        public List<string> Lists { get => o["lists"] is JsonArray ? J.Strs(o["lists"]) : null; set => o["lists"] = J.Arr(value ?? new List<string>()); }
        /// <summary>How many words a run has: 10, 25, 50, or 0 for all.</summary>
        public int HowMany { get { int n = J.Int(o["howMany"], 25); return n == 10 || n == 25 || n == 50 || n == 0 ? n : 25; } set => o["howMany"] = value; }
        public bool Shuffle { get => J.Bool(o["shuffle"], true); set => o["shuffle"] = value; }
        public bool AutoSay { get => J.Bool(o["autoSay"], true); set => o["autoSay"] = value; }

        public List<BeeHistory> History
            => o["history"] is JsonArray a
                ? a.OfType<JsonObject>().Select(h => new BeeHistory(J.Str(h["when"]) ?? "", J.Str(h["lists"]) ?? "", J.Int(h["right"], 0), J.Int(h["total"], 0))).ToList()
                : new List<BeeHistory>();

        public void AddHistory(string lists, int right, int total)
        {
            var a = o["history"] as JsonArray;
            if (a == null) { a = new JsonArray(); o["history"] = a; }
            a.Add(new JsonObject { ["when"] = BeePlayers.Now().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), ["lists"] = lists, ["right"] = right, ["total"] = total });
            while (a.Count > 300) a.RemoveAt(0);
        }
    }

    /// <summary>The imported word lists on this device (lists\&lt;id&gt;.json).</summary>
    public sealed class BeeLists
    {
        public string Folder { get; }
        /// <summary>Sentences about list files that couldn't be read (they are left as they are).</summary>
        public List<string> Problems { get; } = new List<string>();
        public BeeLists(string folder) { Folder = folder; }

        public List<BeeList> Load()
        {
            Problems.Clear();
            var result = new List<BeeList>();
            if (!Directory.Exists(Folder)) return result;
            var files = Directory.GetFiles(Folder, "*.json").ToList();
            // a list whose file is gone but has backups (a crash between two steps) still counts
            foreach (var b in Directory.GetFiles(Folder, "*.json.bak1"))
            {
                string f = b.Substring(0, b.Length - 5);
                if (!files.Contains(f)) files.Add(f);
            }
            foreach (var f in files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var o = SafeFile.Load(f, J.Parse, out SaveLoad info);
                    var l = FromJson(o, Path.GetFileNameWithoutExtension(f));
                    if (l != null) l.SavedFile = f;
                    if (l != null) result.Add(l);
                    if (!string.IsNullOrEmpty(info?.Message)) Problems.Add((l?.Name ?? Path.GetFileName(f)) + ": " + info.Message);
                }
                catch (Exception) { Problems.Add("The word list " + Path.GetFileName(f) + " couldn't be read. It is kept as it was, in case it can be mended."); }
            }
            return result;
        }

        static BeeList FromJson(JsonObject o, string fileId)
        {
            var l = new BeeList { Json = o, Id = J.Str(o["id"]) ?? fileId, Name = J.Str(o["name"]) ?? "My words", Emoji = J.Str(o["emoji"]) ?? "", Note = J.Str(o["note"]) ?? "" };
            l.Words = J.Strs(o["words"]).Select(t => BeeWord.Parse(t, l.Id)).Where(w => w != null).ToList();
            return l;
        }

        string FileOf(BeeList l) => l.SavedFile ?? Path.Combine(Folder, l.Id + ".json");

        /// <summary>Saves a list (new or changed), keeping fields this version doesn't know.</summary>
        public void Save(BeeList l)
        {
            if (l.BuiltIn) throw new InvalidOperationException("The lists that come with the app aren't saved.");
            if (string.IsNullOrEmpty(l.Id) || l.Id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || l.Id.Contains("..")) l.Id = BeeImport.NewId();
            var o = l.Json ?? new JsonObject();
            o["format"] = 1; o["id"] = l.Id; o["name"] = l.Name; o["emoji"] = l.Emoji ?? ""; o["note"] = l.Note ?? "";
            o["words"] = J.Arr(l.Words.Select(w => w.Token));
            l.Json = o;
            Directory.CreateDirectory(Folder);
            string f = FileOf(l);
            if (SafeFile.IsMarkedUnreadable(f)) throw new IOException("That list's file couldn't be read, so it isn't saved over.");
            SafeFile.WriteText(f, o.ToJsonString(J.Indented));
            l.SavedFile = f;
        }

        /// <summary>Deletes a list (the person asked to), its file and its backups.</summary>
        public void Delete(BeeList l)
        {
            string f = FileOf(l);
            foreach (var b in SafeFile.Backups(f)) try { System.IO.File.Delete(b); } catch (IOException) { }
            if (System.IO.File.Exists(f)) System.IO.File.Delete(f);
            l.SavedFile = null;
        }
    }
}
