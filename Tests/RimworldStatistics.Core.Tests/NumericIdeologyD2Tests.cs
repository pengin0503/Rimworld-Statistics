namespace RimworldStatistics.Core.Tests;

/// <summary>Ideology outcomes are confirmed facts, not ritual starts or conversion attempts.</summary>
public sealed class NumericIdeologyD2Tests
{
    private static MetricObservation Save(LiveMetricAccumulator a)
    {
        var d = a.PrepareSave(120000, DateTime.UtcNow, "ideo-d2")!;
        return CheckpointMetrics.ToObservation(d.Payload!, d.Json,
            new ColonyIdentity { ModCampaignId = "ideo-d2" }, null, null);
    }

    [Fact]
    public void RitualOutcomesAreDeduplicated_InterruptionsAreSeparateFromSuccessRate()
    {
        var a = LiveMetricAccumulator.StartNew(new[] { IdeologyFamily.Family.Spec }, 0);
        var s = a.Family(IdeologyFamily.Name)!;
        IdeologyFamily.RitualConcluded(s, "r1", "Great", true, completed: true, interrupted: false);
        IdeologyFamily.RitualConcluded(s, "r1", "Great", true, completed: true, interrupted: false);
        IdeologyFamily.RitualConcluded(s, "r2", "Poor", false, completed: true, interrupted: false);
        IdeologyFamily.RitualConcluded(s, "r3", null, null, completed: false, interrupted: true);
        IdeologyFamily.RitualConcluded(s, "r4", null, null, completed: false, interrupted: false);
        var o = Save(a);
        var outcomes = o.Metrics[IdeologyFamily.RitualResults].Values;
        Assert.Equal(3, outcomes[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(1, outcomes["Great"].IntegerValue);
        Assert.Equal(1, outcomes["Poor"].IntegerValue);
        Assert.Equal(1, outcomes["interrupted"].IntegerValue);
        var rate = o.Scalar(IdeologyFamily.RitualSuccessRate, MetricKeys.Revision(IdeologyFamily.RitualSuccessRate))!;
        Assert.Equal(0.5, rate.RealValue);
        Assert.Equal(1, rate.Numerator);
        Assert.Equal(2, rate.Denominator);
    }

    [Fact]
    public void RoleAppointmentsAndConversionDurationRequireActualTransitionsAndMatchingStart()
    {
        var a = LiveMetricAccumulator.StartNew(new[] { IdeologyFamily.Family.Spec }, 0);
        var s = a.Family(IdeologyFamily.Name)!;
        IdeologyFamily.RoleAppointed(s, "p", null, "Leader", eligible: true, confirmed: false);
        IdeologyFamily.RoleAppointed(s, "p", null, "Leader", eligible: true, confirmed: true);
        IdeologyFamily.RoleAppointed(s, "p", "Leader", "Leader", eligible: true, confirmed: true);
        IdeologyFamily.RoleAppointed(s, "npc", null, "Leader", eligible: false, confirmed: true);
        IdeologyFamily.ConversionActionStarted(s, "p", "NewIdeo", 20000, eligible: true, explicitAction: true);
        IdeologyFamily.ConversionActionStarted(s, "p", "NewIdeo", 30000, eligible: true, explicitAction: true);
        IdeologyFamily.ConversionReachedTarget(s, "p", "Other", 80000, eligible: true);
        IdeologyFamily.ConversionReachedTarget(s, "p", "NewIdeo", 80000, eligible: true);
        IdeologyFamily.ConversionReachedTarget(s, "p", "NewIdeo", 90000, eligible: true);
        var o = Save(a);
        Assert.Equal(1, o.Scalar(IdeologyFamily.RoleAppointments, MetricKeys.Revision(IdeologyFamily.RoleAppointments))!.IntegerValue);
        var duration = o.Scalar(IdeologyFamily.ConversionDaysMean, MetricKeys.Revision(IdeologyFamily.ConversionDaysMean))!;
        Assert.Equal(1, duration.RealValue);
        Assert.Equal(1, duration.Denominator);
    }
}
