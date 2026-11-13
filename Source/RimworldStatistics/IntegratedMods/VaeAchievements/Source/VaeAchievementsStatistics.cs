using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.VaeAchievements;

/// <summary>
/// Successfully unlocked non-debug VAE achievement cards and points actually
/// credited during that UnlockCard call. CheckUnlocks during game setup is
/// excluded, even if the loader invokes it after reaching ProgramState.Playing.
/// </summary>
internal static class VaeAchievementsStatistics
{
    private const string Owner = "vanillaexpanded.achievements";
    private const string Unit = "mod.vanillaexpanded.achievements.achievementunlock";
    private const string Completed = Unit + ".completed";
    private const string Points = Unit + ".points_earned";

    private static FieldInfo? unlockedField;
    private static FieldInfo? definitionField;
    private static FieldInfo? totalPointsField;
    private static MethodInfo? getManagerMethod;
    [ThreadStatic] private static int initializationDepth;

    private sealed record BeforeUnlock(bool WasUnlocked, string? DefKey, int? EarnedBefore);

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        Build = output =>
        {
            OptionalTransitionRecording.PutCount(output, "completed", Completed, withBreakdown: true);
            var points = output.State.Peek("points");
            output.Put(Points, output.Value(integer: points?.N ?? 0,
                partial: points?.Overflow == true));
        },
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
    {
        static ModMetricDescriptor Definition(string name, bool breakdown, string label,
            string description) => new()
        {
            ProviderId = "rws.achievementunlock",
            OwnerPackageId = Owner,
            FamilyId = "achievementunlock",
            MetricName = name,
            Revision = 1,
            Source = MetricObservationSource.Checkpoint,
            Category = MetricCategory.Colony,
            Unit = MetricUnit.Count,
            Overall = OverallAggregation.Sum,
            Selection = ColonySelection.Representative,
            HasBreakdown = breakdown,
            RequiredPackages = [new ModPackageRequirement(Owner)],
            LabelKey = label,
            DescriptionKey = description,
        };
        return
        [
            new ModMetricAdapterRegistration(
                Definition("completed", true, "RWS_VaeAchievements_Unlocked",
                    "RWS_VaeAchievements_UnlockedDesc"), Family, Install),
            new ModMetricAdapterRegistration(
                Definition("points_earned", false, "RWS_VaeAchievements_Points",
                    "RWS_VaeAchievements_PointsDesc"), Family, Install),
        ];
    }

    private static void Install(Harmony harmony)
    {
        var card = AccessTools.TypeByName("AchievementsExpanded.AchievementCard");
        var manager = AccessTools.TypeByName("AchievementsExpanded.AchievementPointManager");
        if (card is null || manager is null ||
            !string.Equals(card.Assembly.GetName().Name, "AchievementsExpanded",
                StringComparison.OrdinalIgnoreCase) ||
            !ReferenceEquals(card.Assembly, manager.Assembly))
            throw new InvalidOperationException("Selected AchievementsExpanded assembly does not match 1.7 contract");

        var unlock = AccessTools.DeclaredMethod(card, "UnlockCard", [typeof(bool), typeof(Map)]);
        var setupScan = AccessTools.DeclaredMethod(manager, "CheckUnlocks", Type.EmptyTypes);
        var unlocked = AccessTools.DeclaredField(card, "unlocked");
        var definition = AccessTools.DeclaredField(card, "def");
        var points = AccessTools.DeclaredField(manager, "totalEarnedPoints");
        var getManager = typeof(Game).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(m => m.Name == "GetComponent" && m.IsGenericMethodDefinition
                && m.GetParameters().Length == 0)?.MakeGenericMethod(manager);

        if (unlock?.ReturnType != typeof(void) || setupScan?.ReturnType != typeof(void)
            || unlocked?.FieldType != typeof(bool)
            || definition is null || !typeof(Def).IsAssignableFrom(definition.FieldType)
            || points?.FieldType != typeof(int) || getManager is null)
            throw new InvalidOperationException("AchievementsExpanded unlock or point field signature changed");

        // Bind fields before publishing patches; every callback checks its unit gate.
        unlockedField = unlocked;
        definitionField = definition;
        totalPointsField = points;
        getManagerMethod = getManager;
        try
        {
            harmony.Patch(unlock,
                prefix: new HarmonyMethod(typeof(VaeAchievementsStatistics), nameof(BeforeCardUnlock)),
                postfix: new HarmonyMethod(typeof(VaeAchievementsStatistics), nameof(AfterCardUnlock)));
            harmony.Patch(setupScan,
                prefix: new HarmonyMethod(typeof(VaeAchievementsStatistics), nameof(BeforeStartupUnlockScan)),
                finalizer: new HarmonyMethod(typeof(VaeAchievementsStatistics), nameof(AfterStartupUnlockScan)));
        }
        catch
        {
            harmony.Unpatch(unlock, HarmonyPatchType.All, harmony.Id);
            harmony.Unpatch(setupScan, HarmonyPatchType.All, harmony.Id);
            unlockedField = definitionField = totalPointsField = null;
            getManagerMethod = null;
            throw;
        }
    }

    private static bool CanObserve()
        => MetricGates.IsRecording(Unit)
           && Current.ProgramState == ProgramState.Playing
           && Scribe.mode == LoadSaveMode.Inactive
           && Current.Game is not null
           && Find.TickManager is not null;

    private static int? TotalEarnedPoints()
    {
        if (Current.Game is null)
            return null;
        var component = getManagerMethod?.Invoke(Current.Game, null);
        return totalPointsField?.GetValue(component) is int value ? value : null;
    }

    private static void BeforeStartupUnlockScan() => initializationDepth++;

    private static Exception? AfterStartupUnlockScan(Exception? __exception)
    {
        // A Harmony finalizer unwinds the scope even when the original throws.
        if (initializationDepth > 0)
            initializationDepth--;
        return __exception;
    }

    private static void BeforeCardUnlock(object __instance, bool __0, out BeforeUnlock? __state)
    {
        __state = null;
        if (!CanObserve() || __0 || initializationDepth > 0)
            return;
        try
        {
            if (unlockedField?.GetValue(__instance) is not bool wasUnlocked || wasUnlocked)
                return;
            var def = definitionField?.GetValue(__instance) as Def;
            var defKey = OptionalTransitionRecording.QualifiedDefKey(
                def?.modContentPack?.PackageId, def?.defName);
            __state = new BeforeUnlock(wasUnlocked, defKey, TotalEarnedPoints());
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterCardUnlock(object __instance, bool __runOriginal, BeforeUnlock? __state)
    {
        if (!__runOriginal || __state is null || !CanObserve())
            return;
        try
        {
            if (unlockedField?.GetValue(__instance) is not bool nowUnlocked ||
                !VaeAchievementRules.NewUnlock(__state.WasUnlocked, nowUnlocked,
                    debug: false, initialization: initializationDepth > 0))
                return;

            var credited = VaeAchievementRules.CreditedPoints(__state.EarnedBefore, TotalEarnedPoints());
            GameFacts.Record(Unit, state =>
            {
                OptionalTransitionRecording.RecordCount(state, "completed", 1,
                    __state.DefKey, withBreakdown: true);
                if (credited is { } number)
                {
                    if (number > 0)
                        OptionalTransitionRecording.RecordCount(state, "points", number);
                }
                else
                    state.Acc("points").Overflow = true;
            });
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }
}
