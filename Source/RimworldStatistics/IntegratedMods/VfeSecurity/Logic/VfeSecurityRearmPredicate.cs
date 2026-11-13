namespace RimworldStatistics.VfeSecurity;

/// <summary>
/// A normal completed rearm must change the same owned, spawned trap from
/// disarmed to armed. Calling Rearm on an already armed trap is not an event.
/// </summary>
internal static class VfeSecurityRearmPredicate
{
    internal static bool Completed(bool before, bool after, bool eligible)
        => eligible && !before && after;
}
