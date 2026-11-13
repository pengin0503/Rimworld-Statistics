using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.VfeDeserters;

/// <summary>Physical intelligence debits confirmed by the Deserter Network's payment method.</summary>
internal static class VfeDesertersStatistics
{
    private const string Owner = "OskarPotocki.VFE.Deserters";
    private const string Unit = "mod.oskarpotocki.vfe.deserters.intelpayments";
    private const string NormalKey = Unit + ".intel_spent";
    private const string CriticalKey = Unit + ".critical_intel_spent";

    private static FieldInfo? dialogMap;
    private static FieldInfo? normalDef;
    private static FieldInfo? criticalDef;

    private sealed record PaymentBefore(Map Map, ThingDef? Normal, long NormalCount,
        ThingDef? Critical, long CriticalCount);

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        Build = output =>
        {
            output.PutCount("normal", NormalKey);
            output.PutCount("critical", CriticalKey);
        },
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
    {
        static ModMetricDescriptor Metric(string name, string label, string description) => new()
        {
            ProviderId = "rws.desertersintel",
            OwnerPackageId = Owner,
            FamilyId = "intelpayments",
            MetricName = name,
            Source = MetricObservationSource.Checkpoint,
            Revision = 1,
            Category = MetricCategory.Economy,
            Unit = MetricUnit.Count,
            Overall = OverallAggregation.Sum,
            Selection = ColonySelection.Representative,
            RequiredPackages = [new ModPackageRequirement(Owner)],
            LabelKey = label,
            DescriptionKey = description,
        };
        return
        [
            new ModMetricAdapterRegistration(Metric("intel_spent",
                "RWS_VfeDeserters_IntelPaid", "RWS_VfeDeserters_IntelPaidDesc"), Family, Install),
            new ModMetricAdapterRegistration(Metric("critical_intel_spent",
                "RWS_VfeDeserters_CriticalIntelPaid", "RWS_VfeDeserters_CriticalIntelPaidDesc"), Family, Install),
        ];
    }

    private static void Install(Harmony harmony)
    {
        var dialog = AccessTools.TypeByName("VFED.Dialog_DeserterNetwork");
        var defs = dialog?.Assembly.GetType("VFED.VFED_DefOf", throwOnError: false);
        if (dialog is null || defs is null || !typeof(Window).IsAssignableFrom(dialog)
            || !string.Equals(dialog.Assembly.GetName().Name, "VFED", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Selected Deserters assembly is unavailable");

        // The (int, bool) overload delegates to this one.
        var spend = AccessTools.DeclaredMethod(dialog, "TrySpendIntel", [typeof(int), typeof(int)]);
        var map = AccessTools.DeclaredField(dialog, "Map");
        var normal = AccessTools.DeclaredField(defs, "VFED_Intel");
        var critical = AccessTools.DeclaredField(defs, "VFED_CriticalIntel");
        if (spend is not { IsStatic: false } || spend.ReturnType != typeof(bool)
            || map is not { IsStatic: false } || map.FieldType != typeof(Map)
            || normal is not { IsStatic: true } || normal.FieldType != typeof(ThingDef)
            || critical is not { IsStatic: true } || critical.FieldType != typeof(ThingDef))
            throw new InvalidOperationException("Deserters payment signature changed");

        dialogMap = map;
        normalDef = normal;
        criticalDef = critical;
        try
        {
            harmony.Patch(spend,
                prefix: new HarmonyMethod(typeof(VfeDesertersStatistics), nameof(BeforePayment)),
                postfix: new HarmonyMethod(typeof(VfeDesertersStatistics), nameof(AfterPayment)));
        }
        catch
        {
            harmony.Unpatch(spend, HarmonyPatchType.All, harmony.Id);
            dialogMap = normalDef = criticalDef = null;
            throw;
        }
    }

    private static bool CanObserve()
        => MetricGates.IsRecording(Unit) && Current.ProgramState == ProgramState.Playing
            && Scribe.mode == LoadSaveMode.Inactive && Find.TickManager is not null;

    private static long Stock(Map map, ThingDef? def)
        => def is null ? 0 : map.listerThings.ThingsOfDef(def)
            .Where(t => t.Spawned && t.stackCount > 0)
            .Sum(t => (long)t.stackCount);

    private static void BeforePayment(object __instance, int __0, int __1, out PaymentBefore? __state)
    {
        __state = null;
        if (!CanObserve() || __0 < 0 || __1 < 0 || (__0 == 0 && __1 == 0))
            return;
        try
        {
            if (dialogMap?.GetValue(__instance) is not Map { IsPlayerHome: true } map)
                return;
            var normal = __0 > 0 ? normalDef?.GetValue(null) as ThingDef : null;
            var critical = __1 > 0 ? criticalDef?.GetValue(null) as ThingDef : null;
            if ((__0 > 0 && normal is null) || (__1 > 0 && critical is null))
                return;
            __state = new PaymentBefore(map, normal, Stock(map, normal), critical, Stock(map, critical));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterPayment(int __0, int __1, bool __result, bool __runOriginal, PaymentBefore? __state)
    {
        if (!__runOriginal || !__result || __state is null || !CanObserve())
            return;
        try
        {
            // The game debit API returns void and can stop early when stocks
            // changed while the dialog was open. Compare physical map stock.
            var normal = VfeDesertersRules.IntelPaid(__result, __state.NormalCount, Stock(__state.Map, __state.Normal), __0);
            var critical = VfeDesertersRules.IntelPaid(__result, __state.CriticalCount, Stock(__state.Map, __state.Critical), __1);
            if (normal == 0 && critical == 0)
                return;
            GameFacts.Record(Unit, state =>
            {
                OptionalTransitionRecording.RecordCount(state, "normal", normal);
                OptionalTransitionRecording.RecordCount(state, "critical", critical);
            });
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }
}
