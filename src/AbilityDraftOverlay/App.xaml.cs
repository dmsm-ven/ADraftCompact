using System.Threading;
using System.Windows;
using AbilityDraftOverlay.Services;
using AbilityDraftOverlay.Logging;
using Microsoft.Extensions.Logging;
using AbilityDraftOverlay.ViewModels;
using AbilityDraftOverlay.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AbilityDraftOverlay;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private IHost? _host;
    private bool _ownsMutex;

    public App()
    {
        GameWindowCapture.EnableDpiAwareness(); // real pixel coordinates of the game window on any monitor
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
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(new FileLoggerProvider());
                logging.SetMinimumLevel(LogLevel.Debug);
            })
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton<DotaAssetsDownloader>();
                services.AddSingleton<IconIndex>();
                services.AddSingleton<DraftLayoutProvider>();
                services.AddSingleton<HeroNameReader>();
                services.AddSingleton<ISettingsService, JsonSettingsService>();
                services.AddSingleton<SessionStore>();
                services.AddSingleton<DraftSessionTracker>();
                services.AddSingleton<GameWindowCapture>();
                services.AddSingleton<AutoTrackingService>();
                services.AddSingleton<ScreenshotRecognitionService>();
                services.AddSingleton<OverlayWindow>();
                services.AddSingleton<OverlaySettingsService>();
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
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
