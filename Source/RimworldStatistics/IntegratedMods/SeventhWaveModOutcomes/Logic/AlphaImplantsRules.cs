using RimworldStatistics.Core;

namespace RimworldStatistics.SeventhWaveModOutcomes;

internal static class AlphaImplantsRules
{
    internal const string OwnerPackageId = "sarg.alphaimplants";
    internal const string VfeCorePackageId = "OskarPotocki.VanillaFactionsExpanded.Core";

    internal static bool IsEligibleImplantAbilityBefore(
        bool isPsycast,
        bool isPlayerCaster,
        string? abilityPackageId) =>
        !isPsycast &&
        isPlayerCaster &&
        string.Equals(abilityPackageId, OwnerPackageId, StringComparison.OrdinalIgnoreCase);

    internal static string? ResolveActivatedAbilityKey(
        bool wasEligibleBefore,
        bool activationResult,
        int lastCastTickBefore,
        int lastCastTickAfter,
        string? abilityPackageId,
        string? abilityDefName) =>
        wasEligibleBefore && activationResult && lastCastTickAfter != lastCastTickBefore
            ? OptionalTransitionRecording.QualifiedDefKey(abilityPackageId, abilityDefName)
            : null;

    internal static bool IsEligibleScarCurePatient(
        bool isAlive,
        bool isPlayerOwned,
        bool isPrisonerOfColony) =>
        isAlive && (isPlayerOwned || isPrisonerOfColony);

    internal static int ComputeCuredScars(int permanentInjuriesBefore, int permanentInjuriesAfter) =>
        permanentInjuriesBefore > permanentInjuriesAfter
            ? permanentInjuriesBefore - permanentInjuriesAfter
            : 0;
}
