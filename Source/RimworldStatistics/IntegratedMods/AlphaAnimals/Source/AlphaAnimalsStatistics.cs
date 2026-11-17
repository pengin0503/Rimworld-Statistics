using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.NewModOutcomes;
using Verse;
using RimworldStatistics.Patches.Metrics;

namespace RimworldStatistics.AlphaAnimals;

/// <summary>
/// Alpha Animals: tracks committed larvae/swarmling-to-cocoon metamorphoses
/// and cocoon-to-adult-insect hatchings (neither calls TrySpawnHatchedOrBornPawn
/// nor VFEInsectoids.CompHive.DoSpawn). VFE Insectoids 2 swarmling/black-cocoon
/// types are only patched when OskarPotocki.VFE.Insectoid2 is active.
/// </summary>
internal static class AlphaAnimalsStatistics
{
    private const string Owner = "sarg.alphaanimals";
    private const string AssemblyName = "AlphaBehavioursAndEvents";
    private const string VfeInsectoidsPackage = "OskarPotocki.VFE.Insectoid2";
    private const string MetamorphUnit = "mod.sarg.alphaanimals.cocoon_metamorphoses";
    private const string HatchUnit = "mod.sarg.alphaanimals.cocoon_hatches";

    [ThreadStatic] private static Stack<MetamorphPending>? metamorphPending;
    [ThreadStatic] private static Stack<HatchPending>? hatchPending;

    private sealed record MetamorphPending(object Comp, Thing Source, Thing Spawned, Map Map, bool PlayerSource);
    private sealed record HatchPending(object SourceOwner, Thing Cocoon, Pawn? Pawn, Map Map, bool PlayerCocoon);

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build() =>
    [
        OutcomeMetric.Count(Owner, "cocoon_metamorphoses",
            "RWS_AlphaAnimals_CocoonMetamorphoses", "RWS_AlphaAnimals_CocoonMetamorphosesDesc",
            MetricCategory.Animals, InstallMetamorphoses, breakdown: true),
        OutcomeMetric.Count(Owner, "cocoon_hatches",
            "RWS_AlphaAnimals_CocoonHatches", "RWS_AlphaAnimals_CocoonHatchesDesc",
            MetricCategory.Animals, InstallHatches, breakdown: true),
    ];

    private static MethodInfo GenSpawnMethod =>
        AccessTools.DeclaredMethod(typeof(GenSpawn), nameof(GenSpawn.Spawn),
            [typeof(Thing), typeof(IntVec3), typeof(Map), typeof(WipeMode)])
        ?? throw new InvalidOperationException("RimWorld GenSpawn.Spawn method missing");

    private static void InstallMetamorphoses(Harmony harmony)
    {
        var larvae = OutcomeMetric.Bind("AlphaBehavioursAndEvents.CompLarvaeToCocoon", AssemblyName);
        if (!typeof(ThingComp).IsAssignableFrom(larvae))
            throw new InvalidOperationException("Alpha Animals CompLarvaeToCocoon base type changed");
        var larvaeTick = OutcomeMetric.Method(larvae, "CompTick", typeof(void));

        MethodInfo? swarmlingTick = null;
        if (ModsConfig.IsActive(VfeInsectoidsPackage))
        {
            var swarmling = OutcomeMetric.Bind("AlphaBehavioursAndEvents.CompBlackSwarmlingToCocoon", AssemblyName);
            if (!typeof(ThingComp).IsAssignableFrom(swarmling))
                throw new InvalidOperationException("Alpha Animals CompBlackSwarmlingToCocoon base type changed");
            swarmlingTick = OutcomeMetric.Method(swarmling, "CompTick", typeof(void));
        }

        try
        {
            PatchWithSelfTranspiler(harmony, larvaeTick,
                nameof(TranspileCompMetamorph), nameof(AfterMetamorph), nameof(CleanupMetamorph));
            if (swarmlingTick is not null)
            {
                PatchWithSelfTranspiler(harmony, swarmlingTick,
                    nameof(TranspileCompMetamorph), nameof(AfterMetamorph), nameof(CleanupMetamorph));
            }
        }
        catch
        {
            harmony.Unpatch(larvaeTick, HarmonyPatchType.All, harmony.Id);
            if (swarmlingTick is not null)
                harmony.Unpatch(swarmlingTick, HarmonyPatchType.All, harmony.Id);
            throw;
        }
    }

