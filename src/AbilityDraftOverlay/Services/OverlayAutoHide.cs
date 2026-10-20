using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using AbilityDraftOverlay.Models;
using Microsoft.Extensions.Logging;

namespace AbilityDraftOverlay.Services;

/// <summary>
/// Hides the overlay when the game process goes away. It reacts only to the running -> not running transition,
/// so an overlay the user opens by hand while the game is closed (e.g. to position it) is left alone.
/// Create and use on the UI thread; the event is raised on it too.
/// </summary>
public sealed class OverlayAutoHide
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    private readonly ISettingsService _settings;
    private readonly ILogger<OverlayAutoHide> _logger;
    private readonly DispatcherTimer _timer = new() { Interval = PollInterval };

    private string _processName = "dota2";
    private bool _gameWasRunning;

    public OverlayAutoHide(ISettingsService settings, ILogger<OverlayAutoHide> logger)
    {
        _settings = settings;
        _logger = logger;
        _timer.Tick += (_, _) => Poll();
    }

    /// <summary>The game was closed: hide the overlay.</summary>
    public event Action? HideRequested;

    /// <summary>When true nothing is hidden (the user is dragging the overlay in the settings window).</summary>
    public Func<bool>? IsSuspended { get; set; }

    public async Task StartAsync()
    {
        var settings = new AppSettings();
        try { settings = await _settings.LoadAsync(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _logger.LogWarning(ex, "Could not read settings; the default overlay auto-hide values are used");
        }

        if (!settings.OverlayAutoHide)
        {
            _logger.LogInformation("Overlay auto-hide is disabled in settings");
            return;
        }

        _processName = string.IsNullOrWhiteSpace(settings.GameProcessName) ? "dota2" : settings.GameProcessName.Trim();
        _gameWasRunning = IsGameRunning();
        _timer.Start();
        _logger.LogInformation("Overlay auto-hide started: process {Process}, game running: {Running}", _processName, _gameWasRunning);
    }

    private void Poll()
    {
        var running = IsGameRunning();
        if (_gameWasRunning && !running)
        {
            _logger.LogInformation("The game was closed");
            if (IsSuspended?.Invoke() != true) HideRequested?.Invoke();
        }
        _gameWasRunning = running;
    }

    private bool IsGameRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName(_processName);
            var running = processes.Length > 0;
            foreach (var process in processes) process.Dispose();
            return running;
        }
        catch (InvalidOperationException)
        {
            return _gameWasRunning; // cannot tell: keep the previous answer instead of hiding the overlay on a hiccup
        }
    }
}
