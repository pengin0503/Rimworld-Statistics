using System.Globalization;

namespace StatisticsSdkSample;

/// <summary>
/// Persist only a random per-game session token and a monotonically increasing ordinal.
/// The event ID is deterministic after save/load, branch-local, and contains no person data.
/// </summary>
public sealed class SampleEventLedger
{
    public string SessionToken { get; }
    public int Count { get; private set; }

    public SampleEventLedger(string sessionToken, int count)
    {
        if (string.IsNullOrWhiteSpace(sessionToken) || count < 0)
            throw new ArgumentException("A session token and nonnegative event ordinal are required");
        SessionToken = sessionToken;
        Count = count;
    }

    /// <summary>
    /// A confirmed sample action always increments the local count, even when the optional
    /// SDK is absent or disabled. The SDK's record result reports only whether it observed it.
    /// </summary>
    public bool Emit(Func<string, bool> tryRecord)
    {
        if (Count == int.MaxValue)
            return false;
        var uniqueId = SessionToken + ":event:" + Count.ToString(CultureInfo.InvariantCulture);
        Count++;
        try { return tryRecord(uniqueId); }
        catch (Exception) { return false; } // diagnostic action must not break the game
    }
}
