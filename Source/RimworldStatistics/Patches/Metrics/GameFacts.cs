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

    /// <summary>
    /// The player's faction, or null while there is none. World generation makes the other factions and their leaders
    /// (with injuries that are tended, records, relations, titles) before the scenario adds the player faction, and the
    /// hooks run then too. <c>Faction.OfPlayer</c> logs "Could not find player faction." in that case, so it is not used.
    /// </summary>
    public static Faction? PlayerFaction => Faction.OfPlayerSilentFail;

    /// <summary>Whether a faction is the player's; a missing faction (null) never is, even while there is no player faction.</summary>
    public static bool IsPlayerFaction(Faction? faction) => faction is not null && faction == Faction.OfPlayerSilentFail;

    public static bool IsPlayer(Thing? t) => IsPlayerFaction(t?.Faction);

    public static bool IsColonyAnimal(Pawn p) => p.RaceProps.Animal && IsPlayerFaction(p.Faction);

    // HAR races and vanilla humans both expose their owning race through Pawn.def.
    // Xenotypes use a separate game identity and are deliberately not part of this key.
    public static string? Species(Pawn pawn)
        => pawn.RaceProps.Humanlike
            ? OptionalTransitionRecording.QualifiedDefKey(pawn.def.modContentPack?.PackageId, pawn.def.defName) : null;

    // A named custom xenotype has no stable package/Def identity. Do not
    // attribute it to the fallback Baseliner Def or use a player-editable label.
    public static string? Xenotype(Pawn pawn)
        => ModsConfig.BiotechActive && pawn.genes is { xenotypeName: null or "" } genes && genes.Xenotype is { } def
            ? OptionalTransitionRecording.QualifiedDefKey(def.modContentPack?.PackageId, def.defName) : null;

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
            if (LiveMetrics.CanCaptureEvent(f))
                return true;
        return false;
    }

    public static bool InPlayerCaravan(Pawn p) => p.GetCaravan() is { IsPlayerControlled: true };
}
