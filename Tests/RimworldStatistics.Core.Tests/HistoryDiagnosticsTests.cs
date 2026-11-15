namespace RimworldStatistics.Core.Tests;

/// <summary>Audit C4/C5 and F02/F04: what a reading means, where it stops, and why a save was not read.</summary>
public sealed class HistoryDiagnosticsTests
{
    private static readonly MetricDefinition LiveCount = MetricRegistry.Find(ResearchFamily.Completed)!;

    private static NumericValue Value(double? number, MetricCoverage coverage = MetricCoverage.Complete, int? since = 0, int? through = 600000)
        => new() { Number = number, Coverage = coverage, SinceTick = since, ThroughTick = through };

    [Fact]
    public void ACompleteLiveCountIsDividedByItsOwnRecordedDays()
    {
        Assert.Equal(MetricObservationSource.Checkpoint, LiveCount.Source);
        Assert.Equal(OverallAggregation.Sum, LiveCount.Overall);

        var rate = Assert.NotNull(MetricRates.PerDay(LiveCount, Value(25, since: 120000, through: 720000)));

        Assert.Equal(10, rate.Days, 9);
        Assert.Equal(2.5, rate.PerDay, 9);
    }

    [Theory]
    [InlineData(MetricCoverage.Partial)]       // started late or has gaps: the days would overstate the recorded time
    [InlineData(MetricCoverage.Unknown)]
    [InlineData(MetricCoverage.SnapshotOnly)]
    [InlineData(MetricCoverage.NotApplicable)]
    public void OnlyACompletelyRecordedIntervalHasADailyRate(MetricCoverage coverage)
        => Assert.Null(MetricRates.PerDay(LiveCount, Value(25, coverage)));

    [Fact]
    public void AnUnknownOrTooShortIntervalHasNoDailyRate()
    {
        Assert.Null(MetricRates.PerDay(LiveCount, Value(25, since: null)));
        Assert.Null(MetricRates.PerDay(LiveCount, Value(25, through: null)));
        Assert.Null(MetricRates.PerDay(LiveCount, Value(25, since: 0, through: 59999)));
        Assert.Null(MetricRates.PerDay(LiveCount, Value(25, since: 600000, through: 0)));
        Assert.Null(MetricRates.PerDay(LiveCount, Value(null)));
    }

    [Fact]
    public void ExtremesRatesStatesAndSignedChangesHaveNoDailyRate()
    {
        Assert.Null(MetricRates.PerDay(LiveCount with { Overall = OverallAggregation.Max }, Value(25)));
        Assert.Null(MetricRates.PerDay(LiveCount with { Selection = ColonySelection.ObservationMax }, Value(25)));
        Assert.Null(MetricRates.PerDay(LiveCount with { Unit = MetricUnit.Percent }, Value(25)));
        Assert.Null(MetricRates.PerDay(LiveCount with { Unit = MetricUnit.PerYear }, Value(25)));
        Assert.Null(MetricRates.PerDay(LiveCount with { Signed = true }, Value(25)));
        Assert.Null(MetricRates.PerDay(LiveCount with { Source = MetricObservationSource.SaveSnapshot }, Value(25)));
        Assert.Null(MetricRates.PerDay(LiveCount with { Source = MetricObservationSource.SaveFile }, Value(25)));
    }

    [Fact]
    public void EveryRegisteredDailyRateIsAnAdditiveLiveTotal()
    {
        var eligible = MetricRegistry.ImplementedCatalog.Where(d => MetricRates.PerDay(d, Value(10)) is not null).ToArray();

        Assert.NotEmpty(eligible);
        Assert.All(eligible, d =>
        {
            // Sizes of sets ("types sold", "distinct tiles") do not grow with time like amounts of events do.
            Assert.DoesNotContain("unique", d.Key, StringComparison.Ordinal);
            Assert.False(d.Key.EndsWith("_defs", StringComparison.Ordinal), d.Key);
            Assert.Equal(MetricObservationSource.Checkpoint, d.Source);
            Assert.Equal(OverallAggregation.Sum, d.Overall);
            Assert.Equal(ColonySelection.Representative, d.Selection);
            Assert.False(d.Signed);
            Assert.Contains(d.Unit, new[] { MetricUnit.Count, MetricUnit.Silver, MetricUnit.Nutrition });
        });
    }

