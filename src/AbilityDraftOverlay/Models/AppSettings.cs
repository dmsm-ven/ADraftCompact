namespace AbilityDraftOverlay.Models;

public sealed class AppSettings
{
    public string Theme { get; set; } = "System";
    public string? BackgroundImagePath { get; set; }
    public double BackgroundOpacity { get; set; } = 35;
    public string BackgroundColorHex { get; set; } = "#FF171A21";
    public bool TileBackgroundImage { get; set; }
    public int TextureScalePercent { get; set; } = 100;

    public int TargetScreenWidth { get; set; } = 1920;
    public int TargetScreenHeight { get; set; } = 1080;

    /// <summary>Pause between automatic screenshots. One minute is about one pass over all 10 players.</summary>
    public int CaptureIntervalSeconds { get; set; } = 60;
    /// <summary>Process whose window is captured (without .exe).</summary>
    public string GameProcessName { get; set; } = "dota2";
}
