using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.SaveOurShip2;

internal static class SaveOurShip2BattleStatistics
{
    private const string Owner = "kentington.saveourship2";
    private const string Unit = "mod.kentington.saveourship2.battles";
    private static PropertyInfo? originComp;
    private static PropertyInfo? targetComp;
    private static PropertyInfo? playerMap;
    private static FieldInfo? targetMap;
    private static FieldInfo? battleState;
    private static FieldInfo? bridges;
    private static object? inCombat;
    private static object? nominal;
    private static object? graveyard;

    private sealed record Combat(MapComponent Player, MapComponent Enemy);

    internal static ModMetricAdapterRegistration Build() => new(new ModMetricDescriptor
    {
        ProviderId = "rws.sos2battles",
        OwnerPackageId = Owner,
        FamilyId = "battles",
        MetricName = "won",
        Source = MetricObservationSource.Checkpoint,
        Revision = 1,
        Category = MetricCategory.Combat,
        Unit = MetricUnit.Count,
        Overall = OverallAggregation.Sum,
        Selection = ColonySelection.Representative,
        RequiredPackages = [new ModPackageRequirement(Owner), new ModPackageRequirement("SmashPhil.VehicleFramework")],
        LabelKey = "RWS_Sos2_BattlesWon",
        DescriptionKey = "RWS_Sos2_BattlesWonDesc",
    }, new LiveFamily
    {
        Name = Unit,
        Revision = 1,
        Build = output => output.PutCount("won", Unit + ".won"),
    }, Install);

    private static void Install(Harmony harmony)
    {
        var comp = AccessTools.TypeByName("SaveOurShip2.ShipMapComp");
        if (comp?.Assembly.GetName().Name != "ShipsHaveInsides" || !typeof(MapComponent).IsAssignableFrom(comp))
            throw new InvalidOperationException("Save Our Ship 2 combat component unavailable");
        var end = AccessTools.DeclaredMethod(comp, "EndBattle", [typeof(Map), typeof(bool), typeof(bool), typeof(int)]);
        var origin = AccessTools.DeclaredProperty(comp, "OriginMapComp");
        var target = AccessTools.DeclaredProperty(comp, "TargetMapComp");
        var own = AccessTools.DeclaredProperty(comp, "IsPlayerShipMap");
        var opposingMap = AccessTools.DeclaredField(comp, "ShipCombatTargetMap");
        var state = AccessTools.DeclaredField(comp, "ShipMapState");
        var roots = AccessTools.DeclaredField(comp, "MapRootListAll");
        if (end?.ReturnType != typeof(void) || origin?.PropertyType != comp || target?.PropertyType != comp ||
            own?.PropertyType != typeof(bool) || opposingMap?.FieldType != typeof(Map) ||
            state is null || !state.FieldType.IsEnum || roots is null || !typeof(IEnumerable).IsAssignableFrom(roots.FieldType) ||
            !Enum.TryParse(state.FieldType, "inCombat", out inCombat) ||
            !Enum.TryParse(state.FieldType, "nominal", out nominal) ||
            !Enum.TryParse(state.FieldType, "isGraveyard", out graveyard))
            throw new InvalidOperationException("Save Our Ship 2 combat signatures changed");

        originComp = origin;
        targetComp = target;
        playerMap = own;
        targetMap = opposingMap;
        battleState = state;
        bridges = roots;
        try
        {
            harmony.Patch(end,
                prefix: new HarmonyMethod(typeof(SaveOurShip2BattleStatistics), nameof(Before)),
                postfix: new HarmonyMethod(typeof(SaveOurShip2BattleStatistics), nameof(After)));
        }
        catch
        {
            harmony.Unpatch(end, HarmonyPatchType.All, harmony.Id);
            throw;
        }
    }

    private static void Before(object __instance, Map __0, bool __1, out Combat? __state)
    {
        __state = null;
        if (__1 || __0 is null || !OptionalTransitionCallSite.Observing(Unit))
            return;
        try
        {
            if (originComp?.GetValue(__instance) is not MapComponent player ||
                targetComp?.GetValue(player) is not MapComponent enemy || ReferenceEquals(player, enemy) ||
                !ReferenceEquals(enemy.map, __0) || !ReferenceEquals(targetMap?.GetValue(enemy), player.map) ||
                !Equals(battleState?.GetValue(player), inCombat) || !Equals(battleState?.GetValue(enemy), inCombat) ||
                playerMap?.GetValue(player) is not true || playerMap.GetValue(enemy) is not false ||
                bridges?.GetValue(player) is not IEnumerable roots ||
                !roots.OfType<Building>().Any(bridge => bridge.Spawned && GameFacts.IsPlayer(bridge)))
                return;
            __state = new Combat(player, enemy);
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void After(bool __runOriginal, Combat? __state)
    {
        if (!__runOriginal || __state is null)
            return;
        try
        {
            if (Equals(battleState?.GetValue(__state.Player), nominal) &&
                Equals(battleState?.GetValue(__state.Enemy), graveyard) &&
                targetMap?.GetValue(__state.Player) is null && targetMap?.GetValue(__state.Enemy) is null)
                GameFacts.Record(Unit, state => OptionalTransitionRecording.RecordCount(state, "won"));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }
}
