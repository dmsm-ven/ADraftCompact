namespace AbilityDraftOverlay.Models;

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

/// <summary>One cell of the draft screen: which player it belongs to and where it is on the screenshot.</summary>
public sealed record SlotPlacement(int Player, int Team, SlotType Type, int Index, PixelRect Roi);

public sealed record SlotResult(
    SlotPlacement Slot,
    SlotState State,
    string? EntityId,
    string? DisplayName,
    double Score,
    double Margin,
    string? RunnerUpId);

public sealed record DraftSnapshot(
    IReadOnlyList<SlotResult> Slots,
    TimeSpan Elapsed,
    TimeSpan IndexLoadTime,
    bool LayoutCalibrated,
    string? DebugImagePath);

/// <summary>A slot that became filled between two frames: "player N took hero/ability X".</summary>
public sealed record DraftPick(
    int Step,
    int Player,
    SlotType Type,
    int SlotIndex,
    string EntityId,
    string DisplayName,
    double Score,
    DateTimeOffset DetectedAt);
