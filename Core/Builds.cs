using System.IO;
using System.Text.Json;

namespace DeadlockMeta.Core;

public sealed record ItemInfo(int Id, string Name, string Slot, int Tier, int Cost, string? ImageUrl)
{
    public string ImagePath => Path.Combine(ItemCatalog.ImageDir, Id + ".png");
}

/// <summary>Shop items with Russian names, from the API's asset list (a few MB, cached for a day).</summary>
public sealed class ItemCatalog
{
    public static readonly string ImageDir = Path.Combine(Settings.DataDir, "items");

    public Dictionary<int, ItemInfo> Items { get; } = [];

    public static async Task<ItemCatalog> Load()
    {
        Directory.CreateDirectory(ImageDir);
        var c = new ItemCatalog();
        string json = await MetaData.Fetch(MetaData.Api + "assets/items?language=russian", "items_ru.json", false, TimeSpan.FromDays(1));
        using var doc = JsonDocument.Parse(json);
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            if (Str(e, "type") != "upgrade") continue;
            int id = (int)e.GetProperty("id").GetInt64();
            c.Items[id] = new ItemInfo(id, Str(e, "name") ?? "?", Str(e, "item_slot_type") ?? "",
                Int(e, "item_tier"), Int(e, "cost"), Str(e, "shop_image") ?? Str(e, "image"));
        }
        return c;
    }

    static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? (int)v.GetDouble() : 0;

    /// <summary>Downloads the icons of the given items that are not cached yet.</summary>
    public async Task FetchImages(IEnumerable<int> ids)
    {
        var missing = ids.Distinct().Select(id => Items.GetValueOrDefault(id))
            .Where(i => i?.ImageUrl != null && !File.Exists(i.ImagePath)).ToList();
        await Parallel.ForEachAsync(missing, new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (i, ct) =>
        {
            try { await File.WriteAllBytesAsync(i!.ImagePath, await MetaData.Http.GetByteArrayAsync(i.ImageUrl, ct), ct); }
            catch (Exception e) { Log.Write($"item icon {i!.Id}: {e.Message}"); }
        });
    }
}

public sealed record BuildCategory(string Name, bool Optional, List<int> ItemIds);

public sealed record BuildInfo(int Id, string Name, string Author, double WinRate, long Matches, List<BuildCategory> Categories);

public sealed record PhaseItem(int ItemId, double WinRate, long Matches);

public sealed record GamePhase(string Title, List<PhaseItem> Items);

public sealed record HeroDetails(List<BuildInfo> Builds, List<GamePhase> Phases);

/// <summary>Best community builds and best items per game phase for one hero, by real match results.</summary>
public static class BuildService
{
    const int BuildCount = 3, ItemsPerPhase = 6;
    static readonly (string Title, int From, int To)[] Phases =
        [("НАЧАЛО · 0–10 МИН", 0, 600), ("СЕРЕДИНА · 10–25 МИН", 600, 1500), ("КОНЕЦ · 25+ МИН", 1500, 0)];

    static readonly Dictionary<string, (DateTime At, HeroDetails Details)> cache = [];

    public static async Task<HeroDetails> Load(int heroId, Settings s, bool highRanks)
    {
        string key = $"{heroId}:{s.StatDays}:{(highRanks ? s.HighBadge : 0)}";
        lock (cache)
            if (cache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < TimeSpan.FromHours(3)) return hit.Details;

        long since = DateTimeOffset.UtcNow.AddDays(-s.StatDays).ToUnixTimeSeconds();
        since -= since % 3600;
        string filter = $"min_unix_timestamp={since}" + (highRanks ? $"&min_average_badge={s.HighBadge}" : "");

        var builds = LoadBuilds(heroId, filter, highRanks);
        var phases = Phases.Select(p => LoadPhase(heroId, filter, p.Title, p.From, p.To)).ToList();
        var d = new HeroDetails(await builds, [.. await Task.WhenAll(phases)]);
        lock (cache) cache[key] = (DateTime.UtcNow, d);
        return d;
    }

    static async Task<string> Get(string url)
    {
        using var r = await MetaData.Http.GetAsync(url);
        r.EnsureSuccessStatusCode();
        return await r.Content.ReadAsStringAsync();
    }