    private static void InstallHatches(Harmony harmony)
    {
        var compCocoon = OutcomeMetric.Bind("AlphaBehavioursAndEvents.CompCocoon", AssemblyName);
        if (!typeof(ThingComp).IsAssignableFrom(compCocoon))
            throw new InvalidOperationException("Alpha Animals CompCocoon base type changed");
        var compCocoonTick = OutcomeMetric.Method(compCocoon, "CompTick", typeof(void));

        MethodInfo? blackCocoonTick = null;
        if (ModsConfig.IsActive(VfeInsectoidsPackage))
        {
            var blackCocoon = OutcomeMetric.Bind("AlphaBehavioursAndEvents.BlackCocoon", AssemblyName);
            if (!typeof(ThingWithComps).IsAssignableFrom(blackCocoon))
                throw new InvalidOperationException("Alpha Animals BlackCocoon base type changed");
            blackCocoonTick = OutcomeMetric.Method(blackCocoon, "Tick", typeof(void));
        }

        try
        {
            PatchWithSelfTranspiler(harmony, compCocoonTick,
                nameof(TranspileCompHatch), nameof(AfterHatch), nameof(CleanupHatch));
            if (blackCocoonTick is not null)
            {
                PatchWithSelfTranspiler(harmony, blackCocoonTick,
                    nameof(TranspileThingHatch), nameof(AfterHatch), nameof(CleanupHatch));
            }
        }
        catch
        {
            harmony.Unpatch(compCocoonTick, HarmonyPatchType.All, harmony.Id);
            if (blackCocoonTick is not null)
                harmony.Unpatch(blackCocoonTick, HarmonyPatchType.All, harmony.Id);
            throw;
        }
    }

    private static void PatchWithSelfTranspiler(Harmony harmony, MethodInfo target,
        string transpilerName, string postfixName, string finalizerName)
    {
        harmony.Patch(target,
            postfix: new HarmonyMethod(typeof(AlphaAnimalsStatistics), postfixName),
            transpiler: new HarmonyMethod(typeof(AlphaAnimalsStatistics), transpilerName),
            finalizer: new HarmonyMethod(typeof(AlphaAnimalsStatistics), finalizerName));
    }

    private static IEnumerable<CodeInstruction> TranspileCompMetamorph(IEnumerable<CodeInstruction> instructions)
        => ReplaceSingleSpawnWithInstance(instructions, nameof(SpawnCocoonFromComp));

    private static IEnumerable<CodeInstruction> TranspileCompHatch(IEnumerable<CodeInstruction> instructions)
        => ReplaceSingleSpawnWithInstance(instructions, nameof(SpawnInsectFromComp));

    private static IEnumerable<CodeInstruction> TranspileThingHatch(IEnumerable<CodeInstruction> instructions)
        => ReplaceSingleSpawnWithInstance(instructions, nameof(SpawnInsectFromThing));

    private static IEnumerable<CodeInstruction> ReplaceSingleSpawnWithInstance(
        IEnumerable<CodeInstruction> instructions, string callbackName)
    {
        var original = GenSpawnMethod;
        var callback = AccessTools.DeclaredMethod(typeof(AlphaAnimalsStatistics), callbackName)
            ?? throw new InvalidOperationException($"Missing callback {callbackName}");

        var code = instructions.ToList();
        int matchIndex = -1;
        for (int i = 0; i < code.Count; i++)
        {
            if (code[i].Calls(original))
            {
                if (matchIndex >= 0)
                    throw new InvalidOperationException("Alpha Animals metamorphosis/hatch method changed spawn count");
                matchIndex = i;
            }
        }
        if (matchIndex < 0)
            throw new InvalidOperationException("Alpha Animals metamorphosis/hatch method missing GenSpawn.Spawn call");

        var call = code[matchIndex];
        var ldarg0 = new CodeInstruction(OpCodes.Ldarg_0)
        {
            labels = call.labels.ToList(),
            blocks = call.blocks.ToList(),
        };
        call.labels.Clear();
        call.blocks.Clear();
        call.opcode = OpCodes.Call;
        call.operand = callback;
        code.Insert(matchIndex, ldarg0);
        return code;
    }

    private static Thing SpawnCocoonFromComp(Thing thing, IntVec3 cell, Map map, WipeMode mode, ThingComp comp)
    {
        Thing? source = null;
        bool playerSource = false;
        bool observe = OutcomeMetric.Observing(MetamorphUnit);
        if (observe)
        {
            source = comp?.parent;
            playerSource = source is not null && source.Spawned && !source.Destroyed &&
                GameFacts.IsPlayerFaction(source.Faction);
        }

        var spawned = GenSpawn.Spawn(thing, cell, map, mode);

        if (observe && playerSource && source is not null && spawned is not null && map is not null)
        {
            try
            {
                (metamorphPending ??= new Stack<MetamorphPending>()).Push(
                    new MetamorphPending(comp!, source, spawned, map, PlayerSource: true));
            }
            catch (Exception e) { LiveMetrics.Fail(MetamorphUnit, e); }
        }
        return spawned!;
    }

