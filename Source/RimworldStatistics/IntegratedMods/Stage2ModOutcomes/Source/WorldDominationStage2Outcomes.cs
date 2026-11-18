using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.NewModOutcomes;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.Stage2ModOutcomes;

/// <summary>
/// Stage 2 metrics for World Domination 2.0 (TSA.WorldDominationExperimental,
/// 1.7/Assemblies/TSA_WorldDomination.dll):
/// 1. Settlement conquests: records non-player settlements conquered by player forces via
///    Raid_Simulated.ResolveVictoryWithPlayerConquestChoices (gated upstream to player origin/attacker,
///    excluding AI-vs-AI ResolveVictory and AllyAwardsToPlayer) or on-map via Patch_InterceptDefeat.Prefix.
/// 2. Player battle victories: records player-only victories across WD simulated offensive/defensive
///    raids (Raid_Simulated.ApplyLossesAndLog) and WD caravan/camp/bridge clashes
///    (NpcRaidKillGoodwillUtility.TryRewardFromSnapshot and WD_MapComponent_CaravanClash exit latches),
///    broken down by defeated enemy FactionDef and strictly excluding AI-vs-AI battles.
/// </summary>
internal static class WorldDominationStage2Outcomes
{
    private const string AssemblyName = "TSA_WorldDomination";
    private const string ConquestsUnit = "mod.tsa.worlddominationexperimental.settlement_conquests";
    private const string VictoriesUnit = "mod.tsa.worlddominationexperimental.player_battle_victories";

    private static PropertyInfo clashPlayerHasWonProperty = null!;
    private static PropertyInfo clashEncounterActiveProperty = null!;
    private static FieldInfo clashEnemyFactionField = null!;

    private sealed record ConquestAttempt(WorldObject Target, FactionDef? DefeatedFaction);

    private sealed record ClashAttempt(MapComponent Clash, bool WasEncounterActive, bool HadPlayerWon);

    internal static IEnumerable<ModMetricAdapterRegistration> Build()
    {
        yield return OutcomeMetric.Count(
            WorldDominationStage2Rules.OwnerPackageId,
            "settlement_conquests",
            "RWS_WorldDominationConquests",
            "RWS_WorldDominationConquestsDesc",
            MetricCategory.Exploration,
            InstallConquests,
            breakdown: true);

        yield return OutcomeMetric.Count(
            WorldDominationStage2Rules.OwnerPackageId,
            "player_battle_victories",
            "RWS_WorldDominationPlayerVictories",
            "RWS_WorldDominationPlayerVictoriesDesc",
            MetricCategory.Combat,
            InstallPlayerVictories,
            breakdown: true);
    }

