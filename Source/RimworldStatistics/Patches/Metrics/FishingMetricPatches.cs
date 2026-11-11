using System.Globalization;
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
    internal const string VfeBonusStage = "vfe-adept";
    private static readonly HashSet<string> VfeAdeptDefs = new(StringComparer.Ordinal)
    {
        "VCEF_Crayfish", "VCEF_ButterFish", "VCEF_FreshwaterStingray",
        "VCEF_FlyingFish", "VCEF_Arapaima", "VCEF_ShortfinMakoShark",
    };

    private sealed class Capture(Pawn pawn, Map map, IntVec3 center, IntVec3 fishingCell, bool bonus)
    {
        public Pawn Pawn { get; } = pawn;
        public Map Map { get; } = map;
        public IntVec3 Center { get; } = center;
        public IntVec3 FishingCell { get; } = fishingCell;
        public bool Bonus { get; } = bonus;
        public HashSet<Thing> Candidates { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<string, int> Accepted { get; } = new(StringComparer.Ordinal);
    }

    [ThreadStatic] private static Capture? current;

    internal static void SetCandidates(Pawn pawn, IntVec3 cell, bool animalFishing, List<Thing>? catches)
    {
        if (current is { Bonus: false } capture && capture.Pawn == pawn && capture.FishingCell == cell
            && !animalFishing && catches != null)
            capture.Candidates.UnionWith(catches);
    }

    internal static void Observe(Thing thing, IntVec3 center, Map map, ThingPlaceMode mode,
        ref Action<Thing, int> placedAction)
    {
        if (current is not { } capture || map != capture.Map || center != capture.Center
            || mode != ThingPlaceMode.Near || thing?.def is not { } definition)
            return;
        var defName = definition.defName;
        if (capture.Bonus
            ? !string.Equals(definition.modContentPack?.PackageId, "vanillaexpanded.vcef", StringComparison.OrdinalIgnoreCase)
                || !VfeAdeptDefs.Contains(defName)
            : !capture.Candidates.Remove(thing))
            return;
        var original = placedAction;
        placedAction = (placed, count) =>
        {
            if (count > 0 && !string.IsNullOrWhiteSpace(defName))
                capture.Accepted[defName] = capture.Accepted.GetValueOrDefault(defName) + count;
            original?.Invoke(placed, count);
        };
    }

    internal static void Run(JobDriver_Fish driver, string stage, Action original)
    {
        var pawn = driver.pawn;
        var job = driver.job;
        if (pawn == null || job == null || job.loadID < 0 || !GameFacts.IsPlayer(pawn)
            || pawn.Map is not { IsPlayerHome: true } map || !GameFacts.AnyRecording(FishingFamily.Name))
        {
            original();
            return;
        }

        var previous = current;
        var capture = new Capture(pawn, map, pawn.Position, job.GetTarget(TargetIndex.A).Cell, stage == VfeBonusStage);
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
/// The private Odyssey factory returns a completion Toil. Wrap its init action and the audited
/// VFE bonus delegate, without running a tick hook or invoking catch generation ourselves.
/// </summary>
[MetricAdapter(FishingFamily.Name)]
[HarmonyPatch(typeof(JobDriver_Fish), "CompleteFishingToil")]
public static class FishingCompletionMetricPatch
{
    [HarmonyPostfix]
    [HarmonyAfter("com.VFEFishing")]
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
            var action = actions[i];
            var owner = action.Method.DeclaringType;
            while (owner?.DeclaringType is { } parent)
                owner = parent;
            if (owner?.FullName != "VCE_Fishing.VCE_Fishing_JobDriver_Fish_CompleteFishingToil_PostFix_Patch")
                continue;
            actions[i] = () =>
            {
                if (completed)
                    FishingPlacementScope.Run(__instance, FishingPlacementScope.VfeBonusStage, action);
                else
                    action();
            };
        }
    }
}

/// <summary>Remember the generated candidates from the original call; never run the RNG again.</summary>
[MetricAdapter(FishingFamily.Name)]
[HarmonyPatch(typeof(FishingUtility), nameof(FishingUtility.GetCatchesFor))]
public static class FishingCandidatesMetricPatch
{
    [HarmonyPostfix]
    [HarmonyAfter("com.VFEFishing")]
    [HarmonyPriority(Priority.Last)]
    public static void Postfix(Pawn pawn, IntVec3 cell, bool animalFishing, List<Thing>? __result)
        => FishingPlacementScope.SetCandidates(pawn, cell, animalFishing, __result);
}

/// <summary>Only the inner GenPlace overload, which the outer overload delegates to, is patched.</summary>
[MetricAdapter(FishingFamily.Name)]
[HarmonyPatch(typeof(GenPlace), nameof(GenPlace.TryPlaceThing),
    new[] { typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode), typeof(Thing),
        typeof(Action<Thing, int>), typeof(Predicate<IntVec3>), typeof(Rot4?), typeof(int) },
    new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out,
        ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal })]
public static class FishingAcceptedPlacementMetricPatch
{
    [HarmonyPrefix]
    public static void Prefix(Thing thing, IntVec3 center, Map map, ThingPlaceMode mode, ref Action<Thing, int> placedAction)
        => FishingPlacementScope.Observe(thing, center, map, mode, ref placedAction);
}
