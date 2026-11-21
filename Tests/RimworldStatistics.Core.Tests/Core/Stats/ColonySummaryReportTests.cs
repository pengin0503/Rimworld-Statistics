namespace RimworldStatistics.Core.Tests;

public sealed class ColonySummaryReportTests
{
    private static Colony MakeDomainColony(
        string key,
        IReadOnlyList<EndingType>? traces = null,
        IReadOnlyList<LineageEntry>? recovered = null)
    {
        var colony = new Colony { Key = key };
        if (traces is not null)
            foreach (var t in traces)
                colony.Traces.Add(t);
        if (recovered is not null)
            colony.RecoveredEndings.AddRange(recovered);
        return colony;
    }

    private static ColonyReportHtmlText SampleHtmlText(string colonyName = "Alpha Colony") => new()
    {
        ColonyName = colonyName,
        ScopeName = "All colonies",
        ModVersion = "2.0.0",
        Language = "English",
        PageTitle = $"Colony summary report · {colonyName}",
        Subtitle = "Scope: All colonies",
        StatusBanner = null,
        SectionOverview = "Overview & lineage",
        SectionEndings = "Endings reached",
        SectionCoverage = "Recording coverage",
        SectionKeyRecords = "Key lifetime & peak records",
        SectionTopRanks = "Top-3 ranks across colonies",
        SectionModMetrics = "MOD statistics",
        OverviewFields = [("World", "RimWorld"), ("Scenario", "Crashlanded")],
        EndingLines = ["Ship (#1) · departed: 5 · 2026-11-17 12:00 · Recorded in play"],
        EndingsEmptyText = "No endings recorded yet.",
        CoverageSummaryLine = "4 tracked statistics · complete: 2 · snapshot/history: 1 · partial: 1 · unknown: 0 · not applicable: 0",
        ColMetric = "Statistic",
        ColValue = "Value",
        ColKind = "Kind",
        ColRank = "Rank",
        ColState = "Source / state",
        ColRange = "Observed range",
        KeyRecords =
        [
            new ColonyReportHtmlItemText("In-game days", null, "120.0 days", "Cumulative", "#1 / 2", "as saved", "Day 120.0"),
        ],
        TopRanks = [],
        ModMetrics = [],
        FooterNote = "Read-only report.",
    };

    [Fact]
    public void MissingColonyReturnsNull()
    {
        var view = new StatsView
        {
            Colonies = [],
            Stats = new StatsResult { Colonies = [], EndingsByType = new Dictionary<EndingType, int>() },
            Numeric = NumericView.Empty,
        };

        Assert.Null(ColonySummaryReport.Build(view, "colony-does-not-exist"));
    }

