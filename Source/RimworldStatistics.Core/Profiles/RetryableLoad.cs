namespace RimworldStatistics.Core;

/// <summary>
/// A value loaded once on a pool thread and shared by every caller, such as the profile session (#326). Polling with
/// <see cref="TryGet"/> never starts another load: a load that failed stays failed, so a window drawn every frame shows the
/// failure instead of rethrowing it every frame or reading the file again every frame. <see cref="Start"/> with
/// <c>retryFailed</c> replaces a failed load; opening the window, its retry button and the callers that need the value now
/// (and report a failure themselves) use it.
/// </summary>
public sealed class RetryableLoad<T> where T : class
{
    private readonly object _gate = new();
    private readonly Func<Task<T>> _load;
    private Task<T>? _task;

    public RetryableLoad(Func<Task<T>> load) => _load = load;

    /// <summary>The load, started if none was; with <paramref name="retryFailed"/>, a failed load is started again.</summary>
    public Task<T> Start(bool retryFailed = false)
    {
        lock (_gate)
        {
            if (_task is null || retryFailed && _task.IsCompleted && _task.Status != TaskStatus.RanToCompletion)
            {
                try
                {
                    _task = _load();
                }
                catch (Exception e)
                {
                    _task = Task.FromException<T>(e);
                }
            }
            return _task;
        }
    }

    /// <summary>The value once loaded. Null while loading, and null with <paramref name="failure"/> when the load failed.</summary>
    public T? TryGet(out Exception? failure)
    {
        var task = Start();
        failure = null;
        if (!task.IsCompleted)
            return null;
        if (task.Status == TaskStatus.RanToCompletion)
            return task.Result;
        failure = task.Exception is { } aggregate
            ? aggregate.InnerExceptions.Count == 1 ? aggregate.InnerExceptions[0] : aggregate
            : new TaskCanceledException(task);
        return null;
    }

    /// <summary>Puts <paramref name="task"/> in place of the current load (a restore hands over the session it opens).</summary>
    public void Replace(Task<T> task)
    {
        lock (_gate)
            _task = task;
    }
}