    // Lower bound of the 95% Wilson interval: a 60% build over 40 games does not beat a 55% one over 3000.
    static double Wilson(long wins, long n)
    {
        if (n == 0) return 0;
        const double z = 1.96;
        double p = (double)wins / n, z2 = z * z / n;
        return (p + z2 / 2 - z * Math.Sqrt(p * (1 - p) / n + z2 / (4.0 * n))) / (1 + z2);
    }

    static async Task<List<BuildInfo>> LoadBuilds(int heroId, string filter, bool highRanks)
    {
        int minMatches = highRanks ? 30 : 100;
        using var stats = JsonDocument.Parse(await Get(
            $"{MetaData.Api}analytics/hero-build-stats/{heroId}?{filter}&min_matches={minMatches}"));
        var top = stats.RootElement.EnumerateArray()
            .Select(e => (Id: (int)e.GetProperty("hero_build_id").GetInt64(), Wins: e.GetProperty("wins").GetInt64(),
                Matches: e.GetProperty("matches").GetInt64()))
            .OrderByDescending(b => Wilson(b.Wins, b.Matches))
            .Take(BuildCount).ToList();
        if (top.Count == 0) return [];

        var details = await Task.WhenAll(top.Select(b => Get($"{MetaData.Api}builds?build_id={b.Id}&only_latest=true")));
        var authors = new Dictionary<long, string>();
        var builds = new List<(int Id, long Author, string Name, List<BuildCategory> Cats, long Wins, long Matches)>();
        for (int i = 0; i < top.Count; i++)
        {
            using var doc = JsonDocument.Parse(details[i]);
            if (doc.RootElement.GetArrayLength() == 0) continue;
            var hb = doc.RootElement[0].GetProperty("hero_build");
            var cats = new List<BuildCategory>();
            if (hb.TryGetProperty("details", out var det) && det.TryGetProperty("mod_categories", out var mc))
                foreach (var c in mc.EnumerateArray())
                {
                    var ids = c.TryGetProperty("mods", out var mods)
                        ? mods.EnumerateArray().Select(m => (int)m.GetProperty("ability_id").GetInt64()).ToList() : [];
                    if (ids.Count == 0) continue;
                    bool optional = c.TryGetProperty("optional", out var o) && o.ValueKind == JsonValueKind.True;
                    cats.Add(new BuildCategory(c.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "", optional, ids));
                }
            builds.Add((top[i].Id, hb.GetProperty("author_account_id").GetInt64(), hb.GetProperty("name").GetString() ?? "",
                cats, top[i].Wins, top[i].Matches));
        }

        try
        {
            string ids = string.Join(",", builds.Select(b => b.Author).Distinct());
            using var steam = JsonDocument.Parse(await Get($"{MetaData.Api}players/steam?account_ids={ids}"));
            foreach (var p in steam.RootElement.EnumerateArray())
                authors[p.GetProperty("account_id").GetInt64()] = p.GetProperty("personaname").GetString() ?? "";
        }
        catch (Exception e) { Log.Write("build authors: " + e.Message); }

        return builds.Select(b => new BuildInfo(b.Id, b.Name.Trim(), authors.GetValueOrDefault(b.Author, ""),
            100.0 * b.Wins / b.Matches, b.Matches, b.Cats)).ToList();
    }

    static async Task<GamePhase> LoadPhase(int heroId, string filter, string title, int from, int to)
    {
        string url = $"{MetaData.Api}analytics/item-stats?hero_id={heroId}&{filter}&min_bought_at_s={from}"
            + (to > 0 ? $"&max_bought_at_s={to}" : "");
        using var doc = JsonDocument.Parse(await Get(url));
        var rows = doc.RootElement.EnumerateArray()
            .Select(e => (Id: (int)e.GetProperty("item_id").GetInt64(), Wins: e.GetProperty("wins").GetInt64(),
                Matches: e.GetProperty("matches").GetInt64()))
            .ToList();
        // Rare picks are left out: only items bought in at least 5% of the most popular item's games count.
        long floor = Math.Max(50, (rows.Count > 0 ? rows.Max(r => r.Matches) : 0) / 20);
        var best = rows.Where(r => r.Matches >= floor)
            .OrderByDescending(r => Wilson(r.Wins, r.Matches))
            .Take(ItemsPerPhase)
            .Select(r => new PhaseItem(r.Id, 100.0 * r.Wins / r.Matches, r.Matches))
            .ToList();
        return new GamePhase(title, best);
    }
}
