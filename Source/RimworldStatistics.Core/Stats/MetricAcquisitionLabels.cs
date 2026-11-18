namespace RimworldStatistics.Core;

public sealed record CoverageGroup(
    string GroupId,
    string Label,
    UnitRecordingState Recording,
    string? RequiredDlc,
    string? OwnerPackageId,
    IReadOnlyList<MetricStatus> Items,
    int MeasuredCount,
    int TotalCount,
    MetricVisualKind VisualKind);

/// <summary>
/// Maps internal acquisition/provenance identifiers to user-facing display labels
/// while keeping internal GroupId keys separate for expansion tracking and grouping.
/// </summary>
public static class MetricAcquisitionLabels
{
    public static string GroupId(MetricStatus status)
        => status.Definition.Acquisition ?? status.Definition.Provenance.ToString();

    public static string ProvenanceGroupLabelKey(MetricProvenance provenance) => provenance switch
    {
        MetricProvenance.VanillaCounter => "RWS_AcqUnit_VanillaCounter",
        MetricProvenance.SaveHistory => "RWS_AcqUnit_SaveHistory",
        MetricProvenance.SaveSnapshot => "RWS_AcqUnit_SaveSnapshot",
        _ => "RWS_AcqUnit_LiveAggregate",
    };

    public static string? UnitLabelKey(string? acquisitionId) => acquisitionId switch
    {
        "research" => "RWS_AcqUnit_research",
        "mental" => "RWS_AcqUnit_mental",
        "health_death" => "RWS_AcqUnit_health_death",
        "down" => "RWS_AcqUnit_down",
        "membership" => "RWS_AcqUnit_membership",
        "raid" => "RWS_AcqUnit_raid",
        "lifetime" => "RWS_AcqUnit_lifetime",
        "social" => "RWS_AcqUnit_social",
        "animals" => "RWS_AcqUnit_animals",
        "work" => "RWS_AcqUnit_work",
        "work_details" => "RWS_AcqUnit_work_details",
        "chore" => "RWS_AcqUnit_chore",
        "food" => "RWS_AcqUnit_food",
        "trade" => "RWS_AcqUnit_trade",
        "diplomacy" => "RWS_AcqUnit_diplomacy",
        "medicine" => "RWS_AcqUnit_medicine",
        "disaster" => "RWS_AcqUnit_disaster",
        ModdedIncidentFamily.Name => "RWS_AcqUnit_modded_incident",
        "caravan" => "RWS_AcqUnit_caravan",
        "research_state" => "RWS_AcqUnit_research_state",
        "factions" => "RWS_AcqUnit_factions",
        "roster" => "RWS_AcqUnit_roster",
        "facilities" => "RWS_AcqUnit_facilities",
        "stock" => "RWS_AcqUnit_stock",
        "colony_start" => "RWS_AcqUnit_colony_start",
        OutpostSnapshot.Unit => "RWS_AcqUnit_outposts_snapshot",
        OutpostProduction.Unit => "RWS_AcqUnit_outposts_production",
        DbhStatistics.FixturesUnit => "RWS_AcqUnit_dbh_fixtures",
        DbhStatistics.ActivitiesUnit => "RWS_AcqUnit_dbh_activities",
        "royalty" => "RWS_AcqUnit_royalty",
        VpeStatistics.EventUnit => "RWS_AcqUnit_vpe_events",
        VpeStatistics.CastUnit => "RWS_AcqUnit_vpe_casts",
        VpeStatistics.SnapshotUnit => "RWS_AcqUnit_vpe_snapshot",
        "royalty_state" => "RWS_AcqUnit_royalty_state",
        "royalty_quests" => "RWS_AcqUnit_royalty_quests",
        "ideology" => "RWS_AcqUnit_ideology",
        "biotech" => "RWS_AcqUnit_biotech",
        "biotech_state" => "RWS_AcqUnit_biotech_state",
        AnomalyFamily.Name => "RWS_AcqUnit_anomaly",
        AnomalyStateSnapshot.Unit => "RWS_AcqUnit_anomaly_state",
        "odyssey" => "RWS_AcqUnit_odyssey",
        FishingFamily.Name => "RWS_AcqUnit_fishing",
        "ending_party" => "RWS_AcqUnit_ending_party",
        _ => null,
    };

