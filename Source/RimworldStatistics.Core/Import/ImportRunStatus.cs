namespace RimworldStatistics.Core;

public enum ImportRunPhase
{
    /// <summary>No scan has started in this game session.</summary>
    NotRun,
    Running,
    /// <summary>The scan went through every save; some of them may have failed (<see cref="ImportResult.Failed"/>).</summary>
    Completed,
    /// <summary>The scan stopped early (the player cancelled, or a game started); what it read is kept.</summary>
    Cancelled,
    /// <summary>The scan as a whole failed, or could not start.</summary>
    Failed,
}

/// <summary>What the Import tab shows about the latest scan: its phase and the outcome of that scan only.</summary>
public sealed record ImportRunState(ImportRunPhase Phase, ImportResult? Result, Exception? Error, ImportProgress? Progress);

/// <summary>
/// The latest scan of this game session and its outcome (#337). Starting a scan forgets the outcome of the one before, so
/// the counts of an earlier scan are never shown as those of the scan running now or of one that failed. Each scan reports
/// under the number <see cref="Begin"/> gave it, so a report of an earlier scan cannot overwrite a later one. A failure of the
/// whole scan, a cancelled scan and a scan in which some saves failed are told apart by <see cref="ImportRunState.Phase"/>.
/// </summary>
public sealed class ImportRunStatus
{
    private readonly object _gate = new();
    private long _run;
    private ImportRunState _state = new(ImportRunPhase.NotRun, null, null, null);

    public ImportRunState Current
    {
        get
        {
            lock (_gate)
                return _state;
        }
    }

    /// <summary>A scan starts; returns the number its reports carry.</summary>
    public long Begin()
    {
        lock (_gate)
        {
            _state = new ImportRunState(ImportRunPhase.Running, null, null, null);
            return ++_run;
        }
    }

    public void Report(long run, ImportProgress progress)
    {
        lock (_gate)
            if (run == _run && _state.Phase == ImportRunPhase.Running)
                _state = _state with { Progress = progress };
    }

    public void Complete(long run, ImportResult result)
    {
        lock (_gate)
            if (run == _run && _state.Phase == ImportRunPhase.Running)
                _state = new ImportRunState(result.Cancelled ? ImportRunPhase.Cancelled : ImportRunPhase.Completed, result, null,
                    _state.Progress);
    }

    /// <summary>The scan failed as a whole (an exception out of the engine), or could not start.</summary>
    public void Fail(long run, Exception error)
    {
        lock (_gate)
            if (run == _run && _state.Phase == ImportRunPhase.Running)
                _state = new ImportRunState(ImportRunPhase.Failed, null, error, _state.Progress);
    }
}
