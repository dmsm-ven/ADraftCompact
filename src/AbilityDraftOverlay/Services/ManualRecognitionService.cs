using System.IO;
using AbilityDraftOverlay.Models;
using Microsoft.Extensions.Logging;

namespace AbilityDraftOverlay.Services;

/// <param name="Status">Short text for the status line.</param>
/// <param name="Update">Null when no frame was recognized (game window missing, covered, ...).</param>
public sealed record RecognitionOutcome(DateTimeOffset At, string Status, DraftSnapshot? Snapshot, SessionUpdate? Update, bool IsError);

/// <summary>
/// One recognition on demand: screenshot of the game window, recognition, draft session update.
/// It is two steps because the caller has to get its own windows (main window, overlay) out of the way
/// between "capture" and "recognize": the screenshot is a plain copy of the screen, so anything on top of the game ends up in it.
/// </summary>
public sealed class ManualRecognitionService
{
    private readonly GameWindowCapture _capture;
    private readonly ScreenshotRecognitionService _recognizer;
    private readonly DraftSessionTracker _tracker;
    private readonly ISettingsService _settings;
    private readonly ILogger<ManualRecognitionService> _logger;

    public ManualRecognitionService(GameWindowCapture capture, ScreenshotRecognitionService recognizer, DraftSessionTracker tracker,
        ISettingsService settings, ILogger<ManualRecognitionService> logger)
    {
        _capture = capture;
        _recognizer = recognizer;
        _tracker = tracker;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Takes the screenshot. The frame inside the outcome (if any) is owned by the caller and handed to <see cref="Recognize"/>.</summary>
    public async Task<CaptureOutcome> CaptureAsync()
    {
        var settings = new AppSettings();
        try { settings = await _settings.LoadAsync(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _logger.LogWarning(ex, "Could not read settings; the default game process name is used");
        }

        var processName = string.IsNullOrWhiteSpace(settings.GameProcessName) ? "dota2" : settings.GameProcessName.Trim();
        return await Task.Run(() => _capture.Capture(processName));
    }

    /// <summary>Recognizes a captured frame (CPU-bound: call it off the UI thread) and disposes the frame.</summary>
    public RecognitionOutcome Recognize(CaptureOutcome capture)
    {
        var now = DateTimeOffset.Now;
        try
        {
            if (capture.Frame is null)
            {
                _logger.LogInformation("Capture skipped: {Status}", capture.Status);
                return new RecognitionOutcome(now, capture.Status, null, null, IsError: false);
            }

            using var frame = capture.Frame;
            var snapshot = _recognizer.Recognize(frame, $"game window {capture.Status}");
            var update = _tracker.Process(snapshot, now);
            return new RecognitionOutcome(now, update.Message, snapshot, update, IsError: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recognition failed");
            capture.Frame?.Dispose();
            return new RecognitionOutcome(now, $"Error: {ex.Message}", null, null, IsError: true);
        }
    }
}
