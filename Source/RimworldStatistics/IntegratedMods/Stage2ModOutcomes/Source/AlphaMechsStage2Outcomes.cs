using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.NewModOutcomes;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.Stage2ModOutcomes;

/// <summary>
/// Stage 2 metric for Alpha Mechs (sarg.alphamechs, 1.6/Assemblies/AlphaMechs.dll selected by &lt;v1.7&gt;):
/// Boss mechanoid kills: observes GameComponent_Bossgroup.Notify_PawnKilled(Pawn) for hostile
/// BossDef commanders owned by sarg.alphamechs (AM_Infernus, AM_WarEmpress, AM_Apoptosis)
/// slain on maps with colony presence. Unlike biotech.bossgroups.first_defeated_ticks (which
/// only records the first-ever defeat timestamp per BossDef), this counts cumulative boss kills
/// across repeated bossgroup summons. (Hostile mechanoid conversions are already tracked in
/// AlphaMechsStatistics.)
/// </summary>
internal static class AlphaMechsStage2Outcomes
{
    private const string AssemblyName = "AlphaMechs";
    private const string BossKillsUnit = "mod.sarg.alphamechs.boss_mech_kills";

    internal static IEnumerable<ModMetricAdapterRegistration> Build()
    {
        yield return OutcomeMetric.Count(
            AlphaMechsStage2Rules.OwnerPackageId,
            "boss_mech_kills",
            "RWS_AlphaMechsBossKills",
            "RWS_AlphaMechsBossKillsDesc",
            MetricCategory.Combat,
            InstallBossKills,
            breakdown: true,
            dlc: AcquisitionUnits.Biotech);
    }

    private static void InstallBossKills(Harmony harmony)
    {
        _ = OutcomeMetric.Bind("AlphaMechs.CompMechConversion", AssemblyName);
        var notifyKilled = OutcomeMetric.Method(typeof(GameComponent_Bossgroup), nameof(GameComponent_Bossgroup.Notify_PawnKilled), typeof(void), typeof(Pawn));
        OutcomeMetric.Patch(harmony, notifyKilled, typeof(AlphaMechsStage2Outcomes), prefix: null, postfix: nameof(AfterBossPawnKilled));
    }

    private static bool HasPlayerPresence(Map? map)
        => map is not null &&
           (map.IsPlayerHome ||
            GameFacts.IsPlayerFaction(map.Parent?.Faction) ||
            (map.mapPawns?.FreeColonistsSpawnedCount ?? 0) > 0);

    private static void AfterBossPawnKilled(bool __runOriginal, GameComponent_Bossgroup __instance, Pawn? __0)
    {
        if (!__runOriginal || !OutcomeMetric.Observing(BossKillsUnit) ||
            __instance is null || __0 is not { kindDef: { } kind } pawn)
            return;

        try
        {
            var bossDef = __instance.AllBosses?.FirstOrDefault(b => b?.kindDef == kind);
            var map = pawn.MapHeld;
            if (AlphaMechsStage2Rules.ShouldRecordBossKill(
                    bossDefPackageId: bossDef?.modContentPack?.PackageId,
                    isPawnDead: pawn.Dead,
                    isPlayerPawn: GameFacts.IsPlayerFaction(pawn.Faction),
                    hasPlayerPresenceOnMap: HasPlayerPresence(map)))
            {
                OutcomeMetric.Record(BossKillsUnit, kind, breakdown: true);
            }
        }
        catch (Exception error)
        {
            LiveMetrics.Fail(BossKillsUnit, error);
        }
    }
}
