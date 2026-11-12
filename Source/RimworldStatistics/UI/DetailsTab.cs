using System.Globalization;
using System.Text;
using RimworldStatistics.Core;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>
/// The numeric statistics (numeric expansion design §4): all colonies or one colony, a category filter, a search over the
/// shown names, and sorting by name or value. Numbers only. The rows are built from the cached immutable
/// <see cref="NumericView"/> when an input changes; drawing touches only the visible rows.
/// </summary>
public static class DetailsTab
{
    private const float ControlHeight = 30f;
    private const float RowHeight = 26f;
    private static ColumnPlan? tablePlan;
    private static ViewPreferences? favoritePreferences;
    private static Action? persistFavorites;
    private static float tableWidth;
    private static object? tableLanguage;
    private static (string Text, string? Full)[][] cellCache = [];
    private static Func<string>?[] tipCache = [];
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private sealed record Line(string Label, string Value, string State, string Count, Func<string> Tip, int TipId, int Depth, string? ExpandKey, bool Dim, string? MetricKey);

    private sealed class LazyTip(Func<string> build)
    {
        private string? _text;
        public string Get() => _text ??= build();
    }

    private static int buildVersion;

    private enum DetailsViewMode { Lifetime, Other, All }
    private enum DetailsStatusFilter { All, Measured, Partial, Missing, Experimental }

    private sealed record Inputs(NumericView View, StatsResult Stats, bool PerColony, string? Colony, MetricCategory? Category, string Search,
        bool ByValue, bool Ascending, int ExpandedVersion, DetailsViewMode Mode, DetailsStatusFilter StatusFilter, object? Language);

    private static DetailsViewMode viewMode = DetailsViewMode.Lifetime;
    private static bool perColony;
    private static string? colonyKey;
    private static MetricCategory? category;
    private static string search = "";
    private static bool byValue;
    private static bool ascending = true;
    private static DetailsStatusFilter statusFilter;
    private static readonly HashSet<string> Expanded = new(StringComparer.Ordinal);
    private static int expandedVersion;
    private static Vector2 scroll;
    private static Inputs? builtFor;
    private static List<Line> lines = new();

