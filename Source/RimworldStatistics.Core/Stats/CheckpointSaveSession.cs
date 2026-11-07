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

    private readonly Dictionary<int, int> _foreign = new();
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
        else
        {
            _foreign[threadId] = _foreign.GetValueOrDefault(threadId) + 1;
        }
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
        else if (_foreign.TryGetValue(threadId, out var n))
        {
            if (n <= 1)
                _foreign.Remove(threadId);
            else
                _foreign[threadId] = n - 1;
        }
    }
}
