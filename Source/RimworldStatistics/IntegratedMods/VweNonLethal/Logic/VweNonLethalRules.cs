using RimworldStatistics.Core;

namespace RimworldStatistics.VweNonLethal;

internal static class VweNonLethalRules
{
    internal const string OwnerPackageId = "VanillaExpanded.VWENL";
    internal const string FrameworkPackageId = "OskarPotocki.VanillaFactionsExpanded.Core";

    internal static string? ResolveCauseDef(
        string? weaponPackageId, string? weaponDefName, bool instigatorIsPlayer,
        string? damagePackageId, string? damageDefName,
        string? hediffPackageId, string? hediffDefName, bool onPlayerPresentMap)
    {
        if (instigatorIsPlayer && IsOwner(weaponPackageId) && !string.IsNullOrWhiteSpace(weaponDefName))
            return OptionalTransitionRecording.QualifiedDefKey(OwnerPackageId, weaponDefName);

        if (instigatorIsPlayer && IsOwner(damagePackageId) && !string.IsNullOrWhiteSpace(damageDefName))
            return OptionalTransitionRecording.QualifiedDefKey(OwnerPackageId, damageDefName);

        if (onPlayerPresentMap && IsOwner(hediffPackageId) && !string.IsNullOrWhiteSpace(hediffDefName))
            return OptionalTransitionRecording.QualifiedDefKey(OwnerPackageId, hediffDefName);

        return null;
    }

    internal static bool IsCommittedDown(
        bool wasDowned, bool isDowned, bool isDead, bool isHostileToPlayer, string? causeKey)
        => !wasDowned && isDowned && !isDead && isHostileToPlayer && causeKey is not null;

    internal static bool IsOwner(string? packageId)
        => string.Equals(packageId, OwnerPackageId, StringComparison.OrdinalIgnoreCase);
}