    public static void Draw(Rect rect, StatsView view, ViewPreferences? favorites = null, Action? saveFavorites = null)
    {
        favoritePreferences = favorites;
        persistFavorites = saveFavorites;
        var numeric = view.Numeric;
        if (numeric.Colonies.Count == 0)
        {
            Widgets.Label(rect, "RWS_NoData".Translate());
            return;
        }
        if (perColony && (colonyKey is null || numeric.Colonies.All(c => c.Key != colonyKey)))
            colonyKey = numeric.Colonies.OrderBy(c => Name(view.Stats, c.Key), StringComparer.CurrentCulture).First().Key;

        var y = rect.y;
        y += DrawControls(new Rect(rect.x, y, rect.width, ControlHeight), view) + 4f;

        GUI.color = new Color(0.8f, 0.8f, 0.8f);
        var note = (string)"RWS_Details_Note".Translate(view.Stats.ColonyCount, view.Stats.ExcludedColonyCount);
        if (view.Stats.OutOfScopeColonyCount > 0)
            note += " " + "RWS_Groups_OutOfScope".Translate(view.Stats.OutOfScopeColonyCount);
        if (numeric.Unattributed > 0)
            note += " " + "RWS_Details_Unattributed".Translate(numeric.Unattributed);
        var noteHeight = Text.CalcHeight(note, rect.width);
        Widgets.Label(new Rect(rect.x, y, rect.width, noteHeight), note);
        GUI.color = Color.white;
        y += noteHeight + 4f;

        const float filterHeight = 25f;
        var filterRect = new Rect(rect.x, y, Math.Min(rect.width, 230f), filterHeight);
        var filterLabel = StatusFilterLabel(statusFilter);
        if (Widgets.ButtonText(filterRect, filterLabel.Truncate(filterRect.width - 10f)))
        {
            Find.WindowStack.Add(new FloatMenu(Enum.GetValues<DetailsStatusFilter>()
                .Select(value => new FloatMenuOption(StatusFilterLabel(value), () =>
                {
                    statusFilter = value;
                    scroll = Vector2.zero;
                })).ToList()));
        }
        y += filterHeight + 4f;

        var language = LanguageDatabase.activeLanguage;
        var inputs = new Inputs(numeric, view.Stats, perColony, colonyKey, category, search, byValue, ascending, expandedVersion, viewMode, statusFilter, language);
        if (builtFor != inputs)
        {
            builtFor = inputs;
            lines = Build(inputs);
            cellCache = new (string, string?)[lines.Count][];
            tipCache = new Func<string>?[lines.Count];
        }

        var available = Math.Max(0, rect.width - 16f);
        if (tablePlan is null || tableWidth != available || !ReferenceEquals(tableLanguage, language))
        {
            tableWidth = available;
            tableLanguage = language;
            string partial = "RWS_Coverage_Partial".Translate();
            var experimental = EvidenceLabel(MetricEvidence.Experimental);
            var stateWidth = Text.CalcSize(experimental + " · " + partial).x + 12f;
            tablePlan = ColumnLayout.Resolve([new(150, 4), new(90, 3), new(Math.Max(115, stateWidth), 2), new(96, 1)], available);
            Array.Clear(cellCache);
            Array.Clear(tipCache);
        }
        var width = tablePlan.TotalWidth;
        GUI.BeginGroup(new Rect(rect.x, y, available, RowHeight));
        DrawHeader(new Rect(-scroll.x, 0, width, RowHeight));
        GUI.EndGroup();
        y += RowHeight;
        var outRect = new Rect(rect.x, y, rect.width, rect.yMax - y);
        if (outRect.height <= 0)
            return;
        if (lines.Count == 0)
        {
            Widgets.Label(outRect, "RWS_Details_NoMatch".Translate());
            return;
        }
        var viewRect = new Rect(0f, 0f, width, lines.Count * RowHeight);
        Widgets.BeginScrollView(outRect, ref scroll, viewRect);
        var first = Math.Max(0, (int)(scroll.y / RowHeight));
        var last = Math.Min(lines.Count - 1, (int)((scroll.y + outRect.height) / RowHeight) + 1);
        for (var i = first; i <= last; i++)
            DrawLine(new Rect(0f, i * RowHeight, width, RowHeight), lines[i], i);
        Widgets.EndScrollView();
    }

