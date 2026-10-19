using System.IO;

namespace AbilityDraftOverlay.Services;

/// <summary>Well-known folders under %LocalAppData%\AbilityDraftOverlay.</summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AbilityDraftOverlay");

    public static string Assets => Path.Combine(Root, "Assets");
    public static string Cache => Path.Combine(Root, "Cache");
    public static string Debug => Path.Combine(Root, "Debug");
    public static string Sessions => Path.Combine(Root, "Sessions");

    /// <summary>Optional user override of the bundled layout; edit it without rebuilding the app.</summary>
    public static string UserLayout => Path.Combine(Root, "layout.1920x1080.json");
}
