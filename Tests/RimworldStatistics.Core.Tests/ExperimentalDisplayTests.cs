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
}
