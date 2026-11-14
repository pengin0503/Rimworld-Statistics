using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.Patches.Metrics;

/// <summary>
/// Membership transitions (evidence #membership): a member is a free, non-slave colonist. Pawn.SetFaction covers recruitment
/// (InteractionWorker_RecruitAttempt.DoRecruit and RecruitUtility.Recruit end there), joiners and departures; slave
/// emancipation keeps the faction and is observed at GenGuest.EmancipateSlave. Routes other than these are not recorded.
/// </summary>
[MetricAdapter(MembershipFamily.Name, LifetimeFamily.Name)]
[HarmonyPatch(typeof(Pawn), nameof(Pawn.SetFaction))]
public static class SetFactionMetricPatch
{
    public struct Before
    {
        public bool Member;
        public bool Prisoner;
        public bool Player;
    }

    public static void Prefix(Pawn __instance, out Before __state)
        => __state = __instance == null ? default : new Before
        {
            Member = GameFacts.IsMember(__instance), Prisoner = __instance.IsPrisonerOfColony, Player = __instance.Faction == Faction.OfPlayer,
        };

    public static void Postfix(Pawn __instance, Before __state)
    {
        if (__instance == null || !GameFacts.AnyRecording(MembershipFamily.Name, LifetimeFamily.Name))
            return;
        var id = GameFacts.Id(__instance);
        var tick = GameFacts.Tick;
        var member = GameFacts.IsMember(__instance);
        if (member && !__state.Member)
        {
            var route = __state.Prisoner ? MembershipFamily.RoutePrisoner : MembershipFamily.RouteOther;
            var age = (double?)__instance.ageTracker?.AgeBiologicalYearsFloat;
            var skills = GameFacts.Skills(__instance);
            GameFacts.Record(MembershipFamily.Name, s => MembershipFamily.Joined(s, id, route, age, tick, GameFacts.Species(__instance)));
            GameFacts.Record(LifetimeFamily.Name, s => LifetimeFamily.Joined(s, id, tick, skills));
        }
        else if (!member && __state.Member)
        {
            GameFacts.Record(LifetimeFamily.Name, s => LifetimeFamily.Left(s, id, tick));
        }
        if (__state.Player && __instance.Faction != Faction.OfPlayer)
            GameFacts.Record(MembershipFamily.Name, s => MembershipFamily.KidnapLost(s, id));   // e.g. a captive who joined the kidnappers
    }
}

[MetricAdapter(MembershipFamily.Name, LifetimeFamily.Name)]
[HarmonyPatch(typeof(GenGuest), nameof(GenGuest.EmancipateSlave))]
public static class EmancipateSlaveMetricPatch
{
    public static void Prefix(Pawn slave, out (bool Slave, bool Member) __state)
        => __state = slave == null ? default : (slave.IsSlaveOfColony, GameFacts.IsMember(slave));

    public static void Postfix(Pawn slave, (bool Slave, bool Member) __state)
    {
        if (slave == null || !__state.Slave || slave.IsSlave)
            return;
        var id = GameFacts.Id(slave);
        var tick = GameFacts.Tick;
        var member = GameFacts.IsMember(slave);
        var age = (double?)slave.ageTracker?.AgeBiologicalYearsFloat;
        var skills = GameFacts.Skills(slave);
        GameFacts.Record(MembershipFamily.Name, s =>
        {
            MembershipFamily.SlaveFreed(s, id);
            if (member && !__state.Member)
                MembershipFamily.Joined(s, id, MembershipFamily.RouteSlave, age, tick, GameFacts.Species(slave));
        });
        if (member && !__state.Member)
            GameFacts.Record(LifetimeFamily.Name, s => LifetimeFamily.Joined(s, id, tick, skills));
    }
}

