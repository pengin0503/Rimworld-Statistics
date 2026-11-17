namespace RimworldStatistics.Core;

/// <summary>
/// A value that a window builds on a pool thread and polls every frame (#322, #326). One build runs at a time, and each key
/// is built once: the key names what the value depends on beyond what <see cref="Reset"/> covers (for the statistics window,
/// the profile's revision). A build that failed is remembered with its key, so polling neither starts the same failing build
/// again every few frames nor waits forever for a value that will not come. It is built again only when the key changes or
/// <see cref="Retry"/> is called; until a build succeeds, <see cref="Failure"/> says why there is no value.
/// </summary>
public sealed class RetainedBuild<TKey, TValue> where TValue : class
{
    private readonly IEqualityComparer<TKey> _keys;
    private Task<TValue>? _running;
    private TKey _runningKey = default!;
    private bool _finished;              // a build, or a value at hand, finished for _finishedKey
    private TKey _finishedKey = default!;
    private bool _retry;
    private bool _reported;              // the failure of _reportedKey was handed out for logging
    private TKey _reportedKey = default!;

    public RetainedBuild(IEqualityComparer<TKey>? keys = null) => _keys = keys ?? EqualityComparer<TKey>.Default;

    /// <summary>The value of the last build that succeeded, kept while a newer one runs; null before the first.</summary>
    public TValue? Value { get; private set; }

    /// <summary>Why the last finished build failed; null once one succeeds. It stays while a retry runs.</summary>
    public Exception? Failure { get; private set; }

    /// <summary>A build is running.</summary>
    public bool Running => _running is not null;

    /// <summary>
    /// Main thread, every frame. Takes the outcome of a finished build. Then, when <paramref name="ready"/> holds the value for
    /// <paramref name="key"/> (a cache hit), uses it; otherwise starts <paramref name="start"/> unless a build is running, or the
    /// last build for this same key finished, successfully or not, and no retry was asked for.
    /// </summary>
    /// <returns>A failure to log: the first one for each key, so a failure that persists is logged once per condition.</returns>
    public Exception? Update(TKey key, Func<Task<TValue>> start, TValue? ready = null)
    {
        Exception? report = null;
        if (_running is { IsCompleted: true } done)
        {
            _running = null;
            if (done.Status == TaskStatus.RanToCompletion)
                Finish(_runningKey, done.Result, null, ref report);
            else
                Finish(_runningKey, null, Unwrap(done), ref report);
        }
        if (ready is not null)
        {
            _running = null;    // the value is at hand: a build still running would only produce it again
            _retry = false;
            Finish(key, ready, null, ref report);
            return report;
        }
        if (_running is null && (_retry || !_finished || !_keys.Equals(_finishedKey, key)))
        {
            _retry = false;
            _runningKey = key;
            try
            {
                _running = start();
            }
            catch (Exception e)
            {
                Finish(key, null, e, ref report);
            }
        }
        return report;
    }

    /// <summary>Builds the current key again on the next <see cref="Update"/>, even though its last build failed.</summary>
    public void Retry() => _retry = true;

    /// <summary>
    /// Forgets the value, the failure and the keys: they belong to other conditions (another session, option or scope).
    /// A build still running is abandoned; its outcome is never used.
    /// </summary>
    public void Reset()
    {
        _running = null;
        _finished = false;
        _retry = false;
        _reported = false;
        Value = null;
        Failure = null;
    }

    private void Finish(TKey key, TValue? value, Exception? failure, ref Exception? report)
    {
        _finished = true;
        _finishedKey = key;
        if (failure is null)
        {
            Value = value;
            Failure = null;
            _reported = false;
            return;
        }
        Failure = failure;
        if (!_reported || !_keys.Equals(_reportedKey, key))
        {
            _reported = true;
            _reportedKey = key;
            report = failure;
        }
    }

    private static Exception Unwrap(Task task) => task.Exception is { } aggregate
        ? aggregate.InnerExceptions.Count == 1 ? aggregate.InnerExceptions[0] : aggregate
        : new TaskCanceledException(task);
}
