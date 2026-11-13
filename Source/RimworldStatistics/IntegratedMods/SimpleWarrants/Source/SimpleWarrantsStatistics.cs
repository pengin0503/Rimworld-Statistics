using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.SimpleWarrants;

internal static class SimpleWarrantsStatistics
{
    private const string Owner = "pb3n.SimpleWarrants";
    private const string Unit = "mod.pb3n.simplewarrants.handin";
    private const string CompletedKey = Unit + ".completed";
    private const string SilverKey = Unit + ".silver_received";

    private static FieldInfo? managerInstance;
    private static FieldInfo? acceptedWarrants;
    private static FieldInfo? warrantStatus;
    private static object? completedStatus;
    private static MethodInfo? baseReward;

    [ThreadStatic] private static HandinScope? active;
    private sealed record HandinScope(Caravan Caravan, IList Accepted, HandinScope? Parent)
    {
        public Dictionary<object, (Thing Target, long Silver)> Rewards { get; } = new(ReferenceEqualityComparer.Instance);
    }

    private sealed record RewardBefore(HandinScope Scope, Thing Target, long Silver);

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        Build = output =>
        {
            output.PutCount("completed", CompletedKey);
            output.PutCount("silver", SilverKey);
        },
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
    {
        static ModMetricDescriptor Metric(string name, MetricUnit unit, string label, string description) => new()
        {
            ProviderId = "rws.simplewarrantsettlements",
            OwnerPackageId = Owner,
            FamilyId = "handin",
            MetricName = name,
            Source = MetricObservationSource.Checkpoint,
            Revision = 1,
            Category = MetricCategory.Economy,
            Unit = unit,
            Overall = OverallAggregation.Sum,
            Selection = ColonySelection.Representative,
            RequiredPackages = [new ModPackageRequirement(Owner)],
            LabelKey = label,
            DescriptionKey = description,
        };
        return
        [
            new ModMetricAdapterRegistration(Metric("completed", MetricUnit.Count,
                "RWS_SimpleWarrants_Completed", "RWS_SimpleWarrants_CompletedDesc"), Family, Install),
            new ModMetricAdapterRegistration(Metric("silver_received", MetricUnit.Silver,
                "RWS_SimpleWarrants_Silver", "RWS_SimpleWarrants_SilverDesc"), Family, Install),
        ];
    }

    private static void Install(Harmony harmony)
    {
        var request = AccessTools.TypeByName("SimpleWarrants.WarrantRequestComp");
        var assembly = request?.Assembly;
        var warrant = assembly?.GetType("SimpleWarrants.Warrant");
        var manager = assembly?.GetType("SimpleWarrants.WarrantsManager");
        if (request is null || warrant is null || manager is null || assembly is null ||
            !string.Equals(assembly.GetName().Name, "SimpleWarrants", StringComparison.Ordinal))
            throw new InvalidOperationException("Simple Warrants assembly is unavailable");

        var fulfill = AccessTools.DeclaredMethod(request, "Fulfill", [typeof(Caravan)]);
        var instance = AccessTools.DeclaredField(manager, "Instance");
        var accepted = AccessTools.DeclaredField(manager, "acceptedWarrants");
        var status = AccessTools.DeclaredField(warrant, "status");
        var rewardArgs = new[] { typeof(Caravan), typeof(Thing) };
        var rootReward = AccessTools.DeclaredMethod(warrant, "GiveReward", rewardArgs);
        var pawn = assembly.GetType("SimpleWarrants.Warrant_Pawn");
        var artifact = assembly.GetType("SimpleWarrants.Warrant_Artifact");
        var pawnReward = pawn is null ? null : AccessTools.DeclaredMethod(pawn, "GiveReward", rewardArgs);
        var artifactReward = artifact is null ? null : AccessTools.DeclaredMethod(artifact, "GiveReward", rewardArgs);
        if (fulfill?.ReturnType != typeof(void) || instance is not { IsStatic: true } ||
            instance.FieldType != manager || accepted is null || accepted.FieldType != typeof(List<>).MakeGenericType(warrant) ||
            status is null || !status.FieldType.IsEnum || !Enum.TryParse(status.FieldType, "Completed", out var completed) ||
            rootReward?.ReturnType != typeof(void) || pawnReward is null || artifactReward is null ||
            !pawnReward.GetBaseDefinition().Equals(rootReward) || !artifactReward.GetBaseDefinition().Equals(rootReward))
            throw new InvalidOperationException("Simple Warrants settlement signature changed");

        managerInstance = instance;
        acceptedWarrants = accepted;
        warrantStatus = status;
        completedStatus = completed;
        baseReward = rootReward;
        var rewards = new[] { rootReward, pawnReward, artifactReward };
        try
        {
            harmony.Patch(fulfill,
                prefix: new HarmonyMethod(typeof(SimpleWarrantsStatistics), nameof(BeforeFulfill)),
                postfix: new HarmonyMethod(typeof(SimpleWarrantsStatistics), nameof(AfterFulfill)),
                finalizer: new HarmonyMethod(typeof(SimpleWarrantsStatistics), nameof(FinishFulfill)));
            foreach (var reward in rewards)
                harmony.Patch(reward,
                    prefix: new HarmonyMethod(typeof(SimpleWarrantsStatistics), nameof(BeforeReward)),
                    postfix: new HarmonyMethod(typeof(SimpleWarrantsStatistics), nameof(AfterReward)));
        }
        catch
        {
            harmony.Unpatch(fulfill, HarmonyPatchType.All, harmony.Id);
            foreach (var reward in rewards)
                harmony.Unpatch(reward, HarmonyPatchType.All, harmony.Id);
            throw;
        }
    }

