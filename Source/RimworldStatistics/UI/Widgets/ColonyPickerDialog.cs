using RimworldStatistics.Core;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>Searchable colony selector that matches custom display names, original faction names, and world names.</summary>
internal sealed class ColonyPickerDialog : Window
{
    private const float ItemHeight = 32f;

    private sealed record Entry(ColonyStats Stats, string Primary, string Detail, string FullTip, bool IsSelected);

    private readonly ViewPreferences? preferences;
    private readonly Entry[] all;
    private readonly HashSet<string> allKeys;
    private readonly Dictionary<string, Entry> entryByKey;
    private readonly Action<ColonyStats> onPick;
    private readonly string heading;
    private string search = "";
    private string? priorSearch;
    private Entry[] filtered = [];
    private IReadOnlyList<Entry> recent = [];
    private string? priorRecentSignature;
    private Vector2 scroll;

    public ColonyPickerDialog(
        IReadOnlyList<ColonyStats> colonies,
        ViewPreferences? preferences,
        Action<ColonyStats> onPick,
        string? heading = null,
        string? selectedColonyKey = null,
        string? currentInGameColonyKey = null)
    {
        this.preferences = preferences;
        this.onPick = onPick;
        this.heading = heading ?? (string)"RWS_ColonyFav_PickColony".Translate();
        all = colonies
            .OrderByDescending(c => currentInGameColonyKey is not null && string.Equals(c.Key, currentInGameColonyKey, StringComparison.Ordinal))
            .ThenBy(c => c.Excluded ? 1 : 0)
            .ThenBy(c => c.OutOfScope ? 1 : 0)
            .ThenBy(c => Dialog_RimworldStatistics.ColonyName(c, preferences), StringComparer.CurrentCulture)
            .ThenByDescending(c => c.MaxTicks)
            .Select(c => BuildEntry(c, preferences, selectedColonyKey, currentInGameColonyKey))
            .ToArray();
        allKeys = new HashSet<string>(StringComparer.Ordinal);
        entryByKey = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var entry in all)
        {
            var key = entry.Stats.Key;
            allKeys.Add(key);
            entryByKey.TryAdd(key, entry);
        }
        doCloseX = true;
        absorbInputAroundWindow = true;
        closeOnClickedOutside = true;
    }

    public override Vector2 InitialSize => new(500f, 520f);

    public override void DoWindowContents(Rect rect)
    {
        try
        {
            var y = rect.y;
            var headingRect = new Rect(rect.x, y, Math.Max(0f, rect.width - 32f), 26f);
            var shownHeading = heading.Truncate(headingRect.width);
            Verse.Widgets.Label(headingRect, shownHeading);
            if (shownHeading != heading)
                TooltipHandler.TipRegion(headingRect, heading);
            y += 28f;

            var searchLabel = (string)"RWS_Colonies_Search".Translate();
            var searchLabelRect = new Rect(rect.x, y, Math.Max(0f, rect.width - 32f), 24f);
            Verse.Widgets.Label(searchLabelRect, searchLabel.Truncate(searchLabelRect.width));
            TooltipHandler.TipRegion(searchLabelRect, "RWS_Colonies_SearchTip".Translate());

            var next = Verse.Widgets.TextField(new Rect(rect.x, y + 26f, rect.width, 28f), search) ?? "";
            if (next != search)
            {
                search = next;
                scroll = Vector2.zero;
            }
            if (priorSearch != search)
            {
                priorSearch = search;
                filtered = string.IsNullOrWhiteSpace(search)
                    ? all
                    : all.Where(e => ColonyPresentation.MatchesSearch(
                        e.Stats, preferences, Dialog_RimworldStatistics.UnnamedColonyFallback, search)).ToArray();
            }
            RefreshRecent();

            var listTop = y + 60f;
            var listRect = new Rect(rect.x, listTop, rect.width, Math.Max(0f, rect.yMax - listTop));
            // The optional title row and the recent rows share the scroll view with the search results.
            var recentRows = recent.Count == 0 ? 0 : recent.Count + 1;
            var totalRows = recentRows + filtered.Length;
            if (totalRows == 0)
            {
                Verse.Widgets.Label(listRect, "RWS_Details_NoMatch".Translate());
                return;
            }

            var viewWidth = Math.Max(0f, listRect.width - 16f);
            ColonyStats? picked = null;
            using (UiScopes.ScrollView(listRect, ref scroll, new Rect(0f, 0f, viewWidth, totalRows * ItemHeight)))
            using (UiScopes.GuiState())
            {
                var first = Math.Max(0, (int)(scroll.y / ItemHeight));
                var last = Math.Min(totalRows - 1, (int)((scroll.y + listRect.height) / ItemHeight) + 1);
                for (var i = first; i <= last; i++)
                {
                    if (recentRows > 0 && i == 0)
                    {
                        var anchor = Text.Anchor;
                        Text.Anchor = TextAnchor.MiddleLeft;
                        Verse.Widgets.Label(new Rect(4f, 0f, Math.Max(0f, viewWidth - 8f), ItemHeight),
                            "RWS_Picker_RecentlyUsed".Translate());
                        Text.Anchor = anchor;
                        continue;
                    }
                    if (i < recentRows)
                    {
                        var recentEntry = recent[i - 1];
                        if (DrawEntryRow(recentEntry, i * ItemHeight, viewWidth))
                            picked = recentEntry.Stats;
                        continue;
                    }
                    var entry = filtered[i - recentRows];
                    if (DrawEntryRow(entry, i * ItemHeight, viewWidth))
                        picked = entry.Stats;
                }
            }
            if (picked is { } pickedStats)
            {
                // onPick may persist view state (which re-serializes this dialog's older preferences snapshot), so
                // record the recent key afterwards or it would be overwritten by that save.
                onPick(pickedStats);
                Dialog_RimworldStatistics.UpdateActivePreferences(p => RecentList.Add(p.RecentColonyKeys, pickedStats.Key));
                Close();
            }
        }
        catch (Exception e)
        {
            Log.ErrorOnce($"[RimworldStatistics] The colony picker window failed to draw.\n{e}", "RWS.colonyPicker".GetHashCode());
            Verse.Widgets.Label(rect.ContractedBy(20f), "RWS_WindowError".Translate());
        }
        finally
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = true;
            GUI.color = Color.white;
        }
    }

    private void RefreshRecent()
    {
        var prefs = Dialog_RimworldStatistics.ActivePreferences;
        var keys = prefs.RecentColonyKeys;
        var signature = search + "\u0000" + string.Join("\u0000", keys);
        if (string.Equals(signature, priorRecentSignature, StringComparison.Ordinal))
            return;
        priorRecentSignature = signature;
        var visible = RecentList.Visible(keys, allKeys);
        var found = new List<Entry>(visible.Count);
        foreach (var key in visible)
        {
            if (entryByKey.TryGetValue(key, out var entry))
                found.Add(entry);
        }
        recent = found.ToArray();
    }

    private static bool DrawEntryRow(Entry entry, float top, float width)
    {
        var row = new Rect(0f, top, width, ItemHeight - 1f);
        if (entry.IsSelected)
            Verse.Widgets.DrawBoxSolid(row, new Color(0.28f, 0.52f, 0.72f, 0.22f));
        Verse.Widgets.DrawHighlightIfMouseover(row);
        var line = entry.Detail.Length == 0 ? entry.Primary : $"{entry.Primary} · {entry.Detail}";
        var shown = line.Truncate(Math.Max(0f, row.width - 8f));
        var anchor = Text.Anchor;
        Text.Anchor = TextAnchor.MiddleLeft;
        if (entry.Stats.Excluded || entry.Stats.OutOfScope)
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
        Verse.Widgets.Label(new Rect(4f, row.y, Math.Max(0f, row.width - 8f), row.height), shown);
        GUI.color = Color.white;
        Text.Anchor = anchor;
        TooltipHandler.TipRegion(row, entry.FullTip);
        return Verse.Widgets.ButtonInvisible(row);
    }

    private static Entry BuildEntry(
        ColonyStats stats,
        ViewPreferences? preferences,
        string? selectedColonyKey,
        string? currentInGameColonyKey)
    {
        var isSelected = selectedColonyKey is not null && string.Equals(stats.Key, selectedColonyKey, StringComparison.Ordinal);
        var isCurrentInGame = currentInGameColonyKey is not null && string.Equals(stats.Key, currentInGameColonyKey, StringComparison.Ordinal);
        var display = (isSelected ? "✓ " : "")
            + Dialog_RimworldStatistics.ColonyName(stats, preferences)
            + (isCurrentInGame ? (string)"RWS_InGame_CurrentColonyMark".Translate() : "")
            + (stats.Excluded ? (string)"RWS_ExcludedMark".Translate() : "");
        var parts = new List<string>(3);
        var tipLines = new List<string>(4) { display };
        if (ColonyPresentation.HasCustomName(preferences, stats))
        {
            var original = Dialog_RimworldStatistics.OriginalColonyName(stats);
            parts.Add(original);
            tipLines.Add((string)"RWS_ColonyName_Original".Translate(original));
        }
        if (!string.IsNullOrWhiteSpace(stats.WorldName))
        {
            parts.Add(stats.WorldName!);
            tipLines.Add((string)"RWS_Col_World".Translate() + ": " + stats.WorldName);
        }
        if (stats.HasObservedPoint)
        {
            var days = Dialog_RimworldStatistics.DaysValue(stats.MaxTicks);
            parts.Add(days);
            tipLines.Add((string)"RWS_Col_Days".Translate() + ": " + days);
        }
        return new Entry(stats, display, string.Join(" · ", parts), string.Join("\n", tipLines), isSelected);
    }
}

