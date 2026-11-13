namespace RimworldStatistics.Core;

/// <summary>
/// A bounded interval between two observed saves with a *direct* embedded
/// checkpoint parent edge. It is not an independently observed sequence of
/// events, nor a lifetime total. Sibling intervals MUST NOT be summed.
/// </summary>
public sealed record HistoricalCounterInterval(
    string ParentCheckpointId, string ChildCheckpointId,
    int StartTick, int EndTick, long RaidsIncrease, long ColonistDeathsIncrease)
{
    public MetricCoverage Coverage => MetricCoverage.Partial;
    public string? ColonyName { get; init; }
    public string? ParentSaveName { get; init; }
    public string? ChildSaveName { get; init; }
}

/// <summary>
/// Optional, read-only historical evidence from vanilla cumulative counters.
/// Only trusted checkpoint parent edges with exact matching saved MOD lists,
/// game versions, identities and source file aliases may be compared.
/// Incomparable files produce no interval; no amount is written into live
/// metrics, so forks and duplicate saves can never inflate the statistics.
/// </summary>
public static class HistoricalCounterIntervals
{
    private static readonly StringComparer Ord = StringComparer.Ordinal;

    internal sealed record Point(MetricObservationSource Source, string Key, string? ParentKey,
        string? PayloadHash, int SaveTick, string? GameVersion, string? ModSetHash, string[]? ActiveDlcIds,
        ColonyIdentity Identity, bool Conflicted, SaveAlias[] Aliases, long? Raids, long? Deaths);

    internal sealed record Inputs(Point[] Observations, IReadOnlyDictionary<string, string> ColonyNames);

    // Capture only fields needed by the diagnostic, while the session lock is held.
    internal static Inputs Capture(Profile profile)
    {
        var names = new Dictionary<string, (int Tick, string Name)>(Ord);
        void Remember(ColonyIdentity identity, int tick, DisplayInfo display)
        {
            if (identity.ModCampaignId is not { Length: > 0 } id
                || (display.FactionName ?? display.WorldName) is not { Length: > 0 } name)
                return;
            if (!names.TryGetValue(id, out var previous) || tick >= previous.Tick)
                names[id] = (tick, name);
        }
        foreach (var save in profile.SaveObservations)
            if (save.Status == ObservationStatus.Ok)
                Remember(save.Identity, save.Values.TicksGame, save.Display);
        foreach (var live in profile.LiveCampaigns)
            if (live.HasObservation)
                Remember(live.Identity, live.Max.TicksGame, live.Display);

        var observations = profile.MetricObservations
            .Where(o => !o.IsQuarantined && o.Source is MetricObservationSource.SaveFile or MetricObservationSource.Checkpoint)
            .Select(o => new Point(o.Source, o.Key, o.ParentKey, o.PayloadHash, o.SaveTick, o.GameVersion,
                o.ModSetHash, o.ActiveDlcIds?.ToArray(), o.Identity.Clone(), o.Conflicted,
                o.FileAliases.Select(a => new SaveAlias(o.SaveTick, o.GameVersion, a.FileName, a.FileSize, a.LastWriteUtc)).ToArray(),
                o.Source == MetricObservationSource.SaveFile ? Counter(o, MetricKeys.RaidsPerYear) : null,
                o.Source == MetricObservationSource.SaveFile ? Counter(o, MetricKeys.DeathsPerYear) : null))
            .ToArray();

        return new Inputs(observations, names.ToDictionary(pair => pair.Key, pair => pair.Value.Name, Ord));
    }

    public static IReadOnlyList<HistoricalCounterInterval> Evaluate(Profile profile) => Evaluate(Capture(profile));

