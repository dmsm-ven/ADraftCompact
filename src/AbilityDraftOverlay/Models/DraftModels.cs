namespace ADraftCompact.Models;

public enum SlotType { Hero, Ability }

public enum SlotState
{
    /// <summary>Slot is visible but nothing has been picked yet (flat, low-contrast area).</summary>
    Empty,
    /// <summary>Icon matched confidently (score and margin above thresholds).</summary>
    Recognized,
    /// <summary>Something is there, but the match is ambiguous or weak. Best guess is still reported.</summary>
    Uncertain,
    /// <summary>The slot rectangle does not fit into the screenshot.</summary>
    OutOfBounds
}

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

/// <summary>
/// One cell of the draft screen: which player it belongs to and where it is on the screenshot.
/// <paramref name="Roi"/> is the icon/art area; for heroes <paramref name="NameRoi"/> is the line with the hero name
/// (the card art is animated, the name text is not).
/// </summary>
public sealed record SlotPlacement(int Player, int Team, SlotType Type, int Index, PixelRect Roi, PixelRect? NameRoi = null);

public sealed record SlotResult(
    SlotPlacement Slot,
    SlotState State,
    string? EntityId,
    string? DisplayName,
    double Score,
    double Margin,
    string? RunnerUpId,
    string? RawText = null);

public sealed record DraftSnapshot(
    IReadOnlyList<SlotResult> Slots,
    TimeSpan Elapsed,
    TimeSpan IndexLoadTime,
    bool LayoutCalibrated,
    string? DebugImagePath,
    string HeroSource = "image",
    /// <summary>False when the frame is not the Ability Draft board (other screen, loading, ...).</summary>
    bool BoardDetected = true);
