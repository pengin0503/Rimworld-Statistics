using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.Patches.Metrics;

internal static class OutpostContract
{
    private const string Target = "Outposts.Outpost";
    internal static readonly Lazy<Type?> Type = new(() => AccessTools.TypeByName(Target));
    internal static readonly Lazy<MethodInfo?> PawnCount = new(() => Type.Value is { } type
        ? AccessTools.PropertyGetter(type, "PawnCount") : null);
    internal static readonly Lazy<MethodInfo?> Produce = new(() => Type.Value is { } type
        ? AccessTools.DeclaredMethod(type, "Produce", System.Type.EmptyTypes) : null);
    internal static readonly Lazy<MethodInfo?> Deliver = new(() => Type.Value is { } type
        ? AccessTools.DeclaredMethod(type, "Deliver", [typeof(IEnumerable<Thing>)]) : null);

    internal static bool Available => Produce.Value is { IsStatic: false, IsVirtual: true } produce
        && produce.ReturnType == typeof(void) && Deliver.Value is { IsStatic: false } deliver && deliver.ReturnType == typeof(void);

    internal static int? CountPawns(WorldObject outpost)
    {
        try
        {
            return PawnCount.Value?.Invoke(outpost, null) is int count && count >= 0 ? count : null;
        }
        catch (Exception error) when (error is TargetInvocationException or ArgumentException or MethodAccessException)
        {
            return null;
        }
    }
}

[MetricAdapter(OutpostProduction.Unit)]
[HarmonyPatch]
internal static class OutpostProduceMetricPatch
{
    [ThreadStatic] internal static WorldObject? CurrentOutpost;

    private static IEnumerable<MethodBase> TargetMethods()
    {
        var root = OutpostContract.Produce.Value ?? throw new MissingMethodException("Outposts.Outpost", "Produce");
        return DefDatabase<WorldObjectDef>.AllDefsListForReading
            .Select(def => def.worldObjectClass)
            .Where(OutpostSnapshot.IsOutpostType)
            .Distinct()
            .Select(type => AccessTools.Method(type, "Produce", Type.EmptyTypes))
            .Where(method => method is { IsAbstract: false } && method.GetBaseDefinition().Equals(root))
            .Cast<MethodBase>().Prepend(root).Distinct();
    }

    [HarmonyPrefix]
    private static void Prefix(WorldObject __instance, out WorldObject? __state)
    {
        __state = CurrentOutpost;
        CurrentOutpost = __instance.Faction == Faction.OfPlayerSilentFail
            && GameFacts.AnyRecording(OutpostProduction.Unit) ? __instance : null;
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(WorldObject? __state, Exception? __exception)
    {
        CurrentOutpost = __state;
        return __exception;
    }
}

[MetricAdapter(OutpostProduction.Unit)]
[HarmonyPatch]
internal static class OutpostDeliverMetricPatch
{
    private static MethodBase TargetMethod() => OutpostContract.Deliver.Value ?? throw new MissingMethodException("Outposts.Outpost", "Deliver");

    [HarmonyPrefix]
    private static void Prefix(WorldObject __instance, ref IEnumerable<Thing> __0, out OutpostProduction.Delivery<Thing>? __state)
    {
        __state = null;
        if (!ReferenceEquals(__instance, OutpostProduceMetricPatch.CurrentOutpost) || __0 is null)
            return;
        // The original begins by materializing the sequence. Reuse exactly those
        // generated items and capture their counts before delivery changes stacks.
        __state = OutpostProduction.Capture(__0, item => (item?.def?.defName, item?.stackCount ?? 0));
        __0 = __state.Items;
    }

    [HarmonyPostfix]
    private static void Postfix(OutpostProduction.Delivery<Thing>? __state, bool __runOriginal)
    {
        if (__state is { } delivery && __runOriginal)
            GameFacts.Record(OutpostProduction.Unit, state => delivery.Record(state, succeeded: true));
    }
}