    [Fact]
    public void SingleColonyRanksFirstOfOneAndClassifiesValueKinds()
    {
        var questDef = MetricRegistry.Find(MetricKeys.QuestSuccess)!;
        var wealthMeanDef = MetricRegistry.Find(MetricKeys.WealthTotalMean)!;
        var raidMaxDef = MetricRegistry.Find(RaidFamily.PointsMax)!;

        var stats = new ColonyStats
        {
            Key = "c1",
            FactionName = "New Hope",
            WorldName = "Terra",
            Scenario = "Crashlanded",
            Storyteller = "Cassandra Classic",
            Difficulty = "Strive to survive",
            Permadeath = true,
            HasObservedPoint = true,
            MaxTicks = 600_000,
            MaxRealPlay = 7200,
            WealthMax = 180_000,
            WealthLast = 150_000,
            PeakWealthKnown = true,
            LastWealthKnown = true,
            Raids = 14,
            Threats = 20,
            Killed = 2,
            Departed = 0,
            MaxPopulation = 11,
            SaveCount = 4,
            GameVersions = ["1.7.4985 rev1269"],
            EndingCounts = new Dictionary<EndingType, int>(),
        };

        var view = new StatsView
        {
            Colonies = [MakeDomainColony("c1")],
            Stats = new StatsResult
            {
                ColonyCount = 1,
                Colonies = [stats],
                EndingsByType = new Dictionary<EndingType, int>(),
            },
            Numeric = new NumericView
            {
                Colonies =
                [
                    new NumericColony
                    {
                        Key = "c1",
                        Rows =
                        [
                            new NumericRow { Definition = questDef, Value = new NumericValue { Number = 9, Coverage = MetricCoverage.Complete, ThroughTick = 600_000 }, Targets = 1, Valid = 1 },
                            new NumericRow { Definition = wealthMeanDef, Value = new NumericValue { Number = 95_000, Coverage = MetricCoverage.SnapshotOnly, ThroughTick = 600_000 }, Targets = 1, Valid = 1 },
                            new NumericRow { Definition = raidMaxDef, Value = new NumericValue { Number = 2400, Coverage = MetricCoverage.Complete, ThroughTick = 600_000 }, Targets = 1, Valid = 1 },
                        ],
                    },
                ],
                Overall = [],
            },
        };

        var report = ColonySummaryReport.Build(view, "c1");
        Assert.NotNull(report);

        var days = report.KeyRecords.Single(i => i.MetricKey == BasicMetrics.Days);
        Assert.Equal(ColonyReportValueKind.CumulativeTotal, days.ValueKind);
        Assert.Equal(ColonyRankStatus.Ranked, days.RankStatus);
        Assert.Equal(1, days.Rank);
        Assert.Equal(1, days.MeasuredColonies);
        Assert.Equal(10.0, days.Value.Number);

        var peakWealth = report.KeyRecords.Single(i => i.MetricKey == BasicMetrics.PeakWealth);
        Assert.Equal(ColonyReportValueKind.PeakOrExtreme, peakWealth.ValueKind);

        var lastWealth = report.KeyRecords.Single(i => i.MetricKey == BasicMetrics.LastWealth);
        Assert.Equal(ColonyReportValueKind.LatestSnapshot, lastWealth.ValueKind);

        var meanWealth = report.KeyRecords.Single(i => i.MetricKey == MetricKeys.WealthTotalMean);
        Assert.Equal(ColonyReportValueKind.MeanOrRate, meanWealth.ValueKind);

        var raidMax = report.KeyRecords.Single(i => i.MetricKey == RaidFamily.PointsMax);
        Assert.Equal(ColonyReportValueKind.PeakOrExtreme, raidMax.ValueKind);
    }

