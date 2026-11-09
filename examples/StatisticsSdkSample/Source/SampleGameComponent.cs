using Verse;

namespace StatisticsSdkSample;

/// <summary>
/// An intentionally tiny game-owned test counter; no tick updates and no per-pawn data.
/// The session token and ordinal are saved with RimWorld's normal save system.
/// </summary>
public sealed class SampleGameComponent : GameComponent
{
    private string? sessionToken;
    private int completedActions;

    public SampleGameComponent(Game game) { }

    public int CompletedActions => completedActions;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref sessionToken, "rwsSdkSampleSession");
        Scribe_Values.Look(ref completedActions, "rwsSdkSampleActions", 0);
    }

    public override void StartedNewGame()
    {
        sessionToken = Guid.NewGuid().ToString("N");
        completedActions = 0;
        SampleSdkClient.SetCurrentCount(completedActions);
    }

    public override void LoadedGame()
    {
        sessionToken ??= Guid.NewGuid().ToString("N");
        completedActions = Math.Max(0, completedActions);
        SampleSdkClient.SetCurrentCount(completedActions);
    }

    /// <summary>Called only by the explicit development settings button on the game thread.</summary>
    public bool SimulateConfirmedAction()
    {
        sessionToken ??= Guid.NewGuid().ToString("N");
        var ledger = new SampleEventLedger(sessionToken, completedActions);
        var recorded = ledger.Emit(SampleSdkClient.OnConfirmedEvent);
        completedActions = ledger.Count;
        SampleSdkClient.SetCurrentCount(completedActions);
        return recorded;
    }
}
