using RimworldStatistics.Core;

namespace RimworldStatistics.UI;

/// <summary>
/// A small snapshot of the record file's health for the UI: its size against the load limit and whether another program
/// changed it. Refreshed on a worker at most every two seconds (never per frame, and never on the main thread), shared by
/// the Data Management tab, the tab badges and the diagnostics panel.
/// </summary>
internal sealed record ProfileStatus(long? ProfileBytes, ProfileExternalChange ExternalChange)
{
    public ProfileCapacity? Capacity => ProfileBytes is { } bytes ? ProfileCapacity.ForSize(bytes) : null;

    public bool IsCriticalCapacity => Capacity?.Level is ProfileCapacityLevel.Critical or ProfileCapacityLevel.OverLimit;

    public bool IsChangedExternally => ExternalChange == ProfileExternalChange.ChangedExternally;
}

internal static class ProfileStatusMonitor
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private static Task<ProfileStatus>? refresh;
    private static ProfileStatus? current;
    private static ProfileSession? polledSession;
    private static DateTime nextRefreshUtc;

    /// <summary>The last completed snapshot, or null before the first check finishes.</summary>
    internal static ProfileStatus? Current => Volatile.Read(ref current);

    /// <summary>
    /// Starts one background check when the interval has passed (or the session changed). Safe to call every frame; does no
    /// IO on the calling thread and never throws.
    /// </summary>
    internal static void Poll(ProfileSession? session)
    {
        Complete();
        if (session is null)
            return;
        if (!ReferenceEquals(polledSession, session))
        {
            polledSession = session;
            Volatile.Write(ref current, null);
            nextRefreshUtc = DateTime.MinValue;
        }
        if (refresh is not null || DateTime.UtcNow < nextRefreshUtc)
            return;
        nextRefreshUtc = DateTime.UtcNow + Interval;
        refresh = Task.Run(() => Read(session));
    }

    private static ProfileStatus Read(ProfileSession session)
    {
        long? bytes = null;
        try
        {
            var info = new FileInfo(Path.Combine(ProfileService.DirectoryPath, "profile.json"));
            if (info.Exists)
                bytes = info.Length;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // unknown size: show no capacity line
        }

        ProfileExternalChange externalChange;
        try
        {
            externalChange = session.CheckExternalChange();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
        {
            externalChange = ProfileExternalChange.Unknown;
        }
        return new ProfileStatus(bytes, externalChange);
    }

    private static void Complete()
    {
        if (refresh is not { IsCompleted: true } completed)
            return;
        refresh = null;
        if (completed.IsCompletedSuccessfully)
            Volatile.Write(ref current, completed.Result);
        else
            _ = completed.Exception;   // observe it; the previous snapshot (if any) stays
    }
}
