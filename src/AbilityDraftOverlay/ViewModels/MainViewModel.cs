using ADraftCompact.Models;
using ADraftCompact.Views;
using ADraftCompact.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;

namespace ADraftCompact.ViewModels;

public partial class MainViewModel : ObservableObject
{
    /// <summary>Time for the desktop compositor to repaint the screen after our own windows were taken away.</summary>
    private static readonly TimeSpan RepaintDelay = TimeSpan.FromMilliseconds(200);

    private readonly DotaAssetsDownloader _assetsDownloader;
    private readonly DraftSessionTracker _tracker;
    private readonly ManualRecognitionService _recognition;
    private readonly OverlayAutoHide _autoHide;
    private readonly HotkeyService _hotkeys;
    private readonly IconIndex _iconIndex;
    private readonly ILogger<MainViewModel> _logger;
    private readonly OverlayWindow _overlayWindow;
    private readonly OverlaySettingsService _overlaySettingsService;
    private bool _isSettingsOpen;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(OverlayButtonText), nameof(OverlayButtonIcon))] private bool isOverlayVisible;
    [ObservableProperty] private string statusText = "Ready.";
    [ObservableProperty] private bool isBusy;
    /// <summary>False until the hero and ability icons are downloaded: recognition is impossible without them.</summary>
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(RecognizeAbilitiesCommand))] private bool hasIcons;
    [ObservableProperty] private string resourceSummary = "Assets: checking...";
    /// <summary>Progress and result of the asset download, shown next to the download button in the overlay settings.</summary>
    [ObservableProperty] private string assetsStatusText = string.Empty;
    [ObservableProperty] private string sessionText = "Session: none";
    /// <summary>False when the startup checks failed: the whole window is disabled except the title-bar buttons.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(EnvironmentBannerVisibility))] private bool isEnvironmentOk = true;
    [ObservableProperty] private string environmentWarning = string.Empty;

    public ObservableCollection<DraftEvent> Events { get; } = new();
    public Visibility EnvironmentBannerVisibility => IsEnvironmentOk ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Started with <c>--debug-mode</c>: the event log is shown.</summary>
    public bool IsDebugMode => AppOptions.DebugMode;
    public Visibility LogVisibility => IsDebugMode ? Visibility.Visible : Visibility.Collapsed;

    public string OverlayButtonText => IsOverlayVisible ? "Hide overlay" : "Show overlay";
    /// <summary>Segoe MDL2 Assets glyph: "Hide" while the overlay is shown, "View" while it is hidden.</summary>
    public string OverlayButtonIcon => IsOverlayVisible ? "" : "";

    public MainViewModel(DotaAssetsDownloader assetsDownloader, DraftSessionTracker tracker, ManualRecognitionService recognition,
        OverlayAutoHide autoHide, HotkeyService hotkeys, IconIndex iconIndex, OverlayWindow overlayWindow,
        OverlaySettingsService overlaySettingsService, ILogger<MainViewModel> logger)
    {
        _assetsDownloader = assetsDownloader;
        _tracker = tracker;
        _recognition = recognition;
        _hotkeys = hotkeys;
        _hotkeys.Pressed += OnHotkeyPressed;
        _autoHide = autoHide;
        _autoHide.IsSuspended = () => _overlayWindow.IsPositionEditing;
        _autoHide.HideRequested += () => SetOverlayVisible(false, "Overlay hidden automatically: the game was closed.");
        _iconIndex = iconIndex;
        _logger = logger;
        _overlayWindow = overlayWindow;
        _overlaySettingsService = overlaySettingsService;
        _ = LoadOverlaySettingsAsync();
        if (_tracker.Current is null && LastBoardStore.Load() is { } lastBoard) _tracker.Restore(lastBoard);
        _overlayWindow.UpdateSession(_tracker.Current);
        _overlayWindow.Closed += (_, _) => IsOverlayVisible = false;
        UpdateResourceSummary();
    }

    /// <summary>Applies the result of the startup checks: a failed check disables the window and shows why.</summary>
    public void ApplyEnvironment(EnvironmentReport report)
    {
        EnvironmentWarning = report.IsOk ? string.Empty : report.ToBannerText();
        IsEnvironmentOk = report.IsOk;
        if (!report.IsOk) StatusText = "Startup checks failed. Fix the problems listed above and restart the application.";
    }

    /// <summary>Starts what runs in the background (hotkey, overlay auto-hide). Called only after the startup checks passed.</summary>
    public async Task StartBackgroundFeaturesAsync()
    {
        var settings = await _overlaySettingsService.LoadAsync();
        var hotkey = string.IsNullOrWhiteSpace(settings.RecognizeHotkey) ? HotkeyService.DefaultHotkey : settings.RecognizeHotkey;
        if (!_hotkeys.TryRegister(hotkey, out var error))
        {
            StatusText = $"Hotkey {hotkey} is not available: {error} Change it in the overlay settings.";
            AddEvent(DraftEventKind.Error, "hotkey", StatusText);
        }
        await _autoHide.StartAsync();
    }

    private void OnHotkeyPressed()
    {
        // While the settings dialog is open the main window cannot be hidden for a screenshot (the dialog belongs to it).
        if (!IsEnvironmentOk || IsBusy || _isSettingsOpen) return;
        if (RecognizeAbilitiesCommand.CanExecute(null)) RecognizeAbilitiesCommand.Execute(null);
    }

    [RelayCommand]
    private void ToggleOverlay() => SetOverlayVisible(!IsOverlayVisible);

    /// <summary>Shows or hides the overlay; <paramref name="reason"/> (automatic changes only) goes to the event log.</summary>
    private void SetOverlayVisible(bool visible, string? reason = null)
    {
        if (visible == IsOverlayVisible) return;
        if (visible)
        {
            _overlayWindow.UpdateSession(_tracker.Current);
            if (!_overlayWindow.IsVisible) _overlayWindow.Show();
        }
        else
        {
            _overlayWindow.Hide();
        }
        IsOverlayVisible = visible;
        if (reason is not null) AddEvent(DraftEventKind.Resource, "overlay", reason);
    }

    private async Task LoadOverlaySettingsAsync()
    {
        var settings = await _overlaySettingsService.LoadAsync();
        await Application.Current.Dispatcher.InvokeAsync(() => _overlayWindow.ApplySettings(settings));
    }

    [RelayCommand]
    private void OpenOverlaySettings()
    {
        var window = new OverlaySettingsWindow(_overlayWindow, _overlaySettingsService, _hotkeys, this) { Owner = Application.Current.MainWindow };
        _isSettingsOpen = true;
        try { window.ShowDialog(); }
        finally { _isSettingsOpen = false; }
    }

    /// <summary>
    /// Screenshot of the game, recognition of every hero and ability on the draft board, then the overlay is updated and shown.
    /// Meant to be used once all players have picked everything. Also bound to the hotkey.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasIcons))]
    private async Task RecognizeAbilitiesAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = "Recognizing abilities...";
        try
        {
            CaptureOutcome capture;
            var restoreWindows = HideOwnWindows();
            try
            {
                // The screenshot is a copy of what is on the screen: our windows must not be on top of the game.
                await Task.Delay(RepaintDelay);
                capture = await _recognition.CaptureAsync();
            }
            finally { restoreWindows(); }

            var outcome = await Task.Run(() => _recognition.Recognize(capture));
            ShowOutcome(outcome);
        }
        catch (Exception ex)
        {
            StatusText = $"Recognition failed: {ex.Message}";
            AddEvent(DraftEventKind.Error, "recognize", StatusText);
            _logger.LogError(ex, "Recognition failed");
        }
        finally { IsBusy = false; }
    }

    private void ShowOutcome(RecognitionOutcome outcome)
    {
        ShowUpdate(outcome.Snapshot, outcome.Update);
        StatusText = $"{outcome.At:HH:mm:ss} · {outcome.Status}";
        AddEvent(outcome.IsError ? DraftEventKind.Error : DraftEventKind.Resource, "recognize", outcome.Status);

        // The user asked for the result: show it.
        if (outcome.Snapshot is { BoardDetected: true }) SetOverlayVisible(true);
    }

    /// <summary>Takes the main window and the overlay out of the way for the screenshot; the returned action brings them back.</summary>
    private Action HideOwnWindows()
    {
        var main = Application.Current.MainWindow;
        var mainWasVisible = main is { IsVisible: true };
        var overlayWasVisible = _overlayWindow.IsVisible;

        if (overlayWasVisible) _overlayWindow.Opacity = 0; // stays "shown" for the window manager, only not painted
        if (mainWasVisible) main!.Hide();

        return () =>
        {
            if (overlayWasVisible) _overlayWindow.Opacity = 1;
            if (mainWasVisible) main!.Show();
        };
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
            AssetsStatusText = "Checking asset sources...";
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
                AssetsStatusText = "Asset sources are unavailable. Check your network connection.";
                AddEvent(DraftEventKind.Error, "resource-check", AssetsStatusText);
                _logger.LogWarning("Download cancelled: OpenDota is unavailable");
                ShowNetworkWarning();
                return;
            }

            // Progress text goes to the overlay settings window only; the main window just shows the file count.
            var result = await _assetsDownloader.DownloadAsync(message => Application.Current.Dispatcher.Invoke(() =>
            {
                AssetsStatusText = message;
                if (message.StartsWith("Downloading images", StringComparison.Ordinal)) UpdateResourceSummary(keepAssetsStatus: true);
            }));
            var summary = $"Download complete: {result.Downloaded} new, {result.Skipped} skipped, {result.Failed} failed.";
            AddEvent(result.Failed > 0 ? DraftEventKind.Error : DraftEventKind.Resource, "assets", summary);
            _logger.LogInformation("{Summary}", summary);
            _iconIndex.Invalidate(); // new icons -> rebuild the descriptor index on next recognition
            UpdateResourceSummary();
            AssetsStatusText = summary;
            if (result.Failed > 0 && !HasIcons) ShowNetworkWarning();
        }
        catch (Exception ex)
        {
            AssetsStatusText = $"Asset download failed: {ex.Message}";
            AddEvent(DraftEventKind.Error, "download", AssetsStatusText);
            _logger.LogError(ex, "Asset download failed");
            if (ex is HttpRequestException or TaskCanceledException) ShowNetworkWarning();
        }
        finally { IsBusy = false; }
    }

    /// <summary>Puts the result of one recognized frame into the event list, the session line and the overlay.</summary>
    private void ShowUpdate(DraftSnapshot? snapshot, SessionUpdate? update)
    {
        if (update is null) return;

        if (update.EndedSession is { } ended)
            AddEvent(DraftEventKind.Resource, "session",
                $"Session {ended.Id} ended: {ended.Captures} captures, {ended.TotalPicks} picks.");
        if (update is { Started: true, Session: { } started })
            AddEvent(DraftEventKind.Resource, "session", $"New session {started.Id} · {started.StartedAt:yyyy-MM-dd HH:mm:ss}");

        foreach (var pick in update.NewPicks)
            AddEvent(pick.Type == SlotType.Hero ? DraftEventKind.Hero : DraftEventKind.Ability, pick.EntityId,
                $"Player {pick.Player}: {(pick.Type == SlotType.Hero ? "hero" : "ability")} {pick.DisplayName}", pick.Player);

        if (snapshot is { BoardDetected: true })
            AddEvent(DraftEventKind.Frame, "report", DraftReportFormatter.Format(snapshot));

        SessionText = update.Session is { } session
            ? $"Session {session.Id} · since {session.StartedAt:MM-dd HH:mm:ss} · captures {session.Captures} · picks {session.TotalPicks}"
            : "Session: none";
        _overlayWindow.UpdateSession(update.Session);
        if (update.Session is { } current) LastBoardStore.Save(current);
    }

    private static void ShowNetworkWarning() => MessageBox.Show(
        "The resources could not be downloaded: OpenDota or the Steam CDN is not reachable from your network.\n\n" +
        "Please connect to another network or use a VPN, then press Download assets again.",
        "Ability Draft Overlay", MessageBoxButton.OK, MessageBoxImage.Warning);

    public void UpdateResourceSummary(bool keepAssetsStatus = false)
    {
        try
        {
            var info = DotaAssetsDownloader.GetStorageInfo();
            HasIcons = DotaAssetsDownloader.HasIcons();
            if (!HasIcons && IsEnvironmentOk && !keepAssetsStatus) StatusText = "Icons are missing: open the overlay settings and press Download assets.";
            ResourceSummary = $"Assets: {info.FileCount:N0} files · {FormatBytes(info.TotalBytes)}";
            if (!keepAssetsStatus) AssetsStatusText = ResourceSummary;
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

    private void AddEvent(DraftEventKind kind, string source, string message, int player = 0)
    {
        // The log is only visible with --debug-mode; do not keep growing a list nobody can see.
        if (!IsDebugMode) return;
        Events.Insert(0, new DraftEvent(Events.Count + 1, player, kind, source, message, DateTimeOffset.Now));
    }
}
