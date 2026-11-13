namespace RimworldStatistics.VqeCryptoforge;

internal static class VqeCryptoforgeRules
{
    internal static bool Completed(bool playerAttended, int before, int after, int threshold,
        bool wasScanning, bool stillScanning)
        => playerAttended && wasScanning && !stillScanning && threshold > 0
            && before >= 0 && before < threshold && after == threshold;
}
