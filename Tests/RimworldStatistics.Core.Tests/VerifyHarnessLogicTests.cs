using RimworldStatistics.VerifyHarness;

namespace RimworldStatistics.Core.Tests;

/// <summary>The game-independent part of the development-only verification harness (tools/VerifyHarness/Logic).</summary>
public sealed class VerifyHarnessLogicTests
{
    private static MetricObservation Obs(params (string Key, string Bucket, MetricValue Value)[] values)
    {
        var o = new MetricObservation { Source = MetricObservationSource.Checkpoint, Key = "k", Identity = new ColonyIdentity() };
        foreach (var (key, bucket, value) in values)
        {
            if (!o.Metrics.TryGetValue(key, out var e))
                o.Metrics[key] = e = new MetricEntry { Revision = 1 };
            e.Values[bucket] = value;
        }
        return o;
    }

    private static MetricValue I(long v, MetricCoverage c = MetricCoverage.Complete, int? n = null) => new() { Coverage = c, IntegerValue = v, SampleCount = n };

    [Fact]
    public void ParseUnits_AllOrEmptyOpensEveryUnit()
    {
        foreach (var arg in new[] { null, "", " ", "all", "ALL" })
        {
            var (units, unknown) = VerifyArgs.ParseUnits(arg);
            Assert.Equal(AcquisitionUnits.All.Select(u => u.Id), units);
            Assert.Empty(unknown);
        }
    }

    [Fact]
    public void ParseUnits_KeepsOrder_DropsDuplicates_AndReportsUnknownIds()
    {
        var (units, unknown) = VerifyArgs.ParseUnits(" mental,research,,mental, nope ,research_state");
        Assert.Equal(new[] { "mental", "research", "research_state" }, units);
        Assert.Equal(new[] { "nope" }, unknown);
    }

    [Fact]
    public void Flatten_NamesBreakdownEntries_AndShowsNonCompleteCoverage()
    {
        var flat = VerifyValues.Flatten(Obs(
            (ResearchFamily.Completed, MetricEntry.Scalar, I(3)),
            (ResearchFamily.CompletedTicks, "Electricity", new MetricValue { Coverage = MetricCoverage.Partial, RealValue = 1.5 }),
            (MedicineFamily.SurgerySuccessRate, MetricEntry.Scalar, new MetricValue { Coverage = MetricCoverage.Complete, RealValue = 0.5, Numerator = 1, Denominator = 2 }),
            (MedicineFamily.TendQualityMean, MetricEntry.Scalar, new MetricValue { Coverage = MetricCoverage.NotApplicable, Numerator = 0, Denominator = 0, SampleCount = 0 })));
        Assert.Equal("3", flat[ResearchFamily.Completed].Display);
        Assert.Equal("1.5 [Partial]", flat[ResearchFamily.CompletedTicks + "[Electricity]"].Display);
        Assert.Equal("0.5 (1/2)", flat[MedicineFamily.SurgerySuccessRate].Display);
        Assert.Equal("(0/0) n=0 [NotApplicable]", flat[MedicineFamily.TendQualityMean].Display);
    }

    [Fact]
    public void UnitOf_FindsTheAcquisitionUnit_OfScalarsAndBreakdownEntries()
    {
        Assert.Equal("research", VerifyValues.UnitOf(ResearchFamily.Completed));
        Assert.Equal("research", VerifyValues.UnitOf(ResearchFamily.CompletedTicks + "[Electricity]"));
        Assert.Null(VerifyValues.UnitOf(MetricKeys.WealthItemsPeak));   // Phase A: no acquisition unit
        Assert.Null(VerifyValues.UnitOf("not.a.key"));
    }

    [Fact]
    public void Diff_ListsChangedAddedAndRemovedValues_ButNotEqualOnes()
    {
        var before = VerifyValues.Flatten(Obs(("a", "", I(1)), ("b", "", I(2)), ("c", "", I(5))));
        var after = VerifyValues.Flatten(Obs(("a", "", I(1)), ("b", "", I(4)), ("d", "", I(7)), ("c", "", I(5, MetricCoverage.Partial))));
        var diff = VerifyValues.Diff(before, after);
        Assert.Equal(new[] { "b", "c", "d" }, diff.Select(d => d.Key));
        Assert.Equal(2, diff[0].Delta);
        Assert.Equal(0, diff[1].Delta);   // coverage only
        Assert.Equal(7, diff[2].Delta);   // new value
    }