    private static bool CanObserve()
        => MetricGates.IsRecording(Unit) && Current.ProgramState == ProgramState.Playing
            && Scribe.mode == LoadSaveMode.Inactive && Find.TickManager is not null;

    private static long Silver(Caravan caravan)
        => CaravanInventoryUtility.AllInventoryItems(caravan)
            .Where(t => t.def == ThingDefOf.Silver && !t.Destroyed && t.stackCount > 0)
            .Sum(t => (long)t.stackCount);

    private static void BeforeFulfill(Caravan __0, out HandinScope? __state)
    {
        __state = null;
        if (!CanObserve() || __0?.IsPlayerControlled != true)
            return;
        try
        {
            if (managerInstance?.GetValue(null) is { } manager &&
                acceptedWarrants?.GetValue(manager) is IList accepted)
                active = __state = new HandinScope(__0, accepted, active);
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void BeforeReward(object __instance, Caravan __0, Thing __1, MethodBase __originalMethod,
        out RewardBefore? __state)
    {
        __state = null;
        if (active is not { } scope || !ReferenceEquals(scope.Caravan, __0))
            return;
        try
        {
            if (__1 is null || __1.Destroyed || __1.ParentHolder is not null || !scope.Accepted.Contains(__instance) ||
                Equals(warrantStatus?.GetValue(__instance), completedStatus))
                return;
            // Pawn and Artifact call the base method before awarding silver.
            // Only observe the base directly when the concrete warrant inherits it.
            if (__originalMethod.Equals(baseReward) &&
                !Equals(AccessTools.Method(__instance.GetType(), "GiveReward", [typeof(Caravan), typeof(Thing)]), baseReward))
                return;
            __state = new RewardBefore(scope, __1, Silver(__0));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterReward(object __instance, bool __runOriginal, RewardBefore? __state)
    {
        if (!__runOriginal || __state is null)
            return;
        try
        {
            if (Equals(warrantStatus?.GetValue(__instance), completedStatus))
                __state.Scope.Rewards.TryAdd(__instance, (__state.Target, Math.Max(0, Silver(__state.Scope.Caravan) - __state.Silver)));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterFulfill(bool __runOriginal, HandinScope? __state)
    {
        if (!__runOriginal || __state is null || !CanObserve())
            return;
        try
        {
            long count = 0, silver = 0;
            foreach (var (warrant, reward) in __state.Rewards)
            {
                var amount = SimpleWarrantsRules.SettledSilver(
                    Equals(warrantStatus?.GetValue(warrant), completedStatus),
                    !__state.Accepted.Contains(warrant), reward.Target.Destroyed, reward.Silver);
                if (amount is null)
                    continue;
                count++;
                silver += amount.Value;
            }
            if (count == 0)
                return;
            GameFacts.Record(Unit, state =>
            {
                OptionalTransitionRecording.RecordCount(state, "completed", count);
                OptionalTransitionRecording.RecordCount(state, "silver", silver);
            });
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static Exception? FinishFulfill(Exception? __exception, HandinScope? __state)
    {
        if (__state is not null)
            active = __state.Parent;
        return __exception;
    }
}
