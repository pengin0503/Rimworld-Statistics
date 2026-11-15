namespace RimworldStatistics.VheHelixienGas;

/// <summary>
/// Vanilla Helixien Gas Expanded pump: only gas units actually accepted by its
/// VHGE_HelixienNet pipe storage, whether produced directly from a gas geyser
/// via VHelixienGasE's TryProducePortion prefix or via PipeSystem's deep extractor.
/// Requested output, geyser pressure, or empty storage capacity are never counted.
/// </summary>
internal static class VheHelixienGasRules
{
    internal static double AcceptedUnits(float stored, bool matchingPipe, bool playerOwned)
        => matchingPipe && playerOwned && float.IsFinite(stored) && stored > 0f
            ? stored : 0d;
}
