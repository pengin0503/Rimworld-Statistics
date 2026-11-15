using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.NewModOutcomes;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.PopularModOutcomes;

internal static class PopularOutcomes
{
    private const string Farming = "mod.vanillaexpanded.vfefarming.sprinkler_growth";
    private const string Hospital = "mod.adamas.hospital.patient_admissions";
    private const string Events = "mod.vanillaexpanded.vee.multistage_completed";
    private const string Dryads = "mod.vanillaexpanded.ideo.dryads.dryad_spawns";
    private const string Archon = "mod.vanillaracesexpanded.archon.archon_returns";
    private static FieldInfo patients = null!;

    internal static IEnumerable<ModMetricAdapterRegistration> Build()
    {
        yield return LandformOutcomes.Build();
        if (!ModsConfig.OdysseyActive)
            yield return FishCatchOutcomes.Build();
        yield return RaceBirthOutcomes.SauridEggs();
        yield return RaceBirthOutcomes.PhytokinSaplings();
        yield return GeneAndPuppetOutcomes.GeneImplants();
        yield return GeneAndPuppetOutcomes.Puppets();
        yield return GeneAndPuppetOutcomes.MindTransfers();
        yield return OutcomeMetric.Count("VanillaExpanded.VFEFarming", "sprinkler_growth",
            "RWS_SprinklerGrowth", "RWS_SprinklerGrowthDesc", MetricCategory.Work, InstallSprinklers, breakdown: true);
        yield return OutcomeMetric.Count("Adamas.Hospital", "patient_admissions",
            "RWS_HospitalAdmissions", "RWS_HospitalAdmissionsDesc", MetricCategory.Medicine, InstallHospital);
        yield return OutcomeMetric.Count("VanillaExpanded.VEE", "multistage_completed",
            "RWS_MultistageEvents", "RWS_MultistageEventsDesc", MetricCategory.Colony, InstallEvents, breakdown: true);
        yield return OutcomeMetric.Count("VanillaExpanded.Ideo.Dryads", "dryad_spawns",
            "RWS_AwakenedDryads", "RWS_AwakenedDryadsDesc", MetricCategory.Ideology, InstallDryads,
            breakdown: true, dlc: AcquisitionUnits.Ideology);
        yield return OutcomeMetric.Count("VanillaRacesExpanded.Archon", "archon_returns",
            "RWS_ArchonReturns", "RWS_ArchonReturnsDesc", MetricCategory.Biotech, InstallArchon,
            dlc: AcquisitionUnits.Biotech, dependencies: [AcquisitionUnits.Royalty]);
    }

    private static void InstallSprinklers(Harmony harmony)
    {
        var manager = OutcomeMetric.Bind("VFEF.VFEF_SprinklersManager", "VFEF");
        var boost = OutcomeMetric.Method(manager, "BoostPlantAt", typeof(void), typeof(IntVec3));
        OutcomeMetric.Patch(harmony, boost, typeof(PopularOutcomes), nameof(BeforeBoost), nameof(AfterBoost));
    }

    private static void BeforeBoost(MapComponent __instance, IntVec3 __0, out (Plant? Plant, float Growth) __state)
    {
        __state = default;
        if (OutcomeMetric.Observing(Farming) && __instance.map is { IsPlayerHome: true } map &&
            __0.InBounds(map) && __0.GetPlant(map) is { } plant)
            __state = (plant, plant.Growth);
    }

    private static void AfterBoost(bool __runOriginal, (Plant? Plant, float Growth) __state)
    {
        if (__runOriginal && __state.Plant is { Spawned: true } plant && OutcomeMetric.Observing(Farming) &&
            // Clamp ripeness because the upstream method can add growth to a mature plant.
            PopularOutcomeRules.GrowthApplied(__state.Growth, plant.Growth))
            OutcomeMetric.Record(Farming, plant.def, breakdown: true);
    }

    private static void InstallHospital(Harmony harmony)
    {
        var component = OutcomeMetric.Bind("Hospital.HospitalMapComponent", "Hospital");
        var data = OutcomeMetric.Bind("Hospital.PatientData", "Hospital");
        patients = AccessTools.DeclaredField(component, "Patients") is { } field &&
            field.FieldType == typeof(Dictionary<,>).MakeGenericType(typeof(Pawn), data)
            ? field : throw new InvalidOperationException("Hospital patient registry changed");
        var admit = OutcomeMetric.Method(component, "PatientArrived", typeof(void), typeof(Pawn), data);
        OutcomeMetric.Patch(harmony, admit, typeof(PopularOutcomes), null, nameof(Admitted));
    }

