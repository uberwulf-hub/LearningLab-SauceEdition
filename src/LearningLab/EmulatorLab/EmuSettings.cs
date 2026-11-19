// Emulator Lab - its settings and the program in the editor: settings.json, the same file App Hub's Emulator Lab wrote
// (PowerShell's ConvertTo-Json of Speed, Screen, Inside, InsideTab, EditorText, EditorFile, ProgName, StopOnCrash and,
// since 2026-10-24, KeptForUpdate). Every field is kept in a JsonObject, so fields this version doesn't know (a later
// version's, or App Hub's) are written back untouched; missing ones keep their defaults; numbers and true / false
// written as text read too. Fixtures in tests\LearningLab.Tests\Fixtures\EmulatorLab.
//
// Saved with LearningLab.Saves.SafeFile: a temporary file swapped in, the five versions before it kept; a file that
// can't be read is copied aside (settings.json.unreadable-<time>) and the newest readable backup is used, and before
// anything is written over a file that couldn't be read it is copied aside again (as App Hub did).
//
// The LabOS disk (disk.img) is next to it: Computer.PrepareDisk backs it up (disk.img.bak1..5) and keeps a damaged one
// aside before LabOS could format it.
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LearningLab.Saves;

namespace LearningLab.EmulatorLab;

public sealed class EmuSettings
{
    public JsonObject Json { get; private set; } = new();
    /// <summary>A sentence for the person when the file couldn't be opened (empty when all is well).</summary>
    public string Message { get; private set; } = "";
    public string File { get; }

    public EmuSettings(string file) { File = file; }

    public int Speed { get => Int("Speed", 7, 0, 7); set => Json["Speed"] = value; }
    /// <summary>0 automatic, 1 text, 2 graphics, 3 both.</summary>
    public int Screen { get => Int("Screen", 0, 0, 3); set => Json["Screen"] = value; }
    public bool Inside { get => Bool("Inside", false); set => Json["Inside"] = value; }
    public int InsideTab { get => Int("InsideTab", 0, 0, 3); set => Json["InsideTab"] = value; }
    public string EditorText { get => Str("EditorText"); set => Json["EditorText"] = value ?? ""; }
    public string EditorFile { get => Str("EditorFile"); set => Json["EditorFile"] = value ?? ""; }
    public string ProgName { get { var s = Str("ProgName"); return s.Length > 0 ? s : "myprog"; } set => Json["ProgName"] = value ?? ""; }
    public bool StopOnCrash { get => Bool("StopOnCrash", false); set => Json["StopOnCrash"] = value; }
    /// <summary>App Hub: set when it closed Emulator Lab for an update with a program not saved to a file.</summary>
    public string KeptForUpdate { get => Str("KeptForUpdate"); set => Json["KeptForUpdate"] = value ?? ""; }

    string Str(string k)
    {
        var n = Json[k];
        if (n is JsonValue v)
        {
            if (v.TryGetValue(out string s)) return s ?? "";
            return v.ToJsonString().Trim('"');
        }
        return "";
    }

    int Int(string k, int def, int min, int max)
    {
        var n = Json[k];
        int r = def;
        if (n is JsonValue v)
        {
            if (v.TryGetValue(out int i)) r = i;
            else if (v.TryGetValue(out double d)) r = (int)d;
            else if (v.TryGetValue(out string s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int p)) r = p;
            else if (v.TryGetValue(out bool b)) r = b ? 1 : 0;
        }
        return Math.Max(min, Math.Min(max, r));
    }

    bool Bool(string k, bool def)
    {
        var n = Json[k];
        if (n is JsonValue v)
        {
            if (v.TryGetValue(out bool b)) return b;
            if (v.TryGetValue(out string s)) return s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1";
            if (v.TryGetValue(out int i)) return i != 0;
        }
        return def;
    }

    /// <summary>Parses settings.json's text; throws when it isn't a JSON object.</summary>
    public static JsonObject Parse(string text)
    {
        if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("The file is empty");
        var node = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        return node as JsonObject ?? throw new InvalidDataException("It isn't a settings file");
    }

    /// <summary>Reads the file (missing: the defaults). Message says what happened when it couldn't be read.</summary>
    public static EmuSettings Load(string file)
    {
        var s = new EmuSettings(file);
        if (!System.IO.File.Exists(file) && SafeFile.Backups(file).Count == 0) return s;
        try
        {
            s.Json = SafeFile.Load(file, b => Parse(Encoding.UTF8.GetString(b)), out SaveLoad info);
            if (!string.IsNullOrEmpty(info?.Message)) s.Message = "Settings: " + info.Message;
        }
        catch (SaveUnreadableException e)
        {
            s.Message = "Settings: your settings couldn't be opened, so Emulator Lab starts with its usual ones. The file is kept as " + Path.GetFileName(e.Quarantined) + ".";
        }
        catch (Exception e)
        {
            s.Message = "Settings: " + e.Message;
        }
        return s;
    }

    public string ToJson() => Json.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    public void Save() => SafeFile.WriteText(File, ToJson());

    /// <summary>On Windows, the first start brings App Hub's Emulator Lab across (settings.json and the LabOS disk,
    /// with their backups): copied, never moved; App Hub's folder is left as it is. Only when this folder has neither.</summary>
    public static void BringAcrossFromAppHub(string dataDir)
    {
        string old = Data.AppHubFolder("EmulatorLab");
        if (old == null) return;
        BringAcross(old, dataDir);
    }

    /// <summary>Copies settings.json and disk.img (and their .bak files) from an App Hub folder; for the checks too.</summary>
    public static void BringAcross(string from, string dataDir)
    {
        foreach (string name in new[] { "settings.json", "disk.img" })
        {
            string mine = Path.Combine(dataDir, name);
            if (System.IO.File.Exists(mine) || SafeFile.Backups(mine).Count > 0) continue;
            string theirs = Path.Combine(from, name);
            var copies = new List<string>();
            if (System.IO.File.Exists(theirs)) copies.Add(theirs);
            copies.AddRange(SafeFile.Backups(theirs));
            foreach (string f in copies)
            {
                try
                {
                    string target = Path.Combine(dataDir, Path.GetFileName(f));
                    if (!System.IO.File.Exists(target)) System.IO.File.Copy(f, target, false);
                    System.IO.File.SetLastWriteTime(target, System.IO.File.GetLastWriteTime(f));
                }
                catch (Exception) { }
            }
        }
    }
}
