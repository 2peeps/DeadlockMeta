using System.IO;
using System.Text.Json;

namespace DeadlockMeta.Core;

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
    // Kept beside the program (not in AppData) so the cache and the log are easy to find.
    public static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    static readonly string FilePath = Path.Combine(DataDir, "settings.json");

    public bool OverlayEnabled { get; set; } = true;
    public int StatDays { get; set; } = 14;
    /// <summary>Lowest average match badge counted as "high rank" (tier * 10 + subtier); 81 is Oracle 1.</summary>
    public int HighBadge { get; set; } = 81;

    public int GridClientW { get; set; }
    public int GridClientH { get; set; }
    public double GridX0 { get; set; }
    public double GridY0 { get; set; }

    public static Settings Load()
    {
        Directory.CreateDirectory(DataDir);
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
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) { Log.Write("settings save: " + e.Message); }
    }
}
