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
    /// <summary>
    /// Rank filter for the stats: lowest average match badge (tier * 10 + subtier, 81 is Oracle 1); 0 means all ranks.
    /// Named HighBadge for settings files written by older versions.
    /// </summary>
    public int HighBadge { get; set; } = 81;

    /// <summary>Applying a reticle also removes the per-hero reticles, so every hero gets it.</summary>
    public bool CrosshairAllHeroes { get; set; } = true;
    /// <summary>After the game closes, puts the chosen reticle back if the game changed it (e.g. after a patch).</summary>
    public bool CrosshairGuard { get; set; } = true;
    /// <summary>The in-game HUD mod (timers, reload countdown, frameless minimap) is wanted in the game.</summary>
    public bool HudEnabled { get; set; }
    public bool HudTimers { get; set; } = true;
    public bool HudReload { get; set; } = true;
    public bool HudZipline { get; set; } = true;
    public bool HudCleanMap { get; set; } = true;
    /// <summary>Minimap background opacity in percent when the frame is removed.</summary>
    public int HudMapOpacity { get; set; } = 75;
    /// <summary>Minimap size in percent of the game's own: 100 (as in the game) up to 130.</summary>
    public int HudMapScale { get; set; } = 100;

    [System.Text.Json.Serialization.JsonIgnore]
    public HudOptions HudOptions => new(HudTimers, HudReload, HudZipline, HudCleanMap, HudMapOpacity, HudMapScale);

    /// <summary>Random id for the crosshair popularity counts, so one install counts once; nothing else is sent.</summary>
    public string InstallId { get; set; } = Guid.NewGuid().ToString("N");

    // Where the hero grid was last seen. The game has more than one screen with it (hero select, build your
    // roster) and they place it differently, so a few spots are kept. The single-spot fields are from older
    // versions and only read once.
    public List<GridSpot> GridSpots { get; set; } = [];
    public int GridClientW { get; set; }
    public int GridClientH { get; set; }
    public double GridX0 { get; set; }
    public double GridY0 { get; set; }
    public double GridScale { get; set; }

    public sealed record GridSpot(int ClientW, int ClientH, double X0, double Y0, double Scale);

    /// <summary>Remembers a grid position, most recent first.</summary>
    public void RememberGrid(int clientW, int clientH, double x0, double y0, double scale)
    {
        lock (saveGate)
        {
            GridSpots.RemoveAll(s => s.ClientW == clientW && s.ClientH == clientH
                && Math.Abs(s.X0 - x0) < 3 && Math.Abs(s.Y0 - y0) < 3);
            GridSpots.Insert(0, new GridSpot(clientW, clientH, x0, y0, scale));
            if (GridSpots.Count > 6) GridSpots.RemoveRange(6, GridSpots.Count - 6);
        }
        Save();
    }

    public List<GridSpot> GridSpotsFor(int clientW, int clientH)
    {
        lock (saveGate)
        {
            if (GridSpots.Count == 0 && GridClientW > 0)
                GridSpots.Add(new GridSpot(GridClientW, GridClientH, GridX0, GridY0, GridScale));
            return GridSpots.Where(s => s.ClientW == clientW && s.ClientH == clientH).ToList();
        }
    }

    public static readonly (int Badge, string Name)[] Ranks =
        [(0, "Все ранги"), (71, "Emissary+"), (81, "Oracle+"), (91, "Phantom+"), (101, "Ascendant+"), (111, "Eternus")];

    public static string RankName(int badge) => Ranks.FirstOrDefault(r => r.Badge == badge).Name ?? $"ранг {badge}+";

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
