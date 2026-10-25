using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DeadlockMeta.Core;

namespace DeadlockMeta;

/// <summary>Draws a reticle at the size the game draws it on this screen (or magnified), over one of a few backgrounds.</summary>
public sealed class CrosshairPreview : FrameworkElement
{
    public static readonly (string Name, Brush Brush)[] Backgrounds =
    [
        ("Тёмный", Solid(0x20, 0x28, 0x2A)),
        ("Светлый", Solid(0xC9, 0xCD, 0xC4)),
        ("Небо", Gradient(Color.FromRgb(0x5B, 0x8A, 0xBD), Color.FromRgb(0xD3, 0xE0, 0xE6))),
        ("Трава", Gradient(Color.FromRgb(0x8E, 0xA5, 0x5A), Color.FromRgb(0x35, 0x50, 0x28))),
        ("Песок", Gradient(Color.FromRgb(0xDC, 0xC7, 0x9A), Color.FromRgb(0x9E, 0x7F, 0x52))),
    ];

    Crosshair? value;
    int background;

    public Crosshair? Value { get => value; set { this.value = value; InvalidateVisual(); } }
    public int BackgroundIndex { get => background; set { background = value; InvalidateVisual(); } }
    double magnify = 1;

    /// <summary>1 draws the reticle at the size the game does on this screen; more is a magnifier.</summary>
    public double Magnify { get => magnify; set { magnify = value; InvalidateVisual(); } }

    // The game's numbers are pixels at 1080p and scale with its resolution.
    static readonly Lazy<double> GameScale = new(() => (CrosshairGame.GameHeight() ?? (int)SystemParameters.PrimaryScreenHeight) / 1080.0);

    public CrosshairPreview()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
    }

    static Brush Solid(byte r, byte g, byte b)
    {
        var br = new SolidColorBrush(Color.FromRgb(r, g, b));
        br.Freeze();
        return br;
    }

    static Brush Gradient(Color top, Color bottom)
    {
        var br = new LinearGradientBrush(top, bottom, 90);
        br.Freeze();
        return br;
    }

    static Color Rgba((byte R, byte G, byte B) c, double alpha) =>
        Color.FromArgb((byte)Math.Clamp(Math.Round(alpha * 255), 0, 255), c.R, c.G, c.B);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Backgrounds[Math.Clamp(background, 0, Backgrounds.Length - 1)].Brush, null, new Rect(0, 0, w, h));
        if (value is not { } c) return;
        // One reticle unit in device pixels is GameScale * magnify; WPF draws in DIPs, so divide by the DPI scale.
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double z = magnify * GameScale.Value / dpi;
        // Edges land on whole device pixels, otherwise a 1 px line is smeared over two at true size.
        double Snap(double v) => Math.Round(v * dpi) / dpi;
        double minPx = 1 / dpi;
        double cx = Snap(w / 2), cy = Snap(h / 2);

        // Pips: centred at the gap distance, "height" long and "width" thick, each with its own outline box.
        double gap = c[CrosshairField.Gap];
        double dist = c.StaticGap ? CrosshairStyle.StaticBase + gap : CrosshairStyle.MovingBase + gap * 2;
        double len = c[CrosshairField.Height], thick = c[CrosshairField.Width];
        double pb = c[CrosshairField.PipBorder], pbGap = c[CrosshairField.PipBorderGap];
        var fill = new SolidColorBrush(Rgba(c.Color, c[CrosshairField.PipOpacity]));
        var outlineFill = pb > 0 && c[CrosshairField.PipBorderOpacity] > 0
            ? new SolidColorBrush(Rgba(c.Outline, c[CrosshairField.PipBorderOpacity])) : null;
        if (len > 0 && thick > 0)
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                double px = cx + dx * dist * z, py = cy + dy * dist * z;
                double rw = (dx != 0 ? len : thick) * z, rh = (dx != 0 ? thick : len) * z;
                Rect Box(double grow)
                {
                    double x0 = Snap(px - rw / 2 - grow), y0 = Snap(py - rh / 2 - grow);
                    return new Rect(x0, y0, Math.Max(minPx, Snap(px + rw / 2 + grow) - x0), Math.Max(minPx, Snap(py + rh / 2 + grow) - y0));
                }
                var r = Box(0);
                if (outlineFill != null)
                {
                    // The outline is a frame pb thick, pbGap / 2 away from the pip, drawn as a filled ring.
                    var frame = new GeometryGroup { FillRule = FillRule.EvenOdd };
                    frame.Children.Add(new RectangleGeometry(Box((pbGap / 2 + pb) * z)));
                    frame.Children.Add(new RectangleGeometry(Box(pbGap / 2 * z)));
                    dc.DrawGeometry(outlineFill, null, frame);
                }
                dc.DrawRectangle(fill, null, r);
            }

        double dot = c[CrosshairField.DotSize];
        double db = c[CrosshairField.DotBorder];
        if (db > 0 && c[CrosshairField.DotBorderOpacity] > 0)
        {
            double radius = (dot / 2 + c[CrosshairField.DotBorderGap] / 2 + db / 2) * z;
            dc.DrawEllipse(null, new Pen(new SolidColorBrush(Rgba(c.Outline, c[CrosshairField.DotBorderOpacity])), db * z),
                new Point(cx, cy), radius, radius);
        }
        if (dot > 0 && c[CrosshairField.DotOpacity] > 0)
            dc.DrawEllipse(new SolidColorBrush(Rgba(c.Color, c[CrosshairField.DotOpacity])), null, new Point(cx, cy), dot / 2 * z, dot / 2 * z);
    }
}

