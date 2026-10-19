using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AbilityDraftOverlay.Models;
using Microsoft.Extensions.Logging;

namespace AbilityDraftOverlay.Services;

/// <summary>Keeps every draft session as a JSON file under %LocalAppData%\AbilityDraftOverlay\Sessions.</summary>
public sealed class SessionStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ILogger<SessionStore> _logger;

    public SessionStore(ILogger<SessionStore> logger) => _logger = logger;

    public static string PathFor(DraftSession session) =>
        Path.Combine(AppPaths.Sessions, $"{session.StartedAt.LocalDateTime:yyyyMMdd-HHmmss}_{session.Id}.json");

    public void Save(DraftSession session)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Sessions);
            var path = PathFor(session);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(session, Options));
            File.Move(temp, path, overwrite: true);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to save session {Id}", session.Id);
        }
    }
}
