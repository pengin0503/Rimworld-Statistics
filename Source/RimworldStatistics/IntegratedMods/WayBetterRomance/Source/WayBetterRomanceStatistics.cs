using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.NewModOutcomes;
using RimworldStatistics.Patches.Metrics;
using Verse;
using Verse.AI;

namespace RimworldStatistics.WayBetterRomance;

/// <summary>
/// Way Better Romance: counts dates and hangouts completed by player pawns when
/// BetterRomance.JobDriver_JobDateLead reaches its finish toil with a living
/// partner still executing the matching follow JobDef. Cancelled/rebuffed dates
/// and failed starts never trigger the finish toil or partner check.
/// </summary>
internal static class WayBetterRomanceStatistics
{
    private const string AssemblyName = "WayBetterRomance";
    private const string Unit = "mod.divinederivative.romance.dates";

    private static MethodInfo? actorGetter;
    private static MethodInfo? partnerGetter;
    private static MethodInfo? isDateGetter;
    private static FieldInfo? jobDateFollowField;
    private static FieldInfo? jobHangoutFollowField;

    private sealed record DateAttempt(bool IsDate);

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build() =>
    [
        OutcomeMetric.Count(
            owner: WayBetterRomanceRules.OwnerPackageId,
            family: "dates",
            label: "RWS_WayBetterRomance_Dates",
            description: "RWS_WayBetterRomance_DatesDesc",
            category: MetricCategory.Social,
            install: Install,
            breakdown: true),
    ];

    private static void Install(Harmony harmony)
    {
        var driverType = OutcomeMetric.Bind("BetterRomance.JobDriver_JobDateLead", AssemblyName);
        var defOfType = OutcomeMetric.Bind("BetterRomance.RomanceDefOf", AssemblyName);
        if (!typeof(JobDriver).IsAssignableFrom(driverType))
            throw new InvalidOperationException("BetterRomance.JobDriver_JobDateLead type changed");

        var actor = AccessTools.PropertyGetter(driverType, "Actor");
        var partner = AccessTools.PropertyGetter(driverType, "Partner");
        var isDate = AccessTools.PropertyGetter(driverType, "IsDate");
        var dateFollow = AccessTools.DeclaredField(defOfType, "JobDateFollow");
        var hangoutFollow = AccessTools.DeclaredField(defOfType, "JobHangoutFollow");

        if (actor?.ReturnType != typeof(Pawn) ||
            partner?.ReturnType != typeof(Pawn) ||
            isDate?.ReturnType != typeof(bool) ||
            dateFollow is null || !dateFollow.IsStatic || dateFollow.FieldType != typeof(JobDef) ||
            hangoutFollow is null || !hangoutFollow.IsStatic || hangoutFollow.FieldType != typeof(JobDef))
            throw new InvalidOperationException("WayBetterRomance property or DefOf contract changed");

        var finish = OutcomeMetric.Method(driverType, "<MakeNewToils>b__12_1", typeof(void));

        actorGetter = actor;
        partnerGetter = partner;
        isDateGetter = isDate;
        jobDateFollowField = dateFollow;
        jobHangoutFollowField = hangoutFollow;

        try
        {
            OutcomeMetric.Patch(harmony, finish, typeof(WayBetterRomanceStatistics), nameof(Before), nameof(After));
        }
        catch
        {
            actorGetter = null;
            partnerGetter = null;
            isDateGetter = null;
            jobDateFollowField = null;
            jobHangoutFollowField = null;
            throw;
        }
    }

    private static void Before(JobDriver? __instance, out DateAttempt? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(Unit) ||
            __instance is null ||
            actorGetter is null ||
            partnerGetter is null ||
            isDateGetter is null ||
            jobDateFollowField is null ||
            jobHangoutFollowField is null)
            return;

        try
        {
            if (actorGetter.Invoke(__instance, null) is not Pawn actor ||
                partnerGetter.Invoke(__instance, null) is not Pawn partner ||
                actor.Dead || partner.Dead)
                return;

            var player = Faction.OfPlayerSilentFail;
            if (player is null || (actor.Faction != player && partner.Faction != player))
                return;

            var isDate = Convert.ToBoolean(isDateGetter.Invoke(__instance, null));
            var expectedFollow = (isDate ? jobDateFollowField : jobHangoutFollowField).GetValue(null) as JobDef;
            if (expectedFollow is null || partner.CurJobDef != expectedFollow)
                return;

            __state = new DateAttempt(isDate);
        }
        catch (Exception e)
        {
            __state = null;
            LiveMetrics.Fail(Unit, e);
        }
    }

    private static void After(bool __runOriginal, DateAttempt? __state)
    {
        if (!__runOriginal || __state is null || !OutcomeMetric.Observing(Unit))
            return;

        try
        {
            var key = WayBetterRomanceRules.ResolveDateBreakdown(
                playerInvolved: true,
                participantsAlive: true,
                partnerWasFollowingExpectedJob: true,
                __state.IsDate);

            if (key is not null)
            {
                GameFacts.Record(Unit, state =>
                    OptionalTransitionRecording.RecordCount(state, "completed", 1, key: key, withBreakdown: true));
            }
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(Unit, e);
        }
    }
}
