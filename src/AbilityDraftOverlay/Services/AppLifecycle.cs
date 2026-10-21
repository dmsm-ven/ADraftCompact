namespace ADraftCompact.Services;

/// <summary>
/// Shared state that decides what closing the main window does.
/// Normally the close button hides the window into the tray; it really exits when the user picks "Exit" in the tray menu,
/// when Windows is logging off / shutting down, or when the startup checks failed (no tray icon is created then).
/// </summary>
public sealed class AppLifecycle
{
    /// <summary>True once the startup checks passed and the tray icon exists.</summary>
    public bool HideToTrayOnClose { get; set; }

    /// <summary>Set right before the application shuts down, so the main window stops hiding itself.</summary>
    public bool IsExiting { get; set; }

    public bool ShouldHideOnClose => HideToTrayOnClose && !IsExiting;
}