/// <summary>The crosshairs tab: catalog and own list as cards, plus the panel for one reticle, the editor and pasting.</summary>
public partial class MainWindow
{
    const string HowToPaste = "Как вставить код в игре: Настройки → «Поделиться настройками» → «Применить код» → «Вставить из буфера обмена» → отметь «Прицел» → «Применить».";

    bool crossReady;
    string crossMode = "all", crossShape = "", crossColor = "";
    int crossBg;
    CrosshairPreview? panelPreview;
    // The editor and the paste panel stay open when a reticle is applied from them; the reticle panel is redrawn.
    bool crossEditing;

    static CrosshairService Cross => App.Current.Crosshairs;

    void InitCrosshairs()
    {
        CrossGuardToggle.IsChecked = App.Current.Settings.CrosshairGuard;
        CrossHeroesToggle.IsChecked = App.Current.Settings.CrosshairAllHeroes;
        Cross.Changed += () => { if (crossReady) RenderCrosshairs(); };
    }

    // Used by --shot to render a panel.
    public void ShowCrossPanelForShot(string panel)
    {
        switch (panel)
        {
            case "editor": ShowEditor(Crosshair.Default, null, "Мой прицел"); break;
            case "import": CrossImport_Click(this, new RoutedEventArgs()); break;
            default:
                if (Cross.All.Skip(20).FirstOrDefault() is { } item) ShowCrosshair(item);
                break;
        }
    }

    void OnCrosshairsShown()
    {
        if (!crossReady)
        {
            crossReady = true;
            BuildCrossFilters();
            if (Cross.Catalog.Count == 0) _ = Cross.Load(false);
        }
        RenderCrosshairs();
    }

