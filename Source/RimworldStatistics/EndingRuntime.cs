using RimworldStatistics.Core;

namespace RimworldStatistics;

/// <summary>
/// Where the ending patches and the GameComponent share the credits context: used by the patches in play, reset by the
/// GameComponent while a game loads or starts (on the long-event thread, when nothing else uses it).
/// </summary>
public static class EndingRuntime
{
    public static readonly CreditsContext Context = new();
}
