using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DeadlockMeta.Core;
using Drawing = System.Drawing;
using WF = System.Windows.Forms;

namespace DeadlockMeta;

public partial class App : Application
{
    const string InstanceName = "DeadlockMetaApp";

    Mutex? mutex;
    EventWaitHandle? showSignal;
    WF.NotifyIcon? tray;
    OverlayWindow? overlay;
    GameWatcher? watcher;
    MainWindow? main;
    bool loading, reloadAgain, reloadForce;

    public Settings Settings { get; private set; } = new();
    public MetaData? Data { get; private set; }
    public CrosshairService Crosshairs { get; private set; } = null!;
    public HudModService Hud { get; private set; } = null!;
    public ImageSource? AppIcon { get; private set; }

    /// <summary>Raised on the UI thread when stats were (re)loaded or loading failed.</summary>
    public event Action? DataChanged;
    public string Status { get; private set; } = "Загрузка статистики…";

    public static new App Current => (App)Application.Current;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Settings = Settings.Load();
        // Anything unexpected goes to the log, so a tester can send it instead of describing a silent crash.
        DispatcherUnhandledException += (_, a) => Log.Write("crash (ui): " + a.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, a) => Log.Write("crash: " + a.ExceptionObject);

        if (e.Args.Length >= 2 && e.Args[0] == "--test")
        {
            await TestImage(e.Args);
            Shutdown();
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0] == "--shot")
        {
            Crosshairs = new CrosshairService(Settings, watch: false);
            Hud = new HudModService(Settings, watch: false);
            int hero = e.Args.Skip(2).Select(a => a.StartsWith("hero=") && int.TryParse(a[5..], out int h) ? h : 0).DefaultIfEmpty(0).Max();
            string tab = e.Args.Contains("settings") ? "settings" : e.Args.Contains("crosshairs") ? "crosshairs" : e.Args.Contains("hud") ? "hud" : "";
            await Screenshot(e.Args[1], tab, hero, e.Args.Skip(2).FirstOrDefault(a => a.StartsWith("panel="))?[6..]);
            Shutdown();
            return;
        }

        if (e.Args.Contains("--selftest-builds"))
        {
            // rank=N overrides the rank filter for this run only (0 = all ranks).
            foreach (var a in e.Args)
                if (a.StartsWith("rank=") && int.TryParse(a[5..], out int badge)) Settings.HighBadge = badge;
            await SelfTestBuilds();
            Shutdown();
            return;
        }

        if (e.Args.Contains("--selftest-crosshairs"))
        {
            await SelfTestCrosshairs();
            Shutdown();
            return;
        }

        if (e.Args.Contains("--selftest-hud"))
        {
            SelfTestHud();
            Shutdown();
            return;
        }

        if (e.Args.Contains("--update-now"))
        {
            // Installs the latest release without the UI; used to check the update path end to end.
            if (await Updater.Check() is { } u) await Updater.Install(u);
            else Log.Write($"update-now: {Updater.Current} is up to date");
            Shutdown();
            return;
        }

        mutex = new Mutex(true, InstanceName, out bool first);
        showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceName + "Show");
        if (!first && e.Args.Contains("--updated"))
        {
            // Started by the previous version during an update: wait for it to finish exiting.
            try { first = mutex.WaitOne(TimeSpan.FromSeconds(20)); }
            catch (AbandonedMutexException) { first = true; }
        }
        if (!first)
        {
            // Already running: ask that copy to bring its window up.
            showSignal.Set();
            Shutdown();
            return;
        }
        new Thread(() =>
        {
            while (showSignal.WaitOne()) Dispatcher.BeginInvoke(ShowMain);
        }) { IsBackground = true }.Start();

        Log.Write($"started {Updater.Current}");
        Updater.CleanUp();
        CreateTray();
        // Keeps the install id that the crosshair counts use from changing between runs.
        Settings.Save();
        Crosshairs = new CrosshairService(Settings, watch: true);
        Crosshairs.Notify += msg => tray?.ShowBalloonTip(6000, "Deadlock Meta", msg, WF.ToolTipIcon.Info);
        Hud = new HudModService(Settings, watch: true);
        Hud.Notify += msg => tray?.ShowBalloonTip(6000, "Deadlock Meta", msg, WF.ToolTipIcon.Info);

        overlay = new OverlayWindow();
        new WindowInteropHelper(overlay).EnsureHandle();
        watcher = new GameWatcher(Settings);
        watcher.HoverChanged += h => Dispatcher.BeginInvoke(() => OnHover(h));
        watcher.Start();

        if (!e.Args.Contains("--tray")) ShowMain();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
        timer.Tick += async (_, _) => { await Reload(false); await Crosshairs.Load(false); await CheckUpdate(); };
        timer.Start();
        await Reload(false);
        await Crosshairs.Load(false);
        await CheckUpdate();
    }

    /// <summary>A newer release on GitHub, when there is one.</summary>
    public UpdateInfo? Update { get; private set; }

    /// <summary>Raised on the UI thread when a newer version is found.</summary>
    public event Action? UpdateAvailable;

    async Task CheckUpdate()
    {
        try
        {
            var u = await Updater.Check();
            if (u == null || u.Version == Update?.Version) return;
            Update = u;
            Log.Write($"update available: {u.Version}");
            UpdateAvailable?.Invoke();
            tray?.ShowBalloonTip(5000, "Deadlock Meta", $"Вышла версия {u.Version}. Открой программу, чтобы обновиться.", WF.ToolTipIcon.Info);
        }
        catch (Exception e) { Log.Write("update check: " + e.Message); }
    }

    /// <summary>Downloads and starts the new version, then closes this one.</summary>
    public async Task InstallUpdate(IProgress<double> progress)
    {
        if (Update == null) return;
        await Updater.Install(Update, progress);
        Quit();
    }

    void CreateTray()
    {
        // The icon embedded in the exe (assets\icon.ico) is reused for the tray and the window.
        var icon = Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? Drawing.SystemIcons.Application;
        AppIcon = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

        tray = new WF.NotifyIcon { Icon = icon, Text = "Deadlock Meta", Visible = true };
        tray.DoubleClick += (_, _) => ShowMain();
        var menu = new WF.ContextMenuStrip();
        menu.Items.Add("Открыть", null, (_, _) => ShowMain());
        menu.Items.Add("Обновить статистику", null, async (_, _) => await Reload(true));
        menu.Items.Add("Выход", null, (_, _) => Quit());
        tray.ContextMenuStrip = menu;
    }

    public void ShowMain()
    {
        main ??= new MainWindow();
        main.Show();
        if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal;
        main.Activate();
    }

    public void Quit()
    {
        watcher?.Stop();
        // Released explicitly so a freshly installed update can take over straight away.
        try { mutex?.ReleaseMutex(); } catch { }
        if (tray != null) { tray.Visible = false; tray.Dispose(); }
        Shutdown();
    }

    /// <summary>Loads heroes and stats (from the cache when fresh, unless forced) and rebuilds the card matcher.</summary>
    public async Task Reload(bool force)
    {
        if (loading)
        {
            // A settings change while a load is running must not be lost: run once more when it finishes.
            reloadAgain = true;
            reloadForce |= force;
            return;
        }
        loading = true;
        try
        {
            var sw = Stopwatch.StartNew();
            var (data, matcher) = await Task.Run(async () =>
            {
                var d = await MetaData.Load(Settings, force);
                return (d, d.BuildMatcher());
            });
            Data = data;
            // Loading parses a couple of megabytes of JSON; hand that memory back right away.
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();
            watcher?.SetMatcher(matcher);
            Status = $"Статистика от {data.Updated:dd.MM HH:mm}";
            Log.Write($"data loaded in {sw.ElapsedMilliseconds} ms: {data.Heroes.Count} heroes, {matcher.TemplateCount} cards, " +
                $"{data.StatsAll.Count} with stats");
        }
        catch (Exception e)
        {
            Log.Write("load failed: " + e);
            Status = Data == null ? "Нет связи с сервером статистики" : "Не удалось обновить, показаны прежние данные";
        }
        finally { loading = false; }
        if (reloadAgain)
        {
            bool again = reloadForce;
            reloadAgain = reloadForce = false;
            await Reload(again);
            return;
        }
        if (tray != null) tray.Text = "Deadlock Meta · " + (Status.Length > 45 ? Status[..45] : Status);
        DataChanged?.Invoke();
    }

    void OnHover(HoverInfo? h)
    {
        if (overlay == null) return;
        if (h is not { } hover || Data is not { } d || !Settings.OverlayEnabled)
        {
            overlay.HideHero();
            return;
        }
        string name = d.Heroes.TryGetValue(hover.HeroId, out var hi) ? hi.Name : "?";
        HeroStat? high = d.StatsHigh.TryGetValue(hover.HeroId, out var s1) ? s1 : null;
        HeroStat? all = d.StatsAll.TryGetValue(hover.HeroId, out var s2) ? s2 : null;
        overlay.ShowHero(name, Core.Settings.RankName(d.HighBadge), high, all, hover.PanelX, hover.PanelY, hover.Scale);
    }

    // DeadlockMeta.exe --shot out.png [settings|crosshairs] [hero=ID] [panel=item|editor|import]: renders the main window
    // off-screen into a PNG (for checking the UI).
    async Task Screenshot(string path, string tab, int heroId, string? panel)
    {
        await Reload(false);
        await Crosshairs.Load(false);
        var w = new MainWindow
        {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -5000, Top = 0,
            ShowActivated = false, ShowInTaskbar = false,
        };
        w.Show();
        if (tab == "settings") w.ShowSettingsTab();
        if (tab == "hud") w.ShowHudTab();
        if (tab == "crosshairs")
        {
            w.ShowCrosshairsTab();
            if (panel != null) w.ShowCrossPanelForShot(panel);
        }
        if (heroId > 0)
        {
            w.Height = 1500;
            w.OpenHero(heroId);
            for (int i = 0; i < 60 && !w.DetailsLoaded; i++) await Task.Delay(500);
        }
        await Task.Delay(700);
        var root = (FrameworkElement)w.Content;
        var rtb = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(w.Background, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        }
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    // DeadlockMeta.exe --selftest-builds [rank=N]: loads the builds panel data for every hero with stats and writes
    // data\selftest.txt (builds found, whether each belongs to the hero, unknown item IDs, timing).
    async Task SelfTestBuilds()
    {
        var sb = new StringBuilder();
        int failed = 0;
        try
        {
            var d = await MetaData.Load(Settings, false);
            var cat = await ItemCatalog.Load();
            sb.AppendLine($"items in catalog: {cat.Items.Count}; ranks: {Core.Settings.RankName(Settings.HighBadge)}, {Settings.StatDays} days");
            foreach (var id in d.StatsAll.Keys.OrderBy(k => d.Heroes.GetValueOrDefault(k)?.Name))
            {
                string name = d.Heroes.GetValueOrDefault(id)?.Name ?? id.ToString();
                var sw = Stopwatch.StartNew();
                try
                {
                    var det = await BuildService.Load(id, Settings);
                    var buildItems = det.Builds.SelectMany(b => b.Categories.SelectMany(c => c.ItemIds)).ToList();
                    int unknown = buildItems.Count(i => !cat.Items.ContainsKey(i));
                    var phaseShown = det.Phases.Select(p => p.Items.Count(i => cat.Items.ContainsKey(i.ItemId))).ToList();
                    bool problem = det.Builds.Count == 0 || det.Builds.Any(b => b.Categories.Count == 0) || phaseShown.Any(n => n < 3);
                    if (problem) failed++;
                    sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                        $"{(problem ? "!!" : "ok")} {name,-14} builds={det.Builds.Count} " +
                        $"[{string.Join(" | ", det.Builds.Select(b => $"{b.Id} v{b.Version} {b.WinRate:F1}% n={b.Matches}"))}] " +
                        $"items={buildItems.Count} unknown={unknown} phases={string.Join("/", phaseShown)} {sw.ElapsedMilliseconds}ms"));
                }
                catch (Exception ex)
                {
                    failed++;
                    sb.AppendLine($"!! {name,-14} FAILED: {ex.Message}");
                }
            }
        }
        catch (Exception ex) { sb.AppendLine("FAILED: " + ex); failed++; }
        sb.AppendLine($"heroes with problems: {failed}");
        File.WriteAllText(Path.Combine(Settings.DataDir, "selftest.txt"), sb.ToString());
    }

    // DeadlockMeta.exe --selftest-crosshairs: round-trips every catalog reticle through each code format and writes the
    // game's settings file on a copy (never the real one), then reports to data\crosshair_selftest.txt.
    static async Task SelfTestCrosshairs()
    {
        var sb = new StringBuilder();
        int failed = 0;
        void Check(bool ok, string what)
        {
            if (!ok) failed++;
            sb.AppendLine((ok ? "ok " : "!! ") + what);
        }
        try
        {
            var catalog = await CrosshairLibrary.LoadCatalog(false);
            Check(catalog.Count > 200, $"catalog: {catalog.Count} reticles");
            int bad = 0;
            foreach (var item in catalog)
            {
                var c = item.Value;
                foreach (var (name, text) in new[] { ("game", c.ToGameCode()), ("console", c.ToConsole()), ("legacy", c.ToLegacyCode()) })
                    if (Crosshair.Parse(text, out string err) is not { } back || !back.SameAs(c))
                    {
                        bad++;
                        sb.AppendLine($"!! {item.Id} {name}: {err} {text}");
                    }
            }
            Check(bad == 0, "every reticle survives game code, console commands and the 19 numbers");
            sb.AppendLine("sample game code: " + catalog[1].Value.ToGameCode());
            sb.AppendLine("shapes: " + string.Join(", ", catalog.GroupBy(i => i.Shape).Select(g => $"{g.Key}={g.Count()}")));
            Check(Crosshair.Parse("hello", out _) == null && Crosshair.Parse("DL.AAAA", out _) == null, "rubbish is refused");

            if (CrosshairGame.SettingsFile() is { } real)
            {
                sb.AppendLine("settings file: " + real);
                string copy = Path.Combine(Core.Settings.DataDir, "selftest_hero_settings.lst");
                File.Copy(real, copy, true);
                string before = File.ReadAllText(real);
                var original = CrosshairGame.ReadBase(copy);
                sb.AppendLine("game base now: " + original?.ToLegacyCode());
                sb.AppendLine("heroes with own reticle: " + string.Join(", ", CrosshairGame.HeroOverrides(copy)));
                var pick = catalog.First(i => i.Id == "c:cdot-green").Value;
                CrosshairGame.WriteBase(copy, pick, allHeroes: false);
                Check(CrosshairGame.ReadBase(copy)?.SameAs(pick) == true, "written base reads back");
                Check(CrosshairGame.HeroOverrides(copy).Count == CrosshairGame.HeroOverrides(real).Count, "hero reticles kept when asked");
                var otherLines = File.ReadAllLines(copy).Where(l => !l.Contains("crosshair_")).ToList();
                Check(otherLines.SequenceEqual(File.ReadAllLines(real).Where(l => !l.Contains("crosshair_"))), "nothing but reticle lines changed");
                CrosshairGame.WriteBase(copy, pick, allHeroes: true);
                Check(CrosshairGame.HeroOverrides(copy).Count == 0, "hero reticles removed for one-for-all");
                Check(CrosshairGame.ReadBase(copy)?.SameAs(pick) == true, "base still right after second write");
                Check(File.ReadAllText(real) == before, "the real settings file was not touched");
                File.Delete(copy);
            }
            else sb.AppendLine("-- no game settings file on this computer");
            sb.AppendLine("old pre-rework reticle: " + (CrosshairGame.ReadLegacy()?.ToLegacyCode() ?? "none"));
        }
        catch (Exception ex) { sb.AppendLine("FAILED: " + ex); failed++; }
        sb.AppendLine($"problems: {failed}");
        File.WriteAllText(Path.Combine(Core.Settings.DataDir, "crosshair_selftest.txt"), sb.ToString());
    }

    // DeadlockMeta.exe --selftest-hud: builds the HUD mod from the installed game without installing it, reads the
    // pack and its files back, and patches a copy of gameinfo.gi both ways; reports to data\hud_selftest.txt and
    // leaves the pack and the decoded files in data\hud_selftest\ to look at.
    static void SelfTestHud()
    {
        var sb = new StringBuilder();
        int failed = 0;
        void Check(bool ok, string what)
        {
            if (!ok) failed++;
            sb.AppendLine((ok ? "ok " : "!! ") + what);
        }
        try
        {
            if (CrosshairGame.GameDir() is not { } game) throw new DirectoryNotFoundException("game not found");
            sb.AppendLine($"game: {game}, build {HudMod.GameBuild(game)}");
            string dir = Path.Combine(Core.Settings.DataDir, "hud_selftest");
            Directory.CreateDirectory(dir);
            var options = new HudOptions(true, true, true, true, 75, 115);
            var files = HudMod.BuildFiles(game, options);
            byte[] vpk = Core.Vpk.Build(files);
            File.WriteAllBytes(Path.Combine(dir, "pak_selftest_dir.vpk"), vpk);
            Check(vpk.Length > 0, $"pack built: {vpk.Length} bytes, {files.Count} files");

            using var pak = new SteamDatabase.ValvePak.Package();
            pak.Read(Path.Combine(dir, "pak_selftest_dir.vpk"));
            try
            {
                pak.VerifyHashes();
                Check(true, "pack checksums verify");
            }
            catch (Exception ex) { Check(false, "pack checksums: " + ex.Message); }
            foreach (var (path, bytes) in files)
            {
                var entry = pak.FindEntry(path);
                byte[]? back = null;
                if (entry != null) pak.ReadEntry(entry, out back);
                Check(back != null && back.AsSpan().SequenceEqual(bytes), "pack returns " + path);
                using var res = new ValveResourceFormat.Resource();
                res.Read(new MemoryStream(bytes));
                string text = res.DataBlock is ValveResourceFormat.ResourceTypes.PanoramaLayout layout
                    ? layout.ToString()!
                    : Encoding.UTF8.GetString(((ValveResourceFormat.ResourceTypes.Panorama)res.DataBlock!).Data);
                File.WriteAllText(Path.Combine(dir, Path.GetFileName(path) + ".txt"), text);
                sb.AppendLine($"   {path}: resource v{res.Version}, blocks {string.Join(",", res.Blocks.Select(b => b.Type))}");
                if (path.EndsWith(".vxml_c"))
                {
                    Check(text.Contains("s2r://panorama/scripts/dlmeta_hud.vts"), "layout includes the mod's script");
                    Check(text.Contains("<CitadelHud id=\"Hud\""), "layout still has the game's HUD");
                }
                else Check(text == HudMod.ScriptSource(options), "script text reads back unchanged");
            }

            string gameinfo = Path.Combine(game, "game", "citadel", "gameinfo.gi");
            string original = File.ReadAllText(gameinfo);
            sb.AppendLine("game's gameinfo.gi has addons path: " + original.Contains("citadel/addons"));
            Check(true, "(gameinfo patching is checked on a copy below)");
            string copyGame = Path.Combine(dir, "fakegame");
            Directory.CreateDirectory(Path.Combine(copyGame, "game", "citadel"));
            File.WriteAllText(Path.Combine(copyGame, "game", "citadel", "gameinfo.gi"), original);
            File.Copy(Path.Combine(game, "game", "citadel", "steam.inf"), Path.Combine(copyGame, "game", "citadel", "steam.inf"), true);
            HudMod.RepairGameInfo(copyGame);
            string patched = File.ReadAllText(Path.Combine(copyGame, "game", "citadel", "gameinfo.gi"));
            int at = patched.IndexOf("citadel/addons", StringComparison.Ordinal);
            Check(at > 0 && at < patched.IndexOf("Game\t\t\t\tcore", StringComparison.Ordinal), "copy of gameinfo.gi gets the addons path before the game's files");
            sb.AppendLine(string.Join(Environment.NewLine, patched.Split('\n').Where(l => l.Contains("citadel") && !l.Contains("//")).Select(l => "   | " + l.TrimEnd())));
            Check(!HudMod.RepairGameInfo(copyGame), "patching twice changes nothing");
            Check(File.ReadAllText(gameinfo) == original, "the game's own gameinfo.gi was not touched");
        }
        catch (Exception ex) { sb.AppendLine("FAILED: " + ex); failed++; }
        sb.AppendLine($"problems: {failed}");
        File.WriteAllText(Path.Combine(Core.Settings.DataDir, "hud_selftest.txt"), sb.ToString());
    }

    // DeadlockMeta.exe --test screenshot.png [clientHeight]: writes what the matcher sees on a saved screenshot.
    async Task TestImage(string[] args)
    {
        var sb = new StringBuilder();
        try
        {
            var d = await MetaData.Load(Settings, false);
            var m = d.BuildMatcher();
            var f = ScreenGrabber.Load(args[1]);
            int h = args.Length > 2 ? int.Parse(args[2]) : f.H;
            var sw = Stopwatch.StartNew();
            bool ok = m.FindGrid(f, f.W, h, out var g);
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"templates={m.TemplateCount} found={ok} in {sw.ElapsedMilliseconds} ms  x0={g.X0:F1} y0={g.Y0:F1}"));
            // Cells are printed even when the grid was not accepted, to see why.
            if (g.PitchX > 0)
            {
                var cells = new CellMatch[Matcher.Cols * Matcher.Rows];
                int good = m.MatchGrid(f, g, Matcher.Rows, 1, cells, out _, nudge: true);
                sb.AppendLine($"good cells={good}");
                for (int r = 0; r < Matcher.Rows; r++)
                {
                    for (int c = 0; c < Matcher.Cols; c++)
                    {
                        var cm = cells[r * Matcher.Cols + c];
                        string nm = cm.Score >= Matcher.MinScore && d.Heroes.TryGetValue(cm.HeroId, out var hi) ? hi.Name : "-";
                        sb.Append(string.Create(CultureInfo.InvariantCulture, $"{nm,-14}{cm.Score:F2} c{cm.Chroma,-3:F0}| "));
                    }
                    sb.AppendLine();
                }
            }
        }
        catch (Exception e) { sb.AppendLine(e.ToString()); }
        File.WriteAllText(Path.Combine(Settings.DataDir, "test_result.txt"), sb.ToString());
    }
}
