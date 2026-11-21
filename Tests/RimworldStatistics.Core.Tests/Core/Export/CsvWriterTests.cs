using System.Globalization;
using System.Text;

namespace RimworldStatistics.Core.Tests;

public sealed class CsvWriterTests
{
    [Fact]
    public void EncodesUtf8BomRfc4180FieldsAndCrLf()
    {
        var bytes = Write(CsvField.Of("a,b"), CsvField.Of("a\"b"), CsvField.Of("a\nb"), CsvField.Of(null), CsvField.Of(new string('名', 300)));
        Assert.Equal(new byte[] { 0xef, 0xbb, 0xbf }, bytes[..3]);
        Assert.Equal("\"a,b\",\"a\"\"b\",\"a\nb\",," + new string('名', 300) + "\r\n", Encoding.UTF8.GetString(bytes[3..]));
    }

    [Fact]
    public void FormatsNumbersInvariantAndProtectsText()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var bytes = Write(CsvField.Number(1234.5), CsvField.Number(-3), CsvField.Integer(-20),
                CsvField.Of("=SUM(A1)"), CsvField.Of("-Alpha"), CsvField.Of(" @A1"), CsvField.Number(double.NaN));
            Assert.Equal("1234.5,-3,-20,'=SUM(A1),'-Alpha,' @A1,\r\n", Encoding.UTF8.GetString(bytes[3..]));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static byte[] Write(params CsvField[] fields)
    {
        using var stream = new MemoryStream();
        using (var csv = new CsvWriter(stream))
            csv.Row(fields);
        return stream.ToArray();
    }
}
