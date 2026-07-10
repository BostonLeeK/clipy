using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using Clipy.Helpers;
using Clipy.Models;

namespace Clipy.Services;

public sealed class UpdateService
{
    private const string Owner = "BostonLeeK";
    private const string Repo = "clipy";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    static UpdateService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Clipy-Assistant");
        Http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public async Task<UpdateInfo?> CheckForUpdateAsync(AppConfig config, bool force = false, CancellationToken ct = default)
    {
        if (!force
            && config.LastUpdateCheckUtc is { } last
            && DateTimeOffset.UtcNow - last < CheckInterval)
            return null;

        config.LastUpdateCheckUtc = DateTimeOffset.UtcNow;

        using var response = await Http.GetAsync(
            $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest",
            ct);
        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        var remoteVersion = AppVersion.Normalize(tag);
        if (!AppVersion.IsNewer(remoteVersion, AppVersion.Current))
            return null;

        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        string? installerUrl = null;
        string? portableUrl = null;
        string? installerName = null;
        string? portableName = null;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
            var url = asset.TryGetProperty("browser_download_url", out var urlEl) ? urlEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                continue;

            if (name.StartsWith("Clipy-Setup-", StringComparison.OrdinalIgnoreCase)
                && name.EndsWith("-x64.exe", StringComparison.OrdinalIgnoreCase))
            {
                installerUrl = url;
                installerName = name;
            }
            else if (name.StartsWith("Clipy-", StringComparison.OrdinalIgnoreCase)
                     && name.EndsWith("-win-x64-portable.zip", StringComparison.OrdinalIgnoreCase))
            {
                portableUrl = url;
                portableName = name;
            }
        }

        if (installerUrl is null && portableUrl is null)
            return null;

        var notes = root.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() : null;
        return new UpdateInfo
        {
            Version = remoteVersion,
            ReleaseNotes = string.IsNullOrWhiteSpace(notes) ? $"Clipy {remoteVersion}" : notes.Trim(),
            InstallerUrl = installerUrl ?? portableUrl!,
            PortableUrl = portableUrl ?? installerUrl!,
            InstallerFileName = installerName ?? $"Clipy-Setup-{remoteVersion}-x64.exe",
            PortableFileName = portableName ?? $"Clipy-{remoteVersion}-win-x64-portable.zip",
        };
    }

    public async Task<string> DownloadAsync(
        UpdateInfo update,
        bool portable,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var url = portable ? update.PortableUrl : update.InstallerUrl;
        var fileName = portable ? update.PortableFileName : update.InstallerFileName;
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClipyAssistant", "updates");
        Directory.CreateDirectory(dir);

        var target = Path.Combine(dir, fileName);
        if (File.Exists(target))
        {
            try { File.Delete(target); } catch { /* ignore */ }
        }

        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = File.Create(target);

        var buffer = new byte[81920];
        long readTotal = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            readTotal += read;
            if (total > 0)
                progress?.Report(readTotal / (double)total);
        }

        progress?.Report(1);
        return target;
    }

    public void ApplyInstaller(string installerPath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NORESTART",
            UseShellExecute = true,
        });
    }

    public void ApplyPortable(string zipPath)
    {
        var exePath = Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, "Clipy.exe");
        var installDir = Path.GetDirectoryName(Path.GetFullPath(exePath))
            ?? AppContext.BaseDirectory;
        var versionDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClipyAssistant", "updates", "staging");
        if (Directory.Exists(versionDir))
        {
            try { Directory.Delete(versionDir, true); } catch { /* ignore */ }
        }
        Directory.CreateDirectory(versionDir);
        ZipFile.ExtractToDirectory(zipPath, versionDir, true);

        var pid = Environment.ProcessId;
        var updater = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClipyAssistant", "updates", "apply.cmd");
        var script = $"""
@echo off
:wait
tasklist /FI "PID eq {pid}" 2>NUL | find "{pid}" >NUL
if %ERRORLEVEL%==0 (
  timeout /t 1 /nobreak >nul
  goto wait
)
xcopy /E /Y /I /Q "{versionDir}\*" "{installDir}\"
start "" "{Path.Combine(installDir, "Clipy.exe")}"
del /F /Q "{zipPath}" 2>nul
rmdir /S /Q "{versionDir}" 2>nul
del /F /Q "%~f0"
""";
        File.WriteAllText(updater, script);
        Process.Start(new ProcessStartInfo
        {
            FileName = updater,
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }

    public static bool ShouldUsePortableUpdate()
    {
        var exePath = Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, "Clipy.exe");
        var installDir = Path.GetDirectoryName(Path.GetFullPath(exePath))
            ?? AppContext.BaseDirectory;
        if (installDir.Contains(@"\Programs\Clipy", StringComparison.OrdinalIgnoreCase))
            return false;
        if (installDir.Contains(@"\Program Files", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }
}
