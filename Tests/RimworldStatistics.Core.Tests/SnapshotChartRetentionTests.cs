namespace RimworldStatistics.Core.Tests;

/// <summary>
/// Issue #318: compacting save-time snapshots (#27) must not take the colony history chart's points with it. Rows are
/// registered through <see cref="ProfileMutations.UpsertMetricObservation"/>, the path of a real save.
/// </summary>
public sealed class SnapshotChartRetentionTests
{
    private static readonly DateTime Epoch = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly MetricDefinition Silver = MetricRegistry.Find(StockSnapshot.SilverLatest)!;
    private static readonly MetricDefinition WeaponsPeak = MetricRegistry.Find(StockSnapshot.WeaponsPeak)!;

    private static ColonyIdentity Identity() => new()
    {
        SeedString = "chart-retention", PersistentRandomValue = 5, StartingTile = "7,0", GameStartAbsTick = 1,
        FirstWealthBits = 23, ModCampaignId = "chart-retention", StartingPawnIds = { "Thing_Human1" },
    };

    private static Profile Live()
    {
        var profile = new Profile();
        var id = Identity();
        ProfileMutations.ApplyLiveObservation(profile, id.ModCampaignId!, id, new ObservedValues { TicksGame = 60000 },
            new DisplayInfo(), Epoch);
        return profile;
    }

