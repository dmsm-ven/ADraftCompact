namespace ADraftCompact.Models;

public sealed class AppSettings
{
    public string Theme { get; set; } = "System";

    /// <summary>Screen resolution the draft layout was measured for; checked at startup.</summary>
    public int TargetScreenWidth { get; set; } = 1920;
    public int TargetScreenHeight { get; set; } = 1080;
    /// <summary>Escape hatch: skip the startup checks (English, OCR, game language, resolution) and never disable the window.</summary>
    public bool SkipEnvironmentChecks { get; set; }

    /// <summary>Hide the overlay when the game closes.</summary>
    public bool OverlayAutoHide { get; set; } = true;

    /// <summary>Process whose window is captured (without .exe).</summary>
    public string GameProcessName { get; set; } = "dota2";
}
