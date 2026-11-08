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
}