    private static void InstallConquests(Harmony harmony)
    {
        var raidSimulated = OutcomeMetric.Bind("TSA_WorldDomination.Raid_Simulated", AssemblyName);
        var travelerType = OutcomeMetric.Bind("TSA_WorldDomination.WorldObject_Traveler", AssemblyName);
        var spreadManagerType = OutcomeMetric.Bind("TSA_WorldDomination.WorldComponent_SpreadManager", AssemblyName);
        var marginTierType = OutcomeMetric.Bind("TSA_WorldDomination.BattleMarginTier", AssemblyName);
        var forceRowListType = typeof(List<>).MakeGenericType(OutcomeMetric.Bind("TSA_WorldDomination.RaidForceLogRow", AssemblyName));
        var interceptPatch = OutcomeMetric.Bind("TSA_WorldDomination.Patch_InterceptDefeat", AssemblyName);

        var resolvePlayerConquest = OutcomeMetric.Method(
            raidSimulated,
            "ResolveVictoryWithPlayerConquestChoices",
            typeof(void),
            typeof(WorldObject),
            travelerType,
            spreadManagerType,
            typeof(float),
            typeof(float),
            typeof(float),
            typeof(List<string>),
            typeof(List<string>),
            typeof(float),
            typeof(float),
            typeof(float),
            marginTierType,
            marginTierType,
            forceRowListType,
            forceRowListType);

        var interceptDefeat = OutcomeMetric.Method(interceptPatch, "Prefix", typeof(bool), typeof(Settlement));

        var beforeSim = AccessTools.DeclaredMethod(typeof(WorldDominationStage2Outcomes), nameof(BeforeSimulatedConquest));
        var afterSim = AccessTools.DeclaredMethod(typeof(WorldDominationStage2Outcomes), nameof(AfterSimulatedConquest));
        var beforeMap = AccessTools.DeclaredMethod(typeof(WorldDominationStage2Outcomes), nameof(BeforeInterceptDefeat));
        var afterMap = AccessTools.DeclaredMethod(typeof(WorldDominationStage2Outcomes), nameof(AfterInterceptDefeat));

        try
        {
            harmony.Patch(resolvePlayerConquest, prefix: new HarmonyMethod(beforeSim), postfix: new HarmonyMethod(afterSim));
            harmony.Patch(interceptDefeat, prefix: new HarmonyMethod(beforeMap), postfix: new HarmonyMethod(afterMap));
        }
        catch
        {
            harmony.Unpatch(resolvePlayerConquest, beforeSim);
            harmony.Unpatch(resolvePlayerConquest, afterSim);
            harmony.Unpatch(interceptDefeat, beforeMap);
            harmony.Unpatch(interceptDefeat, afterMap);
            throw;
        }
    }

    private static void BeforeSimulatedConquest(WorldObject? __0, out ConquestAttempt? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(ConquestsUnit) ||
            __0 is not { Destroyed: false } target ||
            GameFacts.IsPlayerFaction(target.Faction))
            return;

