using System.Reflection;
using HarmonyLib;
using RimWorld.Planet;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.NewModOutcomes;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.Stage2ModOutcomes;

/// <summary>
/// Stage 2 metrics for Dubs Rimatomics (Dubwise.Rimatomics, 1.7/Assemblies/Rimatomics.dll):
/// 1. SACS HEL projectile intercepts: Verb_HELBeam.TryCastShot destroying target projectile
///    and incrementing Building_EnergyWeapon.KillCounter on a player weapon.
/// 2. Punisher railgun world bombardments: WorldObject_Sabot.Arrived completing world transit
///    (arrived: false -> true, removed from WorldObjects) from a player railgun/home tile.
/// 3. Reactor emergency scrams: reactorCore.SCRAM transitioning a fuelled active player reactor
///    from IsShutdown == false to IsShutdown == true.
/// 4. Reactor core meltdowns: reactorCore.Destroy spawning Building_PoppedCore and executing
///    Building_PoppedCore.GoBang on a player home map.
/// </summary>
internal static class RimatomicsStage2Outcomes
{
    private const string AssemblyName = "Rimatomics";
    private const string HelUnit = "mod.dubwise.rimatomics.sacs_intercepts";
    private const string RailgunUnit = "mod.dubwise.rimatomics.railgun_bombardments";
    private const string ScramUnit = "mod.dubwise.rimatomics.reactor_scrams";
    private const string MeltdownUnit = "mod.dubwise.rimatomics.reactor_meltdowns";

    private static Type energyWeaponType = null!;
    private static FieldInfo killCounterField = null!;
    private static FieldInfo sabotArrivedField = null!;
    private static FieldInfo sabotRailgunField = null!;
    private static FieldInfo sabotInitialTileField = null!;
    private static FieldInfo sabotProjectileField = null!;
    private static FieldInfo reactorIsShutdownField = null!;
    private static FieldInfo reactorFuelCountField = null!;

    [ThreadStatic] private static MeltdownScope? activeMeltdown;

    private sealed record HelAttempt(Thing Caster, Thing Target, ThingDef TargetDef, int KillCounterBefore);
    private sealed record SabotAttempt(ThingDef ShellDef, bool WasSpawned, bool WasArrived);
    private sealed record ScramAttempt(Building Reactor, bool WasShutdown, int FuelCount);
    private sealed class MeltdownScope
    {
        internal required Map Map { get; init; }
        internal required ThingDef ReactorDef { get; init; }
        internal required MeltdownScope? Parent { get; init; }
        internal bool Recorded;
    }

    internal static IEnumerable<ModMetricAdapterRegistration> Build()
    {
        yield return OutcomeMetric.Count(
            RimatomicsStage2Rules.OwnerPackageId,
            "sacs_intercepts",
            "RWS_RimatomicsSacsIntercepts",
            "RWS_RimatomicsSacsInterceptsDesc",
            MetricCategory.Combat,
            InstallHelIntercepts,
            breakdown: true);

        yield return OutcomeMetric.Count(
            RimatomicsStage2Rules.OwnerPackageId,
            "railgun_bombardments",
            "RWS_RimatomicsRailgunBombardments",
            "RWS_RimatomicsRailgunBombardmentsDesc",
            MetricCategory.Combat,
            InstallRailgunBombardments,
            breakdown: true);

        yield return OutcomeMetric.Count(
            RimatomicsStage2Rules.OwnerPackageId,
            "reactor_scrams",
            "RWS_RimatomicsReactorScrams",
            "RWS_RimatomicsReactorScramsDesc",
            MetricCategory.Facilities,
            InstallReactorScrams,
            breakdown: true);

        yield return OutcomeMetric.Count(
            RimatomicsStage2Rules.OwnerPackageId,
            "reactor_meltdowns",
            "RWS_RimatomicsReactorMeltdowns",
            "RWS_RimatomicsReactorMeltdownsDesc",
            MetricCategory.Facilities,
            InstallReactorMeltdowns,
            breakdown: true);
    }

