namespace RimworldStatistics.Core;

/// <summary>How many CPU threads reading saves may use (MOD settings, "processing resources").</summary>
public enum ImportThreadMode
{
    /// <summary>As before this setting existed: half the logical processors, at most three, at normal priority.</summary>
    Standard = 0,

    /// <summary>Every logical processor but one, at below-normal priority.</summary>
    More = 1,

    /// <summary>The number the player chose, at below-normal priority.</summary>
    Manual = 2,
}

/// <summary>How much memory the in-memory caches may hold (MOD settings, "processing resources").</summary>
public enum MemoryUseMode
{
    /// <summary>As before this setting existed.</summary>
    Standard = 0,

    /// <summary>Larger caches of parsed chart series, statistics views and colony reports.</summary>
    More = 1,
}

/// <summary>The cache sizes one <see cref="MemoryUseMode"/> (and the background precomputation setting) stands for.</summary>
public readonly record struct CacheLimits(int ChartSeries, int ChartPoints, int Views, int ColonyReports);

/// <summary>
/// What the processing-resource settings mean in numbers. Standard values are exactly the ones the MOD used before the
/// settings existed, so a player who changes nothing gets the same behaviour. Only the amount of work done at once and
/// how much is kept in memory change; nothing here changes what is read, recorded or shown.
/// </summary>
public static class ResourcePolicy
{
    /// <summary>The most saves read at once whatever is chosen: well above any desktop CPU, a guard against bad input.</summary>
    public const int MaxImportThreads = 64;

    /// <summary>Measured on 55 real saves: about 190 bytes per kept chart point (see <see cref="RecorderChartCache"/>).</summary>
    public const int BytesPerChartPoint = 190;

    public static readonly CacheLimits StandardCaches = new(ChartSeries: 256, ChartPoints: 32_768, Views: 8, ColonyReports: 0);

    public static readonly CacheLimits MoreCaches = new(ChartSeries: 2_048, ChartPoints: 262_144, Views: 32, ColonyReports: 16);

    /// <summary>The colony reports kept while background precomputation is on with standard memory use.</summary>
    public const int PrecomputeColonyReports = 4;

    /// <summary>The read-ahead the import used before this setting existed: half the logical processors, one to three.</summary>
    public static int StandardImportThreads(int processorCount) => Math.Clamp(processorCount / 2, 1, 3);

    /// <summary>The highest number a player can choose: one thread per logical processor.</summary>
    public static int MaxManualImportThreads(int processorCount) => Math.Clamp(processorCount, 1, MaxImportThreads);

    /// <summary>How many saves the import reads at once (<see cref="ImportEngine"/>'s read-ahead).</summary>
    public static int ImportThreads(ImportThreadMode mode, int manualThreads, int processorCount) => mode switch
    {
        ImportThreadMode.More => Math.Clamp(processorCount - 1, 1, MaxImportThreads),
        ImportThreadMode.Manual => Math.Clamp(manualThreads, 1, MaxManualImportThreads(processorCount)),
        _ => StandardImportThreads(processorCount),
    };

    /// <summary>
    /// The priority of the threads that read saves. Standard keeps the normal priority it always had; once more threads may
    /// run, they yield to the game's own threads so the title screen stays smooth.
    /// </summary>
    public static ThreadPriority ImportPriority(ImportThreadMode mode)
        => mode == ImportThreadMode.Standard ? ThreadPriority.Normal : ThreadPriority.BelowNormal;

    /// <summary>The cache sizes for <paramref name="memory"/>; background precomputation keeps a few colony reports.</summary>
    public static CacheLimits Caches(MemoryUseMode memory, bool precompute)
    {
        var limits = memory == MemoryUseMode.More ? MoreCaches : StandardCaches;
        return precompute && limits.ColonyReports < PrecomputeColonyReports
            ? limits with { ColonyReports = PrecomputeColonyReports }
            : limits;
    }

    /// <summary>The most memory the chart cache may take with <paramref name="limits"/>, in MiB (for the settings text).</summary>
    public static int ChartCacheMiB(CacheLimits limits)
        => (int)Math.Ceiling(limits.ChartPoints * (double)BytesPerChartPoint / (1024 * 1024));

    /// <summary>Parses a stored setting; anything unknown is <see cref="ImportThreadMode.Standard"/>.</summary>
    public static ImportThreadMode ParseImportThreadMode(int stored)
        => Enum.IsDefined(typeof(ImportThreadMode), stored) ? (ImportThreadMode)stored : ImportThreadMode.Standard;

    /// <summary>Parses a stored setting; anything unknown is <see cref="MemoryUseMode.Standard"/>.</summary>
    public static MemoryUseMode ParseMemoryUseMode(int stored)
        => Enum.IsDefined(typeof(MemoryUseMode), stored) ? (MemoryUseMode)stored : MemoryUseMode.Standard;
}
