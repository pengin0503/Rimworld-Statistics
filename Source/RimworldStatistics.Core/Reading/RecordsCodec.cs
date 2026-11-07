using System.Buffers.Binary;

namespace RimworldStatistics.Core;

/// <summary>
/// Decodes HistoryAutoRecorder "records" (Base64 of little-endian float32) and "recordsDeflate" (the same, raw-deflated).
/// Throws <see cref="FormatException"/> for bad Base64 and <see cref="InvalidDataException"/> for bad or truncated data.
/// </summary>
public static class RecordsCodec
{
    public static float[] Decode(string base64, bool deflate)
    {
        var bytes = Convert.FromBase64String(StripWhitespace(base64));
        if (deflate)
            bytes = Inflate(bytes);
        if (bytes.Length % 4 != 0)
            throw new InvalidDataException($"records length {bytes.Length} is not a multiple of 4");
        var values = new float[bytes.Length / 4];
        for (var i = 0; i < values.Length; i++)
            values[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * 4, 4));
        return values;
    }

    /// <summary>
    /// Like <see cref="Decode"/>, for the numeric expansion only: false (never an exception) for bad data or for more
    /// than <paramref name="maxPoints"/> values, so one broken or oversized series never fails the save.
    /// </summary>
    public static bool TryDecodeLimited(string base64, bool deflate, int maxPoints, out float[] values)
    {
        values = Array.Empty<float>();
        var maxBytes = maxPoints * 4;
        try
        {
            var bytes = Convert.FromBase64String(StripWhitespace(base64));
            if (deflate)
                bytes = RawInflate.Inflate(bytes, maxBytes);
            if (bytes.Length % 4 != 0 || bytes.Length > maxBytes)
                return false;
            values = new float[bytes.Length / 4];
            for (var i = 0; i < values.Length; i++)
                values[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * 4, 4));
            return true;
        }
        catch (Exception e) when (e is FormatException or InvalidDataException)
        {
            return false;
        }
    }

    private static string StripWhitespace(string s)
    {
        if (!s.Any(char.IsWhiteSpace))
            return s;
        return string.Concat(s.Where(c => !char.IsWhiteSpace(c)));
    }

    // RawInflate, not DeflateStream: DeflateStream reports a truncated stream as a clean end of data.
    private static byte[] Inflate(byte[] compressed) => RawInflate.Inflate(compressed);
}
