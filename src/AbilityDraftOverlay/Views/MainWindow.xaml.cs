using System.ComponentModel;
using System.Windows;
using AbilityDraftOverlay.Services;
using AbilityDraftOverlay.ViewModels;

namespace AbilityDraftOverlay.Views;

public partial class MainWindow : Window
{
    private readonly AppLifecycle _lifecycle;

    public MainWindow(MainViewModel viewModel, AppLifecycle lifecycle)
    {
        InitializeComponent();
        _lifecycle = lifecycle;
        DataContext = viewModel;
        Loaded += (_, _) => viewModel.UpdateResourceSummary();
    }

    /// <summary>The close button hides the window into the tray; tracking and the overlay keep running.</summary>
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
