using System.IO;
using System.Text.Json;
using ADraftCompact.Models;
using Microsoft.Extensions.Logging;

namespace ADraftCompact.Services;

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

    private const string EmbeddedLayoutName = "ability_draft_1920x1080.json";

    public DraftLayout Load()
    {
        var path = File.Exists(AppPaths.UserLayout) ? AppPaths.UserLayout : "(embedded)";
        string json;
        if (path != "(embedded)")
            json = File.ReadAllText(path);
        else
        {
            using var stream = typeof(DraftLayoutProvider).Assembly.GetManifestResourceStream(EmbeddedLayoutName)
                               ?? throw new FileNotFoundException("Embedded draft layout not found.", EmbeddedLayoutName);
            using var reader = new StreamReader(stream);
            json = reader.ReadToEnd();
        }

        DraftLayout layout;
        try
        {
            layout = JsonSerializer.Deserialize<DraftLayout>(json, Options)
                     ?? throw new InvalidDataException("Draft layout file is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Invalid draft layout file {path}: {ex.Message}", ex);
        }

        layout.Validate();
        _logger.LogInformation("Layout loaded from {Path} (calibrated={Calibrated})", path, layout.Calibrated);
        return layout;
    }
}
