using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.VfePirateHiring;

/// <summary>
/// VFE Pirates hire contracts are implemented inside the shared Vanilla
/// Expanded Framework Dialog_Hire. Count only a player home-map acceptance
/// that commits a new tracked pawn list under one of the Pirates-owned
/// HireableFactionDefs. Price is measured as actual silver removed from the
/// map, not the nominal CostFinal (which is rounded for payment).
/// </summary>
internal static class VfePirateHiringStatistics
{
    private const string Owner = "OskarPotocki.VFE.Pirates";
    private const string Framework = "OskarPotocki.VanillaFactionsExpanded.Core";
    private const string Unit = "mod.oskarpotocki.vfe.pirates.hiring";
    private const string ContractsKey = Unit + ".contracts_completed";
    private const string SilverKey = Unit + ".silver_paid";

    private static FieldInfo? dialogMapField;
    private static FieldInfo? dialogFactionField;
    private static FieldInfo? currentTrackerField;
    private static FieldInfo? hiredPawnsField;

    private sealed record HireBefore(Map Map, object Tracker, object? OldList,
        int SilverBefore);

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        Build = output =>
        {
            OptionalTransitionRecording.PutCount(output, "contracts", ContractsKey);
            OptionalTransitionRecording.PutCount(output, "silver", SilverKey);
        },
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
    {
        static ModMetricDescriptor D(string name, string label, string description,
            MetricUnit unit) => new()
        {
            ProviderId = "rws.piratehiring",
            OwnerPackageId = Owner,
            FamilyId = "hiring",
            MetricName = name,
            Revision = 1,
            Source = MetricObservationSource.Checkpoint,
            Category = MetricCategory.Economy,
            Unit = unit,
            Overall = OverallAggregation.Sum,
            Selection = ColonySelection.Representative,
            HasBreakdown = false,
            RequiredPackages = [new ModPackageRequirement(Owner), new ModPackageRequirement(Framework)],
            LabelKey = label,
            DescriptionKey = description,
        };
        return
        [
            new ModMetricAdapterRegistration(D("contracts_completed",
                "RWS_VfePirates_HireContracts",
                "RWS_VfePirates_HireContractsDesc", MetricUnit.Count),
                Family, Install),
            new ModMetricAdapterRegistration(D("silver_paid",
                "RWS_VfePirates_HireSilver",
                "RWS_VfePirates_HireSilverDesc", MetricUnit.Silver),
                Family, Install),
        ];
    }

    private static void Install(Harmony harmony)
    {
        var dialog = AccessTools.TypeByName("VEF.Planet.Dialog_Hire");
        var tracker = AccessTools.TypeByName("VEF.Planet.HiringContractTracker");
        var faction = AccessTools.TypeByName("VEF.Planet.HireableFactionDef");
        if (dialog is null || tracker is null || faction is null ||
            !string.Equals(dialog.Assembly.GetName().Name, "VEF",
                StringComparison.OrdinalIgnoreCase) ||
            !ReferenceEquals(dialog.Assembly, tracker.Assembly) ||
            !ReferenceEquals(dialog.Assembly, faction.Assembly) ||
            !typeof(Window).IsAssignableFrom(dialog) ||
            !typeof(Def).IsAssignableFrom(faction))
            throw new InvalidOperationException("Selected 1.7 VEF Hireable contract types not loaded");

        var accept = AccessTools.DeclaredMethod(dialog, "OnAcceptKeyPressed", Type.EmptyTypes);
        var map = AccessTools.DeclaredField(dialog, "targetMap");
        var chosen = AccessTools.DeclaredField(dialog, "curFaction");
        var activeTracker = AccessTools.DeclaredField(tracker, "Instance");
        var hired = AccessTools.DeclaredField(tracker, "pawns");
        if (accept?.ReturnType != typeof(void) || map?.FieldType != typeof(Map) ||
            chosen?.FieldType != faction || activeTracker?.FieldType != tracker ||
            hired is null || !typeof(IList).IsAssignableFrom(hired.FieldType))
            throw new InvalidOperationException("VEF Dialog_Hire acceptance or tracker contract signature changed");

        dialogMapField = map;
        dialogFactionField = chosen;
        currentTrackerField = activeTracker;
        hiredPawnsField = hired;
        try
        {
            harmony.Patch(accept,
                prefix: new HarmonyMethod(typeof(VfePirateHiringStatistics), nameof(BeforeAccept)),
                postfix: new HarmonyMethod(typeof(VfePirateHiringStatistics), nameof(AfterAccept)));
        }
        catch
        {
            harmony.Unpatch(accept, HarmonyPatchType.All, harmony.Id);
            dialogMapField = dialogFactionField = currentTrackerField = hiredPawnsField = null;
            throw;
        }
    }

    private static bool CanObserve()
        => MetricGates.IsRecording(Unit)
           && Current.ProgramState == ProgramState.Playing
           && Scribe.mode == LoadSaveMode.Inactive
           && Find.TickManager is not null;

    private static int SilverCount(Map map)
        => map.listerThings.ThingsOfDef(ThingDefOf.Silver)
            .Where(t => !t.Destroyed && t.stackCount > 0)
            .Sum(t => t.stackCount);

    private static void BeforeAccept(object __instance, out HireBefore? __state)
    {
        __state = null;
        if (!CanObserve())
            return;
        try
        {
            if (dialogFactionField?.GetValue(__instance) is not Def hireableDef ||
                !string.Equals(hireableDef.modContentPack?.PackageId, Owner,
                    StringComparison.OrdinalIgnoreCase) ||
                dialogMapField?.GetValue(__instance) is not Map { IsPlayerHome: true } map ||
                currentTrackerField?.GetValue(null) is not { } tracker)
                return;
            __state = new HireBefore(map, tracker,
                hiredPawnsField?.GetValue(tracker), SilverCount(map));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterAccept(bool __runOriginal, HireBefore? __state)
    {
        if (!__runOriginal || __state is null || !CanObserve())
            return;
        try
        {
            var hired = hiredPawnsField?.GetValue(__state.Tracker) as IList;
            var committed = VfePirateHiringRules.ContractCreated(
                pirateFaction: true,
                changedPawnList: hired is not null && !ReferenceEquals(hired, __state.OldList),
                nowHasPawns: hired?.Count > 0);
            if (!committed)
                return;

            var silver = VfePirateHiringRules.SilverPaid(
                __state.SilverBefore, SilverCount(__state.Map), contractCreated: true);
            GameFacts.Record(Unit, state =>
            {
                OptionalTransitionRecording.RecordCount(state, "contracts");
                if (silver > 0)
                    OptionalTransitionRecording.RecordCount(state, "silver", silver);
            });
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }
}
