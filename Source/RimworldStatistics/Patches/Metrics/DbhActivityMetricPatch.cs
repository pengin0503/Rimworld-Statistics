using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;
using Verse.AI;

namespace RimworldStatistics.Patches.Metrics;

[MetricAdapter(DbhStatistics.ActivitiesUnit)]
[HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob),
    new[] { typeof(JobCondition), typeof(bool), typeof(bool) })]
internal static class DbhActivityMetricPatch
{
    private static readonly Dictionary<string, string> AuditedJobs = new(StringComparer.Ordinal)
    {
        ["UseToilet"] = "DubsBadHygiene.JobDriver_UseToilet",
        ["takeShower"] = "DubsBadHygiene.JobDriver_takeShower",
        ["washHands"] = "DubsBadHygiene.JobDriver_washHands",
        ["useWashBucket"] = "DubsBadHygiene.JobDriver_useWashBucket",
        ["washAtCell"] = "DubsBadHygiene.JobDriver_washAtCell",
    };
    private static readonly HashSet<Guid> AuditedModules =
    [
        new("85b03536-f71c-4780-8aaa-3c790e11de9a"), // 3.2.3258, R2
        new("7f4b7428-565f-4495-86c7-9c01a1dac461"), // 3.2.3260, CI
    ];
    private static HashSet<JobDef>? targets;

    internal static bool Available => (targets ??= Bind()).Count != 0;

    private static HashSet<JobDef> Bind()
    {
        var found = new HashSet<JobDef>(ReferenceEqualityComparer.Instance);
        foreach (var (defName, driverName) in AuditedJobs)
        {
            var def = DefDatabase<JobDef>.GetNamedSilentFail(defName);
            if (def?.driverClass?.FullName == driverName &&
                DbhStatistics.IsDbh(def.modContentPack?.PackageId) &&
                AuditedModules.Contains(def.driverClass.Module.ModuleVersionId))
                found.Add(def);
        }
        // A missing/replaced definition would make the measured job set differ.
        if (found.Count != AuditedJobs.Count)
            found.Clear();
        return found;
    }

    private static void Prefix(Pawn_JobTracker __instance, JobCondition __0,
        out (Job? Job, string? DefName) __state)
    {
        __state = default;
        if (__0 != JobCondition.Succeeded || __instance.curJob is not { } job ||
            targets?.Contains(job.def) != true)
            return;
        if (__instance.curDriver?.pawn is { MapHeld.IsPlayerHome: true } pawn &&
            pawn.Faction == Faction.OfPlayer && LiveMetrics.CanCaptureEvent(DbhStatistics.ActivitiesUnit))
            __state = (job, job.def.defName);
    }

    private static void Postfix(Pawn_JobTracker __instance, bool __runOriginal,
        (Job? Job, string? DefName) __state)
    {
        if (!__runOriginal || __state.DefName is not { } name || ReferenceEquals(__instance.curJob, __state.Job))
            return;
        try
        {
            if (LiveMetrics.Family(DbhStatistics.ActivitiesUnit) is { } family)
                DbhStatistics.Ended(family, name, succeeded: true);
        }
        catch (Exception e) { LiveMetrics.Fail(DbhStatistics.ActivitiesUnit, e); }
    }
}
