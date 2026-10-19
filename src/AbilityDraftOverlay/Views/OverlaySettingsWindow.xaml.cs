using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AbilityDraftOverlay.Models;
using AbilityDraftOverlay.Services;

namespace AbilityDraftOverlay.Views;

public partial class OverlaySettingsWindow : Window
{
    private readonly OverlayWindow _overlay;
    private readonly OverlaySettingsService _settingsService;
    private OverlaySettings _settings = new();
    private bool _initializing = true;
    private bool _positionEditing;
    private bool _settingsSaved;

    public OverlaySettingsWindow(OverlayWindow overlay, OverlaySettingsService settingsService)
    {
        InitializeComponent();
        _overlay = overlay;
        _settingsService = settingsService;

        Loaded += async (_, _) =>
        {
            _settings = await _settingsService.LoadAsync();
            _overlay.ApplySettings(_settings);

            OpacitySlider.Value = _settings.OverlayOpacityPercent;
            IconSizeSlider.Value = _settings.IconSize;
            BorderSizeSlider.Value = _settings.IconBorderThickness;
            RowGapSlider.Value = _settings.PlayerRowGap;
            HeroGapSlider.Value = _settings.HeroGap;
            AbilityGapSlider.Value = _settings.AbilityGap;
            TeamGapSlider.Value = _settings.TeamGap;
            ShowNamesCheckBox.IsChecked = _settings.ShowTeamNames;
            UpdateColorButtons();
            UpdateLabels();
            _initializing = false;
        };

        foreach (var slider in new[] { OpacitySlider, IconSizeSlider, BorderSizeSlider, RowGapSlider, HeroGapSlider, AbilityGapSlider, TeamGapSlider })
            slider.ValueChanged += (_, _) => ApplyControlsLive();
        ShowNamesCheckBox.Checked += (_, _) => ApplyControlsLive();
        ShowNamesCheckBox.Unchecked += (_, _) => ApplyControlsLive();
    }

    private void UpdateLabels()
    {
        OpacityValue.Text = $"{(int)OpacitySlider.Value}%";
        IconSizeValue.Text = $"{NormalizeIconSize((int)IconSizeSlider.Value)} px";
        BorderSizeValue.Text = $"{(int)BorderSizeSlider.Value} px";
        RowGapValue.Text = $"{(int)RowGapSlider.Value} px";
        HeroGapValue.Text = $"{(int)HeroGapSlider.Value} px";
        AbilityGapValue.Text = $"{(int)AbilityGapSlider.Value} px";
        TeamGapValue.Text = $"{(int)TeamGapSlider.Value} px";
    }

    private void ApplyControlsLive()
    {
        if (_initializing) return;
        _settings.OverlayOpacityPercent = (int)OpacitySlider.Value;
        _settings.IconSize = NormalizeIconSize((int)IconSizeSlider.Value);
        _settings.IconBorderThickness = (int)BorderSizeSlider.Value;
        _settings.PlayerRowGap = (int)RowGapSlider.Value;
        _settings.HeroGap = (int)HeroGapSlider.Value;
        _settings.AbilityGap = (int)AbilityGapSlider.Value;
        _settings.TeamGap = (int)TeamGapSlider.Value;
        _settings.ShowTeamNames = ShowNamesCheckBox.IsChecked == true;
        if (_positionEditing)
        {
            _settings.Left = _overlay.Left;
            _settings.Top = _overlay.Top;
        }
        _overlay.ApplySettings(_settings);
        UpdateLabels();
    }

    private static int NormalizeIconSize(int value) => Math.Clamp((int)Math.Round(value / 5.0) * 5, 15, 180);

