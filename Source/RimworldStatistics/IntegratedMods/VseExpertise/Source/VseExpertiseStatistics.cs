using System.Collections;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using UnityEngine;
using Verse;

namespace RimworldStatistics.VseExpertise;

/// <summary>
/// VSE expertise selections, persistent expertise-level gains, and the
/// all-time max attained. Ordinary skill experience is tracked generically;
/// VSE expertise levels are distinct from vanilla skill levels.
/// </summary>
internal static class VseExpertiseStatistics
{
    private const string Owner = "vanillaexpanded.skills";
    private const string Unit = "mod.vanillaexpanded.skills.expertise";
    private const string SelectionKey = Unit + ".selected";
    private const string LevelUpKey = Unit + ".levelups";
    private const string HighestKey = Unit + ".highest_level";
    private static Func<object, IList?>? getAllExpertise;
    private static Func<object, Pawn?>? getTrackerPawn;
    private static Func<object, Def?>? getExpertiseDef;
    private static Func<object, int>? getExpertiseLevel;
    private static Func<object, Pawn?>? getExpertisePawn;
    [ThreadStatic] private static int expertisePanelDepth;

    private sealed record SelectionBefore(Def Definition, int Count, string? DefKey);
    private sealed record ProgressBefore(int Level, Pawn Pawn, string? DefKey);

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        Build = output =>
        {
            OptionalTransitionRecording.PutCount(output, "selected", SelectionKey, true);
            OptionalTransitionRecording.PutCount(output, "levelups", LevelUpKey, true);
            var peak = output.State.Peek("maxLevel")?.Max;
            output.Put(HighestKey, peak is { } amount
                ? output.Value(integer: (long)amount)
                : output.Value(coverage: MetricCoverage.NotApplicable));
        },
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
    {
        static ModMetricDescriptor D(string name, bool breakdown,
            OverallAggregation overall, string label, string description) => new()
        {
            ProviderId = "rws.expertise",
            OwnerPackageId = Owner,
            FamilyId = "expertise",
            MetricName = name,
            Source = MetricObservationSource.Checkpoint,
            Revision = 1,
            Category = MetricCategory.Work,
            Unit = MetricUnit.Count,
            Overall = overall,
            Selection = ColonySelection.Representative,
            HasBreakdown = breakdown,
            RequiredPackages = [new ModPackageRequirement(Owner)],
            LabelKey = label,
            DescriptionKey = description,
        };
        return
        [
            new ModMetricAdapterRegistration(D("selected", true, OverallAggregation.Sum,
                "RWS_VseExpertise_Selected", "RWS_VseExpertise_SelectedDesc"), Family, Install),
            new ModMetricAdapterRegistration(D("levelups", true, OverallAggregation.Sum,
                "RWS_VseExpertise_LevelUps", "RWS_VseExpertise_LevelUpsDesc"), Family, Install),
            new ModMetricAdapterRegistration(D("highest_level", false, OverallAggregation.Max,
                "RWS_VseExpertise_Highest", "RWS_VseExpertise_HighestDesc"), Family, Install),
        ];
    }

    private static void Install(Harmony harmony)
    {
        var tracker = AccessTools.TypeByName("VSE.ExpertiseTracker");
        var record = AccessTools.TypeByName("VSE.ExpertiseRecord");
        var def = AccessTools.TypeByName("VSE.Expertise.ExpertiseDef");
        var panel = AccessTools.TypeByName("VSE.ExpertiseUIUtility");
        if (tracker is null || record is null || def is null || panel is null ||
            !string.Equals(tracker.Assembly.GetName().Name, "VSE",
                StringComparison.OrdinalIgnoreCase) ||
            !ReferenceEquals(tracker.Assembly, record.Assembly) ||
            !ReferenceEquals(tracker.Assembly, def.Assembly) ||
            !ReferenceEquals(tracker.Assembly, panel.Assembly))
            throw new InvalidOperationException("VSE 1.7 expertise assembly not loaded");

        var add = AccessTools.DeclaredMethod(tracker, "AddExpertise", [def]);
        var learn = AccessTools.DeclaredMethod(record, "Learn", [typeof(float)]);
        var all = AccessTools.Property(tracker, "AllExpertise");
        var trackerPawn = AccessTools.Property(tracker, "Pawn");
        var rDef = AccessTools.DeclaredField(record, "def");
        var level = AccessTools.Property(record, "Level");
        var pawn = AccessTools.Property(record, "Pawn");
        var ui = AccessTools.DeclaredMethod(panel, "DoExpertisePanel", [typeof(Rect), typeof(Pawn)]);
        if (add?.ReturnType != typeof(void) || learn?.ReturnType != typeof(void) ||
            ui?.ReturnType != typeof(void) || all is null ||
            !typeof(IList).IsAssignableFrom(all.PropertyType) ||
            trackerPawn?.PropertyType != typeof(Pawn) ||
            rDef?.FieldType != def || level?.PropertyType != typeof(int) ||
            pawn?.PropertyType != typeof(Pawn))
            throw new InvalidOperationException("VSE expertise selection/learning signature mismatch");

        getAllExpertise = OptionalTransitionCallSite.CompilePropertyGetter<IList?>(all);
        getTrackerPawn = OptionalTransitionCallSite.CompilePropertyGetter<Pawn?>(trackerPawn);
        getExpertiseDef = OptionalTransitionCallSite.CompileFieldGetter<Def?>(rDef);
        getExpertiseLevel = OptionalTransitionCallSite.CompilePropertyGetter<int>(level);
        getExpertisePawn = OptionalTransitionCallSite.CompilePropertyGetter<Pawn?>(pawn);

        var patcher = new ScopedHarmonyPatcher(harmony);
        try
        {
            patcher.Patch(add, typeof(VseExpertiseStatistics), nameof(BeforeSelection), nameof(AfterSelection));
            patcher.Patch(learn, typeof(VseExpertiseStatistics), nameof(BeforeLearning), nameof(AfterLearning));
            patcher.Patch(ui, typeof(VseExpertiseStatistics), prefix: nameof(EnterPanel), finalizer: nameof(ExitPanel));
        }
        catch
        {
            getAllExpertise = null;
            getTrackerPawn = null;
            getExpertiseDef = null;
            getExpertiseLevel = null;
            getExpertisePawn = null;
            throw;
        }
    }

