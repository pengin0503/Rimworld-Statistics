namespace RimworldStatistics.Core;
public static partial class ProfileMutations
{
    /// <summary>
    /// Adds a numeric observation, or merges a re-read of the same content (same source and key): file aliases are
    /// unioned, the time is the earliest copy's, and per metric a newer definition revision replaces the old one while,
    /// for the same revision, a worse value never hides a good one (numeric expansion design §2 "同じ入力の再解析").
    /// </summary>
    public static void UpsertMetricObservation(Profile p, MetricObservation obs)
    {
        if (obs.Source == MetricObservationSource.Checkpoint)
        {
            UpsertCheckpoint(p, obs);
            return;
        }
        var observations = p.MetricObservations;
        var isSaveFile = obs.Source == MetricObservationSource.SaveFile;
        MetricObservation? existing = null;
        var obsSharesCheckpointFile = false;
        var hasAnyCheckpoint = false;

        for (var i = 0; i < observations.Count; i++)
        {
            var o = observations[i];
            if (o.IsQuarantined)
                continue;
            if (o.Source == obs.Source && o.Key == obs.Key)
            {
                existing = o;
                break;
            }
            if (isSaveFile && o.Source == MetricObservationSource.Checkpoint)
            {
                hasAnyCheckpoint = true;
                if (!obsSharesCheckpointFile && SharesAnyFileAlias(o.FileAliases, obs.FileAliases))
                    obsSharesCheckpointFile = true;
            }
        }

        if (existing is null)
        {
            // When a SaveFile shares any file alias with a confirmed checkpoint,
            // Supersedes(other, obs, ...) and Supersedes(obs, other, ...) are both
            // guaranteed false because checkpoint-linked SaveFiles are protected.
            if (isSaveFile && !obsSharesCheckpointFile)
            {
                HashSet<(string FileName, long FileSize, DateTime LastWriteUtc)>? protectedFiles = null;
                for (var i = 0; i < observations.Count; i++)
                {
                    if (Supersedes(observations[i], obs, observations, hasAnyCheckpoint, ref protectedFiles))
                        return;
                }
                for (var i = observations.Count - 1; i >= 0; i--)
                {
                    if (Supersedes(obs, observations[i], observations, hasAnyCheckpoint, ref protectedFiles))
                        observations.RemoveAt(i);
                }
            }
            observations.Add(obs);
            if (obs.Source == MetricObservationSource.SaveSnapshot)
                CompactSaveSnapshots(p, obs);
            return;
        }
        foreach (var alias in obs.FileAliases)
            if (!ContainsFileAlias(existing.FileAliases, alias))
                existing.FileAliases.Add(alias);
        if (obs.ObservedUtc < existing.ObservedUtc)
            existing.ObservedUtc = obs.ObservedUtc;
        existing.FormatVersion = Math.Max(existing.FormatVersion, obs.FormatVersion);
        // On an upgrade from reader v5, a re-read of identical .rws bytes
        // adds the saved MOD-set digest without losing earlier good values.
        existing.ModSetHash ??= obs.ModSetHash;
        MergeMetrics(existing, obs);
    }

    /// <summary>Per metric, a newer definition revision replaces the old one; for the same revision a worse value never hides a good one.</summary>
    private static void MergeMetrics(MetricObservation existing, MetricObservation obs)
    {
        if (obs.ProviderCaptures is { } incomingCaptures)
        {
            existing.ProviderCaptures ??= new Dictionary<string, ModProviderCapture>(Ord);
            foreach (var (providerId, capture) in incomingCaptures)
                existing.ProviderCaptures[providerId] = existing.ProviderCaptures.TryGetValue(providerId, out var previous)
                    ? ModProviderCapture.Combine(previous, capture) : capture;
        }
        // Re-reading the same snapshot must not silently overwrite its earlier
        // capture-quality evidence. A conflict is represented conservatively.
        if (obs.EvidenceByAcquisition is { } received)
        {
            existing.EvidenceByAcquisition ??= new Dictionary<string, MetricEvidence>(Ord);
            foreach (var (unit, evidence) in received)
                existing.EvidenceByAcquisition[unit] = existing.EvidenceByAcquisition.TryGetValue(unit, out var previous)
                    ? MetricEvidencePolicy.Combine(previous, evidence) : evidence;
        }
        if (obs.CompactedEvidenceByMetric is { } compacted)
        {
            existing.CompactedEvidenceByMetric ??= new Dictionary<string, MetricEvidence>(Ord);
            foreach (var (key, evidence) in compacted)
                existing.CompactedEvidenceByMetric[key] = existing.CompactedEvidenceByMetric.TryGetValue(key, out var previous)
                    ? MetricEvidencePolicy.Combine(previous, evidence) : evidence;
        }
        foreach (var (key, entry) in obs.Metrics)
        {
            if (entry.IsQuarantined)
                continue;
            if (!existing.Metrics.TryGetValue(key, out var old) || old.IsQuarantined || entry.Revision > old.Revision
                || (entry.Revision == old.Revision && Quality(entry) > Quality(old)))
            {
                existing.Metrics[key] = entry;
            }
            else if (existing.Source == MetricObservationSource.Checkpoint && entry.Revision == old.Revision
                && old.Values.TryGetValue(MetricEntry.Scalar, out var previous)
                && entry.Values.TryGetValue(MetricEntry.Scalar, out var incoming)
                // Re-decoding the same immutable checkpoint may reveal new breakdown
                // evidence. Do not combine children if the parent scalar changed.
                && previous.Coverage == incoming.Coverage
                && previous.IntegerValue == incoming.IntegerValue && previous.RealValue == incoming.RealValue
                && previous.Numerator == incoming.Numerator && previous.Denominator == incoming.Denominator)
            {
                foreach (var (child, value) in entry.Values)
                    if (child != MetricEntry.Scalar && !old.Values.ContainsKey(child))
                        old.Values.Add(child, value);
            }
        }
    }

