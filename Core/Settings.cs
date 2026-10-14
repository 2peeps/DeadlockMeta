using System.IO;
using System.Text.Json;

namespace DeadlockMeta.Core;

static class Files
{
    /// <summary>Writes through a temp file so a crash or power cut never leaves a half-written file behind.</summary>
    public static void WriteAtomic(string path, string text)
    {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        File.Move(tmp, path, true);
    }
}

static class Log
{
    static readonly object gate = new();

    public static void Write(string msg)
    {
        try
        {
            lock (gate)
            {
                string path = Path.Combine(Settings.DataDir, "log.txt");
                if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024) File.Delete(path);
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {msg}{Environment.NewLine}");
            }
        }
        catch { }
    }
}

/// <summary>User choices plus the last place the hero grid was seen. Stored next to the exe.</summary>
public sealed class Settings
{
    // Kept beside the program (not in AppData) so the cache and the log are easy to find, unless that folder
    // is read-only (e.g. unpacked into Program Files): then it goes to %LOCALAPPDATA%\DeadlockMeta.
    public static readonly string DataDir = PickDataDir();
    static readonly string FilePath = Path.Combine(DataDir, "settings.json");
    static readonly object saveGate = new();

    static string PickDataDir()
    {
        string beside = Path.Combine(AppContext.BaseDirectory, "data");
        try
        {
            Directory.CreateDirectory(beside);
            string probe = Path.Combine(beside, ".write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return beside;
        }
        catch
        {
            string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeadlockMeta");
            Directory.CreateDirectory(fallback);
            return fallback;
        }
    }

    public bool OverlayEnabled { get; set; } = true;
    public int StatDays { get; set; } = 14;
    /// <summary>Lowest average match badge counted as "high rank" (tier * 10 + subtier); 81 is Oracle 1.</summary>
    public int HighBadge { get; set; } = 81;

    public int GridClientW { get; set; }
    public int GridClientH { get; set; }
    public double GridX0 { get; set; }
    public double GridY0 { get; set; }
    public double GridScale { get; set; }

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception e) { Log.Write("settings: " + e.Message); }
        return new Settings();
    }

    public void Save()
    {
        // Saved from both the UI and the game watcher thread.
        lock (saveGate)
            try
            {
                Files.WriteAtomic(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception e) { Log.Write("settings save: " + e.Message); }
    }
}
