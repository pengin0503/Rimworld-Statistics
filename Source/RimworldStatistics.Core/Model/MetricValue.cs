using System.Text.Json;
using System.Text.Json.Serialization;

namespace RimworldStatistics.Core;

/// <summary>How much of a metric's defined range a value covers (numeric expansion design §2).</summary>
public enum MetricCoverage
{
    /// <summary>The whole defined range was recorded; a vanilla counter within its own range.</summary>
    Complete,

    /// <summary>Started late, has gaps, unsupported routes or unclassified inputs; never shown as a complete value.</summary>
    Partial,

    /// <summary>The state at save time, retained quests, or a finite-interval history observation.</summary>
    SnapshotOnly,

    /// <summary>The value, its denominator or a needed time is not known.</summary>
    Unknown,

    /// <summary>Not defined for this colony (e.g. the DLC was not active).</summary>
    NotApplicable,
}

/// <summary>
/// One measured value: an integer or a finite real, the numerator and denominator of a rate or mean, and the measured
/// range when needed. A null value with a known coverage means "not reached within the range" (e.g. a threshold).
/// Only the fields in use are written.
/// </summary>
public sealed class MetricValue
{
    public MetricCoverage Coverage { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? IntegerValue { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? RealValue { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Numerator { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Denominator { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SinceTick { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ThroughTick { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SampleCount { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    [JsonIgnore]
    public bool HasValue => IntegerValue.HasValue || RealValue.HasValue;

    /// <summary>The value as a double (integer or real), or null.</summary>
    [JsonIgnore]
    public double? Number => IntegerValue.HasValue ? IntegerValue.Value : RealValue;

    internal bool IsWellFormed()
        => Enum.IsDefined(Coverage)
            && Finite(RealValue) && Finite(Numerator) && Finite(Denominator);

    private static bool Finite(double? v) => v is null || double.IsFinite(v.Value);
}

/// <summary>
/// All values of one metric key in one observation: the definition revision and the values by breakdown key
/// ("" is the scalar). An entry that could not be read is kept as-is and not interpreted.
/// </summary>
public sealed class MetricEntry
{
    public const string Scalar = "";

    public int Revision { get; set; }
    public Dictionary<string, MetricValue> Values { get; set; } = new(StringComparer.Ordinal);

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>The unreadable JSON this entry was read from; written back unchanged.</summary>
    [JsonIgnore]
    public JsonElement? Quarantined { get; set; }

    [JsonIgnore]
    public bool IsQuarantined => Quarantined.HasValue;

    public static MetricEntry Of(int revision, MetricValue scalar) => new() { Revision = revision, Values = { [Scalar] = scalar } };
}
