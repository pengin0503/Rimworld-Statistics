using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.NewModOutcomes;

internal static class ResleevingOutcomes
{
    private const string Unit = "mod.hlx.ultratechalteredcarbon.resleeving";
    private static Type stack = null!;
    private static Type implant = null!;
    private static PropertyInfo sourceData = null!;
    private static PropertyInfo implantData = null!;
    private static PropertyInfo containsData = null!;
    private static MethodInfo emptySleeve = null!;
    private sealed record Transfer(Thing Source, object Data);

    internal static ModMetricAdapterRegistration Build() =>
        OutcomeMetric.Count("hlx.UltratechAlteredCarbon", "resleeving", "RWS_NewMod_Resleeving",
            MetricCategory.Medicine, Install, dlc: "Ludeon.RimWorld.Biotech",
            dependencies: ["OskarPotocki.VanillaFactionsExpanded.Core"]);

    private static void Install(Harmony harmony)
    {
        const string assembly = "AlteredCarbon";
        var recipe = OutcomeMetric.Bind(assembly + ".Recipe_InstallNeuralStack", assembly);
        var utils = OutcomeMetric.Bind(assembly + ".AC_Utils", assembly);
        stack = OutcomeMetric.Bind(assembly + ".NeuralStack", assembly);
        implant = OutcomeMetric.Bind(assembly + ".Hediff_NeuralStack", assembly);
        var data = OutcomeMetric.Bind(assembly + ".NeuralData", assembly);
        sourceData = AccessTools.Property(stack, "NeuralData");
        implantData = AccessTools.DeclaredProperty(implant, "NeuralData");
        containsData = AccessTools.DeclaredProperty(data, "ContainsData");
        emptySleeve = OutcomeMetric.Method(utils, "IsEmptySleeve", typeof(bool), typeof(Pawn));
        if (sourceData?.PropertyType != data || implantData?.PropertyType != data ||
            containsData?.PropertyType != typeof(bool))
            throw new InvalidOperationException("Neural stack data signature changed");
        var method = OutcomeMetric.Method(recipe, "ApplyOnPawn", typeof(void), typeof(Pawn),
            typeof(BodyPartRecord), typeof(Pawn), typeof(List<Thing>), typeof(Bill));
        OutcomeMetric.Patch(harmony, method, typeof(ResleevingOutcomes));
    }

    private static void Before(Pawn __0, Pawn? __2, List<Thing> __3, out Transfer? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(Unit) || !GameFacts.IsPlayer(__2) || __0.Dead)
            return;
        try
        {
            if (emptySleeve.Invoke(null, [__0]) is true &&
                __3.FirstOrDefault(stack.IsInstanceOfType) is { Destroyed: false } source &&
                sourceData.GetValue(source) is { } data && containsData.GetValue(data) is true)
                __state = new Transfer(source, data);
        }
        catch (Exception error) { LiveMetrics.Fail(Unit, error); }
    }

    private static void After(Pawn __0, bool __runOriginal, Transfer? __state)
    {
        if (!__runOriginal || __0.Dead || !GameFacts.IsPlayer(__0) ||
            __state is not { Source.Destroyed: true } transfer)
            return;
        try
        {
            if (emptySleeve.Invoke(null, [__0]) is false &&
                __0.health.hediffSet.hediffs.Any(hediff => implant.IsInstanceOfType(hediff) &&
                    ReferenceEquals(implantData.GetValue(hediff), transfer.Data)))
                OutcomeMetric.Record(Unit);
        }
        catch (Exception error) { LiveMetrics.Fail(Unit, error); }
    }
}
