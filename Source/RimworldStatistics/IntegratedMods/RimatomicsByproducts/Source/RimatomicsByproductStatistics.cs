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
    [ThreadStatic] private static int normalUnloadDepth;

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
        var place = OptionalTransitionCallSite.ResolveGenPlaceTryPlaceThing();
        if (takeOut is null || !typeof(Thing).IsAssignableFrom(takeOut.ReturnType))
            throw new InvalidOperationException("Rimatomics fuel output signature changed");

        var patcher = new ScopedHarmonyPatcher(harmony);
        patcher.Patch(takeOut, typeof(RimatomicsByproductStatistics),
            prefix: nameof(BeforeUnload), postfix: nameof(AfterUnload), finalizer: nameof(FinishUnload));
        patcher.Patch(place, typeof(RimatomicsByproductStatistics), prefix: nameof(BeforePlacement));
        var unloadCallback = OptionalTransitionCallSite.FindDeclaredMethodInHierarchyByContains(job, "<MakeNewToils>b__6_1");
        if (unloadCallback is not null)
            patcher.Patch(unloadCallback, typeof(RimatomicsByproductStatistics), prefix: nameof(EnterUnload), finalizer: nameof(ExitUnload));
    }

    private static bool CanObserve() => OptionalTransitionCallSite.Observing(Unit);

    private static void EnterUnload() => normalUnloadDepth++;

    private static Exception? ExitUnload(Exception? __exception)
    {
        if (normalUnloadDepth > 0)
            normalUnloadDepth--;
        return __exception;
    }

    private static void BeforeUnload(object __instance, out ByproductScope? __state)
    {
        __state = null;
        if (!CanObserve() || __instance is not Building { Spawned: true } building ||
            !GameFacts.IsPlayerFaction(building.Faction) || building.Map is not { IsPlayerHome: true } map ||
            normalUnloadDepth <= 0)
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
