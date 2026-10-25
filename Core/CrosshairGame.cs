using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DeadlockMeta.Core;

/// <summary>
/// The reticle in the game's own settings file. Since the 2025 settings rework the game keeps it in the Steam Cloud
/// file userdata\&lt;account&gt;\1422450\remote\cfg\citadel_hero_settings.lst: a "base" block plus optional per-hero
/// overrides. The file is only changed while the game is closed (the game rewrites it on exit), and a copy of the
/// previous version is kept in data\backups first.
/// </summary>
public static class CrosshairGame
{
    const string AppId = "1422450";
    const long SteamId64Base = 76561197960265728;
    const int BackupsKept = 15;
    static readonly string BackupDir = Path.Combine(Settings.DataDir, "backups");
    static readonly Regex KeyValueRx = new(@"^(\s*)([A-Za-z0-9_]+)\s*=\s*""?([^""]*)""?\s*$", RegexOptions.Compiled);
    static readonly Regex BlockNameRx = new(@"^\s*([A-Za-z0-9_]+)\s*=\s*(\{)?\s*$", RegexOptions.Compiled);

    public static bool IsGameRunning()
    {
        foreach (string name in new[] { "deadlock", "project8" })
        {
            var ps = Process.GetProcessesByName(name);
            bool any = ps.Length > 0;
            foreach (var p in ps) p.Dispose();
            if (any) return true;
        }
        return false;
    }

