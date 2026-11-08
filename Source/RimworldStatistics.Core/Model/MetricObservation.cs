using System.Text.Json;
using System.Text.Json.Serialization;

namespace RimworldStatistics.Core;

/// <summary>Where a numeric observation came from (numeric expansion design §2).</summary>
public enum MetricObservationSource
{
    /// <summary>Phase A: streamed from an existing save file; keyed by the SHA-256 of the bytes read.</summary>
    SaveFile,

    /// <summary>Phase B: the running game's state summarized at a successful save; keyed by a GUID per save.</summary>
    SaveSnapshot,

    /// <summary>Phase C: a checkpoint embedded in a successful save; keyed by its checkpointId.</summary>
    Checkpoint,
}

/// <summary>A save file that held the observed bytes.</summary>
public sealed class MetricFileAlias
{
    public string FileName { get; set; } = "";
    public long FileSize { get; set; }
    public DateTime LastWriteUtc { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public bool Same(string fileName, long size, DateTime lastWriteUtc)
        => FileName == fileName && FileSize == size && LastWriteUtc == lastWriteUtc;
}

/// <summary>
/// The compact numeric summary of one observation (record ⑤, kept apart from v1's observations and their pruning).
/// It is attached to a colony only when the v1 grouping already knows that colony.
/// </summary>
public sealed class MetricObservation
{
    public MetricObservationSource Source { get; set; }
    public int FormatVersion { get; set; }
    public string Key { get; set; } = "";
    public ColonyIdentity Identity { get; set; } = new();
    public List<MetricFileAlias> FileAliases { get; set; } = new();
    public string? GameVersion { get; set; }
    public int SaveTick { get; set; }

    /// <summary>For a save file: the earliest modification time among the copies read so far.</summary>
    public DateTime ObservedUtc { get; set; }

    /// <summary>Official DLC package ids in the save's mod list; null when the list was not in the save.</summary>
    public List<string>? ActiveDlcIds { get; set; }

    /// <summary>Per acquisition unit, captured at recording time. Null means pre-provenance legacy data.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, MetricEvidence>? EvidenceByAcquisition { get; set; }

    [JsonConverter(typeof(MetricEntriesConverter))]
    public Dictionary<string, MetricEntry> Metrics { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// SaveSnapshot retention: a historical maximum/minimum may stand for multiple
    /// numeric snapshot samples after redundant observations were compacted.
    /// A missing key implies one original sample. Only extremum selections use it;
    /// the latest snapshot is always kept separately. No checkpoint or SaveFile uses it.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, int>? CompactedSampleCounts { get; set; }

    /// <summary>Original SaveSnapshot observations represented by this retained record (defaults to one).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CompactedObservationCount { get; set; }

    /// <summary>A checkpoint's parent checkpoint id (Phase C); null for a lineage root and for other sources.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentKey { get; set; }

    /// <summary>A checkpoint's content hash: the same id with another hash is a conflict, never merged.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PayloadHash { get; set; }

    /// <summary>The same checkpoint id was seen with different contents: neither is used, and the lineage is unknown.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Conflicted { get; set; }

    /// <summary>
    /// An ancestor checkpoint whose values were dropped because a later checkpoint of the colony exists (it can never be the
    /// representative again); only its place in the lineage is kept.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Compressed { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>The unreadable JSON this observation was read from; written back unchanged and never used.</summary>
    [JsonIgnore]
    public JsonElement? Quarantined { get; set; }

    [JsonIgnore]
    public bool IsQuarantined => Quarantined.HasValue;

    /// <summary>The scalar value of a metric, unless the entry is missing, unreadable or of another revision.</summary>
    public MetricValue? Scalar(string key, int revision)
        => Metrics.TryGetValue(key, out var e) && !e.IsQuarantined && e.Revision == revision
            ? e.Values.GetValueOrDefault(MetricEntry.Scalar)
            : null;
}

/// <summary>Reads each metric entry on its own: one that cannot be read is set aside and written back as it was.</summary>
internal sealed class MetricEntriesConverter : JsonConverter<Dictionary<string, MetricEntry>>
{
    public override Dictionary<string, MetricEntry>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;
        using var doc = JsonDocument.ParseValue(ref reader);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("metrics is not an object");
        var result = new Dictionary<string, MetricEntry>(StringComparer.Ordinal);
        foreach (var property in doc.RootElement.EnumerateObject())
            result[property.Name] = ReadEntry(property.Value, options);
        return result;
    }

    private static MetricEntry ReadEntry(JsonElement element, JsonSerializerOptions options)
    {
        try
        {
            var entry = element.Deserialize<MetricEntry>(options);
            if (entry is not null)
            {
                entry.Values ??= new Dictionary<string, MetricValue>(StringComparer.Ordinal);
                if (entry.Values.Values.All(v => v is not null && v.IsWellFormed()))
                {
                    if (!ReferenceEquals(entry.Values.Comparer, StringComparer.Ordinal))
                        entry.Values = new Dictionary<string, MetricValue>(entry.Values, StringComparer.Ordinal);
                    return entry;
                }
            }
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException or FormatException)
        {
            // fall through: set aside
        }
        return new MetricEntry { Quarantined = element.Clone() };
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, MetricEntry> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (key, entry) in value)
        {
            writer.WritePropertyName(key);
            if (entry.Quarantined is { } raw)
                raw.WriteTo(writer);
            else
                JsonSerializer.Serialize(writer, entry, options);
        }
        writer.WriteEndObject();
    }
}

/// <summary>Reads each observation on its own: one that cannot be read is set aside and written back as it was.</summary>
internal sealed class MetricObservationsConverter : JsonConverter<List<MetricObservation>>
{
    public override List<MetricObservation>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;
        using var doc = JsonDocument.ParseValue(ref reader);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new JsonException("metricObservations is not an array");
        var result = new List<MetricObservation>();
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            MetricObservation? obs = null;
            try
            {
                obs = element.Deserialize<MetricObservation>(options);
            }
            catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException or FormatException)
            {
                // set aside below
            }
            result.Add(obs is not null && Enum.IsDefined(obs.Source) ? obs : new MetricObservation { Quarantined = element.Clone() });
        }
        return result;
    }

    /// <summary>
    /// Each observation is written on one line: the record file stays readable, at about half the size of indented
    /// observations (it is rewritten, compared and backed up on every change).
    /// </summary>
    public override void Write(Utf8JsonWriter writer, List<MetricObservation> value, JsonSerializerOptions options)
    {
        var compact = Compact(options);
        writer.WriteStartArray();
        foreach (var obs in value)
        {
            if (obs.Quarantined is { } raw)
                raw.WriteTo(writer);
            else
                writer.WriteRawValue(JsonSerializer.SerializeToUtf8Bytes(obs, compact), skipInputValidation: true);
        }
        writer.WriteEndArray();
    }

    private sealed record CompactOptions(JsonSerializerOptions Source, JsonSerializerOptions Compact);

    // One reference, replaced atomically, so concurrent writers never pair one options instance with another's copy.
    private static volatile CompactOptions? cached;

    private static JsonSerializerOptions Compact(JsonSerializerOptions options)
    {
        if (cached is { } c && ReferenceEquals(c.Source, options))
            return c.Compact;
        var compact = new JsonSerializerOptions(options) { WriteIndented = false };
        compact.MakeReadOnly(populateMissingResolver: true);
        cached = new CompactOptions(options, compact);
        return compact;
    }
}
