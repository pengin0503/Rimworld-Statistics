using System.IO.Compression;
using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

public sealed class RecordsCodecTests
{
    private static float[] Series(int n, int seed)
    {
        // Wealth-like: slowly varying with noise, so compressors produce literal/length/distance mixes.
        var rnd = new Random(seed);
        var v = new float[n];
        var w = 12000f;
        for (var i = 0; i < n; i++)
        {
            w += (float)(rnd.NextDouble() * 400 - 150);
            v[i] = i % 7 == 0 ? w : MathF.Round(w);
        }
        return v;
    }

    private static string DeflateBase64(float[] values, CompressionLevel level)
    {
        using var ms = new MemoryStream();
        using (var ds = new DeflateStream(ms, level, leaveOpen: true))
            ds.Write(SaveXmlBuilder.FloatBytes(values));
        return Convert.ToBase64String(ms.ToArray());
    }

    [Theory]
    [InlineData(CompressionLevel.NoCompression, 0)]
    [InlineData(CompressionLevel.NoCompression, 20000)]   // stored blocks, more than one 64 KiB block
    [InlineData(CompressionLevel.Fastest, 3)]
    [InlineData(CompressionLevel.Fastest, 5000)]
    [InlineData(CompressionLevel.Optimal, 1)]
    [InlineData(CompressionLevel.Optimal, 700)]           // ≈ 350 in-game days at 2 points per day
    [InlineData(CompressionLevel.SmallestSize, 30000)]
    public void Deflate_RoundTripsDotNetCompressor(CompressionLevel level, int count)
    {
        var values = Series(count, count + (int)level);
        Assert.Equal(values, RecordsCodec.Decode(DeflateBase64(values, level), deflate: true));
    }

    [Fact]
    public void Deflate_RepetitiveData_UsesLongMatches()
    {
        var values = Enumerable.Repeat(5000f, 4000).Concat(Series(50, 9)).Concat(Enumerable.Repeat(5000f, 4000)).ToArray();
        Assert.Equal(values, RecordsCodec.Decode(DeflateBase64(values, CompressionLevel.Optimal), deflate: true));
    }

    [Theory]
    [InlineData(CompressionLevel.NoCompression)]
    [InlineData(CompressionLevel.Fastest)]
    [InlineData(CompressionLevel.Optimal)]
    public void Deflate_EveryStrictPrefix_Throws(CompressionLevel level)
    {
        var full = Convert.FromBase64String(DeflateBase64(Series(300, 4), level));
        // Length 0 is excluded: an empty payload is the encoding of an empty series (see the count 0 case above).
        for (var len = 1; len < full.Length; len++)
        {
            var prefix = Convert.ToBase64String(full, 0, len);
            Assert.Throws<InvalidDataException>(() => RecordsCodec.Decode(prefix, deflate: true));
        }
    }

    [Theory]
    [InlineData("Bw==")]       // BFINAL=1, BTYPE=11 (reserved)
    [InlineData("AQAAAAA=")]   // stored block whose NLEN is not the complement of LEN
    public void Deflate_InvalidStream_Throws(string payload)
        => Assert.Throws<InvalidDataException>(() => RecordsCodec.Decode(payload, deflate: true));

    [Fact]
    public void Raw_IgnoresLineBreaksAndSpaces()
    {
        var b64 = Convert.ToBase64String(SaveXmlBuilder.FloatBytes(1f, 2f, 3f));
        var wrapped = "\n\t" + b64[..4] + "\r\n " + b64[4..] + "\n";
        Assert.Equal(new[] { 1f, 2f, 3f }, RecordsCodec.Decode(wrapped, deflate: false));
    }

    [Fact]
    public void Raw_LengthNotMultipleOfFour_Throws()
        => Assert.Throws<InvalidDataException>(() => RecordsCodec.Decode(Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5 }), deflate: false));
}
