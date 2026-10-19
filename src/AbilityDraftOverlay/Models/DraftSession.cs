namespace AbilityDraftOverlay.Models;

/// <summary>One thing a player has taken: the hero or an ability.</summary>
public sealed class PickedItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public SlotType Type { get; set; }
    /// <summary>1-based order in which THIS player took it (the hero counts as one of the picks).</summary>
    public int PickOrder { get; set; }
    /// <summary>Capture (screenshot) number of the session at which the item was first seen.</summary>
    public int FirstCapture { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    /// <summary>
    /// True when several of the player's items first appeared within the same capture interval:
    /// their relative order is a guess (by slot), not an observation.
    /// </summary>
    public bool OrderUncertain { get; set; }
    /// <summary>Where the player currently keeps the ability (0-based; the ultimate is always the last slot). Null for heroes.</summary>
    public int? Slot { get; set; }
    public int LastSeenCapture { get; set; }
    public double Score { get; set; }
}

public sealed class PlayerDraft
{
    public int Player { get; set; }
    public int Team { get; set; }
    /// <summary>Everything the player took, in the order of picking - independent of later slot rearrangement.</summary>
    public List<PickedItem> Picks { get; set; } = new();
    /// <summary>Ability ids as the player currently arranged them, slot by slot (null = empty or not recognized).</summary>
    public List<string?> AbilitySlots { get; set; } = new();
}

public sealed class DraftSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public int Captures { get; set; }
    public DateTimeOffset? LastCaptureAt { get; set; }
    public List<PlayerDraft> Players { get; set; } = new();

    public int TotalPicks => Players.Sum(player => player.Picks.Count);
}

/// <summary>A new pick found by the latest capture: "player N took X".</summary>
public sealed record DraftPick(
    int Player,
    SlotType Type,
    string EntityId,
    string DisplayName,
    int PickOrder,
    int Capture,
    bool OrderUncertain,
    double Score,
    DateTimeOffset DetectedAt);

/// <param name="Session">The active session after this capture (null if there is none).</param>
/// <param name="EndedSession">The session that was closed by this capture, if any.</param>
/// <param name="Started">True if <paramref name="Session"/> was created by this capture.</param>
public sealed record SessionUpdate(
    DraftSession? Session,
    DraftSession? EndedSession,
    bool Started,
    IReadOnlyList<DraftPick> NewPicks,
    string Message);
