using RimworldStatistics.AlphaAnimals;
using RimworldStatistics.VreFungoid;
using RimworldStatistics.VreInsector;
using RimworldStatistics.VreLycanthrope;
using RimworldStatistics.VreSanguophage;

namespace RimworldStatistics.Core.Tests;

public sealed class RaceXenotypeModStatisticsTests
{
    private static readonly DateTime T0 = new(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);
    private const string Campaign = "feedfacefeedfacefeedfacefeedface";

    private static FamilyState EmptyState(int since = 0, int through = 100) => new()
    {
        Revision = 1,
        SinceTick = since,
        ThroughTick = through,
        Continuous = true,
    };

    private static ColonyIdentity TestIdentity() => new()
    {
        SeedString = "race-xenotype-seed",
        PersistentRandomValue = 42,
        StartingTile = "2,3",
        GameStartAbsTick = 0,
        FirstWealthBits = 13,
        StartingPawnIds = { "Thing_Human1" },
        ModCampaignId = Campaign,
    };

    [Theory]
    [InlineData(false, true, true, true, true, true)]
    [InlineData(true, false, true, true, true, true)]
    [InlineData(false, false, true, true, true, false)]
    [InlineData(true, true, true, true, true, false)]
    [InlineData(false, true, false, true, true, false)]
    [InlineData(false, true, true, false, true, false)]
    [InlineData(false, true, true, true, false, false)]
    public void VreLycanthropeMorphOnlyCountsCommittedStateAndXenotypeTransition(
        bool wasMorphed,
        bool nowMorphed,
        bool expectedTargetMatched,
        bool xenotypeChanged,
        bool livePlayerPawn,
        bool expected)
    {
        Assert.Equal(expected, VreLycanthropeRules.MorphCommitted(
            wasMorphed, nowMorphed, expectedTargetMatched, xenotypeChanged, livePlayerPawn));
    }

