using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ADraftCompact.Models;

namespace ADraftCompact.Services;

/// <summary>Keeps the last recognized board in last-board.json next to the exe, so it is still there after a restart.</summary>
public static class LastBoardStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static DraftSession? Load()
    {
        try
        {
            return File.Exists(AppPaths.LastBoardFile)
                ? JsonSerializer.Deserialize<DraftSession>(File.ReadAllText(AppPaths.LastBoardFile), Options)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null; // a broken or locked file just means "no previous board"
        }
    }

    public static void Save(DraftSession session)
    {
        try
        {
            var temp = AppPaths.LastBoardFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(session, Options));
            File.Move(temp, AppPaths.LastBoardFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // not being able to save must never break recognition
        }
    }
}
