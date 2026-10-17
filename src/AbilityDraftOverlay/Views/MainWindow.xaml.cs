using System.Windows;
using AbilityDraftOverlay.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AbilityDraftOverlay.Views;

public partial class MainWindow : Window
{
    private readonly IServiceProvider _serviceProvider;

    public MainWindow(MainViewModel viewModel, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        DataContext = viewModel;
        _serviceProvider = serviceProvider;
        viewModel.OpenSettingsRequested += OnOpenSettingsRequested;
    }

    private void OnOpenSettingsRequested(object? sender, EventArgs e)
    {
        var window = _serviceProvider.GetRequiredService<SettingsWindow>();
        window.Owner = this;
        window.ShowDialog();
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e) => Close();
}
