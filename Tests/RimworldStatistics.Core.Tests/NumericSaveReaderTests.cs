using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

/// <summary>Phase A metrics read from save XML (numeric expansion plan A2; ledger sections wealth, population, vanilla, quests).</summary>
public sealed class NumericSaveReaderTests
{
    private const string V17 = "1.7.4985 rev1269";
    private const int Day = 60000;

    private static SaveXmlBuilder Base(int ticks, string version = V17)
        => new SaveXmlBuilder().Meta(version, 3).Info(realPlay: 10f, tile: "100,0", pawns: "Thing_Human1")
            .Tick(ticksGame: ticks, start: 0).World(seed: "seed", prv: 1);

    /// <summary>A save whose Wealth_Total is aligned for the number of points given (ticks = 30000 × (n − 1)).</summary>
    private static SaveXmlBuilder Aligned(float[] wealthTotal, string version = V17)
        => Base(30000 * (wealthTotal.Length - 1), version).WealthTotal(wealthTotal);

    internal static SaveReadResult ReadDetailed(SaveXmlBuilder b, string fileName = "a.rws")
    {
        var bytes = b.Build();
        using var ms = new MemoryStream(bytes);
        return new SaveReader().ReadDetailed(ms, fileName, bytes.Length, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    private static MetricObservation M(SaveXmlBuilder b) => ReadDetailed(b).Metrics ?? throw new Xunit.Sdk.XunitException("no metric observation");

    private static MetricValue V(MetricObservation m, string key, string breakdown = "")
        => m.Metrics.TryGetValue(key, out var e) ? e.Values[breakdown] : throw new Xunit.Sdk.XunitException("missing " + key);

    private static void Unknown(MetricObservation m, string key)
        => Assert.True(!m.Metrics.ContainsKey(key) || V(m, key).Coverage == MetricCoverage.Unknown, key + " should be unknown");

    private static void Real(MetricObservation m, string key, double expected, MetricCoverage coverage = MetricCoverage.SnapshotOnly)
    {
        var v = V(m, key);
        Assert.Equal(coverage, v.Coverage);
        Assert.NotNull(v.Number);
        Assert.Equal(expected, v.Number!.Value, 9);
    }

    // ----- wealth -----

    [Fact]
    public void WealthTotal_PlanExample()
    {
        var r = ReadDetailed(Aligned(new[] { 100f, 150f, 120f }));
        var m = r.Metrics!;
        // v1 values are unchanged.
        Assert.Equal((150d, 120d, 3), (r.Observation.Values.WealthMax, r.Observation.Values.WealthLast, r.Observation.Values.WealthPoints));
        Assert.Equal(BitConverter.SingleToUInt32Bits(100f), r.Observation.Identity.FirstWealthBits);

        var mean = V(m, MetricKeys.WealthTotalMean);
        Assert.Equal((370d, 3d), (mean.Numerator, mean.Denominator));
        Real(m, MetricKeys.WealthTotalMean, 370d / 3);
        Real(m, MetricKeys.WealthTotalMaxGainHalfDay, 50);
        Real(m, MetricKeys.WealthTotalMaxLossHalfDay, 30);
        Real(m, MetricKeys.WealthTotalMaxLossRateHalfDay, 0.2);
        Real(m, MetricKeys.WealthTotalLongestGrowth, 0.5);
        Real(m, MetricKeys.WealthTotalFirst, 100);
        Real(m, MetricKeys.WealthTotalNet, 20);
        Assert.Equal(3, V(m, MetricKeys.WealthTotalNet).SampleCount);
        Real(m, MetricKeys.WealthTotalPeakDay, 0.5);
        // Never reached: a known range without a value.
        var reach = V(m, MetricKeys.WealthTotalFirst100K);
        Assert.Equal(MetricCoverage.SnapshotOnly, reach.Coverage);
        Assert.False(reach.HasValue);
        Unknown(m, MetricKeys.WealthTotalMaxGain15D);    // 3 points: no 30-point window
        Unknown(m, MetricKeys.WealthTotalMaxGrowthRateYear);
    }

    [Fact]
    public void WealthTotal_ThresholdReached_ReportsNominalDay()
    {
        var m = M(Aligned(new[] { 90000f, 99999f, 100000f, 1200000f }));
        Real(m, MetricKeys.WealthTotalFirst100K, 1.0);
        Real(m, MetricKeys.WealthTotalFirst1M, 1.5);
    }

    [Fact]
    public void WealthTotal_ThresholdAfterMissingPoint_IsUnknown()
    {
        var m = M(Aligned(new[] { 100f, float.NaN, 200000f }));
        Unknown(m, MetricKeys.WealthTotalFirst100K);
    }

    [Theory]
    [InlineData(30, false)]
    [InlineData(31, true)]
    public void FifteenDayWindow_NeedsThirtyOnePoints(int points, bool defined)
    {
        var m = M(Aligned(Enumerable.Range(0, points).Select(i => 1000f + 10f * i).ToArray()));
        if (defined)
        {
            Real(m, MetricKeys.WealthTotalMaxGain15D, 300);
            Real(m, MetricKeys.WealthTotalMaxLoss15D, 0);
        }
        else
        {
            Unknown(m, MetricKeys.WealthTotalMaxGain15D);
            Unknown(m, MetricKeys.WealthTotalMaxLoss15D);
        }
    }

    [Theory]
    [InlineData(120, false)]
    [InlineData(121, true)]
    public void YearWindow_NeedsOneHundredTwentyOnePoints(int points, bool defined)
    {
        // Falls from 2000 to 800 over the year: the growth rate may be negative.
        var m = M(Aligned(Enumerable.Range(0, points).Select(i => 2000f - 10f * i).ToArray()));
        if (defined)
            Real(m, MetricKeys.WealthTotalMaxGrowthRateYear, (800d - 2000d) / 2000d);
        else
            Unknown(m, MetricKeys.WealthTotalMaxGrowthRateYear);
    }

    [Fact]
    public void WealthTotal_MissingPoint_IsPartialAndNotBridged()
    {
        var m = M(Aligned(new[] { 100f, float.NaN, 100f, 90f }));
        Real(m, MetricKeys.WealthTotalMean, 290d / 3, MetricCoverage.Partial);
        Real(m, MetricKeys.WealthTotalMaxGainHalfDay, 0, MetricCoverage.Partial);   // the only usable pair falls
        Real(m, MetricKeys.WealthTotalMaxLossHalfDay, 10, MetricCoverage.Partial);
        Real(m, MetricKeys.WealthTotalLongestGrowth, 0, MetricCoverage.Partial);
        Real(m, MetricKeys.WealthTotalNet, -10, MetricCoverage.Partial);
        Real(m, MetricKeys.WealthTotalFirst, 100);
    }

    [Fact]
    public void WealthTotal_EqualValues_AreNotGrowth()
    {
        var m = M(Aligned(new[] { 100f, 100f, 110f, 110f, 120f, 130f }));
        Real(m, MetricKeys.WealthTotalLongestGrowth, 1.0);   // 110→120→130: two increasing intervals
        Real(m, MetricKeys.WealthTotalMaxLossHalfDay, 0);
        Real(m, MetricKeys.WealthTotalMaxLossRateHalfDay, 0);
    }

    [Fact]
    public void LossRate_WithoutPositiveStart_IsUnknown()
    {
        var m = M(Aligned(new[] { 0f, 0f, 10f }));
        Unknown(m, MetricKeys.WealthTotalMaxLossRateHalfDay);
        Real(m, MetricKeys.WealthTotalMaxLossHalfDay, 0);
    }

    [Fact]
    public void LastPointInvalid_LatestIsUnknown_NotThePreviousPoint()
    {
        var m = M(Aligned(new[] { 100f, 150f }).Recorder("Wealth_Items", new[] { 10f, float.PositiveInfinity }));
        Unknown(m, MetricKeys.WealthItemsLatest);
        Real(m, MetricKeys.WealthItemsPeak, 10, MetricCoverage.Partial);
    }

    [Fact]
    public void ItemsBuildingsPawns_PeakAndLatest()
    {
        var m = M(Aligned(new[] { 100f, 200f, 300f })
            .Recorder("Wealth_Items", new[] { 10f, 50f, 30f })
            .Recorder("Wealth_Buildings", new[] { 0f, 100f, 150f }, deflate: true)
            .Recorder("Wealth_Pawns", new[] { 90f, 50f, 120f }));
        Real(m, MetricKeys.WealthItemsPeak, 50);
        Real(m, MetricKeys.WealthItemsLatest, 30);
        Real(m, MetricKeys.WealthBuildingsPeak, 150);
        Real(m, MetricKeys.WealthBuildingsLatest, 150);
        Real(m, MetricKeys.WealthPawnsPeak, 120);
        Real(m, MetricKeys.WealthPawnsLatest, 120);
        Real(m, MetricKeys.WealthBuildingsMaxShare, 0.5);   // 100/200 and 150/300
    }

    [Fact]
    public void BuildingsShare_DifferentLengths_IsUnknown()
    {
        var m = M(Aligned(new[] { 100f, 200f, 300f }).Recorder("Wealth_Buildings", new[] { 0f, 100f }));
        Unknown(m, MetricKeys.WealthBuildingsMaxShare);
        Real(m, MetricKeys.WealthBuildingsPeak, 100);   // the series itself is still summarized
    }

    [Theory]
    [InlineData("1.4.3901 rev1234")]
    [InlineData("2.0.1 rev1")]
    public void UnverifiedVersion_KeepsOnlyUntimedOutputs(string version)
    {
        var m = M(Aligned(new[] { 100f, 150f, 120f }, version));
        Real(m, MetricKeys.WealthTotalMean, 370d / 3);
        Real(m, MetricKeys.WealthItemsPeak, 999999);
        Unknown(m, MetricKeys.WealthTotalFirst);
        Unknown(m, MetricKeys.WealthTotalNet);
        Unknown(m, MetricKeys.WealthTotalPeakDay);
        Unknown(m, MetricKeys.WealthTotalMaxGainHalfDay);
        Unknown(m, MetricKeys.WealthTotalLongestGrowth);
        Unknown(m, MetricKeys.WealthTotalFirst100K);
    }

    [Theory]
    [InlineData("1.5.4409 rev1118")]
    [InlineData("1.6.4566 rev607")]
    public void VerifiedOlderVersions_HaveTimedOutputs(string version)
    {
        var m = M(Aligned(new[] { 100f, 150f, 120f }, version));
        Real(m, MetricKeys.WealthTotalMaxGainHalfDay, 50);
    }

    [Fact]
    public void PointCountNotMatchingTicks_IsNotAligned()
    {
        var m = M(Base(90000).WealthTotal(100f, 150f, 120f));   // 90000 ticks need 4 points
        Unknown(m, MetricKeys.WealthTotalFirst);
        Unknown(m, MetricKeys.WealthTotalMaxGainHalfDay);
        Real(m, MetricKeys.WealthTotalMean, 370d / 3);
    }

    [Fact]
    public void RawAndDeflate_GiveTheSameMetrics()
    {
        var plain = M(Aligned(new[] { 1f, 2f, 3f }).Recorder("Prisoners", new[] { 0f, 2f, 1f }));
        var deflate = M(Aligned(new[] { 1f, 2f, 3f }).Recorder("Prisoners", new[] { 0f, 2f, 1f }, deflate: true));
        Assert.Equal(V(plain, MetricKeys.PrisonersPeak).Number, V(deflate, MetricKeys.PrisonersPeak).Number);
        Assert.Equal(V(plain, MetricKeys.PrisonersMean).Numerator, V(deflate, MetricKeys.PrisonersMean).Numerator);
    }

    // ----- population -----

    [Fact]
    public void Mood_PlanExample_AlignedWithZero()
    {
        var m = M(Base(90000).WealthTotal(1f, 1f, 1f, 1f).Recorder("ColonistMood", new[] { 60f, 0f, 40f, 85f }));
        var mean = V(m, MetricKeys.MoodMean);
        Assert.Equal((185d, 3d), (mean.Numerator, mean.Denominator));
        Real(m, MetricKeys.MoodMean, 185d / 3, MetricCoverage.Partial);
        Real(m, MetricKeys.MoodBelow50Days, 0.5, MetricCoverage.Partial);
        Real(m, MetricKeys.MoodAtLeast80Days, 0, MetricCoverage.Partial);
        Real(m, MetricKeys.MoodMin, 40, MetricCoverage.Partial);
        Real(m, MetricKeys.MoodMax, 85, MetricCoverage.Partial);
    }

    [Fact]
    public void Mood_PlanExample_UnknownStart()
    {
        var m = M(Base(60000).WealthTotal(1f, 1f, 1f).Recorder("ColonistMood", new[] { 0f, 40f, 85f }));
        Real(m, MetricKeys.MoodMean, 62.5, MetricCoverage.Partial);
        Unknown(m, MetricKeys.MoodBelow50Days);
        Unknown(m, MetricKeys.MoodAtLeast80Days);
    }

    [Fact]
    public void FreeColonistsAndPrisoners()
    {
        var m = M(Aligned(new[] { 1f, 1f, 1f, 1f })
            .Recorder("FreeColonists", new[] { 3f, 5f, 10f, 12f })
            .Recorder("Prisoners", new[] { 0f, 2f, 0f, 1f }));
        Real(m, MetricKeys.FreeColonistsMean, 30d / 4);
        Real(m, MetricKeys.FreeColonistsMin, 3);
        Real(m, MetricKeys.FreeColonistsFirst10, 1.0);
        Real(m, MetricKeys.PrisonersPeak, 2);
        Real(m, MetricKeys.PrisonersMean, 3d / 4);
    }

    // ----- broken or oversized extension series never fail the v1 read -----

    [Fact]
    public void BrokenExtensionSeries_KeepsV1AndOtherSeries()
    {
        var r = ReadDetailed(Aligned(new[] { 100f, 150f, 120f })
            .RecorderRaw("Prisoners", "records", "%%% not base64 %%%")
            .RecorderRaw("ColonistMood", "recordsDeflate", "AAAA"));
        Assert.Equal(ObservationStatus.Ok, r.Observation.Status);
        Assert.Equal(150d, r.Observation.Values.WealthMax);
        Unknown(r.Metrics!, MetricKeys.PrisonersPeak);
        Unknown(r.Metrics!, MetricKeys.MoodMean);
        Real(r.Metrics!, MetricKeys.WealthTotalMean, 370d / 3);
    }

    [Fact]
    public void DuplicateRecorderDef_IsAmbiguous()
    {
        var m = M(Aligned(new[] { 100f, 150f, 120f })
            .Recorder("Prisoners", new[] { 0f, 1f, 2f }).Recorder("Prisoners", new[] { 5f, 5f, 5f }));
        Unknown(m, MetricKeys.PrisonersPeak);
    }

    [Fact]
    public void TooManyPoints_LeavesOnlyThatSeriesUnrecorded()
    {
        var big = new float[262145];
        var r = ReadDetailed(Aligned(new[] { 100f, 150f, 120f }).Recorder("Prisoners", big));
        Assert.Equal(ObservationStatus.Ok, r.Observation.Status);
        Unknown(r.Metrics!, MetricKeys.PrisonersPeak);
        Real(r.Metrics!, MetricKeys.WealthTotalMean, 370d / 3);
    }

    [Fact]
    public void TooManyBase64Characters_LeavesOnlyThatSeriesUnrecorded()
    {
        var payload = Convert.ToBase64String(new byte[1572867]);   // 2097156 characters
        var r = ReadDetailed(Aligned(new[] { 100f, 150f, 120f }).RecorderRaw("FreeColonists", "records", payload));
        Assert.Equal(ObservationStatus.Ok, r.Observation.Status);
        Unknown(r.Metrics!, MetricKeys.FreeColonistsMean);
        Real(r.Metrics!, MetricKeys.WealthTotalMean, 370d / 3);
    }

    [Fact]
    public void Base64CharacterLimit_AppliesEvenWhenTheDataWouldDecodeSmall()
    {
        // A valid deflate block followed by ignored trailing bytes: small once decoded, but over the character limit.
        var small = SaveXmlBuilder.Deflate(SaveXmlBuilder.FloatBytes(1f, 2f, 3f));
        var padded = small.Concat(new byte[1572864]).ToArray();
        var within = ReadDetailed(Aligned(new[] { 100f, 150f, 120f }).RecorderRaw("Prisoners", "recordsDeflate", Convert.ToBase64String(small)));
        Real(within.Metrics!, MetricKeys.PrisonersPeak, 3);
        var over = ReadDetailed(Aligned(new[] { 100f, 150f, 120f }).RecorderRaw("Prisoners", "recordsDeflate", Convert.ToBase64String(padded)));
        Assert.Equal(ObservationStatus.Ok, over.Observation.Status);
        Unknown(over.Metrics!, MetricKeys.PrisonersPeak);
    }

    [Fact]
    public void BytesAfterTheDocument_AreHashedToo()
    {
        var bytes = Aligned(new[] { 100f }).Build().Concat(System.Text.Encoding.UTF8.GetBytes(new string(' ', 300000) + "\n")).ToArray();
        using var ms = new MemoryStream(bytes);
        var r = new SaveReader().ReadDetailed(ms, "a.rws", bytes.Length, DateTime.UnixEpoch);
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)), r.Metrics!.Key);
    }

    [Fact]
    public void DeflateBeyondOneMebibyte_LeavesOnlyThatSeriesUnrecorded()
    {
        var r = ReadDetailed(Aligned(new[] { 100f, 150f, 120f }).Recorder("ColonistMood", new float[262200], deflate: true));
        Assert.Equal(ObservationStatus.Ok, r.Observation.Status);
        Unknown(r.Metrics!, MetricKeys.MoodMean);
    }

    [Fact]
    public void WealthTotalBeyondExtensionLimit_KeepsV1Values()
    {
        var points = Enumerable.Repeat(5f, 262145).ToArray();
        var r = ReadDetailed(Base(60000).WealthTotalDeflate(points));
        Assert.Equal((5d, 262145), (r.Observation.Values.WealthMax, r.Observation.Values.WealthPoints));
        Unknown(r.Metrics!, MetricKeys.WealthTotalMean);
    }

    // ----- vanilla counters -----

    [Fact]
    public void RaidsAndDeathsPerYear_UseTheSameSave()
    {
        var m = M(Base(7200000).WealthTotal(1f).Stats(raids: 6, killed: 1));
        var raids = V(m, MetricKeys.RaidsPerYear);
        Assert.Equal((MetricCoverage.Complete, 6d, 2d), (raids.Coverage, raids.Numerator, raids.Denominator));
        Real(m, MetricKeys.RaidsPerYear, 3, MetricCoverage.Complete);
        Real(m, MetricKeys.DeathsPerYear, 0.5, MetricCoverage.Complete);
    }

    [Theory]
    [InlineData(V17, true)]
    [InlineData("1.5.4409 rev1118", true)]
    [InlineData("1.6.4566 rev607", true)]
    [InlineData("1.4.3901 rev1234", false)]
    public void OmittedCounter_IsZeroOnlyWhereTheDefaultIsVerified(string version, bool known)
    {
        var m = M(Base(3600000, version).WealthTotal(1f).Stats(threats: 1));
        if (known)
            Real(m, MetricKeys.RaidsPerYear, 0, MetricCoverage.Complete);
        else
            Unknown(m, MetricKeys.RaidsPerYear);
    }

    [Fact]
    public void MissingStatsRecordOrZeroTicks_IsUnknown()
    {
        Unknown(M(Base(3600000).WealthTotal(1f)), MetricKeys.RaidsPerYear);
        Unknown(M(Base(0).WealthTotal(1f).Stats(raids: 1)), MetricKeys.RaidsPerYear);
    }

    [Fact]
    public void ModCount_AndDlcIds()
    {
        var m = M(Base(60000).MetaMods(V17, "brrainz.harmony", "ludeon.rimworld", "ludeon.rimworld.royalty", "Ludeon.RimWorld.Odyssey", " ").WealthTotal(1f));
        Assert.Equal(4L, V(m, MetricKeys.ModsLatest).IntegerValue);
        Assert.Equal(new[] { "ludeon.rimworld.odyssey", "ludeon.rimworld.royalty" }, m.ActiveDlcIds);

        var empty = M(Base(60000).MetaMods(V17).WealthTotal(1f));
        Assert.Equal(0L, V(empty, MetricKeys.ModsLatest).IntegerValue);
        Assert.Empty(empty.ActiveDlcIds!);

        var missing = M(Base(60000).MetaMods(V17, null).WealthTotal(1f));
        Unknown(missing, MetricKeys.ModsLatest);
        Assert.Null(missing.ActiveDlcIds);
    }

    // ----- retained quests -----

    [Fact]
    public void Quests_PlanExample()
    {
        var m = M(Base(600000).WealthTotal(1f)
            .QuestFull(1, "OpportunitySite_ItemStash", acceptanceTick: 60000, ended: true, outcome: "Success", cleanupTick: 180000)
            .QuestFull(2, "TradeRequest", acceptanceTick: 60000, ended: true, outcome: "Fail", cleanupTick: 120000)
            .QuestFull(3, "Hospitality_Refugee", acceptanceExpireTick: 300000)                       // offer expired unaccepted
            .QuestFull(4, "BanditCamp", acceptanceTick: 400000)                                       // ongoing
            .QuestFull(5, "Mission_BanditCamp", ended: true, outcome: "InvalidPreAcceptance", cleanupTick: 10)
            .QuestFull(1, "OpportunitySite_ItemStash", acceptanceTick: 60000, ended: true, outcome: "Success", cleanupTick: 180000));   // same quest, copied
        Assert.Equal(1L, V(m, MetricKeys.QuestSuccess).IntegerValue);
        Assert.Equal(1L, V(m, MetricKeys.QuestFailure).IntegerValue);
        var rate = V(m, MetricKeys.QuestSuccessRate);
        Assert.Equal((MetricCoverage.SnapshotOnly, 1d, 2d, 0.5), (rate.Coverage, rate.Numerator, rate.Denominator, rate.RealValue));
        Assert.Equal(3L, V(m, MetricKeys.QuestAccepted).IntegerValue);
        Assert.Equal(1L, V(m, MetricKeys.QuestExpired).IntegerValue);
        Assert.Equal(1L, V(m, MetricKeys.QuestSuccessByRoot, "OpportunitySite_ItemStash").IntegerValue);
        Real(m, MetricKeys.QuestSuccessDurationMean, 2.0);
        Real(m, MetricKeys.QuestSuccessDurationMin, 2.0);
        Real(m, MetricKeys.QuestSuccessDurationMax, 2.0);
    }

    [Fact]
    public void Quests_AutoAcceptedWithoutTick_CountsButHasNoDuration()
    {
        var m = M(Base(600000).WealthTotal(1f)
            .QuestFull(0, "EndGame_ShipEscape", initiallyAccepted: true, ended: true, outcome: "Success", cleanupTick: 500000)
            .QuestFull(7, "TradeRequest", acceptanceTick: 120000, ended: true, outcome: "Success", cleanupTick: 150000));
        Assert.Equal(2L, V(m, MetricKeys.QuestSuccess).IntegerValue);
        Assert.Equal(2L, V(m, MetricKeys.QuestAccepted).IntegerValue);
        Real(m, MetricKeys.QuestSuccessDurationMean, 0.5, MetricCoverage.Partial);
        Assert.Equal(1d, V(m, MetricKeys.QuestSuccessDurationMean).Denominator);
    }

    [Fact]
    public void Quests_MissingRoot_CountsInTotalsAndUnknownBucket()
    {
        var m = M(Base(600000).WealthTotal(1f).QuestFull(3, null, acceptanceTick: 1, ended: true, outcome: "Success", cleanupTick: 60001));
        Assert.Equal(1L, V(m, MetricKeys.QuestSuccess).IntegerValue);
        Assert.Equal(1L, V(m, MetricKeys.QuestSuccessByRoot, MetricKeys.UnknownBucket).IntegerValue);
    }

    [Fact]
    public void Quests_ContradictingIds_AreDroppedAndPartial()
    {
        var m = M(Base(600000).WealthTotal(1f)
            .QuestFull(9, "TradeRequest", acceptanceTick: 1, ended: true, outcome: "Success", cleanupTick: 60001)
            .QuestFull(9, "TradeRequest", acceptanceTick: 1, ended: true, outcome: "Fail", cleanupTick: 60001)
            .QuestFull(10, "TradeRequest", acceptanceTick: 1, ended: true, outcome: "Fail", cleanupTick: 60001));
        var success = V(m, MetricKeys.QuestSuccess);
        Assert.Equal((MetricCoverage.Partial, 0L), (success.Coverage, success.IntegerValue));
        Assert.Equal((MetricCoverage.Partial, 1L), (V(m, MetricKeys.QuestFailure).Coverage, V(m, MetricKeys.QuestFailure).IntegerValue));
    }

    [Fact]
    public void Quests_NegativeDuration_IsExcluded()
    {
        var m = M(Base(600000).WealthTotal(1f)
            .QuestFull(1, "A", acceptanceTick: 120000, ended: true, outcome: "Success", cleanupTick: 60000)
            .QuestFull(2, "A", acceptanceTick: 0, ended: true, outcome: "Success", cleanupTick: 60000));
        Real(m, MetricKeys.QuestSuccessDurationMax, 1.0, MetricCoverage.Partial);
        Assert.Equal(1d, V(m, MetricKeys.QuestSuccessDurationMean).Denominator);
    }

    [Fact]
    public void Quests_NoneSaved_AreZeroWithUnknownRate()
    {
        var m = M(Base(600000).WealthTotal(1f).EmptyQuests());
        Assert.Equal(0L, V(m, MetricKeys.QuestSuccess).IntegerValue);
        Assert.Equal(0L, V(m, MetricKeys.QuestAccepted).IntegerValue);
        Unknown(m, MetricKeys.QuestSuccessRate);
        Unknown(m, MetricKeys.QuestSuccessDurationMean);
    }

    [Fact]
    public void Quests_NoQuestManager_IsUnknown()
        => Unknown(M(Base(600000).WealthTotal(1f)), MetricKeys.QuestSuccess);

    [Fact]
    public void BrokenQuestNumber_DropsThatQuestOnly()
    {
        var b = Base(600000).WealthTotal(1f).QuestFull(1, "A", acceptanceTick: 5, ended: true, outcome: "Success", cleanupTick: 60005);
        var xml = System.Text.Encoding.UTF8.GetString(b.Build()).Replace("<acceptanceTick>5</acceptanceTick>", "<acceptanceTick>five</acceptanceTick>");
        var bytes = System.Text.Encoding.UTF8.GetBytes(xml);
        using var ms = new MemoryStream(bytes);
        var r = new SaveReader().ReadDetailed(ms, "a.rws", bytes.Length, DateTime.UnixEpoch);
        Assert.Equal(ObservationStatus.Ok, r.Observation.Status);
        var success = V(r.Metrics!, MetricKeys.QuestSuccess);
        Assert.Equal((MetricCoverage.Partial, 0L), (success.Coverage, success.IntegerValue));   // that quest is left out
        Assert.NotNull(r.Metrics!.Scalar(MetricKeys.WealthTotalMean, MetricKeys.Revision(MetricKeys.WealthTotalMean)));
    }

    // ----- the observation itself -----

    [Fact]
    public void Observation_HashesAllBytes_AndCarriesContext()
    {
        var b = Aligned(new[] { 100f, 150f, 120f });
        var bytes = b.Build();
        var r = ReadDetailed(b, "copy.rws");
        var m = r.Metrics!;
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)), m.Key);
        Assert.Equal(MetricObservationSource.SaveFile, m.Source);
        Assert.Equal(SaveReader.Version, m.FormatVersion);
        Assert.Equal((60000, V17), (m.SaveTick, m.GameVersion));
        Assert.Equal("copy.rws", m.FileAliases.Single().FileName);
        Assert.Equal(r.Observation.Identity.FingerprintHash(), m.Identity.FingerprintHash());
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), m.ObservedUtc);
    }

    [Fact]
    public void Observation_SizeDifferentFromBytesRead_IsNotConfirmed()
    {
        var bytes = Aligned(new[] { 100f }).Build();
        using var ms = new MemoryStream(bytes);
        var r = new SaveReader().ReadDetailed(ms, "a.rws", bytes.Length + 1, DateTime.UnixEpoch);
        Assert.Equal(ObservationStatus.Ok, r.Observation.Status);
        Assert.Null(r.Metrics);
    }

    [Fact]
    public void Observation_OnlyForUsableV1Reads()
    {
        Assert.Null(ReadDetailed(new SaveXmlBuilder().Meta(V17).Tick(ticksGame: 1)).Metrics);                     // unidentifiable
        Assert.Null(ReadDetailed(Aligned(new[] { 1f }).WealthTotalRaw("records", "%%%")).Metrics);                 // v1 error
    }

    [Fact]
    public void ReaderVersion_IsFour()
        => Assert.Equal(4, SaveReader.Version);
}
