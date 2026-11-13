using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.VaeAspirations;

/// <summary>
/// Vanilla Aspirations Expanded: count committed, newly completed aspirations
/// originating in normal CheckCompletion, never the debug Complete delegates.
/// </summary>
internal static class VaeAspirationsStatistics
{
    private const string Owner = "VanillaExpanded.VanillaAspirationsExpanded";
    private const string Unit = "mod.vanillaexpanded.vanillaaspirationsexpanded.personalaspiration";
    private const string Key = Unit + ".completed";
    private static FieldInfo? pawnField;
    private static FieldInfo? aspirationsField;
    private static MethodInfo? isComplete;
    [ThreadStatic] private static int normalCompletionScope;

    private sealed record BeforeCompletion(string? Key, bool WasComplete);

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        Build = output => OptionalTransitionRecording.PutCount(
            output, "completed", Key, withBreakdown: true),
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
        => [new ModMetricAdapterRegistration(new ModMetricDescriptor
        {
            ProviderId = "rws.personalaspiration",
            OwnerPackageId = Owner,
            FamilyId = "personalaspiration",
            MetricName = "completed",
            Revision = 1,
            Source = MetricObservationSource.Checkpoint,
            Category = MetricCategory.Mind,
            Unit = MetricUnit.Count,
            Overall = OverallAggregation.Sum,
            Selection = ColonySelection.Representative,
            HasBreakdown = true,
            RequiredPackages = [new ModPackageRequirement(Owner)],
            LabelKey = "RWS_VaeAspirations_Completed",
            DescriptionKey = "RWS_VaeAspirations_CompletedDesc",
        }, Family, Install)];

    private static void Install(Harmony harmony)
    {
        var need = AccessTools.TypeByName("VAspirE.Need_Fulfillment");
        var aspiration = AccessTools.TypeByName("VAspirE.AspirationDef");
        if (need is null || aspiration is null ||
            !typeof(Need).IsAssignableFrom(need) || !typeof(Def).IsAssignableFrom(aspiration) ||
            !string.Equals(need.Assembly.GetName().Name, "VAspirE", StringComparison.OrdinalIgnoreCase) ||
            !ReferenceEquals(need.Assembly, aspiration.Assembly))
            throw new InvalidOperationException("Selected Vanilla Aspirations 1.7 assembly is missing");

        var complete = AccessTools.DeclaredMethod(need, "Complete", [aspiration]);
        var check = AccessTools.DeclaredMethod(need, "CheckCompletion", [typeof(bool)]);
        var assigned = AccessTools.DeclaredField(need, "Aspirations");
        var pawn = AccessTools.Field(typeof(Need), "pawn");
        var completed = AccessTools.DeclaredMethod(need, "IsComplete", [aspiration]);
        if (complete?.ReturnType != typeof(void) || check?.ReturnType != typeof(void) ||
            completed?.ReturnType != typeof(bool) || assigned is null ||
            !typeof(IList).IsAssignableFrom(assigned.FieldType) || pawn?.FieldType != typeof(Pawn))
            throw new InvalidOperationException("VAspirE aspiration completion methods or fields changed");

        pawnField = pawn;
        aspirationsField = assigned;
        isComplete = completed;
        try
        {
            harmony.Patch(check,
                prefix: new HarmonyMethod(typeof(VaeAspirationsStatistics), nameof(BeforeNormalCheck)),
                finalizer: new HarmonyMethod(typeof(VaeAspirationsStatistics), nameof(AfterNormalCheck)));
            harmony.Patch(complete,
                prefix: new HarmonyMethod(typeof(VaeAspirationsStatistics), nameof(BeforeCompletion)),
                postfix: new HarmonyMethod(typeof(VaeAspirationsStatistics), nameof(AfterCompletion)));
        }
        catch
        {
            harmony.Unpatch(check, HarmonyPatchType.All, harmony.Id);
            harmony.Unpatch(complete, HarmonyPatchType.All, harmony.Id);
            pawnField = aspirationsField = null;
            isComplete = null;
            throw;
        }
    }

    private static bool CanObserve()
        => MetricGates.IsRecording(Unit)
           && Current.ProgramState == ProgramState.Playing
           && Scribe.mode == LoadSaveMode.Inactive
           && Find.TickManager is not null;

    private static void BeforeNormalCheck() => normalCompletionScope++;

    private static Exception? AfterNormalCheck(Exception? __exception)
    {
        if (normalCompletionScope > 0)
            normalCompletionScope--;
        return __exception;
    }

    private static bool ReadCompleted(object need, object def)
        => isComplete?.Invoke(need, [def]) is true;

    private static void BeforeCompletion(object __instance, object __0,
        out BeforeCompletion? __state)
    {
        __state = null;
        if (!CanObserve())
            return;
        try
        {
            var member = pawnField?.GetValue(__instance) as Pawn;
            var assigned = aspirationsField?.GetValue(__instance) as IList;
            var present = __0 is Def && assigned?.Contains(__0) == true;
            var before = present && ReadCompleted(__instance, __0);
            if (member is null || !GameFacts.IsMember(member) ||
                !VaeAspirationsRules.CanComplete(present, before,
                    normalCompletionScope > 0, debug: normalCompletionScope == 0))
                return;
            var def = (Def)__0;
            __state = new BeforeCompletion(OptionalTransitionRecording.QualifiedDefKey(
                def.modContentPack?.PackageId, def.defName), WasComplete: false);
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterCompletion(object __instance, object __0, bool __runOriginal,
        BeforeCompletion? __state)
    {
        if (!__runOriginal || __state is null || !CanObserve())
            return;
        try
        {
            if (!VaeAspirationsRules.Completed(__state.WasComplete,
                    ReadCompleted(__instance, __0)))
                return;
            GameFacts.Record(Unit, state => OptionalTransitionRecording.RecordCount(
                state, "completed", key: __state.Key, withBreakdown: true));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }
}
