using System.Threading;
using System.Windows;
using ADraftCompact.Services;
using ADraftCompact.Logging;
using Microsoft.Extensions.Logging;
using ADraftCompact.ViewModels;
using ADraftCompact.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ADraftCompact;

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
                services.AddSingleton<OverlayAutoHide>();
                services.AddSingleton<HotkeyService>();
                services.AddSingleton<DraftSessionTracker>();
                services.AddSingleton<GameWindowCapture>();
                services.AddSingleton<ManualRecognitionService>();
                services.AddSingleton<ScreenshotRecognitionService>();
                services.AddSingleton<OverlayWindow>();
                services.AddSingleton<OverlaySettingsService>();
                services.AddSingleton<UpdateService>();
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
        if (await TryUpdateAsync(services))
            return;
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
            await services.GetRequiredService<MainViewModel>().StartBackgroundFeaturesAsync();
        }

        mainWindow.Show();
        if (!report.IsOk)
            MessageBox.Show(mainWindow, report.ToWarningText(), "Ability Draft Overlay", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>Quiet start-up update: check GitHub Releases, tell the user, download, hand over to the swap script. True = app is exiting.</summary>
    private async Task<bool> TryUpdateAsync(IServiceProvider services)
    {
        if (System.Diagnostics.Debugger.IsAttached || Environment.GetCommandLineArgs().Contains("--no-update"))
            return false;

        var updater = services.GetRequiredService<UpdateService>();
        var info = await updater.CheckAsync();
        if (info is null)
            return false;

        MessageBox.Show($"Version v{AppVersion.Format(info.Version)} is available (current v{AppVersion.Text}).\n\n" +
                        "The update will now be downloaded and the application restarted.\n" +
                        "Your Assets, settings and log files are kept.",
            "Ability Draft Overlay", MessageBoxButton.OK, MessageBoxImage.Information);

        // The update window is the first window, so WPF would treat it as the main window and exit when it closes.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var window = new UpdateWindow(updater, info);
        window.ShowDialog();
        MainWindow = null;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        if (!window.ReadyToRestart)
            return false;

        services.GetRequiredService<AppLifecycle>().IsExiting = true;
        Shutdown();
        return true;
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
        _host?.Services.GetService<HotkeyService>()?.Dispose();
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
