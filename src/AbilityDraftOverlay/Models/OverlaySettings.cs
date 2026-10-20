namespace AbilityDraftOverlay.Models;

public sealed class OverlaySettings
{
    public double Left { get; set; } = -1;
    public double Top { get; set; } = -1;
    public int IconSize { get; set; } = 40;
    public string OverlayColorHex { get; set; } = "#FF171A21";
    public int OverlayOpacityPercent { get; set; } = 0;
    public string IconBorderColorHex { get; set; } = "#FF343A43";
    public int IconBorderThickness { get; set; } = 1;
    public int PlayerRowGap { get; set; } = 4;
    public int HeroGap { get; set; } = 4;
    public int AbilityGap { get; set; } = 4;
    public int TeamGap { get; set; } = 30;
    public bool ShowTeamNames { get; set; } = true;
    /// <summary>Slightly rounded corners on hero and ability icons (and their borders).</summary>
    public bool RoundIconBorders { get; set; }
}
