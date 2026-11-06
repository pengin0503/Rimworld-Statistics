using System.Globalization;

namespace RimworldStatistics.Core;

public static class StatFormat
{
    public const int TicksPerDay = 60000;
    public const int DaysPerYear = 60;

    /// <summary>Whole days, then years and remaining days (1 year = 60 days). Fractions are dropped.</summary>
    public static (long TotalDays, long Years, long Days) SplitDays(long ticks)
    {
        var totalDays = ticks / TicksPerDay;
        return (totalDays, totalDays / DaysPerYear, totalDays % DaysPerYear);
    }

    /// <summary>Rounded to an integer (midpoint away from zero) with invariant thousands separators.</summary>
    public static string Group(double value)
        => Math.Round(value, MidpointRounding.AwayFromZero).ToString("N0", CultureInfo.InvariantCulture);

    public static double Hours(double seconds) => seconds / 3600.0;
}
