using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DeadlockMeta.Core;

namespace DeadlockMeta;

/// <summary>The HUD tab: the in-game mod switch, what it shows, and where it stands.</summary>
public partial class MainWindow
{
    static HudModService Hud => App.Current.Hud;

    void InitHud()
    {
        var s = App.Current.Settings;
        HudToggle.IsChecked = s.HudEnabled;
        HudTimersToggle.IsChecked = s.HudTimers;
        HudReloadToggle.IsChecked = s.HudReload;
        HudZipToggle.IsChecked = s.HudZipline;
        HudMapToggle.IsChecked = s.HudCleanMap;
        Check(HudOpacityPanel, s.HudMapOpacity);
        Check(HudScalePanel, s.HudMapScale);
        Hud.Changed += RefreshHud;
        RefreshHud();
    }

    void RefreshHud()
    {
        var s = App.Current.Settings;
        HudOpacityPanel.IsEnabled = s.HudCleanMap;
        HudOpacityPanel.Opacity = s.HudCleanMap ? 1 : 0.45;
        string build = Hud.State.GameBuild is { } b ? $" (версия игры {b})" : "";
        (string text, string brush) = true switch
        {
            _ when Hud.GameDir == null => ("Не нашёл Deadlock в библиотеках Steam.", "Bad"),
            _ when Hud.Error != null => ("Не получилось: " + Hud.Error + " Подробности в data\\log.txt.", "Bad"),
            _ when Hud.Waiting && s.HudEnabled => ("Игра запущена — мод встанет, когда ты её закроешь. Он появится со следующего запуска.", "Text"),
            _ when Hud.Waiting => ("Игра запущена — мод уберётся, когда ты её закроешь.", "Text"),
            _ when s.HudEnabled && Hud.Installed && Hud.GameRunning => ("Мод установлен" + build + ". Если игра была открыта до установки, перезапусти её.", "Good"),
            _ when s.HudEnabled && Hud.Installed => ("Мод установлен" + build + " — будет в игре при следующем запуске.", "Good"),
            _ when s.HudEnabled => ("Ставлю мод…", "Text"),
            _ => ("Мод выключен. Файлы игры не изменены.", "Muted"),
        };
        HudStatusText.Text = text;
        HudStatusText.Foreground = Res(brush);
    }

    void HudToggle_Click(object sender, RoutedEventArgs e)
    {
        App.Current.Settings.HudEnabled = HudToggle.IsChecked == true;
        ApplyHud();
    }

    void HudOption_Click(object sender, RoutedEventArgs e)
    {
        var s = App.Current.Settings;
        s.HudTimers = HudTimersToggle.IsChecked == true;
        s.HudReload = HudReloadToggle.IsChecked == true;
        s.HudZipline = HudZipToggle.IsChecked == true;
        s.HudCleanMap = HudMapToggle.IsChecked == true;
        ApplyHud();
    }

    void HudOpacity_Click(object sender, RoutedEventArgs e)
    {
        App.Current.Settings.HudMapOpacity = int.Parse((string)((RadioButton)sender).Tag);
        ApplyHud();
    }

    void HudScale_Click(object sender, RoutedEventArgs e)
    {
        App.Current.Settings.HudMapScale = int.Parse((string)((RadioButton)sender).Tag);
        ApplyHud();
    }

    void ApplyHud()
    {
        App.Current.Settings.Save();
        Hud.Sync();
        RefreshHud();
    }

    void OpenAddons_Click(object sender, RoutedEventArgs e)
    {
        if (Hud.GameDir is not { } game)
        {
            ShowToast("Не нашёл папку игры.", 3);
            return;
        }
        string addons = Path.Combine(game, "game", "citadel", "addons");
        Process.Start(new ProcessStartInfo(Directory.Exists(addons) ? addons : Path.Combine(game, "game", "citadel")) { UseShellExecute = true });
    }
}
