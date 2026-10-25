using System.Windows.Threading;
using DeadlockMeta.Core;

namespace DeadlockMeta;

public enum ApplyResult { Applied, AfterGame, NoGameFiles, Failed }

/// <summary>
/// Keeps the game on the reticle the player chose. The game's settings file can only be changed while the game is
/// closed, so a choice made during a match waits for it to close; after every session the file is checked and
/// the reticle put back if the game replaced it (patches have reset it before). Lives on the UI thread.
/// </summary>
public sealed class CrosshairService
{
    static readonly TimeSpan Poll = TimeSpan.FromSeconds(4);
    // Steam syncs the cloud file right after the game exits; writing waits until that is done.
    static readonly TimeSpan AfterExit = TimeSpan.FromSeconds(10);

    readonly Settings settings;
    readonly DispatcherTimer timer;
    bool gameRunning, loading;

    public CrosshairLibrary Library { get; } = CrosshairLibrary.Load();
    public List<CrosshairItem> Catalog { get; private set; } = [];
    public List<CrosshairItem> Community { get; private set; } = [];
    public bool GameRunning => gameRunning;

    /// <summary>A message for the banner on the crosshairs tab, until the player closes it.</summary>
    public string? Notice { get; private set; }
    /// <summary>What the game had before the reticle was put back, so "keep the game's one" can undo that.</summary>
    public Crosshair? Replaced { get; private set; }

    /// <summary>Raised on the UI thread when lists, counts, the active reticle or the banner changed.</summary>
    public event Action? Changed;
    /// <summary>Raised for things worth a tray notification (the window may be hidden).</summary>
    public event Action<string>? Notify;

    /// <param name="watch">False for the screenshot and test modes: then the game's file is never written.</param>
    public CrosshairService(Settings settings, bool watch)
    {
        this.settings = settings;
        gameRunning = CrosshairGame.IsGameRunning();
        timer = new DispatcherTimer { Interval = Poll };
        if (!watch) return;
        timer.Tick += async (_, _) => await Tick();
        timer.Start();
        if (!gameRunning) CheckGame("startup");
    }

    public string? SettingsFile => CrosshairGame.SettingsFile();

    public IEnumerable<CrosshairItem> All => Catalog.Concat(Community);

    public async Task Load(bool force)
    {
        if (loading) return;
        loading = true;
        try
        {
            Catalog = await CrosshairLibrary.LoadCatalog(force);
            if (CrosshairServer.Enabled)
            {
                try
                {
                    var shared = await CrosshairServer.Community();
                    Community = shared.Select(s => new CrosshairItem
                    {
                        Id = s.Id, Name = s.Name, Code = s.Code, Source = "community", Applies = s.Applies, Likes = s.Likes,
                        Added = DateTimeOffset.FromUnixTimeSeconds(s.Created).LocalDateTime,
                    }).Where(i => Crosshair.FromLegacyCode(i.Code) != null).ToList();
                    var stats = await CrosshairServer.Stats();
                    foreach (var item in Catalog)
                        if (stats.TryGetValue(item.Id, out var st)) (item.Applies, item.Likes) = (st.Applies, st.Likes);
                }
                catch (Exception e) { Log.Write("crosshair server: " + e.Message); }
            }
        }
        catch (Exception e) { Log.Write("crosshair catalog: " + e); }
        finally { loading = false; }
        Changed?.Invoke();
    }

    async Task Tick()
    {
        bool running = CrosshairGame.IsGameRunning();
        if (running == gameRunning) return;
        gameRunning = running;
        Changed?.Invoke();
        if (running) return;
        await Task.Delay(AfterExit);
        if (!CrosshairGame.IsGameRunning()) CheckGame("game closed");
    }

    /// <summary>Writes a waiting reticle, or puts the chosen one back if the game changed it.</summary>
    void CheckGame(string when)
    {
        if (Library.Active is not { } active || SettingsFile is not { } path) return;
        try
        {
            if (Library.Pending)
            {
                Write(path, active.Value);
                Notice = $"Прицел «{active.Name}» записан в игру — он будет при следующем запуске.";
                Changed?.Invoke();
                return;
            }
            if (!settings.CrosshairGuard) return;
            var current = CrosshairGame.ReadBase(path);
            bool heroes = settings.CrosshairAllHeroes && CrosshairGame.HeroOverrides(path).Count > 0;
            if (current != null && current.SameAs(active.Value) && !heroes) return;
            Log.Write($"crosshair changed in the game ({when}), restoring {active.Name}");
            Replaced = current;
            Write(path, active.Value);
            Notice = $"Игра сменила твой прицел — вернул «{active.Name}».";
            Notify?.Invoke(Notice);
            Changed?.Invoke();
        }
        catch (Exception e) { Log.Write("crosshair check: " + e); }
    }

