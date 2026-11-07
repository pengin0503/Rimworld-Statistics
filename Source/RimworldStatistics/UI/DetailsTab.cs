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
    private static readonly float[] Widths = { 0.40f, 0.26f, 0.22f, 0.12f };
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private sealed record Line(string Label, string Value, string State, string Count, string Tip, int Depth, string? ExpandKey, bool Dim);

    private sealed record Inputs(NumericView View, StatsResult Stats, bool PerColony, string? Colony, MetricCategory? Category, string Search,
        bool ByValue, bool Ascending, int ExpandedVersion);

    private static bool perColony;
    private static string? colonyKey;
    private static MetricCategory? category;
    private static string search = "";
    private static bool byValue;
    private static bool ascending = true;
    private static readonly HashSet<string> Expanded = new(StringComparer.Ordinal);
    private static int expandedVersion;
    private static Vector2 scroll;
    private static Inputs? builtFor;
    private static List<Line> lines = new();

    public static void Draw(Rect rect, StatsView view)
    {
        var numeric = view.Numeric;
        if (numeric.Colonies.Count == 0)
        {
            Widgets.Label(rect, "RWS_NoData".Translate());
            return;
        }
        if (perColony && (colonyKey is null || numeric.Colonies.All(c => c.Key != colonyKey)))
            colonyKey = numeric.Colonies.OrderBy(c => Name(view.Stats, c.Key), StringComparer.CurrentCulture).First().Key;

        var y = rect.y;
        DrawControls(new Rect(rect.x, y, rect.width, ControlHeight), view);
        y += ControlHeight + 4f;

        GUI.color = new Color(0.8f, 0.8f, 0.8f);
        var note = (string)"RWS_Details_Note".Translate(view.Stats.ColonyCount, view.Stats.ExcludedColonyCount);
        if (numeric.Unattributed > 0)
            note += " " + "RWS_Details_Unattributed".Translate(numeric.Unattributed);
        var noteHeight = Text.CalcHeight(note, rect.width);
        Widgets.Label(new Rect(rect.x, y, rect.width, noteHeight), note);
        GUI.color = Color.white;
        y += noteHeight + 4f;

        var inputs = new Inputs(numeric, view.Stats, perColony, colonyKey, category, search, byValue, ascending, expandedVersion);
        if (builtFor != inputs)
        {
            builtFor = inputs;
            lines = Build(inputs);
        }

        var width = rect.width - 16f;
        DrawHeader(new Rect(rect.x, y, width, RowHeight));
        y += RowHeight;
        var outRect = new Rect(rect.x, y, rect.width, rect.yMax - y);
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

    private static void DrawControls(Rect rect, StatsView view)
    {
        var x = rect.x;
        Rect Next(float w)
        {
            var r = new Rect(x, rect.y, w, rect.height);
            x += w + 6f;
            return r;
        }

        if (Widgets.ButtonText(Next(150f), perColony ? "RWS_Details_PerColony".Translate() : "RWS_Details_AllColonies".Translate()))
            perColony = !perColony;
        if (perColony && colonyKey is { } selected)
        {
            if (Widgets.ButtonText(Next(220f), Name(view.Stats, selected).Truncate(210f)))
            {
                var options = view.Numeric.Colonies
                    .OrderBy(c => Name(view.Stats, c.Key), StringComparer.CurrentCulture)
                    .Select(c => new FloatMenuOption(Name(view.Stats, c.Key) + (c.Excluded ? (string)"RWS_ExcludedMark".Translate() : ""), () => colonyKey = c.Key))
                    .ToList();
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }
        if (Widgets.ButtonText(Next(180f), category is { } c0 ? CategoryLabel(c0) : (string)"RWS_Details_AllCategories".Translate()))
        {
            var options = new List<FloatMenuOption> { new("RWS_Details_AllCategories".Translate(), () => category = null) };
            options.AddRange(MetricRegistry.All.Select(d => d.Category).Distinct().Select(c => new FloatMenuOption(CategoryLabel(c), () => category = c)));
            Find.WindowStack.Add(new FloatMenu(options));
        }
        var searchLabel = (string)"RWS_Details_Search".Translate();
        var labelWidth = Text.CalcSize(searchLabel).x + 6f;
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(Next(labelWidth), searchLabel);
        Text.Anchor = TextAnchor.UpperLeft;
        var field = new Rect(x, rect.y + 2f, Math.Max(80f, rect.xMax - x), rect.height - 4f);
        search = Widgets.TextField(field, search) ?? "";
    }

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
            var cell = new Rect(x, rect.y, rect.width * Widths[c], rect.height);
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
            TooltipHandler.TipRegion(new Rect(rect.x + rect.width * (1f - Widths[3]), rect.y, rect.width * Widths[3], rect.height), "RWS_Details_ThroughTip".Translate());
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;
    }

    private static void DrawLine(Rect rect, Line line, int index)
    {
        if (index % 2 == 1)
            Widgets.DrawHighlight(rect, 0.25f);
        Widgets.DrawHighlightIfMouseover(rect);
        var cells = new[]
        {
            new string(' ', line.Depth * 4) + (line.ExpandKey is { } k ? (Expanded.Contains(k) ? "▾ " : "▸ ") : "") + line.Label,
            line.Value, line.State, line.Count,
        };
        var x = rect.x;
        Text.Anchor = TextAnchor.MiddleLeft;
        for (var c = 0; c < cells.Length; c++)
        {
            var cell = new Rect(x + 3f, rect.y, rect.width * Widths[c] - 6f, rect.height);
            GUI.color = c == 1 && line.Dim ? new Color(0.75f, 0.75f, 0.75f) : Color.white;
            Widgets.Label(cell, cells[c].Truncate(cell.width));
            x += rect.width * Widths[c];
        }
        GUI.color = Color.white;
        Text.Anchor = TextAnchor.UpperLeft;
        TooltipHandler.TipRegion(rect, line.Tip);
        if (line.ExpandKey is { } key && Widgets.ButtonInvisible(rect))
        {
            if (!Expanded.Remove(key))
                Expanded.Add(key);
            expandedVersion++;
        }
    }

    // ----- building the rows (only when an input changes) -----

    private static List<Line> Build(Inputs input)
    {
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
            .Select(r => (Row: r, Label: (string)r.Definition.LabelKey.Translate()))
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
            var expandable = row.Definition.HasBreakdown && row.Breakdowns.Count > 0;
            var expandKey = expandable ? (input.PerColony ? "c:" : "a:") + row.Definition.Key : null;
            result.Add(MakeLine(row, label, 0, expandKey, input, colony));
            if (expandKey is null || !Expanded.Contains(expandKey))
                continue;
            foreach (var b in row.Breakdowns.OrderByDescending(b => b.Value.Number ?? double.MinValue).ThenBy(b => b.Breakdown, StringComparer.Ordinal))
                result.Add(MakeLine(b, BreakdownLabel(b.Breakdown), 1, null, input, colony));
        }
        return result;
    }

    private static Line MakeLine(NumericRow row, string label, int depth, string? expandKey, Inputs input, NumericColony? colony)
    {
        var d = row.Definition;
        var v = row.Value;
        var count = input.PerColony
            ? v.ThroughTick is { } t ? Dialog_RimworldStatistics.Days(t) : Dialog_RimworldStatistics.Dash
            : $"{row.Valid.ToString(Inv)} / {row.Targets.ToString(Inv)}";

        var tip = new StringBuilder();
        tip.Append(label).Append('\n').Append((string)d.DescriptionKey.Translate()).Append("\n\n");
        tip.Append((string)"RWS_Details_Tip_Source".Translate(ProvenanceLabel(d.Provenance), CoverageLabel(d, v.Coverage))).Append('\n');
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
            if (v.ColonyKey is { } holder)
                tip.Append((string)"RWS_Details_Tip_Holder".Translate(Name(input.Stats, holder))).Append('\n');
        }
        if (v.SampleCount is { } n)
            tip.Append((string)"RWS_Details_Tip_Samples".Translate(n)).Append('\n');
        if (v.Numerator is { } num && v.Denominator is { } den && d.Overall == OverallAggregation.Ratio)
            tip.Append((string)"RWS_Details_Tip_Ratio".Translate(Number(num), Number(den))).Append('\n');
        if (v.Coverage == MetricCoverage.Unknown)
            tip.Append((string)"RWS_Details_Tip_Unknown".Translate()).Append('\n');
        tip.Append((string)"RWS_Details_Tip_Ledger".Translate(d.LedgerIds, d.Key));

        var value = Format(d, v);
        if (d.Category == MetricCategory.Rankings && row.Breakdown.Length == 0)
        {
            // A ranking names what holds it: the colony, or the label(s) for a split such as the top scenario.
            if (v.Label is { } holderLabel)
                value += " · " + holderLabel;
            else if (v.ColonyKey is { } holderColony)
                value += " · " + Name(input.Stats, holderColony);
        }
        return new Line(label, value, CoverageLabel(d, v.Coverage), count, tip.ToString(), depth, expandKey,
            v.Coverage is MetricCoverage.Partial or MetricCoverage.Unknown);
    }

    // ----- formatting -----

    private static string Number(double value)
        => Math.Abs(value - Math.Round(value)) < 1e-9 ? value.ToString("N0", Inv) : value.ToString("N2", Inv);

    internal static string Format(MetricDefinition d, NumericValue v)
    {
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
            _ => n.ToString("N2", Inv),
        };
    }

    private static string BreakdownLabel(string key) => key switch
    {
        MetricKeys.UnknownBucket => "RWS_Details_UnknownBucket".Translate(),
        MetricRankings.PermadeathOn => "RWS_Details_Permadeath".Translate(),
        MetricRankings.PermadeathOff => "RWS_Details_NotPermadeath".Translate(),
        _ => key,
    };

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
        _ => "RWS_Category_Rankings".Translate(),
    };

    private static string ProvenanceLabel(MetricProvenance p) => p switch
    {
        MetricProvenance.VanillaCounter => "RWS_Provenance_VanillaCounter".Translate(),
        MetricProvenance.SaveHistory => "RWS_Provenance_SaveHistory".Translate(),
        MetricProvenance.SaveSnapshot => "RWS_Provenance_SaveSnapshot".Translate(),
        _ => "RWS_Provenance_LiveAggregate".Translate(),
    };

    private static string CoverageLabel(MetricDefinition d, MetricCoverage c) => c switch
    {
        MetricCoverage.Complete => "RWS_Coverage_Complete".Translate(),
        MetricCoverage.Partial => "RWS_Coverage_Partial".Translate(),
        MetricCoverage.SnapshotOnly when d.Provenance == MetricProvenance.SaveHistory => "RWS_Coverage_History".Translate(),
        MetricCoverage.SnapshotOnly => "RWS_Coverage_SnapshotOnly".Translate(),
        MetricCoverage.NotApplicable => "RWS_Coverage_NotApplicable".Translate(),
        _ => "RWS_Coverage_Unknown".Translate(),
    };
}
