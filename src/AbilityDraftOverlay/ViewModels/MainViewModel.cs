using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AbilityDraftOverlay.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string statusText = "Каркас запущен. Захват экрана пока не подключён.";

    public IRelayCommand OpenSettingsCommand { get; }

    public MainViewModel()
    {
        OpenSettingsCommand = new RelayCommand(() => OpenSettingsRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? OpenSettingsRequested;
}
