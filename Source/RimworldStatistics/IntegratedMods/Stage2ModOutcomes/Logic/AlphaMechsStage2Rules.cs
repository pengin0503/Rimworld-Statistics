namespace RimworldStatistics.Stage2ModOutcomes;

/// <summary>
/// Pure eligibility helpers for Alpha Mechs (sarg.alphamechs, 1.6/Assemblies/AlphaMechs.dll
/// selected by &lt;v1.7&gt; in loadFolders.xml) Stage 2 bossgroup commander kills
/// (AM_Infernus, AM_WarEmpress, AM_Apoptosis).
/// </summary>
internal static class AlphaMechsStage2Rules
{
    internal const string OwnerPackageId = "sarg.alphamechs";

    internal static bool IsAlphaMechsBossDef(string? bossDefPackageId)
        => string.Equals(bossDefPackageId, OwnerPackageId, StringComparison.OrdinalIgnoreCase);

    internal static bool ShouldRecordBossKill(
        string? bossDefPackageId,
        bool isPawnDead,
        bool isPlayerPawn,
        bool hasPlayerPresenceOnMap)
        => IsAlphaMechsBossDef(bossDefPackageId) &&
           isPawnDead &&
           !isPlayerPawn &&
           hasPlayerPresenceOnMap;
}
