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
        var derived = definitions.Where(CrossColonyCatalog.IsDerived).ToArray();
        if (derived.Length == 0)
            yield break;
        // Only index source keys needed by derived records. Indexing the entire
        // catalog would allocate hundreds of unnecessary entries per colony.
        var sourceKeys = derived.Select(d => d.StoredKey).ToHashSet(Ord);
        var indexed = colonyRows.OrderBy(c => c.Key, Ord)
            .Select(c => (c.Key, Rows: c.Value.Where(r => sourceKeys.Contains(r.Definition.Key))
                .ToDictionary(r => r.Definition.Key, Ord))).ToList();
        foreach (var definition in derived)
        {
            var values = indexed.Select(c => (c.Key,
                Row: c.Rows.GetValueOrDefault(definition.StoredKey))).ToList();
            if (definition.Key.StartsWith(CrossColonyCatalog.TopPrefix, StringComparison.Ordinal))
                yield return TopType(definition, values);
            else if (definition.Key.StartsWith(CrossColonyCatalog.SumPrefix, StringComparison.Ordinal))
                yield return Sum(definition, values);
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

    /// <summary>Explicitly additive individual-only snapshot: one observation per colony.</summary>
    private static NumericRow Sum(MetricDefinition definition, List<(string Key, NumericRow? Row)> colonies)
    {
        var applicable = colonies.Where(c => c.Row?.Value.Coverage != MetricCoverage.NotApplicable).ToList();
        var measured = applicable.Where(c => c.Row?.Value.Number is { } n && double.IsFinite(n))
            .Select(c => c.Row!.Value).ToList();
        if (measured.Count == 0)
            return Empty(definition, applicable.Count, colonies.Count);
        return new NumericRow
        {
            Definition = definition, Targets = applicable.Count, Valid = measured.Count,
            Value = new NumericValue
            {
                Number = measured.Sum(v => v.Number!.Value),
                Coverage = Coverage(measured.Select(v => v.Coverage), measured.Count < applicable.Count),
                Evidence = MetricEvidencePolicy.Combine(measured.Select(v => v.Evidence)),
            },
        };
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

    /// <summary>
    /// Rank sum(colony[def]), never sum(colony's top item). Preserve the original
    /// top-one row and attach a sorted, read-only complete captured-period
    /// leaderboard. Each item retains measured contributions from independent
    /// colonies; missing/unknown item contributions are never imputed as zero.
    /// </summary>
    private static NumericRow TopType(MetricDefinition definition, List<(string Key, NumericRow? Row)> colonies)
    {
        var totals = new Dictionary<string, double>(Ord);
        var contributors = new Dictionary<string, List<(string Colony, NumericValue Value)>>(Ord);
        var evidence = new List<MetricEvidence>();
        var coverage = new List<MetricCoverage>();
        var targets = 0;
        var valid = 0;
        var partial = false;
        var modCohorts = new HashSet<string>(Ord);
        var missingModFingerprint = false;
        foreach (var (colony, row) in colonies)
        {
            if (row?.Value.Coverage == MetricCoverage.NotApplicable)
                continue;
            targets++;
            if (row?.Value.Number is not { } parent || !double.IsFinite(parent) || parent < 0
                || row.Value.Coverage == MetricCoverage.Unknown)
            {
                partial = true;
                continue;
            }

            valid++;
            if (string.IsNullOrWhiteSpace(row.Value.ModSetHash))
                missingModFingerprint = true;
            else
                modCohorts.Add(row.Value.ModSetHash);
            evidence.Add(row.Value.Evidence);
            coverage.Add(row.Value.Coverage);
            var counted = 0d;
            var seen = new HashSet<string>(Ord);
            foreach (var child in row.Breakdowns)
            {
                if (child.Value.Number is not { } quantity || !double.IsFinite(quantity) || quantity < 0)
                {
                    partial = true;
                    continue;
                }
                // A duplicated key is ambiguous; including it would overcount
                // the same colony even if a malformed input happened to balance.
                if (!seen.Add(child.Breakdown))
                {
                    partial = true;
                    continue;
                }
                if (child.Value.Coverage is MetricCoverage.Unknown or MetricCoverage.NotApplicable)
                {
                    partial = true;
                    continue;
                }
                counted += quantity;
                if (child.Value.Coverage == MetricCoverage.Partial)
                    partial = true;
                if (child.Breakdown == MetricKeys.UnknownBucket)
                {
                    if (quantity > 0)
                        partial = true; // No defensible Def identity for this quantity.
                    continue;
                }
                if (quantity <= 0)
                    continue;
                var total = totals.GetValueOrDefault(child.Breakdown) + quantity;
                if (!double.IsFinite(total))
                {
                    partial = true;
                    continue;
                }
                totals[child.Breakdown] = total;
                if (!contributors.TryGetValue(child.Breakdown, out var list))
                    contributors[child.Breakdown] = list = new List<(string Colony, NumericValue Value)>();
                list.Add((colony, child.Value));
            }
            // A legacy split, dropped key (4096 cap), or scalar-only record
            // cannot prove the ranking, even when the parent total is known.
            if (Math.Abs(counted - parent) > Math.Max(0.000001, Math.Abs(parent) * 0.000000001))
                partial = true;
        }

        // A DefName alone does not prove identical meaning under different
        // MOD inventories. Retain captured quantity as an explicitly partial
        // cross-MOD comparison instead of asserting a complete leaderboard.
        if (modCohorts.Count > 1 || (modCohorts.Count > 0 && missingModFingerprint))
            partial = true;
        var overallCoverage = Coverage(coverage, partial || valid < targets);
        var overallEvidence = MetricEvidencePolicy.Combine(evidence);
        if (totals.Count == 0)
        {
            if (valid == 0)
                return Empty(definition, targets, colonies.Count);
            return new NumericRow
            {
                Definition = definition, Targets = targets, Valid = valid,
                Value = new NumericValue { Coverage = overallCoverage, Evidence = overallEvidence },
            };
        }

        var ranking = totals.OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, Ord).Select(kv =>
            {
                var parts = contributors[kv.Key].OrderByDescending(c => c.Value.Number!.Value)
                    .ThenBy(c => c.Colony, Ord).Select(c => new NumericRow
                    {
                        Definition = definition,
                        Breakdown = c.Colony,
                        Value = c.Value,
                        Targets = 1, Valid = 1,
                    }).ToArray();
                return new NumericRow
                {
                    Definition = definition, Breakdown = kv.Key,
                    Targets = targets, Valid = valid,
                    Value = new NumericValue
                    {
                        Number = kv.Value, Coverage = overallCoverage, Evidence = overallEvidence,
                    },
                    Breakdowns = parts,
                };
            }).ToArray();

        var best = ranking[0].Value.Number!.Value;
        var keys = ranking.Where(r => r.Value.Number == best).Select(r => r.Breakdown).Order(Ord).ToArray();
        var label = string.Join(" / ", keys.Take(4));
        if (keys.Length > 4)
            label += " (+" + (keys.Length - 4).ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
        return new NumericRow
        {
            Definition = definition, Targets = targets, Valid = valid, Breakdowns = ranking,
            Value = new NumericValue
            {
                Number = best, Label = label,
                Coverage = overallCoverage, Evidence = overallEvidence,
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
