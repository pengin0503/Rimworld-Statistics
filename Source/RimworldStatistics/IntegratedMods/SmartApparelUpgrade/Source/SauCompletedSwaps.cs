using System.Collections;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Api;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.SmartApparelAddon;

internal static class SauCompletedSwaps
{
    private const string Unit = "mod.pengin0503.smartapparelupgrade.swaps";
    private const string Metric = Unit + ".completed";
    private static SauOrderHook.Binding? active;

    internal static void TryRegister(SauOrderHook.Binding binding)
    {
        if (!StatisticsApi.TryGetHost(1, 1, out var host) || host is null)
            return;
        var wear = AccessTools.DeclaredMethod(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Wear),
            [typeof(Apparel), typeof(bool), typeof(bool)]);
        if (wear is null || wear.IsStatic || wear.ReturnType != typeof(void))
            return;

        var definition = new ApiMetricDefinition
        {
            ProviderId = "rws.addon.sau.swaps",
            OwnerPackageId = "pengin0503.smartapparelupgrade",
            FamilyId = "swaps",
            MetricName = "completed",
            ProviderVersion = "1.0.0",
            MinimumOwnerVersion = "1.0.1",
            MaximumOwnerVersionExclusive = "1.0.2",
            Revision = 1,
            Kind = ApiMetricKind.Event,
            EventMode = ApiEventMode.Count,
            Category = "Work",
            Unit = "Count",
            Overall = "Sum",
            Selection = "Representative",
            LabelKey = "RWS_SAU_CompletedSwaps",
            DescriptionKey = "RWS_SAU_CompletedSwapsDesc",
        };
        if (host.Register(definition) != ApiRegistrationResult.Accepted)
            return;

        ExternalSdkHost.Instance.IntegratedHooks.Defer(Unit, () =>
        {
            var harmony = new Harmony("pengin0503.rws.smartapparel.swaps");
            try
            {
                harmony.Patch(wear,
                    prefix: new HarmonyMethod(typeof(SauCompletedSwaps), nameof(BeforeWear)),
                    postfix: new HarmonyMethod(typeof(SauCompletedSwaps), nameof(AfterWear)));
                harmony.Patch(binding.Target,
                    prefix: new HarmonyMethod(typeof(SauCompletedSwaps), nameof(BeforeOrder)),
                    postfix: new HarmonyMethod(typeof(SauCompletedSwaps), nameof(AfterOrder)));
                active = binding;
                return true;
            }
            catch
            {
                harmony.Unpatch(wear, HarmonyPatchType.All, harmony.Id);
                harmony.Unpatch(binding.Target, HarmonyPatchType.All, harmony.Id);
                return false;
            }
        });
    }

    private static void BeforeOrder(object __instance, out HashSet<object>? __state)
    {
        __state = null;
        if (active is not { } binding || !LiveMetrics.IsRecordingNow(Unit))
            return;
        try
        {
            int tick = Find.TickManager.TicksGame;
            foreach (var stamp in CurrentStamps(binding, __instance, tick))
                (__state ??= new HashSet<object>(ReferenceEqualityComparer.Instance)).Add(stamp);
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterOrder(object __instance, bool __runOriginal, HashSet<object>? __state)
    {
        if (!__runOriginal || active is not { } binding || __instance is not MapComponent scanner ||
            !LiveMetrics.IsRecordingNow(Unit))
            return;
        try
        {
            int tick = Find.TickManager.TicksGame;
            foreach (var stamp in CurrentStamps(binding, scanner, tick))
            {
                if (__state?.Contains(stamp) == true ||
                    binding.PawnId.GetValue(stamp) is not string pawnId ||
                    binding.ApparelId.GetValue(stamp) is not string apparelId)
                    continue;
                foreach (var pawn in scanner.map.mapPawns.FreeColonistsSpawned)
                {
                    if (pawn.ThingID != pawnId || pawn.CurJob is not { } job || job.def != JobDefOf.Wear ||
                        job.targetA.Thing is not Apparel apparel || apparel.ThingID != apparelId)
                        continue;
                    var ticket = SauSwapTicket.Create(pawnId, apparelId, job.loadID, job.startTick, job.playerForced);
                    if (LiveMetrics.Family(Unit) is { } family && ticket?.Remember(family, tick) != true)
                        LiveMetrics.Accumulator?.MarkGap(Unit, tick);
                    break;
                }
            }
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static IEnumerable<object> CurrentStamps(SauOrderHook.Binding binding, object scanner, int tick)
    {
        if (binding.Stamps.GetValue(scanner) is not IList stamps)
            throw new InvalidOperationException("Smart Apparel Upgrade stamp list is unavailable");
        foreach (var stamp in stamps)
            if (stamp is not null && (int?)binding.IssuedTick.GetValue(stamp) == tick)
                yield return stamp;
    }

    private static void BeforeWear(Pawn_ApparelTracker __instance, Apparel __0, out SauSwapTicket? __state)
    {
        __state = null;
        if (active is null || !LiveMetrics.IsRecordingNow(Unit))
            return;
        try
        {
            var pawn = __instance.pawn;
            if (pawn?.CurJob is not { } job || job.def != JobDefOf.Wear || job.playerForced ||
                pawn.jobs.curDriver is not JobDriver_Wear || !ReferenceEquals(job.targetA.Thing, __0) ||
                __0 is null || __instance.WornApparel.Contains(__0) ||
                LiveMetrics.Accumulator?.Family(Unit) is not { } family)
                return;
            var ticket = SauSwapTicket.Create(pawn.ThingID, __0.ThingID, job.loadID, job.startTick, job.playerForced);
            if (ticket?.Pending(family) == true)
                __state = ticket;
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterWear(Pawn_ApparelTracker __instance, Apparel __0, bool __runOriginal, SauSwapTicket? __state)
    {
        if (!__runOriginal || __state is not { } ticket || !LiveMetrics.IsRecordingNow(Unit))
            return;
        try
        {
            if (!__instance.WornApparel.Contains(__0) || !StatisticsApi.TryGetHost(1, 1, out var host) || host is null ||
                LiveMetrics.Accumulator?.Family(Unit) is not { } family)
                return;
            if (ticket.Complete(family, newlyWorn: true) is { } id)
                _ = host.RecordEvent(Metric, id);
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }
}
