using RimWorld;
using RimWorld.Planet;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.Patches.Metrics;

/// <summary>Reads the plain facts the Core families decide from. Game-thread only; never keeps a game object.</summary>
internal static class GameFacts
{
    public static int Tick => Find.TickManager.TicksGame;

    public static string Id(Thing t) => t.GetUniqueLoadID();

    /// <summary>A member of the colony (membership family): a free, non-slave colonist.</summary>
    public static bool IsMember(Pawn p) => p.IsFreeNonSlaveColonist;

    public static bool IsPlayer(Thing? t) => t?.Faction != null && t.Faction == Faction.OfPlayer;

    public static bool IsColonyAnimal(Pawn p) => p.RaceProps.Animal && p.Faction == Faction.OfPlayer;

    public static string MapId(Map? map) => map == null ? "?" : "Map_" + map.uniqueID;

    public static string VictimKind(Pawn p)
        => p.RaceProps.Humanlike ? DeathFacts.Humanlike : p.RaceProps.Insect ? DeathFacts.Insect : p.RaceProps.IsMechanoid ? DeathFacts.Mechanoid : DeathFacts.Other;

    public static KillerSide Side(Thing? instigator)
    {
        if (instigator == null)
            return KillerSide.Unknown;
        if (!IsPlayer(instigator))
            return KillerSide.NotPlayer;
        return instigator switch
        {
            Pawn p when p.RaceProps.Animal => KillerSide.PlayerAnimal,
            Pawn p when p.RaceProps.IsMechanoid => KillerSide.PlayerMech,
            Pawn => KillerSide.PlayerPawn,
            Building_Turret => KillerSide.PlayerTurret,
            Building_Trap => KillerSide.PlayerTrap,
            _ => KillerSide.PlayerOther,
        };
    }

    /// <summary>Exclusive, in the order explosive → fire → weapon (ranged / melee) → unarmed body attack → unknown.</summary>
    public static KillMethod Method(DamageInfo? dinfo)
    {
        if (dinfo is not { } d || d.Def == null)
            return KillMethod.Unknown;
        if (d.Def.isExplosive)
            return KillMethod.Explosive;
        if (d.Def == DamageDefOf.Flame || d.Def == DamageDefOf.Burn)
            return KillMethod.Environment;
        if (d.Weapon is { } w)
            return w.IsRangedWeapon ? KillMethod.Ranged : w.IsMeleeWeapon ? KillMethod.Melee : KillMethod.Unknown;
        return d.Instigator is Pawn && d.WeaponBodyPartGroup != null ? KillMethod.Unarmed : KillMethod.Unknown;
    }

    public static Dictionary<string, int> Skills(Pawn p)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        if (p.skills?.skills is { } skills)
            foreach (var s in skills)
                if (s?.def != null && !s.TotallyDisabled)
                    result[s.def.defName] = s.levelInt;
        return result;
    }

    /// <summary>Runs one family's handler, closing the unit (not the game) if it throws.</summary>
    public static void Record(string family, Action<FamilyState> handler)
    {
        if (LiveMetrics.Family(family) is not { } s)
            return;
        try
        {
            handler(s);
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(family, e);
        }
    }

    public static bool AnyRecording(params string[] families)
    {
        foreach (var f in families)
            if (LiveMetrics.IsRecordingNow(f))
                return true;
        return false;
    }

    public static bool InPlayerCaravan(Pawn p) => p.GetCaravan() is { IsPlayerControlled: true };
}
