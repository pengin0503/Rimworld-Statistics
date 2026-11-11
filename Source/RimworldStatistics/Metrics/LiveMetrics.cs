using System.Collections.Concurrent;
using RimworldStatistics.Core;
using Verse;

namespace RimworldStatistics.Metrics;

/// <summary>
/// The running totals of the current game, for the adapters (numeric expansion design §3 "Cの確定プロトコル" step 1). Events are
/// accepted only after <c>StartedNewGame</c> or <c>LoadedGame</c> of the game being played (so map generation, starting pawns
/// and starting research are never counted) and only on the game thread. An event that arrives elsewhere is not recorded,
/// and its family is marked as having a gap.
/// </summary>
public static class LiveMetrics
{
    private static Game? game;
    private static LiveMetricAccumulator? accumulator;
    private static CheckpointSaveSession? session;
    private static readonly ConcurrentDictionary<string, bool> Missed = new(StringComparer.Ordinal);
    private static Dictionary<string, int>? hitCounts;

    /// <summary>The totals of the game being played, or null (no game, another game, or still setting up).</summary>
    public static LiveMetricAccumulator? Accumulator => IsCurrent ? accumulator : null;

    public static CheckpointSaveSession? Session => IsCurrent ? session : null;

    private static bool IsCurrent => game != null && ReferenceEquals(Current.Game, game);

    /// <summary>A new game: called from StartedNewGame, after the scenario, starting research and starting pawns are in place.</summary>
    public static void StartNew(Game g)
    {
        Missed.Clear();
        var acc = LiveMetricAccumulator.StartNew(MetricGates.RecordingFamilies(), Find.TickManager.TicksGame, MetricGates.RecordingExperimentalUnits());
        ModProviderAdapters.AttachEventOrigins(acc);
        Install(g, acc);
    }

    /// <summary>A loaded game: called from LoadedGame with the save's numericStatistics text (null when it has none).</summary>
    public static LiveMetricAccumulator Resume(Game g, string? raw)
    {
        Missed.Clear();
        var acc = LiveMetricAccumulator.Resume(raw, MetricGates.RecordingFamilies(), Find.TickManager.TicksGame, MetricGates.RecordingExperimentalUnits());
        ModProviderAdapters.AttachEventOrigins(acc);
        Install(g, acc);
        return acc;
    }

    private static void Install(Game g, LiveMetricAccumulator acc)
    {
        // HitCounts are diagnostic counts for the current game, not a global
        // process-wide total. Reset when starting or resuming another colony.
        hitCounts?.Clear();
        accumulator = acc;
        session = new CheckpointSaveSession(acc);
        game = g;
        FamilyStartup.Apply(acc, Find.TickManager.TicksGame);
    }

    /// <summary>Whether this family is recording on this thread. This query does not change the recording.</summary>
    public static bool IsRecordingNow(string name) => Resolve(name, eventAttempt: false) != null;

    /// <summary>Whether the hook can capture an event. An off-thread attempt may make the family's coverage partial.</summary>
    internal static bool CanCaptureEvent(string name) => Resolve(name, eventAttempt: true) != null;

    /// <summary>Returns the recording state and counts one hit when it is available.</summary>
    public static FamilyState? Family(string name)
    {
        var family = Resolve(name, eventAttempt: true);
        if (family != null && hitCounts is { } hits)
            hits[name] = hits.GetValueOrDefault(name) + 1;
        return family;
    }

    /// <summary>
    /// The totals an adapter adds to, or null when the family does not record now. Off the game thread an event is never
    /// applied (the totals are not thread-safe); the family is marked as having a gap instead.
    /// </summary>
    private static FamilyState? Resolve(string name, bool eventAttempt)
    {
        var acc = Accumulator;
        if (acc is null)
            return null;
        if (!UnityData.IsInMainThread)
        {
            if (eventAttempt && acc.Recording.Contains(name))
                Missed[name] = true;
            return null;
        }
        if (eventAttempt && !Missed.IsEmpty && Missed.TryRemove(name, out _))
            acc.MarkGap(name, Find.TickManager.TicksGame);
        return acc.Family(name);
    }

    /// <summary>A hook of <paramref name="unit"/> threw: close the unit and keep what it recorded, measured through now.</summary>
    public static void Fail(string unit, Exception e)
    {
        MetricGates.Fail(unit, e);
        try
        {
            if (Accumulator is { } acc)
                acc.Stop(unit, Find.TickManager.TicksGame);
        }
        catch (Exception)
        {
            // the unit is already closed; the totals stay as they were
        }
    }

    /// <summary>Applies pending gap marks before a save writes the totals (game thread).</summary>
    internal static void FlushMissed()
    {
        if (Missed.IsEmpty || Accumulator is not { } acc)
            return;
        foreach (var name in Missed.Keys.ToList())
            if (Missed.TryRemove(name, out _))
                acc.MarkGap(name, Find.TickManager.TicksGame);
    }

    /// <summary>The save-time recounts of the families (game thread only; a save elsewhere skips them).</summary>
    internal static void BeforeSave()
    {
        if (!UnityData.IsInMainThread || Accumulator is not { } acc)
            return;
        FamilyStartup.BeforeSave(acc, Find.TickManager.TicksGame);
    }

    /// <summary>Turns on diagnostic state handout counts for an isolated verification session.</summary>
    public static void EnableHitDiagnostics() => hitCounts ??= new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>How often each family handed out its state for recording this game, when diagnostics are enabled.</summary>
    public static IReadOnlyDictionary<string, int> HitCounts() => hitCounts is { } hits
        ? new Dictionary<string, int>(hits, StringComparer.Ordinal)
        : new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>The DLC package ids active now, in the form saves list them (meta/modIds).</summary>
    public static List<string> ActiveDlcIds()
        => LoadedModManager.RunningMods.Select(m => m.PackageId?.ToLowerInvariant())
            .Where(id => id != null && id.StartsWith("ludeon.rimworld.", StringComparison.Ordinal))
            .Select(id => id!).Distinct().Order(StringComparer.Ordinal).ToList();
}
