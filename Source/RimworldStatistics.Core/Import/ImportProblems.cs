namespace RimworldStatistics.Core;

/// <summary>Why a save was not read, grouped by what the player can do about it (audit C5). Read-only diagnostics.</summary>
public enum ImportProblemKind
{
    /// <summary>The file changed while it was read (the game was saving it); the next scan reads it again.</summary>
    ChangedDuringRead,

    /// <summary>The file could not be opened (in use, permissions); the next scan tries again.</summary>
    Unreadable,

    /// <summary>The file is damaged or cut short (invalid XML or compressed data).</summary>
    Damaged,

    /// <summary>The file is not a RimWorld save.</summary>
    NotASave,

    /// <summary>The save was read but holds no colony identity this MOD can match (very old or edited saves).</summary>
    MissingIdentity,

    /// <summary>Anything else.</summary>
    Unexpected,
}

public static class ImportProblems
{
    /// <summary>Null for a save that was read.</summary>
    public static ImportProblemKind? Classify(SaveObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.Status == ObservationStatus.Ok)
            return null;
        if (observation.Status == ObservationStatus.Unidentifiable)
            return ImportProblemKind.MissingIdentity;
        var reason = observation.ErrorReason ?? "";
        if (reason == "not a savegame")
            return ImportProblemKind.NotASave;
        var type = reason.IndexOf(':') is var colon and > 0 ? reason[..colon] : reason;
        return type switch
        {
            "IOException" when reason.EndsWith("changed during read", StringComparison.Ordinal) => ImportProblemKind.ChangedDuringRead,
            "IOException" or "UnauthorizedAccessException" or "FileNotFoundException" or "DirectoryNotFoundException"
                or "PathTooLongException" => ImportProblemKind.Unreadable,
            "XmlException" or "InvalidDataException" or "EndOfStreamException" or "DecoderFallbackException"
                => ImportProblemKind.Damaged,
            _ => ImportProblemKind.Unexpected,
        };
    }

    /// <summary>Failed reads are retried by every scan; a save without an identity is not, until the reader changes.</summary>
    public static bool RetriedOnRescan(ImportProblemKind kind) => kind != ImportProblemKind.MissingIdentity;
}
