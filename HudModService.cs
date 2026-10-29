using System.Windows.Threading;
using DeadlockMeta.Core;

namespace DeadlockMeta;

/// <summary>
/// Keeps the game's HUD mod in step with what the player chose. The mod goes in, comes out or is rebuilt only
/// while the game is closed (the game holds its files open); it is rebuilt by itself when a patch changes the game,
/// and the addons path is put back when a patch resets gameinfo.gi. Lives on the UI thread.
/// </summary>
public sealed class HudModService
{
    static readonly TimeSpan Poll = TimeSpan.FromSeconds(5);

    readonly Settings settings;
    readonly DispatcherTimer timer;
    bool busy;
    string? failedAttempt;

    public HudMod.State State { get; private set; } = HudMod.Load();
    public string? GameDir { get; } = CrosshairGame.GameDir();
    public bool GameRunning { get; private set; }
    /// <summary>The last install or removal failed with this message; cleared by the next success.</summary>
    public string? Error { get; private set; }

    /// <summary>Raised on the UI thread when the state or a status worth showing changed.</summary>
    public event Action? Changed;
    /// <summary>Raised for things worth a tray notification.</summary>
    public event Action<string>? Notify;

    /// <param name="watch">False for the screenshot and test modes: then the game is never written.</param>
    public HudModService(Settings settings, bool watch)
    {
        this.settings = settings;
        GameRunning = CrosshairGame.IsGameRunning();
        timer = new DispatcherTimer { Interval = Poll };
        if (!watch) return;
        timer.Tick += (_, _) => Sync();
        timer.Start();
        Sync();
    }

    public bool Installed => GameDir != null && HudMod.IsActive(GameDir, State);

    /// <summary>The game files differ from what the player chose, and the game has to close first.</summary>
    public bool Waiting => GameDir != null && GameRunning && NeedsWork();

    bool NeedsWork()
    {
        if (GameDir == null) return false;
        if (!settings.HudEnabled) return State.Vpk != null;
        return !HudMod.IsActive(GameDir, State) || HudMod.IsOutdated(GameDir, State, settings.HudOptions);
    }

    /// <summary>Applies the player's choice now if the game is closed, otherwise right after it closes.</summary>
    public void Sync()
    {
        if (busy || GameDir == null) return;
        bool running = CrosshairGame.IsGameRunning();
        bool changed = running != GameRunning;
        GameRunning = running;
        // A failed attempt is not repeated every poll: only after the choice, the game or the game's open state changes.
        string attempt = $"{settings.HudEnabled}|{settings.HudOptions}|{HudMod.GameBuild(GameDir)}";
        if (changed && !running) failedAttempt = null;
        if (running || !NeedsWork() || attempt == failedAttempt)
        {
            if (changed) Changed?.Invoke();
            return;
        }
        busy = true;
        try
        {
            if (!settings.HudEnabled) HudMod.Remove(GameDir);
            else
            {
                bool patched = State.Vpk != null && State.GameBuild != HudMod.GameBuild(GameDir);
                bool onlyGameInfo = State.Vpk != null && !HudMod.IsOutdated(GameDir, State, settings.HudOptions)
                    && System.IO.File.Exists(System.IO.Path.Combine(GameDir, "game", "citadel", "addons", State.Vpk));
                if (onlyGameInfo) HudMod.RepairGameInfo(GameDir);
                else HudMod.Install(GameDir, settings.HudOptions);
                if (patched) Notify?.Invoke("Игра обновилась — мод HUD пересобран под новую версию.");
                else if (onlyGameInfo) Notify?.Invoke("Игра сбросила подключение модов — мод HUD снова включён.");
            }
            Error = null;
        }
        catch (Exception e)
        {
            Log.Write("hud mod: " + e);
            Error = e.Message;
            failedAttempt = attempt;
        }
        finally { busy = false; }
        State = HudMod.Load();
        Changed?.Invoke();
    }
}
