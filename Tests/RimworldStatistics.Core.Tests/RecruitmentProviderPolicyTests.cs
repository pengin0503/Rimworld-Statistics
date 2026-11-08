namespace RimworldStatistics.Core.Tests;

public sealed class RecruitmentProviderPolicyTests
{
    [Fact]
    public void AuditedRegistrationRequiresExactModAndCompatibleVersion()
    {
        var descriptors = new[] { RecruitmentMetricPolicy.StartedDescriptor, RecruitmentMetricPolicy.ActiveDescriptor };
        Assert.All(descriptors, d => Assert.True(d.IsValid()));
        Assert.Equal(new[] { RecruitmentMetricPolicy.StartedKey, RecruitmentMetricPolicy.ActiveKey },
            descriptors.Select(d => d.StableKey));
        Assert.Equal(new[] { RecruitmentMetricPolicy.RequestsFamilyId, RecruitmentMetricPolicy.QueueFamilyId },
            descriptors.Select(d => d.AcquisitionId));
        foreach (var d in descriptors)
        {
            Assert.Equal(ModProviderAvailability.MissingPackage,
                ModProviderDiscovery.Resolve(d, Array.Empty<ModPackageInfo>()).State);
            Assert.Equal(ModProviderAvailability.MissingVersion,
                ModProviderDiscovery.Resolve(d, new[] { new ModPackageInfo("PENGIN0503.RECRUITMENT", null) }).State);
            Assert.Equal(ModProviderAvailability.IncompatibleVersion,
                ModProviderDiscovery.Resolve(d, new[] { new ModPackageInfo(RecruitmentMetricPolicy.OwnerPackageId, "1.1.1") }).State);
            Assert.Equal(ModProviderAvailability.Available,
                ModProviderDiscovery.Resolve(d, new[] { new ModPackageInfo(RecruitmentMetricPolicy.OwnerPackageId, "1.1.2") }).State);
            Assert.Equal(ModProviderAvailability.IncompatibleVersion,
                ModProviderDiscovery.Resolve(d, new[] { new ModPackageInfo(RecruitmentMetricPolicy.OwnerPackageId, "1.2.0") }).State);
        }
    }

    [Fact]
    public void EventIsCountedOnceOnlyAfterValidSuccessfulDriveCreation()
    {
        var f = new FamilyState { Revision = 1 };
        Assert.Equal(RecruitmentRecordResult.Invalid, RecruitmentMetricPolicy.RecordStarted(f, 0, 20));
        Assert.Equal(RecruitmentRecordResult.Recorded, RecruitmentMetricPolicy.RecordStarted(f, 17, 21));
        Assert.Equal(RecruitmentRecordResult.Duplicate, RecruitmentMetricPolicy.RecordStarted(f, 17, 22));
        Assert.Equal(RecruitmentRecordResult.Recorded, RecruitmentMetricPolicy.RecordStarted(f, 18, 23));
        Assert.Equal(2L, f.Peek("requests_started")?.N);
        Assert.Equal(21, f.Peek("requests_started")?.First);
        Assert.Equal(23, f.Peek("requests_started")?.Last);
    }

    [Fact]
    public void RecordLimitIsBoundedAndUnknownRatherThanOvercounted()
    {
        var f = new FamilyState { Revision = 1 };
        for (var i = 1; i <= FamilyOps.KeyCap; i++)
            Assert.Equal(RecruitmentRecordResult.Recorded, RecruitmentMetricPolicy.RecordStarted(f, i, i));
        Assert.Equal(RecruitmentRecordResult.CapExceeded, RecruitmentMetricPolicy.RecordStarted(f, FamilyOps.KeyCap + 1, 5000));
        Assert.Equal(FamilyOps.KeyCap, f.Peek("requests_started")?.N);
        Assert.True(f.Peek("seen_drive_ids")?.Overflow);
    }

    [Fact]
    public void SnapshotIsMissingForUnavailableComponentAndNeverFakesZero()
    {
        Assert.Null(RecruitmentMetricPolicy.Snapshot(null));
        Assert.Null(RecruitmentMetricPolicy.Snapshot(-1));
        var v = RecruitmentMetricPolicy.Snapshot(0);
        Assert.NotNull(v);
        Assert.Equal(0, v.Values[MetricEntry.Scalar].Number);
        Assert.Equal(MetricCoverage.SnapshotOnly, v.Values[MetricEntry.Scalar].Coverage);
        Assert.Equal(3, RecruitmentMetricPolicy.Snapshot(3)!.Values[MetricEntry.Scalar].Number);
    }

    [Fact]
    public void ResumeAndBranchesCarryDedupKeysWithoutAddingParentCountsTwice()
    {
        var id = RecruitmentMetricPolicy.RequestsFamilyId;
        var spec = new[] { new FamilySpec(id, 1) };
        var experimental = new HashSet<string> { id };
        var parent = LiveMetricAccumulator.StartNew(spec, 0, experimental);
        Assert.Equal(RecruitmentRecordResult.Recorded,
            RecruitmentMetricPolicy.RecordStarted(parent.Family(id)!, 1, 55));
        var checkpoint = parent.PrepareSave(100, DateTime.UtcNow, "parent")!;
        parent.Commit(checkpoint);
        var branchA = LiveMetricAccumulator.Resume(checkpoint.Json, spec, 100, experimental);
        var branchB = LiveMetricAccumulator.Resume(checkpoint.Json, spec, 100, experimental);
        Assert.Equal(RecruitmentRecordResult.Duplicate,
            RecruitmentMetricPolicy.RecordStarted(branchA.Family(id)!, 1, 110));
        RecruitmentMetricPolicy.RecordStarted(branchA.Family(id)!, 2, 111);
        RecruitmentMetricPolicy.RecordStarted(branchB.Family(id)!, 3, 112);
        Assert.Equal(2L, branchA.Family(id)!.Peek("requests_started")?.N);
        Assert.Equal(2L, branchB.Family(id)!.Peek("requests_started")?.N);
        Assert.Equal(1L, checkpoint.Payload!.Families[id].Peek("requests_started")?.N);
    }
}
