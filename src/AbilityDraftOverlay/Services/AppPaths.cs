using System.IO;

namespace ADraftCompact.Services;

/// <summary>
/// Where the application keeps its files: everything lives next to the executable (the folder must be writable).
/// </summary>
public static class AppPaths
{
    /// <summary>Folder of the settings files (same as the executable folder).</summary>
    public static string Root => AppDirectory;

    /// <summary>Folder of the executable.</summary>
    public static string AppDirectory { get; } = AppContext.BaseDirectory;

    public static string AppSettingsFile => Path.Combine(Root, "settings.json");
    public static string OverlaySettingsFile => Path.Combine(Root, "overlay-settings.json");
    /// <summary>The last recognized board, restored on the next start.</summary>
    public static string LastBoardFile => Path.Combine(Root, "last-board.json");
    public static string IconIndexFile => Path.Combine(Root, "icons.index");

    /// <summary>The single log file.</summary>
    public static string LogFile => Path.Combine(AppDirectory, "app.log");
    /// <summary>Latest recognition debug image, overwritten on every capture.</summary>
    public static string DebugImageFile => Path.Combine(AppDirectory, "last_recognition.png");
    /// <summary>Downloaded hero / ability images and OpenDota reference data.</summary>
    public static string Assets => Path.Combine(AppDirectory, "Assets");

    /// <summary>Optional user override of the bundled layout; edit it without rebuilding the app.</summary>
    public static string UserLayout => Path.Combine(AppDirectory, "layout.1920x1080.json");

    /// <summary>Tray / application icon shipped next to the executable.</summary>
    public static string IconFile => Path.Combine(AppDirectory, "app.ico");
}