    void Write(string path, Crosshair c)
    {
        CrosshairGame.WriteBase(path, c, settings.CrosshairAllHeroes);
        Library.Pending = false;
        Library.Save();
        Log.Write("crosshair written: " + c.ToLegacyCode());
    }

    public ApplyResult Apply(CrosshairItem item)
    {
        Library.Active = new CrosshairItem { Id = item.Id, Name = item.Name, Code = item.Value.ToLegacyCode(), Added = DateTime.Now };
        Library.Pending = false;
        Notice = null;
        Replaced = null;
        _ = CrosshairServer.Event(settings.InstallId, item.Id, "apply");
        item.Applies++;
        ApplyResult result;
        if (SettingsFile is not { } path) result = ApplyResult.NoGameFiles;
        else if (CrosshairGame.IsGameRunning())
        {
            Library.Pending = true;
            result = ApplyResult.AfterGame;
        }
        else
        {
            try
            {
                Write(path, item.Value);
                result = ApplyResult.Applied;
            }
            catch (Exception e)
            {
                Log.Write("crosshair apply: " + e);
                result = ApplyResult.Failed;
            }
        }
        Library.Save();
        Changed?.Invoke();
        return result;
    }

    /// <summary>Stops looking after the reticle; the game keeps whatever it has.</summary>
    public void Forget()
    {
        Library.Active = null;
        Library.Pending = false;
        Library.Save();
        Notice = null;
        Changed?.Invoke();
    }

    /// <summary>Undo of an automatic restore: the game's reticle goes back and is saved to the player's list.</summary>
    public void KeepGameVersion()
    {
        if (Replaced is not { } game) return;
        var item = Library.AddMine("Прицел из игры", game);
        Replaced = null;
        Apply(item);
    }

    public void CloseNotice()
    {
        Notice = null;
        Replaced = null;
        Changed?.Invoke();
    }

    public bool IsLiked(CrosshairItem item) => Library.Liked.Contains(item.Id);

    public void ToggleLike(CrosshairItem item)
    {
        bool like = !IsLiked(item);
        if (like) Library.Liked.Insert(0, item.Id);
        else Library.Liked.Remove(item.Id);
        item.Likes = Math.Max(0, item.Likes + (like ? 1 : -1));
        Library.Save();
        _ = CrosshairServer.Event(settings.InstallId, item.Id, like ? "like" : "unlike");
        Changed?.Invoke();
    }

    public CrosshairItem SaveMine(string name, Crosshair c, CrosshairItem? replacing)
    {
        if (replacing is { Source: "mine" } && Library.Mine.Contains(replacing))
        {
            replacing.Name = name;
            replacing.Code = c.ToLegacyCode();
            var fresh = new CrosshairItem { Id = replacing.Id, Name = name, Code = replacing.Code, Added = replacing.Added, Source = "mine" };
            Library.Mine[Library.Mine.IndexOf(replacing)] = fresh;
            Library.Save();
            Changed?.Invoke();
            return fresh;
        }
        var item = Library.AddMine(name, c);
        item.Source = "mine";
        Changed?.Invoke();
        return item;
    }

    public void DeleteMine(CrosshairItem item)
    {
        Library.Mine.RemoveAll(m => m.Id == item.Id);
        Library.Save();
        Changed?.Invoke();
    }

    /// <summary>The player's list: own reticles first, then liked ones that still exist.</summary>
    public List<CrosshairItem> MyList()
    {
        foreach (var m in Library.Mine) m.Source = "mine";
        var byId = All.ToDictionary(i => i.Id);
        return [.. Library.Mine, .. Library.Liked.Select(id => byId.GetValueOrDefault(id)).OfType<CrosshairItem>()];
    }

    /// <summary>Saves the reticle the game has now; null when the settings file is missing or unreadable.</summary>
    public CrosshairItem? ImportFromGame()
    {
        try
        {
            if (SettingsFile is { } path && CrosshairGame.ReadBase(path) is { } c)
            {
                var item = Library.AddMine("Мой прицел из игры", c);
                Changed?.Invoke();
                return item;
            }
        }
        catch (Exception e) { Log.Write("crosshair import: " + e.Message); }
        return null;
    }

    /// <summary>The reticle from before the game moved its settings, offered once.</summary>
    public Crosshair? LegacyOffer() => Library.LegacyOffered ? null : CrosshairGame.ReadLegacy();

    public void LegacyDone(bool save)
    {
        if (save && CrosshairGame.ReadLegacy() is { } c) Library.AddMine("Мой старый прицел", c);
        Library.LegacyOffered = true;
        Library.Save();
        Changed?.Invoke();
    }

    public async Task<string?> Share(CrosshairItem item)
    {
        var (id, error) = await CrosshairServer.Submit(settings.InstallId, item.Name, item.Value.ToLegacyCode());
        if (id == null) return error;
        await Load(true);
        return null;
    }
}
