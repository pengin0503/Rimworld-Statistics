namespace RimworldStatistics.Core;

/// <summary>
/// What background precomputation (MOD settings, "processing resources") builds for: the session, the record revision, the
/// developer-mode option and the colony being played (null on the title screen). A new target is built once.
/// </summary>
public readonly record struct PrecomputeTarget(ProfileSession Session, long Revision, bool IncludeDevMode, string? CampaignId);

/// <summary>
/// Builds ahead what the statistics windows build first, so that opening them finds it ready: the all-records view the
/// statistics window opens with and, in play, the summary and chronicle of the colony being played that the in-game window
/// shows. It only calls what the windows call, with their arguments, so they get the very objects from the session's caches
/// (or build them again exactly as before when a cache dropped them); nothing shown changes.
/// </summary>
public static class StatisticsPrecompute
{
    public static void Run(ProfileSession session, StatsOptions options, string? campaignId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (ct.IsCancellationRequested)
            return;
        // The same arguments as Dialog_RimworldStatistics (scope All until a group is chosen) and Dialog_ColonySummaryReport.
        var view = session.GetView(options, MetricDisplayPolicy.AllDetailsVisibleUnits, ViewScope.All);
        if (ct.IsCancellationRequested || string.IsNullOrWhiteSpace(campaignId))
            return;
        var resolution = InGameColonyAccess.Resolve(view, campaignId);
        if (resolution is { Found: true, ColonyKey: { } colonyKey })
            _ = session.ReadColonySummaryAndChronicle(view, colonyKey, options.IncludeDevMode);
    }
}

/// <summary>
/// When to precompute (main thread, polled). A target is built once it has stayed the same for <see cref="QuietPeriod"/>
/// while nothing else runs, so a burst of record changes costs one build, not one per change; it is not built again,
/// even after a failure, until something in it changes.
/// </summary>
public sealed class PrecomputeSchedule
{
    /// <summary>How long a target must stay unchanged and idle before it is built.</summary>
    public static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(2);

    private PrecomputeTarget? _seen;
    private TimeSpan _seenSince;
    private PrecomputeTarget? _attempted;

    /// <summary>The target to build now, or null. <paramref name="busy"/>: an import, data operation, game load or save, or a build runs.</summary>
    public PrecomputeTarget? Next(PrecomputeTarget target, bool busy, TimeSpan now)
    {
        if (_seen != target || busy)
        {
            // Busy time does not count as quiet: the period starts again once the work ends.
            _seen = target;
            _seenSince = now;
            return null;
        }
        if (_attempted == target || now - _seenSince < QuietPeriod)
            return null;
        _attempted = target;
        return target;
    }

    /// <summary>Forgets what was built (precomputation turned off): turning it on again builds the current target.</summary>
    public void Reset()
    {
        _seen = null;
        _attempted = null;
    }
}

/// <summary>
/// Runs <see cref="StatisticsPrecompute"/> on a pool thread at below-normal priority when <see cref="PrecomputeSchedule"/>
/// says so; one build at a time. Owned by the main thread: <see cref="Poll"/> and <see cref="Stop"/> are not thread-safe.
/// </summary>
public sealed class StatisticsPrecomputer
{
    private readonly PrecomputeSchedule _schedule = new();
    private readonly Func<Action, Task> _start;
    private readonly Action<PrecomputeTarget, CancellationToken> _build;
    private Task? _job;
    private CancellationTokenSource? _cts;

    public StatisticsPrecomputer() : this(work => Task.Run(work))
    {
    }

    /// <param name="start">Starts the build (tests run it inline or hold it).</param>
    /// <param name="build">The build itself; <see cref="StatisticsPrecompute.Run"/> unless a test watches it.</param>
    internal StatisticsPrecomputer(Func<Action, Task> start, Action<PrecomputeTarget, CancellationToken>? build = null)
    {
        _start = start;
        _build = build ?? ((target, ct) => StatisticsPrecompute.Run(target.Session,
            new StatsOptions { IncludeDevMode = target.IncludeDevMode }, target.CampaignId, ct));
    }

    /// <summary>A build runs now.</summary>
    public bool Running => _job is { IsCompleted: false };

    /// <summary>Builds started so far (tests and diagnostics).</summary>
    public int Started { get; private set; }

    /// <summary>
    /// Starts a build of <paramref name="target"/> when it is due; with a null target (turned off, or no session) stops.
    /// Returns the failure of a build that ended since the last call, once, for the caller to log.
    /// </summary>
    public Exception? Poll(PrecomputeTarget? target, bool busy, TimeSpan now)
    {
        Exception? failure = null;
        if (_job is { IsCompleted: true } done)
        {
            if (done.IsFaulted && done.Exception is { } aggregate)
                failure = aggregate.InnerExceptions.Count == 1 ? aggregate.InnerExceptions[0] : aggregate;
            _job = null;
            _cts?.Dispose();
            _cts = null;
        }
        if (target is not { } wanted)
        {
            Stop();
            return failure;
        }
        if (_schedule.Next(wanted, busy || _job is not null, now) is not { } next)
            return failure;

        var cts = new CancellationTokenSource();
        var token = cts.Token;
        _cts = cts;
        Started++;
        var build = _build;
        _job = _start(() => ThreadPriorities.Run(ThreadPriority.BelowNormal, () =>
        {
            build(next, token);
            return true;
        }));
        return failure;
    }

    /// <summary>
    /// Turned off: a running build stops at its next step (a view being built is finished, as the windows would finish it)
    /// and the next start builds the current target again.
    /// </summary>
    public void Stop()
    {
        _cts?.Cancel();
        _schedule.Reset();
    }
}
