using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.VgeHybridOutcomes;

/// <summary>
/// VGE successful electro-womb hybrid births and completed mechahybridizer
/// conversions. A machine-ready flag, queued growth cell, failed growth, or
/// spawn request is not a committed event; each checks one *new* spawned pawn.
/// </summary>
internal static class VgeHybridOutcomeStatistics
{
    private const string Owner = "VanillaExpanded.VGeneticsE";
    private const string BirthUnit = "mod.vanillaexpanded.vgeneticse.hybridbirth";
    private const string ConversionUnit = "mod.vanillaexpanded.vgeneticse.mechtransformation";

    private static FieldInfo? wombKind;
    private static FieldInfo? wombProgress;
    private static FieldInfo? wombHours;
    private static FieldInfo? wombFailure;
    private static FieldInfo? wombPower;

    private static FieldInfo? converterProgress;
    private static FieldInfo? converterDuration;
    private static FieldInfo? converterContents;

    private sealed record BirthBefore(Map Map, PawnKindDef Kind, HashSet<Pawn> Previous);
    private sealed record ConversionBefore(Map Map, Pawn Original, HashSet<Pawn> Previous);

    private static readonly LiveFamily BirthFamily = new()
    {
        Name = BirthUnit,
        Revision = 1,
        Build = output => OptionalTransitionRecording.PutCount(
            output, "born", BirthUnit + ".born", withBreakdown: true),
    };

