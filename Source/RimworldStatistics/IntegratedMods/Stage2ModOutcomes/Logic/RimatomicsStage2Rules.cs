using RimworldStatistics.Core;

namespace RimworldStatistics.Stage2ModOutcomes;

internal static class RimatomicsStage2Rules
{
    internal const string OwnerPackageId = "Dubwise.Rimatomics";

    internal static string? ResolveHelInterceptKey(
        bool casterPlayerOwned,
        bool castSucceeded,
        bool targetAliveBefore,
        bool targetDestroyedAfter,
        int killCounterBefore,
        int killCounterAfter,
        string? projectilePackageId,
        string? projectileDefName)
    {
        if (!casterPlayerOwned || !castSucceeded || !targetAliveBefore || !targetDestroyedAfter ||
            killCounterAfter <= killCounterBefore)
            return null;

        return OptionalTransitionRecording.QualifiedDefKey(projectilePackageId, projectileDefName);
    }

    internal static string? ResolveRailgunBombardmentKey(
        bool playerOrigin,
        bool wasSpawnedBefore,
        bool wasArrivedBefore,
        bool isArrivedAfter,
        bool isSpawnedAfter,
        string? shellPackageId,
        string? shellDefName)
    {
        if (!playerOrigin || !wasSpawnedBefore || wasArrivedBefore || !isArrivedAfter || isSpawnedAfter)
            return null;

        return OptionalTransitionRecording.QualifiedDefKey(shellPackageId, shellDefName);
    }

    internal static string? ResolveReactorScramKey(
        bool playerHomeReactor,
        bool wasShutdownBefore,
        int fuelCountBefore,
        bool isShutdownAfter,
        string? reactorPackageId,
        string? reactorDefName)
    {
        if (!playerHomeReactor || wasShutdownBefore || fuelCountBefore <= 0 || !isShutdownAfter)
            return null;

        return OptionalTransitionRecording.QualifiedDefKey(reactorPackageId, reactorDefName);
    }

    internal static string? ResolveReactorMeltdownKey(
        bool activeMeltdownScope,
        string? reactorPackageId,
        string? reactorDefName)
    {
        if (!activeMeltdownScope)
            return null;

        return OptionalTransitionRecording.QualifiedDefKey(reactorPackageId, reactorDefName);
    }
}
