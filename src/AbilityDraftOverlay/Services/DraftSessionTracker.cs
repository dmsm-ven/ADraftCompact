using ADraftCompact.Models;
using Microsoft.Extensions.Logging;

namespace ADraftCompact.Services;

/// <summary>
/// Turns snapshots (one per screenshot) into draft sessions and a per-player pick order.
///
/// Items are tracked by identity (hero / ability id), not by slot: after picking, a player may move abilities
/// between slots, so "what is in slot 2" says nothing about "what was picked second". The order comes from the
/// capture in which an item first showed up for that player. Items that first showed up within the same capture
/// interval cannot be ordered by observation; they are ordered by slot and flagged <see cref="PickedItem.OrderUncertain"/>.
/// (With a snake order the players at the turn, e.g. 10 and 1, pick twice in a row, so this does happen.)
/// </summary>
public sealed class DraftSessionTracker
{
    private readonly record struct Seen(int Player, int Team, SlotType Type, int Slot, string Id, string Name, double Score, bool Confident);
    private sealed record Candidate(int FirstCapture, DateTimeOffset FirstSeenAt, int Count, int LastCapture);

    private readonly ILogger<DraftSessionTracker> _logger;
    private readonly object _gate = new();
    private readonly Dictionary<(int Player, string Id), Candidate> _candidates = new();
    private DraftSession? _session;