    [Fact]
    public void VreLycanthropeMorphBreakdownKeepsBeastKeyDespiteLeftoverNameAndMarksCustomRevertPartial()
    {
        var morphKey = VreLycanthropeRules.MorphBreakdownKey(
            nowMorphed: true,
            packageId: "VanillaRacesExpanded.Lycanthrope",
            defName: "VRE_Wolfman",
            xenotypeName: "Custom Pack Leader");
        Assert.Equal("vanillaracesexpanded.lycanthrope/VRE_Wolfman", morphKey);

        var namedRevertKey = VreLycanthropeRules.MorphBreakdownKey(
            nowMorphed: false,
            packageId: "Ludeon.RimWorld.Biotech",
            defName: "Baseliner",
            xenotypeName: null);
        Assert.Equal("ludeon.rimworld.biotech/Baseliner", namedRevertKey);

        var customRevertKey = VreLycanthropeRules.MorphBreakdownKey(
            nowMorphed: false,
            packageId: "Ludeon.RimWorld.Biotech",
            defName: "Baseliner",
            xenotypeName: "Custom Pack Leader");
        Assert.Null(customRevertKey);

        const string metricId = "mod.vanillaracesexpanded.lycanthrope.morphs.completed";
        var state = EmptyState();
        OptionalTransitionRecording.RecordCount(state, "completed", 1, morphKey, withBreakdown: true);
        OptionalTransitionRecording.RecordCount(state, "completed", 1, customRevertKey, withBreakdown: true);

        var metrics = new Dictionary<string, MetricEntry>(StringComparer.Ordinal);
        OptionalTransitionRecording.PutCount(new FamilyOutputContext(state, 100, metrics),
            "completed", metricId, withBreakdown: true);

        var entry = metrics[metricId];
        Assert.Equal(2L, entry.Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(MetricCoverage.Partial, entry.Values[MetricEntry.Scalar].Coverage);
        Assert.Equal(1L, entry.Values["vanillaracesexpanded.lycanthrope/VRE_Wolfman"].IntegerValue);
        Assert.False(entry.Values.ContainsKey("ludeon.rimworld.biotech/Baseliner"));
    }

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    public void VreLycanthropeReimplantRequiresLiveRecipientPlayerSideAndAppliedChange(
        bool liveRecipient, bool playerInvolved, bool genesOrComaApplied, bool expected)
    {
        Assert.Equal(expected, VreLycanthropeRules.ReimplantCommitted(
            liveRecipient, playerInvolved, genesOrComaApplied));
    }

    [Theory]
    [InlineData(true, true, true, true, true)]
    [InlineData(false, true, true, true, false)]
    [InlineData(true, false, true, true, false)]
    [InlineData(true, true, false, true, false)]
    [InlineData(true, true, true, false, false)]
    public void VreFungoidConversionRequiresFullSeverityRemovalAndGeneInfectedHediff(
        bool severityReached,
        bool liveColonyAffectedPawn,
        bool infectionHediffRemoved,
        bool infectedHediffPresent,
        bool expected)
    {
        Assert.Equal(expected, VreFungoidRules.ConversionCommitted(
            severityReached, liveColonyAffectedPawn, infectionHediffRemoved, infectedHediffPresent));
    }

    [Theory]
    [InlineData(true, true, true, true, true, true, 0, 2, true, nameof(MetapodOutcome.EvolutionOrChange))]
    [InlineData(true, true, true, true, true, true, 2, 2, true, nameof(MetapodOutcome.EvolutionOrChange))]
    [InlineData(true, true, true, true, true, false, 2, 2, true, nameof(MetapodOutcome.EvolutionOrChange))]
    [InlineData(true, true, true, true, true, false, 2, 2, false, nameof(MetapodOutcome.None))]
    [InlineData(true, true, true, false, true, true, 0, 2, true, nameof(MetapodOutcome.None))]
    [InlineData(true, true, true, true, false, true, 0, 2, true, nameof(MetapodOutcome.None))]
    [InlineData(true, true, false, true, false, false, 3, 0, true, nameof(MetapodOutcome.Reversion))]
    [InlineData(true, true, false, true, false, true, 2, 0, true, nameof(MetapodOutcome.Reversion))]
    [InlineData(true, true, false, true, false, false, 0, 0, false, nameof(MetapodOutcome.None))]
    [InlineData(true, true, false, true, false, false, 2, 1, true, nameof(MetapodOutcome.None))]
    [InlineData(false, true, true, true, true, true, 0, 2, true, nameof(MetapodOutcome.None))]
    [InlineData(true, false, true, true, true, true, 0, 2, true, nameof(MetapodOutcome.None))]
    public void VreInsectorClassifiesEvolutionsChangesAndReversionsFromMetapodCompletion(
        bool livePlayerPawn,
        bool metapodRemoved,
        bool hasTargetGeneline,
        bool hasEvolutionGeneAfter,
        bool targetGenelineMatchedAfter,
        bool genelineChanged,
        int beforeGenelineGeneCount,
        int afterGenelineGeneCount,
        bool genelineGenesChanged,
        string expectedOutcome)
    {
        var actual = VreInsectorRules.ClassifyCompletion(
            livePlayerPawn,
            metapodRemoved,
            hasTargetGeneline,
            hasEvolutionGeneAfter,
            targetGenelineMatchedAfter,
            genelineChanged,
            beforeGenelineGeneCount,
            afterGenelineGeneCount,
            genelineGenesChanged);
        Assert.Equal(expectedOutcome, actual.ToString());
    }

    [Theory]
    [InlineData(true, true, true, true, true, true)]
    [InlineData(false, true, true, true, true, false)]
    [InlineData(true, false, true, true, true, false)]
    [InlineData(true, true, false, true, true, false)]
    [InlineData(true, true, true, false, true, false)]
    [InlineData(true, true, true, true, false, false)]
    public void AlphaAnimalsMetamorphosisRequiresDestroyedPlayerSourceAndValidCocoon(
        bool sourceSpawnedAndPlayer,
        bool spawnedValid,
        bool sameMap,
        bool parentDestroyed,
        bool expectedCocoonDef,
        bool expected)
    {
        Assert.Equal(expected, AlphaAnimalsRules.MetamorphosisCommitted(
            sourceSpawnedAndPlayer, spawnedValid, sameMap, parentDestroyed, expectedCocoonDef));
    }

    [Theory]
    [InlineData(true, true, true, true, true, true)]
    [InlineData(false, true, true, true, true, false)]
    [InlineData(true, false, true, true, true, false)]
    [InlineData(true, true, false, true, true, false)]
    [InlineData(true, true, true, false, true, false)]
    [InlineData(true, true, true, true, false, false)]
    public void AlphaAnimalsCocoonHatchRequiresDestroyedPlayerCocoonAndLivePlayerInsectoid(
        bool playerCocoonBefore,
        bool cocoonDestroyed,
        bool livePawnSpawned,
        bool playerPawnSpawned,
        bool sameMap,
        bool expected)
    {
        Assert.Equal(expected, AlphaAnimalsRules.CocoonHatchCommitted(
            playerCocoonBefore, cocoonDestroyed, livePawnSpawned, playerPawnSpawned, sameMap));
    }

    [Theory]
    [InlineData(0.25f, true, true, 0.25d)]
    [InlineData(12.5f, true, true, 12.5d)]
    [InlineData(0f, true, true, 0d)]
    [InlineData(-1f, true, true, 0d)]
    [InlineData(float.NaN, true, true, 0d)]
    [InlineData(float.PositiveInfinity, true, true, 0d)]
    [InlineData(1f, false, true, 0d)]
    [InlineData(1f, true, false, 0d)]
    public void VreSanguophageOnlyAcceptsPositiveFinitePlayerHemogenPipeUnits(
        float stored, bool matchingPipe, bool playerOwned, double expected)
    {
        Assert.Equal(expected, VreSanguophageRules.AcceptedUnits(stored, matchingPipe, playerOwned), 6);
    }

    [Fact]
    public void VreSanguophageAccumulatesFractionalHemogenWithoutRounding()
    {
        var state = EmptyState();
        Assert.True(OptionalTransitionRecording.RecordReal(state, "stored", 0.35));
        Assert.True(OptionalTransitionRecording.RecordReal(state, "stored", 0.65));
        Assert.False(OptionalTransitionRecording.RecordReal(state, "stored", 0d));
        Assert.False(OptionalTransitionRecording.RecordReal(state, "stored", double.NaN));
        Assert.Equal(1d, state.Peek("stored")!.Sum!.Value, 8);
    }

    [Fact]
    public void DescriptorsAreUniqueValidAndPreserveSaveBranchingAndBackwardCompatibility()
    {
        var descriptors = new ModMetricDescriptor[]
        {
            new()
            {
                ProviderId = "rws.morphs",
                OwnerPackageId = "vanillaracesexpanded.lycanthrope",
                FamilyId = "morphs",
                MetricName = "completed",
                Source = MetricObservationSource.Checkpoint,
                Category = MetricCategory.Biotech,
                HasBreakdown = true,
                RequiredDlc = "Ludeon.RimWorld.Biotech",
                RequiredPackages = [new ModPackageRequirement("vanillaracesexpanded.lycanthrope")],
                LabelKey = "RWS_VreLycanthrope_Morphs",
                DescriptionKey = "RWS_VreLycanthrope_MorphsDesc",
            },
            new()
            {
                ProviderId = "rws.germline_reimplants",
                OwnerPackageId = "vanillaracesexpanded.lycanthrope",
                FamilyId = "germline_reimplants",
                MetricName = "completed",
                Source = MetricObservationSource.Checkpoint,
                Category = MetricCategory.Biotech,
                RequiredDlc = "Ludeon.RimWorld.Biotech",
                RequiredPackages = [new ModPackageRequirement("vanillaracesexpanded.lycanthrope")],
                LabelKey = "RWS_VreLycanthrope_GermlineReimplants",
                DescriptionKey = "RWS_VreLycanthrope_GermlineReimplantsDesc",
            },
            new()
            {
                ProviderId = "rws.infection_conversions",
                OwnerPackageId = "vanillaracesexpanded.fungoid",
                FamilyId = "infection_conversions",
                MetricName = "completed",
                Source = MetricObservationSource.Checkpoint,
                Category = MetricCategory.Biotech,
                RequiredDlc = "Ludeon.RimWorld.Biotech",
                RequiredPackages = [new ModPackageRequirement("vanillaracesexpanded.fungoid")],
                LabelKey = "RWS_VreFungoid_InfectionConversions",
                DescriptionKey = "RWS_VreFungoid_InfectionConversionsDesc",
            },
            new()
            {
                ProviderId = "rws.geneline_evolutions",
                OwnerPackageId = "vanillaracesexpanded.insector",
                FamilyId = "geneline_evolutions",
                MetricName = "completed",
                Source = MetricObservationSource.Checkpoint,
                Category = MetricCategory.Biotech,
                RequiredDlc = "Ludeon.RimWorld.Biotech",
                RequiredPackages = [new ModPackageRequirement("vanillaracesexpanded.insector")],
                LabelKey = "RWS_VreInsector_GenelineEvolutions",
                DescriptionKey = "RWS_VreInsector_GenelineEvolutionsDesc",
            },
            new()
            {
                ProviderId = "rws.geneline_reversions",
                OwnerPackageId = "vanillaracesexpanded.insector",
                FamilyId = "geneline_reversions",
                MetricName = "completed",
                Source = MetricObservationSource.Checkpoint,
                Category = MetricCategory.Biotech,
                RequiredDlc = "Ludeon.RimWorld.Biotech",
                RequiredPackages = [new ModPackageRequirement("vanillaracesexpanded.insector")],
                LabelKey = "RWS_VreInsector_GenelineReversions",
                DescriptionKey = "RWS_VreInsector_GenelineReversionsDesc",
            },
            new()
            {
                ProviderId = "rws.cocoon_metamorphoses",
                OwnerPackageId = "sarg.alphaanimals",
                FamilyId = "cocoon_metamorphoses",
                MetricName = "completed",
                Source = MetricObservationSource.Checkpoint,
                Category = MetricCategory.Animals,
                HasBreakdown = true,
                RequiredPackages = [new ModPackageRequirement("sarg.alphaanimals")],
                LabelKey = "RWS_AlphaAnimals_CocoonMetamorphoses",
                DescriptionKey = "RWS_AlphaAnimals_CocoonMetamorphosesDesc",
            },
            new()
            {
                ProviderId = "rws.cocoon_hatches",
                OwnerPackageId = "sarg.alphaanimals",
                FamilyId = "cocoon_hatches",
                MetricName = "completed",
                Source = MetricObservationSource.Checkpoint,
                Category = MetricCategory.Animals,
                HasBreakdown = true,
                RequiredPackages = [new ModPackageRequirement("sarg.alphaanimals")],
                LabelKey = "RWS_AlphaAnimals_CocoonHatches",
                DescriptionKey = "RWS_AlphaAnimals_CocoonHatchesDesc",
            },
            new()
            {
                ProviderId = "rws.draincasket_hemogen",
                OwnerPackageId = "vanillaracesexpanded.sanguophage",
                FamilyId = "draincasket_hemogen",
                MetricName = "stored_units",
                Source = MetricObservationSource.Checkpoint,
                Category = MetricCategory.Biotech,
                RequiredDlc = "Ludeon.RimWorld.Biotech",
                RequiredPackages =
                [
                    new ModPackageRequirement("vanillaracesexpanded.sanguophage"),
                    new ModPackageRequirement("OskarPotocki.VanillaFactionsExpanded.Core"),
                ],
                LabelKey = "RWS_VreSanguophage_DraincasketHemogen",
                DescriptionKey = "RWS_VreSanguophage_DraincasketHemogenDesc",
            },
        };

        var registry = new ModMetricProviderRegistry();
        foreach (var d in descriptors)
            Assert.True(registry.Register(d).Accepted, d.StableKey);

        var missingMod = ModProviderDiscovery.Resolve(descriptors[0], Array.Empty<ModPackageInfo>());
        Assert.Equal(ModProviderAvailability.MissingPackage, missingMod.State);
        Assert.False(missingMod.Available);

        var sangWithoutFramework = ModProviderDiscovery.Resolve(descriptors[7],
            new[] { new ModPackageInfo("vanillaracesexpanded.sanguophage", null) });
        Assert.Equal(ModProviderAvailability.MissingPackage, sangWithoutFramework.State);
        var sangWithFramework = ModProviderDiscovery.Resolve(descriptors[7],
            new[]
            {
                new ModPackageInfo("vanillaracesexpanded.sanguophage", null),
                new ModPackageInfo("OskarPotocki.VanillaFactionsExpanded.Core", null),
            });
        Assert.Equal(ModProviderAvailability.Available, sangWithFramework.State);

        var families = new LiveFamily[]
        {
            new()
            {
                Name = descriptors[0].AcquisitionId,
                Revision = 1,
                RequiredDlc = descriptors[0].RequiredDlc,
                Build = o => OptionalTransitionRecording.PutCount(o, "completed", descriptors[0].StableKey, true),
            },
            new()
            {
                Name = descriptors[5].AcquisitionId,
                Revision = 1,
                Build = o => OptionalTransitionRecording.PutCount(o, "completed", descriptors[5].StableKey, true),
            },
            new()
            {
                Name = descriptors[7].AcquisitionId,
                Revision = 1,
                RequiredDlc = descriptors[7].RequiredDlc,
                Build = o => o.Put(descriptors[7].StableKey, o.Value(real: o.State.Peek("stored")?.Sum ?? 0d)),
            },
        };

        // Branch A records initial events and commits checkpoint save1.
        var specs = families.Select(f => f.Spec).ToArray();
        var accA = LiveMetricAccumulator.StartNew(specs, 0);
        OptionalTransitionRecording.RecordCount(accA.Family(descriptors[0].AcquisitionId)!, "completed",
            1, OptionalTransitionRecording.QualifiedDefKey("vanillaracesexpanded.lycanthrope", "VRE_Wolfman"), true);
        OptionalTransitionRecording.RecordReal(accA.Family(descriptors[7].AcquisitionId)!, "stored", 2.5);
        var save1 = accA.PrepareSave(100, T0, "save1")!;
        accA.Commit(save1);

        // Save1 advances further, while Save2 branches back from Save1 with the mod later unloaded (kept verbatim).
        OptionalTransitionRecording.RecordCount(accA.Family(descriptors[5].AcquisitionId)!, "completed",
            1, OptionalTransitionRecording.QualifiedDefKey("sarg.alphaanimals", "AA_BlackCocoon"), true);
        var branchAdvanced = accA.PrepareSave(200, T0.AddMinutes(5), "saveA")!;
        accA.Commit(branchAdvanced);

        var reloadedWithoutMods = LiveMetricAccumulator.Resume(save1.Json, Array.Empty<FamilySpec>(), 100);
        OptionalTransitionRecording.RecordCount(accA.Family(descriptors[0].AcquisitionId)!, "completed",
            1, OptionalTransitionRecording.QualifiedDefKey("vanillaracesexpanded.lycanthrope", "VRE_Wolfman"), true);
        var branchUnloaded = reloadedWithoutMods.PrepareSave(250, T0.AddMinutes(10), "saveB")!;
        reloadedWithoutMods.Commit(branchUnloaded);

        var obsAdvanced = CheckpointMetrics.ToObservation(
            CheckpointJson.Parse(branchAdvanced.Json).Payload!, branchAdvanced.Json,
            TestIdentity(), "1.7.4985", null, families);
        var obsUnloaded = CheckpointMetrics.ToObservation(
            CheckpointJson.Parse(branchUnloaded.Json).Payload!, branchUnloaded.Json,
            TestIdentity(), "1.7.4985", null, families);

        Assert.Equal(1L, obsAdvanced.Scalar(descriptors[0].StableKey, 1)!.IntegerValue);
        Assert.Equal(1L, obsAdvanced.Scalar(descriptors[5].StableKey, 1)!.IntegerValue);
        Assert.Equal(2.5d, obsAdvanced.Scalar(descriptors[7].StableKey, 1)!.RealValue!.Value, 6);

        // Unloaded save preserves Save1 baseline without either branch A's post-save1 events or double counting.
        Assert.Equal(1L, obsUnloaded.Scalar(descriptors[0].StableKey, 1)!.IntegerValue);
        Assert.Equal(0L, obsUnloaded.Scalar(descriptors[5].StableKey, 1)!.IntegerValue);
        Assert.Equal(2.5d, obsUnloaded.Scalar(descriptors[7].StableKey, 1)!.RealValue!.Value, 6);

        // Aborted save (PrepareSave without Commit) must not drop or double-count subsequent commits.
        OptionalTransitionRecording.RecordReal(accA.Family(descriptors[7].AcquisitionId)!, "stored", 1.25);
        var abortedDraft = accA.PrepareSave(210, T0.AddMinutes(6), "save-aborted")!;
        Assert.True(abortedDraft.IsNew);
        OptionalTransitionRecording.RecordReal(accA.Family(descriptors[7].AcquisitionId)!, "stored", 0.25);
        var committedAfterAbort = accA.PrepareSave(220, T0.AddMinutes(7), "save-recovered")!;
        accA.Commit(committedAfterAbort);
        var obsRecovered = CheckpointMetrics.ToObservation(
            CheckpointJson.Parse(committedAfterAbort.Json).Payload!, committedAfterAbort.Json,
            TestIdentity(), "1.7.4985", null, families);
        Assert.Equal(4.0d, obsRecovered.Scalar(descriptors[7].StableKey, 1)!.RealValue!.Value, 6);
    }
}