    [Fact]
    public void Evaluate_PassesOnExactChanges_AndRequiresHooksForPositiveCases()
    {
        var before = VerifyValues.Flatten(Obs((ResearchFamily.Completed, "", I(2)), (ResearchFamily.DurationMax, "", I(0, n: 1))));
        var after = VerifyValues.Flatten(Obs((ResearchFamily.Completed, "", I(3)), (ResearchFamily.DurationMax, "", I(0, n: 2))));
        var expect = new[] { new Expect(ResearchFamily.Completed, 1), new Expect(ResearchFamily.DurationMax, 1, Samples: true) };

        var pass = CaseCheck.Evaluate("research.threshold", "research", expect, before, after, hooks: 3, requireHooks: true);
        Assert.Equal(CaseOutcome.Pass, pass.Outcome);
        Assert.Equal("research.live.completed:+1,research.live.duration_max#n:+1", pass.Expected);
        Assert.Equal(pass.Expected, pass.Actual);

        Assert.Equal(CaseOutcome.Fail, CaseCheck.Evaluate("x", "research", expect, before, after, hooks: 0, requireHooks: true).Outcome);
        Assert.Equal(CaseOutcome.Fail, CaseCheck.Evaluate("x", "research", new[] { new Expect(ResearchFamily.Completed, 2) }, before, after, 3, true).Outcome);
        // A negative case: nothing may change, hooks or not.
        Assert.Equal(CaseOutcome.Pass, CaseCheck.Evaluate("x", "research", new[] { new Expect("missing.key", 0) }, before, after, 0, false).Outcome);
    }

    [Fact]
    public void Line_HasTheDocumentedFields_InOrder()
    {
        var r = new CaseResult("research.direct", "research", CaseOutcome.Fail, "research.live.completed:+1", "research.live.completed:+0", 2, "no \"hook\"\nfired");
        Assert.Equal("[RWS-VERIFY] case=research.direct unit=research game=1.7.4985_rev1269 expected=research.live.completed:+1 "
            + "actual=research.live.completed:+0 hooks=2 result=FAIL note=\"no 'hook' fired\"", CaseCheck.Line(r, "1.7.4985 rev1269"));
        Assert.EndsWith("result=SKIP note=\"no project\"", CaseCheck.Line(CaseCheck.Skip("c", "u", "no project"), "v"));
    }

    [Fact]
    public void BatchCatalogAudit_ClassifiesEveryUnverifiedOutputWithoutPretendingToProveIt()
    {
        // The 268 active acquisition definitions (after 10 user-approved deletions) are a stable, separately
        // auditable baseline. Derived comparisons reuse those acquisition gates.
        var defs = MetricRegistry.BaseCatalog.Where(d => d.Acquisition is not null).ToArray();
        Assert.Equal(268, defs.Length);
        var snapshot = SnapshotCatalog.Definitions[0];
        var reported = VerifyCatalogAudit.Build(defs,
            new HashSet<string> { snapshot.Acquisition!, "research" },
            new HashSet<string> { snapshot.StoredKey },
            new HashSet<string>());
        Assert.Equal(defs.Length, reported.Count);
        Assert.Equal(CatalogAuditState.Observed, Assert.Single(reported, r => r.Definition.Key == snapshot.Key).State);
        Assert.Equal(CatalogAuditState.NotObserved,
            Assert.Single(reported, r => r.Definition.Key == ResearchFamily.Completed).State);
        Assert.Equal(CatalogAuditState.NotRecording,
            Assert.Single(reported, r => r.Definition.Key == MentalFamily.Breaks).State);
        Assert.All(reported.Where(x => x.Definition.Overall == OverallAggregation.Ranking),
            row => Assert.Equal(CatalogAuditState.DerivedOnly, row.State));
        Assert.All(reported, row => Assert.False(row.Verified));
    }

    [Fact]
    public void BatchCatalogAudit_DlcUnavailableIsNotAFailedTestAndSharedStoredKeysAreRecognized()
    {
        var first = MetricRegistry.Find(MetricKeys.WealthItemsPeak)!;
        var alternate = first with { Key = "alias", Acquisition = "test", StoredKey = first.StoredKey };
        var marked = VerifyCatalogAudit.Build(new[] { alternate },
            new HashSet<string> { "test" }, new HashSet<string> { first.StoredKey }, new HashSet<string>());
        Assert.Equal(CatalogAuditState.Observed, Assert.Single(marked).State);
        var dlc = alternate with { RequiredDlc = AcquisitionUnits.Biotech };
        var missing = VerifyCatalogAudit.Build(new[] { dlc },
            new HashSet<string> { "test" }, new HashSet<string>(), new HashSet<string> { AcquisitionUnits.Biotech });
        Assert.Equal(CatalogAuditState.DlcUnavailable, Assert.Single(missing).State);
    }

