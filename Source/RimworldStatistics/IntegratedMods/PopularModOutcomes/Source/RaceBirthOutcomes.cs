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

/// <summary>
/// Both hooks count only a real PawnUtility spawn. Phytokin is counted after
/// Hatch finishes applying genes, not via its unreliable successfulBirth flag.
/// </summary>
internal static class RaceBirthOutcomes
{
    private const string Saurid = "mod.vanillaracesexpanded.saurid.egg_births";
    private const string Phytokin = "mod.vanillaracesexpanded.phytokin.sapling_births";
    private static FieldInfo? sauridHatched;
    private static FieldInfo? sauridChild;

    [ThreadStatic] private static Stack<BirthAttempt>? pending;

    private sealed class BirthAttempt(Thing source, Map map, Pawn? originalChild = null)
    {
        internal readonly Thing Source = source;
        internal readonly Map Map = map;
        internal readonly Pawn? OriginalChild = originalChild;
        internal Pawn? SpawnedChild;
        internal bool SpawnSucceeded;
    }

    internal static ModMetricAdapterRegistration SauridEggs() =>
        OutcomeMetric.Count("vanillaracesexpanded.saurid", "egg_births",
            "RWS_SauridEggBirths", "RWS_SauridEggBirthsDesc",
            MetricCategory.Population, InstallSaurid, breakdown: true,
            dlc: AcquisitionUnits.Biotech);

    internal static ModMetricAdapterRegistration PhytokinSaplings() =>
        OutcomeMetric.Count("vanillaracesexpanded.phytokin", "sapling_births",
            "RWS_PhytokinSaplingBirths", "RWS_PhytokinSaplingBirthsDesc",
            MetricCategory.Population, InstallPhytokin, breakdown: true,
            dlc: AcquisitionUnits.Biotech);

    private static void InstallSaurid(Harmony harmony)
    {
        var type = OutcomeMetric.Bind("VRESaurids.Comp_HumanHatcher", "VRESaurids");
        if (!typeof(ThingComp).IsAssignableFrom(type))
            throw new InvalidOperationException("Saurid hatcher is no longer a ThingComp");
        sauridHatched = AccessTools.DeclaredField(type, "hatched");
        sauridChild = AccessTools.DeclaredField(type, "hatchee");
        if (sauridHatched?.FieldType != typeof(bool) || sauridChild?.FieldType != typeof(Pawn))
            throw new InvalidOperationException("Saurid hatch state fields have changed");
        PatchHatch(harmony, OutcomeMetric.Method(type, "Hatch", typeof(void)),
            nameof(BeforeSaurid), nameof(AfterSaurid));
    }

    private static void InstallPhytokin(Harmony harmony)
    {
        var type = OutcomeMetric.Bind("VanillaRacesExpandedPhytokin.Building_SaplingChild",
            "VanillaRacesExpanded-Phytokin");
        if (!typeof(Building).IsAssignableFrom(type) ||
            AccessTools.DeclaredField(type, "successfulBirth")?.FieldType != typeof(bool))
            throw new InvalidOperationException("Phytokin sapling birth ABI has changed");
        PatchHatch(harmony, OutcomeMetric.Method(type, "Hatch", typeof(void)),
            nameof(BeforePhytokin), nameof(AfterPhytokin));
    }

    private static void PatchHatch(Harmony harmony, MethodInfo hatch, string prefix, string postfix)
    {
        var patcher = new ScopedHarmonyPatcher(harmony);
        patcher.Patch(hatch, typeof(RaceBirthOutcomes),
            prefix: prefix, postfix: postfix,
            transpiler: nameof(CaptureSpawnReturn), finalizer: nameof(CleanupAttempt));
    }

    private static IEnumerable<CodeInstruction> CaptureSpawnReturn(IEnumerable<CodeInstruction> instructions)
    {
        var original = AccessTools.DeclaredMethod(typeof(PawnUtility),
            nameof(PawnUtility.TrySpawnHatchedOrBornPawn), [typeof(Pawn), typeof(Thing), typeof(IntVec3?)]);
        if (original?.ReturnType != typeof(bool))
            throw new InvalidOperationException("RimWorld newborn spawn API changed");
        var code = instructions.ToList();
        var matches = code.Where(instruction => instruction.Calls(original)).ToList();
        if (matches.Count != 1)
            throw new InvalidOperationException(
                "Optional birth Hatch no longer has exactly one verified spawn call");
        matches[0].opcode = OpCodes.Call;
        matches[0].operand = AccessTools.DeclaredMethod(typeof(RaceBirthOutcomes), nameof(TrySpawnVerified));
        return code;
    }

