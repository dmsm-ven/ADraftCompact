using System.ComponentModel;
using System.Windows;
using ADraftCompact.Services;
using ADraftCompact.ViewModels;

namespace ADraftCompact.Views;

public partial class MainWindow : Window
{
    private readonly AppLifecycle _lifecycle;

    public MainWindow(MainViewModel viewModel, AppLifecycle lifecycle)
    {
        InitializeComponent();
        _lifecycle = lifecycle;
        DataContext = viewModel;
        Title = $"Ability Draft Overlay | v{AppVersion.Text}";
        if (viewModel.IsDebugMode)
        {
            // --debug-mode: room for the event log.
            Width = 980;
            MinWidth = 700;
            ResizeMode = ResizeMode.CanResize;
        }
        Loaded += (_, _) => viewModel.UpdateResourceSummary();
    }

    /// <summary>The close button hides the window into the tray; the hotkey and the overlay keep working.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_lifecycle.ShouldHideOnClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }
}
