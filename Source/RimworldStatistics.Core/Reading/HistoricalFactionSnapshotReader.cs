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

    public static bool MayContainSupportedFacts(string? factionDef)
        => factionDef is not null && (SupportedVisibleDefs.Contains(factionDef) || PlayerDefs.Contains(factionDef));

    public static Dictionary<string, MetricEntry>? Build(
        IReadOnlyList<SavedFaction> factions, string? scenarioPlayerDef, bool overflowed,
        IReadOnlyCollection<string>? activeDlc)
    {
        if (overflowed || factions.Count == 0 || factions.Count > MaxFactions)
            return null;
        // Explicit IDs must be unique, including IDs of unsupported MOD
        // factions. A conflicting reference makes attribution ambiguous.
        var knownIds = new HashSet<int>();
        foreach (var faction in factions)
            if (faction.LoadId is { } id && (id < 0 || !knownIds.Add(id)))
                return null;

        var playerCandidates = factions.Where(f =>
            scenarioPlayerDef is { Length: > 0 } selected
                ? f.Def == selected && PlayerDefs.Contains(selected)
                : f.Def is not null && PlayerDefs.Contains(f.Def)).ToList();
        if (playerCandidates.Count != 1)
            return null;

        var player = playerCandidates[0];
        if (!player.Valid || !player.Active)
            return null;

        // In a real 1.7 save, the default integer loadID=0 may be omitted
        // while other saved factions still explicitly reference Faction_0.
        // Infer it ONLY when a single valid faction omitted loadID, no
        // written zero exists, and the player's saved outgoing relations
        // refer to Faction_0. Otherwise leave omitted identities unknown.
        // Even malformed/hidden faction records occupy the saved reference
        // namespace. Excluding them from this identity proof can incorrectly
        // assign Faction_0 to a different valid faction.
        var omitted = factions.Where(f => f.LoadId is null).ToList();
        var ambiguousZero = omitted.Count > 1
            || omitted.Count > 0 && knownIds.Contains(0);
        var inferZero = omitted.Count == 1 && omitted[0].Valid
            && !ambiguousZero
            && player.Relations.Any(r => r.Valid && r.Other == "Faction_0");
        int? EffectiveId(SavedFaction faction) => faction.LoadId ??
            (inferZero && faction.Equals(omitted[0]) ? 0 : null);
        if (EffectiveId(player) is not { } playerId
            || ambiguousZero && playerId == 0)
            return null;

        var facts = new List<FactionStateFact>();
        foreach (var faction in factions)
        {
            if (!faction.Valid || !faction.Active
                || faction.Def is null || !SupportedVisibleDefs.Contains(faction.Def)
                || EffectiveId(faction) is not { } id || id == playerId
                || ambiguousZero && id == 0)
                continue;
            if (faction.Def == "Empire" && activeDlc?.Contains(
                    AcquisitionUnits.Royalty, StringComparer.OrdinalIgnoreCase) != true)
                continue;

            // Live capture queries the player's outgoing relation. Missing
            // kinds/goodwill (including XML-omitted default enum members)
            // have not been independently proven, so skip *that relation*,
            // never erase other independently supported evidence.
            var targetId = "Faction_" + id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var matches = player.Relations.Where(r => r.Other == targetId).Take(2).ToList();
            if (matches.Count != 1 || !matches[0].Valid)
                continue;
            var relation = matches[0];
            if (relation.Goodwill is not { } goodwill || goodwill is < -100 or > 100
                || relation.Kind is not ("Ally" or "Hostile" or "Neutral"))
                continue;
            facts.Add(new FactionStateFact(
                id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                true, relation.Kind == "Ally", relation.Kind == "Hostile", goodwill));
        }

        if (facts.Count == 0)
            return null;
        return FactionStateSnapshot.Build(facts, partial: true);
    }
}
