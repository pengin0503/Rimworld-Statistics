using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.NewModOutcomes;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.Stage2ModOutcomes;

/// <summary>
/// Stage 2 metric for Vanilla Gravship Expanded (vanillaexpanded.gravship,
/// 1.7/Assemblies/VanillaGravshipExpanded.dll):
/// Net newly earned Gravdata across:
/// 1. Gravship flight landings: transpiles WorldComponent_GravshipController_LandingEnded_Patch.ApplyGravDataYield
///    right after GravdataUtility.CalculateGravdataYield(float, float, Building_GravEngine, Pawn),
///    capturing only uncorrupted flight yield before Building_GravshipBlackBox storage round-trips
///    and avoiding Dialog_BeginLordJob UI preview calls.
/// 2. Orbital Gravtech Console work: observes GravshipResearchUtility.ResearchPerformed(float, Pawn)
///    only when actor is a live player pawn (excluding ApplyGravDataYield's actor == null call so
///    flight/BlackBox Gravdata applied to research is never double-counted).
/// </summary>
internal static class GravshipStage2Outcomes
{
    private const string AssemblyName = "VanillaGravshipExpanded";
    private const string Unit = "mod.vanillaexpanded.gravship.gravdata_gained";
    private const string Key = Unit + ".completed";
    private const string CounterName = "gravdata";

    private static MethodInfo calculateYieldMethod = null!;
    private static FieldInfo currentProjectField = null!;

