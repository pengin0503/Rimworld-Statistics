using System.Text.Json;
using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

public sealed class StatisticsExportTests
{
    private static ExportText Labels => new(new Dictionary<string, string>(), new Dictionary<string, string>(),
        new Dictionary<NumericRow, string>(), "en", "Test group", "test");

    [Fact]
    public void EmptyProfileWritesFiveHeadersAndManifest()
    {
        using var root = new TempDir();
        var export = StatisticsExport.Write(View(), Labels, _ => UnitRecordingState.NotSelected, root.Path, TestContext.Current.CancellationToken);
        Assert.Equal(6, Directory.GetFiles(export.Directory).Length);
        Assert.All(export.Rows, count => Assert.Equal(0, count.Value));
        Assert.All(Directory.GetFiles(export.Directory, "*.csv"), path => Assert.Single(File.ReadAllLines(path)));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(export.Directory, "manifest.json")));
        Assert.Equal("Test group", manifest.RootElement.GetProperty("scope").GetString());
    }

    [Fact]
    public void ScopePreservesColonyListAndExportMatchesMissingAndRankingRules()
    {
        using var root = new TempDir();
        var definition = MetricRegistry.Find(MetricKeys.QuestSuccess)!;
        var measured = new NumericRow
        {
            Definition = definition, Value = new NumericValue { Number = 7, Coverage = MetricCoverage.Partial, ThroughTick = 60000 },
            Valid = 1, Targets = 1,
        };
        var missing = new NumericRow { Definition = definition, Value = NumericValue.Unknown, Targets = 1 };
        var view = View(
            [new NumericColony { Key = "a", Rows = [measured] }, new NumericColony { Key = "b", Rows = [missing] },
                new NumericColony { Key = "c", OutOfScope = true, Rows = [measured] }],
            [measured]);
        var export = StatisticsExport.Write(view, Labels with
        {
            ColonyNames = new Dictionary<string, string> { ["a"] = "=入植地" },
        }, _ => UnitRecordingState.NotSelected, root.Path, TestContext.Current.CancellationToken);
        string[] Read(string file) => File.ReadAllLines(Path.Combine(export.Directory, file));
        Assert.Equal($"Test group,{definition.Key},,{definition.Key},Quests,Count,Sum,7,,Partial,UnknownLegacy,AllMeasuredColonies,Standard,SaveSnapshot,,,1,1,,,,1",
            Read("summary.csv")[1]);
        Assert.Equal($"a,'=入植地,{definition.Key},,{definition.Key},Count,7,Partial,UnknownLegacy,,1",
            Read("colony_metrics.csv")[1]);
        Assert.Equal(3, Read("colony_metrics.csv").Length);
        Assert.Contains(",," + MetricCoverage.Unknown + ",", Read("colony_metrics.csv")[2]);
        Assert.Contains("a,'=入植地,", Read("colonies.csv")[1]);
        Assert.Equal("false", Read("colonies.csv")[3].Split(',')[8]);
        Assert.Contains(Read("rankings.csv"), row => row.StartsWith(definition.Key + "," + definition.Key + ",descending,1,a,") && row.Contains(",7,Partial,"));
    }

    [Fact]
    public void CancelledExportDoesNotPublishDirectory()
    {
        using var root = new TempDir();
        using var cancel = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => StatisticsExport.Write(View(), Labels, _ => UnitRecordingState.NotSelected,
            root.Path, cancel.Token, _ => cancel.Cancel()));
        Assert.Empty(Directory.GetFileSystemEntries(root.Path));
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
