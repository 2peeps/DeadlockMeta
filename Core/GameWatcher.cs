using System.Diagnostics;
using System.Drawing;

namespace DeadlockMeta.Core;

/// <summary>Where to draw the panel for the hovered hero, in screen pixels; Scale is game window height / 1080.</summary>
public record struct HoverInfo(int HeroId, int PanelX, int PanelY, double Scale);

/// <summary>
/// Background loop: while Deadlock is the foreground window it looks for the hero select grid and reports
/// which card the cursor is on.
/// </summary>
sealed class GameWatcher(Settings settings)
{
    // Where the panel sits at 1080p relative to the grid's left edge: right of the screen title, above the grid.
    const double PanelRefDx = 361, PanelRefY = 36;
    const int IdleMs = 1000, LockedMs = 50;
    static readonly TimeSpan FullSearchEvery = TimeSpan.FromSeconds(3);
    static readonly TimeSpan ScanIdle = TimeSpan.FromMilliseconds(500), ScanWhileShown = TimeSpan.FromMilliseconds(200);

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
                        // The grid almost always sits where it was last time, so that spot is checked first and often.
                        if (settings.GridClientW == client.Width && settings.GridClientH == client.Height)
                        {
                            double hk = settings.GridScale > 0 ? settings.GridScale : client.Height / 1080.0;
                            var hint = GridGeom.Make(hk, 0, 0, 0) with { X0 = settings.GridX0, Y0 = settings.GridY0 };
                            if (CaptureGrid(grabber, client, hint, 2, frame) && m.Confirm(frame, hint))
                            {
                                geom = hint; locked = true; how = "remembered position";
                            }
                        }
                        if (!locked && DateTime.UtcNow >= nextFullSearch)
                        {
                            nextFullSearch = DateTime.UtcNow + FullSearchEvery;
                            if (grabber.Capture(client, 0, 0, client.Width, client.Height, frame)
                                && m.FindGrid(frame, client.Width, client.Height, out geom))
                            {
                                locked = true; how = "full search";
                                settings.GridClientW = client.Width; settings.GridClientH = client.Height;
                                settings.GridX0 = geom.X0; settings.GridY0 = geom.Y0; settings.GridScale = geom.Scale;
                                settings.Save();
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
                            hover = new HoverInfo(known[hr * Matcher.Cols + hc],
                                client.X + (int)(geom.X0 + PanelRefDx * k), client.Y + (int)(PanelRefY * k), k);
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
