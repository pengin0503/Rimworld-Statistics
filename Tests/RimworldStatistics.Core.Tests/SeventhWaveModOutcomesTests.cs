using RimworldStatistics.SeventhWaveModOutcomes;

namespace RimworldStatistics.Core.Tests;

public sealed class SeventhWaveModOutcomesTests
{
    [Fact]
    public void PrisonLaborRules_VerifiesResocializationAndInterrogationTransitions()
    {
        Assert.True(PrisonLaborRules.IsEligibleResocializationBefore(
            isPrisonerOfColony: true,
            isDead: false,
            wasMember: false,
            resocializationReady: true));
        Assert.False(PrisonLaborRules.IsEligibleResocializationBefore(
            isPrisonerOfColony: true,
            isDead: false,
            wasMember: false,
            resocializationReady: false));
        Assert.False(PrisonLaborRules.IsEligibleResocializationBefore(
            isPrisonerOfColony: false,
            isDead: false,
            wasMember: false,
            resocializationReady: true));

        Assert.True(PrisonLaborRules.ShouldRecordResocialization(
            wasEligibleBefore: true,
            isDeadAfter: false,
            isPrisonerOfColonyAfter: false,
            isMemberAfter: true));
        Assert.False(PrisonLaborRules.ShouldRecordResocialization(
            wasEligibleBefore: true,
            isDeadAfter: false,
            isPrisonerOfColonyAfter: true,
            isMemberAfter: false));

        Assert.True(PrisonLaborRules.IsEligibleInterrogationBefore(
            isWardenMember: true,
            isPrisonerOfColony: true,
            isPrisonerDead: false,
            hasIntelBefore: true));
        Assert.False(PrisonLaborRules.IsEligibleInterrogationBefore(
            isWardenMember: true,
            isPrisonerOfColony: true,
            isPrisonerDead: false,
            hasIntelBefore: false));
        Assert.True(PrisonLaborRules.ShouldRecordInterrogationIntel(
            wasEligibleBefore: true,
            hasIntelAfter: false));
        Assert.False(PrisonLaborRules.ShouldRecordInterrogationIntel(
            wasEligibleBefore: true,
            hasIntelAfter: true));
    }

    [Fact]
    public void MedPodRules_ExcludesAbortsPowerLossAndNonHealingDischarges()
    {
        Assert.True(MedPodRules.IsEligibleDischargeBefore(
            isSpawnedPlayerHomePod: true,
            isPlayerPod: true,
            isPatientAlive: true,
            finishTreatmentNormally: true,
            statusBefore: MedPodRules.HealingFinishedStatus));

        // Power loss / manual abort passes finishTreatmentNormally: false.
        Assert.False(MedPodRules.IsEligibleDischargeBefore(
            isSpawnedPlayerHomePod: true,
            isPlayerPod: true,
            isPatientAlive: true,
            finishTreatmentNormally: false,
            statusBefore: MedPodRules.HealingFinishedStatus));

        // Non-HealingFinished states (e.g. DiagnosisFinished == 2 or HealingStarted == 3) are excluded.
        Assert.False(MedPodRules.IsEligibleDischargeBefore(
            isSpawnedPlayerHomePod: true,
            isPlayerPod: true,
            isPatientAlive: true,
            finishTreatmentNormally: true,
            statusBefore: 3));

        Assert.Equal("sumghai.medpod/MedPod", MedPodRules.ResolveCompletedPodKey(
            wasEligibleBefore: true,
            isPatientAliveAfter: true,
            podPackageId: "sumghai.Medpod",
            podDefName: "MedPod"));
        Assert.Equal("sumghai.medpod/VetPod", MedPodRules.ResolveCompletedPodKey(
            wasEligibleBefore: true,
            isPatientAliveAfter: true,
            podPackageId: "sumghai.Medpod",
            podDefName: "VetPod"));
        Assert.Null(MedPodRules.ResolveCompletedPodKey(
            wasEligibleBefore: true,
            isPatientAliveAfter: false,
            podPackageId: "sumghai.Medpod",
            podDefName: "MedPod"));
    }

