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
}