/// <summary>A human birth (evidence #membership): the returned thing is the baby, alive on the player's side, or a stillbirth.</summary>
/// <summary>
/// ApplyBirthOutcome returns the actual newly born Thing. The birtherThing parameter
/// (confirmed in RimWorld 1.7.4985 reference metadata) is the physical birther, not
/// necessarily the geneticMother. Only a living player baby proves a live birth.
/// </summary>
[MetricAdapter(MembershipFamily.Name, LifetimeFamily.Name, BiotechFamily.Name)]
[HarmonyPatch(typeof(PregnancyUtility), nameof(PregnancyUtility.ApplyBirthOutcome))]
public static class BirthMetricPatch
{
    public static void Postfix(Thing __result, Thing birtherThing)
    {
        if (__result is not Pawn baby || baby.Dead || baby.Faction != Faction.OfPlayer || !baby.RaceProps.Humanlike)
            return;
        var id = GameFacts.Id(baby);
        var tick = GameFacts.Tick;
        GameFacts.Record(MembershipFamily.Name, s => MembershipFamily.Born(s, id, tick));
        GameFacts.Record(LifetimeFamily.Name, s => LifetimeFamily.Joined(s, id, tick, GameFacts.Skills(baby)));
        // One unique birther across multiple live babies/twins; exclude artificial
        // incubators, outsiders and babies not attributed to a free colony member.
        if (birtherThing is Pawn mother)
            GameFacts.Record(BiotechFamily.Name, s => BiotechFamily.BirthConfirmed(
                s, GameFacts.Id(mother), mother.IsFreeNonSlaveColonist, livingBaby: true));
    }
}

[MetricAdapter(MembershipFamily.Name)]
[HarmonyPatch(typeof(KidnappedPawnsTracker), nameof(KidnappedPawnsTracker.Kidnap))]
public static class KidnapMetricPatch
{
    public static void Prefix(Pawn pawn, out bool __state) => __state = pawn != null && GameFacts.IsMember(pawn);

    public static void Postfix(Pawn pawn, bool __state, KidnappedPawnsTracker __instance)
    {
        if (!__state || pawn == null || !__instance.KidnappedPawnsListForReading.Contains(pawn))
            return;
        var id = GameFacts.Id(pawn);
        var tick = GameFacts.Tick;
        GameFacts.Record(MembershipFamily.Name, s => MembershipFamily.Kidnapped(s, id, tick));
    }
}

/// <summary>The only confirmed return of a kidnapped colonist in 1.7.4985: the ransom letter (ChoiceLetter_RansomDemand).</summary>
[MetricAdapter(MembershipFamily.Name)]
[HarmonyPatch(typeof(KidnappedPawnsTracker), nameof(KidnappedPawnsTracker.RemoveKidnappedPawn))]
public static class KidnapReturnMetricPatch
{
    public static void Postfix(Pawn pawn)
    {
        if (pawn == null || pawn.Dead || pawn.Faction != Faction.OfPlayer)
            return;
        var id = GameFacts.Id(pawn);
        var tick = GameFacts.Tick;
        GameFacts.Record(MembershipFamily.Name, s => MembershipFamily.KidnapReturned(s, id, tick));
    }
}

/// <summary>Capture (evidence #social, #membership): a hostile pawn becoming a prisoner of the colony.</summary>
[MetricAdapter(SocialFamily.Name)]
[HarmonyPatch(typeof(Pawn_GuestTracker), nameof(Pawn_GuestTracker.CapturedBy))]
public static class CapturedMetricPatch
{
    public static void Prefix(Pawn ___pawn, out (bool Hostile, bool Prisoner) __state)
        => __state = ___pawn == null ? default : (Faction.OfPlayer != null && ___pawn.HostileTo(Faction.OfPlayer), ___pawn.IsPrisonerOfColony);

    public static void Postfix(Pawn ___pawn, Faction by, (bool Hostile, bool Prisoner) __state)
    {
        if (___pawn == null || by != Faction.OfPlayer || __state.Prisoner || !___pawn.IsPrisonerOfColony)
            return;
        if (__state.Hostile)
            GameFacts.Record(SocialFamily.Name, SocialFamily.Captured);
    }
}

[MetricAdapter(SocialFamily.Name)]
[HarmonyPatch(typeof(GenGuest), nameof(GenGuest.PrisonerRelease))]
public static class PrisonerReleaseMetricPatch
{
    public static void Prefix(Pawn p, out bool __state) => __state = p != null && p.IsPrisonerOfColony;

    public static void Postfix(Pawn p, bool __state)
    {
        if (!__state || p == null || p.IsPrisonerOfColony)
            return;
        GameFacts.Record(SocialFamily.Name, SocialFamily.PrisonerReleased);
    }
}
