using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using AbilityDraftOverlay.Models;
using Microsoft.Extensions.Logging;

namespace AbilityDraftOverlay.Services;

/// <summary>
/// Shows and hides the overlay on its own:
/// - hides it when the game process goes away (only on the running -> not running transition, so an overlay the user
///   opens by hand while the game is closed, e.g. to position it, is left alone);
/// - shows it <see cref="AppSettings.OverlayAutoShowDelaySeconds"/> (45 by default) after the draft board is first detected,
///   once per draft: if the user hides it afterwards it stays hidden until the next draft or the next game launch.
/// Create and use on the UI thread; the events are raised on it too.
/// </summary>
public sealed class OverlayAutoVisibility
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ProcessCheckInterval = TimeSpan.FromSeconds(3);

    private readonly ISettingsService _settings;
    private readonly ILogger<OverlayAutoVisibility> _logger;
    private readonly DispatcherTimer _timer = new() { Interval = PollInterval };

    private bool _enabled;
    private string _processName = "dota2";
    private TimeSpan _showDelay = TimeSpan.FromSeconds(45);
    private DateTimeOffset? _boardSeenAt;
    private bool _shownForThisDraft;
    private bool _gameWasRunning;
    private DateTimeOffset _nextProcessCheck = DateTimeOffset.MinValue;

    public OverlayAutoVisibility(ISettingsService settings, ILogger<OverlayAutoVisibility> logger)
    {
        _settings = settings;
        _logger = logger;
        _timer.Tick += (_, _) => Poll();
    }

    /// <summary>Time to display the overlay (the delay after the board was detected has passed).</summary>
    public event Action? ShowRequested;

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
            _logger.LogWarning(ex, "Could not read settings; the default overlay auto show/hide values are used");
        }
        _enabled = settings.OverlayAutoVisibility;
        _processName = string.IsNullOrWhiteSpace(settings.GameProcessName) ? "dota2" : settings.GameProcessName.Trim();
        _showDelay = TimeSpan.FromSeconds(Math.Clamp(settings.OverlayAutoShowDelaySeconds, 0, 600));
        if (!_enabled)
        {
            _logger.LogInformation("Overlay auto show/hide is disabled in settings");
            return;
        }

        _gameWasRunning = IsGameRunning();
        _timer.Start();
        _logger.LogInformation("Overlay auto show/hide started: process {Process}, show delay {Delay}s, game running: {Running}",
            _processName, _showDelay.TotalSeconds, _gameWasRunning);
    }

    /// <summary>Feed every automatic capture here (UI thread).</summary>
    public void OnTick(TrackingTick tick)
    {
        if (!_enabled) return;

        // A new draft (or the end of the previous one) starts over: the overlay may be shown once more.
        if (tick.Update is { Started: true } or { EndedSession: not null })
        {
            _shownForThisDraft = false;
            _boardSeenAt = null;
        }

        if (tick.Snapshot is not { BoardDetected: true }) return;
        if (_shownForThisDraft || _boardSeenAt is not null) return;

        _boardSeenAt = tick.At;
        _logger.LogInformation("Draft board detected; the overlay will be shown in {Delay}s", _showDelay.TotalSeconds);
    }

    private void Poll()
    {
        var now = DateTimeOffset.Now;

        if (now >= _nextProcessCheck)
        {
            _nextProcessCheck = now + ProcessCheckInterval;
            var running = IsGameRunning();
            if (_gameWasRunning && !running)
            {
                _logger.LogInformation("The game was closed");
                _boardSeenAt = null;
                _shownForThisDraft = false;
                if (IsSuspended?.Invoke() != true) HideRequested?.Invoke();
            }
            _gameWasRunning = running;
        }

        if (_boardSeenAt is { } seen && !_shownForThisDraft && now - seen >= _showDelay)
        {
            _shownForThisDraft = true;
            _boardSeenAt = null;
            _logger.LogInformation("Showing the overlay {Delay}s after the board was detected", _showDelay.TotalSeconds);
            ShowRequested?.Invoke();
        }
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
