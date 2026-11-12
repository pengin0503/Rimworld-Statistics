using System.Globalization;
using System.Text;
using RimworldStatistics.Core;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>Spec §4.1 on top (large), §4.2 below, with the tooltips of §4.4.</summary>
public static class OverviewTab
{
    private const float TopCellHeight = 92f;
    private const float BottomCellHeight = 70f;

    private sealed record Cell(string Label, string Value, string Tip, string? FavoriteKey = null)
    {
        private float lastWidth = -1;
        private string label = "";
        private string value = "";
        private string tip = "";

        public (string Label, string Value, string Tip) Fit(float width, GameFont font)
        {
            if (lastWidth == width)
                return (label, value, tip);

            lastWidth = width;
            Text.Font = GameFont.Small;
            label = Label.Truncate(width);
            Text.Font = font;
            value = Value.Truncate(width);
            tip = label != Label || value != Value ? Label + " — " + Value + "\n\n" + Tip : Tip;
            return (label, value, tip);
        }
    }
    private sealed record FavoriteCell(string Key, Cell Content);
    private static StatsView? builtFor;
    private static ViewPreferences? preferences;
    private static Action? onSave;
    private static FavoriteCell[] favoriteCells = [];
    private static string favoritesSignature = "";
    private static object? language;
    private static Cell[] top = [];
    private static Cell[] bottom = [];
    private static Vector2 scroll;

    public static void Draw(Rect rect, StatsView view, ViewPreferences? favoritePreferences = null, Action? saveFavorites = null)
    {
        preferences = favoritePreferences;
        onSave = saveFavorites;
        var currentLanguage = LanguageDatabase.activeLanguage;
        var signature = string.Join("\u001f", (IEnumerable<string>?)favoritePreferences?.FavoriteMetricKeys ?? Array.Empty<string>());
        if (!ReferenceEquals(builtFor, view) || !ReferenceEquals(language, currentLanguage) || favoritesSignature != signature)
        {
            (top, bottom) = Build(view);
            favoriteCells = BuildFavorites(view, favoritePreferences);
            builtFor = view;
            language = currentLanguage;
            favoritesSignature = signature;
        }

        const float gap = 10f;
        var width = Math.Max(1, rect.width - 16f);
        var topColumns = Math.Max(1, (int)((width + gap) / (250f + gap)));
        var bottomColumns = Math.Max(1, (int)((width + gap) / (186f + gap)));
        var topHeight = ((top.Length + topColumns - 1) / topColumns) * (TopCellHeight + gap);
        var bottomHeight = ((bottom.Length + bottomColumns - 1) / bottomColumns) * (BottomCellHeight + gap);
        var favoriteTop = topHeight + 14f + bottomHeight + 20f;
        var favoriteRows = (favoriteCells.Length + bottomColumns - 1) / bottomColumns;
        var favoritesContentHeight = favoriteRows == 0 ? 55f : 30f + favoriteRows * (104f + gap);
        Widgets.BeginScrollView(rect, ref scroll,
            new Rect(0, 0, width, favoriteTop + favoritesContentHeight));
        DrawGrid(top, topColumns, 0, TopCellHeight, GameFont.Medium);
        DrawGrid(bottom, bottomColumns, topHeight + 14f, BottomCellHeight, GameFont.Small);
        Widgets.Label(new Rect(0, favoriteTop, width, 27f), "RWS_Fav_Title".Translate());
        if (favoriteCells.Length == 0)
        {
            Widgets.Label(new Rect(0, favoriteTop + 27f, width, 25f), "RWS_Fav_Empty".Translate());
        }
        else
        {
            var cellWidth = (width - gap * (bottomColumns - 1)) / bottomColumns;
            for (var i = 0; i < favoriteCells.Length; i++)
            {
                var c = favoriteCells[i];
                var x = (i % bottomColumns) * (cellWidth + gap);
                var y = favoriteTop + 30f + (i / bottomColumns) * (104f + gap);
                DrawCell(new Rect(x, y, cellWidth, 70f), c.Content, GameFont.Small);
                var up = new Rect(x + 4f, y + 73f, 28f, 26f);
                var down = new Rect(up.xMax + 4f, up.y, 28f, 26f);
                var remove = new Rect(down.xMax + 4f, up.y, 28f, 26f);
                if (Widgets.ButtonText(up, "▲", active: i > 0))
                    MoveFavorite(i, -1);
                if (Widgets.ButtonText(down, "▼", active: i < favoriteCells.Length - 1))
                    MoveFavorite(i, 1);
                if (Widgets.ButtonText(remove, "×"))
                    RemoveFavorite(c.Key);
                TooltipHandler.TipRegion(up, "RWS_Fav_MoveUp".Translate());
                TooltipHandler.TipRegion(down, "RWS_Fav_MoveDown".Translate());
                TooltipHandler.TipRegion(remove, "RWS_Fav_Remove".Translate());
            }
        }
        Widgets.EndScrollView();

        void DrawGrid(Cell[] cells, int columns, float y, float height, GameFont font)
        {
            var cellWidth = (width - gap * (columns - 1)) / columns;
            for (var i = 0; i < cells.Length; i++)
                DrawCell(new Rect((i % columns) * (cellWidth + gap), y + (i / columns) * (height + gap), cellWidth, height), cells[i], font);
        }
    }

