using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ADraftCompact.Services;

/// <summary>
/// Reads the language Steam has set for Dota 2 (the "language" value in appmanifest_570.acf).
/// Returns null whenever it cannot be determined (Steam not found, game installed elsewhere, unexpected file format):
/// "unknown" must never be treated as "wrong".
/// A <c>-language</c> launch option is not visible here.
/// </summary>
public static class SteamGameLanguage
{
    private const string DotaAppId = "570";

    private static readonly Regex LibraryPath = new("\"path\"\\s+\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex LanguageValue = new("\"language\"\\s+\"([^\"]*)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <returns>Lower-case Steam language name ("english", "russian", ...) or null if unknown.</returns>
    public static string? TryRead()
    {
        try
        {
            var steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
            if (string.IsNullOrWhiteSpace(steamPath)) return null;

            foreach (var library in EnumerateLibraries(steamPath.Replace('/', '\\')))
            {
                var manifest = Path.Combine(library, "steamapps", $"appmanifest_{DotaAppId}.acf");
                if (!File.Exists(manifest)) continue;
                var language = ParseLanguage(File.ReadAllText(manifest));
                if (language is not null) return language;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Unknown, not wrong.
        }
        return null;
    }

    private static IEnumerable<string> EnumerateLibraries(string steamPath)
    {
        yield return steamPath;

        var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;
        foreach (Match match in LibraryPath.Matches(File.ReadAllText(vdf)))
        {
            var path = match.Groups[1].Value.Replace(@"\\", @"\");
            if (!string.Equals(path, steamPath, StringComparison.OrdinalIgnoreCase)) yield return path;
        }
    }

    /// <summary>The per-game language lives in the flat "UserConfig" block; "MountedConfig" is the fallback.</summary>
    internal static string? ParseLanguage(string manifest)
    {
        foreach (var block in new[] { "UserConfig", "MountedConfig" })
        {
            var start = manifest.IndexOf($"\"{block}\"", StringComparison.OrdinalIgnoreCase);
            if (start < 0) continue;
            var end = manifest.IndexOf('}', start);
            if (end < 0) continue;

            var match = LanguageValue.Match(manifest, start, end - start);
            if (!match.Success) continue;
            var value = match.Groups[1].Value.Trim().ToLowerInvariant();
            if (value.Length > 0 && value != "none") return value;
        }
        return null;
    }
}
