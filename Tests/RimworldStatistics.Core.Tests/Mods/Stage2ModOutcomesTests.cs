using RimworldStatistics.Stage2ModOutcomes;
using Xunit;

namespace RimworldStatistics.Core.Tests;

public sealed class Stage2ModOutcomesTests
{
    [Fact]
    public void HospitalStage2Rules_RequiresCuredMobilePatientAndDismissedTransition()
    {
        Assert.True(HospitalStage2Rules.IsEligibleCuredPatientBefore(
            isPlayerHomeMap: true,
            isAlive: true,
            isDowned: false,
            wasDismissed: false,
            hasPendingSurgeryBill: false,
            hasHediffsNeedingTend: false,
            shouldSeekMedicalRest: false,
            pendingBillStackCount: 0));

        // Danger evacuations (Trigger_SentAway) with remaining tend/rest/surgery needs must be excluded.
        Assert.False(HospitalStage2Rules.IsEligibleCuredPatientBefore(
            isPlayerHomeMap: true,
            isAlive: true,
            isDowned: false,
            wasDismissed: false,
            hasPendingSurgeryBill: false,
            hasHediffsNeedingTend: true,
            shouldSeekMedicalRest: false,
            pendingBillStackCount: 0));
        Assert.False(HospitalStage2Rules.IsEligibleCuredPatientBefore(
            isPlayerHomeMap: true,
            isAlive: true,
            isDowned: false,
            wasDismissed: false,
            hasPendingSurgeryBill: true,
            hasHediffsNeedingTend: false,
            shouldSeekMedicalRest: false,
            pendingBillStackCount: 0));
        Assert.False(HospitalStage2Rules.IsEligibleCuredPatientBefore(
            isPlayerHomeMap: true,
            isAlive: true,
            isDowned: false,
            wasDismissed: true,
            hasPendingSurgeryBill: false,
            hasHediffsNeedingTend: false,
            shouldSeekMedicalRest: false,
            pendingBillStackCount: 0));

        Assert.True(HospitalStage2Rules.ShouldRecordCuredDischarge(eligibleBefore: true, isAliveAfter: true, dismissedAfter: true));
        Assert.False(HospitalStage2Rules.ShouldRecordCuredDischarge(eligibleBefore: true, isAliveAfter: true, dismissedAfter: false));

        Assert.Equal(320L, HospitalStage2Rules.AcceptedTreatmentSilver(
            placedCount: 320, isSilverDef: true, isPlayerHomeMap: true, activeDischargeScope: true));
        Assert.Equal(0L, HospitalStage2Rules.AcceptedTreatmentSilver(
            placedCount: 320, isSilverDef: true, isPlayerHomeMap: true, activeDischargeScope: false));
        Assert.Equal(0L, HospitalStage2Rules.AcceptedTreatmentSilver(
            placedCount: 320, isSilverDef: true, isPlayerHomeMap: false, activeDischargeScope: true));
    }

    [Fact]
    public void RimatomicsStage2Rules_VerifiesInterceptsBombardmentsScramsAndMeltdowns()
    {
        Assert.Equal("dubwise.rimatomics/MortarShell", RimatomicsStage2Rules.ResolveHelInterceptKey(
            casterPlayerOwned: true,
            castSucceeded: true,
            targetAliveBefore: true,
            targetDestroyedAfter: true,
            killCounterBefore: 4,
            killCounterAfter: 5,
            projectilePackageId: "Dubwise.Rimatomics",
            projectileDefName: "MortarShell"));
        Assert.Null(RimatomicsStage2Rules.ResolveHelInterceptKey(
            casterPlayerOwned: true,
            castSucceeded: true,
            targetAliveBefore: true,
            targetDestroyedAfter: true,
            killCounterBefore: 4,
            killCounterAfter: 4,
            projectilePackageId: "Dubwise.Rimatomics",
            projectileDefName: "MortarShell"));

        Assert.Equal("dubwise.rimatomics/SabotRound", RimatomicsStage2Rules.ResolveRailgunBombardmentKey(
            playerOrigin: true,
            wasSpawnedBefore: true,
            wasArrivedBefore: false,
            isArrivedAfter: true,
            isSpawnedAfter: false,
            shellPackageId: "Dubwise.Rimatomics",
            shellDefName: "SabotRound"));
        Assert.Null(RimatomicsStage2Rules.ResolveRailgunBombardmentKey(
            playerOrigin: false,
            wasSpawnedBefore: true,
            wasArrivedBefore: false,
            isArrivedAfter: true,
            isSpawnedAfter: false,
            shellPackageId: "Dubwise.Rimatomics",
            shellDefName: "SabotRound"));

        Assert.Equal("dubwise.rimatomics/ReactorCoreA", RimatomicsStage2Rules.ResolveReactorScramKey(
            playerHomeReactor: true,
            wasShutdownBefore: false,
            fuelCountBefore: 8,
            isShutdownAfter: true,
            reactorPackageId: "Dubwise.Rimatomics",
            reactorDefName: "ReactorCoreA"));
        Assert.Null(RimatomicsStage2Rules.ResolveReactorScramKey(
            playerHomeReactor: true,
            wasShutdownBefore: false,
            fuelCountBefore: 0,
            isShutdownAfter: true,
            reactorPackageId: "Dubwise.Rimatomics",
            reactorDefName: "ReactorCoreA"));

        Assert.Equal("dubwise.rimatomics/ReactorCoreA", RimatomicsStage2Rules.ResolveReactorMeltdownKey(
            activeMeltdownScope: true,
            reactorPackageId: "Dubwise.Rimatomics",
            reactorDefName: "ReactorCoreA"));
        Assert.Null(RimatomicsStage2Rules.ResolveReactorMeltdownKey(
            activeMeltdownScope: false,
            reactorPackageId: "Dubwise.Rimatomics",
            reactorDefName: "ReactorCoreA"));
    }

