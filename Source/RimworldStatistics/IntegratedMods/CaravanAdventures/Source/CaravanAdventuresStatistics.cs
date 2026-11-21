using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.NewModOutcomes;
using Verse;

namespace RimworldStatistics.CaravanAdventures;

/// <summary>
/// Caravan Adventures:
/// 1) Counts mechanoid bounty credits actually earned by killing hostile mechanoids via
///    StoryUtility.AddBountyPointsForKilledMech, excluding credit purchases/debug additions.
/// 2) Counts ancient master shrine and final judgment completions committed via
///    AncientMasterShrineMP.CheckBossDefeated and LastJudgmentMP.CheckBossDefeated (Royalty DLC).
/// </summary>
internal static class CaravanAdventuresStatistics
{
    private const string AssemblyName = "CaravanAdventures";
    private const string RoyaltyPackageId = "ludeon.rimworld.royalty";
    private const string BountyCreditsUnit = "mod.iforgotmysocks.caravanadventures.bounty_credits";
    private const string ShrinesCompletedUnit = "mod.iforgotmysocks.caravanadventures.shrines_completed";

    private static Func<object, object?>? getBountyWc;
    private static Func<object, float>? getBountyPoints;
    private static Func<object, object?>? getStoryWc;
    private static Func<object, int>? getCountShrinesCompleted;

    private sealed record BountyAttempt(object BountyWc, float BeforePoints, ThingDef MechDef);
    private sealed record ShrineAttempt(object StoryWc, int BeforeCount);

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build() =>
    [
        OutcomeMetric.Count(
            owner: CaravanAdventuresRules.OwnerPackageId,
            family: "bounty_credits",
            label: "RWS_CaravanAdventures_BountyCredits",
            description: "RWS_CaravanAdventures_BountyCreditsDesc",
            category: MetricCategory.Combat,
            install: InstallBountyCredits,
            breakdown: true),
        OutcomeMetric.Count(
            owner: CaravanAdventuresRules.OwnerPackageId,
            family: "shrines_completed",
            label: "RWS_CaravanAdventures_ShrinesCompleted",
            description: "RWS_CaravanAdventures_ShrinesCompletedDesc",
            category: MetricCategory.Exploration,
            install: InstallShrinesCompleted,
            breakdown: false,
            dlc: RoyaltyPackageId),
    ];

    private static void InstallBountyCredits(Harmony harmony)
    {
        var utilityType = OutcomeMetric.Bind("CaravanAdventures.CaravanStory.StoryUtility", AssemblyName);
        var compCacheType = OutcomeMetric.Bind("CaravanAdventures.CompCache", AssemblyName);
        var bountyWcType = OutcomeMetric.Bind("CaravanAdventures.CaravanMechBounty.BountyWC", AssemblyName);

        var wcGetter = AccessTools.PropertyGetter(compCacheType, "BountyWC");
        var pointsGetter = AccessTools.PropertyGetter(bountyWcType, "BountyPoints");
        if (wcGetter is null || !wcGetter.IsStatic || wcGetter.ReturnType != bountyWcType ||
            pointsGetter?.ReturnType != typeof(float))
            throw new InvalidOperationException("CaravanAdventures BountyWC contract changed");

        var addBounty = OutcomeMetric.Method(utilityType, "AddBountyPointsForKilledMech", typeof(void), typeof(Pawn));

        getBountyWc = OptionalTransitionCallSite.CompileGetter<object?>(wcGetter);
        getBountyPoints = OptionalTransitionCallSite.CompileGetter<float>(pointsGetter);
        var patcher = new ScopedHarmonyPatcher(harmony);
        try
        {
            patcher.Patch(addBounty, typeof(CaravanAdventuresStatistics), nameof(BeforeBounty), nameof(AfterBounty));
        }
        catch
        {
            getBountyWc = null;
            getBountyPoints = null;
            throw;
        }
    }

