using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;
using Verse.AI;

namespace RimworldStatistics.CombatExtendedReload;

/// <summary>
/// Each completed player JobDriver_Reload.DoReload counts once only when a
/// scoped CompAmmoUser.LoadAmmo consumes ammo and leaves loaded rounds.
/// Turrets, loaders, disabled ammo and queued jobs do not count.
/// </summary>
internal static class CombatExtendedReloadStatistics
{
    private const string Owner = "CETeam.CombatExtended";
    private const string Unit = "mod.ceteam.combatextended.pawnreload";
    private const string Key = Unit + ".completed";
    private static PropertyInfo? ammoUserProperty;
    private static PropertyInfo? useAmmoProperty;
    private static PropertyInfo? magazineProperty;
    private static FieldInfo? jobDriverPawnField;
    [ThreadStatic] private static ReloadScope? active;

    private sealed class ReloadScope
    {
        internal required object AmmoUser { get; init; }
        internal required ReloadScope? Parent { get; init; }
        internal readonly CombatExtendedReloadRules.ReloadCounter Counter = new();
    }

    private sealed record AmmoBefore(ReloadScope Scope, Thing? Ammo,
        int StackBefore, int MagBefore);

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        Build = output => OptionalTransitionRecording.PutCount(output, "completed", Key),
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
        => [new ModMetricAdapterRegistration(new ModMetricDescriptor
        {
            ProviderId = "rws.pawnreload",
            OwnerPackageId = Owner,
            FamilyId = "pawnreload",
            MetricName = "completed",
            Source = MetricObservationSource.Checkpoint,
            Revision = 1,
            Category = MetricCategory.Combat,
            Unit = MetricUnit.Count,
            Overall = OverallAggregation.Sum,
            Selection = ColonySelection.Representative,
            HasBreakdown = false,
            RequiredPackages = [new ModPackageRequirement(Owner)],
            LabelKey = "RWS_CombatExtended_Reloads",
            DescriptionKey = "RWS_CombatExtended_ReloadsDesc",
        }, Family, Install)];

    private static void Install(Harmony harmony)
    {
        var job = AccessTools.TypeByName("CombatExtended.JobDriver_Reload");
        var ammo = AccessTools.TypeByName("CombatExtended.CompAmmoUser");
        if (job is null || ammo is null || !typeof(JobDriver).IsAssignableFrom(job) ||
            !typeof(ThingComp).IsAssignableFrom(ammo) ||
            !string.Equals(job.Assembly.GetName().Name, "CombatExtended",
                StringComparison.OrdinalIgnoreCase) ||
            !ReferenceEquals(job.Assembly, ammo.Assembly))
            throw new InvalidOperationException("CombatExtended 1.7 effective assembly not loaded");

        var doReload = AccessTools.DeclaredMethod(job, "DoReload", Type.EmptyTypes);
        var load = AccessTools.DeclaredMethod(ammo, "LoadAmmo", [typeof(Thing), typeof(bool)]);
        var comp = AccessTools.Property(job, "compReloader");
        var useAmmo = AccessTools.Property(ammo, "UseAmmo");
        var rounds = AccessTools.Property(ammo, "CurMagCount");
        var pawn = AccessTools.Field(typeof(JobDriver), "pawn");
        if (doReload?.ReturnType != typeof(void) || load?.ReturnType != typeof(void) ||
            comp?.PropertyType != ammo || useAmmo?.PropertyType != typeof(bool) ||
            rounds?.PropertyType != typeof(int) || pawn?.FieldType != typeof(Pawn))
            throw new InvalidOperationException("CE reload job, ammo and pawn signature changed");

        ammoUserProperty = comp;
        useAmmoProperty = useAmmo;
        magazineProperty = rounds;
        jobDriverPawnField = pawn;
        try
        {
            harmony.Patch(doReload,
                prefix: new HarmonyMethod(typeof(CombatExtendedReloadStatistics), nameof(BeforeDoReload)),
                postfix: new HarmonyMethod(typeof(CombatExtendedReloadStatistics), nameof(AfterDoReload)),
                finalizer: new HarmonyMethod(typeof(CombatExtendedReloadStatistics), nameof(FinishDoReload)));
            harmony.Patch(load,
                prefix: new HarmonyMethod(typeof(CombatExtendedReloadStatistics), nameof(BeforeLoadAmmo)),
                postfix: new HarmonyMethod(typeof(CombatExtendedReloadStatistics), nameof(AfterLoadAmmo)));
        }
        catch
        {
            harmony.Unpatch(doReload, HarmonyPatchType.All, harmony.Id);
            harmony.Unpatch(load, HarmonyPatchType.All, harmony.Id);
            ammoUserProperty = useAmmoProperty = magazineProperty = null;
            jobDriverPawnField = null;
            throw;
        }
    }

    private static bool CanObserve()
        => MetricGates.IsRecording(Unit)
           && Current.ProgramState == ProgramState.Playing
           && Scribe.mode == LoadSaveMode.Inactive
           && Find.TickManager is not null;

    private static int Rounds(object comp) => magazineProperty?.GetValue(comp) is int amount ? amount : 0;

    private static void BeforeDoReload(object __instance, out ReloadScope? __state)
    {
        __state = null;
        if (!CanObserve())
            return;
        try
        {
            if (jobDriverPawnField?.GetValue(__instance) is not Pawn pawn ||
                !GameFacts.IsMember(pawn) ||
                ammoUserProperty?.GetValue(__instance) is not { } comp ||
                useAmmoProperty?.GetValue(comp) is not true)
                return;
            __state = new ReloadScope { AmmoUser = comp, Parent = active };
            active = __state;
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterDoReload(bool __runOriginal, ReloadScope? __state)
    {
        if (!__runOriginal || __state is null || !ReferenceEquals(active, __state) || !CanObserve())
            return;
        try
        {
            if (__state.Counter.CountOnce())
                GameFacts.Record(Unit, s => OptionalTransitionRecording.RecordCount(s, "completed"));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static Exception? FinishDoReload(Exception? __exception, ReloadScope? __state)
    {
        if (__state is not null && ReferenceEquals(active, __state))
            active = __state.Parent;
        return __exception;
    }

    private static void BeforeLoadAmmo(object __instance, Thing __0, out AmmoBefore? __state)
    {
        __state = null;
        if (active is not { } scope || !ReferenceEquals(scope.AmmoUser, __instance) || !CanObserve())
            return;
        try
        {
            if (useAmmoProperty?.GetValue(__instance) is not true)
                return;
            __state = new AmmoBefore(scope, __0, __0?.stackCount ?? 0, Rounds(__instance));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterLoadAmmo(object __instance, bool __runOriginal, AmmoBefore? __state)
    {
        if (!__runOriginal || __state is null ||
            !ReferenceEquals(active, __state.Scope) || !CanObserve())
            return;
        try
        {
            var after = Rounds(__instance);
            bool loaded = __state.Ammo is { } ammo
                ? CombatExtendedReloadRules.AmmoAdded(__state.StackBefore,
                    ammo.stackCount, ammo.Destroyed, after)
                : after > __state.MagBefore;
            __state.Scope.Counter.Observe(loaded);
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }
}
