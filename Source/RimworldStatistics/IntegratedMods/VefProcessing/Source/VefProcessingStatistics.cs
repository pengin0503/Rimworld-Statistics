using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.VefProcessing;

/// <summary>
/// Single VEF PipeSystem completed-output hook, with two independent owners.
/// It counts the ProcessDef batch only when the processCount increment proves
/// all recipe outputs were committed, not a paused/blocked/partial spawn.
/// </summary>
internal static class VefProcessingStatistics
{
    private const string VefOwner = "OskarPotocki.VanillaFactionsExpanded.Core";
    private const string CookingOwner = "VanillaExpanded.VCookE";
    private const string BrewingOwner = "VanillaExpanded.VBrewE";
    private static FieldInfo? parentField;
    private static FieldInfo? definitionField;
    private static FieldInfo? processCountField;
    private static readonly object InstallGate = new();
    private static bool installed;

    private sealed record BatchBefore(string Unit, int CountBefore, string? DefKey);

    private static readonly LiveFamily Cooking = new()
    {
        Name = VefProcessingRules.CookingUnit,
        Revision = 1,
        Build = output => OptionalTransitionRecording.PutCount(
            output, "batches", VefProcessingRules.CookingUnit + ".batches", withBreakdown: true),
    };

    private static readonly LiveFamily Brewing = new()
    {
        Name = VefProcessingRules.BrewingUnit,
        Revision = 1,
        Build = output => OptionalTransitionRecording.PutCount(
            output, "batches", VefProcessingRules.BrewingUnit + ".batches", withBreakdown: true),
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
    {
        static ModMetricDescriptor Descriptor(string owner, string family,
            string label, string desc) => new()
        {
            ProviderId = family == "cookingprocess" ? "rws.cookingprocess" : "rws.brewingprocess",
            OwnerPackageId = owner,
            FamilyId = family,
            MetricName = "batches",
            Source = MetricObservationSource.Checkpoint,
            Revision = 1,
            Category = MetricCategory.Work,
            Unit = MetricUnit.Count,
            Overall = OverallAggregation.Sum,
            Selection = ColonySelection.Representative,
            HasBreakdown = true,
            RequiredPackages = [new ModPackageRequirement(owner), new ModPackageRequirement(VefOwner)],
            LabelKey = label,
            DescriptionKey = desc,
        };
        return
        [
            new ModMetricAdapterRegistration(
                Descriptor(CookingOwner, "cookingprocess", "RWS_VefCooking_Batches",
                    "RWS_VefCooking_BatchesDesc"), Cooking, Install),
            new ModMetricAdapterRegistration(
                Descriptor(BrewingOwner, "brewingprocess", "RWS_VefBrewing_Batches",
                    "RWS_VefBrewing_BatchesDesc"), Brewing, Install),
        ];
    }

    private static void Install(Harmony harmony)
    {
        lock (InstallGate)
        {
            if (installed)
                return;

            var type = AccessTools.TypeByName("PipeSystem.Process");
            if (type is null || !string.Equals(type.Assembly.GetName().Name,
                "PipeSystem", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Selected 1.7 VEF PipeSystem assembly unavailable");

            var method = AccessTools.DeclaredMethod(type, "SpawnOrPushToNet",
                [typeof(IntVec3), typeof(List<Thing>).MakeByRefType(), typeof(Pawn)]);
            var parent = AccessTools.DeclaredField(type, "parent");
            var def = AccessTools.DeclaredField(type, "def");
            var count = AccessTools.DeclaredField(type, "processCount");
            if (method?.ReturnType != typeof(void) ||
                parent is null || !typeof(ThingWithComps).IsAssignableFrom(parent.FieldType) ||
                def is null || !typeof(Def).IsAssignableFrom(def.FieldType) ||
                count?.FieldType != typeof(int))
                throw new InvalidOperationException("VEF Process completed batch signature changed");

            parentField = parent;
            definitionField = def;
            processCountField = count;
            try
            {
                harmony.Patch(method,
                    prefix: new HarmonyMethod(typeof(VefProcessingStatistics), nameof(BeforeProcess)),
                    postfix: new HarmonyMethod(typeof(VefProcessingStatistics), nameof(AfterProcess)));
                installed = true;
            }
            catch
            {
                harmony.Unpatch(method, HarmonyPatchType.All, harmony.Id);
                parentField = definitionField = processCountField = null;
                throw;
            }
        }
    }

    private static bool CanObserve()
        => Current.ProgramState == ProgramState.Playing
           && Scribe.mode == LoadSaveMode.Inactive
           && Find.TickManager is not null
           && (MetricGates.IsRecording(VefProcessingRules.CookingUnit) ||
               MetricGates.IsRecording(VefProcessingRules.BrewingUnit));

    private static void BeforeProcess(object __instance, out BatchBefore? __state)
    {
        __state = null;
        if (!CanObserve())
            return;
        try
        {
            if (parentField?.GetValue(__instance) is not ThingWithComps machine ||
                !machine.Spawned || machine.Faction != Faction.OfPlayer ||
                definitionField?.GetValue(__instance) is not Def process ||
                processCountField?.GetValue(__instance) is not int count)
                return;
            // ProcessDefs are shipped by Cooking/Brewing, while the processor
            // building may be defined in the common VEF framework. Matching
            // the machine's own package to the recipe owner would silently
            // discard valid completed batches on shared processors.
            var processOwner = process.modContentPack?.PackageId;
            if (string.IsNullOrWhiteSpace(processOwner))
                return;
            var unit = string.Equals(processOwner, CookingOwner, StringComparison.OrdinalIgnoreCase)
                ? VefProcessingRules.CookingUnit
                : string.Equals(processOwner, BrewingOwner, StringComparison.OrdinalIgnoreCase)
                    ? VefProcessingRules.BrewingUnit : null;
            if (unit is null || !MetricGates.IsRecording(unit))
                return;
            var key = OptionalTransitionRecording.QualifiedDefKey(processOwner, process.defName);
            __state = new BatchBefore(unit, count, key);
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(VefProcessingRules.CookingUnit, e);
            LiveMetrics.Fail(VefProcessingRules.BrewingUnit, e);
        }
    }

    private static void AfterProcess(object __instance, bool __runOriginal, BatchBefore? __state)
    {
        if (!__runOriginal || __state is null ||
            !MetricGates.IsRecording(__state.Unit) ||
            Current.ProgramState != ProgramState.Playing ||
            Scribe.mode != LoadSaveMode.Inactive)
            return;
        try
        {
            if (processCountField?.GetValue(__instance) is not int after ||
                !VefProcessingRules.CompleteBatch(__state.CountBefore, after,
                    ownerMatches: true, player: true))
                return;
            GameFacts.Record(__state.Unit, s => OptionalTransitionRecording.RecordCount(
                s, "batches", key: __state.DefKey, withBreakdown: true));
        }
        catch (Exception e) { LiveMetrics.Fail(__state.Unit, e); }
    }
}
