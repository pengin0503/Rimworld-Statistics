namespace RimworldStatistics.Core;
public static partial class ProfileMutations
{
    /// <summary>
    /// SaveSnapshot selection has three kinds of consumers: a single latest observation,
    /// per-key historical maxima/minima, and the colony history chart (one reading per
    /// save). Keep each selection's actual witness, plus one missing/partial witness when
    /// required for coverage. Accumulate the sample multiplicity of removed observations
    /// on the winning witness so the historical row still reports the real number of
    /// measurements, and move their chart readings into the bounded series carried by
    /// the latest snapshot (#318).
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
        // A chart series written by a newer version may hold fields this one cannot rebuild: compact nothing.
        if (snapshots.Any(o => HasOpaqueChart(o.CompactedChart)))
            return;

        var definitions = SnapshotCatalog.Definitions
            .Concat(BuiltInModMetricCatalog.Definitions.Where(d => d.Source == MetricObservationSource.SaveSnapshot)).Concat(
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

        MetricEntry? Entry(MetricObservation o, MetricDefinition d)
            => o.Metrics.TryGetValue(d.StoredKey, out var e)
                && !e.IsQuarantined && e.Revision == d.Revision
                ? e : null;

        MetricValue? Scalar(MetricObservation o, MetricDefinition d)
            => Entry(o, d)?.Values.GetValueOrDefault(MetricEntry.Scalar);

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
            var best = d.Selection == ColonySelection.ObservationMin
                ? valid.MinBy(o => Scalar(o, d)!.Number)!
                : valid.MaxBy(o => Scalar(o, d)!.Number)!;
            kept.Add(best);
            if (d.Selection == ColonySelection.ObservationMaxByBreakdown)
            {
                var buckets = valid.SelectMany(o => Entry(o, d)!.Values
                    .Where(kv => kv.Key != MetricEntry.Scalar && MetricCalculator.MeasuredNumber(kv.Value) is not null)
                    .Select(kv => (kv.Key, kv.Value, Observation: o)));
                foreach (var bucket in buckets.GroupBy(sample => sample.Key, Ord))
                    kept.Add(bucket.MaxBy(sample => sample.Value.Number).Observation);
            }
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
            var best = d.Selection == ColonySelection.ObservationMin
                ? valid.MinBy(o => Scalar(o, d)!.Number)!
                : valid.MaxBy(o => Scalar(o, d)!.Number)!;
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
        MoveChartReadings(snapshots, kept, representative, definitions);
        p.MetricObservations.RemoveAll(o => snapshots.Contains(o) && !kept.Contains(o));
    }

    private readonly record struct ChartReading(int Revision, double Value, MetricCoverage Coverage);

    /// <summary>
    /// #318: the chart readings of the snapshots about to be removed, and those already carried, go to one series on the
    /// latest snapshot. Each reading is what the chart would show for that save (<see cref="MetricCalculator.ChartValue"/>),
    /// with the revision it was taken with. Beyond <see cref="SnapshotChartSeries.MaxRows"/> rows the series is thinned.
    /// </summary>
    private static void MoveChartReadings(List<MetricObservation> snapshots, HashSet<MetricObservation> kept,
        MetricObservation representative, IReadOnlyList<MetricDefinition> definitions)
    {
        var removed = snapshots.Where(o => !kept.Contains(o)).ToList();
        var carriers = snapshots.Where(o => o.CompactedChart is not null).ToList();
        if (removed.Count == 0 && carriers.All(o => ReferenceEquals(o, representative)))
            return;

        var rows = new List<(SnapshotChartRow Row, Dictionary<string, ChartReading> Readings)>();
        var thinned = 0;
        foreach (var carrier in carriers)
        {
            var series = carrier.CompactedChart!;
            carrier.CompactedChart = null;
            thinned += Math.Max(0, series.Thinned);
            var count = series.Rows?.Count ?? 0;
            var keys = (series.Metrics ?? new Dictionary<string, SnapshotChartKey>())
                .Where(kv => kv.Value?.Fits(count) == true).ToList();
            for (var i = 0; i < count; i++)
            {
                if (series.Rows![i] is not { } row)
                    continue;
                var readings = new Dictionary<string, ChartReading>(Ord);
                foreach (var (key, metric) in keys)
                    if (metric.Values[i] is { } v && double.IsFinite(v))
                        readings[key] = new(metric.Revision, v, metric.Coverage?[i] ?? MetricCoverage.SnapshotOnly);
                rows.Add((row, readings));
            }
        }
        foreach (var o in removed)
        {
            var readings = new Dictionary<string, ChartReading>(Ord);
            foreach (var d in definitions)
                if (MetricCalculator.ChartValue(o, d) is { Number: { } n } value)
                    readings[d.StoredKey] = new(d.Revision, n, value.Coverage);
            rows.Add((new SnapshotChartRow { Tick = o.SaveTick, ObservedUtc = o.ObservedUtc, CheckpointKey = o.CheckpointKey }, readings));
        }
        if (rows.Count == 0)
            return;

        rows = rows.OrderBy(r => r.Row.Tick).ThenBy(r => r.Row.ObservedUtc).ToList();
        while (rows.Count > SnapshotChartSeries.MaxRows)
        {
            rows.RemoveAt(LeastShapeChange(rows.Select(r => (r.Row.Tick, r.Readings)).ToList()));
            thinned++;
        }

        // One revision per key: the current definition's, else (a provider no longer described) the newest stored one.
        var current = definitions.GroupBy(d => d.StoredKey, Ord).ToDictionary(g => g.Key, g => g.First().Revision, Ord);
        var result = new SnapshotChartSeries { Rows = rows.Select(r => r.Row).ToList(), Thinned = thinned };
        foreach (var key in rows.SelectMany(r => r.Readings.Keys).Distinct(Ord).Order(Ord))
        {
            var revision = current.TryGetValue(key, out var known) ? known
                : rows.Where(r => r.Readings.ContainsKey(key)).Max(r => r.Readings[key].Revision);
            var at = rows.Select(r => r.Readings.TryGetValue(key, out var x) && x.Revision == revision ? x : (ChartReading?)null).ToList();
            if (at.All(x => x is null))
                continue;
            result.Metrics[key] = new SnapshotChartKey
            {
                Revision = revision,
                Values = at.Select(x => x?.Value).ToList(),
                Coverage = at.Any(x => x is { } r && r.Coverage != MetricCoverage.SnapshotOnly)
                    ? at.Select(x => x?.Coverage ?? MetricCoverage.SnapshotOnly).ToList() : null,
            };
        }
        representative.CompactedChart = result;
    }

