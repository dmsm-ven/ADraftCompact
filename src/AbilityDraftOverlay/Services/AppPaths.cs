using System.IO;

namespace AbilityDraftOverlay.Services;

/// <summary>
/// Where the application keeps its files.
/// %LocalAppData%\AbilityDraftOverlay holds exactly two files, both directly in its root: the overlay settings and the icon index.
/// Everything else (log, debug image, downloaded assets, optional layout override) lives next to the executable.
/// </summary>
public static class AppPaths
{
    /// <summary>%LocalAppData%\AbilityDraftOverlay - no sub-folders.</summary>
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AbilityDraftOverlay");

    /// <summary>Folder of the executable.</summary>
    public static string AppDirectory { get; } = AppContext.BaseDirectory;

    public static string OverlaySettingsFile => Path.Combine(Root, "overlay-settings.json");
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