    /// <summary>
    /// The provider name shown as "MOD: name": the catalog name of a built-in provider, whether or not the MOD is loaded,
    /// else the loaded MOD's own name, else null (#303). A package ID is never shown as a name.
    /// </summary>
    public static string? SafeModDisplayName(string? packageId, Func<string, string?>? activeModNameLookup)
        => ModProviderNames.Resolve(packageId, activeModNameLookup);

    /// <summary>
    /// The name of an acquisition unit, the same in the Coverage tab and the MOD settings (#320): its translated name, else
    /// the name of the MOD that provides it (<see cref="SafeModDisplayName"/>), else null. Only then does a caller fall back
    /// to something else.
    /// </summary>
    public static string? UnitLabel(
        string? acquisitionId,
        string? ownerPackageId,
        Func<string, string?>? activeModNameLookup,
        Func<string, string> translateKey)
    {
        if (UnitLabelKey(acquisitionId) is { } key && translateKey(key) is { } text && !string.IsNullOrWhiteSpace(text))
            return text.Trim();
        return SafeModDisplayName(ownerPackageId, activeModNameLookup);
    }

    /// <summary>
    /// Unit names told apart (#320): a name that two units share (two units of one MOD) is followed by " · " and the first
    /// of that unit's statistic names in <paramref name="comparer"/> order, the first statistic the Coverage tab lists under it.
    /// A unit with no statistic names keeps the shared name.
    /// </summary>
    public static string[] Distinguish(
        IReadOnlyList<string> labels,
        Func<int, IEnumerable<string>> statisticLabels,
        StringComparer comparer)
    {
        var shared = labels.GroupBy(l => l, comparer).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(comparer);
        var result = new string[labels.Count];
        for (var i = 0; i < labels.Count; i++)
        {
            result[i] = labels[i];
            if (!shared.Contains(labels[i]))
                continue;
            var first = statisticLabels(i).Where(s => !string.IsNullOrWhiteSpace(s)).OrderBy(s => s, comparer).FirstOrDefault();
            if (first is not null)
                result[i] = labels[i] + " · " + first;
        }
        return result;
    }

    public static string ResolveGroupLabel(
        MetricDefinition definition,
        Func<string, string?>? activeModNameLookup,
        Func<string, string> translateKey,
        Func<MetricCategory, string> translateCategory,
        Func<string, string> translateExternalModCategory)
    {
        if (definition.Acquisition is null)
            return NonEmpty(translateKey(ProvenanceGroupLabelKey(definition.Provenance)), "Statistics");

        if (UnitLabel(definition.Acquisition, definition.OwnerPackageId, activeModNameLookup, translateKey) is { } unitLabel)
            return unitLabel;

        if (Enum.IsDefined(definition.Category))
        {
            var cat = translateCategory(definition.Category);
            if (!string.IsNullOrWhiteSpace(cat))
                return NonEmpty(translateExternalModCategory(cat), "External mod");
        }
        return NonEmpty(translateKey("RWS_Status_ExternalMod"), "External mod");
    }

    public static bool IsGroupExpanded(string groupId, IReadOnlySet<string> userExpanded, string? searchTerm)
        => !string.IsNullOrWhiteSpace(searchTerm) || userExpanded.Contains(groupId);

    public static IReadOnlyList<CoverageGroup> BuildGroups(
        IReadOnlyList<MetricStatus> statuses,
        Func<string, string?>? activeModNameLookup,
        Func<string, string> translateKey,
        Func<MetricCategory, string> translateCategory,
        Func<string, string> translateExternalModCategory,
        StringComparer labelComparer)
    {
        if (statuses.Count == 0)
            return Array.Empty<CoverageGroup>();

        var groups = new List<CoverageGroup>();
        foreach (var g in statuses.GroupBy(GroupId, StringComparer.Ordinal))
        {
            var items = g.ToArray();
            var first = items[0];
            var label = ResolveGroupLabel(
                first.Definition, activeModNameLookup, translateKey, translateCategory, translateExternalModCategory);
            var measured = items.Count(s => s.Measured > 0);
            groups.Add(new CoverageGroup(
                g.Key,
                label,
                first.Recording,
                first.Definition.RequiredDlc,
                first.Definition.OwnerPackageId,
                items,
                measured,
                items.Length,
                MetricVisualState.ClassifyGroup(items)));
        }

        groups.Sort((a, b) =>
        {
            var cmp = labelComparer.Compare(a.Label, b.Label);
            return cmp != 0 ? cmp : StringComparer.Ordinal.Compare(a.GroupId, b.GroupId);
        });
        return groups;
    }

    private static string NonEmpty(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