    public static string? SteamDir()
    {
        try
        {
            if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string p && Directory.Exists(p))
                return Path.GetFullPath(p);
        }
        catch { }
        string fallback = @"C:\Program Files (x86)\Steam";
        return Directory.Exists(fallback) ? fallback : null;
    }

    /// <summary>
    /// The settings file of the Steam account that played last: the account Steam marks as most recent when it has
    /// the file, otherwise whichever account's file changed last.
    /// </summary>
    public static string? SettingsFile()
    {
        if (SteamDir() is not { } steam) return null;
        string userdata = Path.Combine(steam, "userdata");
        if (!Directory.Exists(userdata)) return null;

        string FileOf(string account) => Path.Combine(userdata, account, AppId, "remote", "cfg", "citadel_hero_settings.lst");
        if (MostRecentAccount(steam) is { } recent && File.Exists(FileOf(recent))) return FileOf(recent);
        return Directory.EnumerateDirectories(userdata)
            .Select(d => FileOf(Path.GetFileName(d)))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    static string? MostRecentAccount(string steam)
    {
        try
        {
            string path = Path.Combine(steam, "config", "loginusers.vdf");
            if (!File.Exists(path)) return null;
            string text = File.ReadAllText(path);
            foreach (Match m in Regex.Matches(text, @"""(\d{17})""\s*\{([^}]*)\}"))
                if (Regex.IsMatch(m.Groups[2].Value, @"""MostRecent""\s*""1"""))
                    return (long.Parse(m.Groups[1].Value) - SteamId64Base).ToString();
        }
        catch (Exception e) { Log.Write("steam accounts: " + e.Message); }
        return null;
    }

    /// <summary>
    /// The game's screen height in pixels (video.txt next to the settings file), which the reticle scales with:
    /// its numbers are pixels at 1080p. Null when unknown.
    /// </summary>
    public static int? GameHeight()
    {
        try
        {
            if (SettingsFile() is not { } settings) return null;
            string video = Path.Combine(Path.GetDirectoryName(settings)!, "..", "..", "local", "cfg", "video.txt");
            if (!File.Exists(video)) return null;
            var m = Regex.Match(File.ReadAllText(video), @"""setting\.defaultresheight""\s*""(\d+)""");
            return m.Success && int.TryParse(m.Groups[1].Value, out int h) && h >= 360 ? h : null;
        }
        catch { return null; }
    }

    /// <summary>The game folder (…\steamapps\common\Deadlock), from the Steam library that has it installed.</summary>
    public static string? GameDir()
    {
        if (SteamDir() is not { } steam) return null;
        var libraries = new List<string> { steam };
        try
        {
            string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), @"""path""\s*""([^""]+)"""))
                    libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
        }
        catch { }
        foreach (string lib in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string dir = Path.Combine(lib, "steamapps", "common", "Deadlock");
            if (File.Exists(Path.Combine(lib, "steamapps", $"appmanifest_{AppId}.acf")) && Directory.Exists(dir)) return dir;
        }
        return null;
    }

    /// <summary>The base reticle from the settings file; settings the file does not list keep the game defaults.</summary>
    public static Crosshair? ReadBase(string path)
    {
        var lines = File.ReadAllLines(path);
        var c = new Crosshair();
        bool any = false;
        Walk(lines, (p, key, value, _) =>
        {
            if (p is not ["base"]) return;
            if (Crosshair.Settings.FirstOrDefault(s => s.FileKey == key) is { } s)
            {
                c.SetFromText(s, value);
                any = true;
            }
        });
        return any ? c : null;
    }

    /// <summary>Heroes that have a reticle of their own, which the base reticle does not reach.</summary>
    public static List<string> HeroOverrides(string path)
    {
        var heroes = new List<string>();
        Walk(File.ReadAllLines(path), (p, key, _, _) =>
        {
            if (p is ["heroes", var hero] && key.StartsWith("crosshair_") && !heroes.Contains(hero)) heroes.Add(hero);
        });
        return heroes;
    }

    /// <summary>
    /// Writes the reticle into the base block. With <paramref name="allHeroes"/> the per-hero reticle settings are
    /// removed too, so every hero uses this one.
    /// </summary>
    public static void WriteBase(string path, Crosshair c, bool allHeroes)
    {
        string original = File.ReadAllText(path);
        string newline = original.Contains("\r\n") ? "\r\n" : "\n";
        var lines = original.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        Backup(path);

        var written = new HashSet<string>();
        var drop = new HashSet<int>();
        int baseEnd = -1;
        string indent = "\t\t";
        Walk(lines.ToArray(), (p, key, _, i) =>
        {
            if (p is ["base"])
            {
                if (key == "crosshair_themed")
                {
                    lines[i] = KeyValueRx.Match(lines[i]).Groups[1].Value + "crosshair_themed = \"false\"";
                    written.Add(key);
                }
                else if (Crosshair.Settings.FirstOrDefault(s => s.FileKey == key) is { } s)
                {
                    indent = KeyValueRx.Match(lines[i]).Groups[1].Value;
                    lines[i] = $"{indent}{key} = \"{c.FileValue(s)}\"";
                    written.Add(key);
                }
            }
            else if (allHeroes && p is ["heroes", _] && key.StartsWith("crosshair_")) drop.Add(i);
        }, (p, i) => { if (p is ["base"]) baseEnd = i; });

        if (baseEnd < 0) throw new InvalidDataException("в файле настроек нет блока base");
        var missing = new List<string>();
        if (!written.Contains("crosshair_themed")) missing.Add($"{indent}crosshair_themed = \"false\"");
        foreach (var s in Crosshair.Settings)
            if (!written.Contains(s.FileKey)) missing.Add($"{indent}{s.FileKey} = \"{c.FileValue(s)}\"");
        var output = new List<string>(lines.Count + missing.Count);
        for (int i = 0; i < lines.Count; i++)
        {
            if (i == baseEnd) output.AddRange(missing);
            if (!drop.Contains(i)) output.Add(lines[i]);
        }
        Files.WriteAtomic(path, string.Join(newline, output) + newline);
    }

    /// <summary>
    /// Goes through a KV3 text file: <paramref name="onValue"/> gets each key = value line with the block path it is
    /// in, <paramref name="onClose"/> the line index of each closing brace with the block it closes.
    /// </summary>
    static void Walk(string[] lines, Action<string[], string, string, int> onValue, Action<string[], int>? onClose = null)
    {
        var stack = new List<string>();
        string? pending = null;
        for (int i = 0; i < lines.Length; i++)
        {
            string t = lines[i].Trim();
            if (t.Length == 0 || t.StartsWith("<!--") || t.StartsWith("//")) continue;
            if (t == "{")
            {
                // The outermost block has no name; paths below leave it out.
                stack.Add(pending ?? "");
                pending = null;
                continue;
            }
            if (t == "}")
            {
                if (stack.Count > 0)
                {
                    onClose?.Invoke(stack.Skip(1).ToArray(), i);
                    stack.RemoveAt(stack.Count - 1);
                }
                continue;
            }
            if (BlockNameRx.Match(lines[i]) is { Success: true } b)
            {
                if (b.Groups[2].Success) stack.Add(b.Groups[1].Value);
                else pending = b.Groups[1].Value;
                continue;
            }
            if (KeyValueRx.Match(lines[i]) is { Success: true } kv)
                onValue(stack.Skip(1).ToArray(), kv.Groups[2].Value, kv.Groups[3].Value, i);
        }
    }

    static void Backup(string path)
    {
        try
        {
            Directory.CreateDirectory(BackupDir);
            File.Copy(path, Path.Combine(BackupDir, $"citadel_hero_settings_{DateTime.Now:yyyyMMdd_HHmmss}.lst"), true);
            foreach (var old in new DirectoryInfo(BackupDir).GetFiles("citadel_hero_settings_*.lst")
                .OrderByDescending(f => f.Name).Skip(BackupsKept))
                old.Delete();
        }
        catch (Exception e) { Log.Write("settings backup: " + e.Message); }
    }

    public static string BackupFolder
    {
        get
        {
            Directory.CreateDirectory(BackupDir);
            return BackupDir;
        }
    }

    /// <summary>
    /// The reticle from before the settings rework, when the game kept it in console variables
    /// (game\citadel\cfg\machine_convars.vcfg). Null when that file has none or only the defaults.
    /// </summary>
    public static Crosshair? ReadLegacy()
    {
        try
        {
            if (GameDir() is not { } dir) return null;
            string path = Path.Combine(dir, "game", "citadel", "cfg", "machine_convars.vcfg");
            if (!File.Exists(path)) return null;
            var c = new Crosshair();
            int found = 0;
            foreach (string line in File.ReadLines(path))
            {
                var m = Regex.Match(line, @"""citadel_crosshair_([a-z_]+)""\s*""([^""]*)""");
                if (m.Success && Crosshair.Settings.FirstOrDefault(s => s.Name == m.Groups[1].Value) is { } s)
                {
                    c.SetFromText(s, m.Groups[2].Value);
                    found++;
                }
            }
            return found >= 5 && !c.SameAs(Crosshair.Default) ? c : null;
        }
        catch (Exception e)
        {
            Log.Write("legacy crosshair: " + e.Message);
            return null;
        }
    }
}
