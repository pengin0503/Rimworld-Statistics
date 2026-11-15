using RimworldStatistics.VfePower;
using RimworldStatistics.VheHelixienGas;
using RimworldStatistics.VreRecycling;

namespace RimworldStatistics.Core.Tests;

public sealed class Batch1PowerGasRecyclingTests
{
    private static readonly DateTime T0 = new(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);
    private const string Campaign = "feedfacefeedfacefeedfacefeedface";

    private static ColonyIdentity TestIdentity() => new()
    {
        SeedString = "batch1-power-gas-recycling",
        PersistentRandomValue = 17,
        StartingTile = "4,5",
        GameStartAbsTick = 0,
        FirstWealthBits = 9,
        StartingPawnIds = { "Thing_Human1" },
        ModCampaignId = Campaign,
    };

    [Theory]
    [InlineData(false, true, true, true, true, true)]
    [InlineData(true, true, true, true, true, false)]
    [InlineData(false, false, true, true, true, false)]
    [InlineData(false, true, false, true, true, false)]
    [InlineData(false, true, true, false, true, false)]
    [InlineData(false, true, true, true, false, false)]
    public void VfePowerNuclearMeltdownCountsOnlyNewCommittedBreakdown(
        bool wasSignaled,
        bool isSignaled,
        bool isBrokenDown,
        bool playerOwned,
        bool spawned,
        bool expected)
    {
        Assert.Equal(expected, VfePowerRules.MeltdownCommitted(
            wasSignaled, isSignaled, isBrokenDown, playerOwned, spawned));
    }

    [Theory]
    [InlineData(30f, true, true, 30d)]
    [InlineData(12.5f, true, true, 12.5d)]
    [InlineData(0f, true, true, 0d)]
    [InlineData(-5f, true, true, 0d)]
    [InlineData(float.NaN, true, true, 0d)]
    [InlineData(float.PositiveInfinity, true, true, 0d)]
    [InlineData(30f, false, true, 0d)]
    [InlineData(30f, true, false, 0d)]
    public void VheHelixienGasOnlyAcceptsFinitePositiveStoredPipeUnits(
        float stored,
        bool matchingPipe,
        bool playerOwned,
        double expected)
    {
        Assert.Equal(expected, VheHelixienGasRules.AcceptedUnits(stored, matchingPipe, playerOwned), 6);
    }

    [Theory]
    [InlineData(true, true, true, 5L, 0, 0, true, 5L)]
    [InlineData(true, true, true, 0L, 0, 0, true, 0L)]
    [InlineData(true, true, false, 99L, 10, 15, false, 5L)]
    [InlineData(true, true, false, 5L, 10, 10, false, 0L)]
    [InlineData(true, true, false, 5L, 10, 15, true, 0L)]
    [InlineData(false, true, true, 5L, 0, 0, true, 0L)]
    [InlineData(true, false, false, 5L, 10, 15, false, 0L)]
    public void VreRecyclingOutputHandlesGenPlaceAndDirectMergeWithoutDoubleCounting(
        bool spawned,
        bool playerOwned,
        bool tryPlaceCalled,
        long placedActionCount,
        int existingBeforeCount,
        int existingAfterCount,
        bool existingDestroyed,
        long expected)
    {
        Assert.Equal(expected, VreRecyclingRules.ConfirmedOutputCount(
            spawned, playerOwned, tryPlaceCalled, placedActionCount,
            existingBeforeCount, existingAfterCount, existingDestroyed));
    }

