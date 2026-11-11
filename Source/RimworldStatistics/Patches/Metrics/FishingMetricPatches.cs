using System.Globalization;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;
using Verse.AI;

namespace RimworldStatistics.Patches.Metrics;

/// <summary>
/// Active only during a fishing completion toil. The GenPlace callback supplies actually
/// accepted units, including partial stack absorption when TryPlaceThing returns false.
/// </summary>
internal static class FishingPlacementScope
{
    private sealed class Capture
    {
        public Dictionary<string, int> Accepted { get; } = new(StringComparer.Ordinal);
    }

    [ThreadStatic] private static Capture? current;

    internal static bool IsActive => current != null;

    internal static void Accepted(Thing thing, int units)
    {
        if (current is null || thing?.def is null || units <= 0)
            return;
        var def = thing.def.defName;
        if (!string.IsNullOrWhiteSpace(def))
            current.Accepted[def] = current.Accepted.GetValueOrDefault(def) + units;
    }

    internal static void Run(JobDriver_Fish driver, string stage, Action original)
    {
        var pawn = driver.pawn;
        var job = driver.job;
        if (pawn == null || job == null || pawn.Faction != Faction.OfPlayerSilentFail
            || pawn.Map is not { IsPlayerHome: true } || !GameFacts.AnyRecording(FishingFamily.Name))
        {
            original();
            return;
        }

        var previous = current;
        var capture = new Capture();
        current = capture;
        try
        {
            original();
        }
        finally
        {
            current = previous;
        }
        if (capture.Accepted.Count == 0)
            return;
        var jobId = job.loadID.ToString(CultureInfo.InvariantCulture);
        var tick = GameFacts.Tick;
        GameFacts.Record(FishingFamily.Name, s =>
            FishingFamily.RecordAccepted(s, jobId, stage, capture.Accepted, tick));
    }
}

/// <summary>
/// The private vanilla Odyssey factory returns a completion Toil; only its init action and
/// already-registered finish actions are wrapped, not its tick action or random catch generation.
/// </summary>
[MetricAdapter(FishingFamily.Name)]
[HarmonyPatch]
public static class FishingCompletionMetricPatch
{
    private static MethodBase? TargetMethod()
        => AccessTools.Method(typeof(JobDriver_Fish), "CompleteFishingToil");

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    public static void Postfix(JobDriver_Fish __instance, ref Toil __result)
    {
        if (__result?.initAction is not { } original)
            return;

        var completed = false;
        __result.initAction = () =>
        {
            completed = false;
            FishingPlacementScope.Run(__instance, "main", original);
            completed = true;
        };

        // Vanilla Fishing Expanded adds its optional adept catch via AddFinishAction.
        // An interrupted job that never reached this toil never opens the collector.
        if (__result.finishActions is not { Count: > 0 } actions)
            return;
        for (var i = 0; i < actions.Count; i++)
        {
            var index = i;
            var action = actions[i];
            actions[i] = () =>
            {
                if (completed)
                    FishingPlacementScope.Run(__instance, "finish-" + index.ToString(CultureInfo.InvariantCulture), action);
                else
                    action();
            };
        }
    }
}

/// <summary>Only the inner GenPlace overload, which the outer overload delegates to, is patched.</summary>
[MetricAdapter(FishingFamily.Name)]
[HarmonyPatch]
public static class FishingAcceptedPlacementMetricPatch
{
    private static MethodBase? TargetMethod()
        => AccessTools.Method(typeof(GenPlace), nameof(GenPlace.TryPlaceThing),
            new[] {
                typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode),
                typeof(Thing).MakeByRefType(), typeof(Action<Thing, int>),
                typeof(Predicate<IntVec3>), typeof(Rot4?), typeof(int)
            });

    [HarmonyPrefix]
    public static void Prefix(ref Action<Thing, int> placedAction)
    {
        if (!FishingPlacementScope.IsActive)
            return;
        var original = placedAction;
        placedAction = (thing, count) =>
        {
            original?.Invoke(thing, count);
            FishingPlacementScope.Accepted(thing, count);
        };
    }
}
