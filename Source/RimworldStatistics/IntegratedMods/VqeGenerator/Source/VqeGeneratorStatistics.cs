using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.VqeGenerator;

internal static class VqeGeneratorStatistics
{
    private const string Owner = "vanillaquestsexpanded.generator";
    private const string Unit = "mod.vanillaquestsexpanded.generator.arcupgrades";
    private const string Key = Unit + ".completed";
    private static Type? genetronType;

    private sealed record UpgradeBefore(Map Map, IntVec3 Position, Building Original, ThingDef Target);

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        Build = output => OptionalTransitionRecording.PutCount(output, "upgrades", Key, withBreakdown: true),
    };

    internal static ModMetricAdapterRegistration Build() => new(new ModMetricDescriptor
    {
        ProviderId = "rws.genetronupgrades",
        OwnerPackageId = Owner,
        FamilyId = "arcupgrades",
        MetricName = "completed",
        Source = MetricObservationSource.Checkpoint,
        Revision = 1,
        Category = MetricCategory.Work,
        Unit = MetricUnit.Count,
        Overall = OverallAggregation.Sum,
        Selection = ColonySelection.Representative,
        HasBreakdown = true,
        RequiredPackages = [new ModPackageRequirement(Owner)],
        LabelKey = "RWS_VqeGenerator_Upgrades",
        DescriptionKey = "RWS_VqeGenerator_UpgradesDesc",
    }, Family, Install);

    private static void Install(Harmony harmony)
    {
        const string assemblyName = "VanillaQuestsExpandedTheGenerator";
        var type = AccessTools.TypeByName(assemblyName + ".Building_Genetron");
        var complete = AccessTools.DeclaredMethod(typeof(Frame), nameof(Frame.CompleteConstruction), [typeof(Pawn)]);
        if (type is null || !typeof(Building).IsAssignableFrom(type) ||
            !string.Equals(type.Assembly.GetName().Name, assemblyName, StringComparison.Ordinal) ||
            AccessTools.DeclaredMethod(type, "SpawnSetup", [typeof(Map), typeof(bool)])?.ReturnType != typeof(void) ||
            complete?.ReturnType != typeof(void))
            throw new InvalidOperationException("The Generator construction signature changed");

        genetronType = type;
        try
        {
            harmony.Patch(complete,
                prefix: new HarmonyMethod(typeof(VqeGeneratorStatistics), nameof(BeforeConstruction)),
                postfix: new HarmonyMethod(typeof(VqeGeneratorStatistics), nameof(AfterConstruction)));
        }
        catch
        {
            harmony.Unpatch(complete, HarmonyPatchType.All, harmony.Id);
            genetronType = null;
            throw;
        }
    }

    private static bool CanObserve()
        => MetricGates.IsRecording(Unit) && Current.ProgramState == ProgramState.Playing
            && Scribe.mode == LoadSaveMode.Inactive && Find.TickManager is not null;

    private static void BeforeConstruction(Frame __instance, Pawn __0, out UpgradeBefore? __state)
    {
        __state = null;
        if (__instance.def.entityDefToBuild is not ThingDef target ||
            !string.Equals(target.modContentPack?.PackageId, Owner, StringComparison.OrdinalIgnoreCase) ||
            target.thingClass is not { } targetType || genetronType is not { } type || !type.IsAssignableFrom(targetType) ||
            !CanObserve() || !GameFacts.IsPlayer(__0) || !GameFacts.IsPlayer(__instance) ||
            __instance.Map is not Map map)
            return;

        try
        {
            var position = __instance.Position;
            var candidates = map.thingGrid.ThingsListAt(position).OfType<Building>()
                .Where(b => b.Spawned && b.Position == position && type.IsInstanceOfType(b))
                .Take(2).ToArray();
            if (candidates.Length == 1 && candidates[0].def != target && GameFacts.IsPlayer(candidates[0]))
                __state = new UpgradeBefore(map, position, candidates[0], target);
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterConstruction(Frame __instance, bool __runOriginal, UpgradeBefore? __state)
    {
        if (!__runOriginal || __state is null || !CanObserve())
            return;
        try
        {
            var present = __state.Map.thingGrid.ThingsListAt(__state.Position).OfType<Building>()
                .Any(b => b.Spawned && b.Position == __state.Position && b.def == __state.Target &&
                          genetronType?.IsInstanceOfType(b) == true && GameFacts.IsPlayer(b));
            if (!VqeGeneratorRules.Upgraded(__instance.Destroyed, __state.Original.Destroyed,
                    present, __state.Original.def != __state.Target))
                return;
            var def = __state.Target;
            var key = OptionalTransitionRecording.QualifiedDefKey(def.modContentPack?.PackageId, def.defName);
            GameFacts.Record(Unit, state =>
                OptionalTransitionRecording.RecordCount(state, "upgrades", key: key, withBreakdown: true));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }
}