    private static (Cell[] Top, Cell[] Bottom) Build(StatsView view)
    {
        var s = view.Stats;
        var has = s.HasData;
        var basis = Basis(view);
        string V(string value) => has ? value : Dialog_RimworldStatistics.Dash;
        string Tip(string? extra = null) => has ? (extra is null ? basis : extra + "\n\n" + basis) : (string)"RWS_NoData".Translate() + (extra is null ? "" : "\n\n" + extra);

        string WealthValue(double value, int colonies) => colonies == 0 ? Dialog_RimworldStatistics.Dash : StatFormat.Group(value);
        string WealthTip(int colonies) => Tip("RWS_WealthCoverage".Translate(colonies, s.ColonyCount - colonies));

        string preInstall = "RWS_EndingsTooltipPreInstall".Translate();
        var top = new Cell[]
        {
            new("RWS_Stat_Days".Translate(), V(Dialog_RimworldStatistics.DaysWithYears(s.TotalTicks)), Tip(), BasicMetrics.Days),
            new("RWS_Stat_Endings".Translate(), V(s.TotalEndings.ToString(CultureInfo.InvariantCulture)), Tip(EndingBreakdown(s) + preInstall), BasicMetrics.Endings),
            new("RWS_Stat_Departed".Translate(), V(s.TotalDeparted.ToString(CultureInfo.InvariantCulture)), Tip(preInstall), BasicMetrics.Departed),
            new("RWS_Stat_PeakWealthSum".Translate(), WealthValue(s.PeakWealthSum, s.PeakWealthColonyCount), WealthTip(s.PeakWealthColonyCount), BasicMetrics.PeakWealth),
            new("RWS_Stat_Colonies".Translate(), V(s.ColonyCount.ToString(CultureInfo.InvariantCulture)), Tip()),
            new("RWS_Stat_RealPlay".Translate(), V("RWS_HoursValue".Translate(StatFormat.Hours(s.TotalRealPlay).ToString("N1", CultureInfo.InvariantCulture))), Tip(), BasicMetrics.RealPlayHours),
        };
        var bottom = new Cell[]
        {
            new("RWS_Stat_Raids".Translate(), V(s.Raids.ToString(CultureInfo.InvariantCulture)), Tip(), BasicMetrics.Raids),
            new("RWS_Stat_Threats".Translate(), V(s.Threats.ToString(CultureInfo.InvariantCulture)), Tip(), BasicMetrics.Threats),
            new("RWS_Stat_Killed".Translate(), V(s.Killed.ToString(CultureInfo.InvariantCulture)), Tip(), BasicMetrics.Killed),
            new("RWS_Stat_Longest".Translate(), V(Dialog_RimworldStatistics.DaysWithYears(s.LongestTicks)), Tip()),
            new("RWS_Stat_SingleMaxWealth".Translate(), WealthValue(s.SingleMaxWealth, s.PeakWealthColonyCount), WealthTip(s.PeakWealthColonyCount)),
            new("RWS_Stat_MaxPopulation".Translate(), V(s.MaxPopulation.ToString(CultureInfo.InvariantCulture)), Tip(), BasicMetrics.MaxPopulation),
            new("RWS_Stat_LastWealthSum".Translate(), WealthValue(s.LastWealthSum, s.LastWealthColonyCount), WealthTip(s.LastWealthColonyCount), BasicMetrics.LastWealth),
        };

        return (top, bottom);
    }

    private static void DrawCell(Rect cell, Cell item, GameFont valueFont)
    {
        Widgets.DrawHighlight(cell, 0.35f);
        Widgets.DrawHighlightIfMouseover(cell);
        var inner = cell.ContractedBy(8f);
        var (label, value, tip) = item.Fit(inner.width - (item.FavoriteKey is null ? 0f : 27f), valueFont);
        Text.Font = GameFont.Small;
        GUI.color = new Color(0.8f, 0.8f, 0.8f);
        Widgets.Label(new Rect(inner.x, inner.y, inner.width, 24f), label);
        GUI.color = Color.white;
        Text.Font = valueFont;
        Widgets.Label(new Rect(inner.x, inner.y + 26f, inner.width, inner.height - 26f), value);
        Text.Font = GameFont.Small;
        TooltipHandler.TipRegion(cell, tip);
        if (item.FavoriteKey is { } key && preferences is not null)
        {
            var star = new Rect(cell.xMax - 28f, cell.y + 4f, 24f, 25f);
            var selected = preferences.FavoriteMetricKeys.Contains(key, StringComparer.Ordinal);
            if (Widgets.ButtonText(star, selected ? "★" : "☆"))
                ToggleFavorite(key);
            TooltipHandler.TipRegion(star, "RWS_Fav_StarTip".Translate());
        }
    }

