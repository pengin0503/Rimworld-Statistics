using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.NewModOutcomes;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.WinstonWave;

internal static class WinstonWaveOutcomes
{
    private const string Owner = "VanillaStorytellersExpanded.WinstonWave";
    private const string Assembly = "VSEWW";
    private const string WaveFamily = "winston_clears";
    private const string WaveUnit = "mod.vanillastorytellersexpanded.winstonwave." + WaveFamily;
    private const string RewardUnit = "mod.vanillastorytellersexpanded.winstonwave.winston_rewards";
    private static FieldInfo waveNumber = null!;
    private static FieldInfo wasSent = null!;
    private static FieldInfo pawnCount = null!;
    private static FieldInfo pawnsLeft = null!;
    private static readonly ConditionalWeakTable<object, ClearProof> VerifiedRaids = new();
    private static readonly ConditionalWeakTable<Window, object> RecordedWindows = new();
    private static readonly object Recorded = new();

    internal static IEnumerable<ModMetricAdapterRegistration> Build()
    {
        var cleared = WaveDescriptor("cleared", "RWS_WinstonCleared", "RWS_WinstonClearDesc", OverallAggregation.Sum);
        var highest = WaveDescriptor("highest", "RWS_WinstonHighest", "RWS_WinstonClearDesc", OverallAggregation.Max);
        var boss = WaveDescriptor("boss", "RWS_WinstonBoss", "RWS_WinstonClearDesc", OverallAggregation.Sum);
        var family = new LiveFamily
        {
            Name = WaveUnit, Revision = 1,
            Build = output =>
            {
                output.PutCount(WinstonWaveRules.Cleared, cleared.StableKey);
                output.PutExtreme(WinstonWaveRules.Cleared, highest.StableKey, max: true);
                output.PutCount(WinstonWaveRules.Boss, boss.StableKey);
                if (output.State.Peek(WinstonWaveRules.LastByMap)?.Overflow == true)
                    output.MarkPartial(cleared.StableKey, highest.StableKey, boss.StableKey);
            },
        };
        foreach (var descriptor in new[] { cleared, highest, boss })
            yield return new ModMetricAdapterRegistration(descriptor, family, InstallWaves);

        yield return OutcomeMetric.Count(Owner, "winston_rewards", "RWS_WinstonRewards", "RWS_WinstonRewardsDesc",
            MetricCategory.Combat, InstallRewards, breakdown: true);
    }

    private static ModMetricDescriptor WaveDescriptor(string metric, string label, string description, OverallAggregation aggregation)
        => new()
        {
            ProviderId = "rws." + WaveFamily, OwnerPackageId = Owner, FamilyId = WaveFamily, MetricName = metric,
            LabelKey = label, DescriptionKey = description, Overall = aggregation, Category = MetricCategory.Combat,
            Unit = MetricUnit.Count, Source = MetricObservationSource.Checkpoint, Revision = 1,
            RequiredPackages = [new ModPackageRequirement(Owner)],
        };

    private static FieldInfo RequireField(Type owner, string name, Type type)
        => AccessTools.DeclaredField(owner, name) is { } field && field.FieldType == type
            ? field : throw new InvalidOperationException($"Changed {owner.FullName}.{name} field");

    private static void InstallWaves(Harmony harmony)
    {
        var component = OutcomeMetric.Bind("VSEWW.MapComponent_Winston", Assembly);
        var raid = OutcomeMetric.Bind("VSEWW.NextRaidInfo", Assembly);
        _ = RequireField(component, "nextRaidInfo", raid);
        _ = RequireField(component, "currentWave", typeof(int));
        waveNumber = RequireField(raid, "waveNumber", typeof(int));
        wasSent = RequireField(raid, "sent", typeof(bool));
        pawnCount = RequireField(raid, "totalPawnsBefore", typeof(int));
        pawnsLeft = RequireField(raid, "totalPawnsLeft", typeof(int));
        var advance = OutcomeMetric.Method(component, "PrepareNextWave", typeof(void), typeof(bool));
        var raidOver = OutcomeMetric.Method(raid, "RaidOver", typeof(bool));
        OutcomeMetric.Patch(harmony, raidOver, typeof(WinstonWaveOutcomes), null, nameof(AfterRaidOver));
        try
        {
            OutcomeMetric.Patch(harmony, advance, typeof(WinstonWaveOutcomes), nameof(BeforeAdvance), nameof(AfterAdvance));
        }
        catch
        {
            harmony.Unpatch(raidOver, AccessTools.DeclaredMethod(typeof(WinstonWaveOutcomes), nameof(AfterRaidOver)));
            throw;
        }
    }

