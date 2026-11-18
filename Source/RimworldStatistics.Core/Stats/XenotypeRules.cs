namespace RimworldStatistics.Core;

/// <summary>
/// Biotech xenotype breakdown resolution (1.7.5011 <c>Pawn_GeneTracker</c>).
/// Named custom and hybrid xenotypes (<c>UniqueXenotype</c>) map to <see cref="MetricKeys.CustomXenotypeBucket"/>
/// so player-edited strings are never stored and valid custom pawns do not degrade breakdowns to <see cref="MetricCoverage.Partial"/>.
/// </summary>
public static class XenotypeRules
{
    private static readonly HashSet<string> OfficialBiotechXenotypes = new(StringComparer.Ordinal)
    {
        "Baseliner",
        "Dirtmole",
        "Genie",
        "Hussar",
        "Sanguophage",
        "Neanderthal",
        "Pigskin",
        "Impid",
        "Waster",
        "Yttakin",
        "Highmate",
    };

    public static string? BreakdownKey(
        bool biotechActive,
        bool hasGeneTracker,
        string? xenotypeName,
        bool hybrid,
        string? packageId,
        string? defName)
    {
        if (!biotechActive || !hasGeneTracker)
            return null;
        if (!string.IsNullOrWhiteSpace(xenotypeName) || hybrid)
            return MetricKeys.CustomXenotypeBucket;
        return OptionalTransitionRecording.QualifiedDefKey(packageId, defName);
    }

    /// <summary>
    /// Conservative historical <c>.rws</c> mapping from saved <c>&lt;genes&gt;</c> fields when Biotech is active.
    /// Omitted <c>&lt;xenotype&gt;</c> defaults to <c>Baseliner</c> (<c>Pawn_GeneTracker.ExposeData</c> PostLoadInit).
    /// Unrecognized MOD <c>XenotypeDef</c>s without runtime Def resolution map to <see cref="MetricKeys.UnknownBucket"/>.
    /// </summary>
    public static string HistoricalSavedKey(
        bool biotechActive,
        bool odysseyActive,
        bool genesPresent,
        bool customXenotype,
        string? xenotypeDef)
    {
        if (!biotechActive || !genesPresent)
            return MetricKeys.UnknownBucket;
        if (customXenotype)
            return MetricKeys.CustomXenotypeBucket;
        var resolvedDef = string.IsNullOrWhiteSpace(xenotypeDef) ? "Baseliner" : xenotypeDef;
        if (OfficialBiotechXenotypes.Contains(resolvedDef))
            return AcquisitionUnits.Biotech + "/" + resolvedDef;
        if (odysseyActive && resolvedDef == "Starjack")
            return AcquisitionUnits.Odyssey + "/Starjack";
        return MetricKeys.UnknownBucket;
    }
}
