namespace RimworldStatistics.Core.Tests;

public sealed class LeaderboardTests
{
    private static StatsView TestView(params (string Key, double? Value, MetricCoverage Coverage, bool Excluded, bool OutOfScope)[] entries)
    {
        var def = MetricRegistry.Find(MetricKeys.QuestSuccess)
            ?? throw new InvalidOperationException("Expected registered quest-success metric");
        var stats = entries.Select(e => new ColonyStats
        {
            Key = e.Key, Excluded = e.Excluded, OutOfScope = e.OutOfScope,
            HasObservedPoint = true, MaxTicks = 60000,
            GameVersions = [], EndingCounts = new Dictionary<EndingType, int>(),
        }).ToArray();
        var cols = entries.Select(e => new NumericColony
        {
            Key = e.Key, Excluded = e.Excluded, OutOfScope = e.OutOfScope,
            Rows = [new NumericRow
            {
                Definition = def,
                Value = new NumericValue { Number = e.Value, Coverage = e.Coverage, Evidence = MetricEvidence.Experimental },
                Targets = e.Coverage == MetricCoverage.NotApplicable ? 0 : 1,
                Valid = e.Value is null ? 0 : 1,
            }],
        }).ToArray();
        return new StatsView
        {
            Colonies = [],
            Stats = new StatsResult
            {
                Colonies = stats, ColonyCount = stats.Count(s => !s.Excluded && !s.OutOfScope),
                EndingsByType = new Dictionary<EndingType, int>(),
            },
            Numeric = new NumericView { Colonies = cols, Overall = [] },
        };
    }

    [Fact]
    public void TiesShareRankAndSkipNext()
    {
        var board = LeaderboardBuilder.Build(TestView(
            ("b", 5, MetricCoverage.Complete, false, false),
            ("a", 5, MetricCoverage.Complete, false, false),
            ("c", 3, MetricCoverage.Complete, false, false)), MetricKeys.QuestSuccess, true);
        Assert.Equal(new[] { "a", "b", "c" }, board.Entries.Select(e => e.ColonyKey));
        Assert.Equal(new[] { 1, 1, 3 }, board.Entries.Select(e => e.Rank));
        Assert.Equal((3, 3), (board.Measured, board.Targets));
    }

    [Fact]
    public void UnknownOrNotApplicableNeverRanksOrBecomesZero()
    {
        var board = LeaderboardBuilder.Build(TestView(
            ("a", null, MetricCoverage.Unknown, false, false),
            ("b", 0, MetricCoverage.Complete, false, false),
            ("c", null, MetricCoverage.NotApplicable, false, false)), MetricKeys.QuestSuccess, true);
        Assert.Single(board.Entries);
        Assert.Equal("b", board.Entries[0].ColonyKey);
        Assert.Equal(1, board.Measured);
        Assert.Equal(2, board.Targets);
    }

    [Fact]
    public void PartialRanksButIsMarked()
    {
        var board = LeaderboardBuilder.Build(TestView(
            ("a", 12, MetricCoverage.Partial, false, false),
            ("b", 10, MetricCoverage.Complete, false, false)), MetricKeys.QuestSuccess, true);
        Assert.True(board.HasPartial);
        Assert.Equal(MetricCoverage.Partial, board.Entries[0].Coverage);
        Assert.Equal(MetricEvidence.Experimental, board.Entries[0].Evidence);
    }

    [Fact]
    public void AscendingReversesNumericOrder()
    {
        var view = TestView(
            ("a", 3, MetricCoverage.Complete, false, false),
            ("b", 8, MetricCoverage.Complete, false, false));
        Assert.Equal(new[] { "a", "b" }, LeaderboardBuilder.Build(view, MetricKeys.QuestSuccess, false).Entries.Select(e => e.ColonyKey));
        Assert.Equal(new[] { "b", "a" }, LeaderboardBuilder.Build(view, MetricKeys.QuestSuccess, true).Entries.Select(e => e.ColonyKey));
    }

    [Fact]
    public void ExcludedAndOutOfScopeAreNeverRanked()
    {
        var board = LeaderboardBuilder.Build(TestView(
            ("a", 100, MetricCoverage.Complete, true, false),
            ("b", 200, MetricCoverage.Complete, false, true),
            ("c", 1, MetricCoverage.Complete, false, false)), MetricKeys.QuestSuccess, true);
        Assert.Equal("c", Assert.Single(board.Entries).ColonyKey);
        Assert.Equal(1, board.Targets);
    }

    [Fact]
    public void BasicHistoryRequiresObservedPoint()
    {
        var observed = new ColonyStats
        {
            Key = "a", HasObservedPoint = true, MaxTicks = 120000,
            GameVersions = [], EndingCounts = new Dictionary<EndingType, int>(),
        };
        var noPoint = new ColonyStats
        {
            Key = "b", MaxTicks = 600000,
            GameVersions = [], EndingCounts = new Dictionary<EndingType, int>(),
        };
        var view = new StatsView
        {
            Colonies = [],
            Stats = new StatsResult
            {
                ColonyCount = 2, Colonies = [observed, noPoint],
                EndingsByType = new Dictionary<EndingType, int>(),
            },
        };
        var board = LeaderboardBuilder.Build(view, BasicMetrics.Days, true);
        Assert.Equal("a", Assert.Single(board.Entries).ColonyKey);
        Assert.Equal(2d, board.Entries[0].Value);
    }

    [Fact]
    public void EmptyProfileReturnsEmptyLeaderboard()
    {
        var view = new StatsView
        {
            Colonies = [],
            Stats = new StatsResult { Colonies = [], EndingsByType = new Dictionary<EndingType, int>() },
        };
        Assert.Empty(LeaderboardBuilder.Build(view, BasicMetrics.Days, true).Entries);
        Assert.Equal(0, LeaderboardBuilder.Build(view, BasicMetrics.Days, true).Targets);
    }

    [Fact]
    public void RankableKeysIncludeBasicAndIndexedDynamicRows()
    {
        var view = TestView(("a", 3, MetricCoverage.Complete, false, false));
        Assert.Contains(BasicMetrics.Days, LeaderboardBuilder.RankableKeys(view));
        Assert.Contains(MetricKeys.QuestSuccess, LeaderboardBuilder.RankableKeys(view));
    }
}
