using System.Text.Json;
using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

/// <summary>S32: subdivided Partial reasons, their priority, and additive JSON compatibility.</summary>
public sealed class PartialReasonTests
{
    private static readonly LiveFamily Dummy = new()
    {
        Name = "partial.test",
        Revision = 1,
        Build = c =>
        {
            c.Put("partial.test.count", c.Value(integer: c.State.Peek("events")?.N ?? 0));
            c.Put("partial.test.weakened", c.Value(integer: 2, partial: true));
            // A value a family builds by hand: no fact of its own, so the final classifier can only say Unknown.
            c.Put("partial.test.unknown", new MetricValue { Coverage = MetricCoverage.Partial, IntegerValue = 1 });
        },
    };

    private static FamilyState State(int since, bool mixedRules = false, int through = 200)
    {
        var state = new FamilyState
        {
            Revision = Dummy.Revision,
            SinceTick = since,
            ThroughTick = through,
            Continuous = true,
            Metrics = { ["events"] = new Accumulator { N = 3 } },
        };
        if (mixedRules)
            state.Metrics[ColonyWorkAttribution.MixedMarker] = new Accumulator { N = 1 };
        return state;
    }

    private static MetricObservation Checkpoint(FamilyState state)
    {
        var payload = new CheckpointPayload { CheckpointId = "p", SaveTick = 200 };
        payload.Families[Dummy.Name] = state;
        return CheckpointMetrics.ToObservation(payload, "{}", new ColonyIdentity { ModCampaignId = "partial" },
            "1.7.4985 rev1269", null, [Dummy]);
    }

    private static MetricValue Scalar(MetricObservation obs, string key) => obs.Metrics[key].Values[MetricEntry.Scalar];

    // ----- the classifier -----

    private static MetricValue Partial(bool weakened = false, int? since = null) => new()
    {
        Coverage = MetricCoverage.Partial, IntegerValue = 1, Weakened = weakened ? true : null, SinceTick = since,
    };

    [Fact]
    public void ClassifierPriorityIsMixedRuleThenWeakenedThenUnrecordedThenIncomplete()
    {
        var both = Partial(weakened: true, since: 10);
        Assert.Equal(PartialReason.MixedRule, PartialReasonClassifier.Classify(both, mixedRule: true, incompleteSeries: true));
        Assert.Equal(PartialReason.WeakenedByFamily, PartialReasonClassifier.Classify(both, incompleteSeries: true));
        Assert.Equal(PartialReason.UnrecordedInterval, PartialReasonClassifier.Classify(Partial(since: 10), incompleteSeries: true));
        Assert.Equal(PartialReason.IncompleteSeries, PartialReasonClassifier.Classify(Partial(), incompleteSeries: true));
        Assert.Equal(PartialReason.Unknown, PartialReasonClassifier.Classify(Partial()));
    }

    [Theory]
    [InlineData(MetricCoverage.Complete)]
    [InlineData(MetricCoverage.SnapshotOnly)]
    [InlineData(MetricCoverage.Unknown)]
    [InlineData(MetricCoverage.NotApplicable)]
    public void OnlyPartialValuesHaveAReason(MetricCoverage coverage)
    {
        var value = new MetricValue { Coverage = coverage, IntegerValue = 1, Weakened = true, SinceTick = 10 };
        Assert.Null(PartialReasonClassifier.Classify(value, mixedRule: true, incompleteSeries: true));
        PartialReasonClassifier.Apply(value, mixedRule: true, incompleteSeries: true);
        Assert.Null(value.PartialReason);
    }

    [Fact]
    public void ApplyNeverDowngradesAnAlreadyStrongerReason()
    {
        var value = Partial();
        value.PartialReason = PartialReason.MixedRule;
        PartialReasonClassifier.Apply(value);
        Assert.Equal(PartialReason.MixedRule, value.PartialReason);

        var weaker = Partial();
        PartialReasonClassifier.Apply(weaker, incompleteSeries: true);
        PartialReasonClassifier.Apply(weaker, mixedRule: true);
        Assert.Equal(PartialReason.MixedRule, weaker.PartialReason);
    }

    // ----- wiring: checkpoint families -----

    [Fact]
    public void MixedAttributionRulesMakeEveryPartialFamilyValueMixedRule()
    {
        var obs = Checkpoint(State(since: 100, mixedRules: true));
        // Started late would be UnrecordedInterval and partial-by-family would be WeakenedByFamily; MixedRule wins.
        Assert.Equal(PartialReason.MixedRule, Scalar(obs, "partial.test.count").PartialReason);
        Assert.Equal(PartialReason.MixedRule, Scalar(obs, "partial.test.weakened").PartialReason);
        Assert.Equal(PartialReason.MixedRule, Scalar(obs, "partial.test.unknown").PartialReason);
    }

