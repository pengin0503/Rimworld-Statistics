namespace RimworldStatistics.NewModOutcomes;

internal static class CampCompletion
{
    internal static void Record(object session, Func<object?> currentSession, bool mainThread,
        Action<Action> afterLongEvent, Action record)
    {
        if (mainThread)
            RecordForOriginalSession();
        else
            afterLongEvent(RecordForOriginalSession);

        void RecordForOriginalSession()
        {
            if (ReferenceEquals(session, currentSession()))
                record();
        }
    }
}
