using System.Globalization;

namespace RimworldStatistics.Core;

/// <summary>Generates readable, finite, zero-inclusive 1/2/5 × 10^n axis ticks for bar charts (U11).</summary>
public static class MetricChartTicks
{
    public static IReadOnlyList<double> Generate(double min, double max, int targetSteps = 5)
    {
        var safeMin = double.IsFinite(min) ? min : 0d;
        var safeMax = double.IsFinite(max) ? max : 0d;
        var lo = Math.Min(0d, Math.Min(safeMin, safeMax));
        var hi = Math.Max(0d, Math.Max(safeMin, safeMax));

        if (lo == 0d && hi == 0d)
            return [0d, 1d];

        var steps = Math.Clamp(targetSteps, 2, 12);
        var scale = Math.Max(1d, Math.Max(Math.Abs(lo), Math.Abs(hi)));
        var rawStep = ((hi / scale - lo / scale) / steps) * scale;
        if (!double.IsFinite(rawStep) || rawStep <= 0d)
            return Fallback(lo, hi);

        var step = NiceStep(rawStep);
        if (!double.IsFinite(step) || step <= 0d)
            return Fallback(lo, hi);

        var startIndex = (long)Math.Floor(lo / step + 1e-9);
        while (startIndex * step > lo && startIndex > -100)
            startIndex--;

        var endIndex = (long)Math.Ceiling(hi / step - 1e-9);
        while (endIndex * step < hi && endIndex < 100)
            endIndex++;

        if (endIndex - startIndex > 16)
        {
            step = NiceStep(step * 2.01);
            if (!double.IsFinite(step) || step <= 0d)
                return Fallback(lo, hi);
            startIndex = (long)Math.Floor(lo / step + 1e-9);
            if (startIndex * step > lo)
                startIndex--;
            endIndex = (long)Math.Ceiling(hi / step - 1e-9);
            if (endIndex * step < hi)
                endIndex++;
        }

        if (startIndex > 0)
            startIndex = 0;
        if (endIndex < 0)
            endIndex = 0;

        var ticks = new List<double>();
        for (var k = startIndex; k <= endIndex; k++)
        {
            var value = k == 0 ? 0d : k * step;
            if (!double.IsFinite(value))
                return Fallback(lo, hi);
            ticks.Add(value);
        }

        if (ticks.Count < 2)
            return Fallback(lo, hi);
        if (!ticks.Contains(0d))
            return Fallback(lo, hi);

        return ticks;
    }

    public static string FormatAxisValue(double value, bool percent = false)
    {
        if (!double.IsFinite(value))
            return "0";

        var display = percent ? value * 100d : value;
        if (!double.IsFinite(display))
            return "0";
        if (display == 0d)
            return percent ? "0%" : "0";

        var abs = Math.Abs(display);
        string text;
        if (abs >= 1e9 || abs < 1e-3)
        {
            text = display.ToString("0.###E+0", CultureInfo.InvariantCulture);
        }
        else if (Math.Abs(display - Math.Round(display)) < 1e-9)
        {
            text = Math.Round(display).ToString("N0", CultureInfo.InvariantCulture);
        }
        else if (abs >= 100d)
        {
            text = display.ToString("N1", CultureInfo.InvariantCulture);
        }
        else if (abs >= 1d)
        {
            text = display.ToString("N2", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
        }
        else
        {
            text = display.ToString("0.###", CultureInfo.InvariantCulture);
        }

        return percent ? text + "%" : text;
    }

    private static double NiceStep(double rawStep)
    {
        var exp = Math.Floor(Math.Log10(rawStep));
        var mag = Math.Pow(10d, exp);
        if (!double.IsFinite(mag) || mag <= 0d)
            return double.NaN;

        var fraction = rawStep / mag;
        var nice = fraction <= 1.0 + 1e-9 ? 1.0
            : fraction <= 2.0 + 1e-9 ? 2.0
            : fraction <= 5.0 + 1e-9 ? 5.0
            : 10.0;
        return nice * mag;
    }

    private static IReadOnlyList<double> Fallback(double lo, double hi)
    {
        if (lo < 0d && hi > 0d)
            return [lo, 0d, hi];
        if (lo < 0d)
            return [lo, 0d];
        return [0d, hi > 0d ? hi : 1d];
    }
}
