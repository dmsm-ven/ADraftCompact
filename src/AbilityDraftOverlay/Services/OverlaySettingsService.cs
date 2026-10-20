using System.IO;
using System.Text.Json;
using AbilityDraftOverlay.Models;

namespace AbilityDraftOverlay.Services;

public sealed class OverlaySettingsService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static string SettingsPath => AppPaths.OverlaySettingsFile;

    public async Task<OverlaySettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new OverlaySettings();
            await using var stream = File.OpenRead(SettingsPath);
            return await JsonSerializer.DeserializeAsync<OverlaySettings>(stream, Options, cancellationToken) ?? new OverlaySettings();
        }
        catch { return new OverlaySettings(); }
    }

    public async Task SaveAsync(OverlaySettings settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(AppPaths.Root);
        await using var stream = File.Create(SettingsPath);
        await JsonSerializer.SerializeAsync(stream, settings, Options, cancellationToken);
    }
}
