using System.Reflection;
using HarmonyLib;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.NewModOutcomes;

internal static class ArenaOutcomes
{
    private const string Owner = "Mlie.PrisonerArena";
    private const string Unit = "mod.mlie.prisonerarena.bouts";
    private static FieldInfo winners = null!;
    private static FieldInfo state = null!;
    private static object fight = null!;

    internal static ModMetricAdapterRegistration Build() =>
        OutcomeMetric.Count(Owner, "bouts", "RWS_NewMod_Arena", MetricCategory.Social, Install);

    private static void Install(Harmony harmony)
    {
        var type = OutcomeMetric.Bind("ArenaBell.Building_Bell", "ArenaBell");
        winners = AccessTools.DeclaredField(type, "winners");
        state = AccessTools.DeclaredField(type, "currentState");
        if (!typeof(Building).IsAssignableFrom(type) || winners?.FieldType != typeof(List<TaggedString>) ||
            state is null || !state.FieldType.IsEnum || !Enum.TryParse(state.FieldType, "fight", out var value))
            throw new InvalidOperationException("Arena result state changed");
        fight = value;
        OutcomeMetric.Patch(harmony, OutcomeMetric.Method(type, "EndBrawl", typeof(void), typeof(Pawn), typeof(bool)),
            typeof(ArenaOutcomes));
    }

    private static void Before(Building __instance, Pawn? __0, bool __1, out int __state)
    {
        __state = -1;
        if (__1 || __0 is null || !OutcomeMetric.Observing(Unit) ||
            !__instance.Spawned || !GameFacts.IsPlayer(__instance))
            return;
        try
        {
            if (Equals(state.GetValue(__instance), fight) && winners.GetValue(__instance) is List<TaggedString> list)
                __state = list.Count;
        }
        catch (Exception error) { LiveMetrics.Fail(Unit, error); }
    }

    private static void After(Building __instance, bool __runOriginal, int __state)
    {
        if (!__runOriginal || __state < 0)
            return;
        try
        {
            if (winners.GetValue(__instance) is List<TaggedString> list &&
                NewModOutcomeRules.WinnerAdded(__state, list.Count))
                OutcomeMetric.Record(Unit);
        }
        catch (Exception error) { LiveMetrics.Fail(Unit, error); }
    }
}
