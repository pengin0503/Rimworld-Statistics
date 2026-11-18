using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.Patches.Metrics;

/// <summary>
/// A confirmed death (evidence #health-death): the prefix reads the victim's state from before the death (side, hostility,
/// age, caravan, designations, killer), the postfix confirms a living pawn is now dead. Re-entrant calls of the same pawn
/// count once (only the outermost call reads the death facts).
/// </summary>
[MetricAdapter(HealthDeathFamily.Name, LifetimeFamily.Name, AnimalsFamily.Name, RaidFamily.Name, MembershipFamily.Name)]
[HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
public static class PawnKillMetricPatch
{
    [ThreadStatic]
    private static Dictionary<Pawn, RaidDeathScope>? activeRaidDeaths;

    [ThreadStatic]
    private static HashSet<Pawn>? activeDeaths;

    internal static bool TryDeferRaidLoss(Pawn pawn, int tick)
    {
        if (activeRaidDeaths is null || !activeRaidDeaths.TryGetValue(pawn, out var scope))
            return false;
        scope.DeferKilledLoss(tick);
        return true;
    }

    public static void Prefix(Pawn __instance, DamageInfo? dinfo, Hediff exactCulprit, out DeathFacts? __state)
    {
        __state = null;
        if (__instance == null || __instance.Dead)
            return;
        if (!UnityData.IsInMainThread && !__instance.Spawned
            && !GameFacts.IsPlayerFaction(__instance.Faction) && !GameFacts.InPlayerCaravan(__instance))
            return;
        if (!GameFacts.AnyRecording(HealthDeathFamily.Name, LifetimeFamily.Name, AnimalsFamily.Name, RaidFamily.Name, MembershipFamily.Name)
            || !(activeDeaths ??= new(ReferenceEqualityComparer.Instance)).Add(__instance))
            return;
        try
        {
            var p = __instance;
            var instigator = dinfo?.Instigator;
            var raid = LiveMetrics.Family(RaidFamily.Name);
            var map = p.MapHeld;
            var freeColonist = GameFacts.IsFreeColonistSafe(p);
            __state = new DeathFacts
            {
                PawnId = GameFacts.Id(p),
                FreeColonist = freeColonist,
                ColonyAnimal = GameFacts.IsColonyAnimal(p),
                Wild = p.RaceProps.Animal && p.Faction == null,
                HostileToPlayer = GameFacts.PlayerFaction is { } pf && p.HostileTo(pf),
                VictimKind = GameFacts.VictimKind(p),
                Species = freeColonist ? GameFacts.Species(p) : null,
                Xenotype = freeColonist ? GameFacts.Xenotype(p) : null,
                AgeYears = p.ageTracker?.AgeBiologicalYearsFloat,
                InPlayerCaravan = GameFacts.InPlayerCaravan(p),
                Cause = exactCulprit?.def != null ? "hediff:" + exactCulprit.def.defName : dinfo?.Def != null ? "damage:" + dinfo.Value.Def.defName : null,
                Disease = exactCulprit?.def?.makesSickThought == true,
                Killer = GameFacts.Side(instigator),
                KillerPawnId = instigator is Pawn ip ? GameFacts.Id(ip) : null,
                Method = GameFacts.Method(dinfo),
                KillerIsRaider = raid == null ? null : instigator is Pawn rp && RaidFamily.IsMember(raid, GameFacts.Id(rp)),
                HuntDesignated = map?.designationManager?.DesignationOn(p, DesignationDefOf.Hunt) != null,
                SlaughterDesignated = map != null && p.ShouldBeSlaughtered(),
                ExecutionCut = dinfo?.Def == DamageDefOf.ExecutionCut,
            };
            if (raid != null && RaidFamily.IsMember(raid, __state.PawnId))
            {
                var playerKill = __state.HostileToPlayer && __state.Killer is not (KillerSide.Unknown or KillerSide.NotPlayer);
                (activeRaidDeaths ??= new(ReferenceEqualityComparer.Instance)).Add(p, new RaidDeathScope(__state.PawnId, playerKill));
            }
        }
        catch (Exception e)
        {
            activeDeaths.Remove(__instance);
            GameFacts.FailAll(e, HealthDeathFamily.Name, LifetimeFamily.Name, AnimalsFamily.Name, RaidFamily.Name, MembershipFamily.Name);
            __state = null;
        }
    }

    public static void Postfix(Pawn __instance, DeathFacts? __state, bool __runOriginal)
    {
        if (!__runOriginal || __state is not { } d || __instance == null || !__instance.Dead)
            return;
        int tick;
        try { tick = GameFacts.Tick; }
        catch (Exception e)
        {
            GameFacts.FailAll(e, HealthDeathFamily.Name, LifetimeFamily.Name, AnimalsFamily.Name, MembershipFamily.Name);
            return;
        }
        GameFacts.Record(HealthDeathFamily.Name, s => HealthDeathFamily.Died(s, d, tick));
        GameFacts.Record(LifetimeFamily.Name, s => LifetimeFamily.Died(s, d.PawnId, d.FreeColonist, d.AgeYears, tick));
        GameFacts.Record(AnimalsFamily.Name, s => AnimalsFamily.Died(s, d));
        GameFacts.Record(MembershipFamily.Name, s => MembershipFamily.KidnapLost(s, d.PawnId));
    }

    public static Exception? Finalizer(Pawn __instance, DeathFacts? __state, Exception? __exception)
    {
        if (__state is null)
            return __exception;
        activeDeaths?.Remove(__instance);
        if (activeRaidDeaths is not null && activeRaidDeaths.Remove(__instance, out var scope))
            GameFacts.Record(RaidFamily.Name, s => scope.Complete(s, __instance.Dead));
        return __exception;
    }
}
