using System.Windows;
using System.Windows.Media;

namespace ADraftCompact.Views;

public partial class ColorPickerWindow : Window
{
    public string SelectedColor { get; private set; }
    public ColorPickerWindow(string initialColor)
    {
        InitializeComponent(); SelectedColor = initialColor; HexText.Text = initialColor; UpdatePreview();
        HexText.TextChanged += (_, _) => UpdatePreview();
    }
    private void UpdatePreview()
    {
        try { PreviewBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(HexText.Text)); }
        catch { PreviewBorder.Background = Brushes.Transparent; }
    }
    private void Swatch_Click(object sender, RoutedEventArgs e) { if (sender is FrameworkElement { Tag: string color }) HexText.Text = color; }
    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        try { var color = (Color)ColorConverter.ConvertFromString(HexText.Text); SelectedColor = color.ToString(); DialogResult = true; }
        catch { MessageBox.Show(this, "Введите корректный HEX-цвет.", "Цвет", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
