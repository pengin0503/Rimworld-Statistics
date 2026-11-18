using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace RimworldStatistics.Core;

/// <summary>JSON codec for <see cref="Profile"/>: camelCase names and enum strings, UTC ISO 8601 dates.</summary>
public static class ProfileJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions(writeIndented: true);
    public static JsonSerializerOptions CompactOptions { get; } = CreateOptions(writeIndented: false);

    public static string Serialize(Profile profile) => JsonSerializer.Serialize(profile, Options);

    /// <summary>
    /// The same document as <see cref="Serialize"/>, already UTF-8 encoded (no byte order mark): the serializer writes
    /// UTF-8 natively, so this skips building a UTF-16 string of the whole profile and encoding it again to write it.
    /// </summary>
    public static byte[] SerializeToUtf8Bytes(Profile profile) => JsonSerializer.SerializeToUtf8Bytes(profile, Options);

    /// <summary>
    /// Writes the bytes of <see cref="SerializeToUtf8Bytes"/> to <paramref name="output"/>, so a caller that writes the
    /// profile again and again can reuse one buffer instead of a new array of the whole profile each time (Issue #195).
    /// </summary>
    internal static void SerializeTo(IBufferWriter<byte> output, Profile profile)
    {
        using var writer = new Utf8JsonWriter(output, WriterOptions);
        JsonSerializer.Serialize(writer, profile, Options);
    }

    /// <summary>The writer options <see cref="JsonSerializer.SerializeToUtf8Bytes{T}(T, JsonSerializerOptions)"/> uses for <paramref name="options"/>.</summary>
    internal static JsonWriterOptions WriterOptionsFor(JsonSerializerOptions options) => new()
    {
        // As JsonSerializerOptions.GetWriterOptions (internal) builds them.
        Encoder = options.Encoder,
        Indented = options.WriteIndented,
        IndentCharacter = options.IndentCharacter,
        IndentSize = options.IndentSize,
        MaxDepth = options.MaxDepth == 0 ? 64 : options.MaxDepth,
        NewLine = options.NewLine,
        SkipValidation = true,
    };

    private static readonly JsonWriterOptions WriterOptions = WriterOptionsFor(Options);

    /// <summary>Parses a profile. Null lists/objects become empty ones. Throws <see cref="JsonException"/> on invalid JSON.</summary>
    public static Profile Deserialize(string json)
    {
        var profile = JsonSerializer.Deserialize<Profile>(json, Options)
            ?? throw new JsonException("profile root is null");
        Normalize(profile);
        return profile;
    }

    private static JsonSerializerOptions CreateOptions(bool writeIndented)
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = writeIndented,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        o.Converters.Add(new UtcDateTimeConverter());
        o.MakeReadOnly(populateMissingResolver: true);
        return o;
    }

    private static void Normalize(Profile p)
    {
        p.SaveObservations = Clean(p.SaveObservations);
        foreach (var o in p.SaveObservations)
        {
            o.FileName ??= "";
            o.Identity = Normalize(o.Identity);
            o.Values ??= new ObservedValues();
            o.EndingTraces ??= new List<EndingType>();
            o.ExplainedTraces ??= new List<EndingType>();
            o.Lineage ??= new List<LineageEntry>();
            o.Display ??= new DisplayInfo();
            if (o.PermanentTales is not null)
            {
                o.PermanentTales = Clean(o.PermanentTales);
                if (o.PermanentTales.Count == 0)
                    o.PermanentTales = null;
            }
            if (o.PermanentTalesDroppedThroughTick < 0)
                o.PermanentTalesDroppedThroughTick = null;
        }

        p.LiveCampaigns = Clean(p.LiveCampaigns);
        foreach (var c in p.LiveCampaigns)
        {
            c.CampaignId ??= "";
            c.Identity = Normalize(c.Identity);
            c.Max ??= new ObservedValues();
            c.Latest ??= new ObservedValues();
            c.Display ??= new DisplayInfo();
            c.GameVersions = Clean(c.GameVersions);
        }

        p.EndingEvents = Clean(p.EndingEvents);
        foreach (var e in p.EndingEvents)
            e.CampaignId ??= "";

        p.UserDecisions ??= new UserDecisions();
        p.UserDecisions.ExcludedCampaignKeys = Clean(p.UserDecisions.ExcludedCampaignKeys);
        p.UserDecisions.ManualMerges = Clean(p.UserDecisions.ManualMerges).Select(Clean).ToList();
        p.UserDecisions.ManualSplits = Clean(p.UserDecisions.ManualSplits).Select(Clean).ToList();

        p.ColonyKeys = Clean(p.ColonyKeys);
        foreach (var k in p.ColonyKeys)
        {
            k.Key ??= "";
            k.FingerprintHash ??= "";
            k.StartingPawnIds = Clean(k.StartingPawnIds);
        }

        p.ImportState ??= new ImportState();
        p.ModMetricDescriptors = Clean(p.ModMetricDescriptors);

        p.MetricObservations = Clean(p.MetricObservations);
        foreach (var m in p.MetricObservations)
        {
            if (m.IsQuarantined)
                continue;
            m.Key ??= "";
            m.Identity = Normalize(m.Identity);
            m.FileAliases = Clean(m.FileAliases);
            foreach (var a in m.FileAliases)
                a.FileName ??= "";
            m.Metrics ??= new Dictionary<string, MetricEntry>(StringComparer.Ordinal);
        }
    }

    private static ColonyIdentity Normalize(ColonyIdentity? id)
    {
        id ??= new ColonyIdentity();
        id.StartingPawnIds = Clean(id.StartingPawnIds);
        return id;
    }

    /// <summary>Null list → empty; null elements (e.g. "[null]" in hand-edited JSON) are dropped.</summary>
    private static List<T> Clean<T>(List<T>? list) where T : class
        => list is null ? new List<T>() : list.Where(x => x is not null).ToList();

    /// <summary>Writes every DateTime as UTC ISO 8601; reads offsets and unmarked values as UTC.</summary>
    private sealed class UtcDateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => ToUtc(reader.GetDateTime());

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
            => writer.WriteStringValue(ToUtc(value));

        private static DateTime ToUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}
