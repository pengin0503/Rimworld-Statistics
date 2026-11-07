namespace RimworldStatistics.Core;

/// <summary>Phase C · down (ledger B-002=B-014, B-013, N4-C-011, N4-P-009; evidence #down). One call per not-downed → downed transition.</summary>
public static class DownFamily
{
    public const string Name = "down";
    public const string ColonistsDowned = "combat.colonists_downed";
    public const string EnemiesDowned = "combat.enemies_downed";
    public const string ToxicDowns = "combat.downed.toxic";
    public const string SocialFightDowns = "combat.downed.social_fight";

    public static LiveFamily Family { get; } = new() { Name = Name, Revision = 1, Build = Build };

    private const string Colonists = "colonists";
    private const string Enemies = "enemies";
    private const string Toxic = "toxic";
    private const string Fight = "social_fight";

    public static void Downed(FamilyState s, DownFacts d)
    {
        if (d.FreeColonist)
        {
            s.Count(Colonists);
            if (d.Toxic)
                s.Count(Toxic);
            if (d.SocialFight)
                s.Count(Fight);
        }
        else if (d.HostileToPlayer)
        {
            s.Count(Enemies);
        }
    }

    private static void Build(FamilyOutputContext c)
    {
        c.PutCount(Colonists, ColonistsDowned);
        c.PutCount(Enemies, EnemiesDowned);
        c.PutCount(Toxic, ToxicDowns);
        c.PutCount(Fight, SocialFightDowns);
    }
}