    private static IReadOnlyList<HistoricalCounterInterval> Evaluate(Inputs inputs)
    {
        var checkpoints = inputs.Observations.Where(o => o.Source == MetricObservationSource.Checkpoint).ToArray();
        // An indexed alias match preserves distinct source observations while allowing byte-identical copies.
        var saves = inputs.Observations.Where(o => o.Source == MetricObservationSource.SaveFile)
            .SelectMany(o => o.Aliases.Select(a => (Key: a, Save: o)))
            .ToLookup(x => x.Key, x => x.Save);
        var uniqueCheckpoints = checkpoints.GroupBy(o => o.Key, Ord)
            .Where(g => !string.IsNullOrEmpty(g.Key) && g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single(), Ord);

        var found = new List<HistoricalCounterInterval>();
        foreach (var child in checkpoints)
        {
            // Compression hides redundant aggregates but preserves immutable parent
            // links; compressed checkpoints are still valid ancestry evidence.
            if (child.Conflicted || child.ParentKey is not { Length: > 0 } parentId
                || parentId == child.Key || !uniqueCheckpoints.TryGetValue(child.Key, out var onlyChild)
                || !ReferenceEquals(onlyChild, child)
                || !uniqueCheckpoints.TryGetValue(parentId, out var parent)
                || parent.Conflicted || parent.ParentKey == child.Key
                || parent.SaveTick <= 0 || child.SaveTick <= parent.SaveTick
                || string.IsNullOrWhiteSpace(parent.PayloadHash) || string.IsNullOrWhiteSpace(child.PayloadHash)
                || !ValidHash(parent.PayloadHash) || !ValidHash(child.PayloadHash)
                || !SameColony(parent.Identity, child.Identity)
                || !string.Equals(parent.GameVersion, child.GameVersion, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(child.GameVersion))
                continue;

            var from = MatchSingleSave(parent, saves);
            var through = MatchSingleSave(child, saves);
            if (from is null || through is null || ReferenceEquals(from, through)
                || !SameColony(from.Identity, through.Identity)
                || !SameColony(from.Identity, parent.Identity)
                || !SameColony(through.Identity, child.Identity)
                || from.SaveTick != parent.SaveTick || through.SaveTick != child.SaveTick
                || from.GameVersion != parent.GameVersion || through.GameVersion != child.GameVersion
                || !ValidHash(from.ModSetHash) || from.ModSetHash != through.ModSetHash
                || !SameSavedDlc(from.ActiveDlcIds, through.ActiveDlcIds)
                || !SameSavedDlc(from.ActiveDlcIds, parent.ActiveDlcIds)
                || !SameSavedDlc(through.ActiveDlcIds, child.ActiveDlcIds))
                continue;

            var fromRaids = from.Raids;
            var toRaids = through.Raids;
            var fromDeaths = from.Deaths;
            var toDeaths = through.Deaths;
            if (fromRaids is null || toRaids is null || fromDeaths is null || toDeaths is null
                || toRaids < fromRaids || toDeaths < fromDeaths)
                continue;

            found.Add(new HistoricalCounterInterval(parent.Key, child.Key,
                parent.SaveTick, child.SaveTick, toRaids.Value - fromRaids.Value,
                toDeaths.Value - fromDeaths.Value)
            {
                ColonyName = inputs.ColonyNames.GetValueOrDefault(child.Identity.ModCampaignId!),
                ParentSaveName = BoundaryName(parent, from),
                ChildSaveName = BoundaryName(child, through),
            });
        }
        return found.OrderBy(x => x.ParentCheckpointId, Ord)
            .ThenBy(x => x.ChildCheckpointId, Ord).ToArray();
    }

    /// <summary>
    /// On-demand diagnostic rows, newest first. Only proven intervals with a
    /// non-zero change are displayed, with no cumulative total across forks.
    /// </summary>
    public static IReadOnlyList<HistoricalCounterInterval> ForDiagnostics(Profile profile)
        => ForDiagnostics(Capture(profile));

    internal static IReadOnlyList<HistoricalCounterInterval> ForDiagnostics(Inputs inputs)
        => Evaluate(inputs).Where(row => row.RaidsIncrease > 0 || row.ColonistDeathsIncrease > 0)
            .OrderByDescending(row => row.EndTick).ThenByDescending(row => row.StartTick).ToArray();

    internal readonly record struct SaveAlias(int Tick, string? Version, string File, long Size, DateTime LastWriteUtc);

    private static Point? MatchSingleSave(Point checkpoint, ILookup<SaveAlias, Point> saves)
    {
        Point? match = null;
        foreach (var alias in checkpoint.Aliases)
            foreach (var candidate in saves[alias])
            {
                if (ReferenceEquals(match, candidate) || !SameColony(candidate.Identity, checkpoint.Identity))
                    continue;
                if (match is not null)
                    return null;
                match = candidate;
            }
        return match;
    }

    private static string? BoundaryName(Point checkpoint, Point save)
        => checkpoint.Aliases.Intersect(save.Aliases).Select(a => a.File)
            .Where(name => name.Length > 0).Order(Ord).FirstOrDefault();

    private static bool SameColony(ColonyIdentity a, ColonyIdentity b)
        => a.ModCampaignId is { Length: > 0 } id && id == b.ModCampaignId
            && a.FingerprintHash() is { } hash && hash == b.FingerprintHash()
            && a.StartingPawnIds.ToHashSet(Ord).SetEquals(b.StartingPawnIds);

    // Saved MOD-list digests are authoritative for compatibility. This
    // additional consistency check rejects contradictory/corrupted DLC
    // metadata even if a digest was copied across observations.
    internal static bool SameSavedDlc(IReadOnlyCollection<string>? a, IReadOnlyCollection<string>? b)
    {
        if (a is null || b is null)
            return a is null && b is null;
        return a.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(b);
    }

    private static bool ValidHash(string? s)
        => s is { Length: 64 } && s.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static long? Counter(MetricObservation o, string key)
    {
        var value = o.Scalar(key, MetricKeys.Revision(key));
        if (value?.Coverage != MetricCoverage.Complete
            || value.Numerator is not { } n || !double.IsFinite(n)
            || n < 0 || n > int.MaxValue || Math.Floor(n) != n)
            return null;
        return (long)n;
    }
}