internal static class ColonyChartPicker
{
    public static void OpenMetricPicker(StatsView view, ColonyStats colony, ViewPreferences preferences, Action savePreferences)
    {
        var latestPrefs = Dialog_RimworldStatistics.ActivePreferences;
        var colonyDisplay = Dialog_RimworldStatistics.ColonyName(colony, latestPrefs);
        var choices = ColonyHistoryFavorites.CandidateMetricKeys(view, colony.Key)
            .Select(key =>
            {
                var isFavorite = ColonyHistoryFavorites.IsFavorite(latestPrefs, colony.Key, colony.AliasKeys, key);
                string metricLabel;
                string valueText;
                string? source = null;
                string? labelKey = null;
                string? ownerPackageId = null;
                var hasValue = false;
                if (BasicMetrics.Contains(key))
                {
                    var def = BasicMetrics.Definition(key);
                    metricLabel = MetricText.PerColonyBasicLabel(key);
                    labelKey = MetricText.PerColonyBasicLabelKey(key);
                    if (BasicMetrics.Value(colony, key) is { } number && double.IsFinite(number))
                    {
                        valueText = MetricText.Format(def, new NumericValue { Number = number, Coverage = MetricCoverage.SnapshotOnly });
                        hasValue = true;
                    }
                    else
                    {
                        valueText = Dialog_RimworldStatistics.Dash;
                    }
                }
                else
                {
                    var def = MetricLookup.Definition(view, key) ?? MetricRegistry.Find(key);
                    metricLabel = def is null ? key : MetricText.Label(def);
                    source = def is null ? null : MetricText.ModSourceLabel(def);
                    labelKey = def?.LabelKey;
                    ownerPackageId = def?.OwnerPackageId;
                    var row = def is null ? null : MetricLookup.ForColony(view, colony.Key, def.Key);
                    if (def is not null && row is not null
                        && row.Value.Coverage is not (MetricCoverage.Unknown or MetricCoverage.NotApplicable)
                        && row.Value.Number is { } measured && double.IsFinite(measured))
                    {
                        valueText = MetricText.Format(def, row.Value);
                        hasValue = true;
                    }
                    else
                    {
                        valueText = Dialog_RimworldStatistics.Dash;
                    }
                }

                var prefix = isFavorite ? "★ " : "☆ ";
                return (
                    SortGroup: isFavorite ? 0 : hasValue ? 1 : 2,
                    SortLabel: metricLabel,
                    Choice: new MetricPickerChoice(key, $"{prefix}{metricLabel} ({valueText})", source,
                        labelKey, ModPackageId: ownerPackageId));
            })
            .OrderBy(x => x.SortGroup)
            .ThenBy(x => x.SortLabel, StringComparer.CurrentCulture)
            .ThenBy(x => x.Choice.Key, StringComparer.Ordinal)
            .Select(x => x.Choice)
            .ToArray();

        UiScopes.OpenOrReplaceWindow(new MetricPickerDialog(
            choices,
            pickedKey =>
            {
                var changed = false;
                Dialog_RimworldStatistics.UpdateActivePreferences(active =>
                {
                    changed = ColonyHistoryFavorites.Toggle(active, colony.Key, colony.AliasKeys, pickedKey);
                    if (changed && !ReferenceEquals(active, preferences))
                        ColonyHistoryFavorites.Toggle(preferences, colony.Key, colony.AliasKeys, pickedKey);
                });
                if (changed && RimworldStatisticsMod.Settings is null)
                    savePreferences();
            },
            heading: (string)"RWS_ColonyFav_PickMetric".Translate(colonyDisplay)));
    }
}