    [Fact]
    public void TiesPartialCoverageExcludedAndOutOfScopeStatusArePreserved()
    {
        var questDef = MetricRegistry.Find(MetricKeys.QuestSuccess)!;
        var moodMeanDef = MetricRegistry.Find(MetricKeys.MoodMean)!;

        ColonyStats MakeStats(string key, bool excluded = false, bool outOfScope = false) => new()
        {
            Key = key,
            Excluded = excluded,
            OutOfScope = outOfScope,
            HasObservedPoint = true,
            MaxTicks = 120_000,
            GameVersions = [],
            EndingCounts = new Dictionary<EndingType, int>(),
        };

        NumericColony MakeNumeric(string key, double? quests, MetricCoverage questCov, MetricCoverage moodCov, bool excluded = false, bool outOfScope = false) => new()
        {
            Key = key,
            Excluded = excluded,
            OutOfScope = outOfScope,
            Rows =
            [
                new NumericRow
                {
                    Definition = questDef,
                    Value = new NumericValue { Number = quests, Coverage = questCov },
                    Targets = questCov == MetricCoverage.NotApplicable ? 0 : 1,
                    Valid = quests is null ? 0 : 1,
                },
                new NumericRow
                {
                    Definition = moodMeanDef,
                    Value = new NumericValue { Number = null, Coverage = moodCov },
                    Targets = moodCov == MetricCoverage.NotApplicable ? 0 : 1,
                    Valid = 0,
                },
            ],
        };

        var view = new StatsView
        {
            Colonies = [],
            Stats = new StatsResult
            {
                ColonyCount = 4,
                ExcludedColonyCount = 1,
                OutOfScopeColonyCount = 1,
                Colonies =
                [
                    MakeStats("c1"),
                    MakeStats("c2"),
                    MakeStats("c3"),
                    MakeStats("c_missing"),
                    MakeStats("c_excl", excluded: true),
                    MakeStats("c_oos", outOfScope: true),
                ],
                EndingsByType = new Dictionary<EndingType, int>(),
            },
            Numeric = new NumericView
            {
                Colonies =
                [
                    MakeNumeric("c1", 10, MetricCoverage.Complete, MetricCoverage.Unknown),
                    MakeNumeric("c2", 10, MetricCoverage.Partial, MetricCoverage.NotApplicable),
                    MakeNumeric("c3", 4, MetricCoverage.Complete, MetricCoverage.Unknown),
                    MakeNumeric("c_missing", null, MetricCoverage.Unknown, MetricCoverage.Unknown),
                    MakeNumeric("c_excl", 99, MetricCoverage.Complete, MetricCoverage.Unknown, excluded: true),
                    MakeNumeric("c_oos", 99, MetricCoverage.Complete, MetricCoverage.Unknown, outOfScope: true),
                ],
                Overall = [],
            },
        };

        var r1 = ColonySummaryReport.Build(view, "c1")!;
        var q1 = r1.KeyRecords.Single(i => i.MetricKey == MetricKeys.QuestSuccess);
        Assert.Equal(ColonyRankStatus.Ranked, q1.RankStatus);
        Assert.Equal(1, q1.Rank);
        Assert.Equal(3, q1.MeasuredColonies);
        Assert.Equal(4, q1.TargetColonies);
        Assert.True(q1.RankHasPartial);

        var r3 = ColonySummaryReport.Build(view, "c3")!;
        Assert.Equal(3, r3.KeyRecords.Single(i => i.MetricKey == MetricKeys.QuestSuccess).Rank);

        // Unknown never becomes 0 or ranked.
        var rMissing = ColonySummaryReport.Build(view, "c_missing")!;
        var qMissing = rMissing.KeyRecords.Single(i => i.MetricKey == MetricKeys.QuestSuccess);
        Assert.False(qMissing.Value.Known);
        Assert.Null(qMissing.Value.Number);
        Assert.Equal(ColonyRankStatus.Unmeasured, qMissing.RankStatus);
        Assert.Null(qMissing.Rank);

        // NotApplicable gets explicit NotApplicable status.
        var r2 = ColonySummaryReport.Build(view, "c2")!;
        var m2 = r2.KeyRecords.Single(i => i.MetricKey == MetricKeys.MoodMean);
        Assert.Equal(ColonyRankStatus.NotApplicable, m2.RankStatus);
        Assert.Null(m2.Rank);

        // Excluded and OutOfScope are reported explicitly and never ranked.
        var rExcl = ColonySummaryReport.Build(view, "c_excl")!;
        Assert.Equal(ColonyRankStatus.Excluded, rExcl.KeyRecords.Single(i => i.MetricKey == MetricKeys.QuestSuccess).RankStatus);

        var rOos = ColonySummaryReport.Build(view, "c_oos")!;
        Assert.Equal(ColonyRankStatus.OutOfScope, rOos.KeyRecords.Single(i => i.MetricKey == MetricKeys.QuestSuccess).RankStatus);
    }

    [Fact]
    public void EndingOnlyColonyKeepsCountersUnknownWhilePreservingEndingsAndSaves()
    {
        var endingOnly = new ColonyStats
        {
            Key = "trace-only",
            FactionName = "Ancients",
            HasObservedPoint = false,
            SaveCount = 0,
            GameVersions = [],
            EndingCounts = new Dictionary<EndingType, int> { [EndingType.Ship] = 1 },
            CountedEndings = [],
        };

        var view = new StatsView
        {
            Colonies = [MakeDomainColony("trace-only", traces: [EndingType.Ship], recovered: [new LineageEntry(EndingType.Archonexus, 0, 180_000)])],
            Stats = new StatsResult
            {
                ColonyCount = 1,
                Colonies = [endingOnly],
                EndingsByType = new Dictionary<EndingType, int> { [EndingType.Ship] = 1 },
            },
            Numeric = NumericView.Empty,
        };

        var report = ColonySummaryReport.Build(view, "trace-only")!;
        Assert.False(report.Overview.HasObservedPoint);
        Assert.Equal([EndingType.Ship], report.Overview.UnexplainedTraces);
        Assert.Equal(1, report.Overview.RecoveredEndingCount);

        foreach (var unknownBasic in new[] { BasicMetrics.Days, BasicMetrics.Raids, BasicMetrics.PeakWealth, BasicMetrics.LastWealth, BasicMetrics.Killed })
        {
            var item = report.KeyRecords.Single(i => i.MetricKey == unknownBasic);
            Assert.False(item.Value.Known);
            Assert.Null(item.Value.Number);
            Assert.Equal(MetricCoverage.Unknown, item.Value.Coverage);
        }

        var endingsItem = report.KeyRecords.Single(i => i.MetricKey == BasicMetrics.Endings);
        Assert.True(endingsItem.Value.Known);
        Assert.Equal(1d, endingsItem.Value.Number);
    }