    private static readonly LiveFamily ConversionFamily = new()
    {
        Name = ConversionUnit,
        Revision = 1,
        Build = output => OptionalTransitionRecording.PutCount(
            output, "converted", ConversionUnit + ".completed", withBreakdown: true),
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
    {
        static ModMetricDescriptor D(string family, string name, MetricCategory category,
            string label, string description) => new()
        {
            ProviderId = "rws." + family,
            OwnerPackageId = Owner,
            FamilyId = family,
            MetricName = name,
            Source = MetricObservationSource.Checkpoint,
            Revision = 1,
            Category = category,
            Unit = MetricUnit.Count,
            Overall = OverallAggregation.Sum,
            Selection = ColonySelection.Representative,
            HasBreakdown = true,
            RequiredPackages = [new ModPackageRequirement(Owner)],
            LabelKey = label,
            DescriptionKey = description,
        };
        return
        [
            new ModMetricAdapterRegistration(
                D("hybridbirth", "born", MetricCategory.Animals,
                    "RWS_VgeHybrid_Births", "RWS_VgeHybrid_BirthsDesc"),
                BirthFamily, InstallBirth),
            new ModMetricAdapterRegistration(
                D("mechtransformation", "completed", MetricCategory.Population,
                    "RWS_VgeHybrid_Conversions", "RWS_VgeHybrid_ConversionsDesc"),
                ConversionFamily, InstallConversion),
        ];
    }

    private static bool ValidGeneticsType(Type? type) =>
        type is not null &&
        string.Equals(type.Assembly.GetName().Name, "GeneticRim",
            StringComparison.OrdinalIgnoreCase);

    private static void InstallBirth(Harmony harmony)
    {
        var womb = AccessTools.TypeByName("GeneticRim.CompElectroWomb");
        if (!ValidGeneticsType(womb) || !typeof(ThingComp).IsAssignableFrom(womb))
            throw new InvalidOperationException("Selected GeneticRim electro-womb 1.7 type unavailable");

        var tick = AccessTools.DeclaredMethod(womb, "CompTick", Type.EmptyTypes);
        var kind = AccessTools.DeclaredField(womb, "growingResult");
        var progress = AccessTools.DeclaredField(womb, "progress");
        var hours = AccessTools.DeclaredField(womb, "hoursProcess");
        var failure = AccessTools.DeclaredField(womb, "failure");
        var power = AccessTools.DeclaredField(womb, "compPowerTrader");
        if (tick?.ReturnType != typeof(void) ||
            kind?.FieldType != typeof(PawnKindDef) ||
            progress?.FieldType != typeof(float) ||
            hours?.FieldType != typeof(int) ||
            failure?.FieldType != typeof(bool) ||
            power?.FieldType != typeof(CompPowerTrader))
            throw new InvalidOperationException("GeneticRim electro-womb completion signature changed");

        wombKind = kind;
        wombProgress = progress;
        wombHours = hours;
        wombFailure = failure;
        wombPower = power;
        try
        {
            harmony.Patch(tick,
                prefix: new HarmonyMethod(typeof(VgeHybridOutcomeStatistics), nameof(BeforeBirth)),
                postfix: new HarmonyMethod(typeof(VgeHybridOutcomeStatistics), nameof(AfterBirth)));
        }
        catch
        {
            harmony.Unpatch(tick, HarmonyPatchType.All, harmony.Id);
            wombKind = wombProgress = wombHours = wombFailure = wombPower = null;
            throw;
        }
    }

    private static void InstallConversion(Harmony harmony)
    {
        var converter = AccessTools.TypeByName("GeneticRim.Building_Mechahybridizer");
        if (!ValidGeneticsType(converter) || !typeof(Building).IsAssignableFrom(converter))
            throw new InvalidOperationException("GeneticRim mechahybridizer 1.7 type missing");
        var tick = AccessTools.DeclaredMethod(converter, "Tick", Type.EmptyTypes);
        var progress = AccessTools.DeclaredField(converter, "progress");
        var duration = AccessTools.DeclaredField(converter, "duration");
        var contents = AccessTools.DeclaredField(converter, "innerContainer");
        if (tick?.ReturnType != typeof(void) ||
            progress?.FieldType != typeof(float) ||
            duration?.FieldType != typeof(float) ||
            contents is null || !typeof(IEnumerable).IsAssignableFrom(contents.FieldType))
            throw new InvalidOperationException("GeneticRim mechahybridizer conversion signature changed");

        converterProgress = progress;
        converterDuration = duration;
        converterContents = contents;
        try
        {
            harmony.Patch(tick,
                prefix: new HarmonyMethod(typeof(VgeHybridOutcomeStatistics), nameof(BeforeConversion)),
                postfix: new HarmonyMethod(typeof(VgeHybridOutcomeStatistics), nameof(AfterConversion)));
        }
        catch
        {
            harmony.Unpatch(tick, HarmonyPatchType.All, harmony.Id);
            converterProgress = converterDuration = converterContents = null;
            throw;
        }
    }

    private static bool CanObserve(string unit)
        => MetricGates.IsRecording(unit)
           && Current.ProgramState == ProgramState.Playing
           && Scribe.mode == LoadSaveMode.Inactive
           && Find.TickManager is not null;

    private static bool PlayerMap(ThingComp comp, out Map? map)
    {
        map = comp.parent?.Map;
        return comp.parent is { Spawned: true } parent &&
               parent.Faction == Faction.OfPlayer && map is { IsPlayerHome: true };
    }

    private static void BeforeBirth(object __instance, out BirthBefore? __state)
    {
        __state = null;
        if (!CanObserve(BirthUnit) || __instance is not ThingComp comp ||
            !PlayerMap(comp, out var map) || map is null)
            return;
        try
        {
            if (wombFailure?.GetValue(__instance) is not false ||
                wombKind?.GetValue(__instance) is not PawnKindDef kind ||
                wombProgress?.GetValue(__instance) is not float progress ||
                wombHours?.GetValue(__instance) is not int hours || hours <= 0 ||
                wombPower?.GetValue(__instance) is not CompPowerTrader { PowerOn: true } ||
                progress + 1f / hours <= 1f)
                return;

            __state = new BirthBefore(map, kind,
                new HashSet<Pawn>(map.mapPawns.AllPawnsSpawned,
                    ReferenceEqualityComparer.Instance));
        }
        catch (Exception e) { LiveMetrics.Fail(BirthUnit, e); }
    }

    private static void AfterBirth(object __instance, bool __runOriginal, BirthBefore? __state)
    {
        if (!__runOriginal || __state is null || !CanObserve(BirthUnit))
            return;
        try
        {
            if (wombKind?.GetValue(__instance) is not null ||
                wombProgress?.GetValue(__instance) is not float afterProgress || afterProgress != 0f)
                return;
            var children = __state.Map.mapPawns.AllPawnsSpawned
                .Where(p => p.Spawned && p.Map == __state.Map &&
                    p.Faction == Faction.OfPlayer &&
                    ReferenceEquals(p.kindDef, __state.Kind) &&
                    !__state.Previous.Contains(p)).ToArray();
            if (children.Length != 1 ||
                !VgeHybridOutcomeRules.HybridBorn(
                    ready: true, successfulRecipe: true, playerOwned: true,
                    actuallySpawned: true))
                return;
            var kind = __state.Kind;
            var key = OptionalTransitionRecording.QualifiedDefKey(
                kind.modContentPack?.PackageId, kind.defName);
            GameFacts.Record(BirthUnit, state =>
                OptionalTransitionRecording.RecordCount(state, "born",
                    key: key, withBreakdown: true));
        }
        catch (Exception e) { LiveMetrics.Fail(BirthUnit, e); }
    }

    private static void BeforeConversion(object __instance, out ConversionBefore? __state)
    {
        __state = null;
        if (!CanObserve(ConversionUnit) ||
            __instance is not Building { Spawned: true } building ||
            building.Faction != Faction.OfPlayer ||
            building.Map is not { IsPlayerHome: true } map)
            return;
        try
        {
            if (converterProgress?.GetValue(__instance) is not float progress ||
                progress < 0 ||
                converterDuration?.GetValue(__instance) is not float duration ||
                duration <= 0f || progress + 1f / duration < 1f ||
                converterContents?.GetValue(__instance) is not IEnumerable contents)
                return;
            var pawn = contents.Cast<object>().OfType<Pawn>().FirstOrDefault();
            if (pawn is null || pawn.Destroyed || pawn.Faction != Faction.OfPlayer)
                return;
            __state = new ConversionBefore(map, pawn,
                new HashSet<Pawn>(map.mapPawns.AllPawnsSpawned,
                    ReferenceEqualityComparer.Instance));
        }
        catch (Exception e) { LiveMetrics.Fail(ConversionUnit, e); }
    }

    private static void AfterConversion(object __instance, bool __runOriginal,
        ConversionBefore? __state)
    {
        if (!__runOriginal || __state is null || !CanObserve(ConversionUnit))
            return;
        try
        {
            if (converterProgress?.GetValue(__instance) is not float end ||
                end >= 0f || !__state.Original.Destroyed)
                return;
            var created = __state.Map.mapPawns.AllPawnsSpawned
                .Where(p => p.Spawned && p.Faction == Faction.OfPlayer &&
                    p.Map == __state.Map && !__state.Previous.Contains(p)).ToArray();
            if (created.Length != 1 ||
                !VgeHybridOutcomeRules.MechTransformed(
                    completed: true, originalGone: true,
                    newPawnPlayer: true, newPawnSpawned: true))
                return;
            var def = created[0].kindDef;
            var key = OptionalTransitionRecording.QualifiedDefKey(
                def.modContentPack?.PackageId, def.defName);
            GameFacts.Record(ConversionUnit, state =>
                OptionalTransitionRecording.RecordCount(state, "converted",
                    key: key, withBreakdown: true));
        }
        catch (Exception e) { LiveMetrics.Fail(ConversionUnit, e); }
    }
}
