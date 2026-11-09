namespace RimworldStatistics.Core;

/// <summary>Detached, bounded fields from saved Faction.ExposeData. The save does not
/// contain the current FactionDef.hidden/isPlayer metadata: do not infer it from
/// today's MOD load order.</summary>
internal readonly record struct SavedFactionRelation(string? Other, string? Kind, int? Goodwill, bool Valid);
internal readonly record struct SavedFaction(
    string? Def, int? LoadId, bool Active,
    IReadOnlyList<SavedFactionRelation> Relations, bool Valid);

/// <summary>
/// Reads only confirmed explicit player relations of a small first-party visible
/// faction allow-list. This is a historical lower-bound, NOT an exact count of all
/// factions, and always has Partial coverage. Unknown MOD faction defs, invisible
/// factions and absent relations are never silently counted as zero.
/// </summary>
internal static class HistoricalFactionSnapshotReader
{
    public const int MaxFactions = 256;
    public const int MaxRelationsPerFaction = 512;

    // These are known non-hidden Core/Royalty faction defs. Do not extend
    // without auditing the actual 1.7 FactionDefs and save schema.
    private static readonly HashSet<string> SupportedVisibleDefs = new(StringComparer.Ordinal)
    {
        "OutlanderCivil", "OutlanderRough", "TribeCivil", "TribeRough", "Empire",
    };

    private static readonly HashSet<string> PlayerDefs = new(StringComparer.Ordinal)
    {
        "PlayerColony", "PlayerTribe",
    };

    public static Dictionary<string, MetricEntry>? Build(
        IReadOnlyList<SavedFaction> factions, string? scenarioPlayerDef, bool overflowed,
        IReadOnlyCollection<string>? activeDlc)
    {
        if (overflowed || factions.Count == 0 || factions.Count > MaxFactions)
            return null;
        // Every load ID must identify one faction uniquely. A duplicate means
        // saved reference IDs are ambiguous; do not guess which is the player.
        var ids = new HashSet<int>();
        if (factions.Any(f => !f.Valid || f.LoadId is not { } id || id < 0 || !ids.Add(id)))
            return null;

        var playerCandidates = factions.Where(f =>
            scenarioPlayerDef is { Length: > 0 } selected
                ? f.Def == selected && PlayerDefs.Contains(selected)
                : f.Def is not null && PlayerDefs.Contains(f.Def)).ToList();
        if (playerCandidates.Count != 1)
            return null;

        var playerId = "Faction_" + playerCandidates[0].LoadId!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var facts = new List<FactionStateFact>();
        var selectedIds = new HashSet<int>();
        foreach (var faction in factions)
        {
            if (faction.LoadId == playerCandidates[0].LoadId || !faction.Active
                || faction.Def is null || !SupportedVisibleDefs.Contains(faction.Def))
                continue;
            // An Empire relationship is only eligible when Royalty was active
            // in the *saved* game, not merely installed now.
            if (faction.Def == "Empire" && activeDlc?.Contains(AcquisitionUnits.Royalty, StringComparer.OrdinalIgnoreCase) != true)
                continue;
            var matches = faction.Relations.Where(r => r.Other == playerId).ToList();
            if (matches.Count > 1 || matches.Any(r => !r.Valid))
                return null;
            if (matches.Count == 0)
                continue;
            var relation = matches[0];
            if (relation.Goodwill is not { } goodwill || goodwill is < -100 or > 100
                || relation.Kind is not ("Ally" or "Hostile" or "Neutral"))
                return null;
            // A member of a known visible first-party faction only. Do not
            // conflate a relation to a faction with a historical event.
            selectedIds.Add(faction.LoadId!.Value);
            facts.Add(new FactionStateFact(
                faction.LoadId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                true, relation.Kind == "Ally", relation.Kind == "Hostile", goodwill));
        }

        if (selectedIds.Count == 0)
            return null;
        return FactionStateSnapshot.Build(facts, partial: true);
    }
}
