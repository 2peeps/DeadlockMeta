using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeadlockMeta.Core;

/// <summary>One reticle in a list: from the built-in catalog, from other players, or the player's own.</summary>
public sealed class CrosshairItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>The 19 numbers (Crosshair.ToLegacyCode).</summary>
    public string Code { get; set; } = "";
    public DateTime Added { get; set; }

    /// <summary>Catalog shape this is one colour of; a reticle with no siblings is its own group.</summary>
    [JsonIgnore] public string Group { get => string.IsNullOrEmpty(group) ? Id : group; set => group = value; }
    string? group;

    [JsonIgnore] public string Source { get; set; } = "";
    [JsonIgnore] public long Applies { get; set; }
    [JsonIgnore] public long Likes { get; set; }
    [JsonIgnore] public int Order { get; set; }

    Crosshair? value;
    [JsonIgnore] public Crosshair Value => value ??= Crosshair.FromLegacyCode(Code) ?? Crosshair.Default;

    [JsonIgnore] public string Shape => CrosshairStyle.Shape(Value);
    [JsonIgnore] public string ColorName => CrosshairStyle.ColorName(Value);
}

/// <summary>Sorts reticles into the shape and colour groups the filters show.</summary>
public static class CrosshairStyle
{
    public static readonly (string Key, string Name)[] Shapes =
    [
        ("", "Все"), ("dot", "Точка"), ("ring", "Кружок и мишень"), ("cross", "Крестик"), ("crossdot", "Крестик с точкой"),
        ("plus", "Плюс"), ("squares", "Квадратики"), ("hollow", "Контурные"), ("combo", "С кругом"),
        ("moving", "Расходится при стрельбе"),
    ];

    public static readonly (string Name, byte R, byte G, byte B)[] Colors =
    [
        ("Белый", 255, 255, 255), ("Зелёный", 0, 255, 0), ("Лаймовый", 190, 255, 0), ("Голубой", 0, 255, 255),
        ("Синий", 40, 110, 255), ("Фиолетовый", 170, 90, 255), ("Розовый", 255, 0, 255), ("Красный", 255, 0, 0),
        ("Оранжевый", 255, 140, 0), ("Жёлтый", 255, 255, 0), ("Чёрный", 0, 0, 0),
    ];

    // The game draws a static reticle's pips this far from the centre at gap 0 (1080p).
    public const double StaticBase = 11, MovingBase = 17;

    public static bool HasPips(Crosshair c) => c[CrosshairField.PipOpacity] > 0.05 && c[CrosshairField.Height] > 0 && c[CrosshairField.Width] > 0;
    public static bool HasDot(Crosshair c) => c[CrosshairField.DotOpacity] > 0.05 && c[CrosshairField.DotSize] > 0;
    public static bool HasRing(Crosshair c) => c[CrosshairField.DotBorder] > 0 && c[CrosshairField.DotBorderOpacity] > 0.05 && c[CrosshairField.DotBorderGap] >= 3;

    /// <summary>Pips with only their outline showing: hollow boxes.</summary>
    public static bool HasHollowPips(Crosshair c) => c[CrosshairField.PipOpacity] <= 0.05 && c[CrosshairField.PipBorder] > 0
        && c[CrosshairField.PipBorderOpacity] > 0.05 && c[CrosshairField.Height] > 0;

    public static string Shape(Crosshair c)
    {
        if (HasPips(c) || HasHollowPips(c))
        {
            if (!c.StaticGap) return "moving";
            if (HasRing(c)) return "combo";
            if (HasHollowPips(c)) return "hollow";
            if (c[CrosshairField.Height] <= c[CrosshairField.Width] * 1.2) return "squares";
            double inner = StaticBase + c[CrosshairField.Gap] - c[CrosshairField.Height] / 2;
            if (inner <= 1) return "plus";
            return HasDot(c) ? "crossdot" : "cross";
        }
        if (HasRing(c)) return "ring";
        return HasDot(c) ? "dot" : "other";
    }

    public static string ColorName(Crosshair c)
    {
        var (r, g, b) = c.Color;
        return Colors.MinBy(x => Sq(x.R - r) + Sq(x.G - g) + Sq(x.B - b)).Name;
    }

    static int Sq(int x) => x * x;
}