    [Fact]
    public void GuidedAudit_GroupsEveryMetricAndPrioritizesBrokenCasesBeforeManualChecks()
    {
        var rows = new[]
        {
            new CatalogAuditRow(LiveCatalog.Definitions.First(d => d.Acquisition == "research"), CatalogAuditState.Observed),
            new CatalogAuditRow(SnapshotCatalog.Definitions.First(d => d.Acquisition == "roster"), CatalogAuditState.NotObserved),
            new CatalogAuditRow(LiveCatalog.Definitions.First(d => d.Acquisition == "mental"), CatalogAuditState.NotObserved),
            new CatalogAuditRow(LiveCatalog.Definitions.First(d => d.Acquisition == "trade"), CatalogAuditState.NotRecording),
            new CatalogAuditRow(RankingDependencyCatalog.Definitions[0], CatalogAuditState.DerivedOnly),
        };
        var cases = new[] { new CaseResult("research.direct", "research", CaseOutcome.Fail, "+1", "+0", 1) };
        var report = GuidedAuditReport.Build(rows, cases, "1.7", "campaign-test", DateTime.UnixEpoch);
        Assert.Equal(rows.Length, report.Metrics.Count);
        Assert.Equal("INVESTIGATE_FAILURE", report.Units.Single(u => u.Unit == "research").Action);
        Assert.Equal("SAVE_AND_RESCAN", report.Units.Single(u => u.Unit == "roster").Action);
        Assert.Equal("TRIGGER_EVENT", report.Units.Single(u => u.Unit == "mental").Action);
        Assert.Equal("ENABLE_RECORDING", report.Units.Single(u => u.Unit == "trade").Action);
        Assert.Equal("REVIEW_DEPENDENCIES", report.Units.Single(u => u.Unit == "colony_start").Action);
        Assert.Equal("research", report.Units[0].Unit); // failures are always top priority
        Assert.All(report.Metrics, m => Assert.False(m.Verified));
    }

    [Fact]
    public void GuidedAudit_ExportsRecoverableJsonAndCsvWithoutClaimingVerification()
    {
        var data = SnapshotCatalog.Definitions[0] with { Key = "one,\"two\"\nthree" };
        var report = GuidedAuditReport.Build(
            new[] { new CatalogAuditRow(data, CatalogAuditState.Observed) },
            Array.Empty<CaseResult>(), "1.7.4995", "test-campaign", DateTime.UnixEpoch);
        var roundtrip = GuidedAuditReport.ReadJson(report.ToJson());
        Assert.NotNull(roundtrip);
        Assert.Equal(report.Metrics[0].Key, roundtrip.Metrics[0].Key);
        Assert.False(roundtrip.Metrics[0].Verified);
        Assert.Contains("\"one,\"\"two\"\"\nthree\"", report.ToCsv());
        Assert.Contains("NOT VERIFIED", report.ToMarkdown());
    }

    [Fact]
    public void GuidedAudit_ProgressComparisonRejectsDifferentCampaignAndFlagsRegressions()
    {
        var d = SnapshotCatalog.Definitions[0];
        var old = GuidedAuditReport.Build(new[] { new CatalogAuditRow(d, CatalogAuditState.NotObserved) },
            Array.Empty<CaseResult>(), "1.7", "same", DateTime.UnixEpoch);
        var next = GuidedAuditReport.Build(new[] { new CatalogAuditRow(d, CatalogAuditState.Observed) },
            Array.Empty<CaseResult>(), "1.7", "same", DateTime.UnixEpoch);
        var progress = GuidedAuditReport.Compare(old, next);
        Assert.Equal(new[] { d.Key }, progress.NewlyObserved);
        Assert.Empty(progress.LostObservations);
        var regression = GuidedAuditReport.Compare(next, old);
        Assert.Equal(new[] { d.Key }, regression.LostObservations);
        Assert.False(GuidedAuditReport.Compare(old, next with { CampaignId = "other" }).Comparable);
    }

    [Theory]
    [InlineData("""{"GameVersion":"1.7","CampaignId":"same","Metrics":[null],"Units":[],"Cases":[]}""")]
    [InlineData("""{"GameVersion":"1.7","CampaignId":"same","Metrics":[{"Key":"dupe","StoredKey":"a","Unit":"research","Kind":"Family","State":"Observed","Action":"CROSS_CHECK_VALUES"},{"Key":"dupe","StoredKey":"a","Unit":"research","Kind":"Family","State":"Observed","Action":"CROSS_CHECK_VALUES"}],"Units":[],"Cases":[]}""")]
    [InlineData("""{"GameVersion":"1.7","CampaignId":"same","Metrics":[],"Units":[null],"Cases":[]}""")]
    [InlineData("""{"GameVersion":"1.7","CampaignId":"same","Metrics":[],"Units":[],"Cases":[null]}""")]
    public void GuidedAudit_RejectsCorruptedPriorReports(string json)
    {
        // Old reports must not throw from Compare/ToMarkdown when a previous report
        // was truncated, edited, or partially malformed.
        Assert.Null(GuidedAuditReport.ReadJson(json));
    }
}
