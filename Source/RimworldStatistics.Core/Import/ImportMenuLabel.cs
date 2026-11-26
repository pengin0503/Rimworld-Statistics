namespace RimworldStatistics.Core;

/// <summary>
/// Formats the title-screen statistics menu label while an import runs (U20). The label is the translated base text plus
/// <c> (done/total)</c>; when no import runs it is exactly the base text.
/// </summary>
public static class ImportMenuLabel
{
    public static string Format(string baseLabel, ImportRunState state)
    {
        if (state.Phase != ImportRunPhase.Running)
            return baseLabel;
        var progress = state.Progress;
        return $"{baseLabel} ({progress?.Done ?? 0}/{progress?.Total ?? 0})";
    }

    /// <summary>The label for a running scan, or the base text when nothing runs.</summary>
    public static string Format(string baseLabel, ImportProgress? progress, bool running)
        => running ? $"{baseLabel} ({progress?.Done ?? 0}/{progress?.Total ?? 0})" : baseLabel;
}