    [Fact]
    public void AlteredCarbonStage2Rules_VerifiesSleeveGrowthAndNeedlecastTransitions()
    {
        Assert.True(AlteredCarbonStage2Rules.ShouldRecordSleeveGrowth(
            playerHomeVat: true,
            wasGrowingStateBefore: true,
            isInactiveStateAfter: true,
            innerSleeveAliveAfter: true));
        Assert.False(AlteredCarbonStage2Rules.ShouldRecordSleeveGrowth(
            playerHomeVat: true,
            wasGrowingStateBefore: false,
            isInactiveStateAfter: true,
            innerSleeveAliveAfter: true));

        Assert.True(AlteredCarbonStage2Rules.ShouldRecordNeedlecast(
            wasNeedlecastedBefore: false,
            targetAliveBefore: true,
            isNeedlecastedAfter: true,
            targetAliveAfter: true,
            targetIsPlayerAfter: true));
        Assert.False(AlteredCarbonStage2Rules.ShouldRecordNeedlecast(
            wasNeedlecastedBefore: true,
            targetAliveBefore: true,
            isNeedlecastedAfter: true,
            targetAliveAfter: true,
            targetIsPlayerAfter: true));
    }

    [Fact]
    public void EmpireTaxStage2Rules_ExcludesNonTaxAndRescheduledDeliveries()
    {
        Assert.True(EmpireTaxStage2Rules.IsEligibleTaxDeliveryEvent("taxColony", isDelayed: false, ""));
        Assert.True(EmpireTaxStage2Rules.IsEligibleTaxDeliveryEvent("deliveryArrival", isDelayed: true, "Tax delivery from Oakvale"));
        Assert.False(EmpireTaxStage2Rules.IsEligibleTaxDeliveryEvent("deliveryArrival", isDelayed: false, "Tax delivery"));
        Assert.False(EmpireTaxStage2Rules.IsEligibleTaxDeliveryEvent("constructBuilding", isDelayed: true, "Tax hall"));

        Assert.Equal(250L, EmpireTaxStage2Rules.ComputeDeliveredItemSilverValue(
            wasRescheduled: false,
            isDestroyed: false,
            isSpawnedOnPlayerHome: true,
            hasHoldingOwner: false,
            isSilverDef: false,
            stackCount: 100,
            unitMarketValue: 2.5f));
        Assert.Equal(0L, EmpireTaxStage2Rules.ComputeDeliveredItemSilverValue(
            wasRescheduled: true,
            isDestroyed: false,
            isSpawnedOnPlayerHome: true,
            hasHoldingOwner: false,
            isSilverDef: false,
            stackCount: 100,
            unitMarketValue: 2.5f));
        Assert.Equal(100L, EmpireTaxStage2Rules.ComputeDeliveredItemSilverValue(
            wasRescheduled: false,
            isDestroyed: false,
            isSpawnedOnPlayerHome: false,
            hasHoldingOwner: true,
            isSilverDef: true,
            stackCount: 100,
            unitMarketValue: 1.0f));
    }