    [Fact]
    public void TopRankedHighlightsAndModHighlightsAreSeparatedAndCoverageCountsMatch()
    {
        var nonKeyDef = new MetricDefinition
        {
            Key = "custom.core.metric",
            StoredKey = "custom.core.metric",
            LedgerIds = "custom.core.metric",
            Category = MetricCategory.Work,
            Unit = MetricUnit.Count,
            Provenance = MetricProvenance.LiveAggregate,
            Overall = OverallAggregation.Sum,
            LabelKey = "RWS_Metric_Test",
            DescriptionKey = "RWS_MetricDesc_Test",
        };
        var modDef = new MetricDefinition
        {
            Key = "mod.author.pack.widget_count",
            StoredKey = "mod.author.pack.widget_count",
            LedgerIds = "mod.author.pack.widget_count",
            Category = MetricCategory.Work,
            Unit = MetricUnit.Count,
            Provenance = MetricProvenance.LiveAggregate,
            Overall = OverallAggregation.Sum,
            LabelKey = "RWS_Metric_ModTest",
            DescriptionKey = "RWS_MetricDesc_ModTest",
            OwnerPackageId = "author.pack",
        };

        ColonyStats MakeStats(string key) => new()
        {
            Key = key,
            HasObservedPoint = true,
            MaxTicks = 180_000,
            GameVersions = [],
            EndingCounts = new Dictionary<EndingType, int>(),
        };

        var view = new StatsView
        {
            Colonies = [],
            Stats = new StatsResult
            {
                ColonyCount = 2,
                Colonies = [MakeStats("c1"), MakeStats("c2")],
                EndingsByType = new Dictionary<EndingType, int>(),
            },
            Numeric = new NumericView
            {
                Colonies =
                [
                    new NumericColony
                    {
                        Key = "c1",
                        Rows =
                        [
                            new NumericRow { Definition = nonKeyDef, Value = new NumericValue { Number = 50, Coverage = MetricCoverage.Complete }, Targets = 1, Valid = 1 },
                            new NumericRow { Definition = modDef, Value = new NumericValue { Number = 12, Coverage = MetricCoverage.Partial, Evidence = MetricEvidence.Experimental }, Targets = 1, Valid = 1 },
                        ],
                    },
                    new NumericColony
                    {
                        Key = "c2",
                        Rows =
                        [
                            new NumericRow { Definition = nonKeyDef, Value = new NumericValue { Number = 20, Coverage = MetricCoverage.Complete }, Targets = 1, Valid = 1 },
                            new NumericRow { Definition = modDef, Value = new NumericValue { Number = 30, Coverage = MetricCoverage.Complete }, Targets = 1, Valid = 1 },
                        ],
                    },
                ],
                Overall = [],
            },
        };

        var report = ColonySummaryReport.Build(view, "c1")!;
        var top = Assert.Single(report.TopRankedHighlights);
        Assert.Equal("custom.core.metric", top.MetricKey);
        Assert.Equal(1, top.Rank);

        var mod = Assert.Single(report.ModHighlights);
        Assert.Equal("mod.author.pack.widget_count", mod.MetricKey);
        Assert.Equal(2, mod.Rank);

        Assert.Equal(2, report.Coverage.TotalMetrics);
        Assert.Equal(1, report.Coverage.CompleteCount);
        Assert.Equal(1, report.Coverage.PartialCount);
        Assert.Equal(1, report.Coverage.ExperimentalCount);
    }

