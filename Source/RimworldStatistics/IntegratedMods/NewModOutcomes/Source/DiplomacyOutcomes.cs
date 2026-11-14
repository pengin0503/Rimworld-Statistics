using System.Collections;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.NewModOutcomes;

internal static class DiplomacyOutcomes
{
    private const string Treaty = "mod.wowgag.rimpacts.treaties";
    private const string Vassal = "mod.jaeger972.factionterritories2.vassalizations";
    private static FieldInfo treaties = null!;
    private static FieldInfo treatyFaction = null!;
    private static FieldInfo treatyDef = null!;
    private static FieldInfo settlement = null!;
    private static FieldInfo vassal = null!;
    private static FieldInfo rebelling = null!;
    private static FieldInfo attacker = null!;
    private static Type vassalTab = null!;

    internal static ModMetricAdapterRegistration Treaties() =>
        OutcomeMetric.Count("wowgag.RimPacts", "treaties", "RWS_NewMod_Treaties", "RWS_NewMod_TreatiesDesc",
            MetricCategory.Social, InstallTreaties, breakdown: true);

    internal static ModMetricAdapterRegistration Vassalizations() =>
        OutcomeMetric.Count("jaeger972.factionterritories2", "vassalizations",
            "RWS_NewMod_Vassalizations", "RWS_NewMod_VassalizationsDesc",
            MetricCategory.Social, InstallVassals, dependencies: ["jaeger972.utilityframework"]);

    private static void InstallTreaties(Harmony harmony)
    {
        const string assembly = "RimPacts";
        var world = OutcomeMetric.Bind(assembly + ".WorldComponent_RimPacts", assembly);
        var definition = OutcomeMetric.Bind(assembly + ".TreatyDef", assembly);
        var active = OutcomeMetric.Bind(assembly + ".ActiveTreaty", assembly);
        treaties = RequireField(world, "treaties", typeof(List<>).MakeGenericType(active));
        treatyDef = RequireField(active, "def", definition);
        treatyFaction = RequireField(active, "faction", typeof(Faction));
        if (!typeof(Def).IsAssignableFrom(definition))
            throw new InvalidOperationException("RimPacts treaty definition changed");
        var sign = OutcomeMetric.Method(world, "SignTreaty", active, typeof(Faction), definition, typeof(bool));
        OutcomeMetric.Patch(harmony, sign, typeof(DiplomacyOutcomes), nameof(BeforeTreaty), nameof(AfterTreaty));
    }

    private static void BeforeTreaty(object __instance, Faction? __0, Def? __1, out bool __state)
    {
        __state = false;
        if (__0 is not { IsPlayer: false } || __1 is null || !OutcomeMetric.Observing(Treaty))
            return;
        try
        {
            // SignTreaty inserts a new object even when a caller requests the same pair twice.
            __state = treaties.GetValue(__instance) is IList list &&
                !list.Cast<object>().Any(record => record is not null &&
                    ReferenceEquals(treatyFaction.GetValue(record), __0) &&
                    ReferenceEquals(treatyDef.GetValue(record), __1));
        }
        catch (Exception error) { LiveMetrics.Fail(Treaty, error); }
    }

    private static void AfterTreaty(object __instance, Faction __0, Def __1,
        object? __result, bool __runOriginal, bool __state)
    {
        if (!__state || !__runOriginal || __result is null)
            return;
        try
        {
            if (ReferenceEquals(treatyFaction.GetValue(__result), __0) &&
                ReferenceEquals(treatyDef.GetValue(__result), __1) &&
                treaties.GetValue(__instance) is IList list && list.Contains(__result))
                OutcomeMetric.Record(Treaty, __1, breakdown: true);
        }
        catch (Exception error) { LiveMetrics.Fail(Treaty, error); }
    }

    private static void InstallVassals(Harmony harmony)
    {
        const string assembly = "FactionTerritories2";
        var state = OutcomeMetric.Bind(assembly + ".SettlementState", assembly);
        var operations = OutcomeMetric.Bind(assembly + ".Operations", assembly);
        var campaign = OutcomeMetric.Bind(assembly + ".Campaign", assembly);
        var vassalage = OutcomeMetric.Bind(assembly + ".Vassalage", assembly);
        vassalTab = OutcomeMetric.Bind(assembly + ".VassalageTab", assembly);
        settlement = RequireField(state, "settlement", typeof(Settlement));
        vassal = RequireField(state, "vassal", typeof(bool));
        rebelling = RequireField(state, "rebelling", typeof(bool));
        attacker = RequireField(campaign, "attacker", typeof(Faction));

        var establish = OutcomeMetric.Method(vassalage, "Establish", typeof(void), state);
        var expedition = OutcomeMetric.Method(operations, "EstablishAiVassal", typeof(bool), campaign, state);
        try
        {
            OutcomeMetric.Patch(harmony, establish, typeof(DiplomacyOutcomes), nameof(BeforeEstablish), nameof(AfterEstablish));
            OutcomeMetric.Patch(harmony, expedition, typeof(DiplomacyOutcomes), nameof(BeforeExpedition), nameof(AfterExpedition));
        }
        catch
        {
            harmony.Unpatch(establish, AccessTools.DeclaredMethod(typeof(DiplomacyOutcomes), nameof(BeforeEstablish)));
            harmony.Unpatch(establish, AccessTools.DeclaredMethod(typeof(DiplomacyOutcomes), nameof(AfterEstablish)));
            throw;
        }
    }

    private static bool ActiveVassal(object state) =>
        vassal.GetValue(state) is true && rebelling.GetValue(state) is false;

    private static void BeforeEstablish(object? __0, out Settlement? __state)
    {
        __state = null;
        if (__0 is null || !OutcomeMetric.Observing(Vassal))
            return;
        try
        {
            if (Prefs.DevMode && new StackTrace(1, false).GetFrames().Any(frame =>
                Harmony.GetOriginalMethodFromStackframe(frame)?.DeclaringType == vassalTab))
                return;
            __state = Candidate(__0);
        }
        catch (Exception error) { LiveMetrics.Fail(Vassal, error); }
    }

    private static void BeforeExpedition(object? __0, object? __1, out Settlement? __state)
    {
        __state = null;
        if (__0 is null || __1 is null || !OutcomeMetric.Observing(Vassal))
            return;
        try
        {
            if (attacker.GetValue(__0) is Faction { IsPlayer: true })
                __state = Candidate(__1);
        }
        catch (Exception error) { LiveMetrics.Fail(Vassal, error); }
    }

    private static Settlement? Candidate(object state) => !ActiveVassal(state) &&
        settlement.GetValue(state) is Settlement { Spawned: true } site ? site : null;

    private static void AfterEstablish(object __0, bool __runOriginal, Settlement? __state)
    {
        if (__runOriginal)
            RecordVassal(__0, __state);
    }

    private static void AfterExpedition(object __1, bool __result, bool __runOriginal, Settlement? __state)
    {
        if (__runOriginal && __result)
            RecordVassal(__1, __state);
    }

    private static void RecordVassal(object state, Settlement? site)
    {
        if (site is not { Spawned: true })
            return;
        try
        {
            if (ReferenceEquals(settlement.GetValue(state), site) && ActiveVassal(state))
                OutcomeMetric.Record(Vassal);
        }
        catch (Exception error) { LiveMetrics.Fail(Vassal, error); }
    }

    private static FieldInfo RequireField(Type owner, string name, Type type)
    {
        var field = AccessTools.DeclaredField(owner, name);
        return field?.FieldType == type ? field : throw new InvalidOperationException($"{owner.Name}.{name} changed");
    }
}
