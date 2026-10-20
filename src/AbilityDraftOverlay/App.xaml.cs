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
    private TrayIconService? _tray;
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
                services.AddSingleton<AppLifecycle>();
                services.AddSingleton<TrayIconService>();
                services.AddSingleton<EnvironmentCheckService>();
                services.AddSingleton<OverlayAutoVisibility>();
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
        var services = _host.Services;
        var mainWindow = services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;

        var lifecycle = services.GetRequiredService<AppLifecycle>();
        // A window that hides itself instead of closing would otherwise block Windows log-off / shutdown.
        SessionEnding += (_, _) => lifecycle.IsExiting = true;

        var report = await services.GetRequiredService<EnvironmentCheckService>().RunAsync();
        services.GetRequiredService<MainViewModel>().ApplyEnvironment(report);

        if (report.IsOk)
        {
            // Closing the window hides it into the tray (silently: no balloon or toast). Not done when the checks failed.
            var tray = _tray = services.GetRequiredService<TrayIconService>();
            tray.ShowRequested += () => RestoreMainWindow(mainWindow);
            tray.ExitRequested += () =>
            {
                lifecycle.IsExiting = true;
                Shutdown();
            };
            tray.Show();
            lifecycle.HideToTrayOnClose = true;
            await services.GetRequiredService<MainViewModel>().StartOverlayAutomationAsync();
        }

        mainWindow.Show();
        if (!report.IsOk)
            MessageBox.Show(mainWindow, report.ToWarningText(), "Ability Draft Overlay", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static void RestoreMainWindow(Window window)
    {
        if (!window.IsVisible) window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
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
