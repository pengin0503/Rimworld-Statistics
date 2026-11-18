namespace RimworldStatistics.Stage2ModOutcomes;

/// <summary>
/// Pure eligibility helpers for World Domination 2.0 (TSA.WorldDominationExperimental,
/// 1.7/Assemblies/TSA_WorldDomination.dll) Stage 2 statistics:
/// 1. Player-force settlement conquests (strictly excluding AI-vs-AI and ally-claimed raids)
/// 2. Player-force battle victories across simulated raids, outpost defenses, and caravan/camp clashes
/// </summary>
internal static class WorldDominationStage2Rules
{
    internal const string OwnerPackageId = "tsa.worlddominationexperimental";

    internal enum SimulatedBattleOutcome
    {
        None = 0,
        PlayerOffensiveWin = 1,
        PlayerDefensiveWin = 2,
    }

    internal static SimulatedBattleOutcome ClassifySimulatedBattle(
        bool isAttackerPlayer,
        bool isTargetPlayer,
        bool attackerWon)
    {
        if (isAttackerPlayer && !isTargetPlayer && attackerWon)
            return SimulatedBattleOutcome.PlayerOffensiveWin;
        if (!isAttackerPlayer && isTargetPlayer && !attackerWon)
            return SimulatedBattleOutcome.PlayerDefensiveWin;
        return SimulatedBattleOutcome.None;
    }

    internal static bool ShouldRecordPlayerSettlementConquest(
        bool wasTargetDestroyedBefore,
        bool isTargetPlayerFaction,
        bool isTargetDestroyedAfter)
        => !wasTargetDestroyedBefore && !isTargetPlayerFaction && isTargetDestroyedAfter;

    internal static bool ShouldRecordInterceptedOnMapConquest(
        bool wasSettlementDestroyedBefore,
        bool isSettlementPlayerFaction,
        bool skipOriginalCheckDefeated,
        bool isSettlementDestroyedAfter)
        => !wasSettlementDestroyedBefore &&
           !isSettlementPlayerFaction &&
           skipOriginalCheckDefeated &&
           isSettlementDestroyedAfter;

    internal static bool ShouldRecordClashTransitionVictory(
        bool wasEncounterActiveBefore,
        bool hadPlayerWonBefore,
        bool hasPlayerWonAfter)
        => wasEncounterActiveBefore && !hadPlayerWonBefore && hasPlayerWonAfter;
}
