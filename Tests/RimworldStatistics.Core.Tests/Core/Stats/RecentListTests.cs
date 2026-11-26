namespace RimworldStatistics.Core.Tests;

public sealed class RecentListTests
{
    [Fact]
    public void Add_PutsNewestFirst_AndReportsChange()
    {
        var list = new List<string>();
        Assert.True(RecentList.Add(list, "metric.days"));
        Assert.True(RecentList.Add(list, "metric.wealth"));
        Assert.Equal(["metric.wealth", "metric.days"], list);
    }

    [Fact]
    public void Add_ReAddMovesExistingKeyToFront_WithoutDuplicating()
    {
        var list = new List<string> { "first", "second", "third" };
        Assert.True(RecentList.Add(list, "third"));
        Assert.Equal(["third", "first", "second"], list);
        Assert.False(RecentList.Add(list, "third"));
        Assert.Equal(["third", "first", "second"], list);
    }

    [Fact]
    public void Add_EvictsOldestAtCapacity()
    {
        var list = new List<string>();
        foreach (var key in new[] { "a", "b", "c", "d", "e", "f" })
            Assert.True(RecentList.Add(list, key));
        Assert.Equal(["f", "e", "d", "c", "b"], list);
        Assert.Equal(RecentList.DefaultCapacity, list.Count);
    }

    [Fact]
    public void Add_HonorsConfiguredCapacity()
    {
        var list = new List<string>();
        foreach (var key in new[] { "a", "b", "c" })
            RecentList.Add(list, key, 2);
        Assert.Equal(["c", "b"], list);
    }

    [Fact]
    public void Add_TrimsKeys_AndIgnoresBlankOrInvalidCapacity()
    {
        var list = new List<string>();
        Assert.True(RecentList.Add(list, "  key  "));
        Assert.Equal(["key"], list);
        Assert.False(RecentList.Add(list, "   "));
        Assert.False(RecentList.Add(list, "other", 0));
        Assert.Equal(["key"], list);
    }

    [Fact]
    public void Remove_RemovesAllMatches_AndReportsAbsence()
    {
        var list = new List<string> { "a", "b", "a" };
        Assert.True(RecentList.Remove(list, "a"));
        Assert.Equal(["b"], list);
        Assert.False(RecentList.Remove(list, "missing"));
        Assert.False(RecentList.Remove(list, "  "));
        Assert.Equal(["b"], list);
    }

    [Fact]
    public void Visible_Deduplicates_OmitsMissingKeys_AndRespectsCapacity()
    {
        var list = new List<string> { "gone", "a", "a", "b", "c" };
        var existing = new HashSet<string>(["a", "b", "c"], StringComparer.Ordinal);
        Assert.Equal(["a", "b"], RecentList.Visible(list, existing, 2));
        Assert.Equal(["a", "b", "c"], RecentList.Visible(list, existing));
        Assert.Empty(RecentList.Visible(null, existing));
        Assert.Empty(RecentList.Visible(list, existing, 0));
    }

    [Fact]
    public void Normalized_CleansPersistedLists()
    {
        Assert.Equal(["b", "a"], RecentList.Normalized([" b ", "", "b", "a", "  "]));
        Assert.Equal(["a", "b", "c", "d", "e"],
            RecentList.Normalized(["a", "b", "c", "d", "e", "x", "y"]));
    }

    [Fact]
    public void ViewPreferences_RecentKeysAreAdditive_AndValidatedOnParse()
    {
        const string json = """
            {"version":1,"recentMetricKeys":[" metric.days ","metric.wealth","metric.days","metric.a","metric.b","metric.c"],"recentColonyKeys":["col-1"]}
            """;
        var parsed = ViewPreferences.Parse(json);
        Assert.Equal(["metric.days", "metric.wealth", "metric.a", "metric.b", "metric.c"], parsed.RecentMetricKeys);
        Assert.Equal(["col-1"], parsed.RecentColonyKeys);
        Assert.True(RecentList.Add(parsed.RecentMetricKeys, "metric.c"));
        Assert.Equal(["metric.c", "metric.days", "metric.wealth", "metric.a", "metric.b"],
            parsed.RecentMetricKeys);
    }

    [Fact]
    public void ViewPreferences_RecentKeysRoundTripThroughJson()
    {
        var prefs = new ViewPreferences();
        RecentList.Add(prefs.RecentMetricKeys, "metric.one");
        RecentList.Add(prefs.RecentMetricKeys, "metric.two");
        RecentList.Add(prefs.RecentColonyKeys, "col-one");
        var restored = ViewPreferences.Parse(prefs.ToJson());
        Assert.Equal(["metric.two", "metric.one"], restored.RecentMetricKeys);
        Assert.Equal(["col-one"], restored.RecentColonyKeys);
    }
}
