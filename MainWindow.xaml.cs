using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeadlockMeta.Core;

namespace DeadlockMeta;

/// <summary>One line of the hero table.</summary>
public sealed class HeroRow
{
    public int Id { get; init; }
    public int Place { get; set; }
    public string Name { get; init; } = "";
    public ImageSource? Card { get; init; }
    public double WinRate { get; init; }
    public double PickRate { get; init; }
    public long Matches { get; init; }
    public string WinText { get; init; } = "";
    public Brush? WinBrush { get; init; }
    public double NegWidth { get; init; }
    public double PosWidth { get; init; }
    public string PickText { get; init; } = "";
    public string MatchesText { get; init; } = "";
}

public partial class MainWindow : Window
{
    // The bar is full at this many percentage points away from 50%.
    const double BarRange = 10, BarHalfWidth = 100;
    static readonly NumberFormatInfo Thousands = new() { NumberGroupSeparator = " ", NumberDecimalDigits = 0 };

    readonly Dictionary<int, ImageSource> cards = [];
    string sortKey = "win";
    bool sortDesc = true;

    public MainWindow()
    {
        InitializeComponent();
        var app = App.Current;
        Icon = app.AppIcon;
        VersionText.Text = "Версия " + typeof(App).Assembly.GetName().Version?.ToString(3);
        OverlayToggle.IsChecked = app.Settings.OverlayEnabled;
        Check(DaysPanel, app.Settings.StatDays);
        foreach (var (badge, name) in Core.Settings.Ranks)
        {
            var rb = new RadioButton
            {
                Content = name.ToUpperInvariant(), Tag = badge.ToString(CultureInfo.InvariantCulture),
                Style = (Style)FindResource("Segment"), GroupName = "rank",
            };
            rb.Click += Rank_Click;
            RankPanel.Children.Add(rb);
        }
        Check(RankPanel, app.Settings.HighBadge);
        app.DataChanged += Refresh;
        app.UpdateAvailable += ShowUpdate;
        ShowUpdate();
        StateChanged += (_, _) => Root.Margin = new Thickness(WindowState == WindowState.Maximized ? 7 : 0);
        Refresh();
    }

    static void Check(Panel panel, int value)
    {
        foreach (var rb in panel.Children.OfType<RadioButton>())
            rb.IsChecked = (string)rb.Tag == value.ToString(CultureInfo.InvariantCulture);
    }

    // Closing only hides the window: the overlay keeps working from the tray.
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    ImageSource? Card(HeroInfo h)
    {
        if (cards.TryGetValue(h.Id, out var img)) return img;
        if (!File.Exists(h.CardPath)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(h.CardPath);
            bmp.EndInit();
            bmp.Freeze();
            return cards[h.Id] = bmp;
        }
        catch { return null; }
    }

    void Refresh()
    {
        var app = App.Current;
        var d = app.Data;
        StatusText.Text = d == null ? app.Status
            : $"{app.Status}\n{d.RankMatches.ToString("N0", Thousands)} матчей за {d.StatDays} дн.";
        RefreshButton.IsEnabled = true;
        if (d == null)
        {
            EmptyText.Text = app.Status;
            EmptyText.Visibility = Visibility.Visible;
            HeroList.ItemsSource = null;
            return;
        }
        EmptyText.Visibility = Visibility.Collapsed;

        var stats = d.StatsRank;
        var good = (Brush)FindResource("Good");
        var bad = (Brush)FindResource("Bad");
        var rows = new List<HeroRow>();
        foreach (var (id, s) in stats)
        {
            if (!d.Heroes.TryGetValue(id, out var h)) continue;
            double off = Math.Clamp((s.WinRate - 50) / BarRange, -1, 1) * BarHalfWidth;
            rows.Add(new HeroRow
            {
                Id = id,
                Name = h.Name.ToUpperInvariant(),
                Card = Card(h),
                WinRate = s.WinRate, PickRate = s.PickRate, Matches = s.Matches,
                WinText = s.WinRate.ToString("F1", CultureInfo.InvariantCulture) + "%",
                WinBrush = s.WinRate >= 50 ? good : bad,
                PosWidth = Math.Max(0, off), NegWidth = Math.Max(0, -off),
                PickText = s.PickRate.ToString("F0", CultureInfo.InvariantCulture) + "%",
                MatchesText = s.Matches.ToString("N0", Thousands),
            });
        }
        IEnumerable<HeroRow> sorted = sortKey switch
        {
            "name" => rows.OrderBy(r => r.Name, StringComparer.Ordinal),
            "pick" => rows.OrderBy(r => r.PickRate),
            "matches" => rows.OrderBy(r => r.Matches),
            _ => rows.OrderBy(r => r.WinRate),
        };
        // Names read naturally A to Z, numbers biggest first; "descending" flips whichever applies.
        var list = (sortKey == "name" ? !sortDesc : sortDesc) ? sorted.Reverse().ToList() : sorted.ToList();
        for (int i = 0; i < list.Count; i++) list[i].Place = i + 1;
        HeroList.ItemsSource = list;

        foreach (var b in new[] { SortName, SortWin, SortPick, SortMatches })
        {
            string caption = ((string)b.Content).TrimEnd(' ', '▾', '▴');
            b.Content = (string)b.Tag == sortKey ? caption + (sortDesc ? " ▾" : " ▴") : caption;
        }
    }

    public void ShowSettingsTab() => TabSettings.IsChecked = true;

    void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (HeroesView == null || SettingsView == null) return;
        HeroesView.Visibility = TabHeroes.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = TabSettings.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    void Sort_Click(object sender, RoutedEventArgs e)
    {
        string key = (string)((Button)sender).Tag;
        if (key == sortKey) sortDesc = !sortDesc;
        else { sortKey = key; sortDesc = true; }
        Refresh();
    }

    async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshButton.IsEnabled = false;
        StatusText.Text = "Обновление…";
        await App.Current.Reload(true);
    }

    void OverlayToggle_Click(object sender, RoutedEventArgs e)
    {
        App.Current.Settings.OverlayEnabled = OverlayToggle.IsChecked == true;
        App.Current.Settings.Save();
    }

    async void Days_Click(object sender, RoutedEventArgs e)
    {
        App.Current.Settings.StatDays = int.Parse((string)((RadioButton)sender).Tag);
        App.Current.Settings.Save();
        await App.Current.Reload(false);
    }

    async void Rank_Click(object sender, RoutedEventArgs e)
    {
        int badge = int.Parse((string)((RadioButton)sender).Tag);
        if (badge == App.Current.Settings.HighBadge) return;
        App.Current.Settings.HighBadge = badge;
        App.Current.Settings.Save();
        StatusText.Text = "Загрузка…";
        await App.Current.Reload(false);
    }

    void OpenData_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(Settings.DataDir) { UseShellExecute = true });

    void Quit_Click(object sender, RoutedEventArgs e) => App.Current.Quit();

    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
