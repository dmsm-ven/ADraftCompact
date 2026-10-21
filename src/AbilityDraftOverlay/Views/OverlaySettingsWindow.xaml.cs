using ADraftCompact.Models;
using ADraftCompact.Services;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace ADraftCompact.Views;

public partial class OverlaySettingsWindow : Window
{
    private readonly OverlayWindow _overlay;
    private readonly OverlaySettingsService _settingsService;
    private readonly HotkeyService _hotkeys;
    private OverlaySettings _settings = new();
    private bool _initializing = true;
    private bool _positionEditing;
    private bool _settingsSaved;

    public OverlaySettingsWindow(OverlayWindow overlay, OverlaySettingsService settingsService, HotkeyService hotkeys, object viewModel)
    {
        InitializeComponent();
        _overlay = overlay;
        _settingsService = settingsService;
        _hotkeys = hotkeys;
        DataContext = viewModel; // download command and its progress text

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
            RoundBordersCheckBox.IsChecked = _settings.RoundIconBorders;
            (_settings.HorizontalLayout ? HorizontalTile : VerticalTile).IsChecked = true;
            HotkeyBox.Text = _hotkeys.Current ?? _settings.RecognizeHotkey;
            UpdateColorButtons();
            UpdateLabels();
            _initializing = false;
        };

        foreach (var slider in new[] { OpacitySlider, IconSizeSlider, BorderSizeSlider, RowGapSlider, HeroGapSlider, AbilityGapSlider, TeamGapSlider })
            slider.ValueChanged += (_, _) => ApplyControlsLive();
        ShowNamesCheckBox.Checked += (_, _) => ApplyControlsLive();
        ShowNamesCheckBox.Unchecked += (_, _) => ApplyControlsLive();
        RoundBordersCheckBox.Checked += (_, _) => ApplyControlsLive();
        RoundBordersCheckBox.Unchecked += (_, _) => ApplyControlsLive();
        VerticalTile.Checked += (_, _) => ApplyControlsLive();
        HorizontalTile.Checked += (_, _) => ApplyControlsLive();
    }

    private const string HotkeyDefaultHint = "Recognize abilities (works inside the game)";

    // While the field has the focus the hotkey is released, so pressing the current combination selects it instead of recognizing.
    private void HotkeyBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _hotkeys.Suspend();
        HotkeyHint.Text = "Press a combination, e.g. Ctrl+Shift+O";
    }

    private void HotkeyBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _hotkeys.Resume();
        HotkeyBox.Text = _hotkeys.Current ?? _settings.RecognizeHotkey;
        HotkeyHint.Text = HotkeyDefaultHint;
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (HotkeyService.IsModifierKey(key)) return; // wait for the real key

        var modifiers = Keyboard.Modifiers;
        if (key == Key.Escape && modifiers == ModifierKeys.None)
        {
            Keyboard.ClearFocus();
            return;
        }
        if (modifiers == ModifierKeys.None)
        {
            HotkeyHint.Text = "Add Ctrl, Alt, Shift or Win.";
            return;
        }

        var text = HotkeyService.Format(modifiers, key);
        if (_hotkeys.TryRegister(text, out var error))
        {
            _settings.RecognizeHotkey = text;
            HotkeyBox.Text = text;
            HotkeyHint.Text = $"Saved: {text}";
        }
        else
        {
            HotkeyHint.Text = $"{text}: {error}";
        }
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
        _settings.RoundIconBorders = RoundBordersCheckBox.IsChecked == true;
        _settings.HorizontalLayout = HorizontalTile.IsChecked == true;
        if (_positionEditing)
        {
            _settings.Left = _overlay.Left;
            _settings.Top = _overlay.Top;
        }
        _overlay.ApplySettings(_settings);
        UpdateLabels();
    }

    private static int NormalizeIconSize(int value) => Math.Clamp((int)value, 15, 150);

    private void PositionButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_positionEditing)
        {
            _overlay.BeginPositionEdit();
            _positionEditing = true;
            PositionStatus.Text = "Drag the overlay, then save its position";
            PositionButton.Content = "Save position";
        }
        else
        {
            _settings.Left = _overlay.Left;
            _settings.Top = _overlay.Top;
            _overlay.EndPositionEdit(true);
            _positionEditing = false;
            PositionStatus.Text = "Position saved";
            PositionButton.Content = "Move overlay";
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
            PositionButton.Content = "Move overlay";
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
        _hotkeys.Resume();
        if (_positionEditing) _overlay.EndPositionEdit(true);
        base.OnClosed(e);
    }
}