    private static void Admitted(MapComponent __instance, Pawn? __0, object? __1, bool __runOriginal)
    {
        if (!__runOriginal || __0 is not { Dead: false } || __1 is null ||
            __instance.map is not { IsPlayerHome: true } || !OutcomeMetric.Observing(Hospital))
            return;
        try
        {
            if (patients.GetValue(__instance) is IDictionary registry && ReferenceEquals(registry[__0], __1))
                OutcomeMetric.Record(Hospital);
        }
        catch (Exception error) { LiveMetrics.Fail(Hospital, error); }
    }

    private static void InstallEvents(Harmony harmony)
    {
        var type = OutcomeMetric.Bind("VEE.GameCondition_MultiStage", "VEE");
        var tick = OutcomeMetric.Method(type, "GameConditionTick", typeof(void));
        OutcomeMetric.Transpile(harmony, tick, typeof(PopularOutcomes), nameof(ObserveFinalStage));
    }

    private static IEnumerable<CodeInstruction> ObserveFinalStage(IEnumerable<CodeInstruction> instructions)
        => ReplaceSingle(instructions, AccessTools.DeclaredMethod(typeof(GameCondition), nameof(GameCondition.End)),
            nameof(EndNaturally));

    private static void EndNaturally(GameCondition condition)
    {
        GameConditionManager? owner = null;
        try
        {
            if (OutcomeMetric.Observing(Events) &&
                condition.gameConditionManager is { } manager && manager.ActiveConditions.Contains(condition) &&
                condition.AffectedMaps.Any(map => map.IsPlayerHome))
                owner = manager;
        }
        catch (Exception error) { LiveMetrics.Fail(Events, error); }

        condition.End();
        if (owner is not null && !owner.ActiveConditions.Contains(condition))
            OutcomeMetric.Record(Events, condition.def, breakdown: true);
    }

    private static void InstallDryads(Harmony harmony)
    {
        const string assembly = "VanillaIdeologyExpandedDryads";
        var type = OutcomeMetric.Bind("VanillaIdeologyExpanded_Dryads.CompSpawnAwakened", assembly);
        var spawn = OutcomeMetric.Method(type, "DoSpawn", typeof(void));
        OutcomeMetric.Transpile(harmony, spawn, typeof(PopularOutcomes), nameof(ObserveDryadSpawn));
    }

    private static void InstallArchon(Harmony harmony)
    {
        const string assembly = "VREArchon";
        var type = OutcomeMetric.Bind("VREArchon.GameComponent_TranscendentPawns", assembly);
        var tick = OutcomeMetric.Method(type, "GameComponentTick", typeof(void));
        OutcomeMetric.Transpile(harmony, tick, typeof(PopularOutcomes), nameof(ObserveArchonReturn));
    }

    private static MethodInfo SpawnMethod => AccessTools.DeclaredMethod(typeof(GenSpawn), nameof(GenSpawn.Spawn),
        [typeof(Thing), typeof(IntVec3), typeof(Map), typeof(WipeMode)]);

    private static IEnumerable<CodeInstruction> ObserveDryadSpawn(IEnumerable<CodeInstruction> instructions)
        => ReplaceSingle(instructions, SpawnMethod, nameof(SpawnDryad));

    private static IEnumerable<CodeInstruction> ObserveArchonReturn(IEnumerable<CodeInstruction> instructions)
        => ReplaceSingle(instructions, SpawnMethod, nameof(ReturnArchon));

    private static Thing SpawnDryad(Thing thing, IntVec3 cell, Map map, WipeMode mode)
    {
        var spawned = GenSpawn.Spawn(thing, cell, map, mode);
        if (spawned is Pawn { Dead: false, Spawned: true } pawn && GameFacts.IsPlayer(pawn) &&
            pawn.Map == map && map.IsPlayerHome && OutcomeMetric.Observing(Dryads))
            OutcomeMetric.Record(Dryads, pawn.kindDef, breakdown: true);
        return spawned;
    }

    private static Thing ReturnArchon(Thing thing, IntVec3 cell, Map map, WipeMode mode)
    {
        var spawned = GenSpawn.Spawn(thing, cell, map, mode);
        if (spawned is Pawn { Dead: false, Spawned: true } pawn && GameFacts.IsPlayer(pawn) &&
            pawn.Map == map && map.IsPlayerHome && OutcomeMetric.Observing(Archon))
            OutcomeMetric.Record(Archon);
        return spawned;
    }

    private static IEnumerable<CodeInstruction> ReplaceSingle(IEnumerable<CodeInstruction> instructions,
        MethodInfo original, string callback)
    {
        var code = instructions.ToList();
        var call = code.SingleOrDefault(item => item.Calls(original))
            ?? throw new InvalidOperationException($"Missing committed call to {original.Name}");
        call.opcode = OpCodes.Call;
        call.operand = AccessTools.DeclaredMethod(typeof(PopularOutcomes), callback);
        return code;
    }
}