    private static void AfterMetamorph(object __instance, bool __runOriginal)
    {
        if (metamorphPending is not { Count: > 0 } ||
            !ReferenceEquals(metamorphPending.Peek().Comp, __instance))
            return;

        var pending = metamorphPending.Pop();
        if (!__runOriginal || !OutcomeMetric.Observing(MetamorphUnit))
            return;
        try
        {
            var spawned = pending.Spawned;
            bool spawnedValid = spawned is not null && spawned.Spawned && !spawned.Destroyed;
            bool sameMap = spawnedValid && ReferenceEquals(spawned!.Map, pending.Map);
            bool expectedDef = spawned?.def?.defName is "AA_BlackCocoon" or "VFEI2_BlackInsectoidCocoon";
            if (AlphaAnimalsRules.MetamorphosisCommitted(
                    pending.PlayerSource, spawnedValid, sameMap, pending.Source.Destroyed, expectedDef))
                OutcomeMetric.Record(MetamorphUnit, spawned!.def, breakdown: true);
        }
        catch (Exception e) { LiveMetrics.Fail(MetamorphUnit, e); }
    }

    private static Exception? CleanupMetamorph(object __instance, Exception? __exception)
    {
        if (metamorphPending is { Count: > 0 } &&
            ReferenceEquals(metamorphPending.Peek().Comp, __instance))
            metamorphPending.Pop();
        return __exception;
    }

    private static Thing SpawnInsectFromComp(Thing thing, IntVec3 cell, Map map, WipeMode mode, ThingComp comp)
        => RecordPendingHatch(comp, comp?.parent, thing, cell, map, mode);

    private static Thing SpawnInsectFromThing(Thing thing, IntVec3 cell, Map map, WipeMode mode, ThingWithComps cocoon)
        => RecordPendingHatch(cocoon, cocoon, thing, cell, map, mode);

    private static Thing RecordPendingHatch(object owner, Thing? cocoon, Thing thing,
        IntVec3 cell, Map map, WipeMode mode)
    {
        bool observe = OutcomeMetric.Observing(HatchUnit);
        bool playerCocoon = observe && cocoon is { Spawned: true, Destroyed: false } &&
            GameFacts.IsPlayerFaction(cocoon.Faction);

        var spawned = GenSpawn.Spawn(thing, cell, map, mode);

        if (observe && playerCocoon && cocoon is not null && map is not null)
        {
            try
            {
                (hatchPending ??= new Stack<HatchPending>()).Push(
                    new HatchPending(owner, cocoon, spawned as Pawn, map, PlayerCocoon: true));
            }
            catch (Exception e) { LiveMetrics.Fail(HatchUnit, e); }
        }
        return spawned;
    }

    private static void AfterHatch(object __instance, bool __runOriginal)
    {
        if (hatchPending is not { Count: > 0 } ||
            !ReferenceEquals(hatchPending.Peek().SourceOwner, __instance))
            return;

        var pending = hatchPending.Pop();
        if (!__runOriginal || !OutcomeMetric.Observing(HatchUnit))
            return;
        try
        {
            var pawn = pending.Pawn;
            bool liveSpawned = pawn is { Dead: false, Spawned: true };
            bool playerPawn = pawn is not null && GameFacts.IsPlayerFaction(pawn.Faction);
            bool sameMap = pawn is not null && ReferenceEquals(pawn.Map, pending.Map);

            if (AlphaAnimalsRules.CocoonHatchCommitted(
                    pending.PlayerCocoon, pending.Cocoon.Destroyed,
                    liveSpawned, playerPawn, sameMap))
                OutcomeMetric.Record(HatchUnit, pawn!.kindDef, breakdown: true);
        }
        catch (Exception e) { LiveMetrics.Fail(HatchUnit, e); }
    }

    private static Exception? CleanupHatch(object __instance, Exception? __exception)
    {
        if (hatchPending is { Count: > 0 } &&
            ReferenceEquals(hatchPending.Peek().SourceOwner, __instance))
            hatchPending.Pop();
        return __exception;
    }
}
