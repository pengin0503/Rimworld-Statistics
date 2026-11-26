namespace RimworldStatistics.Core;

/// <summary>How full the profile file is against the size this build can load (<see cref="ProfileStore.MaxProfileFileBytes"/>).</summary>
public enum ProfileCapacityLevel
{
    /// <summary>Below 75% of the load limit.</summary>
    Normal,

    /// <summary>At least 75% and below 90%: saving is still supported, but the file is growing.</summary>
    Approaching,

    /// <summary>At least 90% and at most 100%: saving is still supported, but close to the load limit.</summary>
    Critical,

    /// <summary>Above the load limit: this build will not load the file (a backup may still be readable).</summary>
    OverLimit,
}

/// <summary>The profile file's size as a fraction of the 256 MB load limit, for a capacity warning in the UI.</summary>
public readonly record struct ProfileCapacity(long SizeBytes, double Fraction, ProfileCapacityLevel Level)
{
    /// <summary>The largest profile file this build loads.</summary>
    public const long LimitBytes = ProfileStore.MaxProfileFileBytes;

    /// <summary>True when the size deserves a warning (75% or more of the load limit).</summary>
    public bool IsWarning => Level != ProfileCapacityLevel.Normal;

    /// <summary>Classifies <paramref name="sizeBytes"/>; a negative or unknown size counts as zero.</summary>
    public static ProfileCapacity ForSize(long sizeBytes)
    {
        var size = Math.Max(0, sizeBytes);
        var fraction = (double)size / LimitBytes;
        // Compare against the byte counts for 75% and 90% (rounded down), so a size computed with integer
        // arithmetic at exactly 90% is still Critical rather than 89.9999...%.
        var level = size > LimitBytes ? ProfileCapacityLevel.OverLimit
            : size >= LimitBytes * 90 / 100 ? ProfileCapacityLevel.Critical
            : size >= LimitBytes * 75 / 100 ? ProfileCapacityLevel.Approaching
            : ProfileCapacityLevel.Normal;
        return new ProfileCapacity(size, fraction, level);
    }
}
