using System.Globalization;
using System.Text;
using System.Text.Json;
using static RimworldStatistics.Core.CsvField;

namespace RimworldStatistics.Core;

public sealed record ExportText(
    IReadOnlyDictionary<string, string> MetricLabels, IReadOnlyDictionary<string, string> ColonyNames,
    IReadOnlyDictionary<NumericRow, string> DisplayValues, string Language, string ScopeName, string ModVersion);

/// <summary>Header and data rows of one export table, in the exact form written to CSV.</summary>
public sealed record ExportTable(string[] Headers, CsvField[][] Rows);

public sealed record ExportSummary(string Directory, IReadOnlyDictionary<string, int> Rows)
{
    /// <summary>The rows written to summary.csv, also used by the panel's Markdown/TSV copy buttons (E8).</summary>
    public ExportTable? SummaryTable { get; init; }
}

public static class StatisticsExport
{
    private const string ColumnsDocumentation = "https://github.com/pengin0503/Rimworld-Statistics/blob/main/docs/csv-export.md";
    public const int TotalFileCount = 7;

    /// <summary>The name used when the player has not typed one: a UTC stamp plus a unique suffix.</summary>
    public static string DefaultDirectoryName(DateTime utcNow)
        => utcNow.ToString("yyyyMMdd'T'HHmmssfffffff'Z'", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N");

    /// <summary>
    /// Turns a player-supplied export name into a cross-platform directory leaf. Path separators and other invalid file
    /// characters are replaced, the length is bounded, and an empty result falls back to the supplied default.
    /// </summary>
    public static string SanitizeDirectoryName(string? rawName, string? fallback = null)
        => string.IsNullOrWhiteSpace(rawName)
            ? ColonyReportHtmlExport.SanitizeFileName(fallback ?? DefaultDirectoryName(DateTime.UtcNow))
            : ColonyReportHtmlExport.SanitizeFileName(rawName);

    /// <summary>
    /// Builds the summary table once for both the CSV writer and the panel's clipboard buttons, so copied rows are the
    /// same values the export produces. An empty view still returns the header row.
    /// </summary>
    public static ExportTable BuildSummaryTable(StatsView view, ExportText text, Func<string, UnitRecordingState> unitState,
        CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(unitState);
        var statuses = MetricStatusClassifier.Classify(view, unitState).ToDictionary(s => s.Definition.Key, StringComparer.Ordinal);
        string Label(string key) => text.MetricLabels.GetValueOrDefault(key) ?? key;
        string ColonyName(string key) => text.ColonyNames.GetValueOrDefault(key) ?? key;
        static CsvField Day(int? tick) => Number(tick / (double)StatFormat.TicksPerDay);
        static double? Measured(NumericValue value) => value.Known ? value.Number : null;

        var rows = new List<CsvField[]>();
        if (view.Stats.Colonies.Count > 0)
        {
            foreach (var row in Flatten(view.Numeric.Overall))
            {
                cancel.ThrowIfCancellationRequested();
                var d = row.Definition;
                var v = row.Value;
                var status = statuses[d.Key];
                var unit = row.Breakdown.Length != 0 ? d.BreakdownUnit ?? d.Unit : d.Unit;
                rows.Add([Of(text.ScopeName), Of(d.Key), Of(row.Breakdown), Of(Label(d.Key)), Of(d.Category.ToString()),
                    Of(unit.ToString()), Of(d.Overall.ToString()), Number(Measured(v)),
                    Of(v.Known ? text.DisplayValues.GetValueOrDefault(row) : null),
                    Of(v.Coverage.ToString()), Of(v.Evidence.ToString()), Of(MetricAvailabilityPolicy.Classify(row).ToString()),
                    Of(status.Verification.ToString()), Of(d.Provenance.ToString()), Of(d.Acquisition), Of(d.OwnerPackageId),
                    Integer(row.Valid), Integer(row.Targets), Of(v.ColonyKey), Of(v.ColonyKey is { } holder ? ColonyName(holder) : null),
                    Day(v.SinceTick), Day(v.ThroughTick)]);
            }
        }
        return new ExportTable(
            ["scope", "metric_key", "breakdown", "label", "category", "unit", "aggregation", "value", "value_display",
                "coverage", "evidence", "availability", "verification", "provenance", "acquisition_unit", "owner_mod",
                "colonies_measured", "colonies_target", "holder_colony_key", "holder_colony_name", "since_day", "through_day"],
            rows.ToArray());
    }

    /// <summary>A Markdown table of a previously built export table; cells keep the export's plain values.</summary>
    public static string RenderMarkdown(ExportTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var sb = new StringBuilder();
        sb.Append('|').Append(string.Join('|', table.Headers.Select(MarkdownCell))).AppendLine("|");
        sb.Append('|').Append(string.Join('|', table.Headers.Select(_ => "---"))).AppendLine("|");
        foreach (var row in table.Rows)
            sb.Append('|').Append(string.Join('|', row.Select(field => MarkdownCell(field.Text)))).AppendLine("|");
        return sb.ToString().TrimEnd('\r', '\n');
    }

    /// <summary>Tab-separated rows with LF line endings, matching the mod's other instant-copy tables.</summary>
    public static string RenderTsv(ExportTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var rows = new List<string>(table.Rows.Length + 1)
        {
            string.Join('\t', table.Headers.Select(TsvCell)),
        };
        foreach (var row in table.Rows)
            rows.Add(string.Join('\t', row.Select(field => TsvCell(field.Text))));
        return string.Join('\n', rows);
    }

    // Markdown needs its column separator and escape character escaped; line breaks would end the row.
    private static string MarkdownCell(string? value)
        => (value ?? "").Replace("\\", "\\\\").Replace("|", "\\|")
            .Replace("\r\n", "<br>").Replace("\r", "<br>").Replace("\n", "<br>");

    // TSV has no quoting convention, so a tab or line break inside a value is flattened to a space.
    private static string TsvCell(string? value)
        => (value ?? "").Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');

    public static ExportSummary Write(StatsView view, ExportText text, Func<string, UnitRecordingState> unitState,
        string directory, CancellationToken cancel = default, Action<int>? fileCompleted = null,
        IReadOnlyList<ColonyMetricHistorySeries>? historySeries = null, Profile? profile = null)
        => Write(view, text, unitState, directory, cancel, fileCompleted, historySeries, profile, directoryName: null);

    public static ExportSummary Write(StatsView view, ExportText text, Func<string, UnitRecordingState> unitState,
        string directory, CancellationToken cancel, Action<int>? fileCompleted,
        IReadOnlyList<ColonyMetricHistorySeries>? historySeries, Profile? profile, string? directoryName)
    {
        cancel.ThrowIfCancellationRequested();
        Directory.CreateDirectory(directory);
        var utc = DateTime.UtcNow;
        var leaf = string.IsNullOrWhiteSpace(directoryName)
            ? DefaultDirectoryName(utc)
            : SanitizeDirectoryName(directoryName, DefaultDirectoryName(utc));
        if (Directory.Exists(Path.Combine(directory, leaf)))
            leaf += "-" + Guid.NewGuid().ToString("N")[..8];
        var destination = Path.Combine(directory, leaf);
        var staging = destination + ".tmp";
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var summary = BuildSummaryTable(view, text, unitState, cancel);
        string Label(string key) => text.MetricLabels.GetValueOrDefault(key) ?? key;
        string ColonyName(string key) => text.ColonyNames.GetValueOrDefault(key) ?? key;
        static CsvField Day(int? tick) => Number(tick / (double)StatFormat.TicksPerDay);
        static CsvField Flag(bool? flag) => Of(flag switch { true => "true", false => "false", _ => null });
        static double? Measured(NumericValue value) => value.Known ? value.Number : null;

        Directory.CreateDirectory(staging);
        try
        {
            WriteCsv("summary.csv", summary.Headers, add =>
            {
                foreach (var row in summary.Rows)
                    add(row);
            });

            WriteCsv("colony_metrics.csv",
                ["colony_key", "colony_name", "metric_key", "breakdown", "label", "unit", "value", "coverage", "evidence", "since_day", "through_day"], add =>
                {
                    foreach (var colony in view.Numeric.Colonies.Where(c => !c.OutOfScope && !c.Excluded))
                        foreach (var row in Flatten(colony.Rows))
                        {
                            var d = row.Definition;
                            var v = row.Value;
                            var unit = row.Breakdown.Length != 0 ? d.BreakdownUnit ?? d.Unit : d.Unit;
                            add([Of(colony.Key), Of(ColonyName(colony.Key)), Of(d.Key), Of(row.Breakdown), Of(Label(d.Key)),
                                Of(unit.ToString()), Number(Measured(v)), Of(v.Coverage.ToString()), Of(v.Evidence.ToString()),
                                Day(v.SinceTick), Day(v.ThroughTick)]);
                        }
                });

            WriteCsv("colonies.csv",
                ["colony_key", "name", "world", "scenario", "storyteller", "difficulty", "permadeath", "excluded", "in_scope",
                    "days", "real_play_hours", "peak_wealth", "last_wealth", "raids", "threats", "killed", "departed",
                    "max_population", "endings", "saves", "last_played_utc", "game_versions", "faction_name"], add =>
                {
                    foreach (var colony in view.Stats.Colonies)
                        add([Of(colony.Key), Of(ColonyName(colony.Key)), Of(colony.WorldName), Of(colony.Scenario),
                            Of(colony.Storyteller), Of(colony.Difficulty), Flag(colony.Permadeath), Flag(colony.Excluded), Flag(!colony.OutOfScope),
                            .. BasicMetrics.Keys.Select(key => Number(BasicMetrics.Value(colony, key))),
                            Of(Utc(colony.LastPlayedUtc)), Of(string.Join(";", colony.GameVersions)), Of(colony.FactionName)]);
                });

            WriteCsv("rankings.csv", ["metric_key", "label", "direction", "rank", "colony_key", "colony_name", "value", "coverage", "evidence"], add =>
            {
                foreach (var key in LeaderboardBuilder.RankableKeys(view))
                    foreach (var descending in new[] { true, false })
                    {
                        cancel.ThrowIfCancellationRequested();
                        var board = LeaderboardBuilder.Build(view, key, descending);
                        foreach (var entry in board.Entries)
                            add([Of(key), Of(Label(key)), Of(descending ? "descending" : "ascending"), Integer(entry.Rank),
                                Of(entry.ColonyKey), Of(ColonyName(entry.ColonyKey)), Number(entry.Value),
                                Of(entry.Coverage.ToString()), Of(entry.Evidence.ToString())]);
                    }
            });

            WriteCsv("endings.csv", ["time_utc", "colony_key", "colony_name", "ending_type", "ordinal", "departed", "source"], add =>
            {
                foreach (var ending in EndingRows(view).OrderByDescending(e => e.Time))
                    add([Of(Utc(ending.Time)), Of(ending.ColonyKey), Of(ColonyName(ending.ColonyKey)), Of(ending.Type.ToString()),
                        Integer(ending.Ordinal), Integer(ending.Departed), Of(ending.Source)]);
            });

            WriteCsv("colony_metric_history.csv",
                ["colony_key", "colony_name", "metric_key", "label", "unit", "history_kind", "point_index", "day", "tick",
                    "value", "coverage", "evidence", "predecessor_index", "unlinked"], add =>
                {
                    var resolvedHistories = historySeries ?? (view.Colonies.Count > 0
                        ? MetricCalculator.RestoreExportHistories(MetricCalculator.BuildColonyChartHistoriesCore(profile ?? new Profile(), view), null)
                        : []);
                    if (resolvedHistories.Count == 0)
                        return;

                    var inScopeColonies = view.Stats.Colonies
                        .Where(c => !c.OutOfScope && !c.Excluded)
                        .Select(c => c.Key)
                        .ToHashSet(StringComparer.Ordinal);
                    foreach (var series in resolvedHistories)
                    {
                        if (!inScopeColonies.Contains(series.ColonyKey))
                            continue;
                        var def = series.Definition;
                        var points = series.History.Points;
                        if (!points.Any(p => p.Value is { Known: true }))
                            continue;
                        for (var i = 0; i < points.Count; i++)
                        {
                            var pt = points[i];
                            var v = pt.Value;
                            var pred = pt.Predecessor(i);
                            add([
                                Of(series.ColonyKey),
                                Of(ColonyName(series.ColonyKey)),
                                Of(def.Key),
                                Of(Label(def.Key)),
                                Of(def.Unit.ToString()),
                                Of(series.History.Kind.ToString()),
                                Integer(i),
                                Day(pt.Tick),
                                Integer(pt.Tick),
                                Number(v is not null ? Measured(v) : null),
                                Of(v is not null ? v.Coverage.ToString() : nameof(MetricCoverage.Unknown)),
                                Of(v is not null ? v.Evidence.ToString() : null),
                                Integer(pred >= 0 ? pred : null),
                                Flag(pt.Unlinked),
                            ]);
                        }
                    }
                });

            cancel.ThrowIfCancellationRequested();
            using (var output = File.Create(Path.Combine(staging, "manifest.json")))
            {
                JsonSerializer.Serialize(output, new
                {
                    createdUtc = utc, text.ModVersion, scope = text.ScopeName, text.Language,
                    rows = counts, columns = ColumnsDocumentation,
                }, ProfileJson.Options);
                output.Flush(flushToDisk: true);
            }
            fileCompleted?.Invoke(TotalFileCount);
            cancel.ThrowIfCancellationRequested();
            Directory.Move(staging, destination);
            return new ExportSummary(destination, counts) { SummaryTable = summary };
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                try { Directory.Delete(staging, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        void WriteCsv(string name, string[] headers, Action<Action<CsvField[]>> write)
        {
            cancel.ThrowIfCancellationRequested();
            var count = 0;
            using (var stream = File.Create(Path.Combine(staging, name)))
            {
                using (var writer = new CsvWriter(stream))
                {
                    writer.Row(headers.Select(Of).ToArray());
                    write(fields =>
                    {
                        cancel.ThrowIfCancellationRequested();
                        writer.Row(fields);
                        count++;
                    });
                }
                stream.Flush(flushToDisk: true);
            }
            counts.Add(name, count);
            fileCompleted?.Invoke(counts.Count);
        }
    }

    private static IEnumerable<NumericRow> Flatten(IEnumerable<NumericRow> rows)
    {
        foreach (var row in rows)
        {
            yield return row;
            foreach (var child in Flatten(row.Breakdowns))
                yield return child;
        }
    }

    private static string? Utc(DateTime? value) => value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private sealed record EndingRow(DateTime? Time, string ColonyKey, EndingType Type, long? Ordinal, long? Departed, string Source);

    private static IEnumerable<EndingRow> EndingRows(StatsView view)
    {
        var byKey = view.Colonies.ToDictionary(c => c.Key, StringComparer.Ordinal);
        foreach (var stats in view.Stats.Colonies.Where(c => !c.OutOfScope && !c.Excluded))
        {
            if (!byKey.TryGetValue(stats.Key, out var colony))
                continue;
            foreach (var group in stats.CountedEndings.GroupBy(e => (e.Type, e.Ordinal)))
            {
                var best = EndingTraces.Representative(group);
                yield return new EndingRow(group.Min(e => e.RealTimeUtc), stats.Key, best.Type, best.Ordinal + 1L,
                    best.Source == EndingSource.SeededTrace ? null : best.Departed, best.Source.ToString());
            }
            foreach (var ending in colony.RecoveredEndings)
                yield return new EndingRow(null, stats.Key, ending.Type, ending.Ordinal + 1L, null, "RecoveredLineage");
            foreach (var type in EndingTraces.Unexplained(colony))
                yield return new EndingRow(EndingTraces.FirstTraceUtc(colony, type), stats.Key, type, null, null, "SaveTrace");
        }
    }
}
