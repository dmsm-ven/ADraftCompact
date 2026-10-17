namespace AbilityDraftOverlay.Models;

public enum DraftEventKind
{
    Ability,
    Hero
}

public sealed record DraftEvent(
    int Step,
    int PlayerNumber,
    DraftEventKind Kind,
    string EntityId,
    string DisplayName,
    DateTimeOffset DetectedAt);
