using RimworldStatistics.Core.Tests.Fixtures;
using static RimworldStatistics.Core.CsvField;

namespace RimworldStatistics.Core.Tests;

/// <summary>E4/E8: the export folder name and the shared summary table used by CSV export and clipboard copy.</summary>
public sealed class ExportTableTests
{
    private static ExportText Text => new(new Dictionary<string, string>(), new Dictionary<string, string>(),
        new Dictionary<NumericRow, string>(), "en", "Test group", "test");

    [Fact]
    public void SummaryTableIsTheWrittenSummaryCsv()
    {
        using var root = new TempDir();
        var definition = MetricRegistry.Find(MetricKeys.QuestSuccess)!;
        var row = new NumericRow
        {
            Definition = definition,
            Value = new NumericValue { Number = 7, Coverage = MetricCoverage.Partial, ThroughTick = 60000 },
            Valid = 1,
            Targets = 1,
        };
        var view = View([new NumericColony { Key = "a", Rows = [row] }], [row]);

        var export = StatisticsExport.Write(view, Text, _ => UnitRecordingState.NotSelected, root.Path,
            TestContext.Current.CancellationToken, null, null, null, "my export");

        Assert.Equal("my_export", Path.GetFileName(export.Directory));
        Assert.NotNull(export.SummaryTable);
        var lines = File.ReadAllLines(Path.Combine(export.Directory, "summary.csv"));
        Assert.Equal(string.Join(',', export.SummaryTable!.Headers), lines[0]);
        Assert.Equal(export.SummaryTable.Rows.Length, lines.Length - 1);
        Assert.Equal("Test group", export.SummaryTable.Rows[0][0].Text);
    }

    [Fact]
    public void SanitizeDirectoryNameKeepsSafeNamesAndRejectsPaths()
    {
        Assert.Equal("My_Export", StatisticsExport.SanitizeDirectoryName(" My Export "));
        Assert.Equal("evil_name", StatisticsExport.SanitizeDirectoryName("../evil/name"));
        Assert.Equal("colony_CON", StatisticsExport.SanitizeDirectoryName("CON"));
        Assert.Equal("fallback-name", StatisticsExport.SanitizeDirectoryName("   ", "fallback-name"));
        Assert.NotEqual("colony", StatisticsExport.SanitizeDirectoryName(null));
    }

    [Fact]
    public void WriteAvoidsAnExistingExplicitDirectoryName()
    {
        using var root = new TempDir();
        var first = StatisticsExport.Write(View([new NumericColony { Key = "a", Rows = [] }]), Text,
            _ => UnitRecordingState.NotSelected, root.Path, TestContext.Current.CancellationToken, null, null, null, "same");
        var second = StatisticsExport.Write(View([new NumericColony { Key = "a", Rows = [] }]), Text,
            _ => UnitRecordingState.NotSelected, root.Path, TestContext.Current.CancellationToken, null, null, null, "same");

        Assert.Equal("same", Path.GetFileName(first.Directory));
        Assert.StartsWith("same-", Path.GetFileName(second.Directory), StringComparison.Ordinal);
        Assert.NotEqual(first.Directory, second.Directory);
    }

    [Fact]
    public void RenderMarkdownEscapesTableSeparatorsAndLineBreaks()
    {
        var table = new ExportTable(["a", "b"], [[Of("x|y"), Of("back\\slash")], [Of("line\nbreak"), Number(1.5)]]);

        Assert.Equal("|a|b|\n|---|---|\n|x\\|y|back\\\\slash|\n|line<br>break|1.5|",
            StatisticsExport.RenderMarkdown(table));
    }

    [Fact]
    public void RenderTsvKeepsPlainValuesAndFlattensTabsAndLineBreaks()
    {
        var table = new ExportTable(["a", "b"], [[Of("x\\y|z"), Of("tab\there")], [Of("line\r\nbreak"), Number(1.5)]]);

        Assert.Equal("a\tb\nx\\y|z\ttab here\nline break\t1.5", StatisticsExport.RenderTsv(table));
    }

    [Fact]
    public void SummaryTableEmitsEveryRegistryMetricWithItsKey()
    {
        var rows = MetricRegistry.All.Select(definition => new NumericRow
        {
            Definition = definition,
            Value = NumericValue.Unknown,
            Targets = 1,
        }).ToArray();
        var table = StatisticsExport.BuildSummaryTable(View([new NumericColony { Key = "a", Rows = rows }], rows),
            Text, _ => UnitRecordingState.NotSelected, TestContext.Current.CancellationToken);

        Assert.Equal(rows.Length, table.Rows.Length);
        Assert.All(table.Rows, row => Assert.NotNull(MetricRegistry.Find(row[1].Text!)));
        Assert.Equal(rows.Select(row => row.Definition.Key).Order(StringComparer.Ordinal),
            table.Rows.Select(row => row[1].Text!).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EmptyViewStillProducesTheSummaryHeader()
    {
        var table = StatisticsExport.BuildSummaryTable(View(), Text, _ => UnitRecordingState.NotSelected,
            TestContext.Current.CancellationToken);

        Assert.Empty(table.Rows);
        Assert.Equal(22, table.Headers.Length);
        Assert.Equal("metric_key", table.Headers[1]);
        Assert.Equal("through_day", table.Headers[^1]);
    }

    private static StatsView View(NumericColony[]? colonies = null, NumericRow[]? overall = null)
    {
        colonies ??= [];
        return new StatsView
        {
            Colonies = [],
            Stats = new StatsResult
            {
                ColonyCount = colonies.Count(c => !c.OutOfScope),
                Colonies = colonies.Select(c => new ColonyStats
                {
                    Key = c.Key, OutOfScope = c.OutOfScope, EndingCounts = new Dictionary<EndingType, int>(), GameVersions = [],
                }).ToArray(),
                EndingsByType = new Dictionary<EndingType, int>(),
            },
            Numeric = new NumericView { Colonies = colonies, Overall = overall ?? [] },
        };
    }
}
