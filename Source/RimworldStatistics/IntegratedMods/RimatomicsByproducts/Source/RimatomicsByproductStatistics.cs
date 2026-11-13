using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.RimatomicsByproducts;

/// <summary>
/// Counts real by-product item placements in Rimatomics' completed plutonium
/// processor unloading. Source metadata is pinned to the selected 1.7 binary:
/// the job calls Building_PlutoniumProc.TakeOutFuel(), which makes exactly two
/// by-product placement calls before returning spent fuel. The job places the
/// returned spent fuel later, outside this scope.
/// </summary>
internal static class RimatomicsByproductStatistics
{
    private const string Owner = "Dubwise.Rimatomics";
    private const string Unit = "mod.dubwise.rimatomics.fuelreprocessing";
    private const string Key = Unit + ".byproduct_items";
    [ThreadStatic] private static ByproductScope? active;

    private sealed class ByproductScope
    {
        internal required Map Map { get; init; }
        internal required ByproductScope? Parent { get; init; }
        internal readonly Dictionary<ThingDef, long> Output = new();
    }

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        Build = output => OptionalTransitionRecording.PutCount(
            output, "byproducts", Key, withBreakdown: true),
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
        => [new ModMetricAdapterRegistration(new ModMetricDescriptor
        {
            ProviderId = "rws.fuelreprocessing",
            OwnerPackageId = Owner,
            FamilyId = "fuelreprocessing",
            MetricName = "byproduct_items",
            Source = MetricObservationSource.Checkpoint,
            Revision = 1,
            Category = MetricCategory.Work,
            Unit = MetricUnit.Count,
            Overall = OverallAggregation.Sum,
            Selection = ColonySelection.Representative,
            HasBreakdown = true,
            RequiredPackages = [new ModPackageRequirement(Owner)],
            LabelKey = "RWS_Rimatomics_Byproducts",
            DescriptionKey = "RWS_Rimatomics_ByproductsDesc",
        }, Family, Install)];

    private static void Install(Harmony harmony)
    {
        // Effective DLL is supplied as 1.7/Assemblies/Rimatomics.dll.
        // The author ships binaries rather than C# source; look up these exact
        // type names only inside the selected assembly, not across arbitrary mods.
        var assembly = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(a =>
            string.Equals(a.GetName().Name, "Rimatomics", StringComparison.OrdinalIgnoreCase));
        if (assembly is null)
            throw new InvalidOperationException("Rimatomics 1.7 assembly not loaded");

        var proc = assembly.GetTypes().SingleOrDefault(t => t.Name == "Building_PlutoniumProc");
        var job = assembly.GetTypes().SingleOrDefault(t => t.Name == "JobDriver_UnloadPlutonium");
        if (proc is null || job is null ||
            !typeof(Building).IsAssignableFrom(proc) ||
            !typeof(Verse.AI.JobDriver).IsAssignableFrom(job))
            throw new InvalidOperationException("Rimatomics fuel processor/unload types changed");
        var takeOut = AccessTools.DeclaredMethod(proc, "TakeOutFuel", Type.EmptyTypes);
        var place = AccessTools.DeclaredMethod(typeof(GenPlace), "TryPlaceThing",
            [typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode),
             typeof(Action<Thing, int>), typeof(Predicate<IntVec3>)]);
        if (takeOut is null || !typeof(Thing).IsAssignableFrom(takeOut.ReturnType) ||
            place?.ReturnType != typeof(bool))
            throw new InvalidOperationException("Rimatomics fuel output or vanilla placement signature changed");

        try
        {
            harmony.Patch(takeOut,
                prefix: new HarmonyMethod(typeof(RimatomicsByproductStatistics), nameof(BeforeUnload)),
                postfix: new HarmonyMethod(typeof(RimatomicsByproductStatistics), nameof(AfterUnload)),
                finalizer: new HarmonyMethod(typeof(RimatomicsByproductStatistics), nameof(FinishUnload)));
            harmony.Patch(place,
                prefix: new HarmonyMethod(typeof(RimatomicsByproductStatistics), nameof(BeforePlacement)));
        }
        catch
        {
            harmony.Unpatch(takeOut, HarmonyPatchType.All, harmony.Id);
            harmony.Unpatch(place, HarmonyPatchType.All, harmony.Id);
            throw;
        }
    }

    private static bool CanObserve()
        => MetricGates.IsRecording(Unit)
           && Current.ProgramState == ProgramState.Playing
           && Scribe.mode == LoadSaveMode.Inactive
           && Find.TickManager is not null;

    private static bool IsNormalUnload()
    {
        // This is a low-frequency action. Stack inspection is only at
        // TakeOutFuel completion, never inside per-tick or per-projectile hooks.
        foreach (var f in new StackTrace(1, false).GetFrames() ?? [])
            if (f.GetMethod() is { } m &&
                m.DeclaringType?.FullName?.Contains("JobDriver_UnloadPlutonium",
                    StringComparison.Ordinal) == true &&
                m.Name.Contains("<MakeNewToils>b__6_1", StringComparison.Ordinal))
                return true;
        return false;
    }

    private static void BeforeUnload(object __instance, out ByproductScope? __state)
    {
        __state = null;
        if (!CanObserve() || __instance is not Building { Spawned: true } building ||
            building.Faction != Faction.OfPlayer || building.Map is not { IsPlayerHome:true } map ||
            !IsNormalUnload())
            return;
        __state = new ByproductScope { Map = map, Parent = active };
        active = __state;
    }

    private static void BeforePlacement(Thing __0, Map __2, ref Action<Thing,int>? __4)
    {
        if (active is not { } scope || !CanObserve() ||
            !ReferenceEquals(__2, scope.Map) || __0?.def is not { } def)
            return;
        var original = __4;
        __4 = (thing, placed) =>
        {
            original?.Invoke(thing, placed);
            try
            {
                var count = RimatomicsByproductRules.Placed(placed,
                    playerOwned: true, processorMatch: true,
                    normalUnload: ReferenceEquals(active, scope));
                if (count <= 0)
                    return;
                scope.Output.TryGetValue(def, out var previous);
                scope.Output[def] = checked(previous + count);
            }
            catch (Exception e) { LiveMetrics.Fail(Unit, e); }
        };
    }

    private static void AfterUnload(Thing? __result, bool __runOriginal, ByproductScope? __state)
    {
        if (!__runOriginal || __result is null || __state is null ||
            !ReferenceEquals(active, __state) || !CanObserve() ||
            __state.Output.Count == 0)
            return;
        try
        {
            GameFacts.Record(Unit, state =>
            {
                foreach (var (def, count) in __state.Output)
                {
                    if (count <= 0)
                        continue;
                    var qualified = OptionalTransitionRecording.QualifiedDefKey(
                        def.modContentPack?.PackageId, def.defName);
                    OptionalTransitionRecording.RecordCount(state, "byproducts",
                        count, qualified, withBreakdown: true);
                }
            });
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static Exception? FinishUnload(Exception? __exception, ByproductScope? __state)
    {
        if (__state is not null && ReferenceEquals(active, __state))
            active = __state.Parent;
        return __exception;
    }
}
