namespace RimworldStatistics.Core;

public sealed record CoverageGroup(
    string GroupId,
    string Label,
    UnitRecordingState Recording,
    string? RequiredDlc,
    string? OwnerPackageId,
    IReadOnlyList<MetricStatus> Items,
    int MeasuredCount,
    int TotalCount);

/// <summary>
/// Maps internal acquisition/provenance identifiers to user-facing display labels
/// while keeping internal GroupId keys separate for expansion tracking and grouping.
/// </summary>
public static class MetricAcquisitionLabels
{
    private static readonly Dictionary<string, string> KnownModNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["oskarpotocki.vanillafactionsexpanded.core"] = "Vanilla Expanded Framework",
        ["vanillaexpanded.vpsycastse"] = "Vanilla Psycasts Expanded",
        ["dubwise.dubsbadhygiene"] = "Dubs Bad Hygiene",
        ["dubwise.rimefeller"] = "Rimefeller",
        ["dubwise.rimatomics"] = "Rimatomics",
        ["orion.hospitality"] = "Hospitality",
        ["orion.gastronomy"] = "Gastronomy",
        ["pengin0503.recruitment"] = "Recruitment",
        ["pengin0503.smartapparelupgrade"] = "Smart Apparel Upgrade",
        ["vanillaexpanded.vanillatradingexpanded"] = "Vanilla Trading Expanded",
        ["ceteam.combatextended"] = "Combat Extended",
        ["sarg.alphaanimals"] = "Alpha Animals",
        ["sarg.alphabiomes"] = "Alpha Biomes",
        ["sarg.alphamechs"] = "Alpha Mechs",
        ["mrhydralisk.anomaliesexpected"] = "Anomalies Expected",
        ["biomesteam.biomesbees"] = "Biomes! Bees",
        ["biomesteam.biomescaverns"] = "Biomes! Caverns",
        ["iforgotmysocks.caravanadventures"] = "Caravan Adventures",
        ["matathias.empire"] = "Empire",
        ["memegoddess.giddyup"] = "Giddy-up!",
        ["gogatio.mechanoidupgrades"] = "Mechanoid Upgrades",
        ["mlie.prisonerarena"] = "Prisoner Arena",
        ["adamas.hospitalitycasino"] = "Hospitality Casino",
        ["mlie.roadsoftherim"] = "Roads of the Rim",
        ["adamas.vendingmachines"] = "Vending Machines",
        ["zipender.prisonexpanded"] = "Prisoner Expanded",
        ["spdskatr.projectrimfactory"] = "Project RimFactory Revived",
        ["forti.rimmarket"] = "RimMarket",
        ["cj.rimtalk"] = "RimTalk",
        ["torann.arimworldofmagic"] = "A RimWorld of Magic",
        ["kentington.saveourship2"] = "Save Our Ship 2",
        ["pb3n.simplewarrants"] = "Simple Warrants",
        ["smashphil.vehicleframework"] = "Vehicle Framework",
        ["divinederivative.romance"] = "Way Better Romance",
        ["vanillastorytellersexpanded.winstonwave"] = "Winston Wave",
        ["vanillaexpanded.achievements"] = "Vanilla Achievements Expanded",
        ["vanillaexpanded.vanillaaspirations"] = "Vanilla Aspirations Expanded",
        ["vanillaexpanded.vanillaaspirationsexpanded"] = "Vanilla Aspirations Expanded",
        ["vanillaexpanded.vchemfuele"] = "Vanilla Chemfuel Expanded",
        ["vanillaexpanded.vcooke"] = "Vanilla Cooking Expanded",
        ["vanillaexpanded.vbrewe"] = "Vanilla Brewing Expanded",
        ["vanillaexpanded.vcookestews"] = "Vanilla Cooking Expanded - Stews",
        ["oskarPotocki.vfe.classical"] = "Vanilla Factions Expanded - Classical",
        ["oskarPotocki.vfe.deserters"] = "Vanilla Factions Expanded - Deserters",
        ["oskarPotocki.vfe.empire"] = "Vanilla Factions Expanded - Empire",
        ["vanillaexpanded.vfefactory"] = "Vanilla Factions Expanded - Mechanoids",
        ["oskarpotocki.vfe.insectoid2"] = "Vanilla Factions Expanded - Insectoids 2",
        ["oskarpotocki.vfe.medieval2"] = "Vanilla Factions Expanded - Medieval 2",
        ["oskarpotocki.vfe.pirates"] = "Vanilla Factions Expanded - Pirates",
        ["vanillaexpanded.vfepower"] = "Vanilla Furniture Expanded - Power",
        ["vanillaexpanded.vfesecurity"] = "Vanilla Furniture Expanded - Security",
        ["oskarpotocki.vanillafactionsexpanded.settlersmodule"] = "Vanilla Factions Expanded - Settlers",
        ["vanillaexpanded.vfespacer"] = "Vanilla Furniture Expanded - Spacer Module",
        ["oskarpotocki.vfe.tribals"] = "Vanilla Factions Expanded - Tribals",
        ["vanillaexpanded.vgeneticse"] = "Vanilla Genetics Expanded",
        ["vanillaexpanded.helixiengas"] = "Vanilla Helixien Gas Expanded",
        ["vanillaexpanded.vexploratione"] = "Vanilla Exploration Expanded",
        ["vanillaexpanded.vnutriente"] = "Vanilla Nutrient Paste Expanded",
        ["vanillaquestsexpanded.ancients"] = "Vanilla Quests Expanded - Ancients",
        ["vanillaquestsexpanded.cryptoforge"] = "Vanilla Quests Expanded - Cryptoforge",
        ["vanillaquestsexpanded.deadlife"] = "Vanilla Quests Expanded - Deadlife",
        ["vanillaquestsexpanded.dronefactory"] = "Vanilla Quests Expanded - Drone Factory",
        ["vanillaquestsexpanded.generator"] = "Vanilla Quests Expanded - Generator",
        ["vanillaracesexpanded.android"] = "Vanilla Races Expanded - Android",
        ["vanillaracesexpanded.fungoid"] = "Vanilla Races Expanded - Fungoid",
        ["vanillaracesexpanded.insector"] = "Vanilla Races Expanded - Insector",
        ["vanillaracesexpanded.lycanthrope"] = "Vanilla Races Expanded - Lycanthrope",
        ["vanillaexpanded.recycling"] = "Vanilla Recycling Expanded",
        ["vanillaracesexpanded.sanguophage"] = "Vanilla Races Expanded - Sanguophage",
        ["vanillaexpanded.skills"] = "Vanilla Skills Expanded",
        ["vanillaexpanded.vanillasocialinteractionsexpanded"] = "Vanilla Social Interactions Expanded",
        ["vanillaexpanded.vwenl"] = "Vanilla Weapons Expanded - Non-Lethal",
    };

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
        "odyssey" => "RWS_AcqUnit_odyssey",
        FishingFamily.Name => "RWS_AcqUnit_fishing",
        "ending_party" => "RWS_AcqUnit_ending_party",
        _ => null,
    };

    public static string? KnownModDisplayName(string? packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            return null;
        return KnownModNames.GetValueOrDefault(packageId.Trim());
    }

    public static string? SafeModDisplayName(string? packageId, Func<string, string?>? activeModNameLookup)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            return null;
        var trimmed = packageId.Trim();
        var active = activeModNameLookup?.Invoke(trimmed)?.Trim();
        if (!string.IsNullOrEmpty(active) && !string.Equals(active, trimmed, StringComparison.OrdinalIgnoreCase))
            return active;
        return KnownModDisplayName(trimmed);
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

        if (UnitLabelKey(definition.Acquisition) is { } unitKey)
            return NonEmpty(translateKey(unitKey), "Statistics");

        if (SafeModDisplayName(definition.OwnerPackageId, activeModNameLookup) is { } modTitle)
            return modTitle;

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
                items.Length));
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