        __state = new ConquestAttempt(target, target.Faction?.def);
    }

    private static void AfterSimulatedConquest(bool __runOriginal, ConquestAttempt? __state)
    {
        if (!__runOriginal || __state is null || !OutcomeMetric.Observing(ConquestsUnit))
            return;

        try
        {
            if (WorldDominationStage2Rules.ShouldRecordPlayerSettlementConquest(
                    wasTargetDestroyedBefore: false,
                    isTargetPlayerFaction: false,
                    isTargetDestroyedAfter: __state.Target.Destroyed))
            {
                OutcomeMetric.Record(ConquestsUnit, __state.DefeatedFaction, breakdown: true);
            }
        }
        catch (Exception error)
        {
            LiveMetrics.Fail(ConquestsUnit, error);
        }
    }

    private static void BeforeInterceptDefeat(Settlement? __0, out ConquestAttempt? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(ConquestsUnit) ||
            __0 is not { Destroyed: false } settlement ||
            GameFacts.IsPlayerFaction(settlement.Faction))
            return;

        __state = new ConquestAttempt(settlement, settlement.Faction?.def);
    }

    private static void AfterInterceptDefeat(bool __runOriginal, bool __result, ConquestAttempt? __state)
    {
        if (!__runOriginal || __state is null || !OutcomeMetric.Observing(ConquestsUnit))
            return;

        try
        {
            if (WorldDominationStage2Rules.ShouldRecordInterceptedOnMapConquest(
                    wasSettlementDestroyedBefore: false,
                    isSettlementPlayerFaction: false,
                    skipOriginalCheckDefeated: !__result,
                    isSettlementDestroyedAfter: __state.Target.Destroyed))
            {
                OutcomeMetric.Record(ConquestsUnit, __state.DefeatedFaction, breakdown: true);
            }
        }
        catch (Exception error)
        {
            LiveMetrics.Fail(ConquestsUnit, error);
        }
    }

    private static void InstallPlayerVictories(Harmony harmony)
    {
        var raidSimulated = OutcomeMetric.Bind("TSA_WorldDomination.Raid_Simulated", AssemblyName);
        var spreadManagerType = OutcomeMetric.Bind("TSA_WorldDomination.WorldComponent_SpreadManager", AssemblyName);
        var marginTierType = OutcomeMetric.Bind("TSA_WorldDomination.BattleMarginTier", AssemblyName);
        var forceRowListType = typeof(List<>).MakeGenericType(OutcomeMetric.Bind("TSA_WorldDomination.RaidForceLogRow", AssemblyName));
        var goodwillUtil = OutcomeMetric.Bind("TSA_WorldDomination.NpcRaidKillGoodwillUtility", AssemblyName);
        var targetKindType = OutcomeMetric.Bind("TSA_WorldDomination.RaidLaunchTargetKind", AssemblyName);
        var clashComp = OutcomeMetric.Bind("TSA_WorldDomination.WD_MapComponent_CaravanClash", AssemblyName);
        if (!typeof(MapComponent).IsAssignableFrom(clashComp))
            throw new InvalidOperationException("WD_MapComponent_CaravanClash no longer derives from MapComponent");

        var applyLosses = OutcomeMetric.Method(
            raidSimulated,
            "ApplyLossesAndLog",
            typeof(void),
            typeof(WorldObject),
            typeof(WorldObject),
            typeof(List<WorldObject>),
            typeof(List<WorldObject>),
            typeof(float),
            typeof(float),
            typeof(string),
            typeof(string),
            spreadManagerType,
            typeof(float),
            typeof(float),
            typeof(float),
            typeof(bool),
            typeof(float),
            typeof(List<string>),
            typeof(List<string>),
            typeof(float),
            typeof(Dictionary<WorldObject, float>),
            typeof(float),
            marginTierType,
            marginTierType,
            forceRowListType,
            forceRowListType);

        var tryReward = OutcomeMetric.Method(
            goodwillUtil,
            "TryRewardFromSnapshot",
            typeof(void),
            typeof(Faction),
            typeof(float),
            typeof(WorldObject),
            typeof(WorldObject),
            targetKindType,
            typeof(bool),
            typeof(WorldObject),
            typeof(bool).MakeByRefType());

        var notifyAirborne = OutcomeMetric.Method(clashComp, "NotifyAirborneSurvivorsLeftThisClash", typeof(void));
        var mapRemoved = OutcomeMetric.Method(clashComp, "MapRemoved", typeof(void));

        clashPlayerHasWonProperty = AccessTools.Property(clashComp, "PlayerHasWon") is { GetMethod: not null } pw && pw.PropertyType == typeof(bool)
            ? pw
            : throw new InvalidOperationException("WD_MapComponent_CaravanClash.PlayerHasWon signature changed");
        clashEncounterActiveProperty = AccessTools.Property(clashComp, "EncounterActive") is { GetMethod: not null } ea && ea.PropertyType == typeof(bool)
            ? ea
            : throw new InvalidOperationException("WD_MapComponent_CaravanClash.EncounterActive signature changed");
        clashEnemyFactionField = AccessTools.DeclaredField(clashComp, "enemyFaction") is { } ef && ef.FieldType == typeof(Faction)
            ? ef
            : throw new InvalidOperationException("WD_MapComponent_CaravanClash.enemyFaction signature changed");

        var afterSimulated = AccessTools.DeclaredMethod(typeof(WorldDominationStage2Outcomes), nameof(AfterSimulatedBattle));
        var afterClashTickWin = AccessTools.DeclaredMethod(typeof(WorldDominationStage2Outcomes), nameof(AfterClashTickVictory));
        var beforeClashExit = AccessTools.DeclaredMethod(typeof(WorldDominationStage2Outcomes), nameof(BeforeClashExit));
        var afterClashExit = AccessTools.DeclaredMethod(typeof(WorldDominationStage2Outcomes), nameof(AfterClashExit));

        try
        {
            harmony.Patch(applyLosses, postfix: new HarmonyMethod(afterSimulated));
            harmony.Patch(tryReward, postfix: new HarmonyMethod(afterClashTickWin));
            harmony.Patch(notifyAirborne, prefix: new HarmonyMethod(beforeClashExit), postfix: new HarmonyMethod(afterClashExit));
            harmony.Patch(mapRemoved, prefix: new HarmonyMethod(beforeClashExit), postfix: new HarmonyMethod(afterClashExit));
        }
        catch
        {
            harmony.Unpatch(applyLosses, afterSimulated);
            harmony.Unpatch(tryReward, afterClashTickWin);
            harmony.Unpatch(notifyAirborne, beforeClashExit);
            harmony.Unpatch(notifyAirborne, afterClashExit);
            harmony.Unpatch(mapRemoved, beforeClashExit);
            harmony.Unpatch(mapRemoved, afterClashExit);
            throw;
        }
    }

    private static void AfterSimulatedBattle(bool __runOriginal, WorldObject? __0, WorldObject? __1, bool __12)
    {
        if (!__runOriginal || !OutcomeMetric.Observing(VictoriesUnit))
            return;

        try
        {
            var outcome = WorldDominationStage2Rules.ClassifySimulatedBattle(
                isAttackerPlayer: GameFacts.IsPlayerFaction(__0?.Faction),
                isTargetPlayer: GameFacts.IsPlayerFaction(__1?.Faction),
                attackerWon: __12);

            if (outcome == WorldDominationStage2Rules.SimulatedBattleOutcome.PlayerOffensiveWin)
                OutcomeMetric.Record(VictoriesUnit, __1?.Faction?.def, breakdown: true);
            else if (outcome == WorldDominationStage2Rules.SimulatedBattleOutcome.PlayerDefensiveWin)
                OutcomeMetric.Record(VictoriesUnit, __0?.Faction?.def, breakdown: true);
        }
        catch (Exception error)
        {
            LiveMetrics.Fail(VictoriesUnit, error);
        }
    }

    private static void AfterClashTickVictory(bool __runOriginal, Faction? __0)
    {
        if (!__runOriginal || !OutcomeMetric.Observing(VictoriesUnit))
            return;

        try
        {
            if (!GameFacts.IsPlayerFaction(__0))
                OutcomeMetric.Record(VictoriesUnit, __0?.def, breakdown: true);
        }
        catch (Exception error)
        {
            LiveMetrics.Fail(VictoriesUnit, error);
        }
    }

    private static void BeforeClashExit(MapComponent __instance, out ClashAttempt? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(VictoriesUnit) || __instance is null)
            return;

        try
        {
            if (clashEncounterActiveProperty.GetValue(__instance) is bool active &&
                clashPlayerHasWonProperty.GetValue(__instance) is bool won &&
                active && !won)
            {
                __state = new ClashAttempt(__instance, active, won);
            }
        }
        catch (Exception error)
        {
            __state = null;
            LiveMetrics.Fail(VictoriesUnit, error);
        }
    }

    private static void AfterClashExit(bool __runOriginal, ClashAttempt? __state)
    {
        if (!__runOriginal || __state is null || !OutcomeMetric.Observing(VictoriesUnit))
            return;

        try
        {
            if (clashPlayerHasWonProperty.GetValue(__state.Clash) is bool wonAfter &&
                WorldDominationStage2Rules.ShouldRecordClashTransitionVictory(
                    wasEncounterActiveBefore: __state.WasEncounterActive,
                    hadPlayerWonBefore: __state.HadPlayerWon,
                    hasPlayerWonAfter: wonAfter))
            {
                var enemyFaction = clashEnemyFactionField.GetValue(__state.Clash) as Faction;
                OutcomeMetric.Record(VictoriesUnit, enemyFaction?.def, breakdown: true);
            }
        }
        catch (Exception error)
        {
            LiveMetrics.Fail(VictoriesUnit, error);
        }
    }
}
