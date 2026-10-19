using System.Windows;
using System.Windows.Controls;
using AbilityDraftOverlay.Models;
using AbilityDraftOverlay.Services;

namespace AbilityDraftOverlay.Views;

public partial class OverlaySettingsWindow : Window
{
    private readonly OverlayWindow _overlay;
    private readonly OverlaySettingsService _settingsService;
    private OverlaySettings _settings = new();
    private bool _positionEditing;

    public OverlaySettingsWindow(OverlayWindow overlay, OverlaySettingsService settingsService)
    {
        InitializeComponent(); _overlay = overlay; _settingsService = settingsService;
        Loaded += async (_, _) =>
        {
            _settings = await _settingsService.LoadAsync();
            _overlay.ApplySettings(_settings);
            OverlayColorText.Text = _settings.OverlayColorHex; BorderColorText.Text = _settings.IconBorderColorHex;
            OpacitySlider.Value = _settings.OverlayOpacityPercent; IconSizeSlider.Value = _settings.IconSize;
            BorderSizeSlider.Value = _settings.IconBorderThickness; RowGapSlider.Value = _settings.PlayerRowGap;
            HeroGapSlider.Value = _settings.HeroGap; AbilityGapSlider.Value = _settings.AbilityGap; TeamGapSlider.Value = _settings.TeamGap;
            ShowNamesCheckBox.IsChecked = _settings.ShowTeamNames;
            UpdateLabels();
        };
        foreach (var slider in new[] { OpacitySlider, IconSizeSlider, BorderSizeSlider, RowGapSlider, HeroGapSlider, AbilityGapSlider, TeamGapSlider })
            slider.ValueChanged += (_, _) => UpdateLabels();
    }

    private void UpdateLabels()
    {
        if (OpacityValue is null) return;
        OpacityValue.Text = $"{(int)OpacitySlider.Value}%"; IconSizeValue.Text = $"{(int)IconSizeSlider.Value} px";
        BorderSizeValue.Text = $"{(int)BorderSizeSlider.Value} px"; RowGapValue.Text = $"{(int)RowGapSlider.Value} px";
        HeroGapValue.Text = $"{(int)HeroGapSlider.Value} px"; AbilityGapValue.Text = $"{(int)AbilityGapSlider.Value} px";
        TeamGapValue.Text = $"{(int)TeamGapSlider.Value} px";
    }

    private void PositionButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_positionEditing)
        {
            _overlay.BeginPositionEdit(); _positionEditing = true;
            PositionStatus.Text = "Перетащите оверлей мышью, затем нажмите Сохранить";
            PositionButtonText(sender, "Завершить перемещение");
        }
        else
        {
            _settings.Left = _overlay.Left; _settings.Top = _overlay.Top;
            _overlay.EndPositionEdit(true); _positionEditing = false;
            PositionStatus.Text = "Позиция подготовлена к сохранению";
            PositionButtonText(sender, "Переместить оверлей");
        }
    }
    private static void PositionButtonText(object sender, string text) { if (sender is Button button) button.Content = text; }

    private void OverlayColorButton_Click(object sender, RoutedEventArgs e) => PickColor(OverlayColorText);
    private void BorderColorButton_Click(object sender, RoutedEventArgs e) => PickColor(BorderColorText);
    private void PickColor(TextBox target)
    {
        var dialog = new ColorPickerWindow(target.Text) { Owner = this };
        if (dialog.ShowDialog() == true) target.Text = dialog.SelectedColor;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_positionEditing)
        {
            _settings.Left = _overlay.Left; _settings.Top = _overlay.Top;
            _overlay.EndPositionEdit(true); _positionEditing = false;
        }
        _settings.OverlayColorHex = NormalizeColor(OverlayColorText.Text, "#FF171A21");
        _settings.OverlayOpacityPercent = (int)OpacitySlider.Value;
        _settings.IconSize = Math.Clamp((int)Math.Round(IconSizeSlider.Value / 5.0) * 5, 15, 180);
        _settings.IconBorderColorHex = NormalizeColor(BorderColorText.Text, "#FF343A43");
        _settings.IconBorderThickness = (int)BorderSizeSlider.Value;
        _settings.PlayerRowGap = (int)RowGapSlider.Value; _settings.HeroGap = (int)HeroGapSlider.Value;
        _settings.AbilityGap = (int)AbilityGapSlider.Value; _settings.TeamGap = (int)TeamGapSlider.Value;
        _settings.ShowTeamNames = ShowNamesCheckBox.IsChecked == true;
        await _settingsService.SaveAsync(_settings);
        _overlay.ApplySettingsAndSave(_settings);
        if (_overlay.IsVisible) _overlay.EndPositionEdit(false);
        DialogResult = true;
    }

    private static string NormalizeColor(string value, string fallback)
    {
        try { return ((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value)).ToString(); }
        catch { return fallback; }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_positionEditing) _overlay.EndPositionEdit(false);
        Close();
    }
    protected override void OnClosed(EventArgs e)
    {
        if (_positionEditing) _overlay.EndPositionEdit(false);
        base.OnClosed(e);
    }
}
