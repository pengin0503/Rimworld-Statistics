using System.Text.Json;
using System.Text.Json.Serialization;

namespace RimworldStatistics.Core;

/// <summary>
/// The colony history chart readings of save-time snapshots that compaction removed (#318). A snapshot row holds every
/// snapshot statistic of one save, and compaction keeps only the rows that select a latest value or a historical
/// maximum/minimum; the chart needs one reading per save. This keeps what a chart point needs (tick, the save's place in
/// the checkpoint lineage, and per statistic the number and coverage) at a bounded size: beyond <see cref="MaxRows"/> rows,
/// the row whose removal changes the shape of every series least is thinned out, and <see cref="Thinned"/> counts them.
/// It is carried by the campaign's latest snapshot row.
/// </summary>
public sealed class SnapshotChartSeries
{
    /// <summary>The most removed saves kept as chart readings for one campaign and starting pawn set.</summary>
    public const int MaxRows = 64;

    /// <summary>One entry per removed save, in tick order.</summary>
    public List<SnapshotChartRow> Rows { get; set; } = new();

    /// <summary>Per stored metric key: its reading in each row. Keys without any reading are not written.</summary>
    public Dictionary<string, SnapshotChartKey> Metrics { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Removed saves whose readings were thinned out to keep the bound.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Thinned { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Where a removed snapshot sat: what <see cref="CheckpointAncestry.OfSnapshot"/> reads from a snapshot row.</summary>
public sealed class SnapshotChartRow
{
    public int Tick { get; set; }
    public DateTime ObservedUtc { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CheckpointKey { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>One statistic's chart readings, aligned with <see cref="SnapshotChartSeries.Rows"/>.</summary>
public sealed class SnapshotChartKey
{
    /// <summary>The definition revision the readings were taken with; another revision is never shown with them.</summary>
    public int Revision { get; set; }

    /// <summary>The reading of each row; null where that save had no measured value.</summary>
    public List<double?> Values { get; set; } = new();

    /// <summary>The coverage of each row, written only when some reading is not <see cref="MetricCoverage.SnapshotOnly"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<MetricCoverage>? Coverage { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>The readings line up with <paramref name="rows"/> (a damaged or hand-edited series is ignored).</summary>
    public bool Fits(int rows) => Values is not null && Values.Count == rows && (Coverage is null || Coverage.Count == rows);
}
