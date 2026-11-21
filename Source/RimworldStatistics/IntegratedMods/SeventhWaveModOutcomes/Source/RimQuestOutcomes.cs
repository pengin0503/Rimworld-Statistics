using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.NewModOutcomes;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.SeventhWaveModOutcomes;

/// <summary>
/// Metrics for RimQuest (Continued) (Mlie.RimQuest, 1.7/Assemblies/RimQuest.dll):
/// Observes Dialog_QuestGiver.ExecuteSelectedQuest() when a player negotiator purchases
/// a quest or incident from a tracked quest-giver pawn (verified by removal of questPawn
/// from RimQuestTracker.Instance.questPawns), recording both purchased quests by Def
/// and the silver actually paid outside TradeDeal.
/// </summary>
internal static class RimQuestOutcomes
{
    private const string AssemblyName = "RimQuest";
    private const string QuestsPurchasedUnit = "mod.mlie.rimquest.quests_purchased";
    private const string QuestSilverSpentUnit = "mod.mlie.rimquest.quest_silver_spent";

    private static readonly object ContractLock = new();
    private static MethodInfo? executeSelectedQuestMethod;
    private static Func<object, object?> getTrackerInstance = null!;
    private static Func<object, IList?> getQuestPawns = null!;
    private static Func<object, object?> getDialogQuestPawn = null!;
    private static Func<object, Pawn?> getDialogInteractor = null!;
    private static Func<object, Def?> getDialogSelectedQuest = null!;
    private static Func<object, int> getDialogSilverCost = null!;

    private sealed record QuestPurchaseSnapshot(object QuestPawn, Def SelectedQuestDef, int SilverCost);

    internal static IEnumerable<ModMetricAdapterRegistration> Build()
    {
        yield return OutcomeMetric.Count(
            RimQuestRules.OwnerPackageId,
            "quests_purchased",
            "RWS_RimQuestQuestsPurchased",
            "RWS_RimQuestQuestsPurchasedDesc",
            MetricCategory.Exploration,
            InstallQuestsPurchased,
            breakdown: true);

        yield return OutcomeMetric.Count(
            RimQuestRules.OwnerPackageId,
            "quest_silver_spent",
            "RWS_RimQuestSilverSpent",
            "RWS_RimQuestSilverSpentDesc",
            MetricCategory.Economy,
            InstallQuestSilverSpent,
            unit: MetricUnit.Silver);
    }

    private static MethodInfo ResolveExecuteSelectedQuest()
    {
        lock (ContractLock)
        {
            if (executeSelectedQuestMethod is not null)
                return executeSelectedQuestMethod;

            var dialogType = OutcomeMetric.Bind("RimQuest.Dialog_QuestGiver", AssemblyName);
            var trackerType = OutcomeMetric.Bind("RimQuest.RimQuestTracker", AssemblyName);
            var questPawnType = OutcomeMetric.Bind("RimQuest.QuestPawn", AssemblyName);

            var trackerInstanceField = AccessTools.DeclaredField(trackerType, "Instance") is { IsStatic: true } tif &&
                tif.FieldType == trackerType
                ? tif
                : throw new InvalidOperationException("RimQuest RimQuestTracker.Instance field signature changed");
            var questPawnsField = AccessTools.DeclaredField(trackerType, "questPawns") is { } qpf &&
                qpf.FieldType == typeof(List<>).MakeGenericType(questPawnType)
                ? qpf
                : throw new InvalidOperationException("RimQuest RimQuestTracker.questPawns field signature changed");
            var dialogQuestPawnField = AccessTools.DeclaredField(dialogType, "questPawn") is { } dqp &&
                dqp.FieldType == questPawnType
                ? dqp
                : throw new InvalidOperationException("RimQuest Dialog_QuestGiver.questPawn field signature changed");
            var dialogInteractorField = AccessTools.DeclaredField(dialogType, "interactor") is { } dif &&
                dif.FieldType == typeof(Pawn)
                ? dif
                : throw new InvalidOperationException("RimQuest Dialog_QuestGiver.interactor field signature changed");
            var dialogSelectedQuestField = AccessTools.DeclaredField(dialogType, "selectedQuest") is { } dsq &&
                dsq.FieldType == typeof(object)
                ? dsq
                : throw new InvalidOperationException("RimQuest Dialog_QuestGiver.selectedQuest field signature changed");
            var dialogSilverCostField = AccessTools.DeclaredField(dialogType, "actualSilverCost") is { } dsc &&
                dsc.FieldType == typeof(int)
                ? dsc
                : throw new InvalidOperationException("RimQuest Dialog_QuestGiver.actualSilverCost field signature changed");

            var execute = OutcomeMetric.Method(dialogType, "ExecuteSelectedQuest", typeof(void));
            getTrackerInstance = OptionalTransitionCallSite.CompileFieldGetter<object?>(trackerInstanceField);
            getQuestPawns = OptionalTransitionCallSite.CompileFieldGetter<IList?>(questPawnsField);
            getDialogQuestPawn = OptionalTransitionCallSite.CompileFieldGetter<object?>(dialogQuestPawnField);
            getDialogInteractor = OptionalTransitionCallSite.CompileFieldGetter<Pawn?>(dialogInteractorField);
            getDialogSelectedQuest = OptionalTransitionCallSite.CompileFieldGetter<Def?>(dialogSelectedQuestField);
            getDialogSilverCost = OptionalTransitionCallSite.CompileFieldGetter<int>(dialogSilverCostField);
            executeSelectedQuestMethod = execute;
            return execute;
        }
    }

