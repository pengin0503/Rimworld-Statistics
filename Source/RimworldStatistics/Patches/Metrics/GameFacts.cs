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

    /// <summary>A member of the colony (membership family): a free, non-slave colonist, guarded by silent player-faction check.</summary>
    public static bool IsMember([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] Pawn? p) => p is not null && IsPlayerFaction(p.Faction) && p.IsFreeNonSlaveColonist;

    /// <summary>Free colonist check guarded by <see cref="IsPlayerFaction"/> so <c>Faction.OfPlayer</c> is never read before player faction exists.</summary>
    public static bool IsFreeColonistSafe([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] Pawn? p) => p is not null && IsPlayerFaction(p.Faction) && p.IsFreeColonist;

    /// <summary>Colony prisoner check guarded by <see cref="IsPlayerFaction"/> on <c>guest.HostFaction</c> so <c>Faction.OfPlayer</c> is never read when absent.</summary>
    public static bool IsPrisonerOfColonySafe([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] Pawn? p)
        => p is not null && p.guest is { IsPrisoner: true } && IsPlayerFaction(p.guest.HostFaction);

    /// <summary>
    /// Whether a pawn performs labor on behalf of the player's colony: player-faction non-prisoners
    /// (free colonists, colony slaves, colony mechanoids/animals) plus colony-custody prisoners
    /// (<c>guest.IsPrisoner</c> with player <c>HostFaction</c>), while excluding player pawns held
    /// prisoner by another faction and never touching <c>Faction.OfPlayer</c>.
    /// </summary>
    public static bool IsColonyWorker([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] Pawn? p)
        => p is not null && ColonyWorkAttribution.IsColonyWorker(
            isPlayerFaction: IsPlayerFaction(p.Faction),
            isPrisoner: p.guest is { IsPrisoner: true },
            isHostPlayerFaction: IsPlayerFaction(p.guest?.HostFaction));

    /// <summary>Colony slave check guarded by <see cref="IsPlayerFaction"/> on <c>Faction</c> so <c>Faction.OfPlayer</c> is never read when absent.</summary>
    public static bool IsSlaveOfColonySafe([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] Pawn? p) => p is not null && IsPlayerFaction(p.Faction) && p.IsSlaveOfColony;

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

    public static string? AnimalSpecies(Pawn? pawn)
        => pawn is not null && pawn.RaceProps.Animal
            ? OptionalTransitionRecording.QualifiedDefKey(pawn.def?.modContentPack?.PackageId, pawn.def?.defName) : null;

    // HAR races and vanilla humans both expose their owning race through Pawn.def.
    // Xenotypes use a separate game identity and are deliberately not part of this key.
    public static string? Species(Pawn pawn)
        => pawn.RaceProps.Humanlike
            ? OptionalTransitionRecording.QualifiedDefKey(pawn.def.modContentPack?.PackageId, pawn.def.defName) : null;

    // Named custom and hybrid xenotypes map to the reserved CustomXenotypeBucket so
    // player-editable names are not stored and valid custom pawns do not force Partial coverage.
    public static string? Xenotype(Pawn pawn)
        => pawn.genes is { } genes
            ? XenotypeRules.BreakdownKey(
                ModsConfig.BiotechActive,
                hasGeneTracker: true,
                genes.xenotypeName,
                genes.hybrid,
                genes.Xenotype?.modContentPack?.PackageId,
                genes.Xenotype?.defName)
            : null;

    public static string MapId(Map? map) => map == null ? "?" : "Map_" + map.uniqueID;

    /// <summary>
    /// Whether <see cref="IncidentWorker.TryExecute"/> will return true without running the incident: it needs colonists
    /// (<c>requireColonistsPresent</c>) and none is spawned on the target map (1.7.4985 and 1.7.5011). Such a call is not an
    /// incident that happened (#352). Read it before the call, since the worker itself can add colonists.
    /// </summary>
    public static bool SkippedForNoColonists(IncidentWorker? worker, IncidentParms? parms)
        => worker?.def is { requireColonistsPresent: true } && parms?.target is Map map && map.mapPawns.FreeColonistsSpawnedCount == 0;

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

    public static void FailAll(Exception e, params string[] families)
    {
        foreach (var f in families)
            LiveMetrics.Fail(f, e);
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
