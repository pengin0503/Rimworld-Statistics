namespace RimworldStatistics.Core;

/// <summary>
/// Carries which ending is under way from the patch that starts it to the credits hook that records it (spec §8.4).
/// Main thread only.
/// </summary>
public sealed class CreditsContext
{
    private EndingType? _type;
    private int _launchedBefore;
    private bool _alreadyRecorded;
    private bool _questEndingRecorded;

    public void Begin(EndingType type, int launchedBefore)
    {
        _type = type;
        _launchedBefore = launchedBefore;
        _alreadyRecorded = false;
    }

    /// <summary>The royal-ascent style quest ending was recorded when the colonists left.</summary>
    public void NoteQuestEndingRecorded() => _questEndingRecorded = true;

    public bool ConsumeQuestEndingRecorded()
    {
        var was = _questEndingRecorded;
        _questEndingRecorded = false;
        return was;
    }

    /// <summary>The credits about to show belong to an ending that is already recorded.</summary>
    public void MarkAlreadyRecorded()
    {
        _type = null;
        _alreadyRecorded = true;
    }

    /// <summary>Consumes the context. Null when already recorded; <see cref="EndingType.Other"/> when nothing set one up.</summary>
    public EndingSignal? Resolve(int launchedNow, string? creditsText)
    {
        var type = _type;
        var already = _alreadyRecorded;
        var before = _launchedBefore;
        _type = null;
        _alreadyRecorded = false;
        if (already)
            return null;
        if (type is not { } t)
            return new EndingSignal(EndingType.Other, 0, EndingLedger.ExcerptOf(creditsText));
        var departed = t is EndingType.Ship or EndingType.Archonexus ? Math.Max(0, launchedNow - before) : 0;
        return new EndingSignal(t, departed, null);
    }

    public void Reset()
    {
        _type = null;
        _launchedBefore = 0;
        _alreadyRecorded = false;
        _questEndingRecorded = false;
    }
}