    private static bool CanObserve() => OptionalTransitionCallSite.Observing(Unit);

    private static void EnterPanel() => expertisePanelDepth++;

    private static Exception? ExitPanel(Exception? __exception)
    {
        if (expertisePanelDepth > 0)
            expertisePanelDepth--;
        return __exception;
    }

    private static IList? Expertise(object tracker) => getAllExpertise?.Invoke(tracker);

    private static bool ContainsDef(IList records, Def definition)
    {
        for (var i = 0; i < records.Count; i++)
        {
            var record = records[i];
            if (record is not null && ReferenceEquals(getExpertiseDef?.Invoke(record), definition))
                return true;
        }
        return false;
    }

    private static void BeforeSelection(object __instance, object __0,
        out SelectionBefore? __state)
    {
        __state = null;
        if (!CanObserve() || __0 is not Def def ||
            getTrackerPawn?.Invoke(__instance) is not { } owner ||
            !GameFacts.IsMember(owner) || expertisePanelDepth <= 0)
            return;
        try
        {
            var all = Expertise(__instance);
            if (all is null || ContainsDef(all, def))
                return;
            __state = new SelectionBefore(def, all.Count,
                OptionalTransitionRecording.QualifiedDefKey(
                    def.modContentPack?.PackageId, def.defName));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterSelection(object __instance, bool __runOriginal,
        SelectionBefore? __state)
    {
        if (!__runOriginal || __state is null || !CanObserve())
            return;
        try
        {
            var all = Expertise(__instance);
            if (all is null || !VseExpertiseRules.NewSelection(
                    __state.Count, all.Count, normalPanel: true) ||
                !ContainsDef(all, __state.Definition))
                return;
            GameFacts.Record(Unit, s => OptionalTransitionRecording.RecordCount(
                s, "selected", key: __state.DefKey, withBreakdown: true));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void BeforeLearning(object __instance, out ProgressBefore? __state)
    {
        __state = null;
        if (!CanObserve())
            return;
        try
        {
            if (getExpertisePawn?.Invoke(__instance) is not { } pawn ||
                !GameFacts.IsMember(pawn) ||
                getExpertiseLevel is null)
                return;
            var level = getExpertiseLevel(__instance);
            var def = getExpertiseDef?.Invoke(__instance);
            __state = new ProgressBefore(level, pawn,
                OptionalTransitionRecording.QualifiedDefKey(
                    def?.modContentPack?.PackageId, def?.defName));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterLearning(object __instance, bool __runOriginal, ProgressBefore? __state)
    {
        if (!__runOriginal || __state is null || !CanObserve())
            return;
        try
        {
            if (!GameFacts.IsMember(__state.Pawn) || getExpertiseLevel is null)
                return;
            var level = getExpertiseLevel(__instance);
            var gains = VseExpertiseRules.EarnedLevels(__state.Level, level);
            if (gains <= 0)
                return;
            GameFacts.Record(Unit, s =>
            {
                OptionalTransitionRecording.RecordCount(s, "levelups", gains,
                    key: __state.DefKey, withBreakdown: true);
                OptionalTransitionRecording.RecordReal(s, "maxLevel", level);
            });
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }
}
