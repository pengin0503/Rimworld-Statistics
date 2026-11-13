using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.VfeSettlers;

/// <summary>
/// VFE Settlers bounty hand-ins count an actual player caravan reward delivery
/// in the first GiveThing call of CommandTurnInWanted's action and the wanted
/// person's definitive removal. Silver in the prisoner's inventory, offered
/// reward values and creation of a command are never counted.
/// </summary>
internal static class VfeSettlersStatistics
{
    private const string Owner = "OskarPotocki.VanillaFactionsExpanded.SettlersModule";
    private const string Unit = "mod.oskarpotocki.vanillafactionsexpanded.settlersmodule.bountyhandin";
    private const string HandinsKey = Unit + ".completed";
    private const string SilverKey = Unit + ".silver_received";

    private static FieldInfo? caravanField;
    private static FieldInfo? wantedField;
    private static FieldInfo? rewardField;
    [ThreadStatic] private static HandinScope? active;

    private sealed class HandinScope
    {
        internal required Caravan Caravan { get; init; }
        internal required Pawn Wanted { get; init; }
        internal required bool WantedPresent { get; init; }
        internal required long RewardOffered { get; init; }
        internal required HandinScope? Parent { get; init; }
        internal readonly VfeSettlersBountyRules.FirstTransfer Transfer = new();
        internal long DeliveredSilver;
    }

    private sealed record GiveBefore(HandinScope Scope, int SilverCount);

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        Build = output =>
        {
            OptionalTransitionRecording.PutCount(output, "handins", HandinsKey);
            OptionalTransitionRecording.PutCount(output, "silver", SilverKey);
        },
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
    {
        static ModMetricDescriptor D(string key, string label, string description, MetricUnit metricUnit)
            => new()
            {
                ProviderId = "rws.bountyhandin",
                OwnerPackageId = Owner,
                FamilyId = "bountyhandin",
                MetricName = key,
                Revision = 1,
                Source = MetricObservationSource.Checkpoint,
                Category = MetricCategory.Economy,
                Unit = metricUnit,
                Overall = OverallAggregation.Sum,
                Selection = ColonySelection.Representative,
                HasBreakdown = false,
                RequiredPackages = [new ModPackageRequirement(Owner)],
                LabelKey = label,
                DescriptionKey = description,
            };
        return
        [
            new ModMetricAdapterRegistration(
                D("completed", "RWS_VfeSettlers_BountyHandins",
                    "RWS_VfeSettlers_BountyHandinsDesc", MetricUnit.Count), Family, Install),
            new ModMetricAdapterRegistration(
                D("silver_received", "RWS_VfeSettlers_BountySilver",
                    "RWS_VfeSettlers_BountySilverDesc", MetricUnit.Silver), Family, Install),
        ];
    }

