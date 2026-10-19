using AbilityDraftOverlay.Models;
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
    private readonly ScreenshotRecognitionService _recognizer;
    private readonly DraftSessionTracker _tracker;
    private readonly AutoTrackingService _autoTracking;
    private string? _lastTickStatus;
    private readonly IconIndex _iconIndex;
    private readonly ILogger<MainViewModel> _logger;

    [ObservableProperty] private string statusText = "Готово.";
    [ObservableProperty] private string? currentFramePath;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string resourceSummary = "Ресурсы: проверка...";
    [ObservableProperty] private bool isTracking;
    [ObservableProperty] private string sessionText = "Сессия: нет";
    public ObservableCollection<DraftEvent> Events { get; } = new();
    public bool CanRecognize => !IsBusy && !IsTracking && !string.IsNullOrWhiteSpace(CurrentFramePath);
    public string TrackingButtonText => IsTracking ? "■ Остановить автоотслеживание" : "▶ Запустить автоотслеживание";

    public MainViewModel(DotaAssetsDownloader assetsDownloader, ScreenshotRecognitionService recognizer,
        DraftSessionTracker tracker, AutoTrackingService autoTracking, IconIndex iconIndex, ILogger<MainViewModel> logger)
    {
        _assetsDownloader = assetsDownloader;
        _recognizer = recognizer;
        _tracker = tracker;
        _autoTracking = autoTracking;
        _autoTracking.Ticked += OnTick;
        _iconIndex = iconIndex;
        _logger = logger;
        UpdateResourceSummary();
    }

    partial void OnCurrentFramePathChanged(string? value) => OnPropertyChanged(nameof(CanRecognize));
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanRecognize));
    partial void OnIsTrackingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRecognize));
        OnPropertyChanged(nameof(TrackingButtonText));
    }

    public void SetFrame(string path)
    {
        CurrentFramePath = path;
        StatusText = $"Выбран кадр: {Path.GetFileName(path)}";
        AddEvent(DraftEventKind.Frame, path, StatusText);
    }

    [RelayCommand]
    private async Task RecognizeScreenshotAsync()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(CurrentFramePath)) return;
        IsBusy = true;
        AddEvent(DraftEventKind.Resource, "recognition", "Начато распознавание скриншота по слотам драфта.");
        try
        {
            StatusText = "Распознавание скриншота...";
            var snapshot = await _recognizer.RecognizeAsync(CurrentFramePath);
            var update = _tracker.Process(snapshot, DateTimeOffset.Now);
            ShowUpdate(snapshot, update);

            var recognized = snapshot.Slots.Count(s => s.State == SlotState.Recognized);
            var uncertain = snapshot.Slots.Count(s => s.State == SlotState.Uncertain);
            var empty = snapshot.Slots.Count(s => s.State == SlotState.Empty);
            StatusText = snapshot.BoardDetected
                ? $"Готово за {snapshot.Elapsed.TotalMilliseconds:F0} мс: распознано {recognized}, неуверенно {uncertain}, пусто {empty}; новых выборов {update.NewPicks.Count}; герои: {(snapshot.HeroSource == "ocr" ? "по имени (OCR)" : "по картинкам")}."
                : "На кадре не найдена доска Ability Draft.";
            AddEvent(snapshot.BoardDetected ? DraftEventKind.Resource : DraftEventKind.Error, "recognition-summary", StatusText);
            _logger.LogInformation("{StatusText}", StatusText);
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка распознавания: {ex.Message}";
            AddEvent(DraftEventKind.Error, "recognition", StatusText);
            _logger.LogError(ex, "Ошибка распознавания скриншота");
        }
        finally { IsBusy = false; }
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
    }

    [RelayCommand]
    private void ClearEvents()
    {
        Events.Clear();
        _tracker.EndSession(DateTimeOffset.Now, "Журнал очищен"); // the next recognized board starts a new session
        SessionText = "Сессия: нет";
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
