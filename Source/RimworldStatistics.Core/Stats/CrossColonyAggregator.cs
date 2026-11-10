namespace RimworldStatistics.Core;

/// <summary>
/// Cross-colony comparisons over the already selected per-colony rows. Does not
/// read saves, rescan pawns, merge branches, or change the lifetime counters.
/// </summary>
public static class CrossColonyAggregator
{
    private static readonly StringComparer Ord = StringComparer.Ordinal;

    public static IEnumerable<NumericRow> Build(
        IReadOnlyList<MetricDefinition> definitions,
        IReadOnlyDictionary<string, IReadOnlyList<NumericRow>> colonyRows)
    {
        // Build indexes only once for all derived outputs.
        var indexed = colonyRows.OrderBy(c => c.Key, Ord)
            .Select(c => (c.Key, Rows: c.Value.ToDictionary(r => r.Definition.Key, Ord))).ToList();
        foreach (var definition in definitions.Where(CrossColonyCatalog.IsDerived))
        {
            var values = indexed.Select(c => (c.Key,
                Row: c.Rows.GetValueOrDefault(definition.StoredKey))).ToList();
            if (definition.Key.StartsWith(CrossColonyCatalog.TopPrefix, StringComparison.Ordinal))
                yield return TopType(definition, values);
            else
                yield return Extreme(definition, values,
                    definition.Key.StartsWith(CrossColonyCatalog.MaxPrefix, StringComparison.Ordinal));
        }
    }

    private static MetricCoverage Coverage(IEnumerable<MetricCoverage> values, bool partial)
    {
        if (partial)
            return MetricCoverage.Partial;
        var coverages = values.ToList();
        if (coverages.Any(c => c is MetricCoverage.Partial or MetricCoverage.Unknown))
            return MetricCoverage.Partial;
        return coverages.Contains(MetricCoverage.SnapshotOnly)
            ? MetricCoverage.SnapshotOnly : MetricCoverage.Complete;
    }

    private static NumericRow Extreme(MetricDefinition definition,
        List<(string Key, NumericRow? Row)> colonies, bool max)
    {
        var applicable = colonies.Where(c => c.Row?.Value.Coverage != MetricCoverage.NotApplicable).ToList();
        var measured = applicable.Where(c => c.Row?.Value.Number is { } n && double.IsFinite(n))
            .Select(c => (c.Key, Value: c.Row!.Value)).ToList();
        if (measured.Count == 0)
            return Empty(definition, applicable.Count, colonies.Count);
        var extreme = max ? measured.Max(c => c.Value.Number!.Value)
                          : measured.Min(c => c.Value.Number!.Value);
        var ties = measured.Where(c => c.Value.Number == extreme)
            .Select(c => c.Key).Order(Ord).ToArray();
        var winner = measured.First(c => c.Key == ties[0]).Value;
        var value = new NumericValue
        {
            Number = extreme,
            Coverage = Coverage(measured.Select(c => c.Value.Coverage), measured.Count < applicable.Count),
            Evidence = MetricEvidencePolicy.Combine(measured.Select(c => c.Value.Evidence)),
            ColonyKey = ties[0],
            HolderKeys = ties,
            ThroughTick = winner.ThroughTick,
        };
        return new NumericRow
        {
            Definition = definition, Value = value,
            Targets = applicable.Count, Valid = measured.Count,
        };
    }

    /// <summary>Rank sum(colony[def]), never sum(colony's top item).</summary>
    private static NumericRow TopType(MetricDefinition definition, List<(string Key, NumericRow? Row)> colonies)
    {
        var totals = new Dictionary<string, double>(Ord);
        var evidence = new List<MetricEvidence>();
        var coverage = new List<MetricCoverage>();
        var targets = 0;
        var valid = 0;
        var partial = false;
        foreach (var (_, row) in colonies)
        {
            if (row?.Value.Coverage == MetricCoverage.NotApplicable)
                continue;
            targets++;
            if (row?.Value.Number is not { } parent || !double.IsFinite(parent))
            {
                partial = true;
                continue;
            }

            valid++;
            evidence.Add(row.Value.Evidence);
            coverage.Add(row.Value.Coverage);
            var counted = 0d;
            foreach (var child in row.Breakdowns)
            {
                if (child.Value.Number is not { } quantity || !double.IsFinite(quantity) || quantity < 0)
                {
                    partial = true;
                    continue;
                }
                counted += quantity;
                if (child.Value.Coverage is MetricCoverage.Unknown or MetricCoverage.Partial)
                    partial = true;
                if (child.Breakdown == MetricKeys.UnknownBucket)
                {
                    if (quantity > 0)
                        partial = true; // Recorded item without a trustworthy Def identity.
                    continue;
                }
                if (quantity > 0)
                    totals[child.Breakdown] = totals.GetValueOrDefault(child.Breakdown) + quantity;
            }
            // A legacy split, dropped key (4096 cap), or scalar-only record
            // cannot prove the ranking, even when the parent total is known.
            if (Math.Abs(counted - parent) > Math.Max(0.000001, Math.Abs(parent) * 0.000000001))
                partial = true;
        }

        if (totals.Count == 0)
        {
            if (valid == 0)
                return Empty(definition, targets, colonies.Count);
            return new NumericRow
            {
                Definition = definition,
                Targets = targets, Valid = valid,
                Value = new NumericValue
                {
                    Coverage = Coverage(coverage, partial),
                    Evidence = MetricEvidencePolicy.Combine(evidence),
                },
            };
        }

        var best = totals.Values.Max();
        var keys = totals.Where(k => k.Value == best).Select(k => k.Key).Order(Ord).ToArray();
        var label = string.Join(" / ", keys.Take(4));
        if (keys.Length > 4)
            label += " (+" + (keys.Length - 4).ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
        return new NumericRow
        {
            Definition = definition, Targets = targets, Valid = valid,
            Value = new NumericValue
            {
                Number = best, Label = label,
                Coverage = Coverage(coverage, partial || valid < targets),
                Evidence = MetricEvidencePolicy.Combine(evidence),
            },
        };
    }

    private static NumericRow Empty(MetricDefinition definition, int targets, int total)
        => new()
        {
            Definition = definition, Targets = targets,
            Value = new NumericValue
            {
                Coverage = total > 0 && targets == 0
                    ? MetricCoverage.NotApplicable : MetricCoverage.Unknown,
            },
        };
}
