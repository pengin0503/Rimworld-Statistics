using System.Globalization;

namespace RimworldStatistics.Core;

public enum DisplayDayFormat
{
    DecimalDays = 0,
    YearsAndDays = 1,
}

/// <summary>
/// Pure formatting shared by the details table, the rankings table and the window's day labels. The game side supplies the
/// localized "years and days" template (RWS_DaysValue); Core only computes the invariant numbers.
/// </summary>
public static class DisplayFormat
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Decimal days, or years and remaining days (1 year = 60 days) using the supplied template.</summary>
    public static string Days(long ticks, DisplayDayFormat format, bool grouping,
        Func<string, string, string, string> yearsAndDays)
    {
        var decimalDays = Number(ticks / (double)StatFormat.TicksPerDay, 1, grouping);
        if (format != DisplayDayFormat.YearsAndDays)
            return decimalDays;
        var (_, years, days) = StatFormat.SplitDays(ticks);
        return yearsAndDays(decimalDays, years.ToString(Inv), days.ToString(Inv));
    }

    /// <summary>Same choice as <see cref="Days"/> for a value that is already expressed in days.</summary>
    public static string DayCount(double days, DisplayDayFormat format, bool grouping,
        Func<string, string, string, string> yearsAndDays)
    {
        var decimalDays = Number(days, 1, grouping);
        if (format != DisplayDayFormat.YearsAndDays)
            return decimalDays;
        var totalDays = WholeDays(days);
        return yearsAndDays(decimalDays, (totalDays / StatFormat.DaysPerYear).ToString(Inv),
            (totalDays % StatFormat.DaysPerYear).ToString(Inv));
    }

    private static long WholeDays(double days)
    {
        if (double.IsNaN(days) || double.IsInfinity(days))
            return 0;
        // Keep the conversion defined for absurd inputs instead of overflowing the long cast.
        var limit = long.MaxValue / (double)StatFormat.TicksPerDay;
        return (long)Math.Floor(Math.Clamp(days, -limit, limit));
    }

    /// <summary>Invariant fixed-point digits with or without thousands grouping (N/F with the same precision).</summary>
    public static string Number(double value, int decimals, bool grouping)
    {
        var places = Math.Clamp(decimals, 0, 6);
        return value.ToString((grouping ? "N" : "F") + places.ToString(Inv), Inv);
    }

    /// <summary>Whole numbers, matching <see cref="StatFormat.Group"/> when grouping is on, plain digits when off.</summary>
    public static string WholeNumber(double value, bool grouping)
        => grouping ? StatFormat.Group(value)
            : Math.Round(value, MidpointRounding.AwayFromZero).ToString("F0", Inv);
}
