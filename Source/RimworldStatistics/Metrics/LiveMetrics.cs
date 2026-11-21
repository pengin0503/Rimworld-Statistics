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
    private sealed record DeferredEvent(Game TargetGame, string Unit, Action<FamilyState>? Handler);

    private static readonly object SnapshotGate = new();
    private static readonly object HitCountGate = new();
    private static Game? game;
    private static LiveMetricAccumulator? accumulator;
    private static CheckpointSaveSession? session;
    private static volatile HashSet<string> recordingSnapshot = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, bool> FailedPendingStop = new(StringComparer.Ordinal);
    private static readonly ConcurrentQueue<DeferredEvent> Deferred = new();
    private static Dictionary<string, int>? hitCounts;
    [ThreadStatic] private static bool flushingDeferred;

    /// <summary>The totals of the game being played, or null (no game, another game, or still setting up).</summary>
    public static LiveMetricAccumulator? Accumulator => IsCurrent ? Volatile.Read(ref accumulator) : null;

    public static CheckpointSaveSession? Session => IsCurrent ? Volatile.Read(ref session) : null;

    private static bool IsCurrent => Volatile.Read(ref game) is { } activeGame && ReferenceEquals(Current.Game, activeGame);

    /// <summary>A new game: called from StartedNewGame, after the scenario, starting research and starting pawns are in place.</summary>
    public static void StartNew(Game g)
    {
        FailedPendingStop.Clear();
        ClearDeferred();
        var acc = LiveMetricAccumulator.StartNew(MetricGates.RecordingFamilies(), Find.TickManager.TicksGame, MetricGates.RecordingExperimentalUnits());
        ModProviderAdapters.AttachEventOrigins(acc);
        Install(g, acc);
    }

    /// <summary>A loaded game: called from LoadedGame with the save's numericStatistics text (null when it has none).</summary>
    public static LiveMetricAccumulator Resume(Game g, string? raw)
    {
        FailedPendingStop.Clear();
        ClearDeferred();
        var acc = LiveMetricAccumulator.Resume(raw, MetricGates.RecordingFamilies(), Find.TickManager.TicksGame, MetricGates.RecordingExperimentalUnits());
        ModProviderAdapters.AttachEventOrigins(acc);
        Install(g, acc);
        return acc;
    }

    /// <summary>Releases live game references and queued closures when returning to the main menu.</summary>
    internal static void ClearForMainMenu()
    {
        Volatile.Write(ref game, null);
        Volatile.Write(ref accumulator, null);
        Volatile.Write(ref session, null);
        SyncRecordingSnapshot(null);
        FailedPendingStop.Clear();
        ClearDeferred();
        lock (HitCountGate)
            hitCounts?.Clear();
    }

    private static void ClearDeferred()
    {
        while (Deferred.TryDequeue(out _))
        {
        }
    }

    private static void Install(Game g, LiveMetricAccumulator acc)
    {
        // HitCounts are diagnostic counts for the current game, not a global
        // process-wide total. Reset when starting or resuming another colony.
        Volatile.Write(ref game, null);
        SyncRecordingSnapshot(null);
        lock (HitCountGate)
            hitCounts?.Clear();
        // Run startup baselines before publishing the new game so worker/other hooks
        // never observe a partially initialized accumulator.
        FamilyStartup.Apply(acc, Find.TickManager.TicksGame);
        Volatile.Write(ref accumulator, acc);
        Volatile.Write(ref session, new CheckpointSaveSession(acc));
        SyncRecordingSnapshot(acc);
        Volatile.Write(ref game, g);
    }

    internal static void SyncRecordingSnapshot(LiveMetricAccumulator? acc)
    {
        lock (SnapshotGate)
        {
            recordingSnapshot = acc is null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(acc.Recording, StringComparer.Ordinal);
        }
    }

    private static void RemoveFromRecordingSnapshot(string unit)
    {
        lock (SnapshotGate)
        {
            if (!recordingSnapshot.Contains(unit))
                return;
            var next = new HashSet<string>(recordingSnapshot, StringComparer.Ordinal);
            next.Remove(unit);
            recordingSnapshot = next;
        }
    }

    private static void DrainPendingStops(LiveMetricAccumulator acc)
    {
        if (FailedPendingStop.IsEmpty)
            return;
        var changed = false;
        foreach (var unit in FailedPendingStop.Keys.ToList())
        {
            if (!FailedPendingStop.TryRemove(unit, out _))
                continue;
            acc.Stop(unit, Find.TickManager.TicksGame);
            changed = true;
        }
        if (changed)
            SyncRecordingSnapshot(acc);
    }

    /// <summary>
    /// Reflect an acquisition-setting change immediately in the loaded game,
    /// without replacing the save session or discarding an existing checkpoint.
    /// The caller must run on the Unity game thread (the mod settings UI does).
    /// </summary>
    public static void RefreshSelection()
    {
        if (!UnityData.IsInMainThread || Accumulator is not { } acc)
            return;
        // Apply legitimate worker-thread events queued before the setting changed so they are not dropped.
        FlushDeferred();
        DrainPendingStops(acc);
        var tick = Find.TickManager.TicksGame;
        var started = acc.Reconfigure(MetricGates.RecordingFamilies(), tick,
            MetricGates.RecordingExperimentalUnits());
        SyncRecordingSnapshot(acc);
        if (started.Count == 0)
            return;
        ModProviderAdapters.AttachEventOrigins(acc);
        FamilyStartup.Apply(acc, tick, started.ToHashSet(StringComparer.Ordinal));
        SyncRecordingSnapshot(acc);
    }

    /// <summary>Whether this family is recording on this thread. This query does not change the recording.</summary>
    public static bool IsRecordingNow(string name) => Resolve(name, eventAttempt: false) != null;

    /// <summary>Thread-safe check for whether a unit is active in the current game outside Scribe, without marking gaps.</summary>
    internal static bool IsRecordingUnit(string name)
        => Scribe.mode == LoadSaveMode.Inactive && IsCurrent && recordingSnapshot.Contains(name);

    /// <summary>Whether the hook can capture an event. An off-thread attempt may make the family's coverage partial.</summary>
    internal static bool CanCaptureEvent(string name) => Resolve(name, eventAttempt: true) != null;

    /// <summary>Returns the recording state and counts one hit when it is available.</summary>
    public static FamilyState? Family(string name)
    {
        var family = Resolve(name, eventAttempt: true);
        if (family != null && Volatile.Read(ref hitCounts) is not null)
        {
            lock (HitCountGate)
            {
                if (hitCounts is { } hits)
                    hits[name] = hits.GetValueOrDefault(name) + 1;
            }
        }
        return family;
    }

    /// <summary>
    /// Records immediately on the Unity main thread, or queues a pre-extracted value-only callback when a legitimate hook
    /// (such as RimWorld 1.7 async <c>LongEvent</c> map generation) runs on a worker thread, without degrading coverage to Partial.
    /// </summary>
    internal static void RecordOrDefer(string unit, Action<FamilyState> handler)
    {
        if (Scribe.mode != LoadSaveMode.Inactive || Volatile.Read(ref game) is not { } targetGame
            || !ReferenceEquals(Current.Game, targetGame) || !recordingSnapshot.Contains(unit))
            return;
        if (UnityData.IsInMainThread)
        {
            if (Family(unit) is not { } s)
                return;
            try
            {
                handler(s);
            }
            catch (Exception e)
            {
                Fail(unit, e);
            }
            return;
        }
        Deferred.Enqueue(new DeferredEvent(targetGame, unit, handler));
    }

    /// <summary>Drains worker-thread events and gap markers queued during async LongEvents onto the Unity main thread in FIFO order.</summary>
    internal static void FlushDeferred()
    {
        if (flushingDeferred || !UnityData.IsInMainThread || Scribe.mode != LoadSaveMode.Inactive)
            return;
        if (Accumulator is { } currentAcc)
            DrainPendingStops(currentAcc);
        if (Deferred.IsEmpty)
            return;
        flushingDeferred = true;
        try
        {
            while (Deferred.TryDequeue(out var item))
            {
                if (!ReferenceEquals(item.TargetGame, Volatile.Read(ref game)) || Accumulator is not { } acc)
                    continue;
                DrainPendingStops(acc);
                if (item.Handler is null)
                {
                    acc.MarkGap(item.Unit, Find.TickManager.TicksGame);
                    continue;
                }
                if (Family(item.Unit) is not { } s)
                    continue;
                try
                {
                    item.Handler(s);
                }
                catch (Exception e)
                {
                    Fail(item.Unit, e);
                }
            }
        }
        finally
        {
            flushingDeferred = false;
        }
    }

    /// <summary>
    /// The totals an adapter adds to, or null when the family does not record now. Off the game thread an event is never
    /// applied (the totals are not thread-safe); the family is marked as having a gap instead.
    /// </summary>
    private static FamilyState? Resolve(string name, bool eventAttempt)
    {
        if (Scribe.mode != LoadSaveMode.Inactive || Volatile.Read(ref game) is not { } targetGame
            || !ReferenceEquals(Current.Game, targetGame) || Volatile.Read(ref accumulator) is not { } acc)
            return null;
        if (!UnityData.IsInMainThread)
        {
            if (eventAttempt && recordingSnapshot.Contains(name))
                Deferred.Enqueue(new DeferredEvent(targetGame, name, Handler: null));
            return null;
        }
        DrainPendingStops(acc);
        if (eventAttempt && !flushingDeferred && !Deferred.IsEmpty)
            FlushDeferred();
        return acc.Family(name);
    }

    /// <summary>A hook of <paramref name="unit"/> threw: close the unit and keep what it recorded, measured through now.</summary>
    public static void Fail(string unit, Exception e)
    {
        // While Scribe is loading/saving, isolate and log without disabling the unit for the whole session.
        if (Scribe.mode != LoadSaveMode.Inactive)
        {
            Log.WarningOnce($"[RimworldStatistics] Numeric statistics unit '{unit}' threw during save/load and was isolated.\n{e}",
                ("RWS.unit.scribe." + unit).GetHashCode());
            return;
        }
        if (!IsCurrent)
            return;
        MetricGates.Fail(unit, e);
        RemoveFromRecordingSnapshot(unit);
        try
        {
            if (UnityData.IsInMainThread && Accumulator is { } acc)
            {
                acc.Stop(unit, Find.TickManager.TicksGame);
                SyncRecordingSnapshot(acc);
            }
            else if (!UnityData.IsInMainThread)
            {
                FailedPendingStop[unit] = true;
            }
        }
        catch (Exception)
        {
            // the unit is already closed; the totals stay as they were
        }
    }

    /// <summary>Applies pending deferred events, stops, and gap marks before a save writes the totals (game thread).</summary>
    internal static void FlushMissed() => FlushDeferred();

    /// <summary>The save-time recounts of the families (game thread only; a save elsewhere skips them).</summary>
    internal static void BeforeSave()
    {
        if (!UnityData.IsInMainThread || Accumulator is not { } acc)
            return;
        DrainPendingStops(acc);
        FamilyStartup.BeforeSave(acc, Find.TickManager.TicksGame);
    }

    /// <summary>Turns on diagnostic state handout counts for an isolated verification session.</summary>
    public static void EnableHitDiagnostics()
    {
        lock (HitCountGate)
            Volatile.Write(ref hitCounts, hitCounts ?? new Dictionary<string, int>(StringComparer.Ordinal));
    }

    /// <summary>How often each family handed out its state for recording this game, when diagnostics are enabled.</summary>
    public static IReadOnlyDictionary<string, int> HitCounts()
    {
        lock (HitCountGate)
            return hitCounts is { } hits
                ? new Dictionary<string, int>(hits, StringComparer.Ordinal)
                : new Dictionary<string, int>(StringComparer.Ordinal);
    }

    /// <summary>The DLC package ids active now, in the form saves list them (meta/modIds).</summary>
    public static List<string> ActiveDlcIds()
        => LoadedModManager.RunningMods.Select(m => m.PackageId?.ToLowerInvariant())
            .Where(id => id != null && id.StartsWith("ludeon.rimworld.", StringComparison.Ordinal))
            .Select(id => id!).Distinct().Order(StringComparer.Ordinal).ToList();
}
