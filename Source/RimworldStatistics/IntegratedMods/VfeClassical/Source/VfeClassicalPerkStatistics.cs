using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.VfeClassical;

internal static class VfeClassicalPerkStatistics
{
    private const string Owner = "OskarPotocki.VFE.Classical";
    private const string Unit = "mod.oskarpotocki.vfe.classical.perks";
    private static FieldInfo? activePerks;
    [ThreadStatic] private static bool ordinaryFavor;
    private static MethodInfo? gainFavor;
    private static Action<object, Pawn, Faction>? applyFavor;

    internal static ModMetricAdapterRegistration Build() => new(new ModMetricDescriptor
    {
        ProviderId = "rws.classicalperks",
        OwnerPackageId = Owner,
        FamilyId = "perks",
        MetricName = "acquired",
        Source = MetricObservationSource.Checkpoint,
        Revision = 1,
        Category = MetricCategory.Social,
        Unit = MetricUnit.Count,
        Overall = OverallAggregation.Sum,
        Selection = ColonySelection.Representative,
        HasBreakdown = true,
        RequiredPackages = [new ModPackageRequirement(Owner)],
        LabelKey = "RWS_VfeClassical_Perks",
        DescriptionKey = "RWS_VfeClassical_PerksDesc",
    }, new LiveFamily
    {
        Name = Unit,
        Revision = 1,
        Build = output => OptionalTransitionRecording.PutCount(output, "acquired", Unit + ".acquired", withBreakdown: true),
    }, Install);

    private static void Install(Harmony harmony)
    {
        var manager = AccessTools.TypeByName("VFEC.Perks.GameComponent_PerkManager");
        var type = manager?.Assembly.GetType("VFEC.Perks.PerkDef");
        var senators = manager?.Assembly.GetType("VFEC.Senators.WorldComponent_Senators");
        var senatorDialog = manager?.Assembly.GetType("VFEC.Senators.Dialog_SenatorInfo");
        var quest = manager?.Assembly.GetType("VFEC.Senators.QuestPart_GainFavor");
        var gain = senators is null ? null : AccessTools.DeclaredMethod(senators, "GainFavorOf", [typeof(Pawn), typeof(Faction)]);
        var add = manager is null || type is null ? null : AccessTools.DeclaredMethod(manager, "AddPerk", [type]);
        var perks = manager is null ? null : AccessTools.DeclaredField(manager, "ActivePerks");
        var draw = senatorDialog is null ? null : AccessTools.DeclaredMethod(senatorDialog, "DrawSenatorInfo");
        var receive = quest is null ? null : AccessTools.DeclaredMethod(quest, "Notify_QuestSignalReceived", [typeof(Signal)]);
        if (manager?.Assembly.GetName().Name != "VFEC" || type is null || !typeof(Def).IsAssignableFrom(type) ||
            add?.ReturnType != typeof(void) || gain?.ReturnType != typeof(void) ||
            perks?.FieldType != typeof(HashSet<>).MakeGenericType(type) || draw is null || receive is null ||
            PatchProcessor.GetOriginalInstructions(gain).Count(code => code.Calls(add)) != 3 ||
            PatchProcessor.GetOriginalInstructions(draw).Count(code => code.Calls(gain)) != 1 ||
            PatchProcessor.GetOriginalInstructions(receive).Count(code => code.Calls(gain)) != 1)
            throw new InvalidOperationException("Classical perk acquisition signature changed");

        activePerks = perks;
        gainFavor = gain;
        var component = System.Linq.Expressions.Expression.Parameter(typeof(object));
        var pawn = System.Linq.Expressions.Expression.Parameter(typeof(Pawn));
        var faction = System.Linq.Expressions.Expression.Parameter(typeof(Faction));
        applyFavor = System.Linq.Expressions.Expression.Lambda<Action<object, Pawn, Faction>>(
            System.Linq.Expressions.Expression.Call(System.Linq.Expressions.Expression.Convert(component, senators!), gain, pawn, faction),
            component, pawn, faction).Compile();
        try
        {
            var scope = new HarmonyMethod(typeof(VfeClassicalPerkStatistics), nameof(ObserveOrdinaryFavor));
            harmony.Patch(draw, transpiler: scope);
            harmony.Patch(receive, transpiler: scope);
            harmony.Patch(add,
                prefix: new HarmonyMethod(typeof(VfeClassicalPerkStatistics), nameof(Before)),
                postfix: new HarmonyMethod(typeof(VfeClassicalPerkStatistics), nameof(After)));
        }
        catch
        {
            harmony.Unpatch(add, HarmonyPatchType.All, harmony.Id);
            var scope = AccessTools.DeclaredMethod(typeof(VfeClassicalPerkStatistics), nameof(ObserveOrdinaryFavor));
            harmony.Unpatch(draw, scope);
            harmony.Unpatch(receive, scope);
            throw;
        }
    }

    private static IEnumerable<CodeInstruction> ObserveOrdinaryFavor(IEnumerable<CodeInstruction> instructions)
        => OptionalTransitionCallSite.Replace(instructions, gainFavor!,
            AccessTools.DeclaredMethod(typeof(VfeClassicalPerkStatistics), nameof(GainAndObserve)), 1);

    private static void GainAndObserve(object component, Pawn pawn, Faction faction)
    {
        var previous = ordinaryFavor;
        ordinaryFavor = OptionalTransitionCallSite.Observing(Unit);
        try { applyFavor!(component, pawn, faction); }
        finally { ordinaryFavor = previous; }
    }

    private static bool Contains(object manager, Def perk)
        => activePerks?.GetValue(manager) is IEnumerable items &&
           items.Cast<object>().Any(item => ReferenceEquals(item, perk));

    private static void Before(object __instance, Def __0, out bool __state)
    {
        __state = false;
        if (__0 is null || !ordinaryFavor)
            return;
        try { __state = !Contains(__instance, __0); }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void After(object __instance, Def __0, bool __runOriginal, bool __state)
    {
        if (!__runOriginal || !__state)
            return;
        try
        {
            if (!Contains(__instance, __0))
                return;
            var key = OptionalTransitionRecording.QualifiedDefKey(__0.modContentPack?.PackageId, __0.defName);
            GameFacts.Record(Unit, state => OptionalTransitionRecording.RecordCount(
                state, "acquired", key: key, withBreakdown: true));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }
}