    private static void Install(Harmony harmony)
    {
        var ownerType = AccessTools.TypeByName("VFE_Settlers.Utilities.UtilityEvent");
        if (ownerType is null ||
            !string.Equals(ownerType.Assembly.GetName().Name, "VFE_Settlers",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("VFE Settlers 1.7 bounty utility assembly missing");

        var closure = ownerType.GetNestedTypes(BindingFlags.NonPublic)
            .SingleOrDefault(t => t.Name == "<>c__DisplayClass1_0");
        var action = closure is null ? null : AccessTools.DeclaredMethod(
            closure, "<CommandTurnInWanted>b__0", Type.EmptyTypes);
        var caravan = closure is null ? null : AccessTools.DeclaredField(closure, "caravan");
        var criminal = closure is null ? null : AccessTools.DeclaredField(closure, "criminal");
        var reward = closure is null ? null : AccessTools.DeclaredField(closure, "reward");
        var give = AccessTools.DeclaredMethod(typeof(CaravanInventoryUtility),
            "GiveThing", [typeof(Caravan), typeof(Thing)]);

        if (action?.ReturnType != typeof(void) ||
            caravan?.FieldType != typeof(Caravan) ||
            criminal?.FieldType != typeof(Pawn) ||
            reward?.FieldType != typeof(int) ||
            give?.ReturnType != typeof(void))
            throw new InvalidOperationException("VFE Settlers bounty action or GiveThing signature changed");

        caravanField = caravan;
        wantedField = criminal;
        rewardField = reward;
        try
        {
            harmony.Patch(action,
                prefix: new HarmonyMethod(typeof(VfeSettlersStatistics), nameof(BeforeHandin)),
                postfix: new HarmonyMethod(typeof(VfeSettlersStatistics), nameof(AfterHandin)),
                finalizer: new HarmonyMethod(typeof(VfeSettlersStatistics), nameof(FinishHandin)));
            harmony.Patch(give,
                prefix: new HarmonyMethod(typeof(VfeSettlersStatistics), nameof(BeforeGive)),
                postfix: new HarmonyMethod(typeof(VfeSettlersStatistics), nameof(AfterGive)));
        }
        catch
        {
            harmony.Unpatch(action, HarmonyPatchType.All, harmony.Id);
            harmony.Unpatch(give, HarmonyPatchType.All, harmony.Id);
            caravanField = wantedField = rewardField = null;
            throw;
        }
    }

    private static bool CanObserve()
        => MetricGates.IsRecording(Unit)
           && Current.ProgramState == ProgramState.Playing
           && Scribe.mode == LoadSaveMode.Inactive
           && Find.TickManager is not null;

    private static bool HasWanted(Caravan caravan, Pawn wanted)
        => caravan.PawnsListForReading.Contains(wanted) ||
           CaravanInventoryUtility.AllInventoryItems(caravan)
               .OfType<Corpse>().Any(c => ReferenceEquals(c.InnerPawn, wanted));

    private static int SilverCount(Caravan caravan) =>
        CaravanInventoryUtility.AllInventoryItems(caravan)
            .Where(t => t.def == ThingDefOf.Silver)
            .Sum(t => t.stackCount);

    private static void BeforeHandin(object __instance, out HandinScope? __state)
    {
        __state = null;
        if (!CanObserve())
            return;
        try
        {
            if (caravanField?.GetValue(__instance) is not Caravan caravan ||
                wantedField?.GetValue(__instance) is not Pawn wanted ||
                rewardField?.GetValue(__instance) is not int originalReward ||
                originalReward <= 0 ||
                caravan.Faction != Faction.OfPlayer ||
                !HasWanted(caravan, wanted))
                return;
            __state = new HandinScope
            {
                Caravan = caravan,
                Wanted = wanted,
                WantedPresent = true,
                RewardOffered = checked(originalReward * 10L),
                Parent = active,
            };
            active = __state;
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void BeforeGive(Caravan __0, Thing __1, out GiveBefore? __state)
    {
        __state = null;
        if (!CanObserve() || active is not { } scope ||
            !ReferenceEquals(__0, scope.Caravan) ||
            !scope.Transfer.ConsumeFirst() ||
            __1?.def != ThingDefOf.Silver || __1.stackCount <= 0)
            return;
        try { __state = new GiveBefore(scope, SilverCount(__0)); }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterGive(Caravan __0, bool __runOriginal, GiveBefore? __state)
    {
        if (!__runOriginal || __state is null ||
            !ReferenceEquals(active, __state.Scope) || !CanObserve())
            return;
        try
        {
            var added = SilverCount(__0) - __state.SilverCount;
            if (added > 0)
                __state.Scope.DeliveredSilver += added;
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterHandin(bool __runOriginal, HandinScope? __state)
    {
        if (!__runOriginal || __state is null ||
            !ReferenceEquals(active, __state) || !CanObserve())
            return;
        try
        {
            var accepted = VfeSettlersBountyRules.RewardReceived(
                __state.WantedPresent, !HasWanted(__state.Caravan, __state.Wanted),
                __state.Caravan.Faction == Faction.OfPlayer,
                __state.RewardOffered, __state.DeliveredSilver);
            if (accepted <= 0)
                return;
            GameFacts.Record(Unit, state =>
            {
                OptionalTransitionRecording.RecordCount(state, "handins");
                OptionalTransitionRecording.RecordCount(state, "silver", accepted);
            });
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static Exception? FinishHandin(Exception? __exception, HandinScope? __state)
    {
        if (__state is not null && ReferenceEquals(active, __state))
            active = __state.Parent;
        return __exception;
    }
}
