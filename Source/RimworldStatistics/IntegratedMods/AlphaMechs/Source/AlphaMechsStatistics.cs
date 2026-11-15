using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.NewModOutcomes;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.AlphaMechs;

/// <summary>
/// Alpha Mechs: counts hostile/non-player mechanoids converted to the player's
/// faction via AlphaMechs.CompMechConversion.Apply, broken down by converted
/// mechanoid PawnKindDef (falling back to ThingDef). Insufficient-bandwidth early
/// exits, dead targets, VFE Mechanoid substitutions (CompChangeDef), and generic
/// resurrections (CompResurrectMechMinor) are excluded to avoid false positives
/// and overlap with MedicineFamily.Resurrections.
/// </summary>
internal static class AlphaMechsStatistics
{
    private const string AssemblyName = "AlphaMechs";
    private const string Unit = "mod.sarg.alphamechs.mech_conversions";

    private sealed record ConversionAttempt(Pawn Target, Def Def, bool TargetWasPlayerOwned);

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build() =>
    [
        OutcomeMetric.Count(
            owner: AlphaMechsRules.OwnerPackageId,
            family: "mech_conversions",
            label: "RWS_AlphaMechs_MechConversions",
            description: "RWS_AlphaMechs_MechConversionsDesc",
            category: MetricCategory.Biotech,
            install: Install,
            breakdown: true,
            dlc: AcquisitionUnits.Biotech,
            dependencies: AlphaMechsRules.FrameworkPackageId),
    ];

    private static void Install(Harmony harmony)
    {
        var compType = OutcomeMetric.Bind("AlphaMechs.CompMechConversion", AssemblyName);
        if (!typeof(CompAbilityEffect).IsAssignableFrom(compType))
            throw new InvalidOperationException("AlphaMechs.CompMechConversion type changed");

        var apply = OutcomeMetric.Method(
            compType, "Apply", typeof(void), typeof(LocalTargetInfo), typeof(LocalTargetInfo));

        OutcomeMetric.Patch(harmony, apply, typeof(AlphaMechsStatistics), nameof(Before), nameof(After));
    }

    private static void Before(CompAbilityEffect? __instance, LocalTargetInfo __0, out ConversionAttempt? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(Unit) || __instance is null)
            return;

        try
        {
            var player = Faction.OfPlayerSilentFail;
            var caster = __instance.parent?.pawn;
            var target = __0.Pawn;

            if (player is null || caster?.Faction != player ||
                target is null || target.Dead || target.RaceProps?.IsMechanoid != true)
                return;

            var def = (Def?)target.kindDef ?? target.def;
            if (def is null)
                return;

            __state = new ConversionAttempt(target, def, target.Faction == player);
        }
        catch (Exception e)
        {
            __state = null;
            LiveMetrics.Fail(Unit, e);
        }
    }

    private static void After(bool __runOriginal, ConversionAttempt? __state)
    {
        if (!__runOriginal || __state is null || !OutcomeMetric.Observing(Unit))
            return;

        try
        {
            var player = Faction.OfPlayerSilentFail;
            var convertedNow = player is not null && !__state.Target.Dead && __state.Target.Faction == player;

            var key = AlphaMechsRules.ResolveConversionBreakdown(
                casterIsPlayer: true,
                targetIsMechanoid: true,
                targetWasPlayerOwnedBefore: __state.TargetWasPlayerOwned,
                targetIsAliveAndPlayerOwnedAfter: convertedNow,
                targetDefPackageId: __state.Def.modContentPack?.PackageId,
                targetDefName: __state.Def.defName);

            if (key is not null)
            {
                GameFacts.Record(Unit, state =>
                    OptionalTransitionRecording.RecordCount(state, "completed", 1, key: key, withBreakdown: true));
            }
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(Unit, e);
        }
    }
}
