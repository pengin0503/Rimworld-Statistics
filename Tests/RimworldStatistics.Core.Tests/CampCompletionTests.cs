using RimworldStatistics.NewModOutcomes;

namespace RimworldStatistics.Core.Tests;

public sealed class CampCompletionTests
{
    [Theory]
    [InlineData(true, false, 1)]
    [InlineData(false, false, 1)]
    [InlineData(false, true, 0)]
    public void CompletedCampRecordsOnGameThreadOnlyInItsOriginalSession(
        bool mainThread, bool reloadBeforeCallback, int expected)
    {
        object session = new();
        object currentSession = session;
        var callbacks = new Queue<Action>();
        int count = 0;

        CampCompletion.Record(session, () => currentSession, mainThread, callbacks.Enqueue, () => count++);

        Assert.Equal(mainThread ? 1 : 0, count);
        Assert.Equal(mainThread ? 0 : 1, callbacks.Count);
        if (reloadBeforeCallback)
            currentSession = new object();
        while (callbacks.TryDequeue(out var callback))
            callback();
        Assert.Equal(expected, count);
    }
}