    private static MetricObservation Snapshot(int i, long silver, long? weapons = null, string? checkpoint = null,
        MetricCoverage coverage = MetricCoverage.SnapshotOnly)
    {
        var o = new MetricObservation
        {
            Source = MetricObservationSource.SaveSnapshot, FormatVersion = 1, Key = "snap-" + i, Identity = Identity(),
            ObservedUtc = Epoch.AddMinutes(i), SaveTick = 60000 * (i + 1), ActiveDlcIds = new List<string>(),
            CheckpointKey = checkpoint,
        };
        o.Metrics[Silver.StoredKey] = MetricEntry.Of(Silver.Revision, new MetricValue { Coverage = coverage, IntegerValue = silver });
        o.Metrics[WeaponsPeak.StoredKey] = MetricEntry.Of(WeaponsPeak.Revision,
            new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = weapons ?? 3 });
        return o;
    }

    private static Profile Saved(IEnumerable<MetricObservation> snapshots, bool compact = true)
    {
        var profile = Live();
        foreach (var o in snapshots)
            if (compact)
                ProfileMutations.UpsertMetricObservation(profile, o);
            else
                profile.MetricObservations.Add(o);
        return profile;
    }

    private static MetricHistory History(Profile profile, MetricDefinition? definition = null)
    {
        var colonies = CampaignMatcher.BuildColonies(profile);
        var colony = Assert.Single(colonies, c => c.Counted);
        return MetricCalculator.BuildChartHistory(profile, colonies, colony.Key, definition ?? Silver);
    }

    private static NumericColony Details(Profile profile)
    {
        var colonies = CampaignMatcher.BuildColonies(profile);
        return Assert.Single(MetricCalculator.BuildView(profile, colonies, StatsCalculator.Calculate(colonies, new StatsOptions()),
            MetricRegistry.Catalog).Colonies);
    }

    [Theory]
    [InlineData(new long[] { 100, 200, 300, 400, 500, 600, 700, 800, 900, 1000 })]
    [InlineData(new long[] { 100, 500, 200, 800, 300 })]   // the audit's example: neither 500 nor 800 survived compaction
    public void EverySaveKeepsItsPoint(long[] values)
    {
        var profile = Saved(values.Select((v, i) => Snapshot(i, v)));
        Assert.True(profile.MetricObservations.Count(o => o.Source == MetricObservationSource.SaveSnapshot) < values.Length);

        var history = History(profile);
        Assert.Equal(values.Select((_, i) => 60000 * (i + 1)), history.Points.Select(p => p.Tick));
        Assert.Equal(values.Select(v => (double?)v), history.Points.Select(p => p.Value?.Number));
        Assert.Equal(0, history.OmittedSaves);
        // The bar chart and the Details tab read the same latest value and observation count as before.
        var details = Details(profile);
        Assert.Equal(values[^1], details.Rows.Single(r => r.Definition.Key == StockSnapshot.SilverLatest).Value.Number);
        Assert.Equal(3d, details.Rows.Single(r => r.Definition.Key == StockSnapshot.WeaponsPeak).Value.Number);
        Assert.Equal(values.Length, details.Observations);
    }

    [Fact]
    public void ManySaves_ThinToABoundThatKeepsPeaksValleysEndsAndCountsTheRest()
    {
        const int saves = 400;
        static long Wave(int i) => i == 123 ? 9000 : i == 321 ? 5 : 1000 + (long)(500 * Math.Sin(i / 15.0));
        var profile = Saved(Enumerable.Range(0, saves).Select(i => Snapshot(i, Wave(i), weapons: i / 40)));
        var history = History(profile);

        var rows = profile.MetricObservations.Count(o => o.Source == MetricObservationSource.SaveSnapshot);
        Assert.InRange(history.Points.Count, SnapshotChartSeries.MaxRows, SnapshotChartSeries.MaxRows + rows);
        Assert.Equal(saves - history.Points.Count, history.OmittedSaves);
        var values = history.Points.Select(p => p.Value!.Number!.Value).ToList();
        Assert.Contains(9000d, values);   // the peak
        Assert.Contains(5d, values);      // the valley
        Assert.Equal(Wave(0), values[0]);
        Assert.Equal(Wave(saves - 1), values[^1]);
        Assert.True(history.Points.Zip(history.Points.Skip(1)).All(x => x.First.Tick < x.Second.Tick));

        // A step series keeps each of its steps: every distinct value of the other statistic is still on its chart.
        var weapons = History(profile, WeaponsPeak).Points.Select(p => p.Value!.Number!.Value).Distinct().Order();
        Assert.Equal(Enumerable.Range(0, saves / 40).Select(n => (double)n), weapons);
    }

    [Fact]
    public void TheSeriesIsBoundedWhateverTheNumberOfSaves()
    {
        static int Size(int saves) => ProfileJson.Serialize(Saved(Enumerable.Range(0, saves)
            .Select(i => Snapshot(i, 1000 + i % 37 * 13, weapons: i % 5)))).Length;
        var few = Size(SnapshotChartSeries.MaxRows * 2);
        var many = Size(SnapshotChartSeries.MaxRows * 12);
        Assert.InRange(many, few * 9 / 10, few * 11 / 10);
    }

    [Fact]
    public void CheckpointLineageOfCompactedReadingsIsThatOfTheRemovedRows()
    {
        // a ─┬─ b ── d       the player reloaded a after saving b, and played on twice:
        //    └─ c ── e       sorted by tick the two branches interleave.
        var cps = new (string Key, string? Parent, int Index)[] { ("a", null, 0), ("b", "a", 1), ("c", "a", 2), ("d", "b", 3), ("e", "c", 4) };
        IEnumerable<MetricObservation> Rows() => cps.Select(c => Snapshot(c.Index, 100 * (c.Index + 1), checkpoint: c.Key));
        Profile WithCheckpoints(bool compact)
        {
            var profile = Saved(Rows(), compact);
            foreach (var (key, parent, index) in cps)
                profile.MetricObservations.Add(new MetricObservation
                {
                    Source = MetricObservationSource.Checkpoint, Key = key, ParentKey = parent, PayloadHash = key,
                    SaveTick = 60000 * (index + 1), Identity = Identity(), ObservedUtc = Epoch.AddMinutes(index),
                });
            return profile;
        }
        var raw = History(WithCheckpoints(compact: false)).Points;
        var compacted = WithCheckpoints(compact: true);
        Assert.True(compacted.MetricObservations.Count(o => o.Source == MetricObservationSource.SaveSnapshot) < cps.Length);
        var kept = History(compacted).Points;

        Assert.Equal(raw.Select(p => (p.Tick, p.Value?.Number)), kept.Select(p => (p.Tick, p.Value?.Number)));
        Assert.Equal(raw.Select((p, i) => p.Predecessor(i)), kept.Select((p, i) => p.Predecessor(i)));
        Assert.Equal(raw.Select(p => (p.ConnectToPrevious, p.Unlinked, p.SkipsOnlyOtherBranches)),
            kept.Select(p => (p.ConnectToPrevious, p.Unlinked, p.SkipsOnlyOtherBranches)));
        Assert.Equal(new[] { -1, 0, 0, 1, 2 }, kept.Select((p, i) => p.Predecessor(i)));
    }

    [Fact]
    public void CompactedReadingsSurviveAJsonRoundTripWithTheirCoverage()
    {
        var profile = Saved(new[]
        {
            Snapshot(0, 10), Snapshot(1, 20, coverage: MetricCoverage.Partial), Snapshot(2, 30), Snapshot(3, 40),
        });
        var reloaded = ProfileJson.Deserialize(ProfileJson.Serialize(profile));
        var points = History(reloaded).Points;
        Assert.Equal(new double?[] { 10, 20, 30, 40 }, points.Select(p => p.Value?.Number));
        Assert.Equal(MetricCoverage.Partial, points[1].Value!.Coverage);
        Assert.Equal(MetricCoverage.SnapshotOnly, points[0].Value!.Coverage);
    }

    [Fact]
    public void ReadingsOfAnotherDefinitionRevisionAreGapsNotValues()
    {
        var profile = Saved(Enumerable.Range(0, 6).Select(i => Snapshot(i, 100 + i)));
        var series = profile.MetricObservations.Single(o => o.CompactedChart is not null).CompactedChart!;
        Assert.NotEmpty(series.Rows);
        series.Metrics[Silver.StoredKey].Revision = Silver.Revision + 1;   // a reading taken under another definition
        var points = History(profile).Points;
        Assert.Equal(6, points.Count);
        Assert.Equal(series.Rows.Count, points.Count(p => p.Value is null));
    }

    [Fact]
    public void SavesRemovedBeforeTheSeriesExistedAreCountedAsOmitted()
    {
        // A profile compacted by an older version: the latest row stands for five saves, four of which left no reading.
        var profile = Saved(Enumerable.Range(0, 5).Select(i => Snapshot(i, 100 + i)));
        foreach (var o in profile.MetricObservations)
            o.CompactedChart = null;
        var history = History(profile);
        var rows = profile.MetricObservations.Count(o => o.Source == MetricObservationSource.SaveSnapshot);
        Assert.Equal(rows, history.Points.Count);
        Assert.Equal(5 - rows, history.OmittedSaves);
    }
}