    private static float DrawControls(Rect rect, StatsView view)
    {
        var controls = new ControlRow(rect);
        bool Button(string text, float max)
        {
            var button = controls.Next(text, 80f, max);
            var shown = text.Truncate(Math.Max(0, button.width - 12f));
            if (shown != text)
                TooltipHandler.TipRegion(button, text);
            return Widgets.ButtonText(button, shown);
        }

        if (Button(ViewModeLabel(viewMode), 250f))
        {
            Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
            {
                new(ViewModeLabel(DetailsViewMode.Lifetime), () => viewMode = DetailsViewMode.Lifetime),
                new(ViewModeLabel(DetailsViewMode.Other), () => viewMode = DetailsViewMode.Other),
                new(ViewModeLabel(DetailsViewMode.All), () => viewMode = DetailsViewMode.All),
            }));
        }

        if (Button(perColony ? "RWS_Details_PerColony".Translate() : "RWS_Details_AllColonies".Translate(), 240f))
            perColony = !perColony;
        if (perColony && colonyKey is { } selected)
        {
            if (Button(Name(view.Stats, selected), 260f))
            {
                var options = view.Numeric.Colonies
                    .OrderBy(c => Name(view.Stats, c.Key), StringComparer.CurrentCulture)
                    .Select(c => new FloatMenuOption(Name(view.Stats, c.Key) + (c.Excluded ? (string)"RWS_ExcludedMark".Translate() : ""), () => colonyKey = c.Key))
                    .ToList();
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }
        if (Button(category is { } c0 ? CategoryLabel(c0) : (string)"RWS_Details_AllCategories".Translate(), 240f))
        {
            var options = new List<FloatMenuOption> { new("RWS_Details_AllCategories".Translate(), () => category = null) };
            options.AddRange(view.Numeric.Colonies.SelectMany(c => c.Rows).Select(r => r.Definition.Category)
                .Concat(view.Numeric.Overall.Select(r => r.Definition.Category)).Distinct()
                .Select(c => new FloatMenuOption(CategoryLabel(c), () => category = c)));
            Find.WindowStack.Add(new FloatMenu(options));
        }
        var searchLabel = (string)"RWS_Details_Search".Translate();
        var labelWidth = Math.Min(Text.CalcSize(searchLabel).x + 6f, Math.Max(0, rect.width - 120f));
        var searchArea = controls.Next(labelWidth + 120f, fill: true);
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(new Rect(searchArea.x, searchArea.y, labelWidth, searchArea.height), searchLabel.Truncate(labelWidth));
        Text.Anchor = TextAnchor.UpperLeft;
        var field = new Rect(searchArea.x + labelWidth, searchArea.y + 2f, searchArea.width - labelWidth, searchArea.height - 4f);
        search = Widgets.TextField(field, search) ?? "";
        return controls.Height;
    }

    private static string StatusFilterLabel(DetailsStatusFilter filter) => filter switch
    {
        DetailsStatusFilter.Measured => "RWS_Details_StatusMeasured".Translate(),
        DetailsStatusFilter.Partial => "RWS_Details_StatusPartial".Translate(),
        DetailsStatusFilter.Missing => "RWS_Details_StatusMissing".Translate(),
        DetailsStatusFilter.Experimental => "RWS_Details_StatusExperimental".Translate(),
        _ => "RWS_Details_StatusAll".Translate(),
    };

    private static string ViewModeLabel(DetailsViewMode mode) => mode switch
    {
        DetailsViewMode.Lifetime => "RWS_Details_ViewLifetime".Translate(),
        DetailsViewMode.Other => "RWS_Details_ViewOther".Translate(),
        _ => "RWS_Details_ViewAll".Translate(),
    };

    private static void DrawHeader(Rect rect)
    {
        var headers = new[]
        {
            (string)"RWS_Details_Col_Name".Translate() + (!byValue ? (ascending ? " ▲" : " ▼") : ""),
            (string)"RWS_Details_Col_Value".Translate() + (byValue ? (ascending ? " ▲" : " ▼") : ""),
            (string)"RWS_Details_Col_State".Translate(),
            perColony ? (string)"RWS_Details_Col_Through".Translate() : (string)"RWS_Details_Col_Colonies".Translate(),
        };
        var x = rect.x;
        GUI.color = new Color(0.8f, 0.8f, 0.8f);
        Text.Anchor = TextAnchor.MiddleLeft;
        for (var c = 0; c < headers.Length; c++)
        {
            var cell = new Rect(x, rect.y, tablePlan!.Widths[c], rect.height);
            Widgets.Label(new Rect(cell.x + 3f, cell.y, cell.width - 6f, cell.height), headers[c]);
            if (c <= 1)
            {
                Widgets.DrawHighlightIfMouseover(cell);
                if (Widgets.ButtonInvisible(cell))
                {
                    var wantValue = c == 1;
                    if (byValue == wantValue)
                        ascending = !ascending;
                    else
                        (byValue, ascending) = (wantValue, !wantValue);   // values start largest first
                }
            }
            x += cell.width;
        }
        if (perColony)
            TooltipHandler.TipRegion(new Rect(x - tablePlan!.Widths[3], rect.y, tablePlan.Widths[3], rect.height), "RWS_Details_ThroughTip".Translate());
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;
    }

    private static void DrawLine(Rect rect, Line line, int index)
    {
        if (index % 2 == 1)
            Widgets.DrawHighlight(rect, 0.25f);
        Widgets.DrawHighlightIfMouseover(rect);
        var widths = tablePlan!.Widths;
        if (cellCache[index] is not { } cells)
        {
            var values = new[]
            {
                new string(' ', line.Depth * 4) + (line.ExpandKey is { } k ? (Expanded.Contains(k) ? "▾ " : "▸ ") : "") + line.Label,
                line.Value, line.State, line.Count,
            };
            cells = new (string Text, string? Full)[values.Length];
            for (var c = 0; c < values.Length; c++)
            {
                var space = widths[c] - 6f - (c == 0 && line.MetricKey is not null ? 28f : 0f);
                var shown = values[c].Truncate(Math.Max(1f, space));
                cells[c] = (shown, shown == values[c] ? null : values[c]);
            }
            cellCache[index] = cells;
            var fullText = string.Join("\n", cells.Where(c => c.Full is not null).Select(c => c.Full));
            tipCache[index] = fullText.Length == 0 ? line.Tip : new LazyTip(() => fullText + "\n\n" + line.Tip()).Get;
        }
        var x = rect.x;
        Text.Anchor = TextAnchor.MiddleLeft;
        for (var c = 0; c < cells.Length; c++)
        {
            var hasStar = c == 0 && line.MetricKey is not null;
            var cell = new Rect(x + (hasStar ? 30f : 3f), rect.y,
                widths[c] - (hasStar ? 34f : 6f), rect.height);
            GUI.color = c == 1 && line.Dim ? new Color(0.75f, 0.75f, 0.75f) : Color.white;
            Widgets.Label(cell, cells[c].Text);
            x += widths[c];
        }
        GUI.color = Color.white;
        Text.Anchor = TextAnchor.UpperLeft;
        TooltipHandler.TipRegion(rect, tipCache[index]!, line.TipId);
        if (line.MetricKey is { } metricKey && favoritePreferences is not null)
        {
            var starRect = new Rect(rect.x + 2f, rect.y + 2f, 25f, rect.height - 4f);
            var saved = favoritePreferences.FavoriteMetricKeys.Contains(metricKey, StringComparer.Ordinal);
            if (Widgets.ButtonText(starRect, saved ? "★" : "☆"))
            {
                if (saved)
                    favoritePreferences.FavoriteMetricKeys.Remove(metricKey);
                else if (favoritePreferences.FavoriteMetricKeys.Count < 500)
                    favoritePreferences.FavoriteMetricKeys.Add(metricKey);
                persistFavorites?.Invoke();
            }
            TooltipHandler.TipRegion(starRect, "RWS_Fav_StarTip".Translate());
        }
        if (line.ExpandKey is { } key && Widgets.ButtonInvisible(new Rect(
                rect.x + (line.MetricKey is null ? 0f : 30f), rect.y,
                rect.width - (line.MetricKey is null ? 0f : 30f), rect.height)))
        {
            if (!Expanded.Remove(key))
                Expanded.Add(key);
            expandedVersion++;
        }
    }

    // ----- building the rows (only when an input changes) -----

    private static List<Line> Build(Inputs input)
    {
        var version = unchecked(++buildVersion);
        IReadOnlyList<NumericRow> rows;
        NumericColony? colony = null;
        if (input.PerColony)
        {
            colony = input.View.Colonies.FirstOrDefault(c => c.Key == input.Colony);
            rows = colony?.Rows ?? (IReadOnlyList<NumericRow>)Array.Empty<NumericRow>();
        }
        else
        {
            rows = input.View.Overall;
        }

        var term = input.Search.Trim();
        var selected = rows
            .Where(r => input.Category is null || r.Definition.Category == input.Category)
            .Where(r => input.StatusFilter switch
            {
                DetailsStatusFilter.Measured => MetricDisplayPolicy.HasMeasuredNumber(r),
                DetailsStatusFilter.Partial => MetricStatusClassifier.IsPartial(r),
                DetailsStatusFilter.Missing => !MetricDisplayPolicy.HasMeasuredNumber(r)
                    && r.Value.Coverage != MetricCoverage.NotApplicable,
                DetailsStatusFilter.Experimental => MetricStatusClassifier.IsExperimental(r),
                _ => true,
            })
            .Where(r => input.Mode switch
            {
                DetailsViewMode.Lifetime => MetricDisplayPolicy.IsLifetimePrimaryRow(r.Definition),
                DetailsViewMode.Other => !MetricDisplayPolicy.IsLifetimeCumulative(r.Definition),
                _ => true,
            })
            .Select(r => (Row: r, Label: DefinitionLabel(r.Definition)))
            .Where(x => term.Length == 0
                || x.Label.IndexOf(term, StringComparison.CurrentCultureIgnoreCase) >= 0
                || x.Row.Definition.Key.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0
                || x.Row.Definition.LedgerIds.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();

        IEnumerable<(NumericRow Row, string Label)> ordered;
        if (input.ByValue)
        {
            // Unknown values go last in either direction.
            var known = selected.Where(x => x.Row.Value.Number is not null);
            var unknown = selected.Where(x => x.Row.Value.Number is null).OrderBy(x => x.Label, StringComparer.CurrentCulture);
            ordered = (input.Ascending ? known.OrderBy(x => x.Row.Value.Number) : known.OrderByDescending(x => x.Row.Value.Number))
                .ThenBy(x => x.Label, StringComparer.CurrentCulture).Concat(unknown);
        }
        else
        {
            ordered = input.Ascending
                ? selected.OrderBy(x => x.Label, StringComparer.CurrentCulture)
                : selected.OrderByDescending(x => x.Label, StringComparer.CurrentCulture);
        }

        var result = new List<Line>();
        foreach (var (row, label) in ordered)
        {
            var crossTop = CrossColonyCatalog.IsDerived(row.Definition)
                && row.Definition.Key.StartsWith(CrossColonyCatalog.TopPrefix, StringComparison.Ordinal);
            var expandable = row.Breakdowns.Count > 0 && (row.Definition.HasBreakdown || crossTop);
            var expandKey = expandable ? (input.PerColony ? "c:" : "a:") + row.Definition.Key : null;
            result.Add(MakeLine(row, label, 0, expandKey, input, colony, tipId: unchecked(0x52570000 + version * 4099 + result.Count)));
            if (expandKey is null || !Expanded.Contains(expandKey))
                continue;
            foreach (var b in row.Breakdowns.OrderByDescending(b => b.Value.Number ?? double.MinValue).ThenBy(b => b.Breakdown, StringComparer.Ordinal))
            {
                var childKey = crossTop && b.Breakdowns.Count > 0 ? expandKey + ":" + b.Breakdown : null;
                var sourceDef = crossTop ? MetricRegistry.Find(row.Definition.StoredKey) ?? row.Definition : row.Definition;
                var breakdownLabel = crossTop && row.Definition.DescriptionKey == "RWS_CrossDesc_TopType"
                    ? ItemLabel(b.Breakdown)
                    : BreakdownLabel(sourceDef, b.Breakdown);
                result.Add(MakeLine(b, breakdownLabel, 1, childKey, input, colony, tipId: unchecked(0x52570000 + version * 4099 + result.Count)));
                if (childKey is not null && Expanded.Contains(childKey))
                    foreach (var contribution in b.Breakdowns)
                        result.Add(MakeLine(contribution, Name(input.Stats, contribution.Breakdown),
                            2, null, input, colony, tipId: unchecked(0x52570000 + version * 4099 + result.Count)));
            }
        }
        return result;
    }

    private static Line MakeLine(NumericRow row, string label, int depth, string? expandKey, Inputs input, NumericColony? colony, int tipId)
    {
        var d = row.Definition;
        var v = row.Value;
        var count = input.PerColony
            ? v.ThroughTick is { } t ? Dialog_RimworldStatistics.Days(t) : Dialog_RimworldStatistics.Dash
            : $"{row.Valid.ToString(Inv)} / {row.Targets.ToString(Inv)}";

        var value = Format(row.Breakdown.Length > 0 && d.BreakdownUnit is { } bu ? d with { Unit = bu } : d, v);
        if ((d.Category == MetricCategory.Rankings || CrossColonyCatalog.IsDerived(d)) && row.Breakdown.Length == 0)
        {
            // Cross-colony records retain their original category but still
            // show the winning item or the colony holding the record.
            if (v.Label is { } holderLabel)
                value += " · " + (CrossColonyCatalog.IsDerived(d) && d.Key.StartsWith(CrossColonyCatalog.TopPrefix, StringComparison.Ordinal)
                    ? string.Join(" / ", holderLabel.Split(new[] { " / " }, StringSplitOptions.None).Select(ItemLabel))
                    : holderLabel);
            else if (v.ColonyKey is { } holderColony)
                value += " · " + Name(input.Stats, holderColony);
        }
        var state = CoverageLabel(d, v.Coverage);
        if (d.Acquisition is not null && !d.Published)
            state = (string)"RWS_Exp_ExperimentalRow".Translate() + " · " + state;
        var lazy = new LazyTip(() => BuildTip(row, label, depth, input, colony));
        return new Line(label, value, state, count, lazy.Get, tipId, depth, expandKey,
            v.Coverage is MetricCoverage.Partial or MetricCoverage.Unknown, depth == 0 ? d.Key : null);
    }

    private static string BuildTip(NumericRow row, string label, int depth, Inputs input, NumericColony? colony)
    {
        var d = row.Definition;
        var v = row.Value;
        var tip = new StringBuilder();
        tip.Append(label).Append('\n')
            .Append(d.ProviderId is not null && d.DescriptionKey == d.Key ? d.Key
                : (string)d.DescriptionKey.Translate()).Append("\n\n");
        if (d.OwnerPackageId is { } owner)
        {
            tip.Append((string)"RWS_Mod_Provider".Translate(owner, d.ProviderId ?? owner)).Append('\n');
            if (!ModsConfig.IsActive(owner))
                tip.Append((string)"RWS_Mod_SourceUnavailable".Translate()).Append('\n');
        }
        tip.Append((string)"RWS_Details_Tip_Source".Translate(ProvenanceLabel(d.Provenance), CoverageLabel(d, v.Coverage))).Append('\n');
        if (d.Acquisition is { } id && !d.Published)
        {
            tip.Append((string)"RWS_Exp_ExperimentalRow".Translate()).Append(" (").Append(id).Append(")\n");
            tip.Append("RWS_Exp_Evidence".Translate(EvidenceLabel(v.Evidence))).Append('\n');
            if (v.SinceTick is { } first)
                tip.Append("RWS_Exp_Since".Translate(Dialog_RimworldStatistics.Days(first))).Append('\n');
            if (d.RequiredDlc is { } dlc)
                tip.Append("RWS_Exp_RequiresDlc".Translate(dlc)).Append('\n');
        }
        if (input.PerColony)
        {
            if (v.ThroughTick is { } through)
                tip.Append((string)"RWS_Details_Tip_Through".Translate(Dialog_RimworldStatistics.Days(through))).Append('\n');
            tip.Append(d.Provenance == MetricProvenance.SaveSnapshot ? (string)"RWS_Details_Tip_StartSnapshot".Translate() : (string)"RWS_Details_Tip_StartGame".Translate()).Append('\n');
            if (colony is not null)
                tip.Append((string)"RWS_Details_Tip_Observations".Translate(colony.Observations)).Append('\n');
        }
        else
        {
            tip.Append((string)"RWS_Details_Tip_Colonies".Translate(row.Targets, row.Valid)).Append('\n');
            if (depth == 0)
            {
                var stateOfCapture = MetricAvailabilityPolicy.Classify(row) switch
                {
                    MetricAvailability.NoApplicableColonies => "RWS_Details_AvailabilityNone".Translate(),
                    MetricAvailability.NoMeasuredColonies => "RWS_Details_AvailabilityMissing".Translate(),
                    MetricAvailability.SomeMeasuredColonies => "RWS_Details_AvailabilitySome".Translate(),
                    _ => "RWS_Details_AvailabilityAll".Translate(),
                };
                tip.Append(stateOfCapture).Append('\n');
                if (MetricAvailabilityPolicy.IsPeriodIncomplete(row))
                    tip.Append((string)"RWS_Details_AvailabilityPartialPeriod".Translate()).Append('\n');
            }
            if (depth == 1 && CrossColonyCatalog.IsDerived(d)
                && d.Key.StartsWith(CrossColonyCatalog.TopPrefix, StringComparison.Ordinal))
                tip.Append((string)"RWS_Cross_RankedTypeExplanation".Translate()).Append('\n');
            if (depth == 2 && CrossColonyCatalog.IsDerived(d))
                tip.Append((string)"RWS_Cross_ContributionExplanation".Translate()).Append('\n');
            if (v.ColonyKey is { } holder)
                tip.Append((string)"RWS_Details_Tip_Holder".Translate(Name(input.Stats, holder))).Append('\n');
            if (v.HolderKeys is { Count: > 1 } tied)
                tip.Append((string)"RWS_Cross_TiedHolders".Translate(string.Join(", ", tied.Select(k => Name(input.Stats, k))))).Append('\n');
            if (CrossColonyCatalog.IsDerived(d) && v.ThroughTick is { } recorded)
                tip.Append((string)"RWS_Cross_RecordedAt".Translate(Dialog_RimworldStatistics.Days(recorded))).Append('\n');
        }
        if (v.SampleCount is { } n)
            tip.Append((string)"RWS_Details_Tip_Samples".Translate(n)).Append('\n');
        if (v.Numerator is { } num && v.Denominator is { } den && d.Overall == OverallAggregation.Ratio)
            tip.Append((string)"RWS_Details_Tip_Ratio".Translate(Number(num), Number(den))).Append('\n');
        if (v.Coverage == MetricCoverage.Unknown)
        {
            tip.Append((string)"RWS_Details_Tip_Unknown".Translate()).Append('\n');
            if (d.Acquisition is not null)
                tip.Append((string)"RWS_Details_MissingAcquisitionTip".Translate()).Append('\n');
        }
        tip.Append((string)"RWS_Details_Tip_Ledger".Translate(d.LedgerIds, d.Key));
        return tip.ToString();
    }

    // ----- formatting -----

    private static string Number(double value)
        => Math.Abs(value - Math.Round(value)) < 1e-9 ? value.ToString("N0", Inv) : value.ToString("N2", Inv);

    internal static string Format(MetricDefinition d, NumericValue v)
    {
        if (MetricDisplayPolicy.IsUnrankedTop(d, v))
            return "RWS_Details_UnrankedTop".Translate();
        if (v.NotReached)
            return "RWS_Details_NotReached".Translate();
        if (v.Number is not { } n)
            return Dialog_RimworldStatistics.Dash;
        var sign = d.Signed && n > 0 ? "+" : "";
        return d.Unit switch
        {
            MetricUnit.Silver => sign + StatFormat.Group(n),
            MetricUnit.Count => Math.Abs(n - Math.Round(n)) < 1e-9 ? n.ToString("N0", Inv) : n.ToString("N1", Inv),
            MetricUnit.Days => "RWS_Unit_Days".Translate(n.ToString("N1", Inv)),
            MetricUnit.Percent => sign + (n * 100).ToString("N1", Inv) + "%",
            MetricUnit.Mood => n.ToString("N1", Inv) + "%",
            MetricUnit.PerYear => "RWS_Unit_PerYear".Translate(n.ToString("N2", Inv)),
            MetricUnit.Hours => "RWS_HoursValue".Translate(n.ToString("N1", Inv)),
            MetricUnit.SilverPerYear => "RWS_Unit_SilverPerYear".Translate(sign + StatFormat.Group(n)),
            MetricUnit.Years => "RWS_Unit_Years".Translate(n.ToString("N1", Inv)),
            MetricUnit.Celsius => n.ToString("N1", Inv) + " °C",
            MetricUnit.Watts => n.ToString("N1", Inv) + " W",
            MetricUnit.WattDays => n.ToString("N1", Inv) + " Wd",
            MetricUnit.Nutrition => n.ToString("N2", Inv) + " nutrition",
            MetricUnit.PerCellDay => "RWS_Unit_PerCellDay".Translate(n.ToString("N3", Inv)),
            _ => n.ToString("N2", Inv),
        };
    }

    internal static string OverallTip(NumericRow row, StatsResult stats)
        => BuildTip(row, DefinitionLabel(row.Definition), 0,
            new Inputs(NumericView.Empty, stats, false, null, null, "", false, true, 0,
                DetailsViewMode.All, DetailsStatusFilter.All, null), null);

    internal static string DefinitionLabel(MetricDefinition d)
    {
        if (CrossColonyCatalog.IsDerived(d) && MetricRegistry.Find(d.StoredKey) is { } source)
            return d.Key.StartsWith(CrossColonyCatalog.MaxPrefix, StringComparison.Ordinal)
                ? (string)"RWS_Cross_MaxColony".Translate((string)source.LabelKey.Translate())
                : d.Key.StartsWith(CrossColonyCatalog.MinPrefix, StringComparison.Ordinal)
                    ? (string)"RWS_Cross_MinColony".Translate((string)source.LabelKey.Translate())
                    : d.Key.StartsWith(CrossColonyCatalog.SumPrefix, StringComparison.Ordinal)
                        ? (string)"RWS_Cross_Sum".Translate((string)source.LabelKey.Translate())
                        : d.DescriptionKey == "RWS_CrossDesc_TopCategory"
                            ? (string)"RWS_Cross_TopCategory".Translate((string)source.LabelKey.Translate())
                            : (string)"RWS_Cross_TopType".Translate((string)source.LabelKey.Translate());
        return d.ProviderId is not null && d.LabelKey == d.Key
            ? d.Key : (string)d.LabelKey.Translate();
    }

    private static string ItemLabel(string defName)
        => DefDatabase<ThingDef>.GetNamedSilentFail(defName)?.label ?? defName;

    private static string BreakdownLabel(MetricDefinition definition, string key)
        => MetricDisplayPolicy.BreakdownLabelKey(definition, key) is { } translation
            ? (string)translation.Translate() : key;

    private static string Name(StatsResult stats, string key)
        => stats.Colonies.FirstOrDefault(c => c.Key == key) is { } s ? Dialog_RimworldStatistics.ColonyName(s) : key;

    // One literal key per value (no string concatenation), so the translation-key check sees every key.
    internal static string CategoryLabel(MetricCategory c) => c switch
    {
        MetricCategory.Wealth => "RWS_Category_Wealth".Translate(),
        MetricCategory.Population => "RWS_Category_Population".Translate(),
        MetricCategory.Defense => "RWS_Category_Defense".Translate(),
        MetricCategory.Quests => "RWS_Category_Quests".Translate(),
        MetricCategory.Colony => "RWS_Category_Colony".Translate(),
        MetricCategory.Research => "RWS_Category_Research".Translate(),
        MetricCategory.Mind => "RWS_Category_Mind".Translate(),
        MetricCategory.Combat => "RWS_Category_Combat".Translate(),
        MetricCategory.Social => "RWS_Category_Social".Translate(),
        MetricCategory.Medicine => "RWS_Category_Medicine".Translate(),
        MetricCategory.Work => "RWS_Category_Work".Translate(),
        MetricCategory.Animals => "RWS_Category_Animals".Translate(),
        MetricCategory.Economy => "RWS_Category_Economy".Translate(),
        MetricCategory.Environment => "RWS_Category_Environment".Translate(),
        MetricCategory.Exploration => "RWS_Category_Exploration".Translate(),
        MetricCategory.Facilities => "RWS_Category_Facilities".Translate(),
        MetricCategory.Royalty => "RWS_Category_Royalty".Translate(),
        MetricCategory.Ideology => "RWS_Category_Ideology".Translate(),
        MetricCategory.Biotech => "RWS_Category_Biotech".Translate(),
        MetricCategory.Odyssey => "RWS_Category_Odyssey".Translate(),
        _ => "RWS_Category_Rankings".Translate(),
    };

    private static string EvidenceLabel(MetricEvidence evidence) => evidence switch
    {
        MetricEvidence.Experimental => "RWS_Exp_EvidenceExperimental".Translate(),
        MetricEvidence.Verified => "RWS_Exp_EvidenceVerified".Translate(),
        MetricEvidence.Mixed => "RWS_Exp_EvidenceMixed".Translate(),
        _ => "RWS_Exp_EvidenceUnknown".Translate(),
    };

    private static string ProvenanceLabel(MetricProvenance p) => p switch
    {
        MetricProvenance.VanillaCounter => "RWS_Provenance_VanillaCounter".Translate(),
        MetricProvenance.SaveHistory => "RWS_Provenance_SaveHistory".Translate(),
        MetricProvenance.SaveSnapshot => "RWS_Provenance_SaveSnapshot".Translate(),
        _ => "RWS_Provenance_LiveAggregate".Translate(),
    };

    internal static string CoverageLabel(MetricDefinition d, MetricCoverage c) => c switch
    {
        MetricCoverage.Complete => "RWS_Coverage_Complete".Translate(),
        MetricCoverage.Partial => "RWS_Coverage_Partial".Translate(),
        MetricCoverage.SnapshotOnly when d.Provenance == MetricProvenance.SaveHistory => "RWS_Coverage_History".Translate(),
        MetricCoverage.SnapshotOnly => "RWS_Coverage_SnapshotOnly".Translate(),
        MetricCoverage.NotApplicable => "RWS_Coverage_NotApplicable".Translate(),
        _ => "RWS_Coverage_Unknown".Translate(),
    };
}
