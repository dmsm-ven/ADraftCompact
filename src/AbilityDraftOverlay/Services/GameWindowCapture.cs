using System.Diagnostics;
using System.Runtime.InteropServices;
using AbilityDraftOverlay.Models;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace AbilityDraftOverlay.Services;

/// <param name="Frame">BGR screenshot of the game window's client area; the caller disposes it. Null if nothing was captured.</param>
/// <param name="Status">Human-readable reason when <paramref name="Frame"/> is null.</param>
public sealed record CaptureOutcome(Mat? Frame, string Status, PixelRect? ScreenBounds);

/// <summary>
/// Takes a screenshot of the game window only (not of the whole desktop), on whichever monitor it is.
/// Passive: reads what is already on the screen, nothing is injected into or read from the game process. The game does not have to be the foreground window, but it must not be covered.
/// Uses plain GDI (BitBlt of the window's client rectangle), so it needs the game in windowed or borderless
/// mode and the window must not be covered by another window.
/// </summary>
public sealed class GameWindowCapture
{
    private readonly ILogger<GameWindowCapture> _logger;

    public GameWindowCapture(ILogger<GameWindowCapture> logger) => _logger = logger;

    /// <summary>
    /// Window coordinates must be real pixels on every monitor, otherwise a scaled (e.g. 125%) second monitor
    /// would shift the capture rectangle. Call once at start-up, before any window is created.
    /// </summary>
    public static void EnableDpiAwareness()
    {
        try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException) { }
    }

    public CaptureOutcome Capture(string processName)
    {
        var window = FindGameWindow(processName);
        if (window == IntPtr.Zero)
            return new CaptureOutcome(null, $"Game window not found: process {processName}.exe is not running or has no window.", null);
        if (IsIconic(window))
            return new CaptureOutcome(null, "The game window is minimized.", null);

        if (!GetClientRect(window, out var client) || client.Right <= 0 || client.Bottom <= 0)
            return new CaptureOutcome(null, "Could not determine the game window size.", null);
        var origin = new NativePoint { X = 0, Y = 0 };
        if (!ClientToScreen(window, ref origin))
            return new CaptureOutcome(null, "Could not determine the game window position.", null);

        var bounds = new PixelRect(origin.X, origin.Y, client.Right, client.Bottom);
        if (IsCovered(window, bounds))
            return new CaptureOutcome(null, "The game window is covered by another window. Bring Dota 2 to the front and try again.", bounds);

        var frame = CopyScreenRegion(bounds);
        if (frame is null)
            return new CaptureOutcome(null, "Screen capture failed (BitBlt).", bounds);

        // A black frame means the game is in exclusive fullscreen or the capture is blocked.
        var mean = Cv2.Mean(frame);
        if (mean.Val0 + mean.Val1 + mean.Val2 < 1.0)
        {
            frame.Dispose();
            return new CaptureOutcome(null, "The captured frame is completely black. Set Dota 2 to Borderless Window or Windowed mode.", bounds);
        }

        return new CaptureOutcome(frame, $"{bounds.Width}x{bounds.Height} @ ({bounds.X},{bounds.Y})", bounds);
    }

    private IntPtr FindGameWindow(string processName)
    {
        var processIds = new HashSet<uint>();
        foreach (var process in Process.GetProcessesByName(processName))
        {
            processIds.Add((uint)process.Id);
            process.Dispose();
        }
        if (processIds.Count == 0) return IntPtr.Zero;

        var best = IntPtr.Zero;
        long bestArea = 0;
        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out var pid);
            if (!processIds.Contains(pid) || !IsWindowVisible(handle) || !GetClientRect(handle, out var rect)) return true;
            var area = (long)rect.Right * rect.Bottom;
            if (area > bestArea) { bestArea = area; best = handle; }
            return true;
        }, IntPtr.Zero);
        return best;
    }

    /// <summary>The screen shows whatever is on top, so the window has to be visible at its center and corners.</summary>
    private static bool IsCovered(IntPtr window, PixelRect bounds)
    {
        var points = new[]
        {
            (bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2),
            (bounds.X + 8, bounds.Y + 8),
            (bounds.Right - 8, bounds.Y + 8),
            (bounds.X + 8, bounds.Bottom - 8),
            (bounds.Right - 8, bounds.Bottom - 8)
        };
        foreach (var (x, y) in points)
        {
            var top = WindowFromPoint(new NativePoint { X = x, Y = y });
            if (top == IntPtr.Zero) continue;
            var root = GetAncestor(top, GaRoot);
            if (root != window) return true;
        }
        return false;
    }

    private Mat? CopyScreenRegion(PixelRect bounds)
    {
        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return null;
        var memoryDc = CreateCompatibleDC(screenDc);
        var bitmap = IntPtr.Zero;
        var previous = IntPtr.Zero;
        try
        {
            var header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = bounds.Width,
                Height = -bounds.Height, // top-down rows
                Planes = 1,
                BitCount = 32,
                Compression = 0
            };
            bitmap = CreateDIBSection(screenDc, ref header, 0, out var bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero || bits == IntPtr.Zero) return null;
            previous = SelectObject(memoryDc, bitmap);

            if (!BitBlt(memoryDc, 0, 0, bounds.Width, bounds.Height, screenDc, bounds.X, bounds.Y, SrcCopy))
                return null;

            // Wrap the DIB memory, convert BGRA -> BGR into our own buffer, then the DIB can be released.
            using var bgra = Mat.FromPixelData(bounds.Height, bounds.Width, MatType.CV_8UC4, bits);
            var bgr = new Mat();
            Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);
            return bgr;
        }
        catch (Exception ex) when (ex is OpenCVException or ArgumentException)
        {
            _logger.LogWarning(ex, "Failed to copy screen pixels");
            return null;
        }
        finally
        {
            if (previous != IntPtr.Zero) SelectObject(memoryDc, previous);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    // ---- Win32 -------------------------------------------------------------------------------------------

    private const uint SrcCopy = 0x00CC0020;
    private const uint GaRoot = 2;

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);
}
