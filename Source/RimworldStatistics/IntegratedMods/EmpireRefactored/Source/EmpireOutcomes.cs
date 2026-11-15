using HarmonyLib;
using RimWorld.Planet;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.NewModOutcomes;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.EmpireRefactored;

internal static class EmpireOutcomes
{
    private const string Owner = "Matathias.Empire";
    private const string Assembly = "Empire";
    private const string Founding = "mod.matathias.empire.settlement_founding";
    private const string Upgrades = "mod.matathias.empire.settlement_upgrades";
    private const string Buildings = "mod.matathias.empire.settlement_buildings";

    internal static IEnumerable<ModMetricAdapterRegistration> Build()
    {
        yield return OutcomeMetric.Count(Owner, "settlement_founding", "RWS_EmpireFounding", "RWS_EmpireFoundingDesc",
            MetricCategory.Colony, InstallFounding);
        yield return OutcomeMetric.Count(Owner, "settlement_upgrades", "RWS_EmpireUpgrades", "RWS_EmpireUpgradesDesc",
            MetricCategory.Colony, InstallUpgrades);
        yield return OutcomeMetric.Count(Owner, "settlement_buildings", "RWS_EmpireBuildings", "RWS_EmpireBuildingsDesc",
            MetricCategory.Facilities, InstallBuildings, breakdown: true);
    }

    private static Type SettlementType() => OutcomeMetric.Bind("FactionColonies.WorldSettlementFC", Assembly);

    private static void InstallFounding(Harmony harmony)
    {
        var utility = OutcomeMetric.Bind("FactionColonies.util.ColonyUtil", Assembly);
        var def = OutcomeMetric.Bind("FactionColonies.WorldSettlementDef", Assembly);
        var create = OutcomeMetric.Method(utility, "CreatePlayerColonySettlement", SettlementType(),
            typeof(PlanetTile), def, typeof(string));
        OutcomeMetric.Patch(harmony, create, typeof(EmpireOutcomes), null, nameof(Founded));
    }

    private static void Founded(WorldObject? __result, bool __runOriginal)
    {
        if (!__runOriginal || __result is null || !OutcomeMetric.Observing(Founding))
            return;
        GameFacts.Record(Founding, state => EmpireOutcomeRules.RecordFounding(state, __result.ID, __result.Spawned));
    }

    private static void InstallUpgrades(Harmony harmony)
    {
        var settlement = SettlementType();
        if (AccessTools.DeclaredField(settlement, "settlementLevel")?.FieldType != typeof(int))
            throw new InvalidOperationException("Empire settlement level field changed");
        var upgrade = OutcomeMetric.Method(settlement, "UpgradeSettlement", typeof(void), typeof(int), typeof(bool));
        OutcomeMetric.Patch(harmony, upgrade, typeof(EmpireOutcomes), nameof(BeforeUpgrade), nameof(AfterUpgrade));
    }

    private static void BeforeUpgrade(WorldObject __instance, int ___settlementLevel, out int __state)
        => __state = __instance.Spawned && OutcomeMetric.Observing(Upgrades) ? ___settlementLevel : -1;

    private static void AfterUpgrade(WorldObject __instance, int ___settlementLevel, bool __runOriginal, int __state)
    {
        if (__runOriginal && __instance.Spawned && EmpireOutcomeRules.Increased(__state, ___settlementLevel) &&
            OutcomeMetric.Observing(Upgrades))
            OutcomeMetric.Record(Upgrades);
    }

    private static void InstallBuildings(Harmony harmony)
    {
        var buildings = OutcomeMetric.Bind("FactionColonies.WorldObjectComp_SettlementBuildings", Assembly);
        var definition = OutcomeMetric.Bind("FactionColonies.BuildingFCDef", Assembly);
        var construct = OutcomeMetric.Method(buildings, "ConstructBuilding", typeof(bool), definition, typeof(int));
        OutcomeMetric.Patch(harmony, construct, typeof(EmpireOutcomes), null, nameof(Constructed));
    }

    private static void Constructed(WorldObjectComp __instance, Def? __0, bool __result, bool __runOriginal)
    {
        if (__runOriginal && __result && __0 is not null && __instance.parent is { Spawned: true } &&
            OutcomeMetric.Observing(Buildings))
            OutcomeMetric.Record(Buildings, __0, breakdown: true);
    }
}
