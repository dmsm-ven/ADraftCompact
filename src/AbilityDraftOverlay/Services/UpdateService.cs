using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ADraftCompact.Services;

public sealed record UpdateInfo(Version Version, string DownloadUrl, long Size);

/// <summary>
/// Self-update from GitHub Releases. The release asset is a .zip holding the single exe.
/// Only the exe is replaced; Assets, app.log, layout override and the settings files are never touched.
/// </summary>
public sealed class UpdateService
{
    public const string Repository = "dmsm-ven/ADraftCompact";
    private const string LatestUrl = "https://api.github.com/repos/" + Repository + "/releases/latest";

    private readonly ILogger<UpdateService> _logger;
    private static readonly HttpClient Http = CreateClient();

    public UpdateService(ILogger<UpdateService> logger) => _logger = logger;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AbilityDraftOverlay/" + AppVersion.Text);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>Returns the newer release, or null when up to date / offline / no suitable asset. Never throws.</summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            using var response = await Http.GetAsync(LatestUrl, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Update check: HTTP {Status}", (int)response.StatusCode);
                return null;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cts.Token));
            var root = doc.RootElement;
            var version = AppVersion.Parse(root.GetProperty("tag_name").GetString());
            if (version is null || version <= AppVersion.Current)
            {
                _logger.LogInformation("Update check: up to date (current {Current}, latest {Latest})", AppVersion.Text, version);
                return null;
            }

            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    return new UpdateInfo(version, asset.GetProperty("browser_download_url").GetString()!, asset.GetProperty("size").GetInt64());
            }

            _logger.LogWarning("Update check: release {Version} has no .zip asset", version);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogInformation("Update check failed: {Message}", ex.Message);
            return null;
        }
    }

    /// <summary>Downloads the zip, extracts the new exe next to a swap script and starts the script. The caller must exit the app afterwards.</summary>
    public async Task DownloadAndPrepareAsync(UpdateInfo info, IProgress<(long Done, long Total)> progress, CancellationToken ct = default)
    {
        var currentExe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine the executable path.");
        var work = Path.Combine(Path.GetTempPath(), "AbilityDraftOverlay-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);

        var zipPath = Path.Combine(work, "update.zip");
        using (var response = await Http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? info.Size;
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var target = File.Create(zipPath);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                progress.Report((done, total));
            }
        }

        var newExe = Path.Combine(work, "new.exe");
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            var entry = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidDataException("The update archive contains no .exe file.");
            entry.ExtractToFile(newExe, overwrite: true);
        }

        // The running exe cannot overwrite itself: a script waits for this process to exit, swaps the file and restarts the app.
        var script = Path.Combine(work, "update.cmd");
        var pid = Environment.ProcessId;
        File.WriteAllText(script, string.Join("\r\n",
            "@echo off",
            ":wait",
            $"tasklist /FI \"PID eq {pid}\" 2>nul | find \"{pid}\" >nul",
            "if errorlevel 1 goto swap",
            "ping -n 2 127.0.0.1 >nul",
            "goto wait",
            ":swap",
            "set n=0",
            ":retry",
            $"move /Y \"{newExe}\" \"{currentExe}\" >nul 2>&1",
            "if not errorlevel 1 goto done",
            "set /a n+=1",
            "if %n% geq 30 goto done",
            "ping -n 2 127.0.0.1 >nul",
            "goto retry",
            ":done",
            $"start \"\" \"{currentExe}\"",
            $"rmdir /S /Q \"{work}\"",
            ""));

        _logger.LogInformation("Update {Version} downloaded, starting swap script {Script}", AppVersion.Format(info.Version), script);
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(currentExe)!
        });
    }
}
