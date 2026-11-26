using System.Globalization;

namespace RimworldStatistics.Core;

/// <summary>
/// Stable, player-facing identifiers for the import problem categories (Q21). The numeric value is the suffix of the
/// displayed code (<see cref="ImportErrorCodes.Text"/>), so a code never changes once shipped.
/// </summary>
public enum ImportErrorCode
{
    /// <summary>The save was read; there is no problem to report.</summary>
    None = 0,

    /// <summary>The game replaced the file while the importer was reading it; the next scan reads it again.</summary>
    ChangedDuringRead = 1001,

    /// <summary>The file could not be opened (in use, permissions).</summary>
    Unreadable = 1002,

    /// <summary>The file is damaged or cut short (invalid XML/compressed data, format or overflow errors).</summary>
    Damaged = 1003,

    /// <summary>The save's own tick values are outside the sane range (a specific damaged-file reason).</summary>
    TickAnomaly = 1004,

    /// <summary>The file is not a RimWorld save.</summary>
    NotASavegame = 1005,

    /// <summary>The save was read but holds no colony identity this MOD can match.</summary>
    MissingIdentity = 1006,

    /// <summary>Any other, unrecognised read failure.</summary>
    Unexpected = 1007,
}

/// <summary>Maps free-text import reasons (kept unchanged for compatibility) to a stable <see cref="ImportErrorCode"/>.</summary>
public static class ImportErrorCodes
{
    /// <summary>The player-facing code, e.g. <c>RWS-E1001</c>; empty for <see cref="ImportErrorCode.None"/>.</summary>
    public static string Text(this ImportErrorCode code)
        => code == ImportErrorCode.None ? "" : "RWS-E" + ((int)code).ToString("D4", CultureInfo.InvariantCulture);

    /// <summary>The one code for a classified problem category.</summary>
    public static ImportErrorCode CodeFor(ImportProblemKind kind) => kind switch
    {
        ImportProblemKind.ChangedDuringRead => ImportErrorCode.ChangedDuringRead,
        ImportProblemKind.Unreadable => ImportErrorCode.Unreadable,
        ImportProblemKind.Damaged => ImportErrorCode.Damaged,
        ImportProblemKind.NotASave => ImportErrorCode.NotASavegame,
        ImportProblemKind.MissingIdentity => ImportErrorCode.MissingIdentity,
        ImportProblemKind.Unexpected => ImportErrorCode.Unexpected,
        _ => ImportErrorCode.Unexpected,
    };

    /// <summary>
    /// The code for one observation. A tick anomaly is a damaged file for grouping purposes but keeps its own code, so a
    /// report distinguishes it from XML/compression damage; every other reason is mapped through
    /// <see cref="ImportProblems.Classify"/> and an unrecognised one becomes the generic code.
    /// </summary>
    public static ImportErrorCode CodeFor(SaveObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (ImportProblems.Classify(observation) is not { } kind)
            return ImportErrorCode.None;
        return kind == ImportProblemKind.Damaged && ReasonType(observation.ErrorReason) == "TickAnomaly"
            ? ImportErrorCode.TickAnomaly
            : CodeFor(kind);
    }

    private static string ReasonType(string? reason)
    {
        var text = reason ?? "";
        return text.IndexOf(':') is var colon and > 0 ? text[..colon] : text;
    }
}