    [Fact]
    public void AlphaGenesAndAlphaMechsStage2Rules_VerifyPostSuccessTransitions()
    {
        Assert.True(AlphaGenesStage2Rules.IsEligibleSyphonCasterBefore(
            isPlayerCaster: true, isCasterAlive: true, hasGeneTracker: true));
        Assert.Equal(1, AlphaGenesStage2Rules.ComputeNewlyAddedGeneCount(countBefore: 3, countAfter: 4));
        Assert.Equal(0, AlphaGenesStage2Rules.ComputeNewlyAddedGeneCount(countBefore: 3, countAfter: 3));

        Assert.True(AlphaGenesStage2Rules.ShouldRecordTeratogenicMutation(
            isPlayerCaster: true,
            isTargetAlive: true,
            carcinomaCountBefore: 2,
            carcinomaCountAfter: 0,
            hasMutationHediffAfter: true));
        Assert.False(AlphaGenesStage2Rules.ShouldRecordTeratogenicMutation(
            isPlayerCaster: true,
            isTargetAlive: true,
            carcinomaCountBefore: 0,
            carcinomaCountAfter: 0,
            hasMutationHediffAfter: true));

        Assert.True(AlphaMechsStage2Rules.ShouldRecordBossKill(
            bossDefPackageId: "sarg.alphamechs",
            isPawnDead: true,
            isPlayerPawn: false,
            hasPlayerPresenceOnMap: true));
        Assert.False(AlphaMechsStage2Rules.ShouldRecordBossKill(
            bossDefPackageId: "ludeon.rimworld.biotech",
            isPawnDead: true,
            isPlayerPawn: false,
            hasPlayerPresenceOnMap: true));
    }

    [Fact]
    public void GravshipAndWorldDominationStage2Rules_ExcludeStorageRoundTripsAndAiBattles()
    {
        Assert.Equal(45d, GravshipStage2Rules.FilterLandingGravdataYield(isPlayerEngine: true, calculatedYield: 45));
        Assert.Equal(0d, GravshipStage2Rules.FilterLandingGravdataYield(isPlayerEngine: false, calculatedYield: 45));
        Assert.Equal(0d, GravshipStage2Rules.FilterLandingGravdataYield(isPlayerEngine: true, calculatedYield: 0));

        Assert.Equal(12.5d, GravshipStage2Rules.ComputeConsoleGravdataDelta(
            hasPlayerActor: true, sameActiveProject: true, progressBefore: 100f, progressAfter: 112.5f), precision: 4);
        // ApplyGravDataYield invokes ResearchPerformed with actor == null (including BlackBox gravdata); must be 0.
        Assert.Equal(0d, GravshipStage2Rules.ComputeConsoleGravdataDelta(
            hasPlayerActor: false, sameActiveProject: true, progressBefore: 100f, progressAfter: 250f));

        Assert.Equal(
            WorldDominationStage2Rules.SimulatedBattleOutcome.PlayerOffensiveWin,
            WorldDominationStage2Rules.ClassifySimulatedBattle(isAttackerPlayer: true, isTargetPlayer: false, attackerWon: true));
        Assert.Equal(
            WorldDominationStage2Rules.SimulatedBattleOutcome.PlayerDefensiveWin,
            WorldDominationStage2Rules.ClassifySimulatedBattle(isAttackerPlayer: false, isTargetPlayer: true, attackerWon: false));
        // AI-vs-AI and player defeats must never be counted.
        Assert.Equal(
            WorldDominationStage2Rules.SimulatedBattleOutcome.None,
            WorldDominationStage2Rules.ClassifySimulatedBattle(isAttackerPlayer: false, isTargetPlayer: false, attackerWon: true));
        Assert.Equal(
            WorldDominationStage2Rules.SimulatedBattleOutcome.None,
            WorldDominationStage2Rules.ClassifySimulatedBattle(isAttackerPlayer: true, isTargetPlayer: false, attackerWon: false));
        Assert.Equal(
            WorldDominationStage2Rules.SimulatedBattleOutcome.None,
            WorldDominationStage2Rules.ClassifySimulatedBattle(isAttackerPlayer: false, isTargetPlayer: true, attackerWon: true));

        Assert.True(WorldDominationStage2Rules.ShouldRecordInterceptedOnMapConquest(
            wasSettlementDestroyedBefore: false,
            isSettlementPlayerFaction: false,
            skipOriginalCheckDefeated: true,
            isSettlementDestroyedAfter: true));
        Assert.False(WorldDominationStage2Rules.ShouldRecordInterceptedOnMapConquest(
            wasSettlementDestroyedBefore: false,
            isSettlementPlayerFaction: false,
            skipOriginalCheckDefeated: false,
            isSettlementDestroyedAfter: false));

        Assert.True(WorldDominationStage2Rules.ShouldRecordClashTransitionVictory(
            wasEncounterActiveBefore: true,
            hadPlayerWonBefore: false,
            hasPlayerWonAfter: true));
        Assert.False(WorldDominationStage2Rules.ShouldRecordClashTransitionVictory(
            wasEncounterActiveBefore: false,
            hadPlayerWonBefore: true,
            hasPlayerWonAfter: true));
    }
}
