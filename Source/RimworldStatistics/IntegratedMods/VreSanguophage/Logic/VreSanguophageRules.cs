namespace RimworldStatistics.VreSanguophage;

/// <summary>
/// Vanilla Races Expanded - Sanguophage Draincasket: counts only actual
/// accepted units stored into the VRE_HemogenNet pipe net via PipeSystem's out
/// parameter, never requested production, fuel consumed, or unconnected capacity.
/// </summary>
internal static class VreSanguophageRules
{
    internal static double AcceptedUnits(float stored, bool matchingPipe, bool playerOwned)
        => matchingPipe && playerOwned && float.IsFinite(stored) && stored > 0f
            ? stored
            : 0d;
}
