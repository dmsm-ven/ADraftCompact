using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace AbilityDraftOverlay.Services;

public sealed record AssetDownloadResult(int Downloaded, int Skipped);

/// <summary>
/// Downloads public Dota 2 image assets and OpenDota constants. Files are stored under LocalAppData.
/// Existing files are kept, so subsequent runs act as incremental updates.
/// </summary>
public sealed class DotaAssetsDownloader
{
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly Uri OpenDotaHeroes = new("https://api.opendota.com/api/constants/heroes");
    private static readonly Uri OpenDotaAbilities = new("https://api.opendota.com/api/constants/abilities");
    private static readonly Uri OpenDotaHeroAbilities = new("https://api.opendota.com/api/constants/hero_abilities");
    private const string Cdn = "https://cdn.cloudflare.steamstatic.com/apps/dota2/images/";

    private static string AssetsRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AbilityDraftOverlay", "Assets");

    public async Task<AssetDownloadResult> DownloadAsync(Action<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(AssetsRoot);
        var dataDirectory = Path.Combine(AssetsRoot, "data");
        Directory.CreateDirectory(dataDirectory);

        var downloaded = 0;
        var skipped = 0;

        progress?.Invoke("Получение списка героев из OpenDota...");
        using var heroesDocument = await GetJsonAsync(OpenDotaHeroes, cancellationToken);
        await SaveJsonAsync(heroesDocument.RootElement, Path.Combine(dataDirectory, "heroes.json"), cancellationToken);

        progress?.Invoke("Получение списка способностей из OpenDota...");
        using var abilitiesDocument = await GetJsonAsync(OpenDotaAbilities, cancellationToken);
        await SaveJsonAsync(abilitiesDocument.RootElement, Path.Combine(dataDirectory, "abilities.json"), cancellationToken);

        progress?.Invoke("Получение списка способностей героев из OpenDota...");
        using var heroAbilitiesDocument = await GetJsonAsync(OpenDotaHeroAbilities, cancellationToken);
        await SaveJsonAsync(heroAbilitiesDocument.RootElement, Path.Combine(dataDirectory, "hero_abilities.json"), cancellationToken);

        var jobs = new List<(string Url, string Path)>();

        foreach (var hero in heroesDocument.RootElement.EnumerateObject())
        {
            var shortName = hero.Value.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()?.Replace("npc_dota_hero_", "", StringComparison.Ordinal) : null;
            if (string.IsNullOrWhiteSpace(shortName))
                continue;

            jobs.Add(($"{Cdn}dota_react/heroes/{shortName}.png", Path.Combine(AssetsRoot, "heroes", "portraits", $"{shortName}.png")));
            jobs.Add(($"{Cdn}dota_react/heroes/icons/{shortName}.png", Path.Combine(AssetsRoot, "heroes", "icons", $"{shortName}.png")));
            jobs.Add(($"{Cdn}images/heroes/{shortName}_lg.png", Path.Combine(AssetsRoot, "heroes", "legacy", $"{shortName}_lg.png")));
            jobs.Add(($"{Cdn}images/heroes/{shortName}_icon.png", Path.Combine(AssetsRoot, "heroes", "legacy", $"{shortName}_icon.png")));
        }

        foreach (var ability in abilitiesDocument.RootElement.EnumerateObject())
        {
            var key = ability.Name;
            if (string.IsNullOrWhiteSpace(key) || key.Contains('/') || key.Contains('\\'))
                continue;

            jobs.Add(($"{Cdn}dota_react/abilities/{key}.png", Path.Combine(AssetsRoot, "abilities", "modern", $"{key}.png")));
            jobs.Add(($"{Cdn}images/abilities/{key}_lg.png", Path.Combine(AssetsRoot, "abilities", "legacy", $"{key}_lg.png")));
            jobs.Add(($"{Cdn}images/abilities/{key}_md.png", Path.Combine(AssetsRoot, "abilities", "legacy", $"{key}_md.png")));
        }

        var total = jobs.Count;
        var completed = 0;
        using var semaphore = new SemaphoreSlim(8);
        var tasks = jobs.Select(async job =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var outcome = await DownloadFileAsync(job.Url, job.Path, cancellationToken);
                if (outcome)
                    Interlocked.Increment(ref downloaded);
                else
                    Interlocked.Increment(ref skipped);
            }
            finally
            {
                semaphore.Release();
                var current = Interlocked.Increment(ref completed);
                if (current % 25 == 0 || current == total)
                    progress?.Invoke($"Загрузка изображений: {current} из {total}...");
            }
        });

        await Task.WhenAll(tasks);
        return new AssetDownloadResult(downloaded, skipped);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AbilityDraftOverlay/0.1 (local asset downloader)");
        return client;
    }

    private static async Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static async Task SaveJsonAsync(JsonElement json, string path, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(json, JsonOptions), cancellationToken);
    }

    /// <returns>True if a new file was downloaded; false if the file already existed or the CDN returned 404.</returns>
    private static async Task<bool> DownloadFileAsync(string url, string path, CancellationToken cancellationToken)
    {
        if (File.Exists(path) && new FileInfo(path).Length > 0)
            return false;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".download";
        try
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.Gone)
                return false;
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                await input.CopyToAsync(output, cancellationToken);

            if (new FileInfo(temporaryPath).Length == 0)
                return false;
            File.Move(temporaryPath, path, overwrite: true);
            return true;
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