    private static bool IsQuestPawnTracked(object questPawn) =>
        getTrackerInstance(null!) is { } tracker &&
        getQuestPawns(tracker) is { } list &&
        list.Contains(questPawn);

    private static QuestPurchaseSnapshot? CaptureBefore(Window dialog)
    {
        if (getDialogQuestPawn(dialog) is not { } questPawn ||
            getDialogInteractor(dialog) is not { } interactor ||
            getDialogSelectedQuest(dialog) is not { } questDef)
            return null;
        var silverCost = getDialogSilverCost(dialog);

        return RimQuestRules.IsEligiblePurchaseBefore(
            isPlayerInteractor: GameFacts.IsPlayer(interactor),
            hasSelectedQuestDef: true,
            isQuestPawnTrackedBefore: IsQuestPawnTracked(questPawn))
            ? new QuestPurchaseSnapshot(questPawn, questDef, silverCost)
            : null;
    }

    private static void InstallQuestsPurchased(Harmony harmony)
    {
        var execute = ResolveExecuteSelectedQuest();
        OutcomeMetric.Patch(harmony, execute, typeof(RimQuestOutcomes), nameof(BeforeQuestPurchase), nameof(AfterQuestPurchase));
    }

    private static void InstallQuestSilverSpent(Harmony harmony)
    {
        var execute = ResolveExecuteSelectedQuest();
        OutcomeMetric.Patch(harmony, execute, typeof(RimQuestOutcomes), nameof(BeforeQuestSilver), nameof(AfterQuestSilver));
    }

    private static void BeforeQuestPurchase(Window __instance, out QuestPurchaseSnapshot? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(QuestsPurchasedUnit) || __instance is null)
            return;

        try { __state = CaptureBefore(__instance); }
        catch (Exception error)
        {
            __state = null;
            LiveMetrics.Fail(QuestsPurchasedUnit, error);
        }
    }

    private static void AfterQuestPurchase(bool __runOriginal, QuestPurchaseSnapshot? __state)
    {
        if (!__runOriginal || __state is null || !OutcomeMetric.Observing(QuestsPurchasedUnit))
            return;

        try
        {
            var purchased = RimQuestRules.ShouldRecordPurchase(
                wasEligibleBefore: true,
                isQuestPawnTrackedAfter: IsQuestPawnTracked(__state.QuestPawn));
            var key = RimQuestRules.ResolvePurchasedQuestKey(
                purchased,
                __state.SelectedQuestDef.modContentPack?.PackageId,
                __state.SelectedQuestDef.defName);
            if (key is not null)
            {
                GameFacts.Record(QuestsPurchasedUnit, state => OptionalTransitionRecording.RecordCount(
                    state, "completed", 1, key: key, withBreakdown: true));
            }
        }
        catch (Exception error)
        {
            LiveMetrics.Fail(QuestsPurchasedUnit, error);
        }
    }

    private static void BeforeQuestSilver(Window __instance, out QuestPurchaseSnapshot? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(QuestSilverSpentUnit) || __instance is null)
            return;

        try { __state = CaptureBefore(__instance); }
        catch (Exception error)
        {
            __state = null;
            LiveMetrics.Fail(QuestSilverSpentUnit, error);
        }
    }

    private static void AfterQuestSilver(bool __runOriginal, QuestPurchaseSnapshot? __state)
    {
        if (!__runOriginal || __state is null || !OutcomeMetric.Observing(QuestSilverSpentUnit))
            return;

        try
        {
            var purchased = RimQuestRules.ShouldRecordPurchase(
                wasEligibleBefore: true,
                isQuestPawnTrackedAfter: IsQuestPawnTracked(__state.QuestPawn));
            var silver = RimQuestRules.AcceptedSilverSpent(purchased, __state.SilverCost);
            if (silver > 0)
                OutcomeMetric.Record(QuestSilverSpentUnit, amount: silver);
        }
        catch (Exception error)
        {
            LiveMetrics.Fail(QuestSilverSpentUnit, error);
        }
    }
}