    private sealed record ClearProof(int Tick);
    private sealed record Advance(string Map, int Wave, bool Sent, int Dispatched, int Left);

    private static void AfterRaidOver(object __instance, bool __result, bool __runOriginal)
    {
        if (!OutcomeMetric.Observing(WaveUnit))
            return;
        if (__runOriginal && __result)
        {
            var tick = GameFacts.Tick;
            if (!VerifiedRaids.TryGetValue(__instance, out var prior) || prior.Tick != tick)
            {
                VerifiedRaids.Remove(__instance);
                VerifiedRaids.Add(__instance, new ClearProof(tick));
            }
        }
        else
            VerifiedRaids.Remove(__instance);
    }

    private static void BeforeAdvance(MapComponent __instance, bool __0, object? ___nextRaidInfo,
        int ___currentWave, out Advance? __state)
    {
        __state = null;
        if (!__0 || ___nextRaidInfo is null || !OutcomeMetric.Observing(WaveUnit) ||
            __instance.map is not { IsPlayerHome: true } map ||
            !VerifiedRaids.TryGetValue(___nextRaidInfo, out var proof) || proof.Tick != GameFacts.Tick)
            return;
        VerifiedRaids.Remove(___nextRaidInfo);
        try
        {
            // On the normal tick path, RaidOver already computed the remaining
            // enemies in ShouldSendNextWave. Calling it again can send reinforcements.
            if (waveNumber.GetValue(___nextRaidInfo) is int number && number == ___currentWave)
                __state = new Advance(GameFacts.MapId(map), number, wasSent.GetValue(___nextRaidInfo) is true,
                    (int)pawnCount.GetValue(___nextRaidInfo)!, (int)pawnsLeft.GetValue(___nextRaidInfo)!);
        }
        catch (Exception error) { LiveMetrics.Fail(WaveUnit, error); }
    }

    private static void AfterAdvance(int ___currentWave, bool __runOriginal, Advance? __state)
    {
        if (__runOriginal && __state is { } advance && OutcomeMetric.Observing(WaveUnit))
            GameFacts.Record(WaveUnit, state => WinstonWaveRules.RecordClear(state, advance.Map, advance.Wave,
                ___currentWave, advance.Sent, advance.Dispatched, advance.Left));
    }

    private static void InstallRewards(Harmony harmony)
    {
        var window = OutcomeMetric.Bind("VSEWW.Window_ChooseReward", Assembly);
        var reward = OutcomeMetric.Bind("VSEWW.RewardDef", Assembly);
        if (!typeof(Def).IsAssignableFrom(reward))
            throw new InvalidOperationException("Winston reward definition changed");
        _ = RequireField(window, "choosenReward", reward);
        _ = RequireField(window, "map", typeof(Map));
        var close = OutcomeMetric.Method(window, nameof(Window.PostClose), typeof(void));
        OutcomeMetric.Patch(harmony, close, typeof(WinstonWaveOutcomes), null, nameof(RewardChosen));
    }

    private static void RewardChosen(Window __instance, Def? ___choosenReward, Map? ___map, bool __runOriginal)
    {
        if (!__runOriginal || ___choosenReward is null || ___map is not { IsPlayerHome: true } ||
            !OutcomeMetric.Observing(RewardUnit) || RecordedWindows.TryGetValue(__instance, out _))
            return;
        RecordedWindows.Add(__instance, Recorded);
        OutcomeMetric.Record(RewardUnit, ___choosenReward, breakdown: true);
    }
}
