using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ADraftCompact.Services;

public sealed record AssetDownloadResult(int Downloaded, int Skipped, int Failed);
public sealed record AssetStorageInfo(int FileCount, long TotalBytes, string DirectoryPath);

/// <summary>
/// Downloads public Dota 2 image assets and OpenDota constants. Files are stored in the Assets folder next to the executable.
/// Existing files are kept, so subsequent runs act as incremental updates.
/// </summary>
public sealed class DotaAssetsDownloader
{
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly ILogger<DotaAssetsDownloader> _logger;

    public DotaAssetsDownloader(ILogger<DotaAssetsDownloader> logger)
    {
        _logger = logger;
    }
    private static readonly Uri OpenDotaHeroes = new("https://api.opendota.com/api/constants/heroes");
    private static readonly Uri OpenDotaAbilities = new("https://api.opendota.com/api/constants/abilities");
    private static readonly Uri OpenDotaHeroAbilities = new("https://api.opendota.com/api/constants/hero_abilities");
    private const string Cdn = "https://cdn.cloudflare.steamstatic.com/apps/dota2/images/";

    private static string AssetsRoot => AppPaths.Assets;

    public static AssetStorageInfo GetStorageInfo()
    {
        var root = AssetsRoot;
        if (!Directory.Exists(root)) return new AssetStorageInfo(0, 0, root);
        var files = new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories).ToArray();
        return new AssetStorageInfo(files.Length, files.Sum(file => file.Length), root);
    }

    public async Task<bool> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        // Test the exact API needed by the downloader, not a generic internet endpoint.
        using var request = new HttpRequestMessage(HttpMethod.Get, OpenDotaHeroes);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        _logger.LogInformation("OpenDota availability check: HTTP {StatusCode}", (int)response.StatusCode);
        if (!response.IsSuccessStatusCode) return false;

        var cdnProbe = new Uri($"{Cdn}dota_react/heroes/antimage.png");
        using var cdnResponse = await Http.GetAsync(cdnProbe, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        _logger.LogInformation("Steam CDN availability check: HTTP {StatusCode}", (int)cdnResponse.StatusCode);
        return cdnResponse.IsSuccessStatusCode;
    }

    public async Task<AssetDownloadResult> DownloadAsync(Action<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Dota 2 asset download started. Directory: {AssetsRoot}", AssetsRoot);
        progress?.Invoke($"Preparing asset directory: {AssetsRoot}");
        Directory.CreateDirectory(AssetsRoot);
        var dataDirectory = Path.Combine(AssetsRoot, "data");
        Directory.CreateDirectory(dataDirectory);

        var downloaded = 0;
        var skipped = 0;

        _logger.LogInformation("Requesting hero reference: {Url}", OpenDotaHeroes);
        using var heroesDocument = await GetJsonWithRetryAsync(OpenDotaHeroes, "hero list", progress, cancellationToken);
        await SaveJsonAsync(heroesDocument.RootElement, Path.Combine(dataDirectory, "heroes.json"), cancellationToken);

        _logger.LogInformation("Hero reference loaded: {Count} entries", heroesDocument.RootElement.EnumerateObject().Count());
        _logger.LogInformation("Requesting ability reference: {Url}", OpenDotaAbilities);
        using var abilitiesDocument = await GetJsonWithRetryAsync(OpenDotaAbilities, "ability list", progress, cancellationToken);
        await SaveJsonAsync(abilitiesDocument.RootElement, Path.Combine(dataDirectory, "abilities.json"), cancellationToken);

        _logger.LogInformation("Ability reference loaded: {Count} entries", abilitiesDocument.RootElement.EnumerateObject().Count());
        _logger.LogInformation("Requesting hero-ability mappings: {Url}", OpenDotaHeroAbilities);
        using var heroAbilitiesDocument = await GetJsonWithRetryAsync(OpenDotaHeroAbilities, "hero-ability mappings", progress, cancellationToken);
        await SaveJsonAsync(heroAbilitiesDocument.RootElement, Path.Combine(dataDirectory, "hero_abilities.json"), cancellationToken);

        var jobs = new List<(string Url, string Path)>();

        foreach (var hero in heroesDocument.RootElement.EnumerateObject())
        {
            var shortName = hero.Value.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()?.Replace("npc_dota_hero_", "", StringComparison.Ordinal) : null;
            if (string.IsNullOrWhiteSpace(shortName))
                continue;

            jobs.Add(($"{Cdn}dota_react/heroes/{shortName}.png", Path.Combine(AssetsRoot, "heroes", "portraits", $"{shortName}.png")));
        }

        // Only the modern icons of hero abilities are needed (talents, attribute bonuses and other non-draftable entries never appear
        // in the draft), which is ~1/10 of the OpenDota ability list. Hero portraits are the only hero art used.
        var abilityKeys = GetDraftableAbilityKeys(heroAbilitiesDocument.RootElement, abilitiesDocument.RootElement);
        _logger.LogInformation("Draftable abilities: {Count} of {All} in the reference data", abilityKeys.Count,
            abilitiesDocument.RootElement.EnumerateObject().Count());
        foreach (var key in abilityKeys)
            jobs.Add(($"{Cdn}dota_react/abilities/{key}.png", Path.Combine(AssetsRoot, "abilities", "modern", $"{key}.png")));

        var total = jobs.Count;
        var completed = 0;
        var failed = 0;
        _logger.LogInformation("Image download queue created: {Count} files", total);
        progress?.Invoke($"Prepared {total} images for download checks...");
        using var semaphore = new SemaphoreSlim(24);
        // Abilities that exist in the reference data but have no modern icon on the CDN (new patch, rename): they can never be recognized.
        var missingModern = new System.Collections.Concurrent.ConcurrentBag<string>();
        var modernDirectory = Path.Combine(AssetsRoot, "abilities", "modern");
        var tasks = jobs.Select(async job =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var outcome = await DownloadFileAsync(job.Url, job.Path, cancellationToken);
                switch (outcome)
                {
                    case DownloadOutcome.Downloaded:
                        Interlocked.Increment(ref downloaded);
                        break;
                    case DownloadOutcome.Skipped:
                        Interlocked.Increment(ref skipped);
                        break;
                    case DownloadOutcome.Missing:
                        Interlocked.Increment(ref skipped);
                        if (string.Equals(Path.GetDirectoryName(job.Path), modernDirectory, StringComparison.OrdinalIgnoreCase))
                        {
                            var id = Path.GetFileNameWithoutExtension(job.Path);
                            if (!IconIndex.ExcludedAbilityPrefixes.Any(p => id.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                                missingModern.Add(id);
                        }
                        break;
                }
            }
            catch (Exception)
            {
                Interlocked.Increment(ref failed);
                // Individual asset failures are counted and reported in the final summary to avoid huge logs.
            }
            finally
            {
                semaphore.Release();
                var current = Interlocked.Increment(ref completed);
                if (current % 25 == 0 || current == total)
                    progress?.Invoke($"Downloading images: {current} of {total}...");
            }
        });

        await Task.WhenAll(tasks);
        if (!missingModern.IsEmpty)
            _logger.LogWarning("{Count} abilities have no modern icon on the CDN (they will rely on the legacy icon or not be recognized at all): {Ids}",
                missingModern.Count, string.Join(", ", missingModern.OrderBy(x => x, StringComparer.Ordinal).Take(80)));
        var result = new AssetDownloadResult(downloaded, skipped, failed);
        _logger.LogInformation("Download complete. New files: {Downloaded}; skipped/missing: {Skipped}; failed: {Failed}",
            result.Downloaded, result.Skipped, result.Failed);
        progress?.Invoke($"Complete: {result.Downloaded} new, {result.Skipped} skipped/missing from CDN, {result.Failed} failed.");
        return result;
    }

    /// <summary>Ability keys listed in hero_abilities (regular abilities of every hero); falls back to all non-talent keys of abilities.json.</summary>
    private static List<string> GetDraftableAbilityKeys(JsonElement heroAbilities, JsonElement allAbilities)
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        if (heroAbilities.ValueKind == JsonValueKind.Object)
            foreach (var hero in heroAbilities.EnumerateObject())
                if (hero.Value.ValueKind == JsonValueKind.Object && hero.Value.TryGetProperty("abilities", out var list) && list.ValueKind == JsonValueKind.Array)
                    foreach (var item in list.EnumerateArray())
                        if (item.GetString() is { } key) keys.Add(key);

        if (keys.Count == 0 && allAbilities.ValueKind == JsonValueKind.Object)
            foreach (var ability in allAbilities.EnumerateObject()) keys.Add(ability.Name);

        return keys.Where(key => !string.IsNullOrWhiteSpace(key) && !key.Contains('/') && !key.Contains('\\')
                                 && !IconIndex.ExcludedAbilityPrefixes.Any(p => key.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AbilityDraftOverlay/0.1 (local asset downloader)");
        return client;
    }

    /// <summary>OpenDota is sometimes slow: retry a few times instead of waiting silently.</summary>
    private async Task<JsonDocument> GetJsonWithRetryAsync(Uri uri, string what, Action<string>? progress, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                progress?.Invoke(attempt == 1 ? $"Fetching {what} from OpenDota..." : $"Fetching {what} from OpenDota (attempt {attempt} of 3)...");
                return await GetJsonAsync(uri, cancellationToken);
            }
            catch (Exception ex) when (attempt < 3 && !cancellationToken.IsCancellationRequested
                                       && ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                _logger.LogWarning("OpenDota request {Url} failed (attempt {Attempt}): {Message}", uri, attempt, ex.Message);
            }
        }
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
    private enum DownloadOutcome { Downloaded, Skipped, Missing }

    private static async Task<DownloadOutcome> DownloadFileAsync(string url, string path, CancellationToken cancellationToken)
    {
        if (File.Exists(path) && new FileInfo(path).Length > 0)
            return DownloadOutcome.Skipped;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".download";
        try
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.Gone)
                return DownloadOutcome.Missing;
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                await input.CopyToAsync(output, cancellationToken);

            if (new FileInfo(temporaryPath).Length == 0)
                throw new InvalidDataException($"CDN returned an empty file: {url}");
            File.Move(temporaryPath, path, overwrite: true);
            return DownloadOutcome.Downloaded;
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
