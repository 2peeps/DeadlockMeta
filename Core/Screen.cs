using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace DeadlockMeta.Core;

static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hwnd, ref POINT p);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);

    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x8000000;
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOSIZE = 1, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
}

/// <summary>Reads pixels off the screen. The game is never touched: this is the same thing a screenshot does.</summary>
sealed class ScreenGrabber : IDisposable
{
    // DEADLOCKMETA_PROCESS lets the tests point the watcher at a stand-in window.
    static readonly string[] GameProcesses = Environment.GetEnvironmentVariable("DEADLOCKMETA_PROCESS") is { Length: > 0 } p
        ? [p.ToLowerInvariant()] : ["deadlock", "project8"];

    Bitmap? bmp;
    uint cachedPid;
    bool cachedIsGame;

    /// <summary>Client rectangle (screen pixels) of the game window, when the game is the foreground window.</summary>
    public bool GameClient(out Rectangle client)
    {
        client = Rectangle.Empty;
        IntPtr hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid != cachedPid)
        {
            cachedPid = pid;
            bool wasGame = cachedIsGame;
            string name = "?";
            try
            {
                using var p = Process.GetProcessById((int)pid);
                name = p.ProcessName;
                cachedIsGame = Array.IndexOf(GameProcesses, name.ToLowerInvariant()) >= 0;
            }
            catch { cachedIsGame = false; }
            // Which program takes the keyboard from the game (or gives it back) is what to look at when a key such
            // as Esc seems not to reach the game.
            if (wasGame != cachedIsGame) Log.Write(cachedIsGame ? $"game is in front ({name})" : $"game lost focus to {name}");
        }
        if (!cachedIsGame) return false;
        var o = new Native.POINT();
        if (!Native.GetClientRect(hwnd, out var r) || !Native.ClientToScreen(hwnd, ref o)) return false;
        client = new Rectangle(o.X, o.Y, r.R - r.L, r.B - r.T);
        return client.Width > 300 && client.Height > 300;
    }

    /// <summary>Grabs part of the game client area into <paramref name="f"/>; cx/cy are client coordinates.</summary>
    public bool Capture(Rectangle client, int cx, int cy, int w, int h, Frame f)
    {
        cx = Math.Max(0, cx); cy = Math.Max(0, cy);
        w = Math.Min(w, client.Width - cx); h = Math.Min(h, client.Height - cy);
        if (w <= 0 || h <= 0) return false;
        if (bmp == null || bmp.Width != w || bmp.Height != h)
        {
            bmp?.Dispose();
            bmp = new Bitmap(w, h, PixelFormat.Format32bppRgb);
        }
        using (var g = Graphics.FromImage(bmp))
            g.CopyFromScreen(client.X + cx, client.Y + cy, 0, 0, new Size(w, h));
        CopyPixels(bmp, f);
        f.OriginX = cx; f.OriginY = cy;
        return true;
    }

    public static Frame Load(string path)
    {
        using var src = new Bitmap(path);
        using var b = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppRgb);
        using (var g = Graphics.FromImage(b)) g.DrawImage(src, new Rectangle(0, 0, b.Width, b.Height));
        var f = new Frame();
        CopyPixels(b, f);
        return f;
    }

    static void CopyPixels(Bitmap b, Frame f)
    {
        f.W = b.Width; f.H = b.Height;
        if (f.Px.Length != f.W * f.H * 4) f.Px = new byte[f.W * f.H * 4];
        var d = b.LockBits(new Rectangle(0, 0, f.W, f.H), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            if (d.Stride == f.W * 4) Marshal.Copy(d.Scan0, f.Px, 0, f.Px.Length);
            else
                for (int y = 0; y < f.H; y++)
                    Marshal.Copy(d.Scan0 + y * d.Stride, f.Px, y * f.W * 4, f.W * 4);
        }
        finally { b.UnlockBits(d); }
    }

    public void Dispose() => bmp?.Dispose();
}
