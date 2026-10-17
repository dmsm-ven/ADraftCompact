using System.IO;
using System.Windows;
using Microsoft.Win32;
using AbilityDraftOverlay.ViewModels;

namespace AbilityDraftOverlay.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += (_, _) => viewModel.UpdateResourceSummary();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.CurrentFramePath))
                EmptyFrameText.Visibility = string.IsNullOrWhiteSpace(viewModel.CurrentFramePath)
                    ? Visibility.Visible : Visibility.Collapsed;
        };
    }

    private void SelectFrameButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите следующий кадр Dota 2",
            Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp|Все файлы|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.SetFrame(dialog.FileName);
            try
            {
                // Load from disk without locking the selected screenshot file.
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(Path.GetFullPath(dialog.FileName));
                bitmap.EndInit();
                FrameImage.Source = bitmap;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Не удалось открыть изображение: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e) => Close();
}
