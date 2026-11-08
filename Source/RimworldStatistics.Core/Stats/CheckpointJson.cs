using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RimworldStatistics.Core;

public enum CheckpointParseStatus
{
    /// <summary>A checkpoint of the known format.</summary>
    Ok,

    /// <summary>Well-formed JSON of another (newer) format: kept verbatim, never interpreted or extended.</summary>
    UnknownFormat,

    /// <summary>Not a checkpoint: no id, a negative tick, or not JSON at all.</summary>
    Invalid,
}

public sealed record CheckpointParse(CheckpointParseStatus Status, CheckpointPayload? Payload);

/// <summary>The codec of the save-embedded checkpoint (compact JSON, camelCase, invariant numbers, UTC ISO 8601 times).</summary>
public static class CheckpointJson
{
    private static readonly JsonSerializerOptions Compact = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var o = new JsonSerializerOptions(ProfileJson.Options) { WriteIndented = false };
        o.MakeReadOnly(populateMissingResolver: true);
        return o;
    }

    public static string Serialize(CheckpointPayload payload) => JsonSerializer.Serialize(payload, Compact);

    /// <summary>
    /// Reads a checkpoint. Only formatVersion 1 with a non-empty id and a non-negative tick is a checkpoint; another
    /// formatVersion of an object is <see cref="CheckpointParseStatus.UnknownFormat"/>; everything else is invalid. Never throws.
    /// </summary>
    public static CheckpointParse Parse(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return new CheckpointParse(CheckpointParseStatus.Invalid, null);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("formatVersion", out var fv) || !fv.TryGetInt32(out var version))
                return new CheckpointParse(CheckpointParseStatus.Invalid, null);
            if (version != CheckpointPayload.CurrentFormatVersion)
                return new CheckpointParse(version > CheckpointPayload.CurrentFormatVersion ? CheckpointParseStatus.UnknownFormat : CheckpointParseStatus.Invalid, null);
            var payload = root.Deserialize<CheckpointPayload>(Compact);
            if (payload is null || string.IsNullOrEmpty(payload.CheckpointId) || payload.SaveTick < 0)
                return new CheckpointParse(CheckpointParseStatus.Invalid, null);
            payload.Families ??= new Dictionary<string, FamilyState>(StringComparer.Ordinal);
            if (payload.ParentCheckpointId?.Length == 0)
                payload.ParentCheckpointId = null;
            return new CheckpointParse(CheckpointParseStatus.Ok, payload);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException or FormatException or ArgumentException)
        {
            return new CheckpointParse(CheckpointParseStatus.Invalid, null);
        }
    }

    /// <summary>SHA-256 (lowercase hex) of the exact UTF-8 text a save holds: the identity of a checkpoint's content.</summary>
    public static string Hash(string json) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
}
