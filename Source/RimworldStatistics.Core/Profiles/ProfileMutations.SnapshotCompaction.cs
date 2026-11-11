namespace RimworldStatistics.Core;
public static partial class ProfileMutations
{
    /// <summary>
    /// SaveSnapshot selection has two kinds of consumers: a single latest observation,
    /// and per-key historical maxima/minima. Keep each selection's actual witness,
    /// plus one missing/partial witness when required for coverage. Accumulate the
    /// sample multiplicity of removed observations on the winning witness so the
    /// historical row still reports the real number of measurements.
    ///
    /// This is limited to a fully identified live campaign and the exact starting
    /// pawn set. Unknown future metric keys are never compacted away.
    /// </summary>
    private static void CompactSaveSnapshots(Profile p, MetricObservation added)
    {
        var identity = added.Identity;
        if (string.IsNullOrEmpty(identity.ModCampaignId)
            || identity.FingerprintHash() is not { } hash || identity.StartingPawnIds.Count == 0)
            return;

        // Unattributed records are counted individually in the Details tab. Do
        // not delete any until a matching live colony is registered. A conflicting
        // campaign identity must likewise remain untouched for later repair.
        var owners = p.LiveCampaigns.Where(c => c.CampaignId == identity.ModCampaignId).ToList();
        if (owners.Count != 1 || owners[0].Identity.FingerprintHash() != hash
            || p.SaveObservations.Any(o => o.Identity.ModCampaignId == identity.ModCampaignId
                && o.Identity.FingerprintHash() is { } otherHash && otherHash != hash))
            return;

        var pawns = identity.StartingPawnIds.ToHashSet(Ord);
        var snapshots = p.MetricObservations.Where(o => !o.IsQuarantined
            && o.Source == MetricObservationSource.SaveSnapshot
            && o.Identity.ModCampaignId == identity.ModCampaignId
            && o.Identity.FingerprintHash() == hash
            && pawns.SetEquals(o.Identity.StartingPawnIds)).ToList();
        if (snapshots.Count < 2)
            return;

        var definitions = SnapshotCatalog.Definitions.Concat(
            MetricRegistry.Compose(p.ModMetricDescriptors,
                new HashSet<string>(Ord) { MetricRegistry.OptionalModVisibilityId })
                .Where(d => d.ProviderId is not null && d.Source == MetricObservationSource.SaveSnapshot))
            .ToArray();
        var definitionsByKey = definitions.ToDictionary(d => d.StoredKey, Ord);
        var kept = new HashSet<MetricObservation>();
        // Representative selection: latest tick, then UTC, then smallest key.
        var representative = MetricObservationOrder.Latest(snapshots)!;
        kept.Add(representative);
        foreach (var o in snapshots)
            // Preserve future extension fields, unknown keys, unreadable entries and
            // different revisions: the current catalogue cannot prove them redundant.
            if (HasOpaqueData(o) || o.Metrics.Any(kv => !definitionsByKey.TryGetValue(kv.Key, out var d)
                || kv.Value.IsQuarantined || kv.Value.Revision != d.Revision))
                kept.Add(o);

        MetricValue? Scalar(MetricObservation o, MetricDefinition d)
            => o.Metrics.TryGetValue(d.StoredKey, out var e)
                && !e.IsQuarantined && e.Revision == d.Revision
                ? e.Values.GetValueOrDefault(MetricEntry.Scalar) : null;

        bool Applicable(MetricObservation o, MetricDefinition d)
            => d.RequiredDlc is not { } dlc || o.ActiveDlcIds is null
                || o.ActiveDlcIds.Contains(dlc, StringComparer.OrdinalIgnoreCase)
                || MetricCalculator.MeasuredNumber(Scalar(o, d)) is { } n && n != 0;

        foreach (var d in definitions)
        {
            if (d.Selection == ColonySelection.Representative)
            {
                if (SnapshotSelectionPolicy.UsesLastCapture(d))
                {
                    // A later save may have been made after an optional provider
                    // or the experimental faction acquisition unit was disabled.
                    // Preserve its last actual capture; otherwise the Details
                    // view could mistake it for never-recorded and substitute
                    // weaker SaveFile historical fallback evidence.
                    var lastCaptured = MetricObservationOrder.Latest(snapshots.Where(o => Scalar(o, d) is not null));
                    if (lastCaptured is not null)
                        kept.Add(lastCaptured);
                }
                continue;
            }
            var applicable = snapshots.Where(o => Applicable(o, d)).ToList();
            if (applicable.Count == 0)
                continue;
            var valid = applicable.Where(o => MetricCalculator.MeasuredNumber(Scalar(o, d)) is not null).ToList();
            if (valid.Count == 0)
            {
                // A known DLC session but no measurable value means Unknown, not NotApplicable.
                kept.Add(applicable[0]);
                continue;
            }
            // Stable LINQ ordering preserves the original earliest winner on a tie.
            var best = d.Selection == ColonySelection.ObservationMax
                ? valid.OrderByDescending(o => Scalar(o, d)!.Number).First()
                : valid.OrderBy(o => Scalar(o, d)!.Number).First();
            kept.Add(best);
            var partial = valid.FirstOrDefault(o => Scalar(o, d)!.Coverage == MetricCoverage.Partial);
            if (partial is not null)
                kept.Add(partial);
            var missing = applicable.FirstOrDefault(o => Scalar(o, d)?.Number is null);
            if (missing is not null)
                kept.Add(missing);
        }

        // Weights must be moved before removing the observations that own them. A
        // previously compacted winner may itself cease to be an extremum later.
        foreach (var d in definitions)
        {
            if (d.Selection == ColonySelection.Representative)
                continue;
            var valid = snapshots.Where(o => Applicable(o, d) && MetricCalculator.MeasuredNumber(Scalar(o, d)) is not null).ToList();
            if (valid.Count == 0)
                continue;
            var best = d.Selection == ColonySelection.ObservationMax
                ? valid.OrderByDescending(o => Scalar(o, d)!.Number).First()
                : valid.OrderBy(o => Scalar(o, d)!.Number).First();
            long Weight(MetricObservation o) => Math.Max(1, o.CompactedSampleCounts?.GetValueOrDefault(d.StoredKey) ?? 1);
            var removedRows = valid.Where(o => !kept.Contains(o)).ToList();
            var removed = removedRows.Sum(Weight);
            if (removed <= 0)
                continue;

            // Carry the capture-time quality of removed samples into the surviving
            // extremum witness. Otherwise a later verified capture could silently
            // hide earlier experimental observations of the same maximum/minimum.
            MetricEvidence Captured(MetricObservation row)
            {
                var direct = d.Acquisition is { } id && row.EvidenceByAcquisition is { } evidence
                    && evidence.TryGetValue(id, out var e) ? e : MetricEvidence.UnknownLegacy;
                return row.CompactedEvidenceByMetric is { } metadata
                    && metadata.TryGetValue(d.StoredKey, out var prior)
                    ? MetricEvidencePolicy.Combine(direct, prior) : direct;
            }
            var values = removedRows.Select(Captured).Append(Captured(best));
            best.CompactedEvidenceByMetric ??= new Dictionary<string, MetricEvidence>(Ord);
            best.CompactedEvidenceByMetric[d.StoredKey] = MetricEvidencePolicy.Combine(values);
            if (d.ProviderId is { } providerId)
            {
                best.ProviderCaptures ??= new Dictionary<string, ModProviderCapture>(Ord);
                foreach (var row in removedRows)
                {
                    if (row.ProviderCaptures?.GetValueOrDefault(providerId) is not { } capture)
                        continue;
                    best.ProviderCaptures[providerId] = best.ProviderCaptures.TryGetValue(providerId, out var previous)
                        ? ModProviderCapture.Combine(previous, capture) : capture;
                }
            }
            best.CompactedSampleCounts ??= new Dictionary<string, int>(Ord);
            best.CompactedSampleCounts[d.StoredKey] =
                (int)Math.Min(int.MaxValue, Weight(best) + removed);
        }
        // Retaining only the physical rows would understate the number of
        // observations shown in Details, and after a later missing attribution
        // would also understate Unattributed. Carry the deleted rows' multiplicity
        // on the latest survivor, independently of the per-metric sample counts.
        var removedObservations = snapshots.Where(o => !kept.Contains(o)).ToList();
        if (removedObservations.Count > 0)
        {
            var total = (long)Math.Max(1, representative.CompactedObservationCount ?? 1)
                + removedObservations.Sum(o => (long)Math.Max(1, o.CompactedObservationCount ?? 1));
            representative.CompactedObservationCount = (int)Math.Min(int.MaxValue, total);
        }
        p.MetricObservations.RemoveAll(o => snapshots.Contains(o) && !kept.Contains(o));
    }

    // JsonExtensionData represents fields from a minor version this binary does
    // not understand. Discarding their owner would silently destroy information.
    private static bool HasOpaqueData(MetricObservation o)
        => o.ExtensionData is { Count: > 0 }
            || o.Identity.ExtensionData is { Count: > 0 }
            || o.FileAliases.Any(a => a.ExtensionData is { Count: > 0 })
            || o.ProviderCaptures?.Values.Any(c => c?.ExtensionData is { Count: > 0 }) == true
            || o.Metrics.Values.Any(e => e.ExtensionData is { Count: > 0 }
                || e.Values.Values.Any(v => v.ExtensionData is { Count: > 0 }));

}