    private static void ToggleFavorite(string key)
    {
        if (preferences is null)
            return;
        if (!preferences.FavoriteMetricKeys.Remove(key) && preferences.FavoriteMetricKeys.Count < 500)
            preferences.FavoriteMetricKeys.Add(key);
        onSave?.Invoke();
        builtFor = null;
    }

    private static void MoveFavorite(int index, int delta)
    {
        if (preferences is null || index + delta < 0 || index + delta >= preferences.FavoriteMetricKeys.Count)
            return;
        var other = index + delta;
        (preferences.FavoriteMetricKeys[index], preferences.FavoriteMetricKeys[other]) =
            (preferences.FavoriteMetricKeys[other], preferences.FavoriteMetricKeys[index]);
        onSave?.Invoke();
        builtFor = null;
    }

    private static void RemoveFavorite(string key)
    {
        if (preferences is null || !preferences.FavoriteMetricKeys.Remove(key))
            return;
        onSave?.Invoke();
        builtFor = null;
    }

    private static FavoriteCell[] BuildFavorites(StatsView view, ViewPreferences? chosen)
    {
        if (chosen is null)
            return [];
        return chosen.FavoriteMetricKeys.Select(key =>
        {
            if (BasicMetrics.Contains(key))
            {
                var number = BasicMetrics.OverallValue(view.Stats, key);
                var formatted = number is null ? Dialog_RimworldStatistics.Dash
                    : key == BasicMetrics.Days
                        ? (string)"RWS_Unit_Days".Translate(number.Value.ToString("N1", CultureInfo.InvariantCulture))
                        : key == BasicMetrics.RealPlayHours
                            ? (string)"RWS_HoursValue".Translate(number.Value.ToString("N1", CultureInfo.InvariantCulture))
                            : StatFormat.Group(number.Value);
                return new FavoriteCell(key,
                    new Cell(BasicLabel(key), formatted, Basis(view)));
            }

            if (MetricLookup.Overall(view, key) is { } row)
                return new FavoriteCell(key, new Cell(MetricText.Label(row.Definition),
                    MetricText.Format(row.Definition, row.Value), MetricText.Tip(row, view.Stats)));

            var definition = MetricLookup.Definition(view, key);
            return new FavoriteCell(key, new Cell(definition is null ? key : MetricText.Label(definition),
                Dialog_RimworldStatistics.Dash,
                definition is null ? (string)"RWS_Fav_Unknown".Translate()
                    : (string)"RWS_Fav_NoOverall".Translate()));
        }).ToArray();
    }

    private static string BasicLabel(string key) => key switch
    {
        BasicMetrics.Days => "RWS_Stat_Days".Translate(),
        BasicMetrics.RealPlayHours => "RWS_Stat_RealPlay".Translate(),
        BasicMetrics.PeakWealth => "RWS_Stat_PeakWealthSum".Translate(),
        BasicMetrics.LastWealth => "RWS_Stat_LastWealthSum".Translate(),
        BasicMetrics.Raids => "RWS_Stat_Raids".Translate(),
        BasicMetrics.Threats => "RWS_Stat_Threats".Translate(),
        BasicMetrics.Killed => "RWS_Stat_Killed".Translate(),
        BasicMetrics.Departed => "RWS_Stat_Departed".Translate(),
        BasicMetrics.MaxPopulation => "RWS_Stat_MaxPopulation".Translate(),
        BasicMetrics.Endings => "RWS_Stat_Endings".Translate(),
        BasicMetrics.Saves => "RWS_Col_Saves".Translate(),
        _ => key,
    };

    /// <summary>"Computed from N colonies (excluded M)" plus what is missing (spec §4.4).</summary>
    private static string Basis(StatsView view)
    {
        string text = "RWS_TooltipBasis".Translate(view.Stats.ColonyCount, view.Stats.ExcludedColonyCount);
        if (view.Stats.OutOfScopeColonyCount > 0)
            text += "\n" + "RWS_Groups_OutOfScope".Translate(view.Stats.OutOfScopeColonyCount);
        return view.MissingObservations > 0 ? text + "\n" + "RWS_TooltipMissing".Translate(view.MissingObservations) : text;
    }

    private static string EndingBreakdown(StatsResult s)
    {
        var sb = new StringBuilder();
        foreach (var (type, n) in s.EndingsByType.OrderBy(kv => kv.Key))
            sb.Append(Dialog_RimworldStatistics.EndingLabel(type)).Append(": ").Append(n.ToString(CultureInfo.InvariantCulture)).Append('\n');
        if (sb.Length > 0)
            sb.Append('\n');
        return sb.ToString();
    }
}
