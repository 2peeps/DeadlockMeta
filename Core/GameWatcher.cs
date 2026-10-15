using System.Diagnostics;
using System.Drawing;

namespace DeadlockMeta.Core;

/// <summary>
/// Where to draw the panel for the hovered hero, in screen pixels; Scale is the game UI scale (1 at 1080p).
/// </summary>
public record struct HoverInfo(int HeroId, int PanelX, int PanelY, double Scale);

/// <summary>
/// Background loop: while Deadlock is the foreground window it looks for the hero select grid and reports
/// which card the cursor is on.
/// </summary>
sealed class GameWatcher(Settings settings)
{
    // The panel sits on the line of the screen title, just right of it, so it reads as part of the header; offsets
    // are from the grid's top-left corner at 1080p. Hero select: after "ВЫБОР ГЕРОЯ". Build your roster: after
    // the longer "СОЗДАТЬ СОСТАВ", before the round party icon. (Right of the grid it covered hero art; above the
    // title it looked detached.)
    const double SelectPanelDx = 350, SelectPanelDy = -95;
    const double RosterPanelDx = 426, RosterPanelDy = -96;
    // The roster screen's grid starts lower (178 vs 154 at 1080p); this splits the two.
    const double RosterGridY = 166;
    const int IdleMs = 1000, LockedMs = 50;
    static readonly TimeSpan FullSearchEvery = TimeSpan.FromSeconds(3);
    static readonly TimeSpan ScanIdle = TimeSpan.FromMilliseconds(500), ScanWhileShown = TimeSpan.FromMilliseconds(200);

    // DEADLOCKMETA_DEBUG=1 logs every hover change (cursor position and hero).
    static readonly bool Debug = Environment.GetEnvironmentVariable("DEADLOCKMETA_DEBUG") == "1";

    volatile Matcher? matcher;
    volatile bool stop;
    Thread? thread;

    /// <summary>Raised on the worker thread; null means nothing is hovered.</summary>
    public event Action<HoverInfo?>? HoverChanged;

    public void SetMatcher(Matcher m) => matcher = m;

    public void Start()
    {
        thread = new Thread(Loop) { IsBackground = true, Name = "GameWatcher" };
        thread.Start();
    }

    public void Stop() => stop = true;

    void Loop()
    {
        using var grabber = new ScreenGrabber();
        var frame = new Frame();
        var cells = new CellMatch[Matcher.Cols * Matcher.Rows];
        var known = new int[cells.Length];
        var geom = default(GridGeom);
        var lockedClient = Rectangle.Empty;
        bool locked = false;
        int misses = 0;
        HoverInfo? shown = null;
        DateTime nextFullSearch = DateTime.MinValue, nextScan = DateTime.MinValue;

        while (!stop)
        {
            int sleep = IdleMs;
            HoverInfo? hover = null;
            try
            {
                var m = matcher;
                if (m != null && settings.OverlayEnabled && grabber.GameClient(out var client))
                {
                    if (locked && client != lockedClient) locked = false;
                    if (!locked)
                    {
                        var sw = Stopwatch.StartNew();
                        string how = "";
                        // The grid almost always sits where it was seen before, so those spots are checked first and often.
                        foreach (var spot in settings.GridSpotsFor(client.Width, client.Height))
                        {
                            double hk = spot.Scale > 0 ? spot.Scale : client.Height / 1080.0;
                            var hint = GridGeom.Make(hk, 0, 0, 0) with { X0 = spot.X0, Y0 = spot.Y0 };
                            if (CaptureGrid(grabber, client, hint, 2, frame) && m.Confirm(frame, hint))
                            {
                                geom = hint; locked = true; how = "remembered position";
                                break;
                            }
                        }
                        if (!locked && DateTime.UtcNow >= nextFullSearch)
                        {
                            nextFullSearch = DateTime.UtcNow + FullSearchEvery;
                            if (grabber.Capture(client, 0, 0, client.Width, client.Height, frame)
                                && m.FindGrid(frame, client.Width, client.Height, out geom))
                            {
                                locked = true; how = "full search";
                                settings.RememberGrid(client.Width, client.Height, geom.X0, geom.Y0, geom.Scale);
                            }
                        }
                        if (locked)
                        {
                            lockedClient = client; misses = 0; nextScan = DateTime.MinValue;
                            Array.Clear(known);
                            Log.Write($"grid found by {how} in {sw.ElapsedMilliseconds} ms: client {client.Width}x{client.Height}, " +
                                $"x0={geom.X0:F1} y0={geom.Y0:F1}");
                        }
                    }
                    if (locked)
                    {
                        sleep = LockedMs;
                        Native.GetCursorPos(out var cur);
                        bool over = geom.CellAt(cur.X - client.X, cur.Y - client.Y, out int hc, out int hr);

                        // The cursor is followed every tick, but the screen is re-read only now and then: the cards do
                        // not move, the re-read just notices new cards and the screen being closed. While a panel is
                        // up it runs more often so the panel does not linger after the screen is gone.
                        if (DateTime.UtcNow >= nextScan)
                        {
                            nextScan = DateTime.UtcNow + (shown != null ? ScanWhileShown : ScanIdle);
                            int good = 0;
                            if (CaptureGrid(grabber, client, geom, Matcher.Rows, frame))
                            {
                                m.MatchGrid(frame, geom, Matcher.Rows, 1, cells, out _);
                                for (int i = 0; i < cells.Length; i++)
                                {
                                    if (cells[i].Score < Matcher.MinScore) continue;
                                    good++;
                                    // The hovered card grows over its neighbours, so those cells are not trusted.
                                    int c = i % Matcher.Cols, r = i / Matcher.Cols;
                                    if (over && Math.Abs(c - hc) <= 1 && Math.Abs(r - hr) <= 1) continue;
                                    known[i] = cells[i].HeroId;
                                }
                            }
                            if (good >= Matcher.MinCards) misses = 0;
                            else if (++misses >= 2) { locked = false; Log.Write("grid lost"); }
                        }

                        if (locked && over && known[hr * Matcher.Cols + hc] != 0)
                        {
                            double k = geom.Scale;
                            bool roster = geom.Y0 / k > RosterGridY;
                            double px = geom.X0 + (roster ? RosterPanelDx : SelectPanelDx) * k;
                            double py = geom.Y0 + (roster ? RosterPanelDy : SelectPanelDy) * k;
                            hover = new HoverInfo(known[hr * Matcher.Cols + hc], client.X + (int)px, client.Y + (int)py, k);
                        }
                    }
                }
                else locked = false;
            }
            catch (Exception e)
            {
                Log.Write("watcher: " + e);
                sleep = 2000;
            }

            if (hover != shown)
            {
                if (Debug)
                {
                    Native.GetCursorPos(out var dc);
                    Log.Write($"hover: cursor {dc.X},{dc.Y} -> hero {(hover?.HeroId ?? 0)}");
                }
                shown = hover;
                HoverChanged?.Invoke(hover);
            }
            Thread.Sleep(sleep);
        }
    }

    static bool CaptureGrid(ScreenGrabber grabber, Rectangle client, GridGeom g, int rows, Frame frame)
    {
        int pad = (int)(g.PitchX / 2);
        return grabber.Capture(client, (int)g.X0 - pad, (int)g.Y0 - pad,
            (int)(g.PitchX * Matcher.Cols) + 2 * pad, (int)(g.PitchY * rows) + 2 * pad, frame);
    }
}
