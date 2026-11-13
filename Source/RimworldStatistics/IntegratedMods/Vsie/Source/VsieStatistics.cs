using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.Vsie;

/// <summary>
/// Vanilla Social Interactions Expanded: count an aspiration only once when
/// the matching inspiration reaches its required progress and the same entry
/// is removed from the manager's active state by a successful callback.
/// </summary>
internal static class VsieStatistics
{
    private const string Owner = "VanillaExpanded.VanillaSocialInteractionsExpanded";
    private const string Unit = "mod.vanillaexpanded.vanillasocialinteractionsexpanded.inspirationaspiration";
    private const string Key = Unit + ".completed";
    private static FieldInfo? activeAspirationsField;
    private static FieldInfo? countField;
    private static FieldInfo? finalCountField;
    private static FieldInfo? inspirationField;

    private sealed record AspirationBefore(object Entry, int Count, int Threshold, string? DefKey);

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
            ProviderId = "rws.inspirationaspiration",
            OwnerPackageId = Owner,
            FamilyId = "inspirationaspiration",
            MetricName = "completed",
            Revision = 1,
            Source = MetricObservationSource.Checkpoint,
            Category = MetricCategory.Mind,
            Unit = MetricUnit.Count,
            Overall = OverallAggregation.Sum,
            Selection = ColonySelection.Representative,
            HasBreakdown = true,
            RequiredPackages = [new ModPackageRequirement(Owner)],
            LabelKey = "RWS_Vsie_AspirationsCompleted",
            DescriptionKey = "RWS_Vsie_AspirationsCompletedDesc",
        }, Family, Install)];

    private static void Install(Harmony harmony)
    {
        var manager = AccessTools.TypeByName("VanillaSocialInteractionsExpanded.SocialInteractionsManager");
        var aspiration = AccessTools.TypeByName("VanillaSocialInteractionsExpanded.Aspiration");
        if (manager is null || aspiration is null ||
            !string.Equals(manager.Assembly.GetName().Name, "VanillaSocialInteractionsExpanded",
                StringComparison.OrdinalIgnoreCase) ||
            !ReferenceEquals(manager.Assembly, aspiration.Assembly))
            throw new InvalidOperationException("VSIE 1.7 manager assembly missing");

        var notify = AccessTools.DeclaredMethod(manager, "Notify_AspirationProgress", [typeof(Pawn)]);
        var active = AccessTools.DeclaredField(manager, "activeAspirations");
        var count = AccessTools.DeclaredField(aspiration, "count");
        var threshold = AccessTools.DeclaredField(aspiration, "finalCount");
        var inspiration = AccessTools.DeclaredField(aspiration, "inspirationDef");
        if (notify?.ReturnType != typeof(void) || active is null ||
            !typeof(IDictionary).IsAssignableFrom(active.FieldType) ||
            count?.FieldType != typeof(int) || threshold?.FieldType != typeof(int) ||
            inspiration?.FieldType != typeof(InspirationDef))
            throw new InvalidOperationException("VSIE aspiration transition signature changed");

        activeAspirationsField = active;
        countField = count;
        finalCountField = threshold;
        inspirationField = inspiration;
        try
        {
            harmony.Patch(notify,
                prefix: new HarmonyMethod(typeof(VsieStatistics), nameof(BeforeProgress)),
                postfix: new HarmonyMethod(typeof(VsieStatistics), nameof(AfterProgress)));
        }
        catch
        {
            harmony.Unpatch(notify, HarmonyPatchType.All, harmony.Id);
            activeAspirationsField = countField = finalCountField = inspirationField = null;
            throw;
        }
    }

    private static bool CanObserve()
        => MetricGates.IsRecording(Unit)
           && Current.ProgramState == ProgramState.Playing
           && Scribe.mode == LoadSaveMode.Inactive
           && Find.TickManager is not null;

    private static void BeforeProgress(object __instance, Pawn __0,
        out AspirationBefore? __state)
    {
        __state = null;
        if (!CanObserve() || __0 is null || !GameFacts.IsMember(__0))
            return;
        try
        {
            var active = activeAspirationsField?.GetValue(__instance) as IDictionary;
            if (active is null || !active.Contains(__0) || active[__0] is not { } aspiration)
                return;
            if (inspirationField?.GetValue(aspiration) is not InspirationDef inspiration ||
                !ReferenceEquals(inspiration, __0.InspirationDef) ||
                countField?.GetValue(aspiration) is not int before ||
                finalCountField?.GetValue(aspiration) is not int required || required <= 0)
                return;
            __state = new AspirationBefore(aspiration, before, required,
                OptionalTransitionRecording.QualifiedDefKey(
                    inspiration.modContentPack?.PackageId, inspiration.defName));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterProgress(object __instance, Pawn __0, bool __runOriginal,
        AspirationBefore? __state)
    {
        if (!__runOriginal || __state is null || !CanObserve())
            return;
        try
        {
            var active = activeAspirationsField?.GetValue(__instance) as IDictionary;
            var removed = active is not null && !ReferenceEquals(active[__0], __state.Entry);
            if (countField?.GetValue(__state.Entry) is not int after ||
                !VsieAspirationRules.Completed(__state.Count, after, __state.Threshold,
                    removed, inspirationMatches: true))
                return;
            GameFacts.Record(Unit, s => OptionalTransitionRecording.RecordCount(
                s, "completed", key: __state.DefKey, withBreakdown: true));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }
}
