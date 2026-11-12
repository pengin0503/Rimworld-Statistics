namespace RimworldStatistics.Core.Tests;

public sealed class MetricStatusTests
{
    private static MetricDefinition Definition => MetricRegistry.Find(MetricKeys.QuestSuccess)!;

    private static StatsView View(MetricDefinition definition,
        params (string Key, double? Value, MetricCoverage Coverage, int? SinceTick)[] entries)
    {
        var cols = entries.Select(x => new NumericColony
        {
            Key = x.Key, Rows = [new NumericRow
            {
                Definition = definition,
                Value = new NumericValue
                {
                    Number = x.Value, Coverage = x.Coverage, SinceTick = x.SinceTick,
                    Evidence = MetricEvidence.Verified,
                },
                Valid = x.Value.HasValue ? 1 : 0,
                Targets = x.Coverage == MetricCoverage.NotApplicable ? 0 : 1,
            }],
        }).ToArray();
        var totalValid = entries.Count(e => e.Value.HasValue);
        var targets = entries.Count(e => e.Coverage != MetricCoverage.NotApplicable);
        var overall = new NumericRow
        {
            Definition = definition,
            Value = new NumericValue
            {
                Number = totalValid > 0 ? entries.Where(e => e.Value.HasValue).Sum(e => e.Value) : null,
                Coverage = entries.Any(e => e.Coverage == MetricCoverage.Partial)
                    ? MetricCoverage.Partial : MetricCoverage.Complete,
                Evidence = MetricEvidence.Verified,
                SinceTick = entries.Any(e => e.SinceTick is > 0) ? 12000 : null,
            },
            Valid = totalValid, Targets = targets,
        };
        return new StatsView
        {
            Colonies = [],
            Stats = new StatsResult
            {
                ColonyCount = entries.Length,
                Colonies = entries.Select(e => new ColonyStats
                {
                    Key = e.Key, GameVersions = [], EndingCounts = new Dictionary<EndingType, int>(),
                }).ToArray(),
                EndingsByType = new Dictionary<EndingType, int>(),
            },
            Numeric = new NumericView { Colonies = cols, Overall = [overall] },
        };
    }

    private static MetricStatus Only(StatsView view) =>
        Assert.Single(MetricStatusClassifier.Classify(view, _ => UnitRecordingState.NotSelected));

    [Fact]
    public void NoTargetsAreNotApplicable()
    {
        var status = Only(View(Definition, ("a", null, MetricCoverage.NotApplicable, null)));
        Assert.Equal(MetricAvailability.NoApplicableColonies, status.Availability);
        Assert.Equal(0, status.Targets);
    }

    [Fact]
    public void NoMeasuredAndSomeMeasuredAreDistinct()
    {
        var missing = Only(View(Definition, ("a", null, MetricCoverage.Unknown, null)));
        Assert.Equal(MetricAvailability.NoMeasuredColonies, missing.Availability);
        var some = Only(View(Definition,
            ("a", 3, MetricCoverage.Complete, null),
            ("b", null, MetricCoverage.Unknown, null)));
        Assert.Equal(MetricAvailability.SomeMeasuredColonies, some.Availability);
        Assert.Equal(new[] { "b" }, some.MissingColonyKeys);
        Assert.Equal((1, 2), (some.Measured, some.Targets));
    }

    [Fact]
    public void AllMeasuredAndPartialCoverageAreDistinguished()
    {
        var full = Only(View(Definition, ("a", 3, MetricCoverage.Complete, null)));
        Assert.Equal(MetricAvailability.AllMeasuredColonies, full.Availability);
        Assert.False(full.PeriodIncomplete);
        var partial = Only(View(Definition, ("a", 3, MetricCoverage.Partial, null)));
        Assert.True(partial.PeriodIncomplete);
        var late = Only(View(Definition, ("a", 3, MetricCoverage.Complete, 500)));
        Assert.True(late.PeriodIncomplete);
    }

    [Fact]
    public void StandardSaveFileDoesNotBecomeLegacyFromDefaultEvidence()
    {
        var status = Only(View(Definition with { Acquisition = null }, ("a", 1, MetricCoverage.Complete, null)));
        Assert.Equal(MetricVerification.Standard, status.Verification);
        Assert.Equal(UnitRecordingState.NotRequired, status.Recording);
    }

    [Fact]
    public void ProviderAndUnverifiedUnitAreExperimental()
    {
        var def = Definition with { Key = "mod.test.units", ProviderId = "provider", Acquisition = "mod.test.unit" };
        var status = Only(View(def, ("a", 3, MetricCoverage.Complete, null)));
        Assert.Equal(MetricVerification.Experimental, status.Verification);
        Assert.Equal(UnitRecordingState.NotSelected, status.Recording);
    }

    [Fact]
    public void EmptyNumericViewContainsNoIncorrectResults()
    {
        var view = new StatsView
        {
            Colonies = [],
            Stats = new StatsResult { Colonies = [], EndingsByType = new Dictionary<EndingType, int>() },
        };
        Assert.Empty(MetricStatusClassifier.Classify(view, _ => UnitRecordingState.NotSelected));
    }
}