    private static bool TrySpawnVerified(Pawn pawn, Thing source, IntVec3? positionOverride)
    {
        bool succeeded = PawnUtility.TrySpawnHatchedOrBornPawn(pawn, source, positionOverride);
        if (succeeded && pending is { Count: > 0 } &&
            ReferenceEquals(pending.Peek().Source, source))
        {
            pending.Peek().SpawnedChild = pawn;
            pending.Peek().SpawnSucceeded = true;
        }
        return succeeded;
    }

    private static void Start(BirthAttempt attempt, out BirthAttempt state)
    {
        state = attempt;
        (pending ??= new Stack<BirthAttempt>()).Push(attempt);
    }

    private static void BeforeSaurid(ThingComp __instance, out BirthAttempt? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(Saurid)) return;
        try
        {
            if (__instance.parent is not { Spawned: true, Map: { IsPlayerHome: true } map } source ||
                sauridHatched?.GetValue(__instance) is not false ||
                sauridChild?.GetValue(__instance) is not Pawn { Spawned: false } child)
                return;
            Start(new BirthAttempt(source, map, child), out __state);
        }
        catch (Exception error) { LiveMetrics.Fail(Saurid, error); }
    }

    private static void AfterSaurid(ThingComp __instance, bool __runOriginal, BirthAttempt? __state)
    {
        if (!__runOriginal || __state is null || !OutcomeMetric.Observing(Saurid)) return;
        try
        {
            var child = __state.SpawnedChild;
            if (child is null || !ReferenceEquals(child, __state.OriginalChild) ||
                !ReferenceEquals(child, sauridChild?.GetValue(__instance)))
                return;
            bool completed = sauridHatched?.GetValue(__instance) is true;
            if (RaceBirthRules.Committed(__state.SpawnSucceeded, true,
                    OnOriginalPlayerHome(child, __state.Map), LivePlayerNewborn(child), completed))
                Record(Saurid, child);
        }
        catch (Exception error) { LiveMetrics.Fail(Saurid, error); }
    }

    private static void BeforePhytokin(Thing __instance, out BirthAttempt? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(Phytokin)) return;
        try
        {
            if (__instance is { Spawned: true, Map: { IsPlayerHome: true } map })
                Start(new BirthAttempt(__instance, map), out __state);
        }
        catch (Exception error) { LiveMetrics.Fail(Phytokin, error); }
    }

    private static void AfterPhytokin(bool __runOriginal, BirthAttempt? __state)
    {
        if (!__runOriginal || __state is null || !OutcomeMetric.Observing(Phytokin)) return;
        try
        {
            var child = __state.SpawnedChild;
            if (child is null) return;
            // successfulBirth is set in an upstream finally block even on failure.
            if (RaceBirthRules.Committed(__state.SpawnSucceeded, true,
                    OnOriginalPlayerHome(child, __state.Map), LivePlayerNewborn(child),
                    finalOperationCompleted: true))
                Record(Phytokin, child);
        }
        catch (Exception error) { LiveMetrics.Fail(Phytokin, error); }
    }

    private static bool OnOriginalPlayerHome(Pawn child, Map map)
        => child.Spawned && ReferenceEquals(child.Map, map) && map.IsPlayerHome;

    private static bool LivePlayerNewborn(Pawn child)
        => !child.Dead && child.RaceProps.Humanlike && GameFacts.IsPlayer(child);

    private static void Record(string unit, Pawn child)
    {
        // A distinct breakdown, not an increment of the vanilla birth total.
        // Unidentifiable custom xenotypes count in the total with partial coverage.
        string? xenotype = GameFacts.Xenotype(child);
        GameFacts.Record(unit, state => OptionalTransitionRecording.RecordCount(
            state, "completed", key: xenotype, withBreakdown: true));
    }

    private static Exception? CleanupAttempt(Exception? __exception, BirthAttempt? __state)
    {
        if (__state is not null && pending is { Count: > 0 })
        {
            if (ReferenceEquals(pending.Peek(), __state))
                pending.Pop();
            else
                pending.Clear();
        }
        return __exception; // Preserve exceptions; no count on failed Hatch.
    }
}