    /// <summary>
    /// The interior row whose removal changes the series least: for each statistic it holds, the distance between its reading
    /// and the straight line through that statistic's neighbouring readings, as a share of the statistic's range; the row's
    /// cost is the largest. The first and last reading of a statistic cost the whole range, so peaks, valleys and ends stay.
    /// On a tie, the row closest in time to its neighbours goes. The first and last rows are never removed.
    /// </summary>
    private static int LeastShapeChange(IReadOnlyList<(int Tick, Dictionary<string, ChartReading> Readings)> rows)
    {
        var ranges = new Dictionary<string, (double Min, double Max)>(Ord);
        foreach (var (_, readings) in rows)
            foreach (var (key, r) in readings)
                ranges[key] = ranges.TryGetValue(key, out var range)
                    ? (Math.Min(range.Min, r.Value), Math.Max(range.Max, r.Value)) : (r.Value, r.Value);

        var best = 1;
        var bestCost = double.PositiveInfinity;
        var bestSpan = long.MaxValue;
        for (var i = 1; i < rows.Count - 1; i++)
        {
            double cost = 0;
            foreach (var (key, r) in rows[i].Readings)
            {
                var (min, max) = ranges[key];
                if (max <= min)
                    continue;
                var before = Neighbour(rows, i, key, -1);
                var after = Neighbour(rows, i, key, +1);
                double share;
                if (before is not { } b || after is not { } a)
                    share = 1;
                else
                {
                    var span = a.Tick - (double)b.Tick;
                    var line = span <= 0 ? (a.Value + b.Value) / 2 : b.Value + (a.Value - b.Value) * ((rows[i].Tick - (double)b.Tick) / span);
                    share = Math.Abs(r.Value - line) / (max - min);
                }
                cost = Math.Max(cost, share);
            }
            var gap = (long)rows[i + 1].Tick - rows[i - 1].Tick;
            if (cost < bestCost || cost == bestCost && gap < bestSpan)
                (best, bestCost, bestSpan) = (i, cost, gap);
        }
        return best;
    }

    private static (int Tick, double Value)? Neighbour(IReadOnlyList<(int Tick, Dictionary<string, ChartReading> Readings)> rows,
        int from, string key, int step)
    {
        for (var j = from + step; j >= 0 && j < rows.Count; j += step)
            if (rows[j].Readings.TryGetValue(key, out var r))
                return (rows[j].Tick, r.Value);
        return null;
    }

    private static bool HasOpaqueChart(SnapshotChartSeries? series)
        => series is not null && (series.ExtensionData is { Count: > 0 }
            || series.Rows?.Any(r => r?.ExtensionData is { Count: > 0 }) == true
            || series.Metrics?.Values.Any(m => m?.ExtensionData is { Count: > 0 }) == true);

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
