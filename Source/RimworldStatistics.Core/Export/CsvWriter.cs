using System.Globalization;
using System.Text;

namespace RimworldStatistics.Core;

public readonly record struct CsvField(string? Text, bool Numeric)
{
    public static CsvField Of(string? text) => new(text, false);
    public static CsvField Number(double? value) => new(
        value is { } number && double.IsFinite(number) ? number.ToString("R", CultureInfo.InvariantCulture) : null, true);
    public static CsvField Integer(long? value) => new(value?.ToString(CultureInfo.InvariantCulture), true);
}

public sealed class CsvWriter(Stream output) : IDisposable
{
    private readonly StreamWriter writer = new(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true);

    public void Row(params CsvField[] fields)
    {
        for (var i = 0; i < fields.Length; i++)
        {
            if (i > 0)
                writer.Write(',');
            var field = fields[i];
            if (field.Text is not { Length: > 0 } text)
                continue;
            var trimmed = text.AsSpan().TrimStart();
            var protect = !field.Numeric && ((trimmed.Length > 0 && trimmed[0] is '=' or '+' or '-' or '@')
                || text[0] is '\t' or '\r' or '\n');
            var quoted = text.AsSpan().IndexOfAny(",\"\r\n") >= 0;
            if (quoted)
                writer.Write('"');
            if (protect)
                writer.Write('\'');
            writer.Write(quoted ? text.Replace("\"", "\"\"") : text);
            if (quoted)
                writer.Write('"');
        }
        writer.Write("\r\n");
    }

    public void Dispose() => writer.Dispose();
}
