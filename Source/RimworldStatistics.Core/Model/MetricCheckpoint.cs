using System.Text.Json;
using System.Text.Json.Serialization;

namespace RimworldStatistics.Core;

/// <summary>
/// The JSON a successful save embeds in the component's <c>numericStatistics</c> string (numeric expansion design §2 "Cの保存"):
/// the checkpoint's id, its parent (the checkpoint the game was loaded from or last saved), the save tick and time, and the
/// running totals of every acquisition family. Totals are cumulative along the save lineage: a checkpoint replaces its parent's
/// values, it never adds to them. No event sequence and no names are stored.
/// </summary>
public sealed class CheckpointPayload
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public string CheckpointId { get; set; } = "";
    public string? ParentCheckpointId { get; set; }
    public int SaveTick { get; set; }
    public DateTime SavedUtc { get; set; }

    [JsonConverter(typeof(FamilyStatesConverter))]
    public Dictionary<string, FamilyState> Families { get; set; } = new(StringComparer.Ordinal);

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// The totals of one acquisition family (a section of the metrics ledger, e.g. "research") and the measured range:
/// recorded from <see cref="SinceTick"/> to <see cref="ThroughTick"/>, without a gap when <see cref="Continuous"/>.
/// <see cref="Active"/> holds only what an unfinished interval needs to resume after a load (ids and start ticks).
/// </summary>
public sealed class FamilyState
{
    public int Revision { get; set; }
    public int SinceTick { get; set; }
    public int ThroughTick { get; set; }
    public bool Continuous { get; set; } = true;
    public Dictionary<string, Accumulator> Metrics { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, Dictionary<string, long>> Active { get; set; } = new(StringComparer.Ordinal);

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>The unreadable (or unknown) JSON this family was read from; written back unchanged and never interpreted.</summary>
    [JsonIgnore]
    public JsonElement? Quarantined { get; set; }

    [JsonIgnore]
    public bool IsQuarantined => Quarantined.HasValue;

    /// <summary>A deep copy: what a save writes must not change when the game records more events afterwards.</summary>
    public FamilyState Clone()
    {
        if (Quarantined is { } q)
            return new FamilyState { Quarantined = q };
        var c = new FamilyState
        {
            Revision = Revision, SinceTick = SinceTick, ThroughTick = ThroughTick, Continuous = Continuous,
            ExtensionData = ExtensionData is null ? null : new Dictionary<string, JsonElement>(ExtensionData),
        };
        foreach (var (k, v) in Metrics)
            c.Metrics[k] = v.Clone();
        foreach (var (k, v) in Active)
            c.Active[k] = new Dictionary<string, long>(v, StringComparer.Ordinal);
        return c;
    }
}

/// <summary>
/// One running total. Families use only the fields they need: a count <see cref="N"/>; a sum with its extremes (and the key
/// that holds an extreme, e.g. a pawn id); per-key amounts <see cref="By"/> (capped, with <see cref="Overflow"/> once a new key
/// was dropped); and the first and last event ticks (gaps between events).
/// </summary>
public sealed class Accumulator
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? N { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Sum { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Min { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Max { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MinKey { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MaxKey { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, double>? By { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Overflow { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? First { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Last { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public Accumulator Clone() => new()
    {
        N = N, Sum = Sum, Min = Min, Max = Max, MinKey = MinKey, MaxKey = MaxKey,
        By = By is null ? null : new Dictionary<string, double>(By, StringComparer.Ordinal),
        Overflow = Overflow, First = First, Last = Last,
        ExtensionData = ExtensionData is null ? null : new Dictionary<string, JsonElement>(ExtensionData),
    };

    internal bool IsWellFormed()
        => (Sum is null || double.IsFinite(Sum.Value)) && (Min is null || double.IsFinite(Min.Value)) && (Max is null || double.IsFinite(Max.Value))
            && (By is null || By.Values.All(double.IsFinite));
}

/// <summary>Reads each family on its own: one that cannot be read is set aside and written back as it was.</summary>
internal sealed class FamilyStatesConverter : JsonConverter<Dictionary<string, FamilyState>>
{
    public override Dictionary<string, FamilyState>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;
        using var doc = JsonDocument.ParseValue(ref reader);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("families is not an object");
        var result = new Dictionary<string, FamilyState>(StringComparer.Ordinal);
        foreach (var property in doc.RootElement.EnumerateObject())
            result[property.Name] = ReadFamily(property.Value, options);
        return result;
    }

    private static FamilyState ReadFamily(JsonElement element, JsonSerializerOptions options)
    {
        try
        {
            var f = element.Deserialize<FamilyState>(options);
            if (f is not null)
            {
                f.Metrics ??= new Dictionary<string, Accumulator>(StringComparer.Ordinal);
                f.Active ??= new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);
                if (f.Metrics.Values.All(a => a is not null && a.IsWellFormed()) && f.Active.Values.All(a => a is not null))
                {
                    if (!ReferenceEquals(f.Metrics.Comparer, StringComparer.Ordinal))
                        f.Metrics = new Dictionary<string, Accumulator>(f.Metrics, StringComparer.Ordinal);
                    foreach (var a in f.Metrics.Values)
                        if (a.By is { } by && !ReferenceEquals(by.Comparer, StringComparer.Ordinal))
                            a.By = new Dictionary<string, double>(by, StringComparer.Ordinal);
                    f.Active = f.Active.ToDictionary(kv => kv.Key, kv => new Dictionary<string, long>(kv.Value, StringComparer.Ordinal), StringComparer.Ordinal);
                    return f;
                }
            }
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException or FormatException)
        {
            // fall through: set aside
        }
        return new FamilyState { Quarantined = element.Clone() };
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, FamilyState> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (key, family) in value)
        {
            writer.WritePropertyName(key);
            if (family.Quarantined is { } raw)
                raw.WriteTo(writer);
            else
                JsonSerializer.Serialize(writer, family, options);
        }
        writer.WriteEndObject();
    }
}
