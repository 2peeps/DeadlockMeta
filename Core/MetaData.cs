using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace DeadlockMeta.Core;

public sealed record HeroInfo(int Id, string Name, string CardUrl, bool Playable)
{
    public string CardPath => Path.Combine(MetaData.CardDir, Id + ".png");
}

public record struct HeroStat(double WinRate, double PickRate, long Matches);

/// <summary>Hero list, card images and winrates from the community API (deadlock-api.com), cached on disk.</summary>
public sealed class MetaData
{
    internal const string Api = "https://api.deadlock-api.com/v1/";
    static readonly TimeSpan MaxAge = TimeSpan.FromHours(3);
    static readonly TimeSpan CardMaxAge = TimeSpan.FromDays(7);
    public static readonly string CardDir = Path.Combine(Settings.DataDir, "cards");

    internal static readonly HttpClient Http = CreateClient();

    public Dictionary<int, HeroInfo> Heroes { get; } = [];
    public Dictionary<int, HeroStat> StatsAll { get; private set; } = [];
    /// <summary>Stats for the selected rank filter; the same as StatsAll when that filter is "all ranks".</summary>
    public Dictionary<int, HeroStat> StatsRank { get; private set; } = [];
    public int RankBadge { get; private set; }
    /// <summary>Higher-rank stats for the in-game panel (the selected rank, or Oracle+ when that is "all ranks").</summary>
    public Dictionary<int, HeroStat> StatsHigh { get; private set; } = [];
    public int HighBadge { get; private set; }
    const int DefaultHighBadge = 81;
    /// <summary>Matches behind StatsRank: shows how thin the data gets at the top ranks.</summary>
    public long RankMatches { get; private set; }
    public DateTime Updated { get; private set; }
    public int StatDays { get; private set; }

    static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("DeadlockMeta-overlay/2");
        return c;
    }

    internal static async Task<string> Fetch(string url, string cacheName, bool force, TimeSpan? maxAge = null)
    {
        string path = Path.Combine(Settings.DataDir, cacheName);
        string? cached = File.Exists(path) ? await File.ReadAllTextAsync(path) : null;
        if (cached != null && !IsJson(cached))
        {
            Log.Write($"cache {cacheName} is damaged, downloading again");
            cached = null;
        }
        if (cached != null && !force && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < (maxAge ?? MaxAge))
            return cached;
        try
        {
            string body = await Http.GetStringAsync(url);
            // A proxy or captive portal can answer with an HTML page; that must not replace a good cache.
            if (!IsJson(body)) throw new InvalidDataException("the server did not return JSON");
            Files.WriteAtomic(path, body);
            return body;
        }
        catch (Exception e)
        {
            Log.Write($"fetch failed {url}: {e.Message}");
            if (cached != null) return cached;
            throw;
        }
    }

    static bool IsJson(string text)
    {
        try
        {
            using var _ = JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException) { return false; }
    }

    static Dictionary<int, HeroStat> ParseStats(string json)
    {
        var res = new Dictionary<int, HeroStat>();
        using var doc = JsonDocument.Parse(json);
        long total = 0;
        foreach (var r in doc.RootElement.EnumerateArray()) total += r.GetProperty("matches").GetInt64();
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            long m = r.GetProperty("matches").GetInt64();
            if (m == 0) continue;
            // Twelve players per match, so this is the share of matches the hero appears in.
            res[r.GetProperty("hero_id").GetInt32()] = new HeroStat(
                100.0 * r.GetProperty("wins").GetInt64() / m, total > 0 ? 100.0 * 12 * m / total : 0, m);
        }
        return res;
    }

    public static async Task<MetaData> Load(Settings s, bool force)
    {
        Directory.CreateDirectory(CardDir);
        var d = new MetaData { StatDays = s.StatDays };

        using (var doc = JsonDocument.Parse(await Fetch(Api + "assets/heroes", "heroes.json", force)))
            foreach (var h in doc.RootElement.EnumerateArray())
            {
                if (!h.TryGetProperty("images", out var img) || img.ValueKind != JsonValueKind.Object) continue;
                if (!img.TryGetProperty("top_bar_vertical_image", out var url) || url.ValueKind != JsonValueKind.String) continue;
                bool playable = h.TryGetProperty("player_selectable", out var ps) && ps.ValueKind == JsonValueKind.True
                    && !(h.TryGetProperty("disabled", out var dis) && dis.ValueKind == JsonValueKind.True);
                int id = h.GetProperty("id").GetInt32();
                d.Heroes[id] = new HeroInfo(id, h.GetProperty("name").GetString() ?? "?", url.GetString()!, playable);
            }

        long since = DateTimeOffset.UtcNow.AddDays(-s.StatDays).ToUnixTimeSeconds();
        since -= since % 3600;
        string q = $"{Api}analytics/hero-stats?min_unix_timestamp={since}";
        string allFile = $"stats_all_{s.StatDays}.json";
        var all = Fetch(q, allFile, force);
        // The in-game panel always shows a higher-rank winrate next to "all ranks": the selected rank, or Oracle+
        // when the table is set to all ranks.
        int overlayBadge = s.HighBadge > 0 ? s.HighBadge : DefaultHighBadge;
        var high = Fetch($"{q}&min_average_badge={overlayBadge}", $"stats_high_{s.StatDays}_{overlayBadge}.json", force);
        d.StatsAll = ParseStats(await all);
        d.StatsHigh = ParseStats(await high);
        d.HighBadge = overlayBadge;
        d.StatsRank = s.HighBadge > 0 ? d.StatsHigh : d.StatsAll;
        d.RankBadge = s.HighBadge;
        // Each match is counted once per player.
        d.RankMatches = d.StatsRank.Values.Sum(x => x.Matches) / 12;
        d.Updated = File.GetLastWriteTime(Path.Combine(Settings.DataDir, allFile));

        await d.DownloadCards();
        return d;
    }

    async Task DownloadCards()
    {
        var missing = Heroes.Values.Where(h => !File.Exists(h.CardPath)
            || DateTime.UtcNow - File.GetLastWriteTimeUtc(h.CardPath) > CardMaxAge).ToList();
        await Parallel.ForEachAsync(missing, new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (h, ct) =>
        {
            try
            {
                byte[] bytes = await Http.GetByteArrayAsync(h.CardUrl, ct);
                await File.WriteAllBytesAsync(h.CardPath, bytes, ct);
            }
            catch (Exception e) { Log.Write($"card {h.Name}: {e.Message}"); }
        });
    }

    public Matcher BuildMatcher()
    {
        var m = new Matcher();
        foreach (var h in Heroes.Values)
        {
            if (!File.Exists(h.CardPath)) continue;
            try { m.AddTemplate(h.Id, ScreenGrabber.Load(h.CardPath)); }
            catch (Exception e) { Log.Write($"card {h.Name}: {e.Message}"); }
        }
        return m;
    }
}
