namespace RimworldStatistics.Core;

/// <summary>
/// Pure attribution rule for colony labor outcomes (harvesting, sowing, crafting bills,
/// mining, hauling, cleaning, fire-fighting, fishing, etc.).
/// Free colonists, colony slaves, and colony mechanoids/animals carry <c>isPlayerFaction == true</c>
/// while <c>isPrisoner == false</c>. Imprisoned pawns (<c>isPrisoner == true</c>) belong to
/// the colony workforce if and only if their jailer (<c>HostFaction</c>) is the player faction,
/// which both includes colony-custody prisoner labor (e.g. Prison Labor) and excludes player
/// pawns currently held captive by a hostile faction.
/// </summary>
public static class ColonyWorkAttribution
{
    public static bool IsColonyWorker(bool isPlayerFaction, bool isPrisoner, bool isHostPlayerFaction)
        => isPrisoner ? isHostPlayerFaction : isPlayerFaction;
}