    private static void InstallHelIntercepts(Harmony harmony)
    {
        var verbType = OutcomeMetric.Bind("Rimatomics.Verb_HELBeam", AssemblyName);
        energyWeaponType = OutcomeMetric.Bind("Rimatomics.Building_EnergyWeapon", AssemblyName);
        if (!typeof(Verb).IsAssignableFrom(verbType) || !typeof(Building).IsAssignableFrom(energyWeaponType))
            throw new InvalidOperationException("Rimatomics HEL hierarchy changed");

        killCounterField = AccessTools.DeclaredField(energyWeaponType, "KillCounter") is { } kf && kf.FieldType == typeof(int)
            ? kf
            : throw new InvalidOperationException("Rimatomics Building_EnergyWeapon.KillCounter signature changed");

        var tryCast = OutcomeMetric.Method(verbType, "TryCastShot", typeof(bool));
        OutcomeMetric.Patch(harmony, tryCast, typeof(RimatomicsStage2Outcomes), nameof(BeforeHelShot), nameof(AfterHelShot));
    }

    private static void BeforeHelShot(Verb __instance, out HelAttempt? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(HelUnit))
            return;

        try
        {
            if (__instance?.caster is not { Spawned: true } caster ||
                !energyWeaponType.IsInstanceOfType(caster) ||
                !GameFacts.IsPlayer(caster) ||
                __instance.CurrentTarget.Thing is not { Destroyed: false, def: { } targetDef } target ||
                killCounterField.GetValue(caster) is not int killBefore)
                return;

            __state = new HelAttempt(caster, target, targetDef, killBefore);
        }
        catch (Exception error)
        {
            __state = null;
            LiveMetrics.Fail(HelUnit, error);
        }
    }

    private static void AfterHelShot(bool __result, bool __runOriginal, HelAttempt? __state)
    {
        if (!__runOriginal || !__result || __state is null || !OutcomeMetric.Observing(HelUnit))
            return;

        try
        {
            if (killCounterField.GetValue(__state.Caster) is not int killAfter)
                return;

            var key = RimatomicsStage2Rules.ResolveHelInterceptKey(
                casterPlayerOwned: GameFacts.IsPlayer(__state.Caster),
                castSucceeded: __result,
                targetAliveBefore: true,
                targetDestroyedAfter: __state.Target.Destroyed,
                killCounterBefore: __state.KillCounterBefore,
                killCounterAfter: killAfter,
                projectilePackageId: __state.TargetDef.modContentPack?.PackageId,
                projectileDefName: __state.TargetDef.defName);

            if (key is not null)
            {
                GameFacts.Record(HelUnit, state =>
                    OptionalTransitionRecording.RecordCount(state, "completed", 1, key: key, withBreakdown: true));
            }
        }
        catch (Exception error)
        {
            LiveMetrics.Fail(HelUnit, error);
        }
    }

    private static void InstallRailgunBombardments(Harmony harmony)
    {
        var sabotType = OutcomeMetric.Bind("Rimatomics.WorldObject_Sabot", AssemblyName);
        if (!typeof(WorldObject).IsAssignableFrom(sabotType))
            throw new InvalidOperationException("Rimatomics WorldObject_Sabot hierarchy changed");

        sabotArrivedField = RequireField(sabotType, "arrived", typeof(bool));
        sabotRailgunField = RequireField(sabotType, "railgun", typeof(Thing));
        sabotInitialTileField = RequireField(sabotType, "initialTile", typeof(PlanetTile));
        sabotProjectileField = RequireField(sabotType, "Projectile", typeof(ThingDef));

        var arrived = OutcomeMetric.Method(sabotType, "Arrived", typeof(void));
        OutcomeMetric.Patch(harmony, arrived, typeof(RimatomicsStage2Outcomes), nameof(BeforeSabotArrived), nameof(AfterSabotArrived));
    }

    private static void BeforeSabotArrived(WorldObject __instance, out SabotAttempt? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(RailgunUnit) || __instance is not { Spawned: true })
            return;

        try
        {
            if (sabotArrivedField.GetValue(__instance) is not bool wasArrived || wasArrived ||
                sabotProjectileField.GetValue(__instance) is not ThingDef shellDef)
                return;

            var railgun = sabotRailgunField.GetValue(__instance) as Thing;
            var playerOrigin = railgun is not null
                ? GameFacts.IsPlayer(railgun)
                : sabotInitialTileField.GetValue(__instance) is PlanetTile initialTile
                  && initialTile.Valid
                  && Current.Game?.FindMap(initialTile) is { IsPlayerHome: true };

            if (playerOrigin)
                __state = new SabotAttempt(shellDef, WasSpawned: true, WasArrived: false);
        }
        catch (Exception error)
        {
            __state = null;
            LiveMetrics.Fail(RailgunUnit, error);
        }
    }

    private static void AfterSabotArrived(WorldObject __instance, bool __runOriginal, SabotAttempt? __state)
    {
        if (!__runOriginal || __state is null || !OutcomeMetric.Observing(RailgunUnit))
            return;

        try
        {
            if (sabotArrivedField.GetValue(__instance) is not bool arrivedAfter)
                return;

            var key = RimatomicsStage2Rules.ResolveRailgunBombardmentKey(
                playerOrigin: true,
                wasSpawnedBefore: __state.WasSpawned,
                wasArrivedBefore: __state.WasArrived,
                isArrivedAfter: arrivedAfter,
                isSpawnedAfter: __instance.Spawned,
                shellPackageId: __state.ShellDef.modContentPack?.PackageId,
                shellDefName: __state.ShellDef.defName);

            if (key is not null)
            {
                GameFacts.Record(RailgunUnit, state =>
                    OptionalTransitionRecording.RecordCount(state, "completed", 1, key: key, withBreakdown: true));
            }
        }
        catch (Exception error)
        {
            LiveMetrics.Fail(RailgunUnit, error);
        }
    }

    private static void InstallReactorScrams(Harmony harmony)
    {
        var reactorType = BindReactorCore();
        var scram = OutcomeMetric.Method(reactorType, "SCRAM", typeof(void));
        OutcomeMetric.Patch(harmony, scram, typeof(RimatomicsStage2Outcomes), nameof(BeforeScram), nameof(AfterScram));
    }

    private static void BeforeScram(Building __instance, out ScramAttempt? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(ScramUnit) ||
            __instance is not { Spawned: true, Map.IsPlayerHome: true } ||
            !GameFacts.IsPlayer(__instance))
            return;

        try
        {
            if (reactorIsShutdownField.GetValue(__instance) is bool wasShutdown &&
                reactorFuelCountField.GetValue(__instance) is int fuelCount &&
                !wasShutdown && fuelCount > 0)
            {
                __state = new ScramAttempt(__instance, wasShutdown, fuelCount);
            }
        }
        catch (Exception error)
        {
            __state = null;
            LiveMetrics.Fail(ScramUnit, error);
        }
    }

    private static void AfterScram(bool __runOriginal, ScramAttempt? __state)
    {
        if (!__runOriginal || __state is null || !OutcomeMetric.Observing(ScramUnit))
            return;

        try
        {
            if (reactorIsShutdownField.GetValue(__state.Reactor) is not bool shutdownAfter)
                return;

            var key = RimatomicsStage2Rules.ResolveReactorScramKey(
                playerHomeReactor: true,
                wasShutdownBefore: __state.WasShutdown,
                fuelCountBefore: __state.FuelCount,
                isShutdownAfter: shutdownAfter,
                reactorPackageId: __state.Reactor.def?.modContentPack?.PackageId,
                reactorDefName: __state.Reactor.def?.defName);

            if (key is not null)
            {
                GameFacts.Record(ScramUnit, state =>
                    OptionalTransitionRecording.RecordCount(state, "completed", 1, key: key, withBreakdown: true));
            }
        }
        catch (Exception error)
        {
            LiveMetrics.Fail(ScramUnit, error);
        }
    }

    private static void InstallReactorMeltdowns(Harmony harmony)
    {
        var reactorType = BindReactorCore();
        var poppedType = OutcomeMetric.Bind("Rimatomics.Building_PoppedCore", AssemblyName);
        if (!typeof(Building).IsAssignableFrom(poppedType))
            throw new InvalidOperationException("Rimatomics Building_PoppedCore hierarchy changed");

        var destroy = OutcomeMetric.Method(reactorType, "Destroy", typeof(void), typeof(DestroyMode));
        var goBang = OutcomeMetric.Method(poppedType, "GoBang", typeof(void), typeof(int), typeof(Map));

        try
        {
            harmony.Patch(
                destroy,
                prefix: new HarmonyMethod(typeof(RimatomicsStage2Outcomes), nameof(BeginReactorDestroy)),
                finalizer: new HarmonyMethod(typeof(RimatomicsStage2Outcomes), nameof(EndReactorDestroy)));
            harmony.Patch(
                goBang,
                postfix: new HarmonyMethod(typeof(RimatomicsStage2Outcomes), nameof(AfterPoppedCoreGoBang)));
        }
        catch
        {
            harmony.Unpatch(destroy, HarmonyPatchType.All, harmony.Id);
            harmony.Unpatch(goBang, HarmonyPatchType.All, harmony.Id);
            throw;
        }
    }

    private static void BeginReactorDestroy(Building __instance, out MeltdownScope? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(MeltdownUnit) ||
            __instance is not { Spawned: true, Map: { IsPlayerHome: true } map, def: { } def } ||
            !GameFacts.IsPlayer(__instance))
            return;

        __state = new MeltdownScope { Map = map, ReactorDef = def, Parent = activeMeltdown };
        activeMeltdown = __state;
    }

    private static void AfterPoppedCoreGoBang(Map? __1, bool __runOriginal)
    {
        if (!__runOriginal || activeMeltdown is not { Recorded: false } scope ||
            !ReferenceEquals(__1, scope.Map) || !OutcomeMetric.Observing(MeltdownUnit))
            return;

        try
        {
            var key = RimatomicsStage2Rules.ResolveReactorMeltdownKey(
                activeMeltdownScope: true,
                reactorPackageId: scope.ReactorDef.modContentPack?.PackageId,
                reactorDefName: scope.ReactorDef.defName);
            if (key is not null)
            {
                scope.Recorded = true;
                GameFacts.Record(MeltdownUnit, state =>
                    OptionalTransitionRecording.RecordCount(state, "completed", 1, key: key, withBreakdown: true));
            }
        }
        catch (Exception error)
        {
            LiveMetrics.Fail(MeltdownUnit, error);
        }
    }

    private static Exception? EndReactorDestroy(Exception? __exception, MeltdownScope? __state)
    {
        if (__state is not null && ReferenceEquals(activeMeltdown, __state))
            activeMeltdown = __state.Parent;
        return __exception;
    }

    private static Type BindReactorCore()
    {
        var reactorType = OutcomeMetric.Bind("Rimatomics.reactorCore", AssemblyName);
        if (!typeof(Building).IsAssignableFrom(reactorType))
            throw new InvalidOperationException("Rimatomics reactorCore hierarchy changed");

        reactorIsShutdownField = RequireField(reactorType, "IsShutdown", typeof(bool));
        reactorFuelCountField = RequireField(reactorType, "FuelCount", typeof(int));
        return reactorType;
    }

    private static FieldInfo RequireField(Type owner, string name, Type expectedType)
        => AccessTools.DeclaredField(owner, name) is { } field && field.FieldType == expectedType
            ? field
            : throw new InvalidOperationException($"Rimatomics {owner.Name}.{name} signature changed");
}
