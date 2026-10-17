namespace AbilityDraftOverlay.Models;

public enum DraftEventKind
{
    Frame,
    Ability,
    Hero,
    Resource,
    Error
}

public sealed record DraftEvent(
    int Step,
    int PlayerNumber,
    DraftEventKind Kind,
    string EntityId,
    string DisplayName,
    DateTimeOffset DetectedAt);
