using System.Diagnostics;
using RimworldStatistics.Core;
using Verse;

namespace RimworldStatistics;

/// <summary>
/// Background precomputation (MOD settings, "processing resources"), polled on the main thread by
/// <see cref="Patches.PrecomputePatch"/>. When it is on, the record is opened on the title screen and, a moment after it
/// last changed, what the statistics windows build first is built on a below-normal-priority pool thread
/// (<see cref="StatisticsPrecompute"/>). Nothing starts while saves are imported, a backup is written or restored, or the
/// game loads or saves. Off, it costs one field read per frame.
/// </summary>
internal static class PrecomputeService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly StatisticsPrecomputer Precomputer = new();
    private static TimeSpan nextPoll;
    private static bool active;   // on, or a build started while it was on has not been collected yet

    /// <summary>Main thread, every frame.</summary>
    internal static bool Wanted => RimworldStatisticsMod.Settings.precomputeStatistics || active;

    /// <summary>Main thread. Checks at most four times a second whether a build is due.</summary>
    internal static void Poll()
    {
        var now = Clock.Elapsed;
        if (now < nextPoll)
            return;
        nextPoll = now + PollInterval;

        var enabled = RimworldStatisticsMod.Settings.precomputeStatistics;
        PrecomputeTarget? target = null;
        var busy = false;
        var state = Current.ProgramState;
        if (enabled && state is ProgramState.Entry or ProgramState.Playing)
        {
            // Starts opening the record if nothing did yet; a failed open is not retried here (the window reports it).
            if (ProfileService.TryGetSession(out _) is { } session)
            {
                var campaignId = state == ProgramState.Playing ? GameComponent_RimworldStatistics.CurrentOrNull?.campaignId : null;
                target = new PrecomputeTarget(session, session.Revision, RimworldStatisticsMod.Settings.includeDevMode, campaignId);
                busy = ImportRunner.Running || ProfileService.DataOperationRunning || LongEventHandler.AnyEventNowOrWaiting;
            }
        }

        var failure = Precomputer.Poll(target, busy, now);
        active = enabled || Precomputer.Running;
        if (failure is not null)
            Log.Warning("[RimworldStatistics] Precomputing the statistics failed; they are calculated when a window opens, as "
                + $"without precomputation, and precomputed again when the record changes.\n{failure}");
    }
}
