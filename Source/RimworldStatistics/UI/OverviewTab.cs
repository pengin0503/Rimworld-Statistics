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

    private sealed record Cell(string Label, string Value, string Tip, string? FavoriteKey = null, bool ShowStar = true, string? Source = null)
    {
        private float lastWidth = -1;
        private string label = "";
        private string value = "";
        private string tip = "";
        private string chartTip = "";
        private bool? chartable;

        /// <summary>Cells are rebuilt whenever the view changes, so the lookup is made once per built view.</summary>
        public bool Chartable(StatsView view)
            => chartable ??= FavoriteKey is { } key && MetricLookup.IsKnownKey(view, key);

        /// <summary>The tooltip over the area that opens the chart.</summary>
        public string ChartTip => chartTip;

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
            chartTip = FavoriteKey is null ? tip : tip + "\n\n" + "RWS_Chart_OpenTip".Translate();
            return (label, value, tip);
        }
    }
    private sealed record FavoriteCell(string Key, Cell Content);
    private sealed record ColonyFavoriteCard(
        ResolvedColonyHistoryFavorite Resolved,
        string ColonyDisplayName,
        string MetricLabel,
        string Subtitle,
        string ValueText,
        string Tooltip);

    private static StatsView? builtFor;
    private static ViewPreferences? preferences;
    private static Action? onSave;
    private static FavoriteCell[] favoriteCells = [];
    private static IReadOnlyList<ResolvedColonyHistoryFavorite> resolvedColonyFavorites = [];
    private static ColonyFavoriteCard[] colonyFavoriteCards = [];
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
        var signature = string.Join("\u001f", (IEnumerable<string>?)favoritePreferences?.FavoriteMetricKeys ?? Array.Empty<string>())
            + "\u001e" + ColonyPresentation.Signature(favoritePreferences)
            + "\u001e" + ColonyHistoryFavorites.Signature(favoritePreferences);
        if (!ReferenceEquals(builtFor, view) || !ReferenceEquals(language, currentLanguage) || favoritesSignature != signature)
        {
            (top, bottom) = Build(view);
            favoriteCells = BuildFavorites(view, favoritePreferences);
            resolvedColonyFavorites = ColonyHistoryFavorites.ResolveAll(view, favoritePreferences);
            colonyFavoriteCards = BuildColonyFavorites(view, favoritePreferences, resolvedColonyFavorites);
            builtFor = view;
            language = currentLanguage;
            favoritesSignature = signature;
        }

        const float gap = 10f;
        var width = Math.Max(1f, rect.width - 16f);
        var topColumns = Math.Max(1, (int)((width + gap) / (250f + gap)));
        var bottomColumns = Math.Max(1, (int)((width + gap) / (186f + gap)));
        var topHeight = ((top.Length + topColumns - 1) / topColumns) * (TopCellHeight + gap);
        var bottomHeight = ((bottom.Length + bottomColumns - 1) / bottomColumns) * (BottomCellHeight + gap);
        var favoriteTop = topHeight + 14f + bottomHeight + 20f;
        var favoriteRows = (favoriteCells.Length + bottomColumns - 1) / bottomColumns;
        var favoritesContentHeight = favoriteRows == 0 ? 55f : 30f + favoriteRows * (104f + gap);
        var colonyFavTop = favoriteTop + favoritesContentHeight + 16f;
        var colonyFavRows = (colonyFavoriteCards.Length + bottomColumns - 1) / bottomColumns;
        var colonyFavContentHeight = colonyFavRows == 0 ? 55f : 30f + colonyFavRows * (104f + gap);

        Widgets.BeginScrollView(rect, ref scroll,
            new Rect(0f, 0f, width, colonyFavTop + colonyFavContentHeight));
        try
        {
            DrawGrid(top, topColumns, 0f, TopCellHeight, GameFont.Medium);
            DrawGrid(bottom, bottomColumns, topHeight + 14f, BottomCellHeight, GameFont.Small);

            var addBasicLabel = (string)"RWS_Fav_AddBasic".Translate();
            var addBasicWidth = Math.Min(Math.Max(180f, Text.CalcSize(addBasicLabel).x + 20f), Math.Max(110f, width * 0.45f));
            var addBasicRect = new Rect(Math.Max(0f, width - addBasicWidth), favoriteTop, Math.Min(addBasicWidth, width), 27f);
            var favTitle = (string)"RWS_Fav_Title".Translate();
            var favTitleRect = new Rect(0f, favoriteTop, Math.Max(0f, width - addBasicWidth - 6f), 27f);
            var shownFavTitle = favTitle.Truncate(favTitleRect.width);
            Widgets.Label(favTitleRect, shownFavTitle);
            if (shownFavTitle != favTitle)
                TooltipHandler.TipRegion(favTitleRect, favTitle);
            var shownAddBasic = addBasicLabel.Truncate(Math.Max(0f, addBasicRect.width - 12f));
            if (shownAddBasic != addBasicLabel)
                TooltipHandler.TipRegion(addBasicRect, addBasicLabel);
            if (Widgets.ButtonText(addBasicRect, shownAddBasic))
            {
                var available = BasicMetrics.Keys.Where(key =>
                    preferences?.FavoriteMetricKeys.Contains(key, StringComparer.Ordinal) != true)
                    .Select(key => new FloatMenuOption(BasicLabel(key), () => ToggleFavorite(key))).ToList();
                if (available.Count > 0)
                    Find.WindowStack.Add(new FloatMenu(available));
            }
            if (favoriteCells.Length == 0)
            {
                Widgets.Label(new Rect(0f, favoriteTop + 27f, width, 25f), "RWS_Fav_Empty".Translate());
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

            var addColonyLabel = (string)"RWS_ColonyFav_Add".Translate();
            var addColonyWidth = Math.Min(210f, width);
            var addColonyRect = new Rect(Math.Max(0f, width - addColonyWidth), colonyFavTop, addColonyWidth, 27f);
            var colonyTitleRect = new Rect(0f, colonyFavTop, Math.Max(0f, width - addColonyWidth - 6f), 27f);
            var colonyTitle = (string)"RWS_ColonyFav_Title".Translate();
            var shownColonyTitle = colonyTitle.Truncate(colonyTitleRect.width);
            Widgets.Label(colonyTitleRect, shownColonyTitle);
            if (shownColonyTitle != colonyTitle)
                TooltipHandler.TipRegion(colonyTitleRect, colonyTitle);

            var shownAddColony = addColonyLabel.Truncate(Math.Max(0f, addColonyRect.width - 12f));
            if (shownAddColony != addColonyLabel)
                TooltipHandler.TipRegion(addColonyRect, addColonyLabel);
            var canAddColonyFav = preferences is not null && view.Stats.Colonies.Count > 0;
            if (Widgets.ButtonText(addColonyRect, shownAddColony, active: canAddColonyFav) && canAddColonyFav)
            {
                var currentPrefs = preferences!;
                var save = onSave ?? (() => { });
                Find.WindowStack.Add(new ColonyPickerDialog(
                    view.Stats.Colonies,
                    currentPrefs,
                    pickedColony => ColonyChartPicker.OpenMetricPicker(view, pickedColony, currentPrefs, save)));
            }

            if (colonyFavoriteCards.Length == 0)
            {
                var emptyText = (string)"RWS_ColonyFav_Empty".Translate();
                var emptyRect = new Rect(0f, colonyFavTop + 27f, width, 26f);
                var shownEmpty = emptyText.Truncate(width);
                Widgets.Label(emptyRect, shownEmpty);
                if (shownEmpty != emptyText)
                    TooltipHandler.TipRegion(emptyRect, emptyText);
            }
            else
            {
                var cellWidth = (width - gap * (bottomColumns - 1)) / bottomColumns;
                for (var i = 0; i < colonyFavoriteCards.Length; i++)
                {
                    var card = colonyFavoriteCards[i];
                    var x = (i % bottomColumns) * (cellWidth + gap);
                    var y = colonyFavTop + 30f + (i / bottomColumns) * (104f + gap);
                    DrawColonyFavoriteCard(new Rect(x, y, cellWidth, 70f), view, card);
                    var up = new Rect(x + 4f, y + 73f, 28f, 26f);
                    var down = new Rect(up.xMax + 4f, up.y, 28f, 26f);
                    var remove = new Rect(down.xMax + 4f, up.y, 28f, 26f);
                    if (Widgets.ButtonText(up, "▲", active: i > 0))
                        MoveColonyFavorite(i, -1);
                    if (Widgets.ButtonText(down, "▼", active: i < colonyFavoriteCards.Length - 1))
                        MoveColonyFavorite(i, 1);
                    if (Widgets.ButtonText(remove, "×"))
                        RemoveColonyFavorite(card.Resolved);
                    TooltipHandler.TipRegion(up, "RWS_Fav_MoveUp".Translate());
                    TooltipHandler.TipRegion(down, "RWS_Fav_MoveDown".Translate());
                    TooltipHandler.TipRegion(remove, "RWS_Fav_Remove".Translate());
                }
            }
        }
        finally
        {
            Widgets.EndScrollView();
        }

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
        var hasStar = item.ShowStar && item.FavoriteKey is not null && preferences is not null;
        var chartView = item.FavoriteKey is not null && builtFor is { } current && item.Chartable(current) ? current : null;
        var labelReserved = (hasStar ? 27f : 0f) + (chartView is not null ? 18f : 0f);
        var (label, value, tip) = item.Fit(Math.Max(1f, inner.width - labelReserved), valueFont);
        Text.Font = GameFont.Small;
        GUI.color = new Color(0.8f, 0.8f, 0.8f);
        Widgets.Label(new Rect(inner.x, inner.y, inner.width, 24f), label);
        var valueOffset = item.Source is null ? 26f : 35f;
        if (item.Source is { } source)
            Widgets.Label(new Rect(inner.x, inner.y + 18f, inner.width, 18f), source.Truncate(inner.width));
        GUI.color = Color.white;
        Text.Font = valueFont;
        Widgets.Label(new Rect(inner.x, inner.y + valueOffset, inner.width, Math.Max(0f, inner.height - valueOffset)), value);
        Text.Font = GameFont.Small;
        // Do not cover the favourite star at the right edge of the card.
        var chartRect = new Rect(cell.x + 4f, cell.y + 2f, Math.Max(0f, cell.width - (hasStar ? 40f : 8f)), cell.height - 4f);
        if (chartView is not null)
        {
            var iconX = hasStar ? cell.xMax - 46f : cell.xMax - 20f;
            if (iconX >= cell.x + 12f)
                MetricVisualStyle.DrawMiniChartIcon(new Rect(iconX, cell.y + 9f, 12f, 12f), Mouse.IsOver(chartRect));
        }
        // A single tooltip region: a second overlapping one would be drawn as a second stacked copy.
        var star = new Rect(cell.xMax - 28f, cell.y + 4f, 24f, 25f);
        var activeTip = hasStar && Mouse.IsOver(star)
            ? (string)"RWS_Fav_StarTip".Translate()
            : chartView is not null && Mouse.IsOver(chartRect) ? item.ChartTip : tip;
        if (item.Source is { } sourceName && !activeTip.Contains(sourceName, StringComparison.Ordinal)
            && !(hasStar && Mouse.IsOver(star)))
            activeTip += "\n" + sourceName;
        TooltipHandler.TipRegion(cell, activeTip);
        if (chartView is not null && Widgets.ButtonInvisible(chartRect))
            Find.WindowStack.Add(new Dialog_MetricChart(chartView, item.FavoriteKey!, null));
        if (hasStar && item.FavoriteKey is { } key)
        {
            var selected = preferences!.FavoriteMetricKeys.Contains(key, StringComparer.Ordinal);
            if (Widgets.ButtonText(star, selected ? "★" : "☆"))
                ToggleFavorite(key);
        }
    }

    private static void DrawColonyFavoriteCard(Rect cell, StatsView view, ColonyFavoriteCard card)
    {
        Widgets.DrawHighlight(cell, 0.35f);
        if (card.Resolved.CanOpen)
            Widgets.DrawHighlightIfMouseover(cell);
        var inner = cell.ContractedBy(8f);
        var openable = card.Resolved.CanOpen;
        var labelWidth = Math.Max(1f, inner.width - (openable ? 18f : 0f));

        Text.Font = GameFont.Small;
        var shownLabel = card.MetricLabel.Truncate(labelWidth);
        var shownSub = card.Subtitle.Truncate(inner.width);
        var shownVal = card.ValueText.Truncate(inner.width);

        GUI.color = new Color(0.8f, 0.8f, 0.8f);
        Widgets.Label(new Rect(inner.x, inner.y, labelWidth, 22f), shownLabel);
        GUI.color = openable ? new Color(0.75f, 0.82f, 0.88f) : new Color(0.68f, 0.68f, 0.72f);
        Widgets.Label(new Rect(inner.x, inner.y + 18f, inner.width, 18f), shownSub);
        GUI.color = openable ? Color.white : new Color(0.7f, 0.7f, 0.7f);
        Widgets.Label(new Rect(inner.x, inner.y + 35f, inner.width, Math.Max(0f, inner.height - 35f)), shownVal);
        GUI.color = Color.white;

        var clickRect = new Rect(cell.x + 4f, cell.y + 2f, Math.Max(0f, cell.width - 8f), cell.height - 4f);
        if (openable)
        {
            var iconX = cell.xMax - 20f;
            if (iconX >= cell.x + 12f)
                MetricVisualStyle.DrawMiniChartIcon(new Rect(iconX, cell.y + 9f, 12f, 12f), Mouse.IsOver(clickRect));
        }

        TooltipHandler.TipRegion(cell, card.Tooltip);
        if (openable && Widgets.ButtonInvisible(clickRect))
        {
            Find.WindowStack.Add(new Dialog_ColonyMetricHistory(
                view,
                card.Resolved.CanonicalColonyKey,
                card.ColonyDisplayName,
                card.Resolved.Definition!,
                card.MetricLabel,
                card.Resolved.ComparisonValue!));
        }
    }

    private static void ToggleFavorite(string key)
    {
        if (preferences is null)
            return;
        if (!preferences.FavoriteMetricKeys.Remove(key) && preferences.FavoriteMetricKeys.Count < ViewPreferences.MaxFavoriteCount)
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

    private static void MoveColonyFavorite(int index, int delta)
    {
        if (preferences is null || !ColonyHistoryFavorites.Move(preferences, resolvedColonyFavorites, index, delta))
            return;
        onSave?.Invoke();
        builtFor = null;
    }

    private static void RemoveColonyFavorite(ResolvedColonyHistoryFavorite favorite)
    {
        if (preferences is null || !ColonyHistoryFavorites.Remove(preferences, favorite))
            return;
        onSave?.Invoke();
        builtFor = null;
    }

    private static ColonyFavoriteCard[] BuildColonyFavorites(StatsView view, ViewPreferences? chosen,
        IReadOnlyList<ResolvedColonyHistoryFavorite> items)
    {
        if (items.Count == 0)
            return [];
        var cards = new ColonyFavoriteCard[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var fav = items[i];
            var colonyName = fav.Colony is not null
                ? Dialog_RimworldStatistics.ColonyName(fav.Colony, chosen)
                : ColonyPresentation.TryGetCustomName(chosen, fav.StoredColonyKey)
                    ?? (string)"RWS_ColonyFav_MissingColony".Translate();

            string metricLabel;
            string? modSource = null;
            if (BasicMetrics.Contains(fav.MetricKey))
            {
                metricLabel = BasicLabel(fav.MetricKey);
            }
            else if (fav.Definition is { } def)
            {
                metricLabel = MetricText.Label(def);
                modSource = MetricText.ModSourceLabel(def);
            }
            else
            {
                metricLabel = FallbackUnknownMetricLabel(fav.MetricKey);
            }

            var subtitle = modSource is null ? colonyName : $"{colonyName} · {modSource}";
            var valueText = fav.CanOpen
                ? MetricText.Format(fav.Definition!, fav.ComparisonValue!)
                : Dialog_RimworldStatistics.Dash;

            var tip = new StringBuilder();
            tip.Append(metricLabel).Append(" — ").Append(colonyName).Append(": ").Append(valueText);
            if (fav.Colony is { } col)
            {
                if (ColonyPresentation.HasCustomName(chosen, col))
                    tip.Append('\n').Append((string)"RWS_ColonyName_Original".Translate(Dialog_RimworldStatistics.OriginalColonyName(col)));
                if (!string.IsNullOrWhiteSpace(col.WorldName))
                    tip.Append('\n').Append((string)"RWS_Col_World".Translate()).Append(": ").Append(col.WorldName);
            }
            if (modSource is not null)
                tip.Append('\n').Append(modSource);

            var ownerPkg = fav.Definition?.OwnerPackageId ?? fav.RequestedDefinition?.OwnerPackageId;
            if (!string.IsNullOrWhiteSpace(ownerPkg) && MetricText.ActiveModTitle(ownerPkg) is null)
                tip.Append('\n').Append((string)"RWS_ColonyFav_TipModInactive".Translate(MetricText.SafeModDisplayName(ownerPkg)));

            if (fav.CanOpen)
            {
                tip.Append(MetricText.PerDay(fav.Definition!, fav.ComparisonValue!));
                tip.Append("\n\n").Append((string)"RWS_ColonyFav_OpenTip".Translate(colonyName));
            }
            else
            {
                var statusReason = fav.Status switch
                {
                    ColonyHistoryFavoriteStatus.ColonyMissing => (string)"RWS_ColonyFav_TipColonyMissing".Translate(),
                    ColonyHistoryFavoriteStatus.ColonyExcluded => (string)"RWS_ColonyFav_TipColonyExcluded".Translate(),
                    ColonyHistoryFavoriteStatus.ColonyOutOfScope => (string)"RWS_ColonyFav_TipColonyOutOfScope".Translate(),
                    ColonyHistoryFavoriteStatus.MetricUnknown => (string)"RWS_ColonyFav_TipMetricUnknown".Translate(),
                    ColonyHistoryFavoriteStatus.MetricNoHistory => (string)"RWS_ColonyFav_TipMetricNoHistory".Translate(),
                    _ => (string)"RWS_ColonyFav_TipNoValue".Translate(),
                };
                tip.Append("\n\n").Append(statusReason);
            }

            cards[i] = new ColonyFavoriteCard(fav, colonyName, metricLabel, subtitle, valueText, tip.ToString());
        }
        return cards;
    }

    private static string FallbackUnknownMetricLabel(string metricKey)
    {
        var dot = metricKey.LastIndexOf('.');
        var tail = dot >= 0 && dot + 1 < metricKey.Length ? metricKey[(dot + 1)..] : metricKey;
        return tail.Replace('_', ' ').Replace('-', ' ');
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
                    new Cell(BasicLabel(key), formatted, Basis(view), key, ShowStar: false));
            }

            if (MetricLookup.Overall(view, key) is { } row)
                return new FavoriteCell(key, new Cell(MetricText.Label(row.Definition),
                    MetricText.Format(row.Definition, row.Value), MetricText.Tip(row, view.Stats), key, ShowStar: false,
                    Source: MetricText.ModSourceLabel(row.Definition)));

            var definition = MetricLookup.Definition(view, key);
            return new FavoriteCell(key, new Cell(definition is null ? key : MetricText.Label(definition),
                Dialog_RimworldStatistics.Dash,
                definition is null ? (string)"RWS_Fav_Unknown".Translate()
                    : (string)"RWS_Fav_NoOverall".Translate(), key, ShowStar: false,
                Source: definition is null ? null : MetricText.ModSourceLabel(definition)));
        }).ToArray();
    }

    internal static string BasicLabel(string key) => key switch
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
