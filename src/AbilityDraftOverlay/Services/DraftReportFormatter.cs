using AbilityDraftOverlay.Models;
using System.Text;

namespace AbilityDraftOverlay.Services;

/// <summary>Plain-text view of a snapshot: one line per player.</summary>
public static class DraftReportFormatter
{
    public static string Format(DraftSnapshot snapshot)
    {
        var text = new StringBuilder();
        foreach (var player in snapshot.Slots.GroupBy(s => s.Slot.Player).OrderBy(g => g.Key))
        {
            var hero = player.FirstOrDefault(s => s.Slot.Type == SlotType.Hero);
            var abilities = player.Where(s => s.Slot.Type == SlotType.Ability).OrderBy(s => s.Slot.Index);
            text
                .Append($"{player.Key} | ")
                .Append($"{Describe(hero)} | ")
                .AppendJoin(" ", abilities.Select(i => $"[{Describe(i)}]"))
                .AppendLine();
        }
        return text.ToString().TrimEnd();
    }

    private static string Describe(SlotResult? result)
    {
        if (result is null) return "—";
        return result.State switch
        {
            SlotState.Recognized => result.DisplayName ?? result.EntityId ?? "?",
            SlotState.Uncertain => result.DisplayName ?? result.EntityId ?? "?",
            SlotState.OutOfBounds => "[вне кадра]",
            _ => "—"
        };
    }
}
