using System.Globalization;
using RimWorld;
using RimworldStatistics.Core;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>Shared localized labels and FloatMenu submenus for dynamic <see cref="SmartGroupSpec"/> scopes.</summary>
internal static class SmartScopeUi
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string ResolveStorytellerLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Dialog_RimworldStatistics.Dash;
        var def = DefDatabase<StorytellerDef>.GetNamedSilentFail(value);
        return !string.IsNullOrWhiteSpace(def?.label) ? def!.label.CapitalizeFirst() : value;
    }

    public static string ResolveDifficultyLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Dialog_RimworldStatistics.Dash;
        var def = DefDatabase<DifficultyDef>.GetNamedSilentFail(value);
        return !string.IsNullOrWhiteSpace(def?.label) ? def!.label.CapitalizeFirst() : value;
    }

    public static string ResolveBiomeLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Dialog_RimworldStatistics.Dash;
        var def = DefDatabase<BiomeDef>.GetNamedSilentFail(value);
        return !string.IsNullOrWhiteSpace(def?.label) ? def!.label.CapitalizeFirst() : value;
    }

    public static string Label(SmartGroupSpec spec) => spec.Kind switch
    {
        SmartGroupKind.PermadeathOnly => "RWS_SmartGroup_Permadeath".Translate(),
        SmartGroupKind.ReloadAnytimeOnly => "RWS_SmartGroup_ReloadAnytime".Translate(),
        SmartGroupKind.EndingReached => "RWS_SmartGroup_EndingReached".Translate(),
        SmartGroupKind.MinSurvivalDays => "RWS_SmartGroup_MinDays".Translate(spec.MinDays.ToString(Inv)),
        SmartGroupKind.Storyteller => "RWS_SmartGroup_Storyteller".Translate(ResolveStorytellerLabel(spec.Value)),
        SmartGroupKind.Difficulty => "RWS_SmartGroup_Difficulty".Translate(ResolveDifficultyLabel(spec.Value)),
        SmartGroupKind.Scenario => "RWS_SmartGroup_Scenario".Translate(spec.Value),
        SmartGroupKind.StartingBiome => "RWS_SmartGroup_Biome".Translate(ResolveBiomeLabel(spec.Value)),
        _ => spec.Id,
    };

    public static void OpenMenu(StatsView? view, Action<SmartGroupSpec> onSelect, HashSet<string>? excludedIds = null)
    {
        var candidates = view is not null
            ? SmartGroupEvaluator.Discover(view)
            : FallbackCandidates();

        bool Allowed(SmartGroupSpec spec) => excludedIds is null || !excludedIds.Contains(spec.Id);
        string FormatOption(SmartGroupCandidate c)
            => view is null ? Label(c.Spec) : $"{Label(c.Spec)} ({c.ColonyCount.ToString(Inv)})";

        var menu = new List<FloatMenuOption>();

        foreach (var fixedKind in new[]
        {
            SmartGroupKind.PermadeathOnly,
            SmartGroupKind.ReloadAnytimeOnly,
            SmartGroupKind.EndingReached,
        })
        {
            var match = candidates.FirstOrDefault(c => c.Spec.Kind == fixedKind);
            if (match is not null && Allowed(match.Spec))
                menu.Add(new FloatMenuOption(FormatOption(match), () => onSelect(match.Spec)));
        }

        var dayCandidates = candidates
            .Where(c => c.Spec.Kind == SmartGroupKind.MinSurvivalDays && Allowed(c.Spec))
            .ToArray();
        if (dayCandidates.Length > 0)
        {
            menu.Add(new FloatMenuOption("RWS_SmartGroup_ByDays".Translate(), () =>
            {
                Find.WindowStack.Add(new FloatMenu(dayCandidates
                    .Select(c => new FloatMenuOption(FormatOption(c), () => onSelect(c.Spec)))
                    .ToList()));
            }));
        }

        void AddCategorySubmenu(SmartGroupKind kind, string categoryKey)
        {
            var items = candidates
                .Where(c => c.Spec.Kind == kind && Allowed(c.Spec))
                .OrderBy(c => Label(c.Spec), StringComparer.CurrentCulture)
                .ThenBy(c => c.Spec.Value, StringComparer.Ordinal)
                .ToArray();
            if (items.Length == 0)
                return;
            menu.Add(new FloatMenuOption(categoryKey.Translate(), () =>
            {
                Find.WindowStack.Add(new FloatMenu(items
                    .Select(c => new FloatMenuOption(FormatOption(c), () => onSelect(c.Spec)))
                    .ToList()));
            }));
        }

        AddCategorySubmenu(SmartGroupKind.Storyteller, "RWS_SmartGroup_ByStoryteller");
        AddCategorySubmenu(SmartGroupKind.Difficulty, "RWS_SmartGroup_ByDifficulty");
        AddCategorySubmenu(SmartGroupKind.StartingBiome, "RWS_SmartGroup_ByBiome");
        AddCategorySubmenu(SmartGroupKind.Scenario, "RWS_SmartGroup_ByScenario");

        if (menu.Count > 0)
            Find.WindowStack.Add(new FloatMenu(menu));
    }

    private static IReadOnlyList<SmartGroupCandidate> FallbackCandidates()
    {
        var list = new List<SmartGroupCandidate>
        {
            new(new SmartGroupSpec(SmartGroupKind.PermadeathOnly), 0),
            new(new SmartGroupSpec(SmartGroupKind.ReloadAnytimeOnly), 0),
            new(new SmartGroupSpec(SmartGroupKind.EndingReached), 0),
        };
        foreach (var days in SmartGroupSpec.DefaultDayThresholds)
            list.Add(new SmartGroupCandidate(new SmartGroupSpec(SmartGroupKind.MinSurvivalDays, MinDays: days), 0));
        return list;
    }
}
