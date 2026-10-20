namespace AbilityDraftOverlay.Models;

public sealed class AppSettings
{
    public string Theme { get; set; } = "System";
    public string? BackgroundImagePath { get; set; }
    public double BackgroundOpacity { get; set; } = 35;
    public string BackgroundColorHex { get; set; } = "#FF171A21";
    public bool TileBackgroundImage { get; set; }
    public int TextureScalePercent { get; set; } = 100;

    /// <summary>Screen resolution the draft layout was measured for; checked at startup.</summary>
    public int TargetScreenWidth { get; set; } = 1920;
    public int TargetScreenHeight { get; set; } = 1080;
    /// <summary>Escape hatch: skip the startup checks (English, OCR, game language, resolution) and never disable the window.</summary>
    public bool SkipEnvironmentChecks { get; set; }

    /// <summary>Hide the overlay when the game closes and show it a while after the draft board appears.</summary>
    public bool OverlayAutoVisibility { get; set; } = true;
    /// <summary>How long after the draft board is first detected the overlay is shown automatically.</summary>
    public int OverlayAutoShowDelaySeconds { get; set; } = 45;

    /// <summary>Pause between automatic screenshots. One 45 sec is about one pass over all 10 players.</summary>
    public int CaptureIntervalSeconds { get; set; } = 30;
    /// <summary>Process whose window is captured (without .exe).</summary>
    public string GameProcessName { get; set; } = "dota2";
}