    [Fact]
    public void AWeakenedFamilyValueSaysWeakenedByFamily_AndACompleteOneHasNoReason()
    {
        var obs = Checkpoint(State(since: 0));
        Assert.Equal(PartialReason.WeakenedByFamily, Scalar(obs, "partial.test.weakened").PartialReason);
        Assert.Equal(PartialReason.Unknown, Scalar(obs, "partial.test.unknown").PartialReason);
        Assert.Null(Scalar(obs, "partial.test.count").PartialReason);   // complete
    }

    [Fact]
    public void ARecordingThatStartedAfterTheColonyStartIsUnrecordedInterval()
    {
        var obs = Checkpoint(State(since: 100));
        var count = Scalar(obs, "partial.test.count");
        Assert.Equal(MetricCoverage.Partial, count.Coverage);
        Assert.Null(count.Weakened);
        Assert.Equal(PartialReason.UnrecordedInterval, count.PartialReason);
    }

    // ----- wiring: history summaries -----

    [Fact]
    public void AHistorySummaryWithMissingPointsIsIncompleteSeries_AndACompleteOneHasNoReason()
    {
        var bytes = new SaveXmlBuilder().Minimal().WealthTotal(100f, float.NaN, 100f).Build();
        using var stream = new MemoryStream(bytes);
        var metrics = new SaveReader().ReadDetailed(stream, "a.rws", bytes.Length, DateTime.UnixEpoch).Metrics!;

        var mean = metrics.Metrics[MetricKeys.WealthTotalMean].Values[MetricEntry.Scalar];
        Assert.Equal(MetricCoverage.Partial, mean.Coverage);
        Assert.Equal(PartialReason.IncompleteSeries, mean.PartialReason);
        Assert.Null(metrics.Metrics[MetricKeys.WealthTotalFirst].Values[MetricEntry.Scalar].PartialReason);
    }

    // ----- JSON compatibility -----

    [Fact]
    public void AValueWithoutAReasonWritesNoFieldAndOldJsonReadsBackUnchanged()
    {
        var value = new MetricValue { Coverage = MetricCoverage.Partial, RealValue = 1.5, Weakened = true };
        var json = JsonSerializer.Serialize(value, ProfileJson.Options);
        Assert.DoesNotContain("partialReason", json, StringComparison.Ordinal);
        Assert.Equal(MetricCoverage.Partial, JsonSerializer.Deserialize<MetricValue>(json, ProfileJson.Options)!.Coverage);

        // A record written before the field existed: still reads as the same value, with no reason invented.
        var old = JsonSerializer.Deserialize<MetricValue>(
            "{\"coverage\":\"partial\",\"realValue\":1.5,\"weakened\":true}", ProfileJson.Options)!;
        Assert.Equal((MetricCoverage.Partial, 1.5, (bool?)true, (PartialReason?)null),
            (old.Coverage, old.RealValue, old.Weakened, old.PartialReason));
    }

    [Fact]
    public void AReasonRoundTripsAsCamelCaseAndOnlyWhenSet()
    {
        var value = new MetricValue { Coverage = MetricCoverage.Partial, IntegerValue = 1, PartialReason = PartialReason.MixedRule };
        var json = JsonSerializer.Serialize(value, ProfileJson.Options);
        Assert.Contains("\"partialReason\": \"mixedRule\"", json, StringComparison.Ordinal);
        Assert.Equal(PartialReason.MixedRule,
            JsonSerializer.Deserialize<MetricValue>(json, ProfileJson.Options)!.PartialReason);

        var observation = new MetricObservation
        {
            Metrics = { ["k"] = MetricEntry.Of(1, value) },
        };
        var profileJson = JsonSerializer.Serialize(observation, ProfileJson.CompactOptions);
        var back = JsonSerializer.Deserialize<MetricObservation>(profileJson, ProfileJson.Options)!;
        Assert.Equal(PartialReason.MixedRule, back.Metrics["k"].Values[MetricEntry.Scalar].PartialReason);
    }

    [Fact]
    public void IsWellFormedRejectsAnUndefinedReason()
    {
        Assert.True(new MetricValue { Coverage = MetricCoverage.Partial, PartialReason = PartialReason.MixedRule }.IsWellFormed());
        Assert.False(new MetricValue { Coverage = MetricCoverage.Partial, PartialReason = (PartialReason)999 }.IsWellFormed());
    }
}