    public DraftSessionTracker(ILogger<DraftSessionTracker> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// A board that shows fewer than this share of the items the session already knows is a new game.
    /// (Recognition misses a few slots now and then, so a mere dip must not restart the session.)
    /// </summary>
    public double NewGameShare { get; set; } = 0.5;
    /// <summary>Uncertain readings below this score are ignored completely.</summary>
    public double CandidateMinScore { get; set; } = 0.5;
    /// <summary>
    /// An uncertain reading is accepted once it was seen in this many captures. Captures are taken by hand (usually one per draft),
    /// so the best guess is accepted at once: showing a likely ability is better than showing nothing.
    /// </summary>
    public int CandidateConfirmations { get; set; } = 1;

    public DraftSession? Current { get { lock (_gate) return _session; } }

    /// <summary>Puts a board saved by a previous run back as the active session (only if there is none).</summary>
    public void Restore(DraftSession session)
    {
        lock (_gate)
            _session ??= session;
    }

    /// <summary>Closes the active session (new game started or the user cleared the log).</summary>
    public SessionUpdate EndSession(DateTimeOffset now, string reason)
    {
        lock (_gate)
        {
            var ended = CloseSession(now);
            return new SessionUpdate(null, ended, false, Array.Empty<DraftPick>(), ended is null ? "No active session." : reason);
        }
    }

    public SessionUpdate Process(DraftSnapshot snapshot, DateTimeOffset now)
    {
        lock (_gate)
        {
            // Captures are taken by hand: a frame without the board (wrong screen) must never wipe what is already known.
            if (!snapshot.BoardDetected)
                return new SessionUpdate(_session, null, false, Array.Empty<DraftPick>(),
                    "Ability Draft board not detected. Open the draft screen and try again.");

            var seen = CollectSeen(snapshot);
            DraftSession? endedSession = null;
            if (_session is not null && _session.TotalPicks >= 10 && seen.Count < _session.TotalPicks * NewGameShare)
            {
                _logger.LogInformation("Board contains {Seen} of {Total} known picks; treating it as a new game", seen.Count, _session.TotalPicks);
                endedSession = CloseSession(now);
            }

            var started = false;
            if (_session is null)
            {
                _session = new DraftSession { StartedAt = now };
                started = true;
                _logger.LogInformation("New session {Id} started at {At}", _session.Id, now);
            }

            var picks = Apply(_session, snapshot, seen, now);

            var message = $"Capture #{_session.Captures}: {picks.Count} new picks, {_session.TotalPicks} total.";
            return new SessionUpdate(_session, endedSession, started, picks, message);
        }
    }

    private DraftSession? CloseSession(DateTimeOffset now)
    {
        var session = _session;
        _session = null;
        _candidates.Clear();
        if (session is null) return null;
        session.EndedAt = now;
        _logger.LogInformation("Session {Id} ended: {Captures} captures, {Picks} picks", session.Id, session.Captures, session.TotalPicks);
        return session;
    }

    private List<Seen> CollectSeen(DraftSnapshot snapshot)
    {
        var list = new List<Seen>();
        foreach (var result in snapshot.Slots)
        {
            if (result.EntityId is null) continue;
            var confident = result.State == SlotState.Recognized;
            if (!confident && !(result.State == SlotState.Uncertain && result.Score >= CandidateMinScore)) continue;
            list.Add(new Seen(result.Slot.Player, result.Slot.Team, result.Slot.Type, result.Slot.Index,
                result.EntityId, result.DisplayName ?? result.EntityId, result.Score, confident));
        }
        return list;
    }

    private List<DraftPick> Apply(DraftSession session, DraftSnapshot snapshot, List<Seen> seen, DateTimeOffset now)
    {
        var capture = ++session.Captures;
        session.LastCaptureAt = now;
        var slotCount = snapshot.Slots.Where(s => s.Slot.Type == SlotType.Ability).Select(s => s.Slot.Index + 1).DefaultIfEmpty(0).Max();
        var newPicks = new List<DraftPick>();

        foreach (var group in seen.GroupBy(s => s.Player).OrderBy(g => g.Key))
        {
            var player = GetPlayer(session, group.Key, group.First().Team);
            var accepted = new List<(Seen Item, Candidate Candidate)>();

            // Abilities first (by slot), the hero last: a deterministic guess for items that appeared together.
            foreach (var item in group.OrderBy(s => s.Type == SlotType.Hero ? 1 : 0).ThenBy(s => s.Slot))
            {
                var known = player.Picks.FirstOrDefault(p => p.Type == item.Type && p.Id == item.Id);
                if (known is not null)
                {
                    known.Slot = item.Type == SlotType.Ability ? item.Slot : null;
                    known.LastSeenCapture = capture;
                    continue;
                }

                if (item.Type == SlotType.Hero && player.Picks.Any(p => p.Type == SlotType.Hero))
                {
                    _logger.LogWarning("Player {Player}: hero already selected ({Old}); ignoring new reading {New}",
                        player.Player, player.Picks.First(p => p.Type == SlotType.Hero).Id, item.Id);
                    continue;
                }

                var key = (item.Player, item.Id);
                var candidate = _candidates.TryGetValue(key, out var existing)
                    ? existing with { Count = existing.Count + 1, LastCapture = capture }
                    : new Candidate(capture, now, 1, capture);

                if (item.Confident || candidate.Count >= CandidateConfirmations)
                {
                    accepted.Add((item, candidate));
                    _candidates.Remove(key);
                }
                else
                {
                    _candidates[key] = candidate;
                }
            }

            foreach (var (item, candidate) in accepted)
            {
                player.Picks.Add(new PickedItem
                {
                    Id = item.Id,
                    Name = item.Name,
                    Type = item.Type,
                    FirstCapture = candidate.FirstCapture,
                    FirstSeenAt = candidate.FirstSeenAt,
                    Slot = item.Type == SlotType.Ability ? item.Slot : null,
                    LastSeenCapture = capture,
                    Score = item.Score
                });
            }

            if (accepted.Count > 0) RenumberPicks(player);
            foreach (var (item, _) in accepted)
            {
                var pick = player.Picks.First(p => p.Type == item.Type && p.Id == item.Id);
                newPicks.Add(new DraftPick(player.Player, pick.Type, pick.Id, pick.Name, pick.PickOrder, pick.FirstCapture,
                    pick.OrderUncertain, pick.Score, now));
            }

            UpdateArrangement(player, group, slotCount);
        }

        // Uncertain readings that were not seen in this capture start from scratch.
        foreach (var stale in _candidates.Where(pair => pair.Value.LastCapture != capture).Select(pair => pair.Key).ToList())
            _candidates.Remove(stale);

        return newPicks;
    }

    /// <summary>Order by first capture (stable: within a capture the guessed order is kept), then number and flag.</summary>
    private static void RenumberPicks(PlayerDraft player)
    {
        player.Picks = player.Picks.OrderBy(p => p.FirstCapture).ToList();
        var sharedCaptures = player.Picks.GroupBy(p => p.FirstCapture).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        for (var i = 0; i < player.Picks.Count; i++)
        {
            player.Picks[i].PickOrder = i + 1;
            player.Picks[i].OrderUncertain = sharedCaptures.Contains(player.Picks[i].FirstCapture);
        }
    }

    private static void UpdateArrangement(PlayerDraft player, IEnumerable<Seen> items, int slotCount)
    {
        var slots = new string?[Math.Max(slotCount, 1)];
        foreach (var item in items)
        {
            if (item.Type != SlotType.Ability || item.Slot >= slots.Length) continue;
            if (player.Picks.Any(p => p.Type == SlotType.Ability && p.Id == item.Id)) slots[item.Slot] = item.Id;
        }
        player.AbilitySlots = slots.ToList();
    }

    private static PlayerDraft GetPlayer(DraftSession session, int number, int team)
    {
        var player = session.Players.FirstOrDefault(p => p.Player == number);
        if (player is not null) return player;
        player = new PlayerDraft { Player = number, Team = team };
        session.Players.Add(player);
        session.Players.Sort((a, b) => a.Player.CompareTo(b.Player));
        return player;
    }
}