    private static void InstallShrinesCompleted(Harmony harmony)
    {
        var shrineMpType = OutcomeMetric.Bind("CaravanAdventures.CaravanStory.AncientMasterShrineMP", AssemblyName);
        var judgmentMpType = OutcomeMetric.Bind("CaravanAdventures.CaravanStory.LastJudgmentMP", AssemblyName);
        var compCacheType = OutcomeMetric.Bind("CaravanAdventures.CompCache", AssemblyName);
        var storyWcType = OutcomeMetric.Bind("CaravanAdventures.CaravanStory.StoryWC", AssemblyName);

        var wcGetter = AccessTools.PropertyGetter(compCacheType, "StoryWC");
        var counterField = AccessTools.DeclaredField(storyWcType, "countShrinesCompleted");
        if (wcGetter is null || !wcGetter.IsStatic || wcGetter.ReturnType != storyWcType ||
            counterField?.FieldType != typeof(int))
            throw new InvalidOperationException("CaravanAdventures StoryWC shrine counter contract changed");

        var checkShrine = OutcomeMetric.Method(shrineMpType, "CheckBossDefeated", typeof(void));
        var checkJudgment = OutcomeMetric.Method(judgmentMpType, "CheckBossDefeated", typeof(void));

        getStoryWc = OptionalTransitionCallSite.CompileGetter<object?>(wcGetter);
        getCountShrinesCompleted = OptionalTransitionCallSite.CompileFieldGetter<int>(counterField);

        var patcher = new ScopedHarmonyPatcher(harmony);
        try
        {
            patcher.Patch(checkShrine, typeof(CaravanAdventuresStatistics), nameof(BeforeShrineCheck), nameof(AfterShrineCheck));
            patcher.Patch(checkJudgment, typeof(CaravanAdventuresStatistics), nameof(BeforeShrineCheck), nameof(AfterShrineCheck));
        }
        catch
        {
            getStoryWc = null;
            getCountShrinesCompleted = null;
            throw;
        }
    }

    private static void BeforeBounty(Pawn? __0, out BountyAttempt? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(BountyCreditsUnit) ||
            __0 is null || __0.def is null ||
            getBountyWc is null || getBountyPoints is null)
            return;

        try
        {
            var player = Faction.OfPlayerSilentFail;
            if (player is not null && __0.Faction == player)
                return;

            var bountyWc = getBountyWc(null!);
            if (bountyWc is null)
                return;

            var before = getBountyPoints(bountyWc);
            __state = new BountyAttempt(bountyWc, before, __0.def);
        }
        catch (Exception e)
        {
            __state = null;
            LiveMetrics.Fail(BountyCreditsUnit, e);
        }
    }

    private static void AfterBounty(bool __runOriginal, BountyAttempt? __state)
    {
        if (!__runOriginal || __state is null || !OutcomeMetric.Observing(BountyCreditsUnit) || getBountyPoints is null)
            return;

        try
        {
            var after = getBountyPoints(__state.BountyWc);
            var (delta, key) = CaravanAdventuresRules.ResolveBountyCreditReward(
                mechValid: true,
                targetIsNonPlayer: true,
                beforePoints: __state.BeforePoints,
                afterPoints: after,
                defPackageId: __state.MechDef.modContentPack?.PackageId,
                defName: __state.MechDef.defName);

            if (delta > 0 && key is not null)
                OutcomeMetric.Record(BountyCreditsUnit, __state.MechDef, breakdown: true, amount: delta);
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(BountyCreditsUnit, e);
        }
    }

    private static void BeforeShrineCheck(out ShrineAttempt? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(ShrinesCompletedUnit) ||
            getStoryWc is null || getCountShrinesCompleted is null)
            return;

        try
        {
            var storyWc = getStoryWc(null!);
            if (storyWc is null)
                return;

            var before = getCountShrinesCompleted(storyWc);
            __state = new ShrineAttempt(storyWc, before);
        }
        catch (Exception e)
        {
            __state = null;
            LiveMetrics.Fail(ShrinesCompletedUnit, e);
        }
    }

    private static void AfterShrineCheck(bool __runOriginal, ShrineAttempt? __state)
    {
        if (!__runOriginal || __state is null || !OutcomeMetric.Observing(ShrinesCompletedUnit) ||
            getCountShrinesCompleted is null)
            return;

        try
        {
            var after = getCountShrinesCompleted(__state.StoryWc);
            if (CaravanAdventuresRules.ResolveShrineCompletion(worldCompValid: true, __state.BeforeCount, after))
                OutcomeMetric.Record(ShrinesCompletedUnit);
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(ShrinesCompletedUnit, e);
        }
    }
}
