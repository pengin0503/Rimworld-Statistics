namespace RimworldStatistics.Core;

/// <summary>
/// The current records of <see cref="SavePresence.Classify"/>: how many distinct save files the profile still records,
/// how many of them are present on disk, and how many are archived (the file is gone).
/// </summary>
public sealed record SavePresenceSummary(int Total, int Present, int Archived, IReadOnlyList<string> MissingFiles)
{
    /// <summary>The archived count under the name the specification uses for a save whose file is gone.</summary>
    public int Missing => Archived;
}

/// <summary>Classifies a profile's current save records by whether their files are still on disk (S7).</summary>
public static class SavePresence
{
    /// <summary>
    /// Counts the profile's current (non-superseded) save observations by file name. Superseded history is ignored, and
    /// duplicate current records of one name count once. <paramref name="presentFiles"/> is the set of save file names
    /// present on disk at the last scan. Missing names are returned sorted ordinal.
    /// </summary>
    public static SavePresenceSummary Classify(Profile profile, IReadOnlyList<string>? presentFiles)
    {
        ArgumentNullException.ThrowIfNull(profile);
        // Windows/macOS filesystems are case-insensitive: a case-only rename (or a cloud-sync case normalisation)
        // still leaves the save present. Compare both sides the same way; the reported order stays ordinal.
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (presentFiles is not null)
            for (var i = 0; i < presentFiles.Count; i++)
                if (!string.IsNullOrEmpty(presentFiles[i]))
                    present.Add(presentFiles[i]);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var observation in profile.SaveObservations)
        {
            if (observation.Superseded || string.IsNullOrEmpty(observation.FileName))
                continue;
            names.Add(observation.FileName);
        }

        var missing = new List<string>();
        var presentCount = 0;
        foreach (var name in names)
        {
            if (present.Contains(name))
                presentCount++;
            else
                missing.Add(name);
        }
        missing.Sort(StringComparer.Ordinal);
        return new SavePresenceSummary(names.Count, presentCount, missing.Count, missing);
    }
}
