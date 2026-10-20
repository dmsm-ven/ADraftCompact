using AbilityDraftOverlay.Models;
using AbilityDraftOverlay.Views;
using AbilityDraftOverlay.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;

namespace AbilityDraftOverlay.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DotaAssetsDownloader _assetsDownloader;
    private readonly DraftSessionTracker _tracker;
    private readonly AutoTrackingService _autoTracking;
    private string? _lastTickStatus;
    private readonly IconIndex _iconIndex;
    private readonly ILogger<MainViewModel> _logger;
    private readonly OverlayWindow _overlayWindow;
    private readonly OverlaySettingsService _overlaySettingsService;
    [ObservableProperty] private bool isOverlayVisible;

    private readonly DispatcherTimer _countdownTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private const int CountdownBarCount = 10;

    [ObservableProperty] private string statusText = "Ready.";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(RightStatusText))] private string resourceSummary = "Assets: checking...";
    [ObservableProperty] private bool isTracking;
    [ObservableProperty] private string sessionText = "Session: none";
    /// <summary>"Next screenshot in 25 sec |||||||||" while auto-tracking runs, otherwise null.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(RightStatusText))] private string? countdownText;
    /// <summary>False when the startup checks failed: the whole window is disabled except the title-bar buttons.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(EnvironmentBannerVisibility))] private bool isEnvironmentOk = true;
    [ObservableProperty] private string environmentWarning = string.Empty;
    public ObservableCollection<DraftEvent> Events { get; } = new();
    /// <summary>Right part of the status bar: the screenshot countdown while tracking, the assets summary otherwise.</summary>
    public string RightStatusText => CountdownText ?? ResourceSummary;
    public Visibility EnvironmentBannerVisibility => IsEnvironmentOk ? Visibility.Collapsed : Visibility.Visible;
    public string TrackingButtonText => IsTracking ? "■ Stop tracking" : "▶ Start tracking";
    public string OverlayButtonText => IsOverlayVisible ? "▣ Hide overlay" : "▣ Show overlay";

    public MainViewModel(DotaAssetsDownloader assetsDownloader,
        DraftSessionTracker tracker, AutoTrackingService autoTracking, IconIndex iconIndex, OverlayWindow overlayWindow, OverlaySettingsService overlaySettingsService, ILogger<MainViewModel> logger)
    {
        _assetsDownloader = assetsDownloader;
        _tracker = tracker;
        _autoTracking = autoTracking;
        _autoTracking.Ticked += OnTick;
        _iconIndex = iconIndex;
        _logger = logger;
        _overlayWindow = overlayWindow;
        _overlaySettingsService = overlaySettingsService;
        _ = LoadOverlaySettingsAsync();
        _overlayWindow.UpdateSession(_tracker.Current);
        _overlayWindow.Closed += (_, _) => { IsOverlayVisible = false; OnPropertyChanged(nameof(OverlayButtonText)); };
        _countdownTimer.Tick += (_, _) => UpdateCountdown();
        UpdateResourceSummary();
    }

    partial void OnIsTrackingChanged(bool value)
    {
        OnPropertyChanged(nameof(TrackingButtonText));
        if (value)
        {
            UpdateCountdown();
            _countdownTimer.Start();
        }
        else
        {
            _countdownTimer.Stop();
            CountdownText = null;
        }
    }

    /// <summary>Applies the result of the startup checks: a failed check disables the window and shows why.</summary>
    public void ApplyEnvironment(EnvironmentReport report)
    {
        EnvironmentWarning = report.IsOk ? string.Empty : report.ToBannerText();
        IsEnvironmentOk = report.IsOk;
        if (!report.IsOk) StatusText = "Startup checks failed. Fix the problems listed above and restart the application.";
    }

    private void UpdateCountdown()
    {
        var schedule = _autoTracking.Schedule;
        CountdownText = schedule is null ? null : FormatCountdown(schedule.NextAt - DateTimeOffset.Now, schedule.Interval);
    }

    /// <summary>"Next screenshot in 25 sec |||||||||": whole seconds left and a 10-step bar that shrinks as the time runs out.</summary>
    internal static string FormatCountdown(TimeSpan remaining, TimeSpan interval)
    {
        var left = Math.Max(0, remaining.TotalSeconds);
        var fraction = interval.TotalSeconds > 0 ? Math.Clamp(left / interval.TotalSeconds, 0, 1) : 0;
        var bars = (int)Math.Ceiling(fraction * CountdownBarCount);
        return $"Next screenshot in {(int)Math.Ceiling(left)} sec {new string('|', bars)}";
    }

    [RelayCommand]
    private void ToggleOverlay()
    {
        if (IsOverlayVisible)
        {
            _overlayWindow.Hide();
            IsOverlayVisible = false;
        }
        else
        {
            _overlayWindow.UpdateSession(_tracker.Current);
            if (!_overlayWindow.IsVisible) _overlayWindow.Show();
            IsOverlayVisible = true;
        }
        OnPropertyChanged(nameof(OverlayButtonText));
    }

    private async Task LoadOverlaySettingsAsync()
    {
        var settings = await _overlaySettingsService.LoadAsync();
        await Application.Current.Dispatcher.InvokeAsync(() => _overlayWindow.ApplySettings(settings));
    }

    [RelayCommand]
    private void OpenOverlaySettings()
    {
        var window = new OverlaySettingsWindow(_overlayWindow, _overlaySettingsService) { Owner = Application.Current.MainWindow };
        window.ShowDialog();
        OnPropertyChanged(nameof(OverlayButtonText));
    }

    [RelayCommand]
    private async Task DownloadResourcesAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        AddEvent(DraftEventKind.Resource, "download", "Checking asset source availability.");
        _logger.LogInformation("User requested asset download");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            bool available;
            try { available = await _assetsDownloader.CheckAvailabilityAsync(timeout.Token); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(ex, "OpenDota availability check failed");
                available = false;
            }
            if (!available)
            {
                StatusText = "Asset sources are unavailable. Download cancelled; check your network connection.";
                AddEvent(DraftEventKind.Error, "resource-check", StatusText);
                _logger.LogWarning("Download cancelled: OpenDota is unavailable");
                return;
            }

            StatusText = "Downloading assets...";
            var result = await _assetsDownloader.DownloadAsync(message => Application.Current.Dispatcher.Invoke(() => StatusText = message));
            StatusText = $"Download complete: {result.Downloaded} new, {result.Skipped} skipped, {result.Failed} failed.";
            AddEvent(result.Failed > 0 ? DraftEventKind.Error : DraftEventKind.Resource, "assets", StatusText);
            _logger.LogInformation("{Summary}", StatusText);
            _iconIndex.Invalidate(); // new icons -> rebuild the descriptor index on next recognition
            UpdateResourceSummary();
        }
        catch (Exception ex)
        {
            StatusText = $"Asset download failed: {ex.Message}";
            AddEvent(DraftEventKind.Error, "download", StatusText);
            _logger.LogError(ex, "Asset download failed");
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ToggleTrackingAsync()
    {
        if (IsTracking)
        {
            await _autoTracking.StopAsync();
            IsTracking = false;
            StatusText = "Auto-tracking stopped.";
            AddEvent(DraftEventKind.Resource, "tracking", StatusText);
            return;
        }

        try
        {
            _lastTickStatus = null;
            await _autoTracking.StartAsync();
            IsTracking = true;
            StatusText = "Auto-tracking started: the first capture is immediate, then one capture per minute.";
            AddEvent(DraftEventKind.Resource, "tracking", StatusText);
        }
        catch (Exception ex)
        {
            StatusText = $"Could not start auto-tracking: {ex.Message}";
            AddEvent(DraftEventKind.Error, "tracking", StatusText);
            _logger.LogError(ex, "Could not start auto-tracking");
        }
    }

    /// <summary>Called on a worker thread after every automatic capture.</summary>
    private void OnTick(TrackingTick tick) => Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
    {
        ShowUpdate(tick.Snapshot, tick.Update);
        StatusText = $"{tick.At:HH:mm:ss} · {tick.Status}";
        // Identical repeated messages ("game window not found" every minute) are shown once.
        if (tick.Status != _lastTickStatus && tick.Update is null)
            AddEvent(tick.IsError ? DraftEventKind.Error : DraftEventKind.Resource, "tracking", tick.Status);
        _lastTickStatus = tick.Status;
    }));

    /// <summary>Puts the result of one recognized frame (manual or automatic) into the event list and the session line.</summary>
    private void ShowUpdate(DraftSnapshot? snapshot, SessionUpdate? update)
    {
        if (update is null) return;

        if (update.EndedSession is { } ended)
            AddEvent(DraftEventKind.Resource, "session",
                $"Session {ended.Id} ended: {ended.Captures} captures, {ended.TotalPicks} picks. File: {SessionStore.PathFor(ended)}");
        if (update is { Started: true, Session: { } started })
            AddEvent(DraftEventKind.Resource, "session", $"New session {started.Id} · {started.StartedAt:yyyy-MM-dd HH:mm:ss}");

        foreach (var pick in update.NewPicks)
            AddEvent(pick.Type == SlotType.Hero ? DraftEventKind.Hero : DraftEventKind.Ability, pick.EntityId,
                $"Player {pick.Player}: pick #{pick.PickOrder} — {(pick.Type == SlotType.Hero ? "hero" : "ability")} {pick.DisplayName}" +
                $"{(pick.OrderUncertain ? " (order estimated)" : "")} · capture #{pick.Capture}", pick.Player);

        if (snapshot is { BoardDetected: true } && (update.NewPicks.Count > 0 || update.Started))
            AddEvent(DraftEventKind.Frame, "report", DraftReportFormatter.Format(snapshot));

        SessionText = update.Session is { } session
            ? $"Session {session.Id} · since {session.StartedAt:MM-dd HH:mm:ss} · captures {session.Captures} · picks {session.TotalPicks}"
            : "Session: none";
        _overlayWindow.UpdateSession(update.Session);
    }

    [RelayCommand]
    private void ClearEvents()
    {
        Events.Clear();
        _tracker.EndSession(DateTimeOffset.Now, "Log cleared"); // the next recognized board starts a new session
        SessionText = "Session: none";
        _overlayWindow.UpdateSession(null);
    }

    public void UpdateResourceSummary()
    {
        try
        {
            var info = DotaAssetsDownloader.GetStorageInfo();
            ResourceSummary = $"Assets: {info.FileCount:N0} files · {FormatBytes(info.TotalBytes)}";
            _logger.LogInformation("Local assets: {Count} files, {Bytes} bytes", info.FileCount, info.TotalBytes);
        }
        catch (Exception ex)
        {
            ResourceSummary = "Assets: could not read directory";
            _logger.LogWarning(ex, "Failed to count local assets");
        }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824d:F2} GB",
        >= 1_048_576 => $"{bytes / 1_048_576d:F1} MB",
        >= 1_024 => $"{bytes / 1_024d:F1} KB",
        _ => $"{bytes} B"
    };

    private void AddEvent(DraftEventKind kind, string source, string message, int player = 0) =>
        Events.Insert(0, new DraftEvent(Events.Count + 1, player, kind, source, message, DateTimeOffset.Now));
}
