using System.Runtime.InteropServices;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace AbilityDraftOverlay.Services;

/// <summary>
/// Notification-area (tray) icon with a native "Show / Exit" menu. No balloons or toasts are ever shown.
/// Implemented with Shell_NotifyIcon directly: referencing WinForms for NotifyIcon would add System.Windows.Forms to the
/// implicit usings and make Application / MessageBox / Color ambiguous across the whole project.
/// Must be created and disposed on the UI thread (messages arrive through a hidden window).
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private const string Tooltip = "Ability Draft Overlay";

    private const uint CallbackMessage = 0x8001; // WM_APP + 1
    private const int WmLButtonUp = 0x0202;
    private const int WmLButtonDblClk = 0x0203;
    private const int WmRButtonUp = 0x0205;
    private const int WmContextMenu = 0x007B;
    private const int WmNull = 0x0000;

    private const uint NifMessage = 0x1;
    private const uint NifIcon = 0x2;
    private const uint NifTip = 0x4;
    private const uint NimAdd = 0;
    private const uint NimDelete = 2;

    private const uint MfString = 0x0;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmBottomAlign = 0x0020;
    private const uint TpmReturnCmd = 0x0100;
    private const int WsExToolWindow = 0x00000080;
    private const int IdiApplication = 32512;

    private const uint CommandShow = 1;
    private const uint CommandExit = 2;

    private readonly ILogger<TrayIconService> _logger;
    private HwndSource? _source;
    private IntPtr _icon;
    private bool _ownsIcon;
    private uint _taskbarCreatedMessage;
    private bool _added;

    public TrayIconService(ILogger<TrayIconService> logger) => _logger = logger;

    /// <summary>Left click or the "Show" menu item.</summary>
    public event Action? ShowRequested;

    /// <summary>The "Exit" menu item.</summary>
    public event Action? ExitRequested;

    public bool IsVisible => _added;

    public void Show()
    {
        if (_source is not null) return;

        // A hidden top-level window (not message-only): only top-level windows receive the "TaskbarCreated" broadcast
        // that tells us Explorer restarted and the icon has to be added again.
        var parameters = new HwndSourceParameters("AbilityDraftOverlayTray") { WindowStyle = 0, ExtendedWindowStyle = WsExToolWindow };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        _icon = LoadApplicationIcon(out _ownsIcon);
        AddIcon();
    }

    private void AddIcon()
    {
        if (_source is null) return;
        var data = CreateData();
        _added = Shell_NotifyIcon(NimAdd, ref data);
        if (!_added) _logger.LogWarning("Could not add the tray icon");
    }

    private NotifyIconData CreateData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
        hWnd = _source!.Handle,
        uID = 1,
        uFlags = NifMessage | NifIcon | NifTip,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = Tooltip,
        szInfo = string.Empty,
        szInfoTitle = string.Empty
    };

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == CallbackMessage)
        {
            switch ((int)(lParam.ToInt64() & 0xFFFF))
            {
                case WmLButtonUp:
                case WmLButtonDblClk:
                    ShowRequested?.Invoke();
                    break;
                case WmRButtonUp:
                case WmContextMenu:
                    ShowMenu(hwnd);
                    break;
            }
            handled = true;
        }
        else if (_taskbarCreatedMessage != 0 && (uint)msg == _taskbarCreatedMessage)
        {
            AddIcon();
        }
        return IntPtr.Zero;
    }

    private void ShowMenu(IntPtr owner)
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        try
        {
            AppendMenu(menu, MfString, CommandShow, "Show");
            AppendMenu(menu, MfString, CommandExit, "Exit");
            GetCursorPos(out var cursor);

            // Without this the menu would not close when the user clicks elsewhere.
            SetForegroundWindow(owner);
            var command = TrackPopupMenuEx(menu, TpmRightButton | TpmBottomAlign | TpmReturnCmd, cursor.X, cursor.Y, owner, IntPtr.Zero);
            PostMessage(owner, WmNull, IntPtr.Zero, IntPtr.Zero);

            if (command == CommandShow) ShowRequested?.Invoke();
            else if (command == CommandExit) ExitRequested?.Invoke();
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    /// <summary>The icon of the executable; the stock application icon if it has none.</summary>
    private static IntPtr LoadApplicationIcon(out bool owned)
    {
        owned = false;
        var path = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(path))
        {
            var small = new IntPtr[1];
            if (ExtractIconEx(path, 0, null, small, 1) > 0 && small[0] != IntPtr.Zero)
            {
                owned = true;
                return small[0];
            }
        }
        return LoadIcon(IntPtr.Zero, new IntPtr(IdiApplication));
    }

    public void Dispose()
    {
        if (_source is null) return;
        if (_added)
        {
            var data = CreateData();
            Shell_NotifyIcon(NimDelete, ref data);
            _added = false;
        }
        _source.RemoveHook(WndProc);
        _source.Dispose();
        _source = null;
        if (_ownsIcon && _icon != IntPtr.Zero) DestroyIcon(_icon);
        _icon = IntPtr.Zero;
    }

    // ---- Win32 -------------------------------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("shell32.dll", EntryPoint = "ExtractIconExW", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, IntPtr[]? large, IntPtr[]? small, uint count);

    [DllImport("user32.dll", EntryPoint = "LoadIconW")]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, nuint id, string text);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr parameters);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
