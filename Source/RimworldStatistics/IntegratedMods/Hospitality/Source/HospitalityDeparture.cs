using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimworldStatistics.Metrics;
using Verse;
using Verse.AI.Group;

namespace RimworldStatistics.HospitalityAddon;

/// <summary>
/// Records a peaceful visit only after the visiting toil ended AND at least one
/// guest actually exited the map AND the lord disbanded with no remaining pawns.
/// No event is inferred from the leaving flag alone. The per-lord evidence is
/// intentionally transient; a mid-departure reload without evidence is skipped.
/// </summary>
internal static class HospitalityDeparture
{
    private const string Unit = "mod.orion.hospitality.visits";
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;
    private static readonly ConditionalWeakTable<Lord, DepartureState> Evidence = new();
    private static FieldInfo? lordField;
    private static FieldInfo? leavingField;
    private static FieldInfo? recoveredField;
    private static FieldInfo? sentAwayField;
    private static Type? guestCompType;
    private static MethodInfo? onLeave;
    private static MethodInfo? onPawnLost;
    private static MethodInfo? onLordDestroyed;

    private sealed class DepartureState
    {
        internal bool Finished;
        internal bool SawExit;
        internal bool SentAway;
        internal bool OtherLoss;
    }

    internal static bool TryBind()
    {
        try
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a =>
                string.Equals(a.GetName().Name, "Hospitality", StringComparison.Ordinal));
            var visit = assembly?.GetType("Hospitality.LordJob_VisitColony", false);
            var guestComp = assembly?.GetType("Hospitality.CompGuest", false);
            if (visit is null || !typeof(LordJob).IsAssignableFrom(visit) ||
                guestComp is null || !typeof(ThingComp).IsAssignableFrom(guestComp))
                return false;
            var lord = typeof(LordJob).GetField("lord", PublicInstance);
            var leaving = visit.GetField("leaving", PublicInstance);
            var recovered = visit.GetField("recoveredOnMap", PublicInstance);
            var sentAway = guestComp.GetField("sentAway", PublicInstance);
            var leave = visit.GetMethod("OnLeaveTriggered", PublicInstance,
                binder: null, types: Type.EmptyTypes, modifiers: null);
            var lost = visit.GetMethod("Notify_PawnLost", PublicInstance,
                binder: null, types: new[] { typeof(Pawn), typeof(PawnLostCondition) }, modifiers: null);
            var destroyed = visit.GetMethod("Notify_LordDestroyed", PublicInstance,
                binder: null, types: Type.EmptyTypes, modifiers: null);
            if (lord?.FieldType != typeof(Lord) || leaving?.FieldType != typeof(bool) ||
                recovered?.FieldType != typeof(bool) || sentAway?.FieldType != typeof(bool) ||
                leave?.ReturnType != typeof(void) || lost?.ReturnType != typeof(void) ||
                destroyed?.ReturnType != typeof(void))
                return false;
            lordField = lord;
            leavingField = leaving;
            recoveredField = recovered;
            sentAwayField = sentAway;
            guestCompType = guestComp;
            onLeave = leave;
            onPawnLost = lost;
            onLordDestroyed = destroyed;
            return true;
        }
        catch (Exception) { return false; }
    }

    internal static bool TryInstall()
    {
        if (onLeave is null || onPawnLost is null || onLordDestroyed is null)
            return false;
        try
        {
            var harmony = new Harmony("pengin0503.rws.hospitality.departures");
            harmony.Patch(onLeave, postfix: new HarmonyMethod(typeof(HospitalityDeparture), nameof(AfterVisitEnd)));
            harmony.Patch(onPawnLost, postfix: new HarmonyMethod(typeof(HospitalityDeparture), nameof(AfterPawnLost)));
            harmony.Patch(onLordDestroyed, prefix: new HarmonyMethod(typeof(HospitalityDeparture), nameof(BeforeLordDestroyed)));
            return true;
        }
        catch (Exception e)
        {
            MetricGates.Fail(Unit, e);
            return false;
        }
    }

    private static Lord? GetLord(LordJob job) => lordField?.GetValue(job) as Lord;

    private static bool Active => HospitalitySdk.CompletionsConnected &&
        Current.ProgramState == ProgramState.Playing &&
        Scribe.mode == LoadSaveMode.Inactive && MetricGates.IsRecording(Unit);

    private static void AfterVisitEnd(LordJob __instance)
    {
        if (!Active) return;
        try
        {
            var lord = GetLord(__instance);
            if (lord is null) return;
            var state = Evidence.GetValue(lord, _ => new DepartureState());
            var pawns = lord.ownedPawns;
            state.Finished = leavingField?.GetValue(__instance) is true &&
                lord.Map is { IsPlayerHome: true } && pawns is { Count: > 0 };
            if (recoveredField?.GetValue(__instance) is true ||
                lord.faction is null || lord.faction.HostileTo(Faction.OfPlayer))
                state.Finished = false;

            // A guest sent away, or a guest lacking the expected component,
            // cannot be promoted to a normally finished visit.
            if (pawns is not null && guestCompType is not null && sentAwayField is not null)
                foreach (var pawn in pawns)
                {
                    var comp = pawn.AllComps?.FirstOrDefault(guestCompType.IsInstanceOfType);
                    if (comp is null || sentAwayField.GetValue(comp) is not false)
                        state.SentAway = true;
                }
            else state.SentAway = true;
        }
        catch (Exception e) { MetricGates.Fail(Unit, e); }
    }

    private static void AfterPawnLost(LordJob __instance, PawnLostCondition __1)
    {
        if (!Active) return;
        try
        {
            var lord = GetLord(__instance);
            if (lord is null) return;
            var state = Evidence.GetValue(lord, _ => new DepartureState());
            if (__1 == PawnLostCondition.ExitedMap) state.SawExit = true;
            else state.OtherLoss = true;
        }
        catch (Exception e) { MetricGates.Fail(Unit, e); }
    }

    private static void BeforeLordDestroyed(LordJob __instance)
    {
        if (!Active) return;
        try
        {
            var lord = GetLord(__instance);
            if (lord is null || !Evidence.TryGetValue(lord, out var state))
                return;
            var map = lord.Map;
            var result = new VisitExitEvidence(
                VisitFinished: state.Finished,
                SawActualExit: state.SawExit,
                SentAway: state.SentAway,
                Hostile: lord.faction is null || lord.faction.HostileTo(Faction.OfPlayer),
                RecoveredVisitor: recoveredField?.GetValue(__instance) is true,
                OtherLossOccurred: state.OtherLoss,
                ViolentLosses: lord.numPawnsLostViolently,
                RemainingPawns: lord.ownedPawns?.Count ?? -1,
                PlayerHome: map is { IsPlayerHome: true });
            if (!HospitalityDeparturePolicy.ShouldRecord(result) || map is null)
                return;
            var id = HospitalityVisitEventId.Create(map.uniqueID, lord.GetUniqueLoadID());
            if (id is not null)
                HospitalitySdk.RecordCompletion(id);
        }
        catch (Exception e) { MetricGates.Fail(Unit, e); }
    }
}
