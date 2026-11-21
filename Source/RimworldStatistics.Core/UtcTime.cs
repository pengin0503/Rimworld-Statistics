using System.Globalization;

namespace RimworldStatistics.Core;

/// <summary>Shared UTC timestamp normalization and numeric tolerance/clamping helpers.</summary>
public static class UtcTime
{
    public static DateTime ToUtc(DateTime value)
    {
        if (value == default)
            return new DateTime(0, DateTimeKind.Utc);
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }

    public static DateTime? ValidUtc(DateTime value)
    {
        if (value == default)
            return null;
        var utc = ToUtc(value);
        return utc == default || utc.Year <= 1970 ? null : utc;
    }

    public static string FormatIso(DateTime value)
        => ToUtc(value).ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

    public static string FormatCompactStamp(DateTime value)
        => ToUtc(value).ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    public static bool RelativeEquals(double a, double b)
        => double.IsFinite(a) && double.IsFinite(b)
            && Math.Abs(a - b) <= Math.Max(1e-6, Math.Abs(b) * 1e-9);

    public static long ClampToInt64(double value)
    {
        if (!double.IsFinite(value))
            return 0;
        var rounded = Math.Round(value);
        if (rounded >= long.MaxValue)
            return long.MaxValue;
        if (rounded <= long.MinValue)
            return long.MinValue;
        return (long)rounded;
    }

    public static int ClampToInt32(long value)
        => value >= int.MaxValue ? int.MaxValue : value <= int.MinValue ? int.MinValue : (int)value;
}
