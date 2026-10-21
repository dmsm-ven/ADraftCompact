using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace ADraftCompact.Services;

/// <summary>
/// One system-wide hotkey (works while the game has the focus). Registered with RegisterHotKey on a hidden message-only window,
/// so nothing is hooked and no keystrokes are read. Must be used on the UI thread.
/// The combination is stored as text, e.g. "Ctrl+Shift+O".
/// </summary>
public sealed class HotkeyService : IDisposable
{
    public const string DefaultHotkey = "Ctrl+Shift+O";

    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 1;
    private const uint ModAlt = 0x1;
    private const uint ModControl = 0x2;
    private const uint ModShift = 0x4;
    private const uint ModWin = 0x8;
    private const uint ModNoRepeat = 0x4000;
    private static readonly IntPtr MessageOnlyParent = new(-3); // HWND_MESSAGE

    private readonly ILogger<HotkeyService> _logger;
    private HwndSource? _source;
    private string? _current;
    private bool _registered;

    public HotkeyService(ILogger<HotkeyService> logger) => _logger = logger;

    /// <summary>The hotkey was pressed.</summary>
    public event Action? Pressed;

    /// <summary>The combination that is (or, while suspended, will be) active; null if none.</summary>
    public string? Current => _current;

    /// <summary>Makes <paramref name="text"/> the hotkey. On failure the previous hotkey stays active.</summary>
    public bool TryRegister(string text, out string error)
    {
        if (!TryParse(text, out var modifiers, out var virtualKey))
        {
            error = "Use Ctrl, Alt, Shift or Win together with another key.";
            return false;
        }

        EnsureWindow();
        var previous = _current;
        UnregisterNative();

        if (RegisterHotKey(_source!.Handle, HotkeyId, modifiers | ModNoRepeat, virtualKey))
        {
            _registered = true;
            _current = text;
            error = string.Empty;
            _logger.LogInformation("Hotkey registered: {Hotkey}", text);
            return true;
        }

        _logger.LogWarning("Could not register hotkey {Hotkey}: Win32 error {Error}", text, Marshal.GetLastWin32Error());
        if (previous is not null && TryParse(previous, out var oldModifiers, out var oldKey))
            _registered = RegisterHotKey(_source.Handle, HotkeyId, oldModifiers | ModNoRepeat, oldKey);
        error = "This combination is already used by another application.";
        return false;
    }

    /// <summary>Temporarily releases the hotkey (while the user types a new one), so pressing it does not trigger the action.</summary>
    public void Suspend() => UnregisterNative();

    /// <summary>Registers the current hotkey again after <see cref="Suspend"/>.</summary>
    public void Resume()
    {
        if (_registered || _current is null || _source is null) return;
        if (TryParse(_current, out var modifiers, out var virtualKey))
            _registered = RegisterHotKey(_source.Handle, HotkeyId, modifiers | ModNoRepeat, virtualKey);
    }

    private void EnsureWindow()
    {
        if (_source is not null) return;
        _source = new HwndSource(new HwndSourceParameters("AbilityDraftOverlayHotkeys") { ParentWindow = MessageOnlyParent, WindowStyle = 0 });
        _source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke();
        }
        return IntPtr.Zero;
    }

    private void UnregisterNative()
    {
        if (!_registered || _source is null) return;
        UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
    }

    public void Dispose()
    {
        UnregisterNative();
        if (_source is null) return;
        _source.RemoveHook(WndProc);
        _source.Dispose();
        _source = null;
    }

    /// <summary>"Ctrl+Shift+O" -> modifier flags and virtual-key code. At least one modifier is required, so no plain key is ever taken over.</summary>
    public static bool TryParse(string? text, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        Key? key = null;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= ModControl; break;
                case "alt": modifiers |= ModAlt; break;
                case "shift": modifiers |= ModShift; break;
                case "win": modifiers |= ModWin; break;
                default:
                    if (key is not null) return false;
                    if (part.Length == 1 && char.IsDigit(part[0])) key = Key.D0 + (part[0] - '0');
                    else if (Enum.TryParse<Key>(part, ignoreCase: true, out var parsed)) key = parsed;
                    else return false;
                    break;
            }
        }

        if (modifiers == 0 || key is null || IsModifierKey(key.Value)) return false;
        virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key.Value);
        return virtualKey != 0;
    }

    /// <summary>The text stored in the settings and shown in the field, e.g. "Ctrl+Shift+O".</summary>
    public static string Format(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>(4);
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key is >= Key.D0 and <= Key.D9 ? ((char)('0' + (key - Key.D0))).ToString() : key.ToString());
        return string.Join("+", parts);
    }

    public static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
        or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System or Key.None or Key.DeadCharProcessed or Key.ImeProcessed;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
