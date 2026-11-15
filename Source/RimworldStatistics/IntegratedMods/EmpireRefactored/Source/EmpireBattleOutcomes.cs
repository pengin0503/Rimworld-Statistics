using System.Reflection;
using HarmonyLib;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.NewModOutcomes;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.EmpireRefactored;

internal static class EmpireBattleOutcomes
{
    private const string Unit = "mod.matathias.empire.battle_victories";
    private static FieldInfo kind = null!;
    private static FieldInfo operationId = null!;
    private static FieldInfo result = null!;

    internal static ModMetricAdapterRegistration Build() =>
        OutcomeMetric.Count("Matathias.Empire", "battle_victories", "RWS_EmpireVictories", "RWS_EmpireVictoriesDesc",
            MetricCategory.Combat, Install);

    private static void Install(Harmony harmony)
    {
        const string assembly = "Empire";
        var registry = OutcomeMetric.Bind("FactionColonies.LifecycleRegistry", assembly);
        var operation = OutcomeMetric.Bind("FactionColonies.MilitaryOperation", assembly);
        var battle = OutcomeMetric.Bind("FactionColonies.BattleResult", assembly);
        var operationKind = OutcomeMetric.Bind("FactionColonies.MilitaryJobDef", assembly);
        kind = RequireField(operation, "kind", operationKind);
        operationId = RequireField(operation, "id", typeof(int));
        result = RequireField(operation, "result", battle);
        var resolved = OutcomeMetric.Method(registry, "InvokeOnBattleResolved", typeof(void), operation, typeof(bool), battle);
        OutcomeMetric.Patch(harmony, resolved, typeof(EmpireBattleOutcomes), null, nameof(Resolved));
    }

    private static FieldInfo RequireField(Type owner, string name, Type type)
        => AccessTools.DeclaredField(owner, name) is { } field && field.FieldType == type
            ? field : throw new InvalidOperationException($"Empire battle field changed: {name}");

    private static void Resolved(object? __0, bool __1, object? __2, bool __runOriginal)
    {
        if (!__runOriginal || !__1 || __0 is null || __2 is null || !OutcomeMetric.Observing(Unit))
            return;
        try
        {
            if (!ReferenceEquals(result.GetValue(__0), __2) || kind.GetValue(__0) is not Def operation ||
                operationId.GetValue(__0) is not int id)
                return;
            GameFacts.Record(Unit, state => EmpireOutcomeRules.RecordVictory(state, id, operation.defName));
        }
        catch (Exception error) { LiveMetrics.Fail(Unit, error); }
    }
}