/// <summary>
/// The built-in catalog (crosshairs\catalog.json in the repository, fetched like the stats so new reticles arrive
/// without a new version; the copy inside the exe is the fallback) and the player's own list.
/// </summary>
public sealed class CrosshairLibrary
{
    const string CatalogUrl = "https://raw.githubusercontent.com/2peeps/DeadlockMeta/main/crosshairs/catalog.json";
    static readonly string FilePath = Path.Combine(Settings.DataDir, "crosshairs.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public List<CrosshairItem> Mine { get; set; } = [];
    public List<string> Liked { get; set; } = [];
    /// <summary>The reticle the player chose last; the game is kept on it.</summary>
    public CrosshairItem? Active { get; set; }
    /// <summary>Chosen while the game was running: written into the game's file once it closes.</summary>
    public bool Pending { get; set; }
    /// <summary>The old pre-rework reticle was already offered once.</summary>
    public bool LegacyOffered { get; set; }

    public static CrosshairLibrary Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<CrosshairLibrary>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception e) { Log.Write("crosshairs: " + e.Message); }
        return new();
    }

    public void Save()
    {
        try { Files.WriteAtomic(FilePath, JsonSerializer.Serialize(this, Json)); }
        catch (Exception e) { Log.Write("crosshairs save: " + e.Message); }
    }

    public CrosshairItem AddMine(string name, Crosshair c)
    {
        string code = c.ToLegacyCode();
        if (Mine.FirstOrDefault(m => m.Code == code) is { } same)
        {
            same.Name = name;
            Save();
            return same;
        }
        var item = new CrosshairItem { Id = "m:" + Guid.NewGuid().ToString("N")[..12], Name = name, Code = code, Added = DateTime.Now, Source = "mine" };
        Mine.Insert(0, item);
        Save();
        return item;
    }

    public static async Task<List<CrosshairItem>> LoadCatalog(bool force)
    {
        string json;
        try { json = await MetaData.Fetch(CatalogUrl, "crosshairs_catalog.json", force, TimeSpan.FromHours(12)); }
        catch (Exception e)
        {
            Log.Write("crosshair catalog: " + e.Message);
            using var s = typeof(CrosshairLibrary).Assembly.GetManifestResourceStream("catalog.json");
            if (s == null) return [];
            json = await new StreamReader(s).ReadToEndAsync();
        }
        var list = new List<CrosshairItem>();
        using var doc = JsonDocument.Parse(json);
        int order = 0;
        foreach (var e in doc.RootElement.GetProperty("items").EnumerateArray())
        {
            string code = e.GetProperty("code").GetString() ?? "";
            if (Crosshair.FromLegacyCode(code) == null) continue;
            list.Add(new CrosshairItem
            {
                Id = "c:" + e.GetProperty("id").GetString(), Name = e.GetProperty("name").GetString() ?? "", Code = code,
                Group = e.TryGetProperty("group", out var g) ? g.GetString() ?? "" : "", Source = "catalog", Order = order++,
            });
        }
        return list;
    }
}

/// <summary>
/// The small community server (server\worker.js): counts how often each reticle is applied and liked, and keeps
/// reticles players shared. Only an anonymous install id is sent. Without a configured address it is skipped.
/// </summary>
public static class CrosshairServer
{
    /// <summary>Set after the worker is deployed (see server\README.md).</summary>
    public const string Url = "";

    public static bool Enabled => Url.Length > 0;

    public sealed record Stat(long Applies, long Likes);
    public sealed record Shared(string Id, string Name, string Code, long Applies, long Likes, long Created);
    sealed record StatsReply(Dictionary<string, long[]> Items);
    sealed record SharedReply(List<Shared> Items);

    public static async Task<Dictionary<string, Stat>> Stats()
    {
        if (!Enabled) return [];
        var r = await MetaData.Http.GetFromJsonAsync<StatsReply>(Url + "/v1/stats");
        return r?.Items.Where(kv => kv.Value.Length >= 2).ToDictionary(kv => kv.Key, kv => new Stat(kv.Value[0], kv.Value[1])) ?? [];
    }

    public static async Task<List<Shared>> Community()
    {
        if (!Enabled) return [];
        var r = await MetaData.Http.GetFromJsonAsync<SharedReply>(Url + "/v1/community");
        return r?.Items ?? [];
    }

    /// <summary>"apply", "like" or "unlike". Failures are only logged: counting must never get in the way.</summary>
    public static async Task Event(string install, string id, string kind)
    {
        if (!Enabled || id.StartsWith("m:")) return;
        try
        {
            using var _ = await MetaData.Http.PostAsJsonAsync(Url + "/v1/event", new { install, id, kind });
        }
        catch (Exception e) { Log.Write("crosshair event: " + e.Message); }
    }

    /// <summary>Shares a reticle with everyone; returns its id, or null with the server's reason.</summary>
    public static async Task<(string? Id, string Error)> Submit(string install, string name, string code)
    {
        if (!Enabled) return (null, "Обмен прицелами пока не включён.");
        try
        {
            using var resp = await MetaData.Http.PostAsJsonAsync(Url + "/v1/submit", new { install, name, code });
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (resp.IsSuccessStatusCode && doc.RootElement.TryGetProperty("id", out var id)) return (id.GetString(), "");
            return (null, doc.RootElement.TryGetProperty("error", out var err) ? err.GetString() ?? "" : "Сервер не принял прицел.");
        }
        catch (Exception e)
        {
            Log.Write("crosshair submit: " + e.Message);
            return (null, "Нет связи с сервером. Попробуй позже.");
        }
    }
}
