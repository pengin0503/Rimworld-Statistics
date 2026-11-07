namespace RimworldStatistics.Core;

/// <summary>Ideology changes (D2): a successful change of an existing ideology on an eligible player-side pawn.</summary>
public static class IdeologyFamily
{
    public const string Name = "ideology";
    public const string Conversions = "ideology.conversions";
    private const string Changes = "conversions";

    public static LiveFamily Family { get; } = new()
    {
        Name = Name, Revision = 1, RequiredDlc = AcquisitionUnits.Ideology, Build = Build,
    };

    /// <summary>Pawn generation (unknown old ideology), same-ideo assignment, and NPC-only transitions do not count.</summary>
    public static void Converted(FamilyState s, string pawnId, string? from, string? to,
        bool eligible, bool isWorldSpawnedOrPlayerCaravan)
    {
        if (!eligible || !isWorldSpawnedOrPlayerCaravan || string.IsNullOrEmpty(pawnId)
            || string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) || from == to)
            return;
        s.Count(Changes);
    }

    private static void Build(FamilyOutputContext c)
        => c.PutCount(Changes, Conversions);
}
