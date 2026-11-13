using System.Reflection.Emit;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;
using Verse.AI;

namespace RimworldStatistics.VreAndroid;

internal static class VreAndroidManufacturingStatistics
{
    private const string Owner = "vanillaracesexpanded.android";
    private const string Unit = "mod.vanillaracesexpanded.android.manufacture";
    private const string Biotech = "Ludeon.RimWorld.Biotech";
    private static MethodInfo? finishProject;
    private static Action<Building>? finish;
    private static MethodInfo? spawn;
    private static FieldInfo? project;
    [ThreadStatic] private static Completion? active;

    private sealed class Completion(Map map)
    {
        public Map Map { get; } = map;
        public Pawn? Android { get; set; }
    }

    internal static ModMetricAdapterRegistration Build() => new(new ModMetricDescriptor
    {
        ProviderId = "rws.androidmanufacture",
        OwnerPackageId = Owner,
        FamilyId = "manufacture",
        MetricName = "completed",
        Source = MetricObservationSource.Checkpoint,
        Revision = 1,
        Category = MetricCategory.Population,
        Unit = MetricUnit.Count,
        Overall = OverallAggregation.Sum,
        Selection = ColonySelection.Representative,
        HasBreakdown = true,
        RequiredDlc = Biotech,
        RequiredPackages = [new ModPackageRequirement(Owner)],
        LabelKey = "RWS_VreAndroid_Manufactured",
        DescriptionKey = "RWS_VreAndroid_ManufacturedDesc",
    }, new LiveFamily
    {
        Name = Unit,
        Revision = 1,
        RequiredDlc = Biotech,
        Build = output => OptionalTransitionRecording.PutCount(output, "completed", Unit + ".completed", withBreakdown: true),
    }, Install);

    private static void Install(Harmony harmony)
    {
        var station = AccessTools.TypeByName("VREAndroids.Building_AndroidCreationStation");
        var driver = station?.Assembly.GetType("VREAndroids.JobDriver_CreateAndroid");
        var complete = station is null ? null : AccessTools.DeclaredMethod(station, "FinishAndroidProject", []);
        var ordinary = driver is null ? null : AccessTools.DeclaredMethod(driver, "<MakeNewToils>b__3_4", [typeof(int)]);
        var spawnThing = AccessTools.DeclaredMethod(typeof(GenSpawn), nameof(GenSpawn.Spawn),
            [typeof(Thing), typeof(IntVec3), typeof(Map), typeof(WipeMode)]);
        var currentProject = station is null ? null : AccessTools.DeclaredField(station, "curAndroidProject");
        if (station?.Assembly.GetName().Name != "VREAndroids" || !typeof(Building).IsAssignableFrom(station) ||
            driver is null || !typeof(JobDriver).IsAssignableFrom(driver) || complete?.ReturnType != typeof(void) ||
            ordinary?.ReturnType != typeof(void) || spawnThing?.ReturnType != typeof(Thing) ||
            currentProject?.FieldType != typeof(CustomXenotype) ||
            PatchProcessor.GetOriginalInstructions(ordinary).Count(code => code.Calls(complete)) != 1)
            throw new InvalidOperationException("VRE Android manufacture signature changed");

        var instance = System.Linq.Expressions.Expression.Parameter(typeof(Building));
        finish = System.Linq.Expressions.Expression.Lambda<Action<Building>>(
            System.Linq.Expressions.Expression.Call(System.Linq.Expressions.Expression.Convert(instance, station!), complete),
            instance).Compile();
        finishProject = complete;
        spawn = spawnThing;
        project = currentProject;
        try
        {
            harmony.Patch(complete, transpiler: new HarmonyMethod(typeof(VreAndroidManufacturingStatistics), nameof(Observe)));
            harmony.Patch(ordinary, transpiler: new HarmonyMethod(typeof(VreAndroidManufacturingStatistics), nameof(ObserveOrdinaryCompletion)));
        }
        catch
        {
            harmony.Unpatch(ordinary, HarmonyPatchType.All, harmony.Id);
            harmony.Unpatch(complete, HarmonyPatchType.All, harmony.Id);
            throw;
        }
    }

    private static IEnumerable<CodeInstruction> ObserveOrdinaryCompletion(IEnumerable<CodeInstruction> instructions)
    {
        var body = instructions.ToList();
        var call = body.Single(instruction => instruction.Calls(finishProject!));
        var loadDriver = new CodeInstruction(OpCodes.Ldarg_0);
        call.MoveLabelsTo(loadDriver);
        call.MoveBlocksTo(loadDriver);
        body.Insert(body.IndexOf(call), loadDriver);
        call.opcode = OpCodes.Call;
        call.operand = AccessTools.DeclaredMethod(typeof(VreAndroidManufacturingStatistics), nameof(FinishOrdinaryProject));
        return body;
    }

    private static void FinishOrdinaryProject(Building station, JobDriver driver)
    {
        var previous = active;
        active = null;
        try
        {
            try
            {
                if (OptionalTransitionCallSite.Observing(Unit) && station.Spawned &&
                    GameFacts.IsPlayer(station) && GameFacts.IsPlayer(driver.pawn) &&
                    project?.GetValue(station) is not null)
                    active = new Completion(station.Map);
            }
            catch (Exception e) { LiveMetrics.Fail(Unit, e); }

            finish!(station);
            RecordManufacture(station);
        }
        finally { active = previous; }
    }

    private static IEnumerable<CodeInstruction> Observe(IEnumerable<CodeInstruction> instructions)
        => OptionalTransitionCallSite.ObserveSpawn(instructions, spawn!,
            AccessTools.DeclaredMethod(typeof(VreAndroidManufacturingStatistics), nameof(Capture)));

    private static void Capture(Thing spawned)
    {
        if (active is { } completion && spawned is Pawn pawn)
            completion.Android = pawn;
    }

    private static void RecordManufacture(Building station)
    {
        if (active is not { Android: { Spawned: true, Dead: false } pawn } completion ||
            !GameFacts.IsPlayer(pawn) || !ReferenceEquals(pawn.Map, completion.Map))
            return;
        try
        {
            if (project?.GetValue(station) is not null)
                return;
            var key = OptionalTransitionRecording.QualifiedDefKey(pawn.kindDef?.modContentPack?.PackageId, pawn.kindDef?.defName);
            GameFacts.Record(Unit, state => OptionalTransitionRecording.RecordCount(
                state, "completed", key: key, withBreakdown: true));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

}