    [Fact]
    public void HtmlExportEscapesSpecialCharactersAndContainsNoScriptsOrExternalLinks()
    {
        var stats = new ColonyStats
        {
            Key = "xss\"<colony>&1",
            FactionName = "<script>alert('xss')</script> & \"O'Reilly\"",
            HasObservedPoint = true,
            MaxTicks = 60_000,
            GameVersions = [],
            EndingCounts = new Dictionary<EndingType, int>(),
        };
        var view = new StatsView
        {
            Colonies = [],
            Stats = new StatsResult { ColonyCount = 1, Colonies = [stats], EndingsByType = new Dictionary<EndingType, int>() },
            Numeric = NumericView.Empty,
        };

        var report = ColonySummaryReport.Build(view, stats.Key)!;
        var text = SampleHtmlText(stats.FactionName!) with
        {
            StatusBanner = "Warning <img src=x onerror=alert(1)> & 'quote'",
            KeyRecords =
            [
                new ColonyReportHtmlItemText(
                    "Metric <b>bold</b>",
                    "MOD <svg onload=alert(2)>",
                    "100 & <200>",
                    "Cumulative",
                    "#1 / 1",
                    "complete",
                    "Day 0.0 -> 1.0"),
            ],
        };

        var html = ColonyReportHtmlExport.RenderHtml(report, text, new DateTime(2026, 11, 17, 15, 0, 0, DateTimeKind.Utc));
        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.DoesNotContain("<script>", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img ", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<svg ", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;alert(&#39;xss&#39;)&lt;/script&gt;", html);
        Assert.Contains("xss&quot;&lt;colony&gt;&amp;1", html);
    }

    [Theory]
    [InlineData(null, "colony")]
    [InlineData("   ", "colony")]
    [InlineData("...---___", "colony")]
    [InlineData("CON", "colony_CON")]
    [InlineData("lpt1.txt", "colony_lpt1.txt")]
    [InlineData("../aux:bad*name?.rws", "aux_bad_name_.rws")]
    public void SanitizeFileNameRejectsUnsafeAndReservedNames(string? raw, string expected)
    {
        var actual = ColonyReportHtmlExport.SanitizeFileName(raw);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SanitizeFileNameTruncatesLongUnicodeWithoutSplittingSurrogatePairs()
    {
        // Put an emoji (surrogate pair) across index 47..48 so naive [..48] would leave a lone high surrogate.
        var prefix = new string('あ', 47);
        var input = prefix + "🚀extra";
        var sanitized = ColonyReportHtmlExport.SanitizeFileName(input);

        Assert.True(sanitized.Length <= 48);
        Assert.Equal(47, sanitized.Length);
        Assert.False(char.IsSurrogate(sanitized[^1]));
    }

    [Fact]
    public void HtmlExportAtomicFailureLeavesNoPartialOrTempFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "rws-report-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var stats = new ColonyStats
            {
                Key = "c1",
                FactionName = "Atomic Colony",
                HasObservedPoint = true,
                MaxTicks = 60_000,
                GameVersions = [],
                EndingCounts = new Dictionary<EndingType, int>(),
            };
            var view = new StatsView
            {
                Colonies = [],
                Stats = new StatsResult { ColonyCount = 1, Colonies = [stats], EndingsByType = new Dictionary<EndingType, int>() },
                Numeric = NumericView.Empty,
            };
            var report = ColonySummaryReport.Build(view, "c1")!;
            var text = SampleHtmlText("Atomic Colony");

            Assert.Throws<IOException>(() =>
                ColonyReportHtmlExport.Write(
                    report,
                    text,
                    tempDir,
                    new DateTime(2026, 11, 17, 15, 0, 0, DateTimeKind.Utc),
                    injectWriteHook: _ => throw new IOException("simulated disk full")));

            Assert.Empty(Directory.GetFiles(tempDir));

            var ok = ColonyReportHtmlExport.Write(
                report,
                text,
                tempDir,
                new DateTime(2026, 11, 17, 15, 0, 0, DateTimeKind.Utc));
            Assert.True(File.Exists(ok.FilePath));
            Assert.Single(Directory.GetFiles(tempDir));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
