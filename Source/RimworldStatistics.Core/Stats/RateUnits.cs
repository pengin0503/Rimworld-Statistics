namespace RimworldStatistics.Core;

/// <summary>Unit chosen for a per-day rate display. The serialized values are part of ViewPreferences JSON.</summary>
public enum RateUnit
{
    PerDay = 0,
    PerYear = 1,
    PerRealHour = 2,
}

/// <summary>
/// Pure conversions for per-game-day rates. RimWorld has 60 days per year; one game day is 60,000 ticks, i.e.
/// 1,000 real seconds at 1x, so a real hour spans 3.6 game days.
/// </summary>
public static class RateUnits
{
    public const int DaysPerYear = 60;
    public const double GameDaysPerRealHour = 3.6;

    public static double PerYear(double perDay) => perDay * DaysPerYear;

    public static double PerRealHour(double perDay) => perDay * GameDaysPerRealHour;

    /// <summary>The displayed rate in the chosen unit; per-day is the identity conversion.</summary>
    public static double Convert(double perDay, RateUnit unit) => unit switch
    {
        RateUnit.PerYear => PerYear(perDay),
        RateUnit.PerRealHour => PerRealHour(perDay),
        _ => perDay,
    };
}
