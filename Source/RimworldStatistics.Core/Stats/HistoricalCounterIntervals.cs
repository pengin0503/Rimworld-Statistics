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

    public static IReadOnlyList<HistoricalCounterInterval> Evaluate(Profile profile)
    {
        var all = profile.MetricObservations.Where(o => !o.IsQuarantined).ToArray();
        var checkpoints = all.Where(o => o.Source == MetricObservationSource.Checkpoint).ToArray();
        var saves = all.Where(o => o.Source == MetricObservationSource.SaveFile).ToArray();
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

            var fromRaids = Counter(from, MetricKeys.RaidsPerYear);
            var toRaids = Counter(through, MetricKeys.RaidsPerYear);
            var fromDeaths = Counter(from, MetricKeys.DeathsPerYear);
            var toDeaths = Counter(through, MetricKeys.DeathsPerYear);
            if (fromRaids is null || toRaids is null || fromDeaths is null || toDeaths is null
                || toRaids < fromRaids || toDeaths < fromDeaths)
                continue;

            found.Add(new HistoricalCounterInterval(parent.Key, child.Key,
                parent.SaveTick, child.SaveTick, toRaids.Value - fromRaids.Value,
                toDeaths.Value - fromDeaths.Value));
        }
        return found.OrderBy(x => x.ParentCheckpointId, Ord)
            .ThenBy(x => x.ChildCheckpointId, Ord).ToArray();
    }

    private static MetricObservation? MatchSingleSave(
        MetricObservation checkpoint, IEnumerable<MetricObservation> saves)
    {
        if (checkpoint.FileAliases.Count == 0)
            return null;
        // A saved checkpoint payload can be reused on later saves while
        // recording is disabled. Its alias list then contains several .rws
        // files, but only the one with the checkpoint's own tick is evidence
        // of the original boundary. Filter candidates before uniqueness.
        var matching = saves.Where(save =>
            save.SaveTick == checkpoint.SaveTick
            && save.GameVersion == checkpoint.GameVersion
            && SameColony(save.Identity, checkpoint.Identity)
            && save.FileAliases.Any(a => checkpoint.FileAliases.Any(
                b => a.Same(b.FileName, b.FileSize, b.LastWriteUtc))))
            .Take(2).ToList();
        return matching.Count == 1 ? matching[0] : null;
    }

    private static bool SameColony(ColonyIdentity a, ColonyIdentity b)
        => a.ModCampaignId is { Length: > 0 } id && id == b.ModCampaignId
            && a.FingerprintHash() is { } hash && hash == b.FingerprintHash()
            && a.StartingPawnIds.ToHashSet(Ord).SetEquals(b.StartingPawnIds);

    // Saved MOD-list digests are authoritative for compatibility. This
    // additional consistency check rejects contradictory/corrupted DLC
    // metadata even if a digest was copied across observations.
    private static bool SameSavedDlc(IReadOnlyCollection<string>? a, IReadOnlyCollection<string>? b)
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
