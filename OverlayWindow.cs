using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using DeadlockMeta.Core;

namespace DeadlockMeta;

/// <summary>
/// Click-through panel drawn on top of the game with the hovered hero's winrates. Laid out like a part of the
/// screen header: hero name over one line of winrates, with a coloured bar on the left.
/// </summary>
sealed class OverlayWindow : Window
{
    readonly TextBlock name = new();
    readonly TextBlock[] label = new TextBlock[2], win = new TextBlock[2];
    readonly Border accent = new();
    readonly ScaleTransform scale = new(1, 1);
    readonly Brush good, bad, muted, text;
    IntPtr hwnd;

    public OverlayWindow()
    {
        var res = Application.Current.Resources;
        good = (Brush)res["Good"]; bad = (Brush)res["Bad"]; muted = (Brush)res["Muted"]; text = (Brush)res["Text"];
        var display = (FontFamily)res["Display"];

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = (FontFamily)res["Body"];
        Left = -10000; Top = -10000;

        name.FontFamily = display; name.FontStretch = FontStretches.Condensed; name.FontWeight = FontWeights.Bold;
        name.FontSize = 18; name.Foreground = text;

        // One line: "Все 46.2%   Oracle+ 47.1%".
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 0) };
        for (int r = 0; r < 2; r++)
        {
            label[r] = new TextBlock { FontSize = 12.5, Foreground = muted, VerticalAlignment = VerticalAlignment.Center };
            win[r] = new TextBlock
            {
                FontSize = 14, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, r == 0 ? 12 : 0, 0),
            };
            line.Children.Add(label[r]);
            line.Children.Add(win[r]);
        }

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(name);
        stack.Children.Add(line);

        accent.Width = 3;
        accent.CornerRadius = new CornerRadius(1.5);
        accent.Margin = new Thickness(0, 1, 10, 1);

        var row = new DockPanel();
        DockPanel.SetDock(accent, Dock.Left);
        row.Children.Add(accent);
        row.Children.Add(stack);

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD9, 0x0F, 0x14, 0x13)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8, 5, 11, 6),
            Child = row,
            LayoutTransform = scale,
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        hwnd = new WindowInteropHelper(this).Handle;
        int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
        Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW
            | Native.WS_EX_LAYERED | Native.WS_EX_NOACTIVATE);
    }

    Brush Colour(HeroStat? s) => s is not { } st ? muted : st.WinRate >= 50 ? good : bad;

    void Value(int r, string caption, HeroStat? s)
    {
        label[r].Text = caption;
        win[r].Text = s is { } st ? st.WinRate.ToString("F1", CultureInfo.InvariantCulture) + "%" : "—";
        win[r].Foreground = Colour(s);
    }

    /// <summary>screenX/screenY are physical pixels; k is the UI scale of the game (1 at 1080p).</summary>
    /// <param name="rankName">Name of the rank filter shown next to "all ranks" (e.g. Oracle+).</param>
    public void ShowHero(string heroName, string rankName, HeroStat? ranked, HeroStat? all, int screenX, int screenY, double k)
    {
        name.Text = heroName.ToUpperInvariant();
        Value(0, "Все", all);
        Value(1, rankName, ranked);
        // The bar follows the higher-rank winrate: that is the number that says whether the hero is strong.
        accent.Background = Colour(ranked ?? all);
        // WPF already applies the Windows display scale, so it is divided out to keep the panel tied to the game UI.
        scale.ScaleX = scale.ScaleY = Math.Max(0.6, k) / VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (Visibility != Visibility.Visible) Show();
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, screenX, screenY, 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
    }

    public void HideHero()
    {
        if (Visibility == Visibility.Visible) Hide();
    }
}