    [Fact]
    public void Batch1DescriptorsRegisterAndCheckpointAcrossSaveBranches()
    {
        var power = new ModMetricDescriptor
        {
            ProviderId = "rws.nuclear_meltdowns",
            OwnerPackageId = "VanillaExpanded.VFEPower",
            FamilyId = "nuclear_meltdowns",
            MetricName = "completed",
            Revision = 1,
            Source = MetricObservationSource.Checkpoint,
            Category = MetricCategory.Facilities,
            HasBreakdown = true,
            RequiredPackages = [new ModPackageRequirement("VanillaExpanded.VFEPower")],
            LabelKey = "RWS_VfePower_NuclearMeltdowns",
            DescriptionKey = "RWS_VfePower_NuclearMeltdownsDesc",
        };
        var gas = new ModMetricDescriptor
        {
            ProviderId = "rws.helixien_gas",
            OwnerPackageId = "VanillaExpanded.HelixienGas",
            FamilyId = "helixien_gas",
            MetricName = "stored_units",
            Revision = 1,
            Source = MetricObservationSource.Checkpoint,
            Category = MetricCategory.Work,
            HasBreakdown = false,
            RequiredPackages =
            [
                new ModPackageRequirement("VanillaExpanded.HelixienGas"),
                new ModPackageRequirement("OskarPotocki.VanillaFactionsExpanded.Core"),
            ],
            LabelKey = "RWS_VheHelixienGas_StoredUnits",
            DescriptionKey = "RWS_VheHelixienGas_StoredUnitsDesc",
        };
        var recycling = new ModMetricDescriptor
        {
            ProviderId = "rws.vre_recycling",
            OwnerPackageId = "VanillaExpanded.Recycling",
            FamilyId = "processed_items",
            MetricName = "completed",
            Revision = 1,
            Source = MetricObservationSource.Checkpoint,
            Category = MetricCategory.Work,
            HasBreakdown = true,
            RequiredDlc = AcquisitionUnits.Biotech,
            RequiredPackages = [new ModPackageRequirement("VanillaExpanded.Recycling")],
            LabelKey = "RWS_VreRecycling_ProcessedItems",
            DescriptionKey = "RWS_VreRecycling_ProcessedItemsDesc",
        };

        var registry = new ModMetricProviderRegistry();
        Assert.True(registry.Register(power).Accepted);
        Assert.True(registry.Register(gas).Accepted);
        Assert.True(registry.Register(recycling).Accepted);

        Assert.Equal(ModProviderAvailability.MissingPackage,
            ModProviderDiscovery.Resolve(gas, [new ModPackageInfo("VanillaExpanded.HelixienGas", null)]).State);
        Assert.Equal(ModProviderAvailability.Available,
            ModProviderDiscovery.Resolve(gas,
            [
                new ModPackageInfo("VanillaExpanded.HelixienGas", null),
                new ModPackageInfo("OskarPotocki.VanillaFactionsExpanded.Core", null),
            ]).State);

        var families = new LiveFamily[]
        {
            new()
            {
                Name = power.AcquisitionId,
                Revision = 1,
                Build = o => OptionalTransitionRecording.PutCount(o, "completed", power.StableKey, true),
            },
            new()
            {
                Name = gas.AcquisitionId,
                Revision = 1,
                Build = o => o.Put(gas.StableKey, o.Value(real: o.State.Peek("stored")?.Sum ?? 0d)),
            },
            new()
            {
                Name = recycling.AcquisitionId,
                Revision = 1,
                RequiredDlc = recycling.RequiredDlc,
                Build = o => OptionalTransitionRecording.PutCount(o, "completed", recycling.StableKey, true),
            },
        };

        var specs = families.Select(f => f.Spec).ToArray();
        var acc = LiveMetricAccumulator.StartNew(specs, 0);
        OptionalTransitionRecording.RecordCount(acc.Family(power.AcquisitionId)!, "completed",
            1, OptionalTransitionRecording.QualifiedDefKey("VanillaExpanded.VFEPower", "VPE_NuclearGenerator"), true);
        OptionalTransitionRecording.RecordReal(acc.Family(gas.AcquisitionId)!, "stored", 24.5);
        OptionalTransitionRecording.RecordCount(acc.Family(recycling.AcquisitionId)!, "completed",
            5, OptionalTransitionRecording.QualifiedDefKey("VanillaExpanded.Recycling", "VRecyclingE_TrashBrick"), true);

        var save1 = acc.PrepareSave(100, T0, "save1")!;
        acc.Commit(save1);

        OptionalTransitionRecording.RecordReal(acc.Family(gas.AcquisitionId)!, "stored", 5.5);
        OptionalTransitionRecording.RecordCount(acc.Family(recycling.AcquisitionId)!, "completed",
            2, OptionalTransitionRecording.QualifiedDefKey("Ludeon.RimWorld", "Steel"), true);
        var branchA = acc.PrepareSave(200, T0.AddMinutes(5), "branchA")!;
        acc.Commit(branchA);

        // Branched reload without optional mods preserves save1 baseline and adds zero.
        var reloaded = LiveMetricAccumulator.Resume(save1.Json, Array.Empty<FamilySpec>(), 100);
        var branchB = reloaded.PrepareSave(250, T0.AddMinutes(10), "branchB")!;
        reloaded.Commit(branchB);

        var obsA = CheckpointMetrics.ToObservation(
            CheckpointJson.Parse(branchA.Json).Payload!, branchA.Json,
            TestIdentity(), "1.7.4985", null, families);
        var obsB = CheckpointMetrics.ToObservation(
            CheckpointJson.Parse(branchB.Json).Payload!, branchB.Json,
            TestIdentity(), "1.7.4985", null, families);

        Assert.Equal(1L, obsA.Scalar(power.StableKey, 1)!.IntegerValue);
        Assert.Equal(30.0d, obsA.Scalar(gas.StableKey, 1)!.RealValue!.Value, 6);
        Assert.Equal(7L, obsA.Scalar(recycling.StableKey, 1)!.IntegerValue);

        Assert.Equal(1L, obsB.Scalar(power.StableKey, 1)!.IntegerValue);
        Assert.Equal(24.5d, obsB.Scalar(gas.StableKey, 1)!.RealValue!.Value, 6);
        Assert.Equal(5L, obsB.Scalar(recycling.StableKey, 1)!.IntegerValue);
    }
}
