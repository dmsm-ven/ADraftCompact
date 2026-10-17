using System.IO;
using System.Text.Json;
using AbilityDraftOverlay.Models;
using Microsoft.Extensions.Logging;

namespace AbilityDraftOverlay.Services;

/// <summary>
/// Loads the draft screen layout. A file at <see cref="AppPaths.UserLayout"/> wins over the bundled one,
/// so coordinates can be tuned and re-tested without rebuilding the application.
/// </summary>
public sealed class DraftLayoutProvider
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<DraftLayoutProvider> _logger;

    public DraftLayoutProvider(ILogger<DraftLayoutProvider> logger) => _logger = logger;

    public static string BundledLayoutPath => Path.Combine(AppContext.BaseDirectory, "Layouts", "ability_draft_1920x1080.json");

    public DraftLayout Load()
    {
        var path = File.Exists(AppPaths.UserLayout) ? AppPaths.UserLayout : BundledLayoutPath;
        if (!File.Exists(path)) throw new FileNotFoundException("Файл раскладки драфта не найден.", path);

        DraftLayout layout;
        try
        {
            layout = JsonSerializer.Deserialize<DraftLayout>(File.ReadAllText(path), Options)
                     ?? throw new InvalidDataException("Файл раскладки пуст.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Ошибка в файле раскладки {path}: {ex.Message}", ex);
        }

        layout.Validate();
        _logger.LogInformation("Раскладка загружена из {Path} (calibrated={Calibrated})", path, layout.Calibrated);
        return layout;
    }
}
