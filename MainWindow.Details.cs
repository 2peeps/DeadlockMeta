using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using DeadlockMeta.Core;

namespace DeadlockMeta;

/// <summary>The slide-in hero panel: best community builds (one at a time, paged) and best items per game phase.</summary>
public partial class MainWindow
{
    static readonly Duration Slide = new(TimeSpan.FromMilliseconds(260));
    static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };
    static readonly Dictionary<string, (Brush Brush, string Name)> Slots = new()
    {
        ["weapon"] = (Frozen(0xB8, 0x74, 0x2A), "Оружие"),
        ["vitality"] = (Frozen(0x5C, 0x8F, 0x33), "Живучесть"),
        ["spirit"] = (Frozen(0x7B, 0x52, 0xB0), "Дух"),
    };

    static Task<ItemCatalog>? catalogTask;

    int detailHero = -1, detailRequest;
    List<BuildInfo> builds = [];
    int buildIndex;
    ItemCatalog? catalog;
    Border? buildHost;
    TextBlock? pagerText;

    static Brush Frozen(byte r, byte g, byte b)
    {
        var br = new SolidColorBrush(Color.FromRgb(r, g, b));
        br.Freeze();
        return br;
    }

    static ImageSource? LoadImage(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    Brush Res(string key) => (Brush)FindResource(key);

    void HeroRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is HeroRow row) OpenDetails(row.Id);
    }

    async void OpenDetails(int heroId)
    {
        var app = App.Current;
        if (app.Data is not { } d || !d.Heroes.TryGetValue(heroId, out var hero)) return;
        bool high = ScopeAll.IsChecked != true;
        var stats = high ? d.StatsHigh : d.StatsAll;

        detailHero = heroId;
        int request = ++detailRequest;
        DetailName.Text = hero.Name.ToUpperInvariant();
        DetailCard.ImageSource = Card(hero);
        string scope = high ? "высокие ранги" : "все ранги";
        DetailSub.Text = stats.TryGetValue(heroId, out var s)
            ? $"Винрейт {Pct(s.WinRate)} · в {s.PickRate.ToString("F0", CultureInfo.InvariantCulture)}% матчей · {scope}, {d.StatDays} дн."
            : $"{scope}, {d.StatDays} дн.";
        DetailBody.Children.Clear();
        DetailBody.Children.Add(Note("Загрузка сборок…", 14));
        DetailScroll.ScrollToTop();
        ShowDetails();

        try
        {
            catalogTask ??= ItemCatalog.Load();
            var cat = await catalogTask;
            var details = await Task.Run(() => BuildService.Load(heroId, app.Settings, high));
            await cat.FetchImages(details.Builds.SelectMany(b => b.Categories.SelectMany(c => c.ItemIds))
                .Concat(details.Phases.SelectMany(p => p.Items.Select(i => i.ItemId))));
            if (request != detailRequest) return;
            catalog = cat;
            RenderDetails(details);
        }
        catch (Exception ex)
        {
            Log.Write("hero details: " + ex);
            if (catalogTask is { IsFaulted: true }) catalogTask = null;
            if (request != detailRequest) return;
            DetailBody.Children.Clear();
            DetailBody.Children.Add(Note("Не удалось загрузить сборки. Проверь интернет и попробуй ещё раз.", 14));
        }
    }

    // Used by --shot to render the panel for a given hero.
    public void OpenHero(int heroId) => OpenDetails(heroId);
    public bool DetailsLoaded => DetailBody.Children.Count > 1;

    void ShowDetails()
    {
        if (DetailLayer.Visibility == Visibility.Visible) return;
        DetailLayer.Visibility = Visibility.Visible;
        DetailShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(DetailPanel.Width, 0, Slide) { EasingFunction = Ease });
        DetailDim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 0.55, Slide));
    }

    void CloseDetails()
    {
        if (DetailLayer.Visibility != Visibility.Visible) return;
        detailHero = -1;
        detailRequest++;
        var a = new DoubleAnimation(0, DetailPanel.Width, Slide) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        a.Completed += (_, _) => { if (detailHero < 0) DetailLayer.Visibility = Visibility.Collapsed; };
        DetailShift.BeginAnimation(TranslateTransform.XProperty, a);
        DetailDim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, Slide));
    }

    void DetailDim_Click(object sender, MouseButtonEventArgs e) => CloseDetails();

    void DetailClose_Click(object sender, RoutedEventArgs e) => CloseDetails();

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (DetailLayer.Visibility != Visibility.Visible) return;
        if (e.Key == Key.Escape) { CloseDetails(); e.Handled = true; }
        else if (e.Key == Key.Left) { ShowBuild(buildIndex - 1); e.Handled = true; }
        else if (e.Key == Key.Right) { ShowBuild(buildIndex + 1); e.Handled = true; }
    }

    void RenderDetails(HeroDetails d)
    {
        DetailBody.Children.Clear();

        // Builds: one card at a time with a pager.
        var head = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        head.Children.Add(Label("ЛУЧШИЕ СБОРКИ"));
        builds = d.Builds;
        buildIndex = 0;
        if (builds.Count > 1)
        {
            var pager = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            pager.Children.Add(PagerButton("‹", -1));
            pagerText = new TextBlock { Foreground = Res("Muted"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
            pager.Children.Add(pagerText);
            pager.Children.Add(PagerButton("›", 1));
            head.Children.Add(pager);
        }
        DetailBody.Children.Add(head);
        DetailBody.Children.Add(Note("Сборки игроков из игрового каталога. Порядок — по винрейту в реальных матчах, с поправкой на число игр.", 12.5));

        if (builds.Count == 0)
            DetailBody.Children.Add(Note("За выбранный период нет сборок с достаточным числом игр. Попробуй «Все ранги» или период подлиннее.", 13.5, 12));
        else
        {
            buildHost = new Border { Margin = new Thickness(0, 12, 0, 0), RenderTransform = new TranslateTransform() };
            DetailBody.Children.Add(buildHost);
            ShowBuild(0);
        }

        // Items per game phase.
        var itemsHead = Label("ЛУЧШИЕ ПРЕДМЕТЫ ПО ФАЗАМ ИГРЫ");
        itemsHead.Margin = new Thickness(0, 30, 0, 4);
        DetailBody.Children.Add(itemsHead);
        DetailBody.Children.Add(Note("Винрейт матчей, в которых предмет купили в это время. Наведи на иконку, чтобы увидеть название.", 12.5));
        foreach (var phase in d.Phases)
        {
            var title = Label(phase.Title);
            title.FontSize = 13;
            title.Margin = new Thickness(0, 16, 0, 8);
            DetailBody.Children.Add(title);
            var wrap = new WrapPanel();
            foreach (var it in phase.Items)
            {
                if (catalog == null || !catalog.Items.TryGetValue(it.ItemId, out var info)) continue;
                var cell = new StackPanel { Margin = new Thickness(0, 0, 12, 4) };
                cell.Children.Add(ItemTile(info, 52, $"{Pct(it.WinRate)} в {it.Matches.ToString("N0", Thousands)} играх"));
                cell.Children.Add(new TextBlock
                {
                    Text = Pct(it.WinRate), FontSize = 12.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = it.WinRate >= 50 ? Res("Good") : Res("Bad"), Margin = new Thickness(0, 4, 0, 0),
                });
                wrap.Children.Add(cell);
            }
            if (wrap.Children.Count == 0) DetailBody.Children.Add(Note("Мало данных.", 13));
            else DetailBody.Children.Add(wrap);
        }
    }

    Button PagerButton(string text, int step)
    {
        var b = new Button
        {
            Content = text, Style = (Style)FindResource("GhostButton"), FontFamily = (FontFamily)FindResource("Body"),
            FontSize = 20, Padding = new Thickness(8, 0, 8, 2),
        };
        b.Click += (_, _) => ShowBuild(buildIndex + step);
        return b;
    }

    void ShowBuild(int index)
    {
        if (buildHost == null || builds.Count == 0) return;
        index = (index % builds.Count + builds.Count) % builds.Count;
        int dir = index >= buildIndex ? 1 : -1;
        buildIndex = index;
        if (pagerText != null) pagerText.Text = $"{index + 1} / {builds.Count}";
        buildHost.Child = BuildCard(builds[index]);

        var shift = (TranslateTransform)buildHost.RenderTransform;
        var dur = new Duration(TimeSpan.FromMilliseconds(180));
        shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(24 * dir, 0, dur) { EasingFunction = Ease });
        buildHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, dur));
    }

    UIElement BuildCard(BuildInfo b)
    {
        var body = new StackPanel();

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel();
        titles.Children.Add(new TextBlock
        {
            Text = b.Name.Length > 0 ? b.Name : "Без названия", TextWrapping = TextWrapping.Wrap,
            FontFamily = (FontFamily)FindResource("Display"), FontStretch = FontStretches.Condensed, FontWeight = FontWeights.Bold,
            FontSize = 22, Foreground = Res("Text"),
        });
        titles.Children.Add(Note((b.Author.Length > 0 ? $"автор: {b.Author} · " : "") + $"ID {b.Id}", 12.5, 2));
        top.Children.Add(titles);

        var score = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 0, 0, 0) };
        score.Children.Add(new TextBlock
        {
            Text = Pct(b.WinRate), FontSize = 22, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = b.WinRate >= 50 ? Res("Good") : Res("Bad"),
        });
        score.Children.Add(Note($"в {b.Matches.ToString("N0", Thousands)} играх", 12.5, 0));
        Grid.SetColumn(score, 1);
        top.Children.Add(score);
        body.Children.Add(top);

        var copy = new Button
        {
            Content = "КОПИРОВАТЬ ID СБОРКИ", Style = (Style)FindResource("FlatButton"), HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 12, 0, 6), ToolTip = "В игре: Билды → поиск, вставь ID",
        };
        copy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(b.Id.ToString(CultureInfo.InvariantCulture));
                ShowToast($"ID {b.Id} скопирован");
            }
            catch { ShowToast("Не удалось скопировать, попробуй ещё раз"); }
        };
        body.Children.Add(copy);

        foreach (var c in b.Categories)
        {
            var name = new TextBlock { Margin = new Thickness(0, 12, 0, 6), TextWrapping = TextWrapping.Wrap };
            name.Inlines.Add(new System.Windows.Documents.Run(CategoryName(c.Name).ToUpperInvariant())
            {
                FontFamily = (FontFamily)FindResource("Display"), FontStretch = FontStretches.Condensed, FontWeight = FontWeights.SemiBold,
                FontSize = 14, Foreground = Res("Text"),
            });
            if (c.Optional)
                name.Inlines.Add(new System.Windows.Documents.Run("  по выбору") { FontSize = 12, Foreground = Res("Muted") });
            body.Children.Add(name);
            var wrap = new WrapPanel();
            foreach (int id in c.ItemIds)
                if (catalog != null && catalog.Items.TryGetValue(id, out var info))
                    wrap.Children.Add(ItemTile(info, 44, null));
            body.Children.Add(wrap);
        }

        return new Border
        {
            Background = Res("Raised"), BorderBrush = Res("Line"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(18, 14, 18, 16), Child = body,
        };
    }

    FrameworkElement ItemTile(ItemInfo info, double size, string? extra)
    {
        var slot = Slots.GetValueOrDefault(info.Slot, (Res("Line"), ""));
        var tip = new StackPanel();
        tip.Children.Add(new TextBlock { Text = info.Name, FontWeight = FontWeights.Bold, FontSize = 13 });
        tip.Children.Add(new TextBlock
        {
            Text = $"{slot.Item2}{(slot.Item2.Length > 0 ? " · " : "")}{info.Cost.ToString("N0", Thousands)} душ", FontSize = 12,
            Opacity = 0.75,
        });
        if (extra != null) tip.Children.Add(new TextBlock { Text = "Винрейт " + extra, FontSize = 12, Opacity = 0.75 });

        var img = LoadImage(info.ImagePath);
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 6, 6),
            Background = slot.Item1, ClipToBounds = true, ToolTip = tip,
            Child = img == null
                ? new TextBlock { Text = info.Name, FontSize = 9, TextWrapping = TextWrapping.Wrap, Foreground = Res("Text"), Margin = new Thickness(3) }
                : new Image { Source = img, Stretch = Stretch.UniformToFill },
        };
    }

    // Builds made from the game's template keep its section names as localisation keys, e.g. "#Citadel_HeroBuilds_Early".
    static string CategoryName(string name)
    {
        const string prefix = "#Citadel_HeroBuilds_";
        if (name.Length == 0) return "Без названия";
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return name;
        string key = name[prefix.Length..].ToLowerInvariant();
        return key switch
        {
            "early" or "earlygame" => "Ранняя игра",
            "mid" or "midgame" => "Середина игры",
            "late" or "lategame" => "Поздняя игра",
            "situational" => "По ситуации",
            _ => key.Replace('_', ' '),
        };
    }

    TextBlock Label(string text) => new() { Text = text, Style = (Style)FindResource("Label") };

    TextBlock Note(string text, double size, double top = 4) => new()
    {
        Text = text, FontSize = size, Foreground = Res("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0),
    };

    static string Pct(double v) => v.ToString("F1", CultureInfo.InvariantCulture) + "%";

    void ShowToast(string text)
    {
        ToastText.Text = text;
        var a = new DoubleAnimationUsingKeyFrames();
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1800))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2200))));
        Toast.BeginAnimation(OpacityProperty, a);
    }

    void ShowUpdate()
    {
        if (App.Current.Update is not { } u) return;
        UpdateButton.Content = $"ОБНОВИТЬ ДО {u.Version}";
        UpdateButton.Visibility = Visibility.Visible;
    }

    async void Update_Click(object sender, RoutedEventArgs e)
    {
        UpdateButton.IsEnabled = false;
        var progress = new Progress<double>(p => UpdateButton.Content = $"ЗАГРУЗКА {p * 100:F0}%");
        try { await App.Current.InstallUpdate(progress); }
        catch (Exception ex)
        {
            Log.Write("update failed: " + ex);
            ShowToast("Не удалось обновиться. Скачай новую версию со страницы GitHub.");
            UpdateButton.IsEnabled = true;
            ShowUpdate();
        }
    }
}
