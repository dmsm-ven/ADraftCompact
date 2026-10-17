using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AbilityDraftOverlay.Services;

public sealed record AssetDownloadResult(int Downloaded, int Skipped, int Failed);
public sealed record AssetStorageInfo(int FileCount, long TotalBytes, string DirectoryPath);

/// <summary>
/// Downloads public Dota 2 image assets and OpenDota constants. Files are stored under LocalAppData.
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

    private static string AssetsRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AbilityDraftOverlay", "Assets");

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
        _logger.LogInformation("Проверка доступности OpenDota: HTTP {StatusCode}", (int)response.StatusCode);
        if (!response.IsSuccessStatusCode) return false;

        var cdnProbe = new Uri($"{Cdn}dota_react/heroes/antimage.png");
        using var cdnResponse = await Http.GetAsync(cdnProbe, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        _logger.LogInformation("Проверка доступности Steam CDN: HTTP {StatusCode}", (int)cdnResponse.StatusCode);
        return cdnResponse.IsSuccessStatusCode;
    }

    public async Task<AssetDownloadResult> DownloadAsync(Action<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Начата загрузка ресурсов Dota 2. Каталог: {AssetsRoot}", AssetsRoot);
        progress?.Invoke($"Подготовка каталога ресурсов: {AssetsRoot}");
        Directory.CreateDirectory(AssetsRoot);
        var dataDirectory = Path.Combine(AssetsRoot, "data");
        Directory.CreateDirectory(dataDirectory);

        var downloaded = 0;
        var skipped = 0;

        progress?.Invoke("Получение списка героев из OpenDota...");
        _logger.LogInformation("Запрос справочника героев: {Url}", OpenDotaHeroes);
        using var heroesDocument = await GetJsonAsync(OpenDotaHeroes, cancellationToken);
        await SaveJsonAsync(heroesDocument.RootElement, Path.Combine(dataDirectory, "heroes.json"), cancellationToken);

        _logger.LogInformation("Справочник героев получен: {Count} записей", heroesDocument.RootElement.EnumerateObject().Count());
        progress?.Invoke("Получение списка способностей из OpenDota...");
        _logger.LogInformation("Запрос справочника способностей: {Url}", OpenDotaAbilities);
        using var abilitiesDocument = await GetJsonAsync(OpenDotaAbilities, cancellationToken);
        await SaveJsonAsync(abilitiesDocument.RootElement, Path.Combine(dataDirectory, "abilities.json"), cancellationToken);

        _logger.LogInformation("Справочник способностей получен: {Count} записей", abilitiesDocument.RootElement.EnumerateObject().Count());
        progress?.Invoke("Получение списка способностей героев из OpenDota...");
        _logger.LogInformation("Запрос связей героев и способностей: {Url}", OpenDotaHeroAbilities);
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
        var failed = 0;
        _logger.LogInformation("Сформирован список изображений: {Count} файлов", total);
        progress?.Invoke($"Подготовлено изображений к проверке/загрузке: {total}...");
        using var semaphore = new SemaphoreSlim(8);
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
                        _logger.LogDebug("Скачан файл: {Path}", job.Path);
                        break;
                    case DownloadOutcome.Skipped:
                        Interlocked.Increment(ref skipped);
                        break;
                    case DownloadOutcome.Missing:
                        Interlocked.Increment(ref skipped);
                        _logger.LogDebug("Ресурс отсутствует на CDN (404/410): {Url}", job.Url);
                        break;
                }
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failed);
                _logger.LogWarning(ex, "Не удалось скачать ресурс {Url} в {Path}", job.Url, job.Path);
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
        var result = new AssetDownloadResult(downloaded, skipped, failed);
        _logger.LogInformation("Загрузка завершена. Новых файлов: {Downloaded}; пропущено/отсутствует: {Skipped}; ошибок: {Failed}",
            result.Downloaded, result.Skipped, result.Failed);
        progress?.Invoke($"Завершено: новых {result.Downloaded}, пропущено/нет на CDN {result.Skipped}, ошибок {result.Failed}.");
        return result;
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
                throw new InvalidDataException($"CDN вернул пустой файл: {url}");
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
