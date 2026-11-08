namespace RimworldStatistics.Core;

/// <summary>
/// Carries which ending is under way from the patch that starts it to the credits hook that records it (spec §8.4).
/// Not thread-safe: use it only from the thread running the game's logic.
/// </summary>
public sealed class CreditsContext
{
    private EndingType? _type;
    private int _launchedBefore;
    private bool _participantsDefined;
    private bool _alreadyRecorded;
    private bool _questEndingRecorded;

    /// <param name="participantsDefined">
    /// The game is about to count this ending's participants into statsRecord.colonistsLaunched (a ship with caskets, the
    /// archonexus); the launched difference then becomes <see cref="EndingSignal.Party"/>.
    /// </param>
    public void Begin(EndingType type, int launchedBefore, bool participantsDefined = false)
    {
        _type = type;
        _launchedBefore = launchedBefore;
        _participantsDefined = participantsDefined;
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
        var defined = _participantsDefined;
        _type = null;
        _participantsDefined = false;
        _alreadyRecorded = false;
        if (already)
            return null;
        if (type is not { } t)
            return new EndingSignal(EndingType.Other, 0, EndingLedger.ExcerptOf(creditsText));
        var departed = t is EndingType.Ship or EndingType.Archonexus ? Math.Max(0, launchedNow - before) : 0;
        var party = defined && (t is EndingType.Ship or EndingType.Archonexus) && launchedNow >= before ? launchedNow - before : (int?)null;
        return new EndingSignal(t, departed, null, party);
    }

    public void Reset()
    {
        _type = null;
        _launchedBefore = 0;
        _participantsDefined = false;
        _alreadyRecorded = false;
        _questEndingRecorded = false;
    }
}
