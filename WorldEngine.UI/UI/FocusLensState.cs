using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;

namespace WorldEngine.UI.UI;

/// <summary>Which entity type the focus lens is tracking.</summary>
public enum FocusType { None, Character, Civilization }

/// <summary>
/// Tracks the player's current "focus target" for filtering the event log.
/// When a focus is active, the event log highlights events involving the target
/// and dims all others.
/// </summary>
public sealed class FocusLensState
{
    public FocusType Type   { get; private set; } = FocusType.None;
    public long      TargetId { get; private set; }

    /// <summary>Pre-fetched set of event IDs that involve the current focus target.</summary>
    public HashSet<long> FocusedEventIds { get; } = new();

    /// <summary>Focus on a character: pre-fetch all events involving that character.</summary>
    public void FocusCharacter(long charId, IHistoryQuery history)
    {
        Type     = FocusType.Character;
        TargetId = charId;
        FocusedEventIds.Clear();
        foreach (var e in history.GetCharacterHistory(new EntityId(charId)))
            FocusedEventIds.Add(e.Id.Value);
    }

    /// <summary>Focus on a civilization: pre-fetch all events in that civ's history.</summary>
    public void FocusCiv(long civId, IHistoryQuery history)
    {
        Type     = FocusType.Civilization;
        TargetId = civId;
        FocusedEventIds.Clear();
        foreach (var e in history.GetCivHistory(new CivId((int)civId), 0, int.MaxValue))
            FocusedEventIds.Add(e.Id.Value);
    }

    /// <summary>Clear the focus lens — return to unfiltered view.</summary>
    public void Clear()
    {
        Type     = FocusType.None;
        TargetId = 0;
        FocusedEventIds.Clear();
    }
}
