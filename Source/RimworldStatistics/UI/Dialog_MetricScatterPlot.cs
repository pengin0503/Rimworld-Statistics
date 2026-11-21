using System.Globalization;
using System.Text;
using RimworldStatistics.Core;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>
/// Two statistics across the colonies of the current view, one point per colony (V2-D). A colony without both values is
/// listed by its reason and never drawn at 0; no trend line or correlation is drawn. Supports per-axis daily rate (/day)
/// toggles for cumulative additive totals and attribute-based categorical dot coloring.
/// </summary>
internal sealed class Dialog_MetricScatterPlot : Window
{
    private const float MarkerSize = 8f;
    private const float ClusterSpacing = 7f;
    private const float HitSlop = 3f;
    private const int TipColonies = 8;
    private const int TipNamesPerReason = 12;
    private const int MenuColonies = 30;
    private const float LegendRowHeight = 20f;
    private const float LegendItemGap = 14f;
    private const float LegendRowGap = 2f;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly Color PointColor = new(0.35f, 0.68f, 0.86f);
    private static readonly Color CautionColor = new(0.92f, 0.64f, 0.34f);
    private static readonly Color HollowFill = new(0.12f, 0.13f, 0.15f);
    private static readonly Color PlotBackground = new(0f, 0f, 0f, 0.18f);
    private static readonly Color GridColor = new(1f, 1f, 1f, 0.08f);
    private static readonly Color ZeroColor = new(1f, 1f, 1f, 0.32f);
    private static readonly Color NoteColor = new(0.84f, 0.86f, 0.90f);
    private static readonly Color WarnColor = new(0.96f, 0.80f, 0.45f);
    private static readonly Color MixedClusterColor = new(0.86f, 0.86f, 0.92f);
    private static readonly Color OtherCategoryColor = new(0.76f, 0.72f, 0.62f);
    private static readonly Color UnknownCategoryColor = new(0.58f, 0.60f, 0.65f);

    private static readonly Color[] CategoryPalette =
    [
        new(0.35f, 0.68f, 0.88f),
        new(0.30f, 0.78f, 0.56f),
        new(0.94f, 0.76f, 0.32f),
        new(0.90f, 0.46f, 0.38f),
        new(0.72f, 0.54f, 0.88f),
        new(0.34f, 0.82f, 0.80f),
    ];

    /// <summary>Every text the window shows for one plot in one language, built once.</summary>
    private sealed record Texts(
        MetricPickerChoice[] Choices,
        string XLabel, string YLabel, string? XSource, string? YSource,
        string XButton, string YButton, string Kinds,
        string Summary, string SummaryTip, string? NotPlotted, string? NotPlottedTip,
        IReadOnlyList<(string Text, Color Color)> Notices,
        string[] XTicks, string[] YTicks);

    private readonly StatsView view;
    private readonly string? scopeName;
    private string xKey;
    private string yKey;
    private bool xPerDay;
    private bool yPerDay;
    private ScatterColorMode colorMode = ScatterColorMode.DataStatus;
    private MetricScatterPlot? plot;
    private ScatterColorAssignment? colorAssignment;
    private object? textLanguage;
    private Texts? texts;
    private IReadOnlyList<ScatterCluster>? clusters;
    private Vector2 clusterArea;
    private bool[] clusterCaution = [];
    private int[] clusterBucketIndices = [];
    private bool hasMixedCluster;
    private bool hasMultiCluster;
    private string?[] clusterTips = [];
    private Dictionary<string, string>? colonyNames;
    private Dictionary<string, ColonyStats>? colonyStatsByKey;
    private string? namesSignature;
    private List<(string Label, Action<Rect> Swatch, string? Tip)>? legendItems;
    private HistoryLegendPlan? legendPlan;
    private float legendWidth = -1f;

    /// <param name="yKey">The statistic on the Y axis (the peak wealth when missing or not plottable).</param>
    /// <param name="xKey">The statistic on the X axis (days played when missing, not plottable or the same as Y).</param>
    internal Dialog_MetricScatterPlot(
        StatsView view,
        string? yKey,
        string? xKey = null,
        string? scopeName = null,
        bool xPerDay = false,
        bool yPerDay = false)
    {
        this.view = view;
        this.scopeName = scopeName;
        var y = yKey is not null && MetricScatterPlot.CanPlot(view, yKey) ? yKey : BasicMetrics.PeakWealth;
        var x = xKey is not null && MetricScatterPlot.CanPlot(view, xKey) ? xKey : BasicMetrics.Days;
        if (x == y && xPerDay == yPerDay)
            x = y == BasicMetrics.Days ? BasicMetrics.PeakWealth : BasicMetrics.Days;
        this.xKey = x;
        this.yKey = y;
        this.xPerDay = xPerDay && MetricScatterPlot.CanPlotPerDay(view, x);
        this.yPerDay = yPerDay && MetricScatterPlot.CanPlotPerDay(view, y);
        doCloseX = true;
        absorbInputAroundWindow = true;
        closeOnClickedOutside = false;
        Rebuild();
    }

