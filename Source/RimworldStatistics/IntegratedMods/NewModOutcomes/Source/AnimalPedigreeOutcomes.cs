using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.NewModOutcomes;

internal static class AnimalPedigreeOutcomes
{
    private const string Unit = "mod.mlie.animalgenetics.pedigreebirths";
    private static Type pawnComp = null!;
    private static FieldInfo genetics = null!;
    private static FieldInfo mother = null!;
    private static FieldInfo father = null!;

    internal static ModMetricAdapterRegistration Build() =>
        OutcomeMetric.Count("Mlie.AnimalGenetics", "pedigreebirths", "RWS_NewMod_AnimalPedigree", "RWS_NewMod_AnimalPedigreeDesc",
            MetricCategory.Animals, Install, breakdown: true);

    private static void Install(Harmony harmony)
    {
        const string assembly = "AnimalGenetics";
        pawnComp = OutcomeMetric.Bind(assembly + ".PawnGeneticInformation", assembly);
        var info = OutcomeMetric.Bind(assembly + ".GeneticInformation", assembly);
        genetics = AccessTools.Field(pawnComp, "GeneticInformation");
        mother = AccessTools.DeclaredField(info, "Mother");
        father = AccessTools.DeclaredField(info, "Father");
        if (!typeof(ThingComp).IsAssignableFrom(pawnComp) || genetics?.FieldType != info ||
            mother?.FieldType != info || father?.FieldType != info)
            throw new InvalidOperationException("Animal Genetics pedigree signature changed");

        var method = OutcomeMetric.Method(typeof(PawnUtility), nameof(PawnUtility.TrySpawnHatchedOrBornPawn),
            typeof(bool), typeof(Pawn), typeof(Thing));
        OutcomeMetric.Patch(harmony, method, typeof(AnimalPedigreeOutcomes));
    }

    private static void Before(Pawn? __0, out bool __state)
        => __state = __0 is { Spawned: false, Dead: false } && __0.RaceProps.Animal &&
            OutcomeMetric.Observing(Unit);

    private static void After(Pawn? __0, bool __runOriginal, bool __result, bool __state)
    {
        if (!__state || !__runOriginal || !__result || __0 is not { Spawned: true, Dead: false } pawn ||
            !GameFacts.IsPlayer(pawn))
            return;
        try
        {
            var comp = pawn.AllComps.FirstOrDefault(pawnComp.IsInstanceOfType);
            if (comp is null || genetics.GetValue(comp) is not { } pedigree)
                return;

            // GeneRecords is lazy and can consume the game's RNG. These references are
            // already assigned by the upstream birth/hatch context.
            if (AnimalPedigreeRules.IsRecorded(__result, pawn.RaceProps.Animal,
                mother.GetValue(pedigree) is not null, father.GetValue(pedigree) is not null))
                OutcomeMetric.Record(Unit, pawn.kindDef, breakdown: true);
        }
        catch (Exception error) { LiveMetrics.Fail(Unit, error); }
    }
}