    private void PositionButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_positionEditing)
        {
            _overlay.BeginPositionEdit();
            _positionEditing = true;
            PositionStatus.Text = "Перетащите оверлей, затем закрепите позицию";
            PositionButton.Content = "Закрепить позицию";
        }
        else
        {
            _settings.Left = _overlay.Left;
            _settings.Top = _overlay.Top;
            _overlay.EndPositionEdit(true);
            _positionEditing = false;
            PositionStatus.Text = "Позиция закреплена";
            PositionButton.Content = "Переместить оверлей";
        }
    }

    private void OverlayColorButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = PickColor(_settings.OverlayColorHex);
        if (selected is null) return;
        _settings.OverlayColorHex = selected;
        UpdateColorButtons();
        if (_positionEditing) { _settings.Left = _overlay.Left; _settings.Top = _overlay.Top; }
        _overlay.ApplySettings(_settings);
    }

    private void BorderColorButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = PickColor(_settings.IconBorderColorHex);
        if (selected is null) return;
        _settings.IconBorderColorHex = selected;
        UpdateColorButtons();
        if (_positionEditing) { _settings.Left = _overlay.Left; _settings.Top = _overlay.Top; }
        _overlay.ApplySettings(_settings);
    }

    private string? PickColor(string initialColor)
    {
        var initial = ParseColor(initialColor);
        var customColors = Marshal.AllocHGlobal(sizeof(int) * 16);
        try
        {
            for (var i = 0; i < 16; i++) Marshal.WriteInt32(customColors, i * sizeof(int), 0x00FFFFFF);
            var dialog = new ChooseColor
            {
                StructSize = (uint)Marshal.SizeOf<ChooseColor>(),
                Owner = new WindowInteropHelper(this).Handle,
                InitialColor = (uint)(initial.R | (initial.G << 8) | (initial.B << 16)),
                CustomColors = customColors,
                Flags = 0x00000001 | 0x00000002 | 0x00000100 // RGBINIT | FULLOPEN | ANYCOLOR
            };
            if (!ChooseColorNative(ref dialog)) return null;
            var colorRef = dialog.InitialColor;
            var red = (byte)(colorRef & 0xFF);
            var green = (byte)((colorRef >> 8) & 0xFF);
            var blue = (byte)((colorRef >> 16) & 0xFF);
            return $"#{red:X2}{green:X2}{blue:X2}";
        }
        finally
        {
            Marshal.FreeHGlobal(customColors);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ChooseColor
    {
        public uint StructSize;
        public IntPtr Owner;
        public IntPtr Instance;
        public uint InitialColor;
        public IntPtr CustomColors;
        public uint Flags;
        public IntPtr CustomData;
        public IntPtr Hook;
        public string? TemplateName;
    }

    [DllImport("comdlg32.dll", EntryPoint = "ChooseColorW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChooseColorNative(ref ChooseColor chooseColor);

    private void UpdateColorButtons()
    {
        OverlayColorButton.Background = new SolidColorBrush(ParseColor(_settings.OverlayColorHex));
        BorderColorButton.Background = new SolidColorBrush(ParseColor(_settings.IconBorderColorHex));
        OverlayColorButton.Foreground = ContrastForeground(ParseColor(_settings.OverlayColorHex));
        BorderColorButton.Foreground = ContrastForeground(ParseColor(_settings.IconBorderColorHex));
    }

    private static System.Windows.Media.Color ParseColor(string? value)
    {
        try { return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(string.IsNullOrWhiteSpace(value) ? "#FF343A43" : value); }
        catch { return Colors.DimGray; }
    }

    private static Brush ContrastForeground(System.Windows.Media.Color color) =>
        (color.R * 0.299 + color.G * 0.587 + color.B * 0.114) > 145 ? Brushes.Black : Brushes.White;

    private async void CloseButton_Click(object sender, RoutedEventArgs e) => await SaveAndCloseAsync();

    private async Task SaveAndCloseAsync()
    {
        if (_settingsSaved) { Close(); return; }
        if (_positionEditing)
        {
            _settings.Left = _overlay.Left;
            _settings.Top = _overlay.Top;
            _overlay.EndPositionEdit(true);
            _positionEditing = false;
            PositionButton.Content = "Переместить оверлей";
        }
        _settings.Left = _overlay.Left;
        _settings.Top = _overlay.Top;
        await _settingsService.SaveAsync(_settings);
        _settingsSaved = true;
        Close();
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (!_settingsSaved)
        {
            e.Cancel = true;
            if (_positionEditing)
            {
                _settings.Left = _overlay.Left;
                _settings.Top = _overlay.Top;
                _overlay.EndPositionEdit(true);
                _positionEditing = false;
            }
            _settings.Left = _overlay.Left;
            _settings.Top = _overlay.Top;
            await _settingsService.SaveAsync(_settings);
            _settingsSaved = true;
            Dispatcher.BeginInvoke(new Action(Close));
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_positionEditing) _overlay.EndPositionEdit(true);
        base.OnClosed(e);
    }
}
