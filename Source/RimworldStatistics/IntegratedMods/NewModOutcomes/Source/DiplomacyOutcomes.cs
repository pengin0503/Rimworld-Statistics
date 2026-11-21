using System.Collections;
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
    private static Func<object, IList?>? getTreaties;
    private static Func<object, Faction?>? getTreatyFaction;
    private static Func<object, Def?>? getTreatyDef;
    private static Func<object, Settlement?>? getSettlement;
    private static Func<object, bool>? getVassal;
    private static Func<object, bool>? getRebelling;
    private static Func<object, Faction?>? getAttacker;
    [ThreadStatic] private static int vassalTabDepth;

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
        var treaties = RequireField(world, "treaties", typeof(List<>).MakeGenericType(active));
        var treatyDef = RequireField(active, "def", definition);
        var treatyFaction = RequireField(active, "faction", typeof(Faction));
        if (!typeof(Def).IsAssignableFrom(definition))
            throw new InvalidOperationException("RimPacts treaty definition changed");
        var sign = OutcomeMetric.Method(world, "SignTreaty", active, typeof(Faction), definition, typeof(bool));

        getTreaties = OptionalTransitionCallSite.CompileFieldGetter<IList?>(treaties);
        getTreatyDef = OptionalTransitionCallSite.CompileFieldGetter<Def?>(treatyDef);
        getTreatyFaction = OptionalTransitionCallSite.CompileFieldGetter<Faction?>(treatyFaction);
        var patcher = new ScopedHarmonyPatcher(harmony);
        try
        {
            patcher.Patch(sign, typeof(DiplomacyOutcomes), nameof(BeforeTreaty), nameof(AfterTreaty));
        }
        catch
        {
            getTreaties = null;
            getTreatyDef = null;
            getTreatyFaction = null;
            throw;
        }
    }

    private static void BeforeTreaty(object __instance, Faction? __0, Def? __1, out bool __state)
    {
        __state = false;
        if (__0 is not { IsPlayer: false } || __1 is null || !OutcomeMetric.Observing(Treaty))
            return;
        try
        {
            // SignTreaty inserts a new object even when a caller requests the same pair twice.
            if (getTreaties?.Invoke(__instance) is not { } list)
                return;
            for (var i = 0; i < list.Count; i++)
            {
                var record = list[i];
                if (record is not null
                    && ReferenceEquals(getTreatyFaction!(record), __0)
                    && ReferenceEquals(getTreatyDef!(record), __1))
                    return;
            }
            __state = true;
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
            if (ReferenceEquals(getTreatyFaction?.Invoke(__result), __0) &&
                ReferenceEquals(getTreatyDef?.Invoke(__result), __1) &&
                getTreaties?.Invoke(__instance) is { } list && list.Contains(__result))
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
        var vassalTab = OutcomeMetric.Bind(assembly + ".VassalageTab", assembly);
        var settlement = RequireField(state, "settlement", typeof(Settlement));
        var vassal = RequireField(state, "vassal", typeof(bool));
        var rebelling = RequireField(state, "rebelling", typeof(bool));
        var attacker = RequireField(campaign, "attacker", typeof(Faction));

        var establish = OutcomeMetric.Method(vassalage, "Establish", typeof(void), state);
        var expedition = OutcomeMetric.Method(operations, "EstablishAiVassal", typeof(bool), campaign, state);

        getSettlement = OptionalTransitionCallSite.CompileFieldGetter<Settlement?>(settlement);
        getVassal = OptionalTransitionCallSite.CompileFieldGetter<bool>(vassal);
        getRebelling = OptionalTransitionCallSite.CompileFieldGetter<bool>(rebelling);
        getAttacker = OptionalTransitionCallSite.CompileFieldGetter<Faction?>(attacker);

        var patcher = new ScopedHarmonyPatcher(harmony);
        try
        {
            patcher.Patch(establish, typeof(DiplomacyOutcomes), nameof(BeforeEstablish), nameof(AfterEstablish));
            patcher.Patch(expedition, typeof(DiplomacyOutcomes), nameof(BeforeExpedition), nameof(AfterExpedition));
            foreach (var caller in OptionalTransitionCallSite.FindCallersInTypeTree(vassalTab, establish))
                patcher.Patch(caller, typeof(DiplomacyOutcomes), prefix: nameof(EnterVassalTab), finalizer: nameof(ExitVassalTab));
        }
        catch
        {
            getSettlement = null;
            getVassal = null;
            getRebelling = null;
            getAttacker = null;
            throw;
        }
    }

    private static void EnterVassalTab() => vassalTabDepth++;

    private static Exception? ExitVassalTab(Exception? __exception)
    {
        if (vassalTabDepth > 0)
            vassalTabDepth--;
        return __exception;
    }

    private static bool ActiveVassal(object state) =>
        getVassal is not null && getRebelling is not null
        && getVassal(state) && !getRebelling(state);

    private static void BeforeEstablish(object? __0, out Settlement? __state)
    {
        __state = null;
        if (__0 is null || !OutcomeMetric.Observing(Vassal))
            return;
        try
        {
            if (Prefs.DevMode && vassalTabDepth > 0)
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
            if (getAttacker?.Invoke(__0) is { IsPlayer: true })
                __state = Candidate(__1);
        }
        catch (Exception error) { LiveMetrics.Fail(Vassal, error); }
    }

    private static Settlement? Candidate(object state) => !ActiveVassal(state) &&
        getSettlement?.Invoke(state) is { Spawned: true } site ? site : null;

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
            if (ReferenceEquals(getSettlement?.Invoke(state), site) && ActiveVassal(state))
                OutcomeMetric.Record(Vassal);
        }
        catch (Exception error) { LiveMetrics.Fail(Vassal, error); }
    }

    private static System.Reflection.FieldInfo RequireField(Type owner, string name, Type type)
    {
        var field = AccessTools.DeclaredField(owner, name);
        return field?.FieldType == type ? field : throw new InvalidOperationException($"{owner.Name}.{name} changed");
    }
}
