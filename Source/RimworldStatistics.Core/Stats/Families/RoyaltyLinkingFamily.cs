namespace RimworldStatistics.Core;

/// <summary>
/// Phase D2 Royalty Anima tree linking completions (<c>CompPsylinkable.FinishLinkingRitual</c>).
/// Separated into its own acquisition unit so existing <see cref="RoyaltyFamily"/> checkpoints remain revision-compatible.
/// </summary>
public static class RoyaltyLinkingFamily
{
    public const string Name = "royalty_linking";

    public const string AnimaTreeLinks = "royalty.anima_links";

    private const string Links = "anima_links";

    public static LiveFamily Family { get; } = new()
    {
        Name = Name,
        Revision = 1,
        RequiredDlc = AcquisitionUnits.Royalty,
        Build = Build,
    };

    public static void AnimaLinkingCompleted(FamilyState s, bool colonyPawn, int beforePsylinkLevel, int afterPsylinkLevel)
    {
        if (!colonyPawn || afterPsylinkLevel <= beforePsylinkLevel)
            return;
        s.Count(Links);
    }

    private static void Build(FamilyOutputContext c)
    {
        c.PutCount(Links, AnimaTreeLinks);
    }
}
