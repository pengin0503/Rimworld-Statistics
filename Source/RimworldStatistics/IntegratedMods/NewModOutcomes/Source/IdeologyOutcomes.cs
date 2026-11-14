using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.NewModOutcomes;

internal static class IdeologyOutcomes
{
    private const string Ideology = "Ludeon.RimWorld.Ideology";
    private const string SchismUnit = "mod.vanillaexpanded.viesas.schisms";
    private const string FuneralUnit = "mod.sarg.alphamemes.dreadnoughtfunerals";
    private static FieldInfo proposedIdeo = null!;
    private static FieldInfo converts = null!;
    private static HediffDef lifeSupport = null!;
    private sealed record Schism(Ideo NewIdeo, Pawn[] Converts);

    internal static ModMetricAdapterRegistration Schisms() =>
        OutcomeMetric.Count("VanillaExpanded.VIESAS", "schisms", "RWS_NewMod_Schisms",
            MetricCategory.Social, InstallSchism, dlc: Ideology);

    internal static ModMetricAdapterRegistration Funerals() =>
        OutcomeMetric.Count("Sarg.AlphaMemes", "dreadnoughtfunerals", "RWS_NewMod_Funeral",
            MetricCategory.Social, InstallFuneral, dlc: Ideology, dependencies: ["OskarPotocki.VFE.Pirates"]);

    private static void InstallSchism(Harmony harmony)
    {
        var type = OutcomeMetric.Bind("VIESAS.Window_ConfigureIdeo", "VIESAS");
        proposedIdeo = AccessTools.DeclaredField(type, "newIdeo");
        converts = AccessTools.DeclaredField(type, "colonistsToConvert");
        if (!typeof(Window).IsAssignableFrom(type) || proposedIdeo?.FieldType != typeof(Ideo) ||
            converts?.FieldType != typeof(List<Pawn>))
            throw new InvalidOperationException("Schism window signature changed");
        OutcomeMetric.Patch(harmony, OutcomeMetric.Method(type, "Close", typeof(void), typeof(bool)),
            typeof(IdeologyOutcomes), "BeforeSchism", "AfterSchism");
    }

    private static void BeforeSchism(Window __instance, out Schism? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(SchismUnit))
            return;
        try
        {
            if (proposedIdeo.GetValue(__instance) is not Ideo ideology || Find.IdeoManager.IdeosListForReading.Contains(ideology) ||
                converts.GetValue(__instance) is not List<Pawn> pawns)
                return;
            var changing = pawns.Where(pawn => GameFacts.IsPlayer(pawn) && pawn.ideo is not null &&
                pawn.Ideo != ideology).ToArray();
            if (changing.Length > 0)
                __state = new Schism(ideology, changing);
        }
        catch (Exception error) { LiveMetrics.Fail(SchismUnit, error); }
    }

    private static void AfterSchism(bool __runOriginal, Schism? __state)
    {
        if (!__runOriginal || __state is not { } schism)
            return;
        try
        {
            if (Find.IdeoManager.IdeosListForReading.Contains(schism.NewIdeo) &&
                schism.Converts.Any(pawn => pawn.Ideo == schism.NewIdeo))
                OutcomeMetric.Record(SchismUnit);
        }
        catch (Exception error) { LiveMetrics.Fail(SchismUnit, error); }
    }

    private static void InstallFuneral(Harmony harmony)
    {
        var type = OutcomeMetric.Bind("AlphaMemes.RitualOutcomeEffectWorker_FuneralDreadnought", "AlphaMemes");
        lifeSupport = DefDatabase<HediffDef>.GetNamed("AM_WarCasketLifeSupport");
        var original = OutcomeMetric.Method(type, "ApplyOn", typeof(void), typeof(Pawn), typeof(Corpse),
            typeof(List<Thing>), typeof(Dictionary<Pawn, int>), typeof(LordJob_Ritual), typeof(RitualOutcomePossibility));
        OutcomeMetric.Patch(harmony, original, typeof(IdeologyOutcomes), "BeforeFuneral", "AfterFuneral");
    }

    private static void BeforeFuneral(Corpse? __1, out Pawn? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(FuneralUnit) || __1?.InnerPawn is not { Dead: true } pawn ||
            !GameFacts.IsPlayer(pawn))
            return;
        if (!pawn.health.hediffSet.HasHediff(lifeSupport))
            __state = pawn;
    }

    private static void AfterFuneral(bool __runOriginal, Pawn? __state)
    {
        if (!__runOriginal || __state is not { Dead: false } pawn)
            return;
        try
        {
            if (GameFacts.IsPlayer(pawn) && pawn.health.hediffSet.HasHediff(lifeSupport) &&
                pawn.apparel?.WornApparel.Any(apparel => apparel.def.defName.StartsWith("VFEP_Warcasket_", StringComparison.Ordinal)) == true)
                OutcomeMetric.Record(FuneralUnit);
        }
        catch (Exception error) { LiveMetrics.Fail(FuneralUnit, error); }
    }
}