    public override Vector2 InitialSize => new(
        Math.Min(1080f, Verse.UI.screenWidth * 0.92f),
        Math.Min(780f, Verse.UI.screenHeight * 0.90f));

    public override void DoWindowContents(Rect rect)
    {
        try
        {
            DrawContents(rect);
        }
        catch (Exception e)
        {
            Log.ErrorOnce($"[RimworldStatistics] Scatter plot window failed to draw.\n{e}", "RWS.scatter".GetHashCode());
            Widgets.Label(rect.ContractedBy(20f), "RWS_WindowError".Translate());
        }
        finally
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = true;
            GUI.color = Color.white;
        }
    }

    private void Rebuild()
    {
        plot = MetricScatterPlot.Build(view, xKey, yKey, xPerDay, yPerDay);
        colorAssignment = plot is null ? null : ScatterColorGrouping.Assign(plot, view.Stats, colorMode);
        texts = null;
        clusters = null;
        legendItems = null;
        legendPlan = null;
    }

    private void SetColorMode(ScatterColorMode nextMode)
    {
        if (colorMode == nextMode)
            return;
        colorMode = nextMode;
        colorAssignment = plot is null ? null : ScatterColorGrouping.Assign(plot, view.Stats, colorMode);
        clusters = null;
        legendItems = null;
        legendPlan = null;
    }

    private void DrawContents(Rect rect)
    {
        var lang = LanguageDatabase.activeLanguage;
        var prefSig = ColonyPresentation.Signature(Dialog_RimworldStatistics.ActivePreferences);
        if (texts is null || !ReferenceEquals(textLanguage, lang) || namesSignature != prefSig)
        {
            textLanguage = lang;
            namesSignature = prefSig;
            colonyNames = null; // an unnamed colony's name is translated
            texts = BuildTexts();
            clusterTips = new string?[clusterTips.Length];
            legendItems = null;
            legendPlan = null;
        }
        var t = texts;

        using (UiScopes.GuiState())
        {
            Text.Font = GameFont.Medium;
            var title = (string)"RWS_Scatter_Title".Translate();
            Widgets.Label(new Rect(rect.x, rect.y, Math.Max(0f, rect.width - 36f), 32f), title.Truncate(Math.Max(0f, rect.width - 36f)));
        }

        var controls = new ControlRow(new Rect(rect.x, rect.y + 36f, rect.width, 28f));
        if (Button(ref controls, t.XButton, t.XSource is null ? t.XButton : t.XButton + "\n" + t.XSource, 155f, 320f))
            UiScopes.OpenOrReplaceWindow(new MetricPickerDialog(t.Choices, key => SetAxes(key, yKey, xPerDay, yPerDay), t.XButton, xKey));

        var canXRate = MetricScatterPlot.CanPlotPerDay(view, xKey);
        var xRateLabel = (string)(xPerDay ? "RWS_Scatter_XRateOn" : "RWS_Scatter_XRateOff").Translate();
        var xRateTip = (string)(canXRate ? "RWS_Rate_AxisToggleTip" : "RWS_Rate_AxisUnavailableTip").Translate();
        if (Button(ref controls, xRateLabel, xRateTip, 84f, 150f, active: canXRate))
            SetAxes(xKey, yKey, !xPerDay, yPerDay);

        if (Button(ref controls, t.YButton, t.YSource is null ? t.YButton : t.YButton + "\n" + t.YSource, 155f, 320f))
            UiScopes.OpenOrReplaceWindow(new MetricPickerDialog(t.Choices, key => SetAxes(xKey, key, xPerDay, yPerDay), t.YButton, yKey));

        var canYRate = MetricScatterPlot.CanPlotPerDay(view, yKey);
        var yRateLabel = (string)(yPerDay ? "RWS_Scatter_YRateOn" : "RWS_Scatter_YRateOff").Translate();
        var yRateTip = (string)(canYRate ? "RWS_Rate_AxisToggleTip" : "RWS_Rate_AxisUnavailableTip").Translate();
        if (Button(ref controls, yRateLabel, yRateTip, 84f, 150f, active: canYRate))
            SetAxes(xKey, yKey, xPerDay, !yPerDay);

        var swap = (string)"RWS_Scatter_Swap".Translate();
        if (Button(ref controls, swap, swap, 100f, 180f))
            SetAxes(yKey, xKey, yPerDay, xPerDay);

        var colorBtn = (string)"RWS_Scatter_ColorButton".Translate(ColorModeLabel(colorMode));
        if (Button(ref controls, colorBtn, "RWS_Scatter_ColorButtonTip".Translate(), 145f, 260f))
            OpenColorModeMenu();

        var y = rect.y + 36f + controls.Height + 4f;
        y = Line(rect, y, t.Kinds, t.Kinds, NoteColor);
        y = Line(rect, y, t.Summary, t.SummaryTip, Color.white);
        if (t.NotPlotted is { } notPlotted)
            y = Line(rect, y, notPlotted, t.NotPlottedTip ?? notPlotted, NoteColor);
        foreach (var (notice, color) in t.Notices)
            y = Line(rect, y, notice, notice, color);

        EnsureClustersIfNeeded(rect, y);
        EnsureLegendLayout(rect.width);
        var legendHeight = Math.Max(22f, legendPlan?.TotalHeight ?? 22f);
        var area = new Rect(rect.x, y + 4f, rect.width, Math.Max(0f, rect.yMax - y - 4f - legendHeight - 2f));
        if (plot is not null)
            DrawPlot(area, plot, t);
        DrawLegend(new Rect(rect.x, rect.yMax - legendHeight, rect.width, legendHeight));
    }

    private void OpenColorModeMenu()
    {
        var options = Enum.GetValues<ScatterColorMode>()
            .Select(mode => new FloatMenuOption(ColorModeLabel(mode), () => SetColorMode(mode)))
            .ToList();
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private static string ColorModeLabel(ScatterColorMode mode) => mode switch
    {
        ScatterColorMode.EndingReached => "RWS_Scatter_Color_Ending".Translate(),
        ScatterColorMode.Permadeath => "RWS_Scatter_Color_Permadeath".Translate(),
        ScatterColorMode.Difficulty => "RWS_Scatter_Color_Difficulty".Translate(),
        ScatterColorMode.Storyteller => "RWS_Scatter_Color_Storyteller".Translate(),
        _ => "RWS_Scatter_Color_Status".Translate(),
    };

    private static bool Button(ref ControlRow controls, string label, string tip, float min, float max, bool active = true)
    {
        var r = controls.Next(label, min, max);
        var shown = label.Truncate(Math.Max(0f, r.width - 12f));
        if (shown != label || tip != label)
            TooltipHandler.TipRegion(r, tip);
        bool clicked;
        using (UiScopes.GuiState())
        {
            if (!active)
                GUI.color = new Color(0.65f, 0.65f, 0.65f);
            clicked = Widgets.ButtonText(r, shown, active: active);
        }
        return clicked && active;
    }

    private static float Line(Rect rect, float y, string text, string tip, Color color)
    {
        var r = new Rect(rect.x, y, rect.width, 22f);
        string shown;
        using (UiScopes.GuiState())
        {
            GUI.color = color;
            shown = text.Truncate(r.width);
            Widgets.Label(r, shown);
        }
        if (shown != text || tip != text)
            TooltipHandler.TipRegion(r, tip);
        return y + 22f;
    }

    private void SetAxes(string x, string y, bool nextXPerDay, bool nextYPerDay)
    {
        nextXPerDay = nextXPerDay && MetricScatterPlot.CanPlotPerDay(view, x);
        nextYPerDay = nextYPerDay && MetricScatterPlot.CanPlotPerDay(view, y);
        if (x == xKey && y == yKey && nextXPerDay == xPerDay && nextYPerDay == yPerDay)
            return;
        xKey = x;
        yKey = y;
        xPerDay = nextXPerDay;
        yPerDay = nextYPerDay;
        Rebuild();
    }

    // ----- texts -----

    private Texts BuildTexts()
    {
        var choices = LeaderboardBuilder.RankableKeys(view)
            .Where(k => MetricScatterPlot.CanPlot(view, k))
            .Select(k => new MetricPickerChoice(k, RawLabel(k), Source(k)))
            .OrderBy(c => c.Label, StringComparer.CurrentCulture)
            .ThenBy(c => c.Key, StringComparer.Ordinal)
            .ToArray();
        var xRaw = RawLabel(xKey);
        var yRaw = RawLabel(yKey);
        var xLabel = xPerDay ? (string)"RWS_Rate_TitleSuffix".Translate(xRaw) : xRaw;
        var yLabel = yPerDay ? (string)"RWS_Rate_TitleSuffix".Translate(yRaw) : yRaw;
        var xButton = (string)"RWS_Scatter_X".Translate(xLabel);
        var yButton = (string)"RWS_Scatter_Y".Translate(yLabel);
        if (plot is not { } p)
        {
            var unavailable = (string)"RWS_Scatter_Unavailable".Translate();
            return new Texts(choices, xLabel, yLabel, Source(xKey), Source(yKey), xButton, yButton, "", unavailable, unavailable,
                null, null, [], [], []);
        }

        var kinds = (string)"RWS_Scatter_Kinds".Translate(AxisKindLabel(p.X), AxisKindLabel(p.Y));
        var inScope = p.Points.Count + p.Exclusions.Count(e => e.Reason is not (ScatterExclusionReason.Excluded or ScatterExclusionReason.OutOfScope));
        var summary = (string)"RWS_Scatter_Summary".Translate(p.Points.Count.ToString(Inv), inScope.ToString(Inv),
            scopeName ?? (string)"RWS_Groups_All".Translate());

        var parts = new List<string>();
        var tip = new StringBuilder();
        tip.Append((string)"RWS_Scatter_NotPlottedTip".Translate());
        void Part(ScatterExclusionReason reason, ScatterAxes? axes, string key)
        {
            var matched = p.Exclusions.Where(e => e.Reason == reason && (axes is null || e.Axes == axes)).ToArray();
            if (matched.Length == 0)
                return;
            var line = (string)key.Translate(matched.Length.ToString(Inv));
            parts.Add(line);
            tip.AppendLine().AppendLine().Append(line).Append(": ");
            tip.Append(string.Join(", ", matched.Take(TipNamesPerReason).Select(e => axes is null && e.Axes != ScatterAxes.None
                ? Name(e.ColonyKey) + " " + AxesTag(e.Axes) : Name(e.ColonyKey))));
            if (matched.Length > TipNamesPerReason)
                tip.Append(' ').Append((string)"RWS_Scatter_More".Translate((matched.Length - TipNamesPerReason).ToString(Inv)));
        }
        Part(ScatterExclusionReason.Missing, ScatterAxes.X, "RWS_Scatter_Reason_MissingX");
        Part(ScatterExclusionReason.Missing, ScatterAxes.Y, "RWS_Scatter_Reason_MissingY");
        Part(ScatterExclusionReason.Missing, ScatterAxes.Both, "RWS_Scatter_Reason_MissingBoth");
        Part(ScatterExclusionReason.IncompleteRateInterval, null, "RWS_Scatter_Reason_IncompleteRate");
        Part(ScatterExclusionReason.NotApplicable, null, "RWS_Scatter_Reason_NotApplicable");
        Part(ScatterExclusionReason.NotReached, null, "RWS_Scatter_Reason_NotReached");
        Part(ScatterExclusionReason.NoSingleValue, null, "RWS_Scatter_Reason_NoSingleValue");
        Part(ScatterExclusionReason.Excluded, null, "RWS_Scatter_Reason_Excluded");
        Part(ScatterExclusionReason.OutOfScope, null, "RWS_Scatter_Reason_OutOfScope");
        var notPlotted = parts.Count == 0 ? null : (string)"RWS_Scatter_NotPlotted".Translate(string.Join(" · ", parts));

        var notices = new List<(string, Color)>();
        if (p.Points.Count == 0)
            notices.Add(("RWS_Scatter_NoPoints".Translate(), WarnColor));
        else if (p.TooFewPoints)
            notices.Add(("RWS_Scatter_TooFew".Translate(p.Points.Count.ToString(Inv)), WarnColor));
        if (p.CautionCount > 0)
            notices.Add(("RWS_Scatter_Caution".Translate(p.CautionCount.ToString(Inv)), NoteColor));

        return new Texts(choices, xLabel, yLabel, Source(xKey), Source(yKey), xButton, yButton, kinds,
            summary, "RWS_Scatter_SummaryTip".Translate(), notPlotted, notPlotted is null ? null : tip.ToString(),
            notices, TickLabels(p.X), TickLabels(p.Y));
    }

    private string RawLabel(string key)
        => BasicMetrics.Contains(key) ? MetricText.PerColonyBasicLabel(key)
            : MetricLookup.Definition(view, key) is { } d ? MetricText.Label(d) : key;

    private string? Source(string key)
        => BasicMetrics.Contains(key) ? null : MetricLookup.Definition(view, key) is { } d ? MetricText.ModSourceLabel(d) : null;

    private string Name(string colonyKey)
    {
        if (colonyNames is null)
        {
            colonyNames = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var stats in view.Stats.Colonies)
                colonyNames.TryAdd(stats.Key, Dialog_RimworldStatistics.ColonyName(stats));
        }
        return colonyNames.TryGetValue(colonyKey, out var name) ? name : colonyKey;
    }

    private ColonyStats? StatsOf(string colonyKey)
    {
        if (colonyStatsByKey is null)
        {
            colonyStatsByKey = new Dictionary<string, ColonyStats>(StringComparer.Ordinal);
            foreach (var stats in view.Stats.Colonies)
                colonyStatsByKey.TryAdd(stats.Key, stats);
        }
        return colonyStatsByKey.GetValueOrDefault(colonyKey);
    }

    private static string AxesTag(ScatterAxes axes) => axes switch
    {
        ScatterAxes.X => "RWS_Scatter_OnX".Translate(),
        ScatterAxes.Y => "RWS_Scatter_OnY".Translate(),
        _ => "RWS_Scatter_OnBoth".Translate(),
    };

    private static string AxisKindLabel(ScatterAxis axis)
        => axis.PerDay ? "RWS_Rate_AxisKindPerDay".Translate() : KindLabel(axis.ValueKind);

    private static string KindLabel(ColonyReportValueKind kind) => kind switch
    {
        ColonyReportValueKind.CumulativeTotal => "RWS_Report_Kind_Cumulative".Translate(),
        ColonyReportValueKind.PeakOrExtreme => "RWS_Report_Kind_Peak".Translate(),
        ColonyReportValueKind.LatestSnapshot => "RWS_Report_Kind_Snapshot".Translate(),
        _ => "RWS_Report_Kind_MeanRate".Translate(),
    };

    private static string[] TickLabels(ScatterAxis axis)
    {
        if (axis.PerDay)
            return axis.Ticks.Select(v => MetricChartTicks.FormatAxisValue(v, percent: false)).ToArray();
        var percent = axis.Definition.Unit == MetricUnit.Percent;
        var suffix = axis.Definition.Unit == MetricUnit.Mood ? "%" : "";
        return axis.Ticks.Select(v => MetricChartTicks.FormatAxisValue(v, percent) + suffix).ToArray();
    }

    /// <summary>The value and its recording state, as the rankings' value and state columns show them.</summary>
    private static string FormatValue(ScatterAxis axis, ScatterValue value)
    {
        if (axis.PerDay)
        {
            var rateText = MetricText.FormatRateValue(axis.Definition, value.Number);
            return value.RecordedDays is { } days
                ? rateText + " · " + (string)"RWS_Rate_RecordedInterval".Translate(days.ToString("N1", Inv))
                : rateText;
        }
        if (axis.IsBasic)
            return RankingsTab.BasicValue(axis.MetricKey, value.Number) + " · " + (string)"RWS_Coverage_SnapshotOnly".Translate();
        var text = MetricText.Format(axis.Definition, new NumericValue
        {
            Number = value.Number, Coverage = value.Coverage, Evidence = value.Evidence, ThroughTick = value.ThroughTick,
        });
        return text + " · " + MetricText.State(axis.Definition, value.Coverage);
    }

    // ----- coloring & legend -----

    private Color BucketColor(ScatterColorBucket bucket)
    {
        if (colorMode == ScatterColorMode.DataStatus)
            return PointColor;
        if (colorMode == ScatterColorMode.EndingReached)
            return bucket.Kind == ScatterColorBucketKind.EndingReached
                ? CategoryPalette[1]
                : CategoryPalette[0];
        if (colorMode == ScatterColorMode.Permadeath)
            return bucket.Kind switch
            {
                ScatterColorBucketKind.Permadeath => CategoryPalette[3],
                ScatterColorBucketKind.ReloadAnytime => CategoryPalette[0],
                _ => UnknownCategoryColor,
            };
        return bucket.Kind switch
        {
            ScatterColorBucketKind.Other => OtherCategoryColor,
            ScatterColorBucketKind.Unknown => UnknownCategoryColor,
            _ => CategoryPalette[bucket.Index % CategoryPalette.Length],
        };
    }

    private string BucketLabel(ScatterColorBucket bucket)
    {
        var baseName = bucket.Kind switch
        {
            ScatterColorBucketKind.EndingReached => (string)"RWS_Scatter_Bucket_EndingYes".Translate(),
            ScatterColorBucketKind.EndingNotReached => (string)"RWS_Scatter_Bucket_EndingNo".Translate(),
            ScatterColorBucketKind.Permadeath => (string)"RWS_Scatter_Bucket_Permadeath".Translate(),
            ScatterColorBucketKind.ReloadAnytime => (string)"RWS_Scatter_Bucket_ReloadAnytime".Translate(),
            ScatterColorBucketKind.Other => (string)"RWS_Scatter_Bucket_Other".Translate(),
            ScatterColorBucketKind.Unknown => (string)"RWS_Scatter_Bucket_Unknown".Translate(),
            ScatterColorBucketKind.CategoryValue when colorMode == ScatterColorMode.Difficulty
                => SmartScopeUi.ResolveDifficultyLabel(bucket.RawValue ?? ""),
            ScatterColorBucketKind.CategoryValue when colorMode == ScatterColorMode.Storyteller
                => SmartScopeUi.ResolveStorytellerLabel(bucket.RawValue ?? ""),
            _ => bucket.RawValue ?? (string)"RWS_Scatter_LegendPoint".Translate(),
        };
        return $"{baseName} ({bucket.PointCount.ToString(Inv)})";
    }

    private string? PointColorTipLine(string colonyKey)
    {
        if (colorMode == ScatterColorMode.DataStatus)
            return null;
        var stats = StatsOf(colonyKey);
        return colorMode switch
        {
            ScatterColorMode.EndingReached => (string)"RWS_Scatter_TipAttr".Translate(
                "RWS_Scatter_Color_Ending".Translate(),
                (stats is not null && stats.EndingCounts.Values.Sum() > 0
                    ? "RWS_Scatter_Bucket_EndingYes"
                    : "RWS_Scatter_Bucket_EndingNo").Translate()),
            ScatterColorMode.Permadeath => (string)"RWS_Scatter_TipAttr".Translate(
                "RWS_Scatter_Color_Permadeath".Translate(),
                (stats is { HasObservedPoint: true, Permadeath: { } perm }
                    ? (perm ? "RWS_Scatter_Bucket_Permadeath" : "RWS_Scatter_Bucket_ReloadAnytime")
                    : "RWS_Scatter_Bucket_Unknown").Translate()),
            ScatterColorMode.Difficulty => (string)"RWS_Scatter_TipAttr".Translate(
                "RWS_Scatter_Color_Difficulty".Translate(),
                !string.IsNullOrWhiteSpace(stats?.Difficulty)
                    ? SmartScopeUi.ResolveDifficultyLabel(stats!.Difficulty!)
                    : (string)"RWS_Scatter_Bucket_Unknown".Translate()),
            ScatterColorMode.Storyteller => (string)"RWS_Scatter_TipAttr".Translate(
                "RWS_Scatter_Color_Storyteller".Translate(),
                !string.IsNullOrWhiteSpace(stats?.Storyteller)
                    ? SmartScopeUi.ResolveStorytellerLabel(stats!.Storyteller!)
                    : (string)"RWS_Scatter_Bucket_Unknown".Translate()),
            _ => null,
        };
    }

    private void EnsureClustersIfNeeded(Rect rect, float topY)
    {
        if (plot is null || clusters is not null)
            return;
        //clusters are rebuilt accurately inside DrawPlot using exact plotRect.size;
        // initialize flags conservatively before first DrawPlot pass if needed.
        _ = rect;
        _ = topY;
    }

    private void RebuildClusters(MetricScatterPlot p, Vector2 size)
    {
        clusters = p.Cluster(size.x, size.y, ClusterSpacing);
        clusterArea = size;
        clusterCaution = clusters.Select(c => c.PointIndices.Any(i => p.Points[i].NeedsCaution)).ToArray();
        clusterBucketIndices = new int[clusters.Count];
        hasMixedCluster = false;
        hasMultiCluster = false;
        var assignment = colorAssignment;
        for (var i = 0; i < clusters.Count; i++)
        {
            if (clusters[i].PointIndices.Count > 1)
                hasMultiCluster = true;
            var bucketIdx = assignment is null ? 0 : ScatterColorGrouping.ResolveClusterBucket(clusters[i], assignment);
            clusterBucketIndices[i] = bucketIdx;
            if (bucketIdx < 0)
                hasMixedCluster = true;
        }
        clusterTips = new string?[clusters.Count];
        legendItems = null;
        legendPlan = null;
    }

    private void EnsureLegendLayout(float availableWidth)
    {
        if (legendItems is null)
        {
            legendItems = [];
            if (colorMode == ScatterColorMode.DataStatus || colorAssignment is null || colorAssignment.Buckets.Count == 0)
            {
                legendItems.Add(("RWS_Scatter_LegendPoint".Translate(), r => Widgets.DrawBoxSolid(r, PointColor), null));
                legendItems.Add(("RWS_Scatter_LegendCaution".Translate(), r => Widgets.DrawBoxSolidWithOutline(r, HollowFill, CautionColor, 1), null));
                legendItems.Add(("RWS_Scatter_LegendStack".Translate(), r => Widgets.DrawBoxSolid(r, PointColor), null));
            }
            else
            {
                foreach (var bucket in colorAssignment.Buckets)
                {
                    var swatchColor = BucketColor(bucket);
                    legendItems.Add((BucketLabel(bucket), r => Widgets.DrawBoxSolid(r, swatchColor), null));
                }
                if (hasMixedCluster)
                    legendItems.Add(("RWS_Scatter_Bucket_Mixed".Translate(), r => Widgets.DrawBoxSolidWithOutline(r, MixedClusterColor, Color.white, 1), null));
                if (plot is { CautionCount: > 0 })
                    legendItems.Add(("RWS_Scatter_LegendCaution".Translate(), r => Widgets.DrawBoxSolidWithOutline(r, HollowFill, CautionColor, 1), null));
                if (hasMultiCluster)
                    legendItems.Add(("RWS_Scatter_LegendStack".Translate(), r => Widgets.DrawBoxSolid(r, PointColor), null));
            }
            legendPlan = null;
        }

        if (legendPlan is null || Math.Abs(legendWidth - availableWidth) > 0.5f)
        {
            var widths = new float[legendItems.Count];
            for (var i = 0; i < legendItems.Count; i++)
                widths[i] = 16f + Text.CalcSize(legendItems[i].Label).x;
            legendPlan = HistoryLegendLayout.Resolve(widths, availableWidth, LegendRowHeight, LegendItemGap, LegendRowGap);
            legendWidth = availableWidth;
        }
    }

    // ----- plot -----

    private void DrawPlot(Rect area, MetricScatterPlot p, Texts t)
    {
        float yLabelWidth = 36f;
        using (UiScopes.GuiState())
        {
            Text.Font = GameFont.Tiny;
            foreach (var label in t.YTicks)
                yLabelWidth = Math.Max(yLabelWidth, Text.CalcSize(label).x + 8f);
            yLabelWidth = Math.Min(yLabelWidth, 120f);
        }

        var plotRect = new Rect(area.x + yLabelWidth, area.y + 24f,
            Math.Max(0f, area.width - yLabelWidth - 16f), Math.Max(0f, area.height - 24f - 44f));
        if (plotRect.width < 60f || plotRect.height < 60f)
            return;

        // Axis names: Y above the plot, X below its tick labels.
        var yName = (string)"RWS_Scatter_Y".Translate(t.YLabel);
        var yNameRect = new Rect(area.x, area.y, area.width, 22f);
        Widgets.Label(yNameRect, yName.Truncate(yNameRect.width));
        using (UiScopes.GuiState())
        {
            Text.Anchor = TextAnchor.UpperCenter;
            var xName = (string)"RWS_Scatter_X".Translate(t.XLabel);
            Widgets.Label(new Rect(plotRect.x, plotRect.yMax + 20f, plotRect.width, 22f), xName.Truncate(plotRect.width));

            Widgets.DrawBoxSolid(plotRect, PlotBackground);
            Text.Font = GameFont.Tiny;
            for (var i = 0; i < p.Y.Ticks.Count; i++)
            {
                var value = p.Y.Ticks[i];
                var py = plotRect.yMax - (float)p.Y.Position(value) * plotRect.height;
                Widgets.DrawBoxSolid(new Rect(plotRect.x, py, plotRect.width, 1f), value == 0d ? ZeroColor : GridColor);
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(area.x, py - 9f, yLabelWidth - 5f, 18f), t.YTicks[i].Truncate(yLabelWidth - 5f));
            }
            var xLabelWidth = Math.Max(30f, plotRect.width / Math.Max(1, p.X.Ticks.Count) - 4f);
            for (var i = 0; i < p.X.Ticks.Count; i++)
            {
                var value = p.X.Ticks[i];
                var px = plotRect.x + (float)p.X.Position(value) * plotRect.width;
                Widgets.DrawBoxSolid(new Rect(px, plotRect.y, 1f, plotRect.height), value == 0d ? ZeroColor : GridColor);
                Text.Anchor = TextAnchor.UpperCenter;
                Widgets.Label(new Rect(px - xLabelWidth / 2f, plotRect.yMax + 2f, xLabelWidth, 18f), t.XTicks[i].Truncate(xLabelWidth));
            }
        }

        if (clusters is null || clusterArea != plotRect.size)
        {
            RebuildClusters(p, plotRect.size);
            EnsureLegendLayout(area.width);
        }

        var mouse = Event.current.mousePosition;
        var reach = MarkerSize / 2f + HitSlop;
        var hovered = -1;
        var best = float.MaxValue;
        var mouseNear = Mouse.IsOver(plotRect.ExpandedBy(reach));
        using (UiScopes.GuiState())
        {
            Text.Font = GameFont.Tiny;
            for (var i = 0; i < clusters!.Count; i++)
            {
                var c = clusters[i];
                var center = new Vector2(plotRect.x + (float)c.X, plotRect.yMax - (float)c.Y);
                var marker = new Rect(center.x - MarkerSize / 2f, center.y - MarkerSize / 2f, MarkerSize, MarkerSize);
                if (colorMode == ScatterColorMode.DataStatus)
                {
                    if (clusterCaution[i])
                        Widgets.DrawBoxSolidWithOutline(marker, HollowFill, CautionColor, 1);
                    else
                        Widgets.DrawBoxSolid(marker, PointColor);
                }
                else
                {
                    var bucketIdx = clusterBucketIndices[i];
                    var dotColor = bucketIdx >= 0 && colorAssignment is not null && bucketIdx < colorAssignment.Buckets.Count
                        ? BucketColor(colorAssignment.Buckets[bucketIdx])
                        : MixedClusterColor;
                    if (clusterCaution[i])
                        Widgets.DrawBoxSolidWithOutline(marker, HollowFill, dotColor, 1);
                    else if (bucketIdx < 0)
                        Widgets.DrawBoxSolidWithOutline(marker, dotColor, Color.white, 1);
                    else
                        Widgets.DrawBoxSolid(marker, dotColor);
                }
                if (c.PointIndices.Count > 1)
                    Widgets.Label(new Rect(marker.xMax + 1f, marker.y - 11f, 48f, 16f), c.PointIndices.Count.ToString(Inv));

                if (!mouseNear)
                    continue;
                var dx = Math.Abs(mouse.x - center.x);
                var dy = Math.Abs(mouse.y - center.y);
                if (dx <= reach && dy <= reach && dx + dy < best)
                {
                    best = dx + dy;
                    hovered = i;
                }
            }
        }

        if (hovered < 0)
            return;
        var h = clusters[hovered];
        var hit = new Rect(plotRect.x + (float)h.X - reach, plotRect.yMax - (float)h.Y - reach, reach * 2f, reach * 2f);
        Widgets.DrawBox(hit.ContractedBy(HitSlop - 2f), 1);
        TooltipHandler.TipRegion(hit, clusterTips[hovered] ??= ClusterTip(p, t, h));
        if (Widgets.ButtonInvisible(hit))
            OpenMenu(p, t, h);
    }

    private string ClusterTip(MetricScatterPlot p, Texts t, ScatterCluster cluster)
    {
        var sb = new StringBuilder();
        foreach (var index in cluster.PointIndices.Take(TipColonies))
        {
            var point = p.Points[index];
            if (sb.Length > 0)
                sb.AppendLine().AppendLine();
            sb.Append(Name(point.ColonyKey));
            if (PointColorTipLine(point.ColonyKey) is { } attrLine)
                sb.AppendLine().Append(attrLine);
            sb.AppendLine().Append((string)"RWS_Scatter_X".Translate(FormatValue(p.X, point.X)));
            sb.AppendLine().Append((string)"RWS_Scatter_Y".Translate(FormatValue(p.Y, point.Y)));
            if (point.ObservedAtDifferentTicks)
                sb.AppendLine().Append((string)"RWS_Scatter_TipDifferentDays".Translate(
                    Dialog_RimworldStatistics.Days(point.X.ThroughTick!.Value), Dialog_RimworldStatistics.Days(point.Y.ThroughTick!.Value)));
        }
        if (cluster.PointIndices.Count > TipColonies)
            sb.AppendLine().AppendLine().Append((string)"RWS_Scatter_More".Translate((cluster.PointIndices.Count - TipColonies).ToString(Inv)));
        sb.AppendLine().AppendLine().Append((string)"RWS_Scatter_TipClick".Translate());
        return sb.ToString();
    }

    private void OpenMenu(MetricScatterPlot p, Texts t, ScatterCluster cluster)
    {
        if (cluster.PointIndices.Count == 1)
        {
            Find.WindowStack.Add(new FloatMenu(ColonyOptions(p, t, p.Points[cluster.PointIndices[0]])));
            return;
        }
        var points = cluster.PointIndices.Select(i => p.Points[i])
            .OrderBy(pt => Name(pt.ColonyKey), StringComparer.CurrentCulture)
            .ThenBy(pt => pt.ColonyKey, StringComparer.Ordinal)
            .ToArray();
        var options = points.Take(MenuColonies)
            .Select(pt => new FloatMenuOption(Name(pt.ColonyKey), () => Find.WindowStack.Add(new FloatMenu(ColonyOptions(p, t, pt)))))
            .ToList();
        if (points.Length > MenuColonies)
            options.Add(new FloatMenuOption("RWS_Scatter_More".Translate((points.Length - MenuColonies).ToString(Inv)), null));
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private List<FloatMenuOption> ColonyOptions(MetricScatterPlot p, Texts t, ScatterPoint point)
    {
        var key = point.ColonyKey;
        var options = new List<FloatMenuOption>
        {
            new("RWS_Report_Open".Translate(), () => UiScopes.OpenOrReplaceWindow(new Dialog_ColonySummaryReport(view, key, scopeName))),
        };
        void History(ScatterAxis axis, string rawLabel, ScatterValue value)
        {
            if (axis.IsBasic && !BasicMetrics.HasSaveHistory(axis.MetricKey))
                return;
            var fallback = new NumericValue
            {
                Number = value.Number, Coverage = value.Coverage, Evidence = value.Evidence, ThroughTick = value.ThroughTick,
            };
            var comparison = axis.PerDay
                ? MetricLookup.ForColony(view, key, axis.MetricKey)?.Value ?? fallback
                : fallback;
            options.Add(new FloatMenuOption("RWS_Scatter_History".Translate(rawLabel), () => UiScopes.OpenOrReplaceWindow(
                new Dialog_ColonyMetricHistory(view, key, Name(key), axis.Definition, rawLabel, comparison, scopeName))));
        }
        History(p.X, RawLabel(p.X.MetricKey), point.X);
        if (p.Y.MetricKey != p.X.MetricKey)
            History(p.Y, RawLabel(p.Y.MetricKey), point.Y);
        return options;
    }

    private void DrawLegend(Rect rect)
    {
        if (legendItems is null || legendPlan is null)
            return;

        foreach (var placement in legendPlan.Items)
        {
            var (label, swatch, tip) = legendItems[placement.Index];
            var x = rect.x + placement.X;
            var y = rect.y + placement.Y;
            var labelWidth = Math.Max(0f, placement.Width - 14f);
            swatch(new Rect(x, y + 5f, 9f, 9f));
            var itemRect = new Rect(x, y, placement.Width, LegendRowHeight);
            var shown = label.Truncate(labelWidth);
            Widgets.Label(new Rect(x + 14f, y, labelWidth, LegendRowHeight), shown);
            if (tip is not null)
                TooltipHandler.TipRegion(itemRect, tip);
            else if (shown != label)
                TooltipHandler.TipRegion(itemRect, label);
        }
    }
}
