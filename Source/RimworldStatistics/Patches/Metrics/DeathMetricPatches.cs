using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.Patches.Metrics;

/// <summary>
/// A confirmed death (evidence #health-death): the prefix reads the victim's state from before the death (side, hostility,
/// age, caravan, designations, killer), the postfix confirms a living pawn is now dead. Re-entrant calls of the same pawn
/// count once (the prefix of an already dead pawn records nothing).
/// </summary>
[MetricAdapter(HealthDeathFamily.Name, LifetimeFamily.Name, AnimalsFamily.Name, RaidFamily.Name, MembershipFamily.Name)]
[HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
public static class PawnKillMetricPatch
{
    [ThreadStatic]
    private static Dictionary<Pawn, RaidDeathScope>? activeRaidDeaths;

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
        try
        {
            if (__instance == null || __instance.Dead || activeRaidDeaths?.ContainsKey(__instance) == true
                || !GameFacts.AnyRecording(HealthDeathFamily.Name, LifetimeFamily.Name, AnimalsFamily.Name, RaidFamily.Name, MembershipFamily.Name))
                return;
            var p = __instance;
            var instigator = dinfo?.Instigator;
            var raid = LiveMetrics.Family(RaidFamily.Name);
            var map = p.MapHeld;
            __state = new DeathFacts
            {
                PawnId = GameFacts.Id(p),
                FreeColonist = p.IsFreeColonist,
                ColonyAnimal = GameFacts.IsColonyAnimal(p),
                Wild = p.RaceProps.Animal && p.Faction == null,
                HostileToPlayer = Faction.OfPlayer != null && p.HostileTo(Faction.OfPlayer),
                VictimKind = GameFacts.VictimKind(p),
                Species = p.IsFreeColonist ? GameFacts.Species(p) : null,
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
            LiveMetrics.Fail(HealthDeathFamily.Name, e);
            __state = null;
        }
    }

    public static void Postfix(Pawn __instance, DeathFacts? __state)
    {
        if (__state is not { } d || __instance == null || !__instance.Dead)
            return;
        var tick = GameFacts.Tick;
        GameFacts.Record(HealthDeathFamily.Name, s => HealthDeathFamily.Died(s, d, tick));
        GameFacts.Record(LifetimeFamily.Name, s => LifetimeFamily.Died(s, d.PawnId, d.FreeColonist, d.AgeYears, tick));
        GameFacts.Record(AnimalsFamily.Name, s => AnimalsFamily.Died(s, d));
        GameFacts.Record(MembershipFamily.Name, s => MembershipFamily.KidnapLost(s, d.PawnId));
    }

    public static Exception? Finalizer(Pawn __instance, DeathFacts? __state, Exception? __exception)
    {
        if (__state != null && activeRaidDeaths is not null && activeRaidDeaths.Remove(__instance, out var scope))
            GameFacts.Record(RaidFamily.Name, s => scope.Complete(s, __instance.Dead));
        return __exception;
    }
}
