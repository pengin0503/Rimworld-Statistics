using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;
using Verse.AI;

namespace RimworldStatistics.Patches.Metrics;

/// <summary>
/// A confirmed downing (evidence #down): Pawn_HealthTracker.MakeDowned is private; the prefix reads the state before, the
/// postfix confirms not downed → downed and alive. A death without a downing never reaches it.
/// </summary>
[MetricAdapter(DownFamily.Name, RaidFamily.Name)]
[HarmonyPatch(typeof(Pawn_HealthTracker), "MakeDowned")]
public static class MakeDownedMetricPatch
{
    /// <summary>MentalStateDef of 1.7.4985 Core.</summary>
    private const string SocialFighting = "SocialFighting";

    /// <summary>HediffDef of 1.7.4985 Core (toxic fallout and Biotech pollution both build it up).</summary>
    private const string ToxicBuildup = "ToxicBuildup";

    public static void Prefix(Pawn ___pawn, DamageInfo? dinfo, Hediff hediff, out DownFacts? __state)
    {
        __state = null;
        if (___pawn == null || ___pawn.Downed || ___pawn.Dead)
            return;
        bool freeColonist;
        bool hostile;
        try
        {
            freeColonist = ___pawn.IsFreeColonist;
            hostile = ___pawn.HostileTo(GameFacts.PlayerFaction);
        }
        catch (Exception e)
        {
            GameFacts.FailAll(e, DownFamily.Name, RaidFamily.Name);
            return;
        }
        if ((!freeColonist && !hostile) || (!UnityData.IsInMainThread && !___pawn.Spawned && !freeColonist))
            return;
        if (!GameFacts.AnyRecording(DownFamily.Name, RaidFamily.Name))
            return;
        try
        {
            var attacker = dinfo?.Instigator as Pawn;
            __state = new DownFacts
            {
                FreeColonist = freeColonist,
                HostileToPlayer = hostile,
                Toxic = hediff?.def?.defName == ToxicBuildup,
                SocialFight = ___pawn.MentalStateDef?.defName == SocialFighting && attacker?.MentalStateDef?.defName == SocialFighting
                    && (attacker.MentalState as MentalState_SocialFighting)?.otherPawn == ___pawn,
            };
        }
        catch (Exception e)
        {
            GameFacts.FailAll(e, DownFamily.Name, RaidFamily.Name);
        }
    }

    public static void Postfix(Pawn ___pawn, DownFacts? __state)
    {
        if (__state is not { } d || ___pawn == null || !___pawn.Downed || ___pawn.Dead)
            return;
        GameFacts.Record(DownFamily.Name, s => DownFamily.Downed(s, d));
        if (d.FreeColonist)
        {
            try
            {
                var map = GameFacts.MapId(___pawn.MapHeld);
                GameFacts.Record(RaidFamily.Name, s => RaidFamily.ColonistDowned(s, map));
            }
            catch (Exception e) { LiveMetrics.Fail(RaidFamily.Name, e); }
        }
    }
}
