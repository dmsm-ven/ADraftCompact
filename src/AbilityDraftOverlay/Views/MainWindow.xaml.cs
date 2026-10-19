using System.Windows;
using AbilityDraftOverlay.ViewModels;

namespace AbilityDraftOverlay.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => viewModel.UpdateResourceSummary();
    }
}
