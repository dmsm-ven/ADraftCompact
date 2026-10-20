using AbilityDraftOverlay.Models;
using Microsoft.Extensions.Logging;

namespace AbilityDraftOverlay.Services;

/// <param name="Status">Short text for the status line.</param>
/// <param name="Update">Null when no frame was recognized (game window missing, covered, ...).</param>
public sealed record TrackingTick(DateTimeOffset At, string Status, DraftSnapshot? Snapshot, SessionUpdate? Update, bool IsError);

/// <summary>When the next automatic capture is due and how long the pause between captures is.</summary>
public sealed record CaptureSchedule(DateTimeOffset NextAt, TimeSpan Interval);

/// <summary>
/// Takes a screenshot of the game window every <see cref="AppSettings.CaptureIntervalSeconds"/> seconds
/// (the first one immediately), recognizes it and feeds the draft session tracker.
/// One minute is about one pass over the board (players 1..10, then 10..1), so there is no need to read every turn.
/// </summary>
public sealed class AutoTrackingService
{
    private readonly GameWindowCapture _capture;
    private readonly ScreenshotRecognitionService _recognizer;
    private readonly DraftSessionTracker _tracker;
    private readonly DraftLayoutProvider _layouts;
    private readonly HeroNameReader _heroNames;
    private readonly ISettingsService _settings;
    private readonly ILogger<AutoTrackingService> _logger;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private volatile CaptureSchedule? _schedule;

    public AutoTrackingService(GameWindowCapture capture, ScreenshotRecognitionService recognizer, DraftSessionTracker tracker,
        DraftLayoutProvider layouts, HeroNameReader heroNames, ISettingsService settings, ILogger<AutoTrackingService> logger)
    {
        _capture = capture;
        _recognizer = recognizer;
        _tracker = tracker;
        _layouts = layouts;
        _heroNames = heroNames;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Raised on a worker thread after every capture attempt.</summary>
    public event Action<TrackingTick>? Ticked;

    public bool IsRunning { get { lock (_gate) return _cts is not null; } }

    /// <summary>Null while tracking is stopped. Read from the UI thread to show the countdown to the next screenshot.</summary>
    public CaptureSchedule? Schedule => _schedule;

    public async Task StartAsync()
    {
        lock (_gate)
            if (_cts is not null) return;

        // Fail now, with a clear message, instead of silently never finding the board.
        var layout = _layouts.Load();
        if (!_heroNames.IsAvailable(layout.Matching.OcrLanguage))
            throw new InvalidOperationException(
                $"Auto-tracking requires Windows OCR for {layout.Matching.OcrLanguage} to detect the Ability Draft board from hero names. " +
                "Install English (United States) in Windows language settings.");

        var settings = await _settings.LoadAsync();
        var interval = TimeSpan.FromSeconds(Math.Clamp(settings.CaptureIntervalSeconds, 10, 600));
        var processName = string.IsNullOrWhiteSpace(settings.GameProcessName) ? "dota2" : settings.GameProcessName.Trim();

        lock (_gate)
        {
            if (_cts is not null) return;
            _cts = new CancellationTokenSource();
            _schedule = new CaptureSchedule(DateTimeOffset.Now, interval); // the first capture is due right now
            var token = _cts.Token;
            _loop = Task.Run(() => RunAsync(processName, interval, token));
        }
        _logger.LogInformation("Auto-tracking started: process {Process}, interval {Interval}s", processName, interval.TotalSeconds);
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        Task? loop;
        lock (_gate)
        {
            if (_cts is null) return;
            cts = _cts;
            loop = _loop;
            _cts = null;
            _loop = null;
        }

        cts.Cancel();
        try { if (loop is not null) await loop; }
        catch (OperationCanceledException) { }
        finally
        {
            cts.Dispose(); // only after the loop has finished using the token
            _schedule = null;
        }
        _logger.LogInformation("Auto-tracking stopped");
    }

    private async Task RunAsync(string processName, TimeSpan interval, CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(interval);
            var due = DateTimeOffset.Now; // the first capture is immediate
            do
            {
                Tick(processName, token);

                // PeriodicTimer keeps a fixed cadence, so the next capture is one interval after the previous due time
                // (re-anchored to "now" if a capture ever took longer than the interval).
                due += interval;
                var now = DateTimeOffset.Now;
                if (due < now) due = now;
                _schedule = new CaptureSchedule(due, interval);
            }
            while (await timer.WaitForNextTickAsync(token));
        }
        catch (OperationCanceledException)
        {
            // normal stop
        }
    }

    private void Tick(string processName, CancellationToken token)
    {
        var now = DateTimeOffset.Now;
        try
        {
            var outcome = _capture.Capture(processName);
            if (outcome.Status == GameWindowCapture.InactiveWindowStatus)
                return;

            if (outcome.Frame is null)
            {
                _logger.LogInformation("Capture skipped: {Status}", outcome.Status);
                Publish(new TrackingTick(now, outcome.Status, null, null, IsError: false));
                return;
            }

            using var frame = outcome.Frame;
            var snapshot = _recognizer.Recognize(frame, $"game window {outcome.Status}", token);
            var update = _tracker.Process(snapshot, now);
            Publish(new TrackingTick(now, update.Message, snapshot, update, IsError: false));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // One bad capture must not stop the tracking; the next one may work.
            _logger.LogError(ex, "Failed to process capture");
            Publish(new TrackingTick(now, $"Error: {ex.Message}", null, null, IsError: true));
        }
    }

    private void Publish(TrackingTick tick)
    {
        try { Ticked?.Invoke(tick); }
        catch (Exception ex) { _logger.LogError(ex, "Auto-tracking event handler failed"); }
    }
}
