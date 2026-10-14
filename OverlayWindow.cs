using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using DeadlockMeta.Core;

namespace DeadlockMeta;

/// <summary>Click-through panel drawn on top of the game with the hovered hero's winrates.</summary>
sealed class OverlayWindow : Window
{
    readonly TextBlock name = new();
    readonly TextBlock[] label = new TextBlock[2], win = new TextBlock[2], pick = new TextBlock[2];
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
        name.FontSize = 22; name.Foreground = text;
        name.Margin = new Thickness(0, 0, 0, 2);

        var grid = new Grid();
        for (int c = 0; c < 3; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int r = 0; r < 2; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            label[r] = Cell(grid, r, 0, 14, FontWeights.Normal, muted);
            win[r] = Cell(grid, r, 1, 15, FontWeights.Bold, text);
            pick[r] = Cell(grid, r, 2, 13, FontWeights.Normal, muted);
            label[r].Margin = new Thickness(0, 0, 14, 0);
            win[r].Margin = new Thickness(0, 0, 14, 0);
            win[r].MinWidth = 50;
        }

        var stack = new StackPanel();
        stack.Children.Add(name);
        stack.Children.Add(grid);

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xEB, 0x0F, 0x14, 0x13)),
            BorderBrush = (Brush)res["Line"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(13, 6, 13, 8),
            Child = stack,
            LayoutTransform = scale,
        };
    }

    static TextBlock Cell(Grid g, int row, int col, double size, FontWeight weight, Brush brush)
    {
        var t = new TextBlock { FontSize = size, FontWeight = weight, Foreground = brush, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(t, row); Grid.SetColumn(t, col);
        g.Children.Add(t);
        return t;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        hwnd = new WindowInteropHelper(this).Handle;
        int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
        Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW
            | Native.WS_EX_LAYERED | Native.WS_EX_NOACTIVATE);
    }

    void Row(int r, string caption, HeroStat? s)
    {
        label[r].Text = caption;
        if (s is { } st)
        {
            win[r].Text = st.WinRate.ToString("F1", CultureInfo.InvariantCulture) + "%";
            win[r].Foreground = st.WinRate >= 50 ? good : bad;
            pick[r].Text = "в " + st.PickRate.ToString("F0", CultureInfo.InvariantCulture) + "% матчей";
        }
        else
        {
            win[r].Text = "—"; win[r].Foreground = muted; pick[r].Text = "нет данных";
        }
    }

    /// <summary>screenX/screenY are physical pixels; k is the UI scale of the game (window height / 1080).</summary>
    public void ShowHero(string heroName, HeroStat? high, HeroStat? all, int screenX, int screenY, double k)
    {
        name.Text = heroName.ToUpperInvariant();
        Row(0, "Высокие ранги", high);
        Row(1, "Все ранги", all);
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
