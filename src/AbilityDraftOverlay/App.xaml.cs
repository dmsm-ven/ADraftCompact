using AbilityDraftOverlay.Services;
using AbilityDraftOverlay.ViewModels;
using AbilityDraftOverlay.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;

namespace AbilityDraftOverlay;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private IHost? _host;
    private bool _ownsMutex;

    public App()
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, @"Local\AbilityDraftOverlay", out bool createdNew);
        _ownsMutex = createdNew;

        if (!createdNew)
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            MessageBox.Show("Ability Draft Overlay is already running.", "Ability Draft Overlay",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton<ISettingsService, JsonSettingsService>();
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddSingleton<MainWindow>();
                services.AddTransient<SettingsWindow>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (_host is null)
            return;

        await _host.StartAsync();
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        if (_ownsMutex && _singleInstanceMutex is not null)
            _singleInstanceMutex.ReleaseMutex();

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
