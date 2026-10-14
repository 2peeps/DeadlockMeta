using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace DeadlockMeta.Core;

public sealed record UpdateInfo(Version Version, string ZipUrl, string PageUrl);

/// <summary>Checks GitHub Releases for a newer version and swaps the running exe for it.</summary>
public static class Updater
{
    const string Repo = "2peeps/DeadlockMeta";

    public static Version Current { get; } = typeof(Updater).Assembly.GetName().Version is { } v
        ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : new Version(0, 0, 0);

    /// <summary>The latest release when it is newer than this build, otherwise null.</summary>
    public static async Task<UpdateInfo?> Check()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var resp = await MetaData.Http.SendAsync(req);
        if (!resp.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest) || latest <= Current) return null;
        foreach (var a in root.GetProperty("assets").EnumerateArray())
        {
            string name = a.GetProperty("name").GetString() ?? "";
            if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                return new UpdateInfo(latest, a.GetProperty("browser_download_url").GetString()!,
                    root.GetProperty("html_url").GetString() ?? "");
        }
        return null;
    }

    /// <summary>
    /// Downloads the release, puts its exe in place of the running one and starts it. Windows lets a running exe be
    /// renamed (not overwritten), so the old one becomes DeadlockMeta.old.exe and is deleted by the next start.
    /// </summary>
    public static async Task Install(UpdateInfo u, IProgress<double>? progress = null)
    {
        string exe = Environment.ProcessPath!;
        string dir = Path.GetDirectoryName(exe)!;
        string work = Path.Combine(Settings.DataDir, "update");
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Directory.CreateDirectory(work);

        string zip = Path.Combine(work, "update.zip");
        using (var resp = await MetaData.Http.GetAsync(u.ZipUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? 0;
            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var dst = File.Create(zip);
            var buf = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n));
                done += n;
                if (total > 0) progress?.Report((double)done / total);
            }
        }

        ZipFile.ExtractToDirectory(zip, work);
        string fresh = Directory.GetFiles(work, "DeadlockMeta.exe", SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new InvalidDataException("в архиве нет DeadlockMeta.exe");

        string old = Path.Combine(dir, "DeadlockMeta.old.exe");
        if (File.Exists(old)) File.Delete(old);
        File.Move(exe, old);
        try { File.Move(fresh, exe); }
        catch
        {
            File.Move(old, exe);
            throw;
        }
        Log.Write($"updated {Current} -> {u.Version}");
        Process.Start(new ProcessStartInfo(exe, "--updated") { UseShellExecute = false, WorkingDirectory = dir });
    }

    /// <summary>Removes what the previous update left behind.</summary>
    public static void CleanUp()
    {
        try
        {
            string dir = Path.GetDirectoryName(Environment.ProcessPath!)!;
            string old = Path.Combine(dir, "DeadlockMeta.old.exe");
            if (File.Exists(old)) File.Delete(old);
            string work = Path.Combine(Settings.DataDir, "update");
            if (Directory.Exists(work)) Directory.Delete(work, true);
        }
        catch (Exception e) { Log.Write("update cleanup: " + e.Message); }
    }
}
