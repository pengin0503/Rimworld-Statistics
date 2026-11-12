namespace RimworldStatistics.Core;

public enum MetricVerification
{
    Standard,
    Experimental,
    Legacy,
}

public enum UnitRecordingState
{
    Recording,
    NotSelected,
    DlcMissing,
    TargetModMissing,
    Failed,
    NotRequired,
}

/// <summary>Coverage is evidence about existing observations, not proof that a collector is accurate.</summary>
public sealed record MetricStatus(
    MetricDefinition Definition, MetricAvailability Availability, bool PeriodIncomplete,
    MetricVerification Verification, UnitRecordingState Recording,
    int Measured, int Targets, IReadOnlyList<string> MissingColonyKeys);

public static class MetricStatusClassifier
{
    /// <summary>Summarize existing cached readings. Acquisition status is supplied by the game layer.</summary>
    public static IReadOnlyList<MetricStatus> Classify(
        StatsView view, Func<string, UnitRecordingState> unitState)
    {
        ArgumentNullException.ThrowIfNull(unitState);
        var definitions = view.Numeric.Overall.Select(r => r.Definition)
            .Concat(view.Numeric.Colonies.SelectMany(c => c.Rows).Select(r => r.Definition))
            .DistinctBy(d => d.Key, StringComparer.Ordinal)
            .OrderBy(d => d.Key, StringComparer.Ordinal)
            .ToArray();

        var overall = view.Numeric.Overall.ToDictionary(r => r.Definition.Key, StringComparer.Ordinal);
        var counted = view.Numeric.Colonies.Where(c => !c.Excluded && !c.OutOfScope).ToArray();
        var results = new List<MetricStatus>(definitions.Length);

        foreach (var definition in definitions)
        {
            overall.TryGetValue(definition.Key, out var aggregate);
            var missing = new List<string>();
            var measured = 0;
            var targets = 0;
            var partial = false;
            var evidence = aggregate?.Value.Evidence ?? MetricEvidence.UnknownLegacy;
            if (CrossColonyCatalog.IsDerived(definition) && aggregate is not null)
            {
                // Derived rows have no individual colony record; their count is already computed by Core.
                measured = aggregate.Valid;
                targets = aggregate.Targets;
                partial = MetricAvailabilityPolicy.IsPeriodIncomplete(aggregate);
            }
            else
            {
                foreach (var c in counted)
                {
                    var row = c.Rows.FirstOrDefault(r => r.Definition.Key == definition.Key);
                    if (row?.Value.Coverage == MetricCoverage.NotApplicable)
                        continue;
                    targets++;
                    if (row?.Value.Number is { } number
                        && double.IsFinite(number)
                        && row.Value.Known)
                    {
                        measured++;
                        partial |= MetricAvailabilityPolicy.IsPeriodIncomplete(row);
                        if (row.Value.Evidence == MetricEvidence.Experimental
                            || row.Value.Evidence == MetricEvidence.Mixed)
                            evidence = row.Value.Evidence;
                    }
                    else
                    {
                        missing.Add(c.Key);
                    }
                }
                // Certain global-only records have no colony row and are evaluated by their precomputed aggregate.
                if (counted.Length == 0 && aggregate is not null)
                {
                    targets = aggregate.Targets;
                    measured = aggregate.Valid;
                    partial = MetricAvailabilityPolicy.IsPeriodIncomplete(aggregate);
                }
            }

            var availability = targets == 0 ? MetricAvailability.NoApplicableColonies
                : measured == 0 ? MetricAvailability.NoMeasuredColonies
                : measured < targets ? MetricAvailability.SomeMeasuredColonies
                : MetricAvailability.AllMeasuredColonies;

            var verification = definition.Acquisition is not null && evidence == MetricEvidence.UnknownLegacy
                    && measured > 0
                ? MetricVerification.Legacy
                : definition.ProviderId is not null || definition.Acquisition is not null && !definition.Published
                    || evidence is MetricEvidence.Experimental or MetricEvidence.Mixed
                    ? MetricVerification.Experimental
                    : MetricVerification.Standard;

            var recording = definition.Acquisition is { } unit
                ? unitState(unit) : UnitRecordingState.NotRequired;

            results.Add(new MetricStatus(definition, availability, partial, verification,
                recording, measured, targets, missing));
        }

        return results;
    }

    public static bool IsPartial(MetricStatus status)
        => status.Availability == MetricAvailability.SomeMeasuredColonies
            || status.PeriodIncomplete;

    public static bool HasMeasuredNumber(NumericRow row) => MetricDisplayPolicy.HasMeasuredNumber(row);

    public static bool IsPartial(NumericRow row)
        => row.Value.Coverage == MetricCoverage.Partial
            || MetricAvailabilityPolicy.Classify(row) == MetricAvailability.SomeMeasuredColonies
            || MetricAvailabilityPolicy.IsPeriodIncomplete(row);

    public static bool IsExperimental(NumericRow row)
        => row.Definition.ProviderId is not null
            || row.Definition.Acquisition is not null && !row.Definition.Published
            || row.Value.Evidence is MetricEvidence.Experimental or MetricEvidence.Mixed;
}
