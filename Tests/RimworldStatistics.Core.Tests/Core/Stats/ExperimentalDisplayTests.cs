namespace RimworldStatistics.Core.Tests;

public sealed class ExperimentalDisplayTests
{
    [Fact]
    public void NoExperimentalVisibility_IsExactlyTheShippedCatalog()
    {
        var selected = MetricDisplayPolicy.SelectDefinitions(MetricRegistry.Catalog, new HashSet<string>(StringComparer.Ordinal));
        Assert.Equal(MetricRegistry.All.Select(d => d.Key), selected.Select(d => d.Key));
    }

    [Fact]
    public void VisibilitySelectsOnlyMatchingUnits_AndDoesNotPublishEverything()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal) { "research" };
        var selected = MetricDisplayPolicy.SelectDefinitions(MetricRegistry.Catalog, ids);
        Assert.Contains(selected, d => d.Acquisition == "research" && !d.Published);
        Assert.DoesNotContain(selected, d => d.Acquisition == "work");
        Assert.All(MetricRegistry.All, d => Assert.Contains(selected, s => s.Key == d.Key));
        Assert.Equal(selected.Count, selected.Select(d => d.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void DerivedRankingsRequireTheirUnderlyingAcquisition()
    {
        var selected = MetricDisplayPolicy.SelectDefinitions(MetricRegistry.Catalog,
            new HashSet<string>(StringComparer.Ordinal) { "colony_start" });
        Assert.Contains(selected, d => d.Key == MetricRankings.DaysByBiome);
        Assert.DoesNotContain(selected, d => d.Key == MetricRankings.MinPartyAtEnding);
    }

    [Fact]
    public void ProfileSessionViewCacheIncludesDisplaySelection()
    {
        using var dir = new Fixtures.TempDir();
        var session = new ProfileSession(new ProfileStore(dir.Path), () => new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc));
        var options = new StatsOptions();
        var a = session.GetView(options);
        var b = session.GetView(options, new[] { "research" });
        Assert.NotSame(a, b);
        Assert.Contains(b.Numeric.Overall, d => d.Definition.Acquisition == "research");
        Assert.Same(b, session.GetView(options, new[] { "research" }));
        Assert.Same(a, session.GetView(options));
    }

    [Fact]
    public void DefaultDetailsVisibility_ContainsAllImplementedAndUnknownOutputDefinitions()
    {
        var ids = MetricDisplayPolicy.AllDetailsVisibleUnits;
        Assert.Contains(MetricRegistry.OptionalModVisibilityId, ids);
        Assert.Equal(AcquisitionUnits.All.Select(u => u.Id).Order(StringComparer.Ordinal),
            ids.Where(id => id != MetricRegistry.OptionalModVisibilityId).Order(StringComparer.Ordinal));

        var selected = MetricDisplayPolicy.SelectDefinitions(MetricRegistry.Catalog, ids);
        Assert.Equal(MetricRegistry.Catalog.Select(d => d.Key), selected.Select(d => d.Key));
        Assert.Contains(selected, d => !d.Published && d.Acquisition is not null);

        // Visibility of an output never opts in an acquisition hook.
        Assert.Empty(ExperimentalAcquisitionPolicy.Resolve(
            new ExperimentalSelection(false, Array.Empty<string>()), AcquisitionUnits.All));

        // A missing observation remains unknown, rather than a manufactured zero.
        using var dir = new Fixtures.TempDir();
        var session = new ProfileSession(new ProfileStore(dir.Path), () => DateTime.UtcNow);
        var view = session.GetView(new StatsOptions(), ids);
        Assert.Contains(view.Numeric.Overall, r => !r.Definition.Published &&
            r.Value.Coverage == MetricCoverage.Unknown && r.Value.Number is null);
    }

    [Fact]
    public void MeasuredFilter_IncludesGenuineZero_ButExcludesMissingAndNoWinner()
    {
        var definition = MetricRegistry.Find(WorkFamily.HarvestByCrop)!;
        NumericRow Row(double? value, MetricCoverage coverage) => new()
        {
            Definition = definition,
            Value = new NumericValue { Number = value, Coverage = coverage },
        };
        Assert.True(MetricDisplayPolicy.HasMeasuredNumber(Row(0, MetricCoverage.Complete)));
        Assert.True(MetricDisplayPolicy.HasMeasuredNumber(Row(10, MetricCoverage.Partial)));
        Assert.False(MetricDisplayPolicy.HasMeasuredNumber(Row(null, MetricCoverage.Unknown)));
        Assert.False(MetricDisplayPolicy.HasMeasuredNumber(Row(null, MetricCoverage.Partial)));
        Assert.False(MetricDisplayPolicy.HasMeasuredNumber(Row(0, MetricCoverage.NotApplicable)));
        Assert.False(MetricDisplayPolicy.HasMeasuredNumber(Row(double.NaN, MetricCoverage.Complete)));
    }

    [Fact]
    public void UnrankedTopDistribution_IsDifferentFromAnUnreachedThreshold()
    {
        var top = MetricRegistry.Find(CrossColonyCatalog.TopPrefix + WorkFamily.HarvestByCrop)!;
        var threshold = MetricRegistry.Find(MetricKeys.WealthTotalFirst100K)!;
        var noWinner = new NumericValue { Coverage = MetricCoverage.Partial };
        Assert.True(MetricDisplayPolicy.IsUnrankedTop(top, noWinner));
        Assert.False(MetricDisplayPolicy.IsUnrankedTop(threshold, noWinner));
        Assert.False(MetricDisplayPolicy.IsUnrankedTop(top,
            new NumericValue { Coverage = MetricCoverage.Unknown }));
        Assert.False(MetricDisplayPolicy.IsUnrankedTop(top,
            new NumericValue { Coverage = MetricCoverage.Partial, Number = 1 }));
    }
}
