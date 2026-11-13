using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.VfeTribals;

/// <summary>
/// VFE Tribals: successful forward era progress and newly acquired
/// cornerstones, not initialization, repeated clicks, or save hydration.
/// </summary>
internal static class VfeTribalsStatistics
{
    private const string Owner = "OskarPotocki.VFE.Tribals";
    private const string RequiredIdeology = "Ludeon.RimWorld.Ideology";
    private const string Unit = "mod.oskarpotocki.vfe.tribals.tribalprogress";
    private const string EraKey = Unit + ".era_advances";
    private const string CornerstoneKey = Unit + ".cornerstones";
    private static FieldInfo? levelField;
    private static FieldInfo? newTechLevelField;
    private static FieldInfo? cornerstonesField;
    private static FieldInfo? pointsField;

    private sealed record EraBefore(int TechLevel, int TargetLevel, string? Key);
    private sealed record CornerstoneBefore(string? Key, bool WasPresent);

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        RequiredDlc = RequiredIdeology,
        Build = output =>
        {
            OptionalTransitionRecording.PutCount(output, "eras", EraKey, withBreakdown: true);
            OptionalTransitionRecording.PutCount(output, "cornerstones", CornerstoneKey, withBreakdown: true);
        },
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
    {
        static ModMetricDescriptor Definition(string name, string label, string description)
            => new()
            {
                ProviderId = "rws.tribalprogress",
                OwnerPackageId = Owner,
                FamilyId = "tribalprogress",
                MetricName = name,
                Revision = 1,
                Source = MetricObservationSource.Checkpoint,
                Category = MetricCategory.Research,
                Unit = MetricUnit.Count,
                Overall = OverallAggregation.Sum,
                Selection = ColonySelection.Representative,
                HasBreakdown = true,
                RequiredDlc = RequiredIdeology,
                RequiredPackages = [new ModPackageRequirement(Owner)],
                LabelKey = label,
                DescriptionKey = description,
            };
        return
        [
            new ModMetricAdapterRegistration(Definition("era_advances",
                "RWS_VfeTribals_EraAdvances", "RWS_VfeTribals_EraAdvancesDesc"), Family, Install),
            new ModMetricAdapterRegistration(Definition("cornerstones",
                "RWS_VfeTribals_Cornerstones", "RWS_VfeTribals_CornerstonesDesc"), Family, Install),
        ];
    }

    private static void Install(Harmony harmony)
    {
        var owner = AccessTools.TypeByName("VFETribals.GameComponent_Tribals");
        var era = AccessTools.TypeByName("VFETribals.EraAdvancementDef");
        var cornerstone = AccessTools.TypeByName("VFETribals.CornerstoneDef");
        if (owner is null || era is null || cornerstone is null ||
            !string.Equals(owner.Assembly.GetName().Name, "VFETribals", StringComparison.OrdinalIgnoreCase) ||
            !ReferenceEquals(owner.Assembly, era.Assembly) ||
            !ReferenceEquals(owner.Assembly, cornerstone.Assembly) ||
            !typeof(GameComponent).IsAssignableFrom(owner) ||
            !typeof(Def).IsAssignableFrom(era) || !typeof(Def).IsAssignableFrom(cornerstone))
            throw new InvalidOperationException("Selected VFETribals assembly is absent or incompatible");

        var advance = AccessTools.DeclaredMethod(owner, "AdvanceToEra", [era]);
        var add = AccessTools.DeclaredMethod(owner, "AddCornerstone", [cornerstone]);
        var level = AccessTools.DeclaredField(owner, "playerTechLevel");
        var target = AccessTools.DeclaredField(era, "newTechLevel");
        var stones = AccessTools.DeclaredField(owner, "cornerstones");
        var points = AccessTools.DeclaredField(owner, "availableCornerstonePoints");
        if (advance?.ReturnType != typeof(void) || add?.ReturnType != typeof(void) ||
            level?.FieldType != typeof(TechLevel?) || target?.FieldType != typeof(TechLevel) ||
            stones is null || !typeof(IList).IsAssignableFrom(stones.FieldType) ||
            points?.FieldType != typeof(int))
            throw new InvalidOperationException("VFETribals progress fields or methods differ from the pinned source");

        levelField = level;
        newTechLevelField = target;
        cornerstonesField = stones;
        pointsField = points;
        try
        {
            harmony.Patch(advance,
                prefix: new HarmonyMethod(typeof(VfeTribalsStatistics), nameof(BeforeEra)),
                postfix: new HarmonyMethod(typeof(VfeTribalsStatistics), nameof(AfterEra)));
            harmony.Patch(add,
                prefix: new HarmonyMethod(typeof(VfeTribalsStatistics), nameof(BeforeCornerstone)),
                postfix: new HarmonyMethod(typeof(VfeTribalsStatistics), nameof(AfterCornerstone)));
        }
        catch
        {
            harmony.Unpatch(advance, HarmonyPatchType.All, harmony.Id);
            harmony.Unpatch(add, HarmonyPatchType.All, harmony.Id);
            levelField = newTechLevelField = cornerstonesField = pointsField = null;
            throw;
        }
    }

    private static bool CanObserve()
        => MetricGates.IsRecording(Unit)
           && Current.ProgramState == ProgramState.Playing
           && Scribe.mode == LoadSaveMode.Inactive
           && Find.TickManager is not null;

    private static void BeforeEra(object __instance, object __0, out EraBefore? __state)
    {
        __state = null;
        if (!CanObserve() || __0 is not Def era)
            return;
        try
        {
            if (levelField?.GetValue(__instance) is not TechLevel previous ||
                newTechLevelField?.GetValue(era) is not TechLevel target)
                return;
            __state = new EraBefore((int)previous, (int)target,
                OptionalTransitionRecording.QualifiedDefKey(era.modContentPack?.PackageId, era.defName));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterEra(object __instance, bool __runOriginal, EraBefore? __state)
    {
        if (!__runOriginal || __state is null || !CanObserve())
            return;
        try
        {
            if (levelField?.GetValue(__instance) is not TechLevel after ||
                !VfeTribalsRules.EraAdvanced(__state.TechLevel, (int)after, __state.TargetLevel))
                return;
            GameFacts.Record(Unit, state => OptionalTransitionRecording.RecordCount(
                state, "eras", key: __state.Key, withBreakdown: true));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void BeforeCornerstone(object __instance, object __0,
        out CornerstoneBefore? __state)
    {
        __state = null;
        if (!CanObserve() || __0 is not Def stone)
            return;
        try
        {
            if (pointsField?.GetValue(__instance) is not int { } points || points <= 0 ||
                cornerstonesField?.GetValue(__instance) is not IList current || current.Contains(stone))
                return;
            __state = new CornerstoneBefore(
                OptionalTransitionRecording.QualifiedDefKey(stone.modContentPack?.PackageId, stone.defName),
                WasPresent: false);
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterCornerstone(object __instance, object __0, bool __runOriginal,
        CornerstoneBefore? __state)
    {
        if (!__runOriginal || __state is null || !CanObserve())
            return;
        try
        {
            var present = cornerstonesField?.GetValue(__instance) is IList list && list.Contains(__0);
            if (!VfeTribalsRules.NewCornerstone(__state.WasPresent, present))
                return;
            GameFacts.Record(Unit, state => OptionalTransitionRecording.RecordCount(
                state, "cornerstones", key: __state.Key, withBreakdown: true));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }
}
