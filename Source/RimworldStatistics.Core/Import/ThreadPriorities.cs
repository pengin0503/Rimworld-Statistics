namespace RimworldStatistics.Core;

/// <summary>Background work that yields to the game's own threads (the processing-resource settings).</summary>
internal static class ThreadPriorities
{
    /// <summary>
    /// Runs <paramref name="work"/> at <paramref name="priority"/> on this thread, then gives the thread back its own
    /// priority (a pool thread must not keep it). Normal runs the work untouched. The priority is only a hint to the
    /// operating system: where it cannot be set, the work runs all the same.
    /// </summary>
    public static T Run<T>(ThreadPriority priority, Func<T> work)
    {
        if (priority == ThreadPriority.Normal)
            return work();
        var thread = Thread.CurrentThread;
        ThreadPriority? previous = null;
        try
        {
            var current = thread.Priority;
            thread.Priority = priority;
            previous = current;
        }
        catch (Exception e) when (e is ThreadStateException or PlatformNotSupportedException)
        {
            // only a hint: the work is the same at any priority
        }
        try
        {
            return work();
        }
        finally
        {
            if (previous is { } restore)
            {
                try
                {
                    thread.Priority = restore;
                }
                catch (Exception e) when (e is ThreadStateException or PlatformNotSupportedException)
                {
                    // the thread keeps running; nothing else depends on its priority
                }
            }
        }
    }
}
