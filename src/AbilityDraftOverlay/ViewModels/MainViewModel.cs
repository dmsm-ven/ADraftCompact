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
    private readonly DraftStateTracker _tracker;
    private readonly IconIndex _iconIndex;
    private readonly ILogger<MainViewModel> _logger;

    [ObservableProperty] private string statusText = "Готово.";
    [ObservableProperty] private string? currentFramePath;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string resourceSummary = "Ресурсы: проверка...";
    public ObservableCollection<DraftEvent> Events { get; } = new();
    public bool CanRecognize => !IsBusy && !string.IsNullOrWhiteSpace(CurrentFramePath);

    public MainViewModel(DotaAssetsDownloader assetsDownloader, ScreenshotRecognitionService recognizer,
        DraftStateTracker tracker, IconIndex iconIndex, ILogger<MainViewModel> logger)
    {
        _assetsDownloader = assetsDownloader;
        _recognizer = recognizer;
        _tracker = tracker;
        _iconIndex = iconIndex;
        _logger = logger;
        UpdateResourceSummary();
    }

    partial void OnCurrentFramePathChanged(string? value) => OnPropertyChanged(nameof(CanRecognize));
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanRecognize));

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
            var picks = _tracker.Update(snapshot, DateTimeOffset.Now);

            var recognized = snapshot.Slots.Count(s => s.State == SlotState.Recognized);
            var uncertain = snapshot.Slots.Count(s => s.State == SlotState.Uncertain);
            var empty = snapshot.Slots.Count(s => s.State == SlotState.Empty);

            // Events are inserted at the top, so add the details first and the summary last.
            foreach (var pick in picks)
                AddEvent(pick.Type == SlotType.Hero ? DraftEventKind.Hero : DraftEventKind.Ability, pick.EntityId,
                    $"Игрок {pick.Player}: {(pick.Type == SlotType.Hero ? "герой" : $"способность {pick.SlotIndex + 1}")} — {pick.DisplayName} ({pick.Score:0.00})",
                    pick.Player);

            AddEvent(DraftEventKind.Frame, "report", DraftReportFormatter.Format(snapshot));

            if (!snapshot.LayoutCalibrated)
                AddEvent(DraftEventKind.Error, "layout",
                    "Раскладка драфта не откалибрована (calibrated=false): координаты слотов примерные. " +
                    $"Рамки слотов нарисованы на {snapshot.DebugImagePath ?? "отладочном изображении"}.");

            StatusText = $"Готово за {snapshot.Elapsed.TotalMilliseconds:F0} мс: распознано {recognized}, неуверенно {uncertain}, пусто {empty}; новых выборов {picks.Count}; герои: {(snapshot.HeroSource == "ocr" ? "по имени (OCR)" : "по картинкам")}.";
            AddEvent(DraftEventKind.Resource, "recognition-summary", StatusText);
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
    private void ClearEvents()
    {
        Events.Clear();
        _tracker.Reset(); // start a new draft: previously seen picks are forgotten
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
