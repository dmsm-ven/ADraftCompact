using ADraftCompact.Models;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Windows.System.UserProfile;

namespace ADraftCompact.Services;

public sealed record EnvironmentReport(IReadOnlyList<string> Problems)
{
    public static EnvironmentReport Ok { get; } = new(Array.Empty<string>());

    public bool IsOk => Problems.Count == 0;

    /// <summary>Text for the startup warning dialog.</summary>
    public string ToWarningText()
    {
        var text = new StringBuilder("The application cannot work reliably on this system:\n");
        foreach (var problem in Problems) text.Append("\n• ").Append(problem);
        text.Append("\n\nThe window is disabled and will not hide to the tray. Fix the problems and restart the application.");
        return text.ToString();
    }

    /// <summary>Shorter text for the banner inside the main window.</summary>
    public string ToBannerText() => string.Join("\n", Problems.Select(problem => "• " + problem));
}

/// <summary>
/// Startup checks, so the user does not waste time on a tracker that can never detect the draft board:
/// English in Windows, the Windows OCR engine for hero names, Dota 2 language (only when Steam says it is not English),
/// and the screen resolution the layout was measured for.
/// Set <c>SkipEnvironmentChecks</c> to true in settings.json to bypass them.
/// </summary>
public sealed class EnvironmentCheckService
{
    private readonly HeroNameReader _heroNames;
    private readonly DraftLayoutProvider _layouts;
    private readonly ISettingsService _settings;
    private readonly ILogger<EnvironmentCheckService> _logger;

    public EnvironmentCheckService(HeroNameReader heroNames, DraftLayoutProvider layouts, ISettingsService settings,
        ILogger<EnvironmentCheckService> logger)
    {
        _heroNames = heroNames;
        _layouts = layouts;
        _settings = settings;
        _logger = logger;
    }

    public async Task<EnvironmentReport> RunAsync()
    {
        var settings = new AppSettings();
        try { settings = await _settings.LoadAsync(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _logger.LogWarning(ex, "Could not read settings for the startup checks; defaults are used");
        }

        if (settings.SkipEnvironmentChecks)
        {
            _logger.LogInformation("Startup checks are disabled in settings");
            return EnvironmentReport.Ok;
        }

        // OCR engine creation talks to WinRT; keep it off the UI thread.
        var report = await Task.Run(() => Check(settings));
        if (report.IsOk) _logger.LogInformation("Startup checks passed");
        else _logger.LogWarning("Startup checks failed: {Problems}", string.Join(" | ", report.Problems));
        return report;
    }

    private EnvironmentReport Check(AppSettings settings)
    {
        var problems = new List<string>();

        var ocrLanguage = "en-US";
        try { ocrLanguage = _layouts.Load().Matching.OcrLanguage; }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            _logger.LogWarning(ex, "Layout could not be loaded for the startup checks; {Language} is assumed for OCR", ocrLanguage);
        }

        // 1-2. English in Windows, then the OCR engine for it (without the language the engine cannot exist, so one message is enough).
        if (!HasEnglishLanguage())
            problems.Add("English is not installed in Windows. Add English (United States) in Settings → Time & language → Language & region.");
        else if (!IsOcrAvailable(ocrLanguage))
            problems.Add($"Windows OCR for {ocrLanguage} is not available, so hero names cannot be read. " +
                         "Open Settings → Time & language → Language & region → English (United States) → Language options and install \"Optical character recognition\".");

        // 3. The game language, only if Steam positively reports a non-English one.
        var gameLanguage = SteamGameLanguage.TryRead();
        _logger.LogInformation("Dota 2 language according to Steam: {Language}", gameLanguage ?? "unknown");
        if (gameLanguage is not null && gameLanguage != "english")
            problems.Add($"Dota 2 is set to \"{gameLanguage}\". Hero names are read in English: set the language to English in Steam → Dota 2 → Properties → General.");

        // 4. The resolution the layout was measured for.
        var width = GetSystemMetrics(SmCxScreen);
        var height = GetSystemMetrics(SmCyScreen);
        _logger.LogInformation("Primary screen: {Width}x{Height}, target: {TargetWidth}x{TargetHeight}",
            width, height, settings.TargetScreenWidth, settings.TargetScreenHeight);
        if (width > 0 && height > 0 && (width != settings.TargetScreenWidth || height != settings.TargetScreenHeight))
            problems.Add($"The screen resolution is {width}×{height}, but {settings.TargetScreenWidth}×{settings.TargetScreenHeight} is required: " +
                         "the draft board layout is measured for it. Change the display resolution and run Dota 2 at it.");

        return problems.Count == 0 ? EnvironmentReport.Ok : new EnvironmentReport(problems);
    }

    /// <summary>Any English variant in the user's language list. If the list cannot be read the answer is "yes": the OCR check decides.</summary>
    private bool HasEnglishLanguage()
    {
        try
        {
            return GlobalizationPreferences.Languages.Any(tag => tag.StartsWith("en", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not read the Windows language list");
            return true;
        }
    }

    private bool IsOcrAvailable(string languageTag)
    {
        try { return _heroNames.IsAvailable(languageTag); }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "OCR availability check failed");
            return false;
        }
    }

    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;

    /// <summary>Primary monitor size in real pixels (the process is per-monitor DPI aware, see GameWindowCapture.EnableDpiAwareness).</summary>
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