    void BuildCrossFilters()
    {
        CrossShapePanel.Children.Clear();
        foreach (var (key, name) in CrosshairStyle.Shapes)
        {
            var rb = new RadioButton
            {
                Content = name.ToUpperInvariant(), Style = (Style)FindResource("Segment"), GroupName = "crossShape",
                IsChecked = key == crossShape, Margin = new Thickness(0, 0, 6, 6),
            };
            rb.Click += (_, _) => { crossShape = key; RenderCrossGrid(); };
            CrossShapePanel.Children.Add(rb);
        }

        CrossColorPanel.Children.Clear();
        CrossColorPanel.Children.Add(ColorChip("", "Все цвета", null));
        foreach (var (name, r, g, b) in CrosshairStyle.Colors)
            CrossColorPanel.Children.Add(ColorChip(name, name, Color.FromRgb(r, g, b)));

        CrossBgPanel.Children.Clear();
        for (int i = 0; i < CrosshairPreview.Backgrounds.Length; i++)
        {
            int index = i;
            var chip = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Margin = new Thickness(4, 0, 0, 0), Cursor = Cursors.Hand,
                Background = CrosshairPreview.Backgrounds[i].Brush, ToolTip = CrosshairPreview.Backgrounds[i].Name,
                BorderThickness = new Thickness(2), BorderBrush = i == crossBg ? Res("Accent") : Res("Line"),
            };
            chip.MouseLeftButtonUp += (_, _) =>
            {
                crossBg = index;
                foreach (var c in CrossBgPanel.Children.OfType<Border>()) c.BorderBrush = Res("Line");
                chip.BorderBrush = Res("Accent");
                foreach (var p in FindPreviews(CrossGrid)) p.BackgroundIndex = index;
                if (panelPreview != null) panelPreview.BackgroundIndex = index;
            };
            CrossBgPanel.Children.Add(chip);
        }
    }

    static IEnumerable<CrosshairPreview> FindPreviews(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is CrosshairPreview p) yield return p;
            else foreach (var x in FindPreviews(child)) yield return x;
        }
    }

    FrameworkElement ColorChip(string key, string tip, Color? color)
    {
        var chip = new Border
        {
            Height = 26, MinWidth = 26, CornerRadius = new CornerRadius(13), Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand,
            ToolTip = tip, BorderThickness = new Thickness(2), Tag = key,
            BorderBrush = key == crossColor ? Res("Text") : Res("Line"),
        };
        if (color is { } c)
        {
            chip.Width = 26;
            chip.Child = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(c) };
        }
        else
        {
            chip.Padding = new Thickness(10, 0, 10, 0);
            chip.Child = new TextBlock
            {
                Text = "ВСЕ ЦВЕТА", FontFamily = (FontFamily)FindResource("Display"), FontStretch = FontStretches.Condensed,
                FontWeight = FontWeights.SemiBold, FontSize = 13, Foreground = Res("Muted"), VerticalAlignment = VerticalAlignment.Center,
            };
        }
        chip.MouseLeftButtonUp += (_, _) =>
        {
            crossColor = key;
            foreach (var x in CrossColorPanel.Children.OfType<Border>())
                x.BorderBrush = (string)x.Tag == key ? Res("Text") : Res("Line");
            RenderCrossGrid();
        };
        return chip;
    }

    void RenderCrosshairs()
    {
        RenderCrossModes();
        RenderCrossBanner();
        RenderCrossGrid();
    }

    void RenderCrossModes()
    {
        var modes = new List<(string Key, string Name)> { ("all", $"ВСЕ ПРИЦЕЛЫ ({Cross.All.Count()})") };
        if (CrosshairServer.Enabled) modes.Add(("community", $"ОТ ИГРОКОВ ({Cross.Community.Count})"));
        modes.Add(("mine", $"МОИ ({Cross.MyList().Count})"));
        CrossModePanel.Children.Clear();
        foreach (var (key, name) in modes)
        {
            var rb = new RadioButton { Content = name, Style = (Style)FindResource("Segment"), GroupName = "crossMode", IsChecked = key == crossMode };
            rb.Click += (_, _) => { crossMode = key; RenderCrossGrid(); CrossScrollGrid.ScrollToTop(); };
            CrossModePanel.Children.Add(rb);
        }
    }

    List<CrosshairItem> CrossItems()
    {
        IEnumerable<CrosshairItem> items = crossMode switch
        {
            "mine" => Cross.MyList(),
            "community" => Cross.Community.OrderByDescending(i => i.Added),
            // Most applied first once the server counts them; otherwise the catalog's own order.
            _ => Cross.All.OrderByDescending(i => i.Applies).ThenBy(i => i.Source == "catalog" ? 0 : 1).ThenBy(i => i.Order),
        };
        if (crossShape.Length > 0) items = items.Where(i => i.Shape == crossShape);
        if (crossColor.Length > 0) items = items.Where(i => i.ColorName == crossColor);
        return items.ToList();
    }

    void RenderCrossGrid()
    {
        var items = CrossItems();
        CrossGrid.Children.Clear();
        if (crossMode == "mine") CrossGrid.Children.Add(TakeFromGameCard());
        foreach (var item in items) CrossGrid.Children.Add(CrossCard(item));

        string? empty = null;
        if (Cross.Catalog.Count == 0 && crossMode == "all") empty = "Загрузка прицелов…";
        else if (items.Count == 0 && crossMode == "mine" && crossShape.Length == 0 && crossColor.Length == 0)
            empty = null;
        else if (items.Count == 0) empty = "Таких прицелов нет. Выбери другую форму или цвет.";
        CrossEmpty.Text = empty ?? "";
        CrossEmpty.Visibility = empty != null ? Visibility.Visible : Visibility.Collapsed;
    }

    bool IsApplied(CrosshairItem item) => Cross.Library.Active is { } a && (a.Id == item.Id || a.Code == item.Value.ToLegacyCode());

    FrameworkElement CrossCard(CrosshairItem item)
    {
        bool active = IsApplied(item);
        var preview = new CrosshairPreview { Value = item.Value, Height = 104, BackgroundIndex = crossBg };
        var top = new Grid();
        top.Children.Add(new Border { CornerRadius = new CornerRadius(3, 3, 0, 0), ClipToBounds = true, Child = preview });
        if (active)
            top.Children.Add(new Border
            {
                Background = Res("Accent"), CornerRadius = new CornerRadius(2), Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(8),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = "СТОИТ В ИГРЕ", FontFamily = (FontFamily)FindResource("Display"), FontStretch = FontStretches.Condensed,
                    FontWeight = FontWeights.Bold, FontSize = 12, Foreground = Res("Bg"),
                },
            });

        var body = new StackPanel { Margin = new Thickness(10, 8, 10, 10) };
        body.Children.Add(new TextBlock
        {
            // Two lines, always: names fit and the cards in a row stay the same height.
            Text = item.Name, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Res("Text"), LineHeight = 17,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Height = 34,
            TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = item.Name,
        });

        var meta = new Grid { Margin = new Thickness(0, 2, 0, 8) };
        meta.ColumnDefinitions.Add(new ColumnDefinition());
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        meta.Children.Add(new TextBlock
        {
            Text = CardMeta(item), FontSize = 12, Foreground = Res("Muted"), VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var like = LikeButton(item, small: true);
        Grid.SetColumn(like, 1);
        meta.Children.Add(like);
        body.Children.Add(meta);

        var buttons = new Grid();
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var apply = new Button { Content = active ? "ПОСТАВЛЕН" : "ПОСТАВИТЬ", Style = (Style)FindResource("AccentButton"), Padding = new Thickness(8, 5, 8, 6), IsEnabled = !active };
        apply.Click += (_, _) => ApplyCrosshair(item);
        buttons.Children.Add(apply);
        var copy = new Button
        {
            Content = "КОД", Style = (Style)FindResource("FlatButton"), Margin = new Thickness(6, 0, 0, 0),
            ToolTip = "Скопировать код, чтобы вставить его в игре или отправить другу",
        };
        copy.Click += (_, _) => CopyGameCode(item.Value);
        Grid.SetColumn(copy, 1);
        buttons.Children.Add(copy);
        body.Children.Add(buttons);

        var stack = new StackPanel();
        stack.Children.Add(top);
        stack.Children.Add(body);
        var card = new Border
        {
            Width = 182, Margin = new Thickness(0, 0, 10, 10), Background = Res("Panel"), CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1), BorderBrush = active ? Res("Accent") : Res("Line"), Cursor = Cursors.Hand, Child = stack,
        };
        card.MouseEnter += (_, _) => { if (!active) card.BorderBrush = Res("Muted"); };
        card.MouseLeave += (_, _) => { if (!active) card.BorderBrush = Res("Line"); };
        card.MouseLeftButtonUp += (_, _) => ShowCrosshair(item);
        return card;
    }

    static string ShapeName(CrosshairItem item) =>
        CrosshairStyle.Shapes.FirstOrDefault(s => s.Key == item.Shape).Name is { Length: > 0 } n && item.Shape.Length > 0 ? n : "Свой";

    static string CardMeta(CrosshairItem item)
    {
        string where = item.Source switch { "mine" => "мой", "community" => "от игрока", _ => ShapeName(item).ToLowerInvariant() };
        return item.Applies > 0 ? $"{where} · {Installs(item.Applies)}" : where;
    }

    static string Installs(long n)
    {
        long m10 = n % 10, m100 = n % 100;
        string word = m10 == 1 && m100 != 11 ? "установка" : m10 is >= 2 and <= 4 && m100 is < 12 or > 14 ? "установки" : "установок";
        return $"{n.ToString("N0", Thousands)} {word}";
    }

    Button LikeButton(CrosshairItem item, bool small)
    {
        bool liked = Cross.IsLiked(item);
        string count = item.Likes > 0 ? " " + item.Likes.ToString("N0", Thousands) : "";
        var b = new Button
        {
            Content = small ? "♥" + count : liked ? "♥ УБРАТЬ ИЗ МОИХ" : "♥ В МОИ",
            Style = (Style)FindResource(small ? "GhostButton" : "FlatButton"), FontFamily = (FontFamily)FindResource(small ? "Body" : "Display"),
            FontSize = small ? 13 : 14, Padding = small ? new Thickness(4, 0, 0, 0) : new Thickness(0),
            ToolTip = liked ? "Убрать из «Мои»" : "Добавить в «Мои»",
        };
        if (liked) b.Foreground = Res("Bad");
        if (item.Source == "mine") b.Visibility = Visibility.Collapsed;
        b.Click += (_, _) =>
        {
            Cross.ToggleLike(item);
            ShowToast(Cross.IsLiked(item) ? "Добавлен в «Мои»" : "Убран из «Мои»");
            if (CrossLayer.Visibility == Visibility.Visible && !small) ShowCrosshair(item);
        };
        return b;
    }

    FrameworkElement TakeFromGameCard()
    {
        var body = new StackPanel { Margin = new Thickness(14), VerticalAlignment = VerticalAlignment.Center };
        body.Children.Add(new TextBlock
        {
            Text = "+", FontSize = 34, Foreground = Res("Accent"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4),
        });
        body.Children.Add(new TextBlock
        {
            Text = "Сохранить прицел, который сейчас стоит в игре", FontSize = 13.5, FontWeight = FontWeights.SemiBold,
            Foreground = Res("Text"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
        });
        var card = new Border
        {
            Width = 182, MinHeight = 214, Margin = new Thickness(0, 0, 10, 10), CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand,
            BorderThickness = new Thickness(1), BorderBrush = Res("Line"), Background = Brushes.Transparent, Child = body,
        };
        card.MouseEnter += (_, _) => card.BorderBrush = Res("Accent");
        card.MouseLeave += (_, _) => card.BorderBrush = Res("Line");
        card.MouseLeftButtonUp += (_, _) =>
        {
            if (Cross.ImportFromGame() is { } item)
            {
                ShowToast("Сохранён в «Мои»");
                ShowCrosshair(item);
            }
            else ShowToast("Не нашёл настройки игры. Запусти Deadlock хотя бы раз.", 3.5);
        };
        return card;
    }

    // ---- Banner: what the game has and what happens next ----

    void RenderCrossBanner()
    {
        var lib = Cross.Library;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(buttons, 2);

        Crosshair? shown = lib.Active?.Value;
        string text;
        Brush tone = Res("Muted");
        if (Cross.Notice is { } notice)
        {
            text = notice;
            tone = Res("Accent");
            if (Cross.Replaced != null)
                buttons.Children.Add(BannerButton("ОСТАВИТЬ КАК В ИГРЕ", false, () =>
                {
                    Cross.KeepGameVersion();
                    ShowToast("Оставил прицел из игры и сохранил его в «Мои»", 3);
                }));
            buttons.Children.Add(BannerButton("ПОНЯТНО", true, Cross.CloseNotice));
        }
        else if (Cross.LegacyOffer() is { } legacy)
        {
            shown = legacy;
            text = "Нашёл твой старый прицел — тот, что был до того, как игра поменяла настройки. Сохранить его в «Мои»?";
            tone = Res("Accent");
            buttons.Children.Add(BannerButton("СОХРАНИТЬ", true, () => { Cross.LegacyDone(true); ShowToast("Старый прицел сохранён в «Мои»"); }));
            buttons.Children.Add(BannerButton("НЕ НАДО", false, () => Cross.LegacyDone(false)));
        }
        else if (lib.Active is { } a && lib.Pending)
        {
            text = $"Игра запущена, поэтому «{a.Name}» встанет сам, когда ты её закроешь. Нужен прямо сейчас — скопируй код и вставь в игре.";
            buttons.Children.Add(BannerButton("КОПИРОВАТЬ КОД", true, () => CopyGameCode(a.Value)));
        }
        else if (Cross.SettingsFile == null)
        {
            text = "Не нашёл настройки Deadlock. Запусти игру хотя бы раз — а пока можно копировать код и вставлять его в игре.";
            tone = Res("Bad");
        }
        else if (lib.Active is { } act)
        {
            text = $"Сейчас в игре: «{act.Name}»." + (App.Current.Settings.CrosshairGuard ? " Если игра его сбросит, программа вернёт его сама." : "");
            buttons.Children.Add(BannerButton("ПОДРОБНЕЕ", false, () => ShowCrosshair(Cross.All.FirstOrDefault(i => i.Id == act.Id)
                ?? Cross.MyList().FirstOrDefault(i => i.Id == act.Id) ?? act)));
        }
        else text = "Выбери прицел и нажми «Поставить» — он сам появится в игре. Свой можно сделать кнопкой «Создать свой».";

        if (shown != null)
            grid.Children.Add(new Border
            {
                Width = 56, Height = 40, CornerRadius = new CornerRadius(3), ClipToBounds = true, Margin = new Thickness(0, 0, 12, 0),
                Child = new CrosshairPreview { Value = shown, BackgroundIndex = 0 },
            });
        var tb = new TextBlock { Text = text, FontSize = 13.5, Foreground = tone == Res("Muted") ? Res("Text") : tone, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(tb, 1);
        grid.Children.Add(tb);
        grid.Children.Add(buttons);
        CrossBanner.Child = grid;
        CrossBanner.BorderBrush = tone == Res("Muted") ? Res("Line") : tone;
    }

    Button BannerButton(string text, bool accent, Action click)
    {
        var b = new Button
        {
            Content = text, Style = (Style)FindResource(accent ? "AccentButton" : "FlatButton"), Margin = new Thickness(8, 0, 0, 0),
        };
        b.Click += (_, _) => click();
        return b;
    }

    // ---- Actions ----

    void ApplyCrosshair(CrosshairItem item)
    {
        var result = Cross.Apply(item);
        bool copied = TryCopy(item.Value.ToGameCode());
        string tail = copied ? " Код скопирован — его можно вставить в игре." : "";
        switch (result)
        {
            case ApplyResult.Applied:
                ShowToast($"Готово! «{item.Name}» будет в игре, когда ты её запустишь", 3.5);
                break;
            case ApplyResult.AfterGame:
                ShowToast("Игра запущена — прицел встанет, когда ты её закроешь." + tail, 5);
                break;
            case ApplyResult.NoGameFiles:
                ShowToast("Не нашёл настройки игры." + tail, 5);
                break;
            default:
                ShowToast("Не получилось записать прицел в игру." + tail, 5);
                break;
        }
        if (CrossLayer.Visibility == Visibility.Visible && !crossEditing) ShowCrosshair(item);
    }

    static bool TryCopy(string text)
    {
        // The clipboard can be briefly locked by another program.
        for (int i = 0; i < 3; i++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch { Thread.Sleep(60); }
        }
        return false;
    }

    void CopyGameCode(Crosshair c)
    {
        if (TryCopy(c.ToGameCode())) ShowToast("Код скопирован. В игре: Настройки → Поделиться настройками → Применить код", 4);
        else ShowToast("Не удалось скопировать, попробуй ещё раз");
    }

    void CrossGuardToggle_Click(object sender, RoutedEventArgs e)
    {
        App.Current.Settings.CrosshairGuard = CrossGuardToggle.IsChecked == true;
        App.Current.Settings.Save();
    }

    void CrossHeroesToggle_Click(object sender, RoutedEventArgs e)
    {
        App.Current.Settings.CrosshairAllHeroes = CrossHeroesToggle.IsChecked == true;
        App.Current.Settings.Save();
    }

    void OpenBackups_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(CrosshairGame.BackupFolder) { UseShellExecute = true });

    // ---- Side panel ----

    void OpenCrossPanel(string title, Action<StackPanel> fill, bool editing = false)
    {
        crossEditing = editing;
        panelPreview = null;
        CrossTitle.Text = title;
        CrossBody.Children.Clear();
        fill(CrossBody);
        CrossScroll.ScrollToTop();
        if (CrossLayer.Visibility == Visibility.Visible) return;
        CrossLayer.Visibility = Visibility.Visible;
        CrossShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(CrossPanel.Width, 0, Slide) { EasingFunction = Ease });
        CrossDim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 0.55, Slide));
    }

    void CloseCrossPanel()
    {
        if (CrossLayer.Visibility != Visibility.Visible) return;
        crossEditing = false;
        var a = new DoubleAnimation(0, CrossPanel.Width, Slide) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        a.Completed += (_, _) => CrossLayer.Visibility = Visibility.Collapsed;
        CrossShift.BeginAnimation(TranslateTransform.XProperty, a);
        CrossDim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, Slide));
    }

    void CrossDim_Click(object sender, MouseButtonEventArgs e) => CloseCrossPanel();

    void CrossClose_Click(object sender, RoutedEventArgs e) => CloseCrossPanel();

    // The panel's preview is true size unless the player turns the magnifier on; the choice is kept while the app runs.
    double panelMagnify = 1;
    TextBlock magnifyNote = new();

    string ZoomCaption() => panelMagnify > 1 ? "КАК В ИГРЕ" : "ЛУПА ×3";

    string MagnifyText() => panelMagnify > 1
        ? "Увеличено в 3 раза, чтобы разглядеть детали. В игре прицел меньше."
        : "Размер как в игре на твоём экране.";

    FrameworkElement BigPreview(Crosshair c)
    {
        panelPreview = new CrosshairPreview { Value = c, Magnify = panelMagnify, Height = 230, BackgroundIndex = crossBg };
        var stack = new StackPanel();
        stack.Children.Add(new Border { CornerRadius = new CornerRadius(4), ClipToBounds = true, Child = panelPreview });
        var bgs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        bgs.Children.Add(new TextBlock { Text = "Фон:", FontSize = 12.5, Foreground = Res("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        for (int i = 0; i < CrosshairPreview.Backgrounds.Length; i++)
        {
            int index = i;
            var b = new Button
            {
                Content = CrosshairPreview.Backgrounds[i].Name, Style = (Style)FindResource("GhostButton"), FontFamily = (FontFamily)FindResource("Body"),
                FontSize = 12.5, Padding = new Thickness(6, 2, 6, 2),
            };
            b.Click += (_, _) => { if (panelPreview != null) panelPreview.BackgroundIndex = index; };
            bgs.Children.Add(b);
        }
        var zoom = new Button
        {
            Content = ZoomCaption(), Style = (Style)FindResource("FlatButton"), Margin = new Thickness(12, 0, 0, 0),
            ToolTip = "Увеличить, чтобы разглядеть детали. Без лупы размер как в игре.",
        };
        zoom.Click += (_, _) =>
        {
            panelMagnify = panelMagnify > 1 ? 1 : 3;
            if (panelPreview != null) panelPreview.Magnify = panelMagnify;
            zoom.Content = ZoomCaption();
            magnifyNote.Text = MagnifyText();
        };
        bgs.Children.Add(zoom);
        stack.Children.Add(bgs);
        magnifyNote = Note(MagnifyText(), 11.5, 2);
        stack.Children.Add(magnifyNote);
        return stack;
    }

    void ShowCrosshair(CrosshairItem item)
    {
        OpenCrossPanel(item.Name.ToUpperInvariant(), body =>
        {
            body.Children.Add(BigPreview(item.Value));
            string meta = $"{ShapeName(item)} · {item.ColorName.ToLowerInvariant()}" + (item.Source switch
            {
                "mine" => " · мой прицел", "community" => " · от игрока", _ => "",
            }) + (item.Applies > 0 ? " · " + Installs(item.Applies) : "");
            body.Children.Add(Note(meta, 13, 12));

            bool active = IsApplied(item);
            if (active) body.Children.Add(new TextBlock { Text = "✓ Этот прицел стоит у тебя в игре", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Res("Good"), Margin = new Thickness(0, 10, 0, 0) });

            var apply = new Button
            {
                Content = active ? "ПОСТАВИТЬ ЕЩЁ РАЗ" : "ПОСТАВИТЬ В ИГРУ", Style = (Style)FindResource("AccentButton"), FontSize = 17,
                Padding = new Thickness(14, 9, 14, 10), Margin = new Thickness(0, 14, 0, 0),
            };
            apply.Click += (_, _) => ApplyCrosshair(item);
            body.Children.Add(apply);

            var row = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            var copy = new Button { Content = "КОПИРОВАТЬ КОД", Style = (Style)FindResource("FlatButton"), Margin = new Thickness(0, 0, 8, 8) };
            copy.Click += (_, _) => CopyGameCode(item.Value);
            row.Children.Add(copy);
            var like = LikeButton(item, small: false);
            like.Margin = new Thickness(0, 0, 8, 8);
            row.Children.Add(like);
            var edit = new Button { Content = item.Source == "mine" ? "ИЗМЕНИТЬ" : "СДЕЛАТЬ НА ЕГО ОСНОВЕ", Style = (Style)FindResource("FlatButton"), Margin = new Thickness(0, 0, 8, 8) };
            edit.Click += (_, _) => ShowEditor(item.Value.Clone(), item.Source == "mine" ? item : null, item.Source == "mine" ? item.Name : item.Name + " (мой)");
            row.Children.Add(edit);
            if (item.Source == "mine")
            {
                if (CrosshairServer.Enabled)
                {
                    var share = new Button { Content = "ПОДЕЛИТЬСЯ СО ВСЕМИ", Style = (Style)FindResource("FlatButton"), Margin = new Thickness(0, 0, 8, 8) };
                    share.Click += async (_, _) =>
                    {
                        share.IsEnabled = false;
                        string? error = await Cross.Share(item);
                        ShowToast(error ?? "Спасибо! Прицел появился во вкладке «От игроков»", 3.5);
                        share.IsEnabled = error != null;
                    };
                    row.Children.Add(share);
                }
                var delete = new Button { Content = "УДАЛИТЬ", Style = (Style)FindResource("FlatButton"), Margin = new Thickness(0, 0, 8, 8) };
                delete.Click += (_, _) =>
                {
                    if (MessageBox.Show(this, $"Удалить «{item.Name}» из «Мои»?", "Deadlock Meta", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    Cross.DeleteMine(item);
                    CloseCrossPanel();
                    ShowToast("Удалён");
                };
                row.Children.Add(delete);
            }
            body.Children.Add(row);

            body.Children.Add(Note(HowToPaste, 12.5, 10));

            var consoleHead = Label("ДЛЯ КОНСОЛИ (F7)");
            consoleHead.Margin = new Thickness(0, 24, 0, 0);
            body.Children.Add(consoleHead);
            body.Children.Add(Note("Старый способ: команды для консоли игры. В новых версиях Deadlock прицел берётся из настроек, поэтому лучше код выше.", 12.5));
            var cons = new Button { Content = "КОПИРОВАТЬ КОМАНДЫ", Style = (Style)FindResource("FlatButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            cons.Click += (_, _) => ShowToast(TryCopy(item.Value.ToConsole()) ? "Команды скопированы. Открой консоль (F7) и вставь" : "Не удалось скопировать");
            body.Children.Add(cons);
        });
    }

    // ---- Editor ----

    void CrossCreate_Click(object sender, RoutedEventArgs e) =>
        ShowEditor(Cross.Library.Active?.Value.Clone() ?? Crosshair.Default, null, "Мой прицел");

    void ShowEditor(Crosshair c, CrosshairItem? editing, string name)
    {
        var sliders = new Dictionary<CrosshairField, (Slider Slider, TextBlock Value)>();
        bool syncing = false;

        OpenCrossPanel(editing != null ? "ИЗМЕНИТЬ ПРИЦЕЛ" : "СВОЙ ПРИЦЕЛ", body =>
        {
            body.Children.Add(BigPreview(c));
            var preview = panelPreview!;

            var nameHead = Label("НАЗВАНИЕ");
            nameHead.Margin = new Thickness(0, 14, 0, 6);
            body.Children.Add(nameHead);
            var nameBox = new TextBox { Text = name, Style = (Style)FindResource("Input"), MaxLength = 40 };
            body.Children.Add(nameBox);

            void Refresh(CrosshairField? except)
            {
                syncing = true;
                foreach (var (f, (s, v)) in sliders)
                {
                    if (f != except) s.Value = Shown(f, c[f]);
                    v.Text = ValueText(f, c[f]);
                }
                syncing = false;
                preview.Value = c;
                preview.InvalidateVisual();
            }

            void Section(string title, string? hint = null)
            {
                var head = Label(title);
                head.Margin = new Thickness(0, 22, 0, hint == null ? 6 : 0);
                body.Children.Add(head);
                if (hint != null) body.Children.Add(Note(hint, 12, 2));
            }

            void Row(CrosshairField f, string label)
            {
                var setting = Crosshair.Settings.First(s => s.Field == f);
                var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
                grid.Children.Add(new TextBlock { Text = label, FontSize = 13.5, Foreground = Res("Text"), VerticalAlignment = VerticalAlignment.Center });
                bool percent = IsOpacity(f);
                var slider = new Slider
                {
                    Style = (Style)FindResource("Range"), Minimum = percent ? 0 : setting.Min, Maximum = percent ? 100 : setting.Max,
                    SmallChange = percent ? 5 : setting.Step, LargeChange = percent ? 10 : setting.Step * 4,
                    TickFrequency = percent ? 5 : setting.Step, IsSnapToTickEnabled = true, Value = Shown(f, c[f]),
                };
                Grid.SetColumn(slider, 1);
                grid.Children.Add(slider);
                var value = new TextBlock { Text = ValueText(f, c[f]), FontSize = 13, Foreground = Res("Muted"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(value, 2);
                grid.Children.Add(value);
                slider.ValueChanged += (_, a) =>
                {
                    if (syncing) return;
                    c[f] = percent ? a.NewValue / 100 : a.NewValue;
                    Refresh(f);
                };
                sliders[f] = (slider, value);
                body.Children.Add(grid);
            }

            void Swatches(CrosshairField r, CrosshairField g, CrosshairField b, IEnumerable<(string Name, byte R, byte G, byte B)> colors)
            {
                var wrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
                foreach (var col in colors)
                {
                    var sw = new Border
                    {
                        Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 8, 4), Cursor = Cursors.Hand,
                        Background = new SolidColorBrush(Color.FromRgb(col.R, col.G, col.B)), BorderBrush = Res("Line"), BorderThickness = new Thickness(2),
                        ToolTip = col.Name,
                    };
                    sw.MouseLeftButtonUp += (_, _) =>
                    {
                        (c[r], c[g], c[b]) = (col.R, col.G, col.B);
                        Refresh(null);
                    };
                    wrap.Children.Add(sw);
                }
                body.Children.Add(wrap);
            }

            Section("ЛИНИИ");
            Row(CrosshairField.Height, "Длина");
            Row(CrosshairField.Width, "Толщина");
            Row(CrosshairField.Gap, "Расстояние от центра");
            Row(CrosshairField.PipOpacity, "Видимость");
            var moving = new CheckBox { Style = (Style)FindResource("Toggle"), IsChecked = !c.StaticGap, VerticalAlignment = VerticalAlignment.Center };
            moving.Click += (_, _) => { c.StaticGap = moving.IsChecked != true; Refresh(null); };
            var movingRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            movingRow.Children.Add(moving);
            movingRow.Children.Add(new TextBlock { Text = "Расходятся при стрельбе и беге", FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) });
            body.Children.Add(movingRow);

            Section("ТОЧКА В ЦЕНТРЕ");
            Row(CrosshairField.DotSize, "Размер");
            Row(CrosshairField.DotOpacity, "Видимость");

            Section("ЦВЕТ ПРИЦЕЛА");
            Swatches(CrosshairField.R, CrosshairField.G, CrosshairField.B, CrosshairStyle.Colors);
            Row(CrosshairField.R, "Красный");
            Row(CrosshairField.G, "Зелёный");
            Row(CrosshairField.B, "Синий");

            Section("ОБВОДКА ЛИНИЙ", "Тёмная рамка вокруг линий — прицел лучше видно на светлом.");
            Row(CrosshairField.PipBorder, "Толщина");
            Row(CrosshairField.PipBorderGap, "Отступ");
            Row(CrosshairField.PipBorderOpacity, "Видимость");

            Section("КРУЖОК ВОКРУГ ТОЧКИ", "Маленький отступ — обводка точки, большой — кружок вокруг центра.");
            Row(CrosshairField.DotBorder, "Толщина");
            Row(CrosshairField.DotBorderGap, "Отступ");
            Row(CrosshairField.DotBorderOpacity, "Видимость");

            Section("ЦВЕТ ОБВОДКИ И КРУЖКА");
            Swatches(CrosshairField.OutlineR, CrosshairField.OutlineG, CrosshairField.OutlineB,
                [("Чёрный", 0, 0, 0), .. CrosshairStyle.Colors]);
            Row(CrosshairField.OutlineR, "Красный");
            Row(CrosshairField.OutlineG, "Зелёный");
            Row(CrosshairField.OutlineB, "Синий");

            CrosshairItem Save()
            {
                string n = nameBox.Text.Trim().Length > 0 ? nameBox.Text.Trim() : "Мой прицел";
                var saved = Cross.SaveMine(n, c.Clone(), editing);
                editing = saved;
                return saved;
            }

            var actions = new WrapPanel { Margin = new Thickness(0, 26, 0, 0) };
            var saveApply = new Button { Content = "СОХРАНИТЬ И ПОСТАВИТЬ", Style = (Style)FindResource("AccentButton"), FontSize = 16, Padding = new Thickness(14, 8, 14, 9), Margin = new Thickness(0, 0, 8, 8) };
            saveApply.Click += (_, _) =>
            {
                var item = Save();
                crossEditing = false;
                ApplyCrosshair(item);
                ShowCrosshair(item);
            };
            actions.Children.Add(saveApply);
            var save = new Button { Content = "СОХРАНИТЬ В «МОИ»", Style = (Style)FindResource("FlatButton"), Margin = new Thickness(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Center };
            save.Click += (_, _) =>
            {
                var item = Save();
                ShowToast($"«{item.Name}» сохранён в «Мои»");
            };
            actions.Children.Add(save);
            var reset = new Button { Content = "КАК В ИГРЕ ПО УМОЛЧАНИЮ", Style = (Style)FindResource("FlatButton"), Margin = new Thickness(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Center };
            reset.Click += (_, _) =>
            {
                var d = Crosshair.Default;
                foreach (var s in Crosshair.Settings) c[s.Field] = d[s.Field];
                moving.IsChecked = !c.StaticGap;
                Refresh(null);
            };
            actions.Children.Add(reset);
            body.Children.Add(actions);
        }, editing: true);
    }

    static bool IsOpacity(CrosshairField f) =>
        f is CrosshairField.PipOpacity or CrosshairField.DotOpacity or CrosshairField.PipBorderOpacity or CrosshairField.DotBorderOpacity;

    static double Shown(CrosshairField f, double v) => IsOpacity(f) ? Math.Round(v * 100) : v;

    static string ValueText(CrosshairField f, double v) => IsOpacity(f)
        ? Math.Round(v * 100).ToString(CultureInfo.InvariantCulture) + "%"
        : v.ToString("0.#", CultureInfo.InvariantCulture);

    // ---- Pasting a code ----

    void CrossImport_Click(object sender, RoutedEventArgs e)
    {
        string start = "";
        try
        {
            if (Clipboard.ContainsText() && Clipboard.GetText() is { Length: < 4000 } clip && Crosshair.Parse(clip, out _) != null) start = clip.Trim();
        }
        catch { }

        OpenCrossPanel("ВСТАВИТЬ КОД", body =>
        {
            body.Children.Add(Note("Подходит код из игры (Настройки → «Поделиться настройками» → «Скопировать код»), код с сайта с прицелами или команды для консоли.", 13.5, 0));
            var box = new TextBox
            {
                Style = (Style)FindResource("Input"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 90,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 12, 0, 0), FontSize = 12.5, Text = start,
            };
            body.Children.Add(box);
            var paste = new Button { Content = "ВСТАВИТЬ ИЗ БУФЕРА ОБМЕНА", Style = (Style)FindResource("FlatButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            paste.Click += (_, _) =>
            {
                try { box.Text = Clipboard.GetText(); }
                catch { ShowToast("Не удалось прочитать буфер обмена"); }
            };
            body.Children.Add(paste);
            var result = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
            body.Children.Add(result);

            void Update()
            {
                result.Children.Clear();
                if (box.Text.Trim().Length == 0) return;
                if (Crosshair.Parse(box.Text, out string error) is not { } c)
                {
                    result.Children.Add(new TextBlock { Text = error, Foreground = Res("Bad"), FontSize = 13.5, TextWrapping = TextWrapping.Wrap });
                    return;
                }
                result.Children.Add(BigPreview(c));
                var nameHead = Label("НАЗВАНИЕ");
                nameHead.Margin = new Thickness(0, 14, 0, 6);
                result.Children.Add(nameHead);
                var nameBox = new TextBox { Text = "Прицел по коду", Style = (Style)FindResource("Input"), MaxLength = 40 };
                result.Children.Add(nameBox);
                var actions = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
                CrosshairItem Save() => Cross.SaveMine(nameBox.Text.Trim().Length > 0 ? nameBox.Text.Trim() : "Прицел по коду", c, null);
                var apply = new Button { Content = "СОХРАНИТЬ И ПОСТАВИТЬ", Style = (Style)FindResource("AccentButton"), FontSize = 16, Padding = new Thickness(14, 8, 14, 9), Margin = new Thickness(0, 0, 8, 8) };
                apply.Click += (_, _) =>
                {
                    var item = Save();
                    crossEditing = false;
                    ApplyCrosshair(item);
                    ShowCrosshair(item);
                };
                actions.Children.Add(apply);
                var save = new Button { Content = "СОХРАНИТЬ В «МОИ»", Style = (Style)FindResource("FlatButton"), Margin = new Thickness(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Center };
                save.Click += (_, _) =>
                {
                    var item = Save();
                    ShowToast($"«{item.Name}» сохранён в «Мои»");
                    ShowCrosshair(item);
                };
                actions.Children.Add(save);
                var edit = new Button { Content = "ИЗМЕНИТЬ", Style = (Style)FindResource("FlatButton"), Margin = new Thickness(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Center };
                edit.Click += (_, _) => ShowEditor(c.Clone(), null, nameBox.Text.Trim());
                actions.Children.Add(edit);
                result.Children.Add(actions);
            }
            box.TextChanged += (_, _) => Update();
            Update();
        });
    }
}
