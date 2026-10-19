using AbilityDraftOverlay.Models;
using AbilityDraftOverlay.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AbilityDraftOverlay.ViewModels;

public partial class SettingsViewModel(ISettingsService settingsService) : ObservableObject
{
    [ObservableProperty]
    private string theme = "System";

    [ObservableProperty]
    private int targetScreenWidth = 1920;

    [ObservableProperty]
    private int targetScreenHeight = 1080;

    [ObservableProperty]
    private string statusText = "Settings have not been loaded yet.";

    [RelayCommand]
    public async Task LoadAsync()
    {
        var settings = await settingsService.LoadAsync();
        Theme = settings.Theme;
        TargetScreenWidth = settings.TargetScreenWidth;
        TargetScreenHeight = settings.TargetScreenHeight;
        StatusText = "Settings loaded.";
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        var settings = await settingsService.LoadAsync();
        settings.Theme = Theme;
        settings.TargetScreenWidth = TargetScreenWidth;
        settings.TargetScreenHeight = TargetScreenHeight;
        await settingsService.SaveAsync(settings);
        StatusText = "Settings saved.";
    }
}