    private static readonly IReadOnlyDictionary<string, int> SaveFileRevisions = MetricRegistry.BaseCatalog
        .Where(d => d.Source == MetricObservationSource.SaveFile)
        .GroupBy(d => d.StoredKey, Ord)
        .ToDictionary(g => g.Key, g => g.First().Revision, Ord);

    /// <summary>Whether a later equivalent save can replace the older representative without discarding independent evidence.</summary>
    private static bool Supersedes(MetricObservation newer, MetricObservation older,
        List<MetricObservation> observations, bool hasAnyCheckpoint,
        ref HashSet<(string FileName, long FileSize, DateTime LastWriteUtc)>? checkpointFiles)
    {
        if (newer.IsQuarantined || older.IsQuarantined || ReferenceEquals(newer, older)
            || newer.Source != MetricObservationSource.SaveFile || older.Source != MetricObservationSource.SaveFile
            || newer.SaveTick <= older.SaveTick)
            return false;
        // A pair of different or unavailable saved MOD sets cannot prove
        // equivalence. Preserve both observations instead of compacting them.
        if (newer.ModSetHash != older.ModSetHash)
            return false;
        if (ModCount(newer) != ModCount(older))
            return false;
        ColonyIdentity a = newer.Identity, b = older.Identity;
        if (a.FingerprintHash() is not { } hash || hash != b.FingerprintHash() || a.ModCampaignId != b.ModCampaignId
            || !SameStringSet(a.StartingPawnIds, b.StartingPawnIds))
            return false;

        // Historical fallback uses independently captured SaveFile rows for
        // snapshot metrics (including extrema). Preserve every such sample.
        // Also retain anything this reader cannot prove it understands.
        if (older.EvidenceByAcquisition is { Count: > 0 } || HasOpaqueData(older)
            || HasUnknownOrOldSaveFileMetrics(older))
            return false;

        // A checkpoint may represent a direct parent of another checkpoint.
        // Its exact .rws counter observation must not be compacted away, even
        // when the later save has the same MOD count and higher tick.
        if (hasAnyCheckpoint)
        {
            checkpointFiles ??= BuildProtectedCheckpointFiles(observations);
            if (SharesProtectedFile(newer.FileAliases, checkpointFiles)
                || SharesProtectedFile(older.FileAliases, checkpointFiles))
                return false;
        }
        return true;
    }

    private static bool SharesAnyFileAlias(List<MetricFileAlias> a, List<MetricFileAlias> b)
    {
        for (var i = 0; i < a.Count; i++)
        {
            var x = a[i];
            for (var j = 0; j < b.Count; j++)
            {
                var y = b[j];
                if (x.Same(y.FileName, y.FileSize, y.LastWriteUtc))
                    return true;
            }
        }
        return false;
    }

    private static HashSet<(string FileName, long FileSize, DateTime LastWriteUtc)> BuildProtectedCheckpointFiles(
        List<MetricObservation> observations)
    {
        var set = new HashSet<(string FileName, long FileSize, DateTime LastWriteUtc)>();
        for (var i = 0; i < observations.Count; i++)
        {
            var o = observations[i];
            if (o.IsQuarantined || o.Source != MetricObservationSource.Checkpoint)
                continue;
            var aliases = o.FileAliases;
            for (var j = 0; j < aliases.Count; j++)
            {
                var a = aliases[j];
                set.Add((a.FileName, a.FileSize, a.LastWriteUtc));
            }
        }
        return set;
    }

    private static bool SharesProtectedFile(List<MetricFileAlias> aliases,
        HashSet<(string FileName, long FileSize, DateTime LastWriteUtc)> protectedFiles)
    {
        for (var i = 0; i < aliases.Count; i++)
        {
            var a = aliases[i];
            if (protectedFiles.Contains((a.FileName, a.FileSize, a.LastWriteUtc)))
                return true;
        }
        return false;
    }

    private static bool HasUnknownOrOldSaveFileMetrics(MetricObservation older)
    {
        foreach (var kv in older.Metrics)
        {
            if (!SaveFileRevisions.TryGetValue(kv.Key, out var revision)
                || kv.Value.IsQuarantined || kv.Value.Revision != revision)
                return true;
        }
        return false;
    }

    /// <summary>The stored mod count and its revision; a missing or unreadable entry is its own state.</summary>
    private static (bool Readable, long? Count, int Revision) ModCount(MetricObservation o)
        => o.Metrics.TryGetValue(MetricKeys.ModsLatest, out var e)
            ? (!e.IsQuarantined, e.IsQuarantined ? null : e.Values.GetValueOrDefault(MetricEntry.Scalar)?.IntegerValue, e.Revision)
            : (true, null, 0);

    /// <summary>Good (complete, snapshot or not applicable) above partial above unknown; the worst value of the entry counts.</summary>
    private static int Quality(MetricEntry e)
        => e.Values.Count == 0 ? 2 : e.Values.Values.Min(v => v.Coverage switch
        {
            MetricCoverage.Unknown => 0,
            MetricCoverage.Partial => 1,
            _ => 2,
        });
}
