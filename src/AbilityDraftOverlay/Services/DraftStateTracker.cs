using AbilityDraftOverlay.Models;

namespace AbilityDraftOverlay.Services;

/// <summary>
/// Turns a stream of snapshots into discrete pick events. A slot is reported once, when it goes from
/// empty to a confidently recognized icon. With a timer later, raise <see cref="ConfirmFrames"/> so a
/// pick is only accepted after it was seen on several consecutive frames (pick animation, fades).
/// </summary>
public sealed class DraftStateTracker
{
    private readonly record struct SlotKey(int Player, SlotType Type, int Index);

    private readonly Dictionary<SlotKey, string> _confirmed = new();
    private readonly Dictionary<SlotKey, (string Id, int Count)> _pending = new();
    private int _step;

    /// <summary>Consecutive frames with the same result required before a pick is reported.</summary>
    public int ConfirmFrames { get; set; } = 1;

    public void Reset()
    {
        _confirmed.Clear();
        _pending.Clear();
        _step = 0;
    }

    public IReadOnlyList<DraftPick> Update(DraftSnapshot snapshot, DateTimeOffset now)
    {
        var picks = new List<DraftPick>();
        var seen = new HashSet<SlotKey>();

        var recognized = snapshot.Slots
            .Where(s => s.State == SlotState.Recognized && s.EntityId is not null)
            .OrderBy(s => s.Slot.Player).ThenBy(s => s.Slot.Type).ThenBy(s => s.Slot.Index);

        foreach (var result in recognized)
        {
            var key = new SlotKey(result.Slot.Player, result.Slot.Type, result.Slot.Index);
            if (_confirmed.ContainsKey(key)) continue;
            seen.Add(key);

            var id = result.EntityId!;
            var count = _pending.TryGetValue(key, out var pending) && pending.Id == id ? pending.Count + 1 : 1;
            if (count < Math.Max(1, ConfirmFrames))
            {
                _pending[key] = (id, count);
                continue;
            }

            _pending.Remove(key);
            _confirmed[key] = id;
            picks.Add(new DraftPick(++_step, key.Player, key.Type, key.Index, id,
                result.DisplayName ?? id, result.Score, now));
        }

        // A candidate that disappeared or changed its mind must start counting from scratch.
        foreach (var key in _pending.Keys.Where(k => !seen.Contains(k)).ToArray())
            _pending.Remove(key);

        return picks;
    }
}
