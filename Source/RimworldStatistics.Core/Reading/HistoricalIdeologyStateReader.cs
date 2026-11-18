using System.Globalization;

namespace RimworldStatistics.Core;

internal readonly record struct SavedIdeo(int? Id, bool Fluid, int ReformCount, bool Valid);

/// <summary>
/// Conservative 1.7 historical save salvage for <see cref="IdeologyStateSnapshot.FluidReforms"/> from
/// <c>&lt;world&gt;&lt;ideoManager&gt;&lt;ideos&gt;</c> cross-referenced against the proven active player faction's
/// <c>&lt;ideos&gt;</c> tracker. Unresolved ideo references or non-Ideology saves remain unemitted (Unknown);
/// proven non-fluid player ideos emit <see cref="MetricCoverage.NotApplicable"/> rather than a false zero.
/// </summary>
internal static class HistoricalIdeologyStateReader
{
    public const int MaxSavedIdeos = 512;
    public const int MaxMinorIdeosPerFaction = 32;

    private static readonly HashSet<string> StandardPlayerDefs = new(StringComparer.Ordinal)
    {
        "PlayerColony", "PlayerTribe",
    };

    public static Dictionary<string, MetricEntry>? Build(
        IReadOnlyList<SavedFaction> factions,
        string? scenarioPlayerDef,
        bool factionsOverflowed,
        IReadOnlyList<SavedIdeo>? savedIdeos,
        bool ideosMalformed,
        IReadOnlyCollection<string>? activeDlc)
    {
        if (activeDlc?.Contains(AcquisitionUnits.Ideology, StringComparer.OrdinalIgnoreCase) != true)
            return null;
        if (factionsOverflowed || ideosMalformed || savedIdeos is null || savedIdeos.Count == 0 || savedIdeos.Count > MaxSavedIdeos)
            return null;
        if (factions.Count == 0 || factions.Count > HistoricalFactionSnapshotReader.MaxFactions)
            return null;

        var playerCandidates = factions.Where(f =>
            scenarioPlayerDef is { Length: > 0 } selected
                ? f.Def == selected
                : f.Def is not null && StandardPlayerDefs.Contains(f.Def)).ToList();
        if (playerCandidates.Count != 1)
            return null;

        var player = playerCandidates[0];
        if (!player.Valid || !player.Active || player.IdeoTrackerMalformed)
            return null;

        var playerIdeoRefs = new List<string>();
        if (!string.IsNullOrEmpty(player.PrimaryIdeoRef) && player.PrimaryIdeoRef != "null")
            playerIdeoRefs.Add(player.PrimaryIdeoRef);
        if (player.MinorIdeoRefs is { Count: > 0 })
        {
            foreach (var r in player.MinorIdeoRefs)
                if (!string.IsNullOrEmpty(r) && r != "null")
                    playerIdeoRefs.Add(r);
        }
        if (playerIdeoRefs.Count == 0)
            return null;

        // Ideo.ExposeData writes id with defaultValue = 0, so id == 0 omits <id> while FactionIdeosTracker writes "Ideo_0".
        var knownIds = new HashSet<int>();
        var omitted = new List<SavedIdeo>();
        foreach (var ideo in savedIdeos)
        {
            if (!ideo.Valid)
                return null;
            if (ideo.Id is { } explicitId)
            {
                if (explicitId < 0 || !knownIds.Add(explicitId))
                    return null;
            }
            else
            {
                omitted.Add(ideo);
            }
        }

        var ambiguousZero = omitted.Count > 1 || (omitted.Count > 0 && knownIds.Contains(0));
        var byId = new Dictionary<int, SavedIdeo>();
        foreach (var ideo in savedIdeos)
        {
            if (ideo.Id is { } explicitId)
                byId[explicitId] = ideo;
            else if (!ambiguousZero && omitted.Count == 1)
                byId[0] = ideo;
        }

        int? fluidReforms = null;
        foreach (var rawRef in playerIdeoRefs.Distinct(StringComparer.Ordinal))
        {
            if (!TryParseIdeoRef(rawRef, out var id))
                return null;
            if (id == 0 && ambiguousZero)
                return null;
            if (!byId.TryGetValue(id, out var matched))
                return null;
            if (matched.Fluid)
            {
                if (matched.ReformCount < 0)
                    return null;
                fluidReforms = fluidReforms.HasValue ? Math.Max(fluidReforms.Value, matched.ReformCount) : matched.ReformCount;
            }
        }

        var key = IdeologyStateSnapshot.FluidReforms;
        if (fluidReforms is { } count)
        {
            return new Dictionary<string, MetricEntry>(StringComparer.Ordinal)
            {
                [key] = MetricEntry.Of(MetricKeys.Revision(key), new MetricValue
                {
                    Coverage = MetricCoverage.Partial,
                    Weakened = true,
                    IntegerValue = count,
                }),
            };
        }

        return new Dictionary<string, MetricEntry>(StringComparer.Ordinal)
        {
            [key] = MetricEntry.Of(MetricKeys.Revision(key), new MetricValue
            {
                Coverage = MetricCoverage.NotApplicable,
            }),
        };
    }

    private static bool TryParseIdeoRef(string raw, out int id)
    {
        id = -1;
        const string prefix = "Ideo_";
        if (!raw.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        return int.TryParse(raw.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out id) && id >= 0;
    }
}
