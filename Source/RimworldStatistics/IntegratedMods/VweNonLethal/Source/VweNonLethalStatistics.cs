using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.NewModOutcomes;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.VweNonLethal;

/// <summary>
/// Vanilla Weapons Expanded - Non-Lethal: counts hostile pawns specifically
/// downed (alive) by player-attributed VWENL weapons, damage defs, or VWENL
/// hediffs (VWE_Anesthetic / VWE_TearGas / VWE_Dart), broken down by cause Def.
/// </summary>
internal static class VweNonLethalStatistics
{
    private const string Unit = "mod.vanillaexpanded.vwenl.nonlethal_downs";
    private const string Key = Unit + ".completed";

    private sealed record DownAttempt(bool WasHostile, string CauseKey);

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        Build = output => OptionalTransitionRecording.PutCount(output, "completed", Key, withBreakdown: true),
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
        => [new ModMetricAdapterRegistration(new ModMetricDescriptor
        {
            ProviderId = "rws.vwe_nonlethal",
            OwnerPackageId = VweNonLethalRules.OwnerPackageId,
            FamilyId = "nonlethal_downs",
            MetricName = "completed",
            Revision = 1,
            Source = MetricObservationSource.Checkpoint,
            Category = MetricCategory.Combat,
            Unit = MetricUnit.Count,
            Overall = OverallAggregation.Sum,
            Selection = ColonySelection.Representative,
            HasBreakdown = true,
            RequiredPackages =
            [
                new ModPackageRequirement(VweNonLethalRules.OwnerPackageId),
                new ModPackageRequirement(VweNonLethalRules.FrameworkPackageId),
            ],
            LabelKey = "RWS_VweNonLethal_Downs",
            DescriptionKey = "RWS_VweNonLethal_DownsDesc",
        }, Family, Install)];

    private static void Install(Harmony harmony)
    {
        var modType = AccessTools.TypeByName("VanillaWeaponsExpandedNonLethal.Gas_TearGas");
        if (modType is null ||
            !string.Equals(modType.Assembly.GetName().Name, "VanillaWeaponsExpandedNonLethal", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Selected 1.7 VanillaWeaponsExpandedNonLethal assembly not available");

        var makeDowned = OutcomeMetric.Method(
            typeof(Pawn_HealthTracker), "MakeDowned", typeof(void), typeof(DamageInfo?), typeof(Hediff));

        OutcomeMetric.Patch(harmony, makeDowned, typeof(VweNonLethalStatistics),
            nameof(BeforeMakeDowned), nameof(AfterMakeDowned));
    }

    private static bool CanObserve()
        => MetricGates.IsRecording(Unit)
           && Current.ProgramState == ProgramState.Playing
           && Scribe.mode == LoadSaveMode.Inactive
           && Find.TickManager is not null;

    private static void BeforeMakeDowned(Pawn? ___pawn, DamageInfo? __0, Hediff? __1, out DownAttempt? __state)
    {
        __state = null;
        if (!CanObserve() || ___pawn is null || ___pawn.Downed || ___pawn.Dead)
            return;

        try
        {
            var player = Faction.OfPlayerSilentFail;
            if (player is null || !___pawn.HostileTo(player))
                return;

            var instigatorIsPlayer = __0?.Instigator?.Faction == player;
            var map = ___pawn.MapHeld;
            var onPlayerPresentMap = map is not null &&
                (map.IsPlayerHome || map.mapPawns.FreeColonistsSpawnedCount > 0);

            var causeKey = VweNonLethalRules.ResolveCauseDef(
                __0?.Weapon?.modContentPack?.PackageId, __0?.Weapon?.defName, instigatorIsPlayer,
                __0?.Def?.modContentPack?.PackageId, __0?.Def?.defName,
                __1?.def?.modContentPack?.PackageId, __1?.def?.defName, onPlayerPresentMap);

            if (causeKey is not null)
                __state = new DownAttempt(true, causeKey);
        }
        catch (Exception e)
        {
            __state = null;
            LiveMetrics.Fail(Unit, e);
        }
    }

    private static void AfterMakeDowned(Pawn? ___pawn, bool __runOriginal, DownAttempt? __state)
    {
        if (!__runOriginal || __state is null || ___pawn is null || !CanObserve())
            return;

        try
        {
            if (VweNonLethalRules.IsCommittedDown(
                    wasDowned: false, ___pawn.Downed, ___pawn.Dead, __state.WasHostile, __state.CauseKey))
            {
                GameFacts.Record(Unit, state =>
                    OptionalTransitionRecording.RecordCount(state, "completed", 1, key: __state.CauseKey, withBreakdown: true));
            }
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(Unit, e);
        }
    }
}
