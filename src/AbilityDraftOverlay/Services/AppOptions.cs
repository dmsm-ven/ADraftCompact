namespace AbilityDraftOverlay.Services;

/// <summary>Command-line switches.</summary>
public static class AppOptions
{
    /// <summary>Started with <c>--debug-mode</c>: the main window also shows the event log.</summary>
    public static bool DebugMode { get; } = Environment.GetCommandLineArgs().Skip(1)
        .Any(argument => string.Equals(argument, "--debug-mode", StringComparison.OrdinalIgnoreCase));
}