    private sealed record ConsoleResearchSnapshot(ResearchProjectDef Project, float ProgressBefore);

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        RequiredDlc = AcquisitionUnits.Odyssey,
        Build = output =>
        {
            var earned = output.State.Peek(CounterName)?.Sum ?? 0d;
            output.Put(Key, output.Value(real: earned));
        },
    };

    internal static IEnumerable<ModMetricAdapterRegistration> Build()
    {
        yield return new ModMetricAdapterRegistration(new ModMetricDescriptor
        {
            ProviderId = "rws.gravdata_gained",
            OwnerPackageId = GravshipStage2Rules.OwnerPackageId,
            FamilyId = "gravdata_gained",
            MetricName = "completed",
            Source = MetricObservationSource.Checkpoint,
            Revision = 1,
            Category = MetricCategory.Research,
            Unit = MetricUnit.Count,
            Overall = OverallAggregation.Sum,
            Selection = ColonySelection.Representative,
            HasBreakdown = false,
            RequiredDlc = AcquisitionUnits.Odyssey,
            RequiredPackages = [new ModPackageRequirement(GravshipStage2Rules.OwnerPackageId)],
            LabelKey = "RWS_VgeGravdataGained",
            DescriptionKey = "RWS_VgeGravdataGainedDesc",
        }, Family, Install);
    }

    private static void Install(Harmony harmony)
    {
        var landingPatch = OutcomeMetric.Bind("VanillaGravshipExpanded.WorldComponent_GravshipController_LandingEnded_Patch", AssemblyName);
        var gravdataUtility = OutcomeMetric.Bind("VanillaGravshipExpanded.GravdataUtility", AssemblyName);
        var researchUtility = OutcomeMetric.Bind("VanillaGravshipExpanded.GravshipResearchUtility", AssemblyName);
        var worldExposePatch = OutcomeMetric.Bind("VanillaGravshipExpanded.World_ExposeData_Patch", AssemblyName);

        var applyYield = OutcomeMetric.Method(landingPatch, "ApplyGravDataYield", typeof(void), typeof(Gravship), typeof(int).MakeByRefType());
        calculateYieldMethod = OutcomeMetric.Method(
            gravdataUtility,
            "CalculateGravdataYield",
            typeof(int),
            typeof(float),
            typeof(float),
            typeof(Building_GravEngine),
            typeof(Pawn));
        var researchPerformed = OutcomeMetric.Method(researchUtility, "ResearchPerformed", typeof(void), typeof(float), typeof(Pawn));
        currentProjectField = AccessTools.DeclaredField(worldExposePatch, "currentGravtechProject") is { IsStatic: true } pf &&
            pf.FieldType == typeof(ResearchProjectDef)
            ? pf
            : throw new InvalidOperationException("VanillaGravshipExpanded World_ExposeData_Patch.currentGravtechProject signature changed");

        var transpilerMethod = AccessTools.DeclaredMethod(typeof(GravshipStage2Outcomes), nameof(TranspileApplyGravDataYield));
        var beforeMethod = AccessTools.DeclaredMethod(typeof(GravshipStage2Outcomes), nameof(BeforeConsoleResearch));
        var afterMethod = AccessTools.DeclaredMethod(typeof(GravshipStage2Outcomes), nameof(AfterConsoleResearch));

        var patchedLanding = false;
        try
        {
            harmony.Patch(applyYield, transpiler: new HarmonyMethod(transpilerMethod));
            patchedLanding = true;
            harmony.Patch(researchPerformed, prefix: new HarmonyMethod(beforeMethod), postfix: new HarmonyMethod(afterMethod));
        }
        catch
        {
            if (patchedLanding)
                harmony.Unpatch(applyYield, transpilerMethod);
            harmony.Unpatch(researchPerformed, beforeMethod);
            harmony.Unpatch(researchPerformed, afterMethod);
            throw;
        }
    }

    private static IEnumerable<CodeInstruction> TranspileApplyGravDataYield(IEnumerable<CodeInstruction> instructions)
    {
        var observer = AccessTools.DeclaredMethod(typeof(GravshipStage2Outcomes), nameof(ObserveLandingGravdataYield))
            ?? throw new InvalidOperationException("Missing ObserveLandingGravdataYield");
        var matched = 0;

        foreach (var instruction in instructions)
        {
            yield return instruction;
            if (instruction.Calls(calculateYieldMethod))
            {
                matched++;
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call, observer);
            }
        }

        if (matched != 1)
            throw new InvalidOperationException($"Expected 1 CalculateGravdataYield call in ApplyGravDataYield, found {matched}");
    }

    private static int ObserveLandingGravdataYield(int yield, Gravship? gravship)
    {
        if (yield <= 0 || !OutcomeMetric.Observing(Unit))
            return yield;

        try
        {
            var earned = GravshipStage2Rules.FilterLandingGravdataYield(
                isPlayerEngine: GameFacts.IsPlayerFaction(gravship?.Engine?.Faction),
                calculatedYield: yield);
            if (earned > 0d)
            {
                GameFacts.Record(Unit, state => OptionalTransitionRecording.RecordReal(state, CounterName, earned));
            }
        }
        catch (Exception error)
        {
            LiveMetrics.Fail(Unit, error);
        }

        return yield;
    }

    private static void BeforeConsoleResearch(Pawn? __1, out ConsoleResearchSnapshot? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(Unit) ||
            __1 is not { Dead: false, Destroyed: false } actor ||
            !GameFacts.IsPlayer(actor) ||
            Find.ResearchManager is not { } manager)
            return;

        try
        {
            if (currentProjectField.GetValue(null) is ResearchProjectDef project &&
                project.PrerequisitesCompleted &&
                !project.IsFinished)
            {
                __state = new ConsoleResearchSnapshot(project, manager.GetProgress(project));
            }
        }
        catch (Exception error)
        {
            __state = null;
            LiveMetrics.Fail(Unit, error);
        }
    }

    private static void AfterConsoleResearch(bool __runOriginal, Pawn? __1, ConsoleResearchSnapshot? __state)
    {
        if (!__runOriginal || __state is null || !OutcomeMetric.Observing(Unit) ||
            Find.ResearchManager is not { } manager)
            return;

        try
        {
            var delta = GravshipStage2Rules.ComputeConsoleGravdataDelta(
                hasPlayerActor: __1 is { Dead: false, Destroyed: false } && GameFacts.IsPlayer(__1),
                sameActiveProject: true,
                progressBefore: __state.ProgressBefore,
                progressAfter: manager.GetProgress(__state.Project));
            if (delta > 0d)
            {
                GameFacts.Record(Unit, state => OptionalTransitionRecording.RecordReal(state, CounterName, delta));
            }
        }
        catch (Exception error)
        {
            LiveMetrics.Fail(Unit, error);
        }
    }
}
