using RimWorld;
using RimworldStatistics.Core;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>Shared UI text for registered metrics; Details remains the source of formatting rules.</summary>
internal static class MetricText
{
    private static readonly Dictionary<ModContentPack, Dictionary<string, Def>> DefsByPack = new();
    private static object? cachedCycleLanguage;
    private static Dictionary<string, string>? biosculpterCycleLabels;

    public static string Breakdown(MetricDefinition definition, string key, bool useThingLabel = false)
    {
        if (MetricDisplayPolicy.BreakdownLabelKey(definition, key) is { } translation)
            return translation.Translate();

        if (definition.Key == IdeologyExpansionFamily.BiosculpterCompletions)
        {
            if (ResolveBiosculpterCycleLabel(key) is { } cycleLabel)
                return cycleLabel;
            return key switch
            {
                "medic" => "RWS_Biosculpter_Medic".Translate(),
                "bioregeneration" => "RWS_Biosculpter_Bioregeneration".Translate(),
                "ageReversal" => "RWS_Biosculpter_AgeReversal".Translate(),
                "pleasure" => "RWS_Biosculpter_Pleasure".Translate(),
                _ => key,
            };
        }

        if (definition.Key is RaidFamily.ByFaction or RaidFamily.RetreatedByFaction or RaidFamily.WipedByFaction
            && DefDatabase<FactionDef>.GetNamedSilentFail(key) is { } factionDef
            && !string.IsNullOrWhiteSpace(factionDef.label))
            return factionDef.label;

        if (!OptionalTransitionRecording.TryParseQualifiedDefKey(key, out var owner, out var name))
            return useThingLabel ? DefDatabase<ThingDef>.GetNamedSilentFail(key)?.label ?? key : key;

        if (CrossColonyCatalog.UsesThingDefBreakdown(definition)
            && DefDatabase<ThingDef>.GetNamedSilentFail(name) is { } thingDef
            && string.Equals(thingDef.modContentPack?.PackageId, owner, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(thingDef.label))
            return thingDef.label;

        if (definition.Key == RoyaltyFamily.PermitUsesByDef
            && DefDatabase<RoyalTitlePermitDef>.GetNamedSilentFail(name) is { } permitDef
            && string.Equals(permitDef.modContentPack?.PackageId, owner, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(permitDef.label))
            return permitDef.label;

        if (definition.Key == RoyaltyFamily.PsycastsByDef
            && DefDatabase<AbilityDef>.GetNamedSilentFail(name) is { } abilityDef
            && string.Equals(abilityDef.modContentPack?.PackageId, owner, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(abilityDef.label))
            return abilityDef.label;

        if (definition.Key == AnomalyFamily.PsychicRitualsByDef
            && DefDatabase<PsychicRitualDef>.GetNamedSilentFail(name) is { } ritualDef
            && string.Equals(ritualDef.modContentPack?.PackageId, owner, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(ritualDef.label))
            return ritualDef.label;

        if (definition.Key == OdysseyStateSnapshot.UniqueWeaponsByTrait
            && DefDatabase<WeaponTraitDef>.GetNamedSilentFail(name) is { } traitDef
            && string.Equals(traitDef.modContentPack?.PackageId, owner, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(traitDef.label))
            return traitDef.label;

        if (definition.Key == OdysseyVoyageFamily.VisitedLandmarks
            && DefDatabase<LandmarkDef>.GetNamedSilentFail(name) is { } landmarkDef
            && string.Equals(landmarkDef.modContentPack?.PackageId, owner, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(landmarkDef.label))
            return landmarkDef.label;

        if (definition.Key is BiotechStateSnapshot.BossgroupsSummoned
                or BiotechStateSnapshot.BossesDefeated
                or BiotechGeneticsFamily.BossFirstDefeatedTicks)
        {
            if (DefDatabase<BossgroupDef>.GetNamedSilentFail(name) is { } bgDef
                && string.Equals(bgDef.modContentPack?.PackageId, owner, StringComparison.OrdinalIgnoreCase))
            {
                var bgLabel = !string.IsNullOrWhiteSpace(bgDef.label) ? bgDef.label : bgDef.boss?.kindDef?.label;
                if (!string.IsNullOrWhiteSpace(bgLabel))
                    return bgLabel!;
            }
            if (DefDatabase<BossDef>.GetNamedSilentFail(name) is { } bossDef
                && string.Equals(bossDef.modContentPack?.PackageId, owner, StringComparison.OrdinalIgnoreCase))
            {
                var bLabel = !string.IsNullOrWhiteSpace(bossDef.label) ? bossDef.label : bossDef.kindDef?.label;
                if (!string.IsNullOrWhiteSpace(bLabel))
                    return bLabel!;
            }
        }

        var pack = LoadedModManager.RunningMods.FirstOrDefault(mod =>
            string.Equals(mod.PackageId, owner, StringComparison.OrdinalIgnoreCase));
        if (pack is null)
            return key;

        if (!DefsByPack.TryGetValue(pack, out var defs))
        {
            defs = new Dictionary<string, Def>(StringComparer.Ordinal);
            var ambiguous = new HashSet<string>(StringComparer.Ordinal);
            foreach (var def in pack.AllDefs)
            {
                if (string.IsNullOrWhiteSpace(def.label) || ambiguous.Contains(def.defName))
                    continue;
                if (!defs.TryAdd(def.defName, def))
                {
                    defs.Remove(def.defName);
                    ambiguous.Add(def.defName);
                }
            }
            DefsByPack.Add(pack, defs);
        }
        return defs.TryGetValue(name!, out var match) ? match.label : key;
    }

    private static string? ResolveBiosculpterCycleLabel(string cycleKey)
    {
        var lang = LanguageDatabase.activeLanguage;
        if (biosculpterCycleLabels is null || !ReferenceEquals(cachedCycleLanguage, lang))
        {
            cachedCycleLanguage = lang;
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var thingDef in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (thingDef?.comps is not { } comps)
                    continue;
                foreach (var comp in comps)
                {
                    if (comp is CompProperties_BiosculpterPod_BaseCycle cycle
                        && !string.IsNullOrWhiteSpace(cycle.key)
                        && !string.IsNullOrWhiteSpace(cycle.label))
                    {
                        map.TryAdd(cycle.key, cycle.label);
                    }
                }
            }
            biosculpterCycleLabels = map;
        }
        return biosculpterCycleLabels.TryGetValue(cycleKey, out var label) ? label : null;
    }

    // Metric titles and MOD attribution are separate presentation fields.
    public static string Label(MetricDefinition definition) => DetailsTab.DefinitionLabel(definition);

    public static string? ModSourceLabel(MetricDefinition definition)
        => string.IsNullOrWhiteSpace(definition.OwnerPackageId) ? null
            : (string)"RWS_Mod_Provider".Translate(SafeModDisplayName(definition.OwnerPackageId, definition.OwnerDisplayName));
    public static string Format(MetricDefinition definition, NumericValue value) => DetailsTab.Format(definition, value);
    public static string Tip(NumericRow row, StatsResult stats) => DetailsTab.OverallTip(row, stats);
    public static string BasicLabel(string key) => OverviewTab.BasicLabel(key);

    public static string PerColonyBasicLabel(string key) => key switch
    {
        BasicMetrics.Days => "RWS_Col_Days".Translate(),
        BasicMetrics.PeakWealth => "RWS_Col_PeakWealth".Translate(),
        BasicMetrics.LastWealth => "RWS_Col_LastWealth".Translate(),
        _ => BasicLabel(key),
    };

    public static string State(MetricDefinition definition, MetricCoverage coverage)
        => DetailsTab.CoverageLabel(definition, coverage);

    /// <summary>
    /// A tooltip line with the amount per game day over the value's own recorded days (<see cref="MetricRates"/>), or an
    /// empty string when the value is not an additive total recorded completely over a known interval.
    /// </summary>
    public static string FormatRateValue(MetricDefinition definition, double perDay)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var number = perDay.ToString(perDay >= 100 ? "N0" : perDay >= 1 ? "N1" : "N2", inv);
        if (definition.Unit == MetricUnit.Nutrition)
            number = "RWS_Unit_Nutrition".Translate(number);
        return "RWS_Rate_PerDayValue".Translate(number);
    }

    public static string PerDay(MetricDefinition definition, NumericValue value)
    {
        if (MetricRates.PerDay(definition, value) is not { } rate)
            return "";
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var number = rate.PerDay.ToString(rate.PerDay >= 100 ? "N0" : rate.PerDay >= 1 ? "N1" : "N2", inv);
        if (definition.Unit == MetricUnit.Nutrition)
            number = "RWS_Unit_Nutrition".Translate(number);
        return "\n" + (string)"RWS_PerDay".Translate(number, rate.Days.ToString("N1", inv));
    }

    public static string DlcDisplayName(string packageId)
    {
        if (string.Equals(packageId, AcquisitionUnits.Royalty, StringComparison.OrdinalIgnoreCase))
            return "Royalty";
        if (string.Equals(packageId, AcquisitionUnits.Ideology, StringComparison.OrdinalIgnoreCase))
            return "Ideology";
        if (string.Equals(packageId, AcquisitionUnits.Biotech, StringComparison.OrdinalIgnoreCase))
            return "Biotech";
        if (string.Equals(packageId, AcquisitionUnits.Odyssey, StringComparison.OrdinalIgnoreCase))
            return "Odyssey";
        if (string.Equals(packageId, AcquisitionUnits.Anomaly, StringComparison.OrdinalIgnoreCase))
            return "Anomaly";
        return ModDisplayName(packageId);
    }

    public static string? ActiveModTitle(string? packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            return null;
        var pack = LoadedModManager.RunningMods.FirstOrDefault(mod =>
            string.Equals(mod.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(pack?.Name) ? null : pack!.Name;
    }

    // Saved metrics may outlive their source mod. Never expose an unknown package ID in the UI.
    public static string ModDisplayName(string packageId, string? persistedDisplayName = null)
        => SafeModDisplayName(packageId, persistedDisplayName);

    public static string SafeModDisplayName(string packageId, string? persistedDisplayName = null)
        => MetricAcquisitionLabels.SafeModDisplayName(packageId, ActiveModTitle, persistedDisplayName)
            ?? (string)"RWS_Status_ExternalMod".Translate();
}