    [Theory]
    [InlineData(TradeFamily.SoldDefs)]
    [InlineData(TradeFamily.PartnerFactionDefs)]
    [InlineData(CaravanFamily.UniqueTiles)]
    [InlineData(WorkFamily.UniqueCraftedDefs)]
    [InlineData(BiotechFamily.BirthMothers)]
    [InlineData(LifetimeFamily.FirstSkill20)]
    [InlineData(RoyaltyFamily.Honor)]
    public void SizesOfSetsAndMixedTotalsHaveNoDailyRate(string key)
        => Assert.Null(MetricRates.PerDay(MetricRegistry.Find(key)!, Value(10)));

    [Theory]
    [InlineData(ObservationStatus.Error, "IOException: save changed during read", ImportProblemKind.ChangedDuringRead)]
    [InlineData(ObservationStatus.Error, "IOException: save length changed during read", ImportProblemKind.ChangedDuringRead)]
    [InlineData(ObservationStatus.Error, "IOException: The process cannot access the file because it is being used by another process.", ImportProblemKind.Unreadable)]
    [InlineData(ObservationStatus.Error, "UnauthorizedAccessException: Access to the path is denied.", ImportProblemKind.Unreadable)]
    [InlineData(ObservationStatus.Error, "XmlException: unexpected end of file", ImportProblemKind.Damaged)]
    [InlineData(ObservationStatus.Error, "XmlException: '<' is an unexpected token. Line 4, position 2.", ImportProblemKind.Damaged)]
    [InlineData(ObservationStatus.Error, "InvalidDataException: Block length does not match with its complement.", ImportProblemKind.Damaged)]
    [InlineData(ObservationStatus.Error, "EndOfStreamException: Unable to read beyond the end of the stream.", ImportProblemKind.Damaged)]
    [InlineData(ObservationStatus.Error, "not a savegame", ImportProblemKind.NotASave)]
    [InlineData(ObservationStatus.Error, "NullReferenceException: Object reference not set to an instance of an object.", ImportProblemKind.Unexpected)]
    [InlineData(ObservationStatus.Error, null, ImportProblemKind.Unexpected)]
    [InlineData(ObservationStatus.Unidentifiable, "seedString,startingTile", ImportProblemKind.MissingIdentity)]
    public void ImportProblemsAreGroupedByWhatThePlayerCanDo(ObservationStatus status, string? reason, ImportProblemKind expected)
        => Assert.Equal(expected, ImportProblems.Classify(new SaveObservation { Status = status, ErrorReason = reason }));

    [Fact]
    public void AReadSaveIsNoProblemAndOnlyFailuresAreRetried()
    {
        Assert.Null(ImportProblems.Classify(new SaveObservation { Status = ObservationStatus.Ok }));
        Assert.True(ImportProblems.RetriedOnRescan(ImportProblemKind.ChangedDuringRead));
        Assert.True(ImportProblems.RetriedOnRescan(ImportProblemKind.Damaged));
        Assert.False(ImportProblems.RetriedOnRescan(ImportProblemKind.MissingIdentity));
    }

    [Fact]
    public void ACheckpointHistoryKnowsTheColonysFurthestSave()
    {
        var colony = new Colony { Key = "id:one" };
        colony.LiveCampaigns.Add(new LiveCampaign { CampaignId = "one", Max = new ObservedValues { TicksGame = 90 } });
        colony.Observations.Add(new SaveObservation { FileName = "late.rws", Status = ObservationStatus.Ok, Values = new ObservedValues { TicksGame = 70 } });
        var profile = new Profile
        {
            MetricObservations =
            [
                new MetricObservation
                {
                    Source = MetricObservationSource.Checkpoint, Key = "a", SaveTick = 40, PayloadHash = "a",
                    Identity = new ColonyIdentity { ModCampaignId = "one" },
                    Metrics = { [LiveCount.StoredKey] = MetricEntry.Of(LiveCount.Revision,
                        new MetricValue { Coverage = MetricCoverage.Complete, IntegerValue = 3 }) },
                },
            ],
        };

        var history = MetricCalculator.BuildChartHistory(profile, [colony], colony.Key, LiveCount);

        Assert.Equal(40, Assert.Single(history.Points).Tick);
        Assert.Equal(90, history.LatestSaveTick);
    }
}
