using RimworldStatistics.Core;

namespace RimworldStatistics;

/// <summary>Where the ending patches (Task 16) and the GameComponent share the credits context. Main thread only.</summary>
public static class EndingRuntime
{
    public static readonly CreditsContext Context = new();
}
