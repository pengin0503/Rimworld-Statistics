namespace RimworldStatistics.Core;

/// <summary>
/// The names of the MODs whose statistics are built in, by package ID (case-insensitive), shown as "MOD: name" beside a
/// statistic (#303). A built-in provider always shows its name from here, whether or not the MOD is loaded, so a profile
/// keeps the same provider names after the MOD is removed and under another MOD set. The names follow
/// <c>docs/supported-mods.md</c>, with the plain hyphen the MODs' own About.xml files use.
/// <c>tools/check_mod_provider_names.py</c> fails when a built-in statistic's owner has no name here, or a name is not on
/// that list (except the entries marked below).
/// </summary>
public static class ModProviderNames
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AbandonedDuck.AESSchoolExpansion"] = "AES School+",
        ["AbandonedDuck.AnotherEducationSystem"] = "Another Education System",
        ["Adamas.Hospital"] = "Hospital",
        ["Adamas.HospitalityCasino"] = "Hospitality: Casino",
        ["Adamas.HospitalitySpa"] = "Hospitality: Spa",
        ["Adamas.Storefront"] = "Hospitality: Storefront",
        ["Adamas.VendingMachines"] = "Hospitality: Vending machines",
        ["Albion.GoExplore"] = "Go Explore!",
        ["Alias.SetUpCamp"] = "Set Up Camp",
        ["BiomesTeam.BiomesBees"] = "Biomes! Bees",
        ["BiomesTeam.BiomesCaverns"] = "Biomes! Caverns",
        ["BiomesTeam.BiomesFossils"] = "Biomes! Fossils",
        ["CETeam.CombatExtended"] = "Combat Extended",
        ["cj.rimtalk"] = "RimTalk",
        ["divineDerivative.Romance"] = "Way Better Romance",
        ["Dubwise.DubsBadHygiene"] = "Dubs Bad Hygiene",
        ["Dubwise.Rimatomics"] = "Dubs Rimatomics",
        ["Dubwise.Rimefeller"] = "Rimefeller",
        ["forti.rimmarket"] = "RimMarket",
        ["GoGaTio.MechanoidUpgrades"] = "Mechanoid Upgrades",
        ["hlx.UltratechAlteredCarbon"] = "Altered Carbon 2: ReSleeved",
        ["iforgotmysocks.CaravanAdventures"] = "Caravan Adventures",
        ["jaeger972.factionterritories2"] = "Hegemony",
        ["kentington.saveourship2"] = "Save Our Ship 2",
        ["m00nl1ght.GeologicalLandforms"] = "Geological Landforms",
        ["Matathias.Empire"] = "Empire Refactored",
        ["MemeGoddess.GiddyUp"] = "Giddy-Up 2 - Continued",
        ["Mlie.AnimalGenetics"] = "Animal Genetics (Continued)",
        ["Mlie.PrisonerArena"] = "Prisoner Arena (Continued)",
        ["Mlie.RoadsOfTheRim"] = "Roads of the Rim (Continued)",
        ["MrHydralisk.AnomaliesExpected"] = "Anomalies Expected",
        ["Orion.Gastronomy"] = "Gastronomy (Continued)",
        ["Orion.Hospitality"] = "Hospitality (Continued)",
        ["OskarPotocki.VanillaFactionsExpanded.SettlersModule"] = "Vanilla Factions Expanded - Settlers",
        ["OskarPotocki.VanillaVehiclesExpanded"] = "Vanilla Vehicles Expanded",
        ["OskarPotocki.VFE.Classical"] = "Vanilla Factions Expanded - Classical",
        ["OskarPotocki.VFE.Deserters"] = "Vanilla Factions Expanded - Deserters",
        ["OskarPotocki.VFE.Empire"] = "Vanilla Factions Expanded - Empire",
        ["OskarPotocki.VFE.Insectoid2"] = "Vanilla Factions Expanded - Insectoids 2",
        ["OskarPotocki.VFE.Medieval2"] = "Vanilla Factions Expanded - Medieval 2",
        ["OskarPotocki.VFE.Pirates"] = "Vanilla Factions Expanded - Pirates",
        ["OskarPotocki.VFE.Tribals"] = "Vanilla Factions Expanded - Tribals",
        ["pb3n.SimpleLeadership"] = "Simple Leadership",
        ["pb3n.SimpleWarrants"] = "Simple Warrants",
        ["ROBTRG.HotelsAndRecreation"] = "Hotels and Recreation",
        ["sarg.alphaanimals"] = "Alpha Animals",
        ["sarg.alphabiomes"] = "Alpha Biomes",
        ["sarg.alphagenes"] = "Alpha Genes",
        ["sarg.alphamechs"] = "Alpha Mechs",
        ["Sarg.AlphaMemes"] = "Alpha Memes",
        ["SmashPhil.VehicleFramework"] = "Vehicle Framework",
        ["spdskatr.projectrimfactory"] = "Project RimFactory Revived",
        ["Torann.ARimworldOfMagic"] = "A RimWorld of Magic",
        ["Torann.RimWar"] = "Rim War (Continued)",
        ["TSA.WorldDominationExperimental"] = "World Domination 2.0",
        ["vanillaexpanded.achievements"] = "Vanilla Achievements Expanded",
        ["vanillaexpanded.gravship"] = "Vanilla Gravship Expanded - Chapter 1",
        ["vanillaexpanded.gravship2"] = "Vanilla Gravship Expanded - Chapter 2",
        ["VanillaExpanded.HelixienGas"] = "Vanilla Helixien Gas Expanded",
        ["VanillaExpanded.Ideo.Dryads"] = "Vanilla Ideology Expanded - Dryads",
        ["VanillaExpanded.Recycling"] = "Vanilla Recycling Expanded",
        ["vanillaexpanded.skills"] = "Vanilla Skills Expanded",
        ["VanillaExpanded.VanillaAspirationsExpanded"] = "Vanilla Aspirations Expanded",
        ["VanillaExpanded.VanillaSocialInteractionsExpanded"] = "Vanilla Social Interactions Expanded",
        ["VanillaExpanded.VanillaTradingExpanded"] = "Vanilla Trading Expanded",
        ["VanillaExpanded.VBrewE"] = "Vanilla Brewing Expanded",
        ["VanillaExpanded.VCEF"] = "Vanilla Fishing Expanded",
        ["VanillaExpanded.VChemfuelE"] = "Vanilla Chemfuel Expanded",
        ["VanillaExpanded.VCookE"] = "Vanilla Cooking Expanded",
        ["VanillaExpanded.VCookEStews"] = "Vanilla Cooking Expanded - Stews",
        ["VanillaExpanded.VEE"] = "Vanilla Events Expanded",
        // The package ID of Vanilla Landmarks Expanded keeps an older name (its About.xml, checked 2026-11-17).
        ["VanillaExpanded.VExplorationE"] = "Vanilla Landmarks Expanded",
        ["VanillaExpanded.VFEFactory"] = "Vanilla Furniture Expanded - Factory",
        ["VanillaExpanded.VFEFarming"] = "Vanilla Furniture Expanded - Farming",
        ["VanillaExpanded.VFEPower"] = "Vanilla Furniture Expanded - Power",
        ["VanillaExpanded.VFESecurity"] = "Vanilla Furniture Expanded - Security",
        ["VanillaExpanded.VFESpacer"] = "Vanilla Furniture Expanded - Spacer Module",
        ["VanillaExpanded.VGeneticsE"] = "Vanilla Genetics Expanded",
        ["VanillaExpanded.VIESAS"] = "Vanilla Ideology Expanded - Splits and Schisms",
        ["VanillaExpanded.VNutrientE"] = "Vanilla Nutrient Paste Expanded",
        ["VanillaExpanded.VPE.Puppeteer"] = "Vanilla Psycasts Expanded - Puppeteer",
        ["VanillaExpanded.VPsycastsE"] = "Vanilla Psycasts Expanded",
        ["VanillaExpanded.VWENL"] = "Vanilla Weapons Expanded - Non-Lethal",
        ["vanillaquestsexpanded.ancients"] = "Vanilla Quests Expanded - Ancients",
        ["vanillaquestsexpanded.cryptoforge"] = "Vanilla Quests Expanded - Cryptoforge",
        ["vanillaquestsexpanded.deadlife"] = "Vanilla Quests Expanded - Deadlife",
        ["vanillaquestsexpanded.dronefactory"] = "Vanilla Quests Expanded - Drone Factory",
        ["vanillaquestsexpanded.generator"] = "Vanilla Quests Expanded - The Generator",
        ["vanillaracesexpanded.android"] = "Vanilla Races Expanded - Android",
        ["VanillaRacesExpanded.Archon"] = "Vanilla Races Expanded - Archon",
        ["vanillaracesexpanded.fungoid"] = "Vanilla Races Expanded - Fungoid",
        ["vanillaracesexpanded.insector"] = "Vanilla Races Expanded - Insector",
        ["vanillaracesexpanded.lycanthrope"] = "Vanilla Races Expanded - Lycanthrope",
        ["vanillaracesexpanded.phytokin"] = "Vanilla Races Expanded - Phytokin",
        ["vanillaracesexpanded.sanguophage"] = "Vanilla Races Expanded - Sanguophage",
        ["vanillaracesexpanded.saurid"] = "Vanilla Races Expanded - Saurid",
        ["VanillaStorytellersExpanded.WinstonWave"] = "Vanilla Storytellers Expanded - Winston Waves",
        ["Woolstrand.RealRuins"] = "Real Ruins",
        ["wowgag.RimPacts"] = "RimPacts",
        ["ZIPender.PrisonExpanded"] = "Prison Expanded",
        // Not on the published list of gameplay MODs (check exceptions): the framework that hosts the outpost and
        // processing statistics, and this author's own MODs (the README lists them; About.xml of pengin0503.recruitment
        // names it Colonist Recruitment).
        ["OskarPotocki.VanillaFactionsExpanded.Core"] = "Vanilla Expanded Framework",
        ["pengin0503.recruitment"] = "Colonist Recruitment",
        ["pengin0503.smartapparelupgrade"] = "Smart Apparel Upgrade",
    };

    /// <summary>Package IDs whose name is not on <c>docs/supported-mods.md</c> (read by the check).</summary>
    public static IReadOnlyCollection<string> NotOnSupportedList { get; } =
        ["OskarPotocki.VanillaFactionsExpanded.Core", "pengin0503.recruitment", "pengin0503.smartapparelupgrade"];

    /// <summary>Every package ID with a name here.</summary>
    public static IReadOnlyCollection<string> PackageIds => Names.Keys;

    /// <summary>The catalog name of <paramref name="packageId"/>, or null when it is not a built-in provider.</summary>
    public static string? Known(string? packageId)
        => string.IsNullOrWhiteSpace(packageId) ? null : Names.GetValueOrDefault(packageId.Trim());

    /// <summary>
    /// The name to show for <paramref name="packageId"/>: the catalog name of a built-in provider, else the loaded MOD's own
    /// name (an SDK provider, say), else null. A package ID is never returned as its own name.
    /// </summary>
    public static string? Resolve(string? packageId, Func<string, string?>? loadedModName)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            return null;
        var trimmed = packageId.Trim();
        if (Names.TryGetValue(trimmed, out var known))
            return known;
        var loaded = loadedModName?.Invoke(trimmed)?.Trim();
        return !string.IsNullOrEmpty(loaded) && !string.Equals(loaded, trimmed, StringComparison.OrdinalIgnoreCase) ? loaded : null;
    }
}
