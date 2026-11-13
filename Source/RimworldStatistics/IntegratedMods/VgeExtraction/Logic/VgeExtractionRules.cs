namespace RimworldStatistics.VgeExtraction;

/// <summary>Only a committed output from the exact extraction callback counts.</summary>
internal static class VgeExtractionRules
{
    internal static long ItemsPlaced(bool placed, bool colonistUser, bool scopedExtraction,
        int items)
        => placed && colonistUser && scopedExtraction && items > 0 ? items : 0;
}
