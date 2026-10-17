using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AbilityDraftOverlay.Models;
using AbilityDraftOverlay.Services;

namespace AbilityDraftOverlay.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DotaAssetsDownloader _assetsDownloader;

    [ObservableProperty]
    private string statusText = "Готово. Выберите скриншот или загрузите игровые ресурсы.";

    [ObservableProperty]
    private string? currentFramePath;

    [ObservableProperty]
    private bool isBusy;

    public ObservableCollection<DraftEvent> Events { get; } = new();

    public MainViewModel(DotaAssetsDownloader assetsDownloader)
    {
        _assetsDownloader = assetsDownloader;
    }

    public void SetFrame(string path)
    {
        CurrentFramePath = path;
        StatusText = $"Выбран кадр: {Path.GetFileName(path)}. Распознавание пока не подключено.";
        Events.Insert(0, new DraftEvent(Events.Count + 1, 0, DraftEventKind.Frame,
            path, $"Выбран следующий кадр: {Path.GetFileName(path)}", DateTimeOffset.Now));
    }

    [RelayCommand]
    private async Task DownloadResourcesAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        try
        {
            StatusText = "Загрузка ресурсов Dota 2...";
            var result = await _assetsDownloader.DownloadAsync(message =>
            {
                App.Current.Dispatcher.Invoke(() => StatusText = message);
            });

            Events.Insert(0, new DraftEvent(Events.Count + 1, 0, DraftEventKind.Resource,
                "assets", $"Загрузка ресурсов завершена: {result.Downloaded} новых файлов, пропущено {result.Skipped} существующих.", DateTimeOffset.Now));
            StatusText = $"Ресурсы готовы. Новых файлов: {result.Downloaded}; уже было: {result.Skipped}.";
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка загрузки ресурсов: {ex.Message}";
            Events.Insert(0, new DraftEvent(Events.Count + 1, 0, DraftEventKind.Error,
                "download", StatusText, DateTimeOffset.Now));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ClearEvents() => Events.Clear();
}
