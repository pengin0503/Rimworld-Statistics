namespace RimworldStatistics.Core;

/// <summary>
/// The save protocol of numeric expansion design §3 step 2–3, kept apart from Harmony so it can be tested: the SafeSaver
/// prefix calls <see cref="Enter"/>, the component's ExposeData asks <see cref="TextToWrite"/>, the postfix calls
/// <see cref="Complete"/> (which commits only after the game's own save and this mod's checks succeeded) and the finalizer
/// calls <see cref="Exit"/> whatever happened. A nested save or a save on another thread commits nothing; it writes the
/// last committed checkpoint unchanged. A save that failed leaves the parent where it was, and its events in memory.
/// </summary>
public sealed class CheckpointSaveSession
{
    public const string SaveGameElement = "savegame";

    private int? _owner;        // the thread of the outermost save in progress
    private int _depth;
    private bool _suppressed;   // a nested save happened: this save commits nothing
    private bool _written;
    private SaveDraft? _draft;

    public CheckpointSaveSession(LiveMetricAccumulator accumulator) => Accumulator = accumulator;

    public LiveMetricAccumulator Accumulator { get; }

    public void Enter(string documentElementName, int threadId, bool onGameThread, int tick, DateTime utc, Func<string> newId)
    {
        if (documentElementName != SaveGameElement)
            return;
        if (_owner is null && onGameThread)
        {
            _owner = threadId;
            _depth = 1;
            _suppressed = false;
            _written = false;
            _draft = null;
            _draft = Accumulator.PrepareSave(tick, utc, newId());
        }
        else if (_owner == threadId)
        {
            _depth++;
            _suppressed = true;
        }
    }

    /// <summary>
    /// The <c>numericStatistics</c> text a save of the component writes (#319). Another game's component writes what its save
    /// held. With the save scope working, the scope decides (<see cref="TextToWrite"/>). When the scope has stopped but this
    /// game's totals exist, the last committed (or loaded) checkpoint is written, as a nested save would, so checkpoints
    /// committed before the stop stay the parent of the next session; only with no totals at all is the loaded text kept.
    /// </summary>
    /// <param name="sameGame">The game being saved is the component's game.</param>
    /// <param name="activeSession">The save scope, or null when there is none or its patch has stopped.</param>
    /// <param name="accumulator">This game's totals, or null.</param>
    /// <param name="loaded">What the save held when it was loaded (null for a new game or a save without data).</param>
    public static string? TextForSave(bool sameGame, CheckpointSaveSession? activeSession, int threadId,
        LiveMetricAccumulator? accumulator, string? loaded)
    {
        if (!sameGame)
            return loaded;
        if (activeSession is not null)
            return activeSession.TextToWrite(threadId);
        return accumulator is not null ? accumulator.LoadedJson : loaded;
    }

    /// <summary>The text the component writes now (null: write nothing).</summary>
    public string? TextToWrite(int threadId)
    {
        if (_owner == threadId && _depth == 1 && !_suppressed && _draft is not null)
        {
            _written = true;
            return _draft.Json;
        }
        return Accumulator.LoadedJson;
    }

    /// <summary>
    /// The postfix of a save: the draft to record in the profile when the original ran, this save wrote it and the file exists;
    /// null otherwise. A returned draft has been committed (it is the next parent).
    /// </summary>
    public SaveDraft? Complete(string documentElementName, int threadId, bool runOriginal, bool fileExists)
    {
        if (documentElementName != SaveGameElement || _owner != threadId || _depth != 1 || _suppressed || !_written
            || !runOriginal || !fileExists || _draft is not { IsNew: true } draft)
            return null;
        Accumulator.Commit(draft);
        _draft = null;   // committed once
        return draft;
    }

    public void Exit(string documentElementName, int threadId)
    {
        if (documentElementName != SaveGameElement)
            return;
        if (_owner == threadId)
        {
            if (--_depth > 0)
                return;
            _owner = null;
            _depth = 0;
            _suppressed = false;
            _written = false;
            _draft = null;
        }
    }
}
