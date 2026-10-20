using AbilityDraftOverlay.Models;
using AbilityDraftOverlay.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AbilityDraftOverlay.Views;

public partial class OverlayWindow : Window, INotifyPropertyChanged
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExLayered = 0x00080000;
    private const int WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 2;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private bool _positionEditMode;
    private bool _clickThroughEnabled;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    public ObservableCollection<OverlayPlayerRow> RadiantRows { get; } = new();
    public ObservableCollection<OverlayPlayerRow> DireRows { get; } = new();
    public OverlaySettings Settings { get; private set; } = new();
    public double IconSize => Settings.IconSize;
    public double PlayerRowGap => Settings.PlayerRowGap;
    public double HeroGap => Settings.HeroGap;
    public double AbilityGap => Settings.AbilityGap;
    public double TeamGap => Settings.TeamGap;
    public Thickness PlayerRowMargin => new(0, 0, 0, Settings.PlayerRowGap);
    public Thickness HeroMargin => new(0, 0, Settings.HeroGap, 0);
    public Thickness AbilityMargin => new(0, 0, Settings.AbilityGap, 0);
    public bool ShowTeamNames => Settings.ShowTeamNames;
    public Visibility TeamNamesVisibility => ShowTeamNames ? Visibility.Visible : Visibility.Collapsed;
    public Brush OverlayBackground => _positionEditMode
        ? new SolidColorBrush(Color.FromArgb(75, 20, 150, 220))
        : new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(Settings.OverlayOpacityPercent * 255 / 100, 0, 255), ParseColor(Settings.OverlayColorHex).R, ParseColor(Settings.OverlayColorHex).G, ParseColor(Settings.OverlayColorHex).B));
    public Brush IconBorderBrush => new SolidColorBrush(ParseColor(Settings.IconBorderColorHex));
    public Thickness IconBorderThickness => new(Settings.IconBorderThickness);
    /// <summary>The smallest rounding that is still visible; a large radius is deliberately not offered.</summary>
    private const double RoundedIconRadius = 3;
    public CornerRadius IconCornerRadius => new(Settings.RoundIconBorders ? RoundedIconRadius : 0);
    public Brush OverlayBorderBrush => _positionEditMode ? Brushes.DeepSkyBlue : Brushes.Transparent;
    public Thickness OverlayBorderThickness => _positionEditMode ? new Thickness(2) : new Thickness(0);
    public event PropertyChangedEventHandler? PropertyChanged;

    public OverlayWindow()
    {
        InitializeComponent();
        DataContext = this;
        for (var player = 1; player <= 10; player++)
        {
            var row = new OverlayPlayerRow { Player = player };
            (player % 2 == 1 ? RadiantRows : DireRows).Add(row);
        }
        Left = Math.Max(0, SystemParameters.PrimaryScreenWidth - Width - 18);
        Top = Math.Max(0, (SystemParameters.PrimaryScreenHeight - Height) / 2);
        SourceInitialized += (_, _) =>
        {
            SetClickThrough(true);
            SetNoActivate(true);
        };

        PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (!_positionEditMode || e.ChangedButton != System.Windows.Input.MouseButton.Left)
                return;

            e.Handled = true;
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
                return;

            // Ask Windows to move the window as a standard caption-drag operation.
            // This is more reliable than DragMove for a transparent WPF overlay.
            ReleaseCapture();
            SendMessage(handle, WmNcLButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
        };
    }

    public void ApplySettings(OverlaySettings settings)
    {
        Settings = settings;
        if (!_positionEditMode && Settings.Left >= 0 && Settings.Top >= 0) { Left = Settings.Left; Top = Settings.Top; }
        UpdateSize();
        NotifySettingsChanged();
    }

    public void BeginPositionEdit()
    {
        if (!IsVisible)
            Show();

        _positionEditMode = true;

        // Allow interaction with the window.
        SetClickThrough(false);
        SetNoActivate(false);

        NotifySettingsChanged();

        Activate();
    }

    public void EndPositionEdit(bool savePosition)
    {
        if (savePosition)
        {
            Settings.Left = Left;
            Settings.Top = Top;
        }

        _positionEditMode = false;

        SetClickThrough(true);
        SetNoActivate(true);

        NotifySettingsChanged();
    }

    public void ApplySettingsAndSave(OverlaySettings settings)
    {
        Settings = settings;
        UpdateSize();
        if (Settings.Left >= 0 && Settings.Top >= 0) { Left = Settings.Left; Top = Settings.Top; }
        NotifySettingsChanged();
    }

    private void UpdateSize()
    {
        var width = Settings.IconSize * 5 + Settings.HeroGap + Settings.AbilityGap * 3 + Settings.IconBorderThickness * 10 + 8;
        var height = Settings.IconSize * 10 + Settings.PlayerRowGap * 10 + Settings.TeamGap + (Settings.ShowTeamNames ? 32 : 0) + 12;
        Width = Math.Max(80, width);
        Height = Math.Max(80, height);
    }

    private void SetClickThrough(bool enabled)
    {
        if (!IsLoaded && PresentationSource.FromVisual(this) is null) { _clickThroughEnabled = enabled; return; }
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        style |= WsExToolWindow | WsExLayered;
        if (enabled)
            style |= WsExTransparent;
        else
            style &= ~WsExTransparent;

        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style));
        RefreshWindowStyles(handle);
        _clickThroughEnabled = enabled;
    }

    private void SetNoActivate(bool enabled)
    {
        var handle = new WindowInteropHelper(this).Handle;

        if (handle == IntPtr.Zero)
            return;

        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();

        if (enabled)
            style |= WsExNoActivate;
        else
            style &= ~WsExNoActivate;

        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style));
        RefreshWindowStyles(handle);
    }

    private static void RefreshWindowStyles(IntPtr handle)
    {
        SetWindowPos(
            handle,
            IntPtr.Zero,
            0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    private void NotifySettingsChanged()
    {
        OnPropertyChanged(nameof(IconSize)); OnPropertyChanged(nameof(PlayerRowGap));
        OnPropertyChanged(nameof(HeroGap)); OnPropertyChanged(nameof(AbilityGap)); OnPropertyChanged(nameof(TeamGap));
        OnPropertyChanged(nameof(PlayerRowMargin)); OnPropertyChanged(nameof(HeroMargin)); OnPropertyChanged(nameof(AbilityMargin));
        OnPropertyChanged(nameof(ShowTeamNames)); OnPropertyChanged(nameof(TeamNamesVisibility));
        OnPropertyChanged(nameof(OverlayBackground)); OnPropertyChanged(nameof(IconBorderBrush));
        OnPropertyChanged(nameof(IconBorderThickness)); OnPropertyChanged(nameof(IconCornerRadius)); OnPropertyChanged(nameof(OverlayBorderBrush));
        OnPropertyChanged(nameof(OverlayBorderThickness));
    }

    private static Color ParseColor(string? value) => ColorConverter.ConvertFromString(string.IsNullOrWhiteSpace(value) ? "#FF343A43" : value) is Color color ? color : Colors.DimGray;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void UpdateSession(DraftSession? session)
    {
        var players = session?.Players.ToDictionary(p => p.Player) ?? new Dictionary<int, PlayerDraft>();
        for (var player = 1; player <= 10; player++)
        {
            var row = player % 2 == 1 ? RadiantRows[(player - 1) / 2] : DireRows[(player - 2) / 2];
            players.TryGetValue(player, out var draft);
            row.Hero = LoadHero(draft?.Picks.FirstOrDefault(p => p.Type == SlotType.Hero)?.Id);
            var slots = draft?.AbilitySlots ?? new List<string?>();
            row.Ability1 = LoadAbility(GetSlot(slots, 0)); row.Ability2 = LoadAbility(GetSlot(slots, 1));
            row.Ability3 = LoadAbility(GetSlot(slots, 2)); row.Ultimate = LoadAbility(GetSlot(slots, 3));
        }
    }

    private static string? GetSlot(IReadOnlyList<string?> slots, int index) => index < slots.Count ? slots[index] : null;
    private static BitmapImage? LoadHero(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var name = id.Replace("npc_dota_hero_", "", StringComparison.OrdinalIgnoreCase);
        return LoadFirst(Path.Combine(AppPaths.Assets, "heroes", "portraits", name + ".png"), Path.Combine(AppPaths.Assets, "heroes", "legacy", name + "_lg.png"), Path.Combine(AppPaths.Assets, "heroes", "icons", name + ".png"), Path.Combine(AppPaths.Assets, "heroes", "legacy", name + "_icon.png"));
    }
    private static BitmapImage? LoadAbility(string? id) => string.IsNullOrWhiteSpace(id) ? null : LoadFirst(Path.Combine(AppPaths.Assets, "abilities", "modern", id + ".png"), Path.Combine(AppPaths.Assets, "abilities", "legacy", id + "_lg.png"), Path.Combine(AppPaths.Assets, "abilities", "legacy", id + "_md.png"));
    private static BitmapImage? LoadFirst(params string[] paths)
    {
        var path = paths.FirstOrDefault(File.Exists); if (path is null) return null;
        try { var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.CreateOptions = BitmapCreateOptions.IgnoreImageCache; image.UriSource = new Uri(path, UriKind.Absolute); image.EndInit(); image.Freeze(); return image; } catch { return null; }
    }
}