    [Fact]
    public void AlphaImplantsRules_ExcludesPsycastsFailedCastsAndZeroScarCures()
    {
        Assert.True(AlphaImplantsRules.IsEligibleImplantAbilityBefore(
            isPsycast: false,
            isPlayerCaster: true,
            abilityPackageId: "sarg.alphaimplants"));
        Assert.False(AlphaImplantsRules.IsEligibleImplantAbilityBefore(
            isPsycast: true,
            isPlayerCaster: true,
            abilityPackageId: "sarg.alphaimplants"));
        Assert.False(AlphaImplantsRules.IsEligibleImplantAbilityBefore(
            isPsycast: false,
            isPlayerCaster: true,
            abilityPackageId: "Ludeon.RimWorld.Biotech"));

        Assert.Equal("sarg.alphaimplants/AI_AnimalJump", AlphaImplantsRules.ResolveActivatedAbilityKey(
            wasEligibleBefore: true,
            activationResult: true,
            lastCastTickBefore: 100,
            lastCastTickAfter: 250,
            abilityPackageId: "sarg.alphaimplants",
            abilityDefName: "AI_AnimalJump"));
        Assert.Null(AlphaImplantsRules.ResolveActivatedAbilityKey(
            wasEligibleBefore: true,
            activationResult: false,
            lastCastTickBefore: 100,
            lastCastTickAfter: 250,
            abilityPackageId: "sarg.alphaimplants",
            abilityDefName: "AI_AnimalJump"));
        Assert.Null(AlphaImplantsRules.ResolveActivatedAbilityKey(
            wasEligibleBefore: true,
            activationResult: true,
            lastCastTickBefore: 100,
            lastCastTickAfter: 100,
            abilityPackageId: "sarg.alphaimplants",
            abilityDefName: "AI_AnimalJump"));

        Assert.True(AlphaImplantsRules.IsEligibleScarCurePatient(isAlive: true, isPlayerOwned: true, isPrisonerOfColony: false));
        Assert.True(AlphaImplantsRules.IsEligibleScarCurePatient(isAlive: true, isPlayerOwned: false, isPrisonerOfColony: true));
        Assert.False(AlphaImplantsRules.IsEligibleScarCurePatient(isAlive: true, isPlayerOwned: false, isPrisonerOfColony: false));
        Assert.Equal(2, AlphaImplantsRules.ComputeCuredScars(permanentInjuriesBefore: 3, permanentInjuriesAfter: 1));
        Assert.Equal(0, AlphaImplantsRules.ComputeCuredScars(permanentInjuriesBefore: 2, permanentInjuriesAfter: 2));
    }

    [Fact]
    public void BiomesIslandsRules_SnapshotsUniqueHomeSurfaceBiomes()
    {
        var entry = BiomesIslandsRules.HomeBiomes(
        [
            (1, "BiomesTeam.BiomesIslands", "BMT_Atoll"),
            (1, "BiomesTeam.BiomesIslands", "BMT_Atoll"),
            (2, "BiomesTeam.BiomesIslands", "BMT_TropicalIsland"),
            (3, "Ludeon.RimWorld", "TemperateForest"),
        ]);

        Assert.NotNull(entry);
        Assert.Equal(MetricCoverage.SnapshotOnly, entry.Values[MetricEntry.Scalar].Coverage);
        Assert.Equal(2L, entry.Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(1L, entry.Values["biomesteam.biomesislands/BMT_Atoll"].IntegerValue);
        Assert.Equal(1L, entry.Values["biomesteam.biomesislands/BMT_TropicalIsland"].IntegerValue);

        var horrorsEntry = BiomesIslandsRules.HomeBiomesForPackage(
            HorrorsRules.OwnerPackageId,
            [(10, "Mlie.Horrors", "HorrorWastes")]);
        Assert.NotNull(horrorsEntry);
        Assert.Equal(1L, horrorsEntry.Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(1L, horrorsEntry.Values["mlie.horrors/HorrorWastes"].IntegerValue);
    }

    [Fact]
    public void RimQuestAndHorrorsRules_VerifyPostSuccessOutcomes()
    {
        Assert.True(RimQuestRules.IsEligiblePurchaseBefore(
            isPlayerInteractor: true,
            hasSelectedQuestDef: true,
            isQuestPawnTrackedBefore: true));
        Assert.False(RimQuestRules.IsEligiblePurchaseBefore(
            isPlayerInteractor: true,
            hasSelectedQuestDef: true,
            isQuestPawnTrackedBefore: false));

        var purchased = RimQuestRules.ShouldRecordPurchase(wasEligibleBefore: true, isQuestPawnTrackedAfter: false);
        Assert.True(purchased);
        Assert.Equal("ludeon.rimworld/OpportunitySite_ItemStash", RimQuestRules.ResolvePurchasedQuestKey(
            purchased,
            "Ludeon.RimWorld",
            "OpportunitySite_ItemStash"));
        Assert.Equal(225L, RimQuestRules.AcceptedSilverSpent(purchased, actualSilverCost: 225));
        Assert.Equal(0L, RimQuestRules.AcceptedSilverSpent(purchased: false, actualSilverCost: 225));

        Assert.Equal("mlie.horrors/HorrorHive", HorrorsRules.ResolveDestroyedHiveKey(
            isKillFinalize: true,
            wasSpawnedBefore: true,
            isSpawnedAfter: false,
            isPlayerFactionHive: false,
            hasColonyPresenceOnMap: true,
            hivePackageId: "Mlie.Horrors",
            hiveDefName: "HorrorHive"));
        // Vanish / map unload (isKillFinalize: false) or no colony presence records nothing.
        Assert.Null(HorrorsRules.ResolveDestroyedHiveKey(
            isKillFinalize: false,
            wasSpawnedBefore: true,
            isSpawnedAfter: false,
            isPlayerFactionHive: false,
            hasColonyPresenceOnMap: true,
            hivePackageId: "Mlie.Horrors",
            hiveDefName: "HorrorHive"));
        Assert.Null(HorrorsRules.ResolveDestroyedHiveKey(
            isKillFinalize: true,
            wasSpawnedBefore: true,
            isSpawnedAfter: false,
            isPlayerFactionHive: false,
            hasColonyPresenceOnMap: false,
            hivePackageId: "Mlie.Horrors",
            hiveDefName: "HorrorDen"));
    }
}
