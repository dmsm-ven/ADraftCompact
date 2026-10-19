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

    [ObservableProperty] private string statusText = "Готово.";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string resourceSummary = "Ресурсы: проверка...";
    [ObservableProperty] private bool isTracking;
    [ObservableProperty] private string sessionText = "Сессия: нет";
    public ObservableCollection<DraftEvent> Events { get; } = new();
    public string TrackingButtonText => IsTracking ? "■ Остановить отслеживание" : "▶ Отслеживать";
    public string OverlayButtonText => IsOverlayVisible ? "▣ Скрыть оверлей" : "▣ Показать оверлей";

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
        UpdateResourceSummary();
    }

    partial void OnIsTrackingChanged(bool value)
    {
        OnPropertyChanged(nameof(TrackingButtonText));
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
        AddEvent(DraftEventKind.Resource, "download", "Проверка доступности источника ресурсов.");
        _logger.LogInformation("Пользователь запросил загрузку ресурсов");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            bool available;
            try { available = await _assetsDownloader.CheckAvailabilityAsync(timeout.Token); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(ex, "OpenDota недоступен на этапе проверки");
                available = false;
            }
            if (!available)
            {
                StatusText = "Источники ресурсов недоступны. Загрузка отменена; проверьте подключение к сети.";
                AddEvent(DraftEventKind.Error, "resource-check", StatusText);
                _logger.LogWarning("Загрузка отменена: OpenDota недоступен");
                return;
            }

            StatusText = "Загрузка ресурсов...";
            var result = await _assetsDownloader.DownloadAsync(message => Application.Current.Dispatcher.Invoke(() => StatusText = message));
            StatusText = $"Загрузка завершена: новых {result.Downloaded}, пропущено {result.Skipped}, ошибок {result.Failed}.";
            AddEvent(result.Failed > 0 ? DraftEventKind.Error : DraftEventKind.Resource, "assets", StatusText);
            _logger.LogInformation("{Summary}", StatusText);
            _iconIndex.Invalidate(); // new icons -> rebuild the descriptor index on next recognition
            UpdateResourceSummary();
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка загрузки ресурсов: {ex.Message}";
            AddEvent(DraftEventKind.Error, "download", StatusText);
            _logger.LogError(ex, "Загрузка ресурсов завершилась ошибкой");
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
            StatusText = "Автоотслеживание остановлено.";
            AddEvent(DraftEventKind.Resource, "tracking", StatusText);
            return;
        }

        try
        {
            _lastTickStatus = null;
            await _autoTracking.StartAsync();
            IsTracking = true;
            StatusText = "Автоотслеживание запущено: первый снимок делается сразу, дальше раз в минуту.";
            AddEvent(DraftEventKind.Resource, "tracking", StatusText);
        }
        catch (Exception ex)
        {
            StatusText = $"Не удалось запустить автоотслеживание: {ex.Message}";
            AddEvent(DraftEventKind.Error, "tracking", StatusText);
            _logger.LogError(ex, "Не удалось запустить автоотслеживание");
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
                $"Сессия {ended.Id} завершена: снимков {ended.Captures}, выборов {ended.TotalPicks}. Файл: {SessionStore.PathFor(ended)}");
        if (update is { Started: true, Session: { } started })
            AddEvent(DraftEventKind.Resource, "session", $"Новая сессия {started.Id} · {started.StartedAt:dd.MM.yyyy HH:mm:ss}");

        foreach (var pick in update.NewPicks)
            AddEvent(pick.Type == SlotType.Hero ? DraftEventKind.Hero : DraftEventKind.Ability, pick.EntityId,
                $"Игрок {pick.Player}: {pick.PickOrder}-й выбор — {(pick.Type == SlotType.Hero ? "герой" : "способность")} {pick.DisplayName}" +
                $"{(pick.OrderUncertain ? " (порядок приблизительный)" : "")} · снимок №{pick.Capture}", pick.Player);

        if (snapshot is { BoardDetected: true } && (update.NewPicks.Count > 0 || update.Started))
            AddEvent(DraftEventKind.Frame, "report", DraftReportFormatter.Format(snapshot));

        SessionText = update.Session is { } session
            ? $"Сессия {session.Id} · с {session.StartedAt:dd.MM HH:mm:ss} · снимков {session.Captures} · выборов {session.TotalPicks}"
            : "Сессия: нет";
        _overlayWindow.UpdateSession(update.Session);
    }

    [RelayCommand]
    private void ClearEvents()
    {
        Events.Clear();
        _tracker.EndSession(DateTimeOffset.Now, "Журнал очищен"); // the next recognized board starts a new session
        SessionText = "Сессия: нет";
        _overlayWindow.UpdateSession(null);
    }

    public void UpdateResourceSummary()
    {
        try
        {
            var info = DotaAssetsDownloader.GetStorageInfo();
            ResourceSummary = $"Ресурсы: {info.FileCount:N0} файлов · {FormatBytes(info.TotalBytes)}";
            _logger.LogInformation("Локальные ресурсы: файлов {Count}, размер {Bytes} байт", info.FileCount, info.TotalBytes);
        }
        catch (Exception ex)
        {
            ResourceSummary = "Ресурсы: не удалось прочитать каталог";
            _logger.LogWarning(ex, "Не удалось подсчитать локальные ресурсы");
        }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824d:F2} ГБ",
        >= 1_048_576 => $"{bytes / 1_048_576d:F1} МБ",
        >= 1_024 => $"{bytes / 1_024d:F1} КБ",
        _ => $"{bytes} Б"
    };

    private void AddEvent(DraftEventKind kind, string source, string message, int player = 0) =>
        Events.Insert(0, new DraftEvent(Events.Count + 1, player, kind, source, message, DateTimeOffset.Now));
}
