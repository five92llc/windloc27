using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace WinLoc27;

/// <summary>
/// Self-update from GitHub Releases (public repo). A release is tagged vX.Y.Z and carries
/// WinLoc27.exe plus WinLoc27.exe.sha256; the release workflow produces both.
/// </summary>
static class Updater
{
    /// <summary>"owner/repo" of the GitHub repository that publishes the releases.</summary>
    public const string Repo = "five92llc/windloc27";

    const string ExeAsset = "WinLoc27.exe";
    const string ShaAsset = "WinLoc27.exe.sha256";

    static readonly HttpClient Http = CreateClient();

    public sealed record Release(Version Version, string Tag, string ExeUrl, string? ShaUrl);

    public static Version Current
    {
        get
        {
            var v = Assembly.GetEntryAssembly()!.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }
    }

    public static string CurrentText => "v" + Current;

    static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("WinLoc27-Updater");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    /// <summary>Returns the newest release if it is newer than this build, otherwise null.</summary>
    public static async Task<Release?> CheckAsync()
    {
        using var resp = await Http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest");
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // no releases yet
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;
        if (version <= Current) return null;

        string? exe = null, sha = null;
        foreach (var a in root.GetProperty("assets").EnumerateArray())
        {
            var name = a.GetProperty("name").GetString();
            var url = a.GetProperty("browser_download_url").GetString();
            if (name == ExeAsset) exe = url;
            else if (name == ShaAsset) sha = url;
        }
        return exe == null ? null : new Release(version, tag, exe, sha);
    }

    /// <summary>
    /// Downloads and verifies the new exe, then starts a helper script that waits for this process
    /// to exit, swaps the file and relaunches. The caller must exit the app right after this returns.
    /// </summary>
    public static async Task StageAndRestartAsync(Release release)
    {
        var exePath = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown exe path.");
        var newPath = exePath + ".new";

        var bytes = await Http.GetByteArrayAsync(release.ExeUrl);
        if (release.ShaUrl != null)
        {
            var expected = (await Http.GetStringAsync(release.ShaUrl)).Trim().Split(' ')[0].ToLowerInvariant();
            var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (expected != actual) throw new InvalidDataException("Download failed its SHA-256 check.");
        }

        try
        {
            await File.WriteAllBytesAsync(newPath, bytes);
        }
        catch (UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "Cannot write next to the app. Move WinLoc27.exe to a folder you own (for example Documents) and try again.");
        }

        var script = Path.Combine(Path.GetTempPath(), $"winloc27-update-{Environment.ProcessId}.cmd");
        File.WriteAllText(script, string.Join("\r\n",
            "@echo off",
            "set n=0",
            ":retry",
            "set /a n+=1",
            "ping -n 2 127.0.0.1 >nul",
            $"move /y \"{newPath}\" \"{exePath}\" >nul 2>&1",
            "if errorlevel 1 if %n% LSS 30 goto retry",
            $"start \"\" \"{exePath}\"",
            "del \"%~f0\""));

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        });
    }
}
