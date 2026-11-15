namespace RimworldStatistics.VreFungoid;

/// <summary>
/// Validates committed Fungoid fungal infection xenotype conversions.
/// Breakdown by result xenotype is omitted because upstream discards XenotypeDef
/// and assigns Baseliner + freeform/localized xenotypeName string.
/// </summary>
internal static class VreFungoidRules
{
    internal static bool ConversionCommitted(bool severityReached, bool liveColonyAffectedPawn,
        bool infectionHediffRemoved, bool infectedHediffPresent)
        => severityReached && liveColonyAffectedPawn && infectionHediffRemoved && infectedHediffPresent;
}
